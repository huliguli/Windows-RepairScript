using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Die fuenf Zustaende eines Befunds. "absicht" ist neu gegenueber v7: der Zustand ist so
    /// gewollt (der Nutzer hat es bestaetigt), es gibt nichts zu reparieren. Ein Werkzeug, das
    /// ein absichtlich deaktiviertes Geraet "repariert", ist kein Reparaturwerkzeug.
    /// </summary>
    public static class Zustand
    {
        public const string Ok = "ok";
        public const string Warn = "warn";
        public const string Bad = "bad";
        public const string Unknown = "unknown";
        public const string Absicht = "absicht";

        /// <summary>Rang fuer "schlechtester gewinnt". unknown und absicht zaehlen nicht mit.</summary>
        public static int Rang(string z)
        {
            switch (z)
            {
                case Bad: return 3;
                case Warn: return 2;
                case Ok: return 1;
                default: return 0;
            }
        }

        public static string Schlechtester(IEnumerable<string> zustaende)
        {
            string worst = Unknown;
            foreach (string z in zustaende)
                if (Rang(z) > Rang(worst)) worst = z;
            return worst;
        }
    }

    /// <summary>Die Bereiche, in denen Befunde gruppiert werden. Schluessel sind stabil, Titel Anzeige.</summary>
    public static class Bereich
    {
        public const string Hardware = "hardware";
        public const string Datentraeger = "datentraeger";
        public const string Speicherplatz = "speicherplatz";
        public const string Stabilitaet = "stabilitaet";
        public const string Updates = "updates";
        public const string Leistung = "leistung";
        public const string Netz = "netz";
        public const string Sicherheit = "sicherheit";

        public static readonly string[] Reihenfolge =
        {
            Sicherheit, Datentraeger, Speicherplatz, Stabilitaet, Updates, Hardware, Leistung, Netz
        };

        public static string Titel(string bereich)
        {
            switch (bereich)
            {
                case Hardware: return "Geräte und Treiber";
                case Datentraeger: return "Zustand der Festplatten";
                case Speicherplatz: return "Freier Speicherplatz";
                case Stabilitaet: return "Stabilität";
                case Updates: return "Windows-Updates";
                case Leistung: return "Arbeitsspeicher und Auslastung";
                case Netz: return "Netzwerk";
                case Sicherheit: return "Sicherheit";
                default: return bereich;
            }
        }
    }

    /// <summary>
    /// Ein Befund: Messwert, Quelle, Schwelle und Deutung in einem Objekt. Der Laie liest
    /// "laie", der Fachmann klappt "fachmann" auf, und die Oberflaeche entscheidet nichts
    /// selbst - sie zeigt.
    /// </summary>
    [DataContract]
    public class Befund
    {
        [DataMember(Name = "bereich")] public string Bereich;
        /// <summary>Stabiler Schluessel, z. B. "geraet.problem.PCI\...", "datentraeger.nvme.spare".</summary>
        [DataMember(Name = "schluessel")] public string Schluessel;
        [DataMember(Name = "zustand")] public string Zustand;
        [DataMember(Name = "messwert")] public Messwert Messwert;
        /// <summary>Woher der Befund kommt, z. B. "MSFT_PhysicalDisk.HealthStatus", "Kernel-Power 41".</summary>
        [DataMember(Name = "quelle")] public string Quelle;
        [DataMember(Name = "titel")] public string Titel;
        /// <summary>EIN Satz mit Zahl, Bedingung oder Absage.</summary>
        [DataMember(Name = "satz")] public string Satz;
        /// <summary>Was der Nutzer tun kann; null = nichts.</summary>
        [DataMember(Name = "rat")] public string Rat;
        /// <summary>Fachliche Einzelheiten, eine Zeile je Eintrag.</summary>
        [DataMember(Name = "detail")] public List<string> Detail = new List<string>();
        /// <summary>Kennungen von Massnahmen aus dem Katalog (ab Meilenstein 3 ausfuehrbar).</summary>
        [DataMember(Name = "massnahmen")] public List<string> Massnahmen = new List<string>();
        /// <summary>Gesetzt, wenn das System fragen statt handeln muss.</summary>
        [DataMember(Name = "frage")] public Frage Frage;

        public bool IstProblem { get { return Zustand == Kern.Zustand.Bad || Zustand == Kern.Zustand.Warn; } }
    }

    [DataContract]
    public class Messwert
    {
        [DataMember(Name = "wert")] public string Wert;
        [DataMember(Name = "einheit")] public string Einheit;
        [DataMember(Name = "schwelle")] public string Schwelle;

        public static Messwert Von(object wert, string einheit, string schwelle)
        {
            return new Messwert
            {
                Wert = wert == null ? null : System.Convert.ToString(wert, System.Globalization.CultureInfo.InvariantCulture),
                Einheit = einheit,
                Schwelle = schwelle,
            };
        }
    }

    /// <summary>
    /// Eine Frage an den Nutzer. Die Kennung enthaelt den Zustand, um den es geht - aendert
    /// sich der Zustand, gilt die alte Antwort nicht mehr.
    /// </summary>
    [DataContract]
    public class Frage
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "ja")] public string Ja;
        [DataMember(Name = "nein")] public string Nein;
        /// <summary>Was "ja" bedeutet: "absicht" (so lassen) oder "reparieren".</summary>
        [DataMember(Name = "jaHeisst")] public string JaHeisst = "absicht";
    }

    /// <summary>Ergebnis eines Bereichs: Befunde plus die Aussage, ob ueberhaupt Daten da waren.</summary>
    public class BereichErgebnis
    {
        public string Bereich;
        public List<Befund> Befunde = new List<Befund>();
        /// <summary>false = die Quellen des Bereichs haben nichts geliefert (Rechte, Zeit, Fehler).</summary>
        public bool DatenVorhanden;
        /// <summary>Was fehlte, in Alltagssprache (fuer die Zeile "liess sich nicht pruefen").</summary>
        public List<string> Fehlend = new List<string>();

        public string Zustand
        {
            get
            {
                if (!DatenVorhanden && Befunde.Count == 0) return Kern.Zustand.Unknown;
                var z = new List<string>();
                foreach (var b in Befunde) z.Add(b.Zustand);
                string worst = Kern.Zustand.Schlechtester(z);
                if (worst == Kern.Zustand.Unknown && DatenVorhanden) return Kern.Zustand.Ok;
                // Ohne Daten gibt es kein "in Ordnung": ein ok-Befund aus einer Nebenquelle (etwa
                // die Windows-Version, waehrend die Geraeteliste fehlt) macht den Bereich nicht gruen.
                // Warn und bad bleiben, die sind belegt.
                if (worst == Kern.Zustand.Ok && !DatenVorhanden) return Kern.Zustand.Unknown;
                return worst;
            }
        }
    }
}
