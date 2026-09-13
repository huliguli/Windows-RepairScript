using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using WartungsToolbox.Kern;
using WartungsToolbox.Sammler.Quellen;

namespace WartungsToolbox.Sammler
{
    /// <summary>
    /// Fuellt das Systembild - im eigenen Prozess, ohne powershell.exe.
    ///
    /// Jede Quelle laeuft in ihrem eigenen Schritt mit Zeitbudget. Scheitert sie, steht der
    /// Grund in der fehlerliste (zugriff | zeit | fehlt | ausnahme) und die uebrigen Quellen
    /// laufen weiter. Eine Quelle, die nichts liefert und keinen Fehler eintraegt, ist ein
    /// Programmfehler - die Sammlerprobe prueft das.
    ///
    /// Der Sammler weiss, ob er erhoeht laeuft. Nicht erhoeht traegt er fuer jede Quelle, die
    /// Rechte braucht, "zugriff" ein, statt ihre leere Antwort als Befund durchzureichen.
    /// </summary>
    public class Sammler
    {
        public delegate void Fortschritt(string was);

        readonly Fortschritt _melde;
        readonly Protokoll _protokoll;
        public readonly List<string> Zeiten = new List<string>();

        public Sammler(Fortschritt melde = null, Protokoll protokoll = null)
        {
            _melde = melde;
            _protokoll = protokoll;
        }

        /// <summary>Alles, was ohne Erhoehung geht; erhoeht zusaetzlich die Admin-Quellen.</summary>
        public Systembild Erfassen()
        {
            var s = new Systembild();
            s.AufgezeichnetUtc = Zeit.Utc(DateTime.UtcNow);
            s.Erhoeht = Rechte.Erhoeht();
            s.Sprache.Lcid = CultureInfo.InstalledUICulture.LCID;

            // Die Speichermessung laeuft ueber den ganzen Lauf verteilt im Hintergrund; der
            // Leistungs-Schritt holt sie ab (drei Werte im Abstand von Sekunden, kein Mehraufwand).
            Leistungsquelle.SpeicherMessungStarten();

            Schritt(s, "System", "system", () => Grundlagen.Erfassen(s));
            Schritt(s, "Geräte", "geraete", () => Geraete.Erfassen(s));
            Schritt(s, "Festplatten", "datentraeger", () => DatentraegerQuelle.Erfassen(s));
            Schritt(s, "Ereignisse", "ereignisse", () => Ereignisquelle.Erfassen(s));
            Schritt(s, "Startprogramme", "autostart", () => Autostartquelle.Erfassen(s));
            Schritt(s, "Windows-Updates", "windowsupdate", () => Updatequelle.Erfassen(s));
            Schritt(s, "Netzwerk", "netz", () => Netzquelle.Erfassen(s));
            Schritt(s, "Sicherheit", "sicherheit", () => Sicherheitsquelle.Erfassen(s));
            Schritt(s, "Systemschutz", "systemschutz", () => Systemschutzquelle.Erfassen(s));
            // Zuletzt, damit die Hintergrund-Speichermessung ihre drei Werte hat, ohne dass gewartet wird.
            Schritt(s, "Arbeitsspeicher", "leistung", () => Leistungsquelle.Erfassen(s));
            return s;
        }

        void Schritt(Systembild s, string laie, string quelle, Action a)
        {
            if (_melde != null) _melde(laie);
            var sw = Stopwatch.StartNew();
            try { a(); }
            catch (TimeoutException ex) { Fehler(s, quelle, Kern.Fehler.Zeit, ex.Message); }
            catch (Exception ex) when (Wmi.IstZugriffVerweigert(ex)) { Fehler(s, quelle, Kern.Fehler.Zugriff, ex.Message); }
            catch (Exception ex) { Fehler(s, quelle, Kern.Fehler.Ausnahme, ex.GetType().Name + ": " + ex.Message); }
            sw.Stop();
            Zeiten.Add(quelle + " " + sw.ElapsedMilliseconds + " ms");
            if (_protokoll != null)
                _protokoll.Schreibe(Protokoll.Sammler, Protokoll.Messung, quelle,
                    laie + " gelesen", new { ms = sw.ElapsedMilliseconds });
        }

        /// <summary>Fuer die Quellen: einen Teilfehler eintragen, ohne den Schritt abzubrechen.</summary>
        public static void Fehler(Systembild s, string quelle, string art, string text)
        {
            s.Fehlerliste.Add(new Kern.Fehler { Quelle = quelle, Art = art, Text = Kuerzen(text) });
        }

        /// <summary>Eine Teilabfrage innerhalb einer Quelle: Ausnahme wird zum Fehlereintrag.</summary>
        public static bool Versuch(Systembild s, string quelle, Action a)
        {
            try { a(); return true; }
            catch (TimeoutException ex) { Fehler(s, quelle, Kern.Fehler.Zeit, ex.Message); }
            catch (Exception ex) when (Wmi.IstZugriffVerweigert(ex)) { Fehler(s, quelle, Kern.Fehler.Zugriff, ex.Message); }
            catch (Exception ex) { Fehler(s, quelle, Kern.Fehler.Ausnahme, ex.GetType().Name + ": " + ex.Message); }
            return false;
        }

        /// <summary>Eine Quelle, die nur erhoeht geht: nicht erhoeht sofort "zugriff", ohne Versuch.</summary>
        public static bool NurErhoeht(Systembild s, string quelle, Action a)
        {
            if (!s.Erhoeht) { Fehler(s, quelle, Kern.Fehler.Zugriff, "braucht Administratorrechte"); return false; }
            return Versuch(s, quelle, a);
        }

        static string Kuerzen(string t)
        {
            if (t == null) return null;
            t = t.Replace("\r", " ").Replace("\n", " ");
            return t.Length > 300 ? t.Substring(0, 300) : t;
        }
    }
}
