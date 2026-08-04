using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WartungsToolbox
{
    /// <summary>
    /// Die lesenden Pruefungen des Hauptwegs. Sie veraendern NICHTS am System.
    ///
    /// Zwei Grundregeln, beide aus dem Praxistest entstanden:
    ///
    /// 1. Fehlende Daten sind kein Problem. Ohne Adminrechte liefern mehrere Abfragen
    ///    nichts, ein Desktop hat keinen Akku, Windows Home kennt keine Laufwerks-
    ///    verschluesselung. In all diesen Faellen lautet der Zustand "unbekannt" und der
    ///    Text sagt das auch - niemals eine Warnung erfinden.
    ///
    /// 2. Rohstatus nicht durchreichen. Get-PnpDevice meldete auf einem kerngesunden PC
    ///    drei Geraete als fehlerhaft. Wer solche Rohwerte ungefiltert anzeigt, macht
    ///    Laien grundlos Angst - genau das Verhalten unserioeser Programme.
    /// </summary>
    static class Diagnostics
    {
        public const string Ok = "ok";
        public const string Warn = "warn";
        public const string Bad = "bad";
        public const string Unknown = "unknown";

        public class Check
        {
            public string Key;
            public string Title;      // Bereichsname in Alltagssprache
            public string State;      // ok | warn | bad | unknown
            public string Summary;    // EIN Satz: was ist der Befund
            public string Advice;     // was der Nutzer tun soll (null = nichts zu tun)
            public string Detail;     // optionale Zusatzzeilen fuer den Detailbereich
            public bool CanFix;       // kann die App das selbst beheben?

            public object ToJson()
            {
                return new { key = Key, title = Title, state = State, summary = Summary,
                             advice = Advice, detail = Detail, canFix = CanFix };
            }
        }

        /// <summary>Alle lesenden Pruefungen. Nur aus einem Hintergrund-Thread aufrufen.</summary>
        public static List<Check> RunAll(Action<string> progress)
        {
            var res = new List<Check>();
            void Step(string label, Func<Check> f)
            {
                if (progress != null) progress(label);
                try { res.Add(f()); }
                catch (Exception ex)
                {
                    AppLog.Error("Pruefung " + label, ex);
                    res.Add(new Check { Key = label, Title = label, State = Unknown,
                                        Summary = "Diese Prüfung war auf diesem PC nicht möglich." });
                }
            }

            Step("Speicherplatz", CheckSpace);
            Step("Sicherheit", CheckSecurity);
            Step("Festplatten", CheckDiskHealth);
            Step("Dateisystem", CheckFileSystem);
            Step("Windows-Updates", CheckUpdates);
            Step("Stabilität", CheckStability);
            Step("Akku", CheckBattery);
            return res;
        }

        // ---------------------------------------------------------------- Speicherplatz

        static Check CheckSpace()
        {
            var c = new Check { Key = "space", Title = "Freier Speicherplatz", CanFix = true };
            var lines = new List<string>();
            double worstFreePct = 100;
            double worstFreeGb = 0;
            string worstName = null;

            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                    double freeGb = d.TotalFreeSpace / 1073741824.0;
                    double totalGb = d.TotalSize / 1073741824.0;
                    double pct = 100.0 * d.TotalFreeSpace / d.TotalSize;
                    lines.Add(string.Format("Laufwerk {0} {1}: {2:N0} GB von {3:N0} GB frei",
                        d.Name.TrimEnd('\\', ':'),
                        string.IsNullOrEmpty(SafeLabel(d)) ? "" : "(" + SafeLabel(d) + ") ",
                        freeGb, totalGb));
                    if (pct < worstFreePct) { worstFreePct = pct; worstFreeGb = freeGb; worstName = d.Name.TrimEnd('\\', ':'); }
                }
                catch { }
            }

            c.Detail = string.Join(Environment.NewLine, lines);

            if (worstName == null)
            {
                c.State = Unknown;
                c.Summary = "Zu den Laufwerken konnten keine Daten gelesen werden.";
                return c;
            }

            // Schwellen ausdruecklich benannt, damit der Befund nachvollziehbar bleibt.
            if (worstFreePct < 10 || worstFreeGb < 10)
            {
                c.State = Bad;
                c.Summary = string.Format("Auf Laufwerk {0} sind nur noch {1:N0} GB frei. Das ist zu wenig: Windows braucht Platz für Updates und zum Arbeiten.", worstName, worstFreeGb);
                c.Advice = "Machen Sie Platz frei. Der Wartungslauf entfernt Datenmüll; für dauerhaft mehr Platz löschen Sie große Dateien, die Sie nicht mehr brauchen.";
            }
            else if (worstFreePct < 20)
            {
                c.State = Warn;
                c.Summary = string.Format("Auf Laufwerk {0} sind noch {1:N0} GB frei. Das reicht vorerst, wird aber allmählich eng.", worstName, worstFreeGb);
                c.Advice = "Der Wartungslauf schafft etwas Platz. Behalten Sie es im Auge.";
            }
            else
            {
                c.State = Ok;
                c.Summary = string.Format("Auf allen Laufwerken ist genug Platz, mindestens {0:N0} GB frei.", worstFreeGb);
            }
            return c;
        }

        static string SafeLabel(DriveInfo d)
        {
            try { return d.VolumeLabel ?? ""; } catch { return ""; }
        }

        // ---------------------------------------------------------------- Sicherheit

        static Check CheckSecurity()
        {
            var c = new Check { Key = "security", Title = "Sicherheit" };
            var detail = new List<string>();

            var mp = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "$s = Get-MpComputerStatus;" +
                "if ($s) { [pscustomobject]@{ rt=[bool]$s.RealTimeProtectionEnabled; " +
                "svc=[bool]$s.AMServiceEnabled; sigAge=[int]$s.AntivirusSignatureAge } | ConvertTo-Json -Compress }",
                20000);

            var fw = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "@(Get-NetFirewallProfile | ForEach-Object { [pscustomobject]@{ name=[string]$_.Name; on=[bool]$_.Enabled } }) | ConvertTo-Json -Compress",
                20000);

            bool? rt = mp.Count > 0 ? Shell.Flag(mp[0], "rt") : null;
            int sigAge = mp.Count > 0 ? (int)Shell.Num(mp[0], "sigAge", -1) : -1;

            int fwOff = 0, fwTotal = 0;
            foreach (var p in fw)
            {
                fwTotal++;
                if (Shell.Flag(p, "on") == false) fwOff++;
            }

            if (rt == null && fwTotal == 0)
            {
                // Kein Defender greifbar heisst nicht ungeschuetzt: es kann ein anderes
                // Schutzprogramm laufen. Also ehrlich "unbekannt" melden.
                c.State = Unknown;
                c.Summary = "Der Schutzstatus ließ sich nicht auslesen. Möglicherweise ist ein anderes Schutzprogramm als das von Windows aktiv.";
                return c;
            }

            if (rt.HasValue) detail.Add("Virenschutz in Echtzeit: " + (rt.Value ? "an" : "AUS"));
            if (sigAge >= 0) detail.Add("Erkennungsdaten: " + (sigAge == 0 ? "heute aktualisiert" : sigAge + " Tage alt"));
            if (fwTotal > 0) detail.Add("Firewall: " + (fwOff == 0 ? "in allen Bereichen an" : fwOff + " von " + fwTotal + " Bereichen AUS"));
            c.Detail = string.Join(Environment.NewLine, detail);

            if (rt == false)
            {
                c.State = Bad;
                c.Summary = "Der Virenschutz von Windows ist ausgeschaltet.";
                c.Advice = "Schalten Sie ihn wieder ein: Start-Taste, dann „Windows-Sicherheit“ eingeben und öffnen. Falls Sie ein anderes Schutzprogramm nutzen, ist das in Ordnung.";
            }
            else if (fwOff > 0)
            {
                c.State = Warn;
                c.Summary = "Die Firewall ist nicht überall eingeschaltet.";
                c.Advice = "Öffnen Sie die Windows-Sicherheit und schalten Sie die Firewall für alle Netzwerke ein.";
            }
            else if (sigAge > 7)
            {
                c.State = Warn;
                c.Summary = "Die Erkennungsdaten des Virenschutzes sind " + sigAge + " Tage alt.";
                c.Advice = "Prüfen Sie Ihre Internetverbindung und suchen Sie in den Windows-Einstellungen nach Updates.";
            }
            else
            {
                c.State = Ok;
                c.Summary = "Virenschutz und Firewall sind aktiv und auf dem neuesten Stand.";
            }
            return c;
        }

        // ---------------------------------------------------------------- Festplatten

        static Check CheckDiskHealth()
        {
            var c = new Check { Key = "disk", Title = "Zustand der Festplatten" };

            var disks = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "@(Get-PhysicalDisk | ForEach-Object { " +
                "  $rc = $_ | Get-StorageReliabilityCounter;" +
                "  [pscustomobject]@{ name=[string]$_.FriendlyName; media=[string]$_.MediaType; " +
                "    health=[string]$_.HealthStatus; sizeGb=[int]($_.Size/1GB); " +
                "    wear=$(if ($rc -and $rc.Wear -ne $null) { [int]$rc.Wear } else { -1 }); " +
                "    hours=$(if ($rc -and $rc.PowerOnHours -ne $null) { [int]$rc.PowerOnHours } else { -1 }) } }) | ConvertTo-Json -Compress",
                40000);

            if (disks.Count == 0)
            {
                c.State = Unknown;
                c.Summary = "Der Zustand der Festplatten ließ sich nicht auslesen.";
                return c;
            }

            var detail = new List<string>();
            string worst = Ok;
            string problem = null;

            foreach (var d in disks)
            {
                string name = Shell.Str(d, "name") ?? "Laufwerk";
                string health = Shell.Str(d, "health") ?? "";
                string media = Shell.Str(d, "media") ?? "";
                int wear = (int)Shell.Num(d, "wear", -1);
                int hours = (int)Shell.Num(d, "hours", -1);

                string art = media == "SSD" ? "SSD" : (media == "HDD" ? "Festplatte" : "Laufwerk");
                string line = art + " " + name + ": ";

                if (string.Equals(health, "Healthy", StringComparison.OrdinalIgnoreCase))
                    line += "meldet sich als gesund";
                else if (string.IsNullOrEmpty(health))
                    line += "meldet keinen Zustand";
                else
                {
                    line += "meldet ein Problem";
                    if (worst != Bad) { worst = Bad; problem = name; }
                }

                if (wear >= 0) line += ", Verschleiß " + wear + " %";
                if (hours > 0) line += ", " + (hours / 24) + " Tage in Betrieb";
                detail.Add(line);

                // Verschleiss ab 80 % ist eine Vorwarnung, keine Panik.
                if (wear >= 80 && worst == Ok) { worst = Warn; problem = name; }
            }

            c.Detail = string.Join(Environment.NewLine, detail);

            if (worst == Bad)
            {
                c.State = Bad;
                c.Summary = "Ein Laufwerk (" + problem + ") meldet ein Problem mit sich selbst.";
                c.Advice = "Sichern Sie Ihre wichtigen Dateien zeitnah auf eine externe Festplatte oder in die Cloud. Ein Laufwerk, das sich selbst als beschädigt meldet, kann ausfallen.";
            }
            else if (worst == Warn)
            {
                c.State = Warn;
                c.Summary = "Ein Laufwerk (" + problem + ") ist stark abgenutzt, arbeitet aber noch.";
                c.Advice = "Achten Sie darauf, dass Ihre wichtigen Dateien gesichert sind. Ein Austausch ist absehbar, aber nicht dringend.";
            }
            else
            {
                c.State = Ok;
                c.Summary = disks.Count == 1
                    ? "Ihr Laufwerk meldet sich als gesund."
                    : "Alle " + disks.Count + " Laufwerke melden sich als gesund.";
            }
            return c;
        }

        // ---------------------------------------------------------------- Dateisystem

        static Check CheckFileSystem()
        {
            var c = new Check { Key = "filesystem", Title = "Ordnung auf der Festplatte" };
            string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
            string letter = sys.Substring(0, 1);

            // fsutil dirty query ist schnell und sagt, ob Windows selbst eine Pruefung
            // vorgemerkt hat. Repair-Volume -Scan waere gruendlicher, dauert aber Minuten -
            // das gehoert nicht in einen Schnellbefund.
            var r = Shell.Run("fsutil.exe", "dirty query " + letter + ":", 15000);
            string outp = (r.Output ?? "") + (r.Error ?? "");

            if (!r.Started)
            {
                c.State = Unknown;
                c.Summary = "Ob das Dateisystem in Ordnung ist, ließ sich nicht feststellen.";
                return c;
            }

            // Die Ausgabe ist sprachabhaengig. "NICHT" bzw. "not" heisst: alles sauber.
            string low = outp.ToLowerInvariant();
            bool dirty = low.Contains("ist verschmutzt") || (low.Contains("is dirty") && !low.Contains("is not dirty"));
            if (low.Contains("nicht verschmutzt") || low.Contains("is not dirty")) dirty = false;

            c.Detail = outp.Trim();
            if (dirty)
            {
                c.State = Warn;
                c.Summary = "Windows hat für Laufwerk " + letter + " eine Festplattenprüfung vorgemerkt.";
                c.Advice = "Starten Sie den PC bei Gelegenheit neu. Die Prüfung läuft dann automatisch und dauert einige Minuten.";
            }
            else
            {
                c.State = Ok;
                c.Summary = "Das Dateisystem auf Laufwerk " + letter + " ist in Ordnung.";
            }
            return c;
        }

        // ---------------------------------------------------------------- Updates

        static Check CheckUpdates()
        {
            var c = new Check { Key = "updates", Title = "Windows-Updates" };

            // Nur die Registry lesen: schnell und ohne Netz. Die Online-Suche nach offenen
            // Updates dauert bis zu 90 Sekunden und gehoert nicht in einen Schnellbefund.
            bool pending = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                {
                    pending = k != null;
                }
            }
            catch { }

            var hf = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "$h = Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 1;" +
                "if ($h) { [pscustomobject]@{ id=[string]$h.HotFixID; " +
                "days=$(if ($h.InstalledOn) { [int]((Get-Date) - $h.InstalledOn).TotalDays } else { -1 }) } | ConvertTo-Json -Compress }",
                30000);

            int days = hf.Count > 0 ? (int)Shell.Num(hf[0], "days", -1) : -1;
            if (days >= 0) c.Detail = "Zuletzt installiertes Update: vor " + days + " Tagen (" + (Shell.Str(hf[0], "id") ?? "") + ")";

            if (pending)
            {
                c.State = Warn;
                c.Summary = "Ein Update wartet auf einen Neustart, um fertig zu werden.";
                c.Advice = "Starten Sie den PC bei Gelegenheit neu. Bis dahin kann Windows sich merkwürdig verhalten.";
            }
            else if (days > 60)
            {
                c.State = Warn;
                c.Summary = "Das letzte Windows-Update ist " + days + " Tage her.";
                c.Advice = "Öffnen Sie die Windows-Einstellungen und suchen Sie unter „Windows Update“ nach Updates.";
            }
            else if (days < 0)
            {
                c.State = Unknown;
                c.Summary = "Der Update-Stand ließ sich nicht auslesen.";
            }
            else
            {
                c.State = Ok;
                c.Summary = "Windows ist auf einem aktuellen Stand, kein Neustart steht aus.";
            }
            return c;
        }

        // ---------------------------------------------------------------- Stabilität

        static Check CheckStability()
        {
            var c = new Check { Key = "stability", Title = "Stabilität" };

            var m = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "$x = Get-CimInstance Win32_ReliabilityStabilityMetrics | " +
                "  Sort-Object TimeGenerated -Descending | Select-Object -First 1;" +
                "if ($x) { [pscustomobject]@{ idx=[double]$x.SystemStabilityIndex } | ConvertTo-Json -Compress }",
                30000);

            // Abstuerze der letzten 14 Tage ergaenzen den Notenwert.
            var crash = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "$n = @(Get-WinEvent -FilterHashtable @{LogName='System'; Id=41,1001,6008; " +
                "  StartTime=(Get-Date).AddDays(-14)} -EA SilentlyContinue).Count;" +
                "[pscustomobject]@{ n=[int]$n } | ConvertTo-Json -Compress",
                30000);

            int crashes = crash.Count > 0 ? (int)Shell.Num(crash[0], "n", 0) : 0;
            double idx = m.Count > 0 ? Shell.Num(m[0], "idx", -1) : -1;

            if (idx >= 0) c.Detail = string.Format("Windows bewertet die Stabilität mit {0:N1} von 10.", idx);

            if (crashes >= 3)
            {
                c.State = Bad;
                c.Summary = "In den letzten 14 Tagen ist der PC " + crashes + " Mal unerwartet ausgegangen oder abgestürzt.";
                c.Advice = "Der Wartungslauf prüft die Windows-Dateien. Hilft das nicht, prüfen Sie Arbeitsspeicher und Festplatte über die Werkzeuge unter „Nachsehen“.";
            }
            else if (crashes > 0)
            {
                c.State = Warn;
                c.Summary = "In den letzten 14 Tagen gab es " + (crashes == 1 ? "einen unerwarteten Absturz" : crashes + " unerwartete Abstürze") + ".";
                c.Advice = "Einzelne Aussetzer sind meist harmlos. Häufen sie sich, melden Sie sich wieder.";
            }
            else if (idx < 0)
            {
                c.State = Unknown;
                c.Summary = "Zur Stabilität liegen auf diesem PC keine Daten vor.";
            }
            else
            {
                c.State = Ok;
                c.Summary = "Der PC lief in den letzten zwei Wochen ohne Abstürze.";
            }
            return c;
        }

        // ---------------------------------------------------------------- Akku

        static Check CheckBattery()
        {
            var c = new Check { Key = "battery", Title = "Akku" };

            var b = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "$d = Get-CimInstance -Namespace root\\wmi -ClassName BatteryStaticData;" +
                "$f = Get-CimInstance -Namespace root\\wmi -ClassName BatteryFullChargedCapacity;" +
                "if ($d -and $f) { [pscustomobject]@{ design=[int]($d | Select-Object -First 1).DesignedCapacity; " +
                "  full=[int]($f | Select-Object -First 1).FullChargedCapacity } | ConvertTo-Json -Compress }",
                25000);

            if (b.Count == 0)
            {
                // Desktop-PC: voellig normal, also kein Befund und keine Warnung.
                c.State = Unknown;
                c.Summary = "Dieser PC hat keinen Akku.";
                return c;
            }

            double design = Shell.Num(b[0], "design", 0);
            double full = Shell.Num(b[0], "full", 0);
            if (design <= 0 || full <= 0)
            {
                c.State = Unknown;
                c.Summary = "Der Akku meldet keine verwertbaren Werte.";
                return c;
            }

            int pct = (int)Math.Round(100.0 * full / design);
            c.Detail = string.Format("Der Akku speichert noch {0} % der Kapazität, die er neu hatte.", pct);

            if (pct < 60)
            {
                c.State = Warn;
                c.Summary = "Der Akku hält nur noch " + pct + " % der ursprünglichen Ladung.";
                c.Advice = "Das ist deutlicher Verschleiß. Ein Austausch bringt spürbar mehr Laufzeit; ein Sicherheitsproblem ist es nicht.";
            }
            else if (pct < 80)
            {
                c.State = Ok;
                c.Summary = "Der Akku hält noch " + pct + " % der ursprünglichen Ladung. Für sein Alter in Ordnung.";
            }
            else
            {
                c.State = Ok;
                c.Summary = "Der Akku ist mit " + pct + " % der ursprünglichen Ladung gut in Schuss.";
            }
            return c;
        }

        // ---------------------------------------------------------------- Gesamturteil

        /// <summary>
        /// Das Gesamturteil ist die schlechteste Einzelklasse - bewusst KEIN Punktestand.
        /// Eine nicht nachrechenbare Zahl ist das Erkennungszeichen unserioeser Programme.
        /// </summary>
        public static string Overall(IEnumerable<Check> checks)
        {
            bool bad = false, warn = false, any = false;
            foreach (Check c in checks)
            {
                if (c.State == Unknown) continue;
                any = true;
                if (c.State == Bad) bad = true;
                else if (c.State == Warn) warn = true;
            }
            if (!any) return Unknown;
            return bad ? Bad : (warn ? Warn : Ok);
        }

        public static int CountNotOk(IEnumerable<Check> checks)
        {
            return checks.Count(c => c.State == Bad || c.State == Warn);
        }
    }
}
