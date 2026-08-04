using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;

namespace WartungsToolbox
{
    // Listet vorinstallierte Appx-Pakete (Get-AppxPackage) und bietet ausschliesslich
    // bekannte, gefahrlos entfernbare Bloatware zur Auswahl an. Das Tool laeuft elevated,
    // darum ist Sicherheit hier zentral:
    //   * Quelle der Wahrheit ist die Whitelist (Catalog) - nur was dort steht, ist auswaehlbar.
    //   * Eine harte Blockliste (Critical) schuetzt System-, Store- und Defender-Pakete
    //     zusaetzlich (Defense-in-Depth), falls je ein falscher Eintrag in den Katalog geriete.
    //   * Der PackageFullName wird vor jeder Verwendung streng auf erlaubte Zeichen geprueft
    //     -> kein ungeprueftes Einsetzen in PowerShell-Befehle (keine Injektion).
    // Hinweis: C# 5 (csc) - keine String-Interpolation, kein ?./nameof. Literale bleiben ASCII.
    static class AppxCleaner
    {
        struct Bloat
        {
            public string Key;     // Get-AppxPackage-Name (oder Praefix)
            public string Label;   // Anzeigename (ASCII)
            public string Cat;     // Kategorie fuer die Gruppierung im UI
            public bool Prefix;    // true => Name beginnt mit Key (Drittanbieter-Familien)
            public Bloat(string key, string label, string cat, bool prefix)
            { Key = key; Label = label; Cat = cat; Prefix = prefix; }
        }

        // Reihenfolge der Kategorien im UI (Sortierung weiter unten).
        static readonly string[] CatOrder = new string[] {
            "Spiele & Xbox", "Medien", "Bing & Nachrichten",
            "Kommunikation & Office", "System-Extras"
        };

        // Bekannte, sichere Bloatware. Bewusst konservativ gehalten.
        static readonly Bloat[] Catalog = new Bloat[] {
            // --- Spiele & Xbox ---
            new Bloat("Microsoft.MicrosoftSolitaireCollection", "Solitaire-Sammlung",   "Spiele & Xbox", false),
            new Bloat("Microsoft.XboxGamingOverlay",            "Xbox Game Bar",         "Spiele & Xbox", false),
            new Bloat("Microsoft.XboxGameOverlay",              "Xbox Game-Overlay",     "Spiele & Xbox", false),
            new Bloat("Microsoft.XboxSpeechToTextOverlay",      "Xbox Sprache-zu-Text",  "Spiele & Xbox", false),
            new Bloat("Microsoft.GamingApp",                    "Xbox-App",              "Spiele & Xbox", false),
            new Bloat("Microsoft.XboxApp",                      "Xbox (Begleiter)",      "Spiele & Xbox", false),
            new Bloat("king.com.",                              "King-Spiele (Candy Crush u. a.)", "Spiele & Xbox", true),

            // --- Medien ---
            new Bloat("Microsoft.ZuneMusic",          "Groove-Musik / Media Player", "Medien", false),
            new Bloat("Microsoft.ZuneVideo",          "Filme & TV",            "Medien", false),
            new Bloat("Clipchamp.Clipchamp",          "Clipchamp (Videoeditor)", "Medien", false),
            new Bloat("Microsoft.MSPaint",            "Paint 3D",              "Medien", false),
            new Bloat("Microsoft.Microsoft3DViewer",  "3D-Viewer",             "Medien", false),
            new Bloat("Microsoft.3DBuilder",          "3D Builder",            "Medien", false),
            new Bloat("Microsoft.MixedReality.Portal","Mixed-Reality-Portal",  "Medien", false),
            new Bloat("SpotifyAB.SpotifyMusic",       "Spotify",               "Medien", false),

            // --- Bing & Nachrichten ---
            new Bloat("Microsoft.BingNews",     "Nachrichten (Bing)", "Bing & Nachrichten", false),
            new Bloat("Microsoft.BingWeather",  "Wetter (MSN)",       "Bing & Nachrichten", false),
            new Bloat("Microsoft.BingFinance",  "Finanzen (MSN)",     "Bing & Nachrichten", false),
            new Bloat("Microsoft.BingSports",   "Sport (MSN)",        "Bing & Nachrichten", false),
            new Bloat("Microsoft.WindowsMaps",  "Karten",             "Bing & Nachrichten", false),

            // --- Kommunikation & Office ---
            new Bloat("Microsoft.MicrosoftOfficeHub",        "Office (Startseite)", "Kommunikation & Office", false),
            new Bloat("Microsoft.SkypeApp",                  "Skype",                 "Kommunikation & Office", false),
            new Bloat("MicrosoftTeams",                      "Teams (privat / Chat)", "Kommunikation & Office", false),
            new Bloat("Microsoft.Todos",                     "Microsoft To Do",       "Kommunikation & Office", false),
            new Bloat("Microsoft.People",                    "Kontakte",              "Kommunikation & Office", false),
            new Bloat("Microsoft.windowscommunicationsapps", "Mail und Kalender",     "Kommunikation & Office", false),
            new Bloat("Microsoft.YourPhone",                 "Smartphone-Link",       "Kommunikation & Office", false),
            new Bloat("Microsoft.OutlookForWindows",         "Outlook (neu)",         "Kommunikation & Office", false),

            // --- System-Extras ---
            new Bloat("Microsoft.GetHelp",                       "Hilfe anfordern",     "System-Extras", false),
            new Bloat("Microsoft.Getstarted",                    "Tipps",               "System-Extras", false),
            new Bloat("Microsoft.WindowsFeedbackHub",            "Feedback-Hub",        "System-Extras", false),
            new Bloat("Microsoft.PowerAutomateDesktop",          "Power Automate (Desktop)", "System-Extras", false),
            new Bloat("Microsoft.Whiteboard",                    "Whiteboard",          "System-Extras", false),
            new Bloat("Microsoft.549981C3F5F10",                 "Cortana",             "System-Extras", false),
            new Bloat("MicrosoftCorporationII.MicrosoftFamily",  "Microsoft Family",    "System-Extras", false),
        };

