using System;
using System.Diagnostics;
using System.Drawing;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    // Geplanter Wartungslauf (--auto): die Aufgabenplanung startet ihn erhoeht (Aufgabe mit
    // hoechsten Rechten, seit 8.1 ist die EXE selbst asInvoker). Er fuehrt den vom Nutzer
    // gewaehlten Aufgaben-Satz (zeitplan.json in ProgramData) als Plan "wartung.auto" ueber
    // denselben Helfer-Code aus, den auch die Oberflaeche benutzt (lokal im eigenen Prozess,
    // ohne Pipe), schreibt das Laufprotokoll lauf-<id>.jsonl und den Verlaufseintrag und
    // meldet sich per Windows-Benachrichtigung. Keine Oberflaeche, kein eigener Prozessstart:
    // seit 8.1 startet nur noch der Helfer (helfer/Werkzeuge.cs) DISM, SFC und Co.
    //
    // Zwei Protokolle mit verschiedener Tiefe: app.log bekommt nur die tragenden Zeilen
    // (header, good, warn, bad: Anfang, Ergebnis je Werkzeug, Ende). Die Werkzeugzeilen
    // ("›  DISM.exe ...", Ausgabe von sfc /scannow, Tausende Zeilen) stehen NUR im
    // Laufprotokoll lauf-<id>.jsonl (Schicht helfer, Art schritt mit Exit und Sekunden).
    //
    // Run() liefert den Exit-Code des Prozesses (Program.cs setzt Environment.ExitCode):
    // 0/1/2/3/4 wie PlanErgebnis.Exit, 6 uebersprungen (nicht erhoeht, oder ein vorheriger
    // Lauf war noch aktiv). Auch 0 bei Uebergabe an die offene App: die fuehrt den Lauf dann
    // sichtbar aus, das Ergebnis steht in ihrem Verlauf.
    //
    // Waehrend des Laufs haelt der Prozess den Mutex MutexName; der nicht erhoehte Host prueft
    // ihn (Mutex.TryOpenExisting in CheckFlow.StartAbgelehnt) und startet dann kein zweites
    // DISM neben dem Hintergrundlauf.
    // Vertrag: docs/M2-ENTWURF.md, Abschnitt 0 Punkt 6, Abschnitt 3 (wartung.auto), Abschnitt 13.
    static class AutoRunner
    {
        const string Titel = "Geplante Wartung";

        /// <summary>
        /// Name der Sperre, die waehrend eines laufenden --auto gehalten wird. Der Host oeffnet
        /// sie nur (TryOpenExisting), nie anlegen: sonst saehe der --auto-Prozess einen
        /// "vorherigen Lauf" und liesse die Wartung aus.
        /// </summary>
        internal const string MutexName = "WindowsWartung_AutoRun";

        /// <summary>Exit-Code, wenn der Lauf uebersprungen wurde (nicht erhoeht, vorheriger Lauf aktiv).</summary>
        internal const int ExitUebersprungen = 6;

        public static int Run()
        {
            // Erhoehungsstand vorab: er geht als wParam an die offene App (1 = erhoeht), damit
            // die weiss, ob sie den Lauf selbst uebernehmen darf oder dem Nutzer sagen muss,
            // dass der Aufruf nicht erhoeht war. Bis Phase 2 meldete die App "läuft im
            // Hintergrund", waehrend dieser Prozess gleich darauf "Übersprungen" schrieb.
            bool erhoeht = Sammler.Quellen.Rechte.Erhoeht();

            // Ist die App gerade offen, wird der Lauf an sie uebergeben und dort sichtbar
            // ausgefuehrt (verhindert zwei parallele DISM/SFC-Instanzen). Handshake per
            // SendMessageTimeout: Nur wenn die App mit 1 antwortet, hat sie den Lauf
            // WIRKLICH uebernommen; sonst (alte Version, blockierte Nachricht,
            // haengendes Fenster, App ohne Ausfuehrer) laeuft die Wartung hier still weiter.
            // Richtung erhoeht -> nicht erhoeht ist fuer UIPI erlaubt; nur die Gegenrichtung
            // wuerde gefiltert.
            IntPtr win = FindInteractiveWindow();
            if (win != IntPtr.Zero)
            {
                IntPtr res;
                IntPtr ok = Native.SendMessageTimeout(win, Native.WM_WW_RUNAUTO, (IntPtr)(erhoeht ? 1 : 0), IntPtr.Zero,
                                                      Native.SMTO_NORMAL | Native.SMTO_ABORTIFHUNG, 5000, out res);
                if (ok != IntPtr.Zero && res == (IntPtr)1)
                {
                    AppLog.Info("Geplante Wartung: an die offene App übergeben, sie führt den Lauf sichtbar aus.");
                    return PlanErgebnis.Ok;   // die App fuehrt den Lauf sichtbar aus
                }
            }

            // Ohne Erhoehung kein Lauf: DISM, SFC und die Datentraegerbereinigung brauchen
            // Administratorrechte, und ein UAC-Dialog ohne Fenster waere fuer den Nutzer nur
            // ein Raetsel. Das passiert, wenn jemand --auto von Hand aus einer normalen
            // Eingabeaufforderung startet; die Aufgabenplanung startet erhoeht.
            if (!erhoeht)
            {
                History.Add(Titel, "warn", "Übersprungen: die Aufgabe lief ohne Administratorrechte", 0);
                AppLog.Warn("Geplante Wartung nicht gestartet: der Prozess läuft ohne Administratorrechte (--auto von Hand gestartet?). Die Aufgabenplanung startet ihn erhöht.");
                return ExitUebersprungen;
            }

            // Erhoeht darf die Rechte des Laufzeitordners setzen (BUILTIN\Users: Aendern,
            // vererbt): sonst gehoeren die Dateien, die dieser Lauf anlegt (Protokoll, Verlauf),
            // der Administratorengruppe, und der nicht erhoehte Host kommt nicht mehr heran.
            if (!Ablage.RechteSichern())
                AppLog.Warn("Geplante Wartung: die Rechte auf " + Ablage.Maschinenweit() + " ließen sich nicht setzen; der Lauf geht weiter.");

            // Gegen doppelten Trigger in derselben Sitzung absichern; dieselbe Sperre sagt dem
            // nicht erhoehten Host, dass hier gerade DISM oder SFC laufen.
            bool created;
            using (Mutex mx = SperreAnlegen(out created))
            {
                if (!created)
                {
                    // Ein vorheriger Lauf haengt noch. Das gehoert in den Verlauf, sonst
                    // merkt der Nutzer nie, dass seine Wartung seit Wochen ausfaellt.
                    History.Add(Titel, "warn", "Übersprungen: ein vorheriger Lauf war noch aktiv", 0);
                    AppLog.Warn("Geplante Wartung nicht gestartet: vorheriger Lauf ist noch aktiv.");
                    return ExitUebersprungen;
                }

                string kind, msg;
                double sekunden;
                int exit = Ausfuehren(out kind, out msg, out sekunden);

                History.Add(Titel, kind, msg, sekunden);
                // Titel und Satz muessen zusammenpassen: "Wartung abgeschlossen" ueber
                // "Nicht ausgeführt: ..." widerspraeche sich. Die Minutenangabe erst ab 30 s,
                // "(ca. 0 min)" hinter einer Absage sagt nichts.
                Notify(BallonTitel(kind, exit),
                    msg + (sekunden >= 30 ? " (ca. " + Math.Round(sekunden / 60.0, 1) + " min)." : "."),
                    kind == "good" ? ToolTipIcon.Info : kind == "warn" ? ToolTipIcon.Warning : ToolTipIcon.Error);
                return exit;
            }
        }

        // Ueberschrift des Ballons aus Art und Exit des Laufs.
        static string BallonTitel(string kind, int exit)
        {
            if (kind == "good") return "Wartung abgeschlossen";
            if (kind == "warn") return "Wartung mit Hinweisen beendet";
            if (exit == PlanErgebnis.Abgelehnt) return "Wartung nicht ausgeführt";
            return "Wartung abgebrochen";
        }

        /// <summary>
        /// Die Sperre mit ausdruecklicher Zugriffsliste: das eigene Konto (die Aufgabe laeuft
        /// als der Nutzer, der sie eingerichtet hat) darf alles. So kann der NICHT erhoehte Host
        /// desselben Kontos sie mit TryOpenExisting oeffnen, ohne auf die Standard-DACL des
        /// erhoehten Tokens angewiesen zu sein. Scheitert die Liste (Konto nicht lesbar),
        /// bleibt es bei der Standard-DACL; die Sperre selbst gibt es in jedem Fall.
        /// </summary>
        static Mutex SperreAnlegen(out bool created)
        {
            try
            {
                SecurityIdentifier eigenes = WindowsIdentity.GetCurrent().User;
                if (eigenes != null)
                {
                    var sicherheit = new MutexSecurity();
                    sicherheit.AddAccessRule(new MutexAccessRule(eigenes, MutexRights.FullControl, AccessControlType.Allow));
                    sicherheit.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                                                                 MutexRights.FullControl, AccessControlType.Allow));
                    return new Mutex(true, MutexName, out created, sicherheit);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Geplante Wartung: Zugriffsliste der Sperre nicht gesetzt (" + ex.Message + "); Standard-DACL.");
            }
            return new Mutex(true, MutexName, out created);
        }

        // Plan bauen, lokal ausfuehren, Ergebnis in Verlaufs-Art, Satz und Sekunden uebersetzen;
        // Rueckgabe ist der Exit des Plans (3 bei einer Ausnahme ausserhalb der Ausfuehrung).
        // Liefert immer etwas Aussagekraeftiges: auch eine Ausnahme vor dem ersten Schritt wird
        // ein Verlaufseintrag und eine Ende-Zeile im Laufprotokoll, nie ein stilles Ende.
        static int Ausfuehren(out string kind, out string msg, out double sekunden)
        {
            Stopwatch sw = Stopwatch.StartNew();
            kind = "bad"; msg = "Abgebrochen (Fehler)";
            int exit = PlanErgebnis.Ausnahme;
            // Ausserhalb des try, damit das catch die Ende-Zeile in dasselbe Protokoll schreibt.
            Helfer.Ausfuehrungskontext k = null;
            try
            {
                // Vom Nutzer gewaehlter Aufgaben-Satz (zeitplan.json); leer => Standard-Satz.
                // Der Helfer prueft jede Kennung selbst (Katalog.AutoCatalog) und lehnt eine
                // unbekannte ab, bevor irgendetwas laeuft: das Ergebnis ist dann Exit 2.
                string schluessel = string.Join(";", Scheduler.ReadActions() ?? new string[0]);
                Plan plan = Plan.Neu(Titel).Mit("wartung.auto", "schluessel", schluessel);

                // Zeilen des Helfers landen im app.log, aber nur die tragenden Arten: normal
                // und dim sind die Werkzeugausgabe (Tausende Zeilen bei sfc /scannow), die
                // wuerden das Log unlesbar machen. Fortschritt nur als Zeile bei Unterschritten.
                k = new Helfer.Ausfuehrungskontext
                {
                    Protokoll = new Protokoll(plan.Id),
                    Abgebrochen = null,
                    AufruferSid = Sammler.Quellen.Rechte.EigeneSid(),
                    Zeile = delegate (string text, string art, int? prozent)
                    {
                        if (string.IsNullOrEmpty(text)) return;
                        if (art == "bad" || art == "warn") AppLog.Warn("wartung: " + text);
                        else if (art == "header" || art == "good") AppLog.Info("wartung: " + text);
                    },
                    Fortschritt = delegate (int schritt, int gesamt, string label)
                    {
                        AppLog.Info("wartung: Schritt " + schritt + " von " + gesamt + ": " + (label ?? ""));
                    },
                };

                // Anfang und Ende des Plans schreibt die Ausfuehrung selbst (Schicht helfer);
                // die Zeilen der Schicht host sagen dem Leser des Protokolls, WER den Lauf
                // bestellt hat und wie er fuer den Besteller ausging.
                k.Protokoll.Schreibe(Protokoll.Host, Protokoll.Anfang, "wartung.auto", "Geplante Wartung gestartet (Aufgabenplanung)",
                    new { planId = plan.Id, schluessel = schluessel.Length == 0 ? "(Standardsatz)" : schluessel });
                AppLog.Info("Geplante Wartung gestartet (Aufgabenplanung), Plan " + plan.Id
                            + ", Aufgaben: " + (schluessel.Length == 0 ? "Standardsatz" : schluessel)
                            + ", Protokoll " + (k.Protokoll.Pfad ?? "(nur im Speicher)"));

                PlanErgebnis erg = new LokalAusfuehrer().Plan(plan, k);
                sw.Stop();
                if (erg == null)
                    erg = new PlanErgebnis { PlanId = plan.Id, Exit = PlanErgebnis.Ausnahme, Grund = "Der Ausführer lieferte kein Ergebnis" };
                sekunden = erg.Sekunden > 0 ? erg.Sekunden : sw.Elapsed.TotalSeconds;
                exit = erg.Exit;

                // Verlauf wie bis 8.0 (Problem -> warn, sonst good), dazu die Faelle, die es erst
                // seit dem Plan gibt: abgelehnt (nichts lief, z. B. ein unbekannter Schluessel in
                // zeitplan.json) und Kettenbruch. Abgebrochen kommt ohne Oberflaeche nicht vor,
                // wird aber nicht als Erfolg verbucht, falls doch.
                string grund = string.IsNullOrEmpty(erg.Grund) ? "ohne Angabe" : erg.Grund.TrimEnd('.', ' ');
                if (erg.Exit == PlanErgebnis.Abgelehnt) { kind = "bad"; msg = "Nicht ausgeführt: " + grund; }
                else if (erg.Exit == PlanErgebnis.Ausnahme) { kind = "bad"; msg = "Abgebrochen (Fehler): " + grund; }
                else if (erg.Abgebrochen || erg.Exit == PlanErgebnis.AbgebrochenExit) { kind = "bad"; msg = "Abgebrochen"; exit = PlanErgebnis.AbgebrochenExit; }
                else if (erg.Problem || erg.Exit != PlanErgebnis.Ok) { kind = "warn"; msg = "Mit Hinweisen abgeschlossen"; }
                else { kind = "good"; msg = "Erfolgreich abgeschlossen"; }
                if (erg.Wert("neustart") == "1") msg += ", ein Neustart steht noch aus";

                k.Protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "wartung.auto", "Geplante Wartung beendet: " + msg,
                    new { planId = erg.PlanId, kind, exit, sekunden = (int)sekunden, problem = erg.Problem, abgebrochen = erg.Abgebrochen });
                AppLog.Info("Geplante Wartung beendet: " + msg + " (Exit " + exit + ", "
                            + sekunden.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s).");
            }
            catch (Exception ex)
            {
                // Ausnahme ausserhalb der Ausfuehrung (Plan, Protokoll, Zeitplan lesen): der
                // Helfer-Code faengt seine eigenen, diese hier waere sonst ein stilles Ende.
                if (sw.IsRunning) sw.Stop();
                sekunden = sw.Elapsed.TotalSeconds;
                exit = PlanErgebnis.Ausnahme;
                AppLog.Error("Geplante Wartung", ex);
                kind = "bad"; msg = "Abgebrochen (Fehler): " + ex.Message.TrimEnd('.', ' ');
                // Gibt es schon ein Laufprotokoll, bekommt auch dieser Ausgang seine Ende-Zeile;
                // davor (Plan oder Protokoll selbst gescheitert) bleibt nur app.log.
                if (k != null && k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Host, Protokoll.Ende, "wartung.auto", "Geplante Wartung beendet: " + msg,
                        new { kind, exit, sekunden = (int)sekunden, typ = ex.GetType().Name });
            }
            return exit;
        }

        static IntPtr FindInteractiveWindow()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (Process pr in Process.GetProcessesByName("WindowsWartung"))
                {
                    try { if (pr.Id != me && pr.MainWindowHandle != IntPtr.Zero) return pr.MainWindowHandle; }
                    catch { }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        static void Notify(string title, string text, ToolTipIcon icon)
        {
            try
            {
                NotifyIcon ni = new NotifyIcon();
                try { ni.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { ni.Icon = SystemIcons.Information; }
                ni.Visible = true;
                ni.BalloonTipTitle = "Windows-Wartung: " + title;
                ni.BalloonTipText = text;
                ni.BalloonTipIcon = icon;
                ni.ShowBalloonTip(8000);

                // Kurze Message-Pump, damit der Ballon erscheint, dann beenden.
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 6000;
                t.Tick += delegate
                {
                    try { ni.Visible = false; ni.Dispose(); } catch { }
                    Application.ExitThread();
                };
                t.Start();
                Application.Run();
            }
            catch { }
        }
    }
}
