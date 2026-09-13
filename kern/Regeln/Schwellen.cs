namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Alle Schwellen an einer Stelle, jede mit Quelle. Was hier steht, ist nachrechenbar;
    /// was hier nicht steht, darf keine Regel als Grenze benutzen.
    ///
    /// "v7" = aus Version 7 uebernommen (bewaehrt, nie zurueckgenommen).
    /// "Doku" = Microsoft-Dokumentation, Fundstelle im Kommentar.
    /// "Wahl" = eigene Festlegung aus dem Konzept (Abschnitt 10), nicht dokumentiert.
    /// </summary>
    public static class Schwellen
    {
        // Speicherplatz (v7)
        public const double PlatzBadPct = 10;
        public const double PlatzBadGb = 10;
        public const double PlatzWarnPct = 20;

        // Datentraeger
        public const int WearWarn = 80;            // v7: Verschleiss ab 80 % ist eine Vorwarnung
        public const int NvmeUsedWarn = 90;        // Wahl; Doku: 100 = Lebensdauer verbraucht, Wert darf darueber liegen
        public const int NvmeUsedBad = 100;        // Doku NVME_HEALTH_INFO_LOG.PercentageUsed
        public const int DiskEreignisseTage = 30;  // Wahl
        public const int DiskEreignisseWarn = 1;   // Wahl: ein 7/11/51/153 in 30 Tagen
        public const int ResetEreignisseWarn = 3;  // Wahl: drei 129 (Reset) in 30 Tagen

        // Stabilitaet
        public const int EreignisTage = 90;         // Wahl (Konzept 3.3: "nur die abonnierten IDs, 90 Tage"); Modell und Sammler nehmen diesen Wert
        public const int BlauschirmWarn = 1;       // Wahl
        public const int BlauschirmBad = 2;        // Wahl
        public const int StromverlustTage = 30;    // Wahl
        public const int StromverlustWarn = 3;     // Wahl
        public const int ProgrammabsturzTage = 30; // Wahl
        public const int ProgrammabsturzWarn = 3;  // Wahl: dasselbe Programm dreimal
        public const int WheaFatalBad = 1;         // Wahl (Level 2 = schwerwiegend)
        public const int WheaKorrigierbarWarn = 10;// Wahl (Level 3 = behoben)
        public const int WheaKorrigierbarTage = 30;// Wahl (Konzept 4.3: "Level 3 >= 10 in 30 Tagen")
        public const int NeustartAbstand1074Min = 5; // Wahl: 1074 kurz vor 41 = geplanter Neustart, kein Absturz
        public const int KernelPnp219Tage = 30;    // Wahl (Konzept 4.3: dasselbe Geraet dreimal in 30 Tagen)
        public const int DefenderEngineAbsturzWarn = 2; // Konzept 4.3: "1000 mit MsMpEng.exe >= 2 -> warn, Massnahme Signaturen aktualisieren"
        public const int StromverlustAeltereTage = 90;  // Wahl: Abschaltungen ohne Herunterfahren zwischen 31 und 90 Tagen werden genannt, nicht bewertet

        // Updates
        public const int SicherheitsupdateWarnTage = 45; // Wahl
        public const int SicherheitsupdateBadTage = 90;  // Wahl
        public const int UpdateSchleifeMin = 3;          // Wahl: dieselbe Kennung dreimal erfolgreich in 30 Tagen
        public const int UpdateSchleifeTage = 30;
        public const int UpdateFehlschlagMin = 2;        // Wahl: dieselbe Kennung zweimal fehlgeschlagen
        public const int UpdateFehlschlagTage = 90;      // Wahl: Fehlschlaege zaehlen nur im Fenster des System-Protokolls (WindowsUpdateClient 20)
        public const int UpdateSucheWarnTage = 30;       // Wahl: Windows sucht von sich aus taeglich; 30 Tage ohne Erfolg sind ein Hinweis

        // Leistung (Doku: "Troubleshoot performance problems in Windows", learn.microsoft.com)
        public const double VerfuegbarWarnPct = 10;
        public const double VerfuegbarBadPct = 1;
        public const int VerfuegbarBadMb = 500;
        public const int CommitWarnPct = 60;
        public const int CommitBadPct = 80;
        public const int MessungenNoetig = 3;     // Doku: Spitzen unter einer Minute sind tolerierbar
        public const int MessabstandMinMs = 4000; // Wahl: Konzept 4.3 nannte 20 s, das haette den Lauf verdoppelt; drei Werte im Abstand von 4 s ueber den Lauf verteilt gelten als Serie, enger ist eine Momentaufnahme
        public const int AutostartsWarn = 15;     // Wahl: aktive Nicht-Microsoft-Autostarts

        // Sicherheit
        public const int SignaturAlterWarnTage = 7;   // Doku: Defender faellt ab 7 Tagen auf die alternative Quelle zurueck
        public const int SchnellscanAlterWarnTage = 14; // Wahl
        public const int NieWert = 65535;             // Doku MSFT_MpComputerStatus: 65535 = nie
        public const int FundSchwereWarn = 4;         // Doku MSFT_MpThreat.SeverityID: 0 Unknown, 1 Low, 2 Moderate, 4 High, 5 Severe (gemessen ValueMap 0..5); ab 4 = High und Severe
        public const int FundTage = 30;               // Wahl: ein beseitigter schwerer Fund bleibt 30 Tage lang eine Warnung
        public const int VirenschutzGesundheitGut = 0; // Doku WSC_SECURITY_PROVIDER_HEALTH: 0 GOOD, 1 NOTMONITORED, 2 POOR, 3 SNOOZE
        public const int VirenschutzGesundheitSchlecht = 2;

        // Hardware
        public const int KernelPnp219Warn = 3;    // Wahl: dasselbe Geraet dreimal in 30 Tagen
    }
}
