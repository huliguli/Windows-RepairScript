using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WartungsToolbox
{
    // Liest und schaltet Autostart-Eintraege (wie der Task-Manager, ueber StartupApproved -> umkehrbar).
    static class Autostart
    {
        const string RunPath        = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Run32Path      = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedRun    = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ApprovedRun32  = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        public static List<object> List()
        {
            var list = new List<object>();
            AddRun(list, Registry.CurrentUser, RunPath, ApprovedRun, "hkcu_run", "Aktueller Benutzer");
            AddRun(list, Registry.LocalMachine, RunPath, ApprovedRun, "hklm_run", "Alle Benutzer");
            AddRun(list, Registry.LocalMachine, Run32Path, ApprovedRun32, "hklm_run32", "Alle Benutzer (32-Bit)");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, ApprovedFolder, "startup_user", "Autostart-Ordner");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, ApprovedFolder, "startup_common", "Autostart-Ordner (alle)");
            return list;
        }

        static void AddRun(List<object> list, RegistryKey root, string path, string approved, string loc, string locName)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        list.Add(new
                        {
                            name = name,
                            cmd = Convert.ToString(k.GetValue(name)),
                            loc = loc,
                            locName = locName,
                            key = name,
                            enabled = IsEnabled(root, approved, name)
                        });
                    }
                }
            }
            catch { }
        }

        static void AddFolder(List<object> list, string folder, RegistryKey approvedRoot, string approved, string loc, string locName)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string f in Directory.GetFiles(folder))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new
                    {
                        name = Path.GetFileNameWithoutExtension(fn),
                        cmd = f,
                        loc = loc,
                        locName = locName,
                        key = fn,
                        enabled = IsEnabled(approvedRoot, approved, fn)
                    });
                }
            }
            catch { }
        }

        static bool IsEnabled(RegistryKey root, string approvedPath, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(approvedPath))
                {
                    if (k == null) return true;
                    byte[] b = k.GetValue(name) as byte[];
                    if (b == null || b.Length < 1) return true;
                    return (b[0] & 1) == 0; // gerade (2) = aktiv, ungerade (3) = deaktiviert
                }
            }
            catch { return true; }
        }

        /// <summary>
        /// Schaltet ein Startprogramm an oder aus. Rueckgabe false heisst: es hat NICHT
        /// geklappt.
        ///
        /// Frueher verschwand ein Fehlschlag hier spurlos. Die Oberflaeche zeigte den
        /// Schalter danach trotzdem in der neuen Stellung - der Nutzer war also ueberzeugt,
        /// das Programm starte nicht mehr mit, waehrend es weiterhin mitstartete. Eine
        /// stille Falschaussage ist schlimmer als eine sichtbare Fehlermeldung.
        /// </summary>
        public static bool SetEnabled(string loc, string key, bool enabled)
        {
            RegistryKey root; string approved;
            switch (loc)
            {
                case "hkcu_run":       root = Registry.CurrentUser;  approved = ApprovedRun;    break;
                case "hklm_run":       root = Registry.LocalMachine; approved = ApprovedRun;    break;
                case "hklm_run32":     root = Registry.LocalMachine; approved = ApprovedRun32;  break;
                case "startup_user":   root = Registry.CurrentUser;  approved = ApprovedFolder; break;
                case "startup_common": root = Registry.LocalMachine; approved = ApprovedFolder; break;
                default: return false;
            }
            try
            {
                using (RegistryKey k = root.CreateSubKey(approved))
                {
                    byte[] b = new byte[12];
                    if (enabled) b[0] = 2;
                    else
                    {
                        b[0] = 3;
                        byte[] t = BitConverter.GetBytes(DateTime.Now.ToFileTime());
                        Array.Copy(t, 0, b, 4, 8);
                    }
                    k.SetValue(key, b, RegistryValueKind.Binary);
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Startprogramm '" + key + "' liess sich nicht umschalten: " + ex.Message);
                return false;
            }
        }
    }
}
