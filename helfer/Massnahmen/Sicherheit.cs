using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Sicherheits-Massnahmen des Meilensteins 3 (docs/M3-ENTWURF.md, Abschnitt 1 und 3):
    ///   sicherheit.signaturen.aktualisieren  Stufe 1  MSFT_MpSignature.Update(1) (root\Microsoft\Windows\Defender, 1 = Microsoft Update)
    ///   sicherheit.schnellscan               Stufe 1  MpCmdRun.exe -Scan -ScanType 1 (Pfad aus HKLM\SOFTWARE\Microsoft\Windows Defender\InstallLocation), 20 min
    ///   sicherheit.firewall.einschalten      Stufe 1  INetFwPolicy2.FirewallEnabled[profil] = true (COM HNetCfg.FwPolicy2, spaetes Binden); Sicherung des alten Werts
    ///   sicherheit.smartscreen.warn          Stufe 2  Registry Explorer\SmartScreenEnabled = Warn; Sicherung
    ///   sicherheit.uac.standard              Stufe 2  Policies\System EnableLUA 0 → 1 (Neustart) und ConsentPromptBehaviorAdmin 0 → 5; Sicherung beider Werte
    ///
    /// Kein lokalisierter Text wird gedeutet: Rueckgabewerte, Exit-Codes, Registry-Marken (Warn/Block/Off)
    /// und COM-Eigenschaften entscheiden. Kein Skriptprozess. Alle Registry-Zugriffe in der 64-Bit-Sicht
    /// (dieselbe wie sammler\Quellen\Sicherheitsquelle.cs, damit Bild und Eingriff dieselben Werte sehen).
    ///
    /// Trockenlauf: Vorbedingungen und Lesezugriffe laufen, jede Schreibstelle meldet "(trocken) ...",
    /// MpCmdRun startet nicht (Werkzeuge.Schritt), die Sicherungsdatei bleibt ungeschrieben, und
    /// Nachweise.Bestanden wird zur (trocken)-Zeile (Gemeinsam.cs).
    /// </summary>
    public static partial class MassnahmenM3
    {
        static partial void Sicherheit(List<Massnahme> l) { SicherheitMassnahmen.Eintragen(l); }

        /// <summary>true, wenn ZurueckSicherheit die Kennung der Sicherung kennt (firewall.einschalten, smartscreen.warn, uac.standard).</summary>
        public static bool ZurueckSicherheitBekannt(string kennung) { return SicherheitMassnahmen.ZurueckBekannt(kennung); }

        /// <summary>
        /// Prueft die Werte einer Sicherheits-Sicherung, bevor der Rueckweg sie anfasst (Vertrag 8.3):
        /// null = gueltig, sonst der Grund (Rueckweg.cs lehnt den Plan damit ab). Firewall:
        /// FirewallEnabled 0 oder 1, Profil und profil je 1, 2 oder 4 (gleich, wenn beide da sind);
        /// SmartScreen: SmartScreenEnabled nur Buchstaben, hoechstens 20, oder leer (= Wert loeschen);
        /// UAC: EnableLUA und ConsentPromptBehaviorAdmin je 0 bis 5 oder leer (= Wert loeschen); ein
        /// fehlender Schluessel bleibt beim Rueckweg unangetastet.
        /// </summary>
        public static string ZurueckSicherheitPruefen(Sicherung si) { return SicherheitMassnahmen.ZurueckPruefen(si); }

        /// <summary>
        /// Rueckweg einer Sicherheits-Sicherung (Kennung rueckweg, Datei ueber Sicherungsdatei.Lesen):
        /// Firewall-Profil auf den alten Zustand (FirewallEnabled 0 = aus), SmartScreenEnabled auf den
        /// alten Text (leer = Wert loeschen), EnableLUA und ConsentPromptBehaviorAdmin auf die alten
        /// Zahlen (leer = loeschen); neustart nur, wenn sich EnableLUA dabei aendert. Schreibt nur
        /// Werte, die ZurueckSicherheitPruefen angenommen hat. Schreibt Zeilen und Nachweiszeile selbst.
        /// true = zurueckgestellt, false = nicht (mit Problem); Trockenlauf nur die (trocken)-Zeile;
        /// fremde Kennung → ArgumentException.
        /// </summary>
        public static bool ZurueckSicherheit(Ausfuehrungskontext k, Sicherung si) { return SicherheitMassnahmen.Zurueck(k, si); }
    }

    /// <summary>
    /// Die Sicherheits-Massnahmen selbst. Eigene Klasse statt Teil der partiellen MassnahmenM3, damit
    /// private Hilfen (DefenderLesen, ProfilName, WertText ...) nicht mit gleichnamigen Hilfen anderer
    /// Bereichsdateien zusammenstossen; nach aussen zaehlen nur Eintragen, ZurueckBekannt, ZurueckPruefen und Zurueck.
    /// </summary>
    internal static class SicherheitMassnahmen
    {
        const string DefenderNs = @"root\Microsoft\Windows\Defender";
        const string DefenderSchluessel = @"SOFTWARE\Microsoft\Windows Defender";
        const string ExplorerSchluessel = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        const string SmartScreenRichtlinie = @"SOFTWARE\Policies\Microsoft\Windows\System";
        const string UacSchluessel = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        const string FirewallProgId = "HNetCfg.FwPolicy2";
        const int SignaturMs = 10 * 60 * 1000;
        const int SchnellscanMs = 20 * 60 * 1000;
        const int ComMs = 30000;

        public const string KSignaturen = "sicherheit.signaturen.aktualisieren";
        public const string KSchnellscan = "sicherheit.schnellscan";
        public const string KFirewall = "sicherheit.firewall.einschalten";
        public const string KSmartScreen = "sicherheit.smartscreen.warn";
        public const string KUac = "sicherheit.uac.standard";

        public static void Eintragen(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu(KSignaturen, "Virensignaturen aktualisieren", 1, s => null, SignaturenAktualisieren));
            l.Add(Massnahmenbau.Neu(KSchnellscan, "Schnellscan des Virenschutzes", 1, s => null, Schnellscan));
            l.Add(Massnahmenbau.Neu(KFirewall, "Firewall einschalten", 1, ProfilPruefen, FirewallEinschalten));
            l.Add(Massnahmenbau.Neu(KSmartScreen, "SmartScreen auf „Warnen“ stellen", 2, s => null, SmartScreenWarn));
            l.Add(Massnahmenbau.Neu(KUac, "Benutzerkontensteuerung auf Standard", 2, s => null, UacStandard));
        }

        // ------------------------------------------------------------------ Defender: Signaturen

        /// <summary>Was MSFT_MpComputerStatus ueber den Virenschutz sagt; null = Klasse fehlt (kein Defender).</summary>
        sealed class DefenderStand
        {
            public bool DienstAn;
            public int SignaturAlterTage = -1;
            public int SchnellscanAlterTage = -1;
            public string SignaturVersion;
        }

        static DefenderStand DefenderLesen()
        {
            using (ManagementObject mo = Wmi2.Erste(DefenderNs, "SELECT AMServiceEnabled, AntivirusSignatureAge, QuickScanAge, AntivirusSignatureVersion FROM MSFT_MpComputerStatus"))
            {
                if (mo == null) return null;
                return new DefenderStand
                {
                    DienstAn = Sammler.Quellen.Wmi.Wahr(mo, "AMServiceEnabled") ?? false,
                    SignaturAlterTage = Sammler.Quellen.Wmi.Ganz(mo, "AntivirusSignatureAge") ?? -1,
                    SchnellscanAlterTage = Sammler.Quellen.Wmi.Ganz(mo, "QuickScanAge") ?? -1,
                    SignaturVersion = Sammler.Quellen.Wmi.Str(mo, "AntivirusSignatureVersion"),
                };
            }
        }

        /// <summary>
        /// Defender-Stand lesen; ohne Klasse oder ohne Dienst gibt es nichts zu tun, und die
        /// Nachweiszeile sagt das (mit Problem: der Befund bleibt, die Massnahme kann ihn nicht
        /// beheben, weil ein anderes Schutzprogramm den Platz hat).
        /// </summary>
        static DefenderStand DefenderVorbedingung(Ausfuehrungskontext k, string kennung, string was)
        {
            DefenderStand d;
            try { d = DefenderLesen(); }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace || ex.ErrorCode == ManagementStatus.InvalidClass || ex.ErrorCode == ManagementStatus.NotFound)
            {
                d = null;
            }
            if (d == null)
            {
                Nachweise.NichtBestanden(k, kennung, "Microsoft Defender ist auf diesem PC nicht vorhanden (MSFT_MpComputerStatus fehlt); " + was + " gibt es hier nicht.");
                return null;
            }
            if (!d.DienstAn)
            {
                Nachweise.NichtBestanden(k, kennung, "Der Defender-Dienst ist aus, vermutlich läuft ein anderes Schutzprogramm; " + was + " läuft dann dort, nicht hier.");
                return null;
            }
            return d;
        }

        static string AlterText(int tage)
        {
            if (tage < 0) return "unbekannt";
            if (tage == 65535) return "nie";
            return tage + (tage == 1 ? " Tag" : " Tage");
        }

        static void SignaturenAktualisieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            DefenderStand vorher = DefenderVorbedingung(k, KSignaturen, "Signaturen von Microsoft Defender");
            if (vorher == null) return;
            k.Schreibe("   Signaturen vorher: " + AlterText(vorher.SignaturAlterTage) + " alt"
                       + (string.IsNullOrEmpty(vorher.SignaturVersion) ? "" : ", Version " + vorher.SignaturVersion), "dim");

            const string aufruf = "WMI " + DefenderNs + " MSFT_MpSignature.Update(UpdateSource=1)";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KSignaturen, "Die Signaturen (" + AlterText(vorher.SignaturAlterTage) + " alt) wären über Microsoft Update aktualisiert worden.");
                return;
            }
            k.Melde(1, 2, "Die Virensignaturen werden von Microsoft Update geholt");
            k.Schreibe("›  " + aufruf, "dim");
            long? code;
            try { code = Wmi2.Ganz(Wmi2.StatischMitZeit(SignaturMs, DefenderNs, "MSFT_MpSignature", "Update", (byte)1)); }
            catch (Exception ex)
            {
                AppLog.Warn("MSFT_MpSignature.Update: " + ex.Message);
                Nachweise.NichtBestanden(k, KSignaturen, "Das Signaturupdate ist gescheitert: " + ex.Message);
                return;
            }
            if (k.IstAbgebrochen) return;
            k.Melde(2, 2, "Der Stand der Signaturen wird gelesen");
            DefenderStand nachher = null;
            try { nachher = DefenderLesen(); }
            catch (Exception ex) { AppLog.Warn("MSFT_MpComputerStatus nach dem Update: " + ex.Message); }

            if (code.HasValue && code.Value != 0)
            {
                Nachweise.NichtBestanden(k, KSignaturen, "Windows hat das Signaturupdate abgelehnt (Rückgabewert " + code.Value + "); die Signaturen sind "
                    + AlterText(nachher != null ? nachher.SignaturAlterTage : vorher.SignaturAlterTage) + " alt.");
                return;
            }
            // Bestanden nur mit gelesenem Alter innerhalb der Schwelle (Vertrag 8.7): ein Aufruf ohne
            // Fehler ist kein Nachweis, und eine leere Antwort ist kein bestandener Lauf.
            int schwelleSig = Kern.Regeln.Schwellen.NachweisSignaturTage;
            if (nachher == null)
            {
                Nachweise.NichtBestanden(k, KSignaturen, "Das Signaturupdate meldete 0, das neue Alter der Signaturen ließ sich danach aber nicht lesen; ohne Messwert gilt es nicht als aktualisiert.");
                return;
            }
            if (nachher.SignaturAlterTage >= 0 && nachher.SignaturAlterTage <= schwelleSig)
                Nachweise.Bestanden(k, KSignaturen, "Die Signaturen sind jetzt " + AlterText(nachher.SignaturAlterTage) + " alt"
                    + (string.IsNullOrEmpty(nachher.SignaturVersion) ? "" : " (Version " + nachher.SignaturVersion + ")") + "; das Update meldete 0.");
            else if (nachher.SignaturAlterTage < 0)
                Nachweise.NichtBestanden(k, KSignaturen, "Das Update meldete 0, Windows nennt das Alter der Signaturen danach aber nicht (Wert fehlt); ohne Messwert gilt es nicht als aktualisiert.");
            else
                Nachweise.NichtBestanden(k, KSignaturen, "Das Update meldete 0, die Signaturen sind aber weiterhin " + AlterText(nachher.SignaturAlterTage)
                    + " alt (erlaubt: bis " + schwelleSig + "); Microsoft Update hat keine neueren geliefert oder die Verbindung fehlt.");
        }

        // ------------------------------------------------------------------ Defender: Schnellscan

        /// <summary>MpCmdRun.exe aus InstallLocation (die Plattform-Version wechselt mit jedem Update), Rueckfall Program Files; null = nicht gefunden.</summary>
        static string MpCmdRunPfad(out string gesucht)
        {
            var orte = new List<string>();
            try
            {
                using (RegistryKey key = Reg.Hklm(DefenderSchluessel, false))
                {
                    string loc = Reg.Text(key, "InstallLocation");
                    if (!string.IsNullOrWhiteSpace(loc)) orte.Add(Path.Combine(loc.Trim(), "MpCmdRun.exe"));
                }
            }
            catch (Exception ex) { AppLog.Warn("InstallLocation von Windows Defender nicht lesbar: " + ex.Message); }
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(pf)) orte.Add(Path.Combine(pf, @"Windows Defender\MpCmdRun.exe"));
            }
            catch (Exception) { }
            gesucht = string.Join(", ", orte);
            foreach (string p in orte)
            {
                try { if (File.Exists(p)) return p; }
                catch (Exception) { }
            }
            return null;
        }

        static void Schnellscan(PlanSchritt s, Ausfuehrungskontext k)
        {
            DefenderStand vorher = DefenderVorbedingung(k, KSchnellscan, "den Schnellscan von Microsoft Defender");
            if (vorher == null) return;
            string gesucht;
            string exe = MpCmdRunPfad(out gesucht);
            if (exe == null)
            {
                Nachweise.NichtBestanden(k, KSchnellscan, "MpCmdRun.exe wurde nicht gefunden (gesucht: " + gesucht + "); der Schnellscan lief nicht.");
                return;
            }
            k.Schreibe("   Letzter Schnellscan vorher: " + AlterText(vorher.SchnellscanAlterTage) + " alt", "dim");

            // IgnoreExit, weil MpCmdRun mit 2 endet, wenn der Scan Funde meldet; Werkzeuge deutete 2
            // sonst als "Datei nicht gefunden" (Explain.ForExit). -1/-2 zaehlen trotzdem als Problem.
            var schritt = new Step { File = exe, Args = "-Scan -ScanType 1", IgnoreExit = true, TimeoutMs = SchnellscanMs };
            if (k.Trocken)
            {
                string tailT;
                Werkzeuge.Schritt(k, schritt, out tailT);
                Nachweise.Trocken(k, KSchnellscan, "Der Schnellscan wäre gestartet worden (" + exe + ").");
                return;
            }
            k.Melde(1, 2, "Der Schnellscan des Virenschutzes läuft");
            string tail;
            int code = Werkzeuge.Schritt(k, schritt, out tail);
            k.Werte["schnellscan.exit"] = code.ToString(CultureInfo.InvariantCulture);
            if (k.IstAbgebrochen) return;
            k.Melde(2, 2, "Das Ergebnis wird gelesen");
            DefenderStand nachher = null;
            try { nachher = DefenderLesen(); }
            catch (Exception ex) { AppLog.Warn("MSFT_MpComputerStatus nach dem Scan: " + ex.Message); }
            string alter = nachher == null ? "nicht lesbar" : AlterText(nachher.SchnellscanAlterTage);

            if (code < 0)
            {
                Nachweise.NichtBestanden(k, KSchnellscan, code == -2
                    ? "Der Schnellscan wurde nach " + (SchnellscanMs / 60000) + " Minuten abgebrochen und zählt nicht."
                    : "MpCmdRun.exe wurde nicht gestartet; der Schnellscan lief nicht.");
                return;
            }
            if (code != 0 && code != 2)
            {
                Nachweise.NichtBestanden(k, KSchnellscan, "MpCmdRun.exe meldete Exit " + code + "; der Schnellscan lief nicht durch.");
                return;
            }
            if (code == 2)
                k.Schreibe("   Der Schnellscan hat Funde gemeldet oder braucht eine Entscheidung; die Windows-Sicherheit zeigt sie unter „Schutzverlauf“.", "warn");
            // Exit 0 oder 2 allein ist kein Nachweis (2 heisst laut Doku auch "nicht abgeschlossen"):
            // bestanden nur, wenn Windows danach ein Alter innerhalb der Schwelle nennt (Vertrag 8.7).
            int schwelleScan = Kern.Regeln.Schwellen.NachweisSchnellscanTage;
            string exitWort = code == 0 ? "ohne Fund beendet (Exit 0)" : "beendet und hat Funde gemeldet (Exit 2)";
            if (nachher == null)
                Nachweise.NichtBestanden(k, KSchnellscan, "MpCmdRun ist " + exitWort + ", das Alter des letzten Schnellscans ließ sich danach aber nicht lesen; ohne Messwert zählt der Scan nicht.");
            else if (nachher.SchnellscanAlterTage < 0)
                Nachweise.NichtBestanden(k, KSchnellscan, "MpCmdRun ist " + exitWort + ", Windows nennt danach aber kein Alter des letzten Schnellscans (Wert fehlt); ohne Messwert zählt der Scan nicht.");
            else if (nachher.SchnellscanAlterTage <= schwelleScan)
                Nachweise.Bestanden(k, KSchnellscan, "Der Schnellscan ist " + exitWort + "; Windows nennt das Alter des letzten Schnellscans jetzt mit " + alter + ".");
            else
                Nachweise.NichtBestanden(k, KSchnellscan, "MpCmdRun ist " + exitWort + ", Windows nennt als Alter des letzten Schnellscans aber weiterhin "
                    + (nachher.SchnellscanAlterTage == 65535 ? "„nie“" : alter) + " (erlaubt: bis " + schwelleScan + "); der Scan wurde nicht als abgeschlossen verbucht.");
        }

        // ------------------------------------------------------------------ Firewall

        static string ProfilPruefen(PlanSchritt s)
        {
            string v = s.Wert("profil");
            if (v == "1" || v == "2" || v == "4") return null;
            return "Das Profil muss 1 (Domäne), 2 (Privat) oder 4 (Öffentlich) sein, nicht „" + Massnahmenbau.Kurz(v) + "“.";
        }

        static string ProfilName(int p)
        {
            switch (p)
            {
                case 1: return "Domäne";
                case 2: return "Privat";
                case 4: return "Öffentlich";
                default: return "Profil " + p;
            }
        }

        /// <summary>FirewallEnabled[profil] ueber COM lesen und optional setzen; alles auf einem Thread mit Zeitgrenze.</summary>
        static bool FirewallCom(int profil, bool? setzen)
        {
            return Zeitgrenze.Mit(() =>
            {
                object fw = Com.Neu(FirewallProgId);
                try
                {
                    if (setzen.HasValue) Com.Setze(fw, "FirewallEnabled", setzen.Value, profil);
                    return Convert.ToBoolean(Com.Hole(fw, "FirewallEnabled", profil), CultureInfo.InvariantCulture);
                }
                finally { Com.Freigeben(fw); }
            }, ComMs, "COM " + FirewallProgId + ".FirewallEnabled[" + profil + "]");
        }

        static void FirewallEinschalten(PlanSchritt s, Ausfuehrungskontext k)
        {
            int profil = int.Parse(s.Wert("profil"), CultureInfo.InvariantCulture);
            string name = ProfilName(profil);
            bool vorher;
            try { vorher = FirewallCom(profil, null); }
            catch (Exception ex)
            {
                AppLog.Warn("FirewallEnabled[" + profil + "] lesen: " + ex.Message);
                Nachweise.NichtBestanden(k, KFirewall, "Der Zustand der Firewall für „" + name + "“ ließ sich nicht lesen: " + ex.Message);
                return;
            }
            k.Schreibe("   Firewall „" + name + "“ vorher: " + (vorher ? "an" : "aus"), "dim");
            if (vorher)
            {
                Nachweise.Bestanden(k, KFirewall, "Die Firewall für das Profil „" + name + "“ ist schon an; nichts geändert.");
                return;
            }

            try
            {
                Sicherungsdatei.Schreiben(k, KFirewall, s.Parameter,
                    Sicherungsdatei.Werte("FirewallEnabled", "0", "Profil", profil.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KFirewall, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }

            string aufruf = "COM " + FirewallProgId + " INetFwPolicy2.FirewallEnabled[" + profil + "] = true";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KFirewall, "Die Firewall für „" + name + "“ wäre eingeschaltet worden; sie bleibt aus.");
                return;
            }
            k.Schreibe("›  " + aufruf, "dim");
            bool nachher;
            try { nachher = FirewallCom(profil, true); }
            catch (Exception ex)
            {
                AppLog.Warn("FirewallEnabled[" + profil + "] = true: " + ex.Message);
                Nachweise.NichtBestanden(k, KFirewall, "Windows hat das Einschalten der Firewall für „" + name + "“ abgelehnt: " + HResultText(ex));
                return;
            }
            if (nachher)
                Nachweise.Bestanden(k, KFirewall, "Die Firewall für das Profil „" + name + "“ ist jetzt an (FirewallEnabled = true, vorher false).");
            else
                Nachweise.NichtBestanden(k, KFirewall, "Die Firewall für „" + name + "“ steht nach dem Einschalten weiterhin auf aus; eine Richtlinie oder ein anderes Schutzprogramm erzwingt das.");
        }

        // ------------------------------------------------------------------ SmartScreen

        static void SmartScreenWarn(PlanSchritt s, Ausfuehrungskontext k)
        {
            // Die Richtlinie EnableSmartScreen ueberstimmt den lokalen Wert (Sicherheitsquelle):
            // steht sie auf 0, bewirkt Explorer\SmartScreenEnabled = Warn nichts.
            int? richtlinie = null;
            try { using (RegistryKey p = Reg.Hklm(SmartScreenRichtlinie, false)) richtlinie = Reg.Ganz(p, "EnableSmartScreen"); }
            catch (Exception ex) { AppLog.Warn("Richtlinie EnableSmartScreen nicht lesbar: " + ex.Message); }
            if (richtlinie.HasValue && richtlinie.Value == 0)
            {
                Nachweise.NichtBestanden(k, KSmartScreen, "Eine Richtlinie (EnableSmartScreen = 0) schaltet SmartScreen ab; der lokale Wert wirkt dann nicht. Nichts geändert.");
                return;
            }
            string vorher;
            try { using (RegistryKey key = Reg.Hklm(ExplorerSchluessel, false)) vorher = Reg.Text(key, "SmartScreenEnabled"); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KSmartScreen, "Der Schlüssel Explorer ließ sich nicht lesen: " + ex.Message);
                return;
            }
            k.Schreibe("   SmartScreenEnabled vorher: " + (string.IsNullOrEmpty(vorher) ? "nicht gesetzt" : "„" + Massnahmenbau.Kurz(vorher) + "“"), "dim");
            if (!string.IsNullOrEmpty(vorher) && !vorher.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                // Warn, Prompt, Block, RequireAdmin: alles davon warnt mindestens; nichts anzufassen.
                Nachweise.Bestanden(k, KSmartScreen, "SmartScreen steht schon auf „" + Massnahmenbau.Kurz(vorher) + "“; nichts geändert.");
                return;
            }

            try { Sicherungsdatei.Schreiben(k, KSmartScreen, s.Parameter, Sicherungsdatei.Werte("SmartScreenEnabled", vorher ?? "")); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KSmartScreen, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }

            string aufruf = @"Registry HKLM\" + ExplorerSchluessel + @"\SmartScreenEnabled = Warn";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KSmartScreen, "SmartScreenEnabled wäre auf „Warn“ gestellt worden; es bleibt " + (string.IsNullOrEmpty(vorher) ? "ungesetzt" : "„" + vorher + "“") + ".");
                return;
            }
            k.Schreibe("›  " + aufruf, "dim");
            string nachher;
            try
            {
                using (RegistryKey key = Reg.Hklm(ExplorerSchluessel, true))
                {
                    if (key == null) throw new InvalidOperationException("Der Schlüssel Explorer fehlt in der Registrierung.");
                    key.SetValue("SmartScreenEnabled", "Warn", RegistryValueKind.String);
                }
                using (RegistryKey key = Reg.Hklm(ExplorerSchluessel, false)) nachher = Reg.Text(key, "SmartScreenEnabled");
            }
            catch (Exception ex)
            {
                AppLog.Warn("SmartScreenEnabled = Warn: " + ex.Message);
                Nachweise.NichtBestanden(k, KSmartScreen, "SmartScreen ließ sich nicht umstellen: " + ex.Message);
                return;
            }
            if (string.Equals(nachher, "Warn", StringComparison.Ordinal))
                Nachweise.Bestanden(k, KSmartScreen, "SmartScreenEnabled steht jetzt auf „Warn“ (vorher " + (string.IsNullOrEmpty(vorher) ? "nicht gesetzt" : "„" + vorher + "“") + ").");
            else
                Nachweise.NichtBestanden(k, KSmartScreen, "SmartScreenEnabled steht nach dem Schreiben auf „" + (nachher ?? "") + "“ statt „Warn“.");
        }

        // ------------------------------------------------------------------ UAC

        /// <summary>
        /// Ein Registrierungswert in drei Zustaenden: nicht gesetzt (vorhanden = false, Wert null),
        /// lesbar (Zahl), gesetzt aber keine Zahl (vorhanden = true, Wert null). Die UAC-Massnahme
        /// braucht den Unterschied: ein fehlender Wert ist laut Doku
        /// (user-account-control/settings-and-configuration) der Standard, ein unlesbarer ist kein
        /// Messwert (Vertrag 8.4). Wirft die Leseausnahme des Schluessels.
        /// </summary>
        static int? RegZahl(RegistryKey key, string name, out bool vorhanden)
        {
            object roh = key.GetValue(name);
            vorhanden = roh != null;
            return vorhanden ? Reg.Ganz(key, name) : null;
        }

        /// <summary>
        /// Benutzerkontensteuerung auf den Standard (Vertrag 8.4): EnableLUA 0 → 1 (wirkt nach dem
        /// naechsten Neustart, neustart = 1 nur dann) und ConsentPromptBehaviorAdmin 0 → 5 (wirkt
        /// sofort), beide unter Policies\System. Die Sicherung nennt beide alten Werte (leer = Wert
        /// war nicht gesetzt). Ein fehlender Wert gilt als Standard und wird nicht angefasst; ein
        /// gesetzter, aber unlesbarer Wert ist kein Messwert und beendet die Massnahme als nicht
        /// bestanden. Sind beide in Ordnung, gibt es nichts zu tun, und der Nachweis sagt das.
        /// </summary>
        static void UacStandard(PlanSchritt s, Ausfuehrungskontext k)
        {
            int? lua, consent;
            bool luaDa, consentDa;
            try
            {
                using (RegistryKey key = Reg.Hklm(UacSchluessel, false))
                {
                    if (key == null)
                    {
                        Nachweise.NichtBestanden(k, KUac, "Der Schlüssel Policies\\System fehlt in der Registrierung; nichts geändert.");
                        return;
                    }
                    lua = RegZahl(key, "EnableLUA", out luaDa);
                    consent = RegZahl(key, "ConsentPromptBehaviorAdmin", out consentDa);
                }
            }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KUac, "Die UAC-Werte ließen sich nicht lesen: " + ex.Message);
                return;
            }
            k.Schreibe("   EnableLUA vorher: " + WertText(lua) + ", ConsentPromptBehaviorAdmin: " + WertText(consent), "dim");
            if (luaDa && !lua.HasValue)
            {
                Nachweise.NichtBestanden(k, KUac, "EnableLUA ist gesetzt, aber keine Zahl (nicht lesbar); nichts geändert.");
                return;
            }
            if (consentDa && !consent.HasValue)
            {
                Nachweise.NichtBestanden(k, KUac, "ConsentPromptBehaviorAdmin ist gesetzt, aber keine Zahl (nicht lesbar); nichts geändert.");
                return;
            }
            bool luaTun = lua.HasValue && lua.Value == 0;
            bool consentTun = consent.HasValue && consent.Value == 0;
            string luaWort = lua.HasValue ? lua.Value.ToString(CultureInfo.InvariantCulture) : "dem Standard (nicht gesetzt = 1)";
            string consentWort = consent.HasValue ? consent.Value.ToString(CultureInfo.InvariantCulture) : "dem Standard (nicht gesetzt = 5)";
            if (!luaTun && !consentTun)
            {
                Nachweise.Bestanden(k, KUac, "EnableLUA steht schon auf " + luaWort + " und die Nachfragestufe auf " + consentWort + "; nichts geändert.");
                return;
            }

            try
            {
                Sicherungsdatei.Schreiben(k, KUac, s.Parameter, Sicherungsdatei.Werte(
                    "EnableLUA", lua.HasValue ? lua.Value.ToString(CultureInfo.InvariantCulture) : "",
                    "ConsentPromptBehaviorAdmin", consent.HasValue ? consent.Value.ToString(CultureInfo.InvariantCulture) : ""));
            }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KUac, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }

            var teile = new List<string>();
            if (luaTun) teile.Add("EnableLUA = 1");
            if (consentTun) teile.Add("ConsentPromptBehaviorAdmin = 5");
            string aufruf = @"Registry HKLM\" + UacSchluessel + ": " + string.Join(", ", teile);
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KUac, (luaTun ? "EnableLUA wäre von 0 auf 1 gestellt worden (wirkt nach 1 Neustart)" : "")
                    + (luaTun && consentTun ? "; " : "")
                    + (consentTun ? "die Nachfragestufe wäre von 0 auf 5 gestellt worden (wirkt sofort)" : "") + "; nichts geändert.");
                return;
            }
            k.Schreibe("›  " + aufruf, "dim");
            int? luaNachher, consentNachher;
            try
            {
                using (RegistryKey key = Reg.Hklm(UacSchluessel, true))
                {
                    if (key == null) throw new InvalidOperationException("Der Schlüssel Policies\\System fehlt in der Registrierung.");
                    if (luaTun) key.SetValue("EnableLUA", 1, RegistryValueKind.DWord);
                    if (consentTun) key.SetValue("ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);
                }
                using (RegistryKey key = Reg.Hklm(UacSchluessel, false))
                {
                    luaNachher = Reg.Ganz(key, "EnableLUA");
                    consentNachher = Reg.Ganz(key, "ConsentPromptBehaviorAdmin");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("UAC " + string.Join(", ", teile) + ": " + ex.Message);
                Nachweise.NichtBestanden(k, KUac, "Die Benutzerkontensteuerung ließ sich nicht umstellen: " + ex.Message);
                return;
            }
            bool luaOk = !luaTun || luaNachher == 1;
            bool consentOk = !consentTun || consentNachher == 5;
            if (luaTun && luaOk)
            {
                k.Werte["neustart"] = "1";
                k.Schreibe("   Die Änderung an EnableLUA wirkt erst nach 1 Neustart.", "warn");
            }
            if (luaOk && consentOk)
                Nachweise.Bestanden(k, KUac, (luaTun ? "EnableLUA steht jetzt auf 1 (vorher 0); die Benutzerkontensteuerung ist nach 1 Neustart wieder an" : "")
                    + (luaTun && consentTun ? "; " : "")
                    + (consentTun ? "die Nachfragestufe steht jetzt auf 5 (vorher 0) und wirkt sofort" : "") + ".");
            else
                Nachweise.NichtBestanden(k, KUac, (luaOk ? "" : "EnableLUA steht nach dem Schreiben auf " + WertText(luaNachher) + " statt 1")
                    + (!luaOk && !consentOk ? "; " : "")
                    + (consentOk ? "" : "ConsentPromptBehaviorAdmin steht nach dem Schreiben auf " + WertText(consentNachher) + " statt 5") + ".");
        }

        // ------------------------------------------------------------------ Rueckweg (Kennung rueckweg, Datei aus der Sicherung)

        public static bool ZurueckBekannt(string kennung)
        {
            return kennung == KFirewall || kennung == KSmartScreen || kennung == KUac;
        }

        /// <summary>Nur Buchstaben (SmartScreenEnabled: Warn, Prompt, Block, RequireAdmin, Off), hoechstens 20 Zeichen; leer erlaubt.</summary>
        static bool SmartScreenWertGueltig(string v)
        {
            if (v == null || v.Length > 20) return false;
            foreach (char c in v)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))) return false;
            return true;
        }

        /// <summary>UAC-Vorwert: leer (= Wert loeschen) oder ganze Zahl 0 bis 5; null = Schluessel fehlt (auch gueltig, wird nicht angefasst).</summary>
        static string UacWertPruefen(string v, string name)
        {
            int z;
            if (v == null || v.Length == 0) return null;
            if (!Schritte.GanzeZahl(v, 0, 5, out z)) return "Der Vorwert " + name + " der Sicherung muss leer oder eine Zahl von 0 bis 5 sein, nicht „" + Massnahmenbau.Kurz(v) + "“.";
            return null;
        }

        static bool ProfilGueltig(string v) { return v == "1" || v == "2" || v == "4"; }

        /// <summary>
        /// Wertpruefung der Sicherung (Vertrag 8.3), Beschreibung an MassnahmenM3.ZurueckSicherheitPruefen.
        /// Die Datei liegt in einem Ordner, den nur der erhoehte Helfer beschreibt (Gemeinsam.cs);
        /// trotzdem geht kein Wert ungeprueft in die Registrierung oder an COM.
        /// </summary>
        public static string ZurueckPruefen(Sicherung si)
        {
            if (si == null) return "Die Sicherung ist leer.";
            switch (si.Kennung)
            {
                case KFirewall:
                    {
                        string an = si.Wert("FirewallEnabled");
                        if (an != "0" && an != "1") return "Die Sicherung nennt FirewallEnabled nicht als 0 oder 1" + (an == null ? "" : ", sondern „" + Massnahmenbau.Kurz(an) + "“") + ".";
                        string pV = si.Wert("Profil"), pP = si.ParameterWert("profil");
                        if (pV == null && pP == null) return "Die Sicherung nennt kein Firewall-Profil (weder Profil noch profil).";
                        if (pV != null && !ProfilGueltig(pV)) return "Das Profil der Sicherung muss 1, 2 oder 4 sein, nicht „" + Massnahmenbau.Kurz(pV) + "“.";
                        if (pP != null && !ProfilGueltig(pP)) return "Das Profil der Sicherung muss 1, 2 oder 4 sein, nicht „" + Massnahmenbau.Kurz(pP) + "“.";
                        if (pV != null && pP != null && pV != pP) return "Die Sicherung nennt zwei verschiedene Firewall-Profile (" + pV + " und " + pP + ").";
                        return null;
                    }
                case KSmartScreen:
                    {
                        string v = si.Wert("SmartScreenEnabled");
                        if (v == null) return "Die Sicherung nennt keinen Vorwert SmartScreenEnabled (leer = Wert löschen).";
                        if (!SmartScreenWertGueltig(v)) return "Der Vorwert SmartScreenEnabled „" + Massnahmenbau.Kurz(v) + "“ ist kein gültiger Wert (nur Buchstaben, höchstens 20).";
                        return null;
                    }
                case KUac:
                    {
                        string lua = si.Wert("EnableLUA"), consent = si.Wert("ConsentPromptBehaviorAdmin");
                        if (lua == null && consent == null) return "Die Sicherung nennt weder EnableLUA noch ConsentPromptBehaviorAdmin.";
                        return UacWertPruefen(lua, "EnableLUA") ?? UacWertPruefen(consent, "ConsentPromptBehaviorAdmin");
                    }
                default:
                    return "Die Sicherung gehört zu „" + Massnahmenbau.Kurz(si.Kennung) + "“, nicht zu einer Sicherheits-Maßnahme.";
            }
        }

        /// <summary>
        /// Stellt die gemerkten Werte einer Sicherheits-Sicherung zurueck (Beschreibung an
        /// MassnahmenM3.ZurueckSicherheit). Erst ZurueckPruefen (ein Grund → nicht bestanden), dann
        /// je Wert: Ist-Zustand lesen, bei Gleichheit nichts schreiben, sonst schreiben und nachlesen.
        /// </summary>
        public static bool Zurueck(Ausfuehrungskontext k, Sicherung si)
        {
            if (si == null) throw new ArgumentNullException("si");
            if (!ZurueckBekannt(si.Kennung)) throw new ArgumentException("Die Sicherung gehört zu „" + si.Kennung + "“, nicht zu einer Sicherheits-Maßnahme.");
            const string kennung = "rueckweg";
            string grund = ZurueckPruefen(si);
            if (grund != null)
            {
                Nachweise.NichtBestanden(k, kennung, grund + " Nichts zurückgestellt.");
                return false;
            }
            switch (si.Kennung)
            {
                case KFirewall: return FirewallZurueck(k, kennung, si);
                case KSmartScreen: return SmartScreenZurueck(k, kennung, si);
                default: return UacZurueck(k, kennung, si);
            }
        }

        static bool FirewallZurueck(Ausfuehrungskontext k, string kennung, Sicherung si)
        {
            int profil = int.Parse(si.Wert("Profil") ?? si.ParameterWert("profil"), CultureInfo.InvariantCulture);
            bool ziel = si.Wert("FirewallEnabled") == "1";
            string name = ProfilName(profil);
            string zielWort = ziel ? "an" : "aus";
            bool jetzt;
            try { jetzt = FirewallCom(profil, null); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Der Zustand der Firewall für „" + name + "“ ließ sich nicht lesen: " + HResultText(ex) + " Nichts zurückgestellt.");
                return false;
            }
            k.Schreibe("   Firewall „" + name + "“ jetzt: " + (jetzt ? "an" : "aus") + ", gesichert: " + zielWort, "dim");
            if (jetzt == ziel)
            {
                Nachweise.Bestanden(k, kennung, "Die Firewall für das Profil „" + name + "“ steht schon wieder auf " + zielWort + ", wie vor der Maßnahme; nichts geändert.");
                return true;
            }
            string aufruf = "COM " + FirewallProgId + " INetFwPolicy2.FirewallEnabled[" + profil + "] = " + (ziel ? "true" : "false");
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, kennung, "Die Firewall für „" + name + "“ wäre wieder auf " + zielWort + " gestellt worden; sie bleibt " + (jetzt ? "an" : "aus") + ".");
                return true;
            }
            k.Schreibe("›  " + aufruf, "dim");
            bool nachher;
            try { nachher = FirewallCom(profil, ziel); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Windows hat das Umstellen der Firewall für „" + name + "“ abgelehnt: " + HResultText(ex));
                return false;
            }
            if (nachher == ziel)
            {
                Nachweise.Bestanden(k, kennung, "Die Firewall für das Profil „" + name + "“ ist wieder " + zielWort + ", wie vor der Maßnahme.");
                return true;
            }
            Nachweise.NichtBestanden(k, kennung, "Die Firewall für „" + name + "“ steht nach dem Zurückstellen weiterhin auf " + (nachher ? "an" : "aus") + " statt " + zielWort + ".");
            return false;
        }

        static bool SmartScreenZurueck(Ausfuehrungskontext k, string kennung, Sicherung si)
        {
            // alt hat ZurueckPruefen bestanden (nur Buchstaben, hoechstens 20, oder leer); nur so ein
            // Wert wird geschrieben (Vertrag 8.3).
            string alt = si.Wert("SmartScreenEnabled");
            string altWort = alt.Length == 0 ? "„nicht gesetzt“" : "„" + alt + "“";
            string jetzt;
            try { using (RegistryKey key = Reg.Hklm(ExplorerSchluessel, false)) jetzt = Reg.Text(key, "SmartScreenEnabled") ?? ""; }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Der Schlüssel Explorer ließ sich nicht lesen: " + ex.Message + " Nichts zurückgestellt.");
                return false;
            }
            k.Schreibe("   SmartScreenEnabled jetzt: " + (jetzt.Length == 0 ? "nicht gesetzt" : "„" + Massnahmenbau.Kurz(jetzt) + "“") + ", gesichert: " + altWort, "dim");
            if (string.Equals(jetzt, alt, StringComparison.Ordinal))
            {
                Nachweise.Bestanden(k, kennung, "SmartScreenEnabled steht schon wieder auf " + altWort + ", wie vor der Maßnahme; nichts geändert.");
                return true;
            }
            string aufruf = @"Registry HKLM\" + ExplorerSchluessel + @"\SmartScreenEnabled " + (alt.Length == 0 ? "löschen" : "= " + alt);
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, kennung, "SmartScreenEnabled wäre auf " + altWort + " zurückgestellt worden; es bleibt " + (jetzt.Length == 0 ? "ungesetzt" : "„" + Massnahmenbau.Kurz(jetzt) + "“") + ".");
                return true;
            }
            k.Schreibe("›  " + aufruf, "dim");
            string nachher;
            try
            {
                using (RegistryKey key = Reg.Hklm(ExplorerSchluessel, true))
                {
                    if (key == null) throw new InvalidOperationException("Der Schlüssel Explorer fehlt in der Registrierung.");
                    if (alt.Length == 0) key.DeleteValue("SmartScreenEnabled", false);
                    else key.SetValue("SmartScreenEnabled", alt, RegistryValueKind.String);
                }
                using (RegistryKey key = Reg.Hklm(ExplorerSchluessel, false)) nachher = Reg.Text(key, "SmartScreenEnabled") ?? "";
            }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "SmartScreen ließ sich nicht zurückstellen: " + ex.Message);
                return false;
            }
            if (string.Equals(nachher, alt, StringComparison.Ordinal))
            {
                Nachweise.Bestanden(k, kennung, "SmartScreenEnabled steht wieder auf " + altWort + ", wie vor der Maßnahme.");
                return true;
            }
            Nachweise.NichtBestanden(k, kennung, "SmartScreenEnabled steht nach dem Zurückstellen auf " + (nachher.Length == 0 ? "„nicht gesetzt“" : "„" + Massnahmenbau.Kurz(nachher) + "“") + " statt " + altWort + ".");
            return false;
        }

        /// <summary>Ein UAC-Vorwert aus der Sicherung als Wort: null = Schluessel fehlt (nicht anfassen), leer = loeschen, sonst die Zahl.</summary>
        static string UacZielWort(string wert)
        {
            return wert == null ? "unverändert" : wert.Length == 0 ? "„nicht gesetzt“" : wert;
        }

        static bool UacZurueck(Ausfuehrungskontext k, string kennung, Sicherung si)
        {
            string altLua = si.Wert("EnableLUA"), altConsent = si.Wert("ConsentPromptBehaviorAdmin");
            int? luaJetzt, consentJetzt;
            try
            {
                using (RegistryKey key = Reg.Hklm(UacSchluessel, false))
                {
                    if (key == null)
                    {
                        Nachweise.NichtBestanden(k, kennung, "Der Schlüssel Policies\\System fehlt in der Registrierung; nichts zurückgestellt.");
                        return false;
                    }
                    luaJetzt = Reg.Ganz(key, "EnableLUA");
                    consentJetzt = Reg.Ganz(key, "ConsentPromptBehaviorAdmin");
                }
            }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Die UAC-Werte ließen sich nicht lesen: " + ex.Message + " Nichts zurückgestellt.");
                return false;
            }
            k.Schreibe("   EnableLUA jetzt " + WertText(luaJetzt) + ", gesichert " + UacZielWort(altLua)
                       + "; ConsentPromptBehaviorAdmin jetzt " + WertText(consentJetzt) + ", gesichert " + UacZielWort(altConsent), "dim");
            bool luaTun = altLua != null && !UacGleich(luaJetzt, altLua);
            bool consentTun = altConsent != null && !UacGleich(consentJetzt, altConsent);
            if (!luaTun && !consentTun)
            {
                Nachweise.Bestanden(k, kennung, "EnableLUA steht schon wieder auf " + WertText(luaJetzt) + " und die Nachfragestufe auf " + WertText(consentJetzt) + ", wie vor der Maßnahme; nichts geändert.");
                return true;
            }
            var teile = new List<string>();
            if (luaTun) teile.Add("EnableLUA " + (altLua.Length == 0 ? "löschen" : "= " + altLua));
            if (consentTun) teile.Add("ConsentPromptBehaviorAdmin " + (altConsent.Length == 0 ? "löschen" : "= " + altConsent));
            string aufruf = @"Registry HKLM\" + UacSchluessel + ": " + string.Join(", ", teile);
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, kennung, (luaTun ? "EnableLUA wäre auf " + UacZielWort(altLua) + " zurückgestellt worden (wirkt nach 1 Neustart)" : "")
                    + (luaTun && consentTun ? "; " : "")
                    + (consentTun ? "die Nachfragestufe wäre auf " + UacZielWort(altConsent) + " zurückgestellt worden" : "") + ".");
                return true;
            }
            k.Schreibe("›  " + aufruf, "dim");
            int? luaNachher, consentNachher;
            try
            {
                using (RegistryKey key = Reg.Hklm(UacSchluessel, true))
                {
                    if (key == null) throw new InvalidOperationException("Der Schlüssel Policies\\System fehlt in der Registrierung.");
                    if (luaTun) UacSchreiben(key, "EnableLUA", altLua);
                    if (consentTun) UacSchreiben(key, "ConsentPromptBehaviorAdmin", altConsent);
                }
                using (RegistryKey key = Reg.Hklm(UacSchluessel, false))
                {
                    luaNachher = Reg.Ganz(key, "EnableLUA");
                    consentNachher = Reg.Ganz(key, "ConsentPromptBehaviorAdmin");
                }
            }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Die UAC-Werte ließen sich nicht zurückstellen: " + ex.Message);
                return false;
            }
            bool luaOk = !luaTun || UacGleich(luaNachher, altLua);
            bool consentOk = !consentTun || UacGleich(consentNachher, altConsent);
            // neustart nur, wenn EnableLUA wirklich einen anderen Wert bekommen hat (Vertrag 8.3):
            // ein geloeschter Wert gilt wie 1, ein Wechsel von 1 auf "nicht gesetzt" ist keine Aenderung.
            bool neustart = luaTun && luaOk && (luaJetzt ?? 1) != (luaNachher ?? 1);
            if (neustart)
            {
                k.Werte["neustart"] = "1";
                k.Schreibe("   Die Änderung an EnableLUA wirkt erst nach 1 Neustart.", "warn");
            }
            if (luaOk && consentOk)
            {
                Nachweise.Bestanden(k, kennung, "EnableLUA steht wieder auf " + WertText(luaNachher) + " und die Nachfragestufe auf " + WertText(consentNachher) + ", wie vor der Maßnahme"
                    + (neustart ? "; wirksam nach 1 Neustart" : "") + ".");
                return true;
            }
            Nachweise.NichtBestanden(k, kennung, (luaOk ? "" : "EnableLUA steht nach dem Zurückstellen auf " + WertText(luaNachher) + " statt " + UacZielWort(altLua))
                + (!luaOk && !consentOk ? "; " : "")
                + (consentOk ? "" : "ConsentPromptBehaviorAdmin steht nach dem Zurückstellen auf " + WertText(consentNachher) + " statt " + UacZielWort(altConsent)) + ".");
            return false;
        }

        /// <summary>true, wenn der gelesene Wert dem gesicherten entspricht (leer = nicht gesetzt).</summary>
        static bool UacGleich(int? jetzt, string alt)
        {
            if (alt.Length == 0) return !jetzt.HasValue;
            return jetzt.HasValue && jetzt.Value.ToString(CultureInfo.InvariantCulture) == alt;
        }

        static void UacSchreiben(RegistryKey key, string name, string alt)
        {
            if (alt.Length == 0) key.DeleteValue(name, false);
            else key.SetValue(name, int.Parse(alt, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
        }

        // ------------------------------------------------------------------ Hilfen

        static string WertText(int? v)
        {
            return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "nicht gesetzt";
        }

        /// <summary>Fehlertext mit HRESULT in Hex bei COM-Fehlern (der Nutzer sieht die Zahl, der Techniker sucht danach).</summary>
        static string HResultText(Exception ex)
        {
            var ce = ex as COMException;
            if (ce != null) return ex.Message + " (0x" + ce.HResult.ToString("X8") + ")";
            return ex.Message;
        }
    }
}
