using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Der Absichtsspeicher: Antworten des Nutzers auf Fragen des Systems.
    ///
    /// Grundsatz 1 des Konzepts: Absicht ist kein Fehler. Auf dem Rechner des Betreibers ist
    /// eine Grafikeinheit absichtlich deaktiviert (Problemcode 22). Ein naives Werkzeug wuerde
    /// sie "reparieren". Dieses fragt einmal und merkt sich die Antwort - solange sich der
    /// Zustand nicht aendert. Deshalb steckt der Zustand mit in der Kennung der Frage
    /// (z. B. "geraet:PCI\...:22"): tritt derselbe Zustand wieder auf, gilt die Antwort;
    /// ein anderer Code ist eine neue Frage.
    ///
    /// Die Datei liegt maschinenweit (ProgramData), weil die Entscheidung den PC betrifft,
    /// nicht das Konto, das das Werkzeug gerade bedient.
    /// </summary>
    [DataContract]
    public class Entscheidungen
    {
        public const string Absicht = "absicht";
        public const string Reparieren = "reparieren";

        [DataMember(Name = "schema")] public int Schema = 1;
        [DataMember(Name = "antworten")] public Dictionary<string, Antwort> Antworten = new Dictionary<string, Antwort>();

        // Der Serialisierer ruft keinen Konstruktor auf: ohne Feld "antworten" waere das
        // Dictionary null und die naechste Pruefung stuerbe in AntwortAuf.
        [OnDeserialized]
        void NachDemLesen(StreamingContext c)
        {
            if (Antworten == null) Antworten = new Dictionary<string, Antwort>();
        }

        public string AntwortAuf(string frageId)
        {
            Antwort a;
            return frageId != null && Antworten.TryGetValue(frageId, out a) ? a.Wert : null;
        }

        public bool IstAbsicht(string frageId) { return AntwortAuf(frageId) == Absicht; }

        public void Setze(string frageId, string wert, string zeitUtc)
        {
            Antworten[frageId] = new Antwort { Wert = wert, ZeitUtc = zeitUtc };
        }

        public void Vergiss(string frageId) { Antworten.Remove(frageId); }

        // ---------------------------------------------------------------- Ablage

        /// <summary>Testnaht: die Proben lenken die Ablage in einen Wegwerf-Ordner.</summary>
        public static string PfadFuerProbe;

        public static string Pfad()
        {
            if (PfadFuerProbe != null) return PfadFuerProbe;
            return Path.Combine(Ablage.Maschinenweit(), "entscheidungen.json");
        }

        /// <summary>Grund, warum der Speicher leer geladen wurde (unlesbare Datei); sonst null.</summary>
        public static string LadeFehler;

        public static Entscheidungen Laden()
        {
            LadeFehler = null;
            string p = Pfad();
            // Hauptdatei und Sicherung getrennt versuchen: eine unlesbare Hauptdatei darf die
            // intakte .alt-Kopie nicht ueberspringen (sonst waeren alle Antworten still weg).
            foreach (string datei in new[] { p, p + ".alt" })
            {
                if (!File.Exists(datei)) continue;
                try
                {
                    var e = Json.LesenDatei<Entscheidungen>(datei);
                    if (e != null) return e;
                }
                catch (Exception ex) { LadeFehler = datei + ": " + ex.Message; }
            }
            return new Entscheidungen();
        }

        public void Speichern()
        {
            Json.SchreibenDateiSicher(Pfad(), this, true);
        }
    }

    [DataContract]
    public class Antwort
    {
        [DataMember(Name = "wert")] public string Wert;
        [DataMember(Name = "zeit")] public string ZeitUtc;
    }

    /// <summary>
    /// Wo das Werkzeug seine Laufzeitdaten ablegt.
    ///
    /// Maschinenweit (ProgramData) fuer alles, was den PC betrifft: Entscheidungen, Protokolle,
    /// Sicherungen. Nicht LocalAppData: bei einer Erhoehung ueber ein fremdes Konto zeigt das
    /// auf dessen Profil, und der eigentliche Nutzer findet nichts wieder (v7.2.0).
    /// Faellt ProgramData weg (kein Schreibrecht im Dev-Build), weicht es auf LocalAppData aus
    /// und sagt das im Protokoll.
    /// </summary>
    public static class Ablage
    {
        public const string Ordnername = "WindowsWartung";
        static string _maschinenweit;

        public static string Maschinenweit()
        {
            if (_maschinenweit != null) return _maschinenweit;
            string pd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Ordnername);
            try
            {
                Directory.CreateDirectory(pd);
                string probe = Path.Combine(pd, ".schreibprobe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                _maschinenweit = pd;
            }
            catch (Exception)
            {
                string la = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Ordnername);
                Directory.CreateDirectory(la);
                _maschinenweit = la;
            }
            return _maschinenweit;
        }

        public static bool IstAusweichordner()
        {
            return Maschinenweit().StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                              StringComparison.OrdinalIgnoreCase);
        }
    }
}
