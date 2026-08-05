using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WartungsToolbox
{
    /// <summary>
    /// Beantwortet eine Frage, die in einem Buero-Netz staendig auftaucht: Laeuft dieses
    /// Programm unter demselben Konto wie der Mensch, der gerade davorsitzt?
    ///
    /// Warum das zaehlt: Die App verlangt Administratorrechte. Ist der angemeldete Nutzer
    /// ein normaler Anwender, muss er bei der Abfrage die Zugangsdaten eines ANDEREN Kontos
    /// eingeben - und ab da laeuft alles unter diesem fremden Konto. Damit zeigen und
    /// bearbeiten mehrere Ansichten stillschweigend das falsche Profil:
    ///
    ///   * Startprogramme (die Eintraege des angemeldeten Kontos sind unsichtbar)
    ///   * Verlauf (liegt im Profil des Admin-Kontos)
    ///   * "Wo steckt der Platz?" (Downloads, Dokumente, Bilder, Videos, Papierkorb und
    ///     die Zwischenspeicher der Browser gehoeren dann dem Admin-Konto)
    ///   * "Eintraege, die ins Leere zeigen" (alles unter HKCU)
    ///
    /// Gefaehrlich ist das nicht - es wird nichts Fremdes geloescht, sondern das Falsche
    /// angezeigt. Aber es ist verwirrend, und Verwirrung ist bei dieser Zielgruppe teuer.
    /// Deshalb: erkennen und es klar dazusagen, statt so zu tun, als sei alles in Ordnung.
    ///
    /// Bewusst NICHT: sich in das Konto des angemeldeten Nutzers hineinversetzen. Das waere
    /// deutlich mehr Technik und wuerde neue Fehlerquellen aufmachen, ohne dass jemand
    /// darum gebeten hat.
    /// </summary>
    static class Nutzerkontext
    {
        const uint TOKEN_QUERY = 0x0008;
        const int TokenUser = 1;

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr prozess, uint zugriff, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int klasse, IntPtr puffer,
                                               int laenge, out int gebraucht);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        /// <summary>
        /// true, wenn die App unter einem anderen Konto laeuft als der angemeldete Nutzer.
        /// Im Zweifel (nichts feststellbar) wird false gemeldet - lieber nicht warnen als
        /// grundlos verunsichern.
        /// </summary>
        public static bool AnderesKontoAlsAngemeldet(out string laeuftAls, out string angemeldet)
        {
            laeuftAls = "";
            angemeldet = "";
            try
            {
                using (WindowsIdentity ich = WindowsIdentity.GetCurrent())
                {
                    laeuftAls = Kurz(ich.Name);
                    SecurityIdentifier meine = ich.User;
                    if (meine == null) return false;

                    // Die Windows-Oberflaeche gehoert dem angemeldeten Nutzer. Bei schneller
                    // Benutzerumschaltung gibt es mehrere - gehoert AUCH NUR EINE uns, sitzt
                    // derselbe Mensch davor und es gibt nichts zu melden.
                    string fremderName = null;
                    foreach (Process p in Process.GetProcessesByName("explorer"))
                    {
                        using (p)
                        {
                            SecurityIdentifier sid = BesitzerVon(p);
                            if (sid == null) continue;
                            if (sid.Equals(meine)) return false;
                            if (fremderName == null) fremderName = NameVon(sid);
                        }
                    }

                    if (fremderName == null) return false;   // keine Oberflaeche greifbar
                    angemeldet = Kurz(fremderName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Benutzerkonto liess sich nicht bestimmen: " + ex.Message);
                return false;
            }
        }

        static SecurityIdentifier BesitzerVon(Process p)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(p.Handle, TOKEN_QUERY, out token)) return null;

                int gebraucht;
                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out gebraucht);
                if (gebraucht <= 0) return null;

                IntPtr puffer = Marshal.AllocHGlobal(gebraucht);
                try
                {
                    if (!GetTokenInformation(token, TokenUser, puffer, gebraucht, out gebraucht)) return null;
                    // TOKEN_USER beginnt mit SID_AND_ATTRIBUTES, dessen erstes Feld der Zeiger
                    // auf die Kennung ist.
                    IntPtr sid = Marshal.ReadIntPtr(puffer);
                    return sid == IntPtr.Zero ? null : new SecurityIdentifier(sid);
                }
                finally { Marshal.FreeHGlobal(puffer); }
            }
            catch { return null; }   // fremde Sitzung, kein Zugriff: dann eben nicht
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        static string NameVon(SecurityIdentifier sid)
        {
            try { return ((NTAccount)sid.Translate(typeof(NTAccount))).Value; }
            catch { return sid.Value; }
        }

        /// <summary>"RECHNER\Jonas" wird zu "Jonas" - der Rechnername sagt dem Nutzer nichts.</summary>
        static string Kurz(string konto)
        {
            if (string.IsNullOrEmpty(konto)) return "";
            int schnitt = konto.LastIndexOf('\\');
            return schnitt >= 0 && schnitt < konto.Length - 1 ? konto.Substring(schnitt + 1) : konto;
        }
    }
}
