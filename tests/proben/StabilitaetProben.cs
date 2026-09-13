using System;
using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer den Bereich Stabilitaet gegen gepflanzte Systembilder (Leistung: LeistungProben.cs)
    /// (tests/aufzeichnungen/gepflanzt-stabilitaet-*.json). Jede Probe sichert eine Beziehung zu,
    /// prueft vorher ihre Grundmenge und hat eine Gegenprobe, bei der die Bedingung fehlt.
    ///
    /// Wirksamkeitsproben am 12.09.2026 (Regel absichtlich kaputt gemacht, Rot beobachtet, zurueck):
    ///   - Blauschirm: ">= BlauschirmWarn" auf "> BlauschirmWarn" -> Probe "genau ein warn-Befund" rot.
    ///   - Stromverlust: NeustartAbstand1074Min-Fenster entfernt (Geplant() immer false) -> "kein Stromverlust-Befund bei 1074 davor" rot.
    ///   - Programmabsturz: ">= ProgrammabsturzWarn" auf ">" -> "warn fuer beispiel.exe" rot.
    ///   - WHEA: Level 2 gegen Level 3 vertauscht -> "schwer = bad" rot und "behoben = warn" rot.
    /// Wirksamkeitsproben am 13.09.2026 (Befundrunde Stabilitaet):
    ///   - Einschalttaste: Zweig "else if (taste != 0)" gestrichen -> "genau ein Einschalttaste-Befund" rot
    ///     und "kein Stromverlust-Befund" rot (die zwei 41er fielen in den ok-Stromverlust).
    ///   - Defender-Engine: MsMpEng nicht mehr aus der generischen Gruppe genommen -> "kein generischer
    ///     Programmabsturz-Befund fuer msmpeng.exe" rot; Schwelle "<" auf "<=" -> "genau 2 ergibt den Befund" rot.
    ///   - Kernel-PnP 219: Verlaufszeile entfernt -> "Verlaufszeile nennt Instanz, Treiber, Status" rot.
    ///   - Treibermodul: DriverStore-Test hinter den System32-Test gestellt -> "Rat nennt Treiber, nicht Systemdateien" rot.
    ///   - Fehlerliste: hauptquelleGescheitert ignoriert -> "Bereich unknown bei zeit auf kernel-power" rot.
    ///   - Aeltere Stromverluste: aeltere immer 0 -> "Satz sagt nicht 'keine Abschaltung'" rot.
    ///   - Platzhalter: Unbekannt() sofort verlassen -> "Befund stabilitaet.programmabsturz.unbekannt vorhanden" rot.
    ///   - Stromverlust-Satz: zurueck auf "mehr als die Grenze von 3" -> "Satz sagt 'ab 3-mal'" rot.
    ///   - WHEA: Massnahme Speichertest wieder gesetzt -> "keine Massnahme" in (a2) und WheaSchwer rot.
    /// </summary>
    public static class StabilitaetProben
    {
        public static void Laufen(Harness h)
        {
            var alle = new List<Befund>();
            Blauschirm(h, alle);
            BlauschirmZwei(h, alle);
            StromverlustGeplant(h, alle);
            Stromverlust(h, alle);
            StromverlustAlt(h, alle);
            Einschalttaste(h, alle);
            JungesLog(h, alle);
            Programmabsturz(h, alle);
            ProgrammabsturzUnbekannt(h, alle);
            DefenderEngine(h, alle);
            Gesperrt(h, alle);
            QuelleGescheitert(h, alle);
            WheaSchwer(h, alle);
            WheaBehoben(h, alle);
            Pnp219(h, alle);
            Englisch(h, alle);

            h.Gruppe("Stabilität: jeder Satz trägt Zahl, Bedingung oder Absage");
            h.SaetzeTragen(alle);
            h.Ist("Grundmenge: mindestens 18 Befunde über alle Bilder", alle.Count >= 18, alle.Count.ToString());
            h.Ist("kein gerades Anführungszeichen in Sätzen", alle.All(b => (b.Satz ?? "").IndexOf('"') < 0 && (b.Rat ?? "").IndexOf('"') < 0));
        }

        static List<Befund> Sammeln(List<Befund> alle, List<BereichErgebnis> erg)
        {
            var eigene = Harness.Befunde(erg, Bereich.Stabilitaet).Concat(Harness.Befunde(erg, Bereich.Leistung)).ToList();
            alle.AddRange(eigene);
            return eigene;
        }

        static int Anzahl(Systembild s, string anbieter, int id) { return s.Ereignisse.Von(anbieter, id).Count(); }

        // ---------------------------------------------------------------- (a) Blauschirm

        static void Blauschirm(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (a): Kernel-Power 41 mit BugcheckCode 159 = ein Blauschirm, warn, Treiberklasse");
            var s = h.Bild("gepflanzt-stabilitaet-blauschirm.json"); if (s == null) return;
            h.Ist("Grundmenge: genau ein Kernel-Power 41 mit Code != 0", s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).Count(x => x.FeldZahl("BugcheckCode", 0) != 0) == 1);
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            h.Ist("Daten vorhanden", st.DatenVorhanden);
            var b = Harness.AlleMit(erg, "stabilitaet.blauschirm").ToList();
            h.Ist("genau ein Blauschirm-Befund", b.Count == 1, b.Count.ToString());
            if (b.Count != 1) return;
            h.Ist("Zustand warn (1 Blauschirm, Schwelle bad = " + Schwellen.BlauschirmBad + ")", b[0].Zustand == Zustand.Warn, b[0].Zustand);
            h.Ist("Messwert 1", b[0].Messwert != null && b[0].Messwert.Wert == "1");
            h.Ist("Detail nennt DRIVER_POWER_STATE_FAILURE", b[0].Detail.Any(d => d.Contains("DRIVER_POWER_STATE_FAILURE")));
            h.Ist("Detail nennt die Klasse Treiber", b[0].Detail.Any(d => d.Contains("Treiber")));
            h.Ist("Detail sagt, dass kein Ereignis den Treiber nennt", b[0].Detail.Any(d => d.Contains("Kein Ereignis nennt")));
            h.Ist("Detail nennt den WER-Bericht 1001", b[0].Detail.Any(d => d.Contains("WER 1001")));
            h.Ist("Rat vorhanden und nennt Treiber", b[0].Rat != null && b[0].Rat.Contains("Treiber"));
            h.Ist("Satz nennt den Zeitraum (90 Tage)", b[0].Satz.Contains("90"));
            h.Ist("Quelle = Kernel-Power 41.BugcheckCode", b[0].Quelle == "Kernel-Power 41.BugcheckCode");
            h.Ist("kein Stromverlust-Befund (der 41 hatte einen Code)", Harness.Einer(erg, "stabilitaet.stromverlust") == null);
            h.Ist("keine Übersicht (Neustart-Befund vorhanden)", Harness.Einer(erg, "stabilitaet.uebersicht") == null);
            h.Ist("Zuverlässigkeitsindex im Detail", b[0].Detail.Any(d => d.Contains("Zuverlässigkeitsindex") && d.Contains("7,5")));
            h.Ist("Bereich Stabilität = warn", st.Zustand == Zustand.Warn, st.Zustand);

            // Gegenprobe zur Fehlerliste: scheitert Kernel-Power NACH dem gelesenen Blauschirm (zeit),
            // bleibt der Blauschirm warn - gelesene Eintraege sind gueltig, nur "kein Absturz" waere keine Aussage.
            var s2 = h.Bild("gepflanzt-stabilitaet-blauschirm.json"); if (s2 == null) return;
            s2.Fehlerliste.Add(new Fehler { Quelle = "log.system.kernel-power", Art = Fehler.Zeit, Text = "Obergrenze von 500 Einträgen erreicht: 500 gelesen, ältester vom 2026-08-01T00:00:00Z; ältere Ereignisse fehlen" });
            var erg2 = h.Pruefen(s2);
            var st2 = Harness.Bereich(erg2, Bereich.Stabilitaet);
            var b2 = Harness.Einer(erg2, "stabilitaet.blauschirm");
            h.Ist("Gegenprobe: Blauschirm bleibt warn, obwohl Kernel-Power unvollständig", b2 != null && b2.Zustand == Zustand.Warn);
            h.Ist("Gegenprobe: Bereich bleibt warn (belegt), nicht unknown", st2.Zustand == Zustand.Warn, st2.Zustand);
            h.Ist("Gegenprobe: Fehlend nennt Kernel-Power 41 und den Sammlertext", st2.Fehlend.Any(f => f.Contains("Kernel-Power 41") && f.Contains("ältere Ereignisse fehlen")), string.Join(" | ", st2.Fehlend));
        }

        static void BlauschirmZwei(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (a2): zwei Blauschirme in 90 Tagen = bad, ein dritter außerhalb zählt nicht; 0x124 + WHEA Level 2");
            var s = h.Bild("gepflanzt-stabilitaet-blauschirm-2.json"); if (s == null) return;
            h.Ist("Grundmenge: drei 41 mit Code, einer davon älter als 90 Tage", Anzahl(s, "Microsoft-Windows-Kernel-Power", 41) == 3);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.blauschirm");
            h.Ist("Blauschirm-Befund vorhanden", b != null);
            if (b == null) return;
            h.Ist("Zustand bad (2 >= " + Schwellen.BlauschirmBad + ")", b.Zustand == Zustand.Bad, b.Zustand);
            h.Ist("Messwert 2 (der dritte liegt vor dem Fenster)", b.Messwert.Wert == "2", b.Messwert.Wert);
            h.Ist("Detail nennt WHEA_UNCORRECTABLE_ERROR und MEMORY_MANAGEMENT", b.Detail.Any(d => d.Contains("WHEA_UNCORRECTABLE_ERROR")) && b.Detail.Any(d => d.Contains("MEMORY_MANAGEMENT")));
            h.Ist("Detail korreliert WHEA (1 schwerwiegend)", b.Detail.Any(d => d.Contains("WHEA, schwerwiegend") && d.EndsWith("1")));
            h.Ist("Maßnahme Speichertest (Klasse Arbeitsspeicher) bleibt der Blauschirm-Regel", b.Massnahmen.Contains("stabilitaet.speichertest.planen"));
            var w = Harness.Einer(erg, "stabilitaet.hardwarefehler.schwer");
            h.Ist("WHEA-Befund schwer = bad", w != null && w.Zustand == Zustand.Bad);
            h.Ist("WHEA-Befund trägt keine Maßnahme (Konzept: Rat, keine Maßnahme)", w != null && w.Massnahmen.Count == 0);
        }

        // ---------------------------------------------------------------- (b) (c) Stromverlust

        static void StromverlustGeplant(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (b): 41 mit allen Feldern 0, aber 1074 zwei Minuten davor = geplant, kein Befund");
            var s = h.Bild("gepflanzt-stabilitaet-stromverlust-geplant.json"); if (s == null) return;
            h.Ist("Grundmenge: zwei 41 mit Code 0 und zwei 1074", s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).Count(x => x.FeldZahl("BugcheckCode", 0) == 0) == 2 && Anzahl(s, "User32", 1074) == 2);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            h.Ist("kein Stromverlust-Befund", Harness.Einer(erg, "stabilitaet.stromverlust") == null);
            h.Ist("kein Blauschirm-Befund", Harness.Einer(erg, "stabilitaet.blauschirm") == null);
            var u = Harness.Einer(erg, "stabilitaet.uebersicht");
            h.Ist("Übersicht ok mit Zahl der geplanten Neustarts im Detail", u != null && u.Zustand == Zustand.Ok && u.Detail.Any(d => d.Contains("geplant") && d.EndsWith("2")));
            h.Ist("Übersicht sagt 'keine Abschaltung ohne Herunterfahren' (geplante zählen nicht als ältere)", u != null && u.Satz.Contains("keine Abschaltung ohne Herunterfahren"));
            h.Ist("Bereich Stabilität = ok", Harness.Bereich(erg, Bereich.Stabilitaet).Zustand == Zustand.Ok);

            // Gegenprobe: fehlt die User32-Quelle (zeit), sind dieselben 41 nicht einzuordnen:
            // kein Stromverlust-warn aus Verlegenheit, sondern unknown mit Grund.
            var s2 = h.Bild("gepflanzt-stabilitaet-stromverlust-geplant.json"); if (s2 == null) return;
            s2.Ereignisse.Eintraege.RemoveAll(x => x.Anbieter == "User32");
            s2.Fehlerliste.Add(new Fehler { Quelle = "log.system.user32", Art = Fehler.Zeit, Text = "Zeitbudget von 15 s ausgeschöpft, Abonnement übersprungen" });
            var erg2 = h.Pruefen(s2);
            var st2 = Harness.Bereich(erg2, Bereich.Stabilitaet);
            h.Ist("Gegenprobe: ohne User32-Quelle kein Stromverlust-Befund", Harness.Einer(erg2, "stabilitaet.stromverlust") == null);
            var u2 = Harness.Einer(erg2, "stabilitaet.uebersicht");
            h.Ist("Gegenprobe: Übersicht unknown und nennt User32 1074", u2 != null && u2.Zustand == Zustand.Unknown && u2.Satz.Contains("User32 1074"), u2 != null ? u2.Satz : null);
            h.Ist("Gegenprobe: Bereich unknown, nicht ok und nicht warn", st2.Zustand == Zustand.Unknown, st2.Zustand);
            h.Ist("Gegenprobe: Fehlend nennt die geplanten Neustarts (User32 1074)", st2.Fehlend.Any(f => f.Contains("User32 1074")));
        }

        static void Stromverlust(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (c): drei 41 mit allen Feldern 0 ohne 1074 in 30 Tagen = warn; ein vierter vor dem Fenster zählt nicht");
            var s = h.Bild("gepflanzt-stabilitaet-stromverlust.json"); if (s == null) return;
            h.Ist("Grundmenge: vier 41 mit Code 0, davon drei in 30 Tagen", s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).Count(x => x.FeldZahl("BugcheckCode", 0) == 0) == 4);
            h.Ist("Grundmenge: ein 1074, aber 17 Tage vor dem ersten 41", Anzahl(s, "User32", 1074) == 1);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.stromverlust");
            h.Ist("Stromverlust-Befund vorhanden", b != null);
            if (b == null) return;
            h.Ist("Zustand warn (3 >= " + Schwellen.StromverlustWarn + ")", b.Zustand == Zustand.Warn, b.Zustand);
            h.Ist("Messwert 3", b.Messwert.Wert == "3", b.Messwert.Wert);
            h.Ist("Satz sagt 'ab 3-mal', nicht 'mehr als' (bei genau 3 wäre das falsch)", b.Satz.Contains("ab " + Schwellen.StromverlustWarn + "-mal") && !b.Satz.Contains("mehr als"), b.Satz);
            h.Ist("Detail nennt Kernel-Boot 20 LastShutdownGood=false als Zweitquelle (3)", b.Detail.Any(d => d.Contains("LastShutdownGood=false") && d.EndsWith("3")));
            h.Ist("Detail nennt volmgr 46 (1)", b.Detail.Any(d => d.Contains("volmgr 46") && d.EndsWith("1")));
            h.Ist("Detail nennt die 6008-Zeit aus dem Binärfeld", b.Detail.Any(d => d.Contains("6008") && d.Contains("31.08.2026 19:15")));
            h.Ist("Detail nennt die Auslagerungsdatei", b.Detail.Any(d => d.Contains("Auslagerungsdatei") && d.Contains("3968")));
            h.Ist("Rat nennt Netzteil", b.Rat != null && b.Rat.Contains("Netzteil"));
            h.Ist("kein Blauschirm-Befund", Harness.Einer(erg, "stabilitaet.blauschirm") == null);
        }

        static void StromverlustAlt(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (c2): zwei 41 mit allen Feldern 0 bei 45 und 60 Tagen = kein Befund, aber die Übersicht behauptet nicht 'keine Abschaltung'");
            var s = h.Bild("gepflanzt-stabilitaet-stromverlust-alt.json"); if (s == null) return;
            var alle41 = s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).ToList();
            h.Ist("Grundmenge: zwei 41 mit allen Feldern 0, kein 1074, beide älter als " + Schwellen.StromverlustTage + " Tage",
                alle41.Count == 2 && alle41.All(x => x.FeldZahl("BugcheckCode", 0) == 0 && x.FeldZahl("PowerButtonTimestamp", 0) == 0)
                && Anzahl(s, "User32", 1074) == 0
                && alle41.All(x => (Zeit.TageZwischen(x.ZeitUtc, s.AufgezeichnetUtc) ?? 0) > Schwellen.StromverlustTage && (Zeit.TageZwischen(x.ZeitUtc, s.AufgezeichnetUtc) ?? 999) <= Schwellen.StromverlustAeltereTage));
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            h.Ist("kein Stromverlust-Befund (außerhalb des 30-Tage-Fensters)", Harness.Einer(erg, "stabilitaet.stromverlust") == null);
            h.Ist("kein Problem-Befund", !eigene.Any(b => b.IstProblem));
            var u = Harness.Einer(erg, "stabilitaet.uebersicht");
            h.Ist("Übersicht vorhanden und ok", u != null && u.Zustand == Zustand.Ok);
            if (u == null) return;
            h.Ist("Satz sagt NICHT 'keine Abschaltung'", !u.Satz.Contains("keine Abschaltung"), u.Satz);
            h.Ist("Satz nennt die Zahl 2 und das 30-Tage-Fenster", u.Satz.Contains("2 Abschaltungen") && u.Satz.Contains(Schwellen.StromverlustTage + " Tage"), u.Satz);
            h.Ist("Satz behält die 90-Tage-Aussage zum Blauschirm", u.Satz.Contains("Kein Blauschirm") && u.Satz.Contains("90"), u.Satz);
            h.Ist("Messwert 2, nicht 0", u.Messwert != null && u.Messwert.Wert == "2", u.Messwert != null ? u.Messwert.Wert : null);
            h.Ist("Detail-Zeile nennt die älteren Abschaltungen mit 2", u.Detail.Any(d => d.Contains("nicht bewertet") && d.EndsWith("2")), string.Join(" | ", u.Detail));
            h.Ist("Bereich Stabilität = ok", st.Zustand == Zustand.Ok, st.Zustand);
        }

        // ---------------------------------------------------------------- Einschalttaste (dritter Konzeptfall von 41)

        static void Einschalttaste(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität: zwei 41 mit Code 0 und PowerButtonTimestamp != 0 in 30 Tagen = ok-Information Einschalttaste, kein Stromverlust");
            var s = h.Bild("gepflanzt-stabilitaet-einschalttaste.json"); if (s == null) return;
            var alle41 = s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).ToList();
            h.Ist("Grundmenge: zwei 41, Code 0, 18-stelliger PowerButtonTimestamp, innerhalb 30 Tagen, kein 1074",
                alle41.Count == 2 && alle41.All(x => x.FeldZahl("BugcheckCode", 0) == 0 && x.Feld("PowerButtonTimestamp").Length == 18 && x.FeldZahl("PowerButtonTimestamp", 0) > int.MaxValue
                    && (Zeit.TageZwischen(x.ZeitUtc, s.AufgezeichnetUtc) ?? 999) <= Schwellen.StromverlustTage) && Anzahl(s, "User32", 1074) == 0);
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            var b = Harness.AlleMit(erg, "stabilitaet.einschalttaste").ToList();
            h.Ist("genau ein Einschalttaste-Befund", b.Count == 1, b.Count.ToString());
            h.Ist("kein Stromverlust-Befund (PowerButtonTimestamp != 0 ist kein Stromverlust)", Harness.Einer(erg, "stabilitaet.stromverlust") == null);
            h.Ist("keine Übersicht (Neustart-Befund vorhanden)", Harness.Einer(erg, "stabilitaet.uebersicht") == null);
            if (b.Count != 1) return;
            h.Ist("Zustand ok", b[0].Zustand == Zustand.Ok, b[0].Zustand);
            h.Ist("Messwert 2", b[0].Messwert != null && b[0].Messwert.Wert == "2");
            h.Ist("Quelle = Kernel-Power 41.PowerButtonTimestamp", b[0].Quelle == "Kernel-Power 41.PowerButtonTimestamp");
            h.Ist("Satz nennt die Einschalttaste", b[0].Satz.Contains("Einschalttaste"), b[0].Satz);
            h.Ist("Detail nennt den Zeitstempel (long-Parse, kein int-Rückfall)", b[0].Detail.Any(d => d.Contains("134306054393450499")));
            h.Ist("Zuverlässigkeitsindex hängt am Einschalttaste-Befund", b[0].Detail.Any(d => d.Contains("Zuverlässigkeitsindex")));
            h.Ist("kein Problem-Befund", !eigene.Any(x => x.IstProblem));
            h.Ist("Bereich Stabilität = ok", st.Zustand == Zustand.Ok, st.Zustand);
        }

        // ---------------------------------------------------------------- (d) junges Log

        static void JungesLog(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (d): Log 20 Tage alt, 0 Ereignisse = kein Problem, Satz nennt den Log-Beginn");
            var s = h.Bild("gepflanzt-stabilitaet-junges-log.json"); if (s == null) return;
            h.Ist("Grundmenge: keine Einträge, Beginn 20 Tage vor der Aufzeichnung", s.Ereignisse.Eintraege.Count == 0 && Math.Round(Zeit.TageZwischen(s.Ereignisse.BeginnSystemUtc, s.AufgezeichnetUtc) ?? 0) == 20);
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            h.Ist("Daten vorhanden (leeres Log mit Beginn ist Daten)", st.DatenVorhanden);
            h.Ist("kein Problem-Befund", !eigene.Any(b => b.IstProblem));
            h.Ist("Bereich Stabilität = ok, nicht unknown", st.Zustand == Zustand.Ok, st.Zustand);
            var u = Harness.Einer(erg, "stabilitaet.uebersicht");
            h.Ist("Übersicht-Satz sagt 'seit Beginn des Protokolls vor 20 Tagen'", u != null && u.Satz.Contains("seit Beginn des Protokolls vor 20 Tagen"), u != null ? u.Satz : null);
            h.Ist("Fehlend nennt den Log-Beginn", st.Fehlend.Any(f => f.Contains("Beginn des Protokolls")));
            h.Ist("Übersicht-Satz sagt NICHT 'in den letzten 90 Tagen'", u != null && !u.Satz.Contains("in den letzten 90 Tagen"));
        }

        // ---------------------------------------------------------------- (e) Programmabsturz

        static void Programmabsturz(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (e): dreimal 1000 für dasselbe Programm in 30 Tagen = warn; zweimal = kein Befund; älterer zählt nicht; DriverStore-Modul = Treiber-Rat");
            var s = h.Bild("gepflanzt-stabilitaet-programmabsturz.json"); if (s == null) return;
            h.Ist("Grundmenge: neun 1000, davon vier beispiel.exe (einer älter als 30 Tage), zwei zwei.exe, drei drei.exe", Anzahl(s, "Application Error", 1000) == 9);
            h.Ist("Grundmenge: drei.exe mit ModulePath unter DriverStore", s.Ereignisse.Von("Application Error", 1000).Where(x => x.Feld("AppName") == "drei.exe").All(x => (x.Feld("ModulePath") ?? "").Contains("\\DriverStore\\")));
            h.Ist("Grundmenge: WER deaktiviert gepflanzt", s.Sicherheit.WerDeaktiviert == true);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.programmabsturz.beispiel.exe");
            h.Ist("warn für beispiel.exe (Groß-/Kleinschreibung zusammengefasst)", b != null && b.Zustand == Zustand.Warn);
            if (b == null) return;
            h.Ist("Messwert 3 (der Absturz vom 15.07. liegt vor dem Fenster)", b.Messwert.Wert == "3", b.Messwert.Wert);
            h.Ist("Detail nennt ntdll als Opfer, nicht Ursache", b.Detail.Any(d => d.Contains("ntdll") && d.Contains("Opfer")));
            h.Ist("Detail nennt abgeschaltete Fehlerberichte", b.Detail.Any(d => d.Contains("Fehlerberichte")));
            h.Ist("Detail zählt den Hänger (1002) dazu", b.Detail.Any(d => d.Contains("eingefroren")));
            h.Ist("Rat nennt Systemdateien (Windows-Modul beteiligt)", b.Rat != null && b.Rat.Contains("Systemdateien"));
            h.Ist("Satz nennt den Programmnamen in deutschen Anführungszeichen", b.Satz.Contains("„beispiel.exe“") || b.Satz.Contains("„Beispiel.exe“"));
            h.Ist("Gegenprobe: kein Befund für zwei.exe (nur 2 Abstürze)", Harness.Einer(erg, "stabilitaet.programmabsturz.zwei.exe") == null);
            var d = Harness.Einer(erg, "stabilitaet.programmabsturz.drei.exe");
            h.Ist("warn für drei.exe (DriverStore-Modul)", d != null && d.Zustand == Zustand.Warn);
            if (d != null)
            {
                h.Ist("Rat nennt Treiber, nicht Systemdateien (SFC prüft Fremdtreiber nicht)", d.Rat.Contains("Treiber") && !d.Rat.Contains("Systemdateien"), d.Rat);
                h.Ist("Detail kennzeichnet nvml.dll als Treibermodul", d.Detail.Any(x => x.Contains("nvml.dll") && x.Contains("(Treibermodul")), string.Join(" | ", d.Detail));
            }
            h.Ist("genau zwei Programmabsturz-Befunde (beispiel.exe, drei.exe)", Harness.AlleMit(erg, "stabilitaet.programmabsturz.").Count() == 2, Harness.AlleMit(erg, "stabilitaet.programmabsturz.").Count().ToString());
        }

        static void ProgrammabsturzUnbekannt(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (e2): dreimal 1000 mit Platzhalter bad_module_info = eigener Befund ohne Programmnamen, ohne Neuinstallations-Rat");
            var s = h.Bild("gepflanzt-stabilitaet-programmabsturz-unbekannt.json"); if (s == null) return;
            h.Ist("Grundmenge: drei 1000 mit AppName bad_module_info und ein 1002 dazu", s.Ereignisse.Von("Application Error", 1000).Count(x => x.Feld("AppName") == "bad_module_info") == 3 && Anzahl(s, "Application Hang", 1002) == 1);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.programmabsturz.unbekannt");
            h.Ist("Befund stabilitaet.programmabsturz.unbekannt vorhanden, warn", b != null && b.Zustand == Zustand.Warn);
            h.Ist("kein Befund mit dem Platzhalter im Schlüssel", Harness.Einer(erg, "stabilitaet.programmabsturz.bad_module_info") == null);
            if (b == null) return;
            h.Ist("Messwert 3", b.Messwert.Wert == "3", b.Messwert.Wert);
            h.Ist("Satz nennt keinen Programmnamen in Anführungszeichen", !b.Satz.Contains("„"), b.Satz);
            h.Ist("Satz sagt, dass das Programm nicht erkannt wurde", b.Satz.Contains("ohne das Programm zu erkennen"), b.Satz);
            h.Ist("Rat verweist auf die Zeitpunkte, nicht auf Neuinstallation", b.Rat.Contains("Zeitpunkte") && !b.Rat.Contains("neu installieren"), b.Rat);
            h.Ist("Detail ohne Platzhalter-Version und -Ausnahmecode", !b.Detail.Any(d => d.Contains("0.0.0.0") || d.Contains("Ausnahmecode")), string.Join(" | ", b.Detail));
            h.Ist("Detail nennt drei Zeitpunkte", b.Detail.Count(d => d.Contains("UTC")) == 3);
            h.Ist("kein Hang-Abgleich für die Platzhalter-Gruppe", !b.Detail.Any(d => d.Contains("eingefroren")));
            h.Ist("genau ein Programmabsturz-Befund", Harness.AlleMit(erg, "stabilitaet.programmabsturz.").Count() == 1);
        }

        static void DefenderEngine(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität: MsMpEng.exe 1000 >= " + Schwellen.DefenderEngineAbsturzWarn + " in 30 Tagen = Defender-Engine warn mit Maßnahme Signaturen, kein generischer Programmabsturz");
            var s = h.Bild("gepflanzt-stabilitaet-defender-engine.json"); if (s == null) return;
            var msmpeng = s.Ereignisse.Von("Application Error", 1000).Where(x => string.Equals(x.Feld("AppName"), "MsMpEng.exe", StringComparison.OrdinalIgnoreCase)).ToList();
            h.Ist("Grundmenge: drei MsMpEng.exe (eines klein geschrieben) mit mpengine.dll, alle in 30 Tagen, plus ein zwei.exe",
                msmpeng.Count == 3 && msmpeng.All(x => x.Feld("ModuleName") == "mpengine.dll" && (Zeit.TageZwischen(x.ZeitUtc, s.AufgezeichnetUtc) ?? 999) <= Schwellen.ProgrammabsturzTage)
                && Anzahl(s, "Application Error", 1000) == 4);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.defender.engine");
            h.Ist("Befund stabilitaet.defender.engine vorhanden, warn", b != null && b.Zustand == Zustand.Warn);
            h.Ist("kein generischer Programmabsturz-Befund für msmpeng.exe (auch bei 3 >= " + Schwellen.ProgrammabsturzWarn + ")", Harness.Einer(erg, "stabilitaet.programmabsturz.msmpeng.exe") == null);
            h.Ist("kein Programmabsturz-Befund überhaupt (zwei.exe nur 1)", !Harness.AlleMit(erg, "stabilitaet.programmabsturz.").Any());
            if (b == null) return;
            h.Ist("Messwert 3", b.Messwert.Wert == "3", b.Messwert.Wert);
            h.Ist("Maßnahme = Sicherheit.MassnahmeSignaturen", b.Massnahmen.Count == 1 && b.Massnahmen[0] == WartungsToolbox.Kern.Regeln.Sicherheit.MassnahmeSignaturen, string.Join(",", b.Massnahmen));
            h.Ist("Rat nennt Signaturen und sagt nicht 'neu installieren' als Weg", b.Rat.Contains("Signaturen") && !b.Rat.Contains("Programm neu installieren"), b.Rat);
            h.Ist("Satz nennt MsMpEng.exe und die Schwelle", b.Satz.Contains("MsMpEng.exe") && b.Satz.Contains("ab " + Schwellen.DefenderEngineAbsturzWarn + "-mal"), b.Satz);
            h.Ist("Detail nennt mpengine.dll mit Pfad je Ereignis", b.Detail.Count(d => d.Contains("mpengine.dll") && d.Contains("Definition Updates")) == 3, string.Join(" | ", b.Detail));

            // Schwelle genau: mit exakt DefenderEngineAbsturzWarn Ereignissen entsteht der Befund (">=" nicht ">"),
            // mit einem weniger nicht. Beide Varianten aus demselben Bild, die aeltesten Ereignisse entfernt.
            var neueste = msmpeng.OrderByDescending(x => x.ZeitUtc).Select(x => x.ZeitUtc).ToList();
            var s2 = h.Bild("gepflanzt-stabilitaet-defender-engine.json"); if (s2 == null) return;
            s2.Ereignisse.Eintraege.RemoveAll(x => string.Equals(x.Feld("AppName"), "MsMpEng.exe", StringComparison.OrdinalIgnoreCase) && !neueste.Take(Schwellen.DefenderEngineAbsturzWarn).Contains(x.ZeitUtc));
            h.Ist("Schwelle Grundmenge: genau " + Schwellen.DefenderEngineAbsturzWarn + " MsMpEng.exe", s2.Ereignisse.Von("Application Error", 1000).Count(x => string.Equals(x.Feld("AppName"), "MsMpEng.exe", StringComparison.OrdinalIgnoreCase)) == Schwellen.DefenderEngineAbsturzWarn);
            var b2 = Harness.Einer(h.Pruefen(s2), "stabilitaet.defender.engine");
            h.Ist("Schwelle: genau " + Schwellen.DefenderEngineAbsturzWarn + " ergibt den Befund (>=, nicht >)", b2 != null && b2.Messwert.Wert == Schwellen.DefenderEngineAbsturzWarn.ToString());

            var s3 = h.Bild("gepflanzt-stabilitaet-defender-engine.json"); if (s3 == null) return;
            s3.Ereignisse.Eintraege.RemoveAll(x => string.Equals(x.Feld("AppName"), "MsMpEng.exe", StringComparison.OrdinalIgnoreCase) && !neueste.Take(Schwellen.DefenderEngineAbsturzWarn - 1).Contains(x.ZeitUtc));
            h.Ist("Gegenprobe Grundmenge: genau " + (Schwellen.DefenderEngineAbsturzWarn - 1) + " MsMpEng.exe", s3.Ereignisse.Von("Application Error", 1000).Count(x => string.Equals(x.Feld("AppName"), "MsMpEng.exe", StringComparison.OrdinalIgnoreCase)) == Schwellen.DefenderEngineAbsturzWarn - 1);
            var erg3 = h.Pruefen(s3);
            h.Ist("Gegenprobe: " + (Schwellen.DefenderEngineAbsturzWarn - 1) + " < " + Schwellen.DefenderEngineAbsturzWarn + " ergibt keinen Defender-Befund", Harness.Einer(erg3, "stabilitaet.defender.engine") == null);
            h.Ist("Gegenprobe: und keinen generischen Programmabsturz", !Harness.AlleMit(erg3, "stabilitaet.programmabsturz.").Any());
        }

        // ---------------------------------------------------------------- (f) gesperrtes Log

        static void Gesperrt(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (f): Diagnostics-Performance gesperrt = Fehlend, kein Befund dazu");
            var s = h.Bild("gepflanzt-stabilitaet-gesperrt.json"); if (s == null) return;
            h.Ist("Grundmenge: Log in der Sperrliste und Fehler zugriff eingetragen", s.Ereignisse.Gesperrt.Count == 1 && s.ZugriffVerweigert("log.diagnostics-performance"));
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            h.Ist("Daten vorhanden (System-Log selbst lesbar)", st.DatenVorhanden);
            h.Ist("Fehlend nennt die Startdauer und Administratorrechte", st.Fehlend.Any(f => f.Contains("Startdauer") && f.Contains("Administratorrechte")), string.Join(" | ", st.Fehlend));
            h.Ist("Fehlend nennt die Startdauer genau einmal (Sperrliste und Fehlerliste ergeben keine Doppelzeile)", st.Fehlend.Count(f => f.Contains("Startdauer")) == 1);
            h.Ist("kein Problem-Befund", !eigene.Any(b => b.IstProblem));
            h.Ist("kein Befund zur Startdauer", !eigene.Any(b => b.Detail.Any(d => d.Contains("Startdauer"))));
            h.Ist("Bereich Stabilität = ok (gesperrtes Nebenlog kippt nicht auf unknown)", st.Zustand == Zustand.Ok, st.Zustand);

            // Gegenprobe: dieselbe Quelle mit "ausnahme" (beschaedigtes Protokoll) ist kein Rechteproblem.
            var s2 = h.Bild("gepflanzt-stabilitaet-gesperrt.json"); if (s2 == null) return;
            s2.Ereignisse.Gesperrt.Clear();
            s2.Fehlerliste.Clear();
            s2.Fehlerliste.Add(new Fehler { Quelle = "log.diagnostics-performance", Art = Fehler.Ausnahme, Text = "EventLogInvalidDataException: Die Daten sind ungültig" });
            var st2 = Harness.Bereich(h.Pruefen(s2), Bereich.Stabilitaet);
            h.Ist("Gegenprobe: Fehlend nennt den Ausnahmetext statt Administratorrechte", st2.Fehlend.Any(f => f.Contains("Startdauer") && f.Contains("EventLogInvalidDataException") && !f.Contains("Administratorrechte")), string.Join(" | ", st2.Fehlend));
            h.Ist("Gegenprobe: Bereich bleibt ok", st2.Zustand == Zustand.Ok, st2.Zustand);
        }

        // ---------------------------------------------------------------- Fehlerliste jeder Art

        static void QuelleGescheitert(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität: Fehler 'zeit' auf log.system.kernel-power = Übersicht unknown, Bereich unknown, Fehlend nennt beide Quellen");
            var s = h.Bild("gepflanzt-stabilitaet-quelle-zeit.json"); if (s == null) return;
            h.Ist("Grundmenge: Einträge vorhanden, Beginn gesetzt, zeit-Fehler auf kernel-power und application-error, kein zugriff",
                s.Ereignisse.Eintraege.Count > 0 && s.Ereignisse.BeginnSystemUtc != null
                && s.FehlerVon("log.system.kernel-power").Any(f => f.Art == Fehler.Zeit) && s.FehlerVon("log.application.application-error").Any(f => f.Art == Fehler.Zeit)
                && !s.ZugriffVerweigert("log."));
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            h.Ist("Daten NICHT vorhanden (Hauptquelle gescheitert)", !st.DatenVorhanden);
            h.Ist("Bereich unknown, nicht ok", st.Zustand == Zustand.Unknown, st.Zustand);
            var u = Harness.Einer(erg, "stabilitaet.uebersicht");
            h.Ist("Übersicht vorhanden mit Zustand unknown", u != null && u.Zustand == Zustand.Unknown, u != null ? u.Zustand : null);
            h.Ist("Übersicht-Satz sagt 'ließ sich nicht' und nennt Kernel-Power 41", u != null && u.Satz.Contains("ließ sich nicht") && u.Satz.Contains("Kernel-Power 41"), u != null ? u.Satz : null);
            h.Ist("Übersicht-Satz sagt NICHT 'Kein Blauschirm'", u != null && !u.Satz.Contains("Kein Blauschirm"));
            h.Ist("Fehlend nennt Kernel-Power 41 mit dem Sammlertext", st.Fehlend.Any(f => f.Contains("Kernel-Power 41") && f.Contains("ReadEvent")), string.Join(" | ", st.Fehlend));
            h.Ist("Fehlend nennt Application Error 1000 (zeit, nicht nur zugriff)", st.Fehlend.Any(f => f.Contains("Application Error 1000") && f.Contains("Zeitbudget")), string.Join(" | ", st.Fehlend));
            h.Ist("Zuverlässigkeitsindex trotzdem im Detail", u != null && u.Detail.Any(d => d.Contains("Zuverlässigkeitsindex")));
            h.Ist("kein Problem-Befund (nichts belegt)", !eigene.Any(b => b.IstProblem));

            // Gegenprobe: ohne die Fehlereintraege ist dasselbe Bild ok.
            var s2 = h.Bild("gepflanzt-stabilitaet-quelle-zeit.json"); if (s2 == null) return;
            s2.Fehlerliste.Clear();
            var st2 = Harness.Bereich(h.Pruefen(s2), Bereich.Stabilitaet);
            h.Ist("Gegenprobe: ohne Fehlerliste Daten vorhanden und Bereich ok", st2.DatenVorhanden && st2.Zustand == Zustand.Ok, st2.Zustand);
            h.Ist("Gegenprobe: Fehlend nennt weder Kernel-Power noch Application Error", !st2.Fehlend.Any(f => f.Contains("Kernel-Power") || f.Contains("Application Error")), string.Join(" | ", st2.Fehlend));

            // Gegenprobe 2: Fehler auf log.system.beginn, Beginn fehlt - kein "in den letzten 90 Tagen".
            var s3 = h.Bild("gepflanzt-stabilitaet-quelle-zeit.json"); if (s3 == null) return;
            s3.Fehlerliste.Clear();
            s3.Ereignisse.BeginnSystemUtc = null;
            s3.Fehlerliste.Add(new Fehler { Quelle = "log.system.beginn", Art = Fehler.Ausnahme, Text = "EventLogException: Der Ereignisdienst antwortete nicht" });
            // Ein Blauschirm im Bild, damit ein Satz den Zeitraum-Helfer wirklich benutzt; sonst
            // prueft die Zusicherung darunter eine leere Menge (Nachpruefung 13.09.2026).
            s3.Ereignisse.Eintraege.Add(new Ereignis
            {
                Log = "System", Anbieter = "Microsoft-Windows-Kernel-Power", Id = 41, Level = 1, ZeitUtc = "2026-09-08T10:00:00Z",
                Felder = new Dictionary<string, string> { { "BugcheckCode", "159" }, { "SleepInProgress", "0" }, { "PowerButtonTimestamp", "0" } },
            });
            var erg3 = h.Pruefen(s3);
            var st3 = Harness.Bereich(erg3, Bereich.Stabilitaet);
            var u3 = Harness.Einer(erg3, "stabilitaet.uebersicht");
            var bs3 = Harness.Einer(erg3, "stabilitaet.blauschirm");
            h.Ist("Gegenprobe 2: Blauschirm-Befund vorhanden (Grundmenge fuer die Satzpruefung)", bs3 != null);
            h.Ist("Gegenprobe 2: Beginn gescheitert = Bereich nicht ok", st3.Zustand != Zustand.Ok, st3.Zustand);
            h.Ist("Gegenprobe 2: kein Satz behauptet 'in den letzten 90 Tagen'", Harness.Befunde(erg3, Bereich.Stabilitaet).All(b => !(b.Satz ?? "").Contains("in den letzten 90 Tagen")), u3 != null ? u3.Satz : null);
            h.Ist("Gegenprobe 2: der Blauschirm-Satz nennt die unbekannte Reichweite", bs3 != null && (bs3.Satz ?? "").Contains("Reichweite unbekannt"), bs3 != null ? bs3.Satz : null);
            h.Ist("Gegenprobe 2: Fehlend nennt den Beginn des Systemprotokolls", st3.Fehlend.Any(f => f.Contains("Beginn des Systemprotokolls")));
        }

        // ---------------------------------------------------------------- WHEA

        static void WheaSchwer(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität: ein WHEA Level 2 = bad ohne Maßnahme; neun Level 3 = kein warn (Schwelle " + Schwellen.WheaKorrigierbarWarn + ")");
            var s = h.Bild("gepflanzt-stabilitaet-whea-schwer.json"); if (s == null) return;
            var whea = s.Ereignisse.Von("Microsoft-Windows-WHEA-Logger").ToList();
            h.Ist("Grundmenge: 1 Level 2 und 9 Level 3", whea.Count(x => x.Level == 2) == 1 && whea.Count(x => x.Level == 3) == 9);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.hardwarefehler.schwer");
            h.Ist("schwer = bad", b != null && b.Zustand == Zustand.Bad);
            h.Ist("Messwert 1", b != null && b.Messwert.Wert == "1");
            h.Ist("Detail nennt MCA-Bank", b != null && b.Detail.Any(d => d.Contains("MCA-Bank 1")));
            h.Ist("keine Maßnahme (Konzept 4.3: Rat, keine Maßnahme; Speichertest gehört der Blauschirm-Regel)", b != null && b.Massnahmen.Count == 0, b != null ? string.Join(",", b.Massnahmen) : null);
            h.Ist("Rat vorhanden", b != null && !string.IsNullOrEmpty(b.Rat));
            h.Ist("Gegenprobe: kein warn für behobene Fehler (9 < " + Schwellen.WheaKorrigierbarWarn + ")", Harness.Einer(erg, "stabilitaet.hardwarefehler.behoben") == null);
        }

        static void WheaBehoben(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität: zehn WHEA Level 3 in 30 Tagen = warn, zwei ältere zählen nicht; kein Level 2 = kein bad");
            var s = h.Bild("gepflanzt-stabilitaet-whea-behoben.json"); if (s == null) return;
            var whea = s.Ereignisse.Von("Microsoft-Windows-WHEA-Logger").ToList();
            h.Ist("Grundmenge: 12 Level 3, 0 Level 2", whea.Count(x => x.Level == 3) == 12 && whea.Count(x => x.Level == 2) == 0);
            var erg = h.Pruefen(s);
            Sammeln(alle, erg);
            var b = Harness.Einer(erg, "stabilitaet.hardwarefehler.behoben");
            h.Ist("behoben = warn", b != null && b.Zustand == Zustand.Warn);
            h.Ist("Messwert 10 (die zwei vom August liegen vor dem Fenster)", b != null && b.Messwert.Wert == "10", b != null ? b.Messwert.Wert : null);
            h.Ist("Rat ordnet nicht 'meist Arbeitsspeicher' zu (die Ereignisse geben das nicht her)", b != null && !b.Rat.Contains("Meist") && b.Rat.Contains("Je nach Quelle"), b != null ? b.Rat : null);
            h.Ist("keine Maßnahme", b != null && b.Massnahmen.Count == 0);
            h.Ist("Gegenprobe: kein bad ohne Level 2", Harness.Einer(erg, "stabilitaet.hardwarefehler.schwer") == null);
        }

        // ---------------------------------------------------------------- Kernel-PnP 219 (Verlauf, kein Befund)

        static void Pnp219(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität: Kernel-PnP 219 dreimal für dasselbe Gerät = KEIN Befund (Konzept 4.1: nur Historie), aber eine Verlaufszeile mit Instanz, Treiber und Status");
            var s = h.Bild("gepflanzt-stabilitaet-pnp219.json"); if (s == null) return;
            h.Ist("Grundmenge: sechs 219, zwei Geräte, alle mit FailureName \\Driver\\WudfRd", Anzahl(s, "Microsoft-Windows-Kernel-PnP", 219) == 6 && s.Ereignisse.Von("Microsoft-Windows-Kernel-PnP", 219).All(x => x.Feld("FailureName") == "\\Driver\\WudfRd"));
            var erg = h.Pruefen(s);
            var eigene = Sammeln(alle, erg);
            var st = Harness.Bereich(erg, Bereich.Stabilitaet);
            h.Ist("kein stabilitaet.treiberstart-Befund", !Harness.AlleMit(erg, "stabilitaet.treiberstart.").Any());
            h.Ist("kein Problem-Befund (drei Ansteckvorgänge eines iPhones sind kein Problem)", !eigene.Any(b => b.IstProblem));
            h.Ist("Bereich Stabilität = ok", st.Zustand == Zustand.Ok, st.Zustand);
            var u = Harness.Einer(erg, "stabilitaet.uebersicht");
            h.Ist("Übersicht vorhanden", u != null);
            if (u == null) return;
            var zeilen = u.Detail.Where(d => d.Contains("Kernel-PnP 219")).ToList();
            h.Ist("Verlaufszeile je Gerät (zwei Geräte, Gruppierung über DriverName = Geräteinstanz, nicht über den Treiber)", zeilen.Count == 2, string.Join(" | ", zeilen));
            var iphone = zeilen.FirstOrDefault(z => z.Contains("VID_05AC"));
            h.Ist("Verlaufszeile nennt Instanz, Treiber WudfRd und Status 0xC0000365", iphone != null && iphone.Contains("WudfRd") && iphone.Contains("0xC0000365"), iphone);
            h.Ist("Verlaufszeile nennt den Anzeigenamen aus der Geräteliste und 3-mal", iphone != null && iphone.Contains("Apple iPhone") && iphone.Contains("3-mal"), iphone);
            h.Ist("Verlaufszeile sagt 'kein Befund'", iphone != null && iphone.Contains("kein Befund"));
            var zweites = zeilen.FirstOrDefault(z => z.Contains("VID_1234"));
            h.Ist("zweites Gerät: 2-mal im Fenster (der dritte 219 vom 20.07. liegt davor)", zweites != null && zweites.Contains("2-mal"), zweites);
        }

        // ---------------------------------------------------------------- (j) englisch

        static void Englisch(Harness h, List<Befund> alle)
        {
            h.Gruppe("Stabilität (j): englisches Bild (LCID 1033, englische Anzeigetexte) liefert dieselben Befunde wie (a)");
            var de = h.Bild("gepflanzt-stabilitaet-blauschirm.json");
            var en = h.Bild("gepflanzt-stabilitaet-blauschirm-en.json");
            if (de == null || en == null) return;
            h.Ist("Grundmenge: LCID 1031 gegen 1033, gleiche Ereigniszahl", de.Sprache.Lcid == 1031 && en.Sprache.Lcid == 1033 && de.Ereignisse.Eintraege.Count == en.Ereignisse.Eintraege.Count);
            h.Ist("Grundmenge: Anzeigetexte unterscheiden sich (1074-Feld 2)", de.Ereignisse.Von("User32", 1074).First().Feld("2") != en.Ereignisse.Von("User32", 1074).First().Feld("2"));
            var ergDe = h.Pruefen(de);
            var ergEn = h.Pruefen(en);
            Sammeln(alle, ergEn);
            var bDe = Harness.Befunde(ergDe, Bereich.Stabilitaet).OrderBy(b => b.Schluessel).ToList();
            var bEn = Harness.Befunde(ergEn, Bereich.Stabilitaet).OrderBy(b => b.Schluessel).ToList();
            h.Ist("gleiche Anzahl Befunde", bDe.Count == bEn.Count, bDe.Count + " gegen " + bEn.Count);
            h.Ist("mindestens ein Befund", bDe.Count >= 1);
            for (int i = 0; i < Math.Min(bDe.Count, bEn.Count); i++)
            {
                h.Ist("gleicher Schlüssel: " + bDe[i].Schluessel, bDe[i].Schluessel == bEn[i].Schluessel, bEn[i].Schluessel);
                h.Ist("gleicher Zustand: " + bDe[i].Schluessel, bDe[i].Zustand == bEn[i].Zustand);
                h.Ist("gleicher Messwert: " + bDe[i].Schluessel, (bDe[i].Messwert == null ? null : bDe[i].Messwert.Wert) == (bEn[i].Messwert == null ? null : bEn[i].Messwert.Wert));
                h.Ist("gleicher Satz: " + bDe[i].Schluessel, bDe[i].Satz == bEn[i].Satz);
            }
            h.Ist("Bereichs-Zustand gleich", Harness.Bereich(ergDe, Bereich.Stabilitaet).Zustand == Harness.Bereich(ergEn, Bereich.Stabilitaet).Zustand);
        }
    }
}
