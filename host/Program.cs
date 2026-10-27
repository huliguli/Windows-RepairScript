using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WartungsToolbox
{
    /// <summary>
    /// Einstieg. Kommandozeile im Ueberblick (alles ausser dem ersten Punkt laeuft ohne Fenster,
    /// Ergebnis im Exit-Code und in logs\app.log):
    ///
    ///   (ohne Argumente)                          Oberflaeche (WebView2), nicht erhoeht (Manifest asInvoker seit 8.1)
    ///   --pipe &lt;name&gt;                             Oberflaeche verbindet sich mit einem schon laufenden Helfer
    ///                                             statt einen per UAC zu starten (Program.PipeNameArg, Abnahmeweg)
    ///   --vorgaenger &lt;pid&gt;                        Oberflaeche wartet erst, bis dieser Prozess weg ist (Neustart
    ///                                             nach dem Update: die alte Fassung startet die neue so, M4)
    ///   --helfer --pipe &lt;name&gt; --sid &lt;sid&gt;        erhoehter Helfer, bedient den Host ueber die Named Pipe
    ///   --helfer --plan &lt;datei.json&gt; [--trocken]  Plan aus Datei ausfuehren, Exit = PlanErgebnis.Exit (Abnahme)
    ///   --helfer --messen &lt;ausgabe.json&gt;          erhoehte Messung unredigiert in eine Datei (Abnahme)
    ///   --auto                                    geplante Wartung ohne Oberflaeche (Aufgabenplanung)
    ///   --aufzeichnen &lt;datei.json&gt; [--roh]        Systembild aufnehmen, redigiert (--roh: unredigiert)
    ///   --pruefen &lt;datei.json&gt;                    Regeln ueber eine Aufzeichnung, Ergebnis nach &lt;datei&gt;.befunde.txt
    ///   --shot &lt;png&gt; [--view &lt;name&gt;] [--shotwait &lt;ms&gt;]  Bildschirmfoto der Oberflaeche (Tests)
    ///
    /// "--helfer" wird vor allem anderen behandelt (helfer/Helfer.cs): kein WinForms, keine
    /// MessageBox, keine Einzelinstanz-Sperre – der Helfer laeuft neben dem Host.
    ///
    /// Noch davor, als erste Zeile von Main, der DLL-Suchpfad des Prozesses (DllSuchpfadFestlegen,
    /// M4-Entwurf 10.3): NUR System32, nicht das Arbeitsverzeichnis und nicht der Programmordner.
    /// Gemessen in der Pruefung der Phase 1: der erhoehte Helfer lud wintrust.dll, profapi.dll und
    /// cryptbase.dll aus dem Programmordner, sobald sie dort lagen; mit dem Update-Weg (ein Paket
    /// und eine Pruefsumme genuegen) waere das eine Rechteausweitung fuer jeden Prozess des Nutzers
    /// gewesen. Gemessen in der Pruefung der Phase 2: SYSTEM32 | USER_DIRS mit AddDllDirectory auf den
    /// Programmordner stellte ihn VOR System32 (Windows sucht die eingetragenen Ordner zuerst), eine
    /// gepflanzte dwmapi.dll oder cryptbase.dll kam wieder aus dem Programmordner. WebView2Loader.dll
    /// braucht den Eintrag nicht: die CLR loest den DllImport der WebView2-Assembly selbst ueber
    /// deren Ordner auf.
    /// </summary>
    static class Program
    {
        // Ein Name je Sitzung. Verhindert, dass ein zweiter Start in den gesperrten
        // WebView2-Datenordner laeuft und mit einem leeren schwarzen Fenster endet.
        const string SingleInstanceMutex = "WindowsWartung_UI_SingleInstance";

        /// <summary>
        /// Aus "--pipe &lt;name&gt;" (ohne --helfer): der Host verbindet sich mit diesem laufenden Helfer,
        /// statt einen zu starten. null im Normalfall. Gelesen von HelferClient.
        /// </summary>
        public static string PipeNameArg;

        // ---------- DLL-Suchpfad (kernel32; seit Windows 8 vorhanden, KB2533623 auf 7) ----------

        // LOAD_LIBRARY_SEARCH_SYSTEM32: nur %windir%\System32. Kein LOAD_LIBRARY_SEARCH_USER_DIRS und
        // kein AddDllDirectory: eingetragene Ordner sucht Windows VOR System32 (gemessen, Phase 2),
        // der Programmordner darf fuer System-DLLs nie vorn stehen. Die DllImport-Deklarationen
        // dieser Assembly tragen dazu [assembly: DefaultDllImportSearchPaths(System32)] (AssemblyInfo).
        const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetDefaultDllDirectories(uint directoryFlags);

        /// <summary>
        /// Erste Tat des Prozesses: die Standard-Suchreihenfolge fuer DLLs auf System32 festlegen
        /// (SetDefaultDllDirectories). So kommen wintrust.dll, dwmapi.dll, cryptbase.dll, dismapi.dll
        /// und jede Abhaengigkeit aus System32; WebView2Loader.dll findet die CLR weiter ueber den
        /// Ordner der WebView2-Assembly (Startpruefung, WebView2 und Selbststart laufen unveraendert;
        /// Beleg: --pruefen und ein --shot start mit gepflanzter dwmapi.dll neben der EXE, Modulliste
        /// zeigt System32). Scheitert der Aufruf (altes Windows ohne KB2533623, fremde Sperre), laeuft
        /// das Programm mit der Windows-Vorgabe weiter, mit einer Warnung im app.log; nie werfen, es
        /// ist der allererste Aufruf.
        /// </summary>
        static void DllSuchpfadFestlegen()
        {
            try
            {
                if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32))
                    AppLog.Warn("DLL-Suchpfad nicht festgelegt: SetDefaultDllDirectories meldet Win32-Fehler " + Marshal.GetLastWin32Error() + "; es gilt die Windows-Vorgabe.");
            }
            catch (Exception ex)
            {
                // EntryPointNotFoundException auf einem Windows ohne die beiden Funktionen, oder ein
                // Fehler beim Schreiben ins app.log: der Start geht weiter.
                try { AppLog.Warn("DLL-Suchpfad nicht festgelegt: " + ex.Message + "; es gilt die Windows-Vorgabe."); } catch { }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Vor allem anderen, auch vor dem Helfer (M4-Entwurf 10.3): erst danach werden
            // weitere DLLs geladen (wintrust fuer die Startpruefung, WebView2Loader, dismapi).
            DllSuchpfadFestlegen();

            // Der erhoehte Helfer: vor der Oberflaeche, vor WinForms, vor den Auffangnetzen
            // (die zeigen eine MessageBox). Rueckgabe ist der Exit-Code.
            if (Array.IndexOf(args, "--helfer") >= 0)
            {
                // Dieselbe Startpruefung wie fuer die Oberflaeche: ein veraendertes Programm
                // bekommt auch als Helfer keine Administratorrechte (Exit 5, Grund im Protokoll).
                Environment.ExitCode = Selbstpruefung.BeimStart() ? Helfer.Helfer.Starten(args) : 5;
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppLog.InstallGlobalHandlers();

            string shot = null, view = "", aufzeichnen = null, pruefen = null, selbstpruefung = null;
            bool auto = false, roh = false;
            int shotWait = 950, vorgaenger = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--view" && i + 1 < args.Length) view = args[++i];
                else if (args[i] == "--shotwait" && i + 1 < args.Length) int.TryParse(args[++i], out shotWait);
                else if (args[i] == "--auto") auto = true;
                else if (args[i] == "--aufzeichnen" && i + 1 < args.Length) aufzeichnen = args[++i];
                else if (args[i] == "--pruefen" && i + 1 < args.Length) pruefen = args[++i];
                else if (args[i] == "--roh") roh = true;
                else if (args[i] == "--pipe" && i + 1 < args.Length) PipeNameArg = args[++i];
                else if (args[i] == "--selbstpruefung" && i + 1 < args.Length) selbstpruefung = args[++i];
                else if (args[i] == "--vorgaenger" && i + 1 < args.Length) int.TryParse(args[++i], out vorgaenger);
            }

            // --selbstpruefung <datei>: nur pruefen und berichten (Test), nichts starten.
            if (selbstpruefung != null)
            {
                Environment.ExitCode = Selbstpruefung.Kommandozeile(selbstpruefung);
                return;
            }

            // Startpruefung (Konzept 3.8): eigene Signatur und die Oberflaechendateien gegen die
            // eingebettete Liste. Eine signierte Fassung mit Abweichung laeuft nicht weiter;
            // der Dev-Bau ohne Signatur bekommt nur eine Warnung im Protokoll.
            if (!Selbstpruefung.BeimStart())
            {
                if (shot == null && !auto && aufzeichnen == null && pruefen == null)
                    MessageBox.Show(Selbstpruefung.Meldung(), "Windows-Wartung", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 5;
                return;
            }

            // Stiller, geplanter Wartungslauf ohne Oberflaeche.
            if (auto) { Environment.ExitCode = AutoRunner.Run(); return; }

            // Laeuft dieser Prozess erhoeht (8.0-Selbststartaufgabe mit HighestAvailable,
            // "Als Administrator ausführen"), schreibt er Verlauf, Antworten und app.log als
            // Administrator: die Dateien gehoerten dann der Administratorengruppe, und der
            // naechste normale Start koennte sie nicht mehr ueberschreiben (Entwurf, Abschnitt
            // 14, B32). Deshalb dieselbe Rechtezeile wie Helfer und --auto (die haben ihre eigene).
            RechteSichernWennErhoeht();

            // Kommandozeile ohne Oberflaeche (Grundsatz 8: testbar ohne Fenster):
            //   --aufzeichnen <datei.json>  Systembild aufnehmen, redigiert (--roh: unredigiert)
            //   --pruefen <datei.json>      Regeln ueber eine Aufzeichnung laufen lassen,
            //                               Ergebnis nach <datei>.befunde.txt
            // Die EXE hat keine Konsole; Ergebnis und Fehler landen in Dateien und im Protokoll.
            if (aufzeichnen != null || pruefen != null)
            {
                Environment.ExitCode = Kommandozeile(aufzeichnen, pruefen, roh);
                return;
            }

            // Neustart nach dem Update (--vorgaenger <pid>): die alte Fassung hat diese hier
            // gestartet und beendet sich gerade noch; erst wenn sie weg ist, sind Einzelinstanz-
            // Mutex und WebView2-Datenordner frei (frueher wartete die Batch mit tasklist darauf).
            if (vorgaenger > 0) VorgaengerAbwarten(vorgaenger);

            // Screenshot-Laeufe duerfen parallel laufen (eigener Datenordner).
            if (shot == null && !ClaimSingleInstance()) return;

            if (!WebView2Available()) return;

            AppLog.Info("Start (Version " + typeof(Program).Assembly.GetName().Version + ")");
            Application.Run(new ShellForm(shot, view, shotWait));
            AppLog.Info("Beendet.");

            // Nach dem gelungenen Update (ShellForm.FinishUpdate, M4-Entwurf 4 und 10.3) startet
            // dieser Prozess die neue Fassung selbst; das Fenster ist zu, nichts laeuft mehr.
            NeueFassungStarten();
        }

        /// <summary>
        /// Wartet bis 30 s, bis der Prozess mit dieser PID beendet ist (der Vorgaenger nach dem
        /// Update). Ist er schon weg oder nicht zugreifbar, geht es sofort weiter; laeuft er nach
        /// 30 s noch, geht es trotzdem weiter (dann greift die Einzelinstanz-Sperre wie sonst).
        /// Nie werfen.
        /// </summary>
        static void VorgaengerAbwarten(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p.WaitForExit(30000)) AppLog.Info("Neustart nach dem Update: der Vorgänger (PID " + pid + ") ist beendet.");
                    else AppLog.Warn("Neustart nach dem Update: der Vorgänger (PID " + pid + ") lief nach 30 s noch; es geht ohne Warten weiter.");
                }
            }
            catch (ArgumentException) { }   // schon beendet
            catch (Exception ex) { AppLog.Warn("Neustart nach dem Update: Vorgänger (PID " + pid + ") nicht abfragbar: " + ex.Message); }
        }

        /// <summary>
        /// Startet die EXE aus ShellForm.NeustartNachEnde (gesetzt nur nach dem gelungenen
        /// Tausch durch den Helfer), mit --vorgaenger &lt;eigene PID&gt;, damit die neue Fassung
        /// das Ende dieses Prozesses abwartet. Nicht erhoeht: Process.Start mit dem eigenen
        /// Token (die neue Fassung erbt genau die Rechte dieses Hosts, also keine). Erhoeht:
        /// ueber die Oberflaeche des Nutzers (Shell.OeffneImNutzerkontext), damit die neue
        /// Fassung nicht erhoeht erbt und ihr Selbststart-Schalter keine 8.0-Aufgabe anlegt.
        /// Die einzige Stelle in Program.cs, die einen Prozess startet (Ausnahme der Suite neben
        /// HelferClient.cs und ShellForm.StarteErhoeht); Fehler nur ins app.log, das Fenster ist
        /// schon zu.
        /// </summary>
        static void NeueFassungStarten()
        {
            string exe = ShellForm.NeustartNachEnde;
            if (string.IsNullOrEmpty(exe)) return;
            try
            {
                if (!System.IO.File.Exists(exe))
                {
                    AppLog.Warn("Neustart nach dem Update: " + exe + " ist nicht vorhanden; bitte das Programm von Hand starten.");
                    return;
                }
                string argumente = "--vorgaenger " + Process.GetCurrentProcess().Id;
                if (Sammler.Quellen.Rechte.Erhoeht())
                {
                    AppLog.Info("Neustart nach dem Update über die Oberfläche des Nutzers (dieser Prozess läuft erhöht): " + exe);
                    Shell.OeffneImNutzerkontext(exe, argumente);
                    return;
                }
                AppLog.Info("Neustart nach dem Update: " + exe + " " + argumente);
                // Kein Verb runas: die neue Fassung laeuft mit dem Token dieses (nicht erhoehten)
                // Hosts. UseShellExecute, damit Windows die EXE wie einen Doppelklick des Nutzers
                // startet (Manifest, Arbeitsordner, keine geerbten Handles dieses Prozesses).
                using (Process p = Process.Start(new ProcessStartInfo(exe, argumente) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) })) { }   // Neustart nach dem Update, nicht erhoeht (kein UAC)
            }
            catch (Exception ex)
            {
                AppLog.Error("Neustart nach dem Update fehlgeschlagen (" + exe + "); bitte das Programm von Hand starten", ex);
            }
        }

        /// <summary>
        /// Erhoehter Host: BUILTIN\Users bekommt Aenderungsrecht (vererbt) auf den maschinenweiten
        /// Ordner, wie helfer/Helfer.cs beim Start und src/AutoRunner.cs. Nicht erhoeht passiert
        /// nichts (RechteSichern setzt nur, wer erhoeht ist oder den Ordner angelegt hat). Danach
        /// (M4-Entwurf 4, 10.3) holt Sicherungsdatei.OrdnerAbsichern den Sicherungsordner
        /// sicherungen\massnahmen wieder in Helferbesitz (Besitzer Administratoren, Benutzer nur
        /// Lesen), den die Zeile davor sonst bis zur naechsten Stufe-2-Massnahme fuer Benutzer
        /// aenderbar liesse; die Funktion tut nicht erhoeht und ohne Ordner nichts. Nie werfen:
        /// ein Fehler hier steht im app.log, der Start geht weiter.
        /// </summary>
        static void RechteSichernWennErhoeht()
        {
            try
            {
                if (!Sammler.Quellen.Rechte.Erhoeht()) return;
                bool rechte = Kern.Ablage.RechteSichern();
                if (rechte) AppLog.Info("Start erhöht: Rechte auf " + Kern.Ablage.Maschinenweit() + " gesichert (Benutzer dürfen ändern).");
                else AppLog.Warn("Start erhöht: Rechte auf " + Kern.Ablage.Maschinenweit() + " konnten nicht gesetzt werden (kein Zugriff); neue Dateien gehören dann der Administratorengruppe.");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Start erhöht: Rechte sichern fehlgeschlagen: " + ex.Message);
            }
            try
            {
                if (!Sammler.Quellen.Rechte.Erhoeht()) return;
                Helfer.Sicherungsdatei.OrdnerAbsichern();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Start erhöht: Sicherungsordner nicht geschützt: " + ex.Message);
            }
        }

        static int Kommandozeile(string aufzeichnen, string pruefen, bool roh)
        {
            try
            {
                if (aufzeichnen != null)
                {
                    var s = new Sammler.Sammler().Erfassen();
                    Sammler.Aufzeichnung.Schreiben(s, aufzeichnen, !roh);
                    AppLog.Info("Aufzeichnung geschrieben: " + aufzeichnen + (roh ? " (roh)" : " (redigiert)"));
                    if (!roh)
                    {
                        var v = Sammler.Aufzeichnung.Verstoesse(aufzeichnen);
                        if (v.Count > 0) { AppLog.Error("Redaktion unvollständig: " + string.Join(", ", v)); return 2; }
                    }
                    return 0;
                }
                var bild = Sammler.Aufzeichnung.Lesen(pruefen);
                var erg = Kern.Regeln.Alle.Pruefen(bild, Kern.Entscheidungen.Laden());
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Gesamt: " + Kern.Regeln.Alle.Gesamt(erg) + ", Probleme: " + Kern.Regeln.Alle.Probleme(erg));
                foreach (var b in erg)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + b.Zustand + "] " + Kern.Bereich.Titel(b.Bereich) + (b.DatenVorhanden ? "" : "  (keine Daten: " + string.Join("; ", b.Fehlend) + ")"));
                    foreach (var f in b.Befunde)
                    {
                        sb.AppendLine("   " + f.Zustand.PadRight(7) + " " + f.Titel + "  {" + f.Schluessel + "}");
                        sb.AppendLine("           " + f.Satz);
                        if (f.Rat != null) sb.AppendLine("           Rat: " + f.Rat);
                        if (f.Frage != null) sb.AppendLine("           FRAGE [" + f.Frage.Id + "]: " + f.Frage.Text);
                        foreach (string d in f.Detail) sb.AppendLine("           . " + d);
                    }
                }
                string ziel = pruefen + ".befunde.txt";
                System.IO.File.WriteAllText(ziel, sb.ToString(), new System.Text.UTF8Encoding(true));
                AppLog.Info("Befunde geschrieben: " + ziel);
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("Kommandozeile", ex);
                return 3;
            }
        }

        static Mutex _instance;

        /// <summary>
        /// true, wenn diese Instanz die einzige ist. Sonst wird das bereits offene Fenster
        /// nach vorn geholt und diese Instanz beendet sich still.
        /// </summary>
        static bool ClaimSingleInstance()
        {
            try
            {
                bool created;
                _instance = new Mutex(true, SingleInstanceMutex, out created);
                if (created) return true;

                AppLog.Info("Zweitstart: bestehendes Fenster wird nach vorn geholt.");
                FocusExistingWindow();
                return false;
            }
            catch
            {
                return true;   // im Zweifel starten statt den Nutzer aussperren
            }
        }

        static void FocusExistingWindow()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName("WindowsWartung"))
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
                    Native.ShowWindow(p.MainWindowHandle, Native.SW_RESTORE);
                    Native.SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Prueft, ob die WebView2-Laufzeit vorhanden ist. Fehlte sie, blieb bisher ein totes
        /// schwarzes Fenster stehen - ohne Hinweis, was zu tun ist.
        /// </summary>
        static bool WebView2Available()
        {
            try
            {
                string ver = Microsoft.Web.WebView2.Core.CoreWebView2Environment
                                 .GetAvailableBrowserVersionString(null);
                if (!string.IsNullOrEmpty(ver)) return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("WebView2-Laufzeit nicht gefunden: " + ex.Message);
            }

            AppLog.Error("Start abgebrochen: WebView2-Laufzeit fehlt.");
            DialogResult r = MessageBox.Show(
                "Windows-Wartung braucht eine Windows-Komponente, die auf diesem PC fehlt: " +
                "die WebView2-Laufzeit. Sie ist kostenlos und kommt direkt von Microsoft." +
                Environment.NewLine + Environment.NewLine +
                "Soll die Download-Seite jetzt geöffnet werden?" +
                Environment.NewLine + Environment.NewLine +
                "Nach der Installation starten Sie Windows-Wartung einfach erneut.",
                "Windows-Wartung", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (r == DialogResult.Yes)
            {
                // Auch hier nicht erhoeht: ein Browser mit Administratorrechten gibt jeder
                // heruntergeladenen Datei dieselben Rechte mit.
                Shell.OeffneImNutzerkontext("https://developer.microsoft.com/microsoft-edge/webview2/");
            }
            return false;
        }
    }
}
