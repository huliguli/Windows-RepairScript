using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer den Bereich Speicherplatz, soweit er nicht an den Laufwerken haengt (die
    /// Laufwerksregeln 5 GB / 15 % / 40 % laufen weiter in DatentraegerProben, Gruppe (h), weil sie
    /// dieselben gepflanzten Volumes benutzen). Seit M4 (docs/M4-ENTWURF.md 2.4, 8.1) gibt es hier
    /// den Papierkorb des angemeldeten Kontos: 10 Probenklassen, Harness.ErwarteteKlassen zaehlt mit.
    ///
    /// Gruppe (k) Papierkorb: SHQueryRecycleBin liefert Bytes und Dateien; ab PapierkorbWarnBytes
    /// (1 GB) ist das warn mit speicher.papierkorb.leeren und den Parametern dateien/bytes
    /// (invariant, ohne Tausenderzeichen, weil Massnahme.VerlustMit sie als Zahl liest), darunter und
    /// ueber 0 ein ok-Befund mit Zahl ohne Kennung, 0 oder nicht gelesen = kein Befund; die Fehlend-
    /// Zeile "Papierkorb" gibt es nur mit einem Fehlereintrag shell.papierkorb. Gepflanzt wird im
    /// Speicher aus dem gesunden Datentraeger-Bild, dessen Aufzeichnung das Feld noch nicht kennt
    /// (Schema 1, alte Aufzeichnungen lesen sich mit null).
    ///
    /// Wirksamkeitsproben 19.09.2026 (T1, an einer Kopie der Regel, zurueckgebaut): Schwelle auf
    /// "groesser" statt "groesser gleich" -> "genau an der Schwelle: warn" und 4 weitere rot; Kennung
    /// auch ohne gelesene Dateizahl -> "Dateizahl nicht gelesen: ... ohne Kennung" rot; Fehlend ohne
    /// Fehlereintrag -> "nicht gelesen ohne Fehlereintrag: kein Befund, kein Fehlend" rot.
    /// </summary>
    public static class SpeicherplatzProben
    {
        static readonly List<Befund> Alle = new List<Befund>();

        public static void Laufen(Harness h)
        {
            Alle.Clear();
            Papierkorb(h);
            PapierkorbUndVerlust(h);

            h.Gruppe("Speicherplatz: Saetze aller Befunde tragen Zahl, Bedingung oder Absage");
            h.Ist("Grundmenge: mindestens 10 Befunde gesammelt", Alle.Count >= 10, Alle.Count.ToString());
            h.SaetzeTragen(Alle);
            foreach (var b in Alle)
            {
                h.Ist("kein gerades Anfuehrungszeichen in Satz/Titel/Rat: " + b.Schluessel,
                    (b.Satz ?? "").IndexOf('"') < 0 && (b.Titel ?? "").IndexOf('"') < 0 && (b.Rat ?? "").IndexOf('"') < 0);
                h.Ist("Quelle und Messwert gesetzt: " + b.Schluessel, !string.IsNullOrEmpty(b.Quelle) && b.Messwert != null);
            }
        }

        static List<BereichErgebnis> Pruefen(Harness h, Systembild s)
        {
            var erg = h.Pruefen(s);
            Alle.AddRange(Harness.Befunde(erg, Bereich.Speicherplatz));
            return erg;
        }

        // ---------------------------------------------------------------- (k) Papierkorb (M4)

        /// <summary>
        /// docs/M4-ENTWURF.md, Abschnitt 2.4: Papierkorb des eigenen Kontos (SHQueryRecycleBin) ab
        /// PapierkorbWarnBytes = warn mit speicher.papierkorb.leeren und den Parametern dateien/bytes
        /// (invariant), darunter und ueber 0 = ok mit Zahl ohne Kennung, 0 oder nicht gelesen = nichts,
        /// Fehlend "Papierkorb" nur mit Fehlereintrag shell.papierkorb.
        /// </summary>
        static void Papierkorb(Harness h)
        {
            h.Gruppe("Speicherplatz: (k) Papierkorb 1 GB = warn mit speicher.papierkorb.leeren, darunter ok, 0 oder nicht gelesen = nichts");
            var s = h.Bild("gepflanzt-datentraeger-gesund.json"); if (s == null) return;
            h.Ist("Grundmenge: Papierkorb im Bild nicht gelesen (null), kein Fehlereintrag", (s.Speicher == null || !s.Speicher.PapierkorbBytes.HasValue) && !s.Fehlerliste.Any(f => f.Quelle == Speicherplatz.QuellePapierkorb));
            h.Ist("Schwelle PapierkorbWarnBytes = 1 GB", Schwellen.PapierkorbWarnBytes == 1L << 30);
            var erg0 = Pruefen(h, s);
            h.Ist("nicht gelesen ohne Fehlereintrag: kein Befund, kein Fehlend", Harness.Einer(erg0, "speicherplatz.papierkorb") == null && !Harness.Bereich(erg0, Bereich.Speicherplatz).Fehlend.Any(f => f.Contains("Papierkorb")), string.Join("; ", Harness.Bereich(erg0, Bereich.Speicherplatz).Fehlend));
            // Nicht gelesen MIT Fehlereintrag: Fehlend nennt den Papierkorb mit Grund.
            s.Fehlerliste.Add(new Fehler { Quelle = Speicherplatz.QuellePapierkorb, Art = Fehler.Ausnahme, Text = "gepflanzt" });
            var ergF = Pruefen(h, s);
            h.Ist("Fehlereintrag shell.papierkorb: Fehlend 'Papierkorb (Fehler beim Lesen)', kein Befund", Harness.Einer(ergF, "speicherplatz.papierkorb") == null && Harness.Bereich(ergF, Bereich.Speicherplatz).Fehlend.Contains("Papierkorb (Fehler beim Lesen)"), string.Join("; ", Harness.Bereich(ergF, Bereich.Speicherplatz).Fehlend));
            s.Fehlerliste.RemoveAll(f => f.Quelle == Speicherplatz.QuellePapierkorb);
            if (s.Speicher == null) s.Speicher = new Speicher();
            // Genau an der Schwelle (1 GB, 12 Dateien): warn mit Kennung und beiden Parametern, invariant.
            s.Speicher.PapierkorbBytes = 1L << 30; s.Speicher.PapierkorbDateien = 12;
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "speicherplatz.papierkorb");
            h.Ist("genau an der Schwelle: warn", b != null && b.Zustand == Zustand.Warn, b == null ? "null" : b.Zustand);
            if (b != null)
            {
                h.Ist("Kennung speicher.papierkorb.leeren, sonst keine", b.Massnahmen.SequenceEqual(new[] { Speicherplatz.MPapierkorbLeeren }) && Speicherplatz.MPapierkorbLeeren == "speicher.papierkorb.leeren", string.Join(",", b.Massnahmen));
                h.Ist("Parameter dateien = 12 und bytes = 1073741824 (invariant, ohne Tausenderzeichen)", b.Parameter.Count == 2 && b.Parameter[Speicherplatz.ParamDateien] == "12" && b.Parameter[Speicherplatz.ParamBytes] == "1073741824", string.Join(",", b.Parameter.Select(kv => kv.Key + "=" + kv.Value)));
                h.Ist("Parameternamen sind dateien und bytes", Speicherplatz.ParamDateien == "dateien" && Speicherplatz.ParamBytes == "bytes");
                h.Ist("Satz nennt 12 Dateien, 1,0 GB und die Schwelle 1 GB, Rat nennt den fehlenden Rueckweg", b.Satz.Contains("12 Dateien") && b.Satz.Contains("1,0 GB") && b.Satz.Contains("ab 1 GB") && (b.Rat ?? "").Contains("keinen Rückweg"), b.Satz);
                h.Ist("Quelle SHQueryRecycleBin, Messwert 1 GB, Schwelle >= 1 GB warn", b.Quelle == "SHQueryRecycleBin" && b.Messwert.Wert == "1" && b.Messwert.Schwelle == ">= 1 GB warn", b.Messwert == null ? "null" : b.Messwert.Wert + " / " + b.Messwert.Schwelle);
                h.Ist("Detail nennt i64Size, i64NumItems und das eigene Konto", b.Detail.Any(d => d.Contains("i64Size 1073741824") && d.Contains("i64NumItems 12") && d.Contains("angemeldeten Kontos")), string.Join(" | ", b.Detail));
            }
            h.Ist("Bereich Speicherplatz warn (die Laufwerke sind ok)", Harness.Bereich(erg, Bereich.Speicherplatz).Zustand == Zustand.Warn);
            // Ein Byte unter der Schwelle: ok mit Zahl, ohne Kennung, ohne Parameter.
            s.Speicher.PapierkorbBytes = (1L << 30) - 1; s.Speicher.PapierkorbDateien = 1;
            var ergO = Pruefen(h, s);
            var o = Harness.Einer(ergO, "speicherplatz.papierkorb");
            h.Ist("ein Byte unter der Schwelle: ok, keine Kennung, keine Parameter, kein Rat", o != null && o.Zustand == Zustand.Ok && o.Massnahmen.Count == 0 && o.Parameter.Count == 0 && o.Rat == null, o == null ? "null" : o.Zustand + " " + string.Join(",", o.Massnahmen));
            h.Ist("ok-Satz nennt 1 Datei und die Schwelle", o != null && o.Satz.Contains("1 Datei mit") && o.Satz.Contains("unter 1 GB"), o == null ? "null" : o.Satz);
            h.Ist("Bereich Speicherplatz ok", Harness.Bereich(ergO, Bereich.Speicherplatz).Zustand == Zustand.Ok);
            // 3 GB: warn, Messwert 3, Satz nennt 3,0 GB.
            s.Speicher.PapierkorbBytes = 3L << 30; s.Speicher.PapierkorbDateien = 4321;
            var g3 = Harness.Einer(Pruefen(h, s), "speicherplatz.papierkorb");
            h.Ist("3 GB: warn, Messwert 3, Satz nennt 4.321 Dateien mit 3,0 GB, Parameter 4321", g3 != null && g3.Zustand == Zustand.Warn && g3.Messwert.Wert == "3" && g3.Satz.Contains("4.321 Dateien mit 3,0 GB") && g3.Parameter[Speicherplatz.ParamDateien] == "4321", g3 == null ? "null" : g3.Satz);
            // Bytes ueber der Schwelle, aber Dateizahl nicht gelesen: warn ohne Kennung (der Verlust-Satz braucht beide Zahlen).
            s.Speicher.PapierkorbDateien = null;
            var oz = Harness.Einer(Pruefen(h, s), "speicherplatz.papierkorb");
            h.Ist("Dateizahl nicht gelesen: warn bleibt, aber ohne Kennung und Parameter, Rat nennt den Explorer", oz != null && oz.Zustand == Zustand.Warn && oz.Massnahmen.Count == 0 && oz.Parameter.Count == 0 && (oz.Rat ?? "").Contains("Explorer") && oz.Detail.Any(d => d.Contains("i64NumItems nicht gelesen")), oz == null ? "null" : oz.Rat);
            // 0 Byte: kein Befund (leerer Papierkorb ist kein Thema).
            s.Speicher.PapierkorbBytes = 0; s.Speicher.PapierkorbDateien = 0;
            var ergL = Pruefen(h, s);
            h.Ist("0 Byte: kein Befund, kein Fehlend", Harness.Einer(ergL, "speicherplatz.papierkorb") == null && !Harness.Bereich(ergL, Bereich.Speicherplatz).Fehlend.Any(f => f.Contains("Papierkorb")));
            // Voller Papierkorb ohne lesbare Laufwerke: warn ist belegt, der Bereich wird warn; ein ok allein macht ihn nicht gruen.
            s.Speicher.PapierkorbBytes = 2L << 30; s.Speicher.PapierkorbDateien = 7;
            foreach (var v in s.Volumes) v.Versteckt = true;
            var ergV = Pruefen(h, s);
            h.Ist("ohne Nutzer-Laufwerke: Papierkorb-warn bleibt, Bereich warn, DatenVorhanden false", Harness.Einer(ergV, "speicherplatz.papierkorb") != null && Harness.Bereich(ergV, Bereich.Speicherplatz).Zustand == Zustand.Warn && !Harness.Bereich(ergV, Bereich.Speicherplatz).DatenVorhanden);
            s.Speicher.PapierkorbBytes = 5L << 20;
            var ergW = Pruefen(h, s);
            h.Ist("ohne Nutzer-Laufwerke: Papierkorb-ok allein macht den Bereich nicht ok (unknown)", Harness.Einer(ergW, "speicherplatz.papierkorb") != null && Harness.Bereich(ergW, Bereich.Speicherplatz).Zustand == Zustand.Unknown);
        }

        // ---------------------------------------------------------------- (k2) Regel und Verlust-Text

        /// <summary>
        /// Die Regel und der Katalog haengen an denselben Namen: die Parameter dateien/bytes des
        /// Befunds sind die Platzhalter {dateien}/{bytes} im Verlust-Text von speicher.papierkorb.leeren
        /// (M4-Entwurf 1, 2.4). Wird ein Name auf einer Seite umbenannt, zeigt die Vorschau "?" statt
        /// der Zahl; diese Gruppe misst den Weg Befund -> VerlustMit mit dem Paar (c2) der Kernproben.
        /// </summary>
        static void PapierkorbUndVerlust(Harness h)
        {
            h.Gruppe("Speicherplatz: (k2) die Befund-Parameter fuellen den Verlust-Text der Massnahme");
            var s = h.Bild("gepflanzt-massnahmen-speicher.papierkorb.leeren-vorher.json"); if (s == null) return;
            h.Ist("Grundmenge: Vorher-Bild mit 1,5 GB in 12 Dateien", s.Speicher != null && s.Speicher.PapierkorbBytes == 1610612736 && s.Speicher.PapierkorbDateien == 12);
            var b = Harness.Einer(Pruefen(h, s), "speicherplatz.papierkorb");
            var m = Massnahmen.Finde(Speicherplatz.MPapierkorbLeeren);
            h.Ist("Regel: warn mit Kennung speicher.papierkorb.leeren", b != null && b.Zustand == Zustand.Warn && b.Massnahmen.Contains(Speicherplatz.MPapierkorbLeeren), b == null ? "null" : b.Zustand);
            h.Ist("Katalog: Stufe 3, Verlust mit den Platzhaltern {dateien} und {bytes}", m != null && m.Stufe == Massnahme.StufeUnumkehrbar && m.Verlust.Contains("{" + Speicherplatz.ParamDateien + "}") && m.Verlust.Contains("{" + Speicherplatz.ParamBytes + "}"), m == null ? "(fehlt)" : m.Verlust);
            if (b == null || m == null) return;
            string v = m.VerlustMit(b.Parameter);
            h.Ist("VerlustMit(Befund.Parameter) nennt 12 Dateien und 1.5 GB, kein Platzhalter bleibt", v.Contains("12 Dateien") && v.Contains("1.5 GB") && !v.Contains("{") && !v.Contains("?"), v);
            // Der Satz des Befunds und der Verlust-Text nennen dieselben Zahlen (der Satz in deutscher
            // Schreibweise 1,5 GB, der Verlust invariant 1.5 GB, wie VerlustMit es festlegt).
            h.Ist("Befund-Satz nennt 12 Dateien mit 1,5 GB", b.Satz.Contains("12 Dateien mit 1,5 GB"), b.Satz);
            // Nachher-Bild des Paars: 0 Byte, 0 Dateien, kein Befund mehr, also auch kein Verlust-Text.
            var n = h.Bild("gepflanzt-massnahmen-speicher.papierkorb.leeren-nachher.json"); if (n == null) return;
            h.Ist("Nachher-Bild: 0 Byte, 0 Dateien", n.Speicher != null && n.Speicher.PapierkorbBytes == 0 && n.Speicher.PapierkorbDateien == 0);
            h.Ist("Nachher-Bild: kein Papierkorb-Befund", Harness.Einer(Pruefen(h, n), "speicherplatz.papierkorb") == null);
        }
    }
}
