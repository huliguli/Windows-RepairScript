using System;
using System.Collections.Generic;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Die Regeln: reine Funktionen ueber dem Systembild. Sie kennen weder Windows noch
    /// Prozesse noch das Fenster. Genau diese Schicht laeuft in den Proben gegen
    /// aufgezeichnete Systembilder - mit gepflanzten Fehlerfaellen.
    ///
    /// "Jetzt" ist die Aufzeichnungszeit des Systembilds, nie DateTime.Now: nur so liefert
    /// dieselbe Aufzeichnung heute und in einem Jahr dieselben Befunde.
    /// </summary>
    public static class Alle
    {
        public static List<BereichErgebnis> Pruefen(Systembild s, Entscheidungen e)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (e == null) e = new Entscheidungen();
            var ctx = new Kontext(s, e);
            var liste = new List<BereichErgebnis>
            {
                Sicherheit.Pruefen(ctx),
                Datentraeger.Pruefen(ctx),
                Speicherplatz.Pruefen(ctx),
                Stabilitaet.Pruefen(ctx),
                Updates.Pruefen(ctx),
                Hardware.Pruefen(ctx),
                Leistung.Pruefen(ctx),
                Netz.Pruefen(ctx),
            };
            foreach (var b in liste)
                foreach (var f in b.Befunde)
                {
                    if (f.Bereich == null) f.Bereich = b.Bereich;
                    if (f.Zustand == null) f.Zustand = Kern.Zustand.Unknown;
                    if (f.Detail == null) f.Detail = new List<string>();
                    if (f.Massnahmen == null) f.Massnahmen = new List<string>();
                }
            return liste;
        }

        /// <summary>Gesamturteil: schlechteste Klasse; unknown und absicht zaehlen nicht (v7).</summary>
        public static string Gesamt(IEnumerable<BereichErgebnis> ergebnisse)
        {
            return Kern.Zustand.Schlechtester(ergebnisse.Select(x => x.Zustand));
        }

        public static IEnumerable<Befund> AlleBefunde(IEnumerable<BereichErgebnis> ergebnisse)
        {
            return ergebnisse.SelectMany(x => x.Befunde);
        }

        public static int Probleme(IEnumerable<BereichErgebnis> ergebnisse)
        {
            return AlleBefunde(ergebnisse).Count(b => b.IstProblem);
        }

        public static IEnumerable<Befund> OffeneFragen(IEnumerable<BereichErgebnis> ergebnisse)
        {
            return AlleBefunde(ergebnisse).Where(b => b.Frage != null);
        }
    }

    /// <summary>Gemeinsamer Zustand fuer alle Regeln: Systembild, Antworten, "jetzt".</summary>
    public class Kontext
    {
        public readonly Systembild S;
        public readonly Entscheidungen E;
        public readonly DateTime Jetzt;

        public Kontext(Systembild s, Entscheidungen e)
        {
            S = s; E = e;
            var t = Zeit.Lesen(s.AufgezeichnetUtc);
            // Kein Rueckfall auf die Uhr: dieselbe Aufzeichnung muss heute und in einem Jahr
            // dieselben Befunde liefern. Der Sammler setzt die Zeit immer; fehlt sie, ist das
            // Bild kaputt (oder ein Feldname im Testbild falsch geschrieben).
            if (!t.HasValue) throw new ArgumentException("Systembild ohne lesbare Aufzeichnungszeit (aufgezeichnet)");
            Jetzt = t.Value;
        }

        public double? TageSeit(string isoUtc)
        {
            var t = Zeit.Lesen(isoUtc);
            return t.HasValue ? (Jetzt - t.Value).TotalDays : (double?)null;
        }

        public bool Innerhalb(string isoUtc, int tage)
        {
            var d = TageSeit(isoUtc);
            return d.HasValue && d.Value >= 0 && d.Value <= tage;
        }

        /// <summary>
        /// Wie weit ein Log zurueckreicht - "0 Ereignisse in 90 Tagen" heisst bei einem Log,
        /// das nur 30 Tage alt ist, nur "nichts seit Log-Anfang". Der Satz muss das sagen.
        /// </summary>
        public int LogReichweiteTage(string log)
        {
            string beginn = string.Equals(log, "Application", StringComparison.OrdinalIgnoreCase)
                ? S.Ereignisse.BeginnApplicationUtc : S.Ereignisse.BeginnSystemUtc;
            var d = TageSeit(beginn);
            if (!d.HasValue) return S.Ereignisse.Tage;
            return (int)Math.Min(S.Ereignisse.Tage, Math.Max(0, d.Value));
        }

        public string ZeitraumText(string log)
        {
            int t = LogReichweiteTage(log);
            if (t >= S.Ereignisse.Tage) return "in den letzten " + t + " Tagen";
            return "seit Beginn des Protokolls vor " + t + " Tagen";
        }

        public bool LogGesperrt(string log)
        {
            return S.Ereignisse.Gesperrt != null &&
                   S.Ereignisse.Gesperrt.Any(g => string.Equals(g, log, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Kleine Helfer fuer lesbare Saetze mit Zahlen.</summary>
    public static class Text
    {
        public static string Gb(long bytes) { return (bytes / 1073741824.0).ToString("N0", De) + " GB"; }
        public static string Gb1(long bytes) { return (bytes / 1073741824.0).ToString("N1", De) + " GB"; }
        public static string Mb(long bytes) { return (bytes / 1048576.0).ToString("N0", De) + " MB"; }
        public static string Pct(double v) { return v.ToString("N0", De) + " %"; }
        public static string Tage(double v) { int t = (int)Math.Round(v); return t == 1 ? "einem Tag" : t + " Tagen"; }
        /// <summary>"vor 0 Tagen" ist kein Deutsch: "heute", "gestern", sonst "vor N Tagen".</summary>
        public static string Vor(double v) { int t = (int)Math.Round(v); return t <= 0 ? "heute" : t == 1 ? "gestern" : "vor " + t + " Tagen"; }
        public static string Mal(int n) { return n == 1 ? "einmal" : n + "-mal"; }
        public static readonly System.Globalization.CultureInfo De = new System.Globalization.CultureInfo("de-DE");
    }
}
