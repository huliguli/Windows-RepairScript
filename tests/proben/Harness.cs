using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Das Proben-Geruest fuer die Regeln: laedt aufgezeichnete Systembilder aus
    /// tests/aufzeichnungen, laesst die Regeln darueber laufen und prueft Beziehungen.
    ///
    /// Drei Regeln fuer jede Probe (TESTING-QA-Library):
    ///   1. Beziehung, nicht Zustand: "wenn ein Geraet Code 22 ohne Antwort hat, dann genau ein
    ///      Befund 'frage' mit dieser Instanz und keiner 'bad'" - nie "heute gibt es 1 Befund".
    ///   2. Grundmengen-Zusicherung: jede Probe ueber eine Menge sagt vorher, dass die Menge
    ///      gross genug ist ("mindestens 200 Geraete") - eine leere Datei ist nicht gruen.
    ///   3. Gepflanzte Gegenprobe: zu jeder Regel ein Fall, der sie ROT macht, wenn die
    ///      Bedingung fehlt (Kommentar mit Datum, wann das beobachtet wurde).
    ///
    /// Probenklassen heissen *Proben und haben "public static void Laufen(Harness h)".
    /// Das Geruest findet sie per Reflexion; niemand muss eine Liste pflegen.
    /// </summary>
    public class Harness
    {
        public readonly string Aufzeichnungen;
        public int Bestanden, Fehlgeschlagen;
        readonly List<string> _rot = new List<string>();
        string _gruppe = "";

        public Harness(string aufzeichnungen) { Aufzeichnungen = aufzeichnungen; }

        public void Gruppe(string name)
        {
            _gruppe = name;
            Console.WriteLine();
            Console.WriteLine(name);
        }

        /// <summary>
        /// Eine Probenklasse, deren Eingaben bewusst nicht im Repo liegen (echte Aufzeichnungen
        /// dieses Rechners, Konzept 10), meldet das laut statt still gruen zu bleiben.
        /// </summary>
        public readonly List<string> Uebersprungene = new List<string>();
        public void Uebersprungen(string klasse, string grund)
        {
            Uebersprungene.Add(klasse);
            Console.WriteLine("  [UEBERSPRUNGEN] " + klasse + ": " + grund);
        }

        public void Ist(string was, bool ok, string detail = null)
        {
            if (ok) { Bestanden++; Console.WriteLine("  [ok]   " + was); }
            else
            {
                Fehlgeschlagen++;
                Console.WriteLine("  [FEHL] " + was + (detail != null ? "   " + detail : ""));
                _rot.Add(_gruppe + " / " + was);
            }
        }

        /// <summary>Laedt eine Aufzeichnung; fehlt sie, ist das ein roter Fall, kein Ueberspringen.</summary>
        public Systembild Bild(string dateiname)
        {
            string p = Path.Combine(Aufzeichnungen, dateiname);
            if (!File.Exists(p)) { Ist("Aufzeichnung vorhanden: " + dateiname, false, p); return null; }
            try
            {
                var s = Aufzeichnung_Lesen(p);
                // Ohne Aufzeichnungszeit gaebe es kein "jetzt": jede Zeitfenster-Regel wuerde
                // datumsabhaengig. Der Serialisierer schluckt einen Tippfehler im Feldnamen still,
                // deshalb steht die Zusicherung hier und nicht nur im Kern.
                if (s != null && !Zeit.Lesen(s.AufgezeichnetUtc).HasValue)
                    Ist("Aufzeichnungszeit lesbar: " + dateiname, false, s.AufgezeichnetUtc ?? "(fehlt)");
                return s;
            }
            catch (Exception ex) { Ist("Aufzeichnung lesbar: " + dateiname, false, ex.Message); return null; }
        }

        static Systembild Aufzeichnung_Lesen(string p) { return Json.LesenDatei<Systembild>(p); }

        /// <summary>Regeln ueber ein Bild, mit Entscheidungen (leer, wenn null).</summary>
        public List<BereichErgebnis> Pruefen(Systembild s, Entscheidungen e = null)
        {
            return Alle.Pruefen(s, e ?? new Entscheidungen());
        }

        public static IEnumerable<Befund> Befunde(IEnumerable<BereichErgebnis> erg, string bereich = null)
        {
            return erg.Where(b => bereich == null || b.Bereich == bereich).SelectMany(b => b.Befunde);
        }

        public static Befund Einer(IEnumerable<BereichErgebnis> erg, string schluesselAnfang)
        {
            return Befunde(erg).FirstOrDefault(b => b.Schluessel != null && b.Schluessel.StartsWith(schluesselAnfang));
        }

        public static IEnumerable<Befund> AlleMit(IEnumerable<BereichErgebnis> erg, string schluesselAnfang)
        {
            return Befunde(erg).Where(b => b.Schluessel != null && b.Schluessel.StartsWith(schluesselAnfang));
        }

        public static BereichErgebnis Bereich(IEnumerable<BereichErgebnis> erg, string bereich)
        {
            return erg.First(b => b.Bereich == bereich);
        }

        /// <summary>Jeder Satz eines Befunds braucht Zahl, Bedingung oder Absage (Grundsatz 9).</summary>
        public void SaetzeTragen(IEnumerable<Befund> befunde)
        {
            foreach (var b in befunde)
            {
                bool ok = !string.IsNullOrWhiteSpace(b.Satz) && !string.IsNullOrWhiteSpace(b.Titel)
                          && (b.Satz.Any(char.IsDigit) || b.Satz.Contains("nicht") || b.Satz.Contains("kein") || b.Satz.Contains("wenn") || b.Satz.Contains("falls") || b.Satz.Contains("ohne") || b.Satz.Contains("nie"));
                Ist("Satz traegt Zahl, Bedingung oder Absage: " + b.Schluessel, ok, b.Satz);
                Ist("kein Geviertstrich im Satz: " + b.Schluessel, (b.Satz ?? "").IndexOf('—') < 0 && (b.Rat ?? "").IndexOf('—') < 0);
            }
        }

        public static int Main(string[] args)
        {
            string ordner = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aufzeichnungen");
            var h = new Harness(ordner);
            Console.WriteLine("Aufzeichnungen: " + ordner);

            var klassen = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Name.EndsWith("Proben") && t.GetMethod("Laufen", BindingFlags.Public | BindingFlags.Static) != null)
                .OrderBy(t => t.Name).ToList();
            if (klassen.Count == 0) { Console.WriteLine("KEINE Probenklassen gefunden"); return 1; }

            // Grundmenge der Klassen: verschwindet eine Datei oder verliert "Laufen" das public
            // static, bliebe der Lauf sonst gruen. Die Zahl wird bewusst hier gepflegt.
            const int ErwarteteKlassen = 8;
            foreach (var t in klassen)
            {
                int vorher = h.Bestanden + h.Fehlgeschlagen;
                try { t.GetMethod("Laufen").Invoke(null, new object[] { h }); }
                catch (Exception ex)
                {
                    h.Fehlgeschlagen++;
                    Console.WriteLine("  [FEHL] " + t.Name + " warf: " + (ex.InnerException ?? ex).Message);
                }
                int zahl = h.Bestanden + h.Fehlgeschlagen - vorher;
                if (h.Uebersprungene.Contains(t.Name)) continue;
                h.Gruppe("Grundmenge " + t.Name);
                h.Ist("mindestens 25 Zusicherungen in " + t.Name, zahl >= 25, zahl.ToString());
            }
            h.Gruppe("Grundmenge der Probenklassen");
            h.Ist("genau " + ErwarteteKlassen + " Probenklassen gefunden (" + string.Join(", ", klassen.Select(t => t.Name)) + ")", klassen.Count == ErwarteteKlassen, klassen.Count.ToString());

            Console.WriteLine();
            Console.WriteLine("Ergebnis: " + h.Bestanden + " bestanden, " + h.Fehlgeschlagen + " fehlgeschlagen, " + klassen.Count + " Probenklassen"
                              + (h.Uebersprungene.Count > 0 ? " (" + h.Uebersprungene.Count + " uebersprungen: " + string.Join(", ", h.Uebersprungene) + ")" : ""));
            foreach (string r in h._rot) Console.WriteLine("  rot: " + r);
            return h.Fehlgeschlagen == 0 && h.Bestanden > 0 ? 0 : 1;
        }
    }
}
