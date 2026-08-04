using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Fund = WartungsToolbox.RegistryScan.Fund;

namespace WartungsToolbox
{
    /// <summary>
    /// Probelauf fuer src/RegistryScan.cs. Rein lesend - es wird nichts entfernt.
    ///
    /// Zweck: Vor dem Loeschen muss belegt sein, dass die Suche keine Fehlalarme
    /// erzeugt. Genau daran scheitern die unserioesen "Registry-Reiniger": sie melden
    /// Vermutungen als Befund und machen damit Programme kaputt.
    ///
    /// Geprueft werden deshalb Eigenschaften, die auf JEDEM Rechner gelten muessen
    /// (auch auf einem frisch aufgesetzten Bau-Server, wo es null Funde gibt):
    ///
    ///   1. Jeder Fund ist nachweisbar: die genannte Datei bzw. der Ordner fehlt
    ///      wirklich. Ein Fund, dessen Ziel existiert, ist ein Fehlalarm.
    ///   2. Kein Fund auf einem Laufwerk, das gerade nicht da ist (USB, Netz).
    ///   3. Jeder Fund nennt einen absoluten Pfad mit Laufwerksbuchstaben.
    ///   4. Kennungen sind eindeutig, und kein Eintrag steht doppelt in der Liste -
    ///      sonst laeuft das Entfernen beim zweiten Mal ins Leere.
    ///   5. Nichts aus den Schutzbereichen von Windows selbst.
    ///
    /// Zusaetzlich wird jeder Fund ausgedruckt, damit sich die Trefferquote auf einem
    /// echten Rechner von Hand beurteilen laesst.
    /// </summary>
    static class RegistryProbe
    {
        static int fehler = 0;

        static void Ist(string was, bool bedingung, string detail)
        {
            Console.WriteLine((bedingung ? "         [ok]   " : "         [FEHL] ") + was +
                              (detail.Length > 0 ? "   " + detail : ""));
            if (!bedingung) fehler++;
        }

        // Schluesselpfade, an denen ein Fund immer ein Fehlalarm waere: dort haengen
        // Windows-eigene Bestandteile, deren Dateien katalogweise nachgeliefert werden.
        static readonly string[] Tabu =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            @"SYSTEM\CurrentControlSet",
        };

