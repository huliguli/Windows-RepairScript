using System;
using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;
using LeistungRegel = WartungsToolbox.Kern.Regeln.Leistung;
using NetzRegel = WartungsToolbox.Kern.Regeln.Netz;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer den Bereich Leistung (Arbeitsspeicher, Autostart, Winlogon, AppInit) gegen
    /// gepflanzte Systembilder (tests/aufzeichnungen/gepflanzt-stabilitaet-speicher-*.json,
    /// -autostart*.json, -shell.json, -userinit.json, gepflanzt-leistung-*.json). Bis 12.09.2026
    /// Teil von StabilitaetProben.cs.
    ///
    /// Wirksamkeitsproben am 12.09.2026 (Regel absichtlich kaputt gemacht, Rot beobachtet, zurueck):
    ///   - Speicher: VerfuegbarBadMb-Vergleich entfernt -> "320 MB von 16 GB = bad" rot (2 % liegt ueber 1 %).
    ///   - Autostart: "Aktiviert != false" entfernt -> Gegenbild (deaktivierte Eintraege) rot.
    ///   - Winlogon: Shell-Vergleich auf "cmd.exe" -> Gegenbild rot, Shell-Bild rot.
    ///   - AppInit: Secure-Boot-Bedingung entfernt -> "bei Secure Boot ok, nicht warn" rot.
    /// Wirksamkeitsproben am 13.09.2026:
    ///   - Speicher: Max() durch Min() ersetzt -> Spitzen-Bild [300, 8000, 8000] rot ("kein bad").
    ///   - Speicher: VerfuegbarWarnPct-Zweig entfernt -> Warn-Bild rot ("verfuegbar = warn"); CommitWarnPct entfernt -> "Commit = warn" rot.
    ///   - Speicher: AbstandMs-Bedingung entfernt -> Eng-Bild (3 Werte in 1 s) rot ("Momentaufnahme").
    ///   - Autostart: Eigen-Filter entfernt -> Autostart-Bild rot (21 statt 20); DateiVorhanden-Filter entfernt -> rot (22).
    ///   - Autostart: fehlerliste-Ableitung entfernt -> Luecken-Bild rot ("unknown statt ok").
    ///   - Winlogon: Pruefung weiterer Teile entfernt -> Angehaengt-Bild rot; EndsWith(system32) auf Contains(userinit.exe) -> Kopie-Bild rot.
    ///   - AppInit: LoadAppInit-Zweig entfernt -> Schalter-Bild rot ("ok, nicht warn").
    /// </summary>
    public static class LeistungProben
    {
        public static void Laufen(Harness h)
        {
            var alle = new List<Befund>();
            SpeicherBad(h, alle);
            SpeicherMomentaufnahme(h, alle);
            SpeicherWarn(h, alle);
            SpeicherSpitze(h, alle);
            SpeicherEng(h, alle);
            Autostart(h, alle);
            AutostartGrenzen(h, alle);
            Pruefwerte(h);
            AutostartOk(h, alle);
            AutostartLuecke(h, alle);
            Shell(h, alle);
            Userinit(h, alle);
            UserinitAngehaengt(h, alle);
            UserinitKopie(h, alle);
            AppInitSchalter(h, alle);

            h.Gruppe("Leistung: jeder Satz trägt Zahl, Bedingung oder Absage");
            h.SaetzeTragen(alle);
            h.Ist("Grundmenge: mindestens 20 Befunde über alle Bilder", alle.Count >= 20, alle.Count.ToString());
            h.Ist("kein gerades Anführungszeichen in Sätzen", alle.All(b => (b.Satz ?? "").IndexOf('"') < 0 && (b.Rat ?? "").IndexOf('"') < 0));
        }

        static List<Befund> Sammeln(List<Befund> alle, List<BereichErgebnis> erg)
        {
            var eigene = Harness.Befunde(erg, Bereich.Stabilitaet).Concat(Harness.Befunde(erg, Bereich.Leistung)).ToList();
            alle.AddRange(eigene);
            return eigene;
        }

        // ---------------------------------------------------------------- (g) Speicher

        static void SpeicherBad(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (g): höchstens 320 MB von 16 GB frei in 3 Messungen im Abstand von 4 s = bad; Commit 84-86 % = bad");
            var s = h.Bild("gepflanzt-stabilitaet-speicher-bad.json"); if (s == null) return;
            h.Ist("Grundmenge: 3 Messungen, Minimum 300, Maximum 320 MB, Abstand >= " + Schwellen.MessabstandMinMs + " ms", s.Leistung.VerfuegbarMB.Count == Schwellen.MessungenNoetig && s.Leistung.VerfuegbarMB.Min() == 300 && s.Leistung.VerfuegbarMB.Max() == 320 && s.Leistung.AbstandMs >= Schwellen.MessabstandMinMs);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.speicher.verfuegbar");
            h.Ist("verfügbar = bad", b != null && b.Zustand == Zustand.Bad, b != null ? b.Zustand : null);
            if (b == null) return;
            h.Ist("Messwert 320 (günstigste Messung trägt das Urteil)", b.Messwert.Wert == "320", b.Messwert.Wert);
            h.Ist("Satz nennt „höchstens 320 MB“", b.Satz.Contains("höchstens 320 MB"), b.Satz);
            h.Ist("Quelle ist der Leistungszähler", b.Quelle == "Memory\\Available MBytes", b.Quelle);
            h.Ist("Detail nennt den Abstand von 4 s", b.Detail.Any(d => d.Contains("im Abstand von 4 s")));
            h.Ist("Detail nennt den Speicherfresser", b.Detail.Any(d => d.Contains("speicherfresser") && d.Contains("PID 4321")));
            h.Ist("Rat vorhanden", b.Rat != null);
            var c = Harness.Einer(erg, "leistung.speicher.commit");
            h.Ist("Commit = bad (alle >= " + Schwellen.CommitBadPct + ")", c != null && c.Zustand == Zustand.Bad);
            h.Ist("keine Momentaufnahme", Harness.Einer(erg, "leistung.speicher.momentaufnahme") == null);
            h.Ist("Bereich Leistung = bad", Harness.Bereich(erg, Bereich.Leistung).Zustand == Zustand.Bad);
        }

        static void SpeicherMomentaufnahme(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (g2): dieselben 300 MB bei nur einer Messung = kein Urteil");
            var s = h.Bild("gepflanzt-stabilitaet-speicher-momentaufnahme.json"); if (s == null) return;
            h.Ist("Grundmenge: genau eine Messung mit 300 MB", s.Leistung.VerfuegbarMB.Count == 1 && s.Leistung.VerfuegbarMB[0] == 300);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var le = Harness.Bereich(erg, Bereich.Leistung);
            h.Ist("Daten vorhanden", le.DatenVorhanden);
            h.Ist("kein Befund leistung.speicher.verfuegbar", Harness.Einer(erg, "leistung.speicher.verfuegbar") == null);
            h.Ist("kein Befund leistung.speicher.commit", Harness.Einer(erg, "leistung.speicher.commit") == null);
            h.Ist("kein Problem-Befund im Bereich Leistung", !Harness.Befunde(erg, Bereich.Leistung).Any(b => b.IstProblem));
            var m = Harness.Einer(erg, "leistung.speicher.momentaufnahme");
            h.Ist("Momentaufnahme als unknown mit Zahl", m != null && m.Zustand == Zustand.Unknown && m.Satz.Contains("300 MB"));
            h.Ist("Bereich Leistung nicht bad", le.Zustand != Zustand.Bad, le.Zustand);
        }

        static void SpeicherWarn(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (g3): 1000-1100 MB von 16 GB (6-7 %) = warn, nicht bad; Commit 65-70 % = warn");
            var s = h.Bild("gepflanzt-stabilitaet-speicher-warn.json"); if (s == null) return;
            long gesamtMB = s.Leistung.RamGesamtKB / 1024;
            h.Ist("Grundmenge: 3 Messungen, alle über " + Schwellen.VerfuegbarBadMb + " MB und unter 10 %", s.Leistung.VerfuegbarMB.Count == Schwellen.MessungenNoetig && s.Leistung.VerfuegbarMB.All(v => v > Schwellen.VerfuegbarBadMb && v * 100.0 / gesamtMB < Schwellen.VerfuegbarWarnPct));
            h.Ist("Grundmenge: Commit zwischen " + Schwellen.CommitWarnPct + " und " + Schwellen.CommitBadPct, s.Leistung.CommitPct.All(v => v >= Schwellen.CommitWarnPct && v < Schwellen.CommitBadPct));
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.speicher.verfuegbar");
            h.Ist("verfügbar = warn, nicht bad", b != null && b.Zustand == Zustand.Warn, b != null ? b.Zustand : null);
            if (b == null) return;
            h.Ist("Satz nennt „wird es eng“ und 1,1 GB", b.Satz.Contains("wird es eng") && b.Satz.Contains("1,1 GB"), b.Satz);
            h.Ist("Messwert 1100", b.Messwert.Wert == "1100", b.Messwert.Wert);
            var c = Harness.Einer(erg, "leistung.speicher.commit");
            h.Ist("Commit = warn", c != null && c.Zustand == Zustand.Warn, c != null ? c.Zustand : null);
            h.Ist("Commit-Satz nennt 65 % (günstigste Messung)", c != null && c.Satz.Contains("65 %"), c != null ? c.Satz : null);
            h.Ist("Bereich Leistung = warn", Harness.Bereich(erg, Bereich.Leistung).Zustand == Zustand.Warn, Harness.Bereich(erg, Bereich.Leistung).Zustand);
        }

        static void SpeicherSpitze(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (g4): [300, 8000, 8000] MB = eine Spitze, kein Befund; Commit [85, 40, 40] = ok");
            var s = h.Bild("gepflanzt-stabilitaet-speicher-spitze.json"); if (s == null) return;
            h.Ist("Grundmenge: eine Messung unter " + Schwellen.VerfuegbarBadMb + " MB, zwei weit darüber", s.Leistung.VerfuegbarMB.Count == Schwellen.MessungenNoetig && s.Leistung.VerfuegbarMB.Min() < Schwellen.VerfuegbarBadMb && s.Leistung.VerfuegbarMB.Max() == 8000);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.speicher.verfuegbar");
            h.Ist("verfügbar = ok (kein bad, kein warn)", b != null && b.Zustand == Zustand.Ok, b != null ? b.Zustand : null);
            if (b == null) return;
            h.Ist("Messwert 8000 (günstigste Messung)", b.Messwert.Wert == "8000", b.Messwert.Wert);
            h.Ist("Satz nennt die Spitze (300 MB) und das günstigste Ergebnis (7,8 GB)", b.Satz.Contains("300 MB") && b.Satz.Contains("7,8 GB") && b.Satz.Contains("Spitze"), b.Satz);
            h.Ist("kein Rat bei ok", b.Rat == null);
            var c = Harness.Einer(erg, "leistung.speicher.commit");
            h.Ist("Commit = ok (Minimum 40 %)", c != null && c.Zustand == Zustand.Ok, c != null ? c.Zustand : null);
            h.Ist("Commit-Satz nennt 40 % und 85 %, widerspricht sich nicht", c != null && c.Satz.Contains("40 %") && c.Satz.Contains("85 %") && !c.Satz.Contains("höchstens 85"), c != null ? c.Satz : null);
            h.Ist("Bereich Leistung = ok", Harness.Bereich(erg, Bereich.Leistung).Zustand == Zustand.Ok, Harness.Bereich(erg, Bereich.Leistung).Zustand);
        }

        static void SpeicherEng(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (g5): 3 Messungen im Abstand von 1 s (WMI-Rückfall) = Momentaufnahme, kein Urteil; mit 4 s Abstand = bad");
            var s = h.Bild("gepflanzt-leistung-speicher-eng.json"); if (s == null) return;
            h.Ist("Grundmenge: 3 Messungen unter " + Schwellen.VerfuegbarBadMb + " MB, Abstand 1000 ms, Quelle wmi", s.Leistung.VerfuegbarMB.Count == Schwellen.MessungenNoetig && s.Leistung.VerfuegbarMB.Max() < Schwellen.VerfuegbarBadMb && s.Leistung.AbstandMs == 1000 && s.Leistung.SpeicherQuelle == "wmi");
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            h.Ist("kein Befund leistung.speicher.verfuegbar", Harness.Einer(erg, "leistung.speicher.verfuegbar") == null);
            h.Ist("kein Problem-Befund im Bereich Leistung", !Harness.Befunde(erg, Bereich.Leistung).Any(b => b.IstProblem));
            var m = Harness.Einer(erg, "leistung.speicher.momentaufnahme");
            h.Ist("Momentaufnahme als unknown, Satz nennt den Abstand von 1 s", m != null && m.Zustand == Zustand.Unknown && m.Satz.Contains("1 s"), m != null ? m.Satz : null);
            h.Ist("Quelle nennt Win32_PerfFormattedData_PerfOS_Memory", m != null && m.Quelle.Contains("Win32_PerfFormattedData_PerfOS_Memory"), m != null ? m.Quelle : null);
            // Gegenprobe: dieselben Werte mit ausreichendem Abstand sind eine Serie und damit bad.
            s.Leistung.AbstandMs = Schwellen.MessabstandMinMs;
            var erg2 = h.Pruefen(s);
            var b = Harness.Einer(erg2, "leistung.speicher.verfuegbar");
            h.Ist("Gegenprobe: mit " + Schwellen.MessabstandMinMs + " ms Abstand = bad", b != null && b.Zustand == Zustand.Bad, b != null ? b.Zustand : null);
            h.Ist("Gegenprobe: Quelle bleibt WMI", b != null && b.Quelle.Contains("Win32_PerfFormattedData_PerfOS_Memory"));
        }

        // ---------------------------------------------------------------- (h) Autostart

        static void Autostart(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (h): 20 aktive Fremd-Autostarts (14 Einträge + 2 Aufgaben + 4 Dienste) = warn mit Maßnahme; verwaiste, eigene und Microsoft-Einträge nicht gezählt");
            var s = h.Bild("gepflanzt-stabilitaet-autostart.json"); if (s == null) return;
            h.Ist("Grundmenge: 18 Einträge (2 ohne Datei, 1 über rundll32, 1 Skript im Startup-Ordner), 6 Aufgaben (1 eigene), 7 Dienste",
                s.Autostart.Eintraege.Count == 18 && s.Autostart.Eintraege.Count(x => x.DateiVorhanden == false) == 2 && s.Autostart.Eintraege.Any(x => x.Befehl.StartsWith("rundll32")) && s.Autostart.Eintraege.Any(x => x.Name == "start.bat")
                && s.Autostart.Aufgaben.Count == 6 && s.Autostart.Aufgaben.Count(t => t.Eigen) == 1 && s.Autostart.Dienste.Count == 7);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.autostart.anzahl");
            h.Ist("Befund vorhanden", b != null);
            if (b == null) return;
            h.Ist("Zustand warn (20 > " + Schwellen.AutostartsWarn + ")", b.Zustand == Zustand.Warn, b.Zustand);
            h.Ist("Messwert 20", b.Messwert.Wert == "20", b.Messwert.Wert);
            // M3-Entwurf 8.5: die Sammelkarte traegt keine Kennung, der Schalter haengt an den Einzelkarten.
            h.Ist("Sammelkarte ohne Kennung (keine Maßnahme, Rat bleibt)", b.Massnahmen.Count == 0 && b.Rat != null, string.Join(",", b.Massnahmen));
            h.Ist("Detail: Microsoft-Eintrag SecurityHealth nicht in der Liste", !b.Detail.Any(d => d.Contains("SecurityHealth")));
            h.Ist("Detail: deaktiviertes OneDrive nicht in der Liste", !b.Detail.Any(d => d.Contains("OneDrive =")));
            h.Ist("Detail: deaktivierte Aufgabe nicht in der Liste", !b.Detail.Any(d => d.Contains("FremdAbgeschaltet")));
            h.Ist("Detail: manueller Dienst nicht in der Liste", !b.Detail.Any(d => d.Contains("FremdManuell")));
            h.Ist("Detail: Dienst mit unbekannter Signatur (LSM) nicht in der Liste", !b.Detail.Any(d => d.Contains("LSM")));
            h.Ist("Detail: Herausgeber als Kurzname", b.Detail.Any(d => d.Contains("(Fremd GmbH)")));
            h.Ist("Detail: rundll32-Eintrag zählt als fremd (Ziel hook.dll)", b.Detail.Any(d => d.Contains("hook.dll") && d.Contains("ohne gültige Signatur")));
            h.Ist("Detail: Skript start.bat aus dem Startup-Ordner zählt", b.Detail.Any(d => d.Contains("start.bat")));
            h.Ist("Detail: verwaiste Einträge (Fremd11, Fremd12) unter „starten nichts“, nicht in der Zählliste", b.Detail.Any(d => d.Contains("starten nichts") && d.Contains("Fremd11") && d.Contains("Fremd12")) && !b.Detail.Any(d => d.Contains("Fremd11 =") && d.Contains("(ohne")));
            h.Ist("Detail: eigene Aufgabe WindowsWartung-Autostart nicht gezählt, aber genannt", b.Detail.Any(d => d.Contains("Eigene Aufgaben") && d.Contains("WindowsWartung-Autostart")) && !b.Detail.Any(d => d.StartsWith("Aufgabe \\WindowsWartung")));
            h.Ist("Detail: Hinweis, dass Aufgaben ohne Rechte unvollständig sind", b.Detail.Any(d => d.Contains("ohne Administratorrechte")));
            AutostartEinzelkarten(h, s, erg);
            h.Ist("Speicher ok (40 GB von 63 GB)", Harness.Einer(erg, "leistung.speicher.verfuegbar") != null && Harness.Einer(erg, "leistung.speicher.verfuegbar").Zustand == Zustand.Ok);
            h.Ist("Speicher-Satz sagt „mindestens“ (schlechteste Messung reicht)", Harness.Einer(erg, "leistung.speicher.verfuegbar") != null && Harness.Einer(erg, "leistung.speicher.verfuegbar").Satz.Contains("mindestens"));
            h.Ist("Commit ok (38 %)", Harness.Einer(erg, "leistung.speicher.commit") != null && Harness.Einer(erg, "leistung.speicher.commit").Zustand == Zustand.Ok);
            h.Ist("Commit-Satz nennt höchstens 39 %", Harness.Einer(erg, "leistung.speicher.commit") != null && Harness.Einer(erg, "leistung.speicher.commit").Satz.Contains("höchstens 39 %"));
            h.Ist("Gegenprobe Shell: explorer.exe = kein Befund", Harness.Einer(erg, "leistung.winlogon") == null);
            h.Ist("Gegenprobe AppInit: leer = kein Befund", Harness.Einer(erg, "leistung.appinit") == null);

            // Obergrenze: ohne Deaktiviert-Status ist warn nicht belegt (einige der 20 koennten deaktiviert sein).
            s.Fehlerliste.Add(new Fehler { Quelle = "registry.autostart.startupapproved", Art = Fehler.Zugriff, Text = "gepflanzt" });
            var erg2 = h.Pruefen(s);
            var b2 = Harness.Einer(erg2, "leistung.autostart.anzahl");
            h.Ist("Obergrenze: StartupApproved fehlt -> unknown statt warn, keine Maßnahme", b2 != null && b2.Zustand == Zustand.Unknown && b2.Massnahmen.Count == 0, b2 != null ? b2.Zustand : null);
            h.Ist("Obergrenze: ohne Urteil auch keine Einzelkarten", !Harness.AlleMit(erg2, LeistungRegel.SchluesselEintrag).Any());
            h.Ist("Obergrenze: Satz sagt „Bis zu 20“ und „kein Urteil“", b2 != null && b2.Satz.Contains("Bis zu 20") && b2.Satz.Contains("kein Urteil"), b2 != null ? b2.Satz : null);
            h.Ist("Obergrenze: Fehlend nennt den Deaktiviert-Status mit „keine Rechte“", Harness.Bereich(erg2, Bereich.Leistung).Fehlend.Any(f => f.Contains("StartupApproved") && f.Contains("keine Rechte")));
        }

        static void AutostartOk(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (h2): 8 aktive Fremd-Autostarts (deaktivierte, Microsoft, WHQL-Fremdpaket, verwaist und eigene richtig gezählt) = ok");
            var s = h.Bild("gepflanzt-stabilitaet-autostart-ok.json"); if (s == null) return;
            h.Ist("Grundmenge: 18 Einträge, davon 9 deaktiviert und 1 ohne Datei; 5 Aufgaben, 1 eigene", s.Autostart.Eintraege.Count == 18 && s.Autostart.Eintraege.Count(x => x.Aktiviert == false) == 9 && s.Autostart.Eintraege.Count(x => x.DateiVorhanden == false) == 1 && s.Autostart.Aufgaben.Count == 5 && s.Autostart.Aufgaben.Count(t => t.Eigen) == 1);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.autostart.anzahl");
            h.Ist("Befund vorhanden", b != null);
            if (b == null) return;
            h.Ist("Zustand ok (8 <= " + Schwellen.AutostartsWarn + ")", b.Zustand == Zustand.Ok, b.Zustand);
            h.Ist("Messwert 8", b.Messwert.Wert == "8", b.Messwert.Wert);
            h.Ist("keine Maßnahme", b.Massnahmen.Count == 0);
            h.Ist("Gegenprobe M3: bei ok keine Einzelkarten leistung.autostart.eintrag.*", !Harness.AlleMit(erg, LeistungRegel.SchluesselEintrag).Any());
            h.Ist("WHQL-signiertes Fremdpaket zählt als fremd", b.Detail.Any(d => d.Contains("Treiberpaket")));
            h.Ist("Einträge ohne Sammler-Urteil (Fremd04, Fremd05) zählen nach Subject als fremd", b.Detail.Any(d => d.Contains("Fremd04 =")) && b.Detail.Any(d => d.Contains("Fremd05 =")));
            h.Ist("Verwaister Eintrag rest.exe: „starten nichts“, nicht gezählt", b.Detail.Any(d => d.Contains("starten nichts") && d.Contains("rest.exe")) && !b.Detail.Any(d => d.Contains("Verwaist =") && d.Contains("(ohne")));
            h.Ist("Aufgabe mit Autor 'Microsoft Corporation' zählt nicht", !b.Detail.Any(d => d.Contains("OneDrive Standalone")));
            h.Ist("Eigene Aufgabe WindowsWartung-AutoWartung zählt nicht", !b.Detail.Any(d => d.StartsWith("Aufgabe \\WindowsWartung")));
            h.Ist("Detail: kein Hinweis auf fehlende Rechte (erhöht gelesen)", !b.Detail.Any(d => d.Contains("ohne Administratorrechte")));
            var a = Harness.Einer(erg, "leistung.appinit");
            h.Ist("AppInit gesetzt und Schalter an, aber Secure Boot an = ok, nicht warn", a != null && a.Zustand == Zustand.Ok, a != null ? a.Zustand : null);
            h.Ist("Bereich Leistung = ok", Harness.Bereich(erg, Bereich.Leistung).Zustand == Zustand.Ok, Harness.Bereich(erg, Bereich.Leistung).Zustand);
        }

        static void AutostartLuecke(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (h3): 3 Fremd-Autostarts, aber Aufgaben nach 10 s abgebrochen = Untergrenze, unknown statt ok");
            var s = h.Bild("gepflanzt-leistung-autostart-luecke.json"); if (s == null) return;
            h.Ist("Grundmenge: 1 Aufgabe gelesen, com.aufgaben mit Art zeit in der fehlerliste", s.Autostart.Aufgaben.Count == 1 && s.Fehlerliste.Any(f => f.Quelle == "com.aufgaben" && f.Art == Fehler.Zeit));
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.autostart.anzahl");
            h.Ist("Befund vorhanden", b != null);
            if (b == null) return;
            h.Ist("Zustand unknown (Untergrenze 3 belegt kein ok)", b.Zustand == Zustand.Unknown, b.Zustand);
            h.Ist("Messwert 3 als Untergrenze", b.Messwert.Wert == "3" && b.Messwert.Einheit.Contains("Untergrenze"), b.Messwert.Einheit);
            h.Ist("Satz sagt „Mindestens 3“ und nennt die Aufgaben mit „Zeit überschritten“", b.Satz.Contains("Mindestens 3") && b.Satz.Contains("geplante Aufgaben (Zeit überschritten)"), b.Satz);
            h.Ist("keine Maßnahme", b.Massnahmen.Count == 0);
            h.Ist("Fehlend nennt die geplanten Aufgaben trotz Teildaten", Harness.Bereich(erg, Bereich.Leistung).Fehlend.Any(f => f.Contains("geplante Aufgaben")));
            // Ein unknown-Befund zaehlt im Bereich nicht mit (Befund.cs): der Bereich darf nur nicht warn oder bad werden.
            h.Ist("Bereich Leistung weder warn noch bad", Harness.Bereich(erg, Bereich.Leistung).Zustand != Zustand.Warn && Harness.Bereich(erg, Bereich.Leistung).Zustand != Zustand.Bad, Harness.Bereich(erg, Bereich.Leistung).Zustand);
            // Gegenprobe: ohne den Fehlereintrag ist dieselbe Zahl ein ok.
            s.Fehlerliste.Clear();
            var b2 = Harness.Einer(h.Pruefen(s), "leistung.autostart.anzahl");
            h.Ist("Gegenprobe: ohne Fehlereintrag = ok", b2 != null && b2.Zustand == Zustand.Ok, b2 != null ? b2.Zustand : null);
            // Untergrenze ueber der Schwelle: warn bleibt belegt, Satz sagt es.
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.dienste", Art = Fehler.Ausnahme, Text = "gepflanzt" });
            for (int i = 0; i < Schwellen.AutostartsWarn; i++)
                s.Autostart.Eintraege.Add(new AutostartEintrag { Quelle = "HKCU\\Run", Name = "Zusatz" + i, Befehl = "C:\\Users\\NUTZER\\z" + i + ".exe", Microsoft = false, DateiVorhanden = true });
            var b3 = Harness.Einer(h.Pruefen(s), "leistung.autostart.anzahl");
            h.Ist("Untergrenze über der Schwelle: warn bleibt, Satz sagt „Mindestens“ und „Untergrenze“, Sammelkarte ohne Kennung", b3 != null && b3.Zustand == Zustand.Warn && b3.Satz.Contains("Mindestens") && b3.Satz.Contains("Untergrenze") && b3.Massnahmen.Count == 0, b3 != null ? b3.Satz : null);
        }

        /// <summary>
        /// M3: bei warn je gezaehltem Eintrag eine eigene ok-Karte mit Massnahme und Parametern
        /// (docs/M3-ENTWURF.md, Abschnitt 1 und 2). Der Schluessel ist ein 12-Hex-Hash aus Quelle
        /// und Name, deshalb wird ueber die Parameter gesucht, nicht ueber den Schluessel.
        /// </summary>
        static void AutostartEinzelkarten(Harness h, Systembild s, List<BereichErgebnis> erg)
        {
            h.Gruppe("Leistung (h4): Einzelkarten je Autostart-Eintrag mit Massnahme und Parametern");
            var karten = Harness.AlleMit(erg, LeistungRegel.SchluesselEintrag).ToList();
            h.Ist("20 Einzelkarten (14 Startprogramme + 2 Aufgaben + 4 Dienste)", karten.Count == 20, karten.Count.ToString());
            h.Ist("alle Einzelkarten sind ok (Kandidat, kein Fehler) und tragen genau eine Massnahme", karten.All(k => k.Zustand == Zustand.Ok && k.Massnahmen.Count == 1));
            h.Ist("Schluessel: Praefix plus 12 Hex-Zeichen, alle verschieden", karten.All(k => k.Schluessel.Length == LeistungRegel.SchluesselEintrag.Length + 12 && k.Schluessel.Substring(LeistungRegel.SchluesselEintrag.Length).All(c => "0123456789abcdef".IndexOf(c) >= 0)) && karten.Select(k => k.Schluessel).Distinct().Count() == karten.Count);
            h.Ist("Schluessel sind stabil (zweiter Lauf liefert dieselben)", Harness.AlleMit(h.Pruefen(s), LeistungRegel.SchluesselEintrag).Select(k => k.Schluessel).SequenceEqual(karten.Select(k => k.Schluessel)));

            var programme = karten.Where(k => k.Massnahmen.Contains(LeistungRegel.MAutostartDeaktivieren)).ToList();
            var aufgaben = karten.Where(k => k.Massnahmen.Contains(LeistungRegel.MAufgabeDeaktivieren)).ToList();
            var dienste = karten.Where(k => k.Massnahmen.Contains(LeistungRegel.MDienstStartart)).ToList();
            h.Ist("14 Startprogramme mit leistung.autostart.deaktivieren", programme.Count == 14, programme.Count.ToString());
            h.Ist("2 Aufgaben mit leistung.aufgabe.deaktivieren", aufgaben.Count == 2, aufgaben.Count.ToString());
            h.Ist("4 Dienste mit leistung.dienst.startart", dienste.Count == 4, dienste.Count.ToString());
            h.Ist("jedes Startprogramm traegt hive, ort und name", programme.All(k => k.Parameter.ContainsKey(LeistungRegel.ParamHive) && k.Parameter.ContainsKey(LeistungRegel.ParamOrt) && k.Parameter.ContainsKey(LeistungRegel.ParamName)));
            h.Ist("hive ist HKLM oder HKCU, ort ist Run, Run32 oder StartupFolder", programme.All(k => (k.Parameter[LeistungRegel.ParamHive] == "HKLM" || k.Parameter[LeistungRegel.ParamHive] == "HKCU") && (k.Parameter[LeistungRegel.ParamOrt] == "Run" || k.Parameter[LeistungRegel.ParamOrt] == "Run32" || k.Parameter[LeistungRegel.ParamOrt] == "StartupFolder")));
            h.Ist("jede Aufgabe traegt pfad, jeder Dienst name und startart = Manual", aufgaben.All(k => k.Parameter.ContainsKey(LeistungRegel.ParamPfad)) && dienste.All(k => k.Parameter.ContainsKey(LeistungRegel.ParamName) && k.Parameter[LeistungRegel.ParamStartart] == LeistungRegel.StartartManuell));

            var f01 = programme.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamName] == "Fremd01");
            h.Ist("Fremd01 (HKLM\\Run): hive HKLM, ort Run", f01 != null && f01.Parameter[LeistungRegel.ParamHive] == "HKLM" && f01.Parameter[LeistungRegel.ParamOrt] == "Run");
            h.Ist("Fremd01: Satz nennt Herausgeber und Bedingung, kein HKCU-Hinweis", f01 != null && f01.Satz.Contains("Fremd GmbH") && f01.Satz.Contains("nicht täglich") && !f01.Detail.Any(d => d.Contains("eigenes Konto")), f01 == null ? null : f01.Satz);
            var f03 = programme.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamName] == "Fremd03");
            h.Ist("Fremd03 (HKLM\\Run32): ort Run32", f03 != null && f03.Parameter[LeistungRegel.ParamHive] == "HKLM" && f03.Parameter[LeistungRegel.ParamOrt] == "Run32");
            var f04 = programme.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamName] == "Fremd04");
            h.Ist("Fremd04 (HKCU\\Run): hive HKCU, Detail nennt das eigene Konto (profilgebunden)", f04 != null && f04.Parameter[LeistungRegel.ParamHive] == "HKCU" && f04.Parameter[LeistungRegel.ParamOrt] == "Run" && f04.Detail.Any(d => d.Contains("eigenes Konto")));
            var f13 = programme.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamName] == "Fremd13.lnk");
            h.Ist("Fremd13.lnk (Startup): hive HKCU, ort StartupFolder, name mit Endung", f13 != null && f13.Parameter[LeistungRegel.ParamHive] == "HKCU" && f13.Parameter[LeistungRegel.ParamOrt] == "StartupFolder");
            var f14 = programme.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamName] == "Fremd14.lnk");
            h.Ist("Fremd14.lnk (CommonStartup): hive HKLM, ort StartupFolder", f14 != null && f14.Parameter[LeistungRegel.ParamHive] == "HKLM" && f14.Parameter[LeistungRegel.ParamOrt] == "StartupFolder");
            h.Ist("Gegenprobe: deaktiviertes OneDrive, Microsoft-Eintrag und verwaiste Eintraege haben keine Karte", !programme.Any(k => k.Parameter[LeistungRegel.ParamName] == "OneDrive" || k.Parameter[LeistungRegel.ParamName] == "SecurityHealth" || k.Parameter[LeistungRegel.ParamName] == "Fremd11" || k.Parameter[LeistungRegel.ParamName] == "Fremd12"));
            var upd = aufgaben.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamPfad] == "\\FremdUpdater");
            h.Ist("Aufgabe \\FremdUpdater: pfad als Parameter, Satz nennt die Anmeldung", upd != null && upd.Satz.Contains("bei jeder Anmeldung"), upd == null ? null : upd.Satz);
            h.Ist("Gegenprobe: eigene Aufgabe und Microsoft-Aufgaben haben keine Karte", !aufgaben.Any(k => k.Parameter[LeistungRegel.ParamPfad].StartsWith("\\WindowsWartung") || k.Parameter[LeistungRegel.ParamPfad].StartsWith("\\Microsoft\\")));
            var d1 = dienste.FirstOrDefault(k => k.Parameter[LeistungRegel.ParamName] == "FremdDienst1");
            h.Ist("Dienst FremdDienst1: name ist der Dienstname (nicht die Anzeige), startart Manual", d1 != null && d1.Parameter[LeistungRegel.ParamStartart] == "Manual" && d1.Titel.Contains("Fremd Dienst 1"), d1 == null ? null : d1.Titel);
            h.Ist("Gegenprobe: manueller Dienst und LSM (Signatur unbekannt) haben keine Karte", !dienste.Any(k => k.Parameter[LeistungRegel.ParamName] == "FremdManuell" || k.Parameter[LeistungRegel.ParamName] == "LSM"));
            h.Ist("Sammelkarte nennt die Einzelkarten (20 von 20), davon 20 mit Schalter, keine nur mit Rat", Harness.Einer(erg, "leistung.autostart.anzahl").Detail.Any(d => d.Contains("eigene Karte (20 von 20), davon 20 mit dem Schalter") && !d.Contains("nur mit Rat")));

            // RunOnce hat keinen Task-Manager-Schalter: zaehlt in der Sammelkarte, bekommt aber keine Karte.
            // Ein Aufgabenpfad mit Zeichen ausserhalb der Freigabe (Semikolon) bekommt die Karte ohne
            // Massnahme, mit Rat; geschweifte Klammern (Updater-Aufgaben der Fremdhersteller tragen eine
            // GUID im Namen) gehoeren seit dem Nachtrag vom 18.09.2026 zur Freigabe und bekommen den Schalter.
            const string updater = "\\Fremd\\Updater{7B1C2D3E-1111-4222-8333-444444444444}";
            s.Autostart.Eintraege.Add(new AutostartEintrag { Quelle = "HKCU\\RunOnce", Name = "Einmal", Befehl = "C:\\Users\\NUTZER\\einmal.exe", Microsoft = false, DateiVorhanden = true });
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = updater, Autor = "Fremd GmbH", Zustand = 3, Logon = true });
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = "\\Fremd\\Auf;gabe", Autor = "Fremd GmbH", Zustand = 3, Boot = true });
            var erg2 = h.Pruefen(s);
            var karten2 = Harness.AlleMit(erg2, LeistungRegel.SchluesselEintrag).ToList();
            h.Ist("Gegenprobe RunOnce: Sammelkarte zaehlt 23, Einzelkarten 22 (RunOnce ohne Karte)", Harness.Einer(erg2, "leistung.autostart.anzahl").Messwert.Wert == "23" && karten2.Count == 22, karten2.Count.ToString());
            h.Ist("Gegenprobe RunOnce: keine Karte mit name = Einmal", !karten2.Any(k => k.Parameter.ContainsKey(LeistungRegel.ParamName) && k.Parameter[LeistungRegel.ParamName] == "Einmal"));
            h.Ist("Sammelkarte sagt: 22 Karten von 23, davon 21 mit dem Schalter und 1 nur mit Rat", Harness.Einer(erg2, "leistung.autostart.anzahl").Detail.Any(d => d.Contains("(22 von 23), davon 21 mit dem Schalter") && d.Contains("1 nur mit Rat")), string.Join(" | ", Harness.Einer(erg2, "leistung.autostart.anzahl").Detail.Where(d => d.Contains("eigene Karte"))));
            var guid = karten2.FirstOrDefault(k => k.Titel.Contains("7B1C2D3E"));
            h.Ist("Aufgabe \\Fremd\\Updater{GUID}: geschweifte Klammern sind zulaessig, Massnahme mit pfad, kein Rat", guid != null && guid.Massnahmen.Contains(LeistungRegel.MAufgabeDeaktivieren) && guid.Parameter.ContainsKey(LeistungRegel.ParamPfad) && guid.Parameter[LeistungRegel.ParamPfad] == updater && guid.Rat == null, guid == null ? "null" : string.Join(",", guid.Massnahmen) + " " + guid.Rat);
            var semi = karten2.FirstOrDefault(k => k.Titel.Contains("Auf;gabe"));
            h.Ist("Gegenprobe Aufgabenpfad mit Semikolon: Karte ohne Massnahme, ohne Parameter pfad, mit Rat zur Aufgabenplanung", semi != null && semi.Massnahmen.Count == 0 && !semi.Parameter.ContainsKey(LeistungRegel.ParamPfad) && semi.Rat != null && semi.Rat.Contains("Aufgabenplanung"), semi == null ? "null" : semi.Rat);
            h.Ist("Gegenprobe Semikolon: Detail nennt die erlaubten Zeichen samt { }", semi != null && semi.Detail.Any(d => d.Contains("( ) { } im Pfad")), semi == null ? "null" : string.Join(" | ", semi.Detail));
            foreach (string gut in new[] { "\\Fremd\\Update", updater, "\\A b\\c_d.e-f (1)", "\\{GUID}", "x" })
                h.Ist("AufgabenpfadZulaessig: „" + gut + "“ = zulaessig", LeistungRegel.AufgabenpfadZulaessig(gut));
            foreach (string schlecht in new[] { null, "", "\\Fremd\\Auf;gabe", "\\Fremd\\\"Zitat\"", "\\Fremd\\Ap'ostroph", "\\Fremd\\Stern*", "\\Fremd\\Frage?", "\\Fremd\\Prozent%", "\\Fremd\\Ä", "\\Fremd\\a\tb", "\\Fremd\\[eckig]", "\\" + new string('a', 260) })
                h.Ist("AufgabenpfadZulaessig: „" + Kurz(schlecht) + "“ = nicht zulaessig", !LeistungRegel.AufgabenpfadZulaessig(schlecht));
            h.Ist("StartupApprovedOrt: RunOnce und Unbekanntes -> false, die sechs bekannten Quellen -> true", !Probe("HKCU\\RunOnce") && !Probe("HKLM\\RunOnce32") && !Probe("") && !Probe(null) && Probe("HKLM\\Run") && Probe("HKLM\\Run32") && Probe("HKCU\\Run") && Probe("HKCU\\Run32") && Probe("Startup") && Probe("CommonStartup"));
            h.Ist("Hash12: 12 Hex-Zeichen, stabil, ohne Beachtung der Schreibweise, verschieden fuer Fremd01 und Fremd02", LeistungRegel.Hash12("a").Length == 12 && LeistungRegel.Hash12("eintrag|HKLM\\Run|Fremd01") == LeistungRegel.Hash12("EINTRAG|hklm\\run|fremd01") && LeistungRegel.Hash12("eintrag|HKLM\\Run|Fremd01") != LeistungRegel.Hash12("eintrag|HKLM\\Run|Fremd02"));
            s.Autostart.Eintraege.RemoveAt(s.Autostart.Eintraege.Count - 1);
            s.Autostart.Aufgaben.RemoveAt(s.Autostart.Aufgaben.Count - 1);
            s.Autostart.Aufgaben.RemoveAt(s.Autostart.Aufgaben.Count - 1);

            // Befund des Gegenlesens M3 (18.09.2026): die Edge-Updater-Aufgaben liegen in der Wurzel der
            // Aufgabenplanung, tragen keinen Autor und werden von MicrosoftEdgeUpdate.exe (CN=Microsoft
            // Corporation) ausgefuehrt; auf dieser-pc-de.json zaehlte die Heuristik sie als Fremdhersteller
            // und bot den Abschalt-Knopf an. Jetzt: Sammler-Urteil (Aufgabe.Microsoft) zuerst, dann Ordner,
            // Namensanfang der Edge-Aufgaben, Autor. Wirksamkeitsprobe 18.09.2026: EdgeUpdaterAufgabe-Zeile in
            // AufgabeMicrosoft entfernt -> "keine Einzelkarte" und vier Zaehlproben rot (5 von 7810), zurueckgebaut.
            const string edgeCore = "\\MicrosoftEdgeUpdateTaskMachineCore{43A9B155-5DA2-4F8A-A4AA-1EF7D23A7FF4}";
            const string edgeUser = "\\MicrosoftEdgeUpdateTaskUserS-1-5-21-1-2-3-1001Core{2640F5CD-EB5C-4121-B696-35420B2748F9}";
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = edgeCore, Autor = null, Zustand = 3, Logon = true });
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = edgeUser, Autor = null, Zustand = 3, Logon = true });
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = "\\FremdNameMsSigniert", Autor = null, Zustand = 3, Logon = true, AktionPfad = "C:\\Program Files\\Fremd\\ms.exe", Microsoft = true });
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = "\\FremdOhneAutor", Autor = null, Zustand = 3, Logon = true, AktionPfad = "C:\\Program Files\\Fremd\\fremd.exe", Microsoft = false });
            // Schalter umgelegt (Enabled false), Instanz laeuft noch (Zustand 4): deaktiviert, keine Karte, nicht gezaehlt
            // (MassnahmenProben c2, 18.09.2026). Ohne das Feld entscheidet der Zustand wie zuvor.
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = "\\FremdAbgeschaltetLaeuft", Autor = "Fremd GmbH", Zustand = 4, Logon = true, Aktiv = false });
            var erg3 = h.Pruefen(s);
            var karten3 = Harness.AlleMit(erg3, LeistungRegel.SchluesselEintrag).ToList();
            var sammel3 = Harness.Einer(erg3, "leistung.autostart.anzahl");
            Func<string, Befund> karte = pfad => karten3.FirstOrDefault(k => k.Parameter.ContainsKey(LeistungRegel.ParamPfad) && k.Parameter[LeistungRegel.ParamPfad] == pfad);
            h.Ist("Edge-Updater-Aufgabe (Wurzel, ohne Autor): keine Einzelkarte, nicht als Fremdhersteller gezaehlt", karte(edgeCore) == null && !sammel3.Detail.Any(d => d.Contains("MicrosoftEdgeUpdateTaskMachineCore")), string.Join(" | ", sammel3.Detail.Where(d => d.Contains("Edge"))));
            h.Ist("Edge-Updater-Aufgabe je Benutzer (…TaskUser<SID>Core{GUID}): ebenfalls keine Karte", karte(edgeUser) == null && !sammel3.Detail.Any(d => d.Contains("MicrosoftEdgeUpdateTaskUser")));
            h.Ist("Sammler-Urteil microsoft = true schlaegt Wurzelordner und fehlenden Autor: keine Karte", karte("\\FremdNameMsSigniert") == null && !sammel3.Detail.Any(d => d.Contains("FremdNameMsSigniert")));
            var fremdOhne = karte("\\FremdOhneAutor");
            h.Ist("Gegenprobe: Wurzel, ohne Autor, Urteil microsoft = false -> Karte mit Schalter und pfad", fremdOhne != null && fremdOhne.Massnahmen.Contains(LeistungRegel.MAufgabeDeaktivieren) && fremdOhne.Satz.Contains("ohne Autor"), fremdOhne == null ? "fehlt" : fremdOhne.Satz);
            h.Ist("Aufgabe mit Enabled false und Zustand 4 (Instanz laeuft noch): keine Karte, nicht gezaehlt", karte("\\FremdAbgeschaltetLaeuft") == null && !sammel3.Detail.Any(d => d.Contains("FremdAbgeschaltetLaeuft")));
            h.Ist("Sammelkarte zaehlt 21 (20 plus FremdOhneAutor), 21 Einzelkarten", sammel3.Messwert.Wert == "21" && karten3.Count == 21, sammel3.Messwert.Wert + " / " + karten3.Count);
            h.Ist("Gegenprobe: FremdOhneAutor-Karte nennt im Detail die Aktion als nicht von Microsoft signiert", fremdOhne != null && fremdOhne.Detail.Any(d => d.StartsWith("Aktion: ") && d.Contains("nicht von Microsoft signiert")), fremdOhne == null ? "fehlt" : string.Join(" | ", fremdOhne.Detail));
            // Gegenprobe Enabled: Zustand 4 ohne Feld zaehlt (wie bisher), Enabled true mit Zustand 1 zaehlt (der Schalter entscheidet).
            var laeuft = s.Autostart.Aufgaben[s.Autostart.Aufgaben.Count - 1];
            laeuft.Aktiv = null;
            var ohneFeld = Harness.Einer(h.Pruefen(s), "leistung.autostart.anzahl");
            h.Ist("Gegenprobe: Zustand 4 ohne Feld aktiv -> gezaehlt (22)", ohneFeld.Messwert.Wert == "22" && ohneFeld.Detail.Any(d => d.Contains("FremdAbgeschaltetLaeuft")), ohneFeld.Messwert.Wert);
            laeuft.Aktiv = true; laeuft.Zustand = 1;
            var anTrotz = Harness.Einer(h.Pruefen(s), "leistung.autostart.anzahl");
            h.Ist("Gegenprobe: Enabled true mit Zustand 1 -> gezaehlt (22), das Feld schlaegt den Zustand", anTrotz.Messwert.Wert == "22", anTrotz.Messwert.Wert);
            s.Autostart.Aufgaben.RemoveRange(s.Autostart.Aufgaben.Count - 5, 5);
        }

        static bool Probe(string quelle)
        {
            string hive, ort;
            return LeistungRegel.StartupApprovedOrt(quelle, out hive, out ort);
        }

        /// <summary>
        /// M3-Entwurf 8.5: eine Einzelkarte bekommt die Massnahme nur, wenn der Helfer den Parameter
        /// annimmt (DienstNameZulaessig, WertnameZulaessig) und der Dienst keine Schutzsoftware ist
        /// (Netz.IstAvHersteller ueber Signierer oder Anzeigename, dazu Leistung.NenntSchutzprodukt
        /// ueber die im Sicherheitscenter registrierten Namen in Sicherheit.DrittAv). Die Karte bleibt
        /// in jedem Fall, ohne Massnahme mit Rat. Dasselbe Bild wie (h), im Speicher um die
        /// Grenzfaelle ergaenzt.
        /// </summary>
        static void AutostartGrenzen(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (h5): Dienst einer Schutzsoftware (Herstellerliste und Sicherheitscenter), Dienstname mit Apostroph, Dienstname mit Dollar, Wertname mit Semikolon");
            var s = h.Bild("gepflanzt-stabilitaet-autostart.json"); if (s == null) return;
            h.Ist("Grundmenge: Sammelkarte warn (20 Fremd-Autostarts), keine Schutzsoftware im Bild, kein Produkt im Sicherheitscenter",
                Harness.Einer(h.Pruefen(s), "leistung.autostart.anzahl").Zustand == Zustand.Warn
                && !s.Autostart.Dienste.Any(d => NetzRegel.IstAvHersteller(d.Signierer) || NetzRegel.IstAvHersteller(d.Anzeige))
                && s.Sicherheit.DrittAv.Count == 0);
            s.Autostart.Dienste.Add(new Dienst { Name = "avast! Antivirus", Anzeige = "Avast Antivirus", Startart = "Auto", Zustand = "Running", Signierer = "CN=Avast Software s.r.o., O=Avast Software s.r.o., C=CZ", Microsoft = false });
            s.Autostart.Dienste.Add(new Dienst { Name = "ekrn", Anzeige = "ESET Service", Startart = "Auto", Zustand = "Running", Signierer = null, Microsoft = false });
            s.Autostart.Dienste.Add(new Dienst { Name = "Fremd'Dienst", Anzeige = "Fremd mit Apostroph", Startart = "Auto", Zustand = "Running", Signierer = "CN=Fremd GmbH, O=Fremd GmbH, C=DE", Microsoft = false });
            s.Autostart.Dienste.Add(new Dienst { Name = "MSSQL$SQLEXPRESS", Anzeige = "SQL Server (SQLEXPRESS)", Startart = "Auto", Zustand = "Running", Signierer = "CN=Fremd GmbH, O=Fremd GmbH, C=DE", Microsoft = false });
            s.Autostart.Eintraege.Add(new AutostartEintrag { Quelle = "HKLM\\Run", Name = "Semi;kolon", Befehl = "C:\\Fremd\\semi.exe", Microsoft = false, DateiVorhanden = true });
            // Nachtrag 18.09.2026: Avira und Malwarebytes stehen jetzt in der Herstellerliste; ein Produkt,
            // das dort fehlt (Zonk Guard), faellt ueber seinen Namen im Sicherheitscenter; ein Produktname
            // nur aus Gattungswoertern (Total Internet Security Free) entzieht niemandem den Schalter.
            s.Autostart.Dienste.Add(new Dienst { Name = "AviraAntivirusService", Anzeige = "Avira Antivirus", Startart = "Auto", Zustand = "Running", Signierer = "CN=Avira Operations GmbH & Co. KG, O=Avira Operations GmbH & Co. KG, C=DE", Microsoft = false });
            s.Autostart.Dienste.Add(new Dienst { Name = "MBAMService", Anzeige = "Malwarebytes Service", Startart = "Auto", Zustand = "Running", Signierer = "CN=Malwarebytes Inc., O=Malwarebytes Inc., C=US", Microsoft = false });
            s.Autostart.Dienste.Add(new Dienst { Name = "ZonkGuardSvc", Anzeige = "Zonk Guard Service", Startart = "Auto", Zustand = "Running", Signierer = "CN=Zonk Software Ltd., O=Zonk Software Ltd., C=GB", Microsoft = false });
            s.Autostart.Dienste.Add(new Dienst { Name = "TotalUpdate", Anzeige = "Total Update Service", Startart = "Auto", Zustand = "Running", Signierer = "CN=Andere GmbH, O=Andere GmbH, C=DE", Microsoft = false });
            s.Sicherheit.DrittAv.Add("Avira Antivirus");
            s.Sicherheit.DrittAv.Add("Zonk Guard");
            s.Sicherheit.DrittAv.Add("Total Internet Security Free");
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var karten = Harness.AlleMit(erg, LeistungRegel.SchluesselEintrag).ToList();
            h.Ist("Sammelkarte zaehlt 29 (20 + 8 Dienste + 1 Eintrag), 29 Einzelkarten", Harness.Einer(erg, "leistung.autostart.anzahl").Messwert.Wert == "29" && karten.Count == 29, karten.Count.ToString());
            h.Ist("Sammelkarte sagt: 29 Karten, davon 22 mit dem Schalter und 7 nur mit Rat", Harness.Einer(erg, "leistung.autostart.anzahl").Detail.Any(d => d.Contains("(29 von 29), davon 22 mit dem Schalter") && d.Contains("7 nur mit Rat")), string.Join(" | ", Harness.Einer(erg, "leistung.autostart.anzahl").Detail.Where(d => d.Contains("eigene Karte"))));

            var avira = karten.FirstOrDefault(k => k.Quelle == "Win32_Service AviraAntivirusService");
            h.Ist("Avira (Signierer Avira Operations GmbH): Karte ohne Massnahme, Detail nennt den Signierer", avira != null && avira.Massnahmen.Count == 0 && avira.Parameter.Count == 0 && avira.Detail.Any(d => d.Contains("Schutzsoftware erkannt an Signierer")), avira == null ? "null" : string.Join(" | ", avira.Detail));
            var mbam = karten.FirstOrDefault(k => k.Quelle == "Win32_Service MBAMService");
            h.Ist("Malwarebytes (Signierer Malwarebytes Inc.): Karte ohne Massnahme", mbam != null && mbam.Massnahmen.Count == 0 && mbam.Parameter.Count == 0 && mbam.Rat != null && mbam.Rat.Contains("Schutzsoftware"), mbam == null ? "null" : mbam.Rat);
            var zonk = karten.FirstOrDefault(k => k.Quelle == "Win32_Service ZonkGuardSvc");
            h.Ist("Zonk Guard (nur im Sicherheitscenter registriert): Karte ohne Massnahme, Detail nennt Wort „Zonk“ und Produkt „Zonk Guard“", zonk != null && zonk.Massnahmen.Count == 0 && zonk.Parameter.Count == 0 && zonk.Detail.Any(d => d.Contains("am Wort „Zonk“") && d.Contains("„Zonk Guard“")), zonk == null ? "null" : string.Join(" | ", zonk.Detail));
            var total = karten.FirstOrDefault(k => k.Quelle == "Win32_Service TotalUpdate");
            h.Ist("Gegenprobe Gattungswoerter: „Total Update Service“ behaelt die Massnahme trotz Produkt „Total Internet Security Free“", total != null && total.Massnahmen.Contains(LeistungRegel.MDienstStartart) && total.Parameter[LeistungRegel.ParamName] == "TotalUpdate", total == null ? "null" : string.Join(",", total.Massnahmen));
            var avast = karten.FirstOrDefault(k => k.Quelle == "Win32_Service avast! Antivirus");
            h.Ist("Schutzsoftware am Signierer (Avast): Karte ohne Massnahme, ohne Parameter, Rat nennt die Schutzsoftware", avast != null && avast.Massnahmen.Count == 0 && avast.Parameter.Count == 0 && avast.Rat != null && avast.Rat.Contains("Schutzsoftware"), avast == null ? "null" : avast.Rat);
            h.Ist("Schutzsoftware: Satz verspricht keinen Schalter, Detail nennt den Signierer", avast != null && avast.Satz.Contains("keinen Schalter") && avast.Detail.Any(d => d.Contains("Schutzsoftware erkannt an Signierer")), avast == null ? "null" : avast.Satz);
            var eset = karten.FirstOrDefault(k => k.Quelle == "Win32_Service ekrn");
            h.Ist("Schutzsoftware am Anzeigenamen (ESET, ohne Signierer): Karte ohne Massnahme, Detail nennt den Anzeigenamen", eset != null && eset.Massnahmen.Count == 0 && eset.Detail.Any(d => d.Contains("Schutzsoftware erkannt an Anzeigename")), eset == null ? "null" : string.Join(" | ", eset.Detail));
            var apostroph = karten.FirstOrDefault(k => k.Quelle == "Win32_Service Fremd'Dienst");
            h.Ist("Dienstname mit Apostroph: Karte ohne Massnahme, ohne Parameter, Rat nennt services.msc", apostroph != null && apostroph.Massnahmen.Count == 0 && !apostroph.Parameter.ContainsKey(LeistungRegel.ParamName) && apostroph.Rat != null && apostroph.Rat.Contains("services.msc"), apostroph == null ? "null" : apostroph.Rat);
            var dollar = karten.FirstOrDefault(k => k.Quelle == "Win32_Service MSSQL$SQLEXPRESS");
            h.Ist("Dienstname mit Dollar (MSSQL$SQLEXPRESS): Massnahme mit name und startart Manual", dollar != null && dollar.Massnahmen.Contains(LeistungRegel.MDienstStartart) && dollar.Parameter[LeistungRegel.ParamName] == "MSSQL$SQLEXPRESS" && dollar.Parameter[LeistungRegel.ParamStartart] == LeistungRegel.StartartManuell, dollar == null ? "null" : string.Join(",", dollar.Massnahmen));
            var semi = karten.FirstOrDefault(k => k.Titel == "Startprogramm „Semi;kolon“");
            h.Ist("Wertname mit Semikolon: Karte ohne Massnahme, ohne Parameter, Rat nennt den Task-Manager", semi != null && semi.Massnahmen.Count == 0 && semi.Parameter.Count == 0 && semi.Rat != null && semi.Rat.Contains("Task-Manager"), semi == null ? "null" : semi.Rat);
            h.Ist("Gegenprobe: FremdDienst1 behaelt die Massnahme", karten.Any(k => k.Quelle == "Win32_Service FremdDienst1" && k.Massnahmen.Contains(LeistungRegel.MDienstStartart)));
            h.Ist("jede Karte ohne Massnahme hat einen Rat, jede mit Massnahme keinen", karten.All(k => (k.Massnahmen.Count == 0) == (k.Rat != null)));
            // Gegenprobe: ohne Eintrag im Sicherheitscenter bekommt der unbekannte Hersteller den Schalter, Avira nicht.
            s.Sicherheit.DrittAv.Clear();
            var karten0 = Harness.AlleMit(h.Pruefen(s), LeistungRegel.SchluesselEintrag).ToList();
            h.Ist("Gegenprobe ohne Sicherheitscenter-Eintrag: Zonk Guard bekommt die Massnahme, Avira weiter nicht", karten0.Any(k => k.Quelle == "Win32_Service ZonkGuardSvc" && k.Massnahmen.Contains(LeistungRegel.MDienstStartart)) && karten0.Any(k => k.Quelle == "Win32_Service AviraAntivirusService" && k.Massnahmen.Count == 0));
            s.Sicherheit.DrittAv.Add("Zonk Guard");
            // Der Produktname wirkt auch ueber den Anzeigenamen, wenn der Signierer fehlt.
            s.Autostart.Dienste.First(d => d.Name == "ZonkGuardSvc").Signierer = null;
            var zonk2 = Harness.AlleMit(h.Pruefen(s), LeistungRegel.SchluesselEintrag).FirstOrDefault(k => k.Quelle == "Win32_Service ZonkGuardSvc");
            h.Ist("Zonk Guard ohne Signierer: Treffer ueber den Anzeigenamen, Karte ohne Massnahme", zonk2 != null && zonk2.Massnahmen.Count == 0 && zonk2.Detail.Any(d => d.Contains("im Anzeigenamen „Zonk Guard Service“")), zonk2 == null ? "null" : string.Join(" | ", zonk2.Detail));
        }

        /// <summary>
        /// Grundmengen der reinen Pruefungen, die der Helfer mitnutzt (M3-Entwurf 8.5): Dienstname
        /// 1 bis 256 Zeichen ohne \ / ' " ; und Steuerzeichen (Dollar erlaubt), Wertname 1 bis 255
        /// Zeichen ohne ; und Steuerzeichen, Virenschutz-Hersteller in Signierer oder Anzeigename
        /// (21 Namen seit dem Nachtrag vom 18.09.2026), Produktname aus dem Sicherheitscenter als
        /// ganzes Wort (mindestens vier Buchstaben, kein Gattungswort).
        /// </summary>
        static void Pruefwerte(Harness h)
        {
            h.Gruppe("Leistung (h6): DienstNameZulaessig, WertnameZulaessig, IstAvHersteller, NenntSchutzprodukt");
            foreach (string gut in new[] { "wuauserv", "MSSQL$SQLEXPRESS", "SQLAgent$INSTANZ", "Fremd Dienst (2)", "a", "x.y-z_1", new string('a', 256) })
                h.Ist("DienstNameZulaessig: „" + Kurz(gut) + "“ = zulaessig", LeistungRegel.DienstNameZulaessig(gut));
            foreach (string schlecht in new[] { null, "", "a\\b", "a/b", "a'b", "a\"b", "a;b", "a\tb", "a\nb", new string('a', 257) })
                h.Ist("DienstNameZulaessig: „" + Kurz(schlecht) + "“ = nicht zulaessig", !LeistungRegel.DienstNameZulaessig(schlecht));
            foreach (string gut in new[] { "OneDrive", "Fremd13.lnk", "a\\b/c'd\"e$f", "Name mit Leerzeichen", new string('b', 255) })
                h.Ist("WertnameZulaessig: „" + Kurz(gut) + "“ = zulaessig", LeistungRegel.WertnameZulaessig(gut));
            foreach (string schlecht in new[] { null, "", "a;b", "a\rb", "a\u0007b", new string('b', 256) })
                h.Ist("WertnameZulaessig: „" + Kurz(schlecht) + "“ = nicht zulaessig", !LeistungRegel.WertnameZulaessig(schlecht));
            foreach (string av in new[] { "CN=Avast Software s.r.o., O=Avast Software s.r.o., C=CZ", "AVG Technologies USA, LLC", "Bitdefender SRL", "ESET, spol. s r.o.", "AO Kaspersky Lab", "McAfee, LLC", "NortonLifeLock Inc.", "Sophos Limited", "Trend Micro Inc.", "Kaspersky Anti-Virus Service",
                                           "CN=Avira Operations GmbH & Co. KG, O=Avira Operations GmbH & Co. KG, C=DE", "Malwarebytes Inc.", "F-Secure Corporation", "G DATA CyberDefense AG", "Panda Security S.L.", "Webroot Inc.", "Symantec Corporation", "Emsisoft Ltd", "Comodo Security Solutions, Inc.", "CrowdStrike, Inc.", "SentinelOne, Inc.", "Cylance Inc.", "AviraAntivirusService", "Malwarebytes Service" })
                h.Ist("IstAvHersteller: „" + av + "“ = ja", NetzRegel.IstAvHersteller(av));
            foreach (string kein in new[] { null, "", "CN=Fremd GmbH, O=Fremd GmbH, C=DE", "CN=Microsoft Windows, O=Microsoft Corporation", "Avgold Inc.", "Esetting Ltd.", "Fremd Dienst 1", "Reset Service", "Pandora Media", "Big Data GmbH", "Secure Sync Ltd.", "Zonk Software Ltd." })
                h.Ist("IstAvHersteller: „" + kein + "“ = nein", !NetzRegel.IstAvHersteller(kein));

            var produkte = new List<string> { "Avira Antivirus", "Zonk Guard", "Total Internet Security Free", "G DATA Antivirus", null, "" };
            string wort, produkt;
            h.Ist("NenntSchutzprodukt: „CN=Avira Operations GmbH & Co. KG“ trifft „Avira“ aus „Avira Antivirus“", LeistungRegel.NenntSchutzprodukt("CN=Avira Operations GmbH & Co. KG, O=Avira Operations GmbH & Co. KG, C=DE", produkte, out wort, out produkt) && wort == "Avira" && produkt == "Avira Antivirus", wort + " / " + produkt);
            h.Ist("NenntSchutzprodukt: „Zonk Guard Service“ trifft „Zonk“ (Anzeigename, Wort in Produktschreibweise)", LeistungRegel.NenntSchutzprodukt("Zonk Guard Service", produkte, out wort, out produkt) && wort == "Zonk" && produkt == "Zonk Guard", wort + " / " + produkt);
            h.Ist("NenntSchutzprodukt: „zonk guard svc“ trifft ohne Beachtung der Schreibweise", LeistungRegel.NenntSchutzprodukt("zonk guard svc", produkte));
            h.Ist("NenntSchutzprodukt: „Guard Dog Ltd.“ trifft „Guard“ (kein Gattungswort)", LeistungRegel.NenntSchutzprodukt("Guard Dog Ltd.", produkte, out wort, out produkt) && wort == "Guard");
            h.Ist("NenntSchutzprodukt: „G DATA CyberDefense AG“ trifft „DATA“ (vier Buchstaben), nicht „G“", LeistungRegel.NenntSchutzprodukt("G DATA CyberDefense AG", produkte, out wort, out produkt) && wort == "DATA");
            foreach (string kein in new[] { null, "", "Aviram Ltd.", "AviraAntivirusService", "Total Update Service", "Internet Security Suite", "Fremd GmbH", "CN=Microsoft Windows", "Antivirus Free Total" })
                h.Ist("NenntSchutzprodukt: „" + kein + "“ = nein", !LeistungRegel.NenntSchutzprodukt(kein, produkte));
            h.Ist("NenntSchutzprodukt: „Avira-Nachbar“ = ja (der Bindestrich trennt, „Avira“ steht als ganzes Wort)", LeistungRegel.NenntSchutzprodukt("Avira-Nachbar", produkte));
            h.Ist("NenntSchutzprodukt: Produktliste null oder leer = nein", !LeistungRegel.NenntSchutzprodukt("Avira Antivirus", null) && !LeistungRegel.NenntSchutzprodukt("Avira Antivirus", new List<string>()));
            h.Ist("NenntSchutzprodukt: Produkt nur aus Gattungswoertern oder kurzen Woertern trifft nie", !LeistungRegel.NenntSchutzprodukt("Total Internet Security Free K7", new List<string> { "Total Internet Security Free", "K7 Total Security", "360 Total Security" }));
        }

        static string Kurz(string s)
        {
            if (s == null) return "null";
            string t = s.Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\u0007", "\\a");
            return t.Length > 24 ? t.Substring(0, 12) + "…(" + s.Length + " Zeichen)" : t;
        }

        // ---------------------------------------------------------------- (i) Winlogon

        static void Shell(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (i): Shell = cmd.exe = bad; Userinit normal = kein Befund; AppInit ohne Secure Boot mit Schalter 1 = warn");
            var s = h.Bild("gepflanzt-stabilitaet-shell.json"); if (s == null) return;
            h.Ist("Grundmenge: Shell cmd.exe, Userinit mit userinit.exe, Secure Boot aus, LoadAppInit_DLLs an", s.Autostart.Shell == "cmd.exe" && s.Autostart.Userinit.Contains("userinit.exe") && s.Sicherheit.SecureBoot == false && s.Autostart.LoadAppInitDlls == true);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.winlogon.shell");
            h.Ist("Shell-Befund bad", b != null && b.Zustand == Zustand.Bad);
            h.Ist("Satz nennt cmd.exe und explorer.exe", b != null && b.Satz.Contains("cmd.exe") && b.Satz.Contains("explorer.exe"));
            h.Ist("Maßnahme leistung.winlogon.zuruecksetzen (Rat, M3-Entwurf 8.5)", b != null && b.Massnahmen.Contains(LeistungRegel.MWinlogonZuruecksetzen));
            h.Ist("Gegenprobe: kein Userinit-Befund", Harness.Einer(erg, "leistung.winlogon.userinit") == null);
            var a = Harness.Einer(erg, "leistung.appinit");
            h.Ist("AppInit ohne Secure Boot, Schalter 1 = warn", a != null && a.Zustand == Zustand.Warn, a != null ? a.Zustand : null);
            h.Ist("AppInit-Satz behauptet „wird … geladen“ nur mit Schalter 1", a != null && a.Satz.Contains("geladen") && a.Satz.Contains("LoadAppInit_DLLs steht auf 1"), a != null ? a.Satz : null);
            h.Ist("Bereich Leistung = bad", Harness.Bereich(erg, Bereich.Leistung).Zustand == Zustand.Bad);
            // Schalter nicht gelesen: warn bleibt, aber ohne die Behauptung "wird geladen".
            s.Autostart.LoadAppInitDlls = null;
            var a2 = Harness.Einer(h.Pruefen(s), "leistung.appinit");
            h.Ist("Schalter unbekannt: warn mit „nicht ermittelbar“, ohne „wird … geladen“", a2 != null && a2.Zustand == Zustand.Warn && a2.Satz.Contains("nicht ermittelbar") && !a2.Satz.Contains("wird diese Bibliothek"), a2 != null ? a2.Satz : null);
        }

        static void Userinit(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (i2): Userinit ohne userinit.exe = bad (ausgetauscht); Shell normal = kein Befund");
            var s = h.Bild("gepflanzt-stabilitaet-userinit.json"); if (s == null) return;
            h.Ist("Grundmenge: Userinit ohne userinit.exe", s.Autostart.Userinit != null && !s.Autostart.Userinit.Contains("userinit.exe"));
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.winlogon.userinit");
            h.Ist("Userinit-Befund bad", b != null && b.Zustand == Zustand.Bad);
            h.Ist("Titel „ausgetauscht“, Satz nennt anmeldung.exe", b != null && b.Titel.Contains("ausgetauscht") && b.Satz.Contains("anmeldung.exe"), b != null ? b.Satz : null);
            h.Ist("Gegenprobe: kein Shell-Befund", Harness.Einer(erg, "leistung.winlogon.shell") == null);
            h.Ist("Gegenprobe: kein AppInit-Befund (leer)", Harness.Einer(erg, "leistung.appinit") == null);
        }

        static void UserinitAngehaengt(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (i3): Userinit mit angehängtem Programm = bad (ergänzt), Satz nennt den Fremdpfad; ohne Schlusskomma = kein Befund");
            var s = h.Bild("gepflanzt-leistung-userinit-angehaengt.json"); if (s == null) return;
            h.Ist("Grundmenge: Userinit enthält userinit.exe UND x.exe", s.Autostart.Userinit != null && s.Autostart.Userinit.Contains("\\system32\\userinit.exe,") && s.Autostart.Userinit.Contains("C:\\Users\\Public\\x.exe"));
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.winlogon.userinit");
            h.Ist("Userinit-Befund bad", b != null && b.Zustand == Zustand.Bad, b != null ? b.Zustand : null);
            if (b == null) return;
            h.Ist("Titel „ergänzt“, nicht „ausgetauscht“", b.Titel.Contains("ergänzt") && !b.Titel.Contains("ausgetauscht"), b.Titel);
            h.Ist("Satz nennt „C:\\Users\\Public\\x.exe“ und nicht „ohne userinit.exe“", b.Satz.Contains("„C:\\Users\\Public\\x.exe“") && !b.Satz.Contains("ohne „userinit.exe“"), b.Satz);
            h.Ist("Maßnahme leistung.winlogon.zuruecksetzen (Rat, M3-Entwurf 8.5)", b.Massnahmen.Contains(LeistungRegel.MWinlogonZuruecksetzen));
            // Gegenproben: der Sollwert in drei Schreibweisen ergibt keinen Befund.
            foreach (string gut in new[] { "C:\\WINDOWS\\system32\\userinit.exe", "C:\\Windows\\system32\\userinit.exe,", "%SystemRoot%\\system32\\userinit.exe,", "\"C:\\Windows\\System32\\userinit.exe\"," })
            {
                s.Autostart.Userinit = gut;
                h.Ist("Gegenprobe: „" + gut + "“ = kein Befund", Harness.Einer(h.Pruefen(s), "leistung.winlogon.userinit") == null);
            }
            s.Autostart.Userinit = "C:\\Windows\\system32\\userinit.exe,C:\\Windows\\system32\\userinit.exe,";
            var d = Harness.Einer(h.Pruefen(s), "leistung.winlogon.userinit");
            h.Ist("Doppelter userinit-Teil = ergänzt (genau ein Teil erlaubt)", d != null && d.Titel.Contains("ergänzt"), d != null ? d.Titel : null);
        }

        static void UserinitKopie(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (i4): gleichnamige Kopie C:\\Users\\Public\\userinit.exe = bad (ausgetauscht)");
            var s = h.Bild("gepflanzt-leistung-userinit-kopie.json"); if (s == null) return;
            h.Ist("Grundmenge: Dateiname userinit.exe außerhalb von system32", s.Autostart.Userinit != null && s.Autostart.Userinit.Contains("userinit.exe") && !s.Autostart.Userinit.ToLowerInvariant().Contains("\\system32\\"));
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "leistung.winlogon.userinit");
            h.Ist("Userinit-Befund bad", b != null && b.Zustand == Zustand.Bad, b != null ? b.Zustand : null);
            h.Ist("Titel „ausgetauscht“, Satz nennt den Fremdpfad", b != null && b.Titel.Contains("ausgetauscht") && b.Satz.Contains("C:\\Users\\Public\\userinit.exe"), b != null ? b.Satz : null);
            h.Ist("Maßnahme leistung.winlogon.zuruecksetzen (Rat, M3-Entwurf 8.5)", b != null && b.Massnahmen.Contains(LeistungRegel.MWinlogonZuruecksetzen));
        }

        // ---------------------------------------------------------------- (j) AppInit

        static void AppInitSchalter(Harness h, List<Befund> alle)
        {
            h.Gruppe("Leistung (j): AppInit_DLLs in beiden Sichten, Hauptschalter 0, kein UEFI = ok („wirkt nicht“); Schalter 1 = warn");
            var s = h.Bild("gepflanzt-leistung-appinit-schalter.json"); if (s == null) return;
            h.Ist("Grundmenge: beide Listen gefüllt, LoadAppInit_DLLs false, Secure Boot null, UEFI false", !string.IsNullOrEmpty(s.Autostart.AppInitDlls) && !string.IsNullOrEmpty(s.Autostart.AppInitDlls32) && s.Autostart.LoadAppInitDlls == false && s.Sicherheit.SecureBoot == null && s.Hardware.Uefi == false);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var a = Harness.Einer(erg, "leistung.appinit");
            h.Ist("AppInit mit Schalter 0 = ok, nicht warn", a != null && a.Zustand == Zustand.Ok, a != null ? a.Zustand : null);
            if (a == null) return;
            h.Ist("Satz nennt den Hauptschalter und „wirkt aber nicht“", a.Satz.Contains("LoadAppInit_DLLs") && a.Satz.Contains("wirkt aber nicht"), a.Satz);
            h.Ist("kein Rat bei ok", a.Rat == null);
            h.Ist("Detail: Secure Boot „nicht vorhanden (BIOS/CSM)“, nicht „unbekannt“", a.Detail.Any(d => d.Contains("nicht vorhanden (BIOS/CSM)")) && !a.Detail.Any(d => d.Contains("unbekannt")));
            h.Ist("Detail nennt die 32-Bit-Liste", a.Detail.Any(d => d.Contains("WOW6432Node") && d.Contains("hook32.dll")));
            h.Ist("Bereich Leistung = ok", Harness.Bereich(erg, Bereich.Leistung).Zustand == Zustand.Ok, Harness.Bereich(erg, Bereich.Leistung).Zustand);
            // Gegenprobe: Schalter 1 ohne Secure Boot (BIOS) = warn, die DLL wird wirklich geladen.
            s.Autostart.LoadAppInitDlls = true;
            var a2 = Harness.Einer(h.Pruefen(s), "leistung.appinit");
            h.Ist("Gegenprobe: Schalter 1 ohne UEFI = warn mit „wird … geladen“", a2 != null && a2.Zustand == Zustand.Warn && a2.Satz.Contains("geladen") && a2.Rat != null, a2 != null ? a2.Satz : null);
            // Nur die 32-Bit-Liste gefuellt: der Satz nennt sie.
            s.Autostart.AppInitDlls = "";
            var a3 = Harness.Einer(h.Pruefen(s), "leistung.appinit");
            h.Ist("Nur 32-Bit-Liste: Befund bleibt, Satz nennt hook32.dll", a3 != null && a3.Satz.Contains("hook32.dll"), a3 != null ? a3.Satz : null);
        }
    }
}
