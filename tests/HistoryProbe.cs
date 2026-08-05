using System;
using System.Collections.Generic;
using System.IO;

namespace WartungsToolbox
{
    /// <summary>
    /// Probe fuer src/History.cs. Laeuft ausschliesslich in einem Wegwerf-Ordner - der
    /// echte Verlauf des Nutzers wird nicht angefasst.
    ///
    /// Die Frage dahinter: Ueberlebt der Verlauf einen Absturz mitten im Schreiben?
    /// File.WriteAllText kuerzt die Datei zuerst auf null. Wer in diesem Moment den Strom
    /// verliert, hat hinterher eine halbe Datei - und weil die als JSON unlesbar ist, war
    /// frueher der GESAMTE Verlauf weg, still und ohne Meldung.
    /// </summary>
    static class HistoryProbe
    {
        static int fehler = 0;

        static void Ist(string was, bool bedingung, string detail)
        {
            Console.WriteLine((bedingung ? "         [ok]   " : "         [FEHL] ") + was +
                              (detail.Length > 0 ? "   " + detail : ""));
            if (!bedingung) fehler++;
        }

        static void Main(string[] args)
        {
            string ordner = Path.Combine(Path.GetTempPath(),
                "WW-HistoryProbe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(ordner);
            string datei = Path.Combine(ordner, "history.json");
            History.PfadFuerProbe = datei;

            try
            {
                // ---- Erster Eintrag: die Datei gibt es noch gar nicht ----------------
                History.Add("Erster Lauf", "good", "ohne Befund", 1.5);
                Ist("erster Eintrag legt die Datei an", File.Exists(datei), "");
                Ist("erster Eintrag ist lesbar", History.List().Count == 1, History.List().Count + " Eintraege");

                // ---- Zweiter Eintrag: jetzt wird getauscht ---------------------------
                History.Add("Zweiter Lauf", "warn", "ein Hinweis", 2.5);
                Ist("zweiter Eintrag kommt dazu", History.List().Count == 2, "");
                Ist("die vorherige Fassung liegt als Sicherung daneben",
                    File.Exists(datei + ".alt"), "");
                Ist("keine halbe Datei bleibt liegen", !File.Exists(datei + ".neu"), "");
                Ist("neuester Eintrag steht vorn",
                    History.List().Count == 2 && Enthaelt(History.List()[0], "Zweiter Lauf"), "");

                // ---- Der Ernstfall: die Hauptdatei ist zerstoert ---------------------
                // Genau das hinterlaesst ein Stromausfall mitten im Schreiben.
                File.WriteAllText(datei, "[{\"action\":\"halb geschrie");
                List<object> nachAbsturz = History.List();
                Ist("nach einer zerstoerten Datei ist der Verlauf NICHT weg",
                    nachAbsturz.Count > 0, nachAbsturz.Count + " Eintraege aus der Sicherung");

                // ---- Und er laesst sich danach weiterschreiben -----------------------
                History.Add("Nach dem Absturz", "good", "weiter geht es", 0.5);
                Ist("nach dem Absturz laesst sich weiterschreiben",
                    History.List().Count >= 1 && Enthaelt(History.List()[0], "Nach dem Absturz"), "");

                // ---- Leeren raeumt auch die Sicherung weg ----------------------------
                History.Clear();
                Ist("Leeren entfernt Datei UND Sicherung",
                    !File.Exists(datei) && !File.Exists(datei + ".alt"), "");
                Ist("nach dem Leeren ist der Verlauf leer", History.List().Count == 0, "");
            }
            catch (Exception ex)
            {
                Ist("Probe laeuft ohne Ausnahme", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                History.PfadFuerProbe = null;
                try { Directory.Delete(ordner, true); } catch { }
            }

            Environment.Exit(fehler == 0 ? 0 : 1);
        }

        static bool Enthaelt(object eintrag, string text)
        {
            var d = eintrag as Dictionary<string, object>;
            if (d == null) return false;
            object v;
            return d.TryGetValue("action", out v) && Convert.ToString(v) == text;
        }
    }
}