        static void Main(string[] args)
        {
            bool ausfuehrlich = args.Any(a => string.Equals(a, "-all", StringComparison.OrdinalIgnoreCase));

            var uhr = System.Diagnostics.Stopwatch.StartNew();
            List<RegistryScan.Fund> funde = RegistryScan.Run(null, null);
            uhr.Stop();

            Console.WriteLine("         ---- Probelauf Registrierung ----");
            Console.WriteLine("         Dauer: " + uhr.ElapsedMilliseconds + " ms, Funde: " + funde.Count);

            foreach (var g in funde.GroupBy(f => f.Kategorie).OrderByDescending(g => g.Count()))
                Console.WriteLine("           " + g.Count().ToString().PadLeft(4) + "  " + g.Key);

            // ---- 1. Jeder Fund ist nachweisbar --------------------------------------
            var lebendig = funde.Where(f => f.Ziel != null &&
                                            (File.Exists(f.Ziel) || Directory.Exists(f.Ziel))).ToList();
            Ist("kein Fund, dessen Ziel in Wahrheit existiert", lebendig.Count == 0,
                lebendig.Count == 0 ? "" : lebendig.Count + " Fehlalarm(e), z. B. " + lebendig[0].Ziel);

            // ---- 2. Kein Fund auf einem abwesenden Laufwerk --------------------------
            var abwesend = funde.Where(f => !LaufwerkDa(f.Ziel)).ToList();
            Ist("kein Fund auf einem Laufwerk, das gerade fehlt", abwesend.Count == 0,
                abwesend.Count == 0 ? "" : abwesend.Count + " Stueck, z. B. " + (abwesend[0].Ziel ?? "null"));

            // ---- 3. Absolute Pfade ---------------------------------------------------
            var krumm = funde.Where(f => f.Ziel == null || f.Ziel.Length < 4
                                         || f.Ziel[1] != ':' || f.Ziel[2] != '\\').ToList();
            Ist("jeder Fund nennt einen absoluten Pfad", krumm.Count == 0,
                krumm.Count == 0 ? "" : krumm.Count + " Stueck, z. B. " + (krumm[0].Ziel ?? "null"));

            // ---- 4. Eindeutigkeit ----------------------------------------------------
            Ist("Kennungen sind eindeutig",
                funde.Select(f => f.Id).Distinct().Count() == funde.Count, "");

            var doppelt = funde.GroupBy(f => (f.Hive + "\\" + f.Pfad + "|" + (f.Wert ?? "")).ToLowerInvariant())
                               .Where(g => g.Count() > 1).ToList();
            Ist("kein Eintrag steht doppelt in der Liste", doppelt.Count == 0,
                doppelt.Count == 0 ? "" : doppelt.Count + " Dublette(n), z. B. " + doppelt[0].Key);

            // ---- 5. Schutzbereiche ----------------------------------------------------
            var tabu = funde.Where(f => Tabu.Any(t => (f.Pfad ?? "").StartsWith(t, StringComparison.OrdinalIgnoreCase))).ToList();
            Ist("nichts aus den Schutzbereichen von Windows", tabu.Count == 0,
                tabu.Count == 0 ? "" : tabu.Count + " Stueck, z. B. " + tabu[0].Pfad);

            // ---- 6. Jeder Fund traegt die Felder, die die Oberflaeche braucht ---------
            var stumm = funde.Where(f => string.IsNullOrWhiteSpace(f.Titel)
                                      || string.IsNullOrWhiteSpace(f.Grund)
                                      || string.IsNullOrWhiteSpace(f.Kategorie)).ToList();
            Ist("jeder Fund ist erklaert (Titel, Grund, Kategorie)", stumm.Count == 0,
                stumm.Count == 0 ? "" : stumm.Count + " ohne Erklaerung");

            // ---- 7. Der Lauf ist wiederholbar ----------------------------------------
            // Ein zweiter Lauf muss dasselbe finden. Weicht er ab, haengt das Ergebnis an
            // etwas Zufaelligem - dann duerfte man darauf nichts loeschen.
            List<RegistryScan.Fund> zweiter = RegistryScan.Run(null, null);
            Ist("zweiter Lauf findet dasselbe", zweiter.Count == funde.Count,
                zweiter.Count == funde.Count ? "" : funde.Count + " gegen " + zweiter.Count);

            // ---- 8. Das zweite Schloss vor dem Entfernen ------------------------------
            PruefeSchloss();

            // ---- 9. Abbruch liefert trotzdem brauchbare Kennungen ---------------------
            PruefeAbbruch();

            // ---- 10. Erst sichern, dann loeschen --------------------------------------
            PruefeSicherungVorLoeschung();

            // ---- 11. Eine misslungene Sicherung stoppt ALLES --------------------------
            PruefeAllesOderNichts();

            // ---- Auflistung fuer die Beurteilung von Hand -----------------------------
            // Nur auf Anforderung: in der Testsuite wuerde die Liste alles andere
            // zuschuetten. Zum Nachmessen auf einem echten Rechner:
            //   dotnet <csc> ... /out:probe.exe && probe.exe -all
            if (funde.Count > 0 && ausfuehrlich)
            {
                Console.WriteLine("         ---- Funde im Einzelnen ----");
                foreach (var g in funde.GroupBy(f => f.Kategorie))
                {
                    Console.WriteLine("         [" + g.Key + "]");
                    foreach (var f in g)
                    {
                        Console.WriteLine("           " + f.Titel);
                        Console.WriteLine("             Ort  : " + f.Hive + "\\" + f.Pfad + (f.Wert != null ? "  ->  " + f.Wert : ""));
                        Console.WriteLine("             Fehlt: " + f.Ziel);
                    }
                }
            }
            else if (funde.Count > 0)
            {
                Console.WriteLine("         (Einzelauflistung mit -all)");
            }

            Environment.Exit(fehler == 0 ? 0 : 1);
        }

