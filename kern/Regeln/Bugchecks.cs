using System.Collections.Generic;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Die haeufigsten Stop-Codes mit Name und Ursachenklasse. Quelle: learn.microsoft.com
    /// "Bug Check Code Reference" (Namen) und "Stop error troubleshooting" (Ursachen; dort:
    /// 70 % Fremdtreiber, 10 % Hardware).
    ///
    /// Kein Ereignis nennt den schuldigen Treiber. Diese Tabelle liefert die Klasse; die
    /// Deutung sagt "wahrscheinlich ein Treiber", nie "Treiber X".
    ///
    /// Ereignis 41 liefert den Code DEZIMAL, Ereignis 1001 hexadezimal als Text.
    /// </summary>
    public static class Bugchecks
    {
        public const string Treiber = "Treiber";
        public const string Ram = "Arbeitsspeicher";
        public const string Datentraeger = "Datenträger";
        public const string Hardware = "Hardware";
        public const string Grafik = "Grafiktreiber";
        public const string System = "Systemprozess";

        public class Eintrag { public long Code; public string Name; public string[] Klassen; }

        static readonly Dictionary<long, Eintrag> Tabelle = Aufbau();

        static Dictionary<long, Eintrag> Aufbau()
        {
            var t = new Dictionary<long, Eintrag>();
            void E(long c, string n, params string[] k) { t[c] = new Eintrag { Code = c, Name = n, Klassen = k }; }
            E(0x0A, "IRQL_NOT_LESS_OR_EQUAL", Treiber);
            E(0x1A, "MEMORY_MANAGEMENT", Ram, Treiber);
            E(0x1E, "KMODE_EXCEPTION_NOT_HANDLED", Treiber);
            E(0x24, "NTFS_FILE_SYSTEM", Datentraeger);
            E(0x3B, "SYSTEM_SERVICE_EXCEPTION", Treiber);
            E(0x4E, "PFN_LIST_CORRUPT", Ram, Treiber);
            E(0x50, "PAGE_FAULT_IN_NONPAGED_AREA", Treiber, Ram, Datentraeger);
            E(0x7A, "KERNEL_DATA_INPAGE_ERROR", Datentraeger);
            E(0x7E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", Treiber);
            E(0x7F, "UNEXPECTED_KERNEL_MODE_TRAP", Treiber, Hardware);
            E(0x9F, "DRIVER_POWER_STATE_FAILURE", Treiber);
            E(0xC2, "BAD_POOL_CALLER", Treiber);
            E(0xC4, "DRIVER_VERIFIER_DETECTED_VIOLATION", Treiber);
            E(0xC5, "DRIVER_CORRUPTED_EXPOOL", Treiber);
            E(0xD1, "DRIVER_IRQL_NOT_LESS_OR_EQUAL", Treiber);
            E(0xEF, "CRITICAL_PROCESS_DIED", System);
            E(0xF4, "CRITICAL_OBJECT_TERMINATION", System, Datentraeger);
            E(0x109, "CRITICAL_STRUCTURE_CORRUPTION", Treiber, Ram, Hardware);
            E(0x116, "VIDEO_TDR_FAILURE", Grafik);
            E(0x117, "VIDEO_TDR_TIMEOUT_DETECTED", Grafik);
            E(0x124, "WHEA_UNCORRECTABLE_ERROR", Hardware);
            E(0x133, "DPC_WATCHDOG_VIOLATION", Treiber);
            E(0x139, "KERNEL_SECURITY_CHECK_FAILURE", Treiber, Ram);
            E(0x13A, "KERNEL_MODE_HEAP_CORRUPTION", Treiber);
            E(0x154, "UNEXPECTED_STORE_EXCEPTION", Datentraeger, Ram);
            E(0xC000021A, "STATUS_SYSTEM_PROCESS_TERMINATED", System);
            return t;
        }

        public static Eintrag Von(long code)
        {
            Eintrag e;
            return Tabelle.TryGetValue(code, out e) ? e : null;
        }
    }
}
