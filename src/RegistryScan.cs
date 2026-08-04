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

        // Aufrufe, aus denen sich kein Dateipfad zuverlaessig ablesen laesst.
        static readonly string[] Unklar =
        {
            "rundll32", "regsvr32", "msiexec", "cmd.exe", "powershell", "pwsh",
            "explorer.exe", "control.exe", "mshta", "wscript", "cscript",
        };

        public static List<Fund> Run(Action<string> fortschritt, Func<bool> abbruch)
        {
            var funde = new List<Fund>();
            void Melde(string t) { if (fortschritt != null) fortschritt(t); }
            bool Stop() { return abbruch != null && abbruch(); }

            Melde("Deinstallations-Einträge");
            SucheDeinstallation(funde);
            if (Stop()) return funde;

            Melde("Startprogramme");
            SucheAutostart(funde);
            if (Stop()) return funde;

            Melde("Anwendungspfade");
            SucheAppPaths(funde);
            if (Stop()) return funde;

            Melde("gemeinsam genutzte Programmteile");
            SucheSharedDlls(funde);
            if (Stop()) return funde;

            Melde("Dateityp-Verknüpfungen");
            SucheDateitypen(funde);
            if (Stop()) return funde;

            Melde("zuletzt geöffnete Programme");
            SucheMuiCache(funde);

            for (int i = 0; i < funde.Count; i++) funde[i].Id = "r" + i;
            AppLog.Info("Registrierung geprueft: " + funde.Count + " tote Eintraege gefunden.");
            return funde;
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

                            // Bevorzugt InstallLocation, sonst die Datei aus UninstallString.
                            string ort2 = e.GetValue("InstallLocation") as string;
                            string ziel = null;
                            if (!string.IsNullOrEmpty(ort2))
                            {
                                string p = Saeubere(ort2);
                                if (PruefbarerPfad(p) && !Directory.Exists(p)) ziel = p;
                            }
                            else
                            {
                                string p = DateiAus(e.GetValue("UninstallString") as string);
                                if (p != null && !File.Exists(p)) ziel = p;
                            }
                            if (ziel == null) continue;

                            funde.Add(new Fund
                            {
                                Kategorie = "Programme, die es nicht mehr gibt",
                                Titel = anzeige,
                                Grund = "Steht in der Liste der installierten Programme, der zugehörige Ordner fehlt aber.",
                                Hive = ort.Name,
                                Pfad = ort.Pfad + "\\" + unter,
                                Ziel = ziel,
                            });
                        }
                    }
                }
            }
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
                        Grund = "Ein Zähler für eine gemeinsam genutzte Datei, die es nicht mehr gibt.",
                        Hive = "HKLM",
                        Pfad = pfad,
                        Wert = name,
                        Ziel = p,
                    });
                }
            }
        }

        static void SucheDateitypen(List<Fund> funde)
        {
            // Dateiendung -> ProgID -> shell\open\command. Nur melden, wenn die ProgID
            // existiert UND ihr Befehl auf eine fehlende Datei zeigt. Eine fehlende ProgID
            // allein ist KEIN Befund: Windows loest viele Endungen anders auf.
            using (RegistryKey hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default))
            {
                foreach (string progId in Namen(hkcr).Where(n => !n.StartsWith(".") && n.IndexOf('.') > 0).Take(4000))
                {
                    using (RegistryKey cmd = Oeffne(hkcr, progId + @"\shell\open\command"))
                    {
                        if (cmd == null) continue;
                        string p = DateiAus(cmd.GetValue("") as string);
                        if (p == null || File.Exists(p)) continue;
                        funde.Add(new Fund
                        {
                            Kategorie = "Öffnen-mit-Einträge ins Leere",
                            Titel = progId,
                            Grund = "Ein Eintrag zum Öffnen von Dateien verweist auf ein Programm, das fehlt.",
                            Hive = "HKCR",
                            Pfad = progId,
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

        /// <summary>
        /// Sichert die betroffenen Schluessel in eine .reg-Datei und entfernt danach die
        /// ausgewaehlten Eintraege. Gibt den Pfad der Sicherung zurueck.
        /// </summary>
        public static string Entferne(List<Fund> auswahl, out int entfernt, out int fehlgeschlagen)
        {
            entfernt = 0;
            fehlgeschlagen = 0;

            string ordner = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsWartung", "registrierung-sicherung");
            Directory.CreateDirectory(ordner);
            string sicherung = Path.Combine(ordner,
                "registrierung-vorher-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".reg");

            // Erst sichern, dann anfassen. Ohne vollstaendige Sicherung wird NICHTS entfernt.
            var schluessel = auswahl.Select(f => f.Hive + "\\" + f.Pfad)
                                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!ExportiereSchluessel(schluessel, sicherung))
            {
                AppLog.Error("Registrierung: Sicherung fehlgeschlagen - es wird nichts entfernt.");
                throw new InvalidOperationException(
                    "Die Sicherung der Registrierung ist fehlgeschlagen. Es wurde nichts verändert.");
            }

            foreach (Fund f in auswahl)
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

        /// <summary>Exportiert die Schluessel mit reg.exe in EINE .reg-Datei.</summary>
        static bool ExportiereSchluessel(List<string> schluessel, string ziel)
        {
            var gesamt = new StringBuilder();
            gesamt.AppendLine("Windows Registry Editor Version 5.00");
            gesamt.AppendLine();
            gesamt.AppendLine("; Sicherung von Windows-Wartung, " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            gesamt.AppendLine("; Mit einem Doppelklick auf diese Datei laesst sich der Zustand");
            gesamt.AppendLine("; von vor dem Aufräumen wiederherstellen.");
            gesamt.AppendLine();

            bool etwas = false;
            foreach (string s in schluessel)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ww_reg_" + Guid.NewGuid().ToString("N") + ".reg");
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
                        etwas = true;
                    }
                }
                catch (Exception ex) { AppLog.Warn("Export von " + s + " fehlgeschlagen: " + ex.Message); }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            }

            if (!etwas) return false;
            try
            {
                File.WriteAllText(ziel, gesamt.ToString(), Encoding.Unicode);
                return new FileInfo(ziel).Length > 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("Sicherung konnte nicht geschrieben werden", ex);
                return false;
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
        /// Laufwerk, das gerade vorhanden ist. Sonst wuerde ein abgezogener USB-Stick oder
        /// ein getrenntes Netzlaufwerk als "geloescht" gelten.
        /// </summary>
        static bool PruefbarerPfad(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return false;
            if (p.Length < 4 || p[1] != ':' || p[2] != '\\') return false;   // kein C:\...
            if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            try { if (!new DriveInfo(p.Substring(0, 1)).IsReady) return false; }
            catch { return false; }
            return true;
        }
    }
}