        /// <summary>
        /// Das zweite Schloss (DarfEntferntWerden) an Grenzfaellen. Wichtigster davon:
        /// Ein Wurzelschluessel darf NIE als ganzer Schluessel entfernt werden. Bei
        /// "...\CurrentVersion\Run" gegen "...\CurrentVersion\RunOnce" muss ausserdem
        /// die Praefix-Falle greifen: "Run" ist ein Praefix von "RunOnce".
        /// </summary>
        static void PruefeSchloss()
        {
            const string run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string uninst = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

            Fund F(string hive, string pfad, string wert)
            {
                return new Fund { Hive = hive, Pfad = pfad, Wert = wert };
            }

            Ist("Schloss: Wert unter einem Wurzelschluessel ist erlaubt",
                RegistryScan.DarfEntferntWerden(F("HKCU", run, "Irgendwas")), "");
            Ist("Schloss: der Wurzelschluessel selbst wird NICHT entfernt",
                !RegistryScan.DarfEntferntWerden(F("HKCU", run, null)), "");
            Ist("Schloss: Unterschluessel unter Uninstall ist erlaubt",
                RegistryScan.DarfEntferntWerden(F("HKLM", uninst + @"\Irgendwas", null)), "");
            Ist("Schloss: Uninstall selbst wird NICHT entfernt",
                !RegistryScan.DarfEntferntWerden(F("HKLM", uninst, null)), "");
            Ist("Schloss: RunOnce selbst faellt nicht als Schluessel",
                !RegistryScan.DarfEntferntWerden(F("HKCU", run + "Once", null)), "");

            // Der eigentliche Praefix-Test: "Run" ist ein Anfang von "RunOnce". Wer die
            // Liste der erlaubten Bereiche nur bis zum ersten Treffer durchgeht, bleibt an
            // "Run" haengen, liest hinter dem Praefix ein 'O' statt eines Trenners und
            // lehnt ab. RunOnce-Eintraege waeren damit fuer immer unentfernbar.
            Ist("Schloss: RunOnce-WERTE lassen sich entfernen (Praefix-Falle)",
                RegistryScan.DarfEntferntWerden(F("HKCU", run + "Once", "Irgendwas")), "");

            Ist("Schloss: fremder Bereich wird abgelehnt",
                !RegistryScan.DarfEntferntWerden(F("HKLM", @"SYSTEM\CurrentControlSet\Services", null)), "");
            Ist("Schloss: Bereich mit nur zufaellig gleichem Anfang wird abgelehnt",
                !RegistryScan.DarfEntferntWerden(F("HKCU", run + "Boese", "x")), "");
            Ist("Schloss: leerer Pfad wird abgelehnt",
                !RegistryScan.DarfEntferntWerden(F("HKLM", "", null)), "");

            // Bei Dateityp-Eintraegen faellt NUR der nachgewiesene Zweig, nie der ganze
            // Dateityp: unter ihm haengen Symbol, weitere Befehle und Explorer-Erweiterungen,
            // die niemand geprueft hat.
            Ist("Schloss: HKCR nur der Zweig shell\\open, nie der ganze Dateityp",
                RegistryScan.DarfEntferntWerden(F("HKCR", @"Foo.Bar\shell\open", null))
                && !RegistryScan.DarfEntferntWerden(F("HKCR", "Foo.Bar", null))
                && !RegistryScan.DarfEntferntWerden(F("HKCR", @"Foo.Bar\shell", null))
                && !RegistryScan.DarfEntferntWerden(F("HKCR", @"shell\open", null)), "");
        }

        /// <summary>
        /// Ein abgebrochener Lauf muss trotzdem vollstaendige Kennungen liefern. Vorher
        /// wurden sie erst ganz am Ende vergeben: bei Abbruch kamen Funde mit Id=null
        /// zurueck, und der naechste Zugriff darauf riss den Oberflaechen-Thread mit.
        /// </summary>
        static void PruefeAbbruch()
        {
            List<RegistryScan.Fund> teil = RegistryScan.Run(null, () => true);
            Ist("abgebrochener Lauf liefert nur Funde mit Kennung",
                teil.All(f => !string.IsNullOrEmpty(f.Id)), teil.Count + " Funde");
            Ist("Kennungen sind auch nach Abbruch eindeutig",
                teil.Select(f => f.Id).Distinct().Count() == teil.Count, "");
        }

        /// <summary>
        /// Die wichtigste Zusage des ganzen Bausteins: Es wird erst gesichert, dann
        /// geloescht. Behauptungen genuegen dafuer nicht.
        ///
        /// Die Probe legt sich dafuer EINEN eigenen Autostart-Eintrag mit eindeutigem
        /// Namen an, der auf einen Pfad zeigt, den es garantiert nicht gibt. Danach
        /// laeuft die echte Suche darueber, der Fund wird durch Entferne() geschickt,
        /// und geprueft wird: Ist die Sicherungsdatei da, steht der Eintrag darin, und
        /// ist er anschliessend wirklich weg? Aufgeraeumt wird in jedem Fall.
        /// </summary>
        static void PruefeSicherungVorLoeschung()
        {
            const string runPfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            string name = "WW-Probe-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string ziel = @"C:\gibt-es-nicht-" + Guid.NewGuid().ToString("N") + @"\nichts.exe";
            string sicherung = null;

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(runPfad, true))
                {
                    if (k == null) { Ist("Sicherung vor Loeschung", false, "Autostart-Schluessel nicht schreibbar"); return; }
                    k.SetValue(name, "\"" + ziel + "\"", RegistryValueKind.String);
                }

