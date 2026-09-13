using System;
using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer den Bereich Sicherheit gegen gepflanzte Systembilder
    /// (tests/aufzeichnungen/gepflanzt-sicherheit-*.json).
    ///
    /// Jede Regel hat eine Probe UND eine Gegenprobe (Bedingung fehlt -> kein Befund). Die
    /// Wirksamkeitsproben vom 2026-09-12 stehen als Kommentar an der jeweiligen Probe: die
    /// Regel wurde absichtlich kaputt gemacht, die Probe wurde rot, die Regel wieder repariert.
    /// Die Befunde der Widerlegungsrunde vom 2026-09-13 (WSC-Gesundheit, Defender im Normalmodus,
    /// SMB1-Serverkonfiguration, RDP-Richtlinie, Grund der Ausschlussliste, UAC-Neustart,
    /// Firewall-Rueckfallquelle, stille Fehlend-Faelle) haben je eine eigene Probe mit Datum.
    /// </summary>
    public static class SicherheitProben
    {
        const string B = Kern.Bereich.Sicherheit;

        /// <summary>Grundmenge: das Bild muss den Bereich wirklich tragen, sonst ist eine leere Datei gruen.</summary>
        static bool Grundmenge(Harness h, Systembild s, string datei, bool defenderNoetig = true)
        {
            if (s == null) return false;
            bool ok = s.AufgezeichnetUtc != null && s.Sicherheit != null
                      && (!defenderNoetig || (s.Sicherheit.Defender != null && s.Sicherheit.Firewall.Count == 3 && s.Sicherheit.Konten.Count >= 3));
            h.Ist("Grundmenge " + datei + ": aufgezeichnet gesetzt, Sicherheit mit Defender, 3 Firewall-Profilen, >= 3 Konten", ok);
            return ok;
        }

        static List<Befund> Sicherheit(List<BereichErgebnis> erg)
        {
            return Harness.Befunde(erg, B).ToList();
        }

        static bool KeinProblem(List<BereichErgebnis> erg, string schluesselAnfang)
        {
            return !Harness.AlleMit(erg, schluesselAnfang).Any(b => b.IstProblem);
        }

        public static void Laufen(Harness h)
        {
            var alleBefunde = new List<Befund>();

            // ---------------------------------------------------------------- gesund
            h.Gruppe("Sicherheit: gesundes Bild (Gegenprobe fuer jede Regel)");
            {
                var s = h.Bild("gepflanzt-sicherheit-gesund.json");
                if (Grundmenge(h, s, "gesund"))
                {
                    var erg = h.Pruefen(s);
                    var ber = Harness.Bereich(erg, B);
                    var bef = Sicherheit(erg);
                    alleBefunde.AddRange(bef);
                    h.Ist("DatenVorhanden", ber.DatenVorhanden);
                    h.Ist("Bereich ist ok", ber.Zustand == Zustand.Ok, ber.Zustand);
                    h.Ist("kein warn/bad im gesunden Bild", !bef.Any(b => b.IstProblem), string.Join(", ", bef.Where(b => b.IstProblem).Select(b => b.Schluessel)));
                    h.Ist("Virenschutz an als ok-Befund", Harness.Einer(erg, "sicherheit.virenschutz.an") != null && Harness.Einer(erg, "sicherheit.virenschutz.an").Zustand == Zustand.Ok);
                    h.Ist("kein Befund Virenschutz aus", Harness.Einer(erg, "sicherheit.virenschutz.aus") == null);
                    h.Ist("kein Signaturbefund bei 0 Tagen", Harness.Einer(erg, "sicherheit.signaturen") == null);
                    h.Ist("kein Schnellscanbefund bei 2 Tagen", Harness.Einer(erg, "sicherheit.schnellscan") == null);
                    h.Ist("kein Ausschlussbefund fuer C:\\Projekte\\Build", Harness.Einer(erg, "sicherheit.ausschluss") == null);
                    h.Ist("Firewall an als ok-Befund", Harness.Einer(erg, "sicherheit.firewall.an") != null);
                    h.Ist("kein Firewallbefund", Harness.Einer(erg, "sicherheit.firewall.aus") == null && Harness.Einer(erg, "sicherheit.firewall.eingehend") == null);
                    h.Ist("kein UAC-Befund bei EnableLUA 1 / Consent 5", Harness.Einer(erg, "sicherheit.uac") == null);
                    h.Ist("kein SmartScreen-Befund bei Warn", Harness.Einer(erg, "sicherheit.smartscreen") == null);
                    h.Ist("kein SecureBoot-Befund bei an", Harness.Einer(erg, "sicherheit.secureboot") == null);
                    h.Ist("kein Kontobefund (500 deaktiviert, 501 deaktiviert, 1001 mit Kennwort)", Harness.Einer(erg, "sicherheit.konto") == null);
                    h.Ist("kein RDP-Befund bei RDP aus", Harness.Einer(erg, "sicherheit.rdp") == null);
                    h.Ist("kein SMB1-Befund bei InstallState 2", Harness.Einer(erg, "sicherheit.smb1") == null);
                    h.Ist("keine Fehlend-Zeile im gesunden Bild", ber.Fehlend.Count == 0, string.Join("; ", ber.Fehlend));
                }
            }

            // ---------------------------------------------------------------- (a) Dritt-AV
            // Wirksamkeitsprobe 2026-09-12: Bedingung "DrittAv.Count > 0" in Virenschutz() auf
            // "== 0" gedreht -> "kein Befund Virenschutz aus" rot, "Fremdschutz als ok" rot.
            h.Gruppe("Sicherheit (a): Fremd-Virenschutz registriert, Defender passiv");
            {
                var s = h.Bild("gepflanzt-sicherheit-drittav.json");
                if (Grundmenge(h, s, "drittav"))
                {
                    h.Ist("Vorbedingung: DrittAv gesetzt, Echtzeit aus, Modus Passive, WSC-Gesundheit 0", s.Sicherheit.DrittAv.Count == 1 && !s.Sicherheit.Defender.Echtzeit && s.Sicherheit.Defender.Modus == "Passive" && s.Sicherheit.VirenschutzGesundheit == 0);
                    var erg = h.Pruefen(s);
                    var bef = Sicherheit(erg);
                    alleBefunde.AddRange(bef);
                    h.Ist("KEIN Befund 'Virenschutz aus'", Harness.Einer(erg, "sicherheit.virenschutz.aus") == null);
                    var fremd = Harness.Einer(erg, "sicherheit.virenschutz.fremd");
                    h.Ist("Fremdschutz als ok-Befund mit Namen", fremd != null && fremd.Zustand == Zustand.Ok && fremd.Satz.Contains("Beispiel Antivirus Premium"), fremd == null ? "fehlt" : fremd.Satz);
                    h.Ist("genau ein Fremd-Befund, Schluessel ohne Zusatz", Harness.AlleMit(erg, "sicherheit.virenschutz.fremd").Count() == 1 && fremd.Schluessel == "sicherheit.virenschutz.fremd");
                    h.Ist("Detail nennt den Defender-Modus", fremd != null && fremd.Detail.Any(d => d.Contains("Passive")));
                    h.Ist("Detail nennt die WSC-Gesundheit GOOD", fremd != null && fremd.Detail.Any(d => d.Contains("GOOD")));
                    h.Ist("kein Signaturbefund trotz 40 Tagen (Defender ist nicht der Schutz)", Harness.Einer(erg, "sicherheit.signaturen") == null);
                    h.Ist("Bereich nicht bad", Harness.Bereich(erg, B).Zustand != Zustand.Bad);
                    h.Ist("nichts unter 'nicht geprueft' zum Fremdschutz", !Harness.Bereich(erg, B).Fehlend.Any(f => f.Contains("Fremd")));

                    // Gesundheit nicht gemessen (kein wscsvc): der Befund bleibt sichtbar, aber ohne Urteil.
                    s.Sicherheit.VirenschutzGesundheit = null;
                    var ergN = h.Pruefen(s);
                    var fremdN = Harness.Einer(ergN, "sicherheit.virenschutz.fremd");
                    h.Ist("Gesundheit null -> Fremd-Befund unknown, kein ok, kein bad", fremdN != null && fremdN.Zustand == Zustand.Unknown && Harness.Einer(ergN, "sicherheit.virenschutz.fremd.aus") == null);
                    h.Ist("Gesundheit null -> Fehlend nennt den Fremd-Virenschutz", Harness.Bereich(ergN, B).Fehlend.Any(f => f.Contains("Fremd-Virenschutz")), string.Join("; ", Harness.Bereich(ergN, B).Fehlend));
                    h.Ist("Gesundheit null -> Bereich nicht bad", Harness.Bereich(ergN, B).Zustand != Zustand.Bad);
                    // NOTMONITORED (1): ebenfalls kein Urteil, Grund im Fehlend-Text.
                    s.Sicherheit.VirenschutzGesundheit = 1;
                    var erg1 = h.Pruefen(s);
                    h.Ist("Gesundheit 1 (NOTMONITORED) -> unknown und 'nicht überwacht' unter Fehlend", Harness.Einer(erg1, "sicherheit.virenschutz.fremd").Zustand == Zustand.Unknown && Harness.Bereich(erg1, B).Fehlend.Any(f => f.Contains("überwacht")));
                    // SNOOZE (3): warn, das Produkt ist absichtlich pausiert.
                    s.Sicherheit.VirenschutzGesundheit = 3;
                    var erg3 = h.Pruefen(s);
                    var pausiert = Harness.Einer(erg3, "sicherheit.virenschutz.fremd.pausiert");
                    h.Ist("Gesundheit 3 (SNOOZE) -> warn 'pausiert', kein ok-Fremdbefund daneben", pausiert != null && pausiert.Zustand == Zustand.Warn && Harness.AlleMit(erg3, "sicherheit.virenschutz.fremd").Count() == 1);
                    h.Ist("SNOOZE mit passivem Defender: Satz sagt 'ohne Echtzeitschutz'", pausiert != null && pausiert.Satz.Contains("ohne Echtzeitschutz"), pausiert == null ? "" : pausiert.Satz);
                    if (pausiert != null) alleBefunde.Add(pausiert);
                    if (fremdN != null) alleBefunde.Add(fremdN);
                    s.Sicherheit.VirenschutzGesundheit = 0;
                }
            }

            // ---------------------------------------------------------------- (a2) Dritt-AV abgelaufen
            // Befund 1 (13.09.2026): ein abgelaufenes Fremdprodukt bleibt in SecurityCenter2
            // registriert; ohne die WSC-Gesundheit hiess das "ok", obwohl der PC ohne Schutz lief.
            h.Gruppe("Sicherheit (a2): Fremd-Virenschutz registriert, aber laut Sicherheitscenter POOR");
            {
                var s = h.Bild("gepflanzt-sicherheit-drittav-abgelaufen.json");
                if (Grundmenge(h, s, "drittav-abgelaufen"))
                {
                    h.Ist("Vorbedingung: DrittAv gesetzt, Defender passiv, WSC-Gesundheit 2 = Schwelle schlecht", s.Sicherheit.DrittAv.Count == 1 && s.Sicherheit.Defender.Modus == "Passive" && s.Sicherheit.VirenschutzGesundheit == Schwellen.VirenschutzGesundheitSchlecht);
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var aus = Harness.Einer(erg, "sicherheit.virenschutz.fremd.aus");
                    h.Ist("POOR -> bad 'Fremdschutz abgeschaltet oder abgelaufen' mit Namen", aus != null && aus.Zustand == Zustand.Bad && aus.Satz.Contains("Beispiel Antivirus Premium"), aus == null ? "fehlt" : aus.Satz);
                    h.Ist("Rat: verlängern oder deinstallieren, dann übernimmt Defender", aus != null && aus.Rat.Contains("deinstallieren") && aus.Rat.Contains("Defender"));
                    h.Ist("Satz sagt 'ohne wirksamen Echtzeitschutz' (Defender passiv)", aus != null && aus.Satz.Contains("ohne wirksamen Echtzeitschutz"));
                    h.Ist("Quelle ist WscGetSecurityProviderHealth, Messwert 2", aus != null && aus.Quelle.Contains("WscGetSecurityProviderHealth") && aus.Messwert.Wert == "2");
                    h.Ist("kein ok-Fremdbefund und kein Defender-Zweig 'Virenschutz aus' daneben", Harness.AlleMit(erg, "sicherheit.virenschutz").Count() == 1 && Harness.Einer(erg, "sicherheit.virenschutz.aus") == null);
                    h.Ist("kein Signaturbefund (Defender passiv, nicht der Schutz)", Harness.Einer(erg, "sicherheit.signaturen") == null);
                    h.Ist("Bereich ist bad", Harness.Bereich(erg, B).Zustand == Zustand.Bad);
                    // Gegenprobe: dasselbe Bild mit GOOD -> der alte ok-Weg.
                    s.Sicherheit.VirenschutzGesundheit = 0;
                    var erg0 = h.Pruefen(s);
                    h.Ist("Gegenprobe: GOOD -> kein bad, ok-Fremdbefund", Harness.Einer(erg0, "sicherheit.virenschutz.fremd.aus") == null && Harness.Einer(erg0, "sicherheit.virenschutz.fremd").Zustand == Zustand.Ok);
                    // Defender-Status fehlt ganz (per Richtlinie abgeschaltet), nichts registriert, POOR -> bad "keiner".
                    s.Sicherheit.Defender = null; s.Sicherheit.DrittAv.Clear(); s.Sicherheit.VirenschutzGesundheit = 2;
                    var ergK = h.Pruefen(s);
                    var keiner = Harness.Einer(ergK, "sicherheit.virenschutz.keiner");
                    h.Ist("Defender null, DrittAv leer, POOR -> bad 'kein wirksamer Virenschutz'", keiner != null && keiner.Zustand == Zustand.Bad && Harness.Bereich(ergK, B).Zustand == Zustand.Bad);
                    h.Ist("dabei kein Fehlend 'Schutzstatus nicht lesbar'", !Harness.Bereich(ergK, B).Fehlend.Contains("Schutzstatus nicht lesbar"));
                    if (keiner != null) alleBefunde.Add(keiner);
                    s.Sicherheit.VirenschutzGesundheit = null;
                    var ergU = h.Pruefen(s);
                    h.Ist("Gegenprobe: Gesundheit null -> kein Befund, Fehlend 'Schutzstatus nicht lesbar'", Harness.Einer(ergU, "sicherheit.virenschutz") == null && Harness.Bereich(ergU, B).Fehlend.Contains("Schutzstatus nicht lesbar"));
                }
            }

            // ---------------------------------------------------------------- (a3) Dritt-AV, Defender im Normalmodus
            // Befund 5 (13.09.2026): bei registriertem Fremdprodukt und AMRunningMode "Normal"
            // ist Defender der Schutz - Signaturen und Scans muessen gegen ihn laufen.
            h.Gruppe("Sicherheit (a3): Fremd-Virenschutz registriert, Defender läuft im Normalmodus");
            {
                var s = h.Bild("gepflanzt-sicherheit-drittav-normal.json");
                if (Grundmenge(h, s, "drittav-normal"))
                {
                    h.Ist("Vorbedingung: DrittAv gesetzt, Modus Normal, Echtzeit an, Signaturen 40 Tage > " + Schwellen.SignaturAlterWarnTage, s.Sicherheit.DrittAv.Count == 1 && s.Sicherheit.Defender.Modus == "Normal" && s.Sicherheit.Defender.Echtzeit && s.Sicherheit.Defender.SignaturAlterTage == 40 && 40 > Schwellen.SignaturAlterWarnTage);
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var fremd = Harness.Einer(erg, "sicherheit.virenschutz.fremd");
                    h.Ist("Fremd-Befund ok, Satz nennt den Normalmodus und die Frage nach der Gültigkeit", fremd != null && fremd.Zustand == Zustand.Ok && fremd.Satz.Contains("Normalmodus") && fremd.Satz.Contains("gültig"), fremd == null ? "fehlt" : fremd.Satz);
                    h.Ist("Satz sagt NICHT 'prüft deshalb nicht mit'", fremd != null && !fremd.Satz.Contains("prüft deshalb nicht mit"));
                    h.Ist("Quelle ist AMRunningMode", fremd != null && fremd.Quelle == "MSFT_MpComputerStatus.AMRunningMode");
                    var sig = Harness.Einer(erg, "sicherheit.signaturen.alt");
                    h.Ist("Signaturbefund warn trotz Fremdprodukt (Defender ist der Schutz)", sig != null && sig.Zustand == Zustand.Warn && sig.Satz.Contains("40 Tage"));
                    h.Ist("kein Befund 'Virenschutz aus'", Harness.Einer(erg, "sicherheit.virenschutz.aus") == null);
                    h.Ist("Bereich ist warn (Signaturen), nicht bad", Harness.Bereich(erg, B).Zustand == Zustand.Warn);
                    // Gegenprobe: Modus Passive mit Echtzeit true (Doku-Note Endpoint DLP) -> nicht der Schutz.
                    s.Sicherheit.Defender.Modus = "Passive";
                    var ergP = h.Pruefen(s);
                    h.Ist("Gegenprobe: Passive trotz Echtzeit true -> kein Signaturbefund, Fremd-Befund ohne 'Normalmodus'", Harness.Einer(ergP, "sicherheit.signaturen") == null && !Harness.Einer(ergP, "sicherheit.virenschutz.fremd").Satz.Contains("Normalmodus"));
                    // Rueckfall: Modus fehlt -> Dienst und Echtzeit entscheiden.
                    s.Sicherheit.Defender.Modus = null;
                    var ergR = h.Pruefen(s);
                    h.Ist("Modus null, Dienst und Echtzeit an -> Rückfall: Defender ist der Schutz, Signaturbefund", Harness.Einer(ergR, "sicherheit.signaturen.alt") != null);
                    s.Sicherheit.Defender.Echtzeit = false;
                    h.Ist("Modus null, Echtzeit aus -> kein Signaturbefund", Harness.Einer(h.Pruefen(s), "sicherheit.signaturen") == null);
                    // POOR bei Normalmodus: bad bleibt, der Satz sagt, dass Defender derweil mitprüft.
                    s.Sicherheit.Defender.Echtzeit = true; s.Sicherheit.Defender.Modus = "Normal"; s.Sicherheit.VirenschutzGesundheit = 2;
                    var ergB = h.Pruefen(s);
                    var aus = Harness.Einer(ergB, "sicherheit.virenschutz.fremd.aus");
                    h.Ist("POOR trotz Normalmodus -> bad, Satz nennt Defender im Normalmodus", aus != null && aus.Zustand == Zustand.Bad && aus.Satz.Contains("Normalmodus"));
                    s.Sicherheit.VirenschutzGesundheit = 0;
                }
            }

            // ---------------------------------------------------------------- Sicherheitscenter POOR bei Defender an (aus gesund)
            h.Gruppe("Sicherheit: Defender an, aber Sicherheitscenter meldet POOR");
            {
                var s = h.Bild("gepflanzt-sicherheit-gesund.json");
                if (Grundmenge(h, s, "gesund"))
                {
                    s.Sicherheit.VirenschutzGesundheit = 2;
                    var erg = h.Pruefen(s);
                    var w = Harness.Einer(erg, "sicherheit.virenschutz.gesundheit");
                    h.Ist("Defender an + POOR -> warn 'gesundheit', ok-Befund 'an' bleibt", w != null && w.Zustand == Zustand.Warn && Harness.Einer(erg, "sicherheit.virenschutz.an") != null);
                    h.Ist("kein bad daneben", Harness.Bereich(erg, B).Zustand == Zustand.Warn);
                    if (w != null) alleBefunde.Add(w);
                    s.Sicherheit.VirenschutzGesundheit = 0;
                    h.Ist("Gegenprobe: GOOD -> kein 'gesundheit'-Befund", Harness.Einer(h.Pruefen(s), "sicherheit.virenschutz.gesundheit") == null);
                }
            }

            // ---------------------------------------------------------------- (b) Echtzeit aus
            // Wirksamkeitsprobe 2026-09-12: "!d.DienstAn || !d.Echtzeit" auf "&&" verengt
            // -> "genau ein bad-Befund Virenschutz aus" rot.
            h.Gruppe("Sicherheit (b): Echtzeitschutz aus, kein Fremdschutz");
            {
                var s = h.Bild("gepflanzt-sicherheit-echtzeit-aus.json");
                if (Grundmenge(h, s, "echtzeit-aus"))
                {
                    h.Ist("Vorbedingung: Echtzeit aus, DrittAv leer", !s.Sicherheit.Defender.Echtzeit && s.Sicherheit.DrittAv.Count == 0);
                    var erg = h.Pruefen(s);
                    var bef = Sicherheit(erg);
                    alleBefunde.AddRange(bef);
                    var aus = Harness.AlleMit(erg, "sicherheit.virenschutz.aus").ToList();
                    h.Ist("genau ein bad-Befund 'Virenschutz aus'", aus.Count == 1 && aus[0].Zustand == Zustand.Bad);
                    h.Ist("Rat nennt Windows-Sicherheit, keine Massnahme (Tamper)", aus.Count == 1 && aus[0].Rat != null && aus[0].Rat.Contains("Windows-Sicherheit") && aus[0].Massnahmen.Count == 0);
                    h.Ist("Quelle ist RealTimeProtectionEnabled", aus.Count == 1 && aus[0].Quelle == "MSFT_MpComputerStatus.RealTimeProtectionEnabled");
                    h.Ist("kein ok-Befund 'Virenschutz an'", Harness.Einer(erg, "sicherheit.virenschutz.an") == null);
                    h.Ist("Bereich ist bad", Harness.Bereich(erg, B).Zustand == Zustand.Bad);

                    // Gegenprobe im selben Bild: Echtzeit wieder an -> Befund verschwindet.
                    s.Sicherheit.Defender.Echtzeit = true;
                    var erg2 = h.Pruefen(s);
                    h.Ist("Gegenprobe: Echtzeit an -> kein Befund 'Virenschutz aus'", Harness.Einer(erg2, "sicherheit.virenschutz.aus") == null);
                    // Dienst aus statt Echtzeit aus -> ebenfalls bad, andere Quelle.
                    s.Sicherheit.Defender.DienstAn = false;
                    var erg3 = h.Pruefen(s);
                    var aus3 = Harness.Einer(erg3, "sicherheit.virenschutz.aus");
                    h.Ist("Dienst aus -> bad mit Quelle AMServiceEnabled", aus3 != null && aus3.Zustand == Zustand.Bad && aus3.Quelle == "MSFT_MpComputerStatus.AMServiceEnabled");
                }
            }

            // ---------------------------------------------------------------- (i) englisches Bild von (b)
            h.Gruppe("Sicherheit (i): englisches Bild liefert dieselben Befunde wie (b)");
            {
                var de = h.Bild("gepflanzt-sicherheit-echtzeit-aus.json");
                var en = h.Bild("gepflanzt-sicherheit-echtzeit-aus-en.json");
                if (Grundmenge(h, de, "echtzeit-aus") && Grundmenge(h, en, "echtzeit-aus-en"))
                {
                    h.Ist("Vorbedingung: en hat LCID 1033 und andere Kontonamen", en.Sprache.Lcid == 1033 && en.Sicherheit.Konten.Any(k => k.Name == "Guest"));
                    var ergDe = h.Pruefen(de);
                    var ergEn = h.Pruefen(en);
                    alleBefunde.AddRange(Sicherheit(ergEn));
                    var keysDe = Sicherheit(ergDe).Select(b => b.Schluessel + "=" + b.Zustand).OrderBy(x => x).ToList();
                    var keysEn = Sicherheit(ergEn).Select(b => b.Schluessel + "=" + b.Zustand).OrderBy(x => x).ToList();
                    h.Ist("gleiche Schluessel und Zustaende in de und en", keysDe.SequenceEqual(keysEn), string.Join(" | ", keysDe) + "  <->  " + string.Join(" | ", keysEn));
                    h.Ist("gleicher Bereichszustand", Harness.Bereich(ergDe, B).Zustand == Harness.Bereich(ergEn, B).Zustand);
                    h.Ist("en: bad-Befund 'Virenschutz aus' vorhanden", Harness.Einer(ergEn, "sicherheit.virenschutz.aus") != null && Harness.Einer(ergEn, "sicherheit.virenschutz.aus").Zustand == Zustand.Bad);
                }
            }

            // ---------------------------------------------------------------- (c) Signaturalter
            // Wirksamkeitsprobe 2026-09-12: Vergleich "> SignaturAlterWarnTage" auf "<" gedreht
            // -> "10 Tage -> warn" rot und "gesund: kein Signaturbefund" rot.
            h.Gruppe("Sicherheit (c): Alter der Virensignaturen");
            {
                var nie = h.Bild("gepflanzt-sicherheit-signaturen-nie.json");
                if (Grundmenge(h, nie, "signaturen-nie"))
                {
                    h.Ist("Vorbedingung: SignaturAlterTage 65535", nie.Sicherheit.Defender.SignaturAlterTage == Schwellen.NieWert);
                    var erg = h.Pruefen(nie);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var b = Harness.Einer(erg, "sicherheit.signaturen.nie");
                    h.Ist("65535 -> bad 'nie'", b != null && b.Zustand == Zustand.Bad);
                    h.Ist("Massnahme Signaturen aktualisieren", b != null && b.Massnahmen.Contains("sicherheit.signaturen.aktualisieren"));
                    h.Ist("kein warn-Befund 'alt' daneben", Harness.Einer(erg, "sicherheit.signaturen.alt") == null);
                }
                var alt = h.Bild("gepflanzt-sicherheit-signaturen-alt.json");
                if (Grundmenge(h, alt, "signaturen-alt"))
                {
                    h.Ist("Vorbedingung: SignaturAlterTage 10 > Schwelle " + Schwellen.SignaturAlterWarnTage, alt.Sicherheit.Defender.SignaturAlterTage == 10 && 10 > Schwellen.SignaturAlterWarnTage);
                    var erg = h.Pruefen(alt);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var b = Harness.Einer(erg, "sicherheit.signaturen.alt");
                    h.Ist("10 Tage -> warn", b != null && b.Zustand == Zustand.Warn);
                    h.Ist("Satz nennt die 10 Tage", b != null && b.Satz.Contains("10 Tage"));
                    h.Ist("Messwert 10 Tage mit Schwelle", b != null && b.Messwert != null && b.Messwert.Wert == "10" && b.Messwert.Schwelle.Contains(Schwellen.SignaturAlterWarnTage.ToString()));
                    h.Ist("Massnahme Signaturen aktualisieren", b != null && b.Massnahmen.Contains("sicherheit.signaturen.aktualisieren"));
                    // Gegenprobe: genau an der Schwelle -> kein Befund; darueber -> Befund.
                    alt.Sicherheit.Defender.SignaturAlterTage = Schwellen.SignaturAlterWarnTage;
                    h.Ist("Gegenprobe: genau " + Schwellen.SignaturAlterWarnTage + " Tage -> kein Befund", Harness.Einer(h.Pruefen(alt), "sicherheit.signaturen") == null);
                    alt.Sicherheit.Defender.SignaturAlterTage = 0;
                    h.Ist("Gegenprobe: 0 Tage -> kein Befund", Harness.Einer(h.Pruefen(alt), "sicherheit.signaturen") == null);
                    alt.Sicherheit.Defender.SignaturAlterTage = -1;
                    var ergU = h.Pruefen(alt);
                    h.Ist("Feld fehlte (-1) -> kein Befund, aber Fehlend", Harness.Einer(ergU, "sicherheit.signaturen") == null && Harness.Bereich(ergU, B).Fehlend.Any(f => f.Contains("Virensignaturen")));
                }
            }

            // ---------------------------------------------------------------- Schnellscan (aus gesund abgeleitet)
            // Wirksamkeitsprobe 2026-09-12: "<= SchnellscanAlterWarnTage return" auf ">=" gedreht -> "20 Tage -> warn" rot.
            h.Gruppe("Sicherheit: Alter des Schnellscans");
            {
                var s = h.Bild("gepflanzt-sicherheit-gesund.json");
                if (Grundmenge(h, s, "gesund"))
                {
                    s.Sicherheit.Defender.SchnellscanAlterTage = Schwellen.SchnellscanAlterWarnTage + 6;
                    s.Sicherheit.Defender.VollscanAlterTage = 60;
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var b = Harness.Einer(erg, "sicherheit.schnellscan.alt");
                    h.Ist("20 Tage ohne frischen Vollscan -> warn mit Massnahme Schnellscan", b != null && b.Zustand == Zustand.Warn && b.Massnahmen.Contains("sicherheit.schnellscan"));
                    s.Sicherheit.Defender.VollscanAlterTage = 3;
                    h.Ist("Gegenprobe: frischer Vollscan (3 Tage) -> kein Schnellscanbefund", Harness.Einer(h.Pruefen(s), "sicherheit.schnellscan") == null);
                    s.Sicherheit.Defender.VollscanAlterTage = Schwellen.NieWert;
                    s.Sicherheit.Defender.SchnellscanAlterTage = Schwellen.NieWert;
                    var bn = Harness.Einer(h.Pruefen(s), "sicherheit.schnellscan.alt");
                    h.Ist("nie gescannt (65535) -> warn mit 'nie' im Satz", bn != null && bn.Zustand == Zustand.Warn && bn.Satz.Contains("nie"));
                    s.Sicherheit.Defender.SchnellscanAlterTage = 2;
                    h.Ist("Gegenprobe: 2 Tage -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.schnellscan") == null);
                }
            }

            // ---------------------------------------------------------------- (d) Ausschluesse
            // Wirksamkeitsprobe 2026-09-12: Regex GanzesLaufwerk auf "^X:$" ohne Backslash verengt
            // -> "D:\ -> warn" rot; AusschluesseSichtbar-Pruefung entfernt -> "unsichtbar: kein Befund" blieb
            // gruen, aber "Fehlend nennt Administratorrechte" rot.
            h.Gruppe("Sicherheit (d): Ausschluesse des Virenschutzes");
            {
                var s = h.Bild("gepflanzt-sicherheit-ausschluss-laufwerk.json");
                if (Grundmenge(h, s, "ausschluss-laufwerk"))
                {
                    h.Ist("Vorbedingung: Ausschluss D:\\ sichtbar", s.Sicherheit.Defender.AusschluesseSichtbar && s.Sicherheit.Defender.AusschlussPfade.Contains("D:\\"));
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var lw = Harness.AlleMit(erg, "sicherheit.ausschluss.laufwerk").ToList();
                    h.Ist("genau ein warn-Befund fuer Laufwerk D", lw.Count == 1 && lw[0].Zustand == Zustand.Warn && lw[0].Schluessel == "sicherheit.ausschluss.laufwerk.D");
                    h.Ist("keine Massnahme (nur melden)", lw.Count == 1 && lw[0].Massnahmen.Count == 0);
                    h.Ist("C:\\Projekte\\Build erzeugt keinen Befund", Harness.AlleMit(erg, "sicherheit.ausschluss").Count() == 1);
                    // Varianten der Laufwerksschreibweise und die anderen Klassen.
                    s.Sicherheit.Defender.AusschlussPfade = new List<string> { "E:", "F:\\*", "C:\\Users\\NUTZER\\AppData\\Local\\Temp", "C:\\Users\\NUTZER\\Downloads\\", "%TEMP%" };
                    s.Sicherheit.Defender.AusschlussEndungen = new List<string> { ".exe", "dll", "log" };
                    var erg2 = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg2));
                    h.Ist("'E:' und 'F:\\*' zaehlen als ganzes Laufwerk", Harness.AlleMit(erg2, "sicherheit.ausschluss.laufwerk").Count() == 2);
                    h.Ist("Temp-Ordner und %TEMP% -> 2 warn", Harness.AlleMit(erg2, "sicherheit.ausschluss.temp").Count(b => b.Zustand == Zustand.Warn) == 2);
                    h.Ist("Downloads -> warn", Harness.AlleMit(erg2, "sicherheit.ausschluss.downloads").Count(b => b.Zustand == Zustand.Warn) == 1);
                    h.Ist("Endungen exe und dll -> 2 warn, log nicht", Harness.AlleMit(erg2, "sicherheit.ausschluss.endung").Count() == 2 && Harness.Einer(erg2, "sicherheit.ausschluss.endung.log") == null);
                }
                var u = h.Bild("gepflanzt-sicherheit-ausschluss-unsichtbar.json");
                if (Grundmenge(h, u, "ausschluss-unsichtbar"))
                {
                    h.Ist("Vorbedingung: nicht erhoeht, AusschluesseSichtbar false, zugriff eingetragen", !u.Erhoeht && !u.Sicherheit.Defender.AusschluesseSichtbar && u.ZugriffVerweigert("wmi.defender.exclusions"));
                    var erg = h.Pruefen(u);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var ber = Harness.Bereich(erg, B);
                    h.Ist("unsichtbar: kein Ausschlussbefund", Harness.Einer(erg, "sicherheit.ausschluss") == null);
                    h.Ist("Fehlend nennt die Ausnahmen mit Administratorrechten", ber.Fehlend.Any(f => f.Contains("Ausnahmen") && f.Contains("Administratorrechte")), string.Join("; ", ber.Fehlend));
                    h.Ist("Bereich bleibt ok (unbekannt zaehlt nicht)", ber.Zustand == Zustand.Ok);
                    // Erhoeht und trotzdem "zugriff": HideExclusionsFromLocalAdmins - nicht "braucht Administratorrechte".
                    u.Erhoeht = true;
                    var ergR = Harness.Bereich(h.Pruefen(u), B);
                    h.Ist("erhoeht + zugriff -> Fehlend sagt 'durch Richtlinie verborgen', nicht 'Administratorrechte'", ergR.Fehlend.Any(f => f.Contains("Ausnahmen") && f.Contains("Richtlinie")) && !ergR.Fehlend.Any(f => f.Contains("Administratorrechte")), string.Join("; ", ergR.Fehlend));
                }
                // Befund 4 (13.09.2026): erhoeht, Abfrage in die Zeitgrenze gelaufen - der alte Text
                // "braucht Administratorrechte" war falsch, der Grund steht in der Fehlerliste.
                var z = h.Bild("gepflanzt-sicherheit-ausschluss-zeit.json");
                if (Grundmenge(h, z, "ausschluss-zeit"))
                {
                    h.Ist("Vorbedingung: erhoeht, AusschluesseSichtbar false, Fehlerliste 'zeit' fuer wmi.defender.exclusions", z.Erhoeht && !z.Sicherheit.Defender.AusschluesseSichtbar && z.FehlerVon("wmi.defender.exclusions").Any(f => f.Art == Fehler.Zeit));
                    var ber = Harness.Bereich(h.Pruefen(z), B);
                    h.Ist("Fehlend nennt die Ausnahmen mit Zeitüberschreitung", ber.Fehlend.Any(f => f.Contains("Ausnahmen") && f.Contains("Zeitüberschreitung")), string.Join("; ", ber.Fehlend));
                    h.Ist("Fehlend sagt NICHT 'Administratorrechte'", !ber.Fehlend.Any(f => f.Contains("Administratorrechte")), string.Join("; ", ber.Fehlend));
                    h.Ist("kein Ausschlussbefund", Harness.Einer(h.Pruefen(z), "sicherheit.ausschluss") == null);
                }
            }

            // ---------------------------------------------------------------- (e) Firewall
            // Wirksamkeitsprobe 2026-09-12: "if (!p.An)" auf "if (p.An)" gedreht -> "oeffentlich aus -> warn" rot,
            // gesund "kein Firewallbefund" rot.
            h.Gruppe("Sicherheit (e): Firewall");
            {
                var s = h.Bild("gepflanzt-sicherheit-firewall-oeffentlich-aus.json");
                if (Grundmenge(h, s, "firewall-oeffentlich-aus"))
                {
                    h.Ist("Vorbedingung: Profil 4 aus, 1 und 2 an", s.Sicherheit.Firewall.Single(p => p.Profil == 4).An == false && s.Sicherheit.Firewall.Where(p => p.Profil != 4).All(p => p.An));
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var aus = Harness.AlleMit(erg, "sicherheit.firewall.aus").ToList();
                    h.Ist("genau ein warn-Befund, fuer Profil 4", aus.Count == 1 && aus[0].Zustand == Zustand.Warn && aus[0].Schluessel == "sicherheit.firewall.aus.4");
                    h.Ist("Titel nennt Oeffentlich", aus.Count == 1 && aus[0].Titel.Contains("Öffentlich"));
                    h.Ist("Massnahme Firewall einschalten", aus.Count == 1 && aus[0].Massnahmen.Contains("sicherheit.firewall.einschalten"));
                    h.Ist("kein ok-Befund 'Firewall an'", Harness.Einer(erg, "sicherheit.firewall.an") == null);
                    // Eingehend = Allow (1) im privaten Profil -> warn.
                    s.Sicherheit.Firewall.Single(p => p.Profil == 4).An = true;
                    s.Sicherheit.Firewall.Single(p => p.Profil == 2).Eingehend = 1;
                    var erg2 = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg2));
                    h.Ist("Gegenprobe: alle an -> kein 'aus'-Befund", Harness.Einer(erg2, "sicherheit.firewall.aus") == null);
                    h.Ist("Eingehend Allow (1) im Profil 2 -> warn", Harness.Einer(erg2, "sicherheit.firewall.eingehend.2") != null && Harness.Einer(erg2, "sicherheit.firewall.eingehend.2").Zustand == Zustand.Warn);
                    // Rueckfall-Bild: Eingehend null (Persistent Store) -> kein Eingehend-Befund, ok-Befund ohne Block-Aussage.
                    foreach (var p in s.Sicherheit.Firewall) p.Eingehend = null;
                    var erg3 = h.Pruefen(s);
                    h.Ist("Eingehend null -> kein Eingehend-Befund, aber 'Firewall an'", Harness.Einer(erg3, "sicherheit.firewall.eingehend") == null && Harness.Einer(erg3, "sicherheit.firewall.an") != null);
                    // Befund 7 (13.09.2026): aus-Zweig im Rueckfall nannte INetFwPolicy2 als Quelle, obwohl MSFT_NetFirewallProfile gelesen wurde.
                    s.Sicherheit.Firewall.Single(p => p.Profil == 4).An = false;
                    var ausR = Harness.Einer(h.Pruefen(s), "sicherheit.firewall.aus.4");
                    h.Ist("Rückfall (Eingehend null) + aus -> Quelle MSFT_NetFirewallProfile.Enabled, Einheit Enabled, Detail nennt den Rückfall",
                        ausR != null && ausR.Quelle == "MSFT_NetFirewallProfile.Enabled" && ausR.Messwert.Einheit == "Enabled" && ausR.Detail.Any(d => d.Contains("Rückfall")), ausR == null ? "fehlt" : ausR.Quelle);
                    if (ausR != null) alleBefunde.Add(ausR);
                    s.Sicherheit.Firewall.Single(p => p.Profil == 4).Eingehend = 0;
                    var ausW = Harness.Einer(h.Pruefen(s), "sicherheit.firewall.aus.4");
                    h.Ist("Gegenprobe: Eingehend gelesen + aus -> Quelle INetFwPolicy2.FirewallEnabled[4], Einheit FirewallEnabled", ausW != null && ausW.Quelle == "INetFwPolicy2.FirewallEnabled[4]" && ausW.Messwert.Einheit == "FirewallEnabled" && !ausW.Detail.Any(d => d.Contains("Rückfall")));
                    s.Sicherheit.Firewall.Single(p => p.Profil == 4).An = true;
                    // Keine Profile -> Fehlend.
                    s.Sicherheit.Firewall.Clear();
                    var erg4 = h.Pruefen(s);
                    h.Ist("keine Profile -> Fehlend 'Firewall-Status', kein Befund", Harness.Einer(erg4, "sicherheit.firewall") == null && Harness.Bereich(erg4, B).Fehlend.Contains("Firewall-Status"));
                }
            }

            // ---------------------------------------------------------------- (f) UAC
            // Wirksamkeitsprobe 2026-09-12: "si.EnableLua.Value == 0" auf "== 1" gedreht -> "EnableLUA 0 -> bad" rot,
            // gesund "kein UAC-Befund" rot.
            h.Gruppe("Sicherheit (f): Benutzerkontensteuerung");
            {
                var s = h.Bild("gepflanzt-sicherheit-uac-aus.json");
                if (Grundmenge(h, s, "uac-aus"))
                {
                    h.Ist("Vorbedingung: EnableLua 0", s.Sicherheit.EnableLua == 0);
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var b = Harness.Einer(erg, "sicherheit.uac.aus");
                    h.Ist("EnableLUA 0 -> bad", b != null && b.Zustand == Zustand.Bad);
                    h.Ist("Massnahme UAC Standard, Detail nennt Neustart", b != null && b.Massnahmen.Contains("sicherheit.uac.standard") && b.Detail.Any(d => d.Contains("Neustart")));
                    h.Ist("EnableLUA 0: Rat nennt den Neustart", b != null && b.Rat.Contains("Neustart"));
                    h.Ist("Bereich ist bad", Harness.Bereich(erg, B).Zustand == Zustand.Bad);
                    s.Sicherheit.EnableLua = 1; s.Sicherheit.ConsentAdmin = 0;
                    var b2 = Harness.Einer(h.Pruefen(s), "sicherheit.uac.aus");
                    h.Ist("ConsentPromptBehaviorAdmin 0 -> bad mit Quelle ConsentPromptBehaviorAdmin", b2 != null && b2.Zustand == Zustand.Bad && b2.Quelle.Contains("ConsentPromptBehaviorAdmin"));
                    // Befund 6 (13.09.2026): die Nachfragestufe wirkt sofort, nur EnableLUA braucht den Neustart.
                    h.Ist("ConsentPromptBehaviorAdmin 0: weder Rat noch Detail nennen einen Neustart", b2 != null && !b2.Rat.Contains("Neustart") && !b2.Detail.Any(d => d.Contains("Neustart")), b2 == null ? "" : b2.Rat);
                    if (b2 != null) alleBefunde.Add(b2);
                    s.Sicherheit.EnableLua = null;
                    var b3 = Harness.Einer(h.Pruefen(s), "sicherheit.uac.aus");
                    h.Ist("EnableLUA fehlt (Standard) + Consent 0 -> bad ohne Neustart", b3 != null && b3.Zustand == Zustand.Bad && !b3.Rat.Contains("Neustart"));
                    s.Sicherheit.EnableLua = 1;
                    s.Sicherheit.ConsentAdmin = 5;
                    h.Ist("Gegenprobe: 1/5 -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.uac") == null);
                    s.Sicherheit.EnableLua = null; s.Sicherheit.ConsentAdmin = null;
                    var ergU = h.Pruefen(s);
                    h.Ist("beide null -> kein Befund, Fehlend 'Benutzerkontensteuerung'", Harness.Einer(ergU, "sicherheit.uac") == null && Harness.Bereich(ergU, B).Fehlend.Contains("Benutzerkontensteuerung"));
                }
            }

            // ---------------------------------------------------------------- SmartScreen, Secure Boot (aus gesund)
            // Wirksamkeitsprobe 2026-09-12: 'si.SmartScreen != "Off"' auf '== "Off"' gedreht -> "Off -> warn" rot.
            h.Gruppe("Sicherheit: SmartScreen und Secure Boot");
            {
                var s = h.Bild("gepflanzt-sicherheit-gesund.json");
                if (Grundmenge(h, s, "gesund"))
                {
                    s.Sicherheit.SmartScreen = "Off";
                    var b = Harness.Einer(h.Pruefen(s), "sicherheit.smartscreen.aus");
                    h.Ist("SmartScreen Off -> warn mit Massnahme", b != null && b.Zustand == Zustand.Warn && b.Massnahmen.Contains("sicherheit.smartscreen.warn"));
                    if (b != null) alleBefunde.Add(b);
                    s.Sicherheit.SmartScreen = "Block";
                    h.Ist("Gegenprobe: Block -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.smartscreen") == null);
                    s.Sicherheit.SmartScreen = null;
                    var ergU = h.Pruefen(s);
                    h.Ist("null -> kein Befund, Fehlend 'SmartScreen'", Harness.Einer(ergU, "sicherheit.smartscreen") == null && Harness.Bereich(ergU, B).Fehlend.Contains("SmartScreen"));
                    s.Sicherheit.SmartScreen = "Warn";

                    s.Sicherheit.SecureBoot = false;
                    var sb = Harness.Einer(h.Pruefen(s), "sicherheit.secureboot.aus");
                    h.Ist("Secure Boot aus bei UEFI -> ok-Hinweis, kein Problem", sb != null && sb.Zustand == Zustand.Ok && !sb.IstProblem);
                    if (sb != null) alleBefunde.Add(sb);
                    s.Hardware.Uefi = null;
                    h.Ist("Gegenprobe: UEFI unbekannt -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.secureboot") == null);
                    // Befund 8 (13.09.2026): Legacy-BIOS heisst Uefi false; UEFI ohne SecureBoot-Wert ist "nicht geprueft", nicht still.
                    s.Hardware.Uefi = false; s.Sicherheit.SecureBoot = null;
                    var ergL = h.Pruefen(s);
                    h.Ist("Gegenprobe: SecureBoot null bei Legacy-BIOS (Uefi false) -> kein Befund, kein Fehlend", Harness.Einer(ergL, "sicherheit.secureboot") == null && !Harness.Bereich(ergL, B).Fehlend.Contains("Secure Boot"));
                    s.Hardware.Uefi = true;
                    var ergF = h.Pruefen(s);
                    h.Ist("SecureBoot null bei UEFI -> kein Befund, aber Fehlend 'Secure Boot'", Harness.Einer(ergF, "sicherheit.secureboot") == null && Harness.Bereich(ergF, B).Fehlend.Contains("Secure Boot"), string.Join("; ", Harness.Bereich(ergF, B).Fehlend));
                    s.Sicherheit.SecureBoot = true;
                }
            }

            // ---------------------------------------------------------------- (g) Funde
            // Wirksamkeitsprobe 2026-09-12: "if (f.Aktiv)" auf "if (!f.Aktiv)" gedreht -> "aktiver Fund -> bad" rot,
            // "PUA inaktiv -> kein Problem" rot.
            h.Gruppe("Sicherheit (g): Funde des Virenschutzes");
            {
                var s = h.Bild("gepflanzt-sicherheit-fund-aktiv.json");
                if (Grundmenge(h, s, "fund-aktiv"))
                {
                    h.Ist("Vorbedingung: ein aktiver Fund", s.Sicherheit.Defender.Funde.Count == 1 && s.Sicherheit.Defender.Funde[0].Aktiv);
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var b = Harness.Einer(erg, "sicherheit.fund.aktiv");
                    h.Ist("aktiver Fund -> bad mit Namen im Satz", b != null && b.Zustand == Zustand.Bad && b.Satz.Contains("Trojan:Win32/Beispiel.A"));
                    h.Ist("Satz nennt 'gestern' (Aufzeichnungszeit, nicht heute)", b != null && b.Satz.Contains(" gestern"), b == null ? "" : b.Satz);
                    h.Ist("Bereich ist bad", Harness.Bereich(erg, B).Zustand == Zustand.Bad);
                    // Gegenprobe: derselbe Fund inaktiv, Schwere 4, gestern -> warn (beseitigt, aber frisch).
                    s.Sicherheit.Defender.Funde[0].Aktiv = false;
                    var erg2 = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg2));
                    h.Ist("inaktiv, Schwere 4, 1 Tag her -> kein bad, ein warn 'schwer'", Harness.Einer(erg2, "sicherheit.fund.aktiv") == null && Harness.Einer(erg2, "sicherheit.fund.schwer") != null && Harness.Einer(erg2, "sicherheit.fund.schwer").Zustand == Zustand.Warn);
                    s.Sicherheit.Defender.Funde[0].ZeitUtc = "2026-06-01T00:00:00Z";
                    h.Ist("Gegenprobe: schwerer Fund vor 102 Tagen -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.fund") == null);
                    s.Sicherheit.Defender.Funde[0].ZeitUtc = "2026-09-10T12:00:00Z";
                    s.Sicherheit.Defender.Funde[0].Schwere = 2;
                    h.Ist("Gegenprobe: Schwere 2 -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.fund") == null);
                }
                var p = h.Bild("gepflanzt-sicherheit-fund-pua.json");
                if (Grundmenge(h, p, "fund-pua"))
                {
                    h.Ist("Vorbedingung: inaktive PUA (Kategorie 27)", p.Sicherheit.Defender.Funde.Count == 1 && !p.Sicherheit.Defender.Funde[0].Aktiv && p.Sicherheit.Defender.Funde[0].Kategorie == 27);
                    var erg = h.Pruefen(p);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var b = Harness.Einer(erg, "sicherheit.fund.pua");
                    h.Ist("PUA inaktiv -> ok-Hinweis, kein Problem", b != null && b.Zustand == Zustand.Ok && KeinProblem(erg, "sicherheit.fund"));
                    h.Ist("Bereich ist ok", Harness.Bereich(erg, B).Zustand == Zustand.Ok);
                    p.Sicherheit.Defender.Funde[0].ZeitUtc = "2025-01-01T00:00:00Z";
                    h.Ist("Gegenprobe: PUA vor 618 Tagen -> kein Hinweis mehr", Harness.Einer(h.Pruefen(p), "sicherheit.fund") == null);
                }
            }

            // ---------------------------------------------------------------- (h) Konten
            // Wirksamkeitsprobe 2026-09-12: "if (k.Deaktiviert || k.KennwortNoetig) continue;" auf
            // "if (k.Deaktiviert && k.KennwortNoetig)" gedreht -> gesund "kein Kontobefund" rot.
            h.Gruppe("Sicherheit (h): lokale Konten");
            {
                var s = h.Bild("gepflanzt-sicherheit-konto-ohne-kennwort.json");
                if (Grundmenge(h, s, "konto-ohne-kennwort"))
                {
                    h.Ist("Vorbedingung: RID 1001 aktiv ohne Kennwortpflicht, 501 deaktiviert ohne Kennwortpflicht",
                        s.Sicherheit.Konten.Any(k => k.Rid == 1001 && !k.Deaktiviert && !k.KennwortNoetig) && s.Sicherheit.Konten.Any(k => k.Rid == 501 && k.Deaktiviert && !k.KennwortNoetig));
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var ohne = Harness.AlleMit(erg, "sicherheit.konto.ohnekennwort").ToList();
                    h.Ist("genau ein warn-Befund, fuer RID 1001", ohne.Count == 1 && ohne[0].Zustand == Zustand.Warn && ohne[0].Schluessel == "sicherheit.konto.ohnekennwort.1001");
                    h.Ist("Detail nennt den Kontotyp lokal", ohne.Count == 1 && ohne[0].Detail.Any(d => d.Contains("lokal")));
                    // Konzept 4.4: PasswordRequired=false ist bei einem Microsoft-Konto normal - kein Befund.
                    // Gemessen 12.09.2026: das Konto des Betreibers ist LocalConnected (Typ 4) und war ein Fehlalarm.
                    s.Sicherheit.Konten.Single(k => k.Rid == 1001).MicrosoftKonto = true;
                    var ergMsa = h.Pruefen(s);
                    h.Ist("Microsoft-Konto ohne lokale Kennwortpflicht -> kein Befund", Harness.Einer(ergMsa, "sicherheit.konto.ohnekennwort.1001") == null);
                    h.Ist("Microsoft-Konto -> auch nichts unter 'nicht geprueft'", !ergMsa.Single(x => x.Bereich == B).Fehlend.Any(f => f.Contains("Kennwortpflicht")));
                    s.Sicherheit.Konten.Single(k => k.Rid == 1001).MicrosoftKonto = null;
                    var ergNull = h.Pruefen(s);
                    h.Ist("Kontotyp unbekannt -> kein Befund, sondern 'nicht geprueft'", Harness.Einer(ergNull, "sicherheit.konto.ohnekennwort.1001") == null
                        && ergNull.Single(x => x.Bereich == B).Fehlend.Any(f => f.Contains("Kennwortpflicht")));
                    s.Sicherheit.Konten.Single(k => k.Rid == 1001).MicrosoftKonto = false;
                    h.Ist("RID 501 (Gast, deaktiviert) erzeugt nichts", Harness.Einer(erg, "sicherheit.konto.ohnekennwort.501") == null);
                    h.Ist("RID 500 deaktiviert erzeugt nichts", Harness.Einer(erg, "sicherheit.konto.administrator") == null);
                    // Eingebauter Administrator aktiv -> warn.
                    s.Sicherheit.Konten.Single(k => k.Rid == 500).Deaktiviert = false;
                    var erg2 = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg2));
                    h.Ist("RID 500 aktiv -> warn 'Administrator aktiv'", Harness.Einer(erg2, "sicherheit.konto.administrator.aktiv") != null && Harness.Einer(erg2, "sicherheit.konto.administrator.aktiv").Zustand == Zustand.Warn);
                    // Gegenproben: 1001 deaktiviert -> nichts; 1001 mit Kennwort -> nichts.
                    s.Sicherheit.Konten.Single(k => k.Rid == 500).Deaktiviert = true;
                    s.Sicherheit.Konten.Single(k => k.Rid == 1001).Deaktiviert = true;
                    h.Ist("Gegenprobe: 1001 deaktiviert -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.konto") == null);
                    s.Sicherheit.Konten.Single(k => k.Rid == 1001).Deaktiviert = false;
                    s.Sicherheit.Konten.Single(k => k.Rid == 1001).KennwortNoetig = true;
                    h.Ist("Gegenprobe: 1001 mit Kennwortpflicht -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.konto") == null);
                    s.Sicherheit.Konten.Clear();
                    var ergU = h.Pruefen(s);
                    h.Ist("keine Konten -> Fehlend 'Benutzerkonten'", Harness.Einer(ergU, "sicherheit.konto") == null && Harness.Bereich(ergU, B).Fehlend.Contains("Benutzerkonten"));
                }
            }

            // ---------------------------------------------------------------- RDP, SMB1 (aus gesund)
            // Wirksamkeitsprobe 2026-09-12: "si.RdpNla == false" auf "== true" gedreht -> "RDP ohne NLA -> warn" rot;
            // "Smb1Server.Value == Smb1Aktiv" auf "== 2" gedreht -> "SMB1-Server aktiv -> warn" rot.
            h.Gruppe("Sicherheit: Remotedesktop und SMB1");
            {
                var s = h.Bild("gepflanzt-sicherheit-gesund.json");
                if (Grundmenge(h, s, "gesund"))
                {
                    s.Sicherheit.RdpAn = true; s.Sicherheit.RdpNla = false;
                    var b = Harness.Einer(h.Pruefen(s), "sicherheit.rdp.ohnenla");
                    h.Ist("RDP an ohne NLA -> warn", b != null && b.Zustand == Zustand.Warn);
                    if (b != null) alleBefunde.Add(b);
                    s.Sicherheit.RdpNla = true;
                    var b2 = Harness.Einer(h.Pruefen(s), "sicherheit.rdp.an");
                    h.Ist("RDP an mit NLA -> ok-Hinweis", b2 != null && b2.Zustand == Zustand.Ok && Harness.Einer(h.Pruefen(s), "sicherheit.rdp.ohnenla") == null);
                    if (b2 != null) alleBefunde.Add(b2);
                    s.Windows.Edition = "Core";
                    h.Ist("Gegenprobe: Home-Edition -> kein RDP-Befund", Harness.Einer(h.Pruefen(s), "sicherheit.rdp") == null);
                    s.Windows.Edition = "Professional";
                    s.Sicherheit.RdpAn = false;
                    h.Ist("Gegenprobe: RDP aus -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.rdp") == null);
                    // Befund 8 (13.09.2026): RdpAn null auf Pro ist "nicht geprueft", auf Home nichts.
                    s.Sicherheit.RdpAn = null;
                    var ergN = h.Pruefen(s);
                    h.Ist("RdpAn null auf Pro -> kein Befund, Fehlend 'Remotedesktop-Einstellung'", Harness.Einer(ergN, "sicherheit.rdp") == null && Harness.Bereich(ergN, B).Fehlend.Contains("Remotedesktop-Einstellung"));
                    s.Windows.Edition = "Core";
                    h.Ist("RdpAn null auf Home -> auch kein Fehlend", !Harness.Bereich(h.Pruefen(s), B).Fehlend.Contains("Remotedesktop-Einstellung"));
                    s.Windows.Edition = "Professional"; s.Sicherheit.RdpAn = false;

                    s.Sicherheit.Smb1Server = 1;
                    var sm = Harness.Einer(h.Pruefen(s), "sicherheit.smb1.server");
                    h.Ist("SMB1-Server aktiv (InstallState 1) -> warn, Detail nennt NAS", sm != null && sm.Zustand == Zustand.Warn && sm.Detail.Any(d => d.Contains("NAS")));
                    h.Ist("Serverkonfiguration nicht gelesen (null) -> Warnung bleibt, Detail sagt 'nicht gelesen'", sm != null && sm.Schluessel == "sicherheit.smb1.server" && sm.Detail.Any(d => d.Contains("nicht gelesen")));
                    if (sm != null) alleBefunde.Add(sm);
                    s.Sicherheit.Smb1ServerAktiv = true;
                    var smT = Harness.Einer(h.Pruefen(s), "sicherheit.smb1.server");
                    h.Ist("EnableSMB1Protocol true -> warn, Quelle nennt MSFT_SmbServerConfiguration", smT != null && smT.Zustand == Zustand.Warn && smT.Quelle.Contains("MSFT_SmbServerConfiguration"));
                    s.Sicherheit.Smb1ServerAktiv = null;
                    s.Sicherheit.Smb1Server = 3;
                    h.Ist("Gegenprobe: InstallState 3 (Absent) -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.smb1") == null);
                    s.Sicherheit.Smb1Client = 1;
                    var sc = Harness.Einer(h.Pruefen(s), "sicherheit.smb1.client");
                    h.Ist("SMB1-Client aktiv -> nur ok-Hinweis", sc != null && sc.Zustand == Zustand.Ok);
                    if (sc != null) alleBefunde.Add(sc);
                    s.Sicherheit.Smb1Client = null; s.Sicherheit.Smb1Server = null;
                    var ergU = h.Pruefen(s);
                    h.Ist("beide null -> Fehlend SMB1", Harness.Einer(ergU, "sicherheit.smb1") == null && Harness.Bereich(ergU, B).Fehlend.Any(f => f.Contains("SMB1")));
                }
            }

            // ---------------------------------------------------------------- SMB1: Feature installiert, Server abgeschaltet
            // Befund 2 (13.09.2026): Feature 1 allein warnte "SMB1 ist aktiv", obwohl Set-SmbServerConfiguration
            // -EnableSMB1Protocol $false den Server abgeschaltet hatte.
            h.Gruppe("Sicherheit: SMB1-Feature installiert, Server per EnableSMB1Protocol abgeschaltet");
            {
                var s = h.Bild("gepflanzt-sicherheit-smb1-abgeschaltet.json");
                if (Grundmenge(h, s, "smb1-abgeschaltet"))
                {
                    h.Ist("Vorbedingung: Smb1Server 1, Smb1ServerAktiv false", s.Sicherheit.Smb1Server == 1 && s.Sicherheit.Smb1ServerAktiv == false);
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var alle = Harness.AlleMit(erg, "sicherheit.smb1").ToList();
                    h.Ist("genau ein SMB1-Befund: 'server.abgeschaltet' als ok-Hinweis, keine Warnung", alle.Count == 1 && alle[0].Schluessel == "sicherheit.smb1.server.abgeschaltet" && alle[0].Zustand == Zustand.Ok, string.Join(", ", alle.Select(b => b.Schluessel + "=" + b.Zustand)));
                    h.Ist("Satz sagt, dass keine SMB1-Freigaben angeboten werden und das Feature weg kann", alle.Count == 1 && alle[0].Satz.Contains("keine SMB1-Freigaben") && alle[0].Satz.Contains("kann weg"));
                    h.Ist("Quelle ist MSFT_SmbServerConfiguration.EnableSMB1Protocol", alle.Count == 1 && alle[0].Quelle == "MSFT_SmbServerConfiguration.EnableSMB1Protocol");
                    h.Ist("Bereich ist ok", Harness.Bereich(erg, B).Zustand == Zustand.Ok);
                    // Gegenprobe: Feature aus (2) -> auch der Hinweis verschwindet.
                    s.Sicherheit.Smb1Server = 2;
                    h.Ist("Gegenprobe: Feature 2 (Disabled) + Server aus -> kein Befund", Harness.Einer(h.Pruefen(s), "sicherheit.smb1") == null);
                }
            }

            // ---------------------------------------------------------------- RDP per Richtlinie
            // Befund 3 (13.09.2026): die Richtlinie Policies\...\Terminal Services ueberstimmt Control\Terminal Server;
            // stammt der Schalter daher, ist die Einstellung verwaltet und "unter Einstellungen ausschalten" falsch.
            h.Gruppe("Sicherheit: Remotedesktop per Richtlinie eingeschaltet");
            {
                var s = h.Bild("gepflanzt-sicherheit-rdp-richtlinie.json");
                if (Grundmenge(h, s, "rdp-richtlinie"))
                {
                    h.Ist("Vorbedingung: RdpAn, RdpNla, Herkunft richtlinie, Edition Pro", s.Sicherheit.RdpAn == true && s.Sicherheit.RdpNla == true && s.Sicherheit.RdpHerkunft == "richtlinie" && !s.Windows.IstHome);
                    var erg = h.Pruefen(s);
                    alleBefunde.AddRange(Sicherheit(erg));
                    var an = Harness.Einer(erg, "sicherheit.rdp.an");
                    h.Ist("ok-Befund 'RDP an' mit Quelle Policies\\Terminal Services.fDenyTSConnections", an != null && an.Zustand == Zustand.Ok && an.Quelle == "Policies\\Terminal Services.fDenyTSConnections", an == null ? "fehlt" : an.Quelle);
                    h.Ist("Satz sagt, dass die Einstellung verwaltet ist und nennt NLA", an != null && an.Satz.Contains("verwaltet") && an.Satz.Contains("NLA"), an == null ? "" : an.Satz);
                    h.Ist("Detail nennt die Herkunft Richtlinie", an != null && an.Detail.Any(d => d.Contains("Richtlinie")));
                    // NLA aus bei verwaltetem RDP: warn, der Rat verweist fuer das Ausschalten auf die Richtlinie.
                    s.Sicherheit.RdpNla = false;
                    var ohne = Harness.Einer(h.Pruefen(s), "sicherheit.rdp.ohnenla");
                    h.Ist("NLA aus + Richtlinie -> warn, Rat nennt die Richtlinie", ohne != null && ohne.Zustand == Zustand.Warn && ohne.Rat.Contains("Richtlinie") && ohne.Detail.Any(d => d.Contains("Richtlinie")), ohne == null ? "" : ohne.Rat);
                    if (ohne != null) alleBefunde.Add(ohne);
                    // Gegenprobe: Herkunft lokal -> die alte Quelle und der alte Rat.
                    s.Sicherheit.RdpHerkunft = "lokal";
                    var ohneL = Harness.Einer(h.Pruefen(s), "sicherheit.rdp.ohnenla");
                    h.Ist("Gegenprobe: lokal -> Rat ohne Richtlinie", ohneL != null && !ohneL.Rat.Contains("Richtlinie"));
                    s.Sicherheit.RdpNla = true;
                    var anL = Harness.Einer(h.Pruefen(s), "sicherheit.rdp.an");
                    h.Ist("Gegenprobe: lokal -> Quelle Terminal Server.fDenyTSConnections, Satz ohne 'verwaltet'", anL != null && anL.Quelle == "Terminal Server.fDenyTSConnections" && !anL.Satz.Contains("verwaltet"));
                }
            }

            // ---------------------------------------------------------------- keine Daten
            // Wirksamkeitsprobe 2026-09-12: DatenVorhanden fest auf true gesetzt -> "Bereich ist unknown" rot.
            h.Gruppe("Sicherheit: keine Daten (kein Defender, kein Fremdschutz, keine Firewall)");
            {
                var s = h.Bild("gepflanzt-sicherheit-keine-daten.json");
                if (Grundmenge(h, s, "keine-daten", defenderNoetig: false))
                {
                    h.Ist("Vorbedingung: Defender null, DrittAv leer, Firewall leer, 4 Fehlereintraege", s.Sicherheit.Defender == null && s.Sicherheit.DrittAv.Count == 0 && s.Sicherheit.Firewall.Count == 0 && s.Fehlerliste.Count == 4);
                    var erg = h.Pruefen(s);
                    var ber = Harness.Bereich(erg, B);
                    h.Ist("DatenVorhanden false", !ber.DatenVorhanden);
                    h.Ist("Bereich ist unknown, nie ok, nie bad", ber.Zustand == Zustand.Unknown, ber.Zustand);
                    h.Ist("kein Befund Virenschutz (weder an noch aus)", Harness.Einer(erg, "sicherheit.virenschutz") == null);
                    h.Ist("Fehlend nennt Schutzstatus und Firewall", ber.Fehlend.Contains("Schutzstatus nicht lesbar") && ber.Fehlend.Contains("Firewall-Status"), string.Join("; ", ber.Fehlend));
                    h.Ist("kein warn/bad-Befund aus Konten/UAC allein macht den Bereich nicht bad", ber.Zustand != Zustand.Bad);
                }
            }

            // ---------------------------------------------------------------- Saetze
            h.Gruppe("Sicherheit: Saetze tragen Zahl, Bedingung oder Absage");
            h.Ist("Grundmenge: mindestens 25 Befunde ueber alle Bilder gesammelt", alleBefunde.Count >= 25, alleBefunde.Count.ToString());
            var einmalig = alleBefunde.GroupBy(b => b.Schluessel).Select(g => g.First()).ToList();
            h.SaetzeTragen(einmalig);
            h.Ist("jeder Befund traegt Bereich, Quelle und Messwert", einmalig.All(b => b.Bereich == B && !string.IsNullOrEmpty(b.Quelle) && b.Messwert != null),
                string.Join(", ", einmalig.Where(b => b.Bereich != B || string.IsNullOrEmpty(b.Quelle) || b.Messwert == null).Select(b => b.Schluessel)));
            h.Ist("kein Titel enthaelt ein gerades Anfuehrungszeichen", einmalig.All(b => (b.Titel ?? "").IndexOf('"') < 0 && (b.Satz ?? "").IndexOf('"') < 0 && (b.Rat ?? "").IndexOf('"') < 0));
            h.Ist("Fachbegriffe WMI/Bugcheck/NVMe stehen nicht im Laientext", einmalig.All(b => !((b.Titel ?? "") + (b.Satz ?? "") + (b.Rat ?? "")).Contains("WMI")));
        }
    }
}
