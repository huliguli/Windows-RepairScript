using System;
using System.Diagnostics;
using System.IO;

namespace WartungsToolbox
{
    /// <summary>
    /// Probe fuer den Loeschweg von src/StorageScan.cs.
    ///
    /// Die eine Frage, die hier zaehlt: Kann LeereOrdner() etwas ausserhalb des gemeinten
    /// Ordners erwischen? Eine Abzweigung (Junction) im Zwischenspeicher zeigt auf einen
    /// ganz anderen Ordner. Wer beim Loeschen hineinlaeuft, raeumt fremde Daten weg -
    /// und genau daran sind reihenweise "PC-Reiniger" gescheitert.
    ///
    /// Deshalb wird hier ein echter Aufbau im Temp-Ordner gebaut, geloescht und
    /// nachgesehen, was ueberlebt hat. Nichts davon fasst den Rechner des Nutzers an.
    /// </summary>
    static class StorageProbe
    {
        static int fehler = 0;

        static void Ist(string was, bool bedingung, string detail)
        {
            Console.WriteLine((bedingung ? "         [ok]   " : "         [FEHL] ") + was +
                              (detail.Length > 0 ? "   " + detail : ""));
            if (!bedingung) fehler++;
        }

        static void Main(string[] args)
        {
            string basis = Path.Combine(Path.GetTempPath(), "WW-StorageProbe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string cache = Path.Combine(basis, "cache");         // der Ordner, der geleert wird
            string fremd = Path.Combine(basis, "fremd");         // das Ziel der Abzweigung
            string abzweig = Path.Combine(cache, "abzweigung");

            // Das App-Protokoll der Probe bleibt im Wegwerf-Ordner (StorageScan.Aufraeumen ruft
            // AppLog.Info): ohne die Naht legte der erste Aufruf %ProgramData%\WindowsWartung\logs
            // an und zoege eine 8.0-Datei aus dem Nutzerprofil nach. Der Ordner entsteht gleich
            // unten mit; eine Zeile davor ginge verloren, AppLog wirft nie.
            AppLog.PfadFuerProbe = Path.Combine(basis, "app.log");

            try
            {
                Directory.CreateDirectory(cache);
                Directory.CreateDirectory(fremd);
                Directory.CreateDirectory(Path.Combine(cache, "unterordner"));
                Directory.CreateDirectory(Path.Combine(cache, "unterordner", "noch tiefer"));

                File.WriteAllText(Path.Combine(cache, "datei.bin"), new string('x', 5000));
                File.WriteAllText(Path.Combine(cache, "unterordner", "tief.bin"), new string('y', 3000));
                File.WriteAllText(Path.Combine(fremd, "WICHTIG.txt"), "Diese Datei darf NIE geloescht werden.");

                // Schreibgeschuetzte Datei: der Zwischenspeicher enthaelt so etwas gelegentlich.
                string ro = Path.Combine(cache, "schreibgeschuetzt.bin");
                File.WriteAllText(ro, new string('z', 1000));
                File.SetAttributes(ro, FileAttributes.ReadOnly);

                // Zwei Abzweigungen: eine direkt im geleerten Ordner, eine TIEFER im Baum.
                // Die tiefe ist die eigentliche Probe: sie faellt nur dann nicht, wenn beim
                // Loeschen auf JEDER Ebene geprueft wird. Ein Directory.Delete(pfad, true)
                // wuerde den ganzen Baum ohne Ruecksicht raeumen.
                string abzweigTief = Path.Combine(cache, "unterordner", "noch tiefer", "abzweigung2");
                foreach (string[] paar in new[] { new[] { abzweig, fremd }, new[] { abzweigTief, fremd } })
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + paar[0] + "\" \"" + paar[1] + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (Process p = Process.Start(psi)) { p.WaitForExit(15000); }
                }

                bool abzweigDa = Directory.Exists(abzweig) &&
                                 (new DirectoryInfo(abzweig).Attributes & FileAttributes.ReparsePoint) != 0;
                bool tiefDa = Directory.Exists(abzweigTief) &&
                              (new DirectoryInfo(abzweigTief).Attributes & FileAttributes.ReparsePoint) != 0;
                Ist("Abzweigungen fuer den Test angelegt", abzweigDa && tiefDa,
                    (abzweigDa && tiefDa) ? "" : "mklink /J hat nicht funktioniert");

                long frei = StorageScan.LeereOrdner(cache, null);

                Ist("der geleerte Ordner selbst bleibt bestehen", Directory.Exists(cache), "");
                Ist("Dateien darin sind weg", !File.Exists(Path.Combine(cache, "datei.bin")), "");
                Ist("Unterordner sind weg", !Directory.Exists(Path.Combine(cache, "unterordner")), "");
                Ist("auch schreibgeschuetzte Dateien sind weg", !File.Exists(ro), "");

                // Das Herzstueck: die Abzweigung wird als Verweis entfernt, ihr ZIEL bleibt -
                // und zwar auch dann, wenn sie tief im Baum steckt.
                if (abzweigDa && tiefDa)
                {
                    Ist("die Abzweigung selbst ist weg", !Directory.Exists(abzweig), "");
                    Ist("auch die tief liegende Abzweigung ist weg", !Directory.Exists(abzweigTief), "");
                    Ist("der Ordner HINTER den Abzweigungen ist unberuehrt",
                        Directory.Exists(fremd) && File.Exists(Path.Combine(fremd, "WICHTIG.txt")), fremd);
                }

                Ist("die gemeldete Freigabe ist groesser als null", frei > 0, frei + " Byte");

                // Ein leerer bzw. fehlender Ordner darf nicht werfen und nichts melden.
                Ist("fehlender Ordner liefert 0, ohne zu werfen",
                    StorageScan.LeereOrdner(Path.Combine(basis, "gibt-es-nicht"), null) == 0, "");
                Ist("null als Pfad liefert 0, ohne zu werfen",
                    StorageScan.LeereOrdner(null, null) == 0, "");

                // Eine unbekannte Kennung darf nichts ausloesen.
                object leer = StorageScan.Aufraeumen(new[] { "gibt-es-nicht" }, null, null);
                Ist("unbekannte Kennung raeumt nichts weg", leer != null, "");

                AlteDateienProbe(basis);
                NeustartProbe();
            }
            catch (Exception ex)
            {
                Ist("Probe laeuft ohne Ausnahme", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Erst die Abzweigungen aufloesen, dann alles weg: sonst koennte das
                // Aufraeumen der Probe selbst in den fremden Ordner laufen.
                foreach (string a in new[] { abzweig,
                             Path.Combine(cache, "unterordner", "noch tiefer", "abzweigung2") })
                {
                    try { if (Directory.Exists(a)) Directory.Delete(a, false); } catch { }
                }
                AppLog.PfadFuerProbe = null;
                try { if (Directory.Exists(basis)) Directory.Delete(basis, true); } catch { }
            }

            Environment.Exit(fehler == 0 ? 0 : 1);
        }

        /// <summary>
        /// Temporaere Dateien seit 8.3.1 (Audit 10.11.2026, Bluescreen nach dem Aufraeumen gemeldet):
        /// nur Dateien, die seit einem Tag weder angelegt noch geaendert wurden; Ordner bleiben;
        /// Abzweigungen werden nicht betreten. Ein heute entpackter Treiber mit altem
        /// Aenderungsdatum aus dem Archiv ist der Fall, an dem eine reine Aenderungsdatum-Grenze scheitert.
        /// </summary>
        static void AlteDateienProbe(string basis)
        {
            string temp = Path.Combine(basis, "temp");
            string fremd = Path.Combine(basis, "fremd-temp");
            string abzweig = Path.Combine(temp, "abzweigung");
            try
            {
                Directory.CreateDirectory(Path.Combine(temp, "unter"));
                Directory.CreateDirectory(fremd);
                DateTime alt = DateTime.UtcNow.AddDays(-3);
                Action<string, DateTime, DateTime> datei = (p, erstellt, geaendert) =>
                {
                    File.WriteAllText(p, new string('t', 100));
                    File.SetCreationTimeUtc(p, erstellt);
                    File.SetLastWriteTimeUtc(p, geaendert);
                };
                datei(Path.Combine(temp, "alt.tmp"), alt, alt);
                datei(Path.Combine(temp, "unter", "alt2.tmp"), alt, alt);
                datei(Path.Combine(temp, "frisch.tmp"), DateTime.UtcNow, DateTime.UtcNow);
                datei(Path.Combine(temp, "entpackt.sys"), DateTime.UtcNow, alt);
                datei(Path.Combine(fremd, "WICHTIG.txt"), alt, alt);
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + abzweig + "\" \"" + fremd + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(psi)) { p.WaitForExit(15000); }

                DateTime grenze = DateTime.UtcNow - StorageScan.TempMindestalter;
                long gezaehlt = StorageScan.AlteDateien(temp, grenze, null, null, false);
                Ist("Zaehlen nimmt nur die 2 alten Dateien (200 Byte)", gezaehlt == 200, gezaehlt + " Byte");
                Ist("Zaehlen loescht nichts", File.Exists(Path.Combine(temp, "alt.tmp")), "");

                var liste = new StorageScan.Liste();
                long frei = StorageScan.AlteDateien(temp, grenze, null, liste, true);
                Ist("alte Datei ist weg", !File.Exists(Path.Combine(temp, "alt.tmp")), "");
                Ist("alte Datei im Unterordner ist weg", !File.Exists(Path.Combine(temp, "unter", "alt2.tmp")), "");
                Ist("der Unterordner selbst bleibt stehen", Directory.Exists(Path.Combine(temp, "unter")), "");
                Ist("frische Datei bleibt liegen", File.Exists(Path.Combine(temp, "frisch.tmp")), "");
                Ist("heute entpackte Datei mit altem Aenderungsdatum bleibt liegen", File.Exists(Path.Combine(temp, "entpackt.sys")), "");
                Ist("die Abzweigung wird nicht betreten, ihr Ziel bleibt", File.Exists(Path.Combine(fremd, "WICHTIG.txt")), "");
                Ist("die Abzweigung selbst bleibt (nur Dateien fallen)", Directory.Exists(abzweig), "");
                Ist("gemeldet: 200 Byte, 2 Dateien in der Liste", frei == 200 && liste.Anzahl == 2 && liste.Namen.Count == 2,
                    frei + " Byte, " + liste.Anzahl + " Dateien");

                var lang = new StorageScan.Liste();
                for (int i = 0; i < StorageScan.ListeMax + 5; i++) lang.Dazu("x" + i);
                Ist("die Liste zaehlt alles, nennt aber hoechstens " + StorageScan.ListeMax,
                    lang.Anzahl == StorageScan.ListeMax + 5 && lang.Namen.Count == StorageScan.ListeMax, lang.Anzahl + "/" + lang.Namen.Count);
            }
            finally
            {
                try { if (Directory.Exists(abzweig)) Directory.Delete(abzweig, false); } catch { }
            }
        }