        // Hart geschuetzt: taucht NIE in der Auswahl auf und wird beim Entfernen abgelehnt -
        // auch wenn ein Eintrag versehentlich im Katalog landet. Vergleich als Teilstring,
        // klein geschrieben. Bewusst breit gefasst (System/Shell/Store/Defender/Runtime).
        static readonly string[] Critical = new string[] {
            "windowsstore", "desktopappinstaller", "storepurchaseapp", "services.store",
            "vclibs", "net.native", "ui.xaml", "microsoft.ui.", "windowsappruntime",
            "sechealthui", "webexperience", "widgetsplatform",
            "shellexperiencehost", "startmenuexperiencehost", "windows.search",
            "cloudexperiencehost", "contentdeliverymanager", "aad.brokerplugin",
            "accountscontrol", "lockapp", "microsoftedge", "win32webviewhost",
            "creddialoghost", "immersivecontrolpanel", "windows.cbs", "microsoftwindows.client",
            "windows.shellexperience", "secureassessment", "peopleexperiencehost",
            "xboxidentityprovider", "xbox.tcui", "narrator", "asynctextservice",
            "windows.apprep", "windows.assignedaccess", "biometric", "ecapp",
            "ppiprojection", "capturepicker", "parentalcontrols", "oobenetwork",
            "windows.holographic", "pinningconfirmation", "wallet",
        };

        // ----- Sicherheits-Gates -----

