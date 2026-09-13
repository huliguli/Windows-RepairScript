using System;
using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben gegen die echten, redigierten Aufzeichnungen des Entwicklungsrechners
    /// (Konzept 7.1): dieser-pc-de.json (erhoeht), dieser-pc-de-ohne-admin.json (Medium-IL-Token
    /// ueber eine geplante Aufgabe mit /rl LIMITED) und dieser-pc-en.json (dieselbe Aufzeichnung
    /// mit LCID 1033). Zugesichert werden die dokumentierten Befunde dieses Rechners (Uebergabe,
    /// Messwerte vom 11./13.09.2026), als Beziehungen: "wenn X im Bild, dann genau ein Befund Y".
    /// Ein Bild, das keine dieser Beziehungen mehr erfuellt, ist rot, nicht "heute eben anders".
    /// </summary>
    public static class DieserPcProben
    {
        public static void Laufen(Harness h)
        {
            // Die echten Aufzeichnungen bleiben lokal (Konzept 10: Software-Inventar eines
            // privaten Rechners gehoert nicht ins oeffentliche Repo). Fehlen sie, ist das kein
            // stilles Gruen, sondern eine gemeldete Auslassung; in der CI ist das der Normalfall.
            if (!System.IO.File.Exists(System.IO.Path.Combine(h.Aufzeichnungen, "dieser-pc-de.json")))
            {
                h.Uebersprungen("DieserPcProben", "dieser-pc-*.json liegen nur lokal (docs/werkzeuge/aufzeichnen-dieser-pc.ps1 erzeugt sie)");
                return;
            }
            var alle = new List<Befund>();
            Erhoeht(h, alle);
            OhneAdmin(h, alle);
            Englisch(h, alle);

            h.Gruppe("Dieser PC: jeder Satz trägt Zahl, Bedingung oder Absage");
            h.SaetzeTragen(alle);
            h.Ist("Grundmenge: mindestens 40 Befunde über die echten Bilder", alle.Count >= 40, alle.Count.ToString());
        }

        static void Grundmengen(Harness h, Systembild s, string name)
        {
            h.Ist(name + ": mindestens 200 Geräte", s.Geraete.Count >= 200, s.Geraete.Count.ToString());
            h.Ist(name + ": zwei NVMe-Datenträger", s.Datentraeger.Count(d => d.BusTyp == 17) == 2, s.Datentraeger.Count.ToString());
            h.Ist(name + ": mindestens 3 Volumes (C:, D:, EFI)", s.Volumes.Count >= 3, s.Volumes.Count.ToString());
            h.Ist(name + ": Ereignisse vorhanden", s.Ereignisse.Eintraege.Count >= 100, s.Ereignisse.Eintraege.Count.ToString());
            h.Ist(name + ": Redaktion hat gegriffen (kein Rechnername, KONTO-Platzhalter)", s.Sicherheit.Konten.All(k => k.Name.StartsWith("KONTO")));
            h.Ist(name + ": hosts nur Treffer, Rest entfernt", s.Netz.HostsEntfernt >= 50, s.Netz.HostsEntfernt.ToString());
        }

        static void Erhoeht(Harness h, List<Befund> alle)
        {
            h.Gruppe("Dieser PC (erhöht): Code 22 nur an der iGPU, HP-Update verborgen, DNS fest, EFI unbewertet, Einschalttaste");
            var s = h.Bild("dieser-pc-de.json"); if (s == null) return;
            Grundmengen(h, s, "erhöht");
            h.Ist("Grundmenge: Bild ist erhöht aufgezeichnet", s.Erhoeht);
            h.Ist("Grundmenge: kein zugriff-Eintrag in der Fehlerliste", !s.Fehlerliste.Any(f => f.Art == Fehler.Zugriff),
                string.Join(", ", s.Fehlerliste.Where(f => f.Art == Fehler.Zugriff).Select(f => f.Quelle)));

            var code22 = s.Geraete.Where(g => g.Present && g.ProblemCode == 22).ToList();
            h.Ist("Grundmenge: genau ein präsentes Gerät mit Code 22 (die iGPU)", code22.Count == 1, code22.Count.ToString());

            // Ohne Antwort: genau eine Frage, nie bad.
            var erg = h.Pruefen(s, new Entscheidungen());
            alle.AddRange(Harness.Befunde(erg));
            var fragen = Harness.Befunde(erg, Bereich.Hardware).Where(b => b.Frage != null).ToList();
            h.Ist("ohne Antwort: genau eine Frage im Bereich Hardware, für das Code-22-Gerät",
                fragen.Count == 1 && code22.Count == 1 && fragen[0].Frage.Id == "geraet:" + code22[0].InstanzId + ":22", fragen.Count.ToString());
            h.Ist("kein bad-Befund für das Code-22-Gerät", !Harness.Befunde(erg, Bereich.Hardware).Any(b => b.Zustand == Zustand.Bad && b.Schluessel.Contains(code22[0].InstanzId)));
            h.Ist("Bereich Hardware ist nicht bad", Harness.Bereich(erg, Bereich.Hardware).Zustand != Zustand.Bad);

            // Mit Antwort "so lassen": absicht, Befund bleibt.
            var e = new Entscheidungen();
            e.Setze("geraet:" + code22[0].InstanzId + ":22", Entscheidungen.Absicht, s.AufgezeichnetUtc);
            var erg2 = h.Pruefen(s, e);
            var absicht = Harness.Befunde(erg2, Bereich.Hardware).Where(b => b.Zustand == Zustand.Absicht).ToList();
            h.Ist("mit Antwort: genau ein absicht-Befund, keine Frage mehr", absicht.Count == 1 && !Harness.Befunde(erg2, Bereich.Hardware).Any(b => b.Frage != null));

            // Verborgenes HP-Update (Kennung aus der Uebergabe).
            const string hp = "4340b239-4b90-4305-9c2e-6a24dfcb6757";
            h.Ist("Grundmenge: das HP-USB-Update ist verborgen", s.WindowsUpdate.Verborgen.Any(u => string.Equals(u.UpdateId, hp, StringComparison.OrdinalIgnoreCase)));
            var verborgen = Harness.Einer(erg, "update.verborgen");
            h.Ist("Befund 'verborgene Updates' ist ok (Info), nie warn", verborgen != null && verborgen.Zustand == Zustand.Ok);
            h.Ist("kein Schleifen-Befund für das verborgene Update (es ist ja verborgen)", Harness.Einer(erg, "update.schleife." + hp) == null || Harness.Einer(erg, "update.schleife." + hp).Zustand != Zustand.Warn);

            // DNS fest eingetragen auf DHCP-Anschluss: Info, kein Problem.
            var dns = Harness.Einer(erg, "netz.dns.statisch");
            h.Ist("DNS fest auf DHCP-Anschluss: Befund vorhanden und ok", dns != null && dns.Zustand == Zustand.Ok);

            // EFI-Partition mit Dirty-Bit: nie bewertet.
            var efi = s.Volumes.FirstOrDefault(v => string.IsNullOrEmpty(v.Buchstabe) && v.Dateisystem == "FAT32");
            h.Ist("Grundmenge: EFI-Partition (FAT32 ohne Buchstaben) ist im Bild", efi != null);
            h.Ist("kein Dateisystem-Problem wegen der EFI-Partition", !Harness.Befunde(erg, Bereich.Datentraeger).Any(b => b.IstProblem && b.Schluessel.StartsWith("volume.")));

            // Zwei Kernel-Power 41 mit PowerButtonTimestamp: Einschalttaste, kein Absturz.
            var kp41 = s.Ereignisse.Von("Microsoft-Windows-Kernel-Power", 41).ToList();
            h.Ist("Grundmenge: Kernel-Power 41 vorhanden", kp41.Count >= 1, kp41.Count.ToString());
            h.Ist("Einschalttaste als ok-Befund, kein Blauschirm, kein Stromverlust-warn",
                Harness.Einer(erg, "stabilitaet.einschalttaste") != null
                && Harness.Einer(erg, "stabilitaet.blauschirm") == null
                && (Harness.Einer(erg, "stabilitaet.stromverlust") == null || Harness.Einer(erg, "stabilitaet.stromverlust").Zustand == Zustand.Ok));

            // Microsoft-Konto ohne lokale Kennwortpflicht ist kein Befund.
            h.Ist("Grundmenge: ein Konto mit Microsoft-Konto-Bindung", s.Sicherheit.Konten.Any(k => k.MicrosoftKonto == true));
            h.Ist("kein Befund 'Konto ohne Kennwort'", Harness.Einer(erg, "sicherheit.konto.ohnekennwort") == null);
        }

        static void OhneAdmin(Harness h, List<Befund> alle)
        {
            h.Gruppe("Dieser PC (ohne Admin): jede Admin-Quelle mit zugriff-Eintrag, nichts still leer");
            var s = h.Bild("dieser-pc-de-ohne-admin.json"); if (s == null) return;
            Grundmengen(h, s, "ohne Admin");
            h.Ist("Grundmenge: Bild ist nicht erhöht aufgezeichnet", !s.Erhoeht);
            string[] adminQuellen = { "wmi.storage.reliability", "wmi.volume.dirty", "log.diagnostics-performance", "wmi.defender.exclusions", "wmi.systemrestore.punkte", "wmi.shadowstorage" };
            foreach (string q in adminQuellen)
                h.Ist("zugriff-Eintrag für " + q, s.Fehlerliste.Any(f => f.Quelle == q && f.Art == Fehler.Zugriff));
            var erg = h.Pruefen(s, new Entscheidungen());
            alle.AddRange(Harness.Befunde(erg));
            h.Ist("Datenträger: Fehlend nennt die Zähler (nicht erhöht), kein bad", Harness.Bereich(erg, Bereich.Datentraeger).Fehlend.Count >= 1 && Harness.Bereich(erg, Bereich.Datentraeger).Zustand != Zustand.Bad);
            h.Ist("Volumes ohne Dirty-Bit-Wert: kein Dateisystem-Problem erfunden", !Harness.Befunde(erg, Bereich.Datentraeger).Any(b => b.IstProblem && b.Schluessel.StartsWith("volume.")));
            h.Ist("Sicherheit: Ausnahmen des Virenschutzes als nicht prüfbar geführt, nicht als 'keine'", Harness.Bereich(erg, Bereich.Sicherheit).Fehlend.Any(f => f.Contains("Ausnahmen des Virenschutzes")));
        }

        static void Englisch(Harness h, List<Befund> alle)
        {
            h.Gruppe("Dieser PC (englisch): dieselben Befunde wie deutsch, Anzeigetexte spielen keine Rolle");
            var de = h.Bild("dieser-pc-de.json");
            var en = h.Bild("dieser-pc-en.json");
            if (de == null || en == null) return;
            h.Ist("Grundmenge: LCID 1031 gegen 1033", de.Sprache.Lcid == 1031 && en.Sprache.Lcid == 1033);
            var ergDe = h.Pruefen(de, new Entscheidungen());
            var ergEn = h.Pruefen(en, new Entscheidungen());
            alle.AddRange(Harness.Befunde(ergEn));
            var bDe = Harness.Befunde(ergDe).OrderBy(b => b.Schluessel).ToList();
            var bEn = Harness.Befunde(ergEn).OrderBy(b => b.Schluessel).ToList();
            h.Ist("gleiche Anzahl Befunde", bDe.Count == bEn.Count, bDe.Count + " gegen " + bEn.Count);
            for (int i = 0; i < Math.Min(bDe.Count, bEn.Count); i++)
            {
                h.Ist("gleicher Schlüssel und Zustand: " + bDe[i].Schluessel, bDe[i].Schluessel == bEn[i].Schluessel && bDe[i].Zustand == bEn[i].Zustand, bEn[i].Schluessel + " " + bEn[i].Zustand);
            }
        }
    }
}
