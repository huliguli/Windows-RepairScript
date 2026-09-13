using System;
using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

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
            h.Ist("Maßnahme autostart.deaktivieren", b.Massnahmen.Contains("autostart.deaktivieren"));
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
            h.Ist("Untergrenze über der Schwelle: warn bleibt, Satz sagt „Mindestens“ und „Untergrenze“", b3 != null && b3.Zustand == Zustand.Warn && b3.Satz.Contains("Mindestens") && b3.Satz.Contains("Untergrenze") && b3.Massnahmen.Contains("autostart.deaktivieren"), b3 != null ? b3.Satz : null);
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
            h.Ist("Maßnahme autostart.winlogon.zuruecksetzen", b != null && b.Massnahmen.Contains("autostart.winlogon.zuruecksetzen"));
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
            h.Ist("Maßnahme autostart.winlogon.zuruecksetzen", b.Massnahmen.Contains("autostart.winlogon.zuruecksetzen"));
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
            h.Ist("Maßnahme autostart.winlogon.zuruecksetzen", b != null && b.Massnahmen.Contains("autostart.winlogon.zuruecksetzen"));
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