        /// <summary>
        /// Sperre bei ausstehendem Neustart (8.3.1). Die Eintraege bilden PendingFileRenameOperations
        /// nach, wie es auf dem Entwicklungsrechner am 10.11.2026 stand: Paare aus Quelle und Ziel,
        /// Vorsatz "*1" vor "\??\", Ziel mit "!" davor, leeres Ziel = Loeschauftrag.
        /// </summary>
        static void NeustartProbe()
        {
            string[] temp = { @"C:\Users\Probe\AppData\Local\Temp\", @"C:\Windows\Temp" };
            Ist("ohne ausstehenden Neustart: keine Sperre", StorageScan.NeustartGrund(false, false, new string[0], temp) == null, "");
            Ist("CBS wartet auf einen Neustart: Sperre", StorageScan.NeustartGrund(true, false, new string[0], temp) != null, "");
            Ist("Windows Update wartet auf einen Neustart: Sperre", StorageScan.NeustartGrund(false, true, new string[0], temp) != null, "");

            string[] loeschen = { @"*1\??\C:\Users\Probe\AppData\Local\Temp\nsp563A.tmp\nsProcess.dll", "",
                                  @"*1\??\C:\Users\Probe\AppData\Local\Temp\nsp563A.tmp", "" };
            Ist("nur Loeschauftraege in Temp (NSIS): keine Sperre", StorageScan.NeustartGrund(false, false, loeschen, temp) == null, "");

            string[] ersetzen = { @"*1\??\C:\Windows\Temp\neu\treiber.sys", @"!\??\C:\Windows\System32\drivers\treiber.sys" };
            string g = StorageScan.NeustartGrund(false, false, ersetzen, temp);
            Ist("Ersetzung mit Quelle in Windows\\Temp: Sperre, die Datei steht im Satz", g != null && g.IndexOf("treiber.sys", StringComparison.Ordinal) >= 0, g ?? "null");

            string[] ausserhalb = { @"\??\C:\Program Files\X\neu.dll", @"!\??\C:\Program Files\X\alt.dll" };
            Ist("Ersetzung ausserhalb von Temp: keine Sperre", StorageScan.NeustartGrund(false, false, ausserhalb, temp) == null, "");

            string[] aehnlich = { @"\??\C:\Windows\Temp2\neu.sys", @"!\??\C:\Windows\System32\drivers\neu.sys" };
            Ist("Temp2 liegt nicht unter Temp", StorageScan.NeustartGrund(false, false, aehnlich, temp) == null, "");

            string[] ungerade = { @"\??\C:\Windows\Temp\x.tmp", "", @"\??\C:\Windows\Temp\y.tmp" };
            Ist("ungerade Anzahl (Paare nicht lesbar): Sperre, fail closed", StorageScan.NeustartGrund(false, false, ungerade, temp) != null, "");

            Ist("TauschPfad liest *1\\??\\", StorageScan.TauschPfad(@"*1\??\C:\A\b.dll") == @"C:\A\b.dll", StorageScan.TauschPfad(@"*1\??\C:\A\b.dll") ?? "null");
            Ist("TauschPfad liest !\\??\\", StorageScan.TauschPfad(@"!\??\C:\A\b.dll") == @"C:\A\b.dll", "");
            Ist("TauschPfad lehnt leer und relativ ab", StorageScan.TauschPfad("") == null && StorageScan.TauschPfad(@"A\b.dll") == null, "");
            Ist("Unter: Ordner selbst und darunter, ohne Gross/Klein", StorageScan.Unter(@"C:\WINDOWS\TEMP", @"C:\Windows\Temp\") && StorageScan.Unter(@"c:\windows\temp\a\b", @"C:\Windows\Temp"), "");
        }
    }
}
