using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// JSON fuer Systembild, Befunde, Entscheidungen und Protokoll - ueber den
    /// DataContractJsonSerializer des Frameworks, ohne Fremdpaket und ohne
    /// System.Web.Extensions (das bleibt dem Host vorbehalten).
    ///
    /// Zeiten stehen als ISO-8601-Text in UTC im Modell, nie als DateTime - so gibt es keine
    /// Ueberraschungen mit "/Date(...)/" und Zeitzonen.
    /// </summary>
    public static class Json
    {
        static DataContractJsonSerializerSettings Einstellungen()
        {
            return new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
        }

        public static string Schreiben<T>(T obj, bool lesbar = false)
        {
            var ser = new DataContractJsonSerializer(typeof(T), Einstellungen());
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, obj);
                string kompakt = Encoding.UTF8.GetString(ms.ToArray());
                return lesbar ? Einruecken(kompakt) : kompakt;
            }
        }

        public static T Lesen<T>(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(T), Einstellungen());
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)ser.ReadObject(ms);
        }

        public static T LesenDatei<T>(string pfad)
        {
            return Lesen<T>(File.ReadAllText(pfad, Encoding.UTF8));
        }

        /// <summary>
        /// Schreibt daneben und tauscht dann (File.Replace ist auf NTFS unteilbar). Die Lehre
        /// aus v7.2.0: File.WriteAllText kuerzt zuerst auf null; ein Absturz in diesem Moment
        /// hinterlaesst eine halbe, unlesbare Datei.
        /// </summary>
        public static void SchreibenDateiSicher<T>(string pfad, T obj, bool lesbar = false)
        {
            string dir = Path.GetDirectoryName(pfad);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string neu = pfad + ".neu";
            string alt = pfad + ".alt";
            File.WriteAllText(neu, Schreiben(obj, lesbar), new UTF8Encoding(false));
            if (File.Exists(pfad)) File.Replace(neu, pfad, alt, true);
            else File.Move(neu, pfad);
        }

        /// <summary>Einfacher Einruecker, der Zeichenketten unangetastet laesst.</summary>
        public static string Einruecken(string json)
        {
            var sb = new StringBuilder(json.Length * 2);
            int tiefe = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\') { i++; if (i < json.Length) sb.Append(json[i]); }
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        // Leere Klammern bleiben auf einer Zeile.
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[i + 1]); i++; break; }
                        tiefe++; sb.Append('\n').Append(' ', tiefe * 2); break;
                    case '}':
                    case ']':
                        tiefe--; sb.Append('\n').Append(' ', tiefe * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', tiefe * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Zeitstempel als ISO-8601 in UTC. Sprachneutral, sortierbar, im JSON lesbar.</summary>
    public static class Zeit
    {
        const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static string Utc(DateTime t)
        {
            return t.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
        }

        public static string Utc(DateTime? t)
        {
            return t.HasValue ? Utc(t.Value) : null;
        }

        public static DateTime? Lesen(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            DateTime t;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                return t;
            return null;
        }

        /// <summary>Tage zwischen zwei ISO-Zeiten; null, wenn eine fehlt.</summary>
        public static double? TageZwischen(string frueher, string spaeter)
        {
            var a = Lesen(frueher); var b = Lesen(spaeter);
            if (!a.HasValue || !b.HasValue) return null;
            return (b.Value - a.Value).TotalDays;
        }
    }
}
