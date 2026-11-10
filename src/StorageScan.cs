using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

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
        /// <summary>
        /// Alle Schluessel, die Run() vergibt und Aufraeumen() kennt: genau diese sechs, keine
        /// laufwerksabhaengigen Muster (der Papierkorb wird ueber alle festen Laufwerke unter
        /// EINEM Schluessel gefuehrt). Der Helfer prueft jeden Schluessel eines Plans dagegen
        /// (docs/M2-ENTWURF.md, Abschnitt 3, speicher.aufraeumen). Wer hier einen Posten
        /// ergaenzt, traegt den Schluessel auch in diese Liste ein.
        /// </summary>
        public static readonly string[] BekannteSchluessel =
            { "papierkorb", "temp", "updates", "vorschau", "browser", "spiele" };

        /// <summary>true, wenn der Schluessel in BekannteSchluessel steht (Vergleich ohne Gross/Klein, wie Aufraeumen).</summary>
        public static bool IstBekannterSchluessel(string schluessel)
        {
            if (string.IsNullOrEmpty(schluessel)) return false;
            foreach (string s in BekannteSchluessel)
                if (string.Equals(s, schluessel, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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

            // Gezaehlt wird nur, was das Aufraeumen auch wirklich nimmt: Dateien, die seit mehr als
            // einem Tag weder angelegt noch geaendert wurden (siehe TempMindestalter).
            Melde("temporäre Dateien");
            DateTime grenze = DateTime.UtcNow - TempMindestalter;
            long temp = 0;
            foreach (string w in TempWurzeln()) temp += AlteDateien(w, grenze, abbruch, null, false);
            aufraeumbar.Add(new Posten
            {
                Schluessel = "temp",
                Titel = "Temporäre Dateien",
                Erklaerung = "Zwischendateien, die Programme liegen gelassen haben, älter als einen Tag. Jüngere "
                           + "bleiben liegen, laufende Installationen brauchen sie noch.",
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
                                        Func<bool> abbruch, Action<string, Liste> geloeschtMelden = null)
        {
            var gewaehlt = new HashSet<string>(schluessel ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var berichte = new List<object>();
            long gesamt = 0;

            void Melde(string t) { if (fortschritt != null) fortschritt(t); }
            bool Stop() { return abbruch != null && abbruch(); }

            // Die Kennung geht mit an die Oberflaeche. Sie muss dort erkennen koennen, ob
            // der Papierkorb dabei war - und zwar an der Kennung, nicht am deutschen Titel:
            // ein Texteingriff darf keine Sicherheitszusage verschieben.
            void Erledigt(string schluessel, string titel, long bytes, Liste liste)
            {
                gesamt += bytes;
                berichte.Add(new { schluessel = schluessel, titel = titel, bytes = bytes,
                                   groesse = Menschlich(bytes), uebersprungen = (string)null });
                AppLog.Info("Aufgeräumt: " + titel + " -> " + Menschlich(bytes)
                            + (liste != null ? " (" + liste.Anzahl + " Dateien)" : ""));
                // Was weg ist, steht im Laufprotokoll des Helfers (seit 8.3.1); bis dahin nannte
                // das Protokoll nur Groessen, und nach einem Schaden liess sich nicht sagen, was fehlte.
                if (liste != null && geloeschtMelden != null)
                {
                    try { geloeschtMelden(titel, liste); } catch (Exception ex) { AppLog.Warn("Liste der gelöschten Dateien nicht geschrieben: " + ex.Message); }
                }
            }

            // Uebersprungen mit Grund: der Posten erscheint im Bericht mit 0 Byte und dem Satz,
            // warum nichts geloescht wurde, statt still zu fehlen.
            void Uebersprungen(string schluessel, string titel, string grund)
            {
                berichte.Add(new { schluessel = schluessel, titel = titel, bytes = 0L,
                                   groesse = Menschlich(0), uebersprungen = grund });
                Melde(titel + ": nicht aufgeräumt, " + grund + ".");
                AppLog.Warn("Nicht aufgeräumt: " + titel + ", " + grund + ".");
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
                Erledigt("papierkorb", "Papierkorb", vorher > nachher ? vorher - nachher : 0, null);
            }

            // Temporaere Dateien (seit 8.3.1): nur Dateien, die seit mehr als einem Tag weder
            // angelegt noch geaendert wurden; Ordner bleiben stehen; und gar nichts, solange ein
            // Neustart aussteht, der Dateien aus diesen Ordnern braucht. Bis 8.3.0 leerte diese
            // Ansicht beide Ordner vollstaendig und erhoeht, auch die entpackten Dateien einer
            // laufenden Treiber- oder Programminstallation und die Quellen eines Dateitauschs, den
            // Windows erst beim Neustart ausfuehrt (gemeldeter Bluescreen nach dem Aufraeumen,
            // Audit 10.11.2026; die Ursache dort ist nicht belegt).
            if (gewaehlt.Contains("temp") && !Stop())
            {
                const string titel = "Temporäre Dateien";
                Melde("temporäre Dateien");
                string[] wurzeln = TempWurzeln();
                string grund = NeustartGrund(wurzeln);
                if (grund != null) Uebersprungen("temp", titel, grund);
                else
                {
                    DateTime grenze = DateTime.UtcNow - TempMindestalter;
                    var liste = new Liste();
                    long frei = 0;
                    foreach (string w in wurzeln)
                    {
                        if (Stop()) break;
                        frei += AlteDateien(w, grenze, abbruch, liste, true);
                    }
                    Erledigt("temp", titel, frei, liste);
                }
            }

            // Heruntergeladene Update-Dateien (seit 8.3.1): nicht, solange Windows Update oder die
            // Komponenteninstallation arbeitet oder ein Neustart aussteht; sonst erst den
            // Update-Dienst und BITS anhalten, dann loeschen, dann die beiden wieder starten. Bis
            // 8.3.0 loeschte diese Ansicht den Ordner bei laufendem Dienst, mitten in einen Download
            // oder eine Installation hinein (der Werkzeugkasten hielt den Dienst immer schon an).
            if (gewaehlt.Contains("updates") && !Stop())
            {
                const string titel = "Heruntergeladene Update-Dateien";
                Melde("heruntergeladene Update-Dateien");
                string download = DownloadOrdner();
                string grund = NeustartGrund(new[] { download }) ?? UpdateArbeitet();
                if (grund != null) Uebersprungen("updates", titel, grund);
                else
                {
                    string fehler;
                    List<string> angehalten = DiensteAnhalten(UpdateDienste, out fehler);
                    if (fehler != null)
                    {
                        DiensteStarten(angehalten);
                        Uebersprungen("updates", titel, fehler);
                    }
                    else
                    {
                        var liste = new Liste();
                        long frei = LeereOrdner(download, abbruch, liste);
                        DiensteStarten(angehalten);
                        Erledigt("updates", titel, frei, liste);
                    }
                }
            }

            if (gewaehlt.Contains("vorschau") && !Stop())
            {
                Melde("Vorschaubilder");
                long frei = 0;
                var liste = new Liste();
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
                            liste.Dazu(f);
                        }
                        catch { }   // Explorer haelt einen Teil davon offen: dann bleibt er liegen
                    }
                }
                catch { }
                Erledigt("vorschau", "Vorschaubilder", frei, liste);
            }

            if (gewaehlt.Contains("browser") && !Stop())
            {
                Melde("Zwischenspeicher der Browser");
                long frei = 0;
                var liste = new Liste();
                foreach (string p in BrowserCaches())
                {
                    if (Stop()) break;
                    frei += LeereOrdner(p, abbruch, liste);
                }
                Erledigt("browser", "Zwischenspeicher der Browser", frei, liste);
            }

            if (gewaehlt.Contains("spiele") && !Stop())
            {
                Melde("Zwischenspeicher der Spiele-Plattformen");
                long frei = 0;
                var liste = new Liste();
                foreach (string p in SpieleCaches())
                {
                    if (Stop()) break;
                    frei += LeereOrdner(p, abbruch, liste);
                }
                Erledigt("spiele", "Zwischenspeicher der Spiele-Plattformen", frei, liste);
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
        internal static long LeereOrdner(string wurzel, Func<bool> abbruch, Liste geloescht = null)
        {
            if (string.IsNullOrEmpty(wurzel) || !Directory.Exists(wurzel)) return 0;

            long vorher = OrdnerGroesse(wurzel, abbruch, 12);
            try
            {
                var di = new DirectoryInfo(wurzel);
                if (Ueberspringen(di.Attributes)) return 0;
                LeereInhalt(di, abbruch, 0, geloescht);
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
        static void LeereInhalt(DirectoryInfo ordner, Func<bool> abbruch, int tiefe, Liste geloescht)
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
                    if (geloescht != null) geloescht.Dazu(f.FullName);
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

                    LeereInhalt(u, abbruch, tiefe + 1, geloescht);
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

        // ---------------------------------------------------------------- Schutz laufender Installationen (8.3.1)

        /// <summary>
        /// Mindestalter einer temporaeren Datei, bevor sie geloescht wird (wie der Werkzeugkasten,
        /// ActionCatalog.TempAufraeumen). Es zaehlen Erstellung UND letzte Aenderung: ein Installer,
        /// der heute entpackt, uebernimmt das Aenderungsdatum oft aus dem Archiv (Monate alt), das
        /// Erstellungsdatum ist dann von heute.
        /// </summary>
        public static readonly TimeSpan TempMindestalter = TimeSpan.FromDays(1);

        /// <summary>Hoechstens so viele Pfade je Posten gehen ins Laufprotokoll; gezaehlt wird jede Datei.</summary>
        public const int ListeMax = 300;

        /// <summary>Was ein Posten geloescht hat: die ersten ListeMax Pfade und die Gesamtzahl.</summary>
        public sealed class Liste
        {
            public readonly List<string> Namen = new List<string>();
            public int Anzahl;
            public void Dazu(string pfad)
            {
                Anzahl++;
                if (Namen.Count < ListeMax) Namen.Add(pfad);
            }
        }

        static string[] TempWurzeln()
        {
            return new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") };
        }

        static string DownloadOrdner()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        }

        /// <summary>true, wenn die Datei seit grenzeUtc weder angelegt noch geaendert wurde.</summary>
        internal static bool AltGenug(FileSystemInfo f, DateTime grenzeUtc)
        {
            return f.CreationTimeUtc < grenzeUtc && f.LastWriteTimeUtc < grenzeUtc;
        }

        /// <summary>
        /// Dateien unter wurzel, die alt genug sind (AltGenug): zaehlen (loeschen = false) oder
        /// loeschen. Ordner bleiben stehen (leere Ordner sind harmlos und gehoeren oft einem
        /// laufenden Programm), Abzweigungen und Cloud-Platzhalter werden weder betreten noch
        /// geloescht, die Tiefe ist begrenzt. Rueckgabe: Bytes (gezaehlt bzw. wirklich geloescht).
        /// </summary>
        internal static long AlteDateien(string wurzel, DateTime grenzeUtc, Func<bool> abbruch, Liste geloescht, bool loeschen)
        {
            if (string.IsNullOrEmpty(wurzel)) return 0;
            long summe = 0;
            var offen = new Stack<KeyValuePair<DirectoryInfo, int>>();
            try
            {
                var di = new DirectoryInfo(wurzel);
                if (!di.Exists || Ueberspringen(di.Attributes)) return 0;
                offen.Push(new KeyValuePair<DirectoryInfo, int>(di, 0));
            }
            catch { return 0; }

            while (offen.Count > 0)
            {
                if (abbruch != null && abbruch()) break;
                var akt = offen.Pop();
                if (akt.Value > 24) continue;
                try
                {
                    foreach (FileInfo f in akt.Key.EnumerateFiles())
                    {
                        if (abbruch != null && abbruch()) break;
                        try
                        {
                            if (Ueberspringen(f.Attributes) || !AltGenug(f, grenzeUtc)) continue;
                            long g = f.Length;
                            if (!loeschen) { summe += g; continue; }
                            FileAttributes vorher = f.Attributes;
                            if ((vorher & FileAttributes.ReadOnly) != 0) f.Attributes = vorher & ~FileAttributes.ReadOnly;
                            try { f.Delete(); }
                            catch
                            {
                                try { f.Attributes = vorher; } catch { }   // in Benutzung: so lassen, wie sie war
                                continue;
                            }
                            summe += g;
                            if (geloescht != null) geloescht.Dazu(f.FullName);
                        }
                        catch { }
                    }
                    foreach (DirectoryInfo u in akt.Key.EnumerateDirectories())
                    {
                        try { if (!Ueberspringen(u.Attributes)) offen.Push(new KeyValuePair<DirectoryInfo, int>(u, akt.Value + 1)); }
                        catch { }
                    }
                }
                catch { }   // gesperrte oder geschuetzte Ordner still ueberspringen
            }
            return summe;
        }

        const string SessionManager = @"SYSTEM\CurrentControlSet\Control\Session Manager";
        const string CbsNeustart = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
        const string WuNeustart = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

        /// <summary>
        /// Grund, warum in den Ordnern wurzeln jetzt nichts geloescht werden darf, oder null:
        /// Windows wartet auf einen Neustart fuer Updates (CBS RebootPending, Windows Update
        /// RebootRequired), oder ein Dateitausch beim naechsten Start (PendingFileRenameOperations)
        /// nennt eine Datei in diesen Ordnern. Der Tausch nimmt die Quelle beim Start von dort; ist
        /// sie vorher weg, bleibt ein Treiber oder Programm halb ersetzt. Nicht lesbar zaehlt als
        /// Grund (fail closed).
        /// </summary>
        static string NeustartGrund(IEnumerable<string> wurzeln)
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    bool cbs, wu;
                    using (RegistryKey k = hklm.OpenSubKey(CbsNeustart)) cbs = k != null;
                    using (RegistryKey k = hklm.OpenSubKey(WuNeustart)) wu = k != null;
                    // TEMP steht manchmal in Kurzform (C:\Users\LANGER~1\...), die Eintraege des
                    // Dateitauschs in Langform: beide Formen vergleichen.
                    var formen = new List<string>();
                    foreach (string w in wurzeln ?? new string[0]) { formen.Add(w); formen.Add(Langform(w)); }
                    string grund = NeustartGrund(cbs, wu, new string[0], formen);
                    if (grund != null) return grund;
                    // Jeder der beiden Werte fuer sich: die Paare (Quelle, Ziel) gelten je Wert.
                    using (RegistryKey sm = hklm.OpenSubKey(SessionManager))
                    {
                        if (sm == null) return null;
                        foreach (string name in new[] { "PendingFileRenameOperations", "PendingFileRenameOperations2" })
                        {
                            object v = sm.GetValue(name);
                            string[] werte = v as string[] ?? (v is string ? new[] { (string)v } : null);
                            if (werte == null) continue;
                            grund = NeustartGrund(false, false, werte, formen);
                            if (grund != null) return grund;
                        }
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                return "der Neustart-Zustand von Windows ist nicht lesbar (" + ex.Message.TrimEnd('.') + ")";
            }
        }

        /// <summary>
        /// Reine Entscheidung zu NeustartGrund (fuer die Probe). eintraege sind die Rohwerte aus
        /// PendingFileRenameOperations: Paare aus Quelle und Ziel; ein leeres Ziel heisst "beim Start
        /// loeschen". Nur eine QUELLE mit Ziel sperrt: beim Start ersetzt Windows mit ihr eine andere
        /// Datei (so tauschen Installer einen Treiber oder eine Programmdatei, die gerade in Benutzung
        /// war), und ist die Quelle vorher weg, bleibt das Ziel alt oder halb ersetzt. Ein reiner
        /// Loeschauftrag sperrt nicht: was Windows beim Start ohnehin loescht, darf auch vorher weg
        /// (gemessen 10.11.2026: auf dem Entwicklungsrechner stehen 13 Eintraege, alle Loeschauftraege
        /// von NSIS-Installern in %TEMP%; sie sperrten sonst das Aufraeumen bis zum naechsten Start).
        /// Ungerade Anzahl (Paare nicht lesbar): jeder Eintrag zaehlt als Quelle mit Ziel (fail closed).
        /// </summary>
        internal static string NeustartGrund(bool cbs, bool wu, IList<string> eintraege, IEnumerable<string> wurzeln)
        {
            if (cbs) return "Windows wartet auf einen Neustart, um Updates fertig zu installieren; erst neu starten, dann aufräumen";
            if (wu) return "Windows Update wartet auf einen Neustart; erst neu starten, dann aufräumen";
            var liste = eintraege ?? new string[0];
            bool paarweise = liste.Count % 2 == 0;
            for (int i = 0; i < liste.Count; i += paarweise ? 2 : 1)
            {
                if (paarweise && string.IsNullOrWhiteSpace(liste[i + 1])) continue;   // Loeschauftrag
                string pfad = TauschPfad(liste[i]);
                if (pfad == null) continue;
                foreach (string w in wurzeln ?? new string[0])
                    if (Unter(pfad, w))
                        return "beim nächsten Neustart ersetzt Windows eine Datei durch „" + Path.GetFileName(pfad) + "“ aus diesem Ordner; erst neu starten, dann aufräumen";
            }
            return null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern uint GetLongPathNameW(string kurz, System.Text.StringBuilder lang, uint laenge);

        /// <summary>Langform eines vorhandenen Pfads (8.3-Kurznamen aufgeloest); bei Fehler der Pfad selbst.</summary>
        static string Langform(string pfad)
        {
            if (string.IsNullOrEmpty(pfad)) return pfad;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                uint n = GetLongPathNameW(pfad, sb, (uint)sb.Capacity);
                return n > 0 && n < sb.Capacity ? sb.ToString() : pfad;
            }
            catch { return pfad; }
        }

        /// <summary>
        /// Pfad aus einem Eintrag von PendingFileRenameOperations, null bei leer oder unlesbar. Formen
        /// (gemessen 10.11.2026 auf Windows 11 26100): "\??\C:\...", "*1\??\C:\..." (Vorsatz vor dem
        /// Objektpfad), beim Ziel "!\??\C:\..." (ersetzen erlaubt). Massgeblich ist, was nach "\??\" steht.
        /// </summary>
        internal static string TauschPfad(string roh)
        {
            if (string.IsNullOrWhiteSpace(roh)) return null;
            string p = roh.Trim();
            int k = p.IndexOf(@"\??\", StringComparison.Ordinal);
            if (k >= 0) p = p.Substring(k + 4);
            else
            {
                p = p.TrimStart('!');
                if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
            }
            try { return p.Length >= 3 && p[1] == ':' && p[2] == '\\' ? Path.GetFullPath(p) : null; }
            catch { return null; }
        }

        /// <summary>true, wenn pfad der Ordner wurzel selbst ist oder darunter liegt (ohne Gross/Klein; "Temp2" liegt nicht unter "Temp").</summary>
        internal static bool Unter(string pfad, string wurzel)
        {
            if (string.IsNullOrEmpty(pfad) || string.IsNullOrEmpty(wurzel)) return false;
            string p, w;
            try { p = Path.GetFullPath(pfad).TrimEnd('\\'); w = Path.GetFullPath(wurzel).TrimEnd('\\'); }
            catch { return false; }
            return string.Equals(p, w, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(w + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Grund, warum der Update-Speicher jetzt nicht geleert werden darf, oder null: die
        /// Komponenteninstallation laeuft (Dienst TrustedInstaller) oder Windows Update installiert
        /// (IUpdateInstaller.IsBusy). Nicht pruefbar zaehlt als Grund (fail closed).
        /// </summary>
        static string UpdateArbeitet()
        {
            try
            {
                using (var ti = new ServiceController("TrustedInstaller"))
                {
                    if (ti.Status != ServiceControllerStatus.Stopped)
                        return "Windows installiert gerade Komponenten (Dienst TrustedInstaller läuft); bitte später erneut versuchen";
                }
            }
            catch (InvalidOperationException) { }   // Dienst nicht vorhanden: es installiert nichts

            object installer = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Microsoft.Update.Installer", false);
                if (t == null) return "der Zustand von Windows Update ist nicht prüfbar (Microsoft.Update.Installer fehlt)";
                installer = Activator.CreateInstance(t);
                object busy = t.InvokeMember("IsBusy", BindingFlags.GetProperty, null, installer, null);
                if (!(busy is bool)) return "der Zustand von Windows Update ist nicht prüfbar";
                return (bool)busy ? "Windows Update installiert gerade; bitte später erneut versuchen" : null;
            }
            catch (Exception ex)
            {
                return "der Zustand von Windows Update ist nicht prüfbar (" + ex.Message.TrimEnd('.') + ")";
            }
            finally
            {
                if (installer != null && Marshal.IsComObject(installer)) Marshal.ReleaseComObject(installer);
            }
        }

        // BITS laedt in denselben Ordner; beide werden angehalten und danach wieder gestartet.
        static readonly string[] UpdateDienste = { "wuauserv", "bits" };

        /// <summary>
        /// Haelt die laufenden Dienste an (je bis 60 s). Rueckgabe: die Dienste, die liefen und jetzt
        /// gestartet werden muessen (auch der, an dem es scheiterte). fehler ist null oder der Satz.
        /// Ein fehlender Dienst zaehlt nicht als Fehler.
        /// </summary>
        static List<string> DiensteAnhalten(string[] namen, out string fehler)
        {
            fehler = null;
            var liefen = new List<string>();
            foreach (string n in namen)
            {
                ServiceController sc = null;
                try
                {
                    sc = new ServiceController(n);
                    ServiceControllerStatus st;
                    try { st = sc.Status; }
                    catch (InvalidOperationException) { continue; }   // nicht vorhanden
                    if (st == ServiceControllerStatus.Stopped) continue;
                    liefen.Add(n);
                    if (st != ServiceControllerStatus.StopPending) sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                }
                catch (Exception ex)
                {
                    fehler = "der Dienst „" + n + "“ ließ sich nicht anhalten (" + ex.Message.TrimEnd('.') + "); es wurde nichts gelöscht";
                    return liefen;
                }
                finally { if (sc != null) sc.Dispose(); }
            }
            return liefen;
        }

        /// <summary>Startet die Dienste wieder, die DiensteAnhalten angehalten hat; Fehler stehen im app.log.</summary>
        static void DiensteStarten(List<string> namen)
        {
            for (int i = namen.Count - 1; i >= 0; i--)
            {
                try
                {
                    using (var sc = new ServiceController(namen[i]))
                    {
                        if (sc.Status == ServiceControllerStatus.StopPending) sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        if (sc.Status == ServiceControllerStatus.Stopped) sc.Start();
                    }
                }
                catch (Exception ex) { AppLog.Warn("Dienst " + namen[i] + " nach dem Aufräumen nicht wieder gestartet: " + ex.Message); }
            }
        }
    }
}
