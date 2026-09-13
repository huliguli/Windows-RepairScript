using System;
using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer die Windows-Update-Regeln gegen gepflanzte Systembilder
    /// (tests/aufzeichnungen/gepflanzt-updates-*.json, Aufzeichnungszeit 2026-09-11T16:00:00Z).
    ///
    /// Jede Regel hat eine Gegenprobe (Bedingung fehlt -> kein Befund). Wirksamkeitsprobe am
    /// 12.09.2026: Schleifen- und Fehlschlag-Vergleich in Updates.cs von ">=" auf "<" gedreht,
    /// die Proben "genau ein Schleifen-Befund" (a), "kein Schleifen-Befund" (b) und "genau ein
    /// Fehlschlag-Befund" (c) wurden rot; zurueckgedreht, wieder gruen. Ebenso am 12.09.2026 die
    /// Sicherheitsupdate-Schwellen vertauscht (Warn 90 / Bad 45): (d) 100 Tage blieb bad, 50 Tage
    /// wurde faelschlich bad und die Probe "Zustand warn bei 50 Tagen" rot. Am 13.09.2026 den
    /// Store-Filter im Protokoll-Pfad von Schleifen() entfernt: (h) "genau ein Schleifen-Befund"
    /// und "kein Schleifen-Befund fuer das Store-Paket" rot; und den Bad-Vergleich zurueck auf den
    /// Rohwert gedreht: (k) "90,4 Tage -> warn, nicht bad" rot. Beides zurueckgedreht, wieder gruen.
    /// </summary>
    public static class UpdatesProben
    {
        public static void Laufen(Harness h)
        {
            var alle = new List<Befund>();

            // ------------------------------------------------------------ (a) HP-Schleife
            h.Gruppe("Updates (a): dieselbe Kennung viermal erfolgreich in 20 Tagen -> Schleife");
            var a = h.Bild("gepflanzt-updates-schleife.json");
            if (a != null)
            {
                var erg = h.Pruefen(a);
                h.Ist("Grundmenge: Verlauf hat mindestens 4 Einträge", a.WindowsUpdate.Verlauf.Count >= 4, a.WindowsUpdate.Verlauf.Count.ToString());
                h.Ist("Bereich updates hat Daten", Harness.Bereich(erg, Bereich.Updates).DatenVorhanden);
                var schleifen = Harness.AlleMit(erg, "update.schleife.").ToList();
                h.Ist("genau ein Schleifen-Befund", schleifen.Count == 1, schleifen.Count.ToString());
                var b = schleifen.FirstOrDefault();
                h.Ist("Schluessel traegt die Kennung klein und ohne Klammern", b != null && b.Schluessel == "update.schleife.4340b239-4b90-4305-9c2e-6a24dfcb6757", b == null ? null : b.Schluessel);
                h.Ist("Zustand warn", b != null && b.Zustand == Zustand.Warn);
                h.Ist("Massnahme update.verbergen", b != null && b.Massnahmen.Contains("update.verbergen"));
                h.Ist("Messwert 4 (eine Kennung in Gross- und Kleinschreibung zaehlt zusammen)", b != null && b.Messwert != null && b.Messwert.Wert == "4", b == null || b.Messwert == null ? null : b.Messwert.Wert);
                h.Ist("Satz nennt den Titel und 'immer wieder'", b != null && b.Satz.Contains("Hewlett-Packard") && b.Satz.Contains("immer wieder"), b == null ? null : b.Satz);
                h.Ist("kein Fehlschlag-Befund (nur Erfolge im Bild)", !Harness.AlleMit(erg, "update.fehlschlag.").Any());
                h.Ist("kein Neustart-, Sicherheitsalter- oder Suche-Befund im gesunden Rest", Harness.Einer(erg, "update.neustart") == null && Harness.Einer(erg, "update.sicherheitsalter") == null && Harness.Einer(erg, "update.suche.alt") == null);
                h.Ist("kein Dienst-Befund bei wuauserv Manual/Stopped", !Harness.AlleMit(erg, "update.dienst.").Any());
                h.Ist("Bereichszustand warn", Harness.Bereich(erg, Bereich.Updates).Zustand == Zustand.Warn);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));

                // Gegenprobe: dieselbe Kennung ist bereits verborgen -> keine Schleife mehr,
                // nur ein Hinweis beim Info-Befund "verborgen" (Fall dieses Rechners seit 04.09.2026).
                h.Gruppe("Updates (a'): Schleifen-Kennung ist verborgen -> kein Warn-Befund, Hinweis im Info-Befund");
                a.WindowsUpdate.Verborgen.Add(new UpdateKennung { UpdateId = "{4340B239-4B90-4305-9C2E-6A24DFCB6757}", Revision = 1, Titel = "Hewlett-Packard - USB - 11/11/2018 12:00:00 AM - 49.0.4395.18315", Typ = 2 });
                erg = h.Pruefen(a);
                h.Ist("kein Schleifen-Befund", !Harness.AlleMit(erg, "update.schleife.").Any());
                var vb = Harness.Einer(erg, "update.verborgen");
                h.Ist("Info-Befund verborgen mit Zustand ok", vb != null && vb.Zustand == Zustand.Ok);
                h.Ist("Detail nennt die frueheren 4 Installationen", vb != null && vb.Detail.Any(d => d.Contains("4-mal")), vb == null ? null : string.Join(" | ", vb.Detail));
                h.Ist("Bereichszustand ok", Harness.Bereich(erg, Bereich.Updates).Zustand == Zustand.Ok);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                a.WindowsUpdate.Verborgen.Clear();

                // Gegenprobe letzte Suche: 45 Tage alt -> warn; nie bei vorhandenem Verlauf -> warn; frisch -> nichts.
                h.Gruppe("Updates: letzte erfolgreiche Suche");
                a.WindowsUpdate.LetzteSucheUtc = "2026-07-28T10:00:00Z";
                erg = h.Pruefen(a);
                var su = Harness.Einer(erg, "update.suche.alt");
                h.Ist("Suche vor 45 Tagen -> warn", su != null && su.Zustand == Zustand.Warn);
                h.Ist("Satz nennt 45 Tage", su != null && su.Satz.Contains("45 Tagen"), su == null ? null : su.Satz);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                a.WindowsUpdate.LetzteSucheUtc = null;
                erg = h.Pruefen(a);
                su = Harness.Einer(erg, "update.suche.alt");
                h.Ist("nie gesucht, aber Verlauf vorhanden -> warn", su != null && su.Zustand == Zustand.Warn);
                h.Ist("Satz sagt 'noch nie' und nennt die Verlaufsgroesse", su != null && su.Satz.Contains("noch nie") && su.Satz.Contains("5 Einträge"), su == null ? null : su.Satz);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                a.Fehlerliste.Add(new Fehler { Quelle = "com.windowsupdate.autoupdate", Art = Fehler.Zeit, Text = "Probe" });
                erg = h.Pruefen(a);
                h.Ist("AutoUpdate nicht lesbar (zeit) -> kein Suche-Befund, sondern Fehlend", Harness.Einer(erg, "update.suche.alt") == null && Harness.Bereich(erg, Bereich.Updates).Fehlend.Any(f => f.Contains("Suche")));
                a.Fehlerliste.Clear();
                a.WindowsUpdate.LetzteSucheUtc = "2026-09-11T10:00:00Z";
                erg = h.Pruefen(a);
                h.Ist("Suche vor einem Tag -> kein Befund", Harness.Einer(erg, "update.suche.alt") == null);

                // Gegenprobe Hilfsdienste: BITS deaktiviert -> warn; 'Unknown' -> nicht bewertbar.
                h.Gruppe("Updates: Hilfsdienste");
                a.WindowsUpdate.Dienste["bits"] = "Disabled/Stopped";
                erg = h.Pruefen(a);
                var bits = Harness.Einer(erg, "update.dienst.bits");
                h.Ist("BITS Disabled -> warn", bits != null && bits.Zustand == Zustand.Warn);
                h.Ist("kein bad fuer wuauserv (Manual/Stopped)", Harness.Einer(erg, "update.dienst.wuauserv") == null);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                a.WindowsUpdate.Dienste["bits"] = "Unknown/Stopped";
                erg = h.Pruefen(a);
                h.Ist("BITS 'Unknown' -> kein Befund (nicht abfragbar ist nicht abgeschaltet)", Harness.Einer(erg, "update.dienst.bits") == null);
                a.WindowsUpdate.Dienste["bits"] = "Auto/Running";

                // Verwaltet und ausstehend: Info-Befunde mit Zustand ok.
                h.Gruppe("Updates: verwaltet, ausstehend (Info)");
                a.WindowsUpdate.Verwaltet = true;
                a.WindowsUpdate.Ausstehend.Add(new UpdateKennung { UpdateId = "dddddddd-4444-4555-8666-777777777777", Revision = 1, Titel = "2026-09 Kumulatives Update (KB5125000)", Typ = 1 });
                a.WindowsUpdate.Ausstehend.Add(new UpdateKennung { UpdateId = "eeeeeeee-5555-4666-8777-888888888888", Revision = 1, Titel = "Intel - Net - 22.0.0.1", Typ = 2 });
                erg = h.Pruefen(a);
                var vw = Harness.Einer(erg, "update.verwaltet");
                h.Ist("verwaltet=true -> Info-Befund ok", vw != null && vw.Zustand == Zustand.Ok);
                var au = Harness.Einer(erg, "update.ausstehend");
                h.Ist("2 ausstehende -> Info-Befund ok mit Messwert 2", au != null && au.Zustand == Zustand.Ok && au.Messwert.Wert == "2");
                h.Ist("Detail unterscheidet Treiber und Software", au != null && au.Detail.Any(d => d.Contains("Treiber")) && au.Detail.Any(d => d.Contains("Software")));
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                a.WindowsUpdate.Verwaltet = false;
                a.WindowsUpdate.Ausstehend.Clear();
                erg = h.Pruefen(a);
                h.Ist("verwaltet=false, nichts ausstehend -> keine Info-Befunde", Harness.Einer(erg, "update.verwaltet") == null && Harness.Einer(erg, "update.ausstehend") == null);
            }

            // ------------------------------------------------------------ (b) verteilt
            h.Gruppe("Updates (b): dieselbe Kennung viermal, aber ueber 120 Tage -> keine Schleife");
            var bV = h.Bild("gepflanzt-updates-schleife-verteilt.json");
            if (bV != null)
            {
                var erg = h.Pruefen(bV);
                h.Ist("Grundmenge: Verlauf hat mindestens 4 Einträge", bV.WindowsUpdate.Verlauf.Count >= 4);
                h.Ist("Grundmenge: dieselbe Kennung kommt viermal vor", bV.WindowsUpdate.Verlauf.Count(v => Updates.Norm(v.UpdateId) == "4340b239-4b90-4305-9c2e-6a24dfcb6757") == 4);
                h.Ist("kein Schleifen-Befund", !Harness.AlleMit(erg, "update.schleife.").Any());
                h.Ist("Bereichszustand ok", Harness.Bereich(erg, Bereich.Updates).Zustand == Zustand.Ok);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
            }

            // ------------------------------------------------------------ (c) Fehlschlag im Verlauf
            h.Gruppe("Updates (c): zweimal ResultCode 4 mit HResult 0x8024402C -> Fehlschlag mit Deutung");
            var c = h.Bild("gepflanzt-updates-fehlschlag.json");
            if (c != null)
            {
                var erg = h.Pruefen(c);
                h.Ist("Grundmenge: Verlauf hat mindestens 4 Einträge", c.WindowsUpdate.Verlauf.Count >= 4);
                var fs = Harness.AlleMit(erg, "update.fehlschlag.").ToList();
                h.Ist("genau ein Fehlschlag-Befund", fs.Count == 1, fs.Count.ToString());
                var f = fs.FirstOrDefault();
                h.Ist("Zustand warn", f != null && f.Zustand == Zustand.Warn);
                h.Ist("Satz nennt Titel und Anzahl", f != null && f.Satz.Contains("Intel - System") && f.Satz.Contains("2-mal"), f == null ? null : f.Satz);
                h.Ist("Detail nennt den Code hexadezimal", f != null && f.Detail.Any(d => d.Contains("0x8024402C")), f == null ? null : string.Join(" | ", f.Detail));
                h.Ist("Detail deutet den Code als Namensaufloesung", f != null && f.Detail.Any(d => d.Contains("Namensauflösung")));
                h.Ist("Massnahme update.verbergen", f != null && f.Massnahmen.Contains("update.verbergen"));
                h.Ist("kein Schleifen-Befund", !Harness.AlleMit(erg, "update.schleife.").Any());
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));

                // Gegenprobe: nur ein Fehlschlag -> kein Befund.
                var einer = c.WindowsUpdate.Verlauf.First(v => v.Ergebnis == 4);
                c.WindowsUpdate.Verlauf.Remove(einer);
                erg = h.Pruefen(c);
                h.Ist("Gegenprobe: ein einzelner Fehlschlag -> kein Befund", !Harness.AlleMit(erg, "update.fehlschlag.").Any());
                c.WindowsUpdate.Verlauf.Add(einer);

                // Gegenprobe: beide Fehlschlaege liegen vor dem 90-Tage-Fenster -> kein Befund.
                foreach (var v in c.WindowsUpdate.Verlauf.Where(v => v.Ergebnis == 4)) v.ZeitUtc = "2026-05-" + (v.ZeitUtc.Substring(8, 2)) + "T06:00:00Z";
                erg = h.Pruefen(c);
                h.Ist("Gegenprobe: Fehlschlaege aelter als 90 Tage -> kein Befund", !Harness.AlleMit(erg, "update.fehlschlag.").Any());

                // Unbekannter Code bleibt ohne Deutung.
                var d = h.Bild("gepflanzt-updates-fehlschlag.json");
                foreach (var v in d.WindowsUpdate.Verlauf.Where(v => v.Ergebnis == 4)) v.HResult = 0x80073D02;
                erg = h.Pruefen(d);
                f = Harness.Einer(erg, "update.fehlschlag.");
                h.Ist("unbekannter Code 0x80073D02 -> Befund, aber 'keine Deutung hinterlegt'", f != null && f.Detail.Any(x => x.Contains("0x80073D02") && x.Contains("keine Deutung")));
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
            }

            // ------------------------------------------------------------ (c') Fehlschlaege aus dem System-Protokoll
            h.Gruppe("Updates (c'): Fehlschlaege nur im System-Protokoll; Verlauf und Protokoll zaehlen als Maximum, nicht als Summe");
            var cl = h.Bild("gepflanzt-updates-fehlschlag-log.json");
            if (cl != null)
            {
                var erg = h.Pruefen(cl);
                h.Ist("Grundmenge: Verlauf hat mindestens 4 Einträge", cl.WindowsUpdate.Verlauf.Count >= 4);
                h.Ist("Grundmenge: Protokoll hat 3 Fehlschlaege", cl.WindowsUpdate.FehlschlaegeLog.Count == 3);
                var fs = Harness.AlleMit(erg, "update.fehlschlag.").ToList();
                h.Ist("genau ein Fehlschlag-Befund (NVIDIA, zweimal im Protokoll)", fs.Count == 1 && fs[0].Schluessel.EndsWith("cccccccc-3333-4444-8555-666666666666"), string.Join(",", fs.Select(x => x.Schluessel)));
                h.Ist("Quelle nennt das Protokoll (WindowsUpdateClient 20)", fs.Count == 1 && fs[0].Quelle.Contains("WindowsUpdateClient 20"));
                h.Ist("Detail deutet 0x80246007 (nicht heruntergeladen)", fs.Count == 1 && fs[0].Detail.Any(d => d.Contains("0x80246007") && d.Contains("nicht heruntergeladen")));
                h.Ist("Realtek: einmal im Verlauf + einmal im Protokoll (derselbe Vorgang) -> KEIN Befund", !fs.Any(x => x.Schluessel.EndsWith("bbbbbbbb-2222-4333-8444-555555555555")));
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));

                // Schleife aus dem Protokoll: drei Erfolge (ID 19) derselben Kennung in 30 Tagen.
                for (int i = 0; i < 3; i++)
                    cl.WindowsUpdate.ErfolgeLog.Add(new UpdateFehler { ZeitUtc = "2026-09-0" + (i + 2) + "T09:00:00Z", UpdateId = "ffffffff-6666-4777-8888-999999999999", Titel = "Fremdhersteller - USB - 1.0.0.1", FehlerCode = 0 });
                erg = h.Pruefen(cl);
                var sl = Harness.Einer(erg, "update.schleife.");
                h.Ist("drei Erfolge im Protokoll -> Schleife warn", sl != null && sl.Zustand == Zustand.Warn && sl.Schluessel.EndsWith("ffffffff-6666-4777-8888-999999999999"));
                h.Ist("Quelle nennt WindowsUpdateClient 19", sl != null && sl.Quelle.Contains("WindowsUpdateClient 19"));
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                cl.WindowsUpdate.ErfolgeLog.RemoveAt(2);
                erg = h.Pruefen(cl);
                h.Ist("Gegenprobe: zwei Erfolge -> keine Schleife", Harness.Einer(erg, "update.schleife.") == null);
            }

            // ------------------------------------------------------------ (d) Sicherheitsupdate-Alter
            h.Gruppe("Updates (d): Alter des letzten Sicherheitsupdates");
            var d100 = h.Bild("gepflanzt-updates-sicherheit-100.json");
            var d50 = h.Bild("gepflanzt-updates-sicherheit-50.json");
            var d10 = h.Bild("gepflanzt-updates-sicherheit-10.json");
            if (d100 != null && d50 != null && d10 != null)
            {
                h.Ist("Grundmenge: alle drei Bilder haben mindestens 4 Verlaufseintraege", d100.WindowsUpdate.Verlauf.Count >= 4 && d50.WindowsUpdate.Verlauf.Count >= 4 && d10.WindowsUpdate.Verlauf.Count >= 4);
                var e100 = h.Pruefen(d100); var e50 = h.Pruefen(d50); var e10 = h.Pruefen(d10);
                var s100 = Harness.Einer(e100, "update.sicherheitsalter");
                var s50 = Harness.Einer(e50, "update.sicherheitsalter");
                h.Ist("100 Tage -> bad", s100 != null && s100.Zustand == Zustand.Bad);
                h.Ist("Satz nennt 100 Tage", s100 != null && s100.Satz.Contains("100 Tagen"), s100 == null ? null : s100.Satz);
                h.Ist("Messwert 100 Tage mit Schwelle", s100 != null && s100.Messwert.Wert == "100" && s100.Messwert.Einheit == "Tage" && s100.Messwert.Schwelle.Contains("45"));
                h.Ist("Detail sagt 'Indiz, kein Beweis'", s100 != null && s100.Detail.Any(x => x.Contains("Indiz, kein Beweis")));
                h.Ist("Zustand warn bei 50 Tagen", s50 != null && s50.Zustand == Zustand.Warn);
                h.Ist("10 Tage -> kein Befund", Harness.Einer(e10, "update.sicherheitsalter") == null);
                h.Ist("Bereichszustand bad / warn / ok", Harness.Bereich(e100, Bereich.Updates).Zustand == Zustand.Bad && Harness.Bereich(e50, Bereich.Updates).Zustand == Zustand.Warn && Harness.Bereich(e10, Bereich.Updates).Zustand == Zustand.Ok);
                alle.AddRange(Harness.Befunde(e100, Bereich.Updates)); alle.AddRange(Harness.Befunde(e50, Bereich.Updates)); alle.AddRange(Harness.Befunde(e10, Bereich.Updates));

                // Gegenprobe: kein Datum -> kein bad, kein warn, sondern Fehlend (QFE ist Indiz).
                d100.WindowsUpdate.LetztesSicherheitsupdateUtc = null;
                var eNull = h.Pruefen(d100);
                var bereichNull = Harness.Bereich(eNull, Bereich.Updates);
                h.Ist("kein Datum -> kein Befund, aber Fehlend nennt das Sicherheitsupdate", Harness.Einer(eNull, "update.sicherheitsalter") == null && bereichNull.Fehlend.Any(x => x.Contains("Sicherheitsupdate")));
                h.Ist("kein Datum -> Bereich bleibt ok (Daten sonst vorhanden), nicht bad", bereichNull.Zustand == Zustand.Ok && bereichNull.DatenVorhanden);
            }

            // ------------------------------------------------------------ (e) Neustart ausstehend
            h.Gruppe("Updates (e): Neustart ausstehend");
            var eWu = h.Bild("gepflanzt-updates-neustart.json");
            var ePr = h.Bild("gepflanzt-updates-pendingrenames.json");
            if (eWu != null && ePr != null)
            {
                h.Ist("Grundmenge: beide Bilder haben mindestens 4 Verlaufseintraege", eWu.WindowsUpdate.Verlauf.Count >= 4 && ePr.WindowsUpdate.Verlauf.Count >= 4);
                var erg = h.Pruefen(eWu);
                var n = Harness.Einer(erg, "update.neustart");
                h.Ist("RebootRequired -> warn", n != null && n.Zustand == Zustand.Warn);
                h.Ist("Rat nennt den Neustart", n != null && n.Rat != null && n.Rat.Contains("neu"));
                h.Ist("Detail fuehrt PendingFileRenameOperations als 'vorhanden' und 'allein kein'", n != null && n.Detail.Any(x => x.Contains("PendingFileRenameOperations") && x.Contains("vorhanden") && x.Contains("allein kein")));
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                var erg2 = h.Pruefen(ePr);
                h.Ist("Gegenprobe: nur PendingFileRenameOperations -> kein Befund", Harness.Einer(erg2, "update.neustart") == null);
                h.Ist("Bereichszustand ok trotz PendingRenames", Harness.Bereich(erg2, Bereich.Updates).Zustand == Zustand.Ok);
                ePr.WindowsUpdate.NeustartCbs = true;
                var erg3 = h.Pruefen(ePr);
                var n3 = Harness.Einer(erg3, "update.neustart");
                h.Ist("CBS RebootPending allein -> warn, Quelle nennt CBS", n3 != null && n3.Zustand == Zustand.Warn && n3.Quelle.Contains("RebootPending"));
                alle.AddRange(Harness.Befunde(erg3, Bereich.Updates));
            }

            // ------------------------------------------------------------ (f) wuauserv deaktiviert
            h.Gruppe("Updates (f): Dienst wuauserv Disabled/Stopped -> bad");
            var fD = h.Bild("gepflanzt-updates-dienst-aus.json");
            if (fD != null)
            {
                h.Ist("Grundmenge: Verlauf hat mindestens 4 Einträge", fD.WindowsUpdate.Verlauf.Count >= 4);
                var erg = h.Pruefen(fD);
                var w = Harness.Einer(erg, "update.dienst.wuauserv");
                h.Ist("Befund bad", w != null && w.Zustand == Zustand.Bad);
                h.Ist("Messwert traegt den Rohwert 'Disabled/Stopped'", w != null && w.Messwert.Wert == "Disabled/Stopped");
                h.Ist("Detail listet alle 7 Dienste", w != null && w.Detail.Count(x => x.Contains(": ")) >= 7, w == null ? null : w.Detail.Count.ToString());
                h.Ist("Massnahme update.dienst.aktivieren", w != null && w.Massnahmen.Contains("update.dienst.aktivieren"));
                h.Ist("Bereichszustand bad", Harness.Bereich(erg, Bereich.Updates).Zustand == Zustand.Bad);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                fD.WindowsUpdate.Dienste["wuauserv"] = "Manual/Stopped";
                erg = h.Pruefen(fD);
                h.Ist("Gegenprobe: Manual/Stopped -> kein Befund", Harness.Einer(erg, "update.dienst.wuauserv") == null);
                fD.WindowsUpdate.Dienste.Remove("wuauserv");
                erg = h.Pruefen(fD);
                h.Ist("Gegenprobe: Dienst fehlt im Bild -> kein Befund (unbekannt ist nicht abgeschaltet)", Harness.Einer(erg, "update.dienst.wuauserv") == null);
            }

            // ------------------------------------------------------------ (g) englisches Bild von (a)
            h.Gruppe("Updates (g): englisches Systembild (LCID 1033) mit denselben Zahlen -> dieselben Befunde");
            var g = h.Bild("gepflanzt-updates-schleife-en.json");
            var gDe = h.Bild("gepflanzt-updates-schleife.json");
            if (g != null && gDe != null)
            {
                h.Ist("Grundmenge: englisches Bild traegt LCID 1033 und mindestens 4 Verlaufseintraege", g.Sprache.Lcid == 1033 && g.WindowsUpdate.Verlauf.Count >= 4);
                var eEn = h.Pruefen(g); var eDe = h.Pruefen(gDe);
                var kEn = Harness.Befunde(eEn, Bereich.Updates).Select(x => x.Schluessel + ":" + x.Zustand + ":" + (x.Messwert == null ? "" : x.Messwert.Wert)).OrderBy(x => x).ToList();
                var kDe = Harness.Befunde(eDe, Bereich.Updates).Select(x => x.Schluessel + ":" + x.Zustand + ":" + (x.Messwert == null ? "" : x.Messwert.Wert)).OrderBy(x => x).ToList();
                h.Ist("gleiche Schluessel, Zustaende und Messwerte wie im deutschen Bild", kEn.SequenceEqual(kDe), string.Join(",", kEn) + " vs " + string.Join(",", kDe));
                h.Ist("Schleifen-Befund vorhanden und warn", Harness.Einer(eEn, "update.schleife.") != null && Harness.Einer(eEn, "update.schleife.").Zustand == Zustand.Warn);
                h.Ist("Bereichszustand gleich", Harness.Bereich(eEn, Bereich.Updates).Zustand == Harness.Bereich(eDe, Bereich.Updates).Zustand);
                alle.AddRange(Harness.Befunde(eEn, Bereich.Updates));
            }

            // ------------------------------------------------------------ gesund, Zugriff verweigert
            h.Gruppe("Updates: gesundes Bild -> keine Probleme; Zugriff verweigert -> unknown, nie ok");
            var gesund = h.Bild("gepflanzt-updates-gesund.json");
            if (gesund != null)
            {
                h.Ist("Grundmenge: Verlauf hat mindestens 4 Einträge", gesund.WindowsUpdate.Verlauf.Count >= 4);
                var erg = h.Pruefen(gesund);
                var bereich = Harness.Bereich(erg, Bereich.Updates);
                h.Ist("Daten vorhanden", bereich.DatenVorhanden);
                h.Ist("kein Problem-Befund", !bereich.Befunde.Any(x => x.IstProblem), string.Join(",", bereich.Befunde.Select(x => x.Schluessel)));
                h.Ist("Bereichszustand ok", bereich.Zustand == Zustand.Ok);
                h.Ist("nichts Fehlendes", bereich.Fehlend.Count == 0, string.Join(" | ", bereich.Fehlend));
                alle.AddRange(bereich.Befunde);
            }
            var zugriff = h.Bild("gepflanzt-updates-zugriff.json");
            if (zugriff != null)
            {
                h.Ist("Grundmenge: Fehlerliste traegt einen Zugriffsfehler unter com.windowsupdate", zugriff.ZugriffVerweigert("com.windowsupdate"));
                var erg = h.Pruefen(zugriff);
                var bereich = Harness.Bereich(erg, Bereich.Updates);
                h.Ist("DatenVorhanden false", !bereich.DatenVorhanden);
                h.Ist("keine Befunde (leere Listen sind kein 'ok')", bereich.Befunde.Count == 0);
                h.Ist("Bereichszustand unknown", bereich.Zustand == Zustand.Unknown);
                h.Ist("Fehlend nennt Verlauf, Dienste, Protokoll und Sicherheitsupdate", bereich.Fehlend.Any(x => x.Contains("Verlauf")) && bereich.Fehlend.Any(x => x.Contains("Dienste")) && bereich.Fehlend.Any(x => x.Contains("Protokoll")) && bereich.Fehlend.Any(x => x.Contains("Sicherheitsupdate")), string.Join(" | ", bereich.Fehlend));
            }

            // ------------------------------------------------------------ (h) Store ist keine Schleife
            h.Gruppe("Updates (h): Store-Paket sechsmal in 9 Tagen (Verlauf und Protokoll) -> keine Schleife; HP-Treiber daneben bleibt eine");
            var st = h.Bild("gepflanzt-updates-schleife-store.json");
            if (st != null)
            {
                const string store = "aaaa1111-2222-4333-8444-555555555555";
                const string deinst = "dddd0000-1111-4222-8333-444444444444";
                var storeVerlauf = st.WindowsUpdate.Verlauf.Where(v => Updates.Norm(v.UpdateId) == store).ToList();
                var storeLog = st.WindowsUpdate.ErfolgeLog.Where(v => Updates.Norm(v.UpdateId) == store).ToList();
                h.Ist("Grundmenge: Store-Kennung 6-mal im Verlauf mit ResultCode 2 und Store-Dienst", storeVerlauf.Count == 6 && storeVerlauf.All(v => v.Ergebnis == 2 && Updates.Norm(v.DienstId) == "855e8a7c-ecb4-4ca3-b045-1dfa50104289"), storeVerlauf.Count.ToString());
                h.Ist("Grundmenge: Store-Kennung 6-mal im Protokoll mit Store-Dienst", storeLog.Count == 6 && storeLog.All(v => Updates.Norm(v.DienstId) == "855e8a7c-ecb4-4ca3-b045-1dfa50104289"), storeLog.Count.ToString());
                h.Ist("Grundmenge: dieselbe Kennung 3-mal als Deinstallation (Vorgang 2, ResultCode 2)", st.WindowsUpdate.Verlauf.Count(v => Updates.Norm(v.UpdateId) == deinst && v.Vorgang == 2 && v.Ergebnis == 2) == 3);
                var erg = h.Pruefen(st);
                var schleifen = Harness.AlleMit(erg, "update.schleife.").ToList();
                h.Ist("genau ein Schleifen-Befund, und zwar der HP-Treiber (Dienst 8b24b027-...)", schleifen.Count == 1 && schleifen[0].Schluessel == "update.schleife.4340b239-4b90-4305-9c2e-6a24dfcb6757", string.Join(",", schleifen.Select(x => x.Schluessel)));
                h.Ist("HP-Messwert 4 (Verlauf 4, Protokoll 4: Maximum, nicht Summe)", schleifen.Count == 1 && schleifen[0].Messwert.Wert == "4", schleifen.Count == 1 ? schleifen[0].Messwert.Wert : null);
                h.Ist("kein Schleifen-Befund fuer das Store-Paket", !schleifen.Any(x => x.Schluessel.EndsWith(store)));
                h.Ist("kein Schleifen-Befund fuer die Deinstallationen", !schleifen.Any(x => x.Schluessel.EndsWith(deinst)));
                h.Ist("kein Fehlschlag-Befund", !Harness.AlleMit(erg, "update.fehlschlag.").Any());
                h.Ist("Dienst dosvc 'fehlt' -> kein Dienst-Befund (nicht registriert ist nicht abgeschaltet)", !Harness.AlleMit(erg, "update.dienst.").Any());
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));

                // Gegenprobe: ohne Dienst-Feld (aeltere Bilder) zaehlt das Store-Paket -> Schleife mit 6.
                foreach (var v in storeVerlauf) v.DienstId = null;
                foreach (var v in storeLog) v.DienstId = null;
                erg = h.Pruefen(st);
                var ohne = Harness.AlleMit(erg, "update.schleife.").FirstOrDefault(x => x.Schluessel.EndsWith(store));
                h.Ist("Gegenprobe: Dienst-Feld fehlt -> Store-Kennung zaehlt (Schleife mit Messwert 6)", ohne != null && ohne.Messwert.Wert == "6", ohne == null ? null : ohne.Messwert.Wert);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
                for (int i = 0; i < storeVerlauf.Count; i++) storeVerlauf[i].DienstId = "855e8a7c-ecb4-4ca3-b045-1dfa50104289";
                for (int i = 0; i < storeLog.Count; i++) storeLog[i].DienstId = "855e8a7c-ecb4-4ca3-b045-1dfa50104289";

                // Gegenprobe: der Verlauf ist gefiltert, aber 3 Protokolleintraege tragen kein Dienst-Feld ->
                // sie zaehlen, und das Maximum beider Quellen holt den Fall mit 3 zurueck. Deshalb liest der
                // Sammler serviceGuid ueber den Eigenschaftswaehler UND im XML-Rueckfall.
                foreach (var v in storeLog.Take(3)) v.DienstId = null;
                erg = h.Pruefen(st);
                ohne = Harness.AlleMit(erg, "update.schleife.").FirstOrDefault(x => x.Schluessel.EndsWith(store));
                h.Ist("Gegenprobe: 3 Protokolleintraege ohne Dienst-Feld -> Schleife mit 3 (Verlauf gefiltert, Protokoll zaehlt)", ohne != null && ohne.Messwert.Wert == "3" && ohne.Quelle.Contains("WindowsUpdateClient 19"), ohne == null ? null : ohne.Messwert.Wert + " / " + ohne.Quelle);
                foreach (var v in storeLog) v.DienstId = "855e8a7c-ecb4-4ca3-b045-1dfa50104289";

                // Gegenprobe: Deinstallationen als Installationen (Vorgang 1) -> Schleife mit 3.
                foreach (var v in st.WindowsUpdate.Verlauf.Where(v => Updates.Norm(v.UpdateId) == deinst)) v.Vorgang = 1;
                erg = h.Pruefen(st);
                var inst = Harness.AlleMit(erg, "update.schleife.").FirstOrDefault(x => x.Schluessel.EndsWith(deinst));
                h.Ist("Gegenprobe: Vorgang 1 statt 2 -> Schleife mit Messwert 3", inst != null && inst.Messwert.Wert == "3", inst == null ? null : inst.Messwert.Wert);
                foreach (var v in st.WindowsUpdate.Verlauf.Where(v => Updates.Norm(v.UpdateId) == deinst)) v.Vorgang = 0;
                erg = h.Pruefen(st);
                inst = Harness.AlleMit(erg, "update.schleife.").FirstOrDefault(x => x.Schluessel.EndsWith(deinst));
                h.Ist("Vorgang 0 (aelteres Bild ohne Feld) zaehlt wie Installation -> Schleife mit 3", inst != null && inst.Messwert.Wert == "3");
                foreach (var v in st.WindowsUpdate.Verlauf.Where(v => Updates.Norm(v.UpdateId) == deinst)) v.Vorgang = 2;

                // Deinstallation mit ResultCode 4 ist kein Fehlschlag der Installation.
                foreach (var v in st.WindowsUpdate.Verlauf.Where(v => Updates.Norm(v.UpdateId) == deinst)) v.Ergebnis = 4;
                erg = h.Pruefen(st);
                h.Ist("3 fehlgeschlagene Deinstallationen (Vorgang 2, ResultCode 4) -> kein Fehlschlag-Befund", !Harness.AlleMit(erg, "update.fehlschlag.").Any(x => x.Schluessel.EndsWith(deinst)));
                foreach (var v in st.WindowsUpdate.Verlauf.Where(v => Updates.Norm(v.UpdateId) == deinst)) { v.Ergebnis = 2; v.Vorgang = 2; }

                // Ein aelteres Bild traegt neben der Dienstliste den Fehlereintrag "nicht registriert: DoSvc":
                // Daten schlagen den Fehlereintrag, die Dienste gelten nicht als fehlend.
                st.Fehlerliste.Add(new Fehler { Quelle = "wmi.service.windowsupdate", Art = Fehler.Fehlt, Text = "nicht registriert: DoSvc" });
                erg = h.Pruefen(st);
                h.Ist("Dienstliste gefuellt + Fehlt-Eintrag -> Dienste nicht in Fehlend", !Harness.Bereich(erg, Bereich.Updates).Fehlend.Any(x => x.Contains("Dienste")), string.Join(" | ", Harness.Bereich(erg, Bereich.Updates).Fehlend));
                st.WindowsUpdate.Dienste.Clear();
                erg = h.Pruefen(st);
                h.Ist("Gegenprobe: Dienstliste leer + Fehlt-Eintrag -> Dienste in Fehlend", Harness.Bereich(erg, Bereich.Updates).Fehlend.Any(x => x.Contains("Dienste")));
                st.Fehlerliste.Clear();
            }

            // ------------------------------------------------------------ (i) junges Protokoll: der Satz nennt den Zeitraum der zaehlenden Quelle
            h.Gruppe("Updates (i): Protokoll 10 Tage alt, zwei 60 Tage alte Fehlschlaege im Verlauf -> 'in den letzten 90 Tagen', nicht 'seit Beginn'");
            var jl = h.Bild("gepflanzt-updates-fehlschlag-junglog.json");
            if (jl != null)
            {
                var ctxTage = new Kontext(jl, new Entscheidungen());
                h.Ist("Grundmenge: Protokollbeginn 10 Tage vor der Aufzeichnung", ctxTage.LogReichweiteTage("System") == 10, ctxTage.LogReichweiteTage("System").ToString());
                h.Ist("Grundmenge: zwei Fehlschlaege derselben Kennung, 60 Tage alt, nur im Verlauf", jl.WindowsUpdate.Verlauf.Count(v => v.Ergebnis == 4 && Math.Round(ctxTage.TageSeit(v.ZeitUtc).Value) == 60) == 2 && jl.WindowsUpdate.FehlschlaegeLog.Count == 0);
                var erg = h.Pruefen(jl);
                var fs = Harness.AlleMit(erg, "update.fehlschlag.").ToList();
                h.Ist("genau ein Fehlschlag-Befund aus dem Verlauf", fs.Count == 1 && fs[0].Quelle.Contains("ResultCode 4"), string.Join(",", fs.Select(x => x.Quelle)));
                h.Ist("Satz nennt das Fenster der Regel (in den letzten 90 Tagen)", fs.Count == 1 && fs[0].Satz.Contains("in den letzten 90 Tagen"), fs.Count == 1 ? fs[0].Satz : null);
                h.Ist("Satz behauptet NICHT 'seit Beginn des Protokolls'", fs.Count == 1 && !fs[0].Satz.Contains("seit Beginn"));
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));
            }
            // Spiegelprobe: Fehlschlaege nur im Protokoll bei jungem Log -> "seit Beginn des Protokolls vor 10 Tagen".
            var jlLog = h.Bild("gepflanzt-updates-fehlschlag-log.json");
            if (jlLog != null)
            {
                h.Ist("Grundmenge: Bild setzt keinen Protokollbeginn", jlLog.Ereignisse.BeginnSystemUtc == null);
                var erg = h.Pruefen(jlLog);
                var f0 = Harness.Einer(erg, "update.fehlschlag.");
                h.Ist("ohne Protokollbeginn: 'in den letzten 90 Tagen'", f0 != null && f0.Satz.Contains("in den letzten 90 Tagen"), f0 == null ? null : f0.Satz);
                jlLog.Ereignisse.BeginnSystemUtc = "2026-09-01T16:00:00Z";
                erg = h.Pruefen(jlLog);
                var f10 = Harness.Einer(erg, "update.fehlschlag.");
                h.Ist("Protokollbeginn vor 10 Tagen, Zaehlung aus dem Protokoll: 'seit Beginn des Protokolls vor 10 Tagen'", f10 != null && f10.Quelle.Contains("WindowsUpdateClient 20") && f10.Satz.Contains("seit Beginn des Protokolls vor 10 Tagen"), f10 == null ? null : f10.Satz);
                alle.AddRange(Harness.Befunde(erg, Bereich.Updates));

                // Ereignisquelle schreibt "zeit" unter derselben Kennung, obwohl die Updatequelle das Log gelesen hat
                // (3 Fehlschlaege im Bild): Daten schlagen den Fehlereintrag.
                jlLog.Fehlerliste.Add(new Fehler { Quelle = "log.system.windowsupdateclient", Art = Fehler.Zeit, Text = "Zeitbudget von 15 s ausgeschöpft, Abonnement übersprungen" });
                erg = h.Pruefen(jlLog);
                h.Ist("Protokoll-Listen gefuellt + Zeit-Eintrag -> Protokoll nicht in Fehlend", !Harness.Bereich(erg, Bereich.Updates).Fehlend.Any(x => x.Contains("Protokoll")), string.Join(" | ", Harness.Bereich(erg, Bereich.Updates).Fehlend));
                jlLog.WindowsUpdate.FehlschlaegeLog.Clear();
                erg = h.Pruefen(jlLog);
                h.Ist("Gegenprobe: Listen leer + Zeit-Eintrag -> Protokoll in Fehlend", Harness.Bereich(erg, Bereich.Updates).Fehlend.Any(x => x.Contains("Protokoll")));
                jlLog.Fehlerliste.Clear();
            }

            // ------------------------------------------------------------ (j) Fehlend-Texte nennen den wahren Grund
            h.Gruppe("Updates (j): Fehlend-Text zum Sicherheitsupdate folgt der Fehlerart von wmi.qfe, nie einer erfundenen Ursache");
            var zq = h.Bild("gepflanzt-updates-zugriff.json");
            var gq = h.Bild("gepflanzt-updates-gesund.json");
            if (zq != null && gq != null)
            {
                h.Ist("Grundmenge: Zugriff-Bild traegt wmi.qfe/zeit und kein Sicherheitsupdate-Datum", zq.FehlerVon("wmi.qfe").Any(f => f.Art == Fehler.Zeit) && zq.WindowsUpdate.LetztesSicherheitsupdateUtc == null);
                var fz = Harness.Bereich(h.Pruefen(zq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Sicherheitsupdate"));
                h.Ist("zeit -> 'antwortete nicht rechtzeitig', nicht 'kein Eintrag'", fz != null && fz.Contains("antwortete nicht rechtzeitig") && !fz.Contains("kein Eintrag") && !fz.Contains("kein Sicherheitsupdate"), fz);

                gq.WindowsUpdate.LetztesSicherheitsupdateUtc = null;
                var fo = Harness.Bereich(h.Pruefen(gq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Sicherheitsupdate"));
                h.Ist("kein Datum, kein wmi.qfe-Eintrag -> nur 'Alter des letzten Sicherheitsupdates' ohne Grund", fo == "Alter des letzten Sicherheitsupdates", fo);
                gq.Fehlerliste.Add(new Fehler { Quelle = "wmi.qfe", Art = Fehler.Fehlt, Text = "kein Eintrag mit Description 'Security Update' unter 30 Einträgen" });
                var ff = Harness.Bereich(h.Pruefen(gq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Sicherheitsupdate"));
                h.Ist("fehlt -> 'kein Sicherheitsupdate mit lesbarem Datum' und 'Indiz, kein Beweis', ohne den Sammler-Rohtext", ff != null && ff.Contains("kein Sicherheitsupdate mit lesbarem Datum") && ff.Contains("Indiz, kein Beweis") && !ff.Contains("Description"), ff);
                gq.Fehlerliste.Clear();
                gq.Fehlerliste.Add(new Fehler { Quelle = "wmi.qfe", Art = Fehler.Ausnahme, Text = "ManagementException: Probe" });
                var fa = Harness.Bereich(h.Pruefen(gq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Sicherheitsupdate"));
                h.Ist("ausnahme -> 'nicht lesbar', ohne den Sammler-Rohtext", fa != null && fa.Contains("nicht lesbar") && !fa.Contains("Probe"), fa);
                gq.Fehlerliste.Clear();
                gq.Fehlerliste.Add(new Fehler { Quelle = "wmi.qfe", Art = Fehler.Zugriff, Text = "Zugriff verweigert" });
                var fg = Harness.Bereich(h.Pruefen(gq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Sicherheitsupdate"));
                h.Ist("zugriff -> 'Zugriff verweigert'", fg != null && fg.Contains("Zugriff verweigert"), fg);
                gq.Fehlerliste.Clear();

                // Verlauf je Eintrag gelesen: einzelne unlesbare Eintraege machen den Verlauf unvollstaendig, nicht fehlend.
                gq.Fehlerliste.Add(new Fehler { Quelle = "com.windowsupdate.verlauf", Art = Fehler.Ausnahme, Text = "2 von 26 Verlaufseinträgen unlesbar (NullReferenceException: Probe)" });
                var fv = Harness.Bereich(h.Pruefen(gq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Verlauf"));
                h.Ist("Verlauf gefuellt + Ausnahme-Eintrag -> 'unvollständig'", fv != null && fv.Contains("unvollständig"), fv);
                var verlauf = gq.WindowsUpdate.Verlauf.ToList();
                gq.WindowsUpdate.Verlauf.Clear();
                fv = Harness.Bereich(h.Pruefen(gq), Bereich.Updates).Fehlend.FirstOrDefault(x => x.Contains("Verlauf"));
                h.Ist("Gegenprobe: Verlauf leer + Ausnahme-Eintrag -> 'Update-Verlauf (ausnahme)'", fv == "Update-Verlauf (ausnahme)", fv);
                gq.WindowsUpdate.Verlauf.AddRange(verlauf);
                gq.Fehlerliste.Clear();
            }

            // ------------------------------------------------------------ (k) Rundung: Messwert, Satz und Schwelle tragen dieselbe Zahl
            h.Gruppe("Updates (k): verglichen wird der ganze Tag, den der Nutzer liest (45,3 -> 45, keine Warnung; 45,6 -> 46, Warnung)");
            var rd = h.Bild("gepflanzt-updates-sicherheit-50.json");
            if (rd != null)
            {
                var ctxR = new Kontext(rd, new Entscheidungen());
                rd.WindowsUpdate.LetztesSicherheitsupdateUtc = "2026-07-28T08:48:00Z";
                h.Ist("Grundmenge: 45,3 Tage alt", Math.Abs(ctxR.TageSeit(rd.WindowsUpdate.LetztesSicherheitsupdateUtc).Value - 45.3) < 0.01);
                h.Ist("45,3 Tage -> kein Befund (Messwert 45 ueberschreitet 45 nicht)", Harness.Einer(h.Pruefen(rd), "update.sicherheitsalter") == null);
                rd.WindowsUpdate.LetztesSicherheitsupdateUtc = "2026-07-28T01:36:00Z";
                var s46 = Harness.Einer(h.Pruefen(rd), "update.sicherheitsalter");
                h.Ist("45,6 Tage -> warn mit Messwert 46 und Satz '46 Tagen'", s46 != null && s46.Zustand == Zustand.Warn && s46.Messwert.Wert == "46" && s46.Satz.Contains("46 Tagen"), s46 == null ? null : s46.Messwert.Wert + " / " + s46.Satz);
                rd.WindowsUpdate.LetztesSicherheitsupdateUtc = "2026-06-13T06:24:00Z";
                var s90 = Harness.Einer(h.Pruefen(rd), "update.sicherheitsalter");
                h.Ist("90,4 Tage -> warn, nicht bad (Messwert 90)", s90 != null && s90.Zustand == Zustand.Warn && s90.Messwert.Wert == "90", s90 == null ? null : s90.Zustand + " / " + s90.Messwert.Wert);
                rd.WindowsUpdate.LetztesSicherheitsupdateUtc = "2026-06-13T01:36:00Z";
                var s91 = Harness.Einer(h.Pruefen(rd), "update.sicherheitsalter");
                h.Ist("90,6 Tage -> bad mit Messwert 91", s91 != null && s91.Zustand == Zustand.Bad && s91.Messwert.Wert == "91", s91 == null ? null : s91.Zustand + " / " + s91.Messwert.Wert);
                rd.WindowsUpdate.LetztesSicherheitsupdateUtc = "2026-09-11T17:00:00Z";
                var eZ = h.Pruefen(rd);
                h.Ist("Datum eine Stunde in der Zukunft -> kein Befund, Fehlend nennt die Uhr (Rohwert, nicht gerundet)", Harness.Einer(eZ, "update.sicherheitsalter") == null && Harness.Bereich(eZ, Bereich.Updates).Fehlend.Any(x => x.Contains("Uhr")));
                if (s46 != null) alle.Add(s46);
                if (s91 != null) alle.Add(s91);

                rd.WindowsUpdate.LetztesSicherheitsupdateUtc = "2026-09-10T00:00:00Z";
                rd.WindowsUpdate.LetzteSucheUtc = "2026-08-12T08:48:00Z";
                h.Ist("Grundmenge: letzte Suche 30,3 Tage alt", Math.Abs(ctxR.TageSeit(rd.WindowsUpdate.LetzteSucheUtc).Value - 30.3) < 0.01);
                h.Ist("Suche vor 30,3 Tagen -> kein Befund", Harness.Einer(h.Pruefen(rd), "update.suche.alt") == null);
                rd.WindowsUpdate.LetzteSucheUtc = "2026-08-12T01:36:00Z";
                var su31 = Harness.Einer(h.Pruefen(rd), "update.suche.alt");
                h.Ist("Suche vor 30,6 Tagen -> warn mit Messwert 31 und Satz '31 Tagen'", su31 != null && su31.Messwert.Wert == "31" && su31.Satz.Contains("31 Tagen"), su31 == null ? null : su31.Messwert.Wert + " / " + su31.Satz);
                if (su31 != null) alle.Add(su31);
            }

            // ------------------------------------------------------------ Fehlercode-Tabelle
            h.Gruppe("Updates: eingebettete Fehlercode-Tabelle");
            h.Ist("15 Codes hinterlegt", Fehlercodes.Anzahl == 15, Fehlercodes.Anzahl.ToString());
            h.Ist("0x8024402C gedeutet (Namensaufloesung)", (Fehlercodes.Deutung(0x8024402C) ?? "").Contains("Namensauflösung"));
            h.Ist("vorzeichenbehaftete Schreibweise (-2145107924) trifft denselben Eintrag", Fehlercodes.Deutung(-2145107924L) == Fehlercodes.Deutung(0x8024402C));
            h.Ist("0x80070422 nennt den deaktivierten Dienst", (Fehlercodes.Deutung(0x80070422) ?? "").Contains("deaktiviert"));
            h.Ist("unbekannter Code 0x80073D02 -> null", Fehlercodes.Deutung(0x80073D02) == null);
            h.Ist("0 -> null", Fehlercodes.Deutung(0) == null);

            // ------------------------------------------------------------ Saetze
            h.Gruppe("Updates: jeder Satz traegt Zahl, Bedingung oder Absage");
            h.Ist("Grundmenge: mindestens 12 Befunde gesammelt", alle.Count >= 12, alle.Count.ToString());
            h.SaetzeTragen(alle);
            h.Ist("kein Befund ohne Quelle oder Titel", alle.All(x => !string.IsNullOrWhiteSpace(x.Quelle) && !string.IsNullOrWhiteSpace(x.Titel)));
            h.Ist("kein gerades Anfuehrungszeichen in Titel, Satz oder Rat", alle.All(x => (x.Titel ?? "").IndexOf('"') < 0 && (x.Satz ?? "").IndexOf('"') < 0 && (x.Rat ?? "").IndexOf('"') < 0));
            h.Ist("jeder Problem-Befund traegt einen Messwert mit Schwelle", alle.Where(x => x.IstProblem).All(x => x.Messwert != null && x.Messwert.Wert != null && x.Messwert.Schwelle != null));
        }
    }
}
