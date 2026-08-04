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
            public string Schluessel;  // stabile Kennung; die Oberflaeche waehlt NUR darueber
            public string Titel;
            public string Erklaerung;
            public long Bytes;
            public string Pfad;
            public bool Aufraeumbar;   // kann die App das selbst gefahrlos entfernen?
            public bool Unwiederbringlich;  // danach ist es wirklich weg (Papierkorb)

            public object ToJson()
            {
                return new { schluessel = Schluessel, titel = Titel, erklaerung = Erklaerung,
                             bytes = Bytes, groesse = Menschlich(Bytes), pfad = Pfad,
                             aufraeumbar = Aufraeumbar, unwiederbringlich = Unwiederbringlich };
            }
        }

        /// <summary>
        /// Das Ergebnis eines Laufs. Bewusst ein eigener Typ statt eines anonymen Objekts:
        /// Der Host muss die Liste behalten, um spaeter einen der grossen Brocken im
        /// Explorer zeigen zu koennen - und die Reihenfolge in dieser Liste muss dabei
        /// genau die sein, die auch in der Oberflaeche steht.
        /// </summary>
        public class Befund
        {
            public List<Posten> Aufraeumbar = new List<Posten>();
            public List<Posten> Brocken = new List<Posten>();
            public bool Abgebrochen;
            public long FreiBytes;
            public long GesamtBytes;

            public object ToJson()
            {
                long summe = Aufraeumbar.Sum(p => p.Bytes);
                return new
                {
                    type = "storageResult",
                    abgebrochen = Abgebrochen,
                    freiBytes = FreiBytes,
                    frei = Menschlich(FreiBytes),
                    gesamt = Menschlich(GesamtBytes),
                    aufraeumbarBytes = summe,
                    aufraeumbarText = Menschlich(summe),
                    aufraeumbar = Aufraeumbar.Select(p => p.ToJson()).ToArray(),
                    brocken = Brocken.Select(p => p.ToJson()).ToArray(),
                };
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
        public static Befund Run(Action<string> fortschritt, Func<bool> abbruch)
        {
            var aufraeumbar = new List<Posten>();
            var brocken = new List<Posten>();

            void Melde(string text) { if (fortschritt != null) fortschritt(text); }
            bool Abbruch() { return abbruch != null && abbruch(); }

            // ---------- Teil 1: was die App selbst wegraeumen kann ----------

            Melde("Papierkorb");
            long korb = 0;
            foreach (string lw in PapierkorbLaufwerke())
                korb += OrdnerGroesse(Path.Combine(lw, "$Recycle.Bin"), abbruch, 2);
            aufraeumbar.Add(new Posten
            {
                Schluessel = "papierkorb",
                Titel = "Papierkorb",
                Erklaerung = "Gelöschte Dateien von allen fest eingebauten Laufwerken, die sich noch "
                           + "zurückholen ließen.",
                Bytes = korb,
                Aufraeumbar = true,
                Unwiederbringlich = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            Melde("temporäre Dateien");
            long temp = OrdnerGroesse(Path.GetTempPath(), abbruch, 4)
                      + OrdnerGroesse(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), abbruch, 4);
            aufraeumbar.Add(new Posten
            {
                Schluessel = "temp",
                Titel = "Temporäre Dateien",
                Erklaerung = "Zwischendateien, die Programme liegen gelassen haben.",
                Bytes = temp,
                Aufraeumbar = true,
            });

            if (Abbruch()) return Ergebnis(aufraeumbar, brocken, true);

            Melde("heruntergeladene Update-Dateien");
            aufraeumbar.Add(new Posten
            {
                Schluessel = "updates",
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
                Schluessel = "vorschau",
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
                Schluessel = "browser",
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
                    Schluessel = "spiele",
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

            // Firefox: unter LocalAppData liegt der Zwischenspeicher-Teil des Profils.
            // Auch hier NUR die Zwischenspeicher-Ordner, nicht das Profil als Ganzes -
            // sonst faellt alles mit, was Firefox oder eine Erweiterung dort sonst noch
            // ablegt. (Lesezeichen und Anmeldungen liegen ohnehin unter Roaming.)
            string ff = Path.Combine(lokal, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(ff))
            {
                IEnumerable<string> profile;
                try { profile = Directory.EnumerateDirectories(ff); }
                catch { profile = new string[0]; }
                foreach (string prof in profile)
                {
                    foreach (string name in new[] { "cache2", "startupCache", "thumbnails",
                                                    "shader-cache", "jumpListCache", "OfflineCache" })
                    {
                        string p = Path.Combine(prof, name);
                        if (Directory.Exists(p)) yield return p;
                    }
                }
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

        /// <summary>
        /// Die Laufwerke, deren Papierkorb gemessen UND geleert wird: nur fest eingebaute.
        /// Ein USB-Stick oder eine Speicherkarte gehoert nicht dazu - dort liegt oft der
        /// einzige Fotobestand, und der Nutzer denkt beim Wort "Papierkorb" nicht daran.
        /// </summary>
        static IEnumerable<string> PapierkorbLaufwerke()
        {
            DriveInfo[] alle;
            try { alle = DriveInfo.GetDrives(); }
            catch { yield break; }

            foreach (DriveInfo d in alle)
            {
                bool ok = false;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; }
                catch { }
                if (ok) yield return d.Name;
            }
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

        static Befund Ergebnis(List<Posten> aufraeumbar, List<Posten> brocken, bool abgebrochen)
        {
            var b = new Befund { Abgebrochen = abgebrochen };
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (d.IsReady) { b.FreiBytes = d.TotalFreeSpace; b.GesamtBytes = d.TotalSize; }
            }
            catch { }

            // Die Reihenfolge wird HIER festgelegt und nicht erst beim Verwandeln in JSON:
            // die Oberflaeche spricht einen grossen Brocken ueber seine Position an, und die
            // muss auf beiden Seiten dieselbe sein.
            b.Aufraeumbar.AddRange(aufraeumbar.Where(p => p.Bytes > 0).OrderByDescending(p => p.Bytes));
            b.Brocken.AddRange(brocken.OrderByDescending(p => p.Bytes));
            return b;
        }

        // ---------------------------------------------------------------- Aufraeumen

        /// <summary>
        /// Raeumt die gewaehlten Kategorien weg und meldet je Kategorie, wie viel dabei
        /// wirklich frei wurde.
        ///
        /// Zwei Festlegungen, die nicht verhandelbar sind:
        ///
        ///   * Die Oberflaeche schickt ausschliesslich KENNUNGEN, niemals Pfade. Welche
        ///     Ordner dahinterstecken, entscheidet allein dieser Quelltext. Damit gibt es
        ///     keinen Weg, ueber die Oberflaeche einen beliebigen Ordner loeschen zu lassen.
        ///   * Der Downloads-Ordner ist KEINE Kategorie und wird nie geleert. Genau daran
        ///     ist Razer Cortex gescheitert: das Programm hat Fotos und Videos geloescht,
        ///     weil sie im Downloads-Ordner lagen. Was der Nutzer selbst dort abgelegt hat,
        ///     kann kein Programm von Datenmuell unterscheiden.
        ///
        /// Gemeldet wird der tatsaechlich gewonnene Platz (Groesse vorher minus nachher),
        /// nicht der erhoffte. Dateien, die gerade in Benutzung sind, bleiben liegen -
        /// das ist normal und wird ehrlich als Differenz sichtbar.
        /// </summary>
        public static object Aufraeumen(IEnumerable<string> schluessel, Action<string> fortschritt,
                                        Func<bool> abbruch)
        {
            var gewaehlt = new HashSet<string>(schluessel ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var berichte = new List<object>();
            long gesamt = 0;

            void Melde(string t) { if (fortschritt != null) fortschritt(t); }
            bool Stop() { return abbruch != null && abbruch(); }

            // Die Kennung geht mit an die Oberflaeche. Sie muss dort erkennen koennen, ob
            // der Papierkorb dabei war - und zwar an der Kennung, nicht am deutschen Titel:
            // ein Texteingriff darf keine Sicherheitszusage verschieben.
            void Erledigt(string schluessel, string titel, long bytes)
            {
                gesamt += bytes;
                berichte.Add(new { schluessel = schluessel, titel = titel, bytes = bytes,
                                   groesse = Menschlich(bytes) });
                AppLog.Info("Aufgeräumt: " + titel + " -> " + Menschlich(bytes));
            }

            if (gewaehlt.Contains("papierkorb") && !Stop())
            {
                Melde("Papierkorb");
                // Laufwerksweise leeren, und zwar GENAU die Laufwerke, die vorher auch
                // gemessen und angezeigt wurden.
                //
                // "Clear-RecycleBin -Force" ohne Laufwerksangabe leert die Papierkoerbe
                // ALLER Laufwerke, auch angesteckter USB-Datentraeger. Gemessen und gezeigt
                // wurde bisher nur das Systemlaufwerk: Wer 2 GB bestaetigt, haette 40 GB
                // auf einer anderen Platte mitverloren, ohne sie je gesehen zu haben.
                long vorher = 0, nachher = 0;
                foreach (string lw in PapierkorbLaufwerke())
                {
                    if (Stop()) break;
                    string bin = Path.Combine(lw, "$Recycle.Bin");
                    vorher += OrdnerGroesse(bin, abbruch, 2);
                    Shell.Ps("try { Clear-RecycleBin -DriveLetter '" + lw.Substring(0, 1) +
                             "' -Force -EA Stop } catch { }", 120000);
                    nachher += OrdnerGroesse(bin, abbruch, 2);
                }
                Erledigt("papierkorb", "Papierkorb", vorher > nachher ? vorher - nachher : 0);
            }

            if (gewaehlt.Contains("temp") && !Stop())
            {
                Melde("temporäre Dateien");
                long frei = LeereOrdner(Path.GetTempPath(), abbruch)
                          + LeereOrdner(Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), abbruch);
                Erledigt("temp", "Temporäre Dateien", frei);
            }

            if (gewaehlt.Contains("updates") && !Stop())
            {
                Melde("heruntergeladene Update-Dateien");
                long frei = LeereOrdner(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "SoftwareDistribution", "Download"), abbruch);
                Erledigt("updates", "Heruntergeladene Update-Dateien", frei);
            }

            if (gewaehlt.Contains("vorschau") && !Stop())
            {
                Melde("Vorschaubilder");
                long frei = 0;
                string cache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Explorer");
                try
                {
                    foreach (string f in Directory.GetFiles(cache, "thumbcache_*"))
                    {
                        try
                        {
                            long g = new FileInfo(f).Length;
                            File.Delete(f);
                            frei += g;
                        }
                        catch { }   // Explorer haelt einen Teil davon offen: dann bleibt er liegen
                    }
                }
                catch { }
                Erledigt("vorschau", "Vorschaubilder", frei);
            }

            if (gewaehlt.Contains("browser") && !Stop())
            {
                Melde("Zwischenspeicher der Browser");
                long frei = 0;
                foreach (string p in BrowserCaches())
                {
                    if (Stop()) break;
                    frei += LeereOrdner(p, abbruch);
                }
                Erledigt("browser", "Zwischenspeicher der Browser", frei);
            }

            if (gewaehlt.Contains("spiele") && !Stop())
            {
                Melde("Zwischenspeicher der Spiele-Plattformen");
                long frei = 0;
                foreach (string p in SpieleCaches())
                {
                    if (Stop()) break;
                    frei += LeereOrdner(p, abbruch);
                }
                Erledigt("spiele", "Zwischenspeicher der Spiele-Plattformen", frei);
            }

            return new
            {
                type = "storageCleaned",
                abgebrochen = Stop(),
                gesamtBytes = gesamt,
                gesamt = Menschlich(gesamt),
                posten = berichte.ToArray(),
            };
        }

        /// <summary>
        /// Leert einen Ordner, ohne ihn selbst zu entfernen. Gibt zurueck, wie viel Platz
        /// dadurch tatsaechlich frei wurde.
        ///
        /// Abzweigungen (Junctions) werden nur als Verweis entfernt, niemals ihr Ziel -
        /// sonst wuerde ein Verweis im Zwischenspeicher fremde Ordner mitreissen.
        /// Alles, was sich nicht loeschen laesst (Datei in Benutzung), bleibt still liegen.
        /// </summary>
        internal static long LeereOrdner(string wurzel, Func<bool> abbruch)
        {
            if (string.IsNullOrEmpty(wurzel) || !Directory.Exists(wurzel)) return 0;

            long vorher = OrdnerGroesse(wurzel, abbruch, 12);
            try
            {
                var di = new DirectoryInfo(wurzel);
                if (Ueberspringen(di.Attributes)) return 0;
                LeereInhalt(di, abbruch, 0);
            }
            catch { }

            long nachher = OrdnerGroesse(wurzel, abbruch, 12);
            return vorher > nachher ? vorher - nachher : 0;
        }

        /// <summary>
        /// Leert einen Ordner rekursiv und haelt sich dabei auf JEDER Ebene an
        /// Ueberspringen(). Der Ordner selbst bleibt stehen; alles darunter faellt.
        ///
        /// Warum nicht einfach Directory.Delete(pfad, true): Das raeumt den ganzen Baum
        /// ohne Ruecksicht auf die Attribute. Ein Cloud-Platzhalter tief im Baum waere
        /// damit geloescht - und weil ein Platzhalter die Datei in der Wolke vertritt,
        /// waere sie auf allen Geraeten des Nutzers weg. Die Zusage, Platzhalter nicht
        /// anzufassen, galt vorher nur fuer die oberste Ebene.
        /// </summary>
        static void LeereInhalt(DirectoryInfo ordner, Func<bool> abbruch, int tiefe)
        {
            if (tiefe > 24) return;   // Reissleine gegen ungewoehnlich tiefe Baeume

            foreach (FileInfo f in ordner.EnumerateFiles())
            {
                if (abbruch != null && abbruch()) return;
                try
                {
                    if (Ueberspringen(f.Attributes)) continue;
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0)
                        f.Attributes &= ~FileAttributes.ReadOnly;
                    f.Delete();
                }
                catch { }   // in Benutzung oder geschuetzt: liegen lassen
            }

            foreach (DirectoryInfo u in ordner.EnumerateDirectories())
            {
                if (abbruch != null && abbruch()) return;
                try
                {
                    // Abzweigung: nur den Verweis entfernen, nie hineingehen.
                    if ((u.Attributes & FileAttributes.ReparsePoint) != 0) { u.Delete(false); continue; }
                    if ((u.Attributes & FileAttributes.Offline) != 0) continue;

                    LeereInhalt(u, abbruch, tiefe + 1);
                    // Erst wenn wirklich nichts mehr drin ist, faellt auch der Ordner.
                    // Steht noch etwas darin, war es geschuetzt und soll bleiben.
                    try { u.Delete(false); } catch { }
                }
                catch { }
            }
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
