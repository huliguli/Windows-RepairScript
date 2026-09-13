using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// WMI im eigenen Prozess ueber System.Management - kein powershell.exe. Die Cmdlets
    /// Get-PhysicalDisk, Get-NetAdapter, Get-MpComputerStatus, Get-PnpDevice sind cdxml-Huellen
    /// derselben Klassen (in den .cdxml-Dateien unter System32\WindowsPowerShell nachgelesen).
    ///
    /// Zeitgrenze je Abfrage: WMI kann haengen (ein defekter Provider, ein Netzlaufwerk).
    /// Die Abfrage laeuft auf einem eigenen Thread; nach Ablauf wird sie aufgegeben und der
    /// Aufrufer bekommt eine TimeoutException - die der Sammler als fehlerliste:zeit fuehrt.
    /// </summary>
    public static class Wmi
    {
        public const int StandardZeitMs = 5000;

        // Ein Namespace, dessen Abfrage ins Zeitlimit lief, gilt fuer eine Minute als haengend:
        // jede weitere Abfrage dorthin wird sofort abgewiesen, statt je Abfrage erneut das volle
        // Budget zu warten (rund 40 Abfragen mal 5 s waeren sonst mehrere Minuten).
        static readonly Dictionary<string, DateTime> HaengtSeit = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        const int HaengtSperreSekunden = 60;

        public static List<ManagementObject> Abfrage(string ns, string wql, int zeitMs = StandardZeitMs)
        {
            lock (HaengtSeit)
            {
                DateTime seit;
                if (HaengtSeit.TryGetValue(ns, out seit))
                {
                    if ((DateTime.UtcNow - seit).TotalSeconds < HaengtSperreSekunden)
                        throw new TimeoutException("WMI-Namespace hängt seit " + seit.ToString("HH:mm:ss") + " UTC, Abfrage übersprungen: " + ns + " / " + wql);
                    HaengtSeit.Remove(ns);
                }
            }
            List<ManagementObject> ergebnis = null;
            Exception fehler = null;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    var liste = new List<ManagementObject>();
                    var scope = new ManagementScope(ns);
                    scope.Connect();
                    using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(wql)))
                        foreach (ManagementObject mo in s.Get()) liste.Add(mo);
                    ergebnis = liste;
                }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "wmi:" + wql.Substring(0, Math.Min(40, wql.Length)) };
            t.Start();
            if (!t.Join(zeitMs))
            {
                lock (HaengtSeit) { HaengtSeit[ns] = DateTime.UtcNow; }
                throw new TimeoutException("WMI-Abfrage überschritt " + zeitMs + " ms: " + ns + " / " + wql);
            }
            if (fehler != null) throw fehler;
            return ergebnis;
        }

        /// <summary>true, wenn die Ausnahme "Zugriff verweigert" bedeutet (WMI, COM, DCOM).</summary>
        public static bool IstZugriffVerweigert(Exception ex)
        {
            var me = ex as ManagementException;
            if (me != null && me.ErrorCode == ManagementStatus.AccessDenied) return true;
            if (ex is UnauthorizedAccessException) return true;
            var ce = ex as System.Runtime.InteropServices.COMException;
            if (ce != null && (ce.HResult == unchecked((int)0x80070005) || ce.HResult == unchecked((int)0x80041003))) return true;
            return false;
        }

        // ---------------------------------------------------------------- Feldzugriff

        public static string Str(ManagementBaseObject mo, string name)
        {
            try { object v = mo[name]; return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
            catch (ManagementException) { return null; }
        }

        public static long? Lang(ManagementBaseObject mo, string name)
        {
            try
            {
                object v = mo[name];
                if (v == null) return null;
                // REG_DWORD und UInt32 kommen je nach Provider als Int32 mit Vorzeichen.
                if (v is int) return unchecked((uint)(int)v);
                return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return null; }
        }

        public static int? Ganz(ManagementBaseObject mo, string name)
        {
            var l = Lang(mo, name);
            if (!l.HasValue) return null;
            if (l.Value > int.MaxValue || l.Value < int.MinValue) return null;
            return (int)l.Value;
        }

        public static bool? Wahr(ManagementBaseObject mo, string name)
        {
            try { object v = mo[name]; return v == null ? (bool?)null : Convert.ToBoolean(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        public static double? Zahl(ManagementBaseObject mo, string name)
        {
            try { object v = mo[name]; return v == null ? (double?)null : Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        public static List<int> Ganze(ManagementBaseObject mo, string name)
        {
            var l = new List<int>();
            try
            {
                var arr = mo[name] as Array;
                if (arr != null) foreach (object o in arr) { try { l.Add(Convert.ToInt32(o, CultureInfo.InvariantCulture)); } catch (Exception) { } }
            }
            catch (Exception) { }
            return l;
        }

        public static List<string> Strings(ManagementBaseObject mo, string name)
        {
            var l = new List<string>();
            try
            {
                var arr = mo[name] as Array;
                if (arr != null) foreach (object o in arr) if (o != null) l.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
            return l;
        }

        /// <summary>
        /// CIM-Datum (DMTF "20260303010000.000000+060") nach UTC-ISO. System.Management liefert
        /// den DMTF-String, die CIM-Cmdlets eine lokale DateTime - hier wird immer ueber den
        /// Konverter gegangen und in UTC verglichen (sonst rutscht ein BIOS-Datum um einen Tag).
        /// </summary>
        public static string ZeitUtc(ManagementBaseObject mo, string name)
        {
            string s = Str(mo, name);
            if (string.IsNullOrEmpty(s)) return null;
            try { return Kern.Zeit.Utc(ManagementDateTimeConverter.ToDateTime(s)); }
            catch (Exception) { return null; }
        }

        /// <summary>SMBIOS-Platzhalter, die Selbstbau-PCs statt echter Werte liefern.</summary>
        public static string OhnePlatzhalter(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string t = s.Trim();
            string[] platzhalter =
            {
                "System Product Name", "To be filled by O.E.M.", "To Be Filled By O.E.M.", "SKU", "Default string",
                "System Serial Number", "System manufacturer", "System Version", "Not Applicable", "Not Specified", "OEM", "Standard",
            };
            foreach (string p in platzhalter) if (string.Equals(t, p, StringComparison.OrdinalIgnoreCase)) return null;
            return t;
        }
    }
}