                List<RegistryScan.Fund> funde = RegistryScan.Run(null, null);
                RegistryScan.Fund meiner = funde.FirstOrDefault(
                    f => f.Wert == name && f.Hive == "HKCU");
                Ist("angelegter Testeintrag wird gefunden", meiner != null, name);
                if (meiner == null) return;

                int entfernt, fehlgeschlagen;
                sicherung = RegistryScan.Entferne(
                    new List<RegistryScan.Fund> { meiner }, out entfernt, out fehlgeschlagen);

                Ist("Sicherungsdatei wurde geschrieben",
                    sicherung != null && File.Exists(sicherung), sicherung ?? "null");

                string inhalt = (sicherung != null && File.Exists(sicherung))
                    ? File.ReadAllText(sicherung, System.Text.Encoding.Unicode) : "";
                Ist("der Eintrag steht in der Sicherung",
                    inhalt.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0, "");

                Ist("genau ein Eintrag entfernt, keiner fehlgeschlagen",
                    entfernt == 1 && fehlgeschlagen == 0, entfernt + "/" + fehlgeschlagen);

                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(runPfad))
                    Ist("der Eintrag ist danach wirklich weg",
                        k != null && k.GetValue(name) == null, "");
            }
            catch (Exception ex)
            {
                Ist("Sicherung vor Loeschung laeuft ohne Ausnahme", false, ex.Message);
            }
            finally
            {
                // Unter allen Umstaenden aufraeumen: weder der Testeintrag noch die
                // Sicherung (sie enthaelt die Autostart-Liste des Nutzers) bleiben liegen.
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(runPfad, true))
                        if (k != null) k.DeleteValue(name, false);
                }
                catch { }
                try { if (sicherung != null && File.Exists(sicherung)) File.Delete(sicherung); }
                catch { }
            }
        }

        /// <summary>
        /// Alles oder nichts: Laesst sich auch nur EIN betroffener Schluessel nicht sichern,
        /// darf KEIN Eintrag entfernt werden.
        ///
        /// Vorher genuegte ein einziger gelungener Export, damit die Sicherung als
        /// vollstaendig galt. Wer sechs Eintraege auswaehlte und bei fuenf davon scheiterte
        /// die Sicherung, verlor diese fuenf ersatzlos - und las dazu die Zusage, ohne
        /// vollstaendige Sicherung werde nichts entfernt.
        ///
        /// Nachgestellt wird das mit einem echten Eintrag plus einem Schluessel, den es
        /// nicht gibt: reg.exe kann ihn nicht exportieren. Danach muss der echte Eintrag
        /// noch da sein.
        /// </summary>
        static void PruefeAllesOderNichts()
        {
            const string runPfad = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string uninst = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            string name = "WW-Probe2-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(runPfad, true))
                {
                    if (k == null) { Ist("Alles oder nichts", false, "Autostart-Schluessel nicht schreibbar"); return; }
                    k.SetValue(name, @"C:\gibt-es-nicht\nichts.exe", RegistryValueKind.String);
                }

                var echt = new Fund
                {
                    Hive = "HKCU", Pfad = runPfad, Wert = name,
                    Titel = name, Grund = "Testeintrag", Kategorie = "Test", Id = "t0",
                };
                // Liegt im erlaubten Bereich, existiert aber nicht -> reg export scheitert.
                var kaputt = new Fund
                {
                    Hive = "HKCU", Pfad = uninst + @"\WW-GibtEsNicht-" + Guid.NewGuid().ToString("N"),
                    Titel = "nicht vorhanden", Grund = "Testeintrag", Kategorie = "Test", Id = "t1",
                };

                int entfernt = -1, fehlgeschlagen = -1;
                bool geworfen = false;
                try { RegistryScan.Entferne(new List<Fund> { echt, kaputt }, out entfernt, out fehlgeschlagen); }
                catch (InvalidOperationException) { geworfen = true; }

                Ist("misslungene Sicherung bricht den ganzen Vorgang ab", geworfen,
                    geworfen ? "" : "entfernt=" + entfernt);

                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(runPfad))
                    Ist("der sicherbare Eintrag ist dabei UNANGETASTET geblieben",
                        k != null && k.GetValue(name) != null, "");
            }
            catch (Exception ex)
            {
                Ist("Alles-oder-nichts laeuft ohne unerwartete Ausnahme", false,
                    ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(runPfad, true))
                        if (k != null) k.DeleteValue(name, false);
                }
                catch { }
            }
        }

        static bool LaufwerkDa(string pfad)
        {
            if (string.IsNullOrEmpty(pfad) || pfad.Length < 3) return false;
            try { return new DriveInfo(pfad.Substring(0, 1)).IsReady; }
            catch { return false; }
        }
    }
}
