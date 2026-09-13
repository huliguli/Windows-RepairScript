using System;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Die erhoehte Messung: derselbe Sammler wie im Host, nur mit Rechten. Erhoeht liefern
    /// auch die Admin-Quellen (TPM, BitLocker, Dirty-Bit, gesperrte Ereignisprotokolle) Daten
    /// statt "zugriff". Das Bild geht unredigiert als JSON zurueck (ueber die Pipe im Feld
    /// systembildJson, auf der Kommandozeile als Datei) und ersetzt im Host das Bild ohne
    /// Rechte vollstaendig (docs/M2-ENTWURF.md, Abschnitt 0 Punkt 4: kein Zusammenfuehren).
    /// </summary>
    public static class Messung
    {
        /// <summary>Erfasst das Systembild im eigenen Prozess (Sammler.Sammler). fortschritt und protokoll duerfen null sein.</summary>
        public static Systembild Erfassen(Action<string> fortschritt, Protokoll protokoll)
        {
            Sammler.Sammler.Fortschritt melde = null;
            if (fortschritt != null) melde = delegate (string was) { try { fortschritt(was); } catch (Exception) { } };
            var sammler = new Sammler.Sammler(melde, protokoll);
            return sammler.Erfassen();
        }

        /// <summary>Unredigiert: Kern.Json.Schreiben(s), kompakt (eine Zeile, pipe-tauglich).</summary>
        public static string AlsJson(Systembild s)
        {
            return Json.Schreiben(s);
        }

        /// <summary>Kern.Json.Lesen; wirft bei kaputtem Text, damit der Aufrufer es als Fehler meldet und nicht als leeres Bild.</summary>
        public static Systembild AusJson(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("Leerer Systembild-Text: 0 Zeichen.");
            return Json.Lesen<Systembild>(json);
        }
    }
}
