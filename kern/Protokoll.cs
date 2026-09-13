using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Das Laufprotokoll mit zwei Lesern (Grundsatz 7).
    ///
    /// Eine Zeile je Ereignis als JSON (JSONL): Zeit in UTC, Lauf-Kennung, Schicht, Art,
    /// Kennung, dazu "laie" (ein Satz fuer den Nutzer) und "fachmann" (das Objekt fuer den
    /// Techniker: Befehl, Rueckgabewert, Dauer, Pfad der Sicherung). Die Oberflaeche zeigt dem
    /// Laien die laie-Zeilen, der Fachmann klappt fachmann auf.
    ///
    /// "fachmann" ist ein echtes JSON-Objekt (Woerterbuch mit Zahlen, Texten, Wahrheitswerten).
    /// Die Aufrufer uebergeben anonyme Objekte; die kann der DataContractJsonSerializer nicht
    /// schreiben (bis 12.09.2026 stand deshalb der C#-Text "{ ms = 12 }" in der Zeile). Sie
    /// werden hier per Reflexion in ein Woerterbuch uebersetzt.
    ///
    /// Jeder Lauf, den das Programm selbst anstoesst, protokolliert Anfang und Ende - die
    /// Lehre vom 22.08.2026, als die geplante Wartung zehn Minuten lief, ohne eine Zeile zu
    /// hinterlassen.
    ///
    /// Schreibfehler stoeren den Lauf nie. Diese Klasse haengt von nichts ab.
    /// </summary>
    public class Protokoll
    {
        public const string Sammler = "sammler";
        public const string Regeln = "regeln";
        public const string Helfer = "helfer";
        public const string Host = "host";

        public const string Messung = "messung";
        public const string BefundArt = "befund";
        public const string FrageArt = "frage";
        public const string AntwortArt = "antwort";
        public const string Plan = "plan";
        public const string Schritt = "schritt";
        public const string Sicherung = "sicherung";
        public const string Nachweis = "nachweis";
        public const string Rueckweg = "rueckweg";
        public const string FehlerArt = "fehler";
        public const string Anfang = "anfang";
        public const string Ende = "ende";

        readonly object _gate = new object();
        readonly string _pfad;
        public readonly string LaufId;
        public readonly List<Zeile> Zeilen = new List<Zeile>();

        /// <summary>Testnaht: Ordner fuer die Proben.</summary>
        public static string OrdnerFuerProbe;

        public static string Ordner()
        {
            return OrdnerFuerProbe ?? Path.Combine(Ablage.Maschinenweit(), "protokoll");
        }

        public Protokoll(string laufId)
        {
            LaufId = laufId;
            string ordner = Ordner();
            try { Directory.CreateDirectory(ordner); } catch (Exception) { }
            _pfad = Path.Combine(ordner, "lauf-" + laufId + ".jsonl");
        }

        public static string NeueLaufId()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                   + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        public string Pfad { get { return _pfad; } }

        public void Schreibe(string schicht, string art, string kennung, string laie, object fachmann = null)
        {
            var z = new Zeile
            {
                ZeitUtc = Zeit.Utc(DateTime.UtcNow),
                LaufId = LaufId,
                Schicht = schicht,
                Art = art,
                Kennung = kennung,
                Laie = laie,
                Fachmann = fachmann == null ? null : AlsWoerterbuch(fachmann),
            };
            lock (_gate)
            {
                Zeilen.Add(z);
                try { File.AppendAllText(_pfad, Json.Schreiben(z) + "\n", new UTF8Encoding(false)); }
                catch (Exception) { /* Protokollieren darf nie zum Problem werden */ }
            }
        }

        /// <summary>
        /// Anonymes Objekt, Woerterbuch oder Text -> Woerterbuch mit einfachen Werten (Zahl,
        /// Text, Wahrheitswert, null). Verschachteltes wird als Text abgelegt, damit die Zeile
        /// ohne Typhinweise lesbar bleibt.
        /// </summary>
        public static Dictionary<string, object> AlsWoerterbuch(object o)
        {
            var d = new Dictionary<string, object>();
            if (o == null) return d;
            var text = o as string;
            if (text != null) { d["text"] = text; return d; }
            var dict = o as IDictionary;
            if (dict != null)
            {
                foreach (DictionaryEntry e in dict) d[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = Einfach(e.Value);
                return d;
            }
            try
            {
                foreach (var pi in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (pi.GetIndexParameters().Length == 0) d[pi.Name] = Einfach(pi.GetValue(o, null));
                foreach (var fi in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    d[fi.Name] = Einfach(fi.GetValue(o));
            }
            catch (Exception ex) { d["text"] = Convert.ToString(o) + " (" + ex.GetType().Name + ")"; }
            return d;
        }

        static object Einfach(object v)
        {
            if (v == null) return null;
            if (v is string || v is bool) return v;
            if (v is int || v is long || v is short || v is byte || v is uint || v is ushort) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is double || v is float || v is decimal) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (v is DateTime) return Zeit.Utc(((DateTime)v).ToUniversalTime());
            if (v is Enum) return v.ToString();
            var liste = v as IEnumerable;
            if (liste != null)
            {
                var teile = new List<string>();
                foreach (object x in liste) teile.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
                return string.Join(", ", teile);
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>Das Fachliche einer Zeile als Text: "ms = 12, exit = 0".</summary>
        public static string FachmannText(Dictionary<string, object> d)
        {
            if (d == null || d.Count == 0) return null;
            var teile = new List<string>();
            foreach (var kv in d)
                teile.Add(kv.Key + " = " + (kv.Value == null ? "null" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture)));
            return string.Join(", ", teile);
        }

        /// <summary>Alle Protokolldateien, neueste zuerst.</summary>
        public static List<string> Dateien()
        {
            var l = new List<string>();
            try
            {
                if (Directory.Exists(Ordner()))
                {
                    l.AddRange(Directory.GetFiles(Ordner(), "lauf-*.jsonl"));
                    l.Sort(StringComparer.OrdinalIgnoreCase);
                    l.Reverse();
                }
            }
            catch (Exception) { }
            return l;
        }

        public static List<Zeile> LesenDatei(string pfad)
        {
            var l = new List<Zeile>();
            foreach (string line in File.ReadAllLines(pfad, Encoding.UTF8))
            {
                if (line.Trim().Length == 0) continue;
                try { l.Add(Json.Lesen<Zeile>(line)); } catch (Exception) { }
            }
            return l;
        }

        /// <summary>Protokolle aelter als N Tage entfernen.</summary>
        public static void Aufraeumen(int tage)
        {
            try
            {
                var grenze = DateTime.UtcNow.AddDays(-tage);
                foreach (string f in Dateien())
                    if (File.GetLastWriteTimeUtc(f) < grenze) File.Delete(f);
            }
            catch (Exception) { }
        }

        /// <summary>Der Bericht fuer Menschen: laie-Zeilen, darunter eingerueckt das Fachliche.</summary>
        public static string AlsText(IEnumerable<Zeile> zeilen, bool mitFachmann)
        {
            var sb = new StringBuilder();
            foreach (var z in zeilen)
            {
                var t = Zeit.Lesen(z.ZeitUtc);
                string lokal = t.HasValue ? t.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) : z.ZeitUtc;
                sb.Append(lokal).Append("  ").Append(z.Laie ?? z.Kennung ?? z.Art).AppendLine();
                string fach = FachmannText(z.Fachmann);
                if (mitFachmann && !string.IsNullOrEmpty(fach))
                    sb.Append("             ").Append(z.Schicht).Append('/').Append(z.Art).Append(' ')
                      .Append(z.Kennung).Append(": ").Append(fach).AppendLine();
            }
            return sb.ToString();
        }

        [DataContract]
        public class Zeile
        {
            [DataMember(Name = "zeit")] public string ZeitUtc;
            [DataMember(Name = "laufId")] public string LaufId;
            [DataMember(Name = "schicht")] public string Schicht;
            [DataMember(Name = "art")] public string Art;
            [DataMember(Name = "kennung")] public string Kennung;
            [DataMember(Name = "laie")] public string Laie;
            [DataMember(Name = "fachmann")] public Dictionary<string, object> Fachmann;
        }
    }
}
