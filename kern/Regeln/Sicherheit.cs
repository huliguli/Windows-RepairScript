using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Regeln fuer den Bereich Sicherheit: reine Funktion ueber dem Systembild. Kein
    /// Windows-Zugriff, kein DateTime.Now (ctx.Jetzt ist die Aufzeichnungszeit), jede Schwelle
    /// aus Schwellen.cs, jeder Zahlencode aus der Herstellerdokumentation.
    ///
    /// Haltung:
    ///   - Fehlende Daten sind "liess sich nicht pruefen" (Fehlend), nie ok und nie bad.
    ///   - Ein Fremd-Virenschutz macht einen passiven Defender zur Normalitaet, nicht zum Befund;
    ///     ob das Fremdprodukt wirkt, sagt die Gesundheitszahl des Sicherheitscenters (WSC),
    ///     und ein Defender im Normalmodus ist der Schutz, auch wenn daneben etwas registriert ist.
    ///   - Ausschluesse, Konten ohne Kennwortpflicht, RDP: nur melden. Nichts davon wird
    ///     "repariert", weil jede dieser Einstellungen eine Absicht sein kann.
    ///   - Der Laie liest Titel, Satz, Rat; Fachbegriffe stehen in Detail.
    ///
    /// Windows ausser Support meldet Hardware.cs (SupportEnde), nicht dieser Bereich.
    /// </summary>
    public static class Sicherheit
    {
        // Zahlencodes aus der Dokumentation - keine Schwellen, sondern Bedeutungen.
        const int KategoriePua = 27;          // MSFT_MpThreat.CategoryID: POTENTIALUNWANTEDSOFTWARE
        const int RidAdministrator = 500;     // eingebauter Administrator (SID-RID, sprachneutral)
        const int RidGast = 501;              // eingebautes Gastkonto ("Gast" ist lokalisiert)
        const int EingehendErlauben = 1;      // NET_FW_ACTION_ALLOW (INetFwPolicy2.DefaultInboundAction)
        const int Smb1Aktiv = 1;              // Win32_OptionalFeature.InstallState: 1 Enabled
        const int GesundheitNichtUeberwacht = 1; // WSC_SECURITY_PROVIDER_HEALTH_NOTMONITORED (Doku wscapi.h)
        const int GesundheitSchlummert = 3;   // WSC_SECURITY_PROVIDER_HEALTH_SNOOZE
        const string ModusNormal = "Normal";  // MSFT_MpComputerStatus.AMRunningMode: feste englische Marke (Doku: Normal, Passive, EDR Block Mode)
        const string RdpAusRichtlinie = "richtlinie"; // Sicherheit.RdpHerkunft
        const string QuelleWsc = "WscGetSecurityProviderHealth(WSC_SECURITY_PROVIDER_ANTIVIRUS)";
        const string EinheitWsc = "WSC_SECURITY_PROVIDER_HEALTH";

        public const string MassnahmeSignaturen = "sicherheit.signaturen.aktualisieren";
        public const string MassnahmeSchnellscan = "sicherheit.schnellscan";
        public const string MassnahmeFirewall = "sicherheit.firewall.einschalten";
        public const string MassnahmeUac = "sicherheit.uac.standard";
        public const string MassnahmeSmartScreen = "sicherheit.smartscreen.warn";

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var si = s.Sicherheit;
            var e = new BereichErgebnis { Bereich = Bereich.Sicherheit };
            e.DatenVorhanden = si.Defender != null || si.DrittAv.Count > 0 || si.Firewall.Count > 0 || si.VirenschutzGesundheit.HasValue;

            Virenschutz(ctx, si, e);
            Signaturen(ctx, si, e);
            Schnellscan(ctx, si, e);
            Funde(ctx, si, e);
            Ausschluesse(ctx, si, e);
            Firewall(ctx, si, e);
            Uac(ctx, si, e);
            SmartScreen(ctx, si, e);
            SecureBoot(ctx, s, e);
            Konten(ctx, si, e);
            Rdp(ctx, s, e);
            Smb1(ctx, si, e);
            return e;
        }

        // ------------------------------------------------------------------ Virenschutz

        /// <summary>
        /// Defender schuetzt selbst, wenn AMRunningMode "Normal" sagt (Doku: Normal, Passive,
        /// EDR Block Mode). Im passiven Modus kann RealTimeProtectionEnabled trotzdem true melden
        /// (Doku-Note zu Endpoint DLP), deshalb entscheidet der Modus; fehlt er, Dienst und Echtzeit.
        /// </summary>
        static bool DefenderLaeuftNormal(Defender d)
        {
            if (d == null) return false;
            if (d.Modus != null) return d.Modus == ModusNormal;
            return d.DienstAn && d.Echtzeit;
        }

        /// <summary>Signaturen und Scans werden gegen Defender geprueft, wenn er der Schutz ist: kein Fremdprodukt, oder er laeuft trotz Fremdprodukt im Normalmodus.</summary>
        static bool DefenderIstSchutz(Kern.Sicherheit si)
        {
            return si.Defender != null && (si.DrittAv.Count == 0 || DefenderLaeuftNormal(si.Defender));
        }

        static string GesundheitText(int g)
        {
            switch (g)
            {
                case Schwellen.VirenschutzGesundheitGut: return "0 GOOD";
                case GesundheitNichtUeberwacht: return "1 NOTMONITORED";
                case Schwellen.VirenschutzGesundheitSchlecht: return "2 POOR";
                case GesundheitSchlummert: return "3 SNOOZE";
                default: return g + " (undokumentiert)";
            }
        }

        static void Virenschutz(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            var d = si.Defender;
            int? g = si.VirenschutzGesundheit;
            bool schlecht = g == Schwellen.VirenschutzGesundheitSchlecht;
            if (d == null && si.DrittAv.Count == 0)
            {
                if (schlecht)
                {
                    // Defender per Richtlinie abgeschaltet (keine MSFT_MpComputerStatus-Instanz) und
                    // nichts anderes registriert: das Sicherheitscenter sagt POOR, das ist belegt.
                    var b = Neu(e, "sicherheit.virenschutz.keiner", Zustand.Bad,
                        "Kein wirksamer Virenschutz",
                        "Das Sicherheitscenter von Windows stuft den Virenschutz als unzureichend ein; Windows Defender meldet keinen Status, und es ist kein anderes Schutzprogramm registriert.",
                        "Öffnen Sie „Windows-Sicherheit“ und schalten Sie unter „Viren- und Bedrohungsschutz“ den Schutz ein; ist Defender per Richtlinie abgeschaltet, muss die Richtlinie weg oder ein anderes Schutzprogramm her.",
                        QuelleWsc, Messwert.Von(g.Value, EinheitWsc, "0 = GOOD"));
                    b.Detail.Add("WSC_SECURITY_PROVIDER_HEALTH: " + GesundheitText(g.Value) + " (0 GOOD, 1 NOTMONITORED, 2 POOR, 3 SNOOZE, Doku wscapi.h).");
                    b.Detail.Add("MSFT_MpComputerStatus nicht lesbar, SecurityCenter2 ohne Fremdprodukt.");
                    return;
                }
                e.Fehlend.Add("Schutzstatus nicht lesbar");
                return;
            }
            if (si.DrittAv.Count > 0) { Fremdschutz(ctx, si, e); return; }
            // Defender ist der Schutz.
            if (!d.DienstAn || !d.Echtzeit)
            {
                var b = Neu(e, "sicherheit.virenschutz.aus", Zustand.Bad,
                    "Der Virenschutz ist ausgeschaltet",
                    !d.DienstAn
                        ? "Der Dienst von Windows Defender läuft nicht, und es ist kein anderer Virenschutz registriert."
                        : "Der Echtzeitschutz von Windows Defender ist aus, und es ist kein anderer Virenschutz registriert.",
                    "Öffnen Sie „Windows-Sicherheit“ und schalten Sie unter „Viren- und Bedrohungsschutz“ den Echtzeitschutz ein.",
                    !d.DienstAn ? "MSFT_MpComputerStatus.AMServiceEnabled" : "MSFT_MpComputerStatus.RealTimeProtectionEnabled",
                    Messwert.Von(0, !d.DienstAn ? "AMServiceEnabled" : "RealTimeProtectionEnabled", "1"));
                b.Detail.Add("AMRunningMode: " + (d.Modus ?? "unbekannt") + ", IsTamperProtected: " + d.Tamper + ", BehaviorMonitorEnabled: " + d.Verhalten);
                if (g.HasValue) b.Detail.Add("Sicherheitscenter (WSC): " + GesundheitText(g.Value));
                b.Detail.Add("Keine automatische Maßnahme: unter Manipulationsschutz (Tamper Protection) scheinen Änderungen per API zu gelingen, wirken aber nicht.");
                return;
            }
            var ok = Neu(e, "sicherheit.virenschutz.an", Zustand.Ok,
                "Der Virenschutz ist eingeschaltet",
                d.SignaturAlterTage >= 0 && d.SignaturAlterTage != Schwellen.NieWert
                    ? "Windows Defender läuft mit Echtzeitschutz, die Virensignaturen sind " + TageText(d.SignaturAlterTage) + " alt."
                    : "Windows Defender läuft mit Echtzeitschutz; das Alter der Signaturen ist nicht bekannt.",
                null, "MSFT_MpComputerStatus.RealTimeProtectionEnabled",
                Messwert.Von(1, "RealTimeProtectionEnabled", "1"));
            ok.Detail.Add("AMRunningMode: " + (d.Modus ?? "unbekannt") + ", BehaviorMonitorEnabled: " + d.Verhalten + ", IsTamperProtected: " + d.Tamper);
            if (g.HasValue) ok.Detail.Add("Sicherheitscenter (WSC): " + GesundheitText(g.Value));
            if (schlecht)
            {
                // Defender sagt "an", das Sicherheitscenter sagt POOR: meist veraltete Signaturen
                // (die Signaturregel meldet das mit Zahl) oder ein abgeschalteter Baustein. Warn,
                // nicht bad - Defenders eigene Zahlen widersprechen, und die stehen daneben.
                var w = Neu(e, "sicherheit.virenschutz.gesundheit", Zustand.Warn,
                    "Das Sicherheitscenter meldet den Virenschutz als unzureichend",
                    "Windows Defender meldet Echtzeitschutz an, aber das Sicherheitscenter von Windows stuft den Virenschutz als unzureichend ein; meist sind die Signaturen veraltet oder ein Schutzbaustein ist nicht aktiv.",
                    "Öffnen Sie „Windows-Sicherheit“ und folgen Sie der dort angezeigten Meldung.",
                    QuelleWsc, Messwert.Von(g.Value, EinheitWsc, "0 = GOOD"));
                w.Detail.Add("RealTimeProtectionEnabled: true, AMServiceEnabled: true, AntivirusSignatureAge: " + d.SignaturAlterTage + " Tage.");
            }
        }

        /// <summary>
        /// Ein Fremdprodukt ist in SecurityCenter2 registriert. Drei Wege: das Sicherheitscenter
        /// sagt POOR (abgelaufen oder abgeschaltet, bad) oder SNOOZE (warn); Defender laeuft im
        /// Normalmodus und ist der Schutz (ok, Signaturen und Scans werden gegen ihn geprueft);
        /// Defender ist passiv und das Fremdprodukt ist der Schutz - ok nur bei GOOD, sonst
        /// "nicht messbar". productState wird nie gedeutet.
        /// </summary>
        static void Fremdschutz(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            var d = si.Defender;
            int? g = si.VirenschutzGesundheit;
            string namen = string.Join(", ", si.DrittAv.Select(n => "„" + n + "“"));
            string wer = si.DrittAv.Count == 1 ? namen : si.DrittAv.Count + " Schutzprogramme (" + namen + ")";
            bool normal = DefenderLaeuftNormal(d);
            Befund b;
            if (g == Schwellen.VirenschutzGesundheitSchlecht)
            {
                b = Neu(e, "sicherheit.virenschutz.fremd.aus", Zustand.Bad,
                    "Der Virenschutz ist abgeschaltet oder abgelaufen",
                    wer + " ist als Virenschutz registriert, aber das Sicherheitscenter von Windows stuft den Virenschutz als unzureichend ein; das Programm ist abgeschaltet oder die Lizenz abgelaufen"
                        + (normal ? "; solange prüft Windows Defender im Normalmodus mit, nicht das registrierte Programm." : "; der PC läuft ohne wirksamen Echtzeitschutz."),
                    "Das Programm verlängern oder einschalten; wird es nicht mehr gebraucht, deinstallieren, dann übernimmt Windows Defender den Schutz von selbst.",
                    QuelleWsc, Messwert.Von(g.Value, EinheitWsc, "0 = GOOD"));
            }
            else if (g == GesundheitSchlummert)
            {
                b = Neu(e, "sicherheit.virenschutz.fremd.pausiert", Zustand.Warn,
                    "Der Virenschutz ist vorübergehend ausgeschaltet",
                    wer + " ist als Virenschutz registriert, aber im Sicherheitscenter von Windows auf „Schlummern“ gestellt"
                        + (normal ? "; solange prüft nur Windows Defender im Normalmodus mit, nicht das registrierte Programm." : "; bis es wieder eingeschaltet wird, läuft der PC ohne Echtzeitschutz."),
                    "Das Schutzprogramm wieder einschalten, wenn die Pause nicht mehr gebraucht wird.",
                    QuelleWsc, Messwert.Von(g.Value, EinheitWsc, "0 = GOOD"));
            }
            else if (normal)
            {
                b = Neu(e, "sicherheit.virenschutz.fremd", Zustand.Ok,
                    "Virenschutz eines anderen Herstellers",
                    wer + " ist registriert, aber Windows Defender läuft trotzdem im Normalmodus mit Echtzeitschutz und prüft mit; laut Microsoft schaltet er sich wieder ein, wenn das andere Produkt abgelaufen ist; prüfen Sie, ob " + wer + " noch gültig ist.",
                    null, "MSFT_MpComputerStatus.AMRunningMode",
                    Messwert.Von(d.Modus ?? "fehlt", "AMRunningMode", ModusNormal));
                b.Detail.Add("Signaturen und Scans werden gegen Windows Defender geprüft, weil er im Normalmodus der Schutz ist (Doku: Microsoft Defender Antivirus compatibility).");
            }
            else if (g == Schwellen.VirenschutzGesundheitGut)
            {
                b = Neu(e, "sicherheit.virenschutz.fremd", Zustand.Ok,
                    "Virenschutz eines anderen Herstellers",
                    si.DrittAv.Count == 1
                        ? namen + " ist als Virenschutz registriert und laut Sicherheitscenter in Ordnung, Windows Defender prüft deshalb nicht mit."
                        : si.DrittAv.Count + " Schutzprogramme sind registriert (" + namen + ") und laut Sicherheitscenter in Ordnung, Windows Defender prüft deshalb nicht mit.",
                    null, "SecurityCenter2.AntiVirusProduct.displayName",
                    Messwert.Von(si.DrittAv.Count, "Produkte", null));
                if (d != null) b.Detail.Add("Bei registriertem Fremdschutz ist der passive Defender vorgesehen (Doku: Microsoft Defender Antivirus compatibility).");
            }
            else
            {
                // Gesundheit nicht gemessen (kein wscsvc, Zeit) oder NOTMONITORED: der Fremdschutz
                // bleibt sichtbar, aber ohne Urteil - ok waere erfunden, bad auch.
                b = Neu(e, "sicherheit.virenschutz.fremd", Zustand.Unknown,
                    "Virenschutz eines anderen Herstellers",
                    wer + " ist als Virenschutz registriert, Windows Defender prüft deshalb nicht mit; ob das Programm aktiv und aktuell ist, ließ sich nicht messen.",
                    "Öffnen Sie „Windows-Sicherheit“ und prüfen Sie, ob der Virenschutz dort grün ist.",
                    "SecurityCenter2.AntiVirusProduct.displayName",
                    Messwert.Von(si.DrittAv.Count, "Produkte", null));
                e.Fehlend.Add("Zustand des Fremd-Virenschutzes (" + (g == GesundheitNichtUeberwacht ? "vom Sicherheitscenter nicht überwacht" : Grund(ctx.S, "api.wsc.antivirus")) + ")");
            }
            if (d != null)
                b.Detail.Add("Defender AMRunningMode: " + (d.Modus ?? "unbekannt") + ", RealTimeProtectionEnabled: " + d.Echtzeit + ", AMServiceEnabled: " + d.DienstAn);
            b.Detail.Add("Sicherheitscenter (WSC): " + (g.HasValue ? GesundheitText(g.Value) : "nicht gemessen") + "; der Zustand des Fremdprodukts (productState) ist nicht dokumentiert und wird nicht gedeutet.");
        }

        static void Signaturen(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (!DefenderIstSchutz(si)) return;
            var d = si.Defender;
            if (d.SignaturAlterTage < 0) { e.Fehlend.Add("Alter der Virensignaturen"); return; }
            if (d.SignaturAlterTage == Schwellen.NieWert)
            {
                var b = Neu(e, "sicherheit.signaturen.nie", Zustand.Bad,
                    "Die Virensignaturen wurden noch nie aktualisiert",
                    "Windows Defender hat noch nie Virensignaturen geladen; ohne sie erkennt er keine aktuellen Schädlinge.",
                    "Signaturen aktualisieren, dann prüfen, ob der PC Windows-Update erreicht.",
                    "MSFT_MpComputerStatus.AntivirusSignatureAge",
                    Messwert.Von(d.SignaturAlterTage, "Tage", "65535 = nie (Doku)"));
                b.Massnahmen.Add(MassnahmeSignaturen);
                return;
            }
            if (d.SignaturAlterTage > Schwellen.SignaturAlterWarnTage)
            {
                var b = Neu(e, "sicherheit.signaturen.alt", Zustand.Warn,
                    "Die Virensignaturen sind veraltet",
                    "Die Virensignaturen sind " + TageText(d.SignaturAlterTage) + " alt; ab " + Schwellen.SignaturAlterWarnTage + " Tagen gilt der Schutz als lückenhaft.",
                    "Signaturen aktualisieren; klappt das nicht, prüfen Sie die Internetverbindung und Windows-Update.",
                    "MSFT_MpComputerStatus.AntivirusSignatureAge",
                    Messwert.Von(d.SignaturAlterTage, "Tage", "> " + Schwellen.SignaturAlterWarnTage));
                b.Detail.Add("Doku: Defender fällt ab 7 Tagen ohne Update auf die alternative Signaturquelle zurück.");
                b.Massnahmen.Add(MassnahmeSignaturen);
            }
        }

        static void Schnellscan(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (!DefenderIstSchutz(si)) return;
            var d = si.Defender;
            if (d.SchnellscanAlterTage < 0) { e.Fehlend.Add("Alter des letzten Schnellscans"); return; }
            if (d.SchnellscanAlterTage <= Schwellen.SchnellscanAlterWarnTage) return;
            // Ein frischer Vollscan deckt mehr ab als ein Schnellscan: dann kein Befund.
            if (d.VollscanAlterTage >= 0 && d.VollscanAlterTage != Schwellen.NieWert && d.VollscanAlterTage <= Schwellen.SchnellscanAlterWarnTage) return;
            bool nie = d.SchnellscanAlterTage == Schwellen.NieWert;
            var b = Neu(e, "sicherheit.schnellscan.alt", Zustand.Warn,
                nie ? "Noch kein Schnellscan" : "Der letzte Schnellscan liegt lange zurück",
                nie ? "Windows Defender hat noch nie einen Schnellscan ausgeführt; ohne Scan bleibt ein Fund unentdeckt, bis die Datei geöffnet wird."
                    : "Der letzte Schnellscan ist " + TageText(d.SchnellscanAlterTage) + " her; ab " + Schwellen.SchnellscanAlterWarnTage + " Tagen ist ein neuer fällig.",
                "Einen Schnellscan starten (dauert meist wenige Minuten).",
                "MSFT_MpComputerStatus.QuickScanAge",
                Messwert.Von(d.SchnellscanAlterTage, "Tage", "> " + Schwellen.SchnellscanAlterWarnTage));
            if (d.VollscanAlterTage >= 0 && d.VollscanAlterTage != Schwellen.NieWert) b.Detail.Add("FullScanAge: " + d.VollscanAlterTage + " Tage");
            b.Massnahmen.Add(MassnahmeSchnellscan);
        }

        // ------------------------------------------------------------------ Funde

        static void Funde(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            var d = si.Defender;
            if (d == null) return;
            foreach (var f in d.Funde)
            {
                string name = string.IsNullOrEmpty(f.Name) ? "unbekannt" : f.Name;
                double? her = f.ZeitUtc != null ? ctx.TageSeit(f.ZeitUtc) : null;
                string wann = her.HasValue && her.Value >= 0 ? " " + Text.Vor(her.Value) : "";
                if (f.Aktiv)
                {
                    var b = Neu(e, "sicherheit.fund.aktiv." + name, Zustand.Bad,
                        "Ein Schädling ist noch aktiv",
                        "Windows Defender meldet „" + name + "“ als aktiven Fund" + wann + "; die Bedrohung ist noch nicht beseitigt.",
                        "Öffnen Sie „Windows-Sicherheit“ > „Schutzverlauf“ und lassen Sie den Fund entfernen; danach einen vollständigen Scan ausführen.",
                        "MSFT_MpThreat.IsActive",
                        Messwert.Von(f.Schwere, "SeverityID (0 bis 5)", null));
                    b.Detail.Add("ThreatName: " + name + ", SeverityID: " + f.Schwere + ", CategoryID: " + f.Kategorie + (f.ZeitUtc != null ? ", InitialDetectionTime: " + f.ZeitUtc : ""));
                    continue;
                }
                if (f.Kategorie == KategoriePua)
                {
                    // Nur Hinweis, und nur aus den letzten FundTage Tagen: der Defender-Katalog
                    // behaelt jeden je gesehenen Fund (live: fuenf PUA-Eintraege, der aelteste
                    // 385 Tage alt). Ohne Zeitstempel bleibt der Hinweis stehen.
                    if (f.ZeitUtc != null && !ctx.Innerhalb(f.ZeitUtc, Schwellen.FundTage)) continue;
                    var b = Neu(e, "sicherheit.fund.pua." + name, Zustand.Ok,
                        "Unerwünschte Software wurde abgewehrt",
                        "„" + name + "“ (möglicherweise unerwünschte Software) wurde" + wann + " gefunden und ist nicht mehr aktiv.",
                        null, "MSFT_MpThreat.CategoryID",
                        Messwert.Von(f.Kategorie, "CategoryID", "27 = PUA (Doku)"));
                    b.Detail.Add("SeverityID: " + f.Schwere + (f.ZeitUtc != null ? ", InitialDetectionTime: " + f.ZeitUtc : ""));
                    continue;
                }
                if (f.Schwere >= Schwellen.FundSchwereWarn && f.ZeitUtc != null && ctx.Innerhalb(f.ZeitUtc, Schwellen.FundTage))
                {
                    var b = Neu(e, "sicherheit.fund.schwer." + name, Zustand.Warn,
                        "Ein schwerer Fund wurde kürzlich beseitigt",
                        "„" + name + "“ (Schwere " + f.Schwere + " von 5) wurde" + wann + " gefunden und beseitigt; in den letzten " + Schwellen.FundTage + " Tagen zählt das noch als Warnung.",
                        "Ein vollständiger Scan gibt Gewissheit, dass nichts zurückgeblieben ist.",
                        "MSFT_MpThreat.SeverityID",
                        Messwert.Von(f.Schwere, "SeverityID", ">= " + Schwellen.FundSchwereWarn + " in " + Schwellen.FundTage + " Tagen"));
                    b.Detail.Add("CategoryID: " + f.Kategorie + ", InitialDetectionTime: " + f.ZeitUtc);
                }
            }
        }

        // ------------------------------------------------------------------ Ausschluesse

        static readonly Regex GanzesLaufwerk = new Regex(@"^[A-Za-z]:(\\\*?|\\?)?$", RegexOptions.Compiled);
        static readonly Regex TempOrdner = new Regex(@"(^%TE?MP%|\\AppData\\Local\\Temp(\\|\\\*)?$|\\Windows\\Temp(\\|\\\*)?$|\\Temp(\\|\\\*)?$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex Downloads = new Regex(@"\\Downloads(\\|\\\*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static void Ausschluesse(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            var d = si.Defender;
            if (d == null) return;
            if (!d.AusschluesseSichtbar)
            {
                // Der Grund steht in der Fehlerliste: nicht erhoeht "zugriff" (Sentinel), erhoeht
                // "zugriff" (HideExclusionsFromLocalAdmins), sonst Zeit oder fehlende Klasse.
                var f = ctx.S.FehlerVon("wmi.defender.exclusions").FirstOrDefault();
                string grund = f == null ? "nicht gelesen"
                    : f.Art == Fehler.Zugriff ? (ctx.S.Erhoeht ? "durch Richtlinie verborgen" : "braucht Administratorrechte")
                    : Grund(ctx.S, "wmi.defender.exclusions");
                e.Fehlend.Add("Ausnahmen des Virenschutzes (" + grund + ")");
                return;
            }
            foreach (string p in d.AusschlussPfade)
            {
                string pfad = (p ?? "").Trim();
                if (pfad.Length == 0) continue;
                if (GanzesLaufwerk.IsMatch(pfad))
                {
                    string lw = pfad.Substring(0, 1).ToUpperInvariant();
                    var b = Neu(e, "sicherheit.ausschluss.laufwerk." + lw, Zustand.Warn,
                        "Ein ganzes Laufwerk ist vom Virenschutz ausgenommen",
                        "Laufwerk " + lw + ": wird nicht mehr geprüft; ein Schädling dort bleibt unentdeckt.",
                        "Wenn das nicht gewollt ist: „Windows-Sicherheit“ > „Viren- und Bedrohungsschutz“ > „Ausschlüsse“ und den Eintrag entfernen.",
                        "MSFT_MpPreference.ExclusionPath", Messwert.Von(pfad, "Pfad", null));
                    b.Detail.Add("Nur gemeldet, nie entfernt: ein Ausschluss kann Absicht sein (Entwicklung, Spiele, Datensicherung).");
                    continue;
                }
                if (TempOrdner.IsMatch(pfad))
                {
                    var b = Neu(e, "sicherheit.ausschluss.temp." + pfad, Zustand.Warn,
                        "Ein Temp-Ordner ist vom Virenschutz ausgenommen",
                        "„" + pfad + "“ wird nicht geprüft; genau dort landen heruntergeladene Installer und Schadcode zuerst.",
                        "Wenn das nicht gewollt ist: den Ausschluss in „Windows-Sicherheit“ entfernen.",
                        "MSFT_MpPreference.ExclusionPath", Messwert.Von(pfad, "Pfad", null));
                    b.Detail.Add("Nur gemeldet, nie entfernt.");
                    continue;
                }
                if (Downloads.IsMatch(pfad))
                {
                    var b = Neu(e, "sicherheit.ausschluss.downloads." + pfad, Zustand.Warn,
                        "Der Download-Ordner ist vom Virenschutz ausgenommen",
                        "„" + pfad + "“ wird nicht geprüft; heruntergeladene Dateien bleiben ohne Kontrolle.",
                        "Wenn das nicht gewollt ist: den Ausschluss in „Windows-Sicherheit“ entfernen.",
                        "MSFT_MpPreference.ExclusionPath", Messwert.Von(pfad, "Pfad", null));
                    b.Detail.Add("Nur gemeldet, nie entfernt.");
                }
            }
            foreach (string x in d.AusschlussEndungen)
            {
                string endung = (x ?? "").Trim().TrimStart('.', '*').ToLowerInvariant();
                if (endung != "exe" && endung != "dll") continue;
                var b = Neu(e, "sicherheit.ausschluss.endung." + endung, Zustand.Warn,
                    "Programmdateien sind vom Virenschutz ausgenommen",
                    string.Format("Alle Dateien mit der Endung „.{0}“ werden nicht geprüft; damit ist der Schutz vor Programmen praktisch aus.", endung),
                    "Diesen Ausschluss in „Windows-Sicherheit“ entfernen, falls er nicht bewusst gesetzt wurde.",
                    "MSFT_MpPreference.ExclusionExtension", Messwert.Von(x, "Endung", null));
                b.Detail.Add("Nur gemeldet, nie entfernt.");
            }
        }

        // ------------------------------------------------------------------ Firewall

        static string ProfilName(int p)
        {
            switch (p)
            {
                case 1: return "Domäne";
                case 2: return "Privat";
                case 4: return "Öffentlich";
                default: return "Profil " + p;
            }
        }

        static void Firewall(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (si.Firewall.Count == 0) { e.Fehlend.Add("Firewall-Status"); return; }
            bool alleAn = true;
            foreach (var p in si.Firewall)
            {
                if (!p.An)
                {
                    alleAn = false;
                    // Eingehend null heisst: der Sammler ist auf MSFT_NetFirewallProfile zurueckgefallen.
                    bool wirksam = p.Eingehend.HasValue;
                    var b = Neu(e, "sicherheit.firewall.aus." + p.Profil, Zustand.Warn,
                        "Die Firewall ist für das Profil „" + ProfilName(p.Profil) + "“ aus",
                        "Im Netzwerkprofil „" + ProfilName(p.Profil) + "“ ist die Windows-Firewall ausgeschaltet; in diesem Netz ist der PC ohne Schutz erreichbar.",
                        "Die Firewall für dieses Profil einschalten.",
                        wirksam ? "INetFwPolicy2.FirewallEnabled[" + p.Profil + "]" : "MSFT_NetFirewallProfile.Enabled",
                        Messwert.Von(0, wirksam ? "FirewallEnabled" : "Enabled", "1"));
                    if (!wirksam) b.Detail.Add("Gespeicherter Zustand aus MSFT_NetFirewallProfile.Enabled (Rückfall), nicht der wirksame.");
                    b.Massnahmen.Add(MassnahmeFirewall);
                }
                if (p.Eingehend.HasValue && p.Eingehend.Value == EingehendErlauben)
                {
                    var b = Neu(e, "sicherheit.firewall.eingehend." + p.Profil, Zustand.Warn,
                        "Die Firewall lässt im Profil „" + ProfilName(p.Profil) + "“ alles herein",
                        "Für „" + ProfilName(p.Profil) + "“ steht die Standardaktion für eingehende Verbindungen auf „Zulassen“ statt „Blockieren“; jede Verbindung von außen kommt ohne Regel durch.",
                        "In „Windows-Sicherheit“ > „Firewall“ die eingehenden Verbindungen für dieses Profil wieder blockieren.",
                        "INetFwPolicy2.DefaultInboundAction[" + p.Profil + "]", Messwert.Von(p.Eingehend.Value, "NET_FW_ACTION", "0 = Block"));
                    b.Detail.Add("NET_FW_ACTION: 0 Block, 1 Allow (Doku INetFwPolicy2).");
                }
            }
            if (alleAn)
            {
                bool eingehendBekannt = si.Firewall.All(p => p.Eingehend.HasValue);
                var b = Neu(e, "sicherheit.firewall.an", Zustand.Ok,
                    "Die Firewall ist eingeschaltet",
                    "Die Windows-Firewall ist in allen " + si.Firewall.Count + " Netzwerkprofilen an" + (eingehendBekannt ? " und blockiert eingehende Verbindungen" : "") + ".",
                    null, eingehendBekannt ? "INetFwPolicy2.FirewallEnabled" : "MSFT_NetFirewallProfile.Enabled",
                    Messwert.Von(si.Firewall.Count, "Profile an", si.Firewall.Count.ToString()));
                if (!eingehendBekannt) b.Detail.Add("Standardaktion eingehend nicht gelesen (Rückfall auf den gespeicherten Zustand, nicht den wirksamen).");
            }
        }

        // ------------------------------------------------------------------ UAC, SmartScreen, Secure Boot

        static void Uac(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (si.EnableLua == null && si.ConsentAdmin == null) { e.Fehlend.Add("Benutzerkontensteuerung"); return; }
            bool luaAus = si.EnableLua.HasValue && si.EnableLua.Value == 0;
            bool ohneNachfrage = si.ConsentAdmin.HasValue && si.ConsentAdmin.Value == 0;
            if (!luaAus && !ohneNachfrage) return;
            var b = Neu(e, "sicherheit.uac.aus", Zustand.Bad,
                "Die Benutzerkontensteuerung ist abgeschaltet",
                luaAus
                    ? "Die Benutzerkontensteuerung (UAC) ist ganz aus; jedes Programm bekommt ohne Nachfrage volle Rechte."
                    : "Die Benutzerkontensteuerung erhöht Rechte ohne Nachfrage; jedes Programm bekommt volle Rechte, ohne dass Sie es sehen.",
                "Die Benutzerkontensteuerung auf den Standard zurücksetzen" + (luaAus ? "; danach ist ein Neustart nötig." : "; die Nachfragestufe wirkt sofort."),
                luaAus ? "Policies\\System.EnableLUA" : "Policies\\System.ConsentPromptBehaviorAdmin",
                Messwert.Von(luaAus ? si.EnableLua.Value : si.ConsentAdmin.Value, luaAus ? "EnableLUA" : "ConsentPromptBehaviorAdmin", luaAus ? "1" : "5 (Standard)"));
            b.Detail.Add("EnableLUA: " + Wert(si.EnableLua) + ", ConsentPromptBehaviorAdmin: " + Wert(si.ConsentAdmin) + ", PromptOnSecureDesktop: " + Wert(si.SecureDesktop));
            if (luaAus) b.Detail.Add("Neustart nötig: EnableLUA wirkt erst nach dem nächsten Start.");
            b.Massnahmen.Add(MassnahmeUac);
        }

        static void SmartScreen(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (si.SmartScreen == null) { e.Fehlend.Add("SmartScreen"); return; }
            if (si.SmartScreen != "Off") return;
            var b = Neu(e, "sicherheit.smartscreen.aus", Zustand.Warn,
                "SmartScreen ist ausgeschaltet",
                "Der Download- und App-Filter SmartScreen ist aus; unbekannte Programme starten ohne Warnung.",
                "SmartScreen wieder auf „Warnen“ stellen.",
                "Explorer.SmartScreenEnabled", Messwert.Von(si.SmartScreen, null, "Warn"));
            b.Detail.Add("Feste Marken: Warn, Block, Off (Registry Explorer\\SmartScreenEnabled bzw. Richtlinie EnableSmartScreen).");
            b.Massnahmen.Add(MassnahmeSmartScreen);
        }

        static void SecureBoot(Kontext ctx, Systembild s, BereichErgebnis e)
        {
            var si = s.Sicherheit;
            // Auf UEFI muss der Schluessel da sein; fehlt er dort, ist das "nicht geprueft". Auf
            // Legacy-BIOS (Uefi false) oder ohne Firmware-Angabe gibt es nichts zu pruefen.
            if (si.SecureBoot == null) { if (s.Hardware.Uefi == true) e.Fehlend.Add("Secure Boot"); return; }
            if (si.SecureBoot.Value) return;
            if (s.Hardware.Uefi != true) return;
            var b = Neu(e, "sicherheit.secureboot.aus", Zustand.Ok,
                "Secure Boot ist ausgeschaltet",
                "Der PC startet per UEFI, aber ohne Secure Boot; das kann gewollt sein (zweites Betriebssystem, ältere Hardware) und ist kein Fehler.",
                "Wenn es nicht gewollt ist, lässt sich Secure Boot in den Firmware-Einstellungen (BIOS/UEFI) einschalten.",
                "SecureBoot\\State.UEFISecureBootEnabled", Messwert.Von(0, "UEFISecureBootEnabled", null));
            b.Detail.Add("Hardware.Uefi: true (GetFirmwareType). Auf Legacy-BIOS fehlt der Schlüssel und es gibt keinen Befund.");
        }

        // ------------------------------------------------------------------ Konten, RDP, SMB1

        static void Konten(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (si.Konten.Count == 0) { e.Fehlend.Add("Benutzerkonten"); return; }
            foreach (var k in si.Konten)
            {
                string name = string.IsNullOrEmpty(k.Name) ? "RID " + k.Rid : k.Name;
                if (k.Rid == RidAdministrator)
                {
                    if (k.Deaktiviert) continue;
                    var b = Neu(e, "sicherheit.konto.administrator.aktiv", Zustand.Warn,
                        "Das eingebaute Administratorkonto ist eingeschaltet",
                        "Das eingebaute Konto „" + name + "“ ist aktiv; Windows liefert es abgeschaltet aus, weil es ohne Nachfrage volle Rechte hat.",
                        "Wenn es nicht gebraucht wird: in der Computerverwaltung unter „Lokale Benutzer und Gruppen“ deaktivieren.",
                        "Win32_UserAccount.Disabled (RID 500)", Messwert.Von(0, "Disabled", "1"));
                    b.Detail.Add("PasswordRequired: " + k.KennwortNoetig + ", Admin: " + k.Admin);
                    continue;
                }
                if (k.Rid == RidGast) continue;
                if (k.Deaktiviert || k.KennwortNoetig) continue;
                // Konzept 4.4: PasswordRequired=false ist bei einem Microsoft-Konto normal (die
                // Anmeldung läuft über das Microsoft-Konto); nur rein lokale Konten werden gemeldet.
                // Ist der Kontotyp nicht ermittelbar, wird nicht geraten, sondern "nicht geprüft" gesagt.
                if (k.MicrosoftKonto == true) continue;
                if (k.MicrosoftKonto == null) { e.Fehlend.Add("Kennwortpflicht von „" + name + "“ (Kontotyp nicht ermittelbar)"); continue; }
                var w = Neu(e, "sicherheit.konto.ohnekennwort." + k.Rid, Zustand.Warn,
                    "Ein Konto braucht kein Kennwort",
                    "Für das lokale Konto „" + name + "“ ist kein Kennwort erforderlich; wer vor dem PC sitzt, kommt ohne Anmeldung hinein.",
                    "Unter „Einstellungen“ > „Konten“ > „Anmeldeoptionen“ ein Kennwort vergeben.",
                    "Win32_UserAccount.PasswordRequired", Messwert.Von(0, "PasswordRequired", "1"));
                w.Detail.Add("Kontotyp: lokal (LsaLookupUserAccountType = 1), nicht mit einem Microsoft-Konto verbunden.");
                w.Detail.Add("RID: " + k.Rid + ", Administrator: " + k.Admin + ", Disabled: " + k.Deaktiviert);
            }
        }

        static void Rdp(Kontext ctx, Systembild s, BereichErgebnis e)
        {
            var si = s.Sicherheit;
            if (s.Windows != null && s.Windows.IstHome) return;   // Home hat keinen Remotedesktop-Host (Konzept 4.4)
            if (si.RdpAn == null) { e.Fehlend.Add("Remotedesktop-Einstellung"); return; }
            if (si.RdpAn.Value == false) return;
            // Stammt der Schalter aus der Richtlinie, ist die Einstellung verwaltet: der Rat
            // "unter Einstellungen ausschalten" waere dann falsch.
            bool verwaltet = si.RdpHerkunft == RdpAusRichtlinie;
            string quelle = verwaltet ? "Policies\\Terminal Services.fDenyTSConnections" : "Terminal Server.fDenyTSConnections";
            string herkunft = verwaltet ? "Richtlinie (SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services)" : si.RdpHerkunft == null ? "nicht gelesen" : "lokal (Control\\Terminal Server)";
            if (si.RdpNla == false)
            {
                // Auch die NLA-Einstellung kann verwaltet sein (eigene Herkunft): dann hilft kein
                // Klick unter "Einstellungen", nur die Richtlinie.
                bool nlaVerwaltet = si.RdpNlaHerkunft == RdpAusRichtlinie;
                string rat = nlaVerwaltet
                    ? "Die Authentifizierung auf Netzwerkebene ist per Richtlinie abgeschaltet; das lässt sich nur dort ändern (Administrator der Domäne oder Gruppenrichtlinie)."
                    : verwaltet
                        ? "Die Authentifizierung auf Netzwerkebene unter „Einstellungen“ > „System“ > „Remotedesktop“ einschalten; Remotedesktop selbst ist verwaltet und lässt sich nur über die Richtlinie ausschalten."
                        : "Unter „Einstellungen“ > „System“ > „Remotedesktop“ die Authentifizierung auf Netzwerkebene einschalten oder Remotedesktop ausschalten.";
                var b = Neu(e, "sicherheit.rdp.ohnenla", Zustand.Warn,
                    "Remotedesktop ist ohne Anmeldeschutz erreichbar",
                    "Remotedesktop ist " + (verwaltet ? "über eine Richtlinie " : "") + "eingeschaltet, aber ohne Authentifizierung auf Netzwerkebene (NLA); Angreifer erreichen den Anmeldebildschirm ohne Kennwort.",
                    rat,
                    nlaVerwaltet ? "Policies\\Terminal Services.UserAuthentication" : "RDP-Tcp.UserAuthentication", Messwert.Von(0, "UserAuthentication", "1"));
                b.Detail.Add("fDenyTSConnections: 0 (RDP an, Herkunft " + herkunft + "), UserAuthentication: 0 (NLA aus, Herkunft " + (nlaVerwaltet ? "Richtlinie" : si.RdpNlaHerkunft == null ? "nicht gelesen" : "lokal") + ").");
                return;
            }
            string nla = si.RdpNla == true ? " und verlangt die Anmeldung schon auf Netzwerkebene (NLA)" : "";
            var ok = Neu(e, "sicherheit.rdp.an", Zustand.Ok,
                "Remotedesktop ist eingeschaltet",
                verwaltet
                    ? "Remotedesktop ist über eine Richtlinie eingeschaltet" + nla + "; die Einstellung ist verwaltet und lässt sich nicht unter „Einstellungen“ ausschalten."
                    : "Remotedesktop ist an" + nla + "; wenn Sie den PC nicht aus der Ferne bedienen, kann es aus.",
                null, quelle, Messwert.Von(0, "fDenyTSConnections", null));
            ok.Detail.Add("Herkunft: " + herkunft + ". UserAuthentication: " + (si.RdpNla.HasValue ? (si.RdpNla.Value ? "1" : "0") : "nicht gelesen"));
        }

        static void Smb1(Kontext ctx, Kern.Sicherheit si, BereichErgebnis e)
        {
            if (si.Smb1Server == null && si.Smb1Client == null) { e.Fehlend.Add("Netzwerkprotokoll SMB1"); return; }
            if (si.Smb1Server.HasValue && si.Smb1Server.Value == Smb1Aktiv)
            {
                if (si.Smb1ServerAktiv == false)
                {
                    // Feature installiert, Server per EnableSMB1Protocol=false abgeschaltet: der PC
                    // bietet kein SMB1 an. Nur Hinweis, das Feature ist ueberfluessig.
                    var h = Neu(e, "sicherheit.smb1.server.abgeschaltet", Zustand.Ok,
                        "Das SMB1-Feature ist installiert, aber abgeschaltet",
                        "Das Feature „SMB 1.0/CIFS-Server“ ist installiert, der Server bietet aber keine SMB1-Freigaben an (EnableSMB1Protocol steht auf aus); das Feature wird nicht gebraucht und kann weg.",
                        "Unter „Windows-Features“ das Feature „SMB 1.0/CIFS-Server“ abwählen, wenn es nicht mehr gebraucht wird.",
                        "MSFT_SmbServerConfiguration.EnableSMB1Protocol", Messwert.Von(false, "EnableSMB1Protocol", null));
                    h.Detail.Add("Win32_OptionalFeature.InstallState (SMB1Protocol-Server): 1 Enabled; MSFT_SmbServerConfiguration.EnableSMB1Protocol: false.");
                }
                else
                {
                    // EnableSMB1Protocol true: belegt. null: Serverkonfiguration nicht gelesen - dann
                    // entscheidet das Feature, sonst verschluckt ein WMI-Fehler die Warnung.
                    bool belegt = si.Smb1ServerAktiv == true;
                    var b = Neu(e, "sicherheit.smb1.server", Zustand.Warn,
                        "Das veraltete Freigabeprotokoll SMB1 ist aktiv",
                        "Der PC bietet Dateifreigaben über SMB1 an; dieses Protokoll von 1983 hat bekannte Lücken (WannaCry) und ist seit Windows 10 1709 standardmäßig aus.",
                        "Nur nötig für sehr alte Geräte (NAS, Drucker); sonst unter „Windows-Features“ das Feature „SMB 1.0/CIFS-Server“ abwählen.",
                        belegt ? "Win32_OptionalFeature.InstallState (SMB1Protocol-Server), MSFT_SmbServerConfiguration.EnableSMB1Protocol" : "Win32_OptionalFeature.InstallState (SMB1Protocol-Server)",
                        Messwert.Von(si.Smb1Server.Value, "InstallState", "2 = Disabled"));
                    b.Detail.Add("InstallState: 1 Enabled, 2 Disabled, 3 Absent (Doku). Alte NAS-Geräte brauchen meist nur den Client, nicht den Server.");
                    b.Detail.Add(belegt
                        ? "MSFT_SmbServerConfiguration.EnableSMB1Protocol: true."
                        : "Serverkonfiguration nicht gelesen (" + Grund(ctx.S, "wmi.smb.serverconfiguration") + "); das installierte Feature entscheidet.");
                }
            }
            if (si.Smb1Client.HasValue && si.Smb1Client.Value == Smb1Aktiv)
            {
                var b = Neu(e, "sicherheit.smb1.client", Zustand.Ok,
                    "Der alte Netzwerkclient SMB1 ist eingeschaltet",
                    "Der PC kann sich per SMB1 mit sehr alten Geräten verbinden; wenn kein altes NAS im Haus ist, wird das nicht gebraucht.",
                    null, "Win32_OptionalFeature.InstallState (SMB1Protocol-Client)", Messwert.Von(si.Smb1Client.Value, "InstallState", null));
                b.Detail.Add("Nur Hinweis: der Client bietet nichts an, er verbindet sich nur.");
            }
        }

        // ------------------------------------------------------------------ Helfer

        static Befund Neu(BereichErgebnis e, string schluessel, string zustand, string titel, string satz, string rat, string quelle, Messwert m)
        {
            var b = new Befund
            {
                Bereich = Bereich.Sicherheit,
                Schluessel = schluessel,
                Zustand = zustand,
                Titel = titel,
                Satz = satz,
                Rat = rat,
                Quelle = quelle,
                Messwert = m,
            };
            e.Befunde.Add(b);
            return b;
        }

        static string TageText(int tage)
        {
            if (tage == 0) return "0 Tage";
            return tage == 1 ? "1 Tag" : tage + " Tage";
        }

        static string Wert(int? v) { return v.HasValue ? v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "fehlt"; }

        /// <summary>Warum eine Quelle nichts lieferte, in einem Wort fuer die Zeile "liess sich nicht pruefen" (wie Datentraeger.Grund).</summary>
        static string Grund(Systembild s, string quelle)
        {
            var f = s.FehlerVon(quelle).FirstOrDefault();
            if (f == null) return "keine Daten";
            switch (f.Art)
            {
                case Fehler.Zugriff: return "braucht Administratorrechte";
                case Fehler.Zeit: return "Zeitüberschreitung";
                case Fehler.Fehlt: return "nicht verfügbar";
                default: return "Fehler beim Lesen";
            }
        }
    }
}
