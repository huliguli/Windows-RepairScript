using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Die Nachrichten der Pipe (docs/M2-ENTWURF.md, Abschnitte 5 und 11): eine JSON-Zeile je
    /// Nachricht, UTF-8 ohne BOM, Zeilenende "\n". Der Host schickt Anfragen, der Helfer
    /// antwortet; die Nutzlast ist immer ein Woerterbuch aus Texten. Grosse Inhalte (Systembild,
    /// Plan, Ergebnis) stehen als JSON-Text in einem Feld, damit der Rahmen nichts ueber ihre
    /// Form wissen muss und der Host-Client (host/HelferClient.cs) dieselben Klassen nutzt.
    /// </summary>
    [DataContract]
    public class Anfrage
    {
        // Die fuenf Anfragetypen. Alles andere beantwortet der Helfer mit "abgelehnt".
        public const string Ping = "ping";
        public const string Messen = "messen";
        public const string Plan = "plan";
        public const string Abbrechen = "abbrechen";
        public const string Ende = "ende";

        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "typ")] public string Typ;
        [DataMember(Name = "nutzlast")] public Dictionary<string, string> Nutzlast = new Dictionary<string, string>();

        // Der Serialisierer ruft keinen Konstruktor auf: ohne Feld "nutzlast" waere das
        // Woerterbuch null und Wert() stuerbe.
        [OnDeserialized]
        void NachDemLesen(StreamingContext c) { if (Nutzlast == null) Nutzlast = new Dictionary<string, string>(); }

        /// <summary>Nutzlastwert oder null, wenn er fehlt.</summary>
        public string Wert(string name)
        {
            string v;
            return name != null && Nutzlast.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>Neu("a1", "plan", "planJson", json) – Paare aus Schluessel und Wert.</summary>
        public static Anfrage Neu(string id, string typ, params string[] paare)
        {
            var a = new Anfrage { Id = id, Typ = typ };
            Rahmen.Paare(a.Nutzlast, paare);
            return a;
        }
    }

    [DataContract]
    public class Antwort
    {
        // Zwischenmeldungen (beliebig viele je Anfrage) ...
        public const string Fortschritt = "fortschritt";
        public const string Zeile = "zeile";
        // ... und genau eine Abschlussantwort je Anfrage-Id.
        public const string Ergebnis = "ergebnis";
        public const string Abgelehnt = "abgelehnt";
        public const string Fehler = "fehler";

        [DataMember(Name = "antwortAuf")] public string AntwortAuf;
        [DataMember(Name = "typ")] public string Typ;
        [DataMember(Name = "nutzlast")] public Dictionary<string, string> Nutzlast = new Dictionary<string, string>();

        [OnDeserialized]
        void NachDemLesen(StreamingContext c) { if (Nutzlast == null) Nutzlast = new Dictionary<string, string>(); }

        /// <summary>Nutzlastwert oder null, wenn er fehlt.</summary>
        public string Wert(string name)
        {
            string v;
            return name != null && Nutzlast.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>true bei ergebnis, abgelehnt und fehler: danach kommt zu dieser Id nichts mehr.</summary>
        public bool IstAbschluss
        {
            get { return Typ == Ergebnis || Typ == Abgelehnt || Typ == Fehler; }
        }

        /// <summary>Neu("a1", "ergebnis", "version", "8.1.0.0") – Paare aus Schluessel und Wert.</summary>
        public static Antwort Neu(string antwortAuf, string typ, params string[] paare)
        {
            var a = new Antwort { AntwortAuf = antwortAuf, Typ = typ };
            Rahmen.Paare(a.Nutzlast, paare);
            return a;
        }
    }

    /// <summary>
    /// Der Rahmen um jede Nachricht: eine JSON-Zeile. Kodieren liefert nie einen Zeilenumbruch
    /// (der DataContractJsonSerializer schreibt Steuerzeichen in Texten als \n und \r), Dekodieren
    /// nimmt eine Zeile ohne das abschliessende "\n".
    /// </summary>
    public static class Rahmen
    {
        /// <summary>Laengere Zeilen (in Byte) lehnt der Helfer ab, statt sie zu puffern.</summary>
        public const int MaxZeile = 1000000;

        public static string Kodieren<T>(T o)
        {
            string s = Kern.Json.Schreiben(o);
            // Sicherheitsnetz: ein roher Umbruch kann nur aus einem Text stammen; dort ist die
            // Ersatzschreibweise gueltiges JSON.
            if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0) s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s;
        }

        public static T Dekodieren<T>(string zeile)
        {
            if (zeile == null) throw new ArgumentNullException("zeile");
            string s = zeile.TrimEnd('\r', '\n', ' ', '\t');
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);   // BOM eines fremden Clients
            if (s.Length == 0) throw new ArgumentException("Leere Zeile ist keine Nachricht.");
            return Kern.Json.Lesen<T>(s);
        }

        /// <summary>Schluessel-Wert-Paare in ein Woerterbuch; ein ueberzaehliger Schluessel ohne Wert wird ignoriert.</summary>
        public static void Paare(Dictionary<string, string> ziel, string[] paare)
        {
            if (ziel == null || paare == null) return;
            for (int i = 0; i + 1 < paare.Length; i += 2)
                if (paare[i] != null) ziel[paare[i]] = paare[i + 1];
        }
    }
}
