using System;
using System.Security.Principal;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Laeuft dieser Prozess erhoeht? Entscheidend fuer die Deutung leerer Antworten: ohne
    /// Erhoehung sind sie "kein Zugriff", nie "nichts vorhanden".
    ///
    /// Nie ueber whoami oder Gruppennamen ("Hohe Verbindlichkeitsstufe" ist lokalisiert),
    /// sondern ueber die Rolle und die Integritaets-SID S-1-16-12288 (High). Ein
    /// UAC-gefilterter Admin hat die Administratoren-SID nur "zum Ablehnen" und IsInRole=false.
    /// </summary>
    public static class Rechte
    {
        public static bool Erhoeht()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var p = new WindowsPrincipal(id);
                    if (p.IsInRole(WindowsBuiltInRole.Administrator)) return true;
                    if (id.Groups != null)
                        foreach (var g in id.Groups)
                            if (g.Value == "S-1-16-12288" || g.Value == "S-1-16-16384") return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>SID des aktuellen Kontos (fuer HKEY_USERS und die Pipe-Sicherheit).</summary>
        public static string EigeneSid()
        {
            try { using (var id = WindowsIdentity.GetCurrent()) return id.User == null ? null : id.User.Value; }
            catch (Exception) { return null; }
        }
    }
}
