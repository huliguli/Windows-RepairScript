using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    /// <summary>
    /// Der gemeinsame Schrittbaukasten (docs/M2-ENTWURF.md, Abschnitt 11): aus Kennung und
    /// geprueften Parametern werden die Steps gebaut. Der Host benutzt ihn fuer die Anzeige
    /// und die Pruefung vor dem Senden, der Helfer fuer Pruefung und Ausfuehrung. Der Helfer
    /// nimmt nie File+Args von aussen an; er ruft diese Funktionen selbst.
    ///
    /// Die Pruefungen stammen 1:1 aus host/ShellForm.cs (SanitizeHost, SanitizeDesc,
    /// ScheduleCreate) und BloatRemove; die Schrittbauer aus BloatRestoreStep, BloatRemoveStep,
    /// NetDiag und DriverBackup. ShellForm behaelt seine Fassungen, bis Abschnitt B2 umstellt.
    ///
    /// Jede Funktion liefert entweder Steps oder null (= Parameter ungueltig); nie eine
    /// leere Liste ohne Grund.
    /// </summary>
    static class Schritte
    {
        /// <summary>Beschreibung, wenn der Nutzer keine eingibt (nur erlaubte Zeichen, siehe BeschreibungPruefen).</summary>
        public const string StandardBeschreibung = "Manueller Punkt Windows-Wartung";

        // ---------------------------------------------------------------- Werkzeuge (Catalog)

        /// <summary>Katalogeintrag zur Nummer oder null (ausserhalb der Liste).</summary>
        public static MaintenanceAction Aktion(int id)
        {
            List<MaintenanceAction> alle = Catalog.All();
            if (id < 0 || id >= alle.Count) return null;
            return alle[id];
        }

        /// <summary>Die Steps des Katalogeintrags; null, wenn id ungueltig oder eine Sonderaktion (Special) ist.</summary>
        public static List<Step> Werkzeug(int id)
        {
            MaintenanceAction a = Aktion(id);
            if (a == null || a.Special != null) return null;
            if (a.Steps == null || a.Steps.Count == 0) return null;
            return new List<Step>(a.Steps);
        }

        /// <summary>Geplante Wartung: Catalog.AutoSet (leer oder null = Standardsatz).</summary>
        public static List<Step> Auto(List<string> keys)
        {
            return Catalog.AutoSet(keys == null || keys.Count == 0 ? null : keys.ToArray());
        }

        // ---------------------------------------------------------------- Apps entfernen

        /// <summary>
        /// Jede PackageFullName muss AppxCleaner.IsRemovable bestehen (nur [A-Za-z0-9._-],
        /// im Katalog, nicht kritisch), sonst null. Doppelte werden zusammengefasst.
        /// sicherung = vorher ein Wiederherstellungspunkt (bisher BloatRestoreStep).
        /// </summary>
        public static List<Step> AppsEntfernen(List<string> fulls, bool sicherung)
        {
            if (fulls == null || fulls.Count == 0) return null;
            var geprueft = new List<string>();
            foreach (string full in fulls)
            {
                if (!AppxCleaner.IsRemovable(full)) return null;
                if (!geprueft.Contains(full)) geprueft.Add(full);
            }
            var steps = new List<Step>();
            if (sicherung) steps.Add(AppsSicherungSchritt());
            foreach (string full in geprueft)
                steps.Add(AppEntfernenSchritt(full, AppxCleaner.LabelFor(full)));
            return steps;
        }

        // Zuverlaessiger Wiederherstellungspunkt vor dem Entfernen (Frequenz-Drossel kurz aufheben).
        // Ein uebersprungener Punkt (Systemschutz aus) darf den Lauf nicht als Fehler werten.
        static Step AppsSicherungSchritt()
        {
            string cmd =
                "try { " +
                "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name 'SystemRestorePointCreationFrequency' -Value 0 -EA SilentlyContinue; " +
                "Checkpoint-Computer -Description 'Vor Bloatware-Entfernung' -RestorePointType MODIFY_SETTINGS -EA Stop; " +
                "'Wiederherstellungspunkt angelegt.' " +
                "} catch { 'Es wurde kein Sicherungspunkt angelegt: ' + $_.Exception.Message }";
            return new Step
            {
                File = "powershell.exe",
                Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"",
                IgnoreExit = true
            };
        }

        // full ist bereits per AppxCleaner.IsRemovable geprueft (nur [A-Za-z0-9._-]) -> sicher in '' .
        static Step AppEntfernenSchritt(string full, string label)
        {
            string safe = AppxCleaner.SafeLabel(label);
            string cmd =
                "$ErrorActionPreference='Stop'; " +
                "try { Remove-AppxPackage -Package '" + full + "' -EA Stop; 'Entfernt: " + safe + "' } " +
                "catch { 'Fehler bei " + safe + ": ' + $_.Exception.Message; exit 1 }";
            return new Step
            {
                File = "powershell.exe",
                Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\""
            };
        }

        // ---------------------------------------------------------------- Netzwerk-Diagnose

        /// <summary>ping.exe -n 4 und tracert.exe -d -h 20; null, wenn HostPruefen scheitert.</summary>
        public static List<Step> NetzDiagnose(string ziel)
        {
            string t = HostPruefen(ziel);
            if (t == null) return null;
            // ping.exe/tracert.exe werden direkt (ohne Shell) aufgerufen -> der Parameter wird nie interpretiert.
            var steps = new List<Step>();
            steps.Add(new Step { File = "ping.exe", Args = "-n 4 " + t });
            steps.Add(new Step { File = "tracert.exe", Args = "-d -h 20 " + t });
            return steps;
        }

        /// <summary>
        /// Hostname/IP streng auf unkritische Zeichen begrenzen: [A-Za-z0-9.:-], 1 bis 253 Zeichen,
        /// nicht mit '-' beginnend; sonst null. Ein fuehrendes '-' waere fuer ping.exe eine Option
        /// ("-t" liefe bis zur Zeitgrenze), kein Ziel.
        /// </summary>
        public static string HostPruefen(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.Length == 0 || s.Length > 253) return null;
            if (s[0] == '-') return null;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '.' || c == '-' || c == ':';
                if (!ok) return null;
            }
            return s;
        }

        // ---------------------------------------------------------------- Treiber sichern

        /// <summary>pnputil.exe /export-driver * "ordner"; null, wenn OrdnerPruefen scheitert. Den Ordner legt der Aufrufer an.</summary>
        public static List<Step> TreiberSichern(string ordner)
        {
            string p = OrdnerPruefen(ordner);
            if (p == null) return null;
            var steps = new List<Step>();
            steps.Add(new Step { File = "pnputil.exe", Args = "/export-driver * \"" + p + "\"" });
            return steps;
        }

        /// <summary>
        /// Absoluter Pfad (Laufwerk "C:\..." oder UNC "\\server\freigabe\..."), ohne
        /// Anfuehrungszeichen, nicht unter %WINDIR%, keine Laufwerkswurzel, keine Abzweigung
        /// (Junction, symbolischer Link, Einhaengepunkt) im Pfad. Liefert den aufgeloesten
        /// Pfad (ohne Schluss-Backslash) oder null. Laufwerksrelative Angaben ("C:ordner",
        /// "\ordner") und Geraetepfade ("\\?\") gelten nicht als absolut.
        ///
        /// Netzlaufwerke (Z:\...) werden in den Netzwerkpfad (\\server\freigabe\...) uebersetzt:
        /// die Zuordnung kennt nur die Anmeldesitzung des Nutzers, der erhoehte Helfer hat ein
        /// eigenes Token und sieht sie ohne EnableLinkedConnections nicht; dort scheiterte
        /// Directory.CreateDirectory mit "Ein Teil des Pfades konnte nicht gefunden werden",
        /// obwohl der Ordner im Explorer da ist. Gelingt die Uebersetzung nicht, ist der Pfad
        /// ungueltig (Grund ueber die Ueberladung mit grund).
        ///
        /// Die Wurzel ("D:\") ist abgelehnt, weil sie als einziger Pfad mit Backslash endet:
        /// in "/export-driver * "D:\"" wuerde der Backslash das Schlusszeichen maskieren
        /// (CommandLineToArgv), pnputil saehe das Argument D:" mit offenem Anfuehrungszeichen.
        /// Dazu ist ein Treiber-Backup direkt in die Wurzel nie das, was jemand will.
        ///
        /// Abzweigungen sind abgelehnt, weil pnputil im Helfer ERHOEHT schreibt: eine Junction
        /// C:\Users\x\drv auf C:\Windows\System32 bestuende die %WINDIR%-Pruefung (die sieht
        /// nur den Linknamen) und liesse die Treiberpakete erhoeht nach System32 schreiben.
        /// Geprueft wird deshalb jede vorhandene Komponente des Pfads; fehlende Komponenten
        /// (der Ordner darf neu sein) zaehlen nicht.
        /// </summary>
        public static string OrdnerPruefen(string ordner)
        {
            string grund;
            return OrdnerPruefen(ordner, out grund);
        }

        /// <summary>
        /// Wie OrdnerPruefen(ordner); bei null steht in grund ein Satz fuer den Nutzer (Laufwerkswurzel,
        /// Netzlaufwerk nicht uebersetzbar, Laufwerk nicht vorhanden, Abzweigung im Pfad, sonst
        /// der allgemeine Grund). Bei Erfolg ist grund null.
        /// </summary>
        public static string OrdnerPruefen(string ordner, out string grund)
        {
            string roh = ordner == null ? "" : ordner.Trim();
            grund = "Der Zielordner „" + roh + "“ muss ein absoluter Pfad sein und darf nicht im Windows-Ordner liegen.";
            if (roh.Length < 3) return null;
            if (roh.IndexOf('"') >= 0 || roh.IndexOf(';') >= 0) return null;
            if (roh.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            if (!Path.IsPathRooted(roh)) return null;

            bool laufwerk = char.IsLetter(roh[0]) && roh[1] == ':' && (roh[2] == '\\' || roh[2] == '/');
            bool unc = roh.StartsWith("\\\\", StringComparison.Ordinal) && roh[2] != '\\' && roh[2] != '?' && roh[2] != '.';
            if (!laufwerk && !unc) return null;

            string voll;
            try { voll = Path.GetFullPath(roh); }
            catch (Exception) { return null; }
            voll = voll.TrimEnd('\\', '/');
            if (voll.Length < 3)                                // "C:" waere die Wurzel, siehe IstLaufwerkswurzel
            {
                grund = "Der Zielordner „" + roh + "“ ist eine Laufwerkswurzel; bitte einen Unterordner wählen.";
                return null;
            }
            if (laufwerk && voll[1] != ':') return null;        // GetFullPath hat etwas anderes daraus gemacht

            if (laufwerk)
            {
                voll = LaufwerkAufloesen(voll, out grund);
                if (voll == null) return null;
            }

            string windir;
            try { windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\'); }
            catch (Exception) { windir = null; }
            if (!string.IsNullOrEmpty(windir))
            {
                grund = "Der Zielordner „" + roh + "“ liegt im Windows-Ordner; bitte einen Ordner außerhalb von " + windir + " wählen.";
                if (string.Equals(voll, windir, StringComparison.OrdinalIgnoreCase)) return null;
                if (voll.StartsWith(windir + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            }

            string fehler;
            if (HatAbzweigung(voll, out fehler))
            {
                grund = fehler != null
                    ? "Der Zielordner „" + roh + "“ lässt sich nicht prüfen (" + fehler + "); bitte einen erreichbaren Ordner wählen."
                    : "Der Zielordner „" + roh + "“ liegt hinter einer Verknüpfung auf einen anderen Ort (Junction, symbolischer Link oder Einhängepunkt); bitte einen Ordner ohne solche Verknüpfung im Pfad wählen.";
                return null;
            }
            grund = null;
            return voll;
        }

        /// <summary>
        /// Wahr, wenn die Angabe auf eine Laufwerkswurzel zeigt ("D:\", "D:/", "D:\.."); nur fuer
        /// den Grund der Ablehnung ("bitte einen Unterordner waehlen"), OrdnerPruefen lehnt sie ab.
        /// </summary>
        public static bool IstLaufwerkswurzel(string ordner)
        {
            if (string.IsNullOrEmpty(ordner)) return false;
            string p = ordner.Trim();
            if (p.Length < 2 || !char.IsLetter(p[0]) || p[1] != ':') return false;
            string voll;
            try { voll = Path.GetFullPath(p); }
            catch (Exception) { return false; }
            return voll.TrimEnd('\\', '/').Length == 2;
        }

        // Laufwerksbuchstabe pruefen und ein Netzlaufwerk in den Netzwerkpfad uebersetzen. voll ist
        // "X:\..." ohne Schluss-Backslash. Liefert den (ggf. uebersetzten) Pfad oder null mit grund.
        // Ein Buchstabe ohne Laufwerk dahinter (DriveType.NoRootDirectory) wird abgelehnt: das ist
        // ein Tippfehler oder genau die Zuordnung, die dieser Prozess (erhoeht) nicht kennt; die
        // Anlage des Ordners scheiterte spaeter ohnehin, nur mit einer technischen Fehlzeile.
        static string LaufwerkAufloesen(string voll, out string grund)
        {
            grund = null;
            string buchstabe = voll.Substring(0, 2);   // "Z:"
            DriveType art;
            try { art = new DriveInfo(buchstabe).DriveType; }
            catch (Exception) { art = DriveType.Unknown; }

            if (art == DriveType.NoRootDirectory)
            {
                grund = "Das Laufwerk " + buchstabe + " ist auf diesem PC nicht vorhanden oder mit Administratorrechten nicht erreichbar; bitte einen anderen Ordner oder den Netzwerkpfad \\\\server\\freigabe wählen.";
                return null;
            }
            if (art != DriveType.Network) return voll;

            string ziel = NetzwerkpfadVon(buchstabe);
            if (ziel == null)
            {
                grund = "Das Netzlaufwerk " + buchstabe + " ist mit Administratorrechten nicht erreichbar; bitte den Netzwerkpfad \\\\server\\freigabe wählen.";
                return null;
            }
            return ziel + voll.Substring(2);
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetGetConnection(string lokalerName, StringBuilder entfernterName, ref int laenge);

        const int ERROR_MORE_DATA = 234;
        const int ERROR_CONNECTION_UNAVAIL = 1201;   // gemerkte, gerade nicht verbundene Zuordnung: der Name kommt trotzdem

        // "\\server\freigabe" zu einem verbundenen Netzlaufwerk ("Z:") oder null. Nur ein
        // brauchbarer UNC-Pfad mit Freigabe zaehlt; Geraete- und Laengennamen ("\\?\", "\\.\")
        // gelten wie in OrdnerPruefen nicht.
        static string NetzwerkpfadVon(string buchstabe)
        {
            try
            {
                int laenge = 1024;
                var sb = new StringBuilder(laenge);
                int rc = WNetGetConnection(buchstabe, sb, ref laenge);
                if (rc == ERROR_MORE_DATA && laenge > sb.Capacity)
                {
                    sb = new StringBuilder(laenge);
                    rc = WNetGetConnection(buchstabe, sb, ref laenge);
                }
                if (rc != 0 && rc != ERROR_CONNECTION_UNAVAIL) return null;
                string s = sb.ToString().TrimEnd('\\');
                if (s.Length < 5 || !s.StartsWith("\\\\", StringComparison.Ordinal)) return null;
                if (s[2] == '\\' || s[2] == '?' || s[2] == '.') return null;
                if (s.IndexOf('\\', 2) < 0) return null;     // "\\server" ohne Freigabe
                if (s.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || s.IndexOf('"') >= 0) return null;
                return s;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Wahr, wenn der Pfad oder eine vorhandene Elternkomponente eine Abzweigung ist
        /// (FileAttributes.ReparsePoint: Junction, symbolischer Link, Einhaengepunkt, auch
        /// Platzhalter eines Cloud-Speichers). Fehlende Komponenten zaehlen nicht; ein Attribut,
        /// das sich nicht lesen laesst, gilt als Abzweigung: was sich nicht pruefen laesst,
        /// bekommt keinen erhoehten Schreibzugriff.
        /// </summary>
        public static bool HatAbzweigung(string voll)
        {
            string fehler;
            return HatAbzweigung(voll, out fehler);
        }

        /// <summary>Wie HatAbzweigung(voll); fehler ist bei true der Lesefehler (null = echte Abzweigung gefunden).</summary>
        public static bool HatAbzweigung(string voll, out string fehler)
        {
            fehler = null;
            string p = voll;
            while (!string.IsNullOrEmpty(p))
            {
                try
                {
                    FileAttributes a = File.GetAttributes(p);
                    if ((a & FileAttributes.ReparsePoint) != 0) return true;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception ex) { fehler = ex.Message.Trim().TrimEnd('.'); return true; }
                try { p = Path.GetDirectoryName(p); }
                catch (Exception ex) { fehler = ex.Message.Trim().TrimEnd('.'); return true; }
            }
            return false;
        }

        // ---------------------------------------------------------------- Wiederherstellungspunkt

        /// <summary>
        /// Beschreibung fuer CreateRestorePoint: hoechstens 60 Zeichen, nur
        /// [A-Za-z0-9 äöüÄÖÜß._-]. Leer = StandardBeschreibung. Ungueltig = null (kein
        /// stilles Bereinigen: was der Nutzer sieht, muss auch das sein, was angelegt wird).
        /// </summary>
        public static string BeschreibungPruefen(string s)
        {
            if (s == null || s.Trim().Length == 0) return StandardBeschreibung;
            string t = s.Trim();
            if (t.Length > 60) return null;
            foreach (char c in t)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == ' ' || c == '.' || c == '_' || c == '-'
                          || c == 'ä' || c == 'ö' || c == 'ü' || c == 'Ä' || c == 'Ö' || c == 'Ü' || c == 'ß';
                if (!ok) return null;
            }
            return t;
        }

        // ---------------------------------------------------------------- Zeitplan

        static readonly string[] Wochentage = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };

        /// <summary>
        /// Prueft die Parameter von zeitplan.anlegen (modus, tage, tag, stunde, minute,
        /// aktionen) wie bisher ShellForm.ScheduleCreate. null = in Ordnung, sonst der Grund.
        /// dSpec ist das /D-Argument fuer schtasks ("MON,WED" bzw. "15"), tage die Wochentage
        /// in Wochenreihenfolge, aktionen die gewaehlten Aufgaben-Schluessel in Katalogreihenfolge
        /// oder null (= Standardsatz, bewusst nicht gespeichert).
        /// </summary>
        public static string ZeitplanPruefen(PlanSchritt s, out string modus, out string dSpec, out string[] tage,
                                             out int dom, out int hh, out int mm, out string[] aktionen)
        {
            modus = s == null ? null : s.Wert("modus");
            dSpec = "";
            tage = null;
            dom = 1;
            hh = 0;
            mm = 0;
            aktionen = null;
            if (s == null) return "Kein Schritt übergeben.";

            if (modus != "daily" && modus != "weekly" && modus != "monthly")
                return "Der Modus muss daily, weekly oder monthly sein, nicht „" + (modus ?? "") + "“.";
            if (!GanzeZahl(s.Wert("stunde"), 0, 23, out hh))
                return "Die Stunde muss eine ganze Zahl von 0 bis 23 sein, nicht „" + (s.Wert("stunde") ?? "") + "“.";
            if (!GanzeZahl(s.Wert("minute"), 0, 59, out mm))
                return "Die Minute muss eine ganze Zahl von 0 bis 59 sein, nicht „" + (s.Wert("minute") ?? "") + "“.";

            if (modus == "weekly")
            {
                // Wochentage: nur Whitelist-Tokens, dedupliziert, in Wochenreihenfolge (fuer /D MON,WED,...)
                List<string> gewuenscht = s.Liste("tage");
                foreach (string tok in gewuenscht)
                    if (Array.IndexOf(Wochentage, tok) < 0)
                        return "Unbekannter Wochentag „" + tok + "“; erlaubt sind MON, TUE, WED, THU, FRI, SAT und SUN.";
                var gewaehlt = new List<string>();
                foreach (string tok in Wochentage)
                    if (gewuenscht.Contains(tok)) gewaehlt.Add(tok);
                if (gewaehlt.Count == 0) return "Bei weekly braucht es mindestens 1 Wochentag.";
                tage = gewaehlt.ToArray();
                dSpec = string.Join(",", tage);
            }
            if (modus == "monthly")
            {
                if (!GanzeZahl(s.Wert("tag"), 1, 31, out dom))
                    return "Der Monatstag muss eine ganze Zahl von 1 bis 31 sein, nicht „" + (s.Wert("tag") ?? "") + "“.";
                dSpec = dom.ToString(CultureInfo.InvariantCulture);
            }

            // Aufgaben-Satz: nur bekannte Katalog-Schluessel, Katalogreihenfolge; leer = Standard.
            List<string> gewuenschteAktionen = s.Liste("aktionen");
            if (gewuenschteAktionen.Count > 0)
            {
                List<AutoItem> katalog = Catalog.AutoCatalog();
                foreach (string key in gewuenschteAktionen)
                {
                    bool bekannt = false;
                    foreach (AutoItem it in katalog) if (it.Key == key) { bekannt = true; break; }
                    if (!bekannt) return "Unbekannte Aufgabe „" + key + "“ im Zeitplan.";
                }
                var keys = new List<string>();
                foreach (AutoItem it in katalog)
                    if (gewuenschteAktionen.Contains(it.Key) && !keys.Contains(it.Key)) keys.Add(it.Key);
                aktionen = keys.ToArray();
            }
            return null;
        }

        /// <summary>Nur Ziffern (keine Vorzeichen, keine Leerzeichen), im Bereich min..max.</summary>
        public static bool GanzeZahl(string s, int min, int max, out int wert)
        {
            wert = 0;
            if (string.IsNullOrEmpty(s) || s.Length > 9) return false;
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out wert)) return false;
            return wert >= min && wert <= max;
        }
    }
}
