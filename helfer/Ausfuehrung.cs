using System;
using System.Collections.Generic;
using System.Diagnostics;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Fuehrt einen Plan aus (docs/M2-ENTWURF.md, Abschnitt 4 und Nachtraege in Abschnitt 12).
    ///
    /// Erst werden ALLE Schritte geprueft (Kennung im Katalog, Parameter ueber Massnahme.Pruefen,
    /// bei Massnahme.Profilgebunden das Konto: AufruferIstEigenesKonto). Eine einzige Ablehnung
    /// beendet den Plan mit Exit 2, bevor irgendetwas laeuft; ein leerer Plan ebenso. Dann Schritt fuer Schritt: Protokoll anfang/schritt/ende (Schicht helfer),
    /// Fortschritt (i, n, Titel) nur bei mehr als einem Schritt (Katalogschritte melden ihren
    /// Unterfortschritt mit eigenem gesamt), Ausfuehren, Abbruchpruefung dazwischen. Keine
    /// Kopfzeile je Schritt: die Werkzeugzeile "›  file args" reicht, der Host baut keine zweite.
    ///
    /// Eine Ausnahme in einem Schritt gibt eine Zeile bad und Problem=true; der naechste
    /// Schritt laeuft weiter, ausser bei tiefenpruefung und dateien.reparieren, wo die Kette
    /// abbricht (eine halbe Reparatur waere schlechter als keine).
    ///
    /// Exit: 0 alles in Ordnung, 1 mindestens ein Schritt mit Problem (auch eine Ausnahme, nach
    /// der die Kette weiterlief; Grund = erster Fehler), 2 abgelehnt, 3 Kettenbruch oder Ausnahme
    /// vor dem ersten Schritt, 4 abgebrochen. Werte = kontext.Werte (dism.*, sfc.*, sicherung.*,
    /// speicher.bericht, registrierung.*, neustart); der interne Schluessel "problem" wird vor
    /// der Rueckgabe entfernt, das Ergebnis steht in PlanErgebnis.Problem.
    /// </summary>
    public static class Ausfuehrung
    {
        static readonly string[] KetteBrichtBeiAusnahme = { "tiefenpruefung", "dateien.reparieren" };

        public static PlanErgebnis PlanAusfuehren(Plan plan, Ausfuehrungskontext k)
        {
            var sw = Stopwatch.StartNew();
            if (k == null) k = new Ausfuehrungskontext();
            if (k.Werte == null) k.Werte = new Dictionary<string, string>();
            k.Werte.Remove(Katalog.ProblemSchluessel);

            var erg = new PlanErgebnis { Werte = k.Werte };
            if (plan == null)
            {
                erg.Exit = PlanErgebnis.Abgelehnt;
                erg.Grund = "Es wurde kein Plan übergeben.";
                if (k.Protokoll != null) k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Abgelehnt, "plan", "Abgelehnt: " + erg.Grund);
                k.Schreibe("✖  Abgelehnt: " + erg.Grund, "bad");
                erg.Sekunden = sw.Elapsed.TotalSeconds;
                return erg;
            }
            // Die Plan-Id wird Dateiname (lauf-<id>.jsonl) und kommt auch von aussen (Pipe,
            // Plandatei). Der Protokoll-Konstruktor ersetzt ungueltige Ids; hier wird vorher
            // ersetzt und danach die tatsaechlich benutzte Lauf-Id in Plan und Ergebnis gesetzt,
            // damit Protokolldatei, Ergebnis und die Zeilen des Aufrufers dieselbe Id tragen.
            if (!Protokoll.LaufIdGueltig(plan.Id)) plan.Id = Protokoll.NeueLaufId();
            if (k.Protokoll == null) k.Protokoll = new Protokoll(plan.Id);
            plan.Id = k.Protokoll.LaufId;
            erg.PlanId = k.Protokoll.LaufId;
            int n = plan.Schritte.Count;

            // ---- 1. Alles pruefen, bevor irgendetwas laeuft ----
            if (n == 0)
                return Ablehnen(k, erg, sw, "plan", "Der Plan enthält keinen Schritt.");
            var massnahmen = new List<Massnahme>(n);
            for (int i = 0; i < n; i++)
            {
                PlanSchritt s = plan.Schritte[i];
                string kennung = s == null ? null : s.Kennung;
                Massnahme m = Katalog.Finde(kennung);
                if (m == null)
                    return Ablehnen(k, erg, sw, kennung ?? "?", "Unbekannte Kennung „" + (kennung ?? "") + "“ in Schritt " + (i + 1) + " von " + n + ".");
                string grund = null;
                try { if (m.Pruefen != null) grund = m.Pruefen(s); }
                catch (Exception ex) { grund = "Die Prüfung von „" + kennung + "“ scheiterte: " + ex.Message; }
                if (grund != null)
                    return Ablehnen(k, erg, sw, kennung, grund);
                // Profilgebundene Massnahmen arbeiten in HKCU, %TEMP%, %LOCALAPPDATA%, dem
                // Papierkorb oder der Aufgabenplanung DES HELFER-KONTOS. Meldet ein Standardnutzer
                // im UAC-Dialog ein anderes Administratorkonto an, waere das dessen Profil, nicht
                // seins (Nachtraege B5/B39): dann wird der ganze Plan abgelehnt, bevor etwas laeuft.
                if (m.Profilgebunden && !AufruferIstEigenesKonto(k.AufruferSid))
                    return Ablehnen(k, erg, sw, kennung, ProfilFremdesKonto);
                massnahmen.Add(m);
            }

            // ---- 2. Ausfuehren ----
            k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Anfang, "plan",
                (string.IsNullOrEmpty(plan.Titel) ? "Plan" : plan.Titel) + " gestartet" + (k.Trocken ? " (Trockenlauf)" : ""),
                new { planId = erg.PlanId, schritte = n, trocken = k.Trocken });

            bool problem = false;
            bool kettenbruch = false;
            bool abgebrochen = false;
            int fertig = 0;
            string ersterFehler = null;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (k.IstAbgebrochen) { abgebrochen = true; break; }
                    Massnahme m = massnahmen[i];
                    PlanSchritt s = plan.Schritte[i];
                    string titel = Katalog.TitelVon(m, s);
                    int stufe = Katalog.StufeVon(m, s);
                    // Der Hauptweg-Balken zaehlt Planschritte; bei einem einzigen Schritt wuerde
                    // "(1/1)" nur mit dem Unterfortschritt der Massnahme (1/2, 2/2, ...) kollidieren.
                    if (n > 1) k.Melde(i + 1, n, titel);
                    var swSchritt = Stopwatch.StartNew();
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Anfang, m.Kennung, titel + " gestartet",
                        new { schritt = i + 1, gesamt = n, stufe, trocken = k.Trocken });

                    // Der Problem-Schluessel gilt je Schritt: vorher raeumen, nachher lesen.
                    k.Werte.Remove(Katalog.ProblemSchluessel);
                    bool schrittProblem = false;
                    string fehler = null;
                    try
                    {
                        m.Ausfuehren(s, k);
                    }
                    catch (Exception ex)
                    {
                        schrittProblem = true;
                        fehler = ex.Message;
                        if (ersterFehler == null) ersterFehler = titel + ": " + ex.Message;
                        AppLog.Error("Schritt " + m.Kennung, ex);
                        k.Schreibe("   Fehler: " + ex.Message, "bad");
                        k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, m.Kennung, "Fehler in " + titel + ": " + ex.Message,
                            new { typ = ex.GetType().Name });
                    }
                    if (k.Werte.ContainsKey(Katalog.ProblemSchluessel)) schrittProblem = true;
                    if (schrittProblem) problem = true;
                    if (!k.IstAbgebrochen) fertig++;   // ein mittendrin abgebrochener Schritt zaehlt nicht als fertig

                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, m.Kennung, titel + " ist beendet",
                        new { schritt = i + 1, gesamt = n, sekunden = (int)swSchritt.Elapsed.TotalSeconds, problem = schrittProblem, fehler, trocken = k.Trocken });

                    if (fehler != null && Array.IndexOf(KetteBrichtBeiAusnahme, m.Kennung) >= 0)
                    {
                        kettenbruch = true;
                        k.Schreibe("   Die Kette wird nach diesem Fehler nicht fortgesetzt (" + (n - i - 1) + " Schritte übersprungen).", "bad");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Ausnahme ausserhalb einer Massnahme (Titel, Stufe, Protokoll): der Plan ist
                // damit nicht mehr verlaesslich, das ist Exit 3 wie ein Kettenbruch.
                kettenbruch = true;
                problem = true;
                if (ersterFehler == null) ersterFehler = "Ausführung: " + ex.Message;
                AppLog.Error("Plan " + erg.PlanId, ex);
                k.Schreibe("   Fehler: " + ex.Message, "bad");
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, "plan", "Fehler im Ablauf: " + ex.Message,
                    new { typ = ex.GetType().Name });
            }
            if (k.IstAbgebrochen) abgebrochen = true;

            sw.Stop();
            // Der Schluessel war nur das Signal zwischen Massnahme und Ausfuehrung; nach aussen
            // zaehlt PlanErgebnis.Problem (Vertrag Abschnitt 12).
            k.Werte.Remove(Katalog.ProblemSchluessel);
            erg.Sekunden = sw.Elapsed.TotalSeconds;
            erg.Problem = problem;
            erg.Abgebrochen = abgebrochen;
            erg.Grund = ersterFehler;
            if (abgebrochen) erg.Exit = PlanErgebnis.AbgebrochenExit;
            else if (kettenbruch) erg.Exit = PlanErgebnis.Ausnahme;
            else if (problem) erg.Exit = PlanErgebnis.MitProblem;
            else erg.Exit = PlanErgebnis.Ok;

            string satz = abgebrochen ? "Abgebrochen nach " + fertig + " von " + n + " Schritten"
                        : kettenbruch ? "Beendet mit Fehler: " + ersterFehler
                        : problem ? "Beendet, aber nicht alles hat geklappt (" + erg.Sekunden.ToString("0.0") + " s)"
                        : "Beendet in " + erg.Sekunden.ToString("0.0") + " s";
            k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Ende, "plan", satz,
                new { exit = erg.Exit, sekunden = (int)erg.Sekunden, problem = erg.Problem, abgebrochen, grund = ersterFehler, trocken = k.Trocken });
            return erg;
        }

        /// <summary>Grund der Ablehnung einer profilgebundenen Massnahme bei fremdem Konto (Vertrag Abschnitt 14).</summary>
        public const string ProfilFremdesKonto = "Im Dialog ist ein anderes Konto angemeldet; diese Maßnahme braucht Ihr eigenes Konto.";

        /// <summary>
        /// Ist das eigene Konto das des Aufrufers? AufruferSid null = lokaler Lauf im selben Prozess
        /// (Host schon erhoeht, --auto, --plan), dort ist es immer dasselbe Konto. Ueber die Pipe
        /// steht dort die SID aus --sid; weicht sie vom eigenen Token ab, hat der Nutzer im
        /// UAC-Dialog ein anderes Konto angemeldet. Nicht lesbar zaehlt als fremd (nie stille Leere).
        /// Hier statt im Katalog, weil die Vorpruefung (Massnahme.Profilgebunden) und
        /// registrierung.entfernen (HKCU-Funde) dieselbe Antwort brauchen.
        /// </summary>
        public static bool AufruferIstEigenesKonto(string aufruferSid)
        {
            if (string.IsNullOrEmpty(aufruferSid)) return true;
            try
            {
                using (var wi = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    if (wi.User == null) return false;
                    return string.Equals(wi.User.Value, aufruferSid, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Eigenes Konto nicht lesbar, es zählt als fremdes Konto: " + ex.Message);
                return false;
            }
        }

        static PlanErgebnis Ablehnen(Ausfuehrungskontext k, PlanErgebnis erg, Stopwatch sw, string kennung, string grund)
        {
            erg.Exit = PlanErgebnis.Abgelehnt;
            erg.Grund = grund;
            erg.Problem = false;
            k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Abgelehnt, kennung, "Abgelehnt: " + grund, new { grund });
            k.Schreibe("✖  Abgelehnt: " + grund, "bad");
            AppLog.Warn("Plan abgelehnt (" + kennung + "): " + grund);
            erg.Sekunden = sw.Elapsed.TotalSeconds;
            return erg;
        }
    }
}
