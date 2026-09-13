using System;
using System.IO;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Sammler
{
    /// <summary>
    /// Kommandozeile fuer Sammler und Regeln ohne Oberflaeche:
    ///
    ///   aufzeichnen.exe --aufzeichnen <datei.json> [--roh]   Systembild aufnehmen (redigiert, ausser --roh)
    ///   aufzeichnen.exe --pruefen <datei.json>               Regeln gegen eine Aufzeichnung laufen lassen
    ///   aufzeichnen.exe --live                                aufnehmen und sofort pruefen, nichts schreiben
    ///
    /// Das ist der Weg, auf dem die Proben ihre Aufzeichnungen bekommen, und der Weg, auf dem
    /// ein Techniker das Werkzeug auf einem fremden Rechner ohne Klick laufen lassen kann.
    /// </summary>
    public static class AufzeichnenCli
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 2 && args[0] == "--aufzeichnen")
                {
                    bool roh = Array.IndexOf(args, "--roh") >= 0;
                    var s = Sammeln();
                    Aufzeichnung.Schreiben(s, args[1], !roh);
                    Console.WriteLine("geschrieben: " + args[1] + (roh ? " (ROH, nicht redigiert)" : " (redigiert)"));
                    if (!roh)
                    {
                        var v = Aufzeichnung.Verstoesse(args[1]);
                        if (v.Count > 0) { Console.WriteLine("REDAKTION UNVOLLSTÄNDIG: " + string.Join(", ", v)); return 2; }
                    }
                    return 0;
                }
                if (args.Length >= 2 && args[0] == "--pruefen")
                {
                    var s = Aufzeichnung.Lesen(args[1]);
                    return Ausgeben(s);
                }
                if (args.Length >= 1 && args[0] == "--live")
                {
                    var s = Sammeln();
                    return Ausgeben(s);
                }
                Console.WriteLine("Aufruf: --aufzeichnen <datei> [--roh] | --pruefen <datei> | --live");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FEHLER: " + ex);
                return 3;
            }
        }

        static Systembild Sammeln()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var sammler = new Sammler(was => Console.WriteLine("  ... " + was));
            var s = sammler.Erfassen();
            Console.WriteLine("Systembild in " + sw.ElapsedMilliseconds + " ms, erhoeht=" + s.Erhoeht + ", Fehler=" + s.Fehlerliste.Count);
            foreach (string z in sammler.Zeiten) Console.WriteLine("      " + z);
            foreach (var f in s.Fehlerliste) Console.WriteLine("  fehler " + f.Quelle + " [" + f.Art + "] " + f.Text);
            return s;
        }

        static int Ausgeben(Systembild s)
        {
            var e = Entscheidungen.Laden();
            var ergebnisse = Alle.Pruefen(s, e);
            Console.WriteLine();
            Console.WriteLine("Gesamt: " + Alle.Gesamt(ergebnisse) + ", Probleme: " + Alle.Probleme(ergebnisse));
            foreach (var b in ergebnisse)
            {
                Console.WriteLine();
                Console.WriteLine("[" + b.Zustand + "] " + Bereich.Titel(b.Bereich) + (b.DatenVorhanden ? "" : "  (keine Daten: " + string.Join("; ", b.Fehlend) + ")"));
                foreach (var f in b.Befunde)
                {
                    Console.WriteLine("   " + f.Zustand.PadRight(7) + " " + f.Titel + "  {" + f.Schluessel + "}");
                    Console.WriteLine("           " + f.Satz);
                    if (f.Rat != null) Console.WriteLine("           Rat: " + f.Rat);
                    if (f.Messwert != null) Console.WriteLine("           Messwert: " + f.Messwert.Wert + " " + f.Messwert.Einheit + (f.Messwert.Schwelle != null ? " (Schwelle " + f.Messwert.Schwelle + ")" : "") + "  Quelle: " + f.Quelle);
                    if (f.Frage != null) Console.WriteLine("           FRAGE [" + f.Frage.Id + "]: " + f.Frage.Text);
                    foreach (string d in f.Detail) Console.WriteLine("           . " + d);
                }
            }
            return 0;
        }
    }
}
