using System;
using System.Collections.Generic;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Regeln fuer Geraete und Treiber, Windows-Support und Firmware. Reine Funktion ueber dem
    /// Systembild: kein Windows-Zugriff, kein DateTime.Now (ctx.Jetzt ist die
    /// Aufzeichnungszeit), keine Zahl als Schwelle ausser aus Schwellen.cs.
    ///
    /// Grundsatz 1 (Absicht ist kein Fehler): Problemcodes der Klasse "gewollt" (22 deaktiviert,
    /// 29 BIOS, 32 Dienst, 44 angehalten, 53 Debugger, 55 DMA-Schutz, 24 entfernt, 41
    /// Diensteintrag ohne Geraet, 51 wartet auf ein anderes Geraet, 57 einer virtuellen Maschine
    /// zugewiesen) werden nie als Defekt gemeldet. Ohne Antwort fragt das System (Zustand unknown
    /// mit Frage); mit Antwort "absicht" ist der Zustand absicht; mit Antwort "reparieren" ein
    /// normaler Befund mit Massnahme. Die Frage-Kennung traegt den Code
    /// ("geraet:&lt;instanzId&gt;:22"): ein anderer Code ist eine neue Frage.
    ///
    /// Grundsatz "keine erfundenen Diagnosen": nur die 57 dokumentierten Codes aus
    /// Problemcodes.cs werden gedeutet. Ein unbekannter Code bleibt unknown, nie bad.
    /// Elternschaft kommt nur aus dem Feld parent (DEVPKEY_Device_Parent); fehlt es, sagt das
    /// Detail "nicht ermittelbar" und raet nichts aus dem Instanzpfad. Auch die Steckbarkeit
    /// wird nicht aus dem Pfad geraten: der Rat nennt sie als Bedingung ("ein Steckgeraet").
    ///
    /// Der Befund windows.support (Ende der Sicherheitsupdates) steht hier und nicht im Bereich
    /// Sicherheit, wo Konzept 3.3/4.4 ihn fuehrt: Entscheidung des Auftraggebers vom
    /// 2026-09-13 ("SupportEnde bleibt Aufgabe des Bereichs hardware").
    /// </summary>
    public static class Hardware
    {
        public const string QuellePnp = "wmi.pnp";

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var e = new BereichErgebnis { Bereich = Bereich.Hardware };

            // Daten sind da, wenn die Geraeteliste selbst geliefert hat. Ein Zeit- oder
            // Rechtefehler an der Grundabfrage "wmi.pnp" heisst: keine Aussage. Fehler an
            // "wmi.pnp.eigenschaften" (Einzelheiten eines Geraets) zaehlen nicht dagegen: das
            // Geraet steht mit seinem Code in der Liste, nur ohne Treiber- und Elternangaben.
            bool grundabfrageGescheitert = s.Fehlerliste.Any(f => f.Quelle == QuellePnp && (f.Art == Fehler.Zugriff || f.Art == Fehler.Zeit));
            e.DatenVorhanden = s.Geraete.Count > 0 && !grundabfrageGescheitert;
            if (!e.DatenVorhanden)
            {
                // Der Rohtext der Quelle (Ausnahmetyp, WMI-Klasse) bleibt in der fehlerliste; hier
                // steht die Laienfassung, wie bei Datentraeger.Grund().
                var f = s.FehlerVon(QuellePnp).FirstOrDefault();
                e.Fehlend.Add("Geräteliste" + (f == null ? "" : f.Art == Fehler.Zugriff ? " (keine Rechte)" : f.Art == Fehler.Zeit ? " (Zeitüberschreitung)" : " (Fehler beim Lesen)"));
            }

            if (e.DatenVorhanden) GeraetePruefen(ctx, e);
            WindowsSupportPruefen(ctx, e);
            FirmwareHinweis(ctx, e);
            return e;
        }

        // ------------------------------------------------------------------ Geraete

        static void GeraetePruefen(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            int praesent = 0, mitBefund = 0;
            var harmlos = new List<Geraet>();
            foreach (var g in s.Geraete)
            {
                if (!g.Present) continue;   // WMI liefert ohnehin nur Praesente; ein Testbild darf es anders sagen
                praesent++;
                if (g.ProblemCode == 0) continue;
                var b = GeraetBefund(ctx, g);
                if (b == null) { harmlos.Add(g); continue; }   // harmlos (45, 47, 20): kein Befund, aber im Sammelsatz genannt
                mitBefund++;
                e.Befunde.Add(b);
            }

            if (mitBefund == 0)
            {
                // Der Messwert bleibt die Zahl der angeschlossenen Geraete; ein Geraet mit
                // harmlosem Code (zum Auswurf vorbereitet) ist angeschlossen und in Ordnung,
                // laeuft aber nicht "ohne Fehlercode" - der Satz sagt das.
                var ok = new Befund
                {
                    Bereich = Bereich.Hardware,
                    Schluessel = "geraete.ok",
                    Zustand = Zustand.Ok,
                    Messwert = Messwert.Von(praesent, "Geräte", "0 mit Fehler (45, 47, 20 zählen nicht)"),
                    Quelle = "Win32_PnPEntity.ConfigManagerErrorCode",
                    Titel = "Alle Geräte laufen",
                    Satz = SammelSatz(praesent, harmlos),
                    Rat = null,
                };
                foreach (var g in harmlos)
                {
                    var eintrag = Problemcodes.Von(g.ProblemCode);
                    ok.Detail.Add("Harmloser Code: „" + Anzeigename(g) + "“ meldet " + g.ProblemCode + " (CM_PROB_" + eintrag.Name + "): " + eintrag.Bedeutung);
                }
                e.Befunde.Add(ok);
            }
        }

        static string SammelSatz(int praesent, List<Geraet> harmlos)
        {
            if (harmlos.Count == 0) return "Alle " + praesent + " angeschlossenen Geräte laufen ohne Fehlercode.";
            int ohne = praesent - harmlos.Count;
            if (harmlos.Count == 1)
            {
                var g = harmlos[0];
                var eintrag = Problemcodes.Von(g.ProblemCode);
                return "Von " + praesent + " angeschlossenen Geräten laufen " + ohne + " ohne Fehlercode; „" + Anzeigename(g) + "“ meldet Code " + g.ProblemCode + " (" + eintrag.Bedeutung + "), das ist kein Fehler.";
            }
            string codes = string.Join(", ", harmlos.Select(g => g.ProblemCode).Distinct().OrderBy(c => c));
            return "Von " + praesent + " angeschlossenen Geräten laufen " + ohne + " ohne Fehlercode; " + harmlos.Count + " melden nur harmlose Codes (" + codes + "), das ist kein Fehler.";
        }

        static Befund GeraetBefund(Kontext ctx, Geraet g)
        {
            int code = g.ProblemCode;
            var eintrag = Problemcodes.Von(code);
            string klasse = Problemcodes.Klasse(code);
            string name = Anzeigename(g);
            string frageId = "geraet:" + g.InstanzId + ":" + code;

            var b = new Befund
            {
                Bereich = Bereich.Hardware,
                Schluessel = "geraet.problem." + g.InstanzId,
                Messwert = Messwert.Von(code, "Problemcode", "0"),
                // DevNodeStatus gibt es nur ueber die DEVPKEYs; ist es da, kam auch der Code von dort.
                Quelle = g.DevNodeStatus.HasValue ? "DEVPKEY_Device_ProblemCode" : "Win32_PnPEntity.ConfigManagerErrorCode",
            };
            b.Detail.AddRange(Einzelheiten(ctx, g, eintrag));

            if (eintrag == null)
            {
                // Ein Code, den weder cfg.h 10.0.26100 noch die Doku kennt: nichts erfinden.
                b.Zustand = Zustand.Unknown;
                b.Titel = "Gerät meldet einen unbekannten Code";
                b.Satz = "„" + name + "“ meldet den Problemcode " + code + ", den dieses Programm nicht kennt; eine Deutung wäre geraten.";
                b.Rat = "Im Geräte-Manager nachsehen, was Windows zu diesem Gerät sagt.";
                return b;
            }

            switch (klasse)
            {
                case Problemcodes.Harmlos:
                    return null;

                case Problemcodes.Transient:
                    b.Zustand = Zustand.Warn;
                    b.Titel = "Gerät braucht einen Neustart";
                    b.Satz = "„" + name + "“ meldet Code " + code + " (" + eintrag.Bedeutung + "); das geht meist mit einem Neustart weg.";
                    b.Rat = "Den PC neu starten; bleibt der Code danach, ist es ein echtes Problem und kein vorübergehendes.";
                    return b;

                case Problemcodes.Defekt:
                    b.Zustand = Zustand.Bad;
                    b.Satz = "„" + name + "“ meldet Code " + code + ": " + eintrag.Bedeutung;
                    if (eintrag.Ursache == Problemcodes.Treiber)
                    {
                        b.Titel = "Gerät mit Treiberproblem";
                        b.Rat = "Den Treiber neu installieren, am besten die aktuelle Fassung vom Hersteller" + AnbieterZusatz(g) + ".";
                        b.Massnahmen.Add("geraet.treiber.neu");
                    }
                    else if (eintrag.Ursache == Problemcodes.Hardware)
                    {
                        b.Titel = IstFirmwareCode(code) ? "Firmware richtet das Gerät nicht ein" : "Gerät meldet einen Ausfall";
                        b.Rat = HardwareRat(code);
                    }
                    else
                    {
                        b.Titel = "Gerät falsch eingerichtet";
                        b.Rat = "Den Treiber neu installieren; bleibt der Code, das Gerät im Geräte-Manager entfernen und Windows neu erkennen lassen.";
                    }
                    return b;

                case Problemcodes.Gewollt:
                    if (ctx.E.IstAbsicht(frageId))
                    {
                        b.Zustand = Zustand.Absicht;
                        b.Titel = IstAbschaltCode(code) ? "Gerät absichtlich abgeschaltet" : "So gewollt: Gerät läuft nicht";
                        b.Satz = "„" + name + "“ " + AbsichtPraedikat(code) + ", so wie Sie es festgelegt haben; hier gibt es nichts zu tun.";
                        b.Rat = null;
                        b.Detail.Add("Ihre Antwort auf die Frage " + frageId + ": so lassen.");
                        return b;
                    }
                    if (ctx.E.AntwortAuf(frageId) == Entscheidungen.Reparieren)
                    {
                        b.Zustand = Zustand.Bad;
                        b.Titel = code == 22 ? "Gerät ist abgeschaltet, soll aber laufen" : "Gerät läuft nicht, soll aber laufen";
                        b.Satz = "„" + name + "“ meldet Code " + code + " (" + eintrag.Bedeutung + "), und Sie haben festgelegt, dass es laufen soll.";
                        b.Rat = ReparaturRat(code);
                        if (code == 22) b.Massnahmen.Add("geraet.aktivieren");
                        b.Detail.Add("Ihre Antwort auf die Frage " + frageId + ": wieder einschalten.");
                        return b;
                    }
                    // Keine Antwort: fragen, nicht handeln. Zustand unknown zaehlt nicht ins Gesamturteil.
                    b.Zustand = Zustand.Unknown;
                    b.Titel = code == 22 ? "Gerät abgeschaltet: Ihre Entscheidung ist gefragt" : "Gerät läuft nicht: Ihre Entscheidung ist gefragt";
                    b.Satz = "„" + name + "“ meldet Code " + code + " (" + eintrag.Bedeutung + "); ob das gewollt ist, lässt sich nicht automatisch erkennen.";
                    b.Rat = null;
                    b.Frage = FrageFuer(code, name, frageId);
                    return b;
            }
            return null;
        }

        /// <summary>Der Anzeigename ist lokalisiert und nur Anzeige; fehlt er, bleibt die Instanz-ID.</summary>
        static string Anzeigename(Geraet g)
        {
            return string.IsNullOrWhiteSpace(g.Name) ? (g.InstanzId ?? "unbekanntes Gerät") : g.Name.Trim();
        }

        static string AnbieterZusatz(Geraet g)
        {
            return string.IsNullOrWhiteSpace(g.TreiberAnbieter) ? "" : " (" + g.TreiberAnbieter.Trim() + ")";
        }

        /// <summary>33, 35, 36: die Firmware (BIOS/UEFI) richtet das Geraet nicht ein; abziehen hilft da nicht.</summary>
        static bool IstFirmwareCode(int code)
        {
            return code == 33 || code == 35 || code == 36;
        }

        /// <summary>22, 29, 32: das Geraet ist wirklich "abgeschaltet"; die anderen Gewollt-Codes sagen etwas anderes.</summary>
        static bool IstAbschaltCode(int code)
        {
            return code == 22 || code == 29 || code == 32;
        }

        /// <summary>
        /// Rat je Hardware-Code nach learn.microsoft.com "Device Manager error messages":
        /// 33 Ressourcen konfigurieren oder Geraet tauschen, 35 neues BIOS vom Hersteller,
        /// 36 IRQ-Reservierung im BIOS-Setup, 9 Hersteller kontaktieren; 11 und 43 nennen
        /// das Abziehen nur als Bedingung, weil PCI-, ACPI- und Onboard-Geraete nicht steckbar sind.
        /// </summary>
        static string HardwareRat(int code)
        {
            switch (code)
            {
                case 9: return "Den Hersteller des Geräts kontaktieren; laut Microsoft ist bei diesem Code das Gerät oder sein Treiber fehlerhaft.";
                case 33: return "Die Einstellungen des Geräts im BIOS-Setup prüfen oder beim Gerätehersteller nachfragen, welche Ressourcen es braucht; erst wenn das nicht hilft, das Gerät tauschen.";
                case 35: return "Ein BIOS-/UEFI-Update vom PC-Hersteller einspielen; die Firmware liefert zu wenig Angaben, das Gerät selbst ist dabei meist nicht defekt.";
                case 36: return "Im BIOS-Setup die Einstellung für die IRQ-Reservierung ändern (PCI statt ISA oder umgekehrt); das Gerät selbst ist dabei meist nicht defekt.";
                default: return "Das Gerät im Geräte-Manager deinstallieren und Windows neu erkennen lassen (ein Steckgerät dazu abziehen und an einem anderen Anschluss neu anstecken); bleibt der Code, den Treiber des Herstellers neu installieren, erst dann ist das Gerät wahrscheinlich defekt.";
            }
        }

        /// <summary>Satzteil fuer den Zustand absicht, je Code das, was die Frage bestaetigt hat.</summary>
        static string AbsichtPraedikat(int code)
        {
            switch (code)
            {
                case 22: return "ist abgeschaltet";
                case 29: return "ist in der Firmware (BIOS/UEFI) abgeschaltet";
                case 32: return "läuft mit abgeschaltetem Dienst";
                case 44: return "bleibt angehalten";
                case 24: return "ist entfernt";
                case 41: return "bleibt ein Diensteintrag ohne Gerät";
                case 53: return "bleibt dem Kernel-Debugger überlassen";
                case 55: return "bleibt vom DMA-Schutz blockiert";
                case 51: return "bleibt in Wartestellung auf ein anderes Gerät";
                case 57: return "bleibt für die virtuelle Maschine reserviert";
                default: return "meldet weiter Code " + code;
            }
        }

        static string ReparaturRat(int code)
        {
            switch (code)
            {
                case 22: return "Das Gerät im Geräte-Manager wieder aktivieren; das Programm bietet das als Maßnahme an.";
                case 29: return "Das Gerät in den Firmware-Einstellungen (BIOS/UEFI) einschalten; Windows kann das nicht selbst.";
                case 32: return "Den zugehörigen Dienst in der Diensteverwaltung wieder auf automatischen Start stellen.";
                case 44: return "Den PC neu starten; hält ein Programm das Gerät danach wieder an, dieses Programm prüfen.";
                case 24: return "Verbindung und Stromversorgung des Geräts prüfen und den Treiber neu installieren.";
                case 41: return "Das Gerät anschließen oder den verwaisten Diensteintrag entfernen lassen.";
                case 53: return "Den Kernel-Debugger abschalten (bcdedit /debug off) und neu starten.";
                case 55: return "Den PC entsperren; bleibt der Code, den DMA-Schutz in den Windows-Sicherheitseinstellungen prüfen.";
                // Microsoft: keine direkte Loesung; die Ursache liegt bei dem Geraet, auf das gewartet wird.
                case 51: return "Im Geräte-Manager das übergeordnete Gerät und weitere Geräte mit Fehler prüfen; startet eines davon nicht, dort ansetzen, denn für Code 51 selbst gibt es laut Microsoft keine Lösung.";
                case 57: return "Die Zuweisung an die virtuelle Maschine in Hyper-V aufheben und das Gerät dem PC zurückgeben; das ist ein Schritt für den Verwalter der virtuellen Maschinen.";
                default: return "Im Geräte-Manager nachsehen, was Windows zu diesem Gerät sagt.";
            }
        }

        static Frage FrageFuer(int code, string name, string frageId)
        {
            var f = new Frage { Id = frageId, JaHeisst = Entscheidungen.Absicht, Ja = "Ja, so lassen" };
            switch (code)
            {
                case 22:
                    f.Text = "Das Gerät „" + name + "“ ist abgeschaltet. Haben Sie das so eingerichtet?";
                    f.Nein = "Nein, wieder einschalten";
                    break;
                case 29:
                    f.Text = "Das Gerät „" + name + "“ ist in der Firmware (BIOS/UEFI) abgeschaltet. Haben Sie das so eingerichtet?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 32:
                    f.Text = "Der Dienst für das Gerät „" + name + "“ ist abgeschaltet. Haben Sie das so eingerichtet?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 44:
                    f.Text = "Das Gerät „" + name + "“ wurde von einem Programm oder Dienst angehalten. Ist das so gewollt?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 24:
                    f.Text = "Das Gerät „" + name + "“ meldet sich als nicht angeschlossen oder ohne Treiber. Haben Sie es entfernt?";
                    f.Ja = "Ja, ist entfernt";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 41:
                    f.Text = "Zum Diensteintrag „" + name + "“ gibt es kein Gerät mehr. Ist das so gewollt?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 53:
                    f.Text = "Das Gerät „" + name + "“ ist vom Kernel-Debugger belegt. Ist das so gewollt?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 55:
                    f.Text = "Das Gerät „" + name + "“ wird vom DMA-Schutz blockiert, solange der Bildschirm gesperrt ist. Ist das so gewollt?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 51:
                    f.Text = "Das Gerät „" + name + "“ wartet auf ein anderes Gerät, das nicht gestartet ist (etwa ein abgezogenes oder abgeschaltetes). Ist das so gewollt?";
                    f.Nein = "Nein, es soll laufen";
                    break;
                case 57:
                    f.Text = "Das Gerät „" + name + "“ ist für eine virtuelle Maschine reserviert (Hyper-V-Gerätezuweisung), die Zuweisung meldet aber einen Fehler. Soll es bei der Zuweisung bleiben?";
                    f.Nein = "Nein, es soll hier laufen";
                    break;
                default:
                    f.Text = "Das Gerät „" + name + "“ meldet Code " + code + ". Ist das so gewollt?";
                    f.Nein = "Nein, es soll laufen";
                    break;
            }
            return f;
        }

        /// <summary>Fachzeilen: Code-Name, Instanz, Klasse, Eltern (nur aus DEVPKEY), Treiber, Statusbits.</summary>
        static IEnumerable<string> Einzelheiten(Kontext ctx, Geraet g, Problemcodes.Eintrag eintrag)
        {
            yield return "Problemcode " + g.ProblemCode + (eintrag == null ? " (nicht in cfg.h 10.0.26100)" : " (CM_PROB_" + eintrag.Name + "): " + eintrag.Bedeutung);
            yield return "Instanz: " + g.InstanzId;
            if (!string.IsNullOrEmpty(g.Klasse) || !string.IsNullOrEmpty(g.KlasseGuid))
                yield return "Klasse: " + (g.Klasse ?? "?") + (string.IsNullOrEmpty(g.KlasseGuid) ? "" : " " + g.KlasseGuid);
            // Nie aus dem Instanzpfad raten: das Praefix (5&90F2801) ist ein Hash, keine Eltern-ID.
            yield return string.IsNullOrEmpty(g.Parent)
                ? "Übergeordnetes Gerät: nicht ermittelbar (DEVPKEY_Device_Parent fehlt)"
                : "Übergeordnetes Gerät: " + g.Parent;
            if (string.IsNullOrEmpty(g.TreiberVersion) && string.IsNullOrEmpty(g.TreiberInf))
                yield return "Treiber: keine Angaben";
            else
            {
                string datum = g.TreiberDatumUtc == null ? null : Datum(g.TreiberDatumUtc);
                yield return "Treiber: " + (g.TreiberVersion ?? "?") + (datum == null ? "" : " vom " + datum)
                             + (string.IsNullOrEmpty(g.TreiberAnbieter) ? "" : ", " + g.TreiberAnbieter.Trim())
                             + (string.IsNullOrEmpty(g.TreiberInf) ? "" : ", " + g.TreiberInf);
            }
            if (g.ProblemStatus.HasValue && g.ProblemStatus.Value != 0) yield return "ProblemStatus: 0x" + g.ProblemStatus.Value.ToString("X8");
            if (g.DevNodeStatus.HasValue) yield return "DevNodeStatus: 0x" + g.DevNodeStatus.Value.ToString("X");
            if (g.ConfigFlags.HasValue) yield return "ConfigFlags: " + g.ConfigFlags.Value + ((g.ConfigFlags.Value & 1) != 0 ? " (CONFIGFLAG_DISABLED)" : "");
        }

        static string Datum(string isoUtc)
        {
            var t = Zeit.Lesen(isoUtc);
            return t.HasValue ? t.Value.ToString("dd.MM.yyyy", Text.De) : null;
        }

        // ------------------------------------------------------------------ Windows-Support

        static void WindowsSupportPruefen(Kontext ctx, BereichErgebnis e)
        {
            var w = ctx.S.Windows;
            if (w.Build <= 0)
            {
                e.Fehlend.Add("Windows-Build (CurrentBuild nicht gelesen)");
                return;
            }
            string version = "Build " + w.Build + (string.IsNullOrEmpty(w.DisplayVersion) ? "" : " (" + w.DisplayVersion + ")");
            string edition = string.IsNullOrEmpty(w.Edition) ? "(EditionID fehlt)" : w.Edition;
            // Dieselbe Buildnummer endet je Edition anders (23H2 Pro 2025-11-11, Enterprise
            // 2026-11-10): die Spalte kommt aus der EditionID, nie aus dem Build allein.
            string spalte = SupportEnde.Spalte(w.Edition, w.ProduktTyp);
            var ende = SupportEnde.Fuer(w.Build, spalte);
            // Fuer den Satz: "Build 22631 (23H2, Home/Pro)".
            string versionSpalte = "Build " + w.Build + " (" + (string.IsNullOrEmpty(w.DisplayVersion) ? "" : w.DisplayVersion + ", ") + spalte + ")";
            var b = new Befund
            {
                Bereich = Bereich.Hardware,
                Schluessel = "windows.support",
                Messwert = Messwert.Von(w.Build, "Build", ende.HasValue ? "Support-Ende " + ende.Value.ToString("yyyy-MM-dd") + " (Spalte " + spalte + ")" : "nicht in der Tabelle vom " + SupportEnde.Stand),
                Quelle = "CurrentVersion.CurrentBuild und EditionID gegen SupportEnde (" + (spalte == null ? "Edition unbekannt" : "Spalte " + spalte) + ", Stand " + SupportEnde.Stand + ")",
            };
            b.Detail.Add("Windows " + version + ", UBR " + w.Ubr + ", Edition " + edition + ", ProductType " + w.ProduktTyp + (w.X64 ? ", 64 Bit" : ", 32 Bit"));

            if (spalte == SupportEnde.SpalteServer)
            {
                b.Zustand = Zustand.Unknown;
                b.Titel = "Windows Server wird nicht bewertet";
                b.Satz = "Windows " + version + " ist eine Server-Edition (" + edition + "); die Support-Tabelle dieses Programms deckt nur Windows 10 und 11 für Arbeitsplätze ab, ein Urteil gibt es hier nicht.";
                b.Rat = null;
                e.Befunde.Add(b);
                return;
            }
            if (spalte == null)
            {
                // Eine unbekannte Edition bekommt keine geratene Spalte: die Home/Pro-Daten
                // wuerden Enterprise-, Education- und LTSC-Rechner ein Jahr zu frueh abschreiben.
                b.Zustand = Zustand.Unknown;
                b.Titel = "Windows-Edition nicht in der Support-Tabelle";
                b.Satz = "Windows " + version + " mit der Edition " + edition + " steht nicht in der Support-Tabelle vom " + SupportEnde.Stand + "; ob es noch Updates bekommt, lässt sich so nicht sagen.";
                b.Rat = null;
                e.Befunde.Add(b);
                return;
            }
            if (!ende.HasValue)
            {
                // Unbekannte Builds sind unbekannt, nie "ausser Support" (Widerlegungsrunde: 26H1
                // Build 28000 fehlte in jeder Tabelle vom September 2026).
                b.Zustand = Zustand.Unknown;
                b.Titel = "Windows-Version nicht in der Support-Tabelle";
                b.Satz = "Windows " + versionSpalte + " steht nicht in der Support-Tabelle vom " + SupportEnde.Stand + "; ob es noch Updates bekommt, lässt sich so nicht sagen.";
                b.Rat = null;
                e.Befunde.Add(b);
                return;
            }

            double tage = (ctx.Jetzt.Date - ende.Value.Date).TotalDays;
            if (ende.Value.Date < ctx.Jetzt.Date)
            {
                b.Zustand = Zustand.Bad;
                b.Titel = "Windows-Version außer Support";
                b.Satz = "Der Support für Windows " + versionSpalte + " endete laut Microsoft-Tabelle vom " + SupportEnde.Stand + " am " + ende.Value.ToString("dd.MM.yyyy", Text.De) + ", vor " + Text.Tage(tage) + ".";
                // Windows 10 22H2 hat ein ESU-Angebot; ob der PC angemeldet ist, liest keine
                // Quelle - der Rat nennt den Weg, das Urteil bleibt bei der Tabelle (Konzept 4.4).
                b.Rat = SupportEnde.EsuAngebot(w.Build)
                    ? "Auf eine unterstützte Windows-Version wechseln oder den PC bei den erweiterten Sicherheitsupdates anmelden (ESU, Einstellungen > Windows Update); ohne ESU-Anmeldung bekommt der PC keine Sicherheitskorrekturen mehr."
                    : "Auf eine unterstützte Windows-Version aktualisieren (Einstellungen > Windows Update); ohne dieses Update bekommt der PC keine Sicherheitskorrekturen mehr.";
            }
            else
            {
                int rest = (int)Math.Round(-tage);
                b.Zustand = Zustand.Ok;
                b.Titel = "Windows wird unterstützt";
                b.Satz = "Windows " + versionSpalte + " bekommt bis " + ende.Value.ToString("dd.MM.yyyy", Text.De) + " Updates, das " + (rest == 1 ? "ist noch 1 Tag." : "sind noch " + rest + " Tage.");
                b.Rat = null;
            }
            e.Befunde.Add(b);
        }

        // ------------------------------------------------------------------ Firmware

        /// <summary>Nur informativ: ein Legacy-BIOS-Start ist kein Fehler, aber gut zu wissen.</summary>
        static void FirmwareHinweis(Kontext ctx, BereichErgebnis e)
        {
            var h = ctx.S.Hardware;
            if (h.Uefi != false) return;   // UEFI oder nicht ermittelbar: nichts zu sagen
            e.Befunde.Add(new Befund
            {
                Bereich = Bereich.Hardware,
                Schluessel = "firmware.legacy",
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von(1, "FIRMWARE_TYPE", "2 = UEFI"),
                Quelle = "GetFirmwareType",
                Titel = "Start im alten BIOS-Modus",
                Satz = "Der PC startet im Legacy-BIOS-Modus, nicht über UEFI; Secure Boot ist so nicht möglich.",
                Rat = null,
                Detail = { "FIRMWARE_TYPE 1 (FirmwareTypeBios)" },
            });
        }
    }
}
