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
                try { if (Directory.Exists(basis)) Directory.Delete(basis, true); } catch { }
            }

            Environment.Exit(fehler == 0 ? 0 : 1);
        }
    }
}
