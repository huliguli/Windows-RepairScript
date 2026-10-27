using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Der Maßnahmenkatalog des Kerns (Konzept 3.3): je Kennung Stufe, Texte für den Laien,
    /// Sicherung, Rückweg, Dauer und der Nachweis als reine Funktion über dem neuen Systembild.
    /// Reine Daten, kein Windows-Zugriff: die Ausführung liegt im Helfer unter derselben Kennung
    /// (helfer/Katalog.cs), die Regeln hängen die Kennungen an Befunde (Befund.Massnahmen,
    /// Befund.Parameter). Meilenstein 3: Stufen 1 und 2; Meilenstein 4: Stufe 3 (nicht umkehrbar,
    /// nie ohne Klick, mit dem Feld Verlust, das die Vorschau vor der Bestätigung zeigt).
    /// </summary>
    public class Massnahme
    {
        public const string ArtAusfuehren = "ausfuehren";   // laeuft ueber den Helfer
        public const string ArtAnsicht = "ansicht";         // oeffnet eine Ansicht (Speicher, Einstellungen)
        public const string ArtRat = "rat";                 // nur Text (was das Programm nicht selbst ausfuehrt)

        public const string SicherungKeine = "keine";
        public const string SicherungWerte = "werte-merken";
        public const string SicherungPunkt = "wiederherstellungspunkt";
        public const string SicherungExport = "export";     // eine Datei oder ein Ordner unter sicherungen\massnahmen\export (M4)

        public const string RueckwegVonSelbst = "von selbst";
        public const string RueckwegSicherung = "sicherung";
        public const string RueckwegKeiner = "keiner";

        /// <summary>Stufe 3 (Konzept 2.4): nicht umkehrbar; die Vorschau verlangt das Haekchen, der Host lehnt ohne bestaetigt ab.</summary>
        public const int StufeUnumkehrbar = 3;

        public string Kennung;
        public string Bereich;          // Kern.Bereich.*
        public int Stufe;               // 0 messen, 1 harmlos, 2 umkehrbar ueber Sicherung, 3 nicht umkehrbar (StufeUnumkehrbar)
        public string Art = ArtAusfuehren;
        public string Titel;            // Alltagssprache, z. B. "DNS-Cache leeren"
        public string WasPassiert;      // ein bis zwei Saetze
        public string WasBleibt;        // was nicht angefasst wird
        public string Risiko;           // ehrlich, mit Bedingung oder Absage
        public string Verlust;          // Stufe 3 (ausfuehren): Pflicht, ein bis zwei Saetze, was verloren geht; sonst null
        public string Sicherung = SicherungKeine;
        public string Rueckweg = RueckwegVonSelbst;
        public bool Neustart;           // wirkt erst nach Neustart
        public int DauerSekunden;       // geschaetzt
        public bool Freischaltbar;      // Stufe 1: darf in der geplanten Wartung freigeschaltet werden
        public string[] Parameter = new string[0];   // Pflichtparameter (Befund.Parameter)

        /// <summary>Nachweis ueber dem NEUEN Systembild (nach der Massnahme) und den Parametern; nie null.</summary>
        public Func<Systembild, Dictionary<string, string>, Nachweis> Pruefen;

        static readonly Regex Platzhalter = new Regex(@"\{([A-Za-z0-9_.]+)\}", RegexOptions.CultureInvariant);

        /// <summary>
        /// Verlust mit den Zahlen des Befunds: jeder Platzhalter {name} wird durch p[name] ersetzt,
        /// {bytes} als GB mit einer Nachkommastelle (invariant, 1 GB = 1073741824 Byte), jede
        /// andere Zahl als nicht negative ganze Zahl (invariant). Ohne p kommt der Text mit
        /// Platzhaltern zurueck; ein Wert, der in p fehlt oder keine solche Zahl ist, wird "?"
        /// (wie der Sammler einen ungelesenen Wert markiert), nie eine erfundene Zahl und nie ein
        /// Rohtext: die Parameter kommen beim Verlauf aus JSON (M3-Entwurf 8.9), und der Text geht
        /// in die Vorschau.
        /// </summary>
        public string VerlustMit(IDictionary<string, string> p)
        {
            if (Verlust == null || p == null) return Verlust;
            return Platzhalter.Replace(Verlust, t =>
            {
                string name = t.Groups[1].Value, wert;
                if (!p.TryGetValue(name, out wert) || wert == null) return "?";
                long zahl;
                if (!long.TryParse(wert.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out zahl) || zahl < 0) return "?";
                if (name == "bytes") return (zahl / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture);
                return zahl.ToString(CultureInfo.InvariantCulture);
            });
        }
    }

    /// <summary>
    /// Ergebnis des Nachweises mit drei Ergebnisarten (M3-Entwurf 8.1): Ja (Bestanden), Nein
    /// (das Objekt wurde im neuen Bild gefunden, und sein Zustand ist nachweislich der alte) und
    /// Unlesbar (Bestanden = false, Unbestimmt = true: Parameter fehlt, Objekt nicht im Bild oder
    /// Wert nicht lesbar). Unbestimmt ist nie bestanden, aber auch kein Beleg fuer "nichts passiert":
    /// wer den Nachweis anzeigt, sagt "nicht messbar" statt "nicht bestanden".
    /// </summary>
    public class Nachweis
    {
        public bool Bestanden;
        public bool Unbestimmt;  // Nachweis nicht messbar (fehlender Parameter, fehlendes Objekt, unlesbarer Wert)
        public string Satz;
        public bool Neustart;   // Nachweis erst nach einem Neustart moeglich

        public static Nachweis Ja(string satz) { return new Nachweis { Bestanden = true, Satz = satz }; }
        public static Nachweis Nein(string satz) { return new Nachweis { Bestanden = false, Satz = satz }; }
        public static Nachweis NachNeustart(string satz) { return new Nachweis { Bestanden = true, Neustart = true, Satz = satz }; }
        /// <summary>Nicht messbar: nie bestanden, und der Satz nennt, was fehlte.</summary>
        public static Nachweis Unlesbar(string satz) { return new Nachweis { Bestanden = false, Unbestimmt = true, Satz = satz }; }
    }

    public static partial class Massnahmen
    {
        static readonly Dictionary<string, Massnahme> _katalog = new Dictionary<string, Massnahme>(StringComparer.Ordinal);
        static readonly object _gate = new object();
        static bool _gefuellt;

        /// <summary>Katalogeintrag oder null. Die Tabelle wird in Massnahmen.Katalog.cs gefuellt.</summary>
        public static Massnahme Finde(string kennung)
        {
            if (kennung == null) return null;
            Fuellen();
            Massnahme m;
            return _katalog.TryGetValue(kennung, out m) ? m : null;
        }

        public static IEnumerable<Massnahme> Alle()
        {
            Fuellen();
            return new List<Massnahme>(_katalog.Values);
        }

        /// <summary>Stufe-1-Massnahmen, die die geplante Wartung nach Freischaltung von selbst ausfuehren darf.</summary>
        public static IEnumerable<Massnahme> Freischaltbare()
        {
            foreach (var m in Alle()) if (m.Freischaltbar && m.Stufe == 1 && m.Art == Massnahme.ArtAusfuehren) yield return m;
        }

        static void Eintragen(Massnahme m)
        {
            if (m == null || string.IsNullOrEmpty(m.Kennung)) throw new ArgumentException("Massnahme ohne Kennung");
            // Kein stiller Ersatz: ein Eintrag ohne Nachweis-Funktion waere ein "bestanden" ohne
            // Messung. Die Tabelle traegt jede Funktion selbst ein; fehlt sie, faellt es hier auf.
            if (m.Pruefen == null) throw new ArgumentException("Massnahme " + m.Kennung + " ohne Nachweis (Pruefen)");
            // Stufe 3 ohne Verlust-Text gibt es nicht (M4-Entwurf 2.1): die Vorschau zeigt den Text
            // vor dem Haekchen, und ein leeres Feld waere ein Haekchen ohne Aussage.
            if (m.Stufe == Massnahme.StufeUnumkehrbar && m.Art == Massnahme.ArtAusfuehren && string.IsNullOrEmpty(m.Verlust))
                throw new ArgumentException("Massnahme " + m.Kennung + " ist Stufe 3 ohne Verlust");
            _katalog[m.Kennung] = m;
        }

        static void Fuellen()
        {
            lock (_gate)
            {
                if (_gefuellt) return;
                Tabelle();   // Massnahmen.Katalog.cs
                _gefuellt = true;
            }
        }

        // Die Tabelle steht in einer eigenen Datei (kern/Massnahmen.Katalog.cs), damit Kopf und
        // Inhalt getrennt gelesen werden koennen; sie ruft Eintragen(...) je Massnahme.
        static partial void Tabelle();
    }
}
