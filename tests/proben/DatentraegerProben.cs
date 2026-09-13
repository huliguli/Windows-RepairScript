using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;
using Regel = WartungsToolbox.Kern.Regeln.Datentraeger;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer die Bereiche Datentraeger und Speicherplatz gegen gepflanzte Systembilder
    /// (tests/aufzeichnungen/gepflanzt-datentraeger-*.json).
    ///
    /// Jede Zusicherung ist eine Beziehung ("wenn Spare unter der Schwelle liegt, dann genau ein
    /// bad-Befund nvme.kritisch fuer dieses Laufwerk"), jede Probe sichert vorher ihre Grundmenge,
    /// und zu jeder Regel gibt es eine Gegenprobe: dasselbe Bild ohne die Bedingung liefert keinen
    /// Befund. Die Gegenproben entstehen durch Abaendern des geladenen Bilds im Code - so ist
    /// sichtbar, dass genau EIN Feld den Unterschied macht.
    ///
    /// Wirksamkeitsproben (2026-09-12, Nachtrag 2026-09-13): jede Regel wurde einmal absichtlich kaputt
    /// gemacht und die zugehoerige Probe wurde rot - Einzelheiten im Kommentar der jeweiligen Gruppe.
    /// Am 2026-09-13 liefen die Mutationen gegen eine Kopie des Kerns (14 Mutationen, jede mindestens
    /// eine rote Zusicherung): 129-Kennzeichen, Positivliste, Frequenz 0, Systemschutz aus, UNBOUNDED,
    /// TRIM-Geraet (zwei), Medienfehler (zwei), VolumeName, Fehlend statt ok, Messwert bei Ausfallcode,
    /// GptMsr-GUID, Wechseldatentraeger-Nachsatz.
    /// </summary>
    public static class DatentraegerProben
    {
        static readonly List<Befund> Alle = new List<Befund>();

        public static void Laufen(Harness h)
        {
            Alle.Clear();
            Gesund(h);
            NvmeSpare(h);
            NvmeStundenNull(h);
            EfiDirty(h);
            CDirty(h);
            WearGegenUsed(h);
            Health2(h);
            Ntfs55(h);
            Platz(h);
            DirtyNull(h);
            Ereignisse(h);
            SystemschutzLeer(h);
            SystemschutzAus(h);
            Schattenspeicher(h);
            VolumeStatus(h);
            TrimUndTemperatur(h);
            TrimGeraet(h);
            Medienfehler(h);
            UsbDirty(h);
            EreignisseFehlend(h);
            Englisch(h);

            h.Gruppe("Datentraeger: Saetze aller Befunde tragen Zahl, Bedingung oder Absage");
            h.Ist("Grundmenge: mindestens 40 Befunde gesammelt", Alle.Count >= 40, Alle.Count.ToString());
            h.SaetzeTragen(Alle);
            foreach (var b in Alle)
            {
                h.Ist("kein gerades Anfuehrungszeichen in Satz/Titel/Rat: " + b.Schluessel,
                    (b.Satz ?? "").IndexOf('"') < 0 && (b.Titel ?? "").IndexOf('"') < 0 && (b.Rat ?? "").IndexOf('"') < 0);
                h.Ist("Quelle und Messwert gesetzt: " + b.Schluessel, !string.IsNullOrEmpty(b.Quelle) && b.Messwert != null);
            }
        }

        static List<BereichErgebnis> Pruefen(Harness h, Systembild s, Entscheidungen e = null)
        {
            var erg = h.Pruefen(s, e);
            Alle.AddRange(Harness.Befunde(erg, Bereich.Datentraeger));
            Alle.AddRange(Harness.Befunde(erg, Bereich.Speicherplatz));
            return erg;
        }

        /// <summary>Genau dieser Schluessel - Harness.Einer vergleicht nur den Anfang ("datentraeger.ereignis" traefe auch ".disk").</summary>
        static Befund Genau(IEnumerable<BereichErgebnis> erg, string schluessel)
        {
            return Harness.Befunde(erg).FirstOrDefault(b => b.Schluessel == schluessel);
        }

        static IEnumerable<Befund> Probleme(IEnumerable<BereichErgebnis> erg, string bereich)
        {
            return Harness.Befunde(erg, bereich).Where(b => b.IstProblem);
        }

        // ---------------------------------------------------------------- gesund

        /// <summary>Gesundes Bild, erhoeht: kein Problem, ok-Befunde je Laufwerk, keine Fehlend-Zeile.</summary>
        static void Gesund(Harness h)
        {
            h.Gruppe("Datentraeger: gesundes Bild");
            var s = h.Bild("gepflanzt-datentraeger-gesund.json"); if (s == null) return;
            h.Ist("Grundmenge: 1 Datenträger, 4 Volumes, 2 Punkte", s.Datentraeger.Count == 1 && s.Volumes.Count == 4 && s.Systemschutz.Punkte != null && s.Systemschutz.Punkte.Count == 2);
            var erg = Pruefen(h, s);
            var d = Harness.Bereich(erg, Bereich.Datentraeger);
            h.Ist("DatenVorhanden", d.DatenVorhanden);
            h.Ist("Zustand des Bereichs ok", d.Zustand == Zustand.Ok, d.Zustand);
            h.Ist("kein Problem-Befund", !Probleme(erg, Bereich.Datentraeger).Any(), string.Join(", ", Probleme(erg, Bereich.Datentraeger).Select(b => b.Schluessel)));
            h.Ist("ok-Befund je Laufwerk", Harness.Einer(erg, "datentraeger.0.zustand") != null && Harness.Einer(erg, "datentraeger.0.zustand").Zustand == Zustand.Ok);
            h.Ist("ok-Befund Dateisystem", Harness.Einer(erg, "volume.dateisystem") != null);
            h.Ist("ok-Befund Ereignisse nennt den Zeitraum", Genau(erg, "datentraeger.ereignis") != null && Genau(erg, "datentraeger.ereignis").Satz.Contains("90 Tagen"));
            h.Ist("ok-Befund Wiederherstellungspunkte nennt das Alter", Harness.Einer(erg, "systemschutz.punkte") != null && Harness.Einer(erg, "systemschutz.punkte").Satz.Contains("3 Tage"));
            h.Ist("keine Fehlend-Zeile (erhoeht, alles da)", d.Fehlend.Count == 0, string.Join("; ", d.Fehlend));
            var sp = Harness.Bereich(erg, Bereich.Speicherplatz);
            h.Ist("Speicherplatz ok fuer C: und D:", sp.Zustand == Zustand.Ok && Harness.AlleMit(erg, "speicherplatz.").Count() == 2);
            h.Ist("Schattenkopien als Detail am Windows-Laufwerk", Harness.Einer(erg, "speicherplatz.C").Detail.Any(z => z.Contains("Schattenkopien") && z.Contains("16,5 GB")));
            h.Ist("Systempartitionen als Detail, nicht als Befund", Harness.Einer(erg, "speicherplatz.C").Detail.Any(z => z.Contains("EFI-Systempartition")) && Harness.Einer(erg, "speicherplatz.C").Detail.Any(z => z.Contains("Wiederherstellungspartition")));
        }

        // ---------------------------------------------------------------- (a) NVMe Spare

        /// <summary>Wirksamkeit 2026-09-12: Vergleich "Spare &lt; Schwelle" auf "&gt;" gedreht, Probe rot, zurueckgedreht.</summary>
        static void NvmeSpare(Harness h)
        {
            h.Gruppe("Datentraeger: (a) NVMe-Reserve unter der Herstellerschwelle");
            var s = h.Bild("gepflanzt-datentraeger-nvme-spare.json"); if (s == null) return;
            h.Ist("Grundmenge: Laufwerk mit NVMe-Log, Spare 5 < Schwelle 10", s.Datentraeger.Count == 1 && s.Datentraeger[0].Nvme != null && s.Datentraeger[0].Nvme.Spare < s.Datentraeger[0].Nvme.SpareSchwelle);
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "datentraeger.0.nvme.kritisch");
            h.Ist("genau ein Befund nvme.kritisch", Harness.AlleMit(erg, "datentraeger.0.nvme.kritisch").Count() == 1);
            h.Ist("Zustand bad", b != null && b.Zustand == Zustand.Bad);
            h.Ist("Satz nennt 5 % und 10 %", b != null && b.Satz.Contains("5 %") && b.Satz.Contains("10 %"), b == null ? null : b.Satz);
            h.Ist("Rat nennt Datensicherung", b != null && b.Rat != null && b.Rat.Contains("Sichern"));
            h.Ist("kein ok-Befund fuer dasselbe Laufwerk", Harness.Einer(erg, "datentraeger.0.zustand") == null);
            h.Ist("Bereich bad", Harness.Bereich(erg, Bereich.Datentraeger).Zustand == Zustand.Bad);

            // Gegenprobe: Spare auf der Schwelle -> kein Befund (die Doku sagt "faellt unter die Schwelle").
            s.Datentraeger[0].Nvme.Spare = 10;
            var erg2 = Pruefen(h, s);
            h.Ist("Gegenprobe: Spare == Schwelle -> kein nvme.kritisch", Harness.Einer(erg2, "datentraeger.0.nvme.kritisch") == null);
            h.Ist("Gegenprobe: stattdessen ok-Befund", Harness.Einer(erg2, "datentraeger.0.zustand") != null);

            // CriticalWarning allein (Bit 2 = Zuverlaessigkeit beeintraechtigt) -> ebenfalls bad.
            s.Datentraeger[0].Nvme.CriticalWarning = 4;
            var erg3 = Pruefen(h, s);
            var b3 = Harness.Einer(erg3, "datentraeger.0.nvme.kritisch");
            h.Ist("CriticalWarning 0x4 -> bad mit Klartext", b3 != null && b3.Zustand == Zustand.Bad && b3.Satz.Contains("Zuverlässigkeit"), b3 == null ? null : b3.Satz);
        }

        // ---------------------------------------------------------------- (b) Stunden null

        /// <summary>Wirksamkeit 2026-09-12: Bedingung "Nvme.Stunden &gt; 0" im Inventar entfernt, Probe "kein 0 Stunden" rot, wiederhergestellt.</summary>
        static void NvmeStundenNull(Harness h)
        {
            h.Gruppe("Datentraeger: (b) NVMe ohne Betriebsstunden, UsedPct 1");
            var s = h.Bild("gepflanzt-datentraeger-nvme-stunden-null.json"); if (s == null) return;
            var d0 = s.Datentraeger.Count == 1 ? s.Datentraeger[0] : null;
            h.Ist("Grundmenge: Zaehler.Stunden null, Nvme.Stunden 0, UsedPct 1", d0 != null && d0.Zaehler != null && !d0.Zaehler.Stunden.HasValue && d0.Nvme != null && d0.Nvme.Stunden == 0 && d0.Nvme.UsedPct == 1);
            var erg = Pruefen(h, s);
            h.Ist("kein Problem-Befund fuer das Laufwerk", !Probleme(erg, Bereich.Datentraeger).Any(b => b.Schluessel.StartsWith("datentraeger.0.")));
            var alleDetails = Harness.Befunde(erg, Bereich.Datentraeger).SelectMany(b => b.Detail).ToList();
            h.Ist("Grundmenge: Detailzeilen vorhanden", alleDetails.Count >= 3);
            h.Ist("kein \"0 Stunden\" und keine \"Betriebsstunden: 0\" im Detail", !alleDetails.Any(z => z.Contains("0 Stunden") || z.Contains("Betriebsstunden: 0")), string.Join(" | ", alleDetails));
            h.Ist("keine Betriebsstunden-Zeile, weil keine Quelle sie liefert", !alleDetails.Any(z => z.StartsWith("Betriebsstunden")));

            // Gegenprobe: liefert der Zaehler Stunden, steht die Zeile da.
            s.Datentraeger[0].Zaehler.Stunden = 1234;
            var erg2 = Pruefen(h, s);
            h.Ist("Gegenprobe: Zaehler.Stunden 1234 -> Zeile \"Betriebsstunden: 1234\"", Harness.Befunde(erg2, Bereich.Datentraeger).SelectMany(b => b.Detail).Any(z => z == "Betriebsstunden: 1234"));
        }

        // ---------------------------------------------------------------- (c) EFI dirty

        /// <summary>Wirksamkeit 2026-09-12: Filter "!IstNutzerVolume" entfernt, Probe "kein Befund fuer die EFI-Partition" rot, wiederhergestellt.</summary>
        static void EfiDirty(Harness h)
        {
            h.Gruppe("Datentraeger: (c) EFI-Partition versteckt, dirty und 0xD00F");
            var s = h.Bild("gepflanzt-datentraeger-efi-dirty.json"); if (s == null) return;
            var esp = s.Volumes.FirstOrDefault(v => v.Versteckt);
            h.Ist("Grundmenge: versteckte Partition mit Dirty und 0xD00F, C: sauber", esp != null && esp.Dirty == true && esp.OpStatus.Contains(0xD00F) && s.Volumes.Any(v => v.Buchstabe == "C" && v.Dirty == false));
            var erg = Pruefen(h, s);
            h.Ist("kein Problem-Befund im Bereich", !Probleme(erg, Bereich.Datentraeger).Any(), string.Join(", ", Probleme(erg, Bereich.Datentraeger).Select(b => b.Schluessel)));
            h.Ist("kein Befund volume.*.reparatur oder .scan", !Harness.AlleMit(erg, "volume.").Any(b => b.Schluessel.EndsWith(".reparatur") || b.Schluessel.EndsWith(".scan")));
            var ok = Harness.Einer(erg, "volume.dateisystem");
            h.Ist("Info-Detail zur EFI-Partition am ok-Befund", ok != null && ok.Detail.Any(z => z.Contains("EFI-Systempartition") && z.Contains("0xD00F") && z.Contains("wird nicht bewertet")), ok == null ? null : string.Join(" | ", ok.Detail));

            // Gegenprobe: dieselben Werte auf C: -> bad.
            var c = s.Volumes.First(v => v.Buchstabe == "C");
            c.Dirty = true; c.OpStatus = new List<int> { 0xD00F };
            var erg2 = Pruefen(h, s);
            var b2 = Harness.Einer(erg2, "volume.C.reparatur");
            h.Ist("Gegenprobe: 0xD00F auf C: -> bad mit Massnahme volume.reparatur", b2 != null && b2.Zustand == Zustand.Bad && b2.Massnahmen.Contains("volume.reparatur"));
        }

        // ---------------------------------------------------------------- (d) C: dirty

        /// <summary>Wirksamkeit 2026-09-12: "v.Dirty == true" auf "== false" gedreht, Probe rot, zurueckgedreht.</summary>
        static void CDirty(Harness h)
        {
            h.Gruppe("Datentraeger: (d) Laufwerk C: traegt das Dirty-Bit");
            var s = h.Bild("gepflanzt-datentraeger-c-dirty.json"); if (s == null) return;
            h.Ist("Grundmenge: C: dirty, D: nicht", s.Volumes.Any(v => v.Buchstabe == "C" && v.Dirty == true) && s.Volumes.Any(v => v.Buchstabe == "D" && v.Dirty == false));
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "volume.C.scan");
            h.Ist("genau ein Befund volume.C.scan", Harness.AlleMit(erg, "volume.C.").Count() == 1 && b != null);
            h.Ist("Zustand warn", b != null && b.Zustand == Zustand.Warn);
            h.Ist("Massnahme volume.scan", b != null && b.Massnahmen.Contains("volume.scan"));
            h.Ist("kein Befund fuer D:", !Harness.AlleMit(erg, "volume.D.").Any());
            h.Ist("ok-Befund Dateisystem nennt D:, nicht C:", Harness.Einer(erg, "volume.dateisystem") != null && Harness.Einer(erg, "volume.dateisystem").Satz.Contains("D:") && !Harness.Einer(erg, "volume.dateisystem").Satz.Contains("C:"));
            h.Ist("keine Fehlend-Zeile zum Dirty-Bit (es war lesbar)", !Harness.Bereich(erg, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Kennzeichen")));

            // Gegenprobe: Dirty-Bit auf C: geloescht -> kein Befund.
            s.Volumes.First(v => v.Buchstabe == "C").Dirty = false;
            var erg2 = Pruefen(h, s);
            h.Ist("Gegenprobe: C: nicht dirty -> kein volume.C.scan", Harness.Einer(erg2, "volume.C.scan") == null);
        }

        // ---------------------------------------------------------------- (e) Wear gegen UsedPct

        /// <summary>Wirksamkeit 2026-09-12: NvmeUsedWarn-Vergleich auf "&gt;" 95 gestellt (Schwelle vertauscht), Probe rot, zurueck.</summary>
        static void WearGegenUsed(Harness h)
        {
            h.Gruppe("Datentraeger: (e) Wear 0 und PercentageUsed 95: das NVMe-Log gewinnt");
            var s = h.Bild("gepflanzt-datentraeger-wear-vs-used.json"); if (s == null) return;
            var d0 = s.Datentraeger.Count == 1 ? s.Datentraeger[0] : null;
            h.Ist("Grundmenge: Wear 0, UsedPct 95", d0 != null && d0.Zaehler != null && d0.Zaehler.Wear == 0 && d0.Nvme != null && d0.Nvme.UsedPct == 95);
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "datentraeger.0.nvme.verbraucht");
            h.Ist("Befund nvme.verbraucht warn", b != null && b.Zustand == Zustand.Warn);
            h.Ist("Satz nennt 95 %", b != null && b.Satz.Contains("95 %"));
            h.Ist("Detail nennt den Windows-Zaehler Wear 0 als unterlegen", b != null && b.Detail.Any(z => z.Contains("Wear: 0 %")));
            h.Ist("kein Befund verschleiss (Wear-Regel gilt nur ohne NVMe-Log)", Harness.Einer(erg, "datentraeger.0.verschleiss") == null);

            // Gegenproben: UsedPct 100 -> bad; UsedPct 89 -> kein Befund; ohne NVMe-Log mit Wear 85 -> verschleiss warn.
            d0.Nvme.UsedPct = 100;
            h.Ist("UsedPct 100 -> bad", Harness.Einer(Pruefen(h, s), "datentraeger.0.nvme.verbraucht").Zustand == Zustand.Bad);
            d0.Nvme.UsedPct = 89;
            h.Ist("Gegenprobe: UsedPct 89 -> kein nvme.verbraucht", Harness.Einer(Pruefen(h, s), "datentraeger.0.nvme.verbraucht") == null);
            d0.Nvme = null; d0.Zaehler.Wear = 85;
            var b4 = Harness.Einer(Pruefen(h, s), "datentraeger.0.verschleiss");
            h.Ist("ohne NVMe-Log: Wear 85 -> verschleiss warn", b4 != null && b4.Zustand == Zustand.Warn && b4.Satz.Contains("85 %"));
            d0.Zaehler.Wear = 79;
            h.Ist("Gegenprobe: Wear 79 -> kein verschleiss", Harness.Einer(Pruefen(h, s), "datentraeger.0.verschleiss") == null);
        }

        // ---------------------------------------------------------------- (f) HealthStatus 2

        /// <summary>
        /// Wirksamkeit 2026-09-12: "d.Health == 2" aus der Bedingung gestrichen - zuerst blieb alles gruen (OperationalStatus 6 trug mit), nach der Probe "Health 2 allein" rot; wiederhergestellt.
        /// Wirksamkeit 2026-09-13: Messwert fest auf HealthStatus gestellt (ueberOpStatus ignoriert) -> Proben "Health 0 + 0xD007: Messwert ist der Ausfallcode" und "Health 5 + 0xD007" rot; zurueck.
        /// </summary>
        static void Health2(Harness h)
        {
            h.Gruppe("Datentraeger: (f) HealthStatus 2");
            var s = h.Bild("gepflanzt-datentraeger-health2.json"); if (s == null) return;
            h.Ist("Grundmenge: Laufwerk mit HealthStatus 2 und OperationalStatus 6", s.Datentraeger.Count == 1 && s.Datentraeger[0].Health == 2 && s.Datentraeger[0].OpStatus.Contains(6));
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "datentraeger.0.ausfall");
            h.Ist("genau ein Befund ausfall, bad", Harness.AlleMit(erg, "datentraeger.0.ausfall").Count() == 1 && b != null && b.Zustand == Zustand.Bad);
            h.Ist("Satz nennt Zustand 2", b != null && b.Satz.Contains("Zustand 2"));
            h.Ist("Rat: sofort sichern", b != null && b.Rat != null && b.Rat.Contains("sofort"));
            h.Ist("Detail nennt OperationalStatus 0x6", b != null && b.Detail.Any(z => z.Contains("0x6")));
            h.Ist("kein ok-Befund fuer das Laufwerk", Harness.Einer(erg, "datentraeger.0.zustand") == null);

            // Health 2 allein (OperationalStatus 2 = OK) muss reichen - die Wirksamkeitsprobe "Health == 2 gestrichen"
            // blieb am 2026-09-12 zuerst gruen, weil der OperationalStatus 6 den Ausfall mit trug.
            var d0 = s.Datentraeger[0];
            d0.OpStatus = new List<int> { 2 };
            var bH = Harness.Einer(Pruefen(h, s), "datentraeger.0.ausfall");
            h.Ist("Health 2 allein (OpStatus 2) -> bad ueber HealthStatus", bH != null && bH.Zustand == Zustand.Bad && bH.Quelle == "MSFT_PhysicalDisk.HealthStatus");

            // Gegenproben: Health 0 + OpStatus 2 -> kein ausfall; Health 0 + 0xD007 -> ausfall; Health 5 -> unknown, nicht bad.
            d0.Health = 0; d0.OpStatus = new List<int> { 2 };
            h.Ist("Gegenprobe: Health 0, OpStatus 2 -> kein ausfall", Harness.Einer(Pruefen(h, s), "datentraeger.0.ausfall") == null);
            d0.OpStatus = new List<int> { 2, 0xD007 };
            var b3 = Harness.Einer(Pruefen(h, s), "datentraeger.0.ausfall");
            h.Ist("OperationalStatus 0xD007 allein -> bad ueber OperationalStatus", b3 != null && b3.Zustand == Zustand.Bad && b3.Quelle.Contains("OperationalStatus"));
            h.Ist("Health 0 + 0xD007: Messwert ist der Ausfallcode, nicht HealthStatus 0", b3 != null && b3.Messwert.Wert == "0xD007" && b3.Messwert.Einheit == "OperationalStatus", b3 == null ? null : b3.Messwert.Wert + " " + b3.Messwert.Einheit);
            d0.Health = 5; d0.OpStatus = new List<int> { 0 };
            var erg4 = Pruefen(h, s);
            h.Ist("Health 5 (Unknown) -> unknown-Befund, kein bad", Harness.Einer(erg4, "datentraeger.0.ausfall") == null && Harness.Einer(erg4, "datentraeger.0.unbekannt") != null && Harness.Einer(erg4, "datentraeger.0.unbekannt").Zustand == Zustand.Unknown);

            // Health 5 (unbekannt) mit Ausfallcode: der Ausfallcode loest aus, also Quelle und Messwert
            // aus OperationalStatus - nicht "HealthStatus 5 (Schwelle 0 = gesund)" unter einem bad-Befund.
            d0.Health = 5; d0.OpStatus = new List<int> { 0xD007 };
            var b5 = Harness.Einer(Pruefen(h, s), "datentraeger.0.ausfall");
            h.Ist("Health 5 + 0xD007 -> bad, Satz nennt 0xD007", b5 != null && b5.Zustand == Zustand.Bad && b5.Satz.Contains("0xD007"), b5 == null ? null : b5.Satz);
            h.Ist("Health 5 + 0xD007: Quelle OperationalStatus, Messwert 0xD007 mit Schwelle 2 = OK", b5 != null && b5.Quelle == "MSFT_PhysicalDisk.OperationalStatus" && b5.Messwert.Wert == "0xD007" && b5.Messwert.Schwelle == "2 = OK", b5 == null ? null : b5.Quelle + " / " + b5.Messwert.Wert);
            // Gegenprobe: Health 2 + 0xD007 -> HealthStatus gewinnt (Satz "Zustand 2", Quelle HealthStatus, Messwert 2).
            d0.Health = 2;
            var b6 = Harness.Einer(Pruefen(h, s), "datentraeger.0.ausfall");
            h.Ist("Gegenprobe: Health 2 + 0xD007 -> Quelle HealthStatus, Messwert 2", b6 != null && b6.Quelle == "MSFT_PhysicalDisk.HealthStatus" && b6.Messwert.Wert == "2" && b6.Satz.Contains("Zustand 2"));
        }

        // ---------------------------------------------------------------- (g) Ntfs 55

        /// <summary>Wirksamkeit 2026-09-12: Anbieter im Von()-Aufruf auf "Microsoft-Windows-Ntfs" gesetzt (der Fehler aus der Recherche), Probe rot, zurueck auf "Ntfs".</summary>
        static void Ntfs55(Harness h)
        {
            h.Gruppe("Datentraeger: (g) Ntfs 55 im Protokoll");
            var s = h.Bild("gepflanzt-datentraeger-ntfs55.json"); if (s == null) return;
            h.Ist("Grundmenge: ein Ereignis Ntfs 55 vor 10 Tagen", s.Ereignisse.Von("Ntfs", 55).Count() == 1);
            var erg = Pruefen(h, s);
            var b = Genau(erg, "datentraeger.ereignis.ntfs");
            h.Ist("Befund ereignis.ntfs bad", b != null && b.Zustand == Zustand.Bad);
            h.Ist("Satz nennt einmal und 90 Tage", b != null && b.Satz.Contains("einmal") && b.Satz.Contains("90 Tagen"), b == null ? null : b.Satz);
            h.Ist("Rat beginnt mit Datensicherung", b != null && b.Rat != null && b.Rat.StartsWith("Sichern Sie zuerst"));
            h.Ist("Detail traegt DriveName aus den Feldern", b != null && b.Detail.Any(z => z.Contains("DriveName=C:")));
            h.Ist("kein ok-Befund ereignis", Genau(erg, "datentraeger.ereignis") == null);

            // Gegenproben: falscher Anbieter -> nichts; Ereignis aelter als 90 Tage -> nichts; Log-Beginn juenger -> Satz sagt "seit Beginn".
            s.Ereignisse.Eintraege[0].Anbieter = "Microsoft-Windows-Ntfs";
            h.Ist("Gegenprobe: Anbieter Microsoft-Windows-Ntfs 55 -> kein Befund (55 gehoert zu Ntfs)", Genau(Pruefen(h, s), "datentraeger.ereignis.ntfs") == null);
            s.Ereignisse.Eintraege[0].Anbieter = "Ntfs"; s.Ereignisse.Eintraege[0].ZeitUtc = "2026-05-01T00:00:00Z";
            h.Ist("Gegenprobe: 133 Tage alt -> kein Befund", Genau(Pruefen(h, s), "datentraeger.ereignis.ntfs") == null);
            s.Ereignisse.Eintraege.Clear(); s.Ereignisse.BeginnSystemUtc = "2026-08-20T00:00:00Z";
            var ok = Genau(Pruefen(h, s), "datentraeger.ereignis");
            h.Ist("Log 22 Tage alt -> ok-Satz sagt \"seit Beginn des Protokolls\", nicht \"90 Tagen\"", ok != null && ok.Satz.Contains("seit Beginn des Protokolls vor 22 Tagen") && !ok.Satz.Contains("90 Tagen"), ok == null ? null : ok.Satz);
            s.Ereignisse.Gesperrt.Add("System");
            var erg5 = Pruefen(h, s);
            h.Ist("Log gesperrt -> Fehlend, kein ok-Befund", Genau(erg5, "datentraeger.ereignis") == null && Harness.Bereich(erg5, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("gesperrt")));
        }

        // ---------------------------------------------------------------- (h) Platz

        /// <summary>Wirksamkeit 2026-09-12: PlatzBadPct- und PlatzWarnPct-Vergleiche vertauscht, Probe rot (C: warn statt bad), zurueck.</summary>
        static void Platz(Harness h)
        {
            h.Gruppe("Speicherplatz: (h) 5 GB von 500 -> bad, 15 % -> warn, 40 % -> ok");
            var s = h.Bild("gepflanzt-datentraeger-platz.json"); if (s == null) return;
            h.Ist("Grundmenge: drei Nutzer-Volumes C, D, E", s.Volumes.Count(v => v.IstNutzerVolume) == 3);
            var erg = Pruefen(h, s);
            var c = Harness.Einer(erg, "speicherplatz.C"); var d = Harness.Einer(erg, "speicherplatz.D"); var e = Harness.Einer(erg, "speicherplatz.E");
            h.Ist("C: bad mit Massnahme speicher.aufraeumen", c != null && c.Zustand == Zustand.Bad && c.Massnahmen.Contains("speicher.aufraeumen"));
            h.Ist("C: Satz nennt 5 GB und 500 GB", c != null && c.Satz.Contains("5 GB") && c.Satz.Contains("500 GB"), c == null ? null : c.Satz);
            h.Ist("D: warn mit Massnahme", d != null && d.Zustand == Zustand.Warn && d.Massnahmen.Contains("speicher.aufraeumen"));
            h.Ist("D: Satz nennt 15 %", d != null && d.Satz.Contains("15 %"), d == null ? null : d.Satz);
            h.Ist("E: ok ohne Massnahme, Satz sagt \"das reicht\"", e != null && e.Zustand == Zustand.Ok && e.Massnahmen.Count == 0 && e.Satz.Contains("das reicht"));
            h.Ist("Bereich bad, DatenVorhanden", Harness.Bereich(erg, Bereich.Speicherplatz).Zustand == Zustand.Bad && Harness.Bereich(erg, Bereich.Speicherplatz).DatenVorhanden);

            // Gegenproben: 12 GB von 500 GB (2,4 %) bleibt bad ueber die Prozentregel; 30 GB von 100 GB (30 %) ok;
            // 9 GB von 30 GB (30 %) bad ueber die GB-Regel.
            var vc = s.Volumes.First(v => v.Buchstabe == "C");
            vc.FreiBytes = 12L * 1073741824;
            h.Ist("12 GB von 500 GB (2 %) -> bad ueber Prozent", Harness.Einer(Pruefen(h, s), "speicherplatz.C").Zustand == Zustand.Bad);
            vc.GroesseBytes = 100L * 1073741824; vc.FreiBytes = 30L * 1073741824;
            h.Ist("Gegenprobe: 30 GB von 100 GB -> ok", Harness.Einer(Pruefen(h, s), "speicherplatz.C").Zustand == Zustand.Ok);
            vc.GroesseBytes = 30L * 1073741824; vc.FreiBytes = 9L * 1073741824;
            h.Ist("9 GB von 30 GB (30 %) -> bad ueber die GB-Regel", Harness.Einer(Pruefen(h, s), "speicherplatz.C").Zustand == Zustand.Bad);
            // Ohne Nutzer-Volume: DatenVorhanden false, unknown.
            foreach (var v in s.Volumes) v.Versteckt = true;
            var erg5 = Pruefen(h, s);
            h.Ist("nur versteckte Volumes -> DatenVorhanden false, Zustand unknown", !Harness.Bereich(erg5, Bereich.Speicherplatz).DatenVorhanden && Harness.Bereich(erg5, Bereich.Speicherplatz).Zustand == Zustand.Unknown);
        }

        // ---------------------------------------------------------------- (i) Dirty null

        /// <summary>Wirksamkeit 2026-09-12: "v.Dirty == true" auf "v.Dirty != false" gestellt (null wie dirty), Probe rot, zurueck.</summary>
        static void DirtyNull(Harness h)
        {
            h.Gruppe("Datentraeger: (i) Dirty-Bit ohne Rechte nicht lesbar");
            var s = h.Bild("gepflanzt-datentraeger-dirty-null.json"); if (s == null) return;
            h.Ist("Grundmenge: nicht erhoeht, C: Dirty null, Fehler wmi.volume.dirty zugriff", !s.Erhoeht && s.Volumes.Any(v => v.Buchstabe == "C" && !v.Dirty.HasValue) && s.ZugriffVerweigert("wmi.volume.dirty"));
            var erg = Pruefen(h, s);
            var d = Harness.Bereich(erg, Bereich.Datentraeger);
            h.Ist("kein Problem-Befund", !Probleme(erg, Bereich.Datentraeger).Any(), string.Join(", ", Probleme(erg, Bereich.Datentraeger).Select(b => b.Schluessel)));
            h.Ist("kein Befund volume.C.*", !Harness.AlleMit(erg, "volume.C.").Any());
            h.Ist("Fehlend nennt das Dateisystem-Kennzeichen mit Administratorrechten", d.Fehlend.Any(f => f.StartsWith("Dateisystem-Kennzeichen") && f.Contains("Administratorrechte")), string.Join("; ", d.Fehlend));
            h.Ist("Fehlend nennt Zaehler und Wiederherstellungspunkte", d.Fehlend.Any(f => f.Contains("Fehlerzähler")) && d.Fehlend.Any(f => f.Contains("Wiederherstellungspunkte")));
            h.Ist("kein Befund systemschutz.punkte (null ist nicht leer)", Harness.Einer(erg, "systemschutz.punkte") == null);
            h.Ist("ok-Satz Dateisystem erwaehnt das fehlende Kennzeichen", Harness.Einer(erg, "volume.dateisystem") != null && Harness.Einer(erg, "volume.dateisystem").Satz.Contains("ohne Administratorrechte"));
            h.Ist("Bereich trotzdem ok (Daten vorhanden, kein Problem)", d.DatenVorhanden && d.Zustand == Zustand.Ok, d.Zustand);
            h.Ist("Speicherplatz: Schattenkopien als \"nicht lesbar\" im Detail", Harness.Einer(erg, "speicherplatz.C").Detail.Any(z => z.Contains("Schattenkopien: nicht lesbar")));

            // Gegenprobe: ohne Fehlereintrag waere es "nicht verfuegbar", nicht "Administratorrechte".
            s.Fehlerliste.RemoveAll(f => f.Quelle == "wmi.volume.dirty");
            var erg2 = Pruefen(h, s);
            h.Ist("Gegenprobe: ohne zugriff-Eintrag lautet Fehlend \"keine Daten\"", Harness.Bereich(erg2, Bereich.Datentraeger).Fehlend.Any(f => f.StartsWith("Dateisystem-Kennzeichen") && f.Contains("keine Daten")));
        }

        // ---------------------------------------------------------------- Ereignisse 129 / disk / 150

        /// <summary>
        /// Wirksamkeit 2026-09-12: Hyper-V-Ausschluss entfernt -> Probe "Hyper-V-129 auch mit Level 3 nicht gezaehlt" rot; Level-3-Filter entfernt -> zuerst gruen (Hyper-V-Ausschluss ueberlagerte), nach der Probe "storahci-129 auf Level 4" rot; beides zurueck.
        /// Wirksamkeit 2026-09-13: Kennzeichen-Pruefung "IoLogMsg.dll" durch "true" ersetzt -> Probe "drei Time-Service-129 nicht gezaehlt" rot; Positivliste geleert -> Probe "storahci ohne Kennzeichen" rot; beides zurueck.
        /// </summary>
        static void Ereignisse(Harness h)
        {
            h.Gruppe("Datentraeger: Ereignisse disk 7, Ntfs 150, 129 (storahci gegen Hyper-V und Zeitdienst)");
            var s = h.Bild("gepflanzt-datentraeger-ereignisse.json"); if (s == null) return;
            h.Ist("Grundmenge: 1x disk 7, 1x Ntfs 150, 3x storahci 129 (Level 3, IoLogMsg.dll), 5x Hyper-V 129 (Level 4), 3x Time-Service 129 (Level 3, w32time.dll)",
                s.Ereignisse.Von("disk", 7).Count() == 1 && s.Ereignisse.Von("Microsoft-Windows-Ntfs", 150).Count() == 1
                && s.Ereignisse.Von("storahci", 129).Count() == 3 && s.Ereignisse.Von("storahci", 129).All(x => (x.Feld(Regel.MeldungsdateiFeld) ?? "").Contains("IoLogMsg.dll"))
                && s.Ereignisse.Von("Microsoft-Windows-Hyper-V-Hypervisor", 129).Count() == 5
                && s.Ereignisse.Von("Microsoft-Windows-Time-Service", 129).Count() == 3 && s.Ereignisse.Von("Microsoft-Windows-Time-Service", 129).All(x => x.Level == 3 && (x.Feld(Regel.MeldungsdateiFeld) ?? "").Contains("w32time.dll")));
            var erg = Pruefen(h, s);
            var disk = Harness.Einer(erg, "datentraeger.ereignis.disk");
            h.Ist("disk 7 einmal in 30 Tagen -> warn", disk != null && disk.Zustand == Zustand.Warn && disk.Satz.Contains("einmal"));
            h.Ist("disk-Detail nennt \"fehlerhafter Block\"", disk != null && disk.Detail.Any(z => z.Contains("fehlerhafter Block")));
            var n150 = Harness.Einer(erg, "datentraeger.ereignis.ntfs150");
            h.Ist("Ntfs 150 -> warn", n150 != null && n150.Zustand == Zustand.Warn);
            var reset = Harness.Einer(erg, "datentraeger.ereignis.reset");
            h.Ist("129: genau 3 gezaehlt (Hyper-V und Zeitdienst nicht), warn", reset != null && reset.Zustand == Zustand.Warn && reset.Messwert.Wert == "3", reset == null ? null : reset.Messwert.Wert);
            h.Ist("129-Quelle nennt storahci, nicht Hyper-V, nicht Time-Service", reset != null && reset.Quelle.Contains("storahci") && !reset.Quelle.Contains("Hyper-V") && !reset.Quelle.Contains("Time-Service"));
            h.Ist("kein ok-Befund ereignis", Genau(erg, "datentraeger.ereignis") == null);
            h.Ist("kein ntfs-bad (55/131 fehlen)", Genau(erg, "datentraeger.ereignis.ntfs") == null);

            // Der Anbieter-Ausschluss gilt unabhaengig vom Level: Hyper-V-129 auf Level 3 gesetzt, zaehlt trotzdem nicht.
            foreach (var x in s.Ereignisse.Von("Microsoft-Windows-Hyper-V-Hypervisor", 129)) x.Level = 3;
            var reset2 = Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset");
            h.Ist("Hyper-V-129 auch mit Level 3 nicht gezaehlt (bleibt 3)", reset2 != null && reset2.Messwert.Wert == "3", reset2 == null ? null : reset2.Messwert.Wert);

            // Nur Speicher-Miniports zaehlen (Konzept 4.2: Anbieter gegen IoLogMsg.dll pruefen). Der Zeitdienst
            // schreibt 129 mit Level 3 ("kein Domaenenpeer als Zeitquelle", auf diesem Rechner gemessen) - ohne
            // die Pruefung bekaeme ein Domaenen-PC ausserhalb des Firmennetzes einen Laufwerks-Reset gemeldet.
            s.Ereignisse.Eintraege.RemoveAll(x => string.Equals(x.Anbieter, "storahci", System.StringComparison.OrdinalIgnoreCase));
            h.Ist("drei Time-Service-129 (Level 3, w32time.dll) allein -> kein reset-Befund", Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset") == null);
            // Kennzeichen entscheidet, nicht der Name: ein Fremd-Miniport (secnvme, nicht in der Positivliste)
            // mit "IoLogMsg.dll;secnvme.sys" zaehlt, iaStorAVC mit "IoLogMsg.dll;iaStorAVC.sys" ebenso.
            var ts = s.Ereignisse.Von("Microsoft-Windows-Time-Service", 129).ToList();
            ts[0].Anbieter = "secnvme"; ts[0].Felder[Regel.MeldungsdateiFeld] = @"%SystemRoot%\System32\IoLogMsg.dll;%SystemRoot%\System32\drivers\secnvme.sys";
            ts[1].Anbieter = "iaStorAVC"; ts[1].Felder[Regel.MeldungsdateiFeld] = @"%SystemRoot%\System32\IoLogMsg.dll;%SystemRoot%\System32\drivers\iaStorAVC.sys";
            ts[2].Anbieter = "stornvme"; ts[2].Felder[Regel.MeldungsdateiFeld] = @"%SystemRoot%\System32\IoLogMsg.dll";
            var reset3 = Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset");
            h.Ist("secnvme + iaStorAVC + stornvme mit IoLogMsg.dll-Kennzeichen -> 3 gezaehlt, reset warn", reset3 != null && reset3.Messwert.Wert == "3" && reset3.Quelle.Contains("secnvme") && reset3.Quelle.Contains("iaStorAVC"), reset3 == null ? null : reset3.Quelle);
            // Rueckfall ohne Kennzeichen (Sammler ohne Metadaten): Positivliste kennt storahci und die iaStor-Familie, den Zeitdienst nicht.
            foreach (var x in ts) x.Felder.Remove(Regel.MeldungsdateiFeld);
            ts[0].Anbieter = "storahci"; ts[1].Anbieter = "iaStorAC"; ts[2].Anbieter = "Microsoft-Windows-Time-Service";
            var reset4 = Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset");
            h.Ist("ohne Kennzeichen: storahci + iaStorAC zaehlen (Positivliste), Time-Service nicht -> 2, kein reset-Befund", reset4 == null);
            ts[2].Anbieter = "stornvme";
            var reset5 = Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset");
            h.Ist("ohne Kennzeichen: storahci + iaStorAC + stornvme -> 3, reset warn", reset5 != null && reset5.Messwert.Wert == "3", reset5 == null ? null : reset5.Messwert.Wert);
            // Kennzeichen gewinnt gegen den Namen: stornvme mit Kennzeichen w32time.dll zaehlt nicht mehr.
            ts[2].Felder[Regel.MeldungsdateiFeld] = @"%SystemRoot%\system32\w32time.dll";
            h.Ist("Kennzeichen w32time.dll gewinnt gegen den Miniport-Namen -> 2, kein reset-Befund", Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset") == null);

            // Zurueck zum Ausgangsbild fuer die Level- und Zeitproben.
            s = h.Bild("gepflanzt-datentraeger-ereignisse.json"); if (s == null) return;

            // Der Level-Filter gilt unabhaengig vom Anbieter: ein storahci-129 auf Level 4 (informativ) zaehlt nicht mehr.
            // (Wirksamkeitsprobe "Level-3-Filter entfernt" blieb am 2026-09-12 zuerst gruen, weil der Hyper-V-Ausschluss
            // dieselben Ereignisse traf.)
            foreach (var x in s.Ereignisse.Von("Microsoft-Windows-Hyper-V-Hypervisor", 129)) x.Level = 4;
            s.Ereignisse.Von("storahci", 129).First().Level = 4;
            h.Ist("ein storahci-129 auf Level 4 -> nur 2 gezaehlt, kein reset-Befund", Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset") == null);
            s.Ereignisse.Von("storahci", 129).First().Level = 3;

            // Gegenproben: nur zwei storahci -> kein reset; disk 7 aelter als 30 Tage -> kein disk.
            s.Ereignisse.Eintraege.Remove(s.Ereignisse.Von("storahci", 129).First());
            h.Ist("Gegenprobe: 2x 129 -> kein reset-Befund", Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.reset") == null);
            // Wechseldatentraeger: dasselbe disk 7 fuer einen USB-Datentraeger (BusType 7) ist kein
            // Laufwerksbefund, sondern ein ok-Hinweis (gemessen 12.09.2026: 18 x disk 51 fuer einen Stick).
            s.Datentraeger[0].BusTyp = 7;
            var ergUsb = Pruefen(h, s);
            h.Ist("USB-Datenträger: kein warn-Befund datentraeger.ereignis.disk", Harness.Einer(ergUsb, "datentraeger.ereignis.disk") == null || Harness.Einer(ergUsb, "datentraeger.ereignis.disk").Schluessel == "datentraeger.ereignis.disk.usb");
            var usbHinweis = Harness.Einer(ergUsb, "datentraeger.ereignis.disk.usb");
            h.Ist("USB-Datenträger: ok-Hinweis mit Zahl und 'Wechseldatenträger'", usbHinweis != null && usbHinweis.Zustand == Zustand.Ok && usbHinweis.Satz.Contains("1-mal") || (usbHinweis != null && usbHinweis.Satz.Contains("einmal")), usbHinweis == null ? null : usbHinweis.Satz);
            s.Datentraeger[0].BusTyp = 17;
            s.Ereignisse.Von("disk", 7).First().ZeitUtc = "2026-07-01T00:00:00Z";
            h.Ist("Gegenprobe: disk 7 vor 72 Tagen -> kein disk-Befund", Harness.Einer(Pruefen(h, s), "datentraeger.ereignis.disk") == null);
        }

        // ---------------------------------------------------------------- Systemschutz

        /// <summary>Wirksamkeit 2026-09-12: Zustand des Befunds "kein Punkt" auf ok gestellt, Probe rot, zurueck; PolicyAus-Vergleich auf false gedreht, Probe rot, zurueck. ("Count == 0" auf "&lt; 0" laesst die Regel abstuerzen - das faengt das Geruest als Ausnahme, nicht als rote Zeile.)</summary>
        static void SystemschutzLeer(Harness h)
        {
            h.Gruppe("Datentraeger: Systemschutz ohne Wiederherstellungspunkt, per Richtlinie aus");
            var s = h.Bild("gepflanzt-datentraeger-systemschutz-leer.json"); if (s == null) return;
            h.Ist("Grundmenge: erhoeht, Punkte leer (nicht null), Frequenz 1440, Systemschutz aktiv", s.Erhoeht && s.Systemschutz.Punkte != null && s.Systemschutz.Punkte.Count == 0 && s.Systemschutz.Frequenz == 1440 && s.Systemschutz.Aktiv == true);
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "systemschutz.punkte");
            h.Ist("Befund systemschutz.punkte warn mit Massnahme", b != null && b.Zustand == Zustand.Warn && b.Massnahmen.Contains("systemschutz.punkt.anlegen"));
            h.Ist("Satz sagt \"keinen Wiederherstellungspunkt\"", b != null && b.Satz.Contains("keinen Wiederherstellungspunkt"));
            h.Ist("Detail nennt die Drossel 1440 Minuten, nicht als Voreinstellung", b != null && b.Detail.Any(z => z.Contains("1440 Minuten") && !z.Contains("Voreinstellung") && z.Contains("übersprungen")));
            h.Ist("keine Fehlend-Zeile zu Punkten", !Harness.Bereich(erg, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Wiederherstellungspunkte")));
            h.Ist("kein Befund systemschutz.aus (Schutz ist an)", Harness.Einer(erg, "systemschutz.aus") == null);

            // Drossel: null -> Voreinstellung, 60 -> "60 Minuten", 0 -> keine Drossel (kein "Minuten", kein "uebersprungen").
            // Wirksamkeit 2026-09-13: Zweig "Frequenz == 0" entfernt -> Probe "0 -> Keine Drossel" rot; zurueck.
            s.Systemschutz.Frequenz = null;
            string d1 = DrosselZeile(Pruefen(h, s));
            h.Ist("Frequenz null -> \"Voreinstellung (1440 Minuten)\"", d1.Contains("Voreinstellung (1440 Minuten)") && d1.Contains("übersprungen"), d1);
            s.Systemschutz.Frequenz = 60;
            string d2 = DrosselZeile(Pruefen(h, s));
            h.Ist("Frequenz 60 -> \"60 Minuten\", nicht 1440", d2.Contains("60 Minuten") && !d2.Contains("1440") && d2.Contains("übersprungen"), d2);
            s.Systemschutz.Frequenz = 0;
            string d3 = DrosselZeile(Pruefen(h, s));
            h.Ist("Frequenz 0 -> \"Keine Drossel\", kein \"Minuten\", kein \"übersprungen\"", d3.StartsWith("Keine Drossel") && !d3.Contains("Minuten") && !d3.Contains("übersprungen") && d3.Contains("= 0"), d3);
            s.Systemschutz.Frequenz = 1440;

            s.Systemschutz.PolicyAus = true;
            var erg2 = Pruefen(h, s);
            var p = Harness.Einer(erg2, "systemschutz.policy");
            h.Ist("PolicyAus -> warn systemschutz.policy, kein punkte-Befund", p != null && p.Zustand == Zustand.Warn && Harness.Einer(erg2, "systemschutz.punkte") == null);

            // Gegenprobe: Punkte null (nicht erhoeht) -> kein Befund, Fehlend.
            s.Systemschutz.PolicyAus = false; s.Systemschutz.Punkte = null;
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.systemrestore.punkte", Art = Fehler.Zugriff, Text = "braucht Administratorrechte" });
            var erg3 = Pruefen(h, s);
            h.Ist("Gegenprobe: Punkte null -> kein Befund, Fehlend mit Administratorrechten", Harness.Einer(erg3, "systemschutz.punkte") == null && Harness.Bereich(erg3, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Wiederherstellungspunkte") && f.Contains("Administratorrechte")));
        }

        /// <summary>Die Drossel-Zeile des Befunds systemschutz.punkte; ohne Befund ein leerer Text (rot, keine Ausnahme).</summary>
        static string DrosselZeile(IEnumerable<BereichErgebnis> erg)
        {
            var b = Genau(erg, "systemschutz.punkte");
            return b == null ? "" : (b.Detail.FirstOrDefault(z => z.Contains("Drossel")) ?? "");
        }

        // ---------------------------------------------------------------- Systemschutz aus

        /// <summary>
        /// Wirksamkeit 2026-09-13: Vergleich "z.Aktiv == false" auf "== true" gedreht -> Probe "systemschutz.aus warn ohne Massnahme" rot
        /// (stattdessen kam "kein Punkt" mit Massnahme punkt.anlegen, die ohne Schutz scheitert); zurueck.
        /// </summary>
        static void SystemschutzAus(Harness h)
        {
            h.Gruppe("Datentraeger: Systemschutz ausgeschaltet (RPSessionInterval 0, Werkseinstellung)");
            var s = h.Bild("gepflanzt-datentraeger-systemschutz-aus.json"); if (s == null) return;
            h.Ist("Grundmenge: erhoeht, aktiv false, RPSessionInterval 0, Punkte leer, keine Richtlinie", s.Erhoeht && s.Systemschutz.Aktiv == false && s.Systemschutz.SessionInterval == 0 && s.Systemschutz.Punkte != null && s.Systemschutz.Punkte.Count == 0 && s.Systemschutz.PolicyAus == false);
            var erg = Pruefen(h, s);
            var b = Harness.Einer(erg, "systemschutz.aus");
            h.Ist("genau ein Befund systemschutz.aus, warn", b != null && b.Zustand == Zustand.Warn && Harness.AlleMit(erg, "systemschutz.").Count() == 1);
            h.Ist("Satz sagt \"ausgeschaltet\" und \"keine Wiederherstellungspunkte\"", b != null && b.Satz.Contains("ausgeschaltet") && b.Satz.Contains("keine Wiederherstellungspunkte"), b == null ? null : b.Satz);
            h.Ist("keine Massnahme (Punkt anlegen scheitert ohne Schutz)", b != null && b.Massnahmen.Count == 0, b == null ? null : string.Join(",", b.Massnahmen));
            h.Ist("Rat nennt den Weg zum Einschalten (Computerschutz)", b != null && b.Rat != null && b.Rat.Contains("Computerschutz") && b.Rat.Contains("ein"));
            h.Ist("kein Befund systemschutz.punkte (\"kein Punkt\" waere die falsche Ursache)", Harness.Einer(erg, "systemschutz.punkte") == null);
            h.Ist("Detail nennt RPSessionInterval 0 als \"Systemschutz aus\"", b != null && b.Detail.Any(z => z.Contains("RPSessionInterval: 0") && z.Contains("aus")));
            h.Ist("keine Fehlend-Zeile zu Punkten", !Harness.Bereich(erg, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Wiederherstellungspunkte")));

            // Nicht erhoeht (Punkte null) mit aktiv false: der Befund kommt trotzdem, ohne Fehlend-Zeile zu Punkten -
            // die Ursache ist ohne Rechte lesbar, und ohne Schutz gibt es keine Liste, die fehlen koennte.
            s.Systemschutz.Punkte = null;
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.systemrestore.punkte", Art = Fehler.Zugriff, Text = "braucht Administratorrechte" });
            var erg2 = Pruefen(h, s);
            h.Ist("Punkte null + aktiv false -> systemschutz.aus, keine Fehlend-Zeile zu Punkten", Harness.Einer(erg2, "systemschutz.aus") != null && !Harness.Bereich(erg2, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Wiederherstellungspunkte")));

            // Gegenproben: aktiv true mit leerer Liste -> "kein Punkt" mit Massnahme; aktiv null (nicht ermittelbar) -> ebenso, kein "aus".
            s.Fehlerliste.Clear(); s.Systemschutz.Punkte = new List<Punkt>();
            s.Systemschutz.Aktiv = true; s.Systemschutz.SessionInterval = 1;
            var erg3 = Pruefen(h, s);
            h.Ist("Gegenprobe: aktiv true, Punkte leer -> systemschutz.punkte mit Massnahme, kein systemschutz.aus", Harness.Einer(erg3, "systemschutz.aus") == null && Harness.Einer(erg3, "systemschutz.punkte") != null && Harness.Einer(erg3, "systemschutz.punkte").Massnahmen.Contains("systemschutz.punkt.anlegen"));
            s.Systemschutz.Aktiv = null; s.Systemschutz.SessionInterval = null;
            var erg4 = Pruefen(h, s);
            h.Ist("Gegenprobe: aktiv null -> kein systemschutz.aus, \"kein Punkt\" bleibt", Harness.Einer(erg4, "systemschutz.aus") == null && Harness.Einer(erg4, "systemschutz.punkte") != null);
            // Richtlinie gewinnt: PolicyAus und aktiv false -> nur systemschutz.policy.
            s.Systemschutz.Aktiv = false; s.Systemschutz.PolicyAus = true;
            var erg5 = Pruefen(h, s);
            h.Ist("PolicyAus + aktiv false -> nur systemschutz.policy", Harness.Einer(erg5, "systemschutz.policy") != null && Harness.Einer(erg5, "systemschutz.aus") == null);
        }

        // ---------------------------------------------------------------- Schattenspeicher

        /// <summary>
        /// Wirksamkeit 2026-09-13: in SchattenGrenze den UNBOUNDED-Zweig entfernt -> Probe "ohne Obergrenze" rot; zurueck.
        /// (Der Sammler-Teil - MaxSpace 2^64-1 wird nicht zu 0 addiert - ist nur live pruefbar; hier steht das Bild so, wie der Sammler es schreibt.)
        /// </summary>
        static void Schattenspeicher(Harness h)
        {
            h.Gruppe("Datentraeger/Speicherplatz: Schattenspeicher mit und ohne Obergrenze");
            var s = h.Bild("gepflanzt-datentraeger-gesund.json"); if (s == null) return;
            var z = s.Systemschutz;
            h.Ist("Grundmenge: belegt 16,5 GB, Obergrenze 18,6 GB, nicht unbegrenzt", z.SchattenBelegt == 17671159808 && z.SchattenMax == 19982712832 && z.SchattenUnbegrenzt != true);
            var erg = Pruefen(h, s);
            h.Ist("mit Obergrenze: \"von höchstens 18,6 GB\" am Systemschutz-Befund und am Speicherplatz", Harness.Einer(erg, "systemschutz.punkte").Detail.Any(d => d.Contains("16,5 GB von höchstens 18,6 GB")) && Harness.Einer(erg, "speicherplatz.C").Detail.Any(d => d.Contains("16,5 GB von höchstens 18,6 GB")));

            // UNBOUNDED (vssadmin /maxsize=UNBOUNDED): der Sammler schreibt SchattenMax null und SchattenUnbegrenzt true.
            z.SchattenMax = null; z.SchattenUnbegrenzt = true;
            var erg2 = Pruefen(h, s);
            var zeilen = Harness.Einer(erg2, "systemschutz.punkte").Detail.Concat(Harness.Einer(erg2, "speicherplatz.C").Detail).Where(d => d.Contains("Schattenkopien")).ToList();
            h.Ist("Grundmenge: zwei Schattenkopien-Zeilen", zeilen.Count == 2, string.Join(" | ", zeilen));
            h.Ist("unbegrenzt: beide Zeilen sagen \"ohne Obergrenze\", keine \"höchstens\", keine \"0,0 GB\"", zeilen.All(d => d.Contains("ohne Obergrenze") && !d.Contains("höchstens") && !d.Contains("0,0 GB")), string.Join(" | ", zeilen));
            // Obergrenze nicht lesbar (null, nicht unbegrenzt): nur der belegte Wert, kein Nachsatz.
            z.SchattenUnbegrenzt = null;
            var erg3 = Pruefen(h, s);
            var zeilen3 = Harness.Einer(erg3, "systemschutz.punkte").Detail.Concat(Harness.Einer(erg3, "speicherplatz.C").Detail).Where(d => d.Contains("Schattenkopien")).ToList();
            h.Ist("Obergrenze unbekannt: weder \"höchstens\" noch \"ohne Obergrenze\"", zeilen3.Count == 2 && zeilen3.All(d => !d.Contains("höchstens") && !d.Contains("ohne Obergrenze") && d.Contains("16,5 GB")), string.Join(" | ", zeilen3));
        }

        // ---------------------------------------------------------------- Volume-Status

        /// <summary>Wirksamkeit 2026-09-12: Konstanten 0xD00E und 0xD00F vertauscht, Probe rot (C: spotfix statt reparatur), zurueck.</summary>
        static void VolumeStatus(Harness h)
        {
            h.Gruppe("Datentraeger: MSFT_Volume.OperationalStatus 0xD00F / 0xD00E / 0xD00D");
            var s = h.Bild("gepflanzt-datentraeger-volume-status.json"); if (s == null) return;
            h.Ist("Grundmenge: C 0xD00F, D 0xD00E, E 0xD00D", s.Volumes.Any(v => v.Buchstabe == "C" && v.OpStatus.Contains(0xD00F)) && s.Volumes.Any(v => v.Buchstabe == "D" && v.OpStatus.Contains(0xD00E)) && s.Volumes.Any(v => v.Buchstabe == "E" && v.OpStatus.Contains(0xD00D)));
            var erg = Pruefen(h, s);
            var c = Harness.Einer(erg, "volume.C.reparatur"); var d = Harness.Einer(erg, "volume.D.spotfix"); var e = Harness.Einer(erg, "volume.E.scan");
            h.Ist("C: bad reparatur, Massnahme volume.reparatur", c != null && c.Zustand == Zustand.Bad && c.Massnahmen.Contains("volume.reparatur"));
            h.Ist("C: Rat nennt Datensicherung und Neustart", c != null && c.Rat.Contains("Sichern") && c.Rat.Contains("Neustart"));
            h.Ist("D: warn spotfix, Massnahme volume.spotfix", d != null && d.Zustand == Zustand.Warn && d.Massnahmen.Contains("volume.spotfix"));
            h.Ist("E: warn scan, Massnahme volume.scan", e != null && e.Zustand == Zustand.Warn && e.Massnahmen.Contains("volume.scan"));
            h.Ist("je Volume genau ein Befund", Harness.AlleMit(erg, "volume.C.").Count() == 1 && Harness.AlleMit(erg, "volume.D.").Count() == 1 && Harness.AlleMit(erg, "volume.E.").Count() == 1);
            h.Ist("kein ok-Befund Dateisystem (kein gesundes Volume)", Harness.Einer(erg, "volume.dateisystem") == null);
            h.Ist("Bereich bad", Harness.Bereich(erg, Bereich.Datentraeger).Zustand == Zustand.Bad);

            // Gegenprobe: HealthStatus allein (ohne OperationalStatus) loest nichts aus - die Aktion steht in OperationalStatus.
            foreach (var v in s.Volumes) v.OpStatus = new List<int> { 2 };
            var erg2 = Pruefen(h, s);
            h.Ist("Gegenprobe: HealthStatus 1/2 ohne 0xD00x -> kein Volume-Befund", !Harness.AlleMit(erg2, "volume.C.").Any() && !Harness.AlleMit(erg2, "volume.D.").Any() && !Harness.AlleMit(erg2, "volume.E.").Any());
        }

        // ---------------------------------------------------------------- TRIM und Temperatur

        /// <summary>Wirksamkeit 2026-09-12: "d.Trim == false" auf "== true" gedreht, Probe rot; Temperaturvergleich auf "&lt;" gedreht, Probe rot; beides zurueck.</summary>
        static void TrimUndTemperatur(Harness h)
        {
            h.Gruppe("Datentraeger: TRIM aus auf SSD, Temperatur gegen Geraeteschwelle");
            var s = h.Bild("gepflanzt-datentraeger-gesund.json"); if (s == null) return;
            var d0 = s.Datentraeger[0];
            h.Ist("Grundmenge: SSD mit TRIM in Windows an (Registry), Geraet nicht abgefragt, 39 °C, Warngrenze 90", d0.IstSsd && d0.Trim == true && !d0.TrimGeraet.HasValue && d0.Nvme.TempC == 39 && d0.TempWarn == 90);

            // Trim traegt nur die Registry (DisableDeleteNotification): false = 1 = sicher aus, Massnahme setzt 0.
            d0.Trim = false;
            var b = Genau(Pruefen(h, s), "datentraeger.0.trim");
            h.Ist("Registry-TRIM false auf SSD -> warn mit Massnahme datentraeger.trim.einschalten", b != null && b.Zustand == Zustand.Warn && b.Massnahmen.Contains("datentraeger.trim.einschalten"));
            h.Ist("Registry-Befund: Satz nennt DisableDeleteNotification = 1, Quelle die Registry", b != null && b.Satz.Contains("DisableDeleteNotification = 1") && b.Quelle.Contains("DisableDeleteNotification"), b == null ? null : b.Satz);
            d0.Trim = null;
            var ergNull = Pruefen(h, s);
            h.Ist("Gegenprobe: TRIM null (Registry nicht lesbar) -> kein Befund, aber Fehlend-Zeile", Harness.Einer(ergNull, "datentraeger.0.trim") == null && Harness.Bereich(ergNull, Bereich.Datentraeger).Fehlend.Any(f => f.StartsWith("TRIM-Einstellung")), string.Join("; ", Harness.Bereich(ergNull, Bereich.Datentraeger).Fehlend));
            d0.Trim = false; d0.MedienTyp = 3; d0.BusTyp = 11;
            h.Ist("Gegenprobe: TRIM false auf Festplatte (HDD, SATA) -> kein Befund", Harness.Einer(Pruefen(h, s), "datentraeger.0.trim") == null);
            d0.Trim = true; d0.MedienTyp = 4; d0.BusTyp = 17;

            d0.Nvme.TempC = 91;
            var t = Harness.Einer(Pruefen(h, s), "datentraeger.0.temperatur");
            h.Ist("91 °C bei Warngrenze 90 -> warn, Satz nennt beide Zahlen", t != null && t.Zustand == Zustand.Warn && t.Satz.Contains("91 °C") && t.Satz.Contains("90 °C"), t == null ? null : t.Satz);
            d0.Nvme.TempC = 89;
            h.Ist("Gegenprobe: 89 °C -> kein Befund", Harness.Einer(Pruefen(h, s), "datentraeger.0.temperatur") == null);
            d0.Nvme.TempC = 91; d0.TempWarn = null; d0.TempKritisch = null;
            var erg4 = Pruefen(h, s);
            h.Ist("Gegenprobe: 91 °C ohne Geraeteschwelle -> kein Befund, aber Fehlend", Harness.Einer(erg4, "datentraeger.0.temperatur") == null && Harness.Bereich(erg4, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Temperaturgrenze")));
        }

        // ---------------------------------------------------------------- TRIM: Geraet meldet kein TRIM

        /// <summary>
        /// Wirksamkeit 2026-09-13: Zweig "d.TrimGeraet == false" entfernt -> Probe "warn ohne Massnahme" rot; Zweig auf die Registry-Massnahme
        /// umgebogen -> Probe "keine Massnahme, kein Registry-Rat" rot; beides zurueck.
        /// </summary>
        static void TrimGeraet(Harness h)
        {
            h.Gruppe("Datentraeger: SSD an USB-Bruecke, Registry 0, Geraet meldet kein TRIM");
            var s = h.Bild("gepflanzt-datentraeger-trim-geraet.json"); if (s == null) return;
            var d0 = s.Datentraeger.Count == 1 ? s.Datentraeger[0] : null;
            h.Ist("Grundmenge: SSD (MediaType 4) an USB (BusType 7), trim true, trimGeraet false", d0 != null && d0.IstSsd && d0.BusTyp == 7 && d0.Trim == true && d0.TrimGeraet == false);
            var erg = Pruefen(h, s);
            var b = Genau(erg, "datentraeger.0.trim.geraet");
            h.Ist("genau ein Befund datentraeger.0.trim.geraet, warn", b != null && b.Zustand == Zustand.Warn && Harness.AlleMit(erg, "datentraeger.0.trim").Count() == 1);
            h.Ist("kein Registry-Befund datentraeger.0.trim (Registry steht schon auf 0)", Genau(erg, "datentraeger.0.trim") == null);
            h.Ist("keine Massnahme, kein Registry-Rat", b != null && b.Massnahmen.Count == 0 && (b.Rat ?? "").IndexOf("Registry", System.StringComparison.OrdinalIgnoreCase) < 0 && (b.Rat ?? "").IndexOf("DisableDeleteNotification") < 0, b == null ? null : b.Rat);
            h.Ist("Satz nennt die Anbindung (USB-Gehäuse) und sagt, dass an der Registry nichts zu ändern ist", b != null && b.Satz.Contains("USB-Gehäuse") && b.Satz.Contains("Registry ist nichts zu ändern"), b == null ? null : b.Satz);
            h.Ist("Quelle DEVICE_TRIM_DESCRIPTOR.TrimEnabled", b != null && b.Quelle == "DEVICE_TRIM_DESCRIPTOR.TrimEnabled");
            h.Ist("Inventar nennt Windows an und Geraet ohne TRIM", b != null && b.Detail.Any(z => z.StartsWith("TRIM:") && z.Contains("in Windows aktiv") && z.Contains("meldet kein TRIM")), b == null ? null : string.Join(" | ", b.Detail));
            h.Ist("kein ok-Befund fuer das Laufwerk", Harness.Einer(erg, "datentraeger.0.zustand") == null);
            h.Ist("Inventar: Temperatur 35 °C mit Höchstwert 60 °C (Windows-Zaehler ohne Geraeteschwelle)", b != null && b.Detail.Any(z => z.StartsWith("Temperatur: 35 °C") && z.Contains("Höchstwert 60 °C")));
            // TemperatureMax 0 (USB-Bruecke, live gemessen) heisst "nicht geliefert": kein "Höchstwert 0 °C".
            d0.Zaehler.TempMax = 0;
            var bT = Genau(Pruefen(h, s), "datentraeger.0.trim.geraet");
            h.Ist("TempMax 0 -> Temperaturzeile ohne Höchstwert", bT != null && bT.Detail.Any(z => z.StartsWith("Temperatur: 35 °C") && !z.Contains("Höchstwert")), bT == null ? null : string.Join(" | ", bT.Detail));
            d0.Zaehler.TempMax = 60;

            // Registry unlesbar (null) + Geraet 0: ebenfalls die massnahmenlose Variante, kein Registry-Rat.
            d0.Trim = null;
            var erg2 = Pruefen(h, s);
            var b2 = Genau(erg2, "datentraeger.0.trim.geraet");
            // Registry null ist keine Aussage ueber die Registry: der Satz sagt "liess sich nicht lesen" (Nachpruefung 13.09.2026).
            h.Ist("Registry null + Geraet 0 -> trim.geraet ohne Massnahme, kein datentraeger.0.trim, Satz ohne Registry-Behauptung", b2 != null && b2.Massnahmen.Count == 0 && Genau(erg2, "datentraeger.0.trim") == null && b2.Satz.Contains("ließ sich nicht lesen") && !b2.Satz.Contains("nicht abgeschaltet"), b2 == null ? null : b2.Satz);
            // Registry 1 + Geraet 0: der Registry-Befund mit Massnahme, das Geraet nur als Detail - nicht zwei Warnungen.
            d0.Trim = false;
            var erg3 = Pruefen(h, s);
            var b3 = Genau(erg3, "datentraeger.0.trim");
            h.Ist("Registry 1 + Geraet 0 -> nur datentraeger.0.trim mit Massnahme, Geraet im Detail", b3 != null && b3.Massnahmen.Contains("datentraeger.trim.einschalten") && Genau(erg3, "datentraeger.0.trim.geraet") == null && b3.Detail.Any(z => z.Contains("TrimEnabled 0")));
            // Gegenproben: Geraet unterstuetzt TRIM -> kein Befund; Geraet 0 auf einer HDD -> kein Befund.
            d0.Trim = true; d0.TrimGeraet = true;
            h.Ist("Gegenprobe: Registry 0 + Geraet 1 -> kein TRIM-Befund", !Harness.AlleMit(Pruefen(h, s), "datentraeger.0.trim").Any());
            d0.TrimGeraet = false; d0.MedienTyp = 3;
            h.Ist("Gegenprobe: Geraet 0 auf HDD -> kein TRIM-Befund", !Harness.AlleMit(Pruefen(h, s), "datentraeger.0.trim").Any());
        }

        // ---------------------------------------------------------------- Medienfehler

        /// <summary>
        /// Wirksamkeit 2026-09-13: "n.MedienFehler &gt; 0" auf "&lt; 0" gedreht -> Probe "MedienFehler 3 -> warn" rot;
        /// "LeseFehlerUnkorr.Value &gt; 0" auf "&lt; 0" gedreht -> Probe "LeseFehlerUnkorr 2 -> warn" rot; beides zurueck.
        /// </summary>
        static void Medienfehler(Harness h)
        {
            h.Gruppe("Datentraeger: Medienfehler (NVMe MediaErrors, Windows ReadErrorsUncorrected)");
            var s = h.Bild("gepflanzt-datentraeger-gesund.json"); if (s == null) return;
            var d0 = s.Datentraeger[0];
            h.Ist("Grundmenge: MedienFehler 0, LeseFehlerUnkorr null", d0.Nvme != null && d0.Nvme.MedienFehler == 0 && d0.Zaehler != null && !d0.Zaehler.LeseFehlerUnkorr.HasValue);

            d0.Nvme.MedienFehler = 3;
            var erg = Pruefen(h, s);
            var b = Genau(erg, "datentraeger.0.medienfehler");
            h.Ist("MedienFehler 3 -> genau ein warn datentraeger.0.medienfehler", b != null && b.Zustand == Zustand.Warn && Harness.AlleMit(erg, "datentraeger.0.medienfehler").Count() == 1);
            h.Ist("Satz nennt 3 und die Absage \"ein gesundes Laufwerk hat 0\"", b != null && b.Satz.Contains(" 3 ") && b.Satz.Contains("hat 0"), b == null ? null : b.Satz);
            h.Ist("Quelle NVME_HEALTH_INFO_LOG.MediaErrors, Messwert 3", b != null && b.Quelle == "NVME_HEALTH_INFO_LOG.MediaErrors" && b.Messwert.Wert == "3");
            h.Ist("kein ok-Befund datentraeger.0.zustand", Harness.Einer(erg, "datentraeger.0.zustand") == null);

            // Ohne NVMe-Log: der Windows-Zaehler ReadErrorsUncorrected (SATA/USB) traegt denselben Schluessel.
            d0.Nvme = null; d0.Zaehler.LeseFehlerUnkorr = 2;
            var erg2 = Pruefen(h, s);
            var b2 = Genau(erg2, "datentraeger.0.medienfehler");
            h.Ist("Nvme null + LeseFehlerUnkorr 2 -> genau ein warn medienfehler", b2 != null && b2.Zustand == Zustand.Warn && Harness.AlleMit(erg2, "datentraeger.0.medienfehler").Count() == 1);
            h.Ist("Satz nennt 2, Quelle ReadErrorsUncorrected", b2 != null && b2.Satz.Contains(" 2 ") && b2.Quelle == "MSFT_StorageReliabilityCounter.ReadErrorsUncorrected" && b2.Messwert.Wert == "2", b2 == null ? null : b2.Satz);

            // Beide Zaehler > 0: genau EIN Befund mit der NVMe-Quelle (gewollte Entdopplung).
            d0.Nvme = new NvmeLog { CriticalWarning = 0, Spare = 100, SpareSchwelle = 10, UsedPct = 1, TempC = 39, Stunden = 3921, MedienFehler = 3 };
            var erg3 = Pruefen(h, s);
            h.Ist("MedienFehler 3 + LeseFehlerUnkorr 2 -> genau ein Befund, Quelle NVMe", Harness.AlleMit(erg3, "datentraeger.0.medienfehler").Count() == 1 && Genau(erg3, "datentraeger.0.medienfehler").Quelle == "NVME_HEALTH_INFO_LOG.MediaErrors");

            // Gegenproben: 0 bzw. null -> kein Befund, ok-Befund da.
            d0.Nvme.MedienFehler = 0; d0.Zaehler.LeseFehlerUnkorr = 0;
            var erg4 = Pruefen(h, s);
            h.Ist("Gegenprobe: MedienFehler 0 + LeseFehlerUnkorr 0 -> kein medienfehler, ok-Befund", Genau(erg4, "datentraeger.0.medienfehler") == null && Harness.Einer(erg4, "datentraeger.0.zustand") != null);
            d0.Nvme = null; d0.Zaehler.LeseFehlerUnkorr = null;
            var erg5 = Pruefen(h, s);
            h.Ist("Gegenprobe: Nvme null + LeseFehlerUnkorr null -> kein medienfehler", Genau(erg5, "datentraeger.0.medienfehler") == null && Harness.Einer(erg5, "datentraeger.0.zustand") != null);
        }

        // ---------------------------------------------------------------- USB-Stick mit Dirty-Bit

        /// <summary>
        /// Wirksamkeit 2026-09-13: in VolumeName den Buchstaben-Zweig entfernt (immer PartitionName) -> Probe "Zeile beginnt mit E: (Wechseldatenträger, FAT32)" rot; zurueck.
        /// </summary>
        static void UsbDirty(Harness h)
        {
            h.Gruppe("Datentraeger: USB-Stick E: (Typ 2) mit Dirty-Bit und 0xD00D wird gezeigt, nicht bewertet");
            var s = h.Bild("gepflanzt-datentraeger-usb-dirty.json"); if (s == null) return;
            var e = s.Volumes.FirstOrDefault(v => v.Buchstabe == "E");
            h.Ist("Grundmenge: E: Typ 2, FAT32, dirty, 0xD00D, nicht versteckt; C: sauber", e != null && e.Typ == 2 && e.Dateisystem == "FAT32" && e.Dirty == true && e.OpStatus.Contains(0xD00D) && !e.Versteckt && !e.IstNutzerVolume && s.Volumes.Any(v => v.Buchstabe == "C" && v.Dirty == false));
            var erg = Pruefen(h, s);
            h.Ist("kein Problem-Befund im Bereich", !Probleme(erg, Bereich.Datentraeger).Any(), string.Join(", ", Probleme(erg, Bereich.Datentraeger).Select(b => b.Schluessel)));
            h.Ist("kein Befund volume.E.*", !Harness.AlleMit(erg, "volume.E.").Any());
            var ok = Harness.Einer(erg, "volume.dateisystem");
            var zeile = ok == null ? null : ok.Detail.FirstOrDefault(z => z.StartsWith("E:"));
            h.Ist("Info-Zeile beginnt mit \"E: (Wechseldatenträger, FAT32)\"", zeile != null && zeile.StartsWith("E: (Wechseldatenträger, FAT32): "), ok == null ? null : string.Join(" | ", ok.Detail));
            h.Ist("Info-Zeile nennt Kennzeichen und Status 0xD00D und \"nicht bewertet\"", zeile != null && zeile.Contains("Kennzeichen gesetzt, Status 0xD00D") && zeile.Contains("nicht bewertet"), zeile);
            h.Ist("Info-Zeile sagt nicht \"Partition ohne Buchstaben\" und nicht \"versteckten Systempartitionen\"", zeile != null && !zeile.Contains("Partition ohne Buchstaben") && !zeile.Contains("versteckten Systempartitionen") && !zeile.Contains("(,"), zeile);
            h.Ist("Speicherplatz: kein Befund fuer E:, Zusatzzeile mit Wort statt Typ-Zahl", Harness.Einer(erg, "speicherplatz.E") == null && Harness.Einer(erg, "speicherplatz.C").Detail.Any(z => z.StartsWith("E: (Wechseldatenträger, FAT32): ") && z.Contains("frei")), string.Join(" | ", Harness.Einer(erg, "speicherplatz.C").Detail));

            // Partitionsnamen ohne Buchstaben: keine fuehrende Klammer mit Komma, MSR erkannt.
            var msr = new Volume { GptTyp = "{E3C9E316-0B5C-4DB8-817D-F92DF00215AE}", Versteckt = true, GroesseBytes = 16777216 };
            h.Ist("PartitionName: MSR -> \"Reservierte Partition (versteckt, 16 MB)\"", Regel.PartitionName(msr) == "Reservierte Partition (versteckt, 16 MB)", Regel.PartitionName(msr));
            var ohne = new Volume { Dateisystem = "NTFS", Versteckt = false, GroesseBytes = 1073741824 };
            h.Ist("PartitionName: nicht versteckt, ohne Typ -> \"Partition ohne Buchstaben (NTFS, 1.024 MB)\", kein \"(,\"", Regel.PartitionName(ohne) == "Partition ohne Buchstaben (NTFS, 1.024 MB)", Regel.PartitionName(ohne));
            // Netzlaufwerk (Typ 4) und CD (Typ 5) mit Buchstaben: Wort statt Zahl.
            h.Ist("VolumeName: Typ 4 -> Netzlaufwerk, Typ 5 -> CD/DVD, Typ 9 -> Typ 9", Regel.VolumeName(new Volume { Buchstabe = "Z", Typ = 4 }) == "Z: (Netzlaufwerk)" && Regel.VolumeName(new Volume { Buchstabe = "F", Typ = 5, Dateisystem = "UDF" }) == "F: (CD/DVD, UDF)" && Regel.VolumeName(new Volume { Buchstabe = "G", Typ = 9 }) == "G: (Typ 9)");

            // Gegenprobe: dieselben Werte auf einem festen Laufwerk (Typ 3) -> warn volume.E.scan mit Massnahme.
            e.Typ = 3;
            var erg2 = Pruefen(h, s);
            var b2 = Harness.Einer(erg2, "volume.E.scan");
            h.Ist("Gegenprobe: Typ 3 -> warn volume.E.scan mit Massnahme volume.scan", b2 != null && b2.Zustand == Zustand.Warn && b2.Massnahmen.Contains("volume.scan"));
        }

        // ---------------------------------------------------------------- Ereignisquellen ohne Daten

        /// <summary>
        /// Wirksamkeit 2026-09-13: Bedingung "e.Fehlend.Count == fehlendVorher" am ok-Befund entfernt -> Probe "kein ok-Befund bei zeit-Eintrag" rot; zurueck.
        /// </summary>
        static void EreignisseFehlend(Harness h)
        {
            h.Gruppe("Datentraeger: Ereignisabonnement ohne Daten (zeit/ausnahme) ist kein leeres Protokoll");
            var s = h.Bild("gepflanzt-datentraeger-gesund.json"); if (s == null) return;
            h.Ist("Grundmenge: keine Ereignisse, keine Fehler, Log nicht gesperrt", s.Ereignisse.Eintraege.Count == 0 && s.Fehlerliste.Count == 0 && s.Ereignisse.Gesperrt.Count == 0);
            h.Ist("ohne Fehlereintrag: ok-Befund datentraeger.ereignis", Genau(Pruefen(h, s), "datentraeger.ereignis") != null);

            s.Fehlerliste.Add(new Fehler { Quelle = "log.system.disk", Art = Fehler.Zeit, Text = "Zeitbudget von 15 s ausgeschöpft, Abonnement übersprungen" });
            var erg = Pruefen(h, s);
            var d = Harness.Bereich(erg, Bereich.Datentraeger);
            h.Ist("zeit-Eintrag fuer log.system.disk -> kein ok-Befund datentraeger.ereignis", Genau(erg, "datentraeger.ereignis") == null, string.Join(", ", Harness.Befunde(erg, Bereich.Datentraeger).Select(b => b.Schluessel)));
            h.Ist("Fehlend nennt disk und die Zeitüberschreitung", d.Fehlend.Any(f => f.Contains("disk 7/11/51/153") && f.Contains("Zeitüberschreitung")), string.Join("; ", d.Fehlend));
            h.Ist("kein Problem-Befund aus dem Nichts", !Probleme(erg, Bereich.Datentraeger).Any(b => b.Schluessel.StartsWith("datentraeger.ereignis")));
            h.Ist("Bereich nicht bad (Fehlend ist kein Befund)", d.Zustand != Zustand.Bad, d.Zustand);

            // ausnahme fuer 129, zugriff-freies Log: ebenfalls Fehlend statt ok.
            s.Fehlerliste.Clear();
            s.Fehlerliste.Add(new Fehler { Quelle = "log.system.id129", Art = Fehler.Ausnahme, Text = "EventLogException: x" });
            var erg2 = Pruefen(h, s);
            h.Ist("ausnahme-Eintrag fuer log.system.id129 -> kein ok-Befund, Fehlend nennt 129", Genau(erg2, "datentraeger.ereignis") == null && Harness.Bereich(erg2, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("129") && f.Contains("Fehler beim Lesen")));
            // Teilweise gelesen: ein Ntfs 55 vor dem Abbruch bleibt ein bad-Befund, die Fehlend-Zeile kommt dazu.
            s.Fehlerliste.Add(new Fehler { Quelle = "log.system.ntfs", Art = Fehler.Zeit, Text = "Abonnement log.system.ntfs: Zeitbudget nach 1 Einträgen ausgeschöpft" });
            s.Ereignisse.Eintraege.Add(new Ereignis { Log = "System", Anbieter = "Ntfs", Id = 55, Level = 2, ZeitUtc = "2026-09-05T08:00:00Z", Felder = new Dictionary<string, string> { { "DriveName", "C:" } } });
            var erg3 = Pruefen(h, s);
            h.Ist("Ntfs 55 aus teilweise gelesener Quelle -> bad bleibt, Fehlend fuer Ntfs dazu", Genau(erg3, "datentraeger.ereignis.ntfs") != null && Genau(erg3, "datentraeger.ereignis.ntfs").Zustand == Zustand.Bad && Harness.Bereich(erg3, Bereich.Datentraeger).Fehlend.Any(f => f.Contains("Ntfs 55/131")));
            // Gegenprobe: ein Fehler einer fremden Quelle (WindowsUpdateClient) aendert hier nichts.
            s.Fehlerliste.Clear(); s.Ereignisse.Eintraege.Clear();
            s.Fehlerliste.Add(new Fehler { Quelle = "log.system.windowsupdateclient", Art = Fehler.Zeit, Text = "x" });
            var erg4 = Pruefen(h, s);
            h.Ist("Gegenprobe: zeit-Eintrag einer fremden Quelle -> ok-Befund bleibt, keine Fehlend-Zeile zu Laufwerksereignissen", Genau(erg4, "datentraeger.ereignis") != null && !Harness.Bereich(erg4, Bereich.Datentraeger).Fehlend.Any(f => f.StartsWith("Laufwerksereignisse")));
        }

        // ---------------------------------------------------------------- (j) englisch

        /// <summary>Wirksamkeit 2026-09-12: im englischen Bild Spare auf 50 gesetzt -> Vergleich rot; zurueck auf 5.</summary>
        static void Englisch(Harness h)
        {
            h.Gruppe("Datentraeger: (j) englisches Bild von (a) liefert dieselben Befunde");
            var de = h.Bild("gepflanzt-datentraeger-nvme-spare.json");
            var en = h.Bild("gepflanzt-datentraeger-nvme-spare-en.json");
            if (de == null || en == null) return;
            h.Ist("Grundmenge: LCID 1031 gegen 1033, gleiche Zahlen, anderer Name", de.Sprache.Lcid == 1031 && en.Sprache.Lcid == 1033
                && de.Datentraeger[0].Nvme.Spare == en.Datentraeger[0].Nvme.Spare && de.Datentraeger[0].Name != en.Datentraeger[0].Name);
            var ergDe = Pruefen(h, de); var ergEn = Pruefen(h, en);
            var sigDe = Signatur(ergDe); var sigEn = Signatur(ergEn);
            h.Ist("Grundmenge: mindestens 4 Befunde je Bild", sigDe.Count >= 4 && sigEn.Count >= 4);
            h.Ist("gleiche Schluessel und Zustaende", sigDe.SequenceEqual(sigEn), string.Join(" ", sigDe) + "  gegen  " + string.Join(" ", sigEn));
            h.Ist("gleicher Gesamtzustand", Harness.Bereich(ergDe, Bereich.Datentraeger).Zustand == Harness.Bereich(ergEn, Bereich.Datentraeger).Zustand);
            h.Ist("Befundtexte bleiben deutsch (Anzeigename ist nur Anzeige)", Harness.Einer(ergEn, "datentraeger.0.nvme.kritisch").Satz.Contains("Reserve") && Harness.Einer(ergEn, "datentraeger.0.nvme.kritisch").Satz.Contains("Sample NVMe 1TB"));
        }

        static List<string> Signatur(IEnumerable<BereichErgebnis> erg)
        {
            return Harness.Befunde(erg, Bereich.Datentraeger).Concat(Harness.Befunde(erg, Bereich.Speicherplatz))
                .Select(b => b.Schluessel + "=" + b.Zustand).OrderBy(x => x, System.StringComparer.Ordinal).ToList();
        }
    }
}
