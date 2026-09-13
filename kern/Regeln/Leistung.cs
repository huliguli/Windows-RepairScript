using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

        static void Autostarts(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            var A = s.Autostart;
            var liste = new List<string>();
            var verwaist = new List<string>();
            var eigene = new List<string>();

            // Startprogramme: Aktiviert != false (null = kein StartupApproved-Eintrag = aktiv), mit Befehl (ein
            // leerer Run-Wert, gemessen "GalaxyClient" = "", startet nichts), Datei vorhanden, nicht von Microsoft.
            foreach (var x in A.Eintraege.Where(x => x.Aktiviert != false && !string.IsNullOrWhiteSpace(x.Befehl)))
            {
                string zeile = x.Quelle + ": " + x.Name + " = " + x.Befehl;
                if (x.DateiVorhanden == false) { verwaist.Add(zeile); continue; }
                if (EintragFremd(x)) liste.Add(zeile + " (" + (x.Microsoft == null && string.IsNullOrEmpty(x.Signierer) ? "Signatur nicht prüfbar" : Herausgeber(x.Signierer)) + ")");
            }

            // Aufgaben mit Anmelde- oder Start-Ausloeser, nicht deaktiviert (TASK_STATE 1), nicht von Microsoft, nicht die eigenen.
            foreach (var t in A.Aufgaben.Where(t => (t.Logon || t.Boot) && t.Zustand != 1))
            {
                if (t.Eigen) { eigene.Add(t.Pfad); continue; }
                if (!AufgabeMicrosoft(t)) liste.Add("Aufgabe " + t.Pfad + " (" + (t.Autor ?? "ohne Autor") + ", " + (t.Logon ? "bei Anmeldung" : "beim Start") + ")");
            }

            // Dienste mit Startart Auto, deren Binaerdatei nicht von Microsoft signiert ist (null = nicht pruefbar, zaehlt nicht).
            foreach (var d in A.Dienste.Where(d => string.Equals(d.Startart, "Auto", StringComparison.OrdinalIgnoreCase) && d.Microsoft == false))
                liste.Add("Dienst " + d.Name + " (" + Herausgeber(d.Signierer) + (d.Verzoegert ? ", verzögert" : "") + ")");

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

            var detail = new List<string>(liste);
            if (luecken.Count > 0) detail.Add("Nicht lesbar: " + string.Join(", ", luecken) + " (die Zahl ist deshalb " + (obergrenze && untergrenze ? "weder Unter- noch Obergrenze" : obergrenze ? "eine Obergrenze" : "eine Untergrenze") + ")");
            if (!A.AufgabenVollstaendig) detail.Add("Aufgaben ohne Administratorrechte gelesen: rund ein Viertel ist dann nicht sichtbar.");
            int deaktiviert = A.Eintraege.Count(x => x.Aktiviert == false);
            if (deaktiviert > 0) detail.Add("Bereits deaktivierte Startprogramme (Task-Manager): " + deaktiviert);
            if (verwaist.Count > 0) detail.Add("Einträge, deren Datei nicht gefunden wurde (starten nichts, Rest einer Deinstallation; nicht gezählt): " + string.Join("; ", verwaist));
            if (eigene.Count > 0) detail.Add("Eigene Aufgaben dieses Programms (nicht gezählt): " + string.Join(", ", eigene));

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
                Massnahmen = zustand == Zustand.Warn ? new List<string> { "autostart.deaktivieren" } : new List<string>(),
            });
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
        /// Microsoft-Aufgaben liegen unter \Microsoft\ oder tragen als Autor einen Ressourcenverweis
        /// "$(@%SystemRoot%\...dll,-N)"; Fremdhersteller schreiben Klartext. Heuristik, kein Beweis.
        /// </summary>
        static bool AufgabeMicrosoft(Aufgabe t)
        {
            if (t.Pfad != null && t.Pfad.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Autor == null) return false;
            if (t.Autor.StartsWith("$(@", StringComparison.Ordinal)) return true;
            return t.Autor.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---------------------------------------------------------------- Winlogon

        /// <summary>Erwartetes Ende des einzigen erlaubten Userinit-Teils; deckt %SystemRoot%, C:\Windows und D:\Windows ab, nicht die gleichnamige Kopie in einem fremden Ordner.</summary>
        const string UserinitEnde = @"\system32\userinit.exe";

        /// <summary>
        /// Shell muss "explorer.exe" sein, Userinit genau "...\system32\userinit.exe," (Doku
        /// "cannot-log-on-windows"). Jeder weitere, durch Komma getrennte Teil wird beim Anmelden
        /// zusaetzlich gestartet: die uebliche Persistenz-Manipulation.
        /// </summary>
        static void Winlogon(Kontext ctx, BereichErgebnis e)
        {
            var A = ctx.S.Autostart;
            if (A.Shell != null && !string.Equals(A.Shell.Trim().TrimEnd(','), "explorer.exe", StringComparison.OrdinalIgnoreCase))
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
                    Rat = "Das kann Absicht sein (Kiosk-PC), sonst ein Zeichen für Schadsoftware: mit einem aktuellen Virenscanner prüfen und den Wert auf „explorer.exe“ zurücksetzen.",
                    Detail = new List<string> { "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\\Shell = " + A.Shell },
                    Massnahmen = new List<string> { "autostart.winlogon.zuruecksetzen" },
                });
            }
            if (A.Userinit != null)
            {
                var teile = A.Userinit.Split(',').Select(t => t.Trim().Trim('"').Trim()).Where(t => t.Length > 0).ToList();
                int echt = teile.FindIndex(t => t.EndsWith(UserinitEnde, StringComparison.OrdinalIgnoreCase));
                var weitere = teile.Where((t, i) => i != echt).ToList();
                if (echt >= 0 && weitere.Count == 0) return;
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
                    Rat = "Mit einem aktuellen Virenscanner prüfen und den Wert auf „C:\\Windows\\system32\\userinit.exe,“ zurücksetzen.",
                    Detail = new List<string>
                    {
                        "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\\Userinit = " + A.Userinit,
                        "Erlaubt ist genau ein Teil, der auf " + UserinitEnde + " endet; " + (ergaenzt ? "zusätzliche Teile: " + string.Join(", ", weitere) : "kein Teil erfüllt das"),
                    },
                    Massnahmen = new List<string> { "autostart.winlogon.zuruecksetzen" },
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
