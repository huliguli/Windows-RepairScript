using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace WartungsToolbox
{
    /// <summary>
    /// Sucht Eintraege in der Windows-Registrierung, die ins Leere zeigen.
    ///
    /// GRUNDREGEL: Es wird NICHT geraten. Gemeldet wird ausschliesslich, was sich
    /// nachweisen laesst - ein Eintrag nennt eine Datei oder einen Ordner, und der
    /// existiert nachweislich nicht. Kategorien wie "unbenutzte Dateiendungen" oder
    /// "veraltete Software-Schluessel" bleiben aussen vor: dort ist "verwaist" eine
    /// Vermutung, und eine falsche Vermutung macht Programme kaputt.
    ///
    /// Sicherheitsnetz vor jedem Eingriff:
    ///   1. Wiederherstellungspunkt (von ShellForm ausgeloest)
    ///   2. Vollstaendige .reg-Sicherung JEDES betroffenen Schluessels, mit Doppelklick
    ///      wieder einspielbar
    ///   3. Nichts wird ohne ausdrueckliche Auswahl entfernt
    ///
    /// Vorsichtsregeln beim Erkennen:
    ///   * Umgebungsvariablen werden aufgeloest.
    ///   * Aufrufe ueber rundll32, regsvr32, msiexec und Konsorten werden UEBERSPRUNGEN -
    ///     dort steckt der Pfad in Argumenten, und eine Fehldeutung waere teuer.
    ///   * Zeigt ein Pfad auf ein Laufwerk, das gerade nicht da ist (USB-Stick, Netz),
    ///     wird er NICHT gemeldet. Sonst raeumt man Programme weg, die nur gerade
    ///     nicht angesteckt sind.
    ///   * Systemnahe Orte (Windows-Ordner) werden nur gemeldet, wenn die Datei wirklich
    ///     fehlt, und niemals fuer Eintraege von Windows selbst.
    /// </summary>
    static class RegistryScan
    {
        public class Fund
        {
            public string Kategorie;
            public string Titel;        // was der Nutzer liest
            public string Grund;        // warum es als tot gilt
            public string Hive;         // HKLM / HKCU / HKCR
            public string Pfad;         // Schluesselpfad ohne Hive
            public string Wert;         // Wertname (null = ganzer Schluessel)
            public string Ziel;         // die fehlende Datei
            public string Id;           // stabile Kennung fuer die Auswahl

            public object ToJson()
            {
                return new { id = Id, kategorie = Kategorie, titel = Titel, grund = Grund,
                             ort = Hive + "\\" + Pfad, wert = Wert, ziel = Ziel };
            }
        }

        /// <summary>
        /// Einordnung je Kategorie, in Alltagssprache: was ein Fund bedeutet und was das
        /// Entfernen bringt. Die Texte stehen hier und nicht in der Oberflaeche, damit
        /// Fund und Erklaerung an derselben Stelle gepflegt werden.
        ///
        /// Bewusst ohne Angstmache und ohne Versprechen. Kein Eintrag hier macht den PC
        /// schneller; das zu behaupten ist das Erkennungsmerkmal unserioeser Programme.
        /// </summary>
        public static readonly Dictionary<string, string> KategorieHinweise = new Dictionary<string, string>
        {
            { "Programme, die es nicht mehr gibt",
              "Diese Programme stehen noch in der Windows-Liste „Apps und Features“, obwohl weder ihr Ordner noch ihr Deinstallations-Programm vorhanden ist. Das Entfernen räumt die Liste auf." },
            { "Startprogramme ins Leere",
              "Windows versucht bei jeder Anmeldung, diese Programme zu starten. Weil die Dateien fehlen, geht das jedes Mal schief." },
            { "Programm-Verweise ins Leere",
              "Hier merkt sich Windows, wo ein Programm liegt, damit es sich über seinen Namen starten lässt. Die Datei fehlt." },
            { "Verweise auf fehlende Programmteile",
              "Windows notiert sich, welche Dateien mehrere Programme gemeinsam benutzen. Hier stehen Notizen zu Dateien, die es nicht mehr gibt. Sie stören nichts, sie stehen nur noch da." },
            { "Öffnen-mit-Einträge ins Leere",
              "Einträge zum Öffnen von Dateien, die auf ein fehlendes Programm zeigen. Sie sorgen dafür, dass im Menü „Öffnen mit“ etwas angeboten wird, das nicht funktioniert." },
            { "Merkzettel zu alten Programmen",
              "Reine Merkzettel: Windows hat sich die Namen von Programmen gemerkt, die es nicht mehr gibt. Das bremst nichts und belegt keinen nennenswerten Platz. Aufräumen ist hier Geschmackssache." },
        };

        // Aufrufe, aus denen sich kein Dateipfad zuverlaessig ablesen laesst.
        static readonly string[] Unklar =
        {
            "rundll32", "regsvr32", "msiexec", "cmd.exe", "powershell", "pwsh",
            "explorer.exe", "control.exe", "mshta", "wscript", "cscript",
        };

        // Zusaetzliche Startpunkte, ueber die sich ein Programm deinstallieren laesst.
        // QuietUninstallString steht oft allein da, wenn die normale Fassung fehlt.
        static readonly string[] Deinstallationswege = { "QuietUninstallString", "UninstallString" };

        public static List<Fund> Run(Action<string> fortschritt, Func<bool> abbruch)
        {
            var funde = new List<Fund>();
            void Melde(string t) { if (fortschritt != null) fortschritt(t); }
            bool Stop() { return abbruch != null && abbruch(); }

            // Die Kennungen werden auf JEDEM Rueckweg vergeben, auch beim Abbruch. Vorher
            // geschah das nur ganz am Ende: ein abgebrochener Lauf lieferte Funde ohne
            // Kennung, und der naechste Zugriff darauf riss den Oberflaechen-Thread mit.
            //
            // Seit 8.1 ist die Kennung ein stabiler Kurzhash ueber Hive, Pfad und Wertname
            // statt "r"+Index: Der Host zeigt die Liste, der Helfer (erhoeht, eigener Prozess)
            // scannt SELBST neu und findet die Auswahl des Nutzers nur ueber die Kennung
            // wieder. Ein Index waere nach dem zweiten Lauf ein anderer Eintrag.
            List<Fund> Fertig()
            {
                for (int i = 0; i < funde.Count; i++) funde[i].Id = StabileId(funde[i]);
                return funde;
            }

            Melde("Deinstallations-Einträge");
            SucheDeinstallation(funde);
            if (Stop()) return Fertig();

            Melde("Startprogramme");
            SucheAutostart(funde);
            if (Stop()) return Fertig();

            Melde("Anwendungspfade");
            SucheAppPaths(funde);
            if (Stop()) return Fertig();

            Melde("gemeinsam genutzte Programmteile");
            SucheSharedDlls(funde);
            if (Stop()) return Fertig();

            Melde("Dateityp-Verknüpfungen");
            SucheDateitypen(funde);
            if (Stop()) return Fertig();

            Melde("zuletzt geöffnete Programme");
            SucheMuiCache(funde);

            AppLog.Info("Registrierung geprueft: " + funde.Count + " tote Eintraege gefunden.");
            return Fertig();
        }

        /// <summary>
        /// Stabile Kennung eines Fundes: die ersten 12 Hex-Zeichen von SHA-256 ueber
        /// Hive + "\" + Pfad + "|" + (Wertname oder leer). Gleicher Eintrag, gleiche Kennung,
        /// in jedem Prozess und bei jedem Lauf.
        /// </summary>
        public static string StabileId(Fund f)
        {
            string quelle = (f.Hive ?? "") + "\\" + (f.Pfad ?? "") + "|" + (f.Wert ?? "");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(quelle));
                var sb = new StringBuilder(12);
                for (int i = 0; i < 6; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------- Kategorien

        static void SucheDeinstallation(List<Fund> funde)
        {
            var orte = new[]
            {
                new { Hive = RegistryHive.LocalMachine, Name = "HKLM", Pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
                new { Hive = RegistryHive.LocalMachine, Name = "HKLM", Pfad = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" },
                new { Hive = RegistryHive.CurrentUser,  Name = "HKCU", Pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
            };

            foreach (var ort in orte)
            {
                using (RegistryKey basis = RegistryKey.OpenBaseKey(ort.Hive, RegistryView.Default))
                using (RegistryKey k = basis.OpenSubKey(ort.Pfad))
                {
                    if (k == null) continue;
                    foreach (string unter in Namen(k))
                    {
                        using (RegistryKey e = Oeffne(k, unter))
                        {
                            if (e == null) continue;
                            string anzeige = e.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(anzeige)) continue;    // Systemeintrag ohne Namen: Finger weg

                            // Erste und wichtigste Bedingung: der Weg zum Deinstallieren muss
                            // nachweislich tot sein. Ohne sie war jeder dritte Fund falsch.
                            if (!DeinstallationTot(e)) continue;

                            // Bevorzugt InstallLocation, sonst die Datei aus UninstallString.
                            string ort2 = e.GetValue("InstallLocation") as string;
                            string ziel;
                            if (!string.IsNullOrEmpty(ort2))
                            {
                                string p = Saeubere(ort2);
                                if (!PruefbarerPfad(p)) continue;      // nicht beurteilbar
                                if (Directory.Exists(p)) continue;     // Ordner da, Programm also auch
                                ziel = p;
                            }
                            else
                            {
                                string p = DateiAus(e.GetValue("UninstallString") as string);
                                if (p == null || File.Exists(p)) continue;
                                ziel = p;
                            }

                            funde.Add(new Fund
                            {
                                Kategorie = "Programme, die es nicht mehr gibt",
                                Titel = anzeige,
                                // Der Grund benennt genau das, was geprueft wurde. Beides
                                // muss fehlen, sonst waere der Eintrag hier gar nicht.
                                Grund = "Steht noch in der Liste der installierten Programme. "
                                      + "Weder der zugehörige Ordner noch das Programm zum "
                                      + "Deinstallieren sind vorhanden.",
                                Hive = ort.Name,
                                Pfad = ort.Pfad + "\\" + unter,
                                Ziel = ziel,
                            });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fuehrt der Weg zum Deinstallieren nachweislich ins Leere?
        ///
        /// Das ist die Bedingung, an der die Kategorie haengt. Ein fehlender
        /// Installationsordner allein sagt naemlich WENIG: viele Pakete tragen dort den
        /// Ordner ein, in den sie sich zum Installieren entpackt haben - und der wird
        /// danach planmaessig geloescht.
        ///
        /// Am 4. August 2026 auf einem echten Rechner nachgemessen: von neun Funden
        /// waren ohne diese Bedingung drei falsch. Der teuerste war das AMD-Chipsatzpaket.
        /// Sein InstallLocation zeigt auf den laengst entfernten Entpack-Ordner
        /// C:\Chipset_Software, die Deinstallation laeuft aber einwandfrei ueber
        /// MsiExec. Wer den Schluessel entfernt, nimmt dem Nutzer die einzige
        /// Moeglichkeit, das Paket je wieder sauber loszuwerden. Ebenso bei zwei
        /// Spielen, deren Deinstallation ueber den Launcher laeuft (Steam, Battle.net):
        /// deren Programm liegt woanders und ist quicklebendig.
        ///
        /// Ergebnis ist true NUR, wenn ein Deinstallations-Weg eingetragen ist UND jeder
        /// davon eine Datei nennt, die es nicht gibt. Alles Unklare gilt als lebendig.
        /// </summary>
        static bool DeinstallationTot(RegistryKey e)
        {
            bool einerGenannt = false;
            foreach (string name in Deinstallationswege)
            {
                string befehl = e.GetValue(name) as string;
                if (string.IsNullOrWhiteSpace(befehl)) continue;
                einerGenannt = true;

                // DateiAus liefert null bei msiexec, rundll32 und Konsorten, bei Pfaden
                // auf nicht beurteilbaren Laufwerken und bei allem Unlesbaren. In all
                // diesen Faellen laesst sich nichts nachweisen - also gilt der Weg als da.
                string datei = DateiAus(befehl);
                if (datei == null || File.Exists(datei)) return false;
            }
            return einerGenannt;
        }

        static void SucheAutostart(List<Fund> funde)
        {
            var orte = new[]
            {
                new { Hive = RegistryHive.LocalMachine, Name = "HKLM", Pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new { Hive = RegistryHive.LocalMachine, Name = "HKLM", Pfad = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" },
                new { Hive = RegistryHive.CurrentUser,  Name = "HKCU", Pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new { Hive = RegistryHive.CurrentUser,  Name = "HKCU", Pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" },
            };

            foreach (var ort in orte)
            {
                using (RegistryKey basis = RegistryKey.OpenBaseKey(ort.Hive, RegistryView.Default))
                using (RegistryKey k = basis.OpenSubKey(ort.Pfad))
                {
                    if (k == null) continue;
                    foreach (string name in Werte(k))
                    {
                        string p = DateiAus(k.GetValue(name) as string);
                        if (p == null || File.Exists(p)) continue;
                        funde.Add(new Fund
                        {
                            Kategorie = "Startprogramme ins Leere",
                            Titel = name,
                            Grund = "Soll beim Anmelden starten, die Datei ist aber nicht mehr da.",
                            Hive = ort.Name,
                            Pfad = ort.Pfad,
                            Wert = name,
                            Ziel = p,
                        });
                    }
                }
            }
        }

        static void SucheAppPaths(List<Fund> funde)
        {
            var orte = new[]
            {
                new { Name = "HKLM", Pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths" },
                new { Name = "HKLM", Pfad = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" },
            };
            foreach (var ort in orte)
            {
                using (RegistryKey basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                using (RegistryKey k = basis.OpenSubKey(ort.Pfad))
                {
                    if (k == null) continue;
                    foreach (string unter in Namen(k))
                    {
                        using (RegistryKey e = Oeffne(k, unter))
                        {
                            if (e == null) continue;
                            string p = DateiAus(e.GetValue("") as string);
                            if (p == null || File.Exists(p)) continue;
                            funde.Add(new Fund
                            {
                                Kategorie = "Programm-Verweise ins Leere",
                                Titel = unter,
                                Grund = "Windows merkt sich hier, wo ein Programm liegt. Die Datei fehlt.",
                                Hive = ort.Name,
                                Pfad = ort.Pfad + "\\" + unter,
                                Ziel = p,
                            });
                        }
                    }
                }
            }
        }

        static void SucheSharedDlls(List<Fund> funde)
        {
            const string pfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
            using (RegistryKey basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
            using (RegistryKey k = basis.OpenSubKey(pfad))
            {
                if (k == null) return;
                foreach (string name in Werte(k))
                {
                    string p = Saeubere(name);
                    if (!PruefbarerPfad(p) || File.Exists(p)) continue;
                    funde.Add(new Fund
                    {
                        Kategorie = "Verweise auf fehlende Programmteile",
                        Titel = Path.GetFileName(p),
                        Grund = "Windows notiert hier, wie viele Programme diese Datei gemeinsam benutzen. "
                              + "Die Datei gibt es nicht mehr, die Notiz steht noch da.",
                        Hive = "HKLM",
                        Pfad = pfad,
                        Wert = name,
                        Ziel = p,
                    });
                }
            }
        }

        // Entfernt wird ausschliesslich dieser Zweig, nie der ganze Dateityp. Siehe unten.
        const string ProgIdBefehl = @"\shell\open";

        static void SucheDateitypen(List<Fund> funde)
        {
            // Dateiendung -> ProgID -> shell\open\command. Nur melden, wenn die ProgID
            // existiert UND ihr Befehl auf eine fehlende Datei zeigt. Eine fehlende ProgID
            // allein ist KEIN Befund: Windows loest viele Endungen anders auf.
            //
            // Entfernt wird NUR der Zweig "shell\open", nicht der ganze Dateityp. Nachgewiesen
            // ist allein, dass der Befehl zum Oeffnen ins Leere zeigt. Unter der ProgID
            // haengen daneben Symbol, weitere Befehle (Drucken, Bearbeiten) und Erweiterungen
            // des Explorers, die niemand geprueft hat und die funktionieren koennen. Die
            // Loeschung darf nicht breiter sein als der Nachweis, auf dem sie beruht.
            using (RegistryKey hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default))
            {
                foreach (string progId in Namen(hkcr).Where(n => !n.StartsWith(".") && n.IndexOf('.') > 0).Take(4000))
                {
                    using (RegistryKey cmd = Oeffne(hkcr, progId + ProgIdBefehl + @"\command"))
                    {
                        if (cmd == null) continue;
                        string p = DateiAus(cmd.GetValue("") as string);
                        if (p == null || File.Exists(p)) continue;
                        funde.Add(new Fund
                        {
                            Kategorie = "Öffnen-mit-Einträge ins Leere",
                            Titel = progId,
                            Grund = "Der Eintrag zum Öffnen solcher Dateien ruft ein Programm auf, das fehlt. "
                                  + "Entfernt wird nur dieser Aufruf, der Dateityp selbst bleibt bestehen.",
                            Hive = "HKCR",
                            Pfad = progId + ProgIdBefehl,
                            Ziel = p,
                        });
                    }
                }
            }
        }

        static void SucheMuiCache(List<Fund> funde)
        {
            const string pfad = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            using (RegistryKey basis = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
            using (RegistryKey k = basis.OpenSubKey(pfad))
            {
                if (k == null) return;
                foreach (string name in Werte(k))
                {
                    string p = name;
                    int schnitt = p.IndexOf(".FriendlyAppName", StringComparison.OrdinalIgnoreCase);
                    if (schnitt < 0) schnitt = p.IndexOf(".ApplicationCompany", StringComparison.OrdinalIgnoreCase);
                    if (schnitt > 0) p = p.Substring(0, schnitt);
                    p = Saeubere(p);
                    if (!PruefbarerPfad(p) || File.Exists(p)) continue;
                    funde.Add(new Fund
                    {
                        Kategorie = "Merkzettel zu alten Programmen",
                        Titel = Path.GetFileName(p),
                        Grund = "Windows hat sich den Namen eines Programms gemerkt, das es nicht mehr gibt.",
                        Hive = "HKCU",
                        Pfad = pfad,
                        Wert = name,
                        Ziel = p,
                    });
                }
            }
        }

        // ---------------------------------------------------------------- Entfernen

        // Die Bereiche, die diese Klasse durchsucht - und damit die einzigen, in denen sie
        // etwas entfernen darf.
        static readonly string[] ErlaubteWurzeln =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs",
            @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
        };

        /// <summary>
        /// Zweites Schloss unmittelbar vor dem Eingriff: Der Eintrag muss in einem der
        /// Bereiche liegen, die diese Klasse selbst durchsucht.
        ///
        /// Die Auswahl kommt zwar aus dem gespeicherten Ergebnis des eigenen Laufs und nie
        /// aus der Oberflaeche. Aber ein Loeschbefehl auf die Registrierung ist der Ort,
        /// an dem ein spaeterer Fluechtigkeitsfehler am teuersten waere - und
        /// DeleteSubKeyTree auf einen der Wurzelpfade wuerde in einem Zug alle
        /// installierten Programme aus "Apps und Features" entfernen.
        /// Deshalb wird die Zulaessigkeit hier noch einmal aus dem Eintrag selbst
        /// hergeleitet, statt sie vorauszusetzen.
        /// </summary>
        internal static bool DarfEntferntWerden(Fund f)
        {
            if (f == null || string.IsNullOrWhiteSpace(f.Pfad)) return false;

            // Dateityp-Eintraege: entfernt wird nur der nachgewiesene Befehl, nicht die
            // ganze ProgID. Siehe SucheDateitypen.
            if (f.Hive == "HKCR")
                return f.Wert == null
                    && f.Pfad.EndsWith(ProgIdBefehl, StringComparison.OrdinalIgnoreCase)
                    && f.Pfad.Length > ProgIdBefehl.Length;

            // Den LAENGSTEN passenden Bereich suchen, nicht den ersten. Sonst schlaegt die
            // Praefix-Falle zu: "...\CurrentVersion\Run" faengt "...\CurrentVersion\RunOnce"
            // ab, das Zeichen dahinter ist ein 'O' statt eines Trenners, und RunOnce-Eintraege
            // liessen sich nie entfernen.
            string treffer = null;
            foreach (string w in ErlaubteWurzeln)
            {
                if (!f.Pfad.StartsWith(w, StringComparison.OrdinalIgnoreCase)) continue;
                if (f.Pfad.Length != w.Length && f.Pfad[w.Length] != '\\') continue;  // nur zufaellig gleicher Anfang
                if (treffer == null || w.Length > treffer.Length) treffer = w;
            }
            if (treffer == null) return false;

            // Der Bereich selbst darf nur einen WERT verlieren, niemals sich selbst.
            if (f.Pfad.Length == treffer.Length) return f.Wert != null;
            return true;
        }

        /// <summary>
        /// Sichert die betroffenen Schluessel in eine .reg-Datei und entfernt danach die
        /// ausgewaehlten Eintraege. Gibt den Pfad der Sicherung zurueck.
        /// </summary>
        public static string Entferne(List<Fund> auswahl, out int entfernt, out int fehlgeschlagen)
        {
            entfernt = 0;
            fehlgeschlagen = 0;

            var erlaubt = new List<Fund>();
            foreach (Fund f in auswahl ?? new List<Fund>())
            {
                if (DarfEntferntWerden(f)) erlaubt.Add(f);
                else
                {
                    fehlgeschlagen++;
                    AppLog.Warn("Registrierung: '" + (f == null ? "?" : f.Pfad)
                                + "' liegt ausserhalb der durchsuchten Bereiche und wird nicht angefasst.");
                }
            }
            if (erlaubt.Count == 0) return null;

            // Keine Sicherung in eine Abzweigung: das Entfernen laeuft erhoeht, und BUILTIN\Users
            // darf im Laufzeitordner aendern. Ein anderes lokales Konto koennte sicherungen\
            // registrierung leeren und als Junction auf einen fremden Ort neu anlegen; die .reg
            // landete dann dort, und der Nutzer importierte spaeter per Doppelklick eine Datei,
            // die dieses Konto veraendern kann. Ohne Sicherung wird nichts entfernt (unten).
            string ordner = Sicherungsordner();   // null: Ablage.Sicherungen hat den Ort schon verweigert (Abzweigung)
            if (string.IsNullOrEmpty(ordner) || OrdnerIstAbzweigung(ordner))
            {
                string wo = string.IsNullOrEmpty(ordner) ? "für die Registrierung (sicherungen\\registrierung)" : ordner;
                AppLog.Error("Registrierung: der Sicherungsordner " + wo + " liegt hinter einer Abzweigung (Junction oder symbolischer Link); es wird nichts gesichert und nichts entfernt.");
                throw new InvalidOperationException(
                    "Der Sicherungsordner " + wo + " liegt hinter einer Verknüpfung auf einen anderen Ort. " +
                    "Deshalb wurde nichts gesichert und nichts entfernt: Ohne Sicherung fassen wir nichts an.");
            }
            Directory.CreateDirectory(ordner);
            string sicherung = Sicherungsdatei(ordner);

            // Erst sichern, dann anfassen. Ohne vollstaendige Sicherung wird NICHTS entfernt.
            var schluessel = erlaubt.Select(f => f.Hive + "\\" + f.Pfad)
                                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!ExportiereSchluessel(schluessel, ref sicherung))
            {
                AppLog.Error("Registrierung: Sicherung fehlgeschlagen - es wird nichts entfernt.");
                throw new InvalidOperationException(
                    "Die Sicherungsdatei ließ sich nicht vollständig schreiben. Deshalb wurde " +
                    "nichts entfernt: Ohne Sicherung fassen wir nichts an.");
            }

            foreach (Fund f in erlaubt)
            {
                try
                {
                    using (RegistryKey basis = RegistryKey.OpenBaseKey(HiveVon(f.Hive), RegistryView.Default))
                    {
                        if (f.Wert != null)
                        {
                            using (RegistryKey k = basis.OpenSubKey(f.Pfad, true))
                            {
                                if (k == null) { fehlgeschlagen++; continue; }
                                k.DeleteValue(f.Wert, false);
                            }
                        }
                        else
                        {
                            basis.DeleteSubKeyTree(f.Pfad, false);
                        }
                    }
                    entfernt++;
                }
                catch (Exception ex)
                {
                    fehlgeschlagen++;
                    AppLog.Warn("Registrierung: '" + f.Titel + "' liess sich nicht entfernen: " + ex.Message);
                }
            }

            AppLog.Info("Registrierung aufgeraeumt: " + entfernt + " entfernt, " + fehlgeschlagen
                        + " übersprungen. Sicherung: " + sicherung);
            return sicherung;
        }

        /// <summary>
        /// Ordner, in dem die .reg-Sicherungen liegen: %ProgramData%\WindowsWartung\sicherungen\registrierung
        /// (Kern.Ablage.Sicherungen, legt den Ordner an, weicht ohne Schreibrecht ins Profil aus).
        ///
        /// Maschinenweit seit 8.1: das Entfernen laeuft im erhoehten Helfer (Kennung
        /// registrierung.entfernen), der Nutzer oeffnet den Ordner aus der Oberflaeche ohne
        /// Rechte - beide muessen denselben Ort meinen. Sicherungen bis 8.0 liegen unter
        /// %LOCALAPPDATA%\WindowsWartung\registrierung-sicherung und bleiben dort: sie sind
        /// Beweisstuecke des Nutzers, kein Laufzeitzustand, und kein Lauf liest sie zurueck.
        /// Ein Umzug ist deshalb nicht noetig.
        /// </summary>
        public static string Sicherungsordner()
        {
            if (!string.IsNullOrEmpty(OrdnerFuerProbe)) return OrdnerFuerProbe;
            return WartungsToolbox.Kern.Ablage.Sicherungen("registrierung");
        }

        /// <summary>
        /// Nur fuer die Probe in tests/ (RegistryProbe): verlegt die .reg-Sicherung in einen
        /// Wegwerf-Ordner, damit der Test weder ProgramData anlegt noch eine echte Sicherung
        /// des Nutzers daneben schreibt. Geht der Ablage vor, wie History.PfadFuerProbe und
        /// Protokoll.OrdnerFuerProbe. Im laufenden Programm bleibt das Feld immer null.
        /// </summary>
        internal static string OrdnerFuerProbe;

        /// <summary>
        /// Name der Sicherungsdatei: registrierung-vorher-yyyy-MM-dd-HHmmss-xxxx.reg mit Sekunden
        /// und 4 Hex-Zeichen. Bis 8.1.0 war der Name minutengenau, und ExportiereSchluessel
        /// schrieb ueberschreibend: wer innerhalb derselben Minute zweimal entfernte (die
        /// Oberflaeche sucht nach dem Lauf sofort neu), verlor die Sicherung des ersten Laufs,
        /// das einzige Beweisstueck fuer den Rueckweg. Geschrieben wird mit FileMode.CreateNew;
        /// eine Kollision wird nicht ueberschrieben, sondern mit dem naechsten Namen versucht.
        /// </summary>
        static string Sicherungsdatei(string ordner)
        {
            string kennung = Guid.NewGuid().ToString("N").Substring(0, 4);
            return Path.Combine(ordner,
                "registrierung-vorher-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + "-" + kennung + ".reg");
        }

        /// <summary>
        /// Liegt der Sicherungsordner hinter einer Abzweigung? Im Programm entscheidet
        /// Ablage.IstAbzweigung (der Pfad und jede Komponente unterhalb des maschinenweiten
        /// Ordners); fuer den Wegwerf-Ordner der Probe (OrdnerFuerProbe, ausserhalb der Ablage)
        /// nur das Attribut des Ordners selbst. Wirft die Pruefung, gilt "ja": was sich nicht
        /// pruefen laesst, bekommt keinen erhoehten Schreibzugriff.
        /// </summary>
        static bool OrdnerIstAbzweigung(string ordner)
        {
            try
            {
                if (string.IsNullOrEmpty(ordner)) return true;
                if (!string.IsNullOrEmpty(OrdnerFuerProbe))
                    return Directory.Exists(ordner)
                           && (File.GetAttributes(ordner) & FileAttributes.ReparsePoint) != 0;
                return WartungsToolbox.Kern.Ablage.IstAbzweigung(ordner);
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Exportiert die Schluessel mit reg.exe in EINE .reg-Datei.
        ///
        /// Alles oder nichts: Schon EIN misslungener Export laesst die ganze Sicherung
        /// scheitern. Vorher genuegte ein einziger gelungener Export, damit die Sicherung
        /// als vollstaendig galt - danach wurden auch die uebrigen Eintraege entfernt,
        /// ohne dass sie irgendwo gesichert gewesen waeren. Die Zusage "ohne vollstaendige
        /// Sicherung wird nichts entfernt" war damit keine.
        ///
        /// ziel ist ref: bei einer Namenskollision (Datei gibt es schon) wird ein neuer Name
        /// gewaehlt, und der Aufrufer meldet den Namen, unter dem wirklich geschrieben wurde.
        /// </summary>
        static bool ExportiereSchluessel(List<string> schluessel, ref string ziel)
        {
            if (schluessel == null || schluessel.Count == 0) return false;

            var gesamt = new StringBuilder();
            gesamt.AppendLine("Windows Registry Editor Version 5.00");
            gesamt.AppendLine();
            gesamt.AppendLine("; Sicherung von Windows-Wartung, " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            gesamt.AppendLine("; Mit einem Doppelklick auf diese Datei laesst sich der Zustand");
            gesamt.AppendLine("; von vor dem Aufräumen wiederherstellen.");
            gesamt.AppendLine();

            foreach (string s in schluessel)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ww_reg_" + Guid.NewGuid().ToString("N") + ".reg");
                bool ok = false;
                try
                {
                    Shell.Result r = Shell.Run("reg.exe", "export \"" + s + "\" \"" + tmp + "\" /y", 30000);
                    if (r.Ok && File.Exists(tmp))
                    {
                        // Kopfzeile jeder Einzeldatei weglassen, sie steht schon oben.
                        string[] zeilen = File.ReadAllLines(tmp, Encoding.Unicode);
                        foreach (string z in zeilen)
                        {
                            if (z.StartsWith("Windows Registry Editor")) continue;
                            gesamt.AppendLine(z);
                        }
                        ok = true;
                    }
                    else
                    {
                        AppLog.Error("Sicherung von " + s + " fehlgeschlagen (ExitCode "
                                     + r.ExitCode + (r.TimedOut ? ", Zeitlimit" : "") + ").");
                    }
                }
                catch (Exception ex) { AppLog.Error("Sicherung von " + s + " fehlgeschlagen: " + ex.Message); }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }

                if (!ok) return false;
            }

            // CreateNew statt WriteAllText: eine vorhandene Datei gleichen Namens (zweiter Lauf
            // in derselben Sekunde mit gleicher Kennung, praktisch nie) wird nicht ueberschrieben,
            // sondern ein neuer Name versucht; nach 3 Kollisionen scheitert die Sicherung, und
            // damit das Entfernen.
            string text = gesamt.ToString();
            for (int versuch = 0; ; versuch++)
            {
                try
                {
                    using (var fs = new FileStream(ziel, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var w = new StreamWriter(fs, Encoding.Unicode))   // UTF-16 mit BOM, wie reg.exe und File.WriteAllText
                    {
                        w.Write(text);
                    }
                    return new FileInfo(ziel).Length > 0;
                }
                catch (IOException ex) when (File.Exists(ziel) && versuch < 3)
                {
                    string neu = Sicherungsdatei(Path.GetDirectoryName(ziel));
                    AppLog.Warn("Sicherung " + Path.GetFileName(ziel) + " gibt es schon (" + ex.Message.Trim() + "); neuer Name " + Path.GetFileName(neu));
                    ziel = neu;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Sicherung konnte nicht geschrieben werden", ex);
                    return false;
                }
            }
        }

        static RegistryHive HiveVon(string name)
        {
            switch (name)
            {
                case "HKLM": return RegistryHive.LocalMachine;
                case "HKCU": return RegistryHive.CurrentUser;
                case "HKCR": return RegistryHive.ClassesRoot;
                default: throw new ArgumentException("Unbekannter Bereich: " + name);
            }
        }

        // ---------------------------------------------------------------- Hilfen

        static IEnumerable<string> Namen(RegistryKey k)
        {
            string[] n;
            try { n = k.GetSubKeyNames(); } catch { yield break; }
            foreach (string s in n) yield return s;
        }

        static IEnumerable<string> Werte(RegistryKey k)
        {
            string[] n;
            try { n = k.GetValueNames(); } catch { yield break; }
            foreach (string s in n) if (!string.IsNullOrEmpty(s)) yield return s;
        }

        static RegistryKey Oeffne(RegistryKey basis, string unter)
        {
            try { return basis.OpenSubKey(unter); } catch { return null; }
        }

        static string Saeubere(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().Trim('"').Trim();
            // Manche Installationsprogramme schreiben den Pfad mit Schraegstrichen
            // (gemessen an einem echten Eintrag: "E:/Valo/Riot Games/VALORANT/live").
            // Ohne diese Zeile faellt so ein Eintrag nur zufaellig durch die Pruefung -
            // und auf Zufall darf keine Loeschentscheidung stehen.
            s = s.Replace('/', '\\');
            try { s = Environment.ExpandEnvironmentVariables(s); } catch { }
            return s.TrimEnd('\\', ' ');
        }

        /// <summary>
        /// Liest den Dateipfad aus einer Befehlszeile. Liefert null, wenn sich das nicht
        /// zuverlaessig sagen laesst - lieber kein Befund als ein falscher.
        /// </summary>
        static string DateiAus(string befehl)
        {
            if (string.IsNullOrWhiteSpace(befehl)) return null;
            string b = befehl.Trim();

            foreach (string u in Unklar)
                if (b.IndexOf(u, StringComparison.OrdinalIgnoreCase) >= 0) return null;

            string pfad;
            if (b.StartsWith("\""))
            {
                int ende = b.IndexOf('"', 1);
                if (ende <= 1) return null;
                pfad = b.Substring(1, ende - 1);
            }
            else
            {
                // Ohne Anfuehrungszeichen: bis zum ersten Leerzeichen, das auf .exe folgt.
                int exe = b.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe < 0) return null;
                pfad = b.Substring(0, exe + 4);
            }

            pfad = Saeubere(pfad);
            return PruefbarerPfad(pfad) ? pfad : null;
        }

        /// <summary>
        /// Darf dieser Pfad ueberhaupt beurteilt werden? Nur absolute Pfade auf einem
        /// fest eingebauten Laufwerk, das gerade vorhanden ist.
        ///
        /// Warum ausdruecklich NUR fest eingebaut: Bei USB-Stick, Speicherkarte und
        /// Netzlaufwerk sagt ein vorhandener Buchstabe gar nichts. Windows vergibt
        /// Buchstaben in der Reihenfolge des Ansteckens - hinter E: kann heute ein
        /// anderer Datentraeger liegen als an dem Tag, an dem der Eintrag geschrieben
        /// wurde. Ein dort "fehlender" Ordner ist dann keine Aussage, sondern ein
        /// Missverstaendnis, und der Nutzer verliert Eintraege zu Programmen, die nur
        /// gerade nicht angesteckt sind.
        /// </summary>
        static bool PruefbarerPfad(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return false;
            if (p.Length < 4 || p[1] != ':' || p[2] != '\\') return false;   // kein C:\...
            if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            try
            {
                DriveInfo d = new DriveInfo(p.Substring(0, 1));
                if (!d.IsReady || d.DriveType != DriveType.Fixed) return false;
            }
            catch { return false; }
            return true;
        }
    }
}
