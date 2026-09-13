using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WartungsToolbox
{
    /// <summary>
    /// Besorgt dem Host einen Ausfuehrer, und zwar erst dann, wenn eine Massnahme oder die
    /// erhoehte Messung wirklich ansteht (Konzept 2.3: der UAC-Dialog kommt nicht beim Start,
    /// sondern beim ersten Eingriff). Reihenfolge:
    ///   1. Host laeuft schon erhoeht  -> LokalAusfuehrer (einmalig, kein zweiter Prozess)
    ///   2. lebende Pipe vorhanden     -> wiederverwenden (ein UAC-Dialog je Sitzung)
    ///   3. --pipe &lt;name&gt; uebergeben   -> verbinden ohne Start (Abnahmeweg der Tests)
    ///   4. darfStarten                -> dieselbe EXE mit --helfer per runas starten,
    ///                                    auf die Pipe warten (60 s), ping
    /// Kein UI-Code: die Ablehnung im UAC-Dialog kommt als grund "abgelehnt" zurueck, jeder
    /// andere grund ist ein Satzrest hinter "Der Helfer ließ sich nicht starten: ..." (der
    /// Aufrufer formuliert die Meldung). Vertrag: docs/M2-ENTWURF.md, Abschnitt 6.
    /// </summary>
    static class HelferClient
    {
        /// <summary>Pipe-Name aus --pipe; leer = kein Abnahmeweg. Program.cs fuellt heute Program.PipeNameArg, Holen liest beide.</summary>
        public static string PipeName = null;

        /// <summary>Wartezeit auf die Abnahme-Pipe (der Helfer laeuft dort schon, 10 s reichen).</summary>
        const int AbnahmeWartenMs = 10000;

        static readonly object Sperre = new object();
        static LokalAusfuehrer _lokal;
        // Nicht volatile: Lesen ueber Volatile.Read, Tausch ueber Interlocked.Exchange (Beenden
        // kommt vom UI-Thread und darf nicht auf die Sperre warten).
        static PipeAusfuehrer _pipe;
        static bool? _erhoeht;
        // Solange Holen laeuft (UAC-Dialog, Warten auf die Pipe): Beenden merkt sich dann nur,
        // dass die neue Verbindung gleich wieder zu schliessen ist.
        static volatile bool _holenLaeuft;
        static volatile bool _beendenNachStart;

        // Der Helfer laeuft nur in einem erhoehten Prozess; laeuft dieser hier schon erhoeht,
        // ist der Ausfuehrer ohne Dialog verfuegbar. Einmal messen reicht: Rechte aendern
        // sich in einem laufenden Prozess nicht.
        static bool Erhoeht
        {
            get
            {
                if (!_erhoeht.HasValue) _erhoeht = Sammler.Quellen.Rechte.Erhoeht();
                return _erhoeht.Value;
            }
        }

        /// <summary>Ohne Start pruefbar: erhoeht oder eine lebende Helfer-Pipe.</summary>
        public static bool Verbunden
        {
            get
            {
                if (Erhoeht) return true;
                PipeAusfuehrer p = Volatile.Read(ref _pipe);
                return p != null && p.Lebt;
            }
        }

        /// <summary>true, wenn --pipe &lt;name&gt; uebergeben wurde: der Helfer laeuft von aussen, kein UAC-Dialog.</summary>
        public static bool Abnahmeweg
        {
            get { return AbnahmeName() != null; }
        }

        // Der Name aus der Kommandozeile (PipeName zuerst, dann Program.PipeNameArg); null ohne Vorgabe.
        static string AbnahmeName()
        {
            string vorgabe = !string.IsNullOrEmpty(PipeName) ? PipeName : Program.PipeNameArg;
            return string.IsNullOrEmpty(vorgabe) ? null : vorgabe;
        }

        /// <summary>
        /// Liefert den Ausfuehrer oder null mit grund. grund "abgelehnt" heisst: der Nutzer hat
        /// den UAC-Dialog abgelehnt; jeder andere grund ist ein Satzrest fuer das Protokoll.
        /// Blockiert bis zu 60 s (UAC-Dialog plus Start des Helfers): nur aus einem
        /// Hintergrund-Thread rufen.
        /// </summary>
        public static IAusfuehrer Holen(bool darfStarten, out string grund)
        {
            grund = null;
            IAusfuehrer a;
            lock (Sperre)
            {
                _holenLaeuft = true;
                try { a = HolenInnen(darfStarten, out grund); }
                finally { _holenLaeuft = false; }

                if (_beendenNachStart)
                {
                    // Beenden kam waehrend des Starts (Fenster geschlossen): die frische
                    // Verbindung wird nicht benutzt, sondern gleich wieder beendet.
                    _beendenNachStart = false;
                    PipeAusfuehrer p = Interlocked.Exchange(ref _pipe, null);
                    if (p != null)
                    {
                        AppLog.Info("Helfer-Pipe " + p.Name + " wird beendet (Beenden kam während des Starts).");
                        try { p.Ende(); } catch (Exception ex) { AppLog.Warn("Helfer beenden: " + ex.Message); }
                    }
                    if (a is PipeAusfuehrer)
                    {
                        grund = "Das Fenster wurde geschlossen, bevor der Helfer bereit war.";
                        a = null;
                    }
                }
            }
            return a;
        }

        static IAusfuehrer HolenInnen(bool darfStarten, out string grund)
        {
            grund = null;

            // 1. Schon erhoeht: derselbe Prozess.
            if (Erhoeht)
            {
                if (_lokal == null)
                {
                    _lokal = new LokalAusfuehrer();
                    AppLog.Info("Ausführer: lokal (dieser Prozess läuft erhöht).");
                }
                return _lokal;
            }

            // 2. Bestehende Verbindung.
            PipeAusfuehrer alt = Volatile.Read(ref _pipe);
            if (alt != null)
            {
                if (alt.Lebt)
                {
                    AppLog.Info("Ausführer: Helfer-Pipe " + alt.Name + " wiederverwendet.");
                    return alt;
                }
                AppLog.Info("Helfer-Pipe " + alt.Name + " ist beendet, wird verworfen.");
                alt.Ende();
                Interlocked.CompareExchange(ref _pipe, null, alt);
            }

            // 3. Abnahmeweg: --pipe <name>, der Helfer wurde von aussen gestartet. Derselbe
            //    Namensfilter wie im Helfer (Pipe.NameGueltig), dasselbe Warten wie beim Start
            //    (WaitNamedPipe statt Connect-Spin, siehe AufPipeWarten).
            string vorgabe = AbnahmeName();
            if (vorgabe != null)
            {
                if (!Helfer.Pipe.NameGueltig(vorgabe))
                {
                    grund = "Der Name der Abnahme-Pipe ist unzulässig (erlaubt: Buchstaben, Ziffern, . - _; 3 bis 100 Zeichen).";
                    AppLog.Warn("Abnahme-Pipe: " + grund);
                    return null;
                }
                var pa = new PipeAusfuehrer(vorgabe);
                string fehler;
                if (!AufPipeWarten(pa, null, AbnahmeWartenMs, out fehler))
                {
                    pa.Ende();
                    grund = "Die Abnahme-Pipe „" + vorgabe + "“ antwortet nicht (" + fehler.TrimEnd('.') + ").";
                    AppLog.Warn("Abnahme-Pipe: " + grund);
                    return null;
                }
                string version, erhoeht;
                if (!pa.Ping(out version, out erhoeht))
                {
                    string g = pa.LetzterGrund;
                    pa.Ende();
                    grund = "Die Abnahme-Pipe „" + vorgabe + "“ " + (g != null ? "lehnt die Verbindung ab: " + g.TrimEnd('.') : "antwortet nicht auf „ping“ binnen 5 s") + ".";
                    AppLog.Warn("Abnahme-Pipe: " + grund);
                    return null;
                }
                VersionPruefen(version);
                // Auf dem Abnahmeweg ist ein nicht erhoehter Helfer erlaubt (Test ohne erhoehte
                // Shell); er wird nur benannt, damit eine Messung ueber ihn nicht fuer erhoeht gilt.
                if (erhoeht != "1")
                    AppLog.Warn("Abnahme-Pipe " + vorgabe + ": der Helfer läuft ohne Administratorrechte (erhoeht=" + erhoeht + "), seine Messung ist nicht erhöht.");
                Volatile.Write(ref _pipe, pa);
                AppLog.Info("Ausführer: Abnahme-Pipe " + vorgabe + " verbunden (kein Start), Helfer-Version " + version + ", erhöht " + erhoeht + ".");
                return pa;
            }

            // 4. Start per runas.
            if (!darfStarten)
            {
                grund = "Kein Helfer verbunden, Start nicht erlaubt.";
                AppLog.Info("Ausführer: nicht verbunden, Start nicht erlaubt.");
                return null;
            }
            return Starten(out grund);
        }

        /// <summary>
        /// Beim Schliessen des Fensters: ende senden und die Verbindung verwerfen. Nimmt die
        /// Sperre NICHT: Holen haelt sie bis zu 60 s plus UAC-Dialog, und dieser Aufruf kommt
        /// vom UI-Thread (FormClosing). Laeuft gerade ein Start, wird dessen Verbindung am Ende
        /// von Holen beendet; hier steht dann nur eine Zeile im app.log.
        /// </summary>
        public static void Beenden()
        {
            if (_holenLaeuft)
            {
                _beendenNachStart = true;
                AppLog.Info("Helfer-Start läuft noch (UAC-Dialog oder Warten auf die Pipe), das Ende wird nicht abgewartet.");
            }
            PipeAusfuehrer p = Interlocked.Exchange(ref _pipe, null);
            if (p == null) return;
            AppLog.Info("Helfer-Pipe " + p.Name + " wird beendet.");
            try { p.Ende(); } catch (Exception ex) { AppLog.Warn("Helfer beenden: " + ex.Message); }
        }

        // ---------------------------------------------------------------- Start per runas

        static IAusfuehrer Starten(out string grund)
        {
            grund = null;
            string sid = Sammler.Quellen.Rechte.EigeneSid();
            if (string.IsNullOrEmpty(sid))
            {
                grund = "Die SID des eigenen Kontos ließ sich nicht ermitteln.";
                AppLog.Warn("Helfer-Start: " + grund);
                return null;
            }
            string exe = EigeneExe();
            if (exe == null)
            {
                grund = "Der Pfad der eigenen EXE ließ sich nicht ermitteln.";
                AppLog.Warn("Helfer-Start: " + grund);
                return null;
            }

            string name = Helfer.Pipe.NeuerName();
            var psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = "--helfer --pipe " + name + " --sid " + sid;
            psi.UseShellExecute = true;      // Pflicht fuer Verb = runas (UAC-Dialog)
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;

            Process helfer;
            try
            {
                AppLog.Info("Helfer-Start: " + exe + " " + psi.Arguments);
                helfer = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED: UAC abgelehnt
            {
                grund = Kern.Protokoll.Abgelehnt;
                AppLog.Info("Helfer-Start: der UAC-Dialog wurde abgelehnt.");
                return null;
            }
            catch (Exception ex)
            {
                grund = "Windows meldet beim Start „" + ex.Message.TrimEnd('.') + "“.";
                AppLog.Warn("Helfer-Start: " + grund);
                return null;
            }

            // Das Prozessobjekt haelt ein Handle; es wird nur zum Warten gebraucht und danach
            // freigegeben (ein Handle je UAC-Start bliebe sonst bis zum Ende des Hosts offen).
            try
            {
                string pid = SicherePid(helfer);
                var pa = new PipeAusfuehrer(name);
                string fehler;
                if (!AufPipeWarten(pa, helfer, PipeAusfuehrer.VerbindenMs, out fehler))
                {
                    grund = fehler;
                    AppLog.Warn("Helfer-Start: " + fehler);
                    return null;
                }
                string version, erhoeht;
                if (!pa.Ping(out version, out erhoeht))
                {
                    string g = pa.LetzterGrund;
                    pa.Ende();
                    grund = g != null ? "die Verbindung wurde abgelehnt (" + g.TrimEnd('.') + ")."
                                      : "keine Antwort auf „ping“ binnen 5 s.";
                    AppLog.Warn("Helfer-Start: " + grund);
                    return null;
                }
                VersionPruefen(version);
                if (erhoeht != "1")
                {
                    // Nach einem runas-Start MUSS der Helfer erhoeht sein; "0" heisst: der Dialog
                    // hat ihn nicht erhoeht (Richtlinie, fremdes Konto ohne Rechte). Ein solcher
                    // Helfer wuerde jede Massnahme mit "Zugriff verweigert" scheitern lassen und
                    // in CheckFlow als erhoehte Quelle gelten: also ende und null.
                    pa.Ende();
                    grund = "Der Helfer läuft ohne Administratorrechte.";
                    AppLog.Warn("Helfer-Start: " + grund + " (ping: erhoeht=" + erhoeht + ", Version " + version + ")");
                    return null;
                }
                Volatile.Write(ref _pipe, pa);
                AppLog.Info("Ausführer: Helfer gestartet (PID " + pid + ", Version " + version + ", erhöht " + erhoeht + "), Pipe " + name + " verbunden.");
                return pa;
            }
            finally
            {
                if (helfer != null)
                {
                    try { helfer.Dispose(); }
                    catch (Exception ex) { AppLog.Warn("Helfer-Prozessobjekt freigeben: " + ex.Message); }
                }
            }
        }

        // Der Helfer ist dieselbe EXE; eine andere Version heisst: Host und Helfer stammen aus
        // verschiedenen Staenden (Update waehrend der Sitzung, fremder Abnahme-Helfer). Das
        // Protokoll der Pipe ist dann nicht garantiert gleich, deshalb eine Warnung.
        static void VersionPruefen(string helferVersion)
        {
            string eigene;
            try { eigene = typeof(HelferClient).Assembly.GetName().Version.ToString(); }
            catch (Exception) { eigene = null; }
            if (string.IsNullOrEmpty(helferVersion))
                AppLog.Warn("Helfer: die Antwort auf „ping“ nennt keine Version (Host " + (eigene ?? "?") + ").");
            else if (eigene != null && helferVersion != eigene)
                AppLog.Warn("Helfer: Version " + helferVersion + " weicht vom Host (" + eigene + ") ab.");
        }

        // NamedPipeClientStream.Connect(timeout) dreht in .NET Framework ohne Pause, solange
        // die Pipe noch nicht existiert (WaitNamedPipe meldet sofort ERROR_FILE_NOT_FOUND).
        // Waehrend der Nutzer den UAC-Dialog liest, waere das ein voller Kern fuer bis zu
        // 60 s. Darum: erst WaitNamedPipe (kehrt ohne Pipe sofort zurueck), dann 250 ms Pause,
        // Connect erst, wenn eine Instanz frei ist. Endet der Helfer vorher (Exit 4/7), ist
        // Warten sinnlos: sofort mit Grund zurueck. helfer darf null sein (Abnahmeweg).
        static bool AufPipeWarten(PipeAusfuehrer pa, Process helfer, int maxMs, out string fehler)
        {
            string pfad = @"\\.\pipe\" + pa.Name;
            string letzter = null;
            var uhr = Stopwatch.StartNew();
            while (uhr.ElapsedMilliseconds < maxMs)
            {
                if (helfer != null)
                {
                    try
                    {
                        if (helfer.HasExited)
                        {
                            fehler = "sofort beendet mit Exit " + helfer.ExitCode + ".";
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Eingeschraenktes Handle (runas-Start ohne Abfragerecht): einmal melden,
                        // dann ohne Prozessbeobachtung weiterwarten.
                        AppLog.Warn("Helfer-Prozess nicht abfragbar: " + ex.Message);
                        helfer = null;
                    }
                }

                if (WaitNamedPipe(pfad, 1000))
                {
                    try { pa.Verbinden(2000); fehler = null; return true; }
                    catch (Exception ex) { letzter = ex.Message; }
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_FILE_NOT_FOUND && err != ERROR_SEM_TIMEOUT) letzter = "Win32-Fehler " + err;
                }
                Thread.Sleep(250);
            }
            fehler = "binnen " + (maxMs / 1000) + " s keine Pipe"
                     + (letzter != null ? " (" + letzter.TrimEnd('.') + ")" : "") + ".";
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WaitNamedPipe(string name, int timeoutMs);
        const int ERROR_FILE_NOT_FOUND = 2;
        const int ERROR_SEM_TIMEOUT = 121;

        // Application.ExecutablePath ist WinForms; hier reicht der Prozess selbst.
        static string EigeneExe()
        {
            try
            {
                string p = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { AppLog.Warn("Eigene EXE über MainModule nicht lesbar: " + ex.Message); }
            try
            {
                var a = System.Reflection.Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return a.Location;
            }
            catch (Exception ex) { AppLog.Warn("Eigene EXE über EntryAssembly nicht lesbar: " + ex.Message); }
            return null;
        }

        static string SicherePid(Process p)
        {
            try { return p == null ? "?" : p.Id.ToString(); } catch (Exception) { return "?"; }
        }
    }
}
