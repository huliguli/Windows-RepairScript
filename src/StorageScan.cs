using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WartungsToolbox
{
    /// <summary>
    /// Beantwortet die Frage, die nach "Ihr Speicher wird knapp" als naechstes kommt:
    /// WO steckt der Platz?
    ///
    /// Zwei Teile:
    ///   * Aufraeumbares nach Kategorien (Papierkorb, temporaere Dateien, Update-Reste ...)
    ///     mit Groesse UND einer ehrlichen Angabe, ob es gefahrlos weg kann.
    ///   * Die groessten Ordner und Dateien in den eigenen Bereichen, damit man selbst
    ///     entscheiden kann.
    ///
    /// Der Lauf liest NUR. Er loescht nichts.
    ///
    /// Drei Fallen, die hier bewusst umgangen werden:
    ///   1. Abzweigungen (Reparse Points) fuehren im Kreis oder in fremde Laufwerke.
    ///      Sie werden uebersprungen.
    ///   2. OneDrive legt Platzhalter an. Wer deren Inhalt anfasst, LAEDT SIE HERUNTER -
    ///      unter Umstaenden Gigabyte. Platzhalter erkennt man ebenfalls am Reparse Point
    ///      bzw. am Attribut "Offline"; beides wird uebersprungen.
    ///   3. Ein Vollscan von C: dauert Minuten. Gesucht wird deshalb nur dort, wo bei
    ///      Privatrechnern die grossen Brocken liegen, und der Lauf ist abbrechbar.
    /// </summary>
    static class StorageScan
    {
        public class Posten
        {
            public string Titel;
            public string Erklaerung;
            public long Bytes;
            public string Pfad;
            public bool Aufraeumbar;   // kann die App das selbst gefahrlos entfernen?

            public object ToJson()
            {
                return new { titel = Titel, erklaerung = Erklaerung, bytes = Bytes,
                             groesse = Menschlich(Bytes), pfad = Pfad, aufraeumbar = Aufraeumbar };
            }
        }

        public static string Menschlich(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("N1") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("N0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("N0") + " KB";
            return bytes + " Byte";
        }

        /// <summary>
        /// Fuehrt die Suche aus. fortschritt bekommt kurze Zwischenstaende fuer die Anzeige,
        /// abbruch wird regelmaessig abgefragt. Nur aus einem Hintergrund-Thread aufrufen.
        /// </summary>
        public static object Run(Action<string> fortschritt, Func<bool> abbruch)
        {
            var aufraeumbar = new List<Posten>();
            var brocken = new List<Posten>();

            void Melde(string text) { if (fortschritt != null) fortschritt(text); }
            bool Abbruch() { return abbruch != null && abbruch(); }

            // ---------- Teil 1: was die App selbst wegraeumen kann ----------

            Melde("Papierkorb");
            aufraeumbar.Add(new Posten
            {
                Titel = "Papierkorb",
                Erklaerung = "Gelöschte Dateien, die noch zurückgeholt werden könnten.",
                Bytes = OrdnerGroesse(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "$Recycle.Bin"), abbruch, 2),
                Aufraeumbar = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            Melde("temporäre Dateien");
            long temp = OrdnerGroesse(Path.GetTempPath(), abbruch, 4)
                      + OrdnerGroesse(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), abbruch, 4);
            aufraeumbar.Add(new Posten
            {
                Titel = "Temporäre Dateien",
                Erklaerung = "Zwischendateien, die Programme liegen gelassen haben.",
                Bytes = temp,
                Aufraeumbar = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            Melde("heruntergeladene Update-Dateien");
            aufraeumbar.Add(new Posten
            {
                Titel = "Heruntergeladene Update-Dateien",
                Erklaerung = "Windows behält Update-Pakete nach der Installation noch eine Weile.",
                Bytes = OrdnerGroesse(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"), abbruch, 4),
                Aufraeumbar = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            Melde("Vorschaubilder");
            string explorerCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Explorer");
            long thumbs = 0;
            try
            {
                foreach (string f in Directory.GetFiles(explorerCache, "thumbcache_*"))
                    thumbs += new FileInfo(f).Length;
            }
            catch { }
            aufraeumbar.Add(new Posten
            {
                Titel = "Vorschaubilder",
                Erklaerung = "Kleine Vorschauen für Fotos und Dateien. Windows legt sie bei Bedarf neu an.",
                Bytes = thumbs,
                Aufraeumbar = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            // Browser-Zwischenspeicher. Bewusst NUR der Cache, nicht Verlauf, Cookies oder
            // gespeicherte Passwoerter: wer die loescht, meldet den Nutzer ueberall ab und
            // "beschleunigt" nichts. Der Cache dagegen wird bei Bedarf neu gefuellt.
            Melde("Browser-Zwischenspeicher");
            long browser = 0;
            foreach (string p in BrowserCaches())
                browser += OrdnerGroesse(p, abbruch, 3);
            aufraeumbar.Add(new Posten
            {
                Titel = "Zwischenspeicher der Browser",
                Erklaerung = "Zwischengespeicherte Bilder und Dateien von Webseiten. Ihre Lesezeichen, "
                           + "Anmeldungen und Ihr Verlauf bleiben unberührt.",
                Bytes = browser,
                Aufraeumbar = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            // Spiele-Plattformen legen Shader- und Download-Zwischenspeicher an. Auf einem
            // Spiele-PC ist das oft der groesste aufraeumbare Posten ueberhaupt. Die Spiele
            // selbst werden nicht angefasst.
            Melde("Zwischenspeicher der Spiele-Plattformen");
            long spiele = 0;
            foreach (string p in SpieleCaches())
                spiele += OrdnerGroesse(p, abbruch, 3);
            if (spiele > 0)
            {
                aufraeumbar.Add(new Posten
                {
                    Titel = "Zwischenspeicher der Spiele-Plattformen",
                    Erklaerung = "Vorberechnete Grafikdaten und Reste von Downloads (Steam, Epic, GOG, "
                               + "Battle.net). Die Spiele selbst bleiben installiert.",
                    Bytes = spiele,
                    Aufraeumbar = true,
                });
            }

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            // Alte Update-Bausteine: NICHT selbst zaehlen. Der Ordner ist voller harter
            // Verweise, eine Groessenaddition ergibt eine viel zu hohe Zahl. Windows kann
            // das selbst ausrechnen - das macht die Aktion "Nachsehen, ob Aufräumen lohnt".
            Melde("Ordner der Nutzer");

            // ---------- Teil 2: die groessten Brocken in den eigenen Bereichen ----------

            string profil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] bereiche =
            {
                Path.Combine(profil, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };

            foreach (string b in bereiche)
            {
                if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);
                if (string.IsNullOrEmpty(b) || !Directory.Exists(b)) continue;
                Melde(Path.GetFileName(b));
                long g = OrdnerGroesse(b, abbruch, 8);
                if (g <= 0) continue;
                brocken.Add(new Posten
                {
                    Titel = Path.GetFileName(b),
                    Erklaerung = ErklaerungFuer(Path.GetFileName(b)),
                    Bytes = g,
                    Pfad = b,
                    Aufraeumbar = false,
                });
            }

            // Grosse Einzeldateien in denselben Bereichen (ab 200 MB).
            Melde("große Einzeldateien");
            var grosse = new List<Posten>();
            foreach (string b in bereiche)
            {
                if (Abbruch()) break;
                if (string.IsNullOrEmpty(b) || !Directory.Exists(b)) continue;
                SammleGrosseDateien(b, 200L * 1024 * 1024, grosse, abbruch, 8);
            }
            brocken.AddRange(grosse.OrderByDescending(p => p.Bytes).Take(15));

            return Ergebnis(aufraeumbar, brocken, Abbruch());
        }

        /// <summary>
        /// Zwischenspeicher der gaengigen Browser. Bewusst NUR "Cache"-Ordner: Verlauf,
        /// Cookies und Passwoerter bleiben tabu.
        /// </summary>
        static IEnumerable<string> BrowserCaches()
        {
            string lokal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Chromium-Familie: <Hersteller>\<Browser>\User Data\<Profil>\Cache
            var chromium = new[]
            {
                Path.Combine(lokal, "Google", "Chrome", "User Data"),
                Path.Combine(lokal, "Microsoft", "Edge", "User Data"),
                Path.Combine(lokal, "BraveSoftware", "Brave-Browser", "User Data"),
                Path.Combine(lokal, "Vivaldi", "User Data"),
                Path.Combine(roaming, "Opera Software", "Opera Stable"),
                Path.Combine(roaming, "Opera Software", "Opera GX Stable"),
            };
            foreach (string wurzel in chromium)
            {
                if (!Directory.Exists(wurzel)) continue;
                foreach (string name in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    string direkt = Path.Combine(wurzel, name);
                    if (Directory.Exists(direkt)) yield return direkt;
                }
                IEnumerable<string> profile;
                try { profile = Directory.EnumerateDirectories(wurzel); }
                catch { continue; }
                foreach (string prof in profile)
                {
                    foreach (string name in new[] { "Cache", "Code Cache", "GPUCache" })
                    {
                        string p = Path.Combine(prof, name);
                        if (Directory.Exists(p)) yield return p;
                    }
                }
            }

            // Firefox: Profile liegen unter LocalAppData\Mozilla\Firefox\Profiles
            string ff = Path.Combine(lokal, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(ff))
            {
                IEnumerable<string> profile;
                try { profile = Directory.EnumerateDirectories(ff); }
                catch { profile = new string[0]; }
                foreach (string prof in profile) yield return prof;
            }
        }

        /// <summary>
        /// Zwischenspeicher der Spiele-Plattformen. NUR Shader- und Download-Reste,
        /// niemals die Spielordner selbst.
        /// </summary>
        static IEnumerable<string> SpieleCaches()
        {
            string lokal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var kandidaten = new List<string>
            {
                Path.Combine(lokal, "Epic Games", "EpicGamesLauncher", "Saved", "webcache"),
                Path.Combine(lokal, "Battle.net", "Cache"),
                Path.Combine(lokal, "D3DSCache"),                       // DirectX-Shader
                Path.Combine(lokal, "NVIDIA", "DXCache"),
                Path.Combine(lokal, "NVIDIA", "GLCache"),
                Path.Combine(lokal, "AMD", "DxCache"),
            };

            // Steam findet sich ueber seinen Installationsort; Bibliotheken auf anderen
            // Laufwerken bleiben unberuehrt - hier zaehlt nur der Shader-Zwischenspeicher.
            foreach (string basis in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                     })
            {
                kandidaten.Add(Path.Combine(basis, "steamapps", "shadercache"));
                kandidaten.Add(Path.Combine(basis, "appcache", "httpcache"));
            }

            foreach (string p in kandidaten)
                if (Directory.Exists(p)) yield return p;
        }

        static string ErklaerungFuer(string ordner)
        {
            switch ((ordner ?? "").ToLowerInvariant())
            {
                case "downloads": return "Heruntergeladene Dateien. Hier sammelt sich am meisten an, was niemand mehr braucht.";
                case "dokumente":
                case "documents": return "Ihre Dokumente. Manche Programme legen hier auch große Sicherungen ab.";
                case "bilder":
                case "pictures": return "Ihre Fotos und Bilder.";
                case "videos": return "Ihre Videos. Meist der größte Posten.";
                case "musik":
                case "music": return "Ihre Musik.";
                case "desktop": return "Was auf Ihrem Schreibtisch liegt.";
                default: return "Ordner in Ihrem Benutzerbereich.";
            }
        }

        static object Ergebnis(List<Posten> aufraeumbar, List<Posten> brocken, bool abgebrochen)
        {
            long frei = 0, gesamt = 0;
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (d.IsReady) { frei = d.TotalFreeSpace; gesamt = d.TotalSize; }
            }
            catch { }

            long summeAufraeumbar = aufraeumbar.Sum(p => p.Bytes);
            return new
            {
                type = "storageResult",
                abgebrochen = abgebrochen,
                freiBytes = frei,
                frei = Menschlich(frei),
                gesamt = Menschlich(gesamt),
                aufraeumbarBytes = summeAufraeumbar,
                aufraeumbarText = Menschlich(summeAufraeumbar),
                aufraeumbar = aufraeumbar.Where(p => p.Bytes > 0)
                                         .OrderByDescending(p => p.Bytes).Select(p => p.ToJson()).ToArray(),
                brocken = brocken.OrderByDescending(p => p.Bytes).Select(p => p.ToJson()).ToArray(),
            };
        }

        /// <summary>
        /// Groesse eines Ordners. Abzweigungen und Cloud-Platzhalter werden uebersprungen,
        /// die Tiefe ist begrenzt, und der Abbruch wird laufend geprueft.
        /// </summary>
        static long OrdnerGroesse(string pfad, Func<bool> abbruch, int maxTiefe)
        {
            long summe = 0;
            if (string.IsNullOrEmpty(pfad)) return 0;
            var offen = new Stack<KeyValuePair<string, int>>();
            offen.Push(new KeyValuePair<string, int>(pfad, 0));

            while (offen.Count > 0)
            {
                if (abbruch != null && abbruch()) break;
                var akt = offen.Pop();
                if (akt.Value > maxTiefe) continue;

                try
                {
                    var di = new DirectoryInfo(akt.Key);
                    if (!di.Exists) continue;
                    if (Ueberspringen(di.Attributes)) continue;

                    foreach (FileInfo f in di.EnumerateFiles())
                    {
                        try { if (!Ueberspringen(f.Attributes)) summe += f.Length; }
                        catch { }
                    }
                    foreach (DirectoryInfo u in di.EnumerateDirectories())
                    {
                        try { if (!Ueberspringen(u.Attributes)) offen.Push(new KeyValuePair<string, int>(u.FullName, akt.Value + 1)); }
                        catch { }
                    }
                }
                catch { }   // gesperrte oder geschuetzte Ordner still ueberspringen
            }
            return summe;
        }

        static void SammleGrosseDateien(string pfad, long abBytes, List<Posten> ziel,
                                        Func<bool> abbruch, int maxTiefe)
        {
            var offen = new Stack<KeyValuePair<string, int>>();
            offen.Push(new KeyValuePair<string, int>(pfad, 0));

            while (offen.Count > 0)
            {
                if (abbruch != null && abbruch()) return;
                if (ziel.Count > 300) return;          // Reissleine
                var akt = offen.Pop();
                if (akt.Value > maxTiefe) continue;

                try
                {
                    var di = new DirectoryInfo(akt.Key);
                    if (!di.Exists || Ueberspringen(di.Attributes)) continue;

                    foreach (FileInfo f in di.EnumerateFiles())
                    {
                        try
                        {
                            if (Ueberspringen(f.Attributes)) continue;
                            if (f.Length < abBytes) continue;
                            ziel.Add(new Posten
                            {
                                Titel = f.Name,
                                Erklaerung = "Einzelne Datei in " + (f.DirectoryName ?? ""),
                                Bytes = f.Length,
                                Pfad = f.FullName,
                                Aufraeumbar = false,
                            });
                        }
                        catch { }
                    }
                    foreach (DirectoryInfo u in di.EnumerateDirectories())
                    {
                        try { if (!Ueberspringen(u.Attributes)) offen.Push(new KeyValuePair<string, int>(u.FullName, akt.Value + 1)); }
                        catch { }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Abzweigung oder Cloud-Platzhalter? Beides anfassen ist teuer bis schaedlich:
        /// Abzweigungen fuehren im Kreis, Platzhalter werden beim Zugriff heruntergeladen.
        /// </summary>
        static bool Ueberspringen(FileAttributes a)
        {
            if ((a & FileAttributes.ReparsePoint) != 0) return true;
            if ((a & FileAttributes.Offline) != 0) return true;
            return false;
        }
    }
}
