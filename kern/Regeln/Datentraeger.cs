using System;
using System.Collections.Generic;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Regeln fuer den Zustand der Festplatten: Datentraeger (Gesundheit, NVMe-Log, Zaehler,
    /// Temperatur, TRIM), Dateisystem der Volumes (OperationalStatus, Dirty-Bit), Ereignisse der
    /// letzten 90 Tage und der Systemschutz.
    ///
    /// Reine Funktion ueber dem Systembild: kein Windows-Zugriff, kein DateTime.Now (ctx.Jetzt ist
    /// die Aufzeichnungszeit), jede Schwelle aus Schwellen.cs, jede Wertetabelle fest im Code.
    ///
    /// Grundsatz: keine erfundenen Diagnosen. Was das Bild nicht traegt (Zaehler ohne Rechte,
    /// Dirty-Bit ohne Rechte, Temperatur ohne Geraeteschwelle), bleibt "Fehlend" - nie ok, nie bad.
    /// Der Laie liest Titel, Satz und Rat; Fachbegriffe stehen in Detail oder in Klammern.
    /// </summary>
    public static class Datentraeger
    {
        /// <summary>
        /// MSFT_PhysicalDisk.OperationalStatus-Werte, die einen Ausfall bedeuten (Tabelle aus dem
        /// lokalen MOF, amended Values; fest im Code, weil das MOF Tippfehler wie "0xDO1C" enthaelt).
        /// </summary>
        static readonly Dictionary<int, string> AusfallStatus = new Dictionary<int, string>
        {
            { 5, "Ausfall vorhergesagt (Predictive Failure)" },
            { 6, "Fehler (Error)" },
            { 7, "nicht behebbarer Fehler (Non-Recoverable Error)" },
            { 0xD004, "Medium ausgefallen (Failed Media)" },
            { 0xD007, "E/A-Fehler (IO Error)" },
            { 0xD018, "Hardwarefehler des Geräts (Device Hardware Error)" },
            { 0xD019, "nicht verwendbar (Not Usable)" },
            { 0xD025, "Schwellwert überschritten (Threshold Exceeded)" },
        };

        /// <summary>MSFT_Volume.OperationalStatus: die Aktion steht hier, nicht in HealthStatus (lokales MOF, Get-Volume).</summary>
        const int VolumeScanNoetig = 0xD00D, VolumeSpotFixNoetig = 0xD00E, VolumeReparaturNoetig = 0xD00F;

        /// <summary>GptType der EFI-Systempartition, der Wiederherstellungspartition und der Microsoft-Reserved-Partition (MSFT_Partition-Doku).</summary>
        const string GptEsp = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b", GptRecovery = "de94bba4-06d1-4d40-a16a-bfd50179d6ac", GptMsr = "e3c9e316-0b5c-4db8-817d-f92df00215ae";

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var e = new BereichErgebnis { Bereich = Bereich.Datentraeger };
            e.DatenVorhanden = s.Datentraeger.Count > 0 && s.Volumes.Count > 0;
            if (!e.DatenVorhanden)
            {
                if (s.Datentraeger.Count == 0) e.Fehlend.Add("Liste der Datenträger (" + Grund(s, "wmi.storage.physicaldisk") + ")");
                if (s.Volumes.Count == 0) e.Fehlend.Add("Liste der Laufwerke (" + Grund(s, "wmi.volume") + ")");
            }

            if (s.ZugriffVerweigert("wmi.storage.reliability"))
                e.Fehlend.Add("Verschleiß- und Fehlerzähler von Windows (braucht Administratorrechte)");

            int index = 0;
            foreach (var d in s.Datentraeger) PruefeDatentraeger(ctx, e, d, index++);
            PruefeVolumes(ctx, e);
            PruefeEreignisse(ctx, e);
            PruefeSystemschutz(ctx, e);
            return e;
        }

        // ================================================================ Datentraeger

        static void PruefeDatentraeger(Kontext ctx, BereichErgebnis e, Kern.Datentraeger d, int index)
        {
            string kennung = d.Nummer.HasValue ? d.Nummer.Value.ToString() : "i" + index;
            string basis = "datentraeger." + kennung + ".";
            string name = Bezeichnung(d);
            int vorher = e.Befunde.Count;

            // 1. Windows meldet Ausfall: HealthStatus 1 (Warnung) oder 2 (ungesund), oder ein
            //    OperationalStatus aus der Ausfalltabelle. HealthStatus 5 heisst laut Doku "Unknown" -
            //    das ist kein Ausfall, sondern ein fehlender Wert (unknown, siehe unten).
            var ausfallCodes = d.OpStatus.Where(o => AusfallStatus.ContainsKey(o)).Distinct().ToList();
            if (d.Health == 1 || d.Health == 2 || ausfallCodes.Count > 0)
            {
                // Was den Befund ausloest, traegt Satz, Quelle und Messwert gemeinsam: HealthStatus 1/2,
                // sonst (auch bei Health 0 oder 5) der Ausfallcode aus OperationalStatus.
                bool ueberOpStatus = d.Health != 1 && d.Health != 2;
                var b = Neu(basis + "ausfall", Zustand.Bad, "Laufwerk meldet Ausfall",
                    d.Health == 2
                        ? "Windows stuft das Laufwerk " + name + " als ungesund ein (Zustand 2), ein Ausfall kann jederzeit eintreten."
                        : d.Health == 1
                            ? "Windows meldet für das Laufwerk " + name + " eine Warnung (Zustand 1), ein Ausfall kündigt sich an."
                            : "Das Laufwerk " + name + " meldet den Betriebsstatus " + Hex(ausfallCodes[0]) + " (" + AusfallStatus[ausfallCodes[0]] + ").",
                    "Sichern Sie Ihre Daten sofort auf ein anderes Laufwerk und lassen Sie das Laufwerk ersetzen; keine Reparatur behebt einen Hardwaredefekt.",
                    ueberOpStatus ? "MSFT_PhysicalDisk.OperationalStatus" : "MSFT_PhysicalDisk.HealthStatus",
                    ueberOpStatus ? Messwert.Von(Hex(ausfallCodes[0]), "OperationalStatus", "2 = OK") : Messwert.Von(d.Health, "HealthStatus", "0 = gesund"));
                b.Detail.Add("HealthStatus: " + d.Health + " (0 gesund, 1 Warnung, 2 ungesund, 5 unbekannt)");
                foreach (int o in ausfallCodes) b.Detail.Add("OperationalStatus " + Hex(o) + ": " + AusfallStatus[o]);
                e.Befunde.Add(b);
            }
            else if (d.Health != 0)
            {
                var b = Neu(basis + "unbekannt", Zustand.Unknown, "Zustand des Laufwerks nicht ermittelbar",
                    "Windows kennt den Zustand des Laufwerks " + name + " nicht (HealthStatus " + d.Health + "), das ist kein Fehler des Laufwerks.",
                    null, "MSFT_PhysicalDisk.HealthStatus", Messwert.Von(d.Health, "HealthStatus", "0 = gesund"));
                e.Befunde.Add(b);
            }

            // 2. NVMe-Gesundheitslog (geht ohne Rechte). Bei NVMe gewinnt es gegen den Windows-Zaehler
            //    "Wear": auf dem Rechner des Betreibers steht Wear 0 neben PercentageUsed 1.
            if (d.Nvme != null)
            {
                var n = d.Nvme;
                if (n.CriticalWarning != 0 || n.Spare < n.SpareSchwelle)
                {
                    string satz = n.Spare < n.SpareSchwelle
                        ? "Die Reserve des Laufwerks " + name + " ist auf " + n.Spare + " % gefallen, die Grenze des Herstellers liegt bei " + n.SpareSchwelle + " %."
                        : "Das Laufwerk " + name + " meldet eine kritische Warnung (Code " + Hex(n.CriticalWarning) + ": " + CriticalWarningText(n.CriticalWarning) + ").";
                    var b = Neu(basis + "nvme.kritisch", Zustand.Bad, "SSD meldet kritischen Zustand", satz,
                        "Sichern Sie Ihre Daten sofort und planen Sie den Ersatz der SSD.",
                        n.Spare < n.SpareSchwelle ? "NVME_HEALTH_INFO_LOG.AvailableSpare" : "NVME_HEALTH_INFO_LOG.CriticalWarning",
                        n.Spare < n.SpareSchwelle ? Messwert.Von(n.Spare, "%", ">= " + n.SpareSchwelle) : Messwert.Von(Hex(n.CriticalWarning), "Bitmaske", "0"));
                    b.Detail.Add("CriticalWarning: " + Hex(n.CriticalWarning) + (n.CriticalWarning != 0 ? " (" + CriticalWarningText(n.CriticalWarning) + ")" : ""));
                    b.Detail.Add("AvailableSpare: " + n.Spare + " %, Schwelle des Herstellers: " + n.SpareSchwelle + " %");
                    e.Befunde.Add(b);
                }

                if (n.UsedPct >= Schwellen.NvmeUsedWarn)
                {
                    bool schlimm = n.UsedPct >= Schwellen.NvmeUsedBad;
                    var b = Neu(basis + "nvme.verbraucht", schlimm ? Zustand.Bad : Zustand.Warn, "SSD hat ihre Lebensdauer fast verbraucht",
                        "Das Laufwerk " + name + " hat " + n.UsedPct + " % der vom Hersteller angegebenen Lebensdauer verbraucht" + (schlimm ? ", ab 100 % gilt sie als aufgebraucht." : ", ab " + Schwellen.NvmeUsedWarn + " % ist ein Ersatz einzuplanen."),
                        schlimm ? "Sichern Sie Ihre Daten und ersetzen Sie die SSD bald; sie kann jederzeit in den Nur-Lese-Modus wechseln."
                                : "Halten Sie eine Datensicherung aktuell und planen Sie einen Ersatz.",
                        "NVME_HEALTH_INFO_LOG.PercentageUsed", Messwert.Von(n.UsedPct, "%", "< " + Schwellen.NvmeUsedWarn));
                    if (d.Zaehler != null && d.Zaehler.Wear.HasValue) b.Detail.Add("Windows-Zähler Wear: " + d.Zaehler.Wear.Value + " % (das NVMe-Log ist genauer und gewinnt)");
                    e.Befunde.Add(b);
                }

                if (n.MedienFehler > 0)
                {
                    var b = Neu(basis + "medienfehler", Zustand.Warn, "SSD zählt Medienfehler",
                        "Das Laufwerk " + name + " hat " + n.MedienFehler + " nicht korrigierbare Medienfehler gezählt, ein gesundes Laufwerk hat 0.",
                        "Sichern Sie wichtige Daten und beobachten Sie den Zähler; steigt er weiter, ersetzen Sie das Laufwerk.",
                        "NVME_HEALTH_INFO_LOG.MediaErrors", Messwert.Von(n.MedienFehler, "Fehler", "0"));
                    e.Befunde.Add(b);
                }
            }
            else if (d.Zaehler != null && d.Zaehler.Wear.HasValue && d.Zaehler.Wear.Value >= Schwellen.WearWarn)
            {
                var b = Neu(basis + "verschleiss", Zustand.Warn, "Laufwerk ist stark verschlissen",
                    "Windows meldet für das Laufwerk " + name + " einen Verschleiß von " + d.Zaehler.Wear.Value + " %, ab " + Schwellen.WearWarn + " % ist ein Ersatz einzuplanen.",
                    "Halten Sie eine Datensicherung aktuell und planen Sie einen Ersatz.",
                    "MSFT_StorageReliabilityCounter.Wear", Messwert.Von(d.Zaehler.Wear.Value, "%", "< " + Schwellen.WearWarn));
                e.Befunde.Add(b);
            }

            // Unkorrigierbare Lesefehler des Windows-Zaehlers (SATA/USB; bei NVMe null, gemessen).
            if (d.Zaehler != null && d.Zaehler.LeseFehlerUnkorr.HasValue && d.Zaehler.LeseFehlerUnkorr.Value > 0
                && !e.Befunde.Any(x => x.Schluessel == basis + "medienfehler"))
            {
                var b = Neu(basis + "medienfehler", Zustand.Warn, "Laufwerk zählt Lesefehler",
                    "Das Laufwerk " + name + " hat " + d.Zaehler.LeseFehlerUnkorr.Value + " nicht korrigierbare Lesefehler gezählt, ein gesundes Laufwerk hat 0.",
                    "Sichern Sie wichtige Daten und beobachten Sie den Zähler; steigt er weiter, ersetzen Sie das Laufwerk.",
                    "MSFT_StorageReliabilityCounter.ReadErrorsUncorrected", Messwert.Von(d.Zaehler.LeseFehlerUnkorr.Value, "Fehler", "0"));
                e.Befunde.Add(b);
            }

            // 3. Temperatur nur gegen die Schwelle des Geraets - ohne Geraeteschwelle keine Bewertung.
            int? temp = d.Nvme != null ? d.Nvme.TempC : (d.Zaehler != null ? d.Zaehler.Temp : null);
            if (temp.HasValue && d.TempWarn.HasValue && temp.Value >= d.TempWarn.Value)
            {
                var b = Neu(basis + "temperatur", Zustand.Warn, "Laufwerk ist zu warm",
                    "Das Laufwerk " + name + " ist " + temp.Value + " °C warm, die Warngrenze des Herstellers liegt bei " + d.TempWarn.Value + " °C.",
                    "Prüfen Sie Lüfter und Luftwege des Gehäuses; eine SSD drosselt bei Hitze und altert schneller.",
                    d.Nvme != null ? "NVME_HEALTH_INFO_LOG.Temperature" : "MSFT_StorageReliabilityCounter.Temperature",
                    Messwert.Von(temp.Value, "°C", "< " + d.TempWarn.Value + " (Gerät)"));
                if (d.TempKritisch.HasValue) b.Detail.Add("Kritische Grenze des Geräts: " + d.TempKritisch.Value + " °C");
                e.Befunde.Add(b);
            }
            else if (temp.HasValue && !d.TempWarn.HasValue)
                e.Fehlend.Add("Temperaturgrenze des Laufwerks " + name + " (Gerät nennt keine, " + temp.Value + " °C werden nicht bewertet)");

            // 4. TRIM auf einer SSD, zwei getrennte Ursachen (Konzept 4.2): Trim = Registry
            //    DisableDeleteNotification (false = 1, sicher aus, Massnahme setzt den Wert auf 0);
            //    TrimGeraet = DEVICE_TRIM_DESCRIPTOR.TrimEnabled (false = das Laufwerk oder seine
            //    Anbindung meldet kein TRIM - USB-Gehaeuse, RAID-Treiber; an der Registry ist nichts
            //    zu aendern, also keine Massnahme). null = nicht ermittelbar, kein Befund.
            if (d.IstSsd && d.Trim == false)
            {
                var b = Neu(basis + "trim", Zustand.Warn, "TRIM ist abgeschaltet",
                    "Für die SSD " + name + " ist TRIM in Windows abgeschaltet (DisableDeleteNotification = 1); ohne TRIM wird sie mit der Zeit langsamer und verschleißt schneller.",
                    "TRIM einschalten (Registry DisableDeleteNotification auf 0); Windows schaltet es normalerweise selbst ein.",
                    "Registry DisableDeleteNotification", Messwert.Von(1, "DisableDeleteNotification", "0 = TRIM an"));
                b.Massnahmen.Add("datentraeger.trim.einschalten");
                if (d.TrimGeraet == false) b.Detail.Add("Zusätzlich meldet das Laufwerk selbst keine TRIM-Unterstützung (DEVICE_TRIM_DESCRIPTOR.TrimEnabled 0); das ändert die Registry nicht");
                e.Befunde.Add(b);
            }
            else if (d.IstSsd && d.TrimGeraet == false)
            {
                var b = Neu(basis + "trim.geraet", Zustand.Warn, "Laufwerk meldet kein TRIM",
                    "Die SSD " + name + " oder ihre Anbindung (USB-Gehäuse, RAID-Treiber) meldet keine TRIM-Unterstützung; "
                    + (d.Trim == true ? "an der Registry ist nichts zu ändern, Windows hat TRIM eingeschaltet."
                                      : "ob Windows TRIM eingeschaltet hat, ließ sich nicht lesen (Registry DisableDeleteNotification)."),
                    "Hängt die SSD in einem USB-Gehäuse oder hinter einem RAID-Treiber, reicht die Verbindung TRIM meist nicht durch; direkt per SATA oder NVMe angeschlossen läuft sie länger und schneller.",
                    "DEVICE_TRIM_DESCRIPTOR.TrimEnabled", Messwert.Von(0, "TrimEnabled", "1"));
                e.Befunde.Add(b);
            }
            else if (d.IstSsd && !d.Trim.HasValue && !e.Fehlend.Any(f => f.StartsWith("TRIM-Einstellung")))
                e.Fehlend.Add("TRIM-Einstellung von Windows (Registry DisableDeleteNotification: " + Grund(ctx.S, "registry.trim") + ")");

            // 5. Ohne Problem: ein ok-Befund mit dem Inventar, damit der Fachmann die Zahlen sieht.
            //    Betriebsstunden nur, wenn eine Quelle sie wirklich liefert - nie "0 Stunden" aus null.
            if (e.Befunde.Count == vorher)
            {
                var b = Neu(basis + "zustand", Zustand.Ok, "Laufwerk in Ordnung",
                    "Das Laufwerk " + name + " (" + Text.Gb(d.GroesseBytes) + ", " + TypText(d) + ") meldet keine Probleme.",
                    null, "MSFT_PhysicalDisk.HealthStatus", Messwert.Von(d.Health, "HealthStatus", "0 = gesund"));
                Inventar(b, d);
                e.Befunde.Add(b);
            }
            else
            {
                // Das Inventar haengt am ersten Problem-Befund dieses Laufwerks.
                Inventar(e.Befunde[vorher], d);
            }
        }

        static void Inventar(Befund b, Kern.Datentraeger d)
        {
            b.Detail.Add("Typ: " + TypText(d) + ", Größe: " + Text.Gb(d.GroesseBytes) + (d.Firmware != null ? ", Firmware " + d.Firmware : ""));
            b.Detail.Add("HealthStatus " + d.Health + ", OperationalStatus " + (d.OpStatus.Count == 0 ? "leer" : string.Join(", ", d.OpStatus.Select(o => Hex(o)))));
            long? stunden = null;
            if (d.Zaehler != null && d.Zaehler.Stunden.HasValue) stunden = d.Zaehler.Stunden.Value;
            // Ein NVMe-Log mit 0 Stunden gibt es bei einem laufenden Laufwerk nicht: 0 heisst "nicht geliefert".
            else if (d.Nvme != null && d.Nvme.Stunden > 0) stunden = d.Nvme.Stunden;
            if (stunden.HasValue) b.Detail.Add("Betriebsstunden: " + stunden.Value);
            if (d.Nvme != null)
            {
                var n = d.Nvme;
                b.Detail.Add("NVMe-Log: Lebensdauer verbraucht " + n.UsedPct + " %, Reserve " + n.Spare + " % (Grenze " + n.SpareSchwelle + " %), Medienfehler " + n.MedienFehler + ", CriticalWarning " + Hex(n.CriticalWarning));
                b.Detail.Add("Unsaubere Abschaltungen: " + n.UnsafeShutdowns + " (Stromverlust ohne Abmeldung, kein Defekt)");
                b.Detail.Add("Temperatur: " + n.TempC + " °C" + SchwellenText(d));
            }
            else if (d.Zaehler != null)
            {
                var z = d.Zaehler;
                if (z.Wear.HasValue) b.Detail.Add("Verschleiß (Windows): " + z.Wear.Value + " %");
                // TemperatureMax 0 neben einer echten Temperatur heisst "nicht geliefert" (USB-Bruecke, live gemessen), nie "0 °C".
                if (z.Temp.HasValue) b.Detail.Add("Temperatur: " + z.Temp.Value + " °C" + SchwellenText(d) + (z.TempMax.HasValue && z.TempMax.Value > 0 ? ", Höchstwert " + z.TempMax.Value + " °C" : ""));
                if (z.LeseFehlerUnkorr.HasValue) b.Detail.Add("Unkorrigierbare Lesefehler: " + z.LeseFehlerUnkorr.Value);
            }
            if (d.IstSsd)
                b.Detail.Add("TRIM: in Windows " + (d.Trim == true ? "aktiv" : d.Trim == false ? "aus" : "nicht ermittelbar")
                             + ", Gerät " + (d.TrimGeraet == true ? "unterstützt TRIM" : d.TrimGeraet == false ? "meldet kein TRIM" : "nicht abgefragt"));
        }

        static string SchwellenText(Kern.Datentraeger d)
        {
            if (!d.TempWarn.HasValue) return " (keine Geräteschwelle)";
            return " (Warngrenze " + d.TempWarn.Value + " °C" + (d.TempKritisch.HasValue ? ", kritisch " + d.TempKritisch.Value + " °C" : "") + ")";
        }

        static string TypText(Kern.Datentraeger d)
        {
            string medium = d.MedienTyp == 4 ? "SSD" : d.MedienTyp == 3 ? "Festplatte" : d.MedienTyp == 5 ? "Speicherklassenmodul" : "Medium unbekannt";
            string bus = d.BusTyp == 17 ? "NVMe" : d.BusTyp == 11 ? "SATA" : d.BusTyp == 7 ? "USB" : d.BusTyp == 8 ? "RAID" : d.BusTyp == 3 ? "ATA" : "Bus " + d.BusTyp;
            return medium + " über " + bus;
        }

        static string Bezeichnung(Kern.Datentraeger d)
        {
            if (!string.IsNullOrEmpty(d.Name)) return "„" + d.Name + "“";
            return d.Nummer.HasValue ? "Nr. " + d.Nummer.Value : "(ohne Name)";
        }

        /// <summary>NVME_HEALTH_INFO_LOG.CriticalWarning-Bits (nvme.h).</summary>
        static string CriticalWarningText(int bits)
        {
            var t = new List<string>();
            if ((bits & 1) != 0) t.Add("Reserve unter der Schwelle");
            if ((bits & 2) != 0) t.Add("Temperatur außerhalb der Grenzen");
            if ((bits & 4) != 0) t.Add("Zuverlässigkeit beeinträchtigt");
            if ((bits & 8) != 0) t.Add("nur noch lesbar");
            if ((bits & 16) != 0) t.Add("Pufferbatterie ausgefallen");
            if (t.Count == 0) t.Add("unbekanntes Bit");
            return string.Join(", ", t);
        }

        // ================================================================ Volumes

        static void PruefeVolumes(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            bool dirtyFehlt = false;
            var gesund = new List<Volume>();
            var infoZeilen = new List<string>();

            foreach (var v in s.Volumes)
            {
                if (!v.IstNutzerVolume)
                {
                    // Nicht bewertet, nur gezeigt: versteckte Systempartitionen (die EFI-Partition traegt
                    // auf gesunden PCs Dirty-Bit und 0xD00F, gemessen) und Laufwerke mit Buchstaben, die
                    // nicht fest eingebaut sind (USB-Stick nach unsauberem Abziehen, Netzlaufwerk, CD).
                    // Nie reparieren.
                    bool auffaellig = v.Dirty == true || v.OpStatus.Any(o => o == VolumeScanNoetig || o == VolumeSpotFixNoetig || o == VolumeReparaturNoetig);
                    if (auffaellig)
                    {
                        var teile = new List<string>();
                        if (v.Dirty == true) teile.Add("Kennzeichen gesetzt");
                        teile.AddRange(v.OpStatus.Where(o => o >= VolumeScanNoetig && o <= VolumeReparaturNoetig).Select(o => "Status " + Hex(o)));
                        bool systempartition = string.IsNullOrEmpty(v.Buchstabe) || v.Versteckt;
                        infoZeilen.Add(VolumeName(v) + ": " + string.Join(", ", teile)
                                       + (systempartition ? "; bei versteckten Systempartitionen normal, wird nicht bewertet"
                                                          : "; kein festes Laufwerk (" + TypWort(v.Typ) + "), wird nicht bewertet"));
                    }
                    continue;
                }

                string kennung = "volume." + v.Buchstabe + ".";
                string lw = "Laufwerk " + v.Buchstabe + ":";
                if (v.OpStatus.Contains(VolumeReparaturNoetig))
                {
                    var b = Neu(kennung + "reparatur", Zustand.Bad, "Dateisystem braucht eine Reparatur",
                        "Windows meldet für " + lw + " eine Beschädigung des Dateisystems, die nur eine Reparatur im abgemeldeten Zustand behebt (Status 0xD00F).",
                        "Sichern Sie zuerst Ihre Daten; die Reparatur braucht einen Neustart und kann beschädigte Dateien verwerfen.",
                        "MSFT_Volume.OperationalStatus", Messwert.Von(Hex(VolumeReparaturNoetig), "OperationalStatus", "2 = OK"));
                    b.Massnahmen.Add("volume.reparatur");
                    VolumeDetail(b, v);
                    e.Befunde.Add(b);
                }
                else if (v.OpStatus.Contains(VolumeSpotFixNoetig))
                {
                    var b = Neu(kennung + "spotfix", Zustand.Warn, "Dateisystem hat vermerkte Fehler",
                        "Windows hat auf " + lw + " Fehler im Dateisystem vermerkt, die eine kurze Reparatur behebt (Status 0xD00E).",
                        "Die Reparatur nimmt das Laufwerk kurz offline; auf dem Windows-Laufwerk braucht sie einen Neustart.",
                        "MSFT_Volume.OperationalStatus", Messwert.Von(Hex(VolumeSpotFixNoetig), "OperationalStatus", "2 = OK"));
                    b.Massnahmen.Add("volume.spotfix");
                    VolumeDetail(b, v);
                    e.Befunde.Add(b);
                }
                else if (v.OpStatus.Contains(VolumeScanNoetig) || v.Dirty == true)
                {
                    bool dirty = v.Dirty == true;
                    var b = Neu(kennung + "scan", Zustand.Warn, "Dateisystem ist zur Prüfung vorgemerkt",
                        dirty ? lw + " trägt das Kennzeichen für eine ausstehende Prüfung (Dirty-Bit); wenn niemand eingreift, prüft Windows es beim nächsten Start."
                              : "Windows hat für " + lw + " eine Prüfung des Dateisystems vorgemerkt (Status 0xD00D).",
                        "Eine Online-Prüfung verändert nichts und dauert wenige Minuten; danach zeigt sich, ob eine Reparatur nötig ist.",
                        dirty ? "FSCTL_IS_VOLUME_DIRTY / Win32_Volume.DirtyBitSet" : "MSFT_Volume.OperationalStatus",
                        dirty ? Messwert.Von("gesetzt", "Dirty-Bit", "nicht gesetzt") : Messwert.Von(Hex(VolumeScanNoetig), "OperationalStatus", "2 = OK"));
                    b.Massnahmen.Add("volume.scan");
                    VolumeDetail(b, v);
                    e.Befunde.Add(b);
                }
                else
                {
                    gesund.Add(v);
                    if (!v.Dirty.HasValue) dirtyFehlt = true;
                }
            }

            if (dirtyFehlt)
                e.Fehlend.Add(s.ZugriffVerweigert("wmi.volume.dirty")
                    ? "Dateisystem-Kennzeichen (braucht Administratorrechte)"
                    : "Dateisystem-Kennzeichen (" + Grund(s, "wmi.volume.dirty") + ")");

            Befund traeger = null;
            if (gesund.Count > 0)
            {
                string liste = string.Join(", ", gesund.OrderBy(v => v.Buchstabe, StringComparer.Ordinal).Select(v => v.Buchstabe + ":"));
                var b = Neu("volume.dateisystem", Zustand.Ok, "Dateisystem in Ordnung",
                    "Windows meldet für " + liste + " keine Dateisystemfehler" + (dirtyFehlt ? " (das Prüf-Kennzeichen war ohne Administratorrechte nicht lesbar)." : "."),
                    null, "MSFT_Volume.OperationalStatus", Messwert.Von(gesund.Count, "Laufwerke", null));
                foreach (var v in gesund.OrderBy(v => v.Buchstabe, StringComparer.Ordinal)) VolumeDetail(b, v);
                e.Befunde.Add(b);
                traeger = b;
            }
            else
            {
                traeger = e.Befunde.LastOrDefault(x => x.Schluessel != null && x.Schluessel.StartsWith("volume."));
            }
            if (traeger != null) foreach (string z in infoZeilen) traeger.Detail.Add(z);
        }

        static void VolumeDetail(Befund b, Volume v)
        {
            b.Detail.Add(v.Buchstabe + ": " + (v.Dateisystem ?? "?") + ", " + Text.Gb(v.GroesseBytes)
                         + ", HealthStatus " + (v.Health.HasValue ? v.Health.Value.ToString() : "?")
                         + ", OperationalStatus " + (v.OpStatus.Count == 0 ? "?" : string.Join(", ", v.OpStatus.Select(o => Hex(o))))
                         + ", Dirty-Bit " + (v.Dirty == true ? "gesetzt" : v.Dirty == false ? "nicht gesetzt" : "nicht lesbar"));
        }

        /// <summary>Lesbarer Name einer Partition ohne Buchstaben, aus dem GptType (nie aus dem Label).</summary>
        internal static string PartitionName(Volume v)
        {
            string g = (v.GptTyp ?? "").Trim('{', '}').ToLowerInvariant();
            string art = g == GptEsp ? "EFI-Systempartition" : g == GptRecovery ? "Wiederherstellungspartition" : g == GptMsr ? "Reservierte Partition" : "Partition ohne Buchstaben";
            var teile = new List<string>();
            if (v.Versteckt) teile.Add("versteckt");
            if (v.Dateisystem != null) teile.Add(v.Dateisystem);
            teile.Add(Text.Mb(v.GroesseBytes));
            return art + " (" + string.Join(", ", teile) + ")";
        }

        /// <summary>
        /// Name eines Volumes, das nicht bewertet wird: mit Buchstaben "E: (Wechseldatenträger, FAT32)",
        /// ohne Buchstaben der Partitionsname. Muster aus dem Speicherplatz-Detail, an einer Stelle.
        /// </summary>
        internal static string VolumeName(Volume v)
        {
            if (string.IsNullOrEmpty(v.Buchstabe)) return PartitionName(v);
            return v.Buchstabe + ": (" + TypWort(v.Typ) + (v.Dateisystem != null ? ", " + v.Dateisystem : "") + ")";
        }

        /// <summary>Win32_Volume.DriveType als Wort; 3 (fest) taucht hier nur bei versteckten Partitionen mit Buchstaben auf.</summary>
        internal static string TypWort(int typ)
        {
            switch (typ)
            {
                case 2: return "Wechseldatenträger";
                case 3: return "festes Laufwerk";
                case 4: return "Netzlaufwerk";
                case 5: return "CD/DVD";
                case 6: return "RAM-Laufwerk";
                default: return "Typ " + typ;
            }
        }

        // ================================================================ Ereignisse

        static void PruefeEreignisse(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            if (ctx.LogGesperrt("System")) { e.Fehlend.Add("Ereignisprotokoll System (gesperrt)"); return; }
            int tage = s.Ereignisse.Tage;
            int vorher = e.Befunde.Count;

            // Anbieter "Ntfs" (klassisch, auf Windows 11 mit benannten Feldern): 55 = Struktur beschaedigt,
            // 131 = nicht korrigierbar, CHKDSK noetig. Widerlegungsrunde: NICHT "Microsoft-Windows-Ntfs".
            var ntfs = s.Ereignisse.Von("Ntfs", 55, 131).Where(x => ctx.Innerhalb(x.ZeitUtc, tage)).ToList();
            if (ntfs.Count > 0)
            {
                var b = Neu("datentraeger.ereignis.ntfs", Zustand.Bad, "Dateisystem hat eine Beschädigung gemeldet",
                    "Das Dateisystem hat " + ctx.ZeitraumText("System") + " " + Text.Mal(ntfs.Count) + " eine Beschädigung gemeldet (Ereignis " + string.Join("/", ntfs.Select(x => x.Id).Distinct().OrderBy(x => x)) + " von Ntfs).",
                    "Sichern Sie zuerst Ihre Daten; die Ursache ist meist das Laufwerk selbst oder ein Treiber, erst danach lohnt eine Dateisystemprüfung.",
                    "Ntfs " + string.Join("/", ntfs.Select(x => x.Id).Distinct().OrderBy(x => x)), Messwert.Von(ntfs.Count, "Ereignisse", "0"));
                foreach (var x in ntfs.OrderByDescending(x => x.ZeitUtc).Take(10))
                    b.Detail.Add(x.ZeitUtc + " Ntfs " + x.Id + Felder(x, "DriveName", "CorruptionState", "Severity", "VolumeId", "RepairDetail"));
                e.Befunde.Add(b);
            }

            // Microsoft-Windows-Ntfs 150: E/A-Fehler, Cluster von NTFS neu zugeordnet (Level 3).
            var ntfs150 = s.Ereignisse.Von("Microsoft-Windows-Ntfs", 150).Where(x => ctx.Innerhalb(x.ZeitUtc, tage)).ToList();
            if (ntfs150.Count > 0)
            {
                var b = Neu("datentraeger.ereignis.ntfs150", Zustand.Warn, "Dateisystem musste Lesefehler umgehen",
                    "NTFS hat " + ctx.ZeitraumText("System") + " " + Text.Mal(ntfs150.Count) + " einen Lesefehler umgangen und Speicherbereiche neu zugeordnet (Ereignis 150).",
                    "Beobachten Sie das Laufwerk und halten Sie eine Datensicherung aktuell; häufen sich die Meldungen, kündigt sich ein Ausfall an.",
                    "Microsoft-Windows-Ntfs 150", Messwert.Von(ntfs150.Count, "Ereignisse", "0"));
                foreach (var x in ntfs150.OrderByDescending(x => x.ZeitUtc).Take(10))
                    b.Detail.Add(x.ZeitUtc + " Ntfs 150" + Felder(x, "FailureStatus", "BadLcn", "ClustersCount", "FileName", "ProcessName"));
                e.Befunde.Add(b);
            }

            // Klassischer Anbieter "disk": 7 fehlerhafter Block, 11 Controllerfehler, 51 Fehler bei
            // Auslagerungsvorgang, 153 E/A-Vorgang wiederholt - in 30 Tagen.
            var diskAlle = s.Ereignisse.Von("disk", 7, 11, 51, 153).Where(x => ctx.Innerhalb(x.ZeitUtc, Schwellen.DiskEreignisseTage)).ToList();
            // Ereignisse eines Wechseldatentraegers (USB, BusType 7) zaehlen nicht: "Fehler bei
            // einem Auslagerungsvorgang" auf \Device\Harddisk2 ist meist ein abgezogener Stick
            // (gemessen 12.09.2026: 18 x disk 51 fuer den USB-Stick, beide NVMe ohne Eintrag).
            // Sie werden genannt, nicht bewertet.
            // Ein Datentraeger, den es im Bild gar nicht mehr gibt (Nummer unbekannt), ist ebenfalls
            // keiner der festen: feste Platten sind immer da, ein Stick von gestern nicht mehr
            // (gemessen 13.09.2026: derselbe Stick abgezogen, die 18 Ereignisse blieben im Log).
            var usbNummern = new HashSet<int>(s.Datentraeger.Where(d => d.BusTyp == BusUsb && d.Nummer.HasValue).Select(d => d.Nummer.Value));
            var festeNummern = new HashSet<int>(s.Datentraeger.Where(d => d.BusTyp != BusUsb && d.Nummer.HasValue).Select(d => d.Nummer.Value));
            var disk = diskAlle.Where(x => !IstWechseldatentraeger(x, usbNummern, festeNummern)).ToList();
            var diskUsb = diskAlle.Count - disk.Count;
            if (disk.Count >= Schwellen.DiskEreignisseWarn)
            {
                var b = Neu("datentraeger.ereignis.disk", Zustand.Warn, "Datenträgertreiber meldet Fehler",
                    "Der Datenträgertreiber hat in den letzten " + Schwellen.DiskEreignisseTage + " Tagen " + Text.Mal(disk.Count) + " einen Fehler gemeldet (Ereignis " + string.Join("/", disk.Select(x => x.Id).Distinct().OrderBy(x => x)) + " von disk).",
                    "Prüfen Sie Kabel und Anschluss des Laufwerks und sichern Sie wichtige Daten; wiederholte Meldungen deuten auf einen Defekt.",
                    "disk " + string.Join("/", disk.Select(x => x.Id).Distinct().OrderBy(x => x)), Messwert.Von(disk.Count, "Ereignisse", "< " + Schwellen.DiskEreignisseWarn));
                foreach (var x in disk.OrderByDescending(x => x.ZeitUtc).Take(10))
                    b.Detail.Add(x.ZeitUtc + " disk " + x.Id + " (" + DiskText(x.Id) + ")" + Felder(x, "0", "1"));
                if (diskUsb > 0) b.Detail.Add(diskUsb + " weitere Meldungen betreffen einen Wechseldatenträger (USB) und zählen nicht.");
                e.Befunde.Add(b);
            }
            else if (diskUsb > 0)
            {
                var b = Neu("datentraeger.ereignis.disk.usb", Zustand.Ok, "Meldungen eines Wechseldatenträgers",
                    "Der Datenträgertreiber hat in den letzten " + Schwellen.DiskEreignisseTage + " Tagen " + Text.Mal(diskUsb) + " einen Fehler für einen USB-Datenträger gemeldet; das ist meist ein Stick, der ohne Auswerfen abgezogen wurde, kein Laufwerksdefekt.",
                    "USB-Datenträger vor dem Abziehen über „Hardware sicher entfernen“ auswerfen, dann bleiben die Meldungen aus.",
                    "disk " + string.Join("/", diskAlle.Select(x => x.Id).Distinct().OrderBy(x => x)), Messwert.Von(diskUsb, "Ereignisse (USB)", "nicht bewertet"));
                foreach (var x in diskAlle.OrderByDescending(x => x.ZeitUtc).Take(5))
                    b.Detail.Add(x.ZeitUtc + " disk " + x.Id + " (" + DiskText(x.Id) + ")" + Felder(x, "0", "1"));
                e.Befunde.Add(b);
            }

            // 129 = Reset eines Geraets durch den Speichertreiber (storahci, stornvme, iaStorA ...).
            // Der Sammler liest 129 ohne Anbieter; gezaehlt wird nur Level 3 (Warnung) im System-Log
            // und nur, wenn der Anbieter ein Speicher-Miniport ist (Konzept 4.2: gegen IoLogMsg.dll
            // pruefen) - Hyper-V schreibt 129 informativ, der Zeitdienst (Microsoft-Windows-Time-
            // Service) als Warnung "kein Domaenenpeer als Zeitquelle" (auf diesem Rechner gemessen).
            var reset = s.Ereignisse.Eintraege.Where(x => x.Id == 129 && x.Level == 3
                                                          && (string.IsNullOrEmpty(x.Log) || string.Equals(x.Log, "System", StringComparison.OrdinalIgnoreCase))
                                                          && IstSpeichertreiber(x)
                                                          && ctx.Innerhalb(x.ZeitUtc, Schwellen.DiskEreignisseTage)).ToList();
            if (reset.Count >= Schwellen.ResetEreignisseWarn)
            {
                var b = Neu("datentraeger.ereignis.reset", Zustand.Warn, "Laufwerk musste zurückgesetzt werden",
                    "Der Speichertreiber hat in den letzten " + Schwellen.DiskEreignisseTage + " Tagen " + Text.Mal(reset.Count) + " ein Laufwerk zurückgesetzt (Ereignis 129), ab " + Text.Mal(Schwellen.ResetEreignisseWarn) + " gilt das als Warnzeichen.",
                    "Ursachen sind meist Kabel, Firmware, Stromsparmodus oder ein sterbendes Laufwerk; sichern Sie Daten und prüfen Sie die Verbindung.",
                    "129 (" + string.Join(", ", reset.Select(x => x.Anbieter).Distinct()) + ")", Messwert.Von(reset.Count, "Ereignisse", "< " + Schwellen.ResetEreignisseWarn));
                foreach (var x in reset.OrderByDescending(x => x.ZeitUtc).Take(10))
                    b.Detail.Add(x.ZeitUtc + " " + x.Anbieter + " 129" + Felder(x, "0"));
                e.Befunde.Add(b);
            }

            // Ein Abonnement, das nichts lieferte (Zeitbudget, Obergrenze, Ausnahme), ist kein leeres
            // Protokoll: dann Fehlend statt "keine Fehler". Befunde aus teilweise gelesenen Quellen
            // bleiben (das Lesen wirft erst nach n Eintraegen).
            int fehlendVorher = e.Fehlend.Count;
            EreignisquelleFehlt(s, e, "log.system.ntfs", "Ntfs 55/131");
            EreignisquelleFehlt(s, e, "log.system.microsoft-windows-ntfs", "Microsoft-Windows-Ntfs 150");
            EreignisquelleFehlt(s, e, "log.system.disk", "disk 7/11/51/153");
            EreignisquelleFehlt(s, e, "log.system.id129", "129");

            if (e.Befunde.Count == vorher && e.Fehlend.Count == fehlendVorher)
            {
                var b = Neu("datentraeger.ereignis", Zustand.Ok, "Keine Laufwerksfehler im Protokoll",
                    "Das Ereignisprotokoll enthält " + ctx.ZeitraumText("System") + " keine Datenträger- oder Dateisystemfehler.",
                    null, "Ntfs 55/131, Microsoft-Windows-Ntfs 150, disk 7/11/51/153, 129", Messwert.Von(0, "Ereignisse", "0"));
                b.Detail.Add("Reichweite des Protokolls System: " + ctx.LogReichweiteTage("System") + " Tage" + (s.Ereignisse.BeginnSystemUtc != null ? " (seit " + s.Ereignisse.BeginnSystemUtc + ")" : ""));
                e.Befunde.Add(b);
            }
        }

        static void EreignisquelleFehlt(Systembild s, BereichErgebnis e, string quelle, string was)
        {
            if (!s.FehlerVon(quelle).Any()) return;
            e.Fehlend.Add("Laufwerksereignisse " + was + " im Protokoll System (" + Grund(s, quelle) + ")");
        }

        /// <summary>
        /// Vom Sammler gesetztes Kennzeichen am 129-Ereignis: die Meldungsdatei des Anbieters
        /// (ProviderMetadata.MessageFilePath). Speicher-Miniports melden ueber IoLogMsg.dll, bei
        /// iaStorAVC als "IoLogMsg.dll;iaStorAVC.sys" - deshalb "enthaelt", nicht "gleich".
        /// </summary>
        internal const string MeldungsdateiFeld = "_meldungsdatei";

        /// <summary>
        /// Rueckfall ohne Kennzeichen (Anbieter deregistriert, Metadaten nicht lesbar, aelteres Bild):
        /// bekannte Storport-Miniports. Eine Positivliste allein bliebe bei jedem RAID-/VMD-Treiber offen,
        /// deshalb entscheidet zuerst das Kennzeichen.
        /// </summary>
        static readonly string[] Miniports =
        {
            "storahci", "stornvme", "storufs", "secnvme", "amd_sata", "amdsata", "amd_xata", "amdxata", "rcraid",
            "vmd", "mvs91xx", "mv91xx", "nvstor", "asahci64", "jraid", "hpsa", "smartpqi", "arcsas", "ahcix64s",
        };
        static readonly string[] MiniportFamilien = { "iaStor", "lsi_sas", "megasas", "percsas" };

        static bool IstSpeichertreiber(Ereignis x)
        {
            string md = x.Feld(MeldungsdateiFeld);
            if (!string.IsNullOrEmpty(md)) return md.IndexOf("IoLogMsg.dll", StringComparison.OrdinalIgnoreCase) >= 0;
            string a = x.Anbieter ?? "";
            return Miniports.Any(m => string.Equals(a, m, StringComparison.OrdinalIgnoreCase))
                   || MiniportFamilien.Any(f => a.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        }

        const int BusUsb = 7;   // MSFT_PhysicalDisk.BusType: 7 USB

        /// <summary>
        /// disk-Ereignisse nennen das Geraet als "\\Device\\HarddiskN\\DRn" (Feld 0 oder 1). N ist die
        /// Datentraegernummer; steht sie fuer einen USB-Datentraeger, ist das Ereignis eine
        /// Wechseldatentraeger-Meldung. Ohne erkennbare Nummer zaehlt das Ereignis (Vorsicht vor Schweigen).
        /// </summary>
        // Wechseldatentraeger: die Nummer ist als USB im Bild - oder sie fehlt im Bild ganz, obwohl
        // feste Platten bekannt sind (dann war es ein inzwischen abgezogenes Geraet). Ohne jede
        // Nummer im Ereignis oder ohne Datentraeger im Bild wird nichts weggefiltert.
        static bool IstWechseldatentraeger(Ereignis x, HashSet<int> usbNummern, HashSet<int> festeNummern)
        {
            if (usbNummern.Count == 0 && festeNummern.Count == 0) return false;
            foreach (string name in new[] { "0", "1", "DeviceName" })
            {
                string wert = x.Feld(name);
                if (string.IsNullOrEmpty(wert)) continue;
                var m = System.Text.RegularExpressions.Regex.Match(wert, @"Harddisk(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int n;
                if (m.Success && int.TryParse(m.Groups[1].Value, out n))
                    return usbNummern.Contains(n) || (festeNummern.Count > 0 && !festeNummern.Contains(n));
            }
            return false;
        }

        static string DiskText(int id)
        {
            switch (id)
            {
                case 7: return "fehlerhafter Block";
                case 11: return "Controllerfehler";
                case 51: return "Fehler bei einem Auslagerungsvorgang";
                case 153: return "E/A-Vorgang wiederholt";
                default: return "Ereignis " + id;
            }
        }

        static string Felder(Ereignis x, params string[] namen)
        {
            var t = new List<string>();
            foreach (string n in namen) { string w = x.Feld(n); if (!string.IsNullOrEmpty(w)) t.Add(n + "=" + w); }
            return t.Count == 0 ? "" : " [" + string.Join(", ", t) + "]";
        }

        // ================================================================ Systemschutz

        static void PruefeSystemschutz(Kontext ctx, BereichErgebnis e)
        {
            var s = ctx.S;
            var z = s.Systemschutz;

            if (z.PolicyAus == true)
            {
                var b = Neu("systemschutz.policy", Zustand.Warn, "Systemschutz per Richtlinie abgeschaltet",
                    "Der Systemschutz ist per Richtlinie abgeschaltet (DisableSR = 1); ohne Wiederherstellungspunkte gibt es keinen Rückweg vor Reparaturen.",
                    "Auf Firmenrechnern ist das oft gewollt; auf einem privaten PC sollte der Systemschutz eingeschaltet sein.",
                    "Policies\\SystemRestore\\DisableSR", Messwert.Von(1, "DisableSR", "0 oder fehlt"));
                Konfig(b, z);
                e.Befunde.Add(b);
                return;
            }

            // Abgeschalteter Schutz (RPSessionInterval 0 bzw. DisableSR 1 ohne Richtlinie, haeufige
            // Werkseinstellung): ohne Rechte lesbar, deshalb vor der Punkteliste. Ein "Punkt anlegen"
            // scheitert hier, also keine Massnahme, sondern der Weg zum Einschalten.
            if (z.Aktiv == false)
            {
                var b = Neu("systemschutz.aus", Zustand.Warn, "Systemschutz ist ausgeschaltet",
                    "Der Systemschutz ist ausgeschaltet, deshalb legt Windows keine Wiederherstellungspunkte an und vor Reparaturen gibt es keinen Rückweg.",
                    "Schalten Sie den Computerschutz für das Windows-Laufwerk ein („Systemsteuerung > System > Computerschutz > Konfigurieren“); danach legt Windows vor Updates und Installationen selbst Punkte an.",
                    "SystemRestoreConfig.RPSessionInterval / SystemRestore\\DisableSR", Messwert.Von("aus", "Systemschutz", "an"));
                Konfig(b, z);
                e.Befunde.Add(b);
                return;
            }

            if (z.Punkte == null)
            {
                e.Fehlend.Add(s.ZugriffVerweigert("wmi.systemrestore.punkte")
                    ? "Wiederherstellungspunkte (braucht Administratorrechte)"
                    : "Wiederherstellungspunkte (" + Grund(s, "wmi.systemrestore.punkte") + ")");
                return;
            }

            if (z.Punkte.Count == 0)
            {
                var b = Neu("systemschutz.punkte", Zustand.Warn, "Kein Wiederherstellungspunkt vorhanden",
                    "Es gibt keinen Wiederherstellungspunkt; vor jeder Reparatur sollte einer angelegt werden.",
                    "Legen Sie einen Wiederherstellungspunkt an; das dauert unter einer Minute und kostet wenig Platz.",
                    "SystemRestore (root\\default)", Messwert.Von(0, "Punkte", ">= 1"));
                b.Massnahmen.Add("systemschutz.punkt.anlegen");
                Konfig(b, z);
                e.Befunde.Add(b);
                return;
            }

            var juengster = z.Punkte.OrderByDescending(p => p.ZeitUtc).First();
            double? alter = ctx.TageSeit(juengster.ZeitUtc);
            var ok = Neu("systemschutz.punkte", Zustand.Ok, "Wiederherstellungspunkte vorhanden",
                alter.HasValue
                    ? "Der jüngste Wiederherstellungspunkt ist " + TageAlt(alter.Value) + " alt (" + z.Punkte.Count + " vorhanden)."
                    : "Es sind " + z.Punkte.Count + " Wiederherstellungspunkte vorhanden.",
                null, "SystemRestore (root\\default)", Messwert.Von(z.Punkte.Count, "Punkte", ">= 1"));
            foreach (var p in z.Punkte.OrderByDescending(p => p.ZeitUtc).Take(5))
                ok.Detail.Add((p.ZeitUtc ?? "?") + " Nr. " + p.Nummer + " Typ " + p.Typ + (p.Beschreibung != null ? ": " + p.Beschreibung : ""));
            Konfig(ok, z);
            e.Befunde.Add(ok);
        }

        static void Konfig(Befund b, Systemschutz z)
        {
            if (z.DiskPercent.HasValue) b.Detail.Add("Reservierter Platz für Wiederherstellungspunkte: " + z.DiskPercent.Value + " %");
            if (z.SessionInterval.HasValue) b.Detail.Add("RPSessionInterval: " + z.SessionInterval.Value + (z.Aktiv == true ? " (Systemschutz an)" : z.Aktiv == false ? " (Systemschutz aus)" : ""));
            // SystemRestorePointCreationFrequency: 0 = keine Drossel (der Sammler liest es so, und auf
            // diesem Rechner steht 0); fehlt der Wert, gilt die Voreinstellung von 1440 Minuten.
            if (z.Frequenz == 0)
                b.Detail.Add("Keine Drossel für neue Punkte (SystemRestorePointCreationFrequency = 0): jeder Aufruf legt einen Punkt an");
            else
                b.Detail.Add("Drossel für neue Punkte: " + (z.Frequenz.HasValue ? z.Frequenz.Value + " Minuten" : "Voreinstellung (1440 Minuten)") + "; ein zweiter Punkt innerhalb der Drossel wird still übersprungen");
            if (z.SchattenBelegt.HasValue)
                b.Detail.Add("Schattenkopien belegen " + Text.Gb1(z.SchattenBelegt.Value) + SchattenGrenze(z));
        }

        /// <summary>
        /// Obergrenze des Schattenspeichers als Nachsatz: " von höchstens 20,0 GB", bei UNBOUNDED
        /// (MaxSpace 2^64 - 1, im Bild SchattenMax null und SchattenUnbegrenzt true) " ohne Obergrenze",
        /// sonst nichts - nie "von höchstens 0,0 GB" aus einem Überlauf.
        /// </summary>
        internal static string SchattenGrenze(Systemschutz z)
        {
            if (z.SchattenMax.HasValue) return " von höchstens " + Text.Gb1(z.SchattenMax.Value);
            if (z.SchattenUnbegrenzt == true) return " ohne Obergrenze (UNBOUNDED)";
            return "";
        }

        // ================================================================ Helfer

        static Befund Neu(string schluessel, string zustand, string titel, string satz, string rat, string quelle, Messwert mw)
        {
            return new Befund
            {
                Bereich = Bereich.Datentraeger, Schluessel = schluessel, Zustand = zustand,
                Titel = titel, Satz = satz, Rat = rat, Quelle = quelle, Messwert = mw,
            };
        }

        static string Hex(int v) { return "0x" + v.ToString("X"); }

        /// <summary>"einen Tag" / "3 Tage" / "unter einem Tag" fuer "ist ... alt".</summary>
        static string TageAlt(double tage)
        {
            int t = (int)Math.Floor(tage);
            if (t < 1) return "unter einen Tag";
            return t == 1 ? "einen Tag" : t + " Tage";
        }

        /// <summary>Warum eine Quelle nichts lieferte, in einem Wort fuer die Zeile "liess sich nicht pruefen".</summary>
        static string Grund(Systembild s, string quelle)
        {
            var f = s.FehlerVon(quelle).FirstOrDefault();
            if (f == null) return "keine Daten";
            switch (f.Art)
            {
                case Fehler.Zugriff: return "braucht Administratorrechte";
                case Fehler.Zeit: return "Zeitüberschreitung";
                case Fehler.Fehlt: return "nicht verfügbar";
                default: return "Fehler beim Lesen";
            }
        }
    }
}
