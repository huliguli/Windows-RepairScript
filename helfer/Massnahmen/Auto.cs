using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// wartung.massnahmen: der Teil der geplanten Wartung, der aus Befunden handelt (M3-Entwurf,
    /// Abschnitt 0 Punkt 5, Abschnitt 3 und Abschnitt 8.6). Der Nutzer schaltet in den
    /// Einstellungen einzelne Stufe-1-Massnahmen frei (Kern.Massnahmen.Freischaltbare,
    /// zeitplan.json "freigaben"); der Schritt misst den PC im Helfer (erhoeht), wertet die
    /// Regeln aus und fuehrt genau die freigegebenen Massnahmen aus, die ein Befund mit Problem
    /// vorschlaegt, mit den Pflichtparametern dieses Befunds. Danach eine zweite Messung, und
    /// der Nachweis jeder Massnahme kommt aus dem Kern-Katalog ueber dem neuen Bild
    /// (Massnahme.Pruefen), nie aus dem Rueckgabewert.
    ///
    /// Drei Ergebnisarten des Nachweises (Vertrag 8.1): bestanden, nicht bestanden, unbestimmt
    /// (der Kern konnte den Wert nicht lesen). Unbestimmt zaehlt als nicht bestanden und wird als
    /// eigene Zeile "Nachweis nicht messbar" gemeldet. Ein Auftrag, dessen Teilmassnahme mit
    /// Fehler lief (Ausnahme oder Problem-Schluessel), ist NIE bestanden, auch wenn der Kern ueber
    /// dem Bild "Ja" sagt: netz.dnscache.leeren weist nur die Ausfuehrung nach, und die sah nur
    /// dieser Schritt. Der Satz des Helfers (nachweis.satz) bleibt am Auftrag und wird zitiert.
    ///
    /// Die Kennung wartung.auto in helfer\Katalog.cs bleibt, wie sie ist (Werkzeugsatz aus
    /// Catalog.AutoSet); src\AutoRunner.cs stellt diesen Schritt davor, wenn Freigaben gesetzt
    /// sind. Parameter freigaben: ';'-Liste von Kennungen. Jede muss im Kern freischaltbar
    /// (Stufe 1, Art ausfuehren) UND im Helfer-Katalog ausfuehrbar sein; sonst wird der ganze
    /// Plan abgelehnt, bevor etwas laeuft (Ausfuehrung prueft vor dem ersten Schritt).
    ///
    /// Profilgebunden: die Messung liest HKCU, %TEMP% und die Autostart-Eintraege des Kontos,
    /// unter dem der Helfer laeuft. Meldet ein Standardnutzer im UAC-Dialog ein anderes
    /// Administratorkonto an, waeren das dessen Befunde, nicht seine (Nachtraege B5/B39).
    ///
    /// Werte fuer den Host, alle erst NACH der Auswahl gesetzt (scheitert die Messung oder bricht
    /// der Nutzer davor ab, fehlen sie, und der Verlauf nennt keine Zahl statt einer falschen):
    ///   auto.massnahmen       Anzahl gestarteter Massnahmen (auch trocken)
    ///   auto.nachweise        "bestanden/geprueft", trocken und ohne zweite Messung "0/0"
    ///   auto.nichtausfuehrbar Anzahl Vorschlaege, die nicht liefen: Pflichtparameter am Befund
    ///                         fehlte, oder die Pruefung der Teilmassnahme lehnte den Auftrag ab
    ///   sicherung.dateien     ';'-Liste der angelegten Sicherungsdateien (Rueckweg im Host)
    ///   neustart = "1"        eine Massnahme oder ein Nachweis verlangt einen Neustart
    /// Die uebrigen Werte der Teilmassnahmen (nachweis.satz, sicherung.vorher, ...) wandern in
    /// das Laufprotokoll und bleiben nicht im Ergebnis: dort gaelte sonst nur der letzte Wert.
    ///
    /// Trockenlauf: Messung und Auswahl laufen (nur Lesezugriffe), jede Teilmassnahme laeuft
    /// mit Trocken = true und meldet "(trocken) ..." selbst; Nachmessung und Nachweis entfallen.
    /// Kein Skript-Interpreter, kein Prozess aus dieser Datei: alles, was eingreift, tun die
    /// Teilmassnahmen unter ihrer eigenen Kennung.
    /// </summary>
    public static partial class MassnahmenM3
    {
        public const string KennungMassnahmen = "wartung.massnahmen";
        public const string ParameterFreigaben = "freigaben";
        public const string WertMassnahmen = "auto.massnahmen";
        public const string WertNachweise = "auto.nachweise";
        public const string WertNichtAusfuehrbar = "auto.nichtausfuehrbar";
        public const string WertSicherungen = "sicherung.dateien";
        const string WertNeustart = "neustart";

        // Kennungen, die nie in sich selbst laufen duerfen, auch wenn ein Katalog sie eines
        // Tages als freischaltbar fuehrte.
        static readonly string[] NieInSichSelbst = { KennungMassnahmen, "wartung.auto" };

        static partial void Auto(List<Massnahme> l)
        {
            l.Add(new Massnahme
            {
                Kennung = KennungMassnahmen, Titel = "Freigegebene Maßnahmen der geplanten Wartung", Stufe = 1, Profilgebunden = true,
                Pruefen = FreigabenPruefen,
                Ausfuehren = FreigabenAusfuehren,
            });
        }

        // ------------------------------------------------------------------ Pruefung

        static string FreigabenPruefen(PlanSchritt s)
        {
            List<string> freigaben = s.Liste(ParameterFreigaben);
            if (freigaben.Count == 0) return "Es ist keine Maßnahme freigegeben (Parameter freigaben ist leer).";
            foreach (string kennung in freigaben)
            {
                string grund = FreigabeGrund(kennung);
                if (grund != null) return grund;
            }
            return null;
        }

        /// <summary>null = die Kennung darf von selbst laufen; sonst der Grund der Ablehnung.</summary>
        static string FreigabeGrund(string kennung)
        {
            if (kennung.Length > 64)
                return "Die Freigabe „" + KurzText(kennung) + "“ ist länger als 64 Zeichen.";
            if (!NurKennungszeichen(kennung))
                return "Die Freigabe „" + KurzText(kennung) + "“ enthält andere Zeichen als Kleinbuchstaben, Ziffern und Punkt.";
            if (Array.IndexOf(NieInSichSelbst, kennung) >= 0)
                return "Die Freigabe „" + kennung + "“ ist selbst die geplante Wartung und läuft nicht in sich selbst.";
            Kern.Massnahme km = Kern.Massnahmen.Finde(kennung);
            if (km == null || !km.Freischaltbar || km.Stufe != 1 || km.Art != Kern.Massnahme.ArtAusfuehren)
                return "Die Maßnahme „" + kennung + "“ darf in der geplanten Wartung nicht von selbst laufen; erlaubt sind nur freischaltbare Maßnahmen der Stufe 1.";
            if (Katalog.Finde(kennung) == null)
                return "Die Maßnahme „" + kennung + "“ ist freischaltbar, aber im Helfer nicht ausführbar.";
            return null;
        }

        static bool NurKennungszeichen(string s)
        {
            foreach (char c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.')) return false;
            return true;
        }

        // ------------------------------------------------------------------ Ausfuehrung

        /// <summary>Eine faellige Massnahme: Kennung, Parameter aus dem Befund, die Befunde, die sie vorschlagen.</summary>
        class Auftrag
        {
            public string Kennung;
            public Kern.Massnahme KernEintrag;                  // Texte, Pflichtparameter, Nachweis
            public Massnahme HelferEintrag;                     // Pruefen und Ausfuehren
            // Nur die Pflichtparameter des Kern-Eintrags aus dem Befund; nach dem Lauf kommen
            // die optionalen Nachweis-Parameter des Kerns dazu (vorher, neustart; Vertrag 8.1).
            public Dictionary<string, string> Parameter = new Dictionary<string, string>();
            public List<string> Befunde = new List<string>();   // Schluessel der Befunde
            public string BefundTitel;                          // Titel des ersten Befunds, fuer die Zeile
            public bool Gestartet;                              // Ausfuehren wurde aufgerufen (auch trocken)
            public bool Problem;                                // Ausnahme oder Problem-Schluessel
            public bool Neustart;                               // die Teilmassnahme selbst verlangt einen Neustart
            public string HelferSatz;                           // nachweis.satz (sonst sicherung.satz) der Teilmassnahme
            public string Fehler;                               // Text der Ausnahme, wenn eine flog
            public string Sicherung;                            // sicherung.datei der Teilmassnahme

            public string Titel
            {
                get
                {
                    if (KernEintrag != null && !string.IsNullOrEmpty(KernEintrag.Titel)) return KernEintrag.Titel;
                    return HelferEintrag != null && !string.IsNullOrEmpty(HelferEintrag.Titel) ? HelferEintrag.Titel : Kennung;
                }
            }

            /// <summary>Parameter als "a=1;b=2" fuer Protokoll und Doppelt-Schluessel.</summary>
            public string ParameterText { get { return MassnahmenM3.ParameterText(Parameter); } }

            /// <summary>Was der Helfer selbst zu diesem Auftrag gesagt hat, als Zitat fuer den Nachweis-Satz; leer, wenn nichts vorliegt.</summary>
            public string HelferZitat
            {
                get
                {
                    string satz = !string.IsNullOrEmpty(HelferSatz) ? HelferSatz : Fehler;
                    return string.IsNullOrEmpty(satz) ? "" : " Der Helfer meldete: „" + satz.TrimEnd('.', ' ') + "“.";
                }
            }
        }

        static void FreigabenAusfuehren(PlanSchritt s, Ausfuehrungskontext k)
        {
            List<string> freigaben = s.Liste(ParameterFreigaben);

            // ---- 1. Messen und Regeln auswerten ----
            // Vor der Auswahl ist die Zahl der Unterschritte offen; 3 ist die kleinste, die alle
            // drei Phasen hat (Messen, eine Massnahme, Nachmessen). Danach steht gesamt fest.
            k.Melde(1, 3, "Der PC wird gemessen");
            k.Schreibe("Der PC wird gemessen; freigegeben: " + string.Join(", ", freigaben), "header");
            Systembild bild = Messen(k);
            // Ohne Bild keine Zahlen: auto.massnahmen bleibt weg, der Verlauf sagt dann nicht
            // "keine Massnahme war faellig", sondern nur "mit Hinweisen" (Messung gescheitert).
            if (bild == null) { Katalog.Problem(k); return; }
            if (k.IstAbgebrochen) return;
            Entscheidungen entscheidungen = Entscheidungen.Laden();
            List<BereichErgebnis> ergebnisse = Kern.Regeln.Alle.Pruefen(bild, entscheidungen);
            Abarbeiten(k, freigaben, ergebnisse, entscheidungen);
        }

        /// <summary>
        /// Alles nach der ersten Messung: Auswahl, Ausfuehrung, Nachmessung, Nachweis. Getrennt
        /// von FreigabenAusfuehren, damit sich Auswahl und Ausfuehrung ueber gestellte Befunde
        /// pruefen lassen, ohne dass eine echte Massnahme auf diesem PC faellig sein muss.
        /// </summary>
        static void Abarbeiten(Ausfuehrungskontext k, List<string> freigaben, List<BereichErgebnis> ergebnisse, Entscheidungen entscheidungen)
        {
            int probleme = Kern.Regeln.Alle.Probleme(ergebnisse);

            // ---- 2. Auswahl: Befund mit Problem, Kennung freigegeben, Parameter vollstaendig ----
            int ohneParameter, offeneFragen;
            List<Auftrag> auftraege = Auswaehlen(k, ergebnisse, entscheidungen, freigaben, out ohneParameter, out offeneFragen);
            // Erst jetzt gibt es belastbare Zahlen fuer den Host; sie werden unten hochgezaehlt
            // (auto.nichtausfuehrbar um jeden Auftrag, den die Pruefung der Teilmassnahme ablehnt).
            k.Werte[WertMassnahmen] = "0";
            k.Werte[WertNachweise] = "0/0";
            k.Werte[WertNichtAusfuehrbar] = ohneParameter.ToString(CultureInfo.InvariantCulture);
            // Unterschritte einmal bestimmt: Messen, je Auftrag einer, Nachmessen (entfaellt trocken).
            int gesamt = auftraege.Count + (k.Trocken ? 1 : 2);
            if (auftraege.Count == 0)
            {
                // Drei Gruende, drei Saetze: nichts vorgeschlagen (gut), Vorschlag ohne
                // Parameter (Fehler im Programm, oben schon als warn gemeldet), Vorschlag
                // hinter einer offenen Frage (der Nutzer entscheidet, nicht die Wartung).
                bool eine = freigaben.Count == 1;
                string satz;
                if (ohneParameter > 0)
                    satz = (eine ? "Die freigegebene Maßnahme läuft nicht: " : "Keine der " + freigaben.Count + " freigegebenen Maßnahmen läuft: ")
                           + Zahlwort(ohneParameter, "Vorschlag war", "Vorschläge waren") + " nicht ausführbar (Parameter fehlt am Befund).";
                else if (offeneFragen > 0)
                    satz = (eine ? "Die freigegebene Maßnahme läuft nicht: " : "Keine der " + freigaben.Count + " freigegebenen Maßnahmen läuft: ")
                           + Zahlwort(offeneFragen, "Vorschlag wartet", "Vorschläge warten") + " auf Ihre Antwort.";
                else
                    satz = (eine ? "Die freigegebene Maßnahme ist nicht fällig: " : "Keine der " + freigaben.Count + " freigegebenen Maßnahmen ist fällig: ")
                           + (probleme == 0
                                ? "kein Befund hat ein Problem."
                                : probleme == 1
                                    ? "1 Befund mit Problem, er schlägt " + (eine ? "sie nicht vor." : "keine davon vor.")
                                    : probleme + " Befunde mit Problem, keiner schlägt " + (eine ? "sie vor." : "eine davon vor."));
                k.Schreibe("   " + satz, ohneParameter > 0 ? "warn" : "good");
                Protokollieren(k, Protokoll.Schritt, KennungMassnahmen, satz,
                    new { freigaben, probleme, ohneParameter, offeneFragen, trocken = k.Trocken });
                return;
            }
            // Auftraege sind nicht Freigaben: die Firewall fuer zwei Profile sind zwei Auftraege
            // aus einer Freigabe.
            k.Schreibe("   " + Zahlwort(auftraege.Count, "Auftrag", "Aufträge") + " aus " + Zahlwort(freigaben.Count, "Freigabe", "Freigaben")
                       + (ohneParameter > 0 ? "; " + Zahlwort(ohneParameter, "weiterer Vorschlag ist", "weitere Vorschläge sind") + " nicht ausführbar (Parameter fehlt am Befund)" : "")
                       + (offeneFragen > 0 ? "; " + Zahlwort(offeneFragen, "Vorschlag wartet", "Vorschläge warten") + " auf Ihre Antwort" : "")
                       + ".", "normal");
            if (k.IstAbgebrochen) return;

            // ---- 3. Ausfuehren, eine nach der anderen ----
            int gestartet = 0;
            int abgelehnt = 0;   // Auftraege, die die Pruefung der Teilmassnahme abgewiesen hat (Ausfuehren = false)
            var sicherungen = new List<string>();
            for (int i = 0; i < auftraege.Count; i++)
            {
                if (k.IstAbgebrochen) return;
                Auftrag a = auftraege[i];
                k.Melde(i + 2, gesamt, a.Titel);
                k.Schreibe("Maßnahme " + (i + 1) + " von " + auftraege.Count + ": " + a.Titel + " (Befund „" + a.BefundTitel + "“)", "header");
                if (!Ausfuehren(k, a))
                {
                    // Ein abgelehnter Auftrag war faellig und lief nicht: fuer den Verlauf ist das
                    // "nicht ausfuehrbar", nicht "nichts war faellig". Der Wert steht nach jedem
                    // Auftrag, damit ein Abbruch danach ihn nicht verliert.
                    abgelehnt++;
                    k.Werte[WertNichtAusfuehrbar] = (ohneParameter + abgelehnt).ToString(CultureInfo.InvariantCulture);
                    continue;
                }
                gestartet++;
                k.Werte[WertMassnahmen] = gestartet.ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(a.Sicherung)) continue;
                sicherungen.Add(a.Sicherung);
                // Die Liste steht nach jedem Auftrag, damit ein Abbruch danach sie nicht verliert.
                k.Werte[WertSicherungen] = string.Join(";", sicherungen);
            }
            if (k.IstAbgebrochen) return;

            // ---- 4. Nachmessen und nachweisen ----
            if (gestartet == 0)
            {
                // Jeder Auftrag wurde abgelehnt (das Problem-Signal steht schon): ohne eine
                // gestartete Massnahme gibt es nichts nachzuweisen, die zweite Messung (etwa
                // 10 Sekunden) entfaellt, und die Zeile ist kein Erfolg.
                k.Schreibe((k.Trocken ? "(trocken) Keine Maßnahme wäre gelaufen (" : "   Keine Maßnahme lief (")
                           + Zahlwort(abgelehnt, "Auftrag", "Aufträge") + " abgelehnt); "
                           + (k.Trocken ? "Nachmessung und Nachweis entfallen." : "die Nachmessung entfällt."), "warn");
                return;
            }
            if (k.Trocken)
            {
                k.Schreibe("(trocken) Nachmessung und Nachweis entfallen; " + Zahlwort(gestartet, "Maßnahme wäre", "Maßnahmen wären") + " gelaufen"
                           + (abgelehnt > 0 ? ", " + Zahlwort(abgelehnt, "Auftrag", "Aufträge") + " abgelehnt" : "") + ".", "dim");
                return;
            }
            k.Melde(gesamt, gesamt, "Der PC wird erneut gemessen");
            k.Schreibe("Der PC wird erneut gemessen, um die Wirkung nachzuweisen", "header");
            Systembild nachher = Messen(k);
            if (nachher == null)
            {
                k.Schreibe("   Ohne zweite Messung gibt es keinen Nachweis; " + Zahlwort(gestartet, "Maßnahme lief", "Maßnahmen liefen") + ", die Wirkung ist offen.", "warn");
                Katalog.Problem(k);
                return;
            }
            int bestanden, geprueft, unbestimmt;
            Nachweisen(k, auftraege, nachher, out bestanden, out geprueft, out unbestimmt);
            // good nur, wenn mindestens ein Nachweis geprueft wurde, jeder bestand und kein Auftrag
            // abgelehnt war; "0 von 0" ist kein Erfolg.
            k.Schreibe("   " + Zahlwort(gestartet, "Maßnahme", "Maßnahmen") + " ausgeführt, " + bestanden + " von " + Zahlwort(geprueft, "Nachweis", "Nachweisen") + " bestanden"
                       + (unbestimmt > 0 ? ", " + Zahlwort(unbestimmt, "Nachweis", "Nachweise") + " nicht messbar" : "")
                       + (abgelehnt > 0 ? ", " + Zahlwort(abgelehnt, "Auftrag", "Aufträge") + " abgelehnt" : "")
                       + (sicherungen.Count > 0 ? ", " + Zahlwort(sicherungen.Count, "Sicherung", "Sicherungen") + " angelegt" : "")
                       + ".", geprueft > 0 && bestanden == geprueft && abgelehnt == 0 ? "good" : "warn");
        }

        /// <summary>
        /// Der Nachweis je gestartetem Auftrag ueber dem neuen Bild, mit den drei Ergebnisarten
        /// (Vertrag 8.1 und 8.6): bestanden; nicht bestanden; unbestimmt (nicht messbar, Zeile
        /// warn, zaehlt als nicht bestanden). Ein Auftrag mit Problem ist immer "Nein", der
        /// Kern-Satz und das Zitat des Helfers bleiben im Satz. Schreibt auto.nachweise, setzt
        /// neustart und das Problem-Signal; die Protokollzeile traegt quelle = "kern".
        /// </summary>
        static void Nachweisen(Ausfuehrungskontext k, List<Auftrag> auftraege, Systembild nachher, out int bestanden, out int geprueft, out int unbestimmt)
        {
            bestanden = 0; geprueft = 0; unbestimmt = 0;
            foreach (Auftrag a in auftraege)
            {
                if (!a.Gestartet) continue;
                Nachweis nw = KernNachweis(a, nachher);
                geprueft++;
                string zeile;
                if (a.Problem)
                {
                    // Lief die Teilmassnahme mit Fehler, ist der Auftrag nicht bestanden, was auch
                    // immer der Kern ueber dem Bild sieht; sein Satz und der des Helfers bleiben
                    // lesbar, damit der Nutzer beide Sichten hat.
                    nw = Nachweis.Nein(a.Titel + " lief mit Fehler; " + nw.Satz + a.HelferZitat);
                    zeile = "   Nachweis " + a.Titel + ": " + nw.Satz;
                }
                else if (nw.Unbestimmt)
                {
                    unbestimmt++;
                    zeile = "   Nachweis nicht messbar (" + a.Titel + "): " + nw.Satz;
                }
                else zeile = "   Nachweis " + a.Titel + ": " + nw.Satz;
                if (nw.Bestanden) bestanden++;
                if (nw.Neustart) k.Werte[WertNeustart] = "1";
                k.Schreibe(zeile, nw.Bestanden ? "good" : "warn");
                Protokollieren(k, Protokoll.Nachweis, a.Kennung, nw.Satz,
                    new { quelle = "kern", bestanden = nw.Bestanden, unbestimmt = nw.Unbestimmt, neustart = nw.Neustart,
                          befunde = a.Befunde, parameter = a.ParameterText, problem = a.Problem, helfer = a.HelferSatz, fehler = a.Fehler });
                // Eine Massnahme, die lief und nichts bewirkt hat oder deren Wirkung sich nicht
                // messen laesst, ist ein Hinweis fuer den Nutzer, kein Erfolg: der Verlauf sagt
                // dann "mit Hinweisen".
                if (!nw.Bestanden) Katalog.Problem(k);
            }
            k.Werte[WertNachweise] = bestanden.ToString(CultureInfo.InvariantCulture) + "/" + geprueft.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Systembild im eigenen Prozess (Sammler), Messzeilen ins Laufprotokoll; null bei einer Ausnahme (Zeile bad).</summary>
        static Systembild Messen(Ausfuehrungskontext k)
        {
            try
            {
                return Messung.Erfassen(text => k.Schreibe("   Messung: " + text, "dim"), k.Protokoll);
            }
            catch (Exception ex)
            {
                AppLog.Error("wartung.massnahmen: Messung", ex);
                k.Schreibe("   Die Messung scheiterte: " + ex.Message, "bad");
                Protokollieren(k, Protokoll.FehlerArt, KennungMassnahmen, "Die Messung scheiterte: " + ex.Message,
                    new { typ = ex.GetType().Name });
                return null;
            }
        }

        /// <summary>
        /// Aus allen Befunden die Auftraege: Zustand warn oder bad, Kennung in den Freigaben, keine
        /// offene Frage, alle Pflichtparameter des Kern-Katalogs vorhanden. In den Auftrag kommen
        /// NUR die Pflichtparameter (Kern.Massnahme.Parameter), nicht alles, was der Befund traegt:
        /// netz.sonde.dns haengt netz.dnscache.leeren mit adapter=<n> an, und die Massnahme kennt
        /// keinen Anschluss. Dieselbe Kennung mit denselben Pflichtparametern laeuft einmal, auch
        /// wenn zwei Befunde sie vorschlagen. Reihenfolge = Reihenfolge der Bereiche und Befunde,
        /// damit zwei Laeufe gleich ablaufen.
        /// </summary>
        static List<Auftrag> Auswaehlen(Ausfuehrungskontext k, List<BereichErgebnis> ergebnisse, Entscheidungen e, List<string> freigaben,
                                        out int ohneParameter, out int offeneFragen)
        {
            ohneParameter = 0;
            offeneFragen = 0;
            var auftraege = new List<Auftrag>();
            var nachSchluessel = new Dictionary<string, Auftrag>(StringComparer.Ordinal);
            foreach (Befund b in Kern.Regeln.Alle.AlleBefunde(ergebnisse))
            {
                if (b == null || !b.IstProblem || b.Massnahmen == null) continue;
                foreach (string kennung in b.Massnahmen)
                {
                    if (string.IsNullOrEmpty(kennung) || !freigaben.Contains(kennung)) continue;
                    // Grundsatz 1: eine offene Frage heisst fragen, nicht handeln. Beantwortet mit
                    // "absicht" waere der Befund nicht mehr warn/bad; beantwortet mit "reparieren"
                    // darf er laufen.
                    if (b.Frage != null && string.IsNullOrEmpty(e.AntwortAuf(b.Frage.Id)))
                    {
                        k.Schreibe("   Übersprungen: „" + b.Titel + "“ wartet auf Ihre Antwort, ohne sie läuft " + kennung + " nicht.", "dim");
                        offeneFragen++;
                        continue;
                    }
                    Kern.Massnahme km = Kern.Massnahmen.Finde(kennung);
                    Massnahme hm = Katalog.Finde(kennung);
                    if (km == null || hm == null) continue;   // nach der Pruefung nicht mehr moeglich
                    Dictionary<string, string> parameter = Pflichtparameter(km, b);
                    string fehlend = FehlenderParameter(km, parameter);
                    if (fehlend != null)
                    {
                        // Eine Regel, die eine Massnahme anhaengt, ohne ihre Parameter zu setzen, ist
                        // ein Fehler im Programm, kein stiller Ausfall: Zeile, Protokoll, Hinweis.
                        k.Schreibe("   Nicht ausführbar: „" + b.Titel + "“ schlägt " + km.Titel + " vor, aber der Parameter " + fehlend + " fehlt am Befund.", "warn");
                        Protokollieren(k, Protokoll.Abgelehnt, kennung, "Parameter " + fehlend + " fehlt am Befund " + b.Schluessel,
                            new { befund = b.Schluessel, parameter = ParameterText(parameter), pflicht = km.Parameter });
                        Katalog.Problem(k);
                        ohneParameter++;
                        continue;
                    }
                    string schluessel = kennung + "|" + ParameterText(parameter);
                    Auftrag a;
                    if (nachSchluessel.TryGetValue(schluessel, out a)) { a.Befunde.Add(b.Schluessel); continue; }
                    a = new Auftrag { Kennung = kennung, KernEintrag = km, HelferEintrag = hm, Parameter = parameter, BefundTitel = b.Titel ?? b.Schluessel };
                    a.Befunde.Add(b.Schluessel);
                    nachSchluessel[schluessel] = a;
                    auftraege.Add(a);
                }
            }
            return auftraege;
        }

        /// <summary>Die Pflichtparameter des Kern-Eintrags, soweit der Befund sie traegt; andere Befund-Parameter bleiben draussen.</summary>
        static Dictionary<string, string> Pflichtparameter(Kern.Massnahme km, Befund b)
        {
            var parameter = new Dictionary<string, string>(StringComparer.Ordinal);
            if (km.Parameter == null || b.Parameter == null) return parameter;
            foreach (string name in km.Parameter)
            {
                string v;
                if (!string.IsNullOrEmpty(name) && b.Parameter.TryGetValue(name, out v) && v != null) parameter[name] = v;
            }
            return parameter;
        }

        /// <summary>Erster fehlender oder leerer Pflichtparameter, sonst null.</summary>
        static string FehlenderParameter(Kern.Massnahme km, Dictionary<string, string> parameter)
        {
            if (km.Parameter == null) return null;
            foreach (string name in km.Parameter)
            {
                if (string.IsNullOrEmpty(name)) continue;
                string v;
                if (!parameter.TryGetValue(name, out v) || string.IsNullOrEmpty(v)) return name;
            }
            return null;
        }

        /// <summary>Parameter als "a=1;b=2" in fester Reihenfolge, fuer den Doppelt-Schluessel und das Protokoll.</summary>
        static string ParameterText(Dictionary<string, string> parameter)
        {
            var namen = new List<string>(parameter.Keys);
            namen.Sort(StringComparer.Ordinal);
            var teile = new List<string>(namen.Count);
            foreach (string n in namen) teile.Add(n + "=" + parameter[n]);
            return string.Join(";", teile);
        }

        /// <summary>
        /// Eine Teilmassnahme: Parameter durch ihre eigene Pruefung (die Planpruefung sah nur
        /// wartung.massnahmen), Konto bei Profilgebunden, dann Ausfuehren mit einem eigenen
        /// PlanSchritt. Ausnahme oder Problem-Schluessel = Problem des ganzen Schritts, die
        /// naechste Massnahme laeuft trotzdem. true = Ausfuehren wurde aufgerufen; false = die
        /// Pruefung oder das Konto lehnte den Auftrag ab (Zeile warn, Protokoll abgelehnt, Problem),
        /// der Aufrufer zaehlt ihn dann in auto.nichtausfuehrbar.
        /// </summary>
        static bool Ausfuehren(Ausfuehrungskontext k, Auftrag a)
        {
            var schritt = new PlanSchritt { Kennung = a.Kennung, Parameter = new Dictionary<string, string>(a.Parameter) };
            string grund = null;
            try { if (a.HelferEintrag.Pruefen != null) grund = a.HelferEintrag.Pruefen(schritt); }
            catch (Exception ex) { grund = "Die Prüfung von „" + a.Kennung + "“ scheiterte: " + ex.Message; }
            if (grund == null && a.HelferEintrag.Profilgebunden && !Ausfuehrung.AufruferIstEigenesKonto(k.AufruferSid))
                grund = Ausfuehrung.ProfilFremdesKonto;
            if (grund != null)
            {
                k.Schreibe("   Nicht ausgeführt: " + grund, "warn");
                Protokollieren(k, Protokoll.Abgelehnt, a.Kennung, "Nicht ausgeführt: " + grund,
                    new { befunde = a.Befunde, parameter = a.ParameterText, grund });
                Katalog.Problem(k);
                return false;
            }

            // Problem- und Neustart-Schluessel gelten je Teilmassnahme: vorher merken und raeumen,
            // nachher lesen, am Ende wieder setzen, wenn eine fruehere sie gesetzt hatte. Alle
            // Werte der Teilmassnahme wandern ins Protokoll (Feld werte der schritt-Zeile); was der
            // Kern-Nachweis davon braucht (sicherung.vorher, neustart), kommt an den Auftrag.
            bool problemVorher = k.Werte.ContainsKey(Katalog.ProblemSchluessel);
            bool neustartVorher = k.Werte.ContainsKey(WertNeustart);
            k.Werte.Remove(Katalog.ProblemSchluessel);
            k.Werte.Remove(WertNeustart);
            var werteVorher = new HashSet<string>(k.Werte.Keys, StringComparer.Ordinal);
            // Der Unterfortschritt gehoert diesem Schritt (Messen, je Auftrag einer, Nachmessen);
            // die eigenen Melde(1, 2)-Aufrufe einer Teilmassnahme wuerden den Balken zuruecksetzen.
            Action<int, int, string> fortschritt = k.Fortschritt;
            k.Fortschritt = null;
            var sw = Stopwatch.StartNew();
            string fehler = null;
            a.Gestartet = true;
            Protokollieren(k, Protokoll.Anfang, a.Kennung, a.Titel + " gestartet (geplante Wartung)",
                new { befunde = a.Befunde, parameter = a.ParameterText, stufe = a.HelferEintrag.Stufe, trocken = k.Trocken });
            try
            {
                a.HelferEintrag.Ausfuehren(schritt, k);
            }
            catch (Exception ex)
            {
                fehler = ex.Message;
                AppLog.Error("wartung.massnahmen: " + a.Kennung, ex);
                k.Schreibe("   Fehler: " + ex.Message, "bad");
            }
            finally
            {
                k.Fortschritt = fortschritt;
            }
            sw.Stop();
            a.Fehler = fehler;
            a.Problem = fehler != null || k.Werte.ContainsKey(Katalog.ProblemSchluessel);
            a.Neustart = Wert(k.Werte, WertNeustart) == "1";

            var werte = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in new List<string>(k.Werte.Keys))
            {
                if (werteVorher.Contains(name) || name == Katalog.ProblemSchluessel) continue;
                werte[name] = k.Werte[name];
                k.Werte.Remove(name);
            }
            if (problemVorher || a.Problem) k.Werte[Katalog.ProblemSchluessel] = "1";
            if (neustartVorher || a.Neustart) k.Werte[WertNeustart] = "1";
            Protokollieren(k, Protokoll.Schritt, a.Kennung, a.Titel + " ist beendet" + (a.Problem ? " (mit Problem)" : ""),
                new { befunde = a.Befunde, parameter = a.ParameterText, sekunden = (int)sw.Elapsed.TotalSeconds, problem = a.Problem, fehler, werte = ParameterText(werte), trocken = k.Trocken });

            // Was der Helfer selbst gesehen hat, bleibt am Auftrag: der Satz fuer das Zitat im
            // Nachweis, die Sicherungsdatei fuer den Rueckweg, und die optionalen Parameter des
            // Kern-Nachweises (Vertrag 8.1: vorher = Folgenummer des juengsten Punkts vor der
            // Massnahme, neustart = die Teilmassnahme verlangt einen Neustart).
            a.HelferSatz = Wert(werte, Nachweise.SatzSchluessel) ?? Wert(werte, "sicherung.satz");
            a.Sicherung = Wert(werte, Sicherungsdatei.DateiSchluessel);
            string vorher = Wert(werte, "sicherung.vorher");
            if (!string.IsNullOrEmpty(vorher)) a.Parameter["vorher"] = vorher;
            if (a.Neustart) a.Parameter[WertNeustart] = "1";
            return true;
        }

        /// <summary>
        /// Nachweis aus dem Kern-Katalog ueber dem neuen Bild. Eine Ausnahme oder ein fehlendes
        /// Ergebnis ist "unbestimmt" (nicht messbar), nie bestanden: der Kern hat dann nichts
        /// gesehen, weder den alten noch den neuen Zustand.
        /// </summary>
        static Nachweis KernNachweis(Auftrag a, Systembild nachher)
        {
            Nachweis nw = null;
            try
            {
                Kern.Massnahme km = a.KernEintrag ?? Kern.Massnahmen.Finde(a.Kennung);
                if (km != null && km.Pruefen != null) nw = km.Pruefen(nachher, a.Parameter);
            }
            catch (Exception ex)
            {
                AppLog.Warn("wartung.massnahmen: Nachweis " + a.Kennung + ": " + ex.Message);
                return Nachweis.Unlesbar("Der Nachweis für „" + a.Titel + "“ scheiterte: " + ex.Message);
            }
            if (nw == null) return Nachweis.Unlesbar("Der Nachweis für „" + a.Titel + "“ lieferte kein Ergebnis.");
            if (string.IsNullOrEmpty(nw.Satz))
                nw.Satz = nw.Unbestimmt ? "Die Wirkung von „" + a.Titel + "“ ließ sich nicht messen."
                        : nw.Bestanden ? "Die Maßnahme „" + a.Titel + "“ hat gewirkt." : "Die Maßnahme „" + a.Titel + "“ hat nicht gewirkt.";
            return nw;
        }

        /// <summary>Zeile im Laufprotokoll (Schicht helfer); ohne Protokoll (direkter Aufruf in einer Probe) nur im Speicher des Kontexts, nie ein Fehler.</summary>
        static void Protokollieren(Ausfuehrungskontext k, string art, string kennung, string laie, object fachmann)
        {
            if (k.Protokoll == null) return;
            try { k.Protokoll.Schreibe(Protokoll.Helfer, art, kennung, laie, fachmann); }
            catch (Exception) { }
        }

        /// <summary>Wert aus einem Woerterbuch oder null.</summary>
        static string Wert(Dictionary<string, string> d, string name)
        {
            string v;
            return d != null && name != null && d.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>"1 Maßnahme", "2 Maßnahmen": Zahl mit dem passenden Wort.</summary>
        static string Zahlwort(int n, string einzahl, string mehrzahl)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? einzahl : mehrzahl);
        }

        static string KurzText(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }
    }
}
