using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Stabilitaet: unerwartete Neustarts, Programmabstuerze, Hardwarefehler; Kernel-PnP 219/411
    /// nur als Verlauf in den Einzelheiten (Konzept 4.1), nie als eigener Befund.
    /// Reine Funktion ueber dem Systembild: kein Windows-Zugriff, kein DateTime.Now (ctx.Jetzt
    /// ist die Aufzeichnungszeit), jede Schwelle aus Schwellen.cs.
    ///
    /// Was v7 falsch machte und hier anders ist:
    ///   - IDs nur zusammen mit dem Anbieter (41, 1001, 6008 vergeben mehrere Quellen).
    ///   - Kernel-Power 41 wird nach Feldern klassifiziert (Blauschirm / Einschalttaste /
    ///     Stromverlust), nicht gezaehlt. Ein 41 kurz nach User32 1074 ist ein geplanter Neustart.
    ///   - "0 Ereignisse" bei jungem Log heisst "nichts seit Log-Anfang" und steht so im Satz.
    ///   - Kein Ereignis nennt den schuldigen Treiber. Die Regel nennt Stop-Code, Namen und
    ///     Ursachenklasse aus der Tabelle und raet zur Dump-Analyse; sie behauptet nie "Treiber X".
    ///   - Jede Quelle, die die Regel liest, wird gegen die Fehlerliste geprueft - jeder Art, nicht
    ///     nur "zugriff". Scheitert die Hauptquelle (Kernel-Power 41, Log-Beginn), bleiben gelesene
    ///     Blauschirme ein Befund, aber "kein Absturz" ist keine Aussage mehr (unknown, nie ok).
    /// </summary>
    public static class Stabilitaet
    {
        const string LogSystem = "System";
        const string LogApplication = "Application";
        const string DiagnosePerfLog = "Microsoft-Windows-Diagnostics-Performance/Operational";

        const string QuelleBeginn = "log.system.beginn";
        const string QuelleKernelPower = "log.system.kernel-power";
        const string QuelleUser32 = "log.system.user32";
        const string QuelleStartdauer = "log.diagnostics-performance";

        /// <summary>Quellenkennungen des Sammlers (Ereignisquelle.Abos), die diese Regel liest, mit ihrem Laiennamen fuer "ließ sich nicht prüfen".</summary>
        static readonly KeyValuePair<string, string>[] SystemQuellen =
        {
            new KeyValuePair<string, string>(QuelleBeginn, "Beginn des Systemprotokolls"),
            new KeyValuePair<string, string>(QuelleKernelPower, "Unerwartete Neustarts (Kernel-Power 41)"),
            new KeyValuePair<string, string>(QuelleUser32, "Geplante Neustarts (User32 1074)"),
            new KeyValuePair<string, string>("log.system.kernel-boot", "Zweitquelle Kernel-Boot 20"),
            new KeyValuePair<string, string>("log.system.eventlog", "Zweitquelle EventLog 6008"),
            new KeyValuePair<string, string>("log.system.volmgr", "Zweitquelle volmgr 46"),
            new KeyValuePair<string, string>("log.system.wer-systemerrorreporting", "Absturzberichte (WER 1001)"),
            new KeyValuePair<string, string>("log.system.whea-logger", "Hardwarefehler (WHEA)"),
            new KeyValuePair<string, string>("log.system.kernel-pnp", "Treiberstart-Verlauf (Kernel-PnP 219)"),
            new KeyValuePair<string, string>("log.system.verlauf.kernel-pnp", "Gerätestart-Verlauf (Kernel-PnP 411)"),
            new KeyValuePair<string, string>("log.system.kernel-general", "Startzeitpunkte (Kernel-General 12)"),
            new KeyValuePair<string, string>("log.system.kernel-processor-power", "Prozessor-Drosselung (Kernel-Processor-Power 37)"),
        };
        static readonly KeyValuePair<string, string>[] ApplicationQuellen =
        {
            new KeyValuePair<string, string>("log.application.beginn", "Beginn des Anwendungsprotokolls"),
            new KeyValuePair<string, string>("log.application.application-error", "Programmabstürze (Application Error 1000)"),
            new KeyValuePair<string, string>("log.application.application-hang", "Eingefrorene Programme (Application Hang 1002)"),
        };

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var er = s.Ereignisse;
            var e = new BereichErgebnis { Bereich = Bereich.Stabilitaet };

            bool systemGesperrt = ctx.LogGesperrt(LogSystem) || s.ZugriffVerweigert("log.system.");
            bool applicationGesperrt = ctx.LogGesperrt(LogApplication) || s.ZugriffVerweigert("log.application.");
            // Ein leeres Log MIT Beginn ist Daten, ein Log ohne Beginn und ohne Eintrag nicht.
            bool eintraege = er.Eintraege.Count > 0 || !string.IsNullOrEmpty(er.BeginnSystemUtc);
            // Hauptquelle des Bereichs: Kernel-Power 41 und der Log-Beginn. Scheitert eine von beiden
            // (Zeit, Ausnahme, Obergrenze), sind die gelesenen Eintraege gueltig, aber "kein Absturz"
            // ist keine Aussage mehr: ohne DatenVorhanden wird ein Uebersicht-Befund "unknown" nicht
            // von Befund.cs auf ok gehoben; warn und bad aus gelesenen Eintraegen bleiben.
            bool hauptquelleGescheitert = s.FehlerVon(QuelleKernelPower).Any() || s.FehlerVon(QuelleBeginn).Any();
            e.DatenVorhanden = !systemGesperrt && !hauptquelleGescheitert && eintraege;

            if (systemGesperrt) e.Fehlend.Add("Systemprotokoll nicht lesbar (Zugriff verweigert)");
            else if (!eintraege) e.Fehlend.Add("Ereignisprotokoll lieferte nichts (weder Einträge noch Beginn)");
            if (applicationGesperrt) e.Fehlend.Add("Anwendungsprotokoll nicht lesbar (Zugriff verweigert)");
            StartdauerFehlend(ctx, e);
            if (systemGesperrt || !eintraege) return e;
            QuellenFehlend(s, e, SystemQuellen);
            if (!applicationGesperrt) QuellenFehlend(s, e, ApplicationQuellen);

            // Ein junges Log deckt nicht den ganzen Zeitraum ab: das steht als "ließ sich nicht prüfen".
            int reichweite = ctx.LogReichweiteTage(LogSystem);
            if (reichweite < er.Tage)
                e.Fehlend.Add("Abstürze vor Beginn des Protokolls (es reicht nur " + reichweite + " Tage zurück, nicht " + er.Tage + ")");

            var uebersichtDetail = new List<string>();
            int aeltereStromverluste, unbewertete41;
            bool neustartBefund = KernelPower41(ctx, e, uebersichtDetail, out aeltereStromverluste, out unbewertete41);
            if (!applicationGesperrt) Programmabstuerze(ctx, e);
            Whea(ctx, e);
            KernelPnpVerlauf(ctx, uebersichtDetail);

            string unvollstaendig = null;
            if (hauptquelleGescheitert)
            {
                var f = s.FehlerVon(QuelleKernelPower).FirstOrDefault();
                unvollstaendig = f != null ? "Quelle Kernel-Power 41 meldet „" + Grund(f) + "“" : "Beginn des Protokolls unbekannt („" + Grund(s.FehlerVon(QuelleBeginn).First()) + "“)";
            }
            else if (unbewertete41 > 0)
            {
                // Ohne User32 1074 zaehlt jeder geplante Neustart als Stromverlust: nicht bewerten, nicht ok.
                unvollstaendig = Text.Mal(unbewertete41) + " Kernel-Power 41 ohne Blauschirm-Code, aber die geplanten Neustarts (User32 1074) fehlen („" + Grund(s.FehlerVon(QuelleUser32).First()) + "“)";
                e.DatenVorhanden = false;
            }
            Uebersicht(ctx, e, neustartBefund, uebersichtDetail, aeltereStromverluste, unvollstaendig);
            return e;
        }

        // ---------------------------------------------------------------- Fehlerliste

        /// <summary>Jede gescheiterte Quelle, jeder Art: der Nutzer sieht "ließ sich nicht prüfen", nicht ein stilles "nichts gefunden".</summary>
        static void QuellenFehlend(Systembild s, BereichErgebnis e, KeyValuePair<string, string>[] quellen)
        {
            foreach (var q in quellen)
            {
                var f = s.FehlerVon(q.Key).FirstOrDefault();
                if (f != null) e.Fehlend.Add(q.Value + ": " + Grund(f));
            }
        }

        /// <summary>
        /// Startdauer-Log: "zugriff" ist die Kanal-ACL (nicht erhoeht), alles andere (Zeit, beschaedigtes
        /// Protokoll nach Stromverlust) ist kein Rechteproblem und steht mit seinem Text da.
        /// </summary>
        static void StartdauerFehlend(Kontext ctx, BereichErgebnis e)
        {
            var fehler = ctx.S.FehlerVon(QuelleStartdauer).ToList();
            if (fehler.Count == 0)
            {
                if (ctx.LogGesperrt(DiagnosePerfLog)) e.Fehlend.Add("Startdauer (das Protokoll dafür braucht Administratorrechte)");
                return;
            }
            var f = fehler[0];
            e.Fehlend.Add(f.Art == Fehler.Zugriff
                ? "Startdauer (das Protokoll dafür braucht Administratorrechte)"
                : "Startdauer (Protokoll ließ sich nicht lesen: " + (f.Text ?? f.Art) + ")");
        }

        /// <summary>Grund in Alltagssprache; bei Zeit, Fehlt und Ausnahme sagt der Sammlertext mehr als das Wort.</summary>
        static string Grund(Fehler f)
        {
            if (f.Art == Fehler.Zugriff) return "keine Rechte";
            if (!string.IsNullOrEmpty(f.Text)) return f.Text;
            return f.Art == Fehler.Zeit ? "Zeitüberschreitung" : f.Art == Fehler.Fehlt ? "nicht vorhanden" : "Fehler beim Lesen";
        }

        /// <summary>
        /// Zeitraum fuer die Saetze wie ctx.ZeitraumText, aber ohne Log-Beginn nicht "in den letzten
        /// 90 Tagen" (das behauptet eine Reichweite, die niemand gemessen hat).
        /// </summary>
        static string Zeitraum(Kontext ctx)
        {
            if (string.IsNullOrEmpty(ctx.S.Ereignisse.BeginnSystemUtc)) return "im vorliegenden Protokoll (Reichweite unbekannt)";
            return ctx.ZeitraumText(LogSystem);
        }

        // ---------------------------------------------------------------- Kernel-Power 41

        /// <summary>
        /// Drei Faelle nach der Doku "Event ID 41": BugcheckCode != 0 Blauschirm; PowerButtonTimestamp
        /// != 0 Einschalttaste gehalten (nur Information); alles 0 Stromverlust oder Haenger.
        /// Der Code steht DEZIMAL im Feld (159 = 0x9F). Liefert true, wenn ein Neustart-Befund entstand;
        /// aeltere = Stromverluste zwischen StromverlustTage und StromverlustAeltereTage (genannt, nicht
        /// bewertet); unbewertet = 41 ohne Code, die ohne User32-Quelle nicht einzuordnen sind.
        /// </summary>
        static bool KernelPower41(Kontext ctx, BereichErgebnis e, List<string> uebersicht, out int aeltere, out int unbewertet)
        {
            var s = ctx.S;
            aeltere = 0; unbewertet = 0;
            var alle41 = s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).Where(x => ctx.Innerhalb(x.ZeitUtc, s.Ereignisse.Tage)).ToList();
            var blauschirme = new List<Ereignis>();
            var einschalttaste = new List<Ereignis>();
            var stromverlust = new List<Ereignis>();
            var geplant = new List<Ereignis>();
            var zeiten1074 = s.Ereignisse.Von("User32", 1074).Select(x => Zeit.Lesen(x.ZeitUtc)).Where(t => t.HasValue).Select(t => t.Value).ToList();
            bool user32Gescheitert = s.FehlerVon(QuelleUser32).Any();

            foreach (var ev in alle41)
            {
                long code = ev.FeldZahl("BugcheckCode", 0);
                long taste = ev.FeldZahl("PowerButtonTimestamp", 0);
                if (code != 0) blauschirme.Add(ev);
                else if (taste != 0) einschalttaste.Add(ev);
                else if (user32Gescheitert) unbewertet++;
                else if (Geplant(ev, zeiten1074)) geplant.Add(ev);
                else stromverlust.Add(ev);
            }
            bool befund = false;

            // Blauschirm: 1 in 90 Tagen warn, 2 bad (Schwellen.Blauschirm*).
            if (blauschirme.Count >= Schwellen.BlauschirmWarn)
            {
                befund = true;
                var klassen = new List<string>();
                var detail = new List<string>();
                foreach (var ev in blauschirme.OrderByDescending(x => x.ZeitUtc))
                {
                    long code = ev.FeldZahl("BugcheckCode", 0);
                    var bc = Bugchecks.Von(code);
                    string zeile = Datum(ev.ZeitUtc) + ": Stop-Code 0x" + code.ToString("X", CultureInfo.InvariantCulture) + " (" + code + ")";
                    if (bc != null)
                    {
                        zeile += " " + bc.Name + ", Ursachenklasse " + string.Join("/", bc.Klassen);
                        foreach (string k in bc.Klassen) if (!klassen.Contains(k)) klassen.Add(k);
                    }
                    else zeile += " nicht in der Tabelle der häufigen Codes; nur eine Analyse der Abbild-Datei sagt mehr";
                    string p1 = ev.Feld("BugcheckParameter1");
                    if (p1 != null) zeile += "; Parameter " + p1 + " " + (ev.Feld("BugcheckParameter2") ?? "") + " " + (ev.Feld("BugcheckParameter3") ?? "") + " " + (ev.Feld("BugcheckParameter4") ?? "");
                    detail.Add(zeile.TrimEnd());
                }
                foreach (var w in s.Ereignisse.Von("Microsoft-Windows-WER-SystemErrorReporting", 1001).Where(x => ctx.Innerhalb(x.ZeitUtc, s.Ereignisse.Tage)))
                    detail.Add(Datum(w.ZeitUtc) + ": Absturzbericht (WER 1001) Code " + (w.Feld("param1") ?? w.Feld("0") ?? "?") + ", Abbild " + (w.Feld("param2") ?? w.Feld("1") ?? "?"));
                detail.Add("Kein Ereignis nennt den verursachenden Treiber; das sagt nur die Analyse der Abbild-Datei (Minidump, WinDbg !analyze -v).");
                if (klassen.Contains(Bugchecks.Hardware))
                {
                    int whea = s.Ereignisse.Von("Microsoft-Windows-WHEA-Logger").Count(x => x.Level == 2 && ctx.Innerhalb(x.ZeitUtc, s.Ereignisse.Tage));
                    detail.Add("Hardwarefehler-Meldungen (WHEA, schwerwiegend) im selben Zeitraum: " + whea);
                }

                int n = blauschirme.Count;
                string zustand = n >= Schwellen.BlauschirmBad ? Zustand.Bad : Zustand.Warn;
                var massnahmen = new List<string>();
                if (klassen.Contains(Bugchecks.Ram)) massnahmen.Add("stabilitaet.speichertest.planen");
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.blauschirm",
                    Zustand = zustand,
                    Messwert = Messwert.Von(n, "Blauschirme in " + s.Ereignisse.Tage + " Tagen", Schwellen.BlauschirmWarn + " warn, " + Schwellen.BlauschirmBad + " bad"),
                    Quelle = "Kernel-Power 41.BugcheckCode",
                    Titel = "Blauschirm (Absturz von Windows)",
                    Satz = "Windows ist " + Text.Mal(n) + " " + Zeitraum(ctx) + " mit einem Blauschirm abgestürzt" + KlassenSatz(klassen) + ".",
                    Rat = RatJeKlasse(klassen),
                    Detail = detail,
                    Massnahmen = massnahmen,
                });
            }

            // Einschalttaste: kein Fehler, aber eine Information mit Zahl.
            if (einschalttaste.Count > 0)
            {
                befund = true;
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.einschalttaste",
                    Zustand = Zustand.Ok,
                    Messwert = Messwert.Von(einschalttaste.Count, "Ausschaltungen per Taste", null),
                    Quelle = "Kernel-Power 41.PowerButtonTimestamp",
                    Titel = "Ausschalten über die Einschalttaste",
                    Satz = "Der PC wurde " + Text.Mal(einschalttaste.Count) + " " + Zeitraum(ctx) + " über die Einschalttaste hart ausgeschaltet; das ist kein Absturz, aber nicht schonend.",
                    Rat = "Windows über das Startmenü herunterfahren; die Taste nur, wenn nichts mehr reagiert.",
                    Detail = einschalttaste.OrderByDescending(x => x.ZeitUtc).Select(x => Datum(x.ZeitUtc) + ": Kernel-Power 41, PowerButtonTimestamp " + x.Feld("PowerButtonTimestamp")).ToList(),
                });
            }

            // Stromverlust oder Haenger: nur die ohne 1074 davor, im kurzen Fenster (30 Tage).
            var kurz = stromverlust.Where(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.StromverlustTage)).ToList();
            if (kurz.Count > 0)
            {
                befund = true;
                var detail = kurz.OrderByDescending(x => x.ZeitUtc).Select(x => Datum(x.ZeitUtc) + ": Kernel-Power 41 mit BugcheckCode 0 und PowerButtonTimestamp 0, kein User32 1074 in den " + Schwellen.NeustartAbstand1074Min + " Minuten davor").ToList();
                int boot20 = s.Ereignisse.Von("Microsoft-Windows-Kernel-Boot", 20).Count(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.StromverlustTage) && IstFalsch(x.Feld("LastShutdownGood")));
                int volmgr46 = s.Ereignisse.Von("volmgr", 46).Count(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.StromverlustTage));
                var e6008 = s.Ereignisse.Von("EventLog", 6008).Where(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.StromverlustTage)).ToList();
                detail.Add("Zweitquelle Kernel-Boot 20 mit LastShutdownGood=false in " + Schwellen.StromverlustTage + " Tagen: " + boot20);
                detail.Add("Zweitquelle volmgr 46 (Absturzabbild konnte nicht geschrieben werden) in " + Schwellen.StromverlustTage + " Tagen: " + volmgr46);
                foreach (var x in e6008)
                    detail.Add("EventLog 6008: vorheriges Herunterfahren war unerwartet, Zeitpunkt laut Binärfeld " + (x.Feld("zeitBinaerUtc") != null ? Datum(x.Feld("zeitBinaerUtc")) : "unbekannt"));
                if (geplant.Count > 0) detail.Add("Nicht gezählt, weil ein geplanter Neustart (User32 1074) kurz davor lag: " + geplant.Count);
                if (s.Leistung != null && s.Leistung.AuslagerungMB.HasValue) detail.Add("Auslagerungsdatei: " + s.Leistung.AuslagerungMB.Value + " MB");

                int n = kurz.Count;
                bool warn = n >= Schwellen.StromverlustWarn;
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.stromverlust",
                    Zustand = warn ? Zustand.Warn : Zustand.Ok,
                    Messwert = Messwert.Von(n, "Abschaltungen ohne Herunterfahren in " + Schwellen.StromverlustTage + " Tagen", Schwellen.StromverlustWarn + " warn"),
                    Quelle = "Kernel-Power 41 (alle Felder 0)",
                    Titel = "Abschaltung ohne Herunterfahren",
                    // Spiegelbildlich zum ok-Zweig: "ab 3-mal" statt "mehr als 3", denn bei genau 3 waere das falsch.
                    Satz = warn
                        ? "Der PC ging " + Text.Mal(n) + " in " + Schwellen.StromverlustTage + " Tagen aus, ohne heruntergefahren zu werden (Stromverlust oder Hänger); ab " + Schwellen.StromverlustWarn + "-mal gilt das als Muster."
                        : "Der PC ging " + Text.Mal(n) + " in " + Schwellen.StromverlustTage + " Tagen aus, ohne heruntergefahren zu werden; unter " + Schwellen.StromverlustWarn + "-mal ist das kein Muster.",
                    Rat = warn
                        ? "Netzteil, Steckdosenleiste und Kabel prüfen; friert der PC vorher ein, deutet das auf Treiber oder Hardware (Temperatur, Arbeitsspeicher) hin."
                        : null,
                    Detail = detail,
                });
            }
            else
            {
                if (geplant.Count > 0)
                    uebersicht.Add("Neustarts mit Kernel-Power 41, aber geplant (User32 1074 kurz davor): " + geplant.Count);
                // Aeltere Abschaltungen liegen ausserhalb des bewerteten Fensters, aber im Protokoll:
                // sie werden genannt, damit die Uebersicht nicht "keine Abschaltung" behauptet.
                aeltere = stromverlust.Count(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.StromverlustAeltereTage));
                if (aeltere > 0)
                    uebersicht.Add("Abschaltungen ohne Herunterfahren zwischen " + Schwellen.StromverlustTage + " und " + Schwellen.StromverlustAeltereTage + " Tagen (außerhalb des " + Schwellen.StromverlustTage + "-Tage-Fensters, nicht bewertet): " + aeltere);
            }
            if (unbewertet > 0)
                uebersicht.Add("Kernel-Power 41 ohne Blauschirm-Code, nicht bewertet, weil die geplanten Neustarts (User32 1074) fehlen: " + unbewertet);

            return befund;
        }

        /// <summary>User32 1074 innerhalb von NeustartAbstand1074Min Minuten VOR dem 41 = geplanter Neustart.</summary>
        static bool Geplant(Ereignis ev41, List<DateTime> zeiten1074)
        {
            var t41 = Zeit.Lesen(ev41.ZeitUtc);
            if (!t41.HasValue) return false;
            foreach (var t in zeiten1074)
            {
                double min = (t41.Value - t).TotalMinutes;
                if (min >= 0 && min <= Schwellen.NeustartAbstand1074Min) return true;
            }
            return false;
        }

        static bool IstFalsch(string v)
        {
            return v != null && (v == "false" || v == "False" || v == "0");
        }

        static string KlassenSatz(List<string> klassen)
        {
            if (klassen.Count == 0) return "";
            return ", die Fehlernummer deutet auf " + string.Join(" oder ", klassen.Select(KlasseAlltag)) + " hin";
        }

        static string KlasseAlltag(string k)
        {
            switch (k)
            {
                case Bugchecks.Treiber: return "einen Treiber";
                case Bugchecks.Ram: return "den Arbeitsspeicher";
                case Bugchecks.Datentraeger: return "die Festplatte";
                case Bugchecks.Hardware: return "die Hardware";
                case Bugchecks.Grafik: return "den Grafiktreiber";
                case Bugchecks.System: return "einen Systemprozess";
                default: return k;
            }
        }

        static string RatJeKlasse(List<string> klassen)
        {
            var teile = new List<string>();
            if (klassen.Contains(Bugchecks.Treiber)) teile.Add("Zuletzt installierte oder aktualisierte Treiber (Grafik, Netzwerk, Audio) beim Hersteller auf eine neuere Version prüfen");
            if (klassen.Contains(Bugchecks.Grafik)) teile.Add("den Grafiktreiber vom Hersteller neu installieren");
            if (klassen.Contains(Bugchecks.Ram)) teile.Add("einen Speichertest laufen lassen (Windows-Speicherdiagnose, braucht einen Neustart)");
            if (klassen.Contains(Bugchecks.Datentraeger)) teile.Add("den Bereich Festplatten beachten und Daten sichern");
            if (klassen.Contains(Bugchecks.Hardware)) teile.Add("Temperaturen, Netzteil und Steckverbindungen prüfen");
            if (klassen.Contains(Bugchecks.System)) teile.Add("die Systemdateien prüfen lassen (SFC und DISM im Werkzeugkasten)");
            if (teile.Count == 0) teile.Add("Die Abbild-Datei aus C:\\Windows\\Minidump vom Hersteller-Support oder mit WinDbg auswerten lassen");
            string rat = string.Join("; ", teile);
            return char.ToUpper(rat[0]) + rat.Substring(1) + ". Wiederholt sich der Absturz, die Abbild-Datei auswerten lassen.";
        }

        // ---------------------------------------------------------------- Application Error 1000

        const string DefenderDienst = "MsMpEng.exe";

        /// <summary>
        /// Dasselbe Programm >= ProgrammabsturzWarn-mal in ProgrammabsturzTage: warn. Gruppiert
        /// ueber das benannte Feld AppName. ntdll/kernelbase als Modul sind laut Doku Opfer,
        /// nicht Ursache. Sind Fehlerberichte (WER) abgeschaltet, fehlen 1001/1002, nicht die 1000.
        /// Zwei Sonderfaelle vor der Gruppierung: MsMpEng.exe (Defender-Engine, eigene Schwelle,
        /// eigener Rat) und der Platzhalter bad_module_info (Windows kennt das Programm nicht).
        /// </summary>
        static void Programmabstuerze(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            var abstuerze = s.Ereignisse.Von("Application Error", 1000).Where(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.ProgrammabsturzTage)).ToList();
            var defender = abstuerze.Where(x => string.Equals(AppName(x), DefenderDienst, StringComparison.OrdinalIgnoreCase)).ToList();
            var platzhalter = abstuerze.Where(x => IstPlatzhalter(AppName(x))).ToList();
            DefenderEngine(e, defender);
            Unbekannt(e, platzhalter);

            var gruppen = abstuerze.Except(defender).Except(platzhalter)
                .GroupBy(x => AppName(x).ToLowerInvariant()).Where(g => g.Count() >= Schwellen.ProgrammabsturzWarn);
            foreach (var g in gruppen.OrderByDescending(g => g.Count()))
            {
                string app = AppName(g.First());
                var module = g.GroupBy(x => x.Feld("ModuleName") ?? x.Feld("3") ?? "?").OrderByDescending(m => m.Count()).ToList();
                var detail = new List<string>();
                var treiberModule = new List<string>();
                bool windowsModul = false;
                foreach (var m in module)
                {
                    string zeile = "Modul " + m.Key + ": " + Text.Mal(m.Count());
                    string ml = m.Key.ToLowerInvariant();
                    string pfad = m.Select(x => x.Feld("ModulePath") ?? x.Feld("11")).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
                    // Treiberordner VOR dem System32-Test: der DriverStore liegt unter System32, und
                    // SFC/DISM pruefen Fremdtreiberpakete nicht (gemessen: nvml.dll von NVIDIA).
                    if (IstTreiberpfad(pfad)) { zeile += " (Treibermodul, " + pfad + ")"; treiberModule.Add(m.Key); }
                    else if (ml.StartsWith("ntdll") || ml.StartsWith("kernelbase") || ml.StartsWith("kernel32")) { zeile += " (Windows-Modul: Opfer, nicht Ursache)"; windowsModul = true; }
                    else if (pfad.IndexOf("\\System32\\", StringComparison.OrdinalIgnoreCase) >= 0) windowsModul = true;
                    if (IstPlatzhalter(ml)) zeile += " (Modul unbekannt)";
                    detail.Add(zeile);
                }
                foreach (var x in g.OrderByDescending(x => x.ZeitUtc).Take(5))
                    detail.Add(Datum(x.ZeitUtc) + ": Ausnahmecode " + (x.Feld("ExceptionCode") ?? x.Feld("6") ?? "?") + ", Version " + (x.Feld("AppVersion") ?? x.Feld("1") ?? "?") + ", Pfad " + (x.Feld("AppPath") ?? x.Feld("10") ?? "?"));
                int haenger = s.Ereignisse.Von("Application Hang", 1002).Count(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.ProgrammabsturzTage) && string.Equals(x.Feld("AppName") ?? x.Feld("0"), app, StringComparison.OrdinalIgnoreCase));
                if (haenger > 0) detail.Add("Dazu " + Text.Mal(haenger) + " eingefroren (Application Hang 1002)");
                if (s.Sicherheit != null && s.Sicherheit.WerDeaktiviert == true)
                    detail.Add("Fehlerberichte (Windows Error Reporting) sind auf diesem PC abgeschaltet: Berichte 1001/1002 fehlen deshalb, die Abstürze selbst nicht.");

                string rat;
                if (treiberModule.Count > 0)
                    rat = "Das abgestürzte Modul „" + treiberModule[0] + "“ ist ein Gerätetreiber: den Treiber des Herstellers aktualisieren oder neu installieren, danach das Programm aktualisieren. SFC und DISM prüfen Treiberpakete des Herstellers nicht."
                        + (windowsModul ? " Da auch ein Windows-Modul beteiligt ist, zusätzlich die Systemdateien prüfen lassen (SFC und DISM im Werkzeugkasten)." : "");
                else if (windowsModul)
                    rat = "Das Programm aktualisieren oder neu installieren; da ein Windows-Modul beteiligt ist, zusätzlich die Systemdateien prüfen lassen (SFC und DISM im Werkzeugkasten).";
                else
                    rat = "Das Programm aktualisieren oder neu installieren; hilft das nicht, beim Hersteller nach dem Fehler fragen (Modul und Ausnahmecode stehen in den Einzelheiten).";

                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.programmabsturz." + app.ToLowerInvariant(),
                    Zustand = Zustand.Warn,
                    Messwert = Messwert.Von(g.Count(), "Abstürze in " + Schwellen.ProgrammabsturzTage + " Tagen", Schwellen.ProgrammabsturzWarn + " warn"),
                    Quelle = "Application Error 1000.AppName",
                    Titel = "Programm stürzt wiederholt ab",
                    Satz = "„" + app + "“ ist in den letzten " + Schwellen.ProgrammabsturzTage + " Tagen " + Text.Mal(g.Count()) + " abgestürzt, ab " + Schwellen.ProgrammabsturzWarn + "-mal gilt das als Muster.",
                    Rat = rat,
                    Detail = detail,
                });
            }
        }

        static string AppName(Ereignis x) { return x.Feld("AppName") ?? x.Feld("0") ?? "?"; }

        /// <summary>bad_module_info / unknown: Windows kennt das abgestuerzte Programm nicht (gemessen 05.09.2026). Kein Programmname, kein Neuinstallations-Rat.</summary>
        static bool IstPlatzhalter(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n == "bad_module_info" || n == "unknown";
        }

        /// <summary>\DriverStore\ (Fremdtreiberpakete, unter System32) und \System32\drivers\ (Kerntreiber).</summary>
        static bool IstTreiberpfad(string pfad)
        {
            if (string.IsNullOrEmpty(pfad)) return false;
            return pfad.IndexOf("\\DriverStore\\", StringComparison.OrdinalIgnoreCase) >= 0
                || pfad.IndexOf("\\System32\\drivers\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Konzept 4.3: 1000 mit MsMpEng.exe >= DefenderEngineAbsturzWarn in ProgrammabsturzTage -> warn,
        /// Massnahme Signaturen aktualisieren. Die Pruef-Engine (mpengine.dll) kommt mit den Signaturen;
        /// "neu installieren" gibt es fuer diesen Dienst nicht.
        /// </summary>
        static void DefenderEngine(BereichErgebnis e, List<Ereignis> defender)
        {
            if (defender.Count < Schwellen.DefenderEngineAbsturzWarn) return;
            int n = defender.Count;
            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Stabilitaet,
                Schluessel = "stabilitaet.defender.engine",
                Zustand = Zustand.Warn,
                Messwert = Messwert.Von(n, "Abstürze des Virenschutz-Dienstes in " + Schwellen.ProgrammabsturzTage + " Tagen", Schwellen.DefenderEngineAbsturzWarn + " warn"),
                Quelle = "Application Error 1000.AppName = " + DefenderDienst,
                Titel = "Virenschutz-Dienst von Windows stürzt ab",
                Satz = "Der Dienst des Windows-Virenschutzes (" + DefenderDienst + ") ist in den letzten " + Schwellen.ProgrammabsturzTage + " Tagen " + Text.Mal(n) + " abgestürzt; ab " + Schwellen.DefenderEngineAbsturzWarn + "-mal deutet das auf eine beschädigte Prüf-Engine hin.",
                Rat = "Die Prüf-Engine ist Teil von Windows und wird mit den Signatur- und Plattform-Updates ersetzt: Signaturen aktualisieren, danach Windows Update ausführen. Neu installieren lässt sich der Dienst nicht.",
                Detail = defender.OrderByDescending(x => x.ZeitUtc).Take(10)
                    .Select(x => Datum(x.ZeitUtc) + ": Modul " + (x.Feld("ModuleName") ?? x.Feld("3") ?? "?") + " (" + (x.Feld("ModulePath") ?? x.Feld("11") ?? "Pfad unbekannt") + "), Ausnahmecode " + (x.Feld("ExceptionCode") ?? x.Feld("6") ?? "?")).ToList(),
                Massnahmen = new List<string> { Sicherheit.MassnahmeSignaturen },
            });
        }

        /// <summary>Platzhalter-Gruppe: fester Schluessel, nur Zeitpunkte, kein Hang-Abgleich, kein Neuinstallations-Rat.</summary>
        static void Unbekannt(BereichErgebnis e, List<Ereignis> platzhalter)
        {
            if (platzhalter.Count < Schwellen.ProgrammabsturzWarn) return;
            int n = platzhalter.Count;
            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Stabilitaet,
                Schluessel = "stabilitaet.programmabsturz.unbekannt",
                Zustand = Zustand.Warn,
                Messwert = Messwert.Von(n, "Abstürze ohne Programmnamen in " + Schwellen.ProgrammabsturzTage + " Tagen", Schwellen.ProgrammabsturzWarn + " warn"),
                Quelle = "Application Error 1000.AppName = bad_module_info",
                Titel = "Abstürze ohne erkennbares Programm",
                Satz = "Windows hat in den letzten " + Schwellen.ProgrammabsturzTage + " Tagen " + Text.Mal(n) + " einen Absturz gemeldet, ohne das Programm zu erkennen; das können verschiedene Programme sein, häufig Spiele mit Anti-Cheat-Schutz oder Programme beim Herunterfahren.",
                Rat = "Welches Programm das war, steht nicht im Protokoll; die Zeitpunkte in den Einzelheiten mit dem vergleichen, was damals lief.",
                Detail = platzhalter.OrderByDescending(x => x.ZeitUtc).Take(10)
                    .Select(x => Datum(x.ZeitUtc) + ": Platzhalter „" + AppName(x) + "“ statt Programmname").ToList(),
            });
        }

        // ---------------------------------------------------------------- WHEA

        /// <summary>
        /// Level 2 (schwerwiegend) >= WheaFatalBad in 90 Tagen: bad. Level 3 (behoben) >= WheaKorrigierbarWarn
        /// in 30 Tagen: warn. Konzept 4.3: Rat, keine Massnahme - der Speichertest gehoert der Blauschirm-Regel
        /// mit Klasse Arbeitsspeicher; ein PCIe-Fehler braucht keinen Speichertest.
        /// </summary>
        static void Whea(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            var whea = s.Ereignisse.Von("Microsoft-Windows-WHEA-Logger").ToList();
            var fatal = whea.Where(x => x.Level == 2 && ctx.Innerhalb(x.ZeitUtc, s.Ereignisse.Tage)).ToList();
            var behoben = whea.Where(x => x.Level == 3 && ctx.Innerhalb(x.ZeitUtc, Schwellen.WheaKorrigierbarTage)).ToList();

            if (fatal.Count >= Schwellen.WheaFatalBad)
            {
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.hardwarefehler.schwer",
                    Zustand = Zustand.Bad,
                    Messwert = Messwert.Von(fatal.Count, "schwerwiegende Hardwarefehler in " + s.Ereignisse.Tage + " Tagen", Schwellen.WheaFatalBad + " bad"),
                    Quelle = "WHEA-Logger Level 2",
                    Titel = "Schwerwiegender Hardwarefehler gemeldet",
                    Satz = "Windows hat " + Text.Mal(fatal.Count) + " " + Zeitraum(ctx) + " einen nicht behebbaren Hardwarefehler gemeldet (Prozessor, Arbeitsspeicher oder PCI-Gerät).",
                    Rat = "Daten sichern; Temperaturen, Arbeitsspeicher (Speichertest) und Steckverbindungen prüfen; bei Wiederholung den Hersteller einschalten.",
                    Detail = fatal.OrderByDescending(x => x.ZeitUtc).Take(10).Select(x => Datum(x.ZeitUtc) + ": WHEA " + x.Id + ", ErrorSource " + (x.Feld("ErrorSource") ?? "?") + WheaOrt(x)).ToList(),
                });
            }
            if (behoben.Count >= Schwellen.WheaKorrigierbarWarn)
            {
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.hardwarefehler.behoben",
                    Zustand = Zustand.Warn,
                    Messwert = Messwert.Von(behoben.Count, "behobene Hardwarefehler in " + Schwellen.WheaKorrigierbarTage + " Tagen", Schwellen.WheaKorrigierbarWarn + " warn"),
                    Quelle = "WHEA-Logger Level 3",
                    Titel = "Hardware meldet häufig behobene Fehler",
                    Satz = "Die Hardware hat in " + Schwellen.WheaKorrigierbarTage + " Tagen " + Text.Mal(behoben.Count) + " einen Fehler gemeldet, den Windows noch abfangen konnte; ab " + Schwellen.WheaKorrigierbarWarn + "-mal ist das ein Frühwarnzeichen.",
                    Rat = "Je nach Quelle Arbeitsspeicher, Steckkarte oder Prozessor: Speichertest laufen lassen, Module und Karten neu einsetzen, Übertaktung zurücknehmen.",
                    Detail = behoben.GroupBy(x => x.Id).Select(g => "WHEA " + g.Key + ": " + Text.Mal(g.Count()) + WheaOrt(g.First())).ToList(),
                });
            }
        }

        static string WheaOrt(Ereignis x)
        {
            string t = "";
            if (x.Feld("ApicId") != null) t += ", ApicId " + x.Feld("ApicId");
            if (x.Feld("MCABank") != null) t += ", MCA-Bank " + x.Feld("MCABank");
            if (x.Feld("PhysicalAddress") != null) t += ", Adresse " + x.Feld("PhysicalAddress");
            if (x.Feld("Bus") != null) t += ", Bus " + x.Feld("Bus") + " Gerät " + x.Feld("Device") + " Funktion " + x.Feld("Function");
            return t;
        }

        // ---------------------------------------------------------------- Kernel-PnP 219 / 411

        /// <summary>
        /// Verlauf, kein Befund (Konzept 4.1): ein 219 je Ansteckvorgang eines Telefons ist normal.
        /// Widerlegungsrunde SP-12: bei 219 traegt DriverName die GERAETEINSTANZ, FailureName den
        /// Treiber; 411 traegt DeviceInstanceId, Problem, Status. Je Geraet eine Zeile mit Zahl.
        /// </summary>
        static void KernelPnpVerlauf(Kontext ctx, List<string> detail)
        {
            var s = ctx.S;
            int tage = Schwellen.KernelPnp219Tage;
            var e219 = s.Ereignisse.Von("Microsoft-Windows-Kernel-PnP", 219).Where(x => ctx.Innerhalb(x.ZeitUtc, tage)).ToList();
            foreach (var g in e219.GroupBy(x => x.Feld("DriverName") ?? x.Feld("1") ?? "?").OrderByDescending(g => g.Count()).Take(5))
            {
                string instanz = g.Key;
                string treiber = g.Select(x => x.Feld("FailureName") ?? x.Feld("4")).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "?";
                var status = g.Select(x => x.FeldZahl("Status", 0)).Where(v => v != 0).Distinct().Select(v => "0x" + v.ToString("X8", CultureInfo.InvariantCulture)).ToList();
                var geraet = s.Geraete.FirstOrDefault(d => string.Equals(d.InstanzId, instanz, StringComparison.OrdinalIgnoreCase));
                string name = geraet != null && !string.IsNullOrEmpty(geraet.Name) ? "„" + geraet.Name + "“ (" + instanz + ")" : instanz;
                detail.Add("Treiberstart-Verlauf (Kernel-PnP 219) in " + tage + " Tagen: " + Text.Mal(g.Count()) + " für " + name
                    + ", Treiber " + treiber + ", Status " + (status.Count > 0 ? string.Join(", ", status) : "nicht angegeben")
                    + (geraet == null ? ", Gerät derzeit nicht in der Geräteliste" : ", Gerät derzeit " + (geraet.Present ? "vorhanden" : "nicht vorhanden") + ", Problemcode " + geraet.ProblemCode)
                    + "; nur Verlauf, kein Befund");
            }
            var e411 = s.Ereignisse.Von("Microsoft-Windows-Kernel-PnP", 411).Where(x => ctx.Innerhalb(x.ZeitUtc, tage)).ToList();
            foreach (var g in e411.GroupBy(x => x.Feld("DeviceInstanceId") ?? x.Feld("0") ?? "?").OrderByDescending(g => g.Count()).Take(5))
            {
                var probleme = g.Select(x => x.Feld("Problem")).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                var status = g.Select(x => x.FeldZahl("Status", 0)).Where(v => v != 0).Distinct().Select(v => "0x" + v.ToString("X8", CultureInfo.InvariantCulture)).ToList();
                detail.Add("Gerätestart-Verlauf (Kernel-PnP 411) in " + tage + " Tagen: " + Text.Mal(g.Count()) + " für " + g.Key
                    + ", Problem " + (probleme.Count > 0 ? string.Join(", ", probleme) : "nicht angegeben")
                    + ", Status " + (status.Count > 0 ? string.Join(", ", status) : "nicht angegeben") + "; nur Verlauf, kein Befund");
            }
        }

        // ---------------------------------------------------------------- Uebersicht

        /// <summary>
        /// Ohne Neustart-Befund gibt es einen ok-Befund mit dem Zeitraum: bei jungem Log sagt der
        /// Satz "seit Beginn des Protokolls vor N Tagen", nie "keine Abstürze in 90 Tagen"; liegen
        /// Abschaltungen ausserhalb des 30-Tage-Fensters, nennt er ihre Zahl statt "keine".
        /// Ist die Hauptquelle gescheitert (unvollstaendig != null), ist die Uebersicht unknown.
        /// Zuverlaessigkeitsindex, CPU-Drosselung und Kernel-PnP-Verlauf stehen als Einzelheiten dabei.
        /// </summary>
        static void Uebersicht(Kontext ctx, BereichErgebnis e, bool neustartBefund, List<string> detail, int aeltere, string unvollstaendig)
        {
            var s = ctx.S;
            if (s.Zuverlaessigkeit != null && s.Zuverlaessigkeit.Index.HasValue)
                detail.Add("Zuverlässigkeitsindex von Windows: " + s.Zuverlaessigkeit.Index.Value.ToString("N1", Text.De) + " von 10 (Stand " + Datum(s.Zuverlaessigkeit.ZeitUtc) + ")");
            var drossel = s.Ereignisse.Von("Microsoft-Windows-Kernel-Processor-Power", 37).Where(x => ctx.Innerhalb(x.ZeitUtc, s.Ereignisse.Tage)).ToList();
            if (drossel.Count > 0)
                detail.Add("Prozessor durch Firmware gedrosselt (Kernel-Processor-Power 37): " + Text.Mal(drossel.Count) + ", zusammen " + drossel.Sum(x => x.FeldZahl("CapDurationInSeconds", 0)) + " s");
            int starts = s.Ereignisse.Von("Microsoft-Windows-Kernel-General", 12).Count(x => ctx.Innerhalb(x.ZeitUtc, s.Ereignisse.Tage));
            if (starts > 0) detail.Add("Starts von Windows " + Zeitraum(ctx) + ": " + starts);
            var startdauer = s.Ereignisse.Von("Microsoft-Windows-Diagnostics-Performance", 100).OrderByDescending(x => x.ZeitUtc).FirstOrDefault();
            if (startdauer != null)
                detail.Add("Letzte gemessene Startdauer (" + Datum(startdauer.ZeitUtc) + "): " + startdauer.FeldZahl("BootTime", 0) + " ms, davon Hauptpfad " + startdauer.FeldZahl("MainPathBootTime", 0) + " ms");

            if (neustartBefund)
            {
                // Die Einzelheiten haengen an den Neustart-Befunden, damit die Uebersicht nichts doppelt.
                var erster = e.Befunde.FirstOrDefault(b => b.Schluessel == "stabilitaet.blauschirm" || b.Schluessel == "stabilitaet.stromverlust" || b.Schluessel == "stabilitaet.einschalttaste");
                if (erster != null) erster.Detail.AddRange(detail);
                return;
            }
            if (unvollstaendig != null)
            {
                e.Befunde.Add(new Befund
                {
                    Bereich = Bereich.Stabilitaet,
                    Schluessel = "stabilitaet.uebersicht",
                    Zustand = Zustand.Unknown,
                    Messwert = null,
                    Quelle = "Kernel-Power 41",
                    Titel = "Unerwartete Neustarts",
                    Satz = "Ob es Blauschirme oder Abschaltungen ohne Herunterfahren gab, ließ sich nicht vollständig prüfen: " + unvollstaendig + ".",
                    Rat = "Die Prüfung wiederholen; bleibt die Quelle stumm, das Protokoll System in der Ereignisanzeige selbst ansehen (Kernel-Power, Ereignis 41).",
                    Detail = detail,
                });
                return;
            }
            string satz = aeltere == 0
                ? "Kein Blauschirm und keine Abschaltung ohne Herunterfahren " + Zeitraum(ctx) + "."
                : "Kein Blauschirm " + Zeitraum(ctx) + "; " + (aeltere == 1 ? "eine Abschaltung ohne Herunterfahren liegt" : aeltere + " Abschaltungen ohne Herunterfahren liegen")
                  + " länger als " + Schwellen.StromverlustTage + " Tage zurück und werden nicht bewertet.";
            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Stabilitaet,
                Schluessel = "stabilitaet.uebersicht",
                Zustand = Zustand.Ok,
                Messwert = aeltere == 0
                    ? Messwert.Von(0, "unerwartete Neustarts", Schwellen.BlauschirmWarn + " warn")
                    : Messwert.Von(aeltere, "Abschaltungen ohne Herunterfahren, älter als " + Schwellen.StromverlustTage + " Tage", "nicht bewertet (Fenster " + Schwellen.StromverlustTage + " Tage)"),
                Quelle = "Kernel-Power 41",
                Titel = "Unerwartete Neustarts",
                Satz = satz,
                Rat = null,
                Detail = detail,
            });
        }

        // ---------------------------------------------------------------- Helfer

        /// <summary>ISO-UTC als "11.09.2026 16:00 UTC": sprachneutral genug, ohne Zeitzonen-Raten.</summary>
        static string Datum(string isoUtc)
        {
            var t = Zeit.Lesen(isoUtc);
            return t.HasValue ? t.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC" : "unbekannt";
        }
    }
}
