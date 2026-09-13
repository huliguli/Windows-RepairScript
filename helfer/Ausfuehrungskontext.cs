using System;
using System.Collections.Generic;
using System.Diagnostics;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Alles, was eine Maßnahme während der Ausführung braucht: Rückrufe für Zeilen und
    /// Fortschritt (beide dürfen null sein), Abbruch, das Protokoll des Laufs, die Werte für den
    /// Host und den Trockenlauf-Schalter. Lokal (Host schon erhöht, --auto, --plan) und über die
    /// Pipe ist es derselbe Kontext; nur die Rückrufe zeigen woandershin.
    /// </summary>
    public class Ausfuehrungskontext
    {
        /// <summary>(text, art, prozent). art: normal|header|good|bad|dim|warn. prozent nur bei Fortschrittszeilen, dann darf text leer sein.</summary>
        public Action<string, string, int?> Zeile;

        /// <summary>(schritt, gesamt, label) für den Ablaufbalken des Hauptwegs.</summary>
        public Action<int, int, string> Fortschritt;

        /// <summary>Wird zwischen Schritten und im Ausgabeleser abgefragt; null = nie abgebrochen.</summary>
        public Func<bool> Abgebrochen;

        /// <summary>Nie null: wer keins übergibt, bekommt eins mit der Plan-Id (Ausfuehrung.PlanAusfuehren).</summary>
        public Protokoll Protokoll;

        /// <summary>Was der Host nach dem Lauf liest (Schlüssel siehe docs/M2-ENTWURF.md, Abschnitt 2).</summary>
        public Dictionary<string, string> Werte = new Dictionary<string, string>();

        /// <summary>Trockenlauf: Prüfungen und Sicherungspfade laufen, kein Prozess, kein Schreibzugriff.</summary>
        public bool Trocken;

        /// <summary>
        /// SID des Kontos, das den Plan bestellt hat (Pipe: das --sid-Argument; lokal: das eigene Konto).
        /// Maßnahmen, die HKCU anfassen, vergleichen sie mit dem eigenen Konto: meldet ein Standardnutzer
        /// im UAC-Dialog ein anderes Administratorkonto an, ist HKCU im Helfer dessen Profil, nicht seins.
        /// </summary>
        public string AufruferSid;

        /// <summary>Der gerade laufende Prozess (für den Abbruch); wird von Werkzeuge gesetzt.</summary>
        public volatile Process Aktuell;

        volatile bool _abbruch;

        public bool IstAbgebrochen
        {
            get { return _abbruch || (Abgebrochen != null && Abgebrochen()); }
        }

        /// <summary>Abbruch setzen und den laufenden Prozessbaum beenden.</summary>
        public void Abbrechen()
        {
            _abbruch = true;
            try
            {
                Process p = Aktuell;
                if (p != null && !p.HasExited) Werkzeuge.KillTree(p.Id);
            }
            catch (Exception) { }
        }

        public void Schreibe(string text, string art = "normal", int? prozent = null)
        {
            if (Zeile != null) { try { Zeile(text, art, prozent); } catch (Exception) { } }
        }

        public void Melde(int schritt, int gesamt, string label)
        {
            if (Fortschritt != null) { try { Fortschritt(schritt, gesamt, label); } catch (Exception) { } }
        }
    }
}
