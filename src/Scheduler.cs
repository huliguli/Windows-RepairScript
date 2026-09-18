using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    // Geplante Wartung ueber die Windows-Aufgabenplanung (schtasks).
    // Die Anzeige-Parameter werden zusaetzlich lokal gespeichert (sprachunabhaengig,
    // da die schtasks-Textausgabe lokalisiert und damit unzuverlaessig zu parsen waere).
    static class Scheduler
    {
        public const string TaskName = "WindowsWartung-AutoWartung";
        public const string StartTaskName = "WindowsWartung-Autostart";

        public static bool Exists()
        {
            return RunCode("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0;
        }

        // ---- App-Selbststart bei der Anmeldung ----
        // Seit 8.1 (Rechte-Modell B) laeuft die Oberflaeche ohne Adminrechte (Manifest
        // asInvoker). Die Aufgabe bleibt trotzdem der Weg statt des Run-Keys: sie ueberlebt
        // Autostart-Aufraeumer und laesst sich sprachunabhaengig abfragen. Sie laeuft ohne
        // Rechte (LeastPrivilege), weil die Oberflaeche ohne Rechte startet und Windows erst
        // fragt, wenn der Nutzer etwas aendern laesst. Bis 8.0 stand hier HIGHEST (die App
        // war erhoeht, und erhoehte Programme im Run-Key blockiert Windows still).
        //
        // Anlegen und Loeschen laufen im eigenen Nutzerkontext, nicht erhoeht. Dafuer reicht
        // "/SC ONLOGON /RL LIMITED" NICHT: schtasks macht daraus einen Ausloeser "bei jeder
        // Anmeldung" (LogonTrigger ohne UserId), und den darf nur ein Administrator anlegen;
        // nicht erhoeht antwortet schtasks mit "Zugriff verweigert" (live gemessen am
        // 13.09.2026, Medium-Integritaet, auch ohne Vorgaenger-Aufgabe). Ein Ausloeser nur
        // fuer den eigenen Nutzer geht in schtasks allein ueber /XML, deshalb wird die Aufgabe
        // hier als XML-Datei beschrieben und mit "/Create /XML" angelegt: das gelingt ohne
        // Rechte, ebenso das spaetere Ueberschreiben (/F) und Loeschen der eigenen Aufgabe.
        //
        // Grenze: eine Aufgabe, die ein ERHOEHTER Prozess angelegt hat, gehoert der
        // Administratorengruppe (der Nutzer hat nur Leserecht). Der nicht erhoehte Host kann
        // sie abfragen, aber weder ersetzen noch loeschen. Das betrifft die 8.0-Aufgabe, siehe
        // StartTaskAuffrischen.
        public static bool StartTaskExists()
        {
            return RunCode("schtasks.exe", "/Query /TN \"" + StartTaskName + "\"") == 0;
        }

        public static bool StartTaskSet(bool on, string exePath)
        {
            string ausgabe;
            if (!on)
            {
                int rc = Run("schtasks.exe", "/Delete /TN \"" + StartTaskName + "\" /F", out ausgabe);
                if (rc != 0) AppLog.Warn("Selbststart-Aufgabe ließ sich nicht löschen (schtasks Exit " + rc + "): " + Kurz(ausgabe));
                return rc == 0;
            }

            // Die XML-Datei liegt nur fuer den Aufruf im Temp-Ordner des Nutzers; schtasks
            // liest sie ein, danach ist sie ueberfluessig. UTF-16 mit BOM, wie schtasks selbst
            // exportiert. Pfad und Kontoname werden XML-maskiert (ein "&" im Pfad ist erlaubt).
            string xmlPfad = Path.Combine(Path.GetTempPath(), "ww-selbststart-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(xmlPfad, StartTaskXml(exePath), Encoding.Unicode);
                int rc = Run("schtasks.exe", "/Create /TN \"" + StartTaskName + "\" /XML \"" + xmlPfad + "\" /F", out ausgabe);
                if (rc != 0) AppLog.Warn("Selbststart-Aufgabe ließ sich nicht anlegen (schtasks Exit " + rc + "): " + Kurz(ausgabe));
                return rc == 0;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Selbststart-Aufgabe ließ sich nicht anlegen: " + ex.Message);
                return false;
            }
            finally
            {
                // Eine liegen gebliebene Temp-Datei ist keine Warnung wert; Windows raeumt Temp.
                try { if (File.Exists(xmlPfad)) File.Delete(xmlPfad); } catch { }
            }
        }

        /// <summary>
        /// Aufgabenbeschreibung fuer den Selbststart, Schema der Aufgabenplanung (Task 1.2).
        /// Nutzer = das angemeldete Konto (SID im Principal, Name im Ausloeser), interaktives
        /// Token, LeastPrivilege. Kein Zeitlimit (PT0S): die Vorgabe der Aufgabenplanung sind
        /// 72 Stunden, danach wuerde sie die laufende Oberflaeche beenden. Akku-Schalter aus,
        /// sonst startet die App am Notebook ohne Netzteil nicht. Nur eine Instanz.
        /// </summary>
        static string StartTaskXml(string exePath)
        {
            WindowsIdentity ich = WindowsIdentity.GetCurrent();
            string sid = SecurityElement.Escape(ich.User.Value);
            string konto = SecurityElement.Escape(ich.Name);
            string exe = SecurityElement.Escape(exePath);
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Author>" + konto + "</Author><Description>Startet Windows-Wartung bei der Anmeldung von " + konto + ".</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + konto + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>\"" + exe + "\"</Command></Exec></Actions>\r\n" +
                "</Task>\r\n";
        }

        /// <summary>
        /// Erkennt die Selbststart-Aufgabe aus 8.0 (mit /RL HIGHEST angelegt): sie wuerde die
        /// asInvoker-App bei jeder Anmeldung erhoeht starten und damit Rechte-Modell B
        /// aushebeln, und StartTaskExists meldet trotzdem nur "an". Erkennung ueber
        /// "schtasks /Query /XML": das ist die Aufgabenbeschreibung selbst, nicht lokalisiert.
        /// Nur HighestAvailable steht ausdruecklich darin; bei LeastPrivilege laesst schtasks das
        /// Element RunLevel ganz weg (live gemessen), deshalb wird genau der eine Text gesucht.
        /// </summary>
        public static bool StartTaskVeraltet()
        {
            if (!StartTaskExists()) return false;
            string xml;
            if (Run("schtasks.exe", "/Query /TN \"" + StartTaskName + "\" /XML", out xml) != 0) return false;
            return xml != null && xml.IndexOf("<RunLevel>HighestAvailable</RunLevel>", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Legt die 8.0-Aufgabe (HIGHEST) ohne Rechte neu an; true = aufgefrischt, false = nichts
        /// zu tun oder nicht moeglich (dann steht der Grund im Protokoll).
        ///
        /// Nicht moeglich ist es aus dem nicht erhoehten Host: die 8.0-Aufgabe wurde erhoeht
        /// angelegt und gehoert der Administratorengruppe, "/Create /F" und "/Delete /F"
        /// antworten dort mit "Zugriff verweigert" (live gemessen am 13.09.2026). Erhoeht
        /// (Helfer, Installer) gelingt das Ersetzen mit /F in einem Schritt; die neue Aufgabe
        /// gehoert dann allerdings wieder der Administratorengruppe, und der Host kann sie
        /// spaeter nicht mehr abschalten. Sauber ist: erhoeht nur loeschen (StartTaskLoeschen
        /// ueber die Kennung selbststart.loeschen), nicht erhoeht per StartTaskSet(true, exe)
        /// neu anlegen; das ist der Weg der Oberflaeche ("Umstellen", M2-Entwurf Abschnitt 13).
        /// </summary>
        public static bool StartTaskAuffrischen(string exePath)
        {
            if (!StartTaskVeraltet()) return false;
            bool ok = StartTaskSet(true, exePath);
            if (ok) AppLog.Info("Selbststart-Aufgabe aus 8.0 (erhöht) ohne Rechte neu angelegt.");
            else AppLog.Warn("Selbststart-Aufgabe aus 8.0 startet die App noch erhöht; Erneuern ist aus diesem Kontext nicht möglich.");
            return ok;
        }

        /// <summary>
        /// Loescht die Selbststart-Aufgabe ("/Delete /F"), gedacht fuer den ERHOEHTEN Helfer
        /// (Kennung selbststart.loeschen, helfer/Katalog.cs): nur so wird die 8.0-Aufgabe mit
        /// Besitzer Administratoren los, die StartTaskSet(false) aus dem Host nicht loeschen darf.
        /// Bewusst nur loeschen, nicht ersetzen: eine erhoeht angelegte Aufgabe gehoerte wieder
        /// der Administratorengruppe. Neu anlegen tut danach der Host ohne Rechte ueber
        /// StartTaskSet(true, exe). true = schtasks meldete Exit 0; Exit und erste Ausgabezeile
        /// stehen in jedem Fall im app.log.
        /// </summary>
        public static bool StartTaskLoeschen()
        {
            string ausgabe;
            int rc = Run("schtasks.exe", "/Delete /TN \"" + StartTaskName + "\" /F", out ausgabe);
            if (rc == 0) AppLog.Info("Selbststart-Aufgabe „" + StartTaskName + "“ gelöscht (schtasks Exit 0): " + Kurz(ausgabe));
            else AppLog.Warn("Selbststart-Aufgabe „" + StartTaskName + "“ ließ sich nicht löschen (schtasks Exit " + rc + "): " + Kurz(ausgabe));
            return rc == 0;
        }

        /// <summary>Erste nicht leere Zeile einer schtasks-Ausgabe fuer das Protokoll.</summary>
        static string Kurz(string ausgabe)
        {
            if (string.IsNullOrEmpty(ausgabe)) return "(keine Ausgabe)";
            foreach (string z in ausgabe.Split('\n'))
            {
                string t = z.Trim();
                if (t.Length > 0) return t;
            }
            return "(keine Ausgabe)";
        }

        // ---- Geplante Wartung ----
        // Laeuft ueber den Helfer (Kennung zeitplan.anlegen, erhoeht): die Aufgabe startet
        // "--auto" mit /RL HIGHEST, weil DISM und sfc Administratorrechte brauchen. Das ist
        // die einzige Stelle, an der HIGHEST bleibt.
        // mode ("daily"/"weekly"/"monthly") und dSpec sind vom Aufrufer validiert:
        // weekly -> Tages-Liste "MON,WED,FRI" (Whitelist-Tokens), monthly -> Monatstag "1".."31".
        //
        // Aeltere Aufrufform ohne Warnung: die Warnung steht dann nur im app.log. Der Helfer
        // (Kennung zeitplan.anlegen) nimmt die Ueberladung mit warnung und zeigt sie dem Nutzer.
        public static bool Create(string mode, string dSpec, string hh, string mm, string exePath)
        {
            string warnung;
            return Create(mode, dSpec, hh, mm, exePath, out warnung);
        }

        /// <summary>
        /// Legt die Aufgabe an. false = schtasks meldete einen Fehler, nichts angelegt. true = die
        /// Aufgabe steht; ist warnung dann nicht null, gelten fuer sie noch die Windows-Vorgaben
        /// (nie auf Akku, kein Nachholen), und der Aufrufer muss das dem Nutzer sagen: eine
        /// Aufgabe, die auf dem Notebook still nie laeuft, ist schlimmer als eine Fehlermeldung.
        /// </summary>
        public static bool Create(string mode, string dSpec, string hh, string mm, string exePath, out string warnung)
        {
            warnung = null;
            string sc = mode == "weekly" ? "WEEKLY" : (mode == "monthly" ? "MONTHLY" : "DAILY");
            string args =
                "/Create /TN \"" + TaskName + "\" " +
                "/TR \"\\\"" + exePath + "\\\" --auto\" " +
                "/SC " + sc + " /ST " + hh + ":" + mm + " /RL HIGHEST /F";
            if (mode == "weekly" || mode == "monthly") args += " /D " + dSpec;
            if (RunCode("schtasks.exe", args) != 0) return false;

            // schtasks legt mit den Windows-Standardwerten an, und die sind fuer ein
            // Wartungswerkzeug falsch: auf Akku laeuft die Aufgabe NIE, beim Abstecken
            // bricht sie mitten in DISM ab, und ein verpasster Termin (PC war aus) faellt
            // ersatzlos aus. Genau das macht die geplante Wartung auf Notebooks wirkungslos.
            // Deshalb die Einstellungen direkt nachziehen. Schlaegt das fehl, bleibt die
            // Aufgabe trotzdem bestehen - sie laeuft dann eben nur am Netzteil, und genau das
            // steht in warnung.
            //
            // 90 s statt der 15 s von Run: der Kaltstart von PowerShell 5.1 samt Import des
            // ScheduledTasks-Moduls braucht auf aelteren Notebooks (Defender, Festplatte) leicht
            // 10 bis 20 s; mit 15 s wurde die Stufe dort getoetet, und die Aufgabe blieb mit den
            // Vorgaben zurueck, ohne dass es jemand sah.
            string ps =
                "$ErrorActionPreference='Stop';" +
                "$t = Get-ScheduledTask -TaskName '" + TaskName + "';" +
                "$s = $t.Settings;" +
                "$s.DisallowStartIfOnBatteries = $false;" +
                "$s.StopIfGoingOnBatteries = $false;" +
                "$s.StartWhenAvailable = $true;" +          // verpasste Termine nachholen
                "$s.ExecutionTimeLimit = '" + Zeitbudget + "';" +   // Reissleine gegen haengende Laeufe, siehe Zeitbudget
                "Set-ScheduledTask -TaskName '" + TaskName + "' -Settings $s | Out-Null";
            string ausgabe;
            int rc = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"", out ausgabe, PowerShellZeitgrenzeMs);
            if (rc != 0)
            {
                warnung = "Der Zeitplan ist angelegt, aber die Einstellungen für Akku, Nachholen und Zeitbudget ließen sich nicht setzen ("
                          + (rc == -1 ? "PowerShell antwortete nicht binnen " + (PowerShellZeitgrenzeMs / 1000) + " s" : "PowerShell Exit " + rc + ": " + Kurz(ausgabe))
                          + "). Auf einem Notebook läuft die Wartung dann nur am Netzteil, ein verpasster Termin wird nicht nachgeholt, und statt "
                          + ZeitbudgetStunden + " Stunden gilt die Windows-Vorgabe von 72 Stunden je Lauf.";
                AppLog.Warn(warnung);
            }
            else
            {
                AppLog.Info("Zeitplan „" + TaskName + "“ angelegt (" + sc + " " + hh + ":" + mm + "), Zeitbudget je Lauf " + ZeitbudgetStunden
                            + " Stunden (ExecutionTimeLimit " + Zeitbudget + "); dauert ein Lauf länger, beendet ihn die Aufgabenplanung.");
            }
            return true;
        }

        /// <summary>Zeitgrenze fuer die PowerShell-Nachstellung der Aufgabeneinstellungen (Create).</summary>
        public const int PowerShellZeitgrenzeMs = 90000;

        /// <summary>
        /// Zeitbudget der Aufgabe (ExecutionTimeLimit, ISO-8601-Dauer). Bis 8.1 waren es 2 Stunden;
        /// seit den Freigaben (Meilenstein 3) laufen VOR DISM und SFC eine Messung, ein Schnellscan
        /// (bis 20 min), volume.scan (bis 30 min je Laufwerk) und die Nachmessung, danach der
        /// Werkzeugsatz (60 bis 80 min). Mit 2 Stunden toetete die Aufgabenplanung den Prozess
        /// mitten in DISM: kein Verlaufseintrag, keine Ende-Zeile. 4 Stunden lassen zwei
        /// Laufwerke und eine langsame Platte zu und bleiben eine Reissleine gegen Haenger.
        /// </summary>
        public const string Zeitbudget = "PT4H";
        public const int ZeitbudgetStunden = 4;

        public static void Delete()
        {
            RunCode("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F");
        }

        static int RunCode(string file, string args)
        {
            string weg;
            return Run(file, args, out weg);
        }

        // Startet das Werkzeug ohne Fenster und sammelt Ausgabe und Fehlerausgabe ein (beide
        // Leser laufen auf eigenen Threads, deshalb das Schloss). -1 = nicht gestartet oder
        // nach zeitgrenzeMs (Vorgabe 15 s, schtasks antwortet in Sekundenbruchteilen)
        // abgebrochen. Die Textausgabe von schtasks ist lokalisiert und nur fuer das
        // Protokoll gedacht; ausgewertet wird allein "/Query /XML" (StartTaskVeraltet).
        static int Run(string file, string args, out string ausgabe)
        {
            return Run(file, args, out ausgabe, 15000);
        }

        static int Run(string file, string args, out string ausgabe, int zeitgrenzeMs)
        {
            StringBuilder sb = new StringBuilder();
            object schloss = new object();
            ausgabe = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (schloss) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (schloss) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(zeitgrenzeMs)) { try { p.Kill(); } catch { } return -1; }
                    p.WaitForExit();   // ohne Zeitgrenze: erst jetzt sind die Leser sicher durch
                    lock (schloss) ausgabe = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                lock (schloss) ausgabe = sb.ToString() + ex.Message;
                return -1;
            }
        }

        // ---- gespeicherte Anzeige-Parameter ----
        // %ProgramData%\WindowsWartung\zeitplan.json (Kern.Ablage.Maschinenweit). Maschinenweit,
        // weil drei Beteiligte dieselbe Datei brauchen: der Helfer schreibt sie erhoeht, die
        // Oberflaeche liest sie ohne Rechte, und "--auto" liest die gewaehlten Aufgaben
        // erhoeht unter der Aufgabenplanung. Bis 8.0 lag sie als schedule.json im
        // Nutzerprofil; die wird beim ersten Zugriff einmal uebernommen und dort auf
        // schedule.json.uebernommen umbenannt (AppLog.UebernahmeAbschliessen): Ablage.Uebernehmen
        // kopiert, sobald am neuen Ort nichts liegt. Ohne das Umbenennen kaeme ein geloeschter
        // Zeitplan beim naechsten Start zurueck, und bei UAC ueber die Schulter (Standardnutzer,
        // Adminkonto im Dialog) lief die Uebernahme im falschen Profil. Liegt am neuen Ort schon
        // eine Datei (zweites Konto, das 8.0 selbst genutzt hat), wird die alte OHNE Kopie
        // umbenannt: sonst kaeme sie nach zeitplan.loeschen im naechsten Prozess dieses Kontos
        // als Zeitplan zurueck (der Ort wird je Prozess neu ermittelt).
        static readonly object _cfgLock = new object();
        static string _cfgPath;

        static string CfgPath()
        {
            lock (_cfgLock)
            {
                if (_cfgPath == null) _cfgPath = CfgPfadErmitteln();
                return _cfgPath;
            }
        }

        static string CfgPfadErmitteln()
        {
            string alt = AlterCfgPfad();
            string neu;
            try { neu = Path.Combine(Ablage.Maschinenweit(), "zeitplan.json"); }
            catch (Exception ex)
            {
                // Ablage wirft praktisch nie; falls doch, bleibt der Zeitplan im Profil statt gar nicht.
                AppLog.Warn("Ordner für den Zeitplan nicht ermittelbar, Zeitplan bleibt im Nutzerprofil: " + ex.Message);
                return alt;
            }
            try
            {
                if (Ablage.Uebernehmen(alt, neu))
                {
                    bool abgehakt = AppLog.UebernahmeAbschliessen(alt);
                    AppLog.Info("Zeitplan aus dem Nutzerprofil übernommen: " + alt + " nach " + neu
                                + (abgehakt ? "" : " (alte Datei ließ sich nicht umbenennen)"));
                }
                else if (File.Exists(alt) && File.Exists(neu))
                {
                    bool abgehakt = AppLog.UebernahmeAbschliessen(alt);
                    AppLog.Info("Zeitplan aus dem Nutzerprofil nicht übernommen, am neuen Ort liegt schon einer: " + alt
                                + (abgehakt ? " auf .uebernommen umbenannt" : " ließ sich nicht umbenennen"));
                }
            }
            catch (Exception ex) { AppLog.Warn("Zeitplan konnte nicht übernommen werden: " + ex.Message); }
            return neu;
        }

        /// <summary>Der Ort bis 8.0: %LOCALAPPDATA%\WindowsWartung\schedule.json.</summary>
        static string AlterCfgPfad()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Ablage.Ordnername, "schedule.json");
        }

        // days = Wochentage (weekly), dom = Monatstag (monthly), actions = gewaehlte
        // Aufgaben-Schluessel oder null (= Standard-Satz; bewusst NICHT gespeichert,
        // damit ein spaeter geaenderter Standard automatisch gilt).
        // Rueckgabe false, wenn die Datei nicht geschrieben werden konnte (Aufrufer meldet es;
        // die Aufgabe ist dann angelegt, der Aufgaben-Satz aber nicht gemerkt).
        // Die Freigaben (siehe unten) bleiben dabei stehen: der Zeitplan beschreibt WANN und
        // WAS an Werkzeugen laeuft, die Freigaben sind eine eigene Einstellung derselben Datei.
        // Ist die Datei vorhanden, aber nicht lesbar, ersetzt der neue Zeitplan sie trotzdem
        // (die Aufgabe ist in diesem Moment schon angelegt, und eine kaputte Datei liesse sich
        // sonst nie mehr aus der Oberflaeche heraus loswerden); das steht dann im app.log.
        public static bool Write(string mode, string[] days, int dom, string time, string[] actions)
        {
            try
            {
                lock (_cfgLock)
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["mode"] = mode;
                    if (mode == "weekly" && days != null) d["days"] = days;
                    if (mode == "monthly") d["dom"] = dom;
                    d["time"] = time;
                    if (actions != null && actions.Length > 0) d["actions"] = actions;
                    bool unlesbar;
                    Dictionary<string, object> alt = Roh(out unlesbar);
                    if (unlesbar)
                        AppLog.Warn("zeitplan.json war vorhanden, aber nicht lesbar; der neue Zeitplan ersetzt sie, gespeicherte Freigaben gehen dabei verloren: " + CfgPath());
                    string[] freigaben = FreigabenRoh(alt);
                    if (freigaben.Length > 0) d[FreigabenFeld] = freigaben;
                    return Speichern(d);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Zeitplan-Einstellung konnte nicht gespeichert werden: " + ex.Message);
                return false;
            }
        }

        // Gewaehlte Aufgaben-Schluessel fuer den --auto-Lauf (null = Standard-Satz).
        public static string[] ReadActions()
        {
            try
            {
                Dictionary<string, object> d = Read() as Dictionary<string, object>;
                if (d == null || !d.ContainsKey("actions")) return null;
                object[] arr = d["actions"] as object[];
                if (arr == null || arr.Length == 0) return null;
                List<string> keys = new List<string>();
                foreach (object o in arr) { string s = o as string; if (!string.IsNullOrEmpty(s)) keys.Add(s); }
                return keys.Count > 0 ? keys.ToArray() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Der gespeicherte Zeitplan (mode, days, dom, time, actions, freigaben) oder null. Null
        /// auch dann, wenn die Datei nur Freigaben traegt (kein "mode"): fuer die Oberflaeche ist
        /// das "nicht eingerichtet", sonst zeichnete sie einen Zeitplan ohne Uhrzeit. Die
        /// Freigaben liest man ueber ReadFreigaben, nicht aus diesem Objekt.
        /// </summary>
        public static object Read()
        {
            Dictionary<string, object> d = Roh();
            return d != null && d.ContainsKey("mode") ? d : null;
        }

        // Loescht den Zeitplan aus der maschinenweiten Datei. Das Nutzerprofil bleibt
        // unangetastet: die 8.0-Datei ist seit der Uebernahme *.uebernommen (CfgPfadErmitteln),
        // also kommt nichts zurueck; und der Helfer laeuft erhoeht womoeglich unter einem
        // anderen Konto als die Oberflaeche, dessen Profil ihn nichts angeht.
        // Die Freigaben ueberleben das Abschalten: sie stehen in den Einstellungen, nicht im
        // Zeitplan, und ein spaeter neu angelegter Zeitplan soll sie wieder vorfinden. Ohne
        // Freigaben wird die Datei geloescht wie bis 8.1.
        public static void Clear()
        {
            try
            {
                lock (_cfgLock)
                {
                    bool unlesbar;
                    Dictionary<string, object> alt = Roh(out unlesbar);
                    if (unlesbar)
                        AppLog.Warn("zeitplan.json war vorhanden, aber nicht lesbar; das Abschalten löscht sie, gespeicherte Freigaben gehen dabei verloren: " + CfgPath());
                    string[] freigaben = FreigabenRoh(alt);
                    var d = new Dictionary<string, object>();
                    if (freigaben.Length > 0) d[FreigabenFeld] = freigaben;
                    Speichern(d);
                }
            }
            catch (Exception ex) { AppLog.Warn("Zeitplan-Einstellung ließ sich nicht löschen: " + ex.Message); }
        }

        // ---- Freigaben fuer die geplante Wartung (Meilenstein 3) ----
        // zeitplan.json traegt zusaetzlich "freigaben": die Kennungen der Massnahmen, die die
        // geplante Wartung von selbst ausfuehren darf, wenn ein Befund sie vorschlaegt
        // (Einstellungen: "Bei der geplanten Wartung darf das Programm von selbst: ..."). Erlaubt
        // ist nur, was Kern.Massnahmen.Freischaltbare() nennt (Stufe 1, ausfuehrbar); die
        // Voreinstellung ist leer. Die Oberflaeche schreibt sie ohne Rechte (der Laufzeitordner
        // erlaubt Users Aendern, Ablage.RechteSichern), --auto und der Host lesen sie und geben
        // sie dem Plan als Parameter "freigaben" (';'-Liste) mit.
        //
        // Lesen UND Schreiben filtern gegen den Katalog: eine Kennung, die nicht freischaltbar
        // ist (von Hand in die Datei geschrieben, oder in einer spaeteren Fassung zurueckgestuft),
        // kommt nie im Plan an. Der Helfer prueft sie unter wartung.massnahmen ein zweites Mal.
        const string FreigabenFeld = "freigaben";

        /// <summary>Freigegebene Kennungen, nur freischaltbare, ohne Doppelte; nie null.</summary>
        public static string[] ReadFreigaben()
        {
            try { return FreigabenFiltern(FreigabenRoh(Roh()), null); }
            catch (Exception ex)
            {
                AppLog.Warn("Freigaben der geplanten Wartung nicht lesbar, es gilt: keine: " + ex.Message);
                return new string[0];
            }
        }

        /// <summary>
        /// Speichert die Freigaben (null oder leer = keine). Kennungen, die nicht freischaltbar
        /// sind, werden ohne Fehler weggelassen und im app.log genannt: die Oberflaeche schickt
        /// nur, was der Host ihr als freischaltbar gemeldet hat, und ein Rest aus einer aelteren
        /// Fassung darf das Speichern nicht blockieren. false = Datei nicht geschrieben.
        ///
        /// Ist zeitplan.json vorhanden, aber nicht lesbar, wird NICHT geschrieben: die Freigaben
        /// sind nur ein Feld dieser Datei, und ein Schreiben ersetzte den Zeitplan (mode, time,
        /// actions) still durch nichts, waehrend die Aufgabe weiterlaeuft. Der Weg heraus ist
        /// der Zeitplan selbst (Write ersetzt eine kaputte Datei und sagt es) oder das Abschalten.
        /// </summary>
        public static bool WriteFreigaben(string[] kennungen)
        {
            try
            {
                var weggelassen = new List<string>();
                string[] gefiltert = FreigabenFiltern(kennungen ?? new string[0], weggelassen);
                if (weggelassen.Count > 0)
                    AppLog.Warn("Freigaben: " + Zahlwort(weggelassen.Count, "Kennung", "Kennungen") + " nicht freischaltbar und weggelassen: " + string.Join(", ", weggelassen));
                lock (_cfgLock)
                {
                    bool unlesbar;
                    Dictionary<string, object> d = Roh(out unlesbar);
                    if (unlesbar)
                    {
                        AppLog.Warn("Freigaben der geplanten Wartung nicht gespeichert: zeitplan.json ist vorhanden, aber nicht lesbar, und wird nicht überschrieben. "
                                    + "Zeitplan neu anlegen oder abschalten, dann noch einmal speichern: " + CfgPath());
                        return false;
                    }
                    if (d == null) d = new Dictionary<string, object>();
                    if (gefiltert.Length > 0) d[FreigabenFeld] = gefiltert; else d.Remove(FreigabenFeld);
                    bool ok = Speichern(d);
                    if (ok) AppLog.Info("Freigaben der geplanten Wartung gespeichert: " + (gefiltert.Length == 0 ? "keine" : string.Join(", ", gefiltert)));
                    return ok;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Freigaben der geplanten Wartung konnten nicht gespeichert werden: " + ex.Message);
                return false;
            }
        }

        /// <summary>"1 Kennung", "2 Kennungen": Zahl mit dem passenden Wort.</summary>
        static string Zahlwort(int n, string einzahl, string mehrzahl)
        {
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (n == 1 ? einzahl : mehrzahl);
        }

        /// <summary>Nur freischaltbare Kennungen in der Reihenfolge der Eingabe, ohne Doppelte; weggelassen sammelt den Rest.</summary>
        static string[] FreigabenFiltern(IEnumerable<string> roh, List<string> weggelassen)
        {
            var erlaubt = new HashSet<string>(StringComparer.Ordinal);
            foreach (Kern.Massnahme m in Kern.Massnahmen.Freischaltbare()) erlaubt.Add(m.Kennung);
            var erg = new List<string>();
            foreach (string k in roh)
            {
                string t = k == null ? "" : k.Trim();
                if (t.Length == 0) continue;
                if (!erlaubt.Contains(t)) { if (weggelassen != null && !weggelassen.Contains(t)) weggelassen.Add(t); continue; }
                if (!erg.Contains(t)) erg.Add(t);
            }
            return erg.ToArray();
        }

        /// <summary>Die Freigaben so, wie sie in der Datei stehen (ungefiltert, fuer Write und Clear); nie null.</summary>
        static string[] FreigabenRoh(Dictionary<string, object> d)
        {
            var erg = new List<string>();
            if (d == null || !d.ContainsKey(FreigabenFeld)) return erg.ToArray();
            object[] arr = d[FreigabenFeld] as object[];
            if (arr == null) return erg.ToArray();
            foreach (object o in arr)
            {
                string s = o as string;
                if (!string.IsNullOrEmpty(s) && !erg.Contains(s)) erg.Add(s);
            }
            return erg.ToArray();
        }

        /// <summary>Der ganze Inhalt der Datei als Woerterbuch, null wenn sie fehlt oder unlesbar ist.</summary>
        static Dictionary<string, object> Roh()
        {
            bool unlesbar;
            return Roh(out unlesbar);
        }

        /// <summary>
        /// Wie Roh(); unlesbar = die Datei ist da, aber kein JSON-Objekt oder nicht zu oeffnen.
        /// Die Schreibwege unterscheiden das von "fehlt": eine fehlende Datei wird angelegt,
        /// eine unlesbare nie still ueberschrieben.
        /// </summary>
        static Dictionary<string, object> Roh(out bool unlesbar)
        {
            unlesbar = false;
            string pfad = CfgPath();
            try
            {
                if (!File.Exists(pfad)) return null;
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(pfad)) as Dictionary<string, object>;
                if (d == null)
                {
                    unlesbar = true;
                    AppLog.Warn("zeitplan.json ist nicht lesbar: der Inhalt ist kein JSON-Objekt (" + pfad + ").");
                }
                return d;
            }
            catch (Exception ex)
            {
                unlesbar = true;
                AppLog.Warn("zeitplan.json ist nicht lesbar: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Schreibt das Woerterbuch; ein leeres loescht die Datei (wie bis 8.1 nach dem Abschalten).
        /// Erst in eine Temp-Datei im selben Ordner, dann in einem Zug an die Stelle der alten
        /// (File.Replace; File.Move, wenn es noch keine gibt): drei Prozesse lesen diese Datei
        /// (Oberflaeche, Helfer, --auto), und keiner soll eine halb geschriebene sehen. Das
        /// Ersetzen behaelt die Rechte der alten Datei; eine neue erbt die des Ordners (Users:
        /// Aendern, Ablage.RechteSichern), so kann die nicht erhoehte Oberflaeche sie spaeter
        /// weiter schreiben.
        /// </summary>
        static bool Speichern(Dictionary<string, object> d)
        {
            lock (_cfgLock)
            {
                string ziel = CfgPath();
                if (d == null || d.Count == 0)
                {
                    if (File.Exists(ziel)) File.Delete(ziel);
                    return true;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ziel));
                string temp = ziel + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, new JavaScriptSerializer().Serialize(d));
                    try
                    {
                        if (File.Exists(ziel)) File.Replace(temp, ziel, null);
                        else File.Move(temp, ziel);
                    }
                    catch (IOException) when (File.Exists(temp) && File.Exists(ziel))
                    {
                        // Zwischen Pruefung und Move hat ein anderer Prozess die Datei angelegt:
                        // dann ersetzen statt scheitern.
                        File.Replace(temp, ziel, null);
                    }
                    return true;
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
        }
    }
}
