using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace WartungsToolbox
{
    /// <summary>
    /// Vertrauensfragen rund um das Selbstupdate.
    ///
    /// Zwei Dinge, die eine Pruefsumme allein NICHT leistet:
    ///
    /// 1. Die Pruefsumme liegt im selben Release wie die Datei. Wer das Release
    ///    austauschen kann, tauscht beides. Sie beweist Unversehrtheit des Downloads,
    ///    nicht die Herkunft. Dagegen hilft die Authenticode-Signatur - aber nur, wenn
    ///    man sie an den bereits installierten Herausgeber bindet ("Pinning").
    ///
    /// 2. Wurde die App ueber den Installer eingerichtet, fuehrt ein blosser Dateitausch
    ///    dazu, dass der Eintrag unter "Apps und Features" auf der alten Version stehen
    ///    bleibt und eine spaetere Deinstallation Dateien uebersieht. Deshalb wird in
    ///    diesem Fall der Installer des neuen Releases still ausgefuehrt.
    /// </summary>
    static class UpdateTrust
    {
        // Von Inno Setup angelegter Schluessel; die AppId steht in installer/WindowsWartung.iss.
        const string UninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{4D9A7C2E-3B1F-4E8A-9C6D-1A2B3C4D5E6F}_is1";

        /// <summary>
        /// Wurde diese Fassung ueber den Installer eingerichtet? Geprueft wird, ob der
        /// Installationsort aus der Registrierung auf genau diesen Programmordner zeigt -
        /// eine daneben entpackte Zweitkopie soll sich NICHT ueber den Installer aktualisieren.
        /// </summary>
        public static bool IstPerInstallerInstalliert(out string installOrt)
        {
            installOrt = null;
            try
            {
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using (RegistryKey basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey k = basis.OpenSubKey(UninstallKey))
                    {
                        if (k == null) continue;
                        string ort = k.GetValue("InstallLocation") as string;
                        if (string.IsNullOrEmpty(ort)) continue;

                        string a = Path.GetFullPath(ort).TrimEnd('\\');
                        string b = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\');
                        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                        {
                            installOrt = a;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) { AppLog.Warn("Installationsart nicht bestimmbar: " + ex.Message); }
            return false;
        }

        /// <summary>Fingerabdruck des Signaturzertifikats oder null, wenn die Datei nicht signiert ist.</summary>
        public static string Fingerabdruck(string datei)
        {
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(datei));
                return cert.Thumbprint;
            }
            catch
            {
                return null;   // nicht signiert oder Signatur nicht lesbar
            }
        }

        /// <summary>Anzeigename des Signierers, fuer das Protokoll.</summary>
        public static string Signierer(string datei)
        {
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(datei));
                return cert.Subject;
            }
            catch { return null; }
        }

        /// <summary>
        /// Bindet das Update an den Herausgeber der laufenden Fassung.
        ///
        /// Regel: Ist die laufende Datei signiert, MUSS die neue denselben Herausgeber
        /// tragen. Ist die laufende Datei nicht signiert (aktueller Auslieferungszustand),
        /// gibt es nichts zu binden - das wird protokolliert und durchgelassen, sonst
        /// koennte sich die App nie wieder aktualisieren.
        ///
        /// Verglichen wird der HERAUSGEBERNAME, nicht der Fingerabdruck des Zertifikats.
        /// Grund: Ein Fingerabdruck gehoert zu genau einem Zertifikat. Laeuft das ab und
        /// wird erneuert, aendert er sich - und eine Bindung darauf wuerde ab diesem Tag
        /// JEDES weitere Update ablehnen, obwohl alles in Ordnung ist. Beim Testen ist
        /// genau das aufgefallen: die beiden WebView2-DLLs stammen beide von Microsoft,
        /// tragen aber verschiedene Zertifikate.
        ///
        /// Der Name ist etwas schwaecher als der Fingerabdruck (theoretisch koennte jemand
        /// ein Zertifikat auf denselben Namen bekommen), aber er ist die einzige Bindung,
        /// die eine Zertifikatserneuerung ueberlebt. Der Fingerabdruck wird protokolliert,
        /// damit sich ein Wechsel nachvollziehen laesst.
        ///
        /// Gibt null zurueck, wenn alles in Ordnung ist, sonst eine Meldung fuer den Nutzer.
        /// </summary>
        public static string PruefeHerausgeber(string neueDatei)
        {
            string eigeneExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowsWartung.exe");
            string laufend = Signierer(eigeneExe);
            if (laufend == null)
            {
                AppLog.Info("Die laufende Fassung ist nicht signiert - Herausgeber-Bindung entfaellt.");
                return null;
            }

            string neu = Signierer(neueDatei);
            if (neu == null)
            {
                AppLog.Error("Update abgelehnt: die neue Fassung ist nicht signiert, die laufende schon.");
                return "Die neue Fassung trägt keine Signatur, die aktuelle aber schon. " +
                       "Aus Sicherheitsgründen wurde nichts installiert.";
            }
            if (!string.Equals(laufend, neu, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Error("Update abgelehnt: anderer Herausgeber. Laufend [" + laufend + "], neu [" + neu + "]");
                return "Die neue Fassung stammt von einem anderen Herausgeber als die installierte. " +
                       "Aus Sicherheitsgründen wurde nichts installiert.";
            }

            AppLog.Info("Herausgeber bestaetigt: " + neu + " (Zertifikat " + (Fingerabdruck(neueDatei) ?? "?") + ")");
            return null;
        }
    }
}
