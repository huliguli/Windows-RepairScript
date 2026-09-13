using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Einstieg des erhoehten Helfers (docs/M2-ENTWURF.md, Abschnitte 0 und 11). Program.Main
    /// springt hierher, sobald "--helfer" in der Kommandozeile steht – vor der Oberflaeche, vor
    /// WinForms. Drei Modi, alle ohne Fenster:
    ///
    ///   --helfer --pipe &lt;name&gt; --sid &lt;sid&gt;      Named Pipe fuer den Host bedienen (Normalfall, per runas gestartet)
    ///   --helfer --plan &lt;datei&gt; [--trocken]     Plan aus einer JSON-Datei ausfuehren, Exit = PlanErgebnis.Exit (Abnahme, --auto)
    ///   --helfer --messen &lt;ausgabe.json&gt;        erhoehte Messung, unredigiert in die Datei (Abnahme)
    ///
    /// Ergebnis und Fehler stehen im Exit-Code und in logs\app.log ("helfer: ..."). Ein Plan
    /// (Pipe oder --plan) bekommt sein Laufprotokoll lauf-&lt;planId&gt;.jsonl; die Pipe-Sitzung
    /// selbst schreibt keine lauf-Datei (Nachtrag 13.09.2026). Nie eine MessageBox: der Prozess
    /// laeuft erhoeht und oft ohne Sitzung.
    /// </summary>
    public static class Helfer
    {
        /// <summary>Nach so viel Zeit ohne Anfrage beendet sich der Pipe-Helfer von selbst.</summary>
        public const int LeerlaufMs = 10 * 60 * 1000;

        // Exit-Codes: 0 ok, 1 Plan mit Problem, 2 abgelehnt, 3 Ausnahme, 4 Pipe/Sicherheit, 7 Argumente.
        // 1 und 2 kommen aus PlanErgebnis.Exit (kern/Plan.cs).
        public const int ExitOk = 0;
        public const int ExitAusnahme = 3;
        public const int ExitPipe = 4;
        public const int ExitArgumente = 7;

        /// <summary>args ist die ganze Kommandozeile (mit "--helfer"). Rueckgabe = Exit-Code des Prozesses.</summary>
        public static int Starten(string[] args)
        {
            try
            {
                // Kein WinForms hier: nur das Auffangnetz der AppDomain, damit ein Absturz im
                // Arbeitsthread wenigstens eine Zeile hinterlaesst.
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    AppLog.Error("helfer: unbehandelte Ausnahme", e.ExceptionObject as Exception);

                string pipe = null, sid = null, plan = null, messen = null;
                bool trocken = false;
                var unbekannt = new List<string>();
                for (int i = 0; i < (args == null ? 0 : args.Length); i++)
                {
                    string a = args[i];
                    if (a == "--helfer") continue;
                    if (a == "--trocken") { trocken = true; continue; }
                    bool hatWert = i + 1 < args.Length;
                    if (a == "--pipe" && hatWert) pipe = args[++i];
                    else if (a == "--sid" && hatWert) sid = args[++i];
                    else if (a == "--plan" && hatWert) plan = args[++i];
                    else if (a == "--messen" && hatWert) messen = args[++i];
                    else unbekannt.Add(a);
                }

                int modi = (pipe != null ? 1 : 0) + (plan != null ? 1 : 0) + (messen != null ? 1 : 0);
                string grund = null;
                if (unbekannt.Count > 0) grund = "unbekannte oder unvollständige Argumente: " + string.Join(" ", unbekannt);
                else if (modi != 1) grund = "genau einer von --pipe, --plan, --messen ist nötig, " + modi + " angegeben";
                else if (pipe != null && sid == null) grund = "--pipe braucht --sid";
                else if (pipe != null && !Pipe.NameGueltig(pipe)) grund = "Pipe-Name unzulässig (erlaubt: Buchstaben, Ziffern, . - _; 3 bis 100 Zeichen)";
                else if (pipe != null && !SidGueltig(sid)) grund = "--sid muss die SID eines Benutzerkontos sein (S-1-5-21-… oder S-1-12-1-…), keine Gruppe und kein Systemkonto";
                else if (trocken && plan == null) grund = "--trocken gilt nur mit --plan";
                if (grund != null)
                {
                    AppLog.Error("helfer: Start abgelehnt, " + grund + ". Aufruf: " + string.Join(" ", args ?? new string[0]));
                    return ExitArgumente;
                }

                AppLog.Info("helfer: Start (Version " + Version() + ", erhöht " + (Sammler.Quellen.Rechte.Erhoeht() ? "ja" : "nein")
                            + ", Modus " + (pipe != null ? "pipe" : plan != null ? "plan" : "messen") + ")");

                // Bei jedem Start: der maschinenweite Ordner bleibt fuer den nicht erhoehten Host
                // beschreibbar (sonst gehoeren neue Dateien der Administratorengruppe).
                bool rechte = Ablage.RechteSichern();
                if (!rechte) AppLog.Warn("helfer: Rechte auf " + Ablage.Maschinenweit() + " konnten nicht gesetzt werden (nicht erhöht oder kein Zugriff).");

                if (pipe != null) return PipeModus(pipe, sid, rechte);
                if (plan != null) return PlanModus(plan, trocken);
                return MessenModus(messen);
            }
            catch (Exception ex)
            {
                AppLog.Error("helfer: Ausnahme", ex);
                return ExitAusnahme;
            }
        }

        // ------------------------------------------------------------------ Modi

        static int PipeModus(string pipe, string sid, bool rechte)
        {
            // Keine lauf-Datei je UAC-Start: die Sitzungszeilen (gestartet, verbunden, beendet)
            // gehen nur nach app.log, sonst stuende nach jedem Start eine Datei mit drei Zeilen
            // in der Protokollliste des Hosts. Ein Plan ueber die Pipe schreibt sein eigenes.
            AppLog.Info("helfer: gestartet, wartet auf das Hauptprogramm (pipe=" + pipe + ", sid=" + sid
                        + ", version=" + Version() + ", erhoeht=" + (Sammler.Quellen.Rechte.Erhoeht() ? "1" : "0")
                        + ", rechteGesetzt=" + (rechte ? "1" : "0") + ", ordner=" + Ablage.Maschinenweit() + ")");

            int exit = new PipeServer(pipe, sid, null).Bedienen();

            AppLog.Info("helfer: beendet, Exit " + exit + ".");
            return exit;
        }

        static int PlanModus(string datei, bool trocken)
        {
            if (!File.Exists(datei))
            {
                AppLog.Error("helfer: Plandatei fehlt: " + datei);
                return ExitArgumente;
            }
            Plan plan;
            try { plan = Json.LesenDatei<Plan>(datei); }
            catch (Exception ex)
            {
                AppLog.Error("helfer: Plandatei " + datei + " nicht lesbar", ex);
                return ExitArgumente;
            }
            if (plan == null)
            {
                AppLog.Error("helfer: Plandatei " + datei + " enthält keinen Plan.");
                return ExitArgumente;
            }
            // Die Plan-Id wird Dateiname (lauf-<planId>.jsonl). Aus der Datei darf keine mit
            // Pfadzeichen kommen; anders als die Pipe ersetzt --plan sie, statt abzulehnen
            // (Nachtrag 13.09.2026): die Datei hat der Betreiber selbst geschrieben.
            if (!Protokoll.LaufIdGueltig(plan.Id))
            {
                string alt = (plan.Id ?? "").Replace("\r", " ").Replace("\n", " ");
                if (alt.Length > 120) alt = alt.Substring(0, 120) + "…";
                plan.Id = Protokoll.NeueLaufId();
                AppLog.Warn("helfer: Plan-Id „" + alt + "“ ist unzulässig (erlaubt: Buchstaben, Ziffern, . - _; 1 bis 64 Zeichen), ersetzt durch " + plan.Id + ".");
            }

            // Zeilen und Fortschritt landen in app.log; das Laufprotokoll lauf-<planId>.jsonl
            // schreibt die Ausfuehrung selbst (anfang/schritt/ende, Schicht helfer). Besteller
            // ist das eigene Konto (HKCU-Regel in registrierung.entfernen).
            var k = new Ausfuehrungskontext
            {
                Protokoll = new Protokoll(plan.Id),
                Trocken = trocken,
                AufruferSid = Sammler.Quellen.Rechte.EigeneSid(),
                Zeile = (text, art, prozent) =>
                {
                    if (string.IsNullOrEmpty(text)) return;   // reine Fortschrittszeilen fuellen kein Log
                    AppLog.Info("helfer: " + (art == null || art == "normal" ? "" : "[" + art + "] ") + text);
                },
                Fortschritt = (schritt, gesamt, text) =>
                    AppLog.Info("helfer: Schritt " + schritt + " von " + gesamt + ": " + (text ?? "")),
            };

            AppLog.Info("helfer: Plan „" + (plan.Titel ?? plan.Id) + "“ aus " + datei + " mit " + plan.Schritte.Count + " Schritten"
                        + (trocken ? " (Trockenlauf)" : "") + ", Protokoll " + (k.Protokoll.Pfad ?? "(nur im Speicher)"));

            PlanErgebnis erg = Ausfuehrung.PlanAusfuehren(plan, k);

            AppLog.Info("helfer: Plan " + plan.Id + " beendet, Exit " + erg.Exit
                        + (erg.Problem ? ", mit Problem" : "") + (erg.Abgebrochen ? ", abgebrochen" : "")
                        + (string.IsNullOrEmpty(erg.Grund) ? "" : ", Grund: " + erg.Grund)
                        + ", " + erg.Sekunden.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s.");
            return erg.Exit;
        }

        static int MessenModus(string ausgabe)
        {
            if (string.IsNullOrEmpty(ausgabe))
            {
                AppLog.Error("helfer: --messen braucht einen Dateipfad.");
                return ExitArgumente;
            }
            var protokoll = new Protokoll(Protokoll.NeueLaufId());
            protokoll.Schreibe(Protokoll.Helfer, Protokoll.Anfang, "messen", "Die Messung mit Administratorrechten beginnt und wird in eine Datei geschrieben",
                new { ausgabe, erhoeht = Sammler.Quellen.Rechte.Erhoeht() });

            Systembild bild = Messung.Erfassen(text => AppLog.Info("helfer: messe " + text), protokoll);
            string json = Messung.AlsJson(bild);

            string ordner = Path.GetDirectoryName(Path.GetFullPath(ausgabe));
            if (!string.IsNullOrEmpty(ordner)) Directory.CreateDirectory(ordner);
            File.WriteAllText(ausgabe, json, new UTF8Encoding(false));

            protokoll.Schreibe(Protokoll.Helfer, Protokoll.Ende, "messen", "Die Messung ist fertig und in die Datei geschrieben, " + bild.Fehlerliste.Count + " Quellen meldeten einen Fehler",
                new { ausgabe, zeichen = json.Length, fehler = bild.Fehlerliste.Count, erhoeht = bild.Erhoeht });
            AppLog.Info("helfer: Messung geschrieben: " + ausgabe + " (" + json.Length + " Zeichen, " + bild.Fehlerliste.Count + " Fehlereinträge).");
            return ExitOk;
        }

        // ------------------------------------------------------------------ Hilfen

        /// <summary>
        /// Wohlgeformt UND ein Konto: IsAccountSid (S-1-5-21-…, lokale und Domaenenkonten) oder die
        /// Form S-1-12-1-… (Entra-ID-Konten; IsAccountSid kennt sie unter .NET Framework nicht, am
        /// 13.09.2026 belegt). "Jeder" (S-1-1-0), Gruppen (S-1-5-32-544) und SYSTEM (S-1-5-18) sind
        /// damit draussen: mit so einer SID wuerde der Helfer jeden Aufrufer ablehnen, also lieber
        /// sofort Exit 7 mit klarer Zeile als Exit 4 beim ersten Client.
        /// </summary>
        static bool SidGueltig(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            try
            {
                var s = new SecurityIdentifier(sid);
                return s.IsAccountSid() || sid.StartsWith("S-1-12-1-", StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        static string Version()
        {
            try { return typeof(Helfer).Assembly.GetName().Version.ToString(); }
            catch (Exception) { return "0.0.0.0"; }
        }
    }
}
