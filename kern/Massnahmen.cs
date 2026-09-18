using System;
using System.Collections.Generic;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Der Maßnahmenkatalog des Kerns (Konzept 3.3): je Kennung Stufe, Texte für den Laien,
    /// Sicherung, Rückweg, Dauer und der Nachweis als reine Funktion über dem neuen Systembild.
    /// Reine Daten, kein Windows-Zugriff: die Ausführung liegt im Helfer unter derselben Kennung
    /// (helfer/Katalog.cs), die Regeln hängen die Kennungen an Befunde (Befund.Massnahmen,
    /// Befund.Parameter). Meilenstein 3, Stufen 1 und 2; Stufe 3 bleibt Rat bis Meilenstein 4.
    /// </summary>
    public class Massnahme
    {
        public const string ArtAusfuehren = "ausfuehren";   // laeuft ueber den Helfer
        public const string ArtAnsicht = "ansicht";         // oeffnet eine Ansicht (Speicher, Einstellungen)
        public const string ArtRat = "rat";                 // nur Text (Stufe 3 bis M4)

        public const string SicherungKeine = "keine";
        public const string SicherungWerte = "werte-merken";
        public const string SicherungPunkt = "wiederherstellungspunkt";

        public const string RueckwegVonSelbst = "von selbst";
        public const string RueckwegSicherung = "sicherung";
        public const string RueckwegKeiner = "keiner";

        public string Kennung;
        public string Bereich;          // Kern.Bereich.*
        public int Stufe;               // 0 messen, 1 harmlos, 2 umkehrbar ueber Sicherung, 3 nicht umkehrbar (M4)
        public string Art = ArtAusfuehren;
        public string Titel;            // Alltagssprache, z. B. "DNS-Cache leeren"
        public string WasPassiert;      // ein bis zwei Saetze
        public string WasBleibt;        // was nicht angefasst wird
        public string Risiko;           // ehrlich, mit Bedingung oder Absage
        public string Sicherung = SicherungKeine;
        public string Rueckweg = RueckwegVonSelbst;
        public bool Neustart;           // wirkt erst nach Neustart
        public int DauerSekunden;       // geschaetzt
        public bool Freischaltbar;      // Stufe 1: darf in der geplanten Wartung freigeschaltet werden
        public string[] Parameter = new string[0];   // Pflichtparameter (Befund.Parameter)

        /// <summary>Nachweis ueber dem NEUEN Systembild (nach der Massnahme) und den Parametern; nie null.</summary>
        public Func<Systembild, Dictionary<string, string>, Nachweis> Pruefen;
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
