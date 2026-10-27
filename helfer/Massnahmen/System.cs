using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // System (docs/M4-ENTWURF.md, Abschnitte 3.1 und 3.4): tiefenpruefung, dateien.reparieren und
        // wiederherstellungspunkt.zurueck sind M2-Kennungen und bleiben in helfer\Katalog.cs angemeldet;
        // Katalog.cs ruft dafuer SystemMassnahmen. Hier gibt es deshalb nichts anzumelden; die
        // Teilmethode steht, damit Alle() fuer jede Datei dieselbe Aufrufstelle hat.
        static partial void System(List<Massnahme> l)
        {
        }
    }

    /// <summary>
    /// Die Systemmassnahmen des Helfers (Vertrag M4, Abschnitt 3.4): Tiefenpruefung und Reparatur
    /// der Windows-Dateien ueber die DISM-API (kein Konsolentext des DISM-Werkzeugs mehr) und sfc.exe (Prozess ueber
    /// Werkzeuge.Lauf, Zustand aus den [SR]-Zeilen der CBS.log), dazu das Zuruecksetzen auf einen
    /// Wiederherstellungspunkt ueber WMI (root\default SystemRestore.Restore, danach
    /// Win32_OperatingSystem.Win32Shutdown(6)); kein PowerShell, kein eigener Prozessstart.
    ///
    /// Werte fuer den Host: dism.zustand (healthy|repairable|nonrepairable|fehler), dism.hresult (hex),
    /// dism.dauerS, bei der Reparatur dism.repariert ("1", wenn RestoreHealth 0 lieferte und der
    /// folgende CheckImageHealth healthy meldet), sfc.exit, sfc.zustand (ohneBefund|repariert|
    /// nichtRepariert|fehler), neustart = "1" bei sfc-Exit 3010 oder einer [SR]-Zeile mit "pending".
    /// dism.exit und dism.ausgabe bleiben als Uebergang fuer den Host von 8.2 stehen (englische
    /// Ergebniszeile, die Explain.ForOutput kennt); der Host von 8.3 liest dism.zustand.
    ///
    /// Trockenlauf: keine DISM-Initialisierung, kein sfc.exe, keine Wiederherstellung; nur
    /// "(trocken) ..."-Zeilen, die Folgenummer wird gegen die Liste der Punkte geprueft.
    /// </summary>
    internal static class SystemMassnahmen
    {
        // Zeitgrenzen wie bisher in helfer/Katalog.cs (und davor host/CheckFlow.cs).
        const int DismScanMs = 20 * 60 * 1000;
        const int SfcVerifyMs = 30 * 60 * 1000;
        const int DismRestoreMs = 40 * 60 * 1000;
        const int SfcScanMs = 40 * 60 * 1000;
        const int RestoreComputerMs = 10 * 60 * 1000;
        const int ShutdownMs = 60 * 1000;

        const string KennungReparatur = "dateien.reparieren";
        const string KennungZurueck = "wiederherstellungspunkt.zurueck";

        // ================================================================ tiefenpruefung

        public static void Tiefenpruefung(PlanSchritt s, Ausfuehrungskontext k)
        {
            k.Melde(1, 2, "Die Grundbestandteile von Windows werden geprüft");
            DismApi.Ergebnis dism = DismApi.Pruefen(k, DismScanMs);
            DismWerte(k, dism, false);
            if (k.IstAbgebrochen) return;
            if (dism.Hresult != 0) Katalog.Problem(k);

            k.Melde(2, 2, "Die Dateien von Windows werden auf Beschädigungen geprüft");
            Sfc(k, "/verifyonly", SfcVerifyMs);
        }

        // ================================================================ dateien.reparieren

        public static void DateienReparieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            // Sicherungspunkt zuerst (Grundsatz 2); Erfolg an der Folgenummer gemessen, nicht am
            // Rueckgabewert (Katalog.Wiederherstellungspunkt). Die 24-Stunden-Drossel bleibt hier
            // bewusst stehen, deshalb passt der zweite der drei Saetze.
            k.Melde(1, 4, "Ein Sicherungspunkt wird angelegt");
            Katalog.Wiederherstellungspunkt(k, "Vor der Wartung", false, "die Reparatur läuft ohne Rückweg über einen Punkt");
            if (k.IstAbgebrochen) return;

            k.Melde(2, 4, "Fehlende Windows-Bausteine werden neu geholt");
            DismApi.Ergebnis dism = DismApi.Reparieren(k, DismRestoreMs);
            DismWerte(k, dism, true);
            if (k.IstAbgebrochen) return;

            k.Melde(3, 4, "Beschädigte Windows-Dateien werden ersetzt");
            SfcStand sfc = Sfc(k, "/scannow", SfcScanMs);
            if (k.IstAbgebrochen) return;
            // Schritt 4 von 4 ("Das Ergebnis wird zusammengestellt") ist die Messung im Host.

            if (k.Trocken)
            {
                Nachweise.Trocken(k, KennungReparatur, "DISM würde den Komponentenspeicher über die DISM-API reparieren und SFC die Windows-Dateien ersetzen; der Nachweis kommt aus dism.zustand und sfc.zustand.");
                return;
            }
            bool dismGut = dism.Hresult == 0 && dism.Zustand == DismApi.Healthy;
            bool sfcGut = sfc.Exit == 0 && (sfc.Zustand == "ohneBefund" || sfc.Zustand == "repariert");
            string dismSatz = DismSatz(dism, true);
            string sfcSatz = SfcSatz(sfc);
            if (dismGut && sfcGut)
                Nachweise.Bestanden(k, KennungReparatur, dismSatz + " " + sfcSatz);
            else
                Nachweise.NichtBestanden(k, KennungReparatur, dismSatz + " " + sfcSatz);
        }

        // ---------------------------------------------------------------- DISM-Werte und Saetze

        static void DismWerte(Ausfuehrungskontext k, DismApi.Ergebnis e, bool reparatur)
        {
            if (k.Trocken) return;
            string zustand = e.Hresult != 0 || e.Zustand < 0 ? "fehler" : DismApi.ZustandWort(e.Zustand);
            k.Werte["dism.zustand"] = zustand;
            k.Werte["dism.hresult"] = "0x" + e.Hresult.ToString("X8");
            k.Werte["dism.dauerS"] = ((int)e.Sekunden).ToString(CultureInfo.InvariantCulture);
            if (reparatur) k.Werte["dism.repariert"] = e.Hresult == 0 && e.Repariert && e.Zustand == DismApi.Healthy ? "1" : "0";
            // Uebergang fuer den Host von 8.2 (EvaluateFiles liest dism.exit und dism.ausgabe):
            // eine englische Ergebniszeile, die src/Explain.cs kennt, kein Konsolentext.
            k.Werte["dism.exit"] = e.Hresult.ToString(CultureInfo.InvariantCulture);
            k.Werte["dism.ausgabe"] = DismAusgabe(e, reparatur);
        }

        static string DismAusgabe(DismApi.Ergebnis e, bool reparatur)
        {
            if (e.Hresult != 0) return "Error: 0x" + e.Hresult.ToString("X8") + " " + (e.Meldung ?? "");
            var sb = new StringBuilder();
            if (reparatur && e.Repariert) sb.Append("The restore operation completed successfully.").Append('\n');
            switch (e.Zustand)
            {
                case DismApi.Healthy: sb.Append("No component store corruption detected."); break;
                case DismApi.Repairable: sb.Append("The component store is repairable."); break;
                case DismApi.NonRepairable: sb.Append("The component store is not repairable."); break;
                default: sb.Append("No health state reported."); break;
            }
            return sb.ToString();
        }

        static string DismSatz(DismApi.Ergebnis e, bool reparatur)
        {
            if (e.Abgebrochen) return "DISM wurde abgebrochen.";
            if (e.Hresult != 0)
                return "DISM meldete den Fehler 0x" + e.Hresult.ToString("X8") + (string.IsNullOrEmpty(e.Meldung) ? "" : " (" + Massnahmenbau.Kurz(e.Meldung) + ")") + ".";
            switch (e.Zustand)
            {
                case DismApi.Healthy:
                    return reparatur && e.Repariert
                        ? "DISM hat den Komponentenspeicher repariert; die Prüfung danach meldet keine Beschädigung."
                        : "DISM meldet keine Beschädigung im Komponentenspeicher.";
                case DismApi.Repairable:
                    return "DISM meldet nach " + ((int)e.Sekunden) + " Sekunden weiterhin reparierbare Beschädigungen im Komponentenspeicher.";
                case DismApi.NonRepairable:
                    return "DISM meldet Beschädigungen im Komponentenspeicher, die sich nicht reparieren lassen.";
                default:
                    return "DISM lieferte keinen Zustand des Komponentenspeichers.";
            }
        }

        // ---------------------------------------------------------------- SFC

        sealed class SfcStand
        {
            public int Exit;
            public string Zustand;     // ohneBefund | repariert | nichtRepariert | fehler
            public int Zeilen;         // [SR]-Zeilen des eigenen Laufs
            public int NichtRepariert; // Zeilen "Cannot repair" / "Could not reproject"
            public bool Pending;
        }

        /// <summary>
        /// sfc.exe aus System32 ueber Werkzeuge.Lauf (Encoding.Unicode, Fortschritt), danach der
        /// Zustand aus den [SR]-Zeilen der CBS.log ab Startzeit (englisch, unabhaengig von der
        /// Konsolensprache). Werte sfc.exit, sfc.ausgabe (Uebergang), sfc.zustand; neustart = "1"
        /// setzt Werkzeuge.Lauf bei 3010, hier zusaetzlich bei einer [SR]-Zeile mit "pending".
        /// </summary>
        static SfcStand Sfc(Ausfuehrungskontext k, string args, int zeitMs)
        {
            DateTime start = DateTime.Now;
            string tail;
            int exit = Werkzeuge.Lauf(k, Path.Combine(Environment.SystemDirectory, "sfc.exe"), args, zeitMs, Encoding.Unicode, true, out tail);
            k.Werte["sfc.exit"] = exit.ToString(CultureInfo.InvariantCulture);
            k.Werte["sfc.ausgabe"] = tail ?? "";
            var stand = new SfcStand { Exit = exit, Zustand = "fehler" };
            if (k.Trocken) return stand;

            if (exit >= 0)
            {
                SfcAuswerten(start, stand);
                if (stand.Pending) k.Werte["neustart"] = "1";
            }
            k.Werte["sfc.zustand"] = stand.Zustand;
            k.Schreibe("   SFC: " + SfcSatz(stand), stand.Zustand == "ohneBefund" || stand.Zustand == "repariert" ? "good" : "warn");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, "sfc", "SFC-Zustand aus CBS.log: " + stand.Zustand,
                    new { exit, zustand = stand.Zustand, zeilen = stand.Zeilen, nichtRepariert = stand.NichtRepariert, pending = stand.Pending, ab = start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });
            return stand;
        }

        static string SfcSatz(SfcStand s)
        {
            switch (s.Zustand)
            {
                case "ohneBefund": return "SFC fand in " + s.Zeilen + " Protokollzeilen keine beschädigte Windows-Datei.";
                case "repariert": return "SFC hat beschädigte Windows-Dateien ersetzt" + (s.Pending ? "; der Abschluss braucht 1 Neustart." : ".");
                case "nichtRepariert": return "SFC konnte " + s.NichtRepariert + " Datei(en) nicht reparieren (CBS.log, Zeilen mit „Cannot repair“).";
                default:
                    return s.Exit < 0
                        ? "SFC lief nicht bis zum Ende (Exit " + s.Exit + ")."
                        : "SFC lieferte kein lesbares Ergebnis (Exit " + s.Exit + ", " + s.Zeilen + " [SR]-Zeilen ab Startzeit in CBS.log).";
            }
        }

        static readonly Regex SrZeitRx = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}), .*\[SR\] (.*)$", RegexOptions.Compiled);
        static readonly Regex SrRepairingRx = new Regex(@"^Repairing (\d+) components", RegexOptions.Compiled);

        /// <summary>
        /// Liest %SystemRoot%\Logs\CBS\CBS.log (UTF-8, geteilt mit TrustedInstaller) und wertet nur
        /// die [SR]-Zeilen mit Zeitstempel ab Startzeit (2 s Spielraum) aus. Muster (englisch, so
        /// schreibt CSI sie unabhaengig von der Anzeigesprache): "Cannot repair member file" oder
        /// "Could not reproject" → nichtRepariert; "Repairing corrupted file" oder "Repairing N components"
        /// mit N > 0 → repariert; sonst "Verify complete" → ohneBefund; keine Zeile oder Datei nicht
        /// lesbar → fehler (ein nicht lesbarer Wert ist kein bestandener Nachweis).
        /// </summary>
        static void SfcAuswerten(DateTime start, SfcStand stand)
        {
            DateTime ab = start.AddSeconds(-2);
            string pfad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS", "CBS.log");
            bool verify = false, repariert = false;
            try
            {
                using (var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string zeile;
                    while ((zeile = r.ReadLine()) != null)
                    {
                        if (zeile.IndexOf("[SR]", StringComparison.Ordinal) < 0) continue;
                        Match m = SrZeitRx.Match(zeile);
                        if (!m.Success) continue;
                        DateTime zeit;
                        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out zeit)) continue;
                        if (zeit < ab) continue;
                        stand.Zeilen++;
                        string text = m.Groups[2].Value;
                        if (text.StartsWith("Cannot repair", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Could not reproject", StringComparison.OrdinalIgnoreCase))
                            stand.NichtRepariert++;
                        else if (text.StartsWith("Repairing corrupted file", StringComparison.OrdinalIgnoreCase))
                            repariert = true;
                        else
                        {
                            Match rep = SrRepairingRx.Match(text);
                            int n;
                            if (rep.Success && int.TryParse(rep.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0) repariert = true;
                        }
                        if (text.StartsWith("Verify complete", StringComparison.OrdinalIgnoreCase)) verify = true;
                        if (text.IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0) stand.Pending = true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("CBS.log nicht lesbar, der SFC-Zustand gilt als fehler: " + ex.Message);
                stand.Zustand = "fehler";
                return;
            }
            if (stand.NichtRepariert > 0) stand.Zustand = "nichtRepariert";
            else if (repariert) stand.Zustand = "repariert";
            else if (verify) stand.Zustand = "ohneBefund";
            else stand.Zustand = "fehler";
        }

        // ================================================================ wiederherstellungspunkt.zurueck

        /// <summary>
        /// WMI root\default SystemRestore.Restore(folge) (statische Methode, Rueckgabe 0), danach
        /// Win32_OperatingSystem.Win32Shutdown(6) (Neustart erzwingen) mit SeShutdownPrivilege ueber
        /// Sicherungsdatei.Privileg.Einschalten und EnablePrivileges an der WMI-Verbindung. Die
        /// Folgenummer ist eine reine Zahl (Katalog.Pruefen) und muss in der Liste der Punkte stehen,
        /// sonst Zeile bad und Problem. Trockenlauf: nur die Liste lesen und die Nummer pruefen.
        /// </summary>
        public static void WiederherstellungspunktZurueck(PlanSchritt s, Ausfuehrungskontext k)
        {
            int folge;
            if (!Schritte.GanzeZahl(s.Wert("folge"), 1, int.MaxValue, out folge))
                throw new InvalidOperationException("Die Folgenummer ist nach der Prüfung nicht mehr gültig.");

            string beschreibung;
            bool lesbar;
            bool vorhanden = PunktVorhanden(folge, out beschreibung, out lesbar);
            if (!lesbar)
            {
                k.Schreibe("   Die Liste der Wiederherstellungspunkte ließ sich nicht lesen; Windows wird nicht auf den Stand Nr. " + folge + " zurückgesetzt.", "bad");
                Katalog.Problem(k);
                return;
            }
            if (!vorhanden)
            {
                k.Schreibe("   Es gibt keinen Wiederherstellungspunkt mit der Nr. " + folge + "; Windows wird nicht zurückgesetzt.", "bad");
                Katalog.Problem(k);
                return;
            }
            k.Schreibe("   Wiederherstellungspunkt Nr. " + folge + " „" + beschreibung + "“ ist vorhanden.", "dim");

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI SystemRestore.Restore(" + folge + ")", "dim");
                k.Schreibe("(trocken) WMI Win32_OperatingSystem.Win32Shutdown(6)", "dim");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, KennungZurueck, "(trocken) Windows würde auf den Stand Nr. " + folge + " zurückgesetzt und neu gestartet", new { folge, beschreibung, trocken = true });
                Nachweise.Trocken(k, KennungZurueck, "Windows würde auf den Stand Nr. " + folge + " „" + beschreibung + "“ zurückgesetzt und dafür neu gestartet.");
                return;
            }

            long? rc;
            try { rc = Wmi2.Ganz(Wmi2.StatischMitZeit(RestoreComputerMs, @"root\default", "SystemRestore", "Restore", (uint)folge)); }
            catch (Exception ex)
            {
                k.Schreibe("   Windows wurde nicht auf den Stand Nr. " + folge + " zurückgesetzt: " + ex.Message, "bad");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, KennungZurueck, "SystemRestore.Restore scheiterte: " + ex.Message, new { folge });
                Katalog.Problem(k);
                return;
            }
            if (rc != 0)
            {
                k.Schreibe("   Windows wurde nicht auf den Stand Nr. " + folge + " zurückgesetzt (SystemRestore.Restore meldete " + (rc.HasValue ? rc.Value.ToString(CultureInfo.InvariantCulture) : "keinen Rückgabewert") + ").", "bad");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, KennungZurueck, "SystemRestore.Restore lehnte ab", new { folge, rc });
                Katalog.Problem(k);
                return;
            }
            k.Werte["neustart"] = "1";
            k.Schreibe("   Wiederherstellung auf den Stand Nr. " + folge + " vorgemerkt; Windows führt sie beim Neustart aus.", "good");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, KennungZurueck, "Wiederherstellung auf Nr. " + folge + " vorgemerkt, Neustart folgt", new { folge, beschreibung });

            // Neustart erzwingen: 6 = EWX_REBOOT | EWX_FORCE. Das Privileg muss im eigenen Token an
            // sein (Privileg.Einschalten) und an der WMI-Verbindung mitgehen (EnablePrivileges).
            long? shutdown = null;
            string fehler = null;
            try
            {
                using (Sicherungsdatei.Privileg.Einschalten("SeShutdownPrivilege"))
                    shutdown = Win32Shutdown(6);
            }
            catch (Exception ex) { fehler = ex.Message; }
            if (shutdown == 0)
            {
                k.Schreibe("   Der Neustart ist ausgelöst; der PC startet in wenigen Sekunden neu.", "good");
                return;
            }
            // Die Wiederherstellung ist vorgemerkt und laeuft beim naechsten Start; nur der Neustart
            // fehlt. Kein Problem-Signal: der Nutzer startet selbst neu, die Zeile sagt es.
            k.Schreibe("   Der Neustart ließ sich nicht auslösen (" + (fehler ?? ("Win32Shutdown meldete " + (shutdown.HasValue ? shutdown.Value.ToString(CultureInfo.InvariantCulture) : "keinen Rückgabewert"))) + "); bitte den PC selbst neu starten, dann läuft die Wiederherstellung.", "warn");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, KennungZurueck, "Neustart nicht ausgelöst, Wiederherstellung bleibt vorgemerkt", new { folge, shutdown, fehler });
        }

        /// <summary>true, wenn root\default SystemRestore einen Punkt mit dieser Folgenummer fuehrt; lesbar = false, wenn die Liste nicht lesbar war.</summary>
        static bool PunktVorhanden(int folge, out string beschreibung, out bool lesbar)
        {
            beschreibung = "";
            lesbar = false;
            try
            {
                foreach (ManagementObject mo in Sammler.Quellen.Wmi.Abfrage(@"root\default", "SELECT SequenceNumber, Description FROM SystemRestore", 10000))
                {
                    using (mo)
                    {
                        int? n = Sammler.Quellen.Wmi.Ganz(mo, "SequenceNumber");
                        if (n.HasValue && n.Value == folge)
                        {
                            beschreibung = Massnahmenbau.Kurz(Sammler.Quellen.Wmi.Str(mo, "Description") ?? "");
                            lesbar = true;
                            return true;
                        }
                    }
                }
                lesbar = true;
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Warn("SystemRestore nicht lesbar: " + ex.Message);
                return false;
            }
        }

        /// <summary>Win32_OperatingSystem.Win32Shutdown(flags) mit EnablePrivileges; Rueckgabe der ReturnValue (0 = angenommen).</summary>
        static long? Win32Shutdown(int flags)
        {
            return Zeitgrenze.Mit(() =>
            {
                var optionen = new ConnectionOptions { EnablePrivileges = true };
                var scope = new ManagementScope(@"\\.\root\cimv2", optionen);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_OperatingSystem")))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementObject mo in rows)
                    {
                        using (mo)
                        using (ManagementBaseObject rein = mo.GetMethodParameters("Win32Shutdown"))
                        {
                            rein["Flags"] = flags;
                            using (ManagementBaseObject raus = mo.InvokeMethod("Win32Shutdown", rein, null))
                                return Wmi2.Ganz(raus == null ? null : raus["ReturnValue"]);
                        }
                    }
                }
                throw new InvalidOperationException("Win32_OperatingSystem lieferte keine Instanz.");
            }, ShutdownMs, "WMI Win32_OperatingSystem.Win32Shutdown");
        }
    }

    /// <summary>
    /// DISM ueber die Windows-Schnittstelle (dismapi.dll aus System32, x64-Prozess, Unicode), Vertrag
    /// M4 Abschnitt 3.4: DismInitialize und DismShutdown je Lauf unter einer prozessweiten Sperre
    /// (DISM erlaubt eine Initialisierung je Prozess), Sitzung auf dem laufenden Windows
    /// (DISM_ONLINE_IMAGE), CheckImageHealth(scanImage) und RestoreImageHealth mit Abbruch-Ereignis
    /// (CreateEvent; ein Waechter-Thread setzt es bei Abbruch oder abgelaufener Zeitgrenze) und
    /// Fortschritt ueber den Rueckruf (als statisches Feld gehalten, damit der GC ihn nicht
    /// einsammelt). HRESULT ungleich 0 → Zeile bad mit DismGetLastErrorMessage (lokalisiert, nur
    /// Anzeige). Protokoll: &lt;Laufzeitordner&gt;\protokoll\dism-&lt;datum&gt;.log, vom Helfer vor
    /// DismInitialize selbst angelegt (LogPfad: nie eine vorhandene Datei, nie eine Verknuepfung).
    /// Die Bibliothek wird vorher aus Environment.SystemDirectory geladen (LoadLibrary mit vollem
    /// Pfad); scheitert das, laeuft nichts (kein Suchpfad, keine fremde dismapi.dll).
    /// </summary>
    internal static class DismApi
    {
        public const string OnlineImage = "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}";
        public const int Healthy = 0, Repairable = 1, NonRepairable = 2;   // DismImageHealthState
        const int LogErrorsWarnings = 1;                                    // DismLogLevel

        public sealed class Ergebnis
        {
            public int Hresult;         // 0 = alle Aufrufe angenommen
            public int Zustand = -1;    // Healthy | Repairable | NonRepairable, -1 = keiner
            public bool Repariert;      // RestoreImageHealth lieferte 0
            public string Meldung;      // DismGetLastErrorMessage zum ersten Fehler
            public double Sekunden;
            public bool Abgebrochen;
            public string Schritt;      // der Aufruf, der scheiterte
        }

        public static string ZustandWort(int zustand)
        {
            switch (zustand)
            {
                case Healthy: return "healthy";
                case Repairable: return "repairable";
                case NonRepairable: return "nonrepairable";
                default: return "fehler";
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void ProgressCallback(uint current, uint total, IntPtr userData);

        [DllImport("dismapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int DismInitialize(int logLevel, string logFilePath, string scratchDirectory);

        [DllImport("dismapi.dll", ExactSpelling = true)]
        static extern int DismShutdown();

        [DllImport("dismapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int DismOpenSession(string imagePath, string windowsDirectory, string systemDrive, out uint session);

        [DllImport("dismapi.dll", ExactSpelling = true)]
        static extern int DismCloseSession(uint session);

        [DllImport("dismapi.dll", ExactSpelling = true)]
        static extern int DismCheckImageHealth(uint session, [MarshalAs(UnmanagedType.Bool)] bool scanImage, IntPtr cancelEvent,
                                               ProgressCallback progress, IntPtr userData, out int imageHealth);

        [DllImport("dismapi.dll", ExactSpelling = true)]
        static extern int DismRestoreImageHealth(uint session, IntPtr sourcePaths, uint sourcePathCount, [MarshalAs(UnmanagedType.Bool)] bool limitAccess,
                                                 IntPtr cancelEvent, ProgressCallback progress, IntPtr userData);

        [DllImport("dismapi.dll", ExactSpelling = true)]
        static extern int DismGetLastErrorMessage(out IntPtr errorMessage);

        [DllImport("dismapi.dll", ExactSpelling = true)]
        static extern int DismDelete(IntPtr dismStructure);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr CreateEventW(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string name);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetEvent(IntPtr handle);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr handle);

        // Eine Initialisierung je Prozess: die Sperre haelt den ganzen Lauf, der Rueckruf lebt als
        // Feld (Vertrag 3.4), der Kontext des laufenden Aufrufs fuer die Fortschrittszeilen ebenso.
        static readonly object _sperre = new object();
        static readonly ProgressCallback _rueckruf = Fortschritt;
        static Ausfuehrungskontext _aktuell;
        static int _letztesProzent = -1;
        static IntPtr _bibliothek = IntPtr.Zero;

        /// <summary>CheckImageHealth(scanImage = true) ueber der laufenden Installation.</summary>
        public static Ergebnis Pruefen(Ausfuehrungskontext k, int zeitMs)
        {
            if (k.Trocken)
            {
                Trockenzeile(k, "DISM-API CheckImageHealth(scan)");
                return new Ergebnis();
            }
            return Lauf(k, false, zeitMs);
        }

        /// <summary>RestoreImageHealth (Quellen: Windows Update), danach CheckImageHealth(scan) fuer dism.zustand.</summary>
        public static Ergebnis Reparieren(Ausfuehrungskontext k, int zeitMs)
        {
            if (k.Trocken)
            {
                Trockenzeile(k, "DISM-API RestoreHealth");
                Trockenzeile(k, "DISM-API CheckImageHealth(scan)");
                return new Ergebnis();
            }
            return Lauf(k, true, zeitMs);
        }

        static void Trockenzeile(Ausfuehrungskontext k, string was)
        {
            k.Schreibe("(trocken) " + was, "dim");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, "dism", "(trocken) " + was + " ist beendet", new { exit = 0, sekunden = 0, trocken = true });
        }

        static Ergebnis Lauf(Ausfuehrungskontext k, bool reparieren, int zeitMs)
        {
            var e = new Ergebnis();
            var sw = Stopwatch.StartNew();
            lock (_sperre)
            {
                _aktuell = k;
                _letztesProzent = -1;
                try
                {
                    k.Schreibe("›  DISM-API " + (reparieren ? "RestoreHealth" : "CheckImageHealth(scan)") + " (Online)", "dim");
                    if (!BibliothekLaden(e)) return Abschluss(k, e, sw, reparieren);

                    string log = LogPfad();
                    int hr = DismInitialize(LogErrorsWarnings, log, null);
                    if (hr != 0) { Fehler(e, hr, "DismInitialize"); return Abschluss(k, e, sw, reparieren); }
                    try
                    {
                        uint session;
                        hr = DismOpenSession(OnlineImage, null, null, out session);
                        if (hr != 0) { Fehler(e, hr, "DismOpenSession"); return Abschluss(k, e, sw, reparieren); }
                        try
                        {
                            IntPtr abbruch = CreateEventW(IntPtr.Zero, true, false, null);
                            if (abbruch == IntPtr.Zero)
                            {
                                Fehler(e, Marshal.GetHRForLastWin32Error(), "CreateEvent");
                                return Abschluss(k, e, sw, reparieren);
                            }
                            bool fertig = false;
                            var waechter = new Thread(() =>
                            {
                                // Setzt das Ereignis bei Abbruch oder abgelaufener Zeitgrenze; DISM
                                // bricht dann den laufenden Aufruf ab und liefert einen HRESULT.
                                while (!Volatile.Read(ref fertig))
                                {
                                    if (k.IstAbgebrochen || sw.ElapsedMilliseconds >= zeitMs) { SetEvent(abbruch); break; }
                                    Thread.Sleep(500);
                                }
                            }) { IsBackground = true, Name = "dism:waechter" };
                            waechter.Start();
                            try
                            {
                                if (reparieren)
                                {
                                    hr = DismRestoreImageHealth(session, IntPtr.Zero, 0, false, abbruch, _rueckruf, IntPtr.Zero);
                                    if (hr == 0) e.Repariert = true;
                                    else { Fehler(e, hr, "DismRestoreImageHealth"); }
                                }
                                if (hr == 0)
                                {
                                    int zustand;
                                    _letztesProzent = -1;
                                    hr = DismCheckImageHealth(session, true, abbruch, _rueckruf, IntPtr.Zero, out zustand);
                                    if (hr == 0) e.Zustand = zustand;
                                    else Fehler(e, hr, "DismCheckImageHealth");
                                }
                            }
                            finally
                            {
                                Volatile.Write(ref fertig, true);
                                waechter.Join(2000);
                                CloseHandle(abbruch);
                            }
                            if (k.IstAbgebrochen || (e.Hresult != 0 && sw.ElapsedMilliseconds >= zeitMs)) e.Abgebrochen = true;
                        }
                        finally { DismCloseSession(session); }
                    }
                    finally
                    {
                        int hrEnde = DismShutdown();
                        if (hrEnde != 0) AppLog.Warn("DismShutdown meldete 0x" + hrEnde.ToString("X8") + ".");
                    }
                }
                catch (Exception ex)
                {
                    // DllNotFound, EntryPointNotFound, Marshalling: kein Ergebnis, aber die Spur bleibt.
                    AppLog.Error("DISM-API", ex);
                    if (e.Hresult == 0) e.Hresult = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
                    e.Meldung = ex.Message;
                    e.Schritt = e.Schritt ?? "DISM-API";
                }
                finally { _aktuell = null; }
            }
            return Abschluss(k, e, sw, reparieren);
        }

        static Ergebnis Abschluss(Ausfuehrungskontext k, Ergebnis e, Stopwatch sw, bool reparieren)
        {
            e.Sekunden = sw.Elapsed.TotalSeconds;
            int sek = (int)e.Sekunden;
            if (e.Abgebrochen)
                k.Schreibe("   ↳ DISM abgebrochen nach " + sek + " Sekunden (Zeitgrenze oder Abbruch); der Aufruf zählt als fehlgeschlagen.", "bad");
            else if (e.Hresult != 0)
                k.Schreibe("   ↳ " + (e.Schritt ?? "DISM") + " meldete 0x" + e.Hresult.ToString("X8") + (string.IsNullOrEmpty(e.Meldung) ? "" : ": " + Massnahmenbau.Kurz(e.Meldung)), "bad");
            else
            {
                string wort = ZustandWort(e.Zustand);
                k.Schreibe("   ↳ DISM " + (reparieren && e.Repariert ? "RestoreHealth abgeschlossen, " : "") + "Zustand " + wort + " nach " + sek + " Sekunden.",
                    e.Zustand == Healthy ? "good" : "warn");
            }
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, "dism",
                    "DISM-API " + (reparieren ? "RestoreHealth" : "CheckImageHealth") + " ist beendet",
                    new { exit = e.Hresult, hresult = "0x" + e.Hresult.ToString("X8"), zustand = ZustandWort(e.Zustand), repariert = e.Repariert, sekunden = sek, abgebrochen = e.Abgebrochen, schritt = e.Schritt, trocken = false });
            return e;
        }

        static void Fehler(Ergebnis e, int hr, string schritt)
        {
            if (e.Hresult != 0) return;   // der erste Fehler zaehlt
            e.Hresult = hr;
            e.Schritt = schritt;
            e.Meldung = LetzteMeldung();
        }

        /// <summary>DismGetLastErrorMessage: DismString* (erstes Feld PCWSTR), danach DismDelete. null, wenn keine Meldung lesbar ist.</summary>
        static string LetzteMeldung()
        {
            try
            {
                IntPtr p;
                int hr = DismGetLastErrorMessage(out p);
                if (hr != 0 || p == IntPtr.Zero) return null;
                try
                {
                    IntPtr text = Marshal.ReadIntPtr(p);
                    string s = text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text);
                    return string.IsNullOrEmpty(s) ? null : s.Trim();
                }
                finally { DismDelete(p); }
            }
            catch (Exception) { return null; }
        }

        static bool BibliothekLaden(Ergebnis e)
        {
            if (_bibliothek != IntPtr.Zero) return true;
            string pfad = Path.Combine(Environment.SystemDirectory, "dismapi.dll");
            IntPtr h = LoadLibraryW(pfad);
            if (h == IntPtr.Zero)
            {
                Fehler(e, Marshal.GetHRForLastWin32Error(), "LoadLibrary");
                e.Meldung = "dismapi.dll aus " + Environment.SystemDirectory + " ließ sich nicht laden (Win32-Fehler " + Marshal.GetLastWin32Error() + ").";
                return false;
            }
            _bibliothek = h;   // bleibt geladen; die P/Invoke-Aufrufe finden das Modul ueber den Namen
            return true;
        }

        /// <summary>
        /// &lt;Laufzeitordner&gt;\protokoll\dism-&lt;datum&gt;.log, vor DismInitialize vom Helfer selbst
        /// angelegt. Der Protokollordner ist fuer Benutzer beschreibbar (Ablage.RechteSichern), und der
        /// Name ist auf die Sekunde vorhersagbar: eine vorher untergeschobene Verknuepfung unter diesem
        /// Namen liesse DISM als Administrator in deren Ziel schreiben. Deshalb CREATE_NEW mit
        /// FILE_FLAG_OPEN_REPARSE_POINT (gemessen 18.09.2026: eine Verknuepfung unter dem Namen gibt
        /// Fehler 80, ohne das Flag wuerde ihr Ziel angelegt), bei vergebenem Namen der naechste mit
        /// 4 Hex, eigene DACL (SYSTEM und Administratoren Vollzugriff, Benutzer Lesen, Vererbung aus)
        /// und Besitzer Administratoren, damit kein Benutzer die Datei danach ersetzt. Geht das nicht,
        /// bleibt es bei null: DISM schreibt dann in seine Vorgabe %windir%\Logs\DISM\dismapi.log
        /// (nur Administratoren), nie an einen Ort, den ein Benutzer gewaehlt hat.
        /// </summary>
        static string LogPfad()
        {
            try
            {
                string ordner = Protokoll.Ordner();
                if (string.IsNullOrEmpty(ordner)) return null;
                string stamm = "dism-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                byte[] sd = LogSicherheit().GetSecurityDescriptorBinaryForm();
                for (int versuch = 0; versuch < 8; versuch++)
                {
                    string pfad = Path.Combine(ordner, stamm + (versuch == 0 ? "" : "-" + Guid.NewGuid().ToString("N").Substring(0, 4)) + ".log");
                    int fehler;
                    if (LogAnlegen(pfad, sd, out fehler)) return pfad;
                    if (fehler != ERROR_FILE_EXISTS && fehler != ERROR_ALREADY_EXISTS)
                    {
                        AppLog.Warn("DISM-Protokoll " + pfad + " nicht angelegt (Win32-Fehler " + fehler + "); DISM schreibt in seine Vorgabe unter %windir%\\Logs\\DISM.");
                        return null;
                    }
                }
                AppLog.Warn("DISM-Protokoll unter " + ordner + " nicht angelegt (8 Namen vergeben); DISM schreibt in seine Vorgabe unter %windir%\\Logs\\DISM.");
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Warn("DISM-Protokoll nicht angelegt: " + ex.Message + "; DISM schreibt in seine Vorgabe unter %windir%\\Logs\\DISM.");
                return null;
            }
        }

        const uint GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, CREATE_NEW = 1;
        const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        const int ERROR_FILE_EXISTS = 80, ERROR_ALREADY_EXISTS = 183;

        [StructLayout(LayoutKind.Sequential)]
        struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, ref SECURITY_ATTRIBUTES security, uint disposition, uint flags, IntPtr template);

        /// <summary>SYSTEM und Administratoren Vollzugriff, Benutzer Lesen, Vererbung aus, Besitzer Administratoren.</summary>
        static FileSecurity LogSicherheit()
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var sec = new FileSecurity();
            sec.SetAccessRuleProtection(true, false);
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
            sec.SetOwner(admins);
            return sec;
        }

        /// <summary>Legt die Datei neu an (nie eine vorhandene oeffnen, nie einer Verknuepfung folgen) und schliesst sie sofort. false und der Win32-Fehler, wenn es nicht geht.</summary>
        static bool LogAnlegen(string pfad, byte[] sd, out int win32)
        {
            win32 = 0;
            GCHandle pin = GCHandle.Alloc(sd, GCHandleType.Pinned);
            try
            {
                var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), lpSecurityDescriptor = pin.AddrOfPinnedObject(), bInheritHandle = false };
                using (SafeFileHandle h = CreateFileW(pfad, GENERIC_WRITE, FILE_SHARE_READ, ref sa, CREATE_NEW, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
                {
                    if (h == null || h.IsInvalid) { win32 = Marshal.GetLastWin32Error(); return false; }
                    return true;
                }
            }
            finally { pin.Free(); }
        }

        // Fortschritt wie bei Werkzeuge.Starten: nur die Prozentzahl, kein Zeilentext.
        static void Fortschritt(uint current, uint total, IntPtr userData)
        {
            try
            {
                Ausfuehrungskontext k = _aktuell;
                if (k == null || total == 0) return;
                int pct = (int)Math.Min(100, current * 100UL / total);
                if (pct == _letztesProzent) return;
                _letztesProzent = pct;
                k.Schreibe("", "normal", pct);
            }
            catch (Exception) { }
        }
    }
}
