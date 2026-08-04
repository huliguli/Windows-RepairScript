using System;
using System.IO;

namespace WartungsToolbox
{
    /// <summary>
    /// Prueft die Bausteine der Herausgeber-Bindung (src/UpdateTrust.cs) gegen echte
    /// Dateien. Wird von tests/run-tests.ps1 uebersetzt und ausgefuehrt.
    ///
    /// Bewusst nur Dateien aus dem Projekt selbst: die WebView2-DLLs in libs/ sind von
    /// Microsoft EINGEBETTET signiert und liegen im Repository, sind also auf jedem
    /// Rechner und in der CI vorhanden.
    /// </summary>
    static class TrustProbe
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
            string wurzel = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            string signiert = Path.Combine(wurzel, "libs", "WebView2Loader.dll");
            string signiert2 = Path.Combine(wurzel, "libs", "Microsoft.Web.WebView2.Core.dll");

            if (!File.Exists(signiert))
            {
                Console.WriteLine("         [FEHL] libs\\WebView2Loader.dll nicht gefunden unter " + wurzel);
                Environment.Exit(1);
            }

            string fp = UpdateTrust.Fingerabdruck(signiert);
            Ist("eingebettet signierte Datei liefert Fingerabdruck", fp != null, fp ?? "null");
            Ist("Signierer wird gelesen", UpdateTrust.Signierer(signiert) != null, "");

            // Beide DLLs stammen von Microsoft, tragen aber VERSCHIEDENE Zertifikate.
            // Genau daran ist die urspruengliche Fingerabdruck-Bindung aufgefallen: sie
            // haette bei jeder Zertifikatserneuerung alle Updates abgelehnt. Gebunden wird
            // deshalb am Herausgebernamen, und der muss hier uebereinstimmen.
            Ist("verschiedene Zertifikate beim selben Herausgeber",
                fp != null && UpdateTrust.Fingerabdruck(signiert2) != null
                           && fp != UpdateTrust.Fingerabdruck(signiert2), "");
            Ist("gleicher Herausgebername trotz anderem Zertifikat",
                UpdateTrust.Signierer(signiert) == UpdateTrust.Signierer(signiert2),
                UpdateTrust.Signierer(signiert) ?? "null");

            // Unsignierte und fehlende Dateien duerfen null liefern, nicht werfen.
            string leer = Path.GetTempFileName();
            File.WriteAllText(leer, "kein PE");
            Ist("unsignierte Datei liefert null", UpdateTrust.Fingerabdruck(leer) == null, "");
            File.Delete(leer);
            Ist("fehlende Datei liefert null", UpdateTrust.Fingerabdruck(
                Path.Combine(wurzel, "gibt-es-nicht-12345.exe")) == null, "");

            // WICHTIG, damit niemand spaeter darueber stolpert: CreateFromSignedFile sieht
            // nur EINGEBETTETE Signaturen. Windows-Systemdateien sind katalog-signiert und
            // liefern deshalb null. Fuer unsere eigene EXE ist das der richtige Weg, weil
            // Set-AuthenticodeSignature eingebettet signiert.
            string katalog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            if (File.Exists(katalog))
                Ist("katalog-signierte Datei liefert null (so gewollt)",
                    UpdateTrust.Fingerabdruck(katalog) == null, "");

            // Die Probe selbst ist unsigniert: die Bindung entfaellt und darf NICHT
            // blockieren, sonst koennte sich eine unsignierte Auslieferung nie aktualisieren.
            Ist("unsignierte laufende Fassung blockiert das Update nicht",
                UpdateTrust.PruefeHerausgeber(signiert) == null, "");

            string ort;
            UpdateTrust.IstPerInstallerInstalliert(out ort);
            Ist("Installationsart laesst sich bestimmen, ohne zu werfen", true, "");

            Environment.Exit(fehler == 0 ? 0 : 1);
        }
    }
}
