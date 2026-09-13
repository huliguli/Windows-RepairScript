using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Ein Plan ist die Liste der Maßnahmen, die der Nutzer gesehen und bestätigt hat: nur
    /// Kennungen und Parameter, nie Befehlszeilen. Der Helfer kennt den Katalog selbst und prüft
    /// jede Kennung und jeden Parameter erneut, bevor er etwas ausführt (Konzept 3.1, 3.4).
    ///
    /// Der Plan wandert als JSON über die Pipe und liegt für die Abnahme als Datei vor
    /// (--helfer --plan datei.json). Jeder Wert ist ein Text; Listen sind mit ';' getrennt.
    /// </summary>
    [DataContract]
    public class Plan
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "erstellt")] public string ErstelltUtc;
        [DataMember(Name = "titel")] public string Titel;
        [DataMember(Name = "schritte")] public List<PlanSchritt> Schritte = new List<PlanSchritt>();

        // Der Serialisierer ruft keine Konstruktoren auf: Listen nach dem Lesen absichern.
        [OnDeserialized]
        void NachDemLesen(StreamingContext c) { if (Schritte == null) Schritte = new List<PlanSchritt>(); }

        public static Plan Neu(string titel)
        {
            return new Plan { Id = Protokoll.NeueLaufId(), ErstelltUtc = Zeit.Utc(DateTime.UtcNow), Titel = titel };
        }

        /// <summary>Schritt anhängen: Mit("werkzeug", "id", "6") – Paare aus Schlüssel und Wert.</summary>
        public Plan Mit(string kennung, params string[] paare)
        {
            var s = new PlanSchritt { Kennung = kennung };
            for (int i = 0; i + 1 < paare.Length; i += 2) s.Parameter[paare[i]] = paare[i + 1];
            Schritte.Add(s);
            return this;
        }
    }

    [DataContract]
    public class PlanSchritt
    {
        [DataMember(Name = "kennung")] public string Kennung;
        [DataMember(Name = "parameter")] public Dictionary<string, string> Parameter = new Dictionary<string, string>();

        [OnDeserialized]
        void NachDemLesen(StreamingContext c) { if (Parameter == null) Parameter = new Dictionary<string, string>(); }

        /// <summary>Parameterwert oder null.</summary>
        public string Wert(string name)
        {
            string v;
            return name != null && Parameter.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>Listenparameter (';'-getrennt), leere Einträge entfernt; leer, wenn nicht vorhanden.</summary>
        public List<string> Liste(string name)
        {
            var erg = new List<string>();
            string v = Wert(name);
            if (string.IsNullOrEmpty(v)) return erg;
            foreach (string t in v.Split(';'))
            {
                string x = t.Trim();
                if (x.Length > 0 && !erg.Contains(x)) erg.Add(x);
            }
            return erg;
        }
    }

    /// <summary>
    /// Ergebnis eines Plans. Exit: 0 alles in Ordnung, 1 mindestens ein Schritt mit Problem,
    /// 2 abgelehnt (unbekannte Kennung oder ungültiger Parameter, nichts lief), 3 Ausnahme,
    /// 4 abgebrochen. Werte trägt, was der Host weiterverarbeitet (dism.ausgabe, sfc.ausgabe,
    /// sicherung.satz, speicher.bericht, registrierung.entfernt, neustart).
    /// </summary>
    [DataContract]
    public class PlanErgebnis
    {
        public const int Ok = 0, MitProblem = 1, Abgelehnt = 2, Ausnahme = 3, AbgebrochenExit = 4;

        [DataMember(Name = "planId")] public string PlanId;
        [DataMember(Name = "exit")] public int Exit;
        [DataMember(Name = "problem")] public bool Problem;
        [DataMember(Name = "abgebrochen")] public bool Abgebrochen;
        [DataMember(Name = "sekunden")] public double Sekunden;
        [DataMember(Name = "grund")] public string Grund;
        [DataMember(Name = "werte")] public Dictionary<string, string> Werte = new Dictionary<string, string>();

        [OnDeserialized]
        void NachDemLesen(StreamingContext c) { if (Werte == null) Werte = new Dictionary<string, string>(); }

        public string Wert(string name)
        {
            string v;
            return name != null && Werte.TryGetValue(name, out v) ? v : null;
        }
    }
}