        public static bool IsCritical(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string n = name.ToLowerInvariant();
            for (int i = 0; i < Critical.Length; i++)
                if (n.IndexOf(Critical[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static bool TryMatch(string name, out string label, out string cat)
        {
            label = null; cat = null;
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < Catalog.Length; i++)
            {
                Bloat b = Catalog[i];
                bool hit = b.Prefix
                    ? name.StartsWith(b.Key, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(name, b.Key, StringComparison.OrdinalIgnoreCase);
                if (hit) { label = b.Label; cat = b.Cat; return true; }
            }
            return false;
        }

        // PackageFullName darf nur unkritische Zeichen enthalten -> kein Ausbrechen aus dem
        // PowerShell-String, keine Shell-Metazeichen.
        public static bool ValidFullName(string full)
        {
            if (string.IsNullOrEmpty(full) || full.Length > 200) return false;
            foreach (char c in full)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // Name = Teil vor dem ersten '_' (Appx-Namen enthalten keinen Unterstrich).
        static string NameOfFull(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            int u = full.IndexOf('_');
            return u > 0 ? full.Substring(0, u) : full;
        }

        // Massgebliches Gate fuers Entfernen: gueltige Zeichen + im Katalog + nicht kritisch.
        public static bool IsRemovable(string full)
        {
            if (!ValidFullName(full)) return false;
            string name = NameOfFull(full);
            if (IsCritical(name)) return false;
            string l, c;
            return TryMatch(name, out l, out c);
        }

        public static string LabelFor(string full)
        {
            string name = NameOfFull(full);
            string l, c;
            if (TryMatch(name, out l, out c)) return l;
            return name;
        }

        // Anzeigetext fuer eine Ausgabe absichern (in einfache Anfuehrungszeichen eingebettet).
        public static string SafeLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Paket";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                bool ok = char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '-' || c == '_'
                          || c == '(' || c == ')' || c == '/' || c == '&' || c == '+' || c == ':';
                if (ok) sb.Append(c);
                if (sb.Length >= 60) break;
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "Paket" : r;
        }

        // ----- Auflisten -----

        // Vorhandene, sicher entfernbare Bloatware. Synchron -> aus einem Hintergrund-Thread aufrufen.
        public static List<object> List()
        {
            List<object> list = new List<object>();
            string ps =
                "$ErrorActionPreference='SilentlyContinue';" +
                "$p = Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable };" +
                "$out = @($p | ForEach-Object { [pscustomobject]@{ " +
                "name=[string]$_.Name; full=[string]$_.PackageFullName; pub=[string]$_.Publisher } });" +
                "$out | ConvertTo-Json -Compress";

            string json = RunPs(ps, 30000);
            if (string.IsNullOrEmpty(json)) return list;

            object parsed;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                parsed = js.DeserializeObject(json);
            }
            catch { return list; }

            List<object> raw = new List<object>();
            if (parsed is object[]) { foreach (object o in (object[])parsed) raw.Add(o); }
            else if (parsed is Dictionary<string, object>) raw.Add(parsed); // genau ein Treffer

            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (object o in raw)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                string name = GetStr(d, "name");
                string full = GetStr(d, "full");
                string pub = GetStr(d, "pub");
                if (name.Length == 0 || full.Length == 0) continue;
                if (!ValidFullName(full)) continue;                 // nur sauber benannte Pakete
                if (IsCritical(name)) continue;                     // harter Schutz
                string label, cat;
                if (!TryMatch(name, out label, out cat)) continue;  // nur bekannte Bloatware
                if (seen.ContainsKey(name)) continue;               // pro App nur eine Zeile
                seen[name] = true;

                Dictionary<string, object> item = new Dictionary<string, object>();
                item["name"] = name;
                item["full"] = full;
                item["label"] = label;
                item["cat"] = cat;
                item["pub"] = ShortPublisher(pub);
                list.Add(item);
            }

            list.Sort(delegate(object a, object b)
            {
                int ra = CatRank(CatOf(a)), rb = CatRank(CatOf(b));
                if (ra != rb) return ra.CompareTo(rb);
                return string.Compare(LabelOf(a), LabelOf(b), StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        // ----- Helfer -----

        static int CatRank(string cat)
        {
            for (int i = 0; i < CatOrder.Length; i++)
                if (string.Equals(cat, CatOrder[i], StringComparison.OrdinalIgnoreCase)) return i;
            return CatOrder.Length;
        }

        static string CatOf(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null && d.ContainsKey("cat") && d["cat"] != null) return d["cat"].ToString();
            return "";
        }
        static string LabelOf(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null && d.ContainsKey("label") && d["label"] != null) return d["label"].ToString();
            return "";
        }

        static string GetStr(Dictionary<string, object> d, string k)
        {
            object v;
            return (d.TryGetValue(k, out v) && v != null) ? v.ToString() : "";
        }

        // "CN=Spotify AB, O=..." -> "Spotify AB"
        static string ShortPublisher(string pub)
        {
            if (string.IsNullOrEmpty(pub)) return "";
            string s = pub;
            int i = pub.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                s = pub.Substring(i + 3);
                int comma = s.IndexOf(',');
                if (comma >= 0) s = s.Substring(0, comma);
            }
            s = s.Trim();
            if (s.Length > 30) s = s.Substring(0, 29).Trim() + "...";
            return s;
        }

        static string RunPs(string command, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding((int)Native.GetOEMCP());
                using (Process p = Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> rd = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                    try { return rd.Result; } catch { return ""; }
                }
            }
            catch { return ""; }
        }
    }
}
