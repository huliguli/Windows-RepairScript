using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Arbeitsspeicher, Auslastung und Autostart. Reine Funktion ueber dem Systembild: kein
    /// Windows-Zugriff, kein DateTime.Now, jede Schwelle aus Schwellen.cs.
    ///
    /// Speicher-Schwellen stammen aus "Troubleshoot performance problems in Windows"
    /// (learn.microsoft.com) und gelten dort fuer Werte, die laenger als eine Minute anhalten.
    /// Deshalb bewertet die Regel nur eine Serie (mindestens MessungenNoetig Messungen im Abstand
    /// von mindestens MessabstandMinMs) und nimmt das guenstigste Ergebnis: das MAXIMUM der
    /// verfuegbaren MB und das MINIMUM des Commit. Warn oder bad faellt nur, wenn alle Messungen
    /// die Schwelle reissen; eine einzelne Spitze ist kein Befund. Weniger oder engere Messungen
    /// sind eine Momentaufnahme ohne Urteil.
    ///
    /// Autostart: gezaehlt wird, was ohne Zutun des Nutzers beim Anmelden oder Starten laeuft und
    /// nicht von Microsoft stammt (geprueftes Signierer-Urteil des Sammlers; Aufgaben nach Autor
    /// und Ordner, eigene Aufgaben des Programms nie). Ein Eintrag, dessen Datei fehlt, startet
    /// nichts und zaehlt nicht. Fehlt eine Quelle laut fehlerliste, ist die Zahl nur eine Unter-
    /// oder Obergrenze, und das Urteil sagt das, statt ok oder warn zu behaupten.
    /// Seit M3 (v8.2): ist die Sammelkarte warn, bekommt jeder gezaehlte Eintrag eine eigene
    /// ok-Karte leistung.autostart.eintrag.&lt;hash&gt; mit der Massnahme und ihren Parametern
    /// (StartupApproved-Schalter, schtasks /DISABLE, Startart Manual), damit der Nutzer einzeln
    /// entscheidet und nicht "alle 20 auf einmal". Die Sammelkarte selbst traegt keine Kennung.
    /// Eine Karte bekommt die Massnahme nur, wenn der Helfer den Parameter annimmt (Wertname,
    /// Dienstname, Aufgabenpfad: die Pruefungen stehen hier, der Helfer ruft dieselben auf) und
    /// der Dienst keine Schutzsoftware ist (Netz.IstAvHersteller ueber Signierer oder Anzeigename,
    /// dazu NenntSchutzprodukt ueber die im Sicherheitscenter registrierten Namen aus
    /// Sicherheit.DrittAv): ein Virenschutz, der nur noch auf Anforderung startet, ist keiner mehr.
    /// </summary>
    public static class Leistung
    {
        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var L = s.Leistung;
            var A = s.Autostart;
            var e = new BereichErgebnis { Bereich = Bereich.Leistung };

            bool speicherDa = L.VerfuegbarMB.Count > 0 || L.RamGesamtKB > 0;
            bool autostartDa = A.Eintraege.Count > 0 || A.Dienste.Count > 0 || A.Aufgaben.Count > 0 || A.Shell != null;
            e.DatenVorhanden = speicherDa || autostartDa;

            if (!speicherDa) e.Fehlend.Add("Arbeitsspeicher (weder Leistungszähler noch WMI lieferten Werte)");
            else if (L.VerfuegbarMB.Count == 0) e.Fehlend.Add("Speicherauslastung (Leistungszähler und WMI-Rückfall scheiterten)");
            // Ein Fehlereintrag zaehlt auch bei Teildaten (Aufgaben nach 10 s abgebrochen, StartupApproved
            // nicht lesbar): die Zaehl-Regel liest dieselben Eintraege und nennt die Luecke im Befund.
            foreach (var q in Autostartquellen) Fehlt(e, s, q.Key, q.Value);
            Fehlt(e, s, "registry.winlogon", "Anmelde-Einstellungen (Winlogon)");
            Fehlt(e, s, "registry.appinit", "AppInit_DLLs");
            if (!e.DatenVorhanden) return e;

            if (speicherDa) Speicher(ctx, e);
            if (autostartDa)
            {
                Autostarts(ctx, e);
                Winlogon(ctx, e);
                AppInit(ctx, e);
            }
            return e;
        }

        /// <summary>Quellen der Autostart-Zaehlung und ihr Name in Alltagssprache; StartupApproved liefert nur den Deaktiviert-Status.</summary>
        static readonly KeyValuePair<string, string>[] Autostartquellen =
        {
            new KeyValuePair<string, string>("registry.autostart.run", "Startprogramme aus der Registrierung (Run)"),
            new KeyValuePair<string, string>("ordner.autostart", "Autostart-Ordner"),
            new KeyValuePair<string, string>("com.aufgaben", "geplante Aufgaben"),
            new KeyValuePair<string, string>("wmi.dienste", "Dienste"),
            new KeyValuePair<string, string>("registry.autostart.startupapproved", "Deaktiviert-Status der Startprogramme (StartupApproved)"),
        };

        static string FehlerGrund(Fehler f)
        {
            return f == null ? "" : f.Art == Fehler.Zugriff ? " (keine Rechte)" : f.Art == Fehler.Zeit ? " (Zeit überschritten)" : "";
        }

        static void Fehlt(BereichErgebnis e, Systembild s, string quelle, string was)
        {
            var f = s.FehlerVon(quelle).FirstOrDefault();
            if (f != null) e.Fehlend.Add(was + FehlerGrund(f));
        }

        // ---------------------------------------------------------------- Speicher

        static void Speicher(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            var L = s.Leistung;
            long gesamtKB = L.RamGesamtKB > 0 ? L.RamGesamtKB : s.Hardware.RamGesamtKB;
            long gesamtMB = gesamtKB / 1024;
            var prozesse = TopProzesse(L);
            int n = L.VerfuegbarMB.Count;
            bool wmi = L.SpeicherQuelle == "wmi";
            string quelleFrei = wmi ? "Win32_PerfFormattedData_PerfOS_Memory.AvailableMBytes" : "Memory\\Available MBytes";
            string quelleCommit = wmi ? "Win32_PerfFormattedData_PerfOS_Memory.PercentCommittedBytesInUse" : "Memory\\% Committed Bytes In Use";
            string abstand = L.AbstandMs > 0 ? "im Abstand von " + (L.AbstandMs / 1000.0).ToString("0.#", Text.De) + " s" : "ohne bekannten Abstand";
            // Eine Serie braucht genug Messungen UND genug Abstand: drei Werte in zwei Sekunden sind
            // dieselbe Momentaufnahme dreimal (Doku: Werte muessen laenger als eine Minute anhalten).
            bool serie = n >= Schwellen.MessungenNoetig && L.AbstandMs >= Schwellen.MessabstandMinMs;

            if (!serie || gesamtMB <= 0)
            {
                // Momentaufnahme: Zahlen zeigen, nichts bewerten (unknown zaehlt nicht mit).
                var detail = new List<string>();
                detail.Add("Momentaufnahme: " + n + " von " + Schwellen.MessungenNoetig + " Messungen " + abstand + " (Serie ab " + Schwellen.MessungenNoetig + " Messungen im Abstand von " + (Schwellen.MessabstandMinMs / 1000) + " s)" + (n > 0 ? ", verfügbar " + string.Join(" / ", L.VerfuegbarMB) + " MB" : "") + (L.CommitPct.Count > 0 ? ", zugesagt " + string.Join(" / ", L.CommitPct) + " %" : ""));
                if (gesamtMB > 0) detail.Add("Arbeitsspeicher gesamt: " + Text.Gb1(gesamtKB * 1024) + ", frei laut Betriebssystem " + Text.Gb1(L.RamFreiKB * 1024));
                if (L.AuslagerungMB.HasValue) detail.Add("Auslagerungsdatei: " + L.AuslagerungMB.Value + " MB");
                detail.AddRange(prozesse);
                string grund = n < Schwellen.MessungenNoetig
                    ? "Nur " + n + " statt " + Schwellen.MessungenNoetig + " Messungen"
                    : (gesamtMB <= 0 ? "Die Größe des Arbeitsspeichers ließ sich nicht lesen"
                                     : n + " Messungen " + abstand + " statt " + (Schwellen.MessabstandMinMs / 1000) + " s");
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Leistung,
                    Schluessel = "leistung.speicher.momentaufnahme",
                    Zustand = Zustand.Unknown,
                    Messwert = Messwert.Von(n > 0 ? (object)L.VerfuegbarMB[0] : null, "MB verfügbar (Momentaufnahme)", null),
                    Quelle = quelleFrei,
                    Titel = "Arbeitsspeicher (nicht bewertet)",
                    Satz = grund + ": " + (n > 0 ? L.VerfuegbarMB[0] + " MB frei sind" : "der Wert ist") + " eine Momentaufnahme, kein Urteil.",
                    Rat = null,
                    Detail = detail,
                });
                return;
            }

            int maxFrei = L.VerfuegbarMB.Max(), minFrei = L.VerfuegbarMB.Min();
            double maxPct = maxFrei * 100.0 / gesamtMB, minPct = minFrei * 100.0 / gesamtMB;
            string zustand;
            if (maxPct < Schwellen.VerfuegbarBadPct || maxFrei < Schwellen.VerfuegbarBadMb) zustand = Zustand.Bad;
            else if (maxPct < Schwellen.VerfuegbarWarnPct) zustand = Zustand.Warn;
            else zustand = Zustand.Ok;
            // Reicht schon die schlechteste Messung, darf der Satz "mindestens" sagen; sonst war es eine Spitze.
            bool minReicht = minPct >= Schwellen.VerfuegbarWarnPct && minFrei >= Schwellen.VerfuegbarBadMb;

            var det = new List<string>
            {
                "Verfügbar (" + (wmi ? "AvailableMBytes per WMI" : "Available MBytes") + "), " + n + " Messungen " + abstand + ": " + string.Join(" / ", L.VerfuegbarMB) + " MB, günstigste " + maxFrei + " MB = " + Text.Pct(maxPct) + " von " + gesamtMB + " MB",
                "Schwellen (Microsoft-Doku, gelten für anhaltende Werte, deshalb zählt die günstigste Messung): unter " + Text.Pct(Schwellen.VerfuegbarWarnPct) + " Warnung, unter " + Text.Pct(Schwellen.VerfuegbarBadPct) + " oder unter " + Schwellen.VerfuegbarBadMb + " MB kritisch",
            };
            if (L.AuslagerungMB.HasValue) det.Add("Auslagerungsdatei: " + L.AuslagerungMB.Value + " MB");
            det.AddRange(prozesse);

            string gesamt = Text.Gb1(gesamtKB * 1024);
            string satz;
            switch (zustand)
            {
                case Zustand.Bad:
                    satz = "Von " + gesamt + " Arbeitsspeicher waren in allen " + n + " Messungen höchstens " + maxFrei + " MB frei (" + Text.Pct(maxPct) + "), das ist zu wenig.";
                    break;
                case Zustand.Warn:
                    satz = "Von " + gesamt + " Arbeitsspeicher waren in allen " + n + " Messungen höchstens " + Text.Gb1((long)maxFrei * 1048576) + " frei (" + Text.Pct(maxPct) + "), unter " + Text.Pct(Schwellen.VerfuegbarWarnPct) + " wird es eng.";
                    break;
                default:
                    satz = minReicht
                        ? "Von " + gesamt + " Arbeitsspeicher sind mindestens " + Text.Gb1((long)minFrei * 1048576) + " frei (" + Text.Pct(minPct) + "), das reicht."
                        : "Von " + gesamt + " Arbeitsspeicher waren zeitweise nur " + minFrei + " MB frei (" + Text.Pct(minPct) + "), in der günstigsten von " + n + " Messungen aber " + Text.Gb1((long)maxFrei * 1048576) + " (" + Text.Pct(maxPct) + "): eine einzelne Spitze ist kein Befund.";
                    break;
            }
            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Leistung,
                Schluessel = "leistung.speicher.verfuegbar",
                Zustand = zustand,
                Messwert = Messwert.Von(maxFrei, "MB verfügbar (günstigste von " + n + " Messungen)", "warn < " + Schwellen.VerfuegbarWarnPct + " %, bad < " + Schwellen.VerfuegbarBadPct + " % oder < " + Schwellen.VerfuegbarBadMb + " MB"),
                Quelle = quelleFrei,
                Titel = "Freier Arbeitsspeicher",
                Satz = satz,
                Rat = zustand == Zustand.Ok ? null : "Programme schließen, die viel Speicher belegen (Liste in den Einzelheiten); reicht das dauerhaft nicht, mehr Arbeitsspeicher einbauen.",
                Detail = det,
            });

            if (L.CommitPct.Count >= Schwellen.MessungenNoetig)
            {
                int minCommit = L.CommitPct.Min(), maxCommit = L.CommitPct.Max();
                int m = L.CommitPct.Count;
                string zc = minCommit >= Schwellen.CommitBadPct ? Zustand.Bad : (minCommit >= Schwellen.CommitWarnPct ? Zustand.Warn : Zustand.Ok);
                string sc;
                if (zc != Zustand.Ok)
                    sc = "Der zugesagte Speicher war in allen " + m + " Messungen zu mindestens " + Text.Pct(minCommit) + " belegt" + (zc == Zustand.Bad ? ", ab " + Schwellen.CommitBadPct + " % droht Windows der Speicher auszugehen." : ", ab " + Schwellen.CommitWarnPct + " % wird es eng.");
                else if (maxCommit < Schwellen.CommitWarnPct)
                    sc = "Der zugesagte Speicher ist in allen " + m + " Messungen zu höchstens " + Text.Pct(maxCommit) + " belegt, unter " + Schwellen.CommitWarnPct + " % ist das unauffällig.";
                else
                    sc = "Der zugesagte Speicher war zeitweise zu " + Text.Pct(maxCommit) + " belegt, in der günstigsten von " + m + " Messungen aber nur zu " + Text.Pct(minCommit) + "; unter " + Schwellen.CommitWarnPct + " % ist das unauffällig, eine einzelne Spitze kein Befund.";
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Leistung,
                    Schluessel = "leistung.speicher.commit",
                    Zustand = zc,
                    Messwert = Messwert.Von(minCommit, "% zugesagter Speicher (günstigste von " + m + " Messungen)", "warn >= " + Schwellen.CommitWarnPct + " %, bad >= " + Schwellen.CommitBadPct + " %"),
                    Quelle = quelleCommit,
                    Titel = "Zugesagter Speicher (Arbeitsspeicher plus Auslagerungsdatei)",
                    Satz = sc,
                    Rat = zc == Zustand.Ok ? null : "Speicherhungrige Programme schließen; wenn die Auslagerungsdatei klein oder abgeschaltet ist, sie von Windows verwalten lassen.",
                    Detail = new List<string>
                    {
                        (wmi ? "PercentCommittedBytesInUse per WMI" : "% Committed Bytes In Use") + ", " + m + " Messungen " + abstand + ": " + string.Join(" / ", L.CommitPct) + " %, günstigste " + minCommit + " %",
                        "Schwellen (Microsoft-Doku, gelten für anhaltende Werte, deshalb zählt die günstigste Messung): " + Schwellen.CommitWarnPct + " bis " + Schwellen.CommitBadPct + " % Warnung, über " + Schwellen.CommitBadPct + " % kritisch",
                        "Auslagerungsdatei: " + (L.AuslagerungMB.HasValue ? L.AuslagerungMB.Value + " MB" : "unbekannt"),
                    },
                });
            }
        }

        static List<string> TopProzesse(Kern.Leistung L)
        {
            var liste = new List<string>();
            if (L.Prozesse.Count == 0) return liste;
            liste.Add("Prozesse mit dem meisten Arbeitsspeicher (CPU-Anteil über alle Kerne):");
            foreach (var p in L.Prozesse.OrderByDescending(p => p.ArbeitsspeicherBytes).Take(10))
                liste.Add("  " + p.Name + " (PID " + p.Pid + "): " + Text.Mb(p.ArbeitsspeicherBytes) + ", CPU " + p.CpuPct.ToString("N1", Text.De) + " %" + (p.Pfad != null ? ", " + p.Pfad : ""));
            return liste;
        }

        // ---------------------------------------------------------------- Autostart

        // Kennungen aus kern/Massnahmen.cs (docs/M3-ENTWURF.md, Abschnitt 1 und 8.5). Die Sammelkarte
        // leistung.autostart.anzahl traegt keine Kennung (ihre Detailzeile verweist auf die
        // Einzelkarten); ausfuehrbar sind die drei Autostart-Kennungen je Eintrag auf einer eigenen
        // Karte und seit M4 leistung.winlogon.zuruecksetzen (Stufe 2, docs/M4-ENTWURF.md, Abschnitt 2.4)
        // an den Winlogon-Befunden; der Helfer setzt genau die Werte, die WinlogonShellStandard und
        // WinlogonUserinitStandard verlangen.
        public const string MAutostartDeaktivieren = "leistung.autostart.deaktivieren";
        public const string MAufgabeDeaktivieren = "leistung.aufgabe.deaktivieren";
        public const string MDienstStartart = "leistung.dienst.startart";
        public const string MWinlogonZuruecksetzen = "leistung.winlogon.zuruecksetzen";

        /// <summary>
        /// Planparameter der drei Autostart-Massnahmen (docs/M3-ENTWURF.md, Abschnitt 1):
        /// Startprogramm hive (HKCU|HKLM), ort (Run|Run32|StartupFolder), name (Wertname im
        /// StartupApproved-Schluessel = Registry-Wertname bzw. Dateiname im Autostart-Ordner);
        /// Aufgabe pfad (Aufgabenplanung, z. B. \Fremd\Update); Dienst name (Win32_Service.Name)
        /// und startart (Zielwert, hier immer Manual).
        /// </summary>
        public const string ParamHive = "hive", ParamOrt = "ort", ParamName = "name", ParamPfad = "pfad", ParamStartart = "startart";
        public const string StartartManuell = "Manual";

        /// <summary>Schluessel-Praefix der Einzelkarten: leistung.autostart.eintrag.&lt;12 Hex aus Quelle und Name&gt;.</summary>
        public const string SchluesselEintrag = "leistung.autostart.eintrag.";

        static void Autostarts(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            var A = s.Autostart;
            var liste = new List<string>();
            var verwaist = new List<string>();
            var eigene = new List<string>();
            // Dieselben Eintraege noch einmal als Objekte: aus ihnen entstehen bei warn die Einzelkarten.
            var fremdEintraege = new List<AutostartEintrag>();
            var fremdAufgaben = new List<Aufgabe>();
            var fremdDienste = new List<Dienst>();

            // Startprogramme: Aktiviert != false (null = kein StartupApproved-Eintrag = aktiv), mit Befehl (ein
            // leerer Run-Wert, gemessen "GalaxyClient" = "", startet nichts), Datei vorhanden, nicht von Microsoft.
            foreach (var x in A.Eintraege.Where(x => x.Aktiviert != false && !string.IsNullOrWhiteSpace(x.Befehl)))
            {
                string zeile = x.Quelle + ": " + x.Name + " = " + x.Befehl;
                if (x.DateiVorhanden == false) { verwaist.Add(zeile); continue; }
                if (EintragFremd(x)) { liste.Add(zeile + " (" + Herkunft(x) + ")"); fremdEintraege.Add(x); }
            }

            // Aufgaben mit Anmelde- oder Start-Ausloeser, nicht deaktiviert, nicht von Microsoft, nicht die eigenen.
            foreach (var t in A.Aufgaben.Where(t => (t.Logon || t.Boot) && AufgabeAktiv(t)))
            {
                if (t.Eigen) { eigene.Add(t.Pfad); continue; }
                if (!AufgabeMicrosoft(t)) { liste.Add("Aufgabe " + t.Pfad + " (" + (t.Autor ?? "ohne Autor") + ", " + (t.Logon ? "bei Anmeldung" : "beim Start") + ")"); fremdAufgaben.Add(t); }
            }

            // Dienste mit Startart Auto, deren Binaerdatei nicht von Microsoft signiert ist (null = nicht pruefbar, zaehlt nicht).
            foreach (var d in A.Dienste.Where(d => string.Equals(d.Startart, "Auto", StringComparison.OrdinalIgnoreCase) && d.Microsoft == false))
            {
                liste.Add("Dienst " + d.Name + " (" + Herausgeber(d.Signierer) + (d.Verzoegert ? ", verzögert" : "") + ")");
                fremdDienste.Add(d);
            }

            int n = liste.Count;
            bool warn = n > Schwellen.AutostartsWarn;

            // Luecken laut fehlerliste: fehlt eine Zaehlquelle, ist n eine Untergrenze (warn bleibt, ok nicht);
            // fehlt der Deaktiviert-Status, ist n eine Obergrenze (ok bleibt, warn nicht).
            var luecken = new List<string>();
            bool untergrenze = false, obergrenze = false;
            foreach (var q in Autostartquellen)
            {
                var f = s.FehlerVon(q.Key).FirstOrDefault();
                if (f == null) continue;
                luecken.Add(q.Value + FehlerGrund(f));
                if (q.Key == "registry.autostart.startupapproved") obergrenze = true; else untergrenze = true;
            }
            string zustand = warn ? (obergrenze ? Zustand.Unknown : Zustand.Warn) : (untergrenze ? Zustand.Unknown : Zustand.Ok);

            // Einzelkarten nur bei warn: ohne Urteil (unknown) gibt es nichts abzuschalten, und bei ok
            // stuende neben einem "unauffaellig" ein Dutzend Schalter. Die im Sicherheitscenter
            // registrierten Schutzprogramme entscheiden mit, welcher Dienst keinen Schalter bekommt.
            var drittAv = s.Sicherheit == null ? new List<string>() : s.Sicherheit.DrittAv;
            var einzel = zustand == Zustand.Warn ? Einzelbefunde(fremdEintraege, fremdAufgaben, fremdDienste, drittAv) : new List<Befund>();

            var detail = new List<string>(liste);
            if (luecken.Count > 0) detail.Add("Nicht lesbar: " + string.Join(", ", luecken) + " (die Zahl ist deshalb " + (obergrenze && untergrenze ? "weder Unter- noch Obergrenze" : obergrenze ? "eine Obergrenze" : "eine Untergrenze") + ")");
            if (!A.AufgabenVollstaendig) detail.Add("Aufgaben ohne Administratorrechte gelesen: rund ein Viertel ist dann nicht sichtbar.");
            int deaktiviert = A.Eintraege.Count(x => x.Aktiviert == false);
            if (deaktiviert > 0) detail.Add("Bereits deaktivierte Startprogramme (Task-Manager): " + deaktiviert);
            if (verwaist.Count > 0) detail.Add("Einträge, deren Datei nicht gefunden wurde (starten nichts, Rest einer Deinstallation; nicht gezählt): " + string.Join("; ", verwaist));
            if (eigene.Count > 0) detail.Add("Eigene Aufgaben dieses Programms (nicht gezählt): " + string.Join(", ", eigene));
            if (einzel.Count > 0)
            {
                int mitSchalter = einzel.Count(k => k.Massnahmen.Count > 0);
                detail.Add("Je Eintrag folgt eine eigene Karte (" + einzel.Count + " von " + n + "), davon " + mitSchalter + " mit dem Schalter zum Abschalten"
                           + (mitSchalter < einzel.Count ? " und " + (einzel.Count - mitSchalter) + " nur mit Rat (Schutzsoftware oder ein Name, den das Programm nicht weitergibt)" : "")
                           + "; Einträge unter RunOnce laufen nur einmal und haben keinen Schalter.");
            }

            string was = " Programme, Aufgaben und Dienste von Fremdherstellern starten automatisch mit Windows";
            string nichtLesbar = "nicht lesbar: " + string.Join(", ", luecken);
            string satz;
            if (zustand == Zustand.Warn)
                satz = (untergrenze ? "Mindestens " : "") + n + was + ", mehr als " + Schwellen.AutostartsWarn + " bremsen den Start und laufen dauerhaft mit" + (untergrenze ? "; " + nichtLesbar + ", die Zahl ist eine Untergrenze." : ".");
            else if (zustand == Zustand.Ok)
                satz = n + was + ", bis " + Schwellen.AutostartsWarn + " ist das unauffällig.";
            else if (warn)
                satz = "Bis zu " + n + was + "; " + nichtLesbar + ", ohne diesen Status gibt es kein Urteil.";
            else
                satz = "Mindestens " + n + was + "; " + nichtLesbar + ", also kein Urteil, ob es bei bis zu " + Schwellen.AutostartsWarn + " bleibt.";

            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Leistung,
                Schluessel = "leistung.autostart.anzahl",
                Zustand = zustand,
                Messwert = Messwert.Von(n, "aktive Fremd-Autostarts" + (untergrenze ? " (Untergrenze)" : obergrenze ? " (Obergrenze)" : ""), "> " + Schwellen.AutostartsWarn + " warn"),
                Quelle = "Run, Startup-Ordner, StartupApproved, Schedule.Service, Win32_Service, Signierer",
                Titel = "Programme, die mit Windows starten",
                Satz = satz,
                Rat = zustand == Zustand.Warn ? "Startprogramme, die nicht täglich gebraucht werden, deaktivieren (im Task-Manager unter Autostart oder hier nach Vorschau); sie lassen sich jederzeit wieder einschalten." : null,
                Detail = detail,
                // Keine Kennung an der Sammelkarte: der Schalter haengt an den Einzelkarten (M3-Entwurf 8.5).
                Massnahmen = new List<string>(),
            });
            e.Befunde.AddRange(einzel);
        }

        /// <summary>
        /// Je gezaehltem Eintrag eine eigene Karte (Zustand ok: der Eintrag ist kein Fehler, nur ein
        /// Kandidat) mit der passenden Massnahme und ihren Parametern. Startprogramme bekommen den
        /// Task-Manager-Schalter (StartupApproved), Aufgaben schtasks /DISABLE, Dienste die Startart
        /// Manual. RunOnce-Eintraege haben keinen Schalter und bekommen keine Karte; ein Eintrag, dessen
        /// Name, Pfad oder Dienstname der Helfer ablehnen wuerde (WertnameZulaessig, AufgabenpfadZulaessig,
        /// DienstNameZulaessig), und ein Dienst einer Schutzsoftware (Herstellerliste oder ein im
        /// Sicherheitscenter registriertes Produkt, drittAv) bekommen die Karte ohne Massnahme,
        /// mit dem Rat, was stattdessen gilt.
        /// </summary>
        static List<Befund> Einzelbefunde(List<AutostartEintrag> eintraege, List<Aufgabe> aufgaben, List<Dienst> dienste, IList<string> drittAv)
        {
            var l = new List<Befund>();
            foreach (var x in eintraege)
            {
                var b = EintragBefund(x);
                if (b != null) l.Add(b);
            }
            foreach (var t in aufgaben)
            {
                var b = AufgabeBefund(t);
                if (b != null) l.Add(b);
            }
            foreach (var d in dienste)
            {
                var b = DienstBefund(d, drittAv);
                if (b != null) l.Add(b);
            }
            return l;
        }

        static Befund EintragBefund(AutostartEintrag x)
        {
            string hive, ort;
            if (string.IsNullOrEmpty(x.Name) || !StartupApprovedOrt(x.Quelle, out hive, out ort)) return null;
            string herkunft = Herkunft(x);
            // Der Wertname geht als Parameter in den Plan; was der Helfer ablehnt (WertnameZulaessig),
            // bekommt keine Massnahme, damit die Karte nichts verspricht, was mit Exit 2 endet.
            bool zulaessig = WertnameZulaessig(x.Name);
            var b = new Befund
            {
                Bereich = Bereich.Leistung,
                Schluessel = SchluesselEintrag + Hash12("eintrag|" + x.Quelle + "|" + x.Name),
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von("aktiv", "Zustand im Task-Manager (Autostart)", null),
                Quelle = "StartupApproved\\" + ort + " (" + x.Quelle + ")",
                Titel = "Startprogramm „" + x.Name + "“",
                Satz = "„" + x.Name + "“ (" + herkunft + ") startet bei jeder Anmeldung aus " + OrtSatz(x.Quelle) + " mit; wird es nicht täglich gebraucht, lässt es sich "
                       + (zulaessig ? "hier abschalten und im Task-Manager jederzeit wieder einschalten." : "nur im Task-Manager unter Autostart von Hand abschalten."),
                Rat = zulaessig ? null : "Der Name des Eintrags ist länger als 255 Zeichen oder enthält ein Semikolon oder Steuerzeichen, die das Programm nicht weitergibt; im Task-Manager unter Autostart von Hand abschalten.",
            };
            b.Detail.Add("Befehl: " + x.Befehl);
            b.Detail.Add("Herausgeber: " + herkunft + (string.IsNullOrEmpty(x.Signierer) ? "" : " (" + x.Signierer + ")"));
            b.Detail.Add("Schalter: " + hive + "\\...\\Explorer\\StartupApproved\\" + ort + ", Wert „" + x.Name + "“ (Byte 0: 2 aktiv, 3 deaktiviert); der Eintrag selbst bleibt stehen.");
            if (hive == "HKCU") b.Detail.Add("Der Schalter liegt im Profil des angemeldeten Kontos (HKCU): im Dialog von Windows muss Ihr eigenes Konto stehen.");
            if (!zulaessig) return b;
            b.Massnahmen.Add(MAutostartDeaktivieren);
            b.Parameter[ParamHive] = hive;
            b.Parameter[ParamOrt] = ort;
            b.Parameter[ParamName] = x.Name;
            return b;
        }

        static Befund AufgabeBefund(Aufgabe t)
        {
            if (string.IsNullOrWhiteSpace(t.Pfad)) return null;
            string name = t.Pfad.TrimEnd('\\');
            int i = name.LastIndexOf('\\');
            if (i >= 0) name = name.Substring(i + 1);
            bool zulaessig = AufgabenpfadZulaessig(t.Pfad);
            string ausloeser = t.Logon && t.Boot ? "bei jeder Anmeldung und bei jedem Start" : t.Logon ? "bei jeder Anmeldung" : "bei jedem Start";
            var b = new Befund
            {
                Bereich = Bereich.Leistung,
                Schluessel = SchluesselEintrag + Hash12("aufgabe|" + t.Pfad),
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von(t.Zustand, "TASK_STATE (1 deaktiviert, 3 bereit, 4 läuft)", null),
                Quelle = "Schedule.Service " + t.Pfad,
                Titel = "Aufgabe „" + name + "“",
                Satz = "Die geplante Aufgabe „" + t.Pfad + "“ (" + (t.Autor ?? "ohne Autor") + ") läuft " + ausloeser + "; wird sie nicht gebraucht, lässt sie sich "
                       + (zulaessig ? "hier deaktivieren und in der Aufgabenplanung wieder einschalten." : "nur in der Aufgabenplanung von Hand deaktivieren."),
                Rat = zulaessig ? null : "Der Aufgabenname enthält Zeichen, die das Programm nicht an die Aufgabenplanung weitergibt; dort von Hand deaktivieren.",
            };
            b.Detail.Add("Pfad in der Aufgabenplanung: " + t.Pfad);
            b.Detail.Add("Auslöser: " + (t.Logon ? "Anmeldung" : "") + (t.Logon && t.Boot ? ", " : "") + (t.Boot ? "Systemstart" : "") + "; Zustand " + t.Zustand + (t.Aktiv.HasValue ? ", Enabled " + (t.Aktiv.Value ? "true" : "false") : ""));
            if (t.AktionPfad != null) b.Detail.Add("Aktion: " + t.AktionPfad + (t.Microsoft == true ? " (Microsoft-signiert)" : t.Microsoft == false ? " (nicht von Microsoft signiert)" : ""));
            if (t.LetzterLaufUtc != null) b.Detail.Add("Letzter Lauf: " + t.LetzterLaufUtc + (t.LetztesErgebnis.HasValue ? ", Ergebnis 0x" + t.LetztesErgebnis.Value.ToString("X", CultureInfo.InvariantCulture) : ""));
            if (zulaessig)
            {
                b.Detail.Add("Schalter: schtasks /Change /DISABLE; der Rückweg ist /ENABLE über die Sicherung.");
                b.Massnahmen.Add(MAufgabeDeaktivieren);
                b.Parameter[ParamPfad] = t.Pfad;
            }
            else b.Detail.Add("Erlaubt sind Buchstaben, Ziffern, Leerzeichen und die Zeichen _ . - \\ ( ) { } im Pfad; dieser Pfad enthält andere.");
            return b;
        }

        static Befund DienstBefund(Dienst d, IList<string> drittAv)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) return null;
            string anzeige = string.IsNullOrWhiteSpace(d.Anzeige) ? d.Name : d.Anzeige.Trim();
            // Zwei Gruende gegen die Massnahme: der Helfer nimmt den Namen nicht an (DienstNameZulaessig,
            // dieselbe Pruefung dort), oder der Dienst gehoert zu einer Schutzsoftware. Die erkennt die
            // Regel zweifach: am Signierer oder Anzeigenamen mit einem bekannten Hersteller
            // (Netz.IstAvHersteller) und an einem Wort eines im Sicherheitscenter registrierten
            // Schutzprogramms (NenntSchutzprodukt ueber Sicherheit.DrittAv, faengt Hersteller ab, die
            // in der Liste fehlen). Ein Virenschutz, der nur noch auf Anforderung startet, schuetzt
            // nicht; der bekommt nur den Rat, das Programm zu deinstallieren.
            bool zulaessig = DienstNameZulaessig(d.Name);
            string erkannt = null;
            string wort, produkt;
            if (Netz.IstAvHersteller(d.Signierer)) erkannt = "an Signierer „" + d.Signierer + "“";
            else if (Netz.IstAvHersteller(anzeige)) erkannt = "an Anzeigename „" + anzeige + "“";
            else if (NenntSchutzprodukt(d.Signierer, drittAv, out wort, out produkt)) erkannt = "am Wort „" + wort + "“ des im Sicherheitscenter registrierten Schutzprogramms „" + produkt + "“ im Signierer „" + d.Signierer + "“";
            else if (NenntSchutzprodukt(anzeige, drittAv, out wort, out produkt)) erkannt = "am Wort „" + wort + "“ des im Sicherheitscenter registrierten Schutzprogramms „" + produkt + "“ im Anzeigenamen „" + anzeige + "“";
            bool schutz = erkannt != null;
            bool massnahme = zulaessig && !schutz;
            var b = new Befund
            {
                Bereich = Bereich.Leistung,
                Schluessel = SchluesselEintrag + Hash12("dienst|" + d.Name),
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von(d.Startart + (d.Verzoegert ? " (verzögert)" : ""), "Win32_Service.StartMode", null),
                Quelle = "Win32_Service " + d.Name,
                Titel = "Dienst „" + anzeige + "“",
                Satz = "Der Dienst „" + anzeige + "“ (" + Herausgeber(d.Signierer) + ") startet automatisch mit Windows" + (d.Verzoegert ? ", verzögert" : "")
                       + (massnahme ? "; wird das zugehörige Programm nicht dauernd gebraucht, lässt sich die Startart hier auf „Manuell“ stellen, dann startet er nur auf Anforderung."
                          : schutz ? "; er gehört zu einer Schutzsoftware, die beim Start mitlaufen muss, deshalb bietet das Programm hier keinen Schalter an."
                          : "; sein Dienstname enthält Zeichen, die das Programm nicht an die Dienststeuerung weitergibt, deshalb gibt es hier keinen Schalter."),
                Rat = massnahme ? null
                    : schutz ? "Den Dienst nicht abschalten, solange die Schutzsoftware gebraucht wird; wer sie nicht mehr braucht, deinstalliert das Programm über die Windows-Einstellungen."
                    : "Die Startart nur in der Diensteverwaltung (services.msc) von Hand ändern; der Name enthält einen Backslash, Schrägstrich, ein Anführungszeichen, ein Semikolon oder Steuerzeichen oder ist länger als 256 Zeichen.",
            };
            b.Detail.Add("Dienstname: " + d.Name + (string.IsNullOrEmpty(d.Pfad) ? "" : ", Datei: " + d.Pfad));
            b.Detail.Add("Startart: " + d.Startart + (d.Verzoegert ? " (verzögerter Start)" : "") + ", Zustand: " + (d.Zustand ?? "unbekannt"));
            if (schutz) b.Detail.Add("Schutzsoftware erkannt " + erkannt + "; keine Maßnahme, die Startart bleibt Automatisch.");
            if (!massnahme) return b;
            b.Detail.Add("Schalter: Win32_Service.ChangeStartMode(Manual); der Rückweg stellt die alte Startart aus der Sicherung wieder her.");
            b.Massnahmen.Add(MDienstStartart);
            b.Parameter[ParamName] = d.Name;
            b.Parameter[ParamStartart] = StartartManuell;
            return b;
        }

        /// <summary>
        /// Quelle des Sammlers (Autostartquelle.cs) -> Hive und Ort des StartupApproved-Schluessels,
        /// in dem der Task-Manager den Schalter fuehrt. RunOnce hat dort keinen Schluessel (laeuft
        /// einmal und loescht sich), eine unbekannte Quelle ebenso wenig: dann false.
        /// </summary>
        public static bool StartupApprovedOrt(string quelle, out string hive, out string ort)
        {
            hive = null; ort = null;
            if (string.IsNullOrEmpty(quelle)) return false;
            switch (quelle.ToUpperInvariant())
            {
                case "HKLM\\RUN": hive = "HKLM"; ort = "Run"; return true;
                case "HKLM\\RUN32": hive = "HKLM"; ort = "Run32"; return true;
                case "HKCU\\RUN": hive = "HKCU"; ort = "Run"; return true;
                case "HKCU\\RUN32": hive = "HKCU"; ort = "Run32"; return true;
                case "STARTUP": hive = "HKCU"; ort = "StartupFolder"; return true;
                case "COMMONSTARTUP": hive = "HKLM"; ort = "StartupFolder"; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Dienstname (Win32_Service.Name), wie ihn der Helfer fuer leistung.dienst.startart annimmt
        /// (docs/M3-ENTWURF.md, Abschnitt 8.5; der Helfer ruft diese Funktion, nicht eine Kopie):
        /// 1 bis 256 Zeichen, verboten sind nur Backslash, Schraegstrich, Apostroph, Anfuehrungszeichen,
        /// Semikolon und Steuerzeichen. Das Dollarzeichen ist erlaubt (MSSQL$SQLEXPRESS); die WQL-
        /// Bedingung maskiert der Helfer selbst. Reine Funktion, kein Windows-Zugriff.
        /// </summary>
        public static bool DienstNameZulaessig(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 256) return false;
            foreach (char c in name)
                if (char.IsControl(c) || c == '\\' || c == '/' || c == '\'' || c == '"' || c == ';') return false;
            return true;
        }

        /// <summary>
        /// Wertname im StartupApproved-Schluessel (Registry-Wertname des Run-Eintrags bzw. Dateiname im
        /// Autostart-Ordner), wie ihn der Helfer fuer leistung.autostart.deaktivieren annimmt
        /// (docs/M3-ENTWURF.md, Abschnitt 8.5): 1 bis 255 Zeichen, kein Semikolon (Listentrenner des
        /// Plans), keine Steuerzeichen. Reine Funktion, kein Windows-Zugriff.
        /// </summary>
        public static bool WertnameZulaessig(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 255) return false;
            foreach (char c in name)
                if (char.IsControl(c) || c == ';') return false;
            return true;
        }

        /// <summary>
        /// Gattungswoerter in Produktnamen des Sicherheitscenters ("Avira Antivirus", "Kaspersky
        /// Internet Security"), die keinen Hersteller bezeichnen und deshalb bei NenntSchutzprodukt
        /// nicht zaehlen: sonst hiesse jeder Dienst mit "Service" im Anzeigenamen Schutzsoftware.
        /// </summary>
        static readonly HashSet<string> SchutzGattungswoerter = new HashSet<string>(StringComparer.Ordinal)
        {
            "antivirus", "antimalware", "anti", "virus", "malware", "security", "secure", "service", "services",
            "protection", "protect", "internet", "total", "free", "premium", "plus", "home", "suite", "edition",
            "essential", "essentials", "business", "cloud", "endpoint", "client", "agent", "windows", "microsoft",
        };

        /// <summary>
        /// Ob ein Text (Signierer-Subject oder Anzeigename eines Dienstes) ein Wort eines im
        /// Sicherheitscenter registrierten Schutzprogramms traegt (Sicherheit.DrittAv, z. B. "Avira
        /// Antivirus", "Malwarebytes"). Gezaehlt wird nur ein Wort aus mindestens vier Buchstaben, das
        /// kein Gattungswort ist (SchutzGattungswoerter), und es muss im Text als ganzes Wort stehen
        /// (Buchstaben und Ziffern bilden ein Wort, alles andere trennt; "Avira" in "CN=Avira
        /// Operations GmbH &amp; Co. KG", nicht in "Aviram"). Ergaenzt Netz.IstAvHersteller um die
        /// Hersteller, die dort fehlen; ein Treffer entzieht nur die Massnahme, er erzeugt keinen
        /// Befund (fail closed). Reine Funktion; null oder leer ist kein Treffer. wort und produkt
        /// nennen den ersten Treffer fuer die Detailzeile, in der Schreibweise des Produktnamens.
        /// </summary>
        public static bool NenntSchutzprodukt(string text, IEnumerable<string> produkte, out string wort, out string produkt)
        {
            wort = null; produkt = null;
            if (string.IsNullOrEmpty(text) || produkte == null) return false;
            var imText = new HashSet<string>(Woerter(text).Select(w => w.ToLowerInvariant()), StringComparer.Ordinal);
            if (imText.Count == 0) return false;
            foreach (string p in produkte)
            {
                foreach (string w in Woerter(p))
                {
                    if (w.Length < 4 || !w.All(char.IsLetter)) continue;
                    string klein = w.ToLowerInvariant();
                    if (SchutzGattungswoerter.Contains(klein) || !imText.Contains(klein)) continue;
                    wort = w; produkt = p;
                    return true;
                }
            }
            return false;
        }

        public static bool NenntSchutzprodukt(string text, IEnumerable<string> produkte)
        {
            string wort, produkt;
            return NenntSchutzprodukt(text, produkte, out wort, out produkt);
        }

        /// <summary>Woerter eines Texts: Folgen aus Buchstaben und Ziffern, alles andere trennt (wie Netz.IstAvHersteller).</summary>
        static List<string> Woerter(string text)
        {
            var woerter = new List<string>();
            if (string.IsNullOrEmpty(text)) return woerter;
            var aktuell = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) { aktuell.Append(c); continue; }
                if (aktuell.Length > 0) { woerter.Add(aktuell.ToString()); aktuell.Length = 0; }
            }
            if (aktuell.Length > 0) woerter.Add(aktuell.ToString());
            return woerter;
        }

        /// <summary>
        /// Aufgabenpfad, wie ihn der Helfer an schtasks.exe /TN weitergibt (docs/M3-ENTWURF.md,
        /// Abschnitt 1): nur Buchstaben, Ziffern, Leerzeichen, _ . - \ ( ) { }. Die geschweiften
        /// Klammern brauchen die Updater-Aufgaben der Fremdhersteller (GoogleUpdaterTaskSystem…{GUID},
        /// MicrosoftEdgeUpdateTaskMachineCore{GUID}); in einem ueber Process.Start (nicht cmd.exe)
        /// uebergebenen, in Anfuehrungszeichen stehenden Argument haben sie keine Sonderbedeutung.
        /// </summary>
        public static bool AufgabenpfadZulaessig(string pfad)
        {
            if (string.IsNullOrEmpty(pfad) || pfad.Length > 260) return false;
            foreach (char c in pfad)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                          || c == ' ' || c == '_' || c == '.' || c == '-' || c == '\\' || c == '(' || c == ')'
                          || c == '{' || c == '}';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Stabiler Kurzschluessel: FNV-1a (64 Bit) ueber den kleingeschriebenen Text, danach die
        /// Durchmischung aus MurmurHash3 (sonst unterscheiden sich "Fremd01" und "Fremd02" nur in
        /// zwei Stellen), die ersten 12 Hex-Zeichen. Ohne Kryptografie, weil SHA256Managed unter der
        /// FIPS-Richtlinie wirft und der Schluessel nur Wiedererkennung braucht (Antworten, Verlauf),
        /// keine Faelschungssicherheit. Gross-/Kleinschreibung zaehlt nicht: Registry-Wertnamen und
        /// Dateinamen sind es auch nicht.
        /// </summary>
        public static string Hash12(string text)
        {
            const ulong Basis = 14695981039346656037UL, Prim = 1099511628211UL;
            ulong h = Basis;
            unchecked
            {
                foreach (byte b in System.Text.Encoding.UTF8.GetBytes((text ?? "").ToLowerInvariant()))
                {
                    h ^= b;
                    h *= Prim;
                }
                h ^= h >> 33; h *= 0xff51afd7ed558ccdUL;
                h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL;
                h ^= h >> 33;
            }
            return h.ToString("x16", CultureInfo.InvariantCulture).Substring(0, 12);
        }

        /// <summary>Herausgeber-Text eines Startprogramms: geprueftes Urteil oder "nicht pruefbar" (rundll32 mit fremdem Ziel, .lnk ohne Aufloesung).</summary>
        static string Herkunft(AutostartEintrag x)
        {
            return x.Microsoft == null && string.IsNullOrEmpty(x.Signierer) ? "Signatur nicht prüfbar" : Herausgeber(x.Signierer);
        }

        /// <summary>Ort eines Startprogramms in Alltagssprache, mit dem Fachnamen in Klammern.</summary>
        static string OrtSatz(string quelle)
        {
            switch ((quelle ?? "").ToUpperInvariant())
            {
                case "STARTUP": return "dem Autostart-Ordner Ihres Kontos";
                case "COMMONSTARTUP": return "dem Autostart-Ordner aller Benutzer";
                default: return "der Registrierung (" + quelle + ")";
            }
        }

        /// <summary>
        /// Das Urteil des Sammlers (geprueft: WinVerifyTrust, Host-Ziel) hat Vorrang; fehlt es (aeltere
        /// Aufzeichnung, Datei nicht geprueft), entscheidet das Subject. Nicht pruefbar gilt als fremd:
        /// sichtbar ist besser als still verschwunden.
        /// </summary>
        static bool EintragFremd(AutostartEintrag x)
        {
            if (x.Microsoft.HasValue) return !x.Microsoft.Value;
            return !SigniererMicrosoft(x.Signierer);
        }

        /// <summary>Nur der CN aus dem Subject ("CN=Razer USA Ltd., O=..." -> "Razer USA Ltd."); ein CN in Anfuehrungszeichen darf Kommas enthalten.</summary>
        static string Herausgeber(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "ohne gültige Signatur";
            int i = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return subject;
            string rest = subject.Substring(i + 3);
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                int ende = rest.IndexOf('"', 1);
                return ende > 1 ? rest.Substring(1, ende - 1) : rest.Trim('"');
            }
            int komma = rest.IndexOf(',');
            return komma > 0 ? rest.Substring(0, komma).Trim() : rest.Trim();
        }

        static bool SigniererMicrosoft(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return false;
            if (subject.IndexOf("Hardware Compatibility Publisher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return subject.IndexOf("CN=Microsoft Windows", StringComparison.OrdinalIgnoreCase) >= 0
                || subject.IndexOf("CN=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Namensanfang der Edge-Updater-Aufgaben in der Wurzel der Aufgabenplanung
        /// (\MicrosoftEdgeUpdateTaskMachineCore{GUID}, ...TaskMachineUA{GUID}, ...TaskUser&lt;SID&gt;...):
        /// sie tragen keinen Autor, ihre Aktion ist MicrosoftEdgeUpdate.exe (CN=Microsoft Corporation).
        /// Bis 18.09.2026 zaehlte die Heuristik sie als Fremdhersteller und bot den Abschalt-Knopf an.
        /// </summary>
        const string EdgeUpdaterAufgabe = "\\MicrosoftEdgeUpdateTask";

        /// <summary>
        /// Das gepruefte Urteil des Sammlers (Aufgabe.Microsoft: Signatur der Aktionsdatei, wie bei
        /// Startprogrammen) hat Vorrang. Fehlt es (aeltere Aufzeichnung, COM-Handler ohne Pfad),
        /// entscheidet die Heuristik: Microsoft-Aufgaben liegen unter \Microsoft\, heissen wie die
        /// Edge-Updater-Aufgaben oder tragen als Autor einen Ressourcenverweis
        /// "$(@%SystemRoot%\...dll,-N)"; Fremdhersteller schreiben Klartext. Heuristik, kein Beweis:
        /// ein Schaedling, der sich so nennt, bleibt in der Sammelkarte unsichtbar, bis der Sammler
        /// die Signatur liefert (Microsoft = false wiegt hier nicht schwerer als der Ordner, weil
        /// Skript-Aufgaben unter \Microsoft\ einen fremden Zielpfad haben koennen).
        /// </summary>
        /// <summary>
        /// Deaktiviert ist eine Aufgabe, deren Schalter umgelegt ist (IRegisteredTask.Enabled false,
        /// Feld Aktiv), nicht eine mit TASK_STATE 1: eine Anmelde-Aufgabe, die ein residentes Programm
        /// startet, meldet nach schtasks /DISABLE weiter 4 (Doku TASK_STATE: RUNNING = "one or more
        /// instances running"), und mit Zustand als Massstab bliebe die Einzelkarte samt Knopf stehen.
        /// Ohne das Feld (aeltere Aufzeichnung) zaehlt der Zustand wie bis 18.09.2026.
        /// </summary>
        static bool AufgabeAktiv(Aufgabe t)
        {
            if (t.Aktiv.HasValue) return t.Aktiv.Value;
            return t.Zustand != 1;
        }

        static bool AufgabeMicrosoft(Aufgabe t)
        {
            if (t.Microsoft == true) return true;
            if (t.Pfad != null && t.Pfad.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Pfad != null && t.Pfad.StartsWith(EdgeUpdaterAufgabe, StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Autor == null) return false;
            if (t.Autor.StartsWith("$(@", StringComparison.Ordinal)) return true;
            return t.Autor.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---------------------------------------------------------------- Winlogon

        /// <summary>Erwartetes Ende des einzigen erlaubten Userinit-Teils (nur fuer die Detailzeile); das Urteil faellt UserinitMuster.</summary>
        const string UserinitEnde = @"\system32\userinit.exe";
        /// <summary>
        /// Der einzige erlaubte Userinit-Teil, am Anfang verankert: %SystemRoot% oder ein Laufwerk
        /// A bis Z mit \Windows, dann \system32\userinit.exe, sonst nichts. Ein blosses EndsWith
        /// liess bis 18.09.2026 C:\Users\Public\system32\userinit.exe, \\server\share\system32\userinit.exe
        /// und C:\Windows\system32\..\..\evil\system32\userinit.exe als Standard durch (gemessen);
        /// ein fremder Ordner namens system32, ein UNC-Pfad und ".." fallen jetzt durch. Seit Vista
        /// heisst der Windows-Ordner immer Windows, der Helfer schreibt &lt;SystemDirectory&gt;\userinit.exe,
        /// (C:\WINDOWS\system32\userinit.exe,) und erfuellt das Muster.
        /// </summary>
        static readonly Regex UserinitMuster = new Regex(@"^(%SystemRoot%|[A-Za-z]:\\Windows)\\system32\\userinit\.exe$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        const string ShellStandard = "explorer.exe";

        /// <summary>
        /// Ob Winlogon\Shell der Standard ist (docs/M4-ENTWURF.md, Abschnitt 2.4): null oder leer
        /// (Wert nicht gesetzt, Windows nimmt explorer.exe) oder genau explorer.exe, Gross/Klein
        /// egal, Leerraum getrimmt. Ein abschliessendes Komma ohne weiteren Eintrag (explorer.exe,)
        /// zaehlt wie bisher als Standard: Winlogon ignoriert den leeren Teil. Regel, Nachweis und
        /// Helfer (leistung.winlogon.zuruecksetzen) rufen genau diese Funktion. Reine Funktion.
        /// </summary>
        public static bool WinlogonShellStandard(string shell)
        {
            if (string.IsNullOrWhiteSpace(shell)) return true;
            return string.Equals(shell.Trim().TrimEnd(',').Trim(), ShellStandard, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ob Winlogon\Userinit der Standard ist (docs/M4-ENTWURF.md, Abschnitt 2.4): null oder leer,
        /// oder genau EIN Teil, der dem UserinitMuster entspricht (Anfuehrungszeichen und Leerraum
        /// getrimmt), optional mit abschliessendem Komma, ohne weiteren Eintrag nach dem Komma. Der
        /// Pfad ist nicht beliebig: die gleichnamige Kopie in einem fremden Ordner ist der Austausch,
        /// den die Regel seit v8.0 meldet (Doku "cannot-log-on-windows"); der Helfer schreibt
        /// &lt;SystemDirectory&gt;\userinit.exe, und erfuellt damit dieselbe Bedingung. Reine Funktion.
        /// </summary>
        public static bool WinlogonUserinitStandard(string userinit)
        {
            if (string.IsNullOrWhiteSpace(userinit)) return true;
            var teile = UserinitTeile(userinit);
            return teile.Count == 1 && UserinitTeilStandard(teile[0]);
        }

        /// <summary>Ein einzelner, schon getrimmter Userinit-Teil gegen das verankerte Muster; UNC, ".." und fremde Ordner namens system32 fallen durch.</summary>
        static bool UserinitTeilStandard(string teil)
        {
            return !string.IsNullOrEmpty(teil) && UserinitMuster.IsMatch(teil);
        }

        /// <summary>Die durch Komma getrennten Teile von Userinit, je ohne Leerraum und Anfuehrungszeichen; leere Teile (abschliessendes Komma) fallen weg.</summary>
        static List<string> UserinitTeile(string userinit)
        {
            return (userinit ?? "").Split(',').Select(t => t.Trim().Trim('"').Trim()).Where(t => t.Length > 0).ToList();
        }

        /// <summary>
        /// Shell muss "explorer.exe" sein, Userinit genau "...\system32\userinit.exe," (Doku
        /// "cannot-log-on-windows"). Jeder weitere, durch Komma getrennte Teil wird beim Anmelden
        /// zusaetzlich gestartet: die uebliche Persistenz-Manipulation. Beide Befunde entscheiden
        /// ueber WinlogonShellStandard/WinlogonUserinitStandard, nie ueber eine zweite Pruefung.
        /// </summary>
        static void Winlogon(Kontext ctx, BereichErgebnis e)
        {
            var A = ctx.S.Autostart;
            if (!WinlogonShellStandard(A.Shell))
            {
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Leistung,
                    Schluessel = "leistung.winlogon.shell",
                    Zustand = Zustand.Bad,
                    Messwert = Messwert.Von(A.Shell, "Winlogon\\Shell", "explorer.exe"),
                    Quelle = "HKLM\\...\\Winlogon\\Shell",
                    Titel = "Windows-Oberfläche wurde ausgetauscht",
                    Satz = "Beim Anmelden startet „" + A.Shell + "“ statt „explorer.exe“; das ist nicht die normale Windows-Oberfläche.",
                    Rat = "Das kann Absicht sein (Kiosk-PC), sonst ein Zeichen für Schadsoftware: mit einem aktuellen Virenscanner prüfen und den Wert auf „explorer.exe“ zurücksetzen lassen (Maßnahme an dieser Karte, der alte Wert bleibt in der Sicherung).",
                    Detail = new List<string> { "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\\Shell = " + A.Shell },
                    Massnahmen = new List<string> { MWinlogonZuruecksetzen },
                });
            }
            if (!WinlogonUserinitStandard(A.Userinit))
            {
                var teile = UserinitTeile(A.Userinit);
                int echt = teile.FindIndex(UserinitTeilStandard);
                var weitere = teile.Where((t, i) => i != echt).ToList();
                bool ergaenzt = echt >= 0;
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Leistung,
                    Schluessel = "leistung.winlogon.userinit",
                    Zustand = Zustand.Bad,
                    Messwert = Messwert.Von(A.Userinit, "Winlogon\\Userinit", "%SystemRoot%\\system32\\userinit.exe,"),
                    Quelle = "HKLM\\...\\Winlogon\\Userinit",
                    Titel = ergaenzt ? "Anmeldeprogramm wurde ergänzt" : "Anmeldeprogramm wurde ausgetauscht",
                    Satz = ergaenzt
                        ? "Neben „userinit.exe“ startet der Anmeldevorgang zusätzlich " + string.Join(" und ", weitere.Select(w => "„" + w + "“")) + "; das gehört nicht zu Windows und führt bei jeder Anmeldung fremden Code aus."
                        : "Der Anmeldevorgang startet „" + A.Userinit + "“ statt „userinit.exe“ aus dem Windows-Ordner; ohne dieses Programm schlägt die Anmeldung fehl oder läuft über fremden Code.",
                    Rat = "Mit einem aktuellen Virenscanner prüfen und den Wert auf „C:\\Windows\\system32\\userinit.exe,“ zurücksetzen lassen (Maßnahme an dieser Karte, der alte Wert bleibt in der Sicherung).",
                    Detail = new List<string>
                    {
                        "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\\Userinit = " + A.Userinit,
                        "Erlaubt ist genau ein Teil %SystemRoot%" + UserinitEnde + " oder <Laufwerk>:\\Windows" + UserinitEnde + " (kein fremder Ordner, kein Netzpfad, kein „..“); " + (ergaenzt ? "zusätzliche Teile: " + string.Join(", ", weitere) : "kein Teil erfüllt das"),
                    },
                    Massnahmen = new List<string> { MWinlogonZuruecksetzen },
                });
            }
        }

        // ---------------------------------------------------------------- AppInit_DLLs

        /// <summary>
        /// AppInit_DLLs laedt eine DLL in jeden Prozess, aber nur, wenn der Hauptschalter
        /// LoadAppInit_DLLs auf 1 steht (Vorgabe seit Windows 8: 0) und Secure Boot aus ist
        /// (Doku "Secure Boot and AppInit_DLLs"). Ohne UEFI gibt es kein Secure Boot; dort wirkt
        /// der Schalter allein, deshalb kippt "Secure Boot unbekannt" nicht auf unknown.
        /// </summary>
        static void AppInit(Kontext ctx, BereichErgebnis e)
        {
            var A = ctx.S.Autostart;
            bool da64 = !string.IsNullOrWhiteSpace(A.AppInitDlls), da32 = !string.IsNullOrWhiteSpace(A.AppInitDlls32);
            if (!da64 && !da32) return;
            string eintrag = da64 ? A.AppInitDlls : A.AppInitDlls32;
            bool? secureBoot = ctx.S.Sicherheit == null ? null : ctx.S.Sicherheit.SecureBoot;
            bool? uefi = ctx.S.Hardware == null ? null : ctx.S.Hardware.Uefi;
            bool? load = A.LoadAppInitDlls;

            string zustand, satz, rat = null;
            if (secureBoot == true)
            {
                zustand = Zustand.Ok;
                satz = "AppInit_DLLs enthält „" + eintrag + "“, wirkt aber nicht, weil Secure Boot eingeschaltet ist.";
            }
            else if (load == false)
            {
                zustand = Zustand.Ok;
                satz = "AppInit_DLLs enthält „" + eintrag + "“, wirkt aber nicht, weil der Hauptschalter LoadAppInit_DLLs auf 0 steht.";
            }
            else
            {
                zustand = Zustand.Warn;
                satz = load == true
                    ? "AppInit_DLLs enthält „" + eintrag + "“ und LoadAppInit_DLLs steht auf 1: ohne Secure Boot wird diese Bibliothek in jedes Programm geladen."
                    : "AppInit_DLLs enthält „" + eintrag + "“; ob sie geladen wird, war nicht ermittelbar (LoadAppInit_DLLs nicht gelesen), ohne Secure Boot ist das möglich.";
                rat = "Prüfen, welches Programm den Eintrag angelegt hat (oft Sicherheits- oder Fernwartungssoftware, sonst Schadsoftware); den Eintrag nur leeren, wenn das Programm ihn nicht braucht.";
            }
            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Leistung,
                Schluessel = "leistung.appinit",
                Zustand = zustand,
                Messwert = Messwert.Von(eintrag, "AppInit_DLLs", "leer"),
                Quelle = "HKLM\\...\\Windows\\AppInit_DLLs, LoadAppInit_DLLs",
                Titel = zustand == Zustand.Ok ? "Eintrag für eine Bibliothek in jedem Programm, wirkt nicht"
                      : (load == true ? "Fremde Bibliothek wird in jedes Programm geladen" : "Eintrag für eine Bibliothek in jedem Programm"),
                Satz = satz,
                Rat = rat,
                Detail = new List<string>
                {
                    "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Windows\\AppInit_DLLs = " + (A.AppInitDlls ?? "nicht gelesen"),
                    "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows NT\\CurrentVersion\\Windows\\AppInit_DLLs = " + (A.AppInitDlls32 ?? "nicht gelesen"),
                    "LoadAppInit_DLLs: " + (load == null ? "nicht gelesen" : load == true ? "1 (an)" : "0 (aus)"),
                    "Secure Boot: " + (secureBoot == null ? (uefi == false ? "nicht vorhanden (BIOS/CSM)" : "nicht ermittelbar") : secureBoot == true ? "an" : "aus"),
                },
            });
        }
    }
}
