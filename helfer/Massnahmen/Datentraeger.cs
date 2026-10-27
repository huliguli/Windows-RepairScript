using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Threading;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Datentraeger (docs/M3-ENTWURF.md, Abschnitt 1): volume.scan, volume.optimieren,
        // datentraeger.trim.einschalten, systemschutz.punkt.anlegen; seit M4 (docs/M4-ENTWURF.md,
        // Abschnitt 1) dazu volume.spotfix und volume.reparatur (Stufe 3). Hier nur die Anmeldung;
        // die Arbeit liegt in DatentraegerMassnahmen, damit die Sammelstelle keine Namen mit den
        // anderen Bereichsdateien teilt.
        static partial void Datentraeger(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("volume.scan", "Laufwerk prüfen (nur Scan)", 1,
                s => DatentraegerMassnahmen.BuchstabeGrund(s.Wert("buchstabe")),
                DatentraegerMassnahmen.VolumeScan));
            l.Add(Massnahmenbau.Neu("volume.optimieren", "Laufwerk optimieren", 1,
                s => DatentraegerMassnahmen.BuchstabeGrund(s.Wert("buchstabe")),
                DatentraegerMassnahmen.VolumeOptimieren));
            l.Add(Massnahmenbau.Neu("datentraeger.trim.einschalten", "TRIM einschalten", 2,
                s => null,
                DatentraegerMassnahmen.TrimEinschalten));
            l.Add(Massnahmenbau.Neu("systemschutz.punkt.anlegen", "Wiederherstellungspunkt anlegen", 1,
                s => null,
                DatentraegerMassnahmen.PunktAnlegen));
            l.Add(Massnahmenbau.Neu("volume.spotfix", "Dateisystem kurz reparieren (Spot-Fix)", 3,
                s => DatentraegerMassnahmen.BuchstabeGrund(s.Wert("buchstabe")),
                DatentraegerMassnahmen.VolumeSpotfix));
            l.Add(Massnahmenbau.Neu("volume.reparatur", "Dateisystem beim Neustart reparieren", 3,
                s => DatentraegerMassnahmen.BuchstabeGrund(s.Wert("buchstabe")),
                DatentraegerMassnahmen.VolumeReparatur));
        }
    }

    /// <summary>
    /// Die Datentraeger-Massnahmen des Helfers. Vorbedingungen misst der Helfer selbst und frisch
    /// (MSFT_Volume, MSFT_Partition, MSFT_PhysicalDisk, Registry), nie aus dem Systembild des
    /// Hosts. Lange WMI-Methoden (Repair 30 min, Spot-Fix 30 min, Offline-Reparatur 60 min,
    /// Optimize 45 min) laufen ueber Langlauf: eigener Thread, Abbruchpruefung jede Sekunde, alle
    /// 30 s eine Lebenszeile, harte Zeitgrenze. Die Stufe-3-Reparaturen (M4) auf dem
    /// Windows-Laufwerk werden nur vorgemerkt (neustart = "1"); Windows faehrt sie beim Start.
    /// Das Laufwerk mit dem Programm oder dem Laufzeitordner (BelegtDurch) wird im Betrieb nie
    /// abgehaengt: Zeile bad und Problem, nichts geaendert.
    ///
    /// Trockenlauf: Lesezugriffe laufen (Volume, Medientyp, Registry, Folgenummer), Sicherung und
    /// Eingriff nicht; Werkzeuge.Lauf und Sicherungsdatei.Schreiben melden "(trocken) ..." selbst.
    /// </summary>
    internal static class DatentraegerMassnahmen
    {
        public const string StorageNs = @"root\Microsoft\Windows\Storage";
        const string Cimv2 = @"root\cimv2";
        const int ScanMs = 30 * 60 * 1000;
        const int OptimierenMs = 45 * 60 * 1000;
        const int LeseMs = 30000;
        const int FsutilMs = 2 * 60 * 1000;
        // M4 (Vertrag Abschnitt 1): Spot-Fix bis 30 Minuten, Offline-Reparatur eines Datenlaufwerks
        // bis 60 Minuten; das Vormerken fuer den naechsten Start kehrt in Sekunden zurueck.
        const int SpotfixMs = 30 * 60 * 1000;
        const int ReparaturMs = 60 * 60 * 1000;
        const int VormerkenMs = 5 * 60 * 1000;

        /// <summary>Session Manager\BootExecute (REG_MULTI_SZ): traegt eine vorgemerkte Laufwerkspruefung als "autocheck autochk ... \??\C:".</summary>
        const string SessionManagerSchluessel = @"SYSTEM\CurrentControlSet\Control\Session Manager";

        /// <summary>HKLM-Schluessel und Wert, den auch der Sammler liest (DatentraegerQuelle.TrimSystemweit).</summary>
        public const string TrimSchluessel = @"SYSTEM\CurrentControlSet\Control\FileSystem";
        public const string TrimWert = "DisableDeleteNotification";

        // MSFT_Volume.OperationalStatus, wie in kern/Regeln/Datentraeger.cs: Scan noetig, Spot-Fix noetig, Reparatur noetig.
        const int OpScan = 0xD00D, OpSpotFix = 0xD00E, OpReparatur = 0xD00F;

        // ------------------------------------------------------------------ Parameter

        /// <summary>"C" oder "c:" → "C"; alles andere null (nie ein Pfad, nie eine Liste).</summary>
        public static string BuchstabePruefen(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            if (t.Length == 2 && t[1] == ':') t = t.Substring(0, 1);
            if (t.Length != 1) return null;
            char c = char.ToUpperInvariant(t[0]);
            return c >= 'A' && c <= 'Z' ? c.ToString() : null;
        }

        public static string BuchstabeGrund(string s)
        {
            return BuchstabePruefen(s) == null
                ? "Der Laufwerksbuchstabe muss ein einzelner Buchstabe sein (etwa „C“ oder „C:“), nicht „" + Massnahmenbau.Kurz(s) + "“."
                : null;
        }

        // ------------------------------------------------------------------ volume.scan

        /// <summary>
        /// MSFT_Volume.Repair(OfflineScanAndFix=false, Scan=true, SpotFix=false): die Online-Pruefung
        /// (chkdsk /scan), veraendert nichts. Ergebnis ist der Ausgabeparameter Output
        /// (Volume.RepairStatus aus Volume.cdxml, Tabelle unten), nicht nur der ReturnValue.
        /// </summary>
        public static void VolumeScan(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "volume.scan";
            string lw = BuchstabePruefen(s.Wert("buchstabe"));
            VolumeStand v = VolumeLesen(k, lw, true);
            if (v == null) return;
            k.Schreibe("   Laufwerk " + lw + ": (" + v.Dateisystem + ", " + Gb(v.Groesse) + ") wird online geprüft; das verändert nichts und dauert meist wenige Minuten.", "dim");

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI MSFT_Volume(" + lw + ":).Repair(OfflineScanAndFix=false, Scan=true, SpotFix=false)", "dim");
                Nachweise.Trocken(k, kennung, "Laufwerk " + lw + ": würde online geprüft; Ergebnis erst im echten Lauf.");
                return;
            }

            long? rc = null, output = null;
            string erweitert = null;
            Langlauf.Mit(k, "Die Prüfung von " + lw + ":", ScanMs, () =>
            {
                // Reihenfolge nach ID-Qualifier: 0 OfflineScanAndFix, 1 Scan, 2 SpotFix (Wmi2 ordnet so zu).
                using (ManagementBaseObject raus = Wmi2.InstanzVoll(ScanMs + 60000, StorageNs, "MSFT_Volume", "DriveLetter='" + lw + "'", "Repair", false, true, false))
                {
                    rc = Rueckgabe(raus);
                    output = Sammler.Quellen.Wmi.Lang(raus, "Output");
                    erweitert = ErweiterterStatus(raus);
                }
                return true;
            });

            if (rc != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "Die Prüfung von " + lw + ": " + (rc == 4096 ? "lieferte kein Ergebnis" : "wurde nicht angenommen") + ": " + StorageFehler(rc) + (erweitert == null ? "" : " (" + erweitert + ")") + ".");
                return;
            }
            bool gut;
            string deutung = RepairDeutung(output, out gut);
            VolumeStand nach = VolumeLesen(k, lw, false);
            string zustand = nach == null ? "" : "; " + ZustandSatz(nach);
            if (gut) Nachweise.Bestanden(k, kennung, "Laufwerk " + lw + ": geprüft, " + deutung + " (Ergebnis " + (output.HasValue ? output.Value.ToString(CultureInfo.InvariantCulture) : "?") + ")" + zustand + ".");
            else Nachweise.NichtBestanden(k, kennung, "Laufwerk " + lw + ": geprüft, " + deutung + " (Ergebnis " + (output.HasValue ? output.Value.ToString(CultureInfo.InvariantCulture) : "?") + ")" + zustand + ".");
        }

        // ------------------------------------------------------------------ volume.optimieren

        /// <summary>
        /// MSFT_Volume.Optimize: ReTrim auf einer SSD, Defrag auf einer HDD (Konzept 4.2). Den
        /// Medientyp liest der Helfer ueber MSFT_Partition.DiskNumber → MSFT_PhysicalDisk.MediaType/BusType.
        /// Ist er nicht bestimmbar (Speicherplatz, virtuelle Platte), laeuft Optimize ohne Schalter:
        /// dann waehlt Windows selbst die passende Optimierung, wie die eigene Wartungsaufgabe (defrag /O).
        /// </summary>
        public static void VolumeOptimieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "volume.optimieren";
            string lw = BuchstabePruefen(s.Wert("buchstabe"));
            VolumeStand v = VolumeLesen(k, lw, false);
            if (v == null) return;

            string medium = MedienArt(k, lw);
            string was = medium == "ssd" ? "ReTrim (freie Bereiche an die SSD melden)"
                       : medium == "hdd" ? "Defragmentierung"
                       : "die von Windows gewählte Optimierung (Medientyp nicht bestimmbar)";
            k.Schreibe("   Laufwerk " + lw + ": (" + v.Dateisystem + ", " + Gb(v.Groesse) + ", " + MediumWort(medium) + "): " + was + ".", "dim");

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI MSFT_Volume(" + lw + ":).Optimize(" + (medium == "ssd" ? "ReTrim=true" : medium == "hdd" ? "Defrag=true" : "ohne Schalter") + ")", "dim");
                Nachweise.Trocken(k, kennung, "Laufwerk " + lw + ": würde optimiert (" + was + ").");
                return;
            }

            long? rc = null;
            string erweitert = null;
            Langlauf.Mit(k, "Die Optimierung von " + lw + ":", OptimierenMs, () =>
            {
                // ID-Reihenfolge: 0 ReTrim, 1 Analyze, 2 Defrag. null laesst einen Parameter auf dem Standardwert.
                object reTrim = medium == "ssd" ? (object)true : null;
                object defrag = medium == "hdd" ? (object)true : null;
                using (ManagementBaseObject raus = Wmi2.InstanzVoll(OptimierenMs + 60000, StorageNs, "MSFT_Volume", "DriveLetter='" + lw + "'", "Optimize", reTrim, null, defrag))
                {
                    rc = Rueckgabe(raus);
                    erweitert = ErweiterterStatus(raus);
                }
                return true;
            });

            if (rc != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "Die Optimierung von " + lw + ": " + (rc == 4096 ? "lieferte kein Ergebnis" : "scheiterte") + ": " + StorageFehler(rc) + (erweitert == null ? "" : " (" + erweitert + ")") + ".");
                return;
            }
            Nachweise.Bestanden(k, kennung, "Laufwerk " + lw + ": optimiert (" + was + ", Rückgabe 0).");
        }

        // ------------------------------------------------------------------ datentraeger.trim.einschalten

        /// <summary>
        /// fsutil.exe behavior set DisableDeleteNotify 0 (Vertrag Abschnitt 1). Der Registry-Wert
        /// Control\FileSystem\DisableDeleteNotification traegt seit ReFS v2 mehr als ein Bit: Bit 0
        /// gilt fuer NTFS, Bit 1 fuer ReFS (fsutil behavior query zeigt beide getrennt aus demselben
        /// DWORD). fsutil ohne Dateisystem setzt nur NTFS; deshalb deutet der Helfer ueberall nur
        /// Bit 0 (NtfsBit): "schon so", Nachweis und Rueckweg. In die Sicherung geht der Rohwert,
        /// der Rueckweg stellt daraus Bit 0 mit demselben Werkzeug zurueck. Der Klammertext von
        /// fsutil ist lokalisiert und wird nicht gelesen.
        /// </summary>
        public static void TrimEinschalten(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "datentraeger.trim.einschalten";
            int? wert;
            if (!TrimLesen(out wert))
            {
                k.Schreibe("   Der Registrierungsschlüssel Control\\FileSystem ist nicht lesbar; TRIM bleibt unverändert.", "bad");
                Katalog.Problem(k);
                return;
            }
            if (NtfsBit(wert) == 0)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "TRIM war für NTFS schon eingeschaltet (" + TrimWort(true, wert) + "); es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   TRIM ist in Windows für NTFS abgeschaltet (" + TrimWort(true, wert) + ").", "dim");

            // Sicherung VOR dem Eingriff, mit dem Rohwert (der Rueckweg deutet daraus Bit 0); im
            // Trockenlauf meldet Schreiben "(trocken) ..." und schreibt nichts.
            Sicherungsdatei.Schreiben(k, kennung, s.Parameter, Sicherungsdatei.Werte(TrimWert, wert.Value.ToString(CultureInfo.InvariantCulture)));

            int exit = TrimSetzen(k, 0);
            if (k.Trocken)
            {
                Nachweise.Trocken(k, kennung, "TRIM würde für NTFS eingeschaltet (NTFS-Bit von DisableDeleteNotification von 1 auf 0, Rohwert vorher " + wert.Value.ToString(CultureInfo.InvariantCulture) + ").");
                return;
            }
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "fsutil meldete Exit " + exit + "; TRIM bleibt für NTFS abgeschaltet.");
                return;
            }
            int? nachher;
            bool lesbar = TrimLesen(out nachher);
            if (lesbar && NtfsBit(nachher) == 0)
                Nachweise.Bestanden(k, kennung, "TRIM ist für NTFS eingeschaltet (" + TrimWort(true, nachher) + ", vorher " + wert.Value.ToString(CultureInfo.InvariantCulture) + ").");
            else
                Nachweise.NichtBestanden(k, kennung, "fsutil meldete Erfolg, die Registrierung zeigt aber " + TrimWort(lesbar, nachher) + ".");
        }

        /// <summary>Bit 0 des Rohwerts = NTFS (0 = TRIM an, 1 = aus); fehlender Wert = Voreinstellung 0. Bit 1 (ReFS) zaehlt nicht.</summary>
        public static int NtfsBit(int? roh)
        {
            return (roh ?? 0) & 1;
        }

        /// <summary>
        /// Der TRIM-Vorwert aus einer Sicherung: "" (Wert fehlte) → 0, eine ganze Zahl 0 bis 255 →
        /// der Rohwert, alles andere null (Ablehnung). Die Grenze 255 deckt die beiden bekannten
        /// Bits und laesst keinen Muellwert in fsutil.
        /// </summary>
        public static int? TrimZahl(string s)
        {
            if (s == null) return null;
            string t = s.Trim();
            if (t.Length == 0) return 0;
            int n;
            if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return null;
            return n >= 0 && n <= 255 ? n : (int?)null;
        }

        /// <summary>"DisableDeleteNotification = N (NTFS-Bit b)"; Schluessel nicht lesbar → "DisableDeleteNotification nicht lesbar"; Schluessel lesbar, Wert fehlt → "DisableDeleteNotification fehlt (Voreinstellung 0)". Nie ein leerer Wert im Satz.</summary>
        public static string TrimWort(bool lesbar, int? wert)
        {
            if (!lesbar) return TrimWert + " nicht lesbar";
            if (!wert.HasValue) return TrimWert + " fehlt (Voreinstellung 0)";
            return TrimWert + " = " + wert.Value.ToString(CultureInfo.InvariantCulture) + " (NTFS-Bit " + NtfsBit(wert) + ")";
        }

        /// <summary>true = Schluessel lesbar; wert null = Wert fehlt (Voreinstellung 0 = TRIM aktiv). Rohwert; Bit 0 liefert NtfsBit.</summary>
        public static bool TrimLesen(out int? wert)
        {
            wert = null;
            try
            {
                using (RegistryKey key = Reg.Hklm(TrimSchluessel, false))
                {
                    if (key == null) return false;
                    wert = Reg.Ganz(key, TrimWert);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("DisableDeleteNotification nicht lesbar: " + ex.Message);
                return false;
            }
        }

        /// <summary>fsutil behavior set DisableDeleteNotify 0|1 ueber Werkzeuge.Lauf (ohne Dateisystem = NTFS, also Bit 0); Rueckgabe Exit-Code (0 = angenommen).</summary>
        public static int TrimSetzen(Ausfuehrungskontext k, int wert)
        {
            string tail;
            return Werkzeuge.Lauf(k, Fsutil(), "behavior set DisableDeleteNotify " + (wert == 0 ? "0" : "1"), FsutilMs, null, false, out tail);
        }

        // Fester Pfad aus System32: ein blosser Dateiname wuerde zuerst im Programmordner gesucht,
        // und der ist bei einer ZIP-Installation fuer Benutzer beschreibbar (M4-Grundsatz, wie Pnputil()).
        static string Fsutil()
        {
            return Path.Combine(Environment.SystemDirectory, "fsutil.exe");
        }

        // ------------------------------------------------------------------ systemschutz.punkt.anlegen

        /// <summary>
        /// Alias auf wiederherstellungspunkt.anlegen (Vertrag Abschnitt 1): die Logik (Drossel
        /// aufheben, WMI CreateRestorePoint, Nachweis ueber die Folgenummer, drei Saetze) liegt
        /// privat in Katalog.cs und wird ueber den oeffentlichen Katalogeintrag gerufen, nicht
        /// kopiert. Der Katalog ist zur Laufzeit laengst gebaut; beim Aufbau selbst ruft dieser
        /// Code nichts. Hier IST der Punkt die Massnahme: kein neuer Punkt = nicht bestanden.
        /// </summary>
        public static void PunktAnlegen(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "systemschutz.punkt.anlegen";
            Massnahme basis = Katalog.Finde("wiederherstellungspunkt.anlegen");
            if (basis == null || basis.Ausfuehren == null)
                throw new InvalidOperationException("Die Kennung wiederherstellungspunkt.anlegen fehlt im Helfer-Katalog; der Alias systemschutz.punkt.anlegen kann nicht laufen.");
            var schritt = new PlanSchritt { Kennung = basis.Kennung };
            schritt.Parameter["beschreibung"] = "Systemschutz Windows-Wartung";
            string grund = basis.Pruefen == null ? null : basis.Pruefen(schritt);
            if (grund != null) throw new InvalidOperationException(grund);

            basis.Ausfuehren(schritt, k);

            string satz;
            if (!k.Werte.TryGetValue("sicherung.satz", out satz) || string.IsNullOrEmpty(satz)) satz = "Kein Ergebnis vom Wiederherstellungspunkt";
            if (k.Trocken)
            {
                Nachweise.Trocken(k, kennung, satz.StartsWith("(trocken) ", StringComparison.Ordinal) ? satz.Substring(10) : satz);
                return;
            }
            int vorher, nachher;
            bool gestiegen = ZahlWert(k, "sicherung.vorher", out vorher) && ZahlWert(k, "sicherung.nachher", out nachher) && nachher > vorher;
            if (gestiegen) Nachweise.Bestanden(k, kennung, satz + ".");
            else Nachweise.NichtBestanden(k, kennung, satz + ".");
        }

        static bool ZahlWert(Ausfuehrungskontext k, string name, out int wert)
        {
            wert = 0;
            string v;
            return k.Werte.TryGetValue(name, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out wert);
        }

        // ------------------------------------------------------------------ volume.spotfix / volume.reparatur (M4)

        /// <summary>
        /// MSFT_Volume.Repair(OfflineScanAndFix=false, Scan=false, SpotFix=true), also chkdsk
        /// /spotfix: behebt nur die im Scan vermerkten Fehler ($corrupt), das Laufwerk ist dabei
        /// Sekunden abgehaengt. Das Windows-Laufwerk (Environment.SystemDirectory) laesst sich im
        /// Betrieb nicht abhaengen: dort wird stattdessen die Offline-Reparatur fuer den naechsten
        /// Start vorgemerkt (Repair(OfflineScanAndFix=true), neustart = "1"), wie bei
        /// volume.reparatur (docs/M4-ENTWURF.md, Abschnitt 1). Keine Sicherung, kein Rueckweg.
        /// </summary>
        public static void VolumeSpotfix(PlanSchritt s, Ausfuehrungskontext k)
        {
            Reparieren(s, k, "volume.spotfix", false);
        }

        /// <summary>
        /// MSFT_Volume.Repair(OfflineScanAndFix=true), also chkdsk /f: Windows-Laufwerk → vorgemerkt
        /// fuer den naechsten Start (neustart = "1"); anderes festes Laufwerk → sofort, das Laufwerk
        /// ist waehrenddessen abgehaengt (5 bis 60 Minuten, Lebenszeichen ueber Langlauf).
        /// </summary>
        public static void VolumeReparatur(PlanSchritt s, Ausfuehrungskontext k)
        {
            Reparieren(s, k, "volume.reparatur", true);
        }

        static void Reparieren(PlanSchritt s, Ausfuehrungskontext k, string kennung, bool offline)
        {
            string lw = BuchstabePruefen(s.Wert("buchstabe"));
            // Spot-Fix gibt es nur fuer NTFS und ReFS; die Offline-Reparatur kennt auch FAT.
            VolumeStand v = VolumeLesen(k, lw, !offline);
            if (v == null) return;
            // Nichts zu tun, wenn das Laufwerk nachweislich sauber ist: kein Reparaturvermerk und das
            // Dirty-Bit lesbar und nicht gesetzt. Ein unlesbares Dirty-Bit (null) ist kein Beleg
            // fuer "sauber", dann laeuft die Reparatur, die der Nutzer bestaetigt hat.
            if (!BrauchtReparatur(v) && v.Dirty == false)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "Laufwerk " + lw + ": braucht keine Reparatur (" + ZustandSatz(v) + "); es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   Laufwerk " + lw + ": (" + v.Dateisystem + ", " + Gb(v.Groesse) + "): " + ZustandSatz(v) + ".", "dim");
            string windows = WindowsLaufwerk();
            if (windows != null && windows == lw) { OfflineVormerken(k, kennung, lw); return; }
            // Das Laufwerk mit dem Programm (Abbild des laufenden Helfers) oder dem Laufzeitordner
            // (app.log, Laufprotokoll, Sicherungen) wird im Betrieb nicht abgehaengt: beim erzwungenen
            // Abhaengen verloere der Helfer sein Abbild oder sein Protokoll, und das Ergebnis erreichte
            // den Host nie. Ob Repair bei offenen Handles erzwingt oder verschiebt, ist nicht belegt.
            string traeger = BelegtDurch(lw);
            if (traeger != null)
            {
                k.Schreibe("   Laufwerk " + lw + ": trägt " + traeger + "; es wird nicht im Betrieb abgehängt, die Reparatur läuft hier nicht.", "bad");
                Katalog.Problem(k);
                return;
            }
            if (offline)
            {
                k.Schreibe("   Laufwerk " + lw + ": wird für die Reparatur abgehängt und ist solange nicht benutzbar (5 bis 60 Minuten).", "dim");
                if (k.Trocken)
                {
                    k.Schreibe("(trocken) WMI MSFT_Volume(" + lw + ":).Repair(OfflineScanAndFix=true, Scan=false, SpotFix=false)", "dim");
                    Nachweise.Trocken(k, kennung, "Laufwerk " + lw + ": würde abgehängt und offline repariert; Ergebnis erst im echten Lauf.");
                    return;
                }
                RepairLauf(k, kennung, lw, "Die Reparatur von " + lw + ":", ReparaturMs, true, false, false);
                return;
            }
            k.Schreibe("   Laufwerk " + lw + ": wird für den Spot-Fix einige Sekunden abgehängt; behoben werden nur die im Scan vermerkten Fehler.", "dim");
            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI MSFT_Volume(" + lw + ":).Repair(OfflineScanAndFix=false, Scan=false, SpotFix=true)", "dim");
                Nachweise.Trocken(k, kennung, "Laufwerk " + lw + ": würde per Spot-Fix repariert; Ergebnis erst im echten Lauf.");
                return;
            }
            RepairLauf(k, kennung, lw, "Der Spot-Fix von " + lw + ":", SpotfixMs, false, false, true);
        }

        /// <summary>true, wenn OperationalStatus 0xD00E oder 0xD00F traegt oder das Dirty-Bit gesetzt ist.</summary>
        static bool BrauchtReparatur(VolumeStand v)
        {
            return v.OpStatus.Contains(OpSpotFix) || v.OpStatus.Contains(OpReparatur) || v.Dirty == true;
        }

        /// <summary>Buchstabe des Windows-Laufwerks aus Environment.SystemDirectory (C:\Windows\system32 → "C"); null, wenn nicht bestimmbar.</summary>
        public static string WindowsLaufwerk()
        {
            return LaufwerkVon(Environment.SystemDirectory);
        }

        /// <summary>Laufwerksbuchstabe eines Pfads (Path.GetPathRoot → BuchstabePruefen); null bei UNC-Pfad oder nicht bestimmbar.</summary>
        static string LaufwerkVon(string pfad)
        {
            try
            {
                if (string.IsNullOrEmpty(pfad)) return null;
                string root = Path.GetPathRoot(pfad);
                if (string.IsNullOrEmpty(root) || root.Length < 2) return null;
                return BuchstabePruefen(root.Substring(0, 2));
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Was auf dem Laufwerk lw liegt und den laufenden Helfer traegt: das Programm
        /// (AppDomain.CurrentDomain.BaseDirectory, beim Betreiber D:\Tools) oder der Laufzeitordner
        /// (Ablage.Maschinenweit(): app.log, Laufprotokoll, Sicherungen). null = nichts davon.
        /// </summary>
        static string BelegtDurch(string lw)
        {
            bool programm = false, laufzeit = false;
            try { programm = LaufwerkVon(AppDomain.CurrentDomain.BaseDirectory) == lw; } catch (Exception) { }
            try { laufzeit = LaufwerkVon(Ablage.Maschinenweit()) == lw; } catch (Exception) { }
            if (programm && laufzeit) return "das Programm und den Laufzeitordner";
            if (programm) return "das Programm";
            if (laufzeit) return "den Laufzeitordner (Protokolle, Sicherungen)";
            return null;
        }

        // Das Windows-Laufwerk laesst sich im Betrieb nicht abhaengen: Repair(OfflineScanAndFix=true)
        // merkt die Reparatur fuer den naechsten Start vor (autochk vor der Anmeldung), Windows selbst
        // laeuft sie dann. Rueckgabe 0 heisst "vorgemerkt"; als Beleg liest der Helfer danach
        // BootExecute und das Dirty-Bit und nennt, was er fand.
        static void OfflineVormerken(Ausfuehrungskontext k, string kennung, string lw)
        {
            k.Schreibe("   Laufwerk " + lw + ": ist das Windows-Laufwerk und lässt sich im Betrieb nicht abhängen; die Reparatur läuft beim nächsten Start vor der Anmeldung (5 bis 60 Minuten).", "dim");
            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI MSFT_Volume(" + lw + ":).Repair(OfflineScanAndFix=true, Scan=false, SpotFix=false)", "dim");
                Nachweise.Trocken(k, kennung, "Laufwerk " + lw + ": würde für die Reparatur beim nächsten Start vorgemerkt; 1 Neustart wäre nötig.");
                return;
            }
            long? rc = null, output = null;
            string erweitert = null;
            Langlauf.Mit(k, "Das Vormerken der Reparatur von " + lw + ":", VormerkenMs, () =>
            {
                using (ManagementBaseObject raus = Wmi2.InstanzVoll(VormerkenMs + 60000, StorageNs, "MSFT_Volume", "DriveLetter='" + lw + "'", "Repair", true, false, false))
                {
                    rc = Rueckgabe(raus);
                    output = Sammler.Quellen.Wmi.Lang(raus, "Output");
                    erweitert = ErweiterterStatus(raus);
                }
                return true;
            });
            if (rc != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "Das Vormerken der Reparatur von " + lw + ": wurde nicht angenommen: " + StorageFehler(rc) + (erweitert == null ? "" : " (" + erweitert + ")") + ".");
                return;
            }
            k.Werte["neustart"] = "1";
            k.Schreibe("   1 Neustart nötig; das Programm startet ihn nicht selbst.", "warn");
            string beleg = VormerkBeleg(lw);
            Nachweise.Bestanden(k, kennung, "Laufwerk " + lw + ": ist für die Reparatur beim nächsten Start vorgemerkt (Repair(OfflineScanAndFix) angenommen, Rückgabe 0"
                + (output.HasValue ? ", Ergebnis " + output.Value.ToString(CultureInfo.InvariantCulture) : "")
                + (beleg == null ? "" : "; " + beleg) + "); die Reparatur selbst läuft beim Neustart.");
        }

        /// <summary>Was nach dem Vormerken zu sehen ist: ein BootExecute-Eintrag fuer das Laufwerk oder das Dirty-Bit; null = nichts davon lesbar.</summary>
        static string VormerkBeleg(string lw)
        {
            try
            {
                using (RegistryKey key = Reg.Hklm(SessionManagerSchluessel, false))
                {
                    var zeilen = key == null ? null : key.GetValue("BootExecute") as string[];
                    if (zeilen != null)
                        foreach (string z in zeilen)
                            if (z != null && z.IndexOf("\\??\\" + lw + ":", StringComparison.OrdinalIgnoreCase) >= 0)
                                return "BootExecute nennt " + lw + ":";
                }
            }
            catch (Exception ex) { AppLog.Warn("BootExecute nicht lesbar: " + ex.Message); }
            return DirtyLesen(lw) == true ? "Dirty-Bit gesetzt" : null;
        }

        // Repair mit den drei booleschen Parametern (ID-Reihenfolge 0 OfflineScanAndFix, 1 Scan,
        // 2 SpotFix) und Nachmessung: bestanden nur, wenn Windows ein gutes Ergebnis nennt UND das
        // Laufwerk danach ohne 0xD00E/0xD00F und ohne Dirty-Bit lesbar ist. Nicht lesbar ist nicht bestanden.
        static void RepairLauf(Ausfuehrungskontext k, string kennung, string lw, string was, int zeitMs, bool offline, bool scan, bool spotfix)
        {
            long? rc = null, output = null;
            string erweitert = null;
            Langlauf.Mit(k, was, zeitMs, () =>
            {
                using (ManagementBaseObject raus = Wmi2.InstanzVoll(zeitMs + 60000, StorageNs, "MSFT_Volume", "DriveLetter='" + lw + "'", "Repair", offline, scan, spotfix))
                {
                    rc = Rueckgabe(raus);
                    output = Sammler.Quellen.Wmi.Lang(raus, "Output");
                    erweitert = ErweiterterStatus(raus);
                }
                return true;
            });
            if (rc != 0)
            {
                Nachweise.NichtBestanden(k, kennung, was + " " + (rc == 4096 ? "lieferte kein Ergebnis" : "wurde nicht angenommen") + ": " + StorageFehler(rc) + (erweitert == null ? "" : " (" + erweitert + ")") + ".");
                return;
            }
            bool gut;
            string deutung = RepairDeutung(output, out gut);
            string ergebnis = " (Ergebnis " + (output.HasValue ? output.Value.ToString(CultureInfo.InvariantCulture) : "?") + ")";
            VolumeStand nach = VolumeLeise(lw);
            if (nach == null)
            {
                Nachweise.NichtBestanden(k, kennung, "Laufwerk " + lw + ": " + deutung + ergebnis + "; der Zustand danach ist nicht lesbar.");
                return;
            }
            // Leerer OperationalStatus und Dirty-Bit null: beides nicht lesbar, wie der Kern-Nachweis
            // VolumeRepariert (Unlesbar). Ein nicht lesbarer Wert ist kein bestandener Nachweis.
            if (nach.OpStatus.Count == 0 && !nach.Dirty.HasValue)
            {
                Nachweise.NichtBestanden(k, kennung, "Laufwerk " + lw + ": " + deutung + ergebnis + "; der Zustand danach ist nicht lesbar (weder OperationalStatus noch Dirty-Bit).");
                return;
            }
            bool frei = !nach.OpStatus.Contains(OpSpotFix) && !nach.OpStatus.Contains(OpReparatur) && nach.Dirty != true;
            if (gut && frei) Nachweise.Bestanden(k, kennung, "Laufwerk " + lw + ": repariert, " + deutung + ergebnis + "; " + ZustandSatz(nach) + ".");
            else Nachweise.NichtBestanden(k, kennung, "Laufwerk " + lw + ": " + deutung + ergebnis + "; " + ZustandSatz(nach) + ".");
        }

        // ------------------------------------------------------------------ Volume lesen

        public sealed class VolumeStand
        {
            public string Dateisystem;
            public int Typ;               // MSFT_Volume.DriveType: 3 fest
            public long Groesse;
            public List<int> OpStatus = new List<int>();
            public bool? Dirty;           // Win32_Volume.DirtyBitSet (erhoeht lesbar)
        }

        /// <summary>
        /// Vorbedingung frisch messen: das Volume gibt es, es ist ein festes Laufwerk (DriveType 3),
        /// fuer die Online-Pruefung NTFS oder ReFS. Sonst Zeile bad + Problem und null.
        /// </summary>
        static VolumeStand VolumeLesen(Ausfuehrungskontext k, string lw, bool nurNtfsRefs)
        {
            try
            {
                VolumeStand v = VolumeAbfrage(lw);
                if (v == null)
                {
                    k.Schreibe("   Ein Laufwerk " + lw + ": gibt es nicht (MSFT_Volume kennt keinen Buchstaben " + lw + ").", "bad");
                    Katalog.Problem(k);
                    return null;
                }
                if (v.Typ != 3)
                {
                    k.Schreibe("   Laufwerk " + lw + ": ist kein festes Laufwerk (DriveType " + v.Typ + "); Wechsel- und Netzlaufwerke werden nicht geprüft oder optimiert.", "bad");
                    Katalog.Problem(k);
                    return null;
                }
                if (nurNtfsRefs && !string.Equals(v.Dateisystem, "NTFS", StringComparison.OrdinalIgnoreCase) && !string.Equals(v.Dateisystem, "ReFS", StringComparison.OrdinalIgnoreCase))
                {
                    k.Schreibe("   Laufwerk " + lw + ": hat das Dateisystem " + v.Dateisystem + "; die Online-Prüfung gibt es nur für NTFS und ReFS.", "bad");
                    Katalog.Problem(k);
                    return null;
                }
                v.Dirty = DirtyLesen(lw);
                return v;
            }
            catch (Exception ex)
            {
                k.Schreibe("   Laufwerk " + lw + ": ließ sich nicht lesen: " + ex.Message, "bad");
                Katalog.Problem(k);
                return null;
            }
        }

        /// <summary>Nachmessung ohne Zeilen und ohne Problem-Signal: Volume samt Dirty-Bit oder null (fehlt oder nicht lesbar); der Aufrufer deutet null als "nicht lesbar".</summary>
        static VolumeStand VolumeLeise(string lw)
        {
            try
            {
                VolumeStand v = VolumeAbfrage(lw);
                if (v != null) v.Dirty = DirtyLesen(lw);
                return v;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Laufwerk " + lw + ": nach der Reparatur nicht lesbar: " + ex.Message);
                return null;
            }
        }

        /// <summary>MSFT_Volume zum Buchstaben (ohne Dirty-Bit) oder null, wenn es das Laufwerk nicht gibt; wirft bei WMI-Fehlern.</summary>
        static VolumeStand VolumeAbfrage(string lw)
        {
            ManagementObject mo = null;
            try
            {
                mo = Wmi2.Erste(StorageNs, "SELECT DriveLetter, FileSystem, DriveType, Size, OperationalStatus FROM MSFT_Volume WHERE DriveLetter='" + lw + "'", LeseMs);
                if (mo == null) return null;
                return new VolumeStand
                {
                    Dateisystem = Sammler.Quellen.Wmi.Str(mo, "FileSystem") ?? "?",
                    Typ = Sammler.Quellen.Wmi.Ganz(mo, "DriveType") ?? 0,
                    Groesse = Sammler.Quellen.Wmi.Lang(mo, "Size") ?? 0,
                    OpStatus = Sammler.Quellen.Wmi.Ganze(mo, "OperationalStatus"),
                };
            }
            finally { if (mo != null) mo.Dispose(); }
        }

        static bool? DirtyLesen(string lw)
        {
            ManagementObject mo = null;
            try
            {
                mo = Wmi2.Erste(Cimv2, "SELECT DirtyBitSet FROM Win32_Volume WHERE DriveLetter='" + lw + ":'", LeseMs);
                return mo == null ? null : Sammler.Quellen.Wmi.Wahr(mo, "DirtyBitSet");
            }
            catch (Exception) { return null; }
            finally { if (mo != null) mo.Dispose(); }
        }

        static string ZustandSatz(VolumeStand v)
        {
            var teile = new List<string>();
            if (v.OpStatus.Contains(OpReparatur)) teile.Add("Windows meldet weiter Reparaturbedarf (0xD00F)");
            else if (v.OpStatus.Contains(OpSpotFix)) teile.Add("Windows meldet weiter vermerkte Fehler (0xD00E)");
            else if (v.OpStatus.Contains(OpScan)) teile.Add("die Prüfung ist weiter vorgemerkt (0xD00D)");
            else teile.Add("kein Prüf- oder Reparaturvermerk mehr");
            if (v.Dirty == true) teile.Add("Dirty-Bit gesetzt");
            else if (v.Dirty == false) teile.Add("Dirty-Bit nicht gesetzt");
            return string.Join(", ", teile);
        }

        /// <summary>"ssd" | "hdd" | null (nicht bestimmbar). Weg: MSFT_Partition.DiskNumber → MSFT_PhysicalDisk.DeviceId.</summary>
        static string MedienArt(Ausfuehrungskontext k, string lw)
        {
            ManagementObject part = null, pd = null;
            try
            {
                part = Wmi2.Erste(StorageNs, "SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='" + lw + "'", LeseMs);
                int? nr = part == null ? null : Sammler.Quellen.Wmi.Ganz(part, "DiskNumber");
                if (!nr.HasValue) return null;
                pd = Wmi2.Erste(StorageNs, "SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId='" + nr.Value.ToString(CultureInfo.InvariantCulture) + "'", LeseMs);
                if (pd == null) return null;
                int medien = Sammler.Quellen.Wmi.Ganz(pd, "MediaType") ?? 0;
                int bus = Sammler.Quellen.Wmi.Ganz(pd, "BusType") ?? 0;
                // Wie Kern.Datentraeger.IstSsd/IstHdd: MediaType 4 SSD, 5 SCM, BusType 17 NVMe; 3 HDD.
                if (medien == 4 || medien == 5 || bus == 17) return "ssd";
                if (medien == 3) return "hdd";
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Medientyp von " + lw + ": nicht lesbar: " + ex.Message);
                k.Schreibe("   Der Medientyp von " + lw + ": ließ sich nicht lesen (" + Massnahmenbau.Kurz(ex.Message) + ").", "warn");
                return null;
            }
            finally { if (part != null) part.Dispose(); if (pd != null) pd.Dispose(); }
        }

        static string MediumWort(string medium)
        {
            return medium == "ssd" ? "SSD" : medium == "hdd" ? "Festplatte" : "Medientyp unbekannt";
        }

        // ------------------------------------------------------------------ Deutung

        /// <summary>
        /// Volume.RepairStatus aus der Storage-Moduldatei Volume.cdxml unter System32 (Enum
        /// "Volume.RepairStatus", lokal nachgelesen, nicht aus dem MOF, das keine Wertetabelle traegt): 0 NoErrorsFound, 1 ErrorsFixed, 2 MinorErrorsFixedOrCleanup,
        /// 3 Failed, 4 ScanNoErrorsFound, 5 ScanErrorsFoundAndFixedOnline, 6 ScanErrorsFixedOnlineAlsoNeedSpotFix,
        /// 7 ScanErrorsFoundNeedSpotFix, 8 ScanNeedsRetry, 9 ScanRunning, 10 ScanTransientSnapshotError,
        /// 11 ScanFatalSnapshotError, 12 ScanNeedsMetaDataRetry, 13 NeedOfflineScanAndFix, 15 ScanFoundLeaks,
        /// 16 ScanFoundAndFixedLeaks, 17 TriageComplete, 18 TriageUncertain, 19 TriageFailed, 20 SalvageDone.
        /// gut = das Laufwerk braucht nichts weiter; ein Code ohne diese Aussage (18, 19, 20, unbekannt) ist es nicht.
        /// </summary>
        public static string RepairDeutung(long? output, out bool gut)
        {
            gut = false;
            if (!output.HasValue) return "Windows nannte kein Ergebnis";
            switch (output.Value)
            {
                case 0: gut = true; return "keine Fehler gefunden";
                case 1: gut = true; return "Fehler gefunden und behoben";
                case 2: gut = true; return "kleinere Fehler behoben oder aufgeräumt";
                case 3: return "die Prüfung ist fehlgeschlagen";
                case 4: gut = true; return "keine Fehler gefunden";
                case 5: gut = true; return "Fehler gefunden und online behoben";
                case 6: return "Fehler online behoben, ein Rest braucht eine kurze Offline-Reparatur (Spot-Fix)";
                case 7: return "Fehler gefunden, sie brauchen eine kurze Offline-Reparatur (Spot-Fix)";
                case 8: return "die Prüfung muss wiederholt werden";
                case 9: return "eine Prüfung läuft bereits";
                case 10: return "die Momentaufnahme des Laufwerks (Schattenkopie) scheiterte vorübergehend";
                case 11: return "die Momentaufnahme des Laufwerks (Schattenkopie) scheiterte";
                case 12: return "die Prüfung der Metadaten muss wiederholt werden";
                case 13: return "das Laufwerk braucht eine vollständige Offline-Reparatur";
                case 15: return "verlorene Speicherblöcke gefunden; eine Reparatur räumt sie auf";
                case 16: gut = true; return "verlorene Speicherblöcke gefunden und aufgeräumt";
                case 17: gut = true; return "Sichtung abgeschlossen";
                case 18: return "die Sichtung brachte kein eindeutiges Ergebnis; eine Prüfung mit Reparatur ist nötig";
                case 19: return "die Sichtung ist fehlgeschlagen";
                case 20: return "die Datenrettung ist abgeschlossen; ob das Laufwerk fehlerfrei ist, sagt dieser Code nicht";
                default: return "unbekannter Ergebniscode " + output.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// ReturnValue von MSFT_Volume.Repair und .Optimize nach der ValueMap in
        /// System32\wbem\storagewmi.mof und den Values in de-DE\storagewmi.mfl (lokal nachgelesen):
        /// gemeinsam 0 bis 5, 7, 4096 (Auftrag gestartet, Ergebnis nicht abgewartet), 40001; Repair
        /// zusaetzlich 40004, 43001, 43006 bis 43014; Optimize zusaetzlich 8, 43022 bis 43032. 0 = angenommen.
        /// </summary>
        public static string StorageFehler(long? rc)
        {
            if (!rc.HasValue) return "kein Rückgabewert";
            switch (rc.Value)
            {
                case 0: return "angenommen";
                case 1: return "nicht unterstützt (Fehlercode 1)";
                case 2: return "unbestimmter Fehler (Fehlercode 2)";
                case 3: return "Zeitüberschreitung (Fehlercode 3)";
                case 4: return "fehlgeschlagen (Fehlercode 4)";
                case 5: return "ungültiger Parameter (Fehlercode 5)";
                case 7: return "der Befehl läuft nicht als 32-Bit-Prozess auf 64-Bit-Windows (Fehlercode 7)";
                case 8: return "Objekt nicht gefunden (Fehlercode 8)";
                case 4096: return "Windows hat den Auftrag nur gestartet und kein Ergebnis geliefert (Fehlercode 4096)";
                case 40001: return "Zugriff verweigert (Fehlercode 40001)";
                case 40004: return "unerwarteter E/A-Fehler (Fehlercode 40004)";
                case 43001: return "das Dateisystem wird nicht unterstützt (Fehlercode 43001)";
                case 43006: return "das Laufwerk ist schreibgeschützt (Fehlercode 43006)";
                case 43007: return "die Reparatur ist fehlgeschlagen (Fehlercode 43007)";
                case 43008: return "die Prüfung ist fehlgeschlagen (Fehlercode 43008)";
                case 43009: return "Fehler bei der Momentaufnahme des Laufwerks; ein zweiter Versuch kann gelingen, sonst hilft nur die Offline-Reparatur (Fehlercode 43009)";
                case 43010: return "auf diesem Laufwerk läuft schon eine Prüfung; chkdsk lässt nur 1 Prüfung je Laufwerk zu (Fehlercode 43010)";
                case 43011: return "Fehler bei der Momentaufnahme des Laufwerks; ein zweiter Versuch kann gelingen, sonst hilft nur die Offline-Reparatur (Fehlercode 43011)";
                case 43012: return "Fehler bei der Momentaufnahme des Laufwerks; es bleibt nur die Offline-Reparatur (Fehlercode 43012)";
                case 43013: return "das Laufwerk lässt sich nicht für den Direktzugriff öffnen (Fehlercode 43013)";
                case 43014: return "das Dateisystem des Laufwerks ist nicht bestimmbar (Fehlercode 43014)";
                case 43022: return "die Hardware hinter dem Laufwerk unterstützt diese Optimierung nicht (Fehlercode 43022)";
                case 43023: return "der Datenträger hinter dem Laufwerk ist voll (Fehlercode 43023)";
                case 43024: return "auf dem Laufwerk läuft schon eine Optimierung (Fehlercode 43024)";
                case 43025: return "das Laufwerk lässt sich nicht optimieren (Fehlercode 43025)";
                case 43026: return "das Laufwerk ist beschädigt; erst prüfen, dann optimieren (Fehlercode 43026)";
                case 43027: return "der Vorgang wurde abgebrochen (Fehlercode 43027)";
                case 43028: return "eine Datei auf dem Laufwerk ließ sich nicht öffnen (Fehlercode 43028)";
                case 43029: return "abgebrochen, weil sich die Slab-Größe des Speichers während des Vorgangs änderte (Fehlercode 43029)";
                case 43030: return "Slab-Konsolidierung und -Analyse laufen nur bei Slabs ab 8 MB (Fehlercode 43030)";
                case 43031: return "die Ausrichtung des Laufwerks passt nicht zur gewünschten Slab- oder TRIM-Optimierung (Fehlercode 43031)";
                case 43032: return "die Master File Table des Dateisystems ist zu stark fragmentiert (Fehlercode 43032)";
                default: return "Fehlercode " + rc.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        static long? Rueckgabe(ManagementBaseObject raus)
        {
            if (raus == null) return null;
            try { return Wmi2.Ganz(raus["ReturnValue"]); }
            catch (ManagementException) { return null; }
        }

        /// <summary>MSFT_StorageExtendedStatus.ErrorDescription (lokalisiert, nur als Zusatz gezeigt, nie gedeutet).</summary>
        static string ErweiterterStatus(ManagementBaseObject raus)
        {
            if (raus == null) return null;
            try
            {
                var es = raus["ExtendedStatus"] as ManagementBaseObject;
                if (es == null) return null;
                string t = Sammler.Quellen.Wmi.Str(es, "ErrorDescription");
                return string.IsNullOrWhiteSpace(t) ? null : Massnahmenbau.Kurz(t.Trim());
            }
            catch (Exception) { return null; }
        }

        static string Gb(long bytes)
        {
            double gb = bytes / 1073741824.0;
            return (gb >= 100 ? gb.ToString("0", CultureInfo.InvariantCulture) : gb.ToString("0.0", CultureInfo.InvariantCulture)) + " GB";
        }
    }

    /// <summary>
    /// Ein langer Aufruf (WMI Repair/Optimize, COM) auf eigenem Thread mit Zeitgrenze, wie
    /// Zeitgrenze.Mit, aber mit Lebenszeichen fuer den Nutzer und Abbruchpruefung: jede Sekunde
    /// wird kontext.IstAbgebrochen gefragt, alle 30 s (spaeter jede Minute) steht "laeuft seit"
    /// im Ablauf, damit eine 20-Minuten-Pruefung nicht wie ein Haenger aussieht. Ein Abbruch
    /// oder Zeitablauf gibt den Thread auf (Hintergrund-Thread); der Vorgang in Windows selbst
    /// laeuft dann zu Ende, das sagt die Ausnahme auch.
    /// </summary>
    internal static class Langlauf
    {
        public static T Mit<T>(Ausfuehrungskontext k, string was, int zeitMs, Func<T> f)
        {
            T ergebnis = default(T);
            Exception fehler = null;
            var t = new Thread(() =>
            {
                try { ergebnis = f(); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "langlauf:" + (was ?? "").Substring(0, Math.Min(40, (was ?? "").Length)) };
            t.Start();
            var sw = Stopwatch.StartNew();
            int naechsteSekunde = 30;
            while (!t.Join(1000))
            {
                if (k != null && k.IstAbgebrochen)
                    throw new OperationCanceledException(was + " wurde abgebrochen; der schon laufende Vorgang endet in Windows von selbst.");
                if (sw.ElapsedMilliseconds >= zeitMs)
                    throw new TimeoutException(was + " überschritt " + (zeitMs / 60000) + " Minuten; der Vorgang in Windows läuft weiter, das Ergebnis fehlt.");
                if (k != null && sw.Elapsed.TotalSeconds >= naechsteSekunde)
                {
                    k.Schreibe("   " + was + " läuft seit " + Dauer(sw.Elapsed) + " …", "dim");
                    naechsteSekunde += naechsteSekunde < 120 ? 30 : 60;
                }
            }
            if (fehler != null) throw fehler;
            return ergebnis;
        }

        static string Dauer(TimeSpan t)
        {
            return ((int)t.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture) + " min";
        }
    }
}
