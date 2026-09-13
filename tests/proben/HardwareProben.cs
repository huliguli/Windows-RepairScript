using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer kern/Regeln/Hardware.cs gegen gepflanzte Systembilder
    /// (tests/aufzeichnungen/gepflanzt-hardware-*.json).
    ///
    /// Jede Zusicherung ist eine Beziehung ("wenn Code 22 ohne Antwort, dann genau eine Frage
    /// und kein bad"), jede Probe ueber eine Menge sagt vorher, dass die Menge gross genug ist,
    /// und zu jeder Regel gibt es eine Gegenprobe, bei der die Bedingung fehlt.
    ///
    /// Beleg fuer die Elternschaft (Recherche R1-21, auf diesem PC gemessen): die iGPU
    /// PCI\VEN_1002&amp;DEV_13C0&amp;SUBSYS_88771043&amp;REV_C1\4&amp;1EBE6A9C&amp;0&amp;0041 hat als
    /// DEVPKEY_Device_Parent den PCI-Express-Root-Port
    /// PCI\VEN_1022&amp;DEV_14DD&amp;SUBSYS_88771043&amp;REV_00\3&amp;11583659&amp;0&amp;41. Nichts davon
    /// steckt im Instanzpfad des Kindes: das Praefix "4&amp;1EBE6A9C" ist ein Hash, keine
    /// Eltern-ID. Die Regel darf den Baum deshalb nie aus dem Pfad raten; sie nimmt nur das
    /// Feld parent, und fehlt es, sagt das Detail "nicht ermittelbar" (Bild code14).
    ///
    /// Wirksamkeitsproben am 2026-09-12 (Regel absichtlich kaputt gemacht, Rot beobachtet,
    /// zurueckgebaut, Ergebnis danach wieder 199 bestanden / 0 fehlgeschlagen):
    ///  - Hardware.cs, Klasse Defekt: Zustand.Bad gegen Zustand.Warn getauscht -> 6 rot:
    ///    "Code 43 ist bad", "Code 10 ist bad", "Bereich bad", je deutsch und englisch.
    ///  - Hardware.cs, Klasse Gewollt: Zweig ctx.E.IstAbsicht(frageId) abgeschaltet -> 3 rot:
    ///    "Zustand absicht" (kam unknown), "keine Frage mehr", "Satz: so wie Sie es festgelegt haben".
    ///  - Hardware.cs, GeraetePruefen: Bedingung !g.Present abgeschaltet -> 4 rot im Bild harmlos:
    ///    Befund fuer das nicht praesente Code-22-Geraet, eine Frage, kein geraete.ok mehr.
    ///  - Hardware.cs, WindowsSupportPruefen: Vergleich ende &lt; Jetzt umgedreht -> 12 rot:
    ///    "Support-Ende 22631 ist bad" (kam ok), "Windows 26200 ist ok" (kam bad) und alle
    ///    Bereichsurteile, die daran haengen.
    ///  - Hardware.cs, Klasse Transient auf Bad und unbekannter Code auf Bad -> 6 rot:
    ///    "Code 14 ist warn", "kein bad", "Bereich warn", "Zustand unknown" (Code 99),
    ///    "kein bad, kein warn", "Bereich ok (unknown zaehlt nicht)".
    ///
    /// Wirksamkeitsproben am 2026-09-13 (Widerlegungsrunde hardware, je Regel gebrochen,
    /// Rot gezaehlt, zurueckgebaut; fremde Bereiche liefen parallel rot, hier nur Hardware):
    ///  - SupportEnde.Spalte liefert fuer jede Edition Home/Pro -> 14 rot: Spaltenwahl,
    ///    "Enterprise 22631: ok, nie bad", LTSC 2029/2034, "unbekannte Edition: unknown", Server.
    ///  - Problemcodes.cs, 51 zurueck auf Transient -> 8 rot: Konzepttabelle "Code 51: Gewollt"
    ///    und die ganze Gruppe 51/57 (Frage, absicht, reparieren).
    ///  - Hardware.cs, harmlose Codes nicht mehr gesammelt -> 3 rot in (e): Satz nennt Code 47,
    ///    Zahl ohne Fehlercode, Detail CM_PROB_HELD_FOR_EJECT.
    ///  - Hardware.cs, Code 35 bekommt den allgemeinen Abzieh-Rat -> 1 rot: "Code 35: Rat nennt
    ///    das BIOS-Update, weder Abziehen noch Defekt-Urteil".
    /// </summary>
    public static class HardwareProben
    {
        const string IGpu = @"PCI\VEN_1002&DEV_13C0&SUBSYS_88771043&REV_C1\4&1EBE6A9C&0&0041";
        const string IGpuParent = @"PCI\VEN_1022&DEV_14DD&SUBSYS_88771043&REV_00\3&11583659&0&41";
        const string UsbGeraet = @"USB\VID_046D&PID_C52B\5&2A1B3C4D&0&3";
        const string NetzGeraet = @"PCI\VEN_8086&DEV_125C&SUBSYS_88731043&REV_04\4&3B2C1D0E&0&00E4";
        const string WlanGeraet = @"PCI\VEN_14C3&DEV_7925&SUBSYS_00011043&REV_00\4&2F1E0D9C&0&0019";
        const string ZukunftGeraet = @"SWD\TESTENUM\{00000000-0000-0000-0000-000000000099}";

        static readonly List<Befund> AlleBefunde = new List<Befund>();

        public static void Laufen(Harness h)
        {
            Code22OhneAntwort(h);
            Code22MitAntwort(h);
            Code43UndCode10(h);
            Code14(h);
            Harmlos(h);
            UnbekannterCode(h);
            Englisch(h);
            WindowsSupport(h);
            SupportEditionen(h);
            KeineDaten(h);
            Konzepttabelle(h);
            Gehaeuse(h);
            RamRueckfall(h);
            Code24(h);
            Code51Und57(h);
            FirmwareCodes(h);

            h.Gruppe("Hardware: Saetze aller Befunde tragen Zahl, Bedingung oder Absage");
            h.Ist("Grundmenge: mindestens 10 Befunde gesammelt", AlleBefunde.Count >= 10, AlleBefunde.Count.ToString());
            h.SaetzeTragen(AlleBefunde);
            foreach (var b in AlleBefunde)
                h.Ist("kein gerades Anfuehrungszeichen in Titel/Satz/Rat: " + b.Schluessel,
                      (b.Titel ?? "").IndexOf('"') < 0 && (b.Satz ?? "").IndexOf('"') < 0 && (b.Rat ?? "").IndexOf('"') < 0);
        }

        static List<BereichErgebnis> Laufen(Harness h, Systembild s, Entscheidungen e = null)
        {
            var erg = h.Pruefen(s, e);
            AlleBefunde.AddRange(Harness.Befunde(erg, Bereich.Hardware));
            return erg;
        }

        // ---------------------------------------------------------------- (a) Code 22 ohne Antwort

        static void Code22OhneAntwort(Harness h)
        {
            h.Gruppe("Hardware (a): Code 22 mit ConfigFlags 1 ohne Antwort -> genau eine Frage, kein bad");
            var s = h.Bild("gepflanzt-hardware-code22.json");
            if (s == null) return;

            var mit22 = s.Geraete.Where(g => g.ProblemCode == 22 && g.Present).ToList();
            var gesund = s.Geraete.Where(g => g.ProblemCode == 0 && g.Present).ToList();
            h.Ist("Grundmenge: mindestens 1 praesentes Geraet mit Code 22", mit22.Count >= 1, mit22.Count.ToString());
            h.Ist("Grundmenge: mindestens 3 gesunde Geraete", gesund.Count >= 3, gesund.Count.ToString());
            h.Ist("Grundmenge: das Code-22-Geraet traegt ConfigFlags 1", mit22.All(g => g.ConfigFlags == 1));
            h.Ist("Beleg: Parent steht nicht im Instanzpfad des Kindes", mit22.All(g => g.Parent != null && g.InstanzId.IndexOf(g.Parent.Split('\\').Last()) < 0));

            var erg = Laufen(h, s);
            var hw = Harness.Bereich(erg, Bereich.Hardware);
            h.Ist("DatenVorhanden", hw.DatenVorhanden);

            foreach (var g in mit22)
            {
                var befunde = Harness.AlleMit(erg, "geraet.problem." + g.InstanzId).ToList();
                h.Ist("genau ein Befund fuer " + g.InstanzId, befunde.Count == 1, befunde.Count.ToString());
                var b = befunde.FirstOrDefault();
                if (b == null) continue;
                h.Ist("Zustand unknown (fragen, nicht urteilen)", b.Zustand == Zustand.Unknown, b.Zustand);
                h.Ist("Frage vorhanden", b.Frage != null);
                h.Ist("Frage-Id traegt Instanz und Code", b.Frage != null && b.Frage.Id == "geraet:" + g.InstanzId + ":22", b.Frage == null ? null : b.Frage.Id);
                h.Ist("Ja heisst absicht", b.Frage != null && b.Frage.JaHeisst == Entscheidungen.Absicht);
                h.Ist("Frage nennt das Geraet", b.Frage != null && b.Frage.Text.Contains("„" + g.Name + "“"), b.Frage == null ? null : b.Frage.Text);
                h.Ist("Frage-Text: abgeschaltet, so eingerichtet?", b.Frage != null && b.Frage.Text.Contains("abgeschaltet") && b.Frage.Text.Contains("eingerichtet"));
                h.Ist("Antworten: Ja, so lassen / Nein, wieder einschalten", b.Frage != null && b.Frage.Ja == "Ja, so lassen" && b.Frage.Nein == "Nein, wieder einschalten");
                h.Ist("keine Massnahme ohne Antwort", b.Massnahmen.Count == 0);
                h.Ist("Detail: Eltern aus DEVPKEY, nicht aus dem Pfad", b.Detail.Any(d => d == "Übergeordnetes Gerät: " + IGpuParent), string.Join(" | ", b.Detail));
                h.Ist("Detail: Code-Name CM_PROB_DISABLED", b.Detail.Any(d => d.Contains("CM_PROB_DISABLED")));
                h.Ist("Detail: Treiberversion, -datum, -anbieter", b.Detail.Any(d => d.Contains("32.0.21043.5001") && d.Contains("03.03.2026") && d.Contains("Advanced Micro Devices")));
                h.Ist("Detail: ConfigFlags gedeutet", b.Detail.Any(d => d.Contains("CONFIGFLAG_DISABLED")));
                h.Ist("Quelle ist der DEVPKEY", b.Quelle == "DEVPKEY_Device_ProblemCode", b.Quelle);
            }
            h.Ist("kein bad im Bereich Hardware", !Harness.Befunde(erg, Bereich.Hardware).Any(b => b.Zustand == Zustand.Bad));
            h.Ist("kein Befund fuer gesunde Geraete", gesund.All(g => !Harness.AlleMit(erg, "geraet.problem." + g.InstanzId).Any()));
            h.Ist("Bereichszustand nicht bad und nicht warn", hw.Zustand != Zustand.Bad && hw.Zustand != Zustand.Warn, hw.Zustand);
            h.Ist("Windows 26200 am 2026-09-11 ist ok (Gegenprobe zu Support-Ende)", Harness.Einer(erg, "windows.support") != null && Harness.Einer(erg, "windows.support").Zustand == Zustand.Ok);
            h.Ist("kein Firmware-Hinweis bei UEFI (Gegenprobe)", Harness.Einer(erg, "firmware.legacy") == null);
        }

        // ---------------------------------------------------------------- (b) Code 22 mit Antwort

        static void Code22MitAntwort(Harness h)
        {
            h.Gruppe("Hardware (b): dasselbe Bild mit Antwort absicht -> Zustand absicht; mit Antwort reparieren -> bad mit Massnahme");
            var s = h.Bild("gepflanzt-hardware-code22.json");
            if (s == null) return;
            string frageId = "geraet:" + IGpu + ":22";

            var e = new Entscheidungen();
            e.Setze(frageId, Entscheidungen.Absicht, "2026-09-11T15:00:00Z");
            var erg = Laufen(h, s, e);
            var b = Harness.Einer(erg, "geraet.problem." + IGpu);
            h.Ist("Befund vorhanden", b != null);
            if (b != null)
            {
                h.Ist("Zustand absicht", b.Zustand == Zustand.Absicht, b.Zustand);
                h.Ist("keine Frage mehr", b.Frage == null);
                h.Ist("Satz: so wie Sie es festgelegt haben", b.Satz.Contains("so wie Sie es festgelegt haben"), b.Satz);
                h.Ist("kein Rat, keine Massnahme", b.Rat == null && b.Massnahmen.Count == 0);
            }
            h.Ist("absicht zaehlt nicht ins Bereichsurteil (ok)", Harness.Bereich(erg, Bereich.Hardware).Zustand == Zustand.Ok, Harness.Bereich(erg, Bereich.Hardware).Zustand);

            // Gegenprobe: eine Antwort auf einen ANDEREN Code gilt nicht (Zustand steckt in der Kennung).
            var e2 = new Entscheidungen();
            e2.Setze("geraet:" + IGpu + ":43", Entscheidungen.Absicht, "2026-09-11T15:00:00Z");
            var erg2 = Laufen(h, s, e2);
            var b2 = Harness.Einer(erg2, "geraet.problem." + IGpu);
            h.Ist("Antwort auf Code 43 gilt nicht fuer Code 22: weiter Frage", b2 != null && b2.Frage != null && b2.Zustand == Zustand.Unknown);

            var e3 = new Entscheidungen();
            e3.Setze(frageId, Entscheidungen.Reparieren, "2026-09-11T15:00:00Z");
            var erg3 = Laufen(h, s, e3);
            var b3 = Harness.Einer(erg3, "geraet.problem." + IGpu);
            h.Ist("Antwort reparieren: Zustand bad", b3 != null && b3.Zustand == Zustand.Bad, b3 == null ? null : b3.Zustand);
            h.Ist("Antwort reparieren: Massnahme geraet.aktivieren", b3 != null && b3.Massnahmen.Contains("geraet.aktivieren"));
            h.Ist("Antwort reparieren: keine Frage", b3 != null && b3.Frage == null);
            h.Ist("Antwort reparieren: Bereich bad", Harness.Bereich(erg3, Bereich.Hardware).Zustand == Zustand.Bad);
        }

        // ---------------------------------------------------------------- (c) Code 43 und Code 10

        static void Code43UndCode10(Harness h)
        {
            h.Gruppe("Hardware (c): Code 43 (Hardware) -> bad ohne Massnahme; Code 10 (Treiber) -> bad mit Massnahme");
            var s = h.Bild("gepflanzt-hardware-code43.json");
            if (s == null) return;
            Code43UndCode10Pruefen(h, s, "USB-Verbundgerät");
        }

        static void Code43UndCode10Pruefen(Harness h, Systembild s, string usbName)
        {
            h.Ist("Grundmenge: ein Geraet mit Code 43, eines mit Code 10, mindestens 3 gesunde",
                  s.Geraete.Count(g => g.ProblemCode == 43) == 1 && s.Geraete.Count(g => g.ProblemCode == 10) == 1 && s.Geraete.Count(g => g.ProblemCode == 0) >= 3);
            var erg = Laufen(h, s);
            var hw = Harness.Bereich(erg, Bereich.Hardware);

            var b43 = Harness.Einer(erg, "geraet.problem." + UsbGeraet);
            h.Ist("Code 43: Befund vorhanden", b43 != null);
            if (b43 != null)
            {
                h.Ist("Code 43 ist bad", b43.Zustand == Zustand.Bad, b43.Zustand);
                h.Ist("Code 43: keine Frage", b43.Frage == null);
                h.Ist("Code 43: keine automatische Massnahme (Hardware-Ursache)", b43.Massnahmen.Count == 0);
                h.Ist("Code 43: Rat vorhanden", !string.IsNullOrEmpty(b43.Rat));
                h.Ist("Code 43: Satz nennt Geraet und Code", b43.Satz.Contains("„" + usbName + "“") && b43.Satz.Contains("43"), b43.Satz);
                h.Ist("Code 43: Messwert 43, Schwelle 0", b43.Messwert != null && b43.Messwert.Wert == "43" && b43.Messwert.Schwelle == "0");
                h.Ist("Code 43: Detail Eltern aus DEVPKEY", b43.Detail.Any(d => d == @"Übergeordnetes Gerät: USB\ROOT_HUB30\4&1D2E3F4A&0&0"));
                h.Ist("Code 43: Detail CM_PROB_FAILED_POST_START", b43.Detail.Any(d => d.Contains("CM_PROB_FAILED_POST_START")));
                h.Ist("Code 43: Detail ProblemStatus als Hex", b43.Detail.Any(d => d.Contains("ProblemStatus: 0xC0000001")), string.Join(" | ", b43.Detail));
            }

            var b10 = Harness.Einer(erg, "geraet.problem." + NetzGeraet);
            h.Ist("Code 10: Befund vorhanden", b10 != null);
            if (b10 != null)
            {
                h.Ist("Code 10 ist bad", b10.Zustand == Zustand.Bad, b10.Zustand);
                h.Ist("Code 10: Massnahme geraet.treiber.neu", b10.Massnahmen.Contains("geraet.treiber.neu"));
                h.Ist("Code 10: Rat nennt Treiber und Anbieter", b10.Rat != null && b10.Rat.Contains("Treiber") && b10.Rat.Contains("Intel"), b10.Rat);
                h.Ist("Code 10: keine Frage", b10.Frage == null);
            }
            h.Ist("Bereich bad", hw.Zustand == Zustand.Bad, hw.Zustand);
            h.Ist("kein geraete.ok-Sammelbefund, wenn Probleme da sind", Harness.Einer(erg, "geraete.ok") == null);
            h.Ist("gesunde Geraete ohne Befund", s.Geraete.Where(g => g.ProblemCode == 0).All(g => !Harness.AlleMit(erg, "geraet.problem." + g.InstanzId).Any()));
        }

        // ---------------------------------------------------------------- (d) Code 14

        static void Code14(Harness h)
        {
            h.Gruppe("Hardware (d): Code 14 -> warn mit Neustart-Rat; Parent fehlt -> nicht aus dem Pfad geraten");
            var s = h.Bild("gepflanzt-hardware-code14.json");
            if (s == null) return;
            h.Ist("Grundmenge: ein Geraet mit Code 14 ohne parent, mindestens 3 gesunde",
                  s.Geraete.Count(g => g.ProblemCode == 14 && g.Parent == null) == 1 && s.Geraete.Count(g => g.ProblemCode == 0) >= 3);
            var erg = Laufen(h, s);
            var hw = Harness.Bereich(erg, Bereich.Hardware);
            h.Ist("DatenVorhanden trotz Zeitfehler an wmi.pnp.eigenschaften", hw.DatenVorhanden);

            var b = Harness.Einer(erg, "geraet.problem." + WlanGeraet);
            h.Ist("Befund vorhanden", b != null);
            if (b != null)
            {
                h.Ist("Code 14 ist warn", b.Zustand == Zustand.Warn, b.Zustand);
                h.Ist("Rat nennt den Neustart", b.Rat != null && b.Rat.Contains("neu starten"), b.Rat);
                h.Ist("keine Massnahme (Neustart bleibt Rat)", b.Massnahmen.Count == 0);
                h.Ist("keine Frage", b.Frage == null);
                h.Ist("Detail: Eltern nicht ermittelbar, kein Raten", b.Detail.Any(d => d.Contains("nicht ermittelbar")) && !b.Detail.Any(d => d.StartsWith("Übergeordnetes Gerät: PCI")), string.Join(" | ", b.Detail));
                h.Ist("Detail: Treiber keine Angaben", b.Detail.Any(d => d == "Treiber: keine Angaben"));
                h.Ist("Quelle ist die Grundabfrage, wenn die DEVPKEYs fehlen", b.Quelle == "Win32_PnPEntity.ConfigManagerErrorCode", b.Quelle);
            }
            h.Ist("kein bad", !Harness.Befunde(erg, Bereich.Hardware).Any(x => x.Zustand == Zustand.Bad));
            h.Ist("Bereich warn", hw.Zustand == Zustand.Warn, hw.Zustand);
        }

        // ---------------------------------------------------------------- (e) harmlos

        static void Harmlos(Harness h)
        {
            h.Gruppe("Hardware (e): Code 45 (Phantom), Code 47 (Auswurf) und Code 22 an nicht praesentem Geraet -> kein Befund");
            var s = h.Bild("gepflanzt-hardware-harmlos.json");
            if (s == null) return;
            h.Ist("Grundmenge: je ein Geraet mit 45, 47 und nicht praesentem 22, mindestens 3 gesunde",
                  s.Geraete.Count(g => g.ProblemCode == 45) == 1 && s.Geraete.Count(g => g.ProblemCode == 47 && g.Present) == 1
                  && s.Geraete.Count(g => g.ProblemCode == 22 && !g.Present) == 1 && s.Geraete.Count(g => g.ProblemCode == 0 && g.Present) >= 3);
            var erg = Laufen(h, s);
            var probleme = Harness.AlleMit(erg, "geraet.problem.").ToList();
            h.Ist("kein Geraete-Befund", probleme.Count == 0, string.Join(", ", probleme.Select(b => b.Schluessel)));
            h.Ist("keine Frage (Code 22 nicht praesent)", !Harness.Befunde(erg, Bereich.Hardware).Any(b => b.Frage != null));
            var ok = Harness.Einer(erg, "geraete.ok");
            h.Ist("Sammelbefund geraete.ok vorhanden", ok != null);
            int praesent = s.Geraete.Count(g => g.Present);
            h.Ist("geraete.ok zaehlt nur praesente Geraete (" + praesent + ")", ok != null && ok.Messwert != null && ok.Messwert.Wert == praesent.ToString() && ok.Satz.Contains(praesent.ToString()), ok == null ? null : ok.Satz);
            // Der Stick mit Code 47 laeuft nicht "ohne Fehlercode": der Satz nennt ihn, statt ihn mitzuzaehlen.
            var stick = s.Geraete.First(g => g.ProblemCode == 47 && g.Present);
            h.Ist("Satz nennt Code 47 und das Geraet, nicht „alle ohne Fehlercode“", ok != null && ok.Satz.Contains("47") && ok.Satz.Contains("„" + stick.Name + "“") && !ok.Satz.StartsWith("Alle"), ok == null ? null : ok.Satz);
            h.Ist("Satz nennt die Zahl ohne Fehlercode (" + (praesent - 1) + ")", ok != null && ok.Satz.Contains(" " + (praesent - 1) + " ohne Fehlercode"), ok == null ? null : ok.Satz);
            h.Ist("Detail nennt den harmlosen Code mit CM_PROB_HELD_FOR_EJECT", ok != null && ok.Detail.Any(d => d.Contains(stick.Name) && d.Contains("CM_PROB_HELD_FOR_EJECT")), ok == null ? null : string.Join(" | ", ok.Detail));
            h.Ist("Schwelle nennt die harmlosen Codes", ok != null && ok.Messwert.Schwelle.Contains("47"));
            h.Ist("Bereich ok", Harness.Bereich(erg, Bereich.Hardware).Zustand == Zustand.Ok);

            // Gegenprobe: ohne den Stick sagt der Satz wieder "Alle N ... ohne Fehlercode".
            s.Geraete.Remove(stick);
            var ok2 = Harness.Einer(Laufen(h, s), "geraete.ok");
            h.Ist("ohne harmlosen Code: Satz „Alle N angeschlossenen Geräte laufen ohne Fehlercode“", ok2 != null && ok2.Satz == "Alle " + (praesent - 1) + " angeschlossenen Geräte laufen ohne Fehlercode.", ok2 == null ? null : ok2.Satz);
        }

        // ---------------------------------------------------------------- (f) unbekannter Code

        static void UnbekannterCode(Harness h)
        {
            h.Gruppe("Hardware (f): unbekannter Code 99 -> unknown, nie bad");
            var s = h.Bild("gepflanzt-hardware-code99.json");
            if (s == null) return;
            h.Ist("Grundmenge: ein Geraet mit Code 99", s.Geraete.Count(g => g.ProblemCode == 99) == 1);
            h.Ist("Voraussetzung: Problemcodes kennt 99 nicht", Problemcodes.Von(99) == null);
            var erg = Laufen(h, s);
            var b = Harness.Einer(erg, "geraet.problem." + ZukunftGeraet);
            h.Ist("Befund vorhanden (Unbekanntes bleibt sichtbar)", b != null);
            if (b != null)
            {
                h.Ist("Zustand unknown", b.Zustand == Zustand.Unknown, b.Zustand);
                h.Ist("Satz nennt den Code 99 und dass er unbekannt ist", b.Satz.Contains("99") && b.Satz.Contains("nicht kennt"), b.Satz);
                h.Ist("keine Massnahme, keine Frage", b.Massnahmen.Count == 0 && b.Frage == null);
            }
            h.Ist("kein bad, kein warn", !Harness.Befunde(erg, Bereich.Hardware).Any(x => x.Zustand == Zustand.Bad || x.Zustand == Zustand.Warn));
            h.Ist("Bereich ok (unknown zaehlt nicht)", Harness.Bereich(erg, Bereich.Hardware).Zustand == Zustand.Ok);
        }

        // ---------------------------------------------------------------- (g) englisch

        static void Englisch(Harness h)
        {
            h.Gruppe("Hardware (g): englisches Bild (lcid 1033) mit denselben Zahlen -> dieselben Befunde");
            var de = h.Bild("gepflanzt-hardware-code43.json");
            var en = h.Bild("gepflanzt-hardware-code43-en.json");
            if (de == null || en == null) return;
            h.Ist("Grundmenge: en ist 1033, de ist 1031", en.Sprache.Lcid == 1033 && de.Sprache.Lcid == 1031);
            h.Ist("Grundmenge: Anzeigenamen unterscheiden sich", de.Geraete[0].Name != en.Geraete[0].Name);

            Code43UndCode10Pruefen(h, en, "USB Composite Device");

            var ergDe = h.Pruefen(de);
            var ergEn = h.Pruefen(en);
            var de1 = Harness.Befunde(ergDe, Bereich.Hardware).Select(b => b.Schluessel + "=" + b.Zustand + "/" + string.Join(",", b.Massnahmen)).OrderBy(x => x).ToList();
            var en1 = Harness.Befunde(ergEn, Bereich.Hardware).Select(b => b.Schluessel + "=" + b.Zustand + "/" + string.Join(",", b.Massnahmen)).OrderBy(x => x).ToList();
            h.Ist("gleiche Schluessel, Zustaende und Massnahmen", de1.SequenceEqual(en1), string.Join(" ; ", de1) + "  <>  " + string.Join(" ; ", en1));
            h.Ist("Grundmenge: mindestens 3 Befunde verglichen", de1.Count >= 3, de1.Count.ToString());
        }

        // ---------------------------------------------------------------- Windows-Support, Firmware

        static void WindowsSupport(Harness h)
        {
            h.Gruppe("Hardware: Windows ausser Support -> bad; unbekannter Build -> unknown; Legacy-BIOS -> nur Info");
            var s = h.Bild("gepflanzt-hardware-support-ende.json");
            if (s == null) return;
            h.Ist("Grundmenge: Build 22631 hat in der Home/Pro-Spalte ein Support-Ende vor der Aufzeichnung", SupportEnde.Fuer(22631, SupportEnde.SpalteHomePro).HasValue && SupportEnde.Fuer(22631, SupportEnde.SpalteHomePro).Value < Zeit.Lesen(s.AufgezeichnetUtc).Value);
            h.Ist("Grundmenge: Edition Professional gehoert zur Home/Pro-Spalte", SupportEnde.Spalte(s.Windows.Edition, s.Windows.ProduktTyp) == SupportEnde.SpalteHomePro);
            var erg = Laufen(h, s);
            var b = Harness.Einer(erg, "windows.support");
            h.Ist("Befund vorhanden", b != null);
            if (b != null)
            {
                h.Ist("Support-Ende 22631 ist bad", b.Zustand == Zustand.Bad, b.Zustand);
                h.Ist("Satz nennt das Datum 11.11.2025, Build 22631 und die Spalte Home/Pro", b.Satz.Contains("11.11.2025") && b.Satz.Contains("22631") && b.Satz.Contains(SupportEnde.SpalteHomePro), b.Satz);
                h.Ist("Titel sagt „außer Support“, nicht „keine Sicherheitsupdates“", b.Titel.Contains("außer Support") && !b.Titel.Contains("Sicherheitsupdates"), b.Titel);
                h.Ist("Rat vorhanden, ohne ESU (kein Angebot fuer Windows 11)", !string.IsNullOrEmpty(b.Rat) && !b.Rat.Contains("ESU"), b.Rat);
                h.Ist("Messwert Build, Schwelle nennt die Spalte", b.Messwert != null && b.Messwert.Wert == "22631" && b.Messwert.Schwelle.Contains(SupportEnde.SpalteHomePro), b.Messwert == null ? null : b.Messwert.Schwelle);
            }
            var fw = Harness.Einer(erg, "firmware.legacy");
            h.Ist("Legacy-BIOS: Hinweis vorhanden", fw != null);
            h.Ist("Legacy-BIOS: nur informativ (ok, kein Rat)", fw != null && fw.Zustand == Zustand.Ok && fw.Rat == null);
            h.Ist("Bereich bad (wegen Support-Ende)", Harness.Bereich(erg, Bereich.Hardware).Zustand == Zustand.Bad);

            var s2 = h.Bild("gepflanzt-hardware-build-unbekannt.json");
            if (s2 == null) return;
            h.Ist("Grundmenge: Build 27000 in keiner Spalte", !SupportEnde.Fuer(27000, SupportEnde.SpalteHomePro).HasValue && !SupportEnde.Fuer(27000, SupportEnde.SpalteEnterprise).HasValue);
            var erg2 = Laufen(h, s2);
            var b2 = Harness.Einer(erg2, "windows.support");
            h.Ist("unbekannter Build: Befund unknown, nie bad", b2 != null && b2.Zustand == Zustand.Unknown, b2 == null ? null : b2.Zustand);
            h.Ist("unbekannter Build: Satz nennt Tabellenstand", b2 != null && b2.Satz.Contains(SupportEnde.Stand));
            h.Ist("unbekannter Build: kein Firmware-Hinweis bei UEFI", Harness.Einer(erg2, "firmware.legacy") == null);
            h.Ist("Bereich ok (nur unknown und geraete.ok)", Harness.Bereich(erg2, Bereich.Hardware).Zustand == Zustand.Ok, Harness.Bereich(erg2, Bereich.Hardware).Zustand);
        }

        // ---------------------------------------------------------------- keine Daten

        static void KeineDaten(Harness h)
        {
            h.Gruppe("Hardware: Geraeteliste nicht geliefert (wmi.pnp zeit) -> unknown, nie ok, nie bad");
            var s = h.Bild("gepflanzt-hardware-keine-daten.json");
            if (s == null) return;
            h.Ist("Grundmenge: keine Geraete und ein Zeitfehler an wmi.pnp", s.Geraete.Count == 0 && s.FehlerVon("wmi.pnp").Any(f => f.Art == Fehler.Zeit));
            var erg = Laufen(h, s);
            var hw = Harness.Bereich(erg, Bereich.Hardware);
            h.Ist("DatenVorhanden false", !hw.DatenVorhanden);
            h.Ist("Fehlend nennt die Geraeteliste mit Grund", hw.Fehlend.Any(f => f.Contains("Geräteliste") && f.Contains("Zeit")), string.Join(" | ", hw.Fehlend));
            h.Ist("kein geraete.ok (leer ist nicht gesund)", Harness.Einer(erg, "geraete.ok") == null);
            h.Ist("kein Windows-Befund ohne Build", Harness.Einer(erg, "windows.support") == null);
            h.Ist("Bereich unknown", hw.Zustand == Zustand.Unknown, hw.Zustand);

            // Dasselbe Bild mit einer Ausnahme statt Zeitfehler: der Nutzer sieht die Laienfassung,
            // nicht den Ausnahmetyp und nicht den Rohtext der Quelle.
            var f = s.FehlerVon("wmi.pnp").First();
            f.Art = Fehler.Ausnahme;
            f.Text = "InvalidOperationException: Win32_PnPEntity lieferte keine Geräte";
            var hw2 = Harness.Bereich(Laufen(h, s), Bereich.Hardware);
            h.Ist("Ausnahme: Fehlend sagt „Fehler beim Lesen“", hw2.Fehlend.Any(x => x.Contains("Geräteliste") && x.Contains("Fehler beim Lesen")), string.Join(" | ", hw2.Fehlend));
            h.Ist("Ausnahme: weder Ausnahmetyp noch Rohtext im Fehlend", !hw2.Fehlend.Any(x => x.Contains("Exception") || x.Contains("Win32_PnPEntity")), string.Join(" | ", hw2.Fehlend));
        }
        // ---------------------------------------------------------------- Support je Edition

        static void SupportEditionen(Harness h)
        {
            h.Gruppe("Hardware: Support-Ende je Edition (Enterprise, LTSC, unbekannte Edition, Server, Windows 10 mit ESU)");

            // Spaltenwahl aus der EditionID: Positivliste, alles andere unbekannt.
            h.Ist("Spalte: Core, CoreSingleLanguage, Professional, ProfessionalWorkstation, CloudEdition -> Home/Pro",
                  new[] { "Core", "CoreSingleLanguage", "Professional", "ProfessionalWorkstation", "CloudEdition" }.All(x => SupportEnde.Spalte(x, 1) == SupportEnde.SpalteHomePro));
            h.Ist("Spalte: Enterprise, Education, IoTEnterprise, EnterpriseMultiSession, ServerRdsh -> Enterprise/Education",
                  new[] { "Enterprise", "Education", "IoTEnterprise", "EnterpriseMultiSession", "ServerRdsh" }.All(x => SupportEnde.Spalte(x, 3) == SupportEnde.SpalteEnterprise));
            h.Ist("Spalte: EnterpriseS -> Enterprise LTSC; IoTEnterpriseS -> IoT Enterprise LTSC",
                  SupportEnde.Spalte("EnterpriseS", 1) == SupportEnde.SpalteLtsc && SupportEnde.Spalte("IoTEnterpriseS", 1) == SupportEnde.SpalteIotLtsc);
            h.Ist("Spalte: ServerStandard oder ProductType 3 -> Server", SupportEnde.Spalte("ServerStandard", 3) == SupportEnde.SpalteServer && SupportEnde.Spalte("", 3) == SupportEnde.SpalteServer);
            h.Ist("Spalte: leer, null, EnterpriseSEval -> unbekannt (null)", SupportEnde.Spalte("", 1) == null && SupportEnde.Spalte(null, 0) == null && SupportEnde.Spalte("EnterpriseSEval", 1) == null);

            // Die Daten selbst, gegen die Microsoft-Tabelle vom 2026-09-08 gelesen.
            h.Ist("22631: Home/Pro 2025-11-11, Enterprise 2026-11-10",
                  SupportEnde.Fuer(22631, SupportEnde.SpalteHomePro) == new System.DateTime(2025, 11, 11) && SupportEnde.Fuer(22631, SupportEnde.SpalteEnterprise) == new System.DateTime(2026, 11, 10));
            h.Ist("26100: Home/Pro 2026-10-13, Enterprise 2027-10-12, Enterprise LTSC 2029-10-09, IoT LTSC 2034-10-10",
                  SupportEnde.Fuer(26100, SupportEnde.SpalteHomePro) == new System.DateTime(2026, 10, 13) && SupportEnde.Fuer(26100, SupportEnde.SpalteEnterprise) == new System.DateTime(2027, 10, 12)
                  && SupportEnde.Fuer(26100, SupportEnde.SpalteLtsc) == new System.DateTime(2029, 10, 9) && SupportEnde.Fuer(26100, SupportEnde.SpalteIotLtsc) == new System.DateTime(2034, 10, 10));
            h.Ist("19045: alle Editionen 2025-10-14, ESU-Angebot",
                  SupportEnde.Fuer(19045, SupportEnde.SpalteHomePro) == new System.DateTime(2025, 10, 14) && SupportEnde.Fuer(19045, SupportEnde.SpalteEnterprise) == new System.DateTime(2025, 10, 14) && SupportEnde.EsuAngebot(19045));
            h.Ist("Server-Spalte hat keine Tabelle; 26100 Enterprise/LTSC in jeder Spalte spaeter als Home/Pro",
                  !SupportEnde.Fuer(26100, SupportEnde.SpalteServer).HasValue && !SupportEnde.EsuAngebot(26100)
                  && SupportEnde.Fuer(26100, SupportEnde.SpalteEnterprise) > SupportEnde.Fuer(26100, SupportEnde.SpalteHomePro)
                  && SupportEnde.Fuer(26100, SupportEnde.SpalteLtsc) > SupportEnde.Fuer(26100, SupportEnde.SpalteEnterprise));

            // Enterprise 23H2: derselbe Build, der als Pro bad ist, ist als Enterprise ok.
            var s = h.Bild("gepflanzt-hardware-support-enterprise.json");
            if (s == null) return;
            h.Ist("Grundmenge: Build 22631, Edition Enterprise, aufgezeichnet vor 2026-11-10", s.Windows.Build == 22631 && s.Windows.Edition == "Enterprise" && Zeit.Lesen(s.AufgezeichnetUtc).Value < new System.DateTime(2026, 11, 10));
            var b = Harness.Einer(Laufen(h, s), "windows.support");
            h.Ist("Enterprise 22631: ok, nie bad", b != null && b.Zustand == Zustand.Ok, b == null ? null : b.Zustand);
            h.Ist("Enterprise 22631: Satz nennt 10.11.2026 und die Spalte", b != null && b.Satz.Contains("10.11.2026") && b.Satz.Contains(SupportEnde.SpalteEnterprise), b == null ? null : b.Satz);
            h.Ist("Enterprise 22631: Quelle nennt EditionID und Spalte", b != null && b.Quelle.Contains("EditionID") && b.Quelle.Contains(SupportEnde.SpalteEnterprise), b == null ? null : b.Quelle);
            // Gegenprobe: dasselbe Bild als Professional ist bad (Zustand haengt an der Edition, nicht am Build).
            s.Windows.Edition = "Professional";
            var bPro = Harness.Einer(Laufen(h, s), "windows.support");
            h.Ist("Gegenprobe: derselbe Build als Professional ist bad", bPro != null && bPro.Zustand == Zustand.Bad, bPro == null ? null : bPro.Zustand);

            var s2 = h.Bild("gepflanzt-hardware-support-ltsc.json");
            if (s2 == null) return;
            h.Ist("Grundmenge: Build 26100, Edition EnterpriseS", s2.Windows.Build == 26100 && s2.Windows.Edition == "EnterpriseS");
            var b2 = Harness.Einer(Laufen(h, s2), "windows.support");
            h.Ist("Enterprise LTSC 2024: ok bis 09.10.2029", b2 != null && b2.Zustand == Zustand.Ok && b2.Satz.Contains("09.10.2029"), b2 == null ? null : b2.Satz);
            s2.Windows.Edition = "IoTEnterpriseS";
            var b2iot = Harness.Einer(Laufen(h, s2), "windows.support");
            h.Ist("IoT Enterprise LTSC 2024: ok bis 10.10.2034", b2iot != null && b2iot.Zustand == Zustand.Ok && b2iot.Satz.Contains("10.10.2034"), b2iot == null ? null : b2iot.Satz);

            var s3 = h.Bild("gepflanzt-hardware-support-edition-unbekannt.json");
            if (s3 == null) return;
            h.Ist("Grundmenge: Build 22631 (Home/Pro waere bad) mit Edition EnterpriseSEval", s3.Windows.Build == 22631 && SupportEnde.Spalte(s3.Windows.Edition, s3.Windows.ProduktTyp) == null);
            var erg3 = Laufen(h, s3);
            var b3 = Harness.Einer(erg3, "windows.support");
            h.Ist("unbekannte Edition: unknown, nie bad", b3 != null && b3.Zustand == Zustand.Unknown, b3 == null ? null : b3.Zustand);
            h.Ist("unbekannte Edition: Satz nennt die Edition und den Tabellenstand", b3 != null && b3.Satz.Contains("EnterpriseSEval") && b3.Satz.Contains(SupportEnde.Stand), b3 == null ? null : b3.Satz);
            h.Ist("unbekannte Edition: Bereich nicht bad", Harness.Bereich(erg3, Bereich.Hardware).Zustand != Zustand.Bad);

            var s4 = h.Bild("gepflanzt-hardware-support-server.json");
            if (s4 == null) return;
            h.Ist("Grundmenge: ProductType 3, Edition ServerStandard", s4.Windows.ProduktTyp == 3 && s4.Windows.Edition == "ServerStandard");
            var b4 = Harness.Einer(Laufen(h, s4), "windows.support");
            h.Ist("Server: unknown mit eigenem Satz, nie bad", b4 != null && b4.Zustand == Zustand.Unknown && b4.Satz.Contains("Server"), b4 == null ? null : b4.Satz);

            var s5 = h.Bild("gepflanzt-hardware-support-win10.json");
            if (s5 == null) return;
            h.Ist("Grundmenge: Build 19045, Edition Core, aufgezeichnet nach 2025-10-14", s5.Windows.Build == 19045 && s5.Windows.Edition == "Core" && Zeit.Lesen(s5.AufgezeichnetUtc).Value > new System.DateTime(2025, 10, 14));
            var b5 = Harness.Einer(Laufen(h, s5), "windows.support");
            h.Ist("Windows 10 22H2: bad", b5 != null && b5.Zustand == Zustand.Bad, b5 == null ? null : b5.Zustand);
            h.Ist("Windows 10 22H2: Rat nennt ESU als Weg, ohne festes Enddatum", b5 != null && b5.Rat.Contains("ESU") && !b5.Rat.Contains("2026") && !b5.Rat.Contains("2027"), b5 == null ? null : b5.Rat);
            h.Ist("Windows 10 22H2: Satz nennt 14.10.2025 und den Tabellenstand", b5 != null && b5.Satz.Contains("14.10.2025") && b5.Satz.Contains(SupportEnde.Stand), b5 == null ? null : b5.Satz);
        }

        // ---------------------------------------------------------------- Konzepttabelle 4.1

        /// <summary>Die fuenf Klassenmengen aus Konzept 4.1, Zeile fuer Zeile; Code 23 fehlt dort und bleibt transient.</summary>
        static void Konzepttabelle(Harness h)
        {
            h.Gruppe("Hardware: Problemcodes.cs deckt sich mit der Tabelle in Konzept 4.1");
            var treiber = new[] { 1, 2, 10, 18, 28, 31, 37, 39, 40, 48, 50, 52 };
            var hardware = new[] { 4, 5, 6, 7, 8, 9, 11, 12, 13, 16, 17, 19, 27, 30, 33, 34, 35, 36, 43, 49 };
            var transient = new[] { 3, 14, 15, 21, 25, 26, 38, 42, 46, 54, 56 };
            var gewollt = new[] { 22, 29, 32, 44, 53, 55, 24, 41, 51, 57 };
            var harmlos = new[] { 45, 47, 20 };
            int summe = treiber.Length + hardware.Length + transient.Length + gewollt.Length + harmlos.Length;
            h.Ist("Grundmenge: die fuenf Listen nennen 56 verschiedene Codes (57 minus Code 23)", summe == 56 && treiber.Concat(hardware).Concat(transient).Concat(gewollt).Concat(harmlos).Distinct().Count() == 56, summe.ToString());
            h.Ist("Grundmenge: Problemcodes kennt genau die Codes 1 bis 57", Enumerable.Range(1, 57).All(c => Problemcodes.Von(c) != null) && Problemcodes.Von(0) == null && Problemcodes.Von(58) == null);

            foreach (int c in treiber)
                h.Ist("Code " + c + ": Defekt mit Ursache treiber (Massnahme Treiber neu)", Problemcodes.Klasse(c) == Problemcodes.Defekt && Problemcodes.Von(c).Ursache == Problemcodes.Treiber, Problemcodes.Klasse(c) + "/" + Problemcodes.Von(c).Ursache);
            foreach (int c in hardware)
                h.Ist("Code " + c + ": Defekt ohne Treiber-Massnahme (hardware oder konfiguration)", Problemcodes.Klasse(c) == Problemcodes.Defekt && Problemcodes.Von(c).Ursache != Problemcodes.Treiber, Problemcodes.Klasse(c) + "/" + Problemcodes.Von(c).Ursache);
            foreach (int c in transient)
                h.Ist("Code " + c + ": Transient", Problemcodes.Klasse(c) == Problemcodes.Transient, Problemcodes.Klasse(c));
            foreach (int c in gewollt)
                h.Ist("Code " + c + ": Gewollt", Problemcodes.Klasse(c) == Problemcodes.Gewollt, Problemcodes.Klasse(c));
            foreach (int c in harmlos)
                h.Ist("Code " + c + ": Harmlos", Problemcodes.Klasse(c) == Problemcodes.Harmlos, Problemcodes.Klasse(c));
            h.Ist("Code 23 (nicht im Konzept): Transient, dokumentiert in Problemcodes.cs", Problemcodes.Klasse(23) == Problemcodes.Transient);
            h.Ist("unbekannter Code 99: Klasse harmlos (nie Defekt raten)", Problemcodes.Klasse(99) == Problemcodes.Harmlos);
        }

        // ---------------------------------------------------------------- Gehaeuse

        static void Gehaeuse(Harness h)
        {
            h.Gruppe("Hardware: Gehaeusetypen an EINER Stelle (All-in-One, Mini-PC, Stick-PC sind Desktop)");
            var desktop = new[] { 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 34, 35, 36 };
            var mobil = new[] { 8, 9, 10, 11, 12, 14, 30, 31, 32 };
            h.Ist("Grundmenge: 14 Desktop- und 9 Mobil-Typen erwartet", desktop.Length == 14 && mobil.Length == 9);
            h.Ist("Hardware.GehaeuseDesktop = " + string.Join(",", desktop), Kern.Hardware.GehaeuseDesktop.OrderBy(x => x).SequenceEqual(desktop), string.Join(",", Kern.Hardware.GehaeuseDesktop));
            h.Ist("Hardware.GehaeuseMobil = " + string.Join(",", mobil), Kern.Hardware.GehaeuseMobil.OrderBy(x => x).SequenceEqual(mobil), string.Join(",", Kern.Hardware.GehaeuseMobil));
            h.Ist("keine Ueberschneidung der beiden Mengen", !Kern.Hardware.GehaeuseDesktop.Intersect(Kern.Hardware.GehaeuseMobil).Any());
            foreach (int t in new[] { 13, 15, 16, 24, 35, 36 })
            {
                var hw = new Kern.Hardware { SystemTyp = 1, GehaeuseTypen = { t }, Akku = new Akku { Vorhanden = false } };
                h.Ist("ChassisType " + t + " ohne Akku: sicher Desktop, IstMobil false", hw.GehaeuseSagtDesktop && !hw.GehaeuseSagtMobil && hw.IstMobil == false);
            }
            foreach (int t in new[] { 9, 10, 31 })
            {
                var hw = new Kern.Hardware { SystemTyp = 2, GehaeuseTypen = { t } };
                h.Ist("ChassisType " + t + ": mobil, nicht Desktop", hw.GehaeuseSagtMobil && !hw.GehaeuseSagtDesktop && hw.IstMobil == true);
            }
            var leer = new Kern.Hardware();
            h.Ist("ohne Gehaeusetyp: weder Desktop noch mobil, IstMobil null", !leer.GehaeuseSagtDesktop && !leer.GehaeuseSagtMobil && leer.IstMobil == null);

            var s = h.Bild("gepflanzt-hardware-gehaeuse-aio.json");
            if (s == null) return;
            h.Ist("Grundmenge: ChassisTypes [13], 0 Akkus", s.Hardware.GehaeuseTypen.SequenceEqual(new[] { 13 }) && s.Hardware.Akku != null);
            h.Ist("All-in-One: akku.vorhanden false, kein wmi.battery-Eintrag in der Fehlerliste", s.Hardware.Akku.Vorhanden == false && !s.FehlerVon("wmi.battery").Any());
            h.Ist("All-in-One: IstMobil false", s.Hardware.IstMobil == false);
            var erg = Laufen(h, s);
            h.Ist("Bereich Hardware ok", Harness.Bereich(erg, Bereich.Hardware).Zustand == Zustand.Ok, Harness.Bereich(erg, Bereich.Hardware).Zustand);
        }

        // ---------------------------------------------------------------- RAM-Rueckfall

        static void RamRueckfall(Harness h)
        {
            h.Gruppe("Hardware: hardware.ramGesamtKB bleibt TotalPhysicalMemory (Nenner der Zaehler), Modulsumme in ramInstalliertKB");
            var s = h.Bild("gepflanzt-hardware-ram-rueckfall.json");
            if (s == null) return;
            var L = s.Leistung;
            h.Ist("Grundmenge: leistung ohne ramGesamtKB, hardware 64644836 KB, Module 67108864 KB, 3 Messungen", L.RamGesamtKB == 0 && s.Hardware.RamGesamtKB == 64644836 && s.Hardware.RamInstalliertKB == 67108864 && L.VerfuegbarMB.Count == 3);
            // 6400 MB von 63129 MB (TotalPhysicalMemory) sind 10,1 % -> ok; von 65536 MB (Modulsumme) 9,8 % -> warn.
            double pctGesamt = L.VerfuegbarMB.Min() * 100.0 / (s.Hardware.RamGesamtKB / 1024);
            double pctModule = L.VerfuegbarMB.Min() * 100.0 / (s.Hardware.RamInstalliertKB.Value / 1024);
            h.Ist("Grundmenge: der Wert liegt genau zwischen den beiden Nennern (" + pctGesamt.ToString("N1") + " % gegen " + pctModule.ToString("N1") + " %)", pctGesamt >= Schwellen.VerfuegbarWarnPct && pctModule < Schwellen.VerfuegbarWarnPct);
            var erg = h.Pruefen(s);
            var b = Harness.Einer(erg, "leistung.speicher.verfuegbar");
            h.Ist("Rueckfall auf hardware.ramGesamtKB: Befund vorhanden und ok, nicht warn", b != null && b.Zustand == Zustand.Ok, b == null ? "kein Befund" : b.Zustand + " / " + b.Satz);
        }

        // ---------------------------------------------------------------- Code 24 (Gewollt, nicht "abgeschaltet")

        static void Code24(Harness h)
        {
            h.Gruppe("Hardware: Code 24 -> Frage „entfernt?“; mit Antwort absicht sagt der Satz „entfernt“, nicht „abgeschaltet“");
            var s = h.Bild("gepflanzt-hardware-code24.json");
            if (s == null) return;
            var g = s.Geraete.FirstOrDefault(x => x.ProblemCode == 24 && x.Present);
            h.Ist("Grundmenge: ein praesentes Geraet mit Code 24, mindestens 3 gesunde", g != null && s.Geraete.Count(x => x.ProblemCode == 0) >= 3);
            if (g == null) return;
            string frageId = "geraet:" + g.InstanzId + ":24";

            var b = Harness.Einer(Laufen(h, s), "geraet.problem." + g.InstanzId);
            h.Ist("ohne Antwort: unknown mit Frage, Ja heisst absicht", b != null && b.Zustand == Zustand.Unknown && b.Frage != null && b.Frage.JaHeisst == Entscheidungen.Absicht, b == null ? null : b.Zustand);
            h.Ist("ohne Antwort: Frage fragt nach „entfernt“, Ja-Text „Ja, ist entfernt“", b != null && b.Frage != null && b.Frage.Text.Contains("entfernt") && b.Frage.Ja == "Ja, ist entfernt", b == null || b.Frage == null ? null : b.Frage.Text);

            var e = new Entscheidungen();
            e.Setze(frageId, Entscheidungen.Absicht, "2026-09-11T15:00:00Z");
            var erg2 = Laufen(h, s, e);
            var b2 = Harness.Einer(erg2, "geraet.problem." + g.InstanzId);
            h.Ist("Antwort absicht: Zustand absicht, keine Frage", b2 != null && b2.Zustand == Zustand.Absicht && b2.Frage == null, b2 == null ? null : b2.Zustand);
            h.Ist("Antwort absicht: Satz sagt „ist entfernt“ und „so wie Sie es festgelegt haben“", b2 != null && b2.Satz.Contains("ist entfernt") && b2.Satz.Contains("so wie Sie es festgelegt haben"), b2 == null ? null : b2.Satz);
            h.Ist("Antwort absicht: weder Satz noch Titel sagen „abgeschaltet“", b2 != null && !b2.Satz.Contains("abgeschaltet") && !b2.Titel.Contains("abgeschaltet"), b2 == null ? null : b2.Titel + " / " + b2.Satz);
            h.Ist("Antwort absicht: Bereich ok", Harness.Bereich(erg2, Bereich.Hardware).Zustand == Zustand.Ok);

            var e3 = new Entscheidungen();
            e3.Setze(frageId, Entscheidungen.Reparieren, "2026-09-11T15:00:00Z");
            var b3 = Harness.Einer(Laufen(h, s, e3), "geraet.problem." + g.InstanzId);
            h.Ist("Antwort reparieren: bad, Rat nennt Verbindung, keine Massnahme geraet.aktivieren (nur Code 22)", b3 != null && b3.Zustand == Zustand.Bad && b3.Rat.Contains("Verbindung") && !b3.Massnahmen.Contains("geraet.aktivieren"), b3 == null ? null : b3.Rat);
        }

        // ---------------------------------------------------------------- Code 51 und 57

        static void Code51Und57(Harness h)
        {
            h.Gruppe("Hardware: Code 51 (wartet auf Geraet) und 57 (VM-Zuweisung) -> Gewollt: Frage, nie bad, nie warn");
            var s = h.Bild("gepflanzt-hardware-code51-57.json");
            if (s == null) return;
            var g51 = s.Geraete.FirstOrDefault(x => x.ProblemCode == 51);
            var g57 = s.Geraete.FirstOrDefault(x => x.ProblemCode == 57);
            h.Ist("Grundmenge: je ein Geraet mit 51 und 57, mindestens 3 gesunde", g51 != null && g57 != null && s.Geraete.Count(x => x.ProblemCode == 0) >= 3);
            if (g51 == null || g57 == null) return;

            var erg = Laufen(h, s);
            var hw = Harness.Bereich(erg, Bereich.Hardware);
            h.Ist("ohne Antwort: kein bad, kein warn im Bereich", !Harness.Befunde(erg, Bereich.Hardware).Any(b => b.Zustand == Zustand.Bad || b.Zustand == Zustand.Warn), hw.Zustand);
            var b51 = Harness.Einer(erg, "geraet.problem." + g51.InstanzId);
            var b57 = Harness.Einer(erg, "geraet.problem." + g57.InstanzId);
            h.Ist("Code 51: unknown mit Frage, Frage-Id traegt 51", b51 != null && b51.Zustand == Zustand.Unknown && b51.Frage != null && b51.Frage.Id == "geraet:" + g51.InstanzId + ":51", b51 == null ? null : b51.Zustand);
            h.Ist("Code 51: Frage nennt das Warten auf ein anderes Geraet, nicht den generischen Text", b51 != null && b51.Frage != null && b51.Frage.Text.Contains("wartet auf ein anderes Gerät") && !b51.Frage.Text.Contains("meldet Code 51"), b51 == null || b51.Frage == null ? null : b51.Frage.Text);
            h.Ist("Code 57: unknown mit Frage, Text nennt die virtuelle Maschine", b57 != null && b57.Zustand == Zustand.Unknown && b57.Frage != null && b57.Frage.Text.Contains("virtuelle Maschine"), b57 == null || b57.Frage == null ? null : b57.Frage.Text);
            h.Ist("keine Massnahme ohne Antwort", b51 != null && b57 != null && b51.Massnahmen.Count == 0 && b57.Massnahmen.Count == 0);

            var e = new Entscheidungen();
            e.Setze("geraet:" + g51.InstanzId + ":51", Entscheidungen.Absicht, "2026-09-11T15:00:00Z");
            e.Setze("geraet:" + g57.InstanzId + ":57", Entscheidungen.Absicht, "2026-09-11T15:00:00Z");
            var erg2 = Laufen(h, s, e);
            var a51 = Harness.Einer(erg2, "geraet.problem." + g51.InstanzId);
            var a57 = Harness.Einer(erg2, "geraet.problem." + g57.InstanzId);
            h.Ist("Antwort absicht: beide absicht, Titel „So gewollt“, nicht „abgeschaltet“", a51 != null && a57 != null && a51.Zustand == Zustand.Absicht && a57.Zustand == Zustand.Absicht && !a51.Titel.Contains("abgeschaltet") && !a57.Titel.Contains("abgeschaltet"), a51 == null ? null : a51.Titel);
            h.Ist("Antwort absicht: Satz 51 nennt die Wartestellung, Satz 57 die virtuelle Maschine", a51 != null && a57 != null && a51.Satz.Contains("Wartestellung") && a57.Satz.Contains("virtuelle Maschine"), a51 == null || a57 == null ? null : a51.Satz + " / " + a57.Satz);
            h.Ist("Antwort absicht: Bereich ok", Harness.Bereich(erg2, Bereich.Hardware).Zustand == Zustand.Ok);

            var e3 = new Entscheidungen();
            e3.Setze("geraet:" + g51.InstanzId + ":51", Entscheidungen.Reparieren, "2026-09-11T15:00:00Z");
            e3.Setze("geraet:" + g57.InstanzId + ":57", Entscheidungen.Reparieren, "2026-09-11T15:00:00Z");
            var erg3 = Laufen(h, s, e3);
            var r51 = Harness.Einer(erg3, "geraet.problem." + g51.InstanzId);
            var r57 = Harness.Einer(erg3, "geraet.problem." + g57.InstanzId);
            h.Ist("Antwort reparieren 51: bad, Rat verweist auf das uebergeordnete Geraet, verspricht keinen Neustart", r51 != null && r51.Zustand == Zustand.Bad && r51.Rat.Contains("übergeordnete") && !r51.Rat.Contains("neu starten") && !r51.Rat.Contains("Neustart"), r51 == null ? null : r51.Rat);
            h.Ist("Antwort reparieren 57: bad, Rat nennt Hyper-V, keine Massnahme", r57 != null && r57.Zustand == Zustand.Bad && r57.Rat.Contains("Hyper-V") && r57.Massnahmen.Count == 0, r57 == null ? null : r57.Rat);
        }

        // ---------------------------------------------------------------- Firmware-Codes 33/35/36 und Code 9

        static void FirmwareCodes(Harness h)
        {
            h.Gruppe("Hardware: Codes 33/35/36 raten zu BIOS-Setup oder BIOS-Update, nicht zum Abziehen; Code 9 zum Hersteller");
            var s = h.Bild("gepflanzt-hardware-firmware-codes.json");
            if (s == null) return;
            h.Ist("Grundmenge: je ein Geraet mit 33, 35, 36 und 9, mindestens 3 gesunde",
                  new[] { 33, 35, 36, 9 }.All(c => s.Geraete.Count(g => g.ProblemCode == c) == 1) && s.Geraete.Count(g => g.ProblemCode == 0) >= 3);
            var erg = Laufen(h, s);
            foreach (int c in new[] { 33, 35, 36, 9 })
            {
                var g = s.Geraete.First(x => x.ProblemCode == c);
                var b = Harness.Einer(erg, "geraet.problem." + g.InstanzId);
                h.Ist("Code " + c + ": bad ohne Massnahme, ohne Frage", b != null && b.Zustand == Zustand.Bad && b.Massnahmen.Count == 0 && b.Frage == null, b == null ? null : b.Zustand);
                if (b == null) continue;
                switch (c)
                {
                    case 33:
                        h.Ist("Code 33: Rat nennt BIOS-Setup und Hersteller, nicht das Abziehen", b.Rat.Contains("BIOS-Setup") && b.Rat.Contains("hersteller") && !b.Rat.Contains("abziehen"), b.Rat);
                        break;
                    case 35:
                        h.Ist("Code 35: Rat nennt das BIOS-Update, weder Abziehen noch Defekt-Urteil", b.Rat.Contains("BIOS") && b.Rat.Contains("Update") && !b.Rat.Contains("abziehen") && !b.Rat.Contains("wahrscheinlich defekt"), b.Rat);
                        h.Ist("Code 35: Titel nennt die Firmware, nicht den Ausfall", b.Titel.Contains("Firmware") && !b.Titel.Contains("Ausfall"), b.Titel);
                        break;
                    case 36:
                        h.Ist("Code 36: Rat nennt IRQ-Reservierung im BIOS-Setup, nicht das Abziehen", b.Rat.Contains("BIOS-Setup") && b.Rat.Contains("IRQ") && !b.Rat.Contains("abziehen"), b.Rat);
                        break;
                    case 9:
                        h.Ist("Code 9: Rat nennt den Hersteller, kein BIOS-Rat, kein Abziehen", b.Rat.Contains("Hersteller") && !b.Rat.Contains("BIOS") && !b.Rat.Contains("abziehen"), b.Rat);
                        break;
                }
            }
            h.Ist("Bereich bad", Harness.Bereich(erg, Bereich.Hardware).Zustand == Zustand.Bad);

            // Gegenprobe: Code 43 (Steckgeraet moeglich) nennt das Abziehen nur als Bedingung ("ein Steckgeraet").
            var s43 = h.Bild("gepflanzt-hardware-code43.json");
            if (s43 == null) return;
            var b43 = Harness.Einer(Laufen(h, s43), "geraet.problem." + UsbGeraet);
            h.Ist("Code 43: Rat nennt Geraete-Manager zuerst und das Abziehen nur fuer ein Steckgeraet", b43 != null && b43.Rat.Contains("Geräte-Manager") && b43.Rat.Contains("Steckgerät") && b43.Rat.IndexOf("Geräte-Manager") < b43.Rat.IndexOf("Steckgerät"), b43 == null ? null : b43.Rat);
        }
    }
}
