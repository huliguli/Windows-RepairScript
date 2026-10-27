using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Hardware (docs/M3-ENTWURF.md, Abschnitt 1): geraet.aktivieren; seit M4 (docs/M4-ENTWURF.md,
        // Abschnitt 1) dazu geraet.treiber.neu (Stufe 3). Anmeldung hier, Arbeit in
        // HardwareMassnahmen. Ob der Nutzer die Frage mit "reparieren" beantwortet hat, prueft der
        // Host (Entscheidungen); der Helfer prueft Parameter und Zustand.
        static partial void Hardware(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("geraet.aktivieren", "Gerät wieder einschalten", 2,
                HardwareMassnahmen.Pruefen, HardwareMassnahmen.Aktivieren));
            l.Add(Massnahmenbau.Neu("geraet.treiber.neu", "Treiber neu installieren", 3,
                HardwareMassnahmen.Pruefen, HardwareMassnahmen.TreiberNeu));
        }
    }

    /// <summary>
    /// Win32_PnPEntity.Enable() auf der Instanz mit PNPDeviceID = instanzId (Konzept 4.1): nur,
    /// wenn das Geraet gerade Code 22 (abgeschaltet) meldet; rebootNeeded wird als Wert neustart
    /// und als Zeile gemeldet. Der Rueckweg ruft Disable() auf derselben Instanz.
    ///
    /// geraet.treiber.neu (M4): INF aus Win32_PnPSignedDriver.InfName zur DeviceID; ein Herstellertreiber
    /// (oemNN.inf) wird vor dem Eingriff mit pnputil /export-driver in einen Ordner unter
    /// sicherungen\massnahmen\export gesichert (Besitzer und DACL ueber Sicherungsdatei.ExportSchuetzen),
    /// dann pnputil /remove-device und /scan-devices. Ein Windows-eigener Treiber liegt im
    /// Treiberspeicher, dort gibt es nichts zu exportieren. Kein Rueckweg ueber dieses Programm
    /// (rueckweg.moeglich = "0"): der Exportordner steht in der Sicherung und im Nachweissatz.
    /// Prozesse nur ueber Werkzeuge.Lauf mit pnputil.exe aus Environment.SystemDirectory.
    /// </summary>
    internal static class HardwareMassnahmen
    {
        const string Cimv2 = @"root\cimv2";
        const int LeseMs = 30000;
        const int SchaltMs = 2 * 60 * 1000;
        // Win32_PnPSignedDriver zaehlt beim Filtern alle Treiber durch; pnputil-Export und -Scan
        // brauchen je nach Treiber Minuten, das Entfernen Sekunden.
        const int TreiberLeseMs = 2 * 60 * 1000;
        const int ExportMs = 5 * 60 * 1000;
        const int EntfernenMs = 2 * 60 * 1000;
        const int ScanMs = 5 * 60 * 1000;
        // Nach /scan-devices richtet Windows das Geraet im Hintergrund ein; bis 30 s nachlesen.
        const int NachleseMs = 30000;

        /// <summary>Problemcode "Geraet abgeschaltet" (CM_PROB_DISABLED), wie kern/Regeln/Hardware.cs.</summary>
        public const int CodeAbgeschaltet = 22;

        /// <summary>Veroeffentlichter Name eines Fremdtreibers im Treiberspeicher (oem0.inf ... oem999999.inf); nur so ein Name geht an pnputil /export-driver.</summary>
        static readonly Regex OemInfRx = new Regex(@"^oem[0-9]{1,6}\.inf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// PnP-Instanz-Id (PCI\VEN_8086&amp;DEV_...\3&amp;11583659&amp;0&amp;C8, USB\VID_...\5&amp;..., SWD\MMDEVAPI\{...}.{...}).
        /// Die Zeichenmenge (1 bis 400 Zeichen aus Buchstaben, Ziffern und \ &amp; _ { } . # ( ) + , = @ ~ ! -
        /// Leerzeichen; kein doppelter, kein fuehrender oder abschliessender Backslash) fuehrt der Kern in
        /// Regeln.Hardware.InstanzIdZulaessig; die Regel haengt geraet.aktivieren nur an Ids, die hier
        /// auch durchkommen. Sonst null. In der WQL-Bedingung steht die Id nur ueber Wmi2.Wql maskiert.
        /// </summary>
        public static string InstanzIdNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            return Kern.Regeln.Hardware.InstanzIdZulaessig(t) ? t : null;
        }

        public static string Pruefen(PlanSchritt s)
        {
            return InstanzIdNorm(s.Wert("instanzId")) == null
                ? "Ungültige Instanz-Id „" + Massnahmenbau.Kurz(s.Wert("instanzId")) + "“: erlaubt sind bis zu 400 Zeichen aus Buchstaben, Ziffern und \\ & _ { } . # ( ) + , = @ ~ ! -."
                : null;
        }

        public sealed class GeraetStand
        {
            public bool Gefunden;
            public string Name;
            public int Code;          // ConfigManagerErrorCode
            public bool Vorhanden;    // Present
        }

        public static GeraetStand Lesen(string instanzId)
        {
            ManagementObject mo = null;
            try
            {
                mo = Wmi2.Erste(Cimv2, "SELECT Name, ConfigManagerErrorCode, Present FROM Win32_PnPEntity WHERE PNPDeviceID='" + Wmi2.Wql(instanzId) + "'", LeseMs);
                if (mo == null) return new GeraetStand { Gefunden = false };
                return new GeraetStand
                {
                    Gefunden = true,
                    Name = Sammler.Quellen.Wmi.Str(mo, "Name"),
                    Code = Sammler.Quellen.Wmi.Ganz(mo, "ConfigManagerErrorCode") ?? -1,
                    Vorhanden = Sammler.Quellen.Wmi.Wahr(mo, "Present") ?? true,
                };
            }
            finally { if (mo != null) mo.Dispose(); }
        }

        public sealed class SchaltErgebnis
        {
            public long? Rueckgabe;
            public bool Neustart;
        }

        /// <summary>Enable() oder Disable() auf Win32_PnPEntity PNPDeviceID='instanzId'; liefert ReturnValue und rebootNeeded.</summary>
        public static SchaltErgebnis Schalten(string instanzId, bool einschalten)
        {
            using (ManagementBaseObject raus = Wmi2.InstanzVoll(SchaltMs, Cimv2, "Win32_PnPEntity", "PNPDeviceID='" + Wmi2.Wql(instanzId) + "'", einschalten ? "Enable" : "Disable"))
            {
                var e = new SchaltErgebnis();
                if (raus != null)
                {
                    try { e.Rueckgabe = Wmi2.Ganz(raus["ReturnValue"]); } catch (ManagementException) { }
                    e.Neustart = Sammler.Quellen.Wmi.Wahr(raus, "rebootNeeded") ?? false;
                }
                return e;
            }
        }

        public static string Anzeigename(GeraetStand g, string instanzId)
        {
            return string.IsNullOrWhiteSpace(g.Name) ? instanzId : g.Name.Trim();
        }

        public static void Aktivieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "geraet.aktivieren";
            string id = InstanzIdNorm(s.Wert("instanzId"));
            GeraetStand stand = Lesen(id);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Ein Gerät mit der Instanz-Id „" + id + "“ ist nicht (mehr) vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string name = Anzeigename(stand, id);
            if (stand.Code == 0)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "„" + name + "“ läuft schon (Problemcode 0); es wurde nichts geändert.");
                return;
            }
            if (stand.Code != CodeAbgeschaltet)
            {
                k.Schreibe("   „" + name + "“ meldet Problemcode " + stand.Code + ", nicht 22 (abgeschaltet); Einschalten hilft hier nicht.", "bad");
                Katalog.Problem(k);
                return;
            }
            k.Schreibe("   „" + name + "“ ist abgeschaltet (Problemcode 22" + (stand.Vorhanden ? "" : ", nicht angeschlossen") + ").", "dim");

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("instanzId", id),
                Sicherungsdatei.Werte("code", stand.Code.ToString(CultureInfo.InvariantCulture), "name", name));

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI Win32_PnPEntity(" + id + ").Enable()", "dim");
                Nachweise.Trocken(k, kennung, "„" + name + "“ würde eingeschaltet (Win32_PnPEntity.Enable).");
                return;
            }
            SchaltErgebnis erg = Schalten(id, true);
            if (erg.Rueckgabe != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "Enable meldete " + Fehlertext(erg.Rueckgabe) + "; „" + name + "“ bleibt abgeschaltet.");
                return;
            }
            if (erg.Neustart)
            {
                k.Werte["neustart"] = "1";
                k.Schreibe("   Windows braucht 1 Neustart, damit „" + name + "“ wieder läuft (rebootNeeded).", "warn");
            }
            GeraetStand nach = Lesen(id);
            if (nach.Gefunden && nach.Code == 0)
                Nachweise.Bestanden(k, kennung, "„" + name + "“ ist eingeschaltet (Problemcode 0, vorher 22)" + (erg.Neustart ? "; vollständig wirksam nach 1 Neustart" : "") + ".");
            else if (erg.Neustart)
                Nachweise.Bestanden(k, kennung, "Enable wurde angenommen; „" + name + "“ meldet bis zum Neustart noch Code " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + ".");
            else
                Nachweise.NichtBestanden(k, kennung, "Enable wurde angenommen, „" + name + "“ meldet aber weiter Problemcode " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + ".");
        }

        public static string Fehlertext(long? rc)
        {
            if (!rc.HasValue) return "keinen Rückgabewert";
            return "Fehlercode " + rc.Value.ToString(CultureInfo.InvariantCulture) + " (0x" + unchecked((uint)rc.Value).ToString("X8", CultureInfo.InvariantCulture) + ")";
        }

        // ================================================================ geraet.treiber.neu (M4)

        public sealed class TreiberStand
        {
            public string Inf;        // Win32_PnPSignedDriver.InfName: oemNN.inf (Hersteller) oder ein Windows-eigener Name
            public string Version;
            public string Anbieter;
        }

        /// <summary>Treibereintrag zur Instanz-Id (Win32_PnPSignedDriver.DeviceID) oder null, wenn kein Treiber eingetragen ist (etwa Code 28).</summary>
        public static TreiberStand TreiberLesen(string instanzId)
        {
            ManagementObject mo = null;
            try
            {
                mo = Wmi2.Erste(Cimv2, "SELECT InfName, DriverVersion, DriverProviderName FROM Win32_PnPSignedDriver WHERE DeviceID='" + Wmi2.Wql(instanzId) + "'", TreiberLeseMs);
                if (mo == null) return null;
                string inf = Sammler.Quellen.Wmi.Str(mo, "InfName");
                if (string.IsNullOrWhiteSpace(inf)) return null;
                return new TreiberStand
                {
                    Inf = inf.Trim(),
                    Version = Sammler.Quellen.Wmi.Str(mo, "DriverVersion"),
                    Anbieter = Sammler.Quellen.Wmi.Str(mo, "DriverProviderName"),
                };
            }
            finally { if (mo != null) mo.Dispose(); }
        }

        /// <summary>true fuer oemNN.inf (Herstellertreiber im Treiberspeicher); Windows-eigene INFs heissen anders und liegen nicht zum Export vor.</summary>
        public static bool IstOemInf(string inf)
        {
            return !string.IsNullOrEmpty(inf) && OemInfRx.IsMatch(inf);
        }

        static string Pnputil()
        {
            return Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        }

        /// <summary>
        /// Reihenfolge (Vertrag Abschnitt 1): Geraet lesen (Code weder 0 noch 22), Treiber lesen,
        /// bei oemNN.inf Export und Schutz des Exportordners, Sicherung (inf, export, name, code),
        /// dann /remove-device (Exit 0 oder 3010; 3010 setzt Werkzeuge.Lauf als neustart) und
        /// /scan-devices (Exit 0), zuletzt bis 30 s nachlesen, ob das Geraet mit Code 0 zurueck ist.
        /// Scheitert der Export, wird nichts entfernt. Trockenlauf: lesen, (trocken)-Zeilen, kein
        /// Export, keine Datei, kein Prozess.
        /// </summary>
        public static void TreiberNeu(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "geraet.treiber.neu";
            string id = InstanzIdNorm(s.Wert("instanzId"));
            GeraetStand stand = Lesen(id);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Ein Gerät mit der Instanz-Id „" + id + "“ ist nicht (mehr) vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string name = Anzeigename(stand, id);
            // Nicht lesbar (Lesen liefert -1) ist kein Befund: ohne gemessenen Problemcode wird kein
            // Treiber entfernt (fail closed), auch wenn -1 weder 0 noch 22 ist.
            if (stand.Code < 0)
            {
                k.Schreibe("   Der Problemcode von „" + name + "“ ist nicht lesbar (Win32_PnPEntity ohne ConfigManagerErrorCode); der Treiber wird nicht neu installiert.", "bad");
                Katalog.Problem(k);
                return;
            }
            if (stand.Code == 0)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "„" + name + "“ läuft ohne Fehlercode (Problemcode 0); der Treiber wurde nicht neu installiert.");
                return;
            }
            if (stand.Code == CodeAbgeschaltet)
            {
                k.Schreibe("   „" + name + "“ ist abgeschaltet (Problemcode 22); erst einschalten, eine Neuinstallation des Treibers ändert daran nichts.", "bad");
                Katalog.Problem(k);
                return;
            }
            k.Schreibe("   „" + name + "“ meldet Problemcode " + stand.Code + (stand.Vorhanden ? "" : ", nicht angeschlossen") + ".", "dim");

            TreiberStand treiber = TreiberLesen(id);
            string inf = treiber == null ? "" : treiber.Inf;
            if (treiber == null)
                k.Schreibe("   Für „" + name + "“ ist kein Treiber eingetragen (Win32_PnPSignedDriver ohne Eintrag); es gibt nichts zu exportieren, Windows sucht nach der Neuerkennung selbst einen Treiber.", "dim");
            else
                k.Schreibe("   Treiber: " + inf + (string.IsNullOrWhiteSpace(treiber.Anbieter) ? "" : " von " + treiber.Anbieter.Trim()) + (string.IsNullOrWhiteSpace(treiber.Version) ? "" : ", Version " + treiber.Version.Trim()) + ".", "dim");

            // Export nur fuer Herstellertreiber: der Ordnername (kein Pfad) geht in die Sicherung.
            string export = "";
            if (IstOemInf(inf))
            {
                export = Sicherungsdatei.ExportName("treiber", "");
                if (k.Trocken)
                {
                    // Nicht ExportOrdner(): der legt den Ordner an, und der Trockenlauf schreibt nichts.
                    string ziel = Path.Combine(Sicherungsdatei.ExportOrdnerPfad() ?? "<sicherungen>", export);
                    k.Schreibe("(trocken) " + Pnputil() + " /export-driver " + inf + " \"" + ziel + "\"", "dim");
                }
                else if (!Exportieren(k, inf, export))
                {
                    return;
                }
            }
            else if (treiber != null)
            {
                k.Schreibe("   " + inf + " gehört zu Windows (kein oemNN.inf); der Treiber bleibt im Treiberspeicher, ein Export ist nicht nötig.", "dim");
            }

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("instanzId", id),
                Sicherungsdatei.Werte("inf", inf, "export", export, "name", name, "code", stand.Code.ToString(CultureInfo.InvariantCulture)));
            k.Werte["treiber.inf"] = inf;
            k.Werte["treiber.export"] = export;
            LeistungMassnahmen.RueckwegAusgeschlossen(k, "Rückgängig gibt es hier nicht: " + (export.Length == 0
                ? "der Treiber liegt im Treiberspeicher von Windows und wird bei der Neuerkennung wieder eingerichtet."
                : "der bisherige Treiber liegt exportiert unter sicherungen\\massnahmen\\export\\" + export + " und lässt sich mit pnputil /add-driver " + inf + " /install von Hand einspielen."));

            // Werkzeuge.Lauf setzt neustart bei 3010 und raeumt den Schluessel nie: ein frueherer Schritt
            // desselben Plans (etwa volume.spotfix auf dem Windows-Laufwerk) kann ihn schon auf "1"
            // gesetzt haben. Nur ein Wechsel waehrend des /remove-device-Laufs zaehlt fuer dieses Geraet.
            string neustart;
            bool schonNeustart = k.Werte.TryGetValue("neustart", out neustart) && neustart == "1";
            string tail;
            int exit = Werkzeuge.Lauf(k, Pnputil(), "/remove-device \"" + id + "\"", EntfernenMs, null, false, out tail);
            if (k.Trocken)
            {
                Werkzeuge.Lauf(k, Pnputil(), "/scan-devices", ScanMs, null, false, out tail);
                Nachweise.Trocken(k, kennung, "„" + name + "“ würde entfernt und von Windows neu erkannt (pnputil /remove-device, /scan-devices); vorher " + (export.Length == 0 ? "kein Export nötig" : "Export des Treibers " + inf) + ".");
                return;
            }
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "pnputil /remove-device meldete Exit " + exit + "; „" + name + "“ bleibt mit Problemcode " + stand.Code + ".");
                return;
            }
            bool neustartNoetig = !schonNeustart && k.Werte.TryGetValue("neustart", out neustart) && neustart == "1";
            exit = Werkzeuge.Lauf(k, Pnputil(), "/scan-devices", ScanMs, null, false, out tail);
            if (exit != 0)
            {
                k.Werte["neustart"] = "1";
                Nachweise.NichtBestanden(k, kennung, "„" + name + "“ wurde entfernt, aber pnputil /scan-devices meldete Exit " + exit + "; Windows erkennt das Gerät erst beim nächsten Start neu.");
                return;
            }
            GeraetStand nach = Nachlesen(k, id);
            if (nach.Gefunden && nach.Code == 0)
                Nachweise.Bestanden(k, kennung, "„" + name + "“ läuft wieder (Problemcode 0, vorher " + stand.Code + ")" + (export.Length == 0 ? "" : "; der bisherige Treiber liegt unter export\\" + export) + (neustartNoetig ? "; vollständig wirksam nach 1 Neustart" : "") + ".");
            else if (neustartNoetig)
                Nachweise.Bestanden(k, kennung, "Windows hat „" + name + "“ entfernt und neu erkannt, die Einrichtung endet mit 1 Neustart; bis dahin meldet es Code " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + ".");
            else if (!nach.Gefunden)
                Nachweise.NichtBestanden(k, kennung, "Nach der Neuerkennung ist „" + name + "“ nicht mehr vorhanden (Instanz-Id nicht gefunden); ein Steckgerät neu anstecken oder den PC neu starten.");
            else
                Nachweise.NichtBestanden(k, kennung, "„" + name + "“ meldet nach der Neuinstallation weiter Problemcode " + nach.Code + " (vorher " + stand.Code + ").");
        }

        /// <summary>
        /// pnputil /export-driver in den frisch angelegten Ordner unter Sicherungsdatei.ExportOrdner();
        /// danach Exit 0, Ordner nicht leer und Sicherungsdatei.ExportSchuetzen (Besitzer, DACL).
        /// false = Zeile bad, Problem gesetzt, nichts weiter tun (der Eingriff kommt erst danach).
        /// </summary>
        static bool Exportieren(Ausfuehrungskontext k, string inf, string export)
        {
            string basis;
            try { basis = Sicherungsdatei.ExportOrdner(); }
            catch (Exception ex)
            {
                k.Schreibe("   Der Exportordner unter den Sicherungen ließ sich nicht anlegen (" + Massnahmenbau.Kurz(ex.Message) + "); ohne Export wird der Treiber nicht entfernt.", "bad");
                Katalog.Problem(k);
                return false;
            }
            if (string.IsNullOrEmpty(basis))
            {
                k.Schreibe("   Der Sicherungsordner liegt hinter einer Verknüpfung auf einen anderen Ort; ohne Export wird der Treiber nicht entfernt.", "bad");
                Katalog.Problem(k);
                return false;
            }
            string ziel = Path.Combine(basis, export);
            string tail;
            Directory.CreateDirectory(ziel);
            int exit = Werkzeuge.Lauf(k, Pnputil(), "/export-driver " + inf + " \"" + ziel + "\"", ExportMs, null, false, out tail);
            bool inhalt = false;
            try { inhalt = Directory.Exists(ziel) && Directory.EnumerateFileSystemEntries(ziel).GetEnumerator().MoveNext(); }
            catch (Exception ex) { AppLog.Warn("Exportordner " + ziel + " nicht lesbar: " + ex.Message); }
            if (exit != 0 || !inhalt)
            {
                ExportEntfernen(ziel);
                k.Schreibe("   Der Export des Treibers " + inf + " scheiterte (Exit " + exit + (inhalt ? "" : ", Ordner leer") + "); ohne Export wird der Treiber nicht entfernt.", "bad");
                Katalog.Problem(k);
                return false;
            }
            // Besitzer und DACL am Handle; scheitert das, gilt der Export als nicht angelegt: der
            // ungeschuetzte Ordner wird entfernt, und die Massnahme bricht vor dem Eingriff ab.
            try { Sicherungsdatei.ExportSchuetzen(ziel); }
            catch (InvalidOperationException ex)
            {
                ExportEntfernen(ziel);
                k.Schreibe("   Der Exportordner ließ sich nicht schützen (" + Massnahmenbau.Kurz(ex.Message) + "); der Export wurde entfernt, der Treiber wird nicht entfernt.", "bad");
                Katalog.Problem(k);
                return false;
            }
            k.Schreibe("   Treiber " + inf + " exportiert nach sicherungen\\massnahmen\\export\\" + export + ".", "good");
            return true;
        }

        // Einen gescheiterten oder ungeschuetzten Export wegraeumen; scheitert das, bleibt er liegen (app.log).
        static void ExportEntfernen(string ziel)
        {
            try { if (Directory.Exists(ziel)) Directory.Delete(ziel, true); }
            catch (Exception ex) { AppLog.Warn("Exportordner " + ziel + " nicht entfernt: " + ex.Message); }
        }

        /// <summary>Nach /scan-devices bis 30 s alle 2 s lesen, bis das Geraet mit Code 0 da ist; Abbruch beendet das Warten.</summary>
        static GeraetStand Nachlesen(Ausfuehrungskontext k, string id)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            GeraetStand nach = Lesen(id);
            while (!(nach.Gefunden && nach.Code == 0) && sw.ElapsedMilliseconds < NachleseMs && !k.IstAbgebrochen)
            {
                Thread.Sleep(2000);
                nach = Lesen(id);
            }
            return nach;
        }
    }
}
