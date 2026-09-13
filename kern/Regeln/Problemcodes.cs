using System.Collections.Generic;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Die 57 Problemcodes des Konfigurations-Managers (CM_PROB_* aus cfg.h, Windows SDK
    /// 10.0.26100) und ihre Einstufung. Quelle der Bedeutungen: learn.microsoft.com
    /// "Device Manager error messages" (Codes 1-57) und die Kommentare in cfg.h.
    ///
    /// Vier Klassen, Zuordnung nach der freigegebenen Tabelle in Konzept 4.1:
    ///   Defekt      - echtes Problem, Rat oder Massnahme. Ursache "treiber" (1, 2, 10, 18, 28,
    ///                 31, 37, 39, 40, 48, 50, 52) bekommt die Massnahme Treiber-Neuinstallation;
    ///                 Ursache "hardware"/"konfiguration" (4-9, 11-13, 16, 17, 19, 27, 30, 33-36,
    ///                 43, 49) nur Rat. 7 und 8 sind laut cfg.h Fehler des Treiberladers, das
    ///                 Konzept fuehrt sie bewusst ohne automatische Massnahme; der Rat nennt die
    ///                 Neuinstallation trotzdem.
    ///   Transient   - geht meist mit einem Neustart weg (3, 14, 15, 21, 25, 26, 38, 42, 46, 54,
    ///                 56; 23 fehlt in der Konzepttabelle und bleibt hier, Windows-95-Lader).
    ///   Gewollt     - kann Absicht sein: fragen, nie automatisch aendern (Grundsatz 1):
    ///                 22, 29, 32, 44, 53, 55, 24, 41, 51, 57.
    ///   Harmlos     - kein Befund (45, 47, 20).
    /// Die Probe "Konzepttabelle" in HardwareProben.cs haelt diese Listen gegen die Tabelle.
    ///
    /// Auf dem Rechner des Betreibers ist eine Grafikeinheit mit Code 22 absichtlich
    /// deaktiviert (Entscheidung vom 04.09.2026). Genau dieser Fall steht als Probe im Repo.
    /// </summary>
    public static class Problemcodes
    {
        public const string Defekt = "defekt";
        public const string Transient = "transient";
        public const string Gewollt = "gewollt";
        public const string Harmlos = "harmlos";

        public class Eintrag
        {
            public int Code; public string Name; public string Klasse; public string Ursache; public string Bedeutung;
            public Eintrag(int c, string n, string k, string u, string b) { Code = c; Name = n; Klasse = k; Ursache = u; Bedeutung = b; }
        }

        public const string Treiber = "treiber";
        public const string Hardware = "hardware";
        public const string Konfiguration = "konfiguration";
        public const string Keine = "";

        static readonly Dictionary<int, Eintrag> Tabelle = Aufbau();

        static Dictionary<int, Eintrag> Aufbau()
        {
            var t = new Dictionary<int, Eintrag>();
            void E(int c, string n, string k, string u, string b) { t[c] = new Eintrag(c, n, k, u, b); }
            E(1, "NOT_CONFIGURED", Defekt, Treiber, "Das Gerät ist nicht richtig konfiguriert.");
            E(2, "DEVLOADER_FAILED", Defekt, Treiber, "Der Treiber lässt sich nicht laden.");
            E(3, "OUT_OF_MEMORY", Transient, Keine, "Der Treiber ist beschädigt oder der Speicher reicht nicht.");
            E(4, "ENTRY_IS_WRONG_TYPE", Defekt, Konfiguration, "Treiber oder Registrierung sind beschädigt.");
            E(5, "LACKED_ARBITRATOR", Defekt, Konfiguration, "Eine Ressource lässt sich nicht verwalten.");
            E(6, "BOOT_CONFIG_CONFLICT", Defekt, Konfiguration, "Konflikt mit der Startkonfiguration.");
            E(7, "FAILED_FILTER", Defekt, Konfiguration, "Ein Filtertreiber ist fehlgeschlagen.");
            E(8, "DEVLOADER_NOT_FOUND", Defekt, Konfiguration, "Der Treiber-Lader fehlt.");
            E(9, "INVALID_DATA", Defekt, Hardware, "Die Firmware meldet ungültige Daten.");
            E(10, "FAILED_START", Defekt, Treiber, "Das Gerät kann nicht gestartet werden.");
            E(11, "LIAR", Defekt, Hardware, "Das Gerät ist ausgefallen.");
            E(12, "NORMAL_CONFLICT", Defekt, Konfiguration, "Zwei Geräte streiten um dieselben Ressourcen.");
            E(13, "NOT_VERIFIED", Defekt, Konfiguration, "Die Ressourcen ließen sich nicht prüfen.");
            E(14, "NEED_RESTART", Transient, Keine, "Erst nach einem Neustart einsatzbereit.");
            E(15, "REENUMERATION", Transient, Keine, "Problem beim Neuerkennen; nach Neustart bewerten.");
            E(16, "PARTIAL_LOG_CONF", Defekt, Konfiguration, "Nicht alle Ressourcen erkannt.");
            E(17, "UNKNOWN_RESOURCE", Defekt, Konfiguration, "Unbekannter Ressourcentyp.");
            E(18, "REINSTALL", Defekt, Treiber, "Der Treiber muss neu installiert werden.");
            E(19, "REGISTRY", Defekt, Konfiguration, "Die Registrierung ist für dieses Gerät beschädigt.");
            E(20, "VXDLDR", Harmlos, Keine, "Nur Windows 95; kommt nicht mehr vor.");
            E(21, "WILL_BE_REMOVED", Transient, Keine, "Wird gerade entfernt.");
            E(22, "DISABLED", Gewollt, Keine, "Vom Benutzer oder einem Programm abgeschaltet.");
            E(23, "DEVLOADER_NOT_READY", Transient, Keine, "Der Lader ist nicht bereit.");
            E(24, "DEVICE_NOT_THERE", Gewollt, Hardware, "Nicht vorhanden, nicht richtig verbunden oder Treiber fehlen.");
            E(25, "MOVED", Transient, Keine, "Windows richtet das Gerät noch ein.");
            E(26, "TOO_EARLY", Transient, Keine, "Windows richtet das Gerät noch ein.");
            E(27, "NO_VALID_LOG_CONF", Defekt, Konfiguration, "Keine gültige Konfiguration.");
            E(28, "FAILED_INSTALL", Defekt, Treiber, "Es ist kein Treiber installiert.");
            E(29, "HARDWARE_DISABLED", Gewollt, Hardware, "Per Firmware (BIOS) abgeschaltet oder ohne Ressourcen.");
            E(30, "CANT_SHARE_IRQ", Defekt, Konfiguration, "Unterbrechungsleitung kann nicht geteilt werden.");
            E(31, "FAILED_ADD", Defekt, Treiber, "Der Treiber lässt sich nicht laden.");
            E(32, "DISABLED_SERVICE", Gewollt, Konfiguration, "Der zugehörige Dienst ist abgeschaltet.");
            E(33, "TRANSLATION_FAILED", Defekt, Hardware, "Ressourcen ließen sich nicht zuordnen.");
            E(34, "NO_SOFTCONFIG", Defekt, Konfiguration, "Ressourcen müssen von Hand eingestellt werden.");
            E(35, "BIOS_TABLE", Defekt, Hardware, "Die Firmware liefert zu wenig Informationen.");
            E(36, "IRQ_TRANSLATION_FAILED", Defekt, Hardware, "Unterbrechungsleitung nicht zuzuordnen (Firmware).");
            E(37, "FAILED_DRIVER_ENTRY", Defekt, Treiber, "Der Treiber startet nicht.");
            E(38, "DRIVER_FAILED_PRIOR_UNLOAD", Transient, Keine, "Eine alte Fassung des Treibers ist noch im Speicher.");
            E(39, "DRIVER_FAILED_LOAD", Defekt, Treiber, "Der Treiber ist beschädigt oder fehlt.");
            E(40, "DRIVER_SERVICE_KEY_INVALID", Defekt, Treiber, "Der Dienst-Eintrag des Treibers ist beschädigt.");
            E(41, "LEGACY_SERVICE_NO_DEVICES", Gewollt, Keine, "Ein alter Dienst ohne zugehöriges Gerät.");
            E(42, "DUPLICATE_DEVICE", Transient, Keine, "Ein gleiches Gerät läuft bereits.");
            E(43, "FAILED_POST_START", Defekt, Hardware, "Der Treiber meldet einen Ausfall des Geräts.");
            E(44, "HALTED", Gewollt, Keine, "Von einer Anwendung oder einem Dienst angehalten.");
            E(45, "PHANTOM", Harmlos, Keine, "Nicht angeschlossen.");
            E(46, "SYSTEM_SHUTDOWN", Transient, Keine, "Windows fährt gerade herunter.");
            E(47, "HELD_FOR_EJECT", Harmlos, Keine, "Zum sicheren Entfernen vorbereitet.");
            E(48, "DRIVER_BLOCKED", Defekt, Treiber, "Der Treiber steht auf der Sperrliste von Windows.");
            E(49, "REGISTRY_TOO_LARGE", Defekt, Konfiguration, "Die Registrierung ist zu groß.");
            E(50, "SETPROPERTIES_FAILED", Defekt, Treiber, "Eigenschaften ließen sich nicht anwenden.");
            E(51, "WAITING_ON_DEPENDENCY", Gewollt, Keine, "Wartet auf ein anderes Gerät, das nicht gestartet ist.");
            E(52, "UNSIGNED_DRIVER", Defekt, Treiber, "Der Treiber ist nicht signiert.");
            E(53, "USED_BY_DEBUGGER", Gewollt, Keine, "Vom Kernel-Debugger belegt.");
            E(54, "DEVICE_RESET", Transient, Keine, "Wird gerade zurückgesetzt.");
            E(55, "CONSOLE_LOCKED", Gewollt, Keine, "Durch den DMA-Schutz bei gesperrtem Bildschirm blockiert.");
            E(56, "NEED_CLASS_CONFIG", Transient, Keine, "Die Klassenkonfiguration läuft noch.");
            E(57, "GUEST_ASSIGNMENT_FAILED", Gewollt, Keine, "Für eine virtuelle Maschine reserviert (Hyper-V-Gerätezuweisung), die Zuweisung schlug fehl.");
            return t;
        }

        public static Eintrag Von(int code)
        {
            Eintrag e;
            return Tabelle.TryGetValue(code, out e) ? e : null;
        }

        public static string Klasse(int code)
        {
            var e = Von(code);
            // Unbekannte Codes (kuenftige Windows-Versionen) nie als Defekt raten.
            return e == null ? Harmlos : e.Klasse;
        }
    }
}
