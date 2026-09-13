using System;
using System.Collections.Generic;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Ende der Sicherheitsupdates je Build UND Edition. Quelle: learn.microsoft.com
    /// "Windows 11 release information", "Windows 10 release information" und die
    /// Lifecycle-Seiten der LTSC-Editionen, Stand 2026-09-08. Es gibt keine lokale
    /// Schnittstelle dafuer; die Tabellen muessen mit jedem Release gepflegt werden.
    ///
    /// Vier Spalten, weil dieselbe Buildnummer je Edition ein anderes Ende hat (gemessen an
    /// der Microsoft-Tabelle: 22631 Home/Pro seit 2025-11-11 ohne Updates, Enterprise bis
    /// 2026-11-10; 26100 Home/Pro bis 2026-10-13, Enterprise bis 2027-10-12, Enterprise LTSC
    /// 2024 bis 2029-10-09 ohne erweiterten Support, IoT Enterprise LTSC 2024 bis 2034-10-10).
    /// Die Spalte kommt aus der EditionID der Registry (Positivliste, exakte Werte), nie aus
    /// dem lokalisierten Caption und nie aus CompositionEditionID.
    ///
    /// Bei Fixed-Lifecycle-Editionen (LTSC/LTSB) steht hier das Ende der Sicherheitsupdates,
    /// also das Ende des erweiterten Supports, wo es einen gibt - nicht das Ende des
    /// Mainstream-Supports.
    ///
    /// Unbekannte Builds und unbekannte Editionen sind "unbekannt", nie "ausser Support":
    /// Windows 11 26H1 (Build 28000) fehlte in jeder Tabelle vom September 2026, obwohl
    /// Neugeraete damit ausgeliefert werden (Widerlegungsrunde). Server-Editionen bewertet
    /// die Tabelle nicht (Konzept 4.4 nennt nur Client-Builds).
    /// </summary>
    public static class SupportEnde
    {
        public const string Stand = "2026-09-08";

        public const string SpalteHomePro = "Home/Pro";
        public const string SpalteEnterprise = "Enterprise/Education";
        public const string SpalteLtsc = "Enterprise LTSC";
        public const string SpalteIotLtsc = "IoT Enterprise LTSC";
        /// <summary>Server-Editionen: erkannt, aber ohne Tabelle (Ergebnis unbekannt, nie bad).</summary>
        public const string SpalteServer = "Server";

        // Home, Pro, Pro Education, Pro for Workstations, SE (Lifecycle-Seite "Windows 11 Home and Pro").
        static readonly Dictionary<int, DateTime> HomePro = new Dictionary<int, DateTime>
        {
            { 19044, new DateTime(2023, 6, 13) },    // Windows 10 21H2
            { 19045, new DateTime(2025, 10, 14) },   // Windows 10 22H2, alle Editionen
            { 22000, new DateTime(2023, 10, 10) },   // Windows 11 21H2
            { 22621, new DateTime(2024, 10, 8) },    // Windows 11 22H2
            { 22631, new DateTime(2025, 11, 11) },   // Windows 11 23H2
            { 26100, new DateTime(2026, 10, 13) },   // Windows 11 24H2 (letzte Version fuer SE)
            { 26200, new DateTime(2027, 10, 12) },   // Windows 11 25H2
            { 28000, new DateTime(2028, 3, 14) },    // Windows 11 26H1 (Neugeraete ab 2026-02)
        };

        // Enterprise, Education, IoT Enterprise, Enterprise multi-session.
        static readonly Dictionary<int, DateTime> Enterprise = new Dictionary<int, DateTime>
        {
            { 19044, new DateTime(2024, 6, 11) },    // Windows 10 21H2
            { 19045, new DateTime(2025, 10, 14) },   // Windows 10 22H2, alle Editionen
            { 22000, new DateTime(2024, 10, 8) },    // Windows 11 21H2
            { 22621, new DateTime(2025, 10, 14) },   // Windows 11 22H2
            { 22631, new DateTime(2026, 11, 10) },   // Windows 11 23H2
            { 26100, new DateTime(2027, 10, 12) },   // Windows 11 24H2
            { 26200, new DateTime(2028, 10, 10) },   // Windows 11 25H2
            { 28000, new DateTime(2029, 3, 13) },    // Windows 11 26H1 (IoT Enterprise dort nicht angeboten)
        };

        // Enterprise LTSC/LTSB (EditionID EnterpriseS): Ende der Sicherheitsupdates.
        static readonly Dictionary<int, DateTime> Ltsc = new Dictionary<int, DateTime>
        {
            { 10240, new DateTime(2025, 10, 14) },   // Windows 10 Enterprise 2015 LTSB
            { 14393, new DateTime(2026, 10, 13) },   // Windows 10 Enterprise 2016 LTSB (erweiterter Support)
            { 17763, new DateTime(2029, 1, 9) },     // Windows 10 Enterprise LTSC 2019 (erweiterter Support)
            { 19044, new DateTime(2027, 1, 12) },    // Windows 10 Enterprise LTSC 2021: kein erweiterter Support
            { 26100, new DateTime(2029, 10, 9) },    // Windows 11 Enterprise LTSC 2024: kein erweiterter Support
        };

        // IoT Enterprise LTSC/LTSB (EditionID IoTEnterpriseS): zehn Jahre.
        static readonly Dictionary<int, DateTime> IotLtsc = new Dictionary<int, DateTime>
        {
            { 10240, new DateTime(2025, 10, 14) },   // Windows 10 IoT Enterprise 2015 LTSB
            { 14393, new DateTime(2026, 10, 13) },   // Windows 10 IoT Enterprise 2016 LTSB
            { 17763, new DateTime(2029, 1, 9) },     // Windows 10 IoT Enterprise LTSC 2019
            { 19044, new DateTime(2032, 1, 13) },    // Windows 10 IoT Enterprise LTSC 2021
            { 26100, new DateTime(2034, 10, 10) },   // Windows 11 IoT Enterprise LTSC 2024
        };

        // Positivlisten der EditionID (HKLM\...\CurrentVersion\EditionID). Alles, was hier
        // nicht steht, bleibt unbekannt - eine geratene Spalte waere ein geratenes Urteil.
        static readonly string[] EditionenEnterprise =
        {
            "Enterprise", "EnterpriseN", "EnterpriseG", "EnterpriseGN", "Education", "EducationN",
            "IoTEnterprise", "IoTEnterpriseK", "EnterpriseMultiSession",
            "ServerRdsh",   // Windows 10 Enterprise multi-session traegt diese EditionID (und ProductType 3)
        };
        static readonly string[] EditionenLtsc = { "EnterpriseS", "EnterpriseSN" };
        static readonly string[] EditionenIotLtsc = { "IoTEnterpriseS", "IoTEnterpriseSK" };
        static readonly string[] PraefixeHomePro = { "Core", "Professional", "CloudEdition" };

        /// <summary>
        /// Die Spalte zu einer Edition; null = Edition unbekannt (leer, Eval, kuenftige Namen).
        /// SpalteServer heisst: erkannt, aber nicht bewertet.
        /// </summary>
        public static string Spalte(string editionId, int produktTyp)
        {
            string e = (editionId ?? "").Trim();
            if (e.Length > 0)
            {
                if (Array.Exists(EditionenEnterprise, x => x.Equals(e, StringComparison.OrdinalIgnoreCase))) return SpalteEnterprise;
                if (Array.Exists(EditionenLtsc, x => x.Equals(e, StringComparison.OrdinalIgnoreCase))) return SpalteLtsc;
                if (Array.Exists(EditionenIotLtsc, x => x.Equals(e, StringComparison.OrdinalIgnoreCase))) return SpalteIotLtsc;
                if (Array.Exists(PraefixeHomePro, x => e.StartsWith(x, StringComparison.OrdinalIgnoreCase))) return SpalteHomePro;
                if (e.StartsWith("Server", StringComparison.OrdinalIgnoreCase)) return SpalteServer;
            }
            // Win32_OperatingSystem.ProductType: 1 Workstation, 2 Domaenencontroller, 3 Server; 0 = nicht gelesen.
            if (produktTyp == 2 || produktTyp == 3) return SpalteServer;
            return null;
        }

        /// <summary>Ende der Sicherheitsupdates; null = Build in dieser Spalte unbekannt oder Spalte ohne Tabelle.</summary>
        public static DateTime? Fuer(int build, string spalte)
        {
            Dictionary<int, DateTime> t;
            switch (spalte)
            {
                case SpalteHomePro: t = HomePro; break;
                case SpalteEnterprise: t = Enterprise; break;
                case SpalteLtsc: t = Ltsc; break;
                case SpalteIotLtsc: t = IotLtsc; break;
                default: return null;
            }
            DateTime d;
            return t.TryGetValue(build, out d) ? d : (DateTime?)null;
        }

        /// <summary>
        /// Builds, fuer die Microsoft nach dem Support-Ende erweiterte Sicherheitsupdates (ESU)
        /// anbietet: Windows 10 22H2 fuer Verbraucher und Unternehmen. Das Enddatum steht hier
        /// bewusst nicht - es wurde schon einmal verschoben; der Rat nennt nur das Angebot.
        /// </summary>
        public static bool EsuAngebot(int build)
        {
            return build == 19045;
        }
    }
}
