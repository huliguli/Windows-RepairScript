using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Fuellt s.Sicherheit: Virenschutz (Defender-WMI), Fremd-Virenschutz (SecurityCenter2),
    /// Firewall (COM, Rueckfall WMI), UAC, SmartScreen, Secure Boot, TPM (tbs.dll), lokale
    /// Konten, Remotedesktop, SMB1 und die Windows-Fehlerberichterstattung.
    ///
    /// Alles im eigenen Prozess: System.Management, Microsoft.Win32.Registry, COM ueber
    /// ProgID und ein P/Invoke. Kein Skript-Interpreter, kein Prozessstart.
    ///
    /// Grundsaetze aus der Recherche (NS-22 bis NS-38) und der Widerlegungsrunde:
    ///   - Kein lokalisierter Text wird gedeutet. AMRunningMode und SmartScreenEnabled sind
    ///     feste englische Marken, keine Uebersetzungen; Gruppen werden ueber die SID
    ///     S-1-5-32-544 gefunden ("Administratoren" ist lokalisiert), Konten ueber die RID.
    ///   - Was nicht erhoeht nicht lesbar ist, traegt "zugriff" ein: die Defender-Ausschluesse
    ///     (nicht erhoeht steht ein Sentinel-String "N/A: ..." im Array, gemessen).
    ///   - Leer ohne Fehler gibt es nicht: fehlt eine Klasse oder ein Schluessel, steht "fehlt"
    ///     in der Fehlerliste (kein Defender, Legacy-BIOS ohne SecureBoot-Schluessel, Server
    ///     ohne SecurityCenter2).
    ///   - productState aus SecurityCenter2 ist undokumentiert und wird NICHT gedeutet; nur
    ///     die Namen der registrierten Fremdprodukte werden gesammelt. Ob der registrierte
    ///     Schutz wirkt, sagt die dokumentierte Zahl aus WscGetSecurityProviderHealth
    ///     (0 GOOD, 1 NOTMONITORED, 2 POOR, 3 SNOOZE) - ein abgelaufenes Fremdprodukt bleibt
    ///     registriert, meldet aber POOR.
    /// </summary>
    public static class Sicherheitsquelle
    {
        const string DefenderNs = @"root\Microsoft\Windows\Defender";
        const string SecurityCenterNs = @"root\SecurityCenter2";
        const string Cimv2Ns = @"root\cimv2";
        const string StandardCimv2Ns = @"root\StandardCimv2";

        /// <summary>instanceGuid, unter dem Windows Defender sich selbst in SecurityCenter2 eintraegt (gemessen 11.09.2026).</summary>
        const string DefenderInstanzGuid = "{D68DDC3A-831F-4FAE-9E44-DA132C1ACF46}";

        public static void Erfassen(Systembild s)
        {
            var si = s.Sicherheit;
            Defender(s, si);
            Ausschluesse(s, si);
            Funde(s, si);
            DrittAv(s, si);
            Gesundheit(s, si);
            Firewall(s, si);
            Uac(s, si);
            SmartScreen(s, si);
            SecureBoot(s, si);
            Tpm(s, si);
            Konten(s, si);
            Rdp(s, si);
            Smb1(s, si);
            Smb1Konfiguration(s, si);
            Wer(s, si);
        }

        // ------------------------------------------------------------------ Defender

        /// <summary>
        /// MSFT_MpComputerStatus (Doku: previous-versions/windows/desktop/defender/msft-mpcomputerstatus).
        /// Fehlt Namespace oder Klasse, gibt es keinen Defender: Defender bleibt null, Fehler "fehlt".
        /// Die Alter-Felder sind uint32 mit 65535 = nie; fehlt ein Feld, steht -1 im Bild
        /// (nicht 0: 0 hiesse "heute aktualisiert" und waere eine erfundene Entwarnung).
        /// </summary>
        static void Defender(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "wmi.defender.status", () =>
            {
                List<ManagementObject> rows;
                try { rows = Wmi.Abfrage(DefenderNs, "SELECT * FROM MSFT_MpComputerStatus"); }
                catch (ManagementException ex) when (KlasseFehlt(ex))
                {
                    Sammler.Fehler(s, "wmi.defender.status", Fehler.Fehlt, "MSFT_MpComputerStatus nicht vorhanden (kein Microsoft Defender): " + ex.ErrorCode);
                    return;
                }
                if (rows.Count == 0)
                {
                    Sammler.Fehler(s, "wmi.defender.status", Fehler.Fehlt, "MSFT_MpComputerStatus lieferte keine Instanz");
                    return;
                }
                var mo = rows[0];
                var d = new Defender
                {
                    DienstAn = Wmi.Wahr(mo, "AMServiceEnabled") ?? false,
                    Echtzeit = Wmi.Wahr(mo, "RealTimeProtectionEnabled") ?? false,
                    Verhalten = Wmi.Wahr(mo, "BehaviorMonitorEnabled") ?? false,
                    SignaturAlterTage = Wmi.Ganz(mo, "AntivirusSignatureAge") ?? -1,
                    SchnellscanAlterTage = Wmi.Ganz(mo, "QuickScanAge") ?? -1,
                    VollscanAlterTage = Wmi.Ganz(mo, "FullScanAge") ?? -1,
                    Tamper = Wmi.Wahr(mo, "IsTamperProtected") ?? false,
                    // Feste englische Marke ("Normal", "Passive", "EDR Block", "SxS Passive Mode",
                    // "Not running"), keine Lokalisierung - darf als Wert gespeichert werden.
                    Modus = Wmi.Str(mo, "AMRunningMode"),
                    AusschluesseSichtbar = false,
                };
                si.Defender = d;
            });
        }

        /// <summary>
        /// MSFT_MpPreference.ExclusionPath/Process/Extension - nur erhoeht. Nicht erhoeht liefert
        /// die Klasse den Sentinel "N/A: Must be an administrator to view exclusions" im Array
        /// (gemessen); der wird verworfen und heisst "nicht sichtbar". Auch die Richtlinie
        /// HideExclusionsFromLocalAdmins macht die Liste unsichtbar (Doku
        /// microsoft-defender-antivirus-exclusions-configure).
        /// </summary>
        static void Ausschluesse(Systembild s, Sicherheit si)
        {
            if (si.Defender == null) return;
            Sammler.NurErhoeht(s, "wmi.defender.exclusions", () =>
            {
                // SELECT *: HideExclusionsFromLocalAdmins gibt es erst auf neueren Plattform-Versionen;
                // ein benanntes SELECT auf eine fehlende Eigenschaft waere "Invalid query".
                var rows = Wmi.Abfrage(DefenderNs, "SELECT * FROM MSFT_MpPreference");
                if (rows.Count == 0)
                {
                    Sammler.Fehler(s, "wmi.defender.exclusions", Fehler.Fehlt, "MSFT_MpPreference lieferte keine Instanz");
                    return;
                }
                var mo = rows[0];
                bool sentinel = false;
                si.Defender.AusschlussPfade = OhneSentinel(Wmi.Strings(mo, "ExclusionPath"), ref sentinel);
                si.Defender.AusschlussProzesse = OhneSentinel(Wmi.Strings(mo, "ExclusionProcess"), ref sentinel);
                si.Defender.AusschlussEndungen = OhneSentinel(Wmi.Strings(mo, "ExclusionExtension"), ref sentinel);
                bool versteckt = Wmi.Wahr(mo, "HideExclusionsFromLocalAdmins") ?? false;
                si.Defender.AusschluesseSichtbar = !sentinel && !versteckt;
                if (sentinel) Sammler.Fehler(s, "wmi.defender.exclusions", Fehler.Zugriff, "Ausschlussliste trägt den Sentinel N/A (nicht sichtbar)");
                else if (versteckt) Sammler.Fehler(s, "wmi.defender.exclusions", Fehler.Zugriff, "HideExclusionsFromLocalAdmins ist gesetzt");
            });
        }

        static List<string> OhneSentinel(List<string> eingabe, ref bool sentinel)
        {
            var l = new List<string>();
            foreach (string e in eingabe)
            {
                if (string.IsNullOrWhiteSpace(e)) continue;
                if (e.StartsWith("N/A:", StringComparison.OrdinalIgnoreCase)) { sentinel = true; continue; }
                l.Add(e.Trim());
            }
            return l;
        }

        /// <summary>
        /// MSFT_MpThreat (Name, SeverityID, CategoryID, IsActive) plus die juengste
        /// InitialDetectionTime je ThreatID aus MSFT_MpThreatDetection. Beide Klassen sind nicht
        /// erhoeht lesbar (gemessen: 5 Threats, 14 Detections). Eine leere Liste ist hier eine
        /// echte Antwort ("nie etwas gefunden"), kein stilles Scheitern - die Klasse hat geantwortet.
        /// </summary>
        static void Funde(Systembild s, Sicherheit si)
        {
            if (si.Defender == null) return;
            var zeiten = new Dictionary<long, string>();
            Sammler.Versuch(s, "wmi.defender.detections", () =>
            {
                var rows = Wmi.Abfrage(DefenderNs, "SELECT ThreatID, InitialDetectionTime FROM MSFT_MpThreatDetection", 10000);
                foreach (var mo in rows)
                {
                    long? id = Wmi.Lang(mo, "ThreatID");
                    string t = Wmi.ZeitUtc(mo, "InitialDetectionTime");
                    if (!id.HasValue || t == null) continue;
                    string alt;
                    if (!zeiten.TryGetValue(id.Value, out alt) || string.CompareOrdinal(t, alt) > 0) zeiten[id.Value] = t;
                }
            });
            Sammler.Versuch(s, "wmi.defender.threats", () =>
            {
                var rows = Wmi.Abfrage(DefenderNs, "SELECT ThreatID, ThreatName, SeverityID, CategoryID, IsActive FROM MSFT_MpThreat", 10000);
                foreach (var mo in rows)
                {
                    long id = Wmi.Lang(mo, "ThreatID") ?? -1;
                    string zeit;
                    zeiten.TryGetValue(id, out zeit);
                    si.Defender.Funde.Add(new Fund
                    {
                        // ThreatName ist die englische Defender-Kennung ("PUA:Win32/..."), nicht lokalisiert; nur Anzeige.
                        Name = Wmi.Str(mo, "ThreatName"),
                        Schwere = Wmi.Ganz(mo, "SeverityID") ?? 0,
                        Kategorie = Wmi.Ganz(mo, "CategoryID") ?? 0,
                        Aktiv = Wmi.Wahr(mo, "IsActive") ?? false,
                        ZeitUtc = zeit,
                    });
                }
            });
        }

        // ------------------------------------------------------------------ Fremd-Virenschutz

        /// <summary>
        /// root\SecurityCenter2 AntiVirusProduct: nur displayName der Fremdprodukte. productState
        /// ist undokumentiert (Microsoft Q&A) und wird nicht gedeutet; ob der registrierte Schutz
        /// wirkt, misst Gesundheit() ueber die dokumentierte WSC-Schnittstelle. Defender selbst
        /// wird ueber seine instanceGuid ausgefiltert, zur Sicherheit auch ueber den Namen. Auf
        /// Server-SKUs fehlt der Namespace: "fehlt", kein Fremdschutz.
        /// </summary>
        static void DrittAv(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "wmi.securitycenter2.antivirus", () =>
            {
                List<ManagementObject> rows;
                try { rows = Wmi.Abfrage(SecurityCenterNs, "SELECT displayName, instanceGuid FROM AntiVirusProduct"); }
                catch (ManagementException ex) when (KlasseFehlt(ex))
                {
                    Sammler.Fehler(s, "wmi.securitycenter2.antivirus", Fehler.Fehlt, "root\\SecurityCenter2 nicht vorhanden (Server-SKU?): " + ex.ErrorCode);
                    return;
                }
                foreach (var mo in rows)
                {
                    string guid = (Wmi.Str(mo, "instanceGuid") ?? "").Trim().ToUpperInvariant();
                    string name = (Wmi.Str(mo, "displayName") ?? "").Trim();
                    if (guid == DefenderInstanzGuid) continue;
                    if (name.StartsWith("Windows Defender", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Microsoft Defender", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Length == 0) continue;
                    if (!si.DrittAv.Contains(name)) si.DrittAv.Add(name);
                }
                // Kein Fremdprodukt ist eine gueltige Antwort (Defender allein), kein Fehler.
            });
        }

        // WscGetSecurityProviderHealth (Doku: wscapi/nf-wscapi-wscgetsecurityproviderhealth):
        // Providers ist WSC_SECURITY_PROVIDER (ANTIVIRUS = 4), pHealth ein
        // WSC_SECURITY_PROVIDER_HEALTH (0 GOOD, 1 NOTMONITORED, 2 POOR, 3 SNOOZE). Rueckgabe S_OK
        // = gemessen; S_FALSE heisst laut Doku "der WSC-Dienst laeuft nicht" und pHealth steht
        // dann fest auf POOR - das ist "nicht messbar", kein Befund. Doku: "Minimum supported
        // server: None supported" - auf Server-SKUs fehlt die DLL oder der Aufruf scheitert.
        [DllImport("wscapi.dll", ExactSpelling = true)]
        static extern int WscGetSecurityProviderHealth(uint providers, out int health);

        const uint WscProviderAntivirus = 4;
        const int S_FALSE = 1;

        /// <summary>
        /// Gesundheit des Virenschutzes aus Sicht des Sicherheitscenters, als Zahl ins Bild.
        /// Nur bei S_OK wird geschrieben; alles andere ist "fehlt" (Server ohne wscsvc darf
        /// daraus nie einen Befund bekommen). Der Aufruf geht ueber COM zum Dienst und bekommt
        /// deshalb dieselbe Zeitgrenze wie die Firewall.
        /// </summary>
        static void Gesundheit(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "api.wsc.antivirus", () =>
            {
                int[] r;
                try
                {
                    r = MitZeitgrenze(() =>
                    {
                        int h;
                        int hr = WscGetSecurityProviderHealth(WscProviderAntivirus, out h);
                        return new[] { hr, h };
                    }, Wmi.StandardZeitMs, "WscGetSecurityProviderHealth");
                }
                catch (DllNotFoundException) { Sammler.Fehler(s, "api.wsc.antivirus", Fehler.Fehlt, "wscapi.dll nicht vorhanden (Server-SKU?)"); return; }
                catch (EntryPointNotFoundException) { Sammler.Fehler(s, "api.wsc.antivirus", Fehler.Fehlt, "WscGetSecurityProviderHealth nicht exportiert"); return; }
                if (r[0] == 0) { si.VirenschutzGesundheit = r[1]; return; }
                if (r[0] == S_FALSE) { Sammler.Fehler(s, "api.wsc.antivirus", Fehler.Fehlt, "Sicherheitscenter-Dienst (wscsvc) läuft nicht (S_FALSE), Gesundheit nicht messbar"); return; }
                Sammler.Fehler(s, "api.wsc.antivirus", Fehler.Fehlt, "WscGetSecurityProviderHealth lieferte 0x" + r[0].ToString("X8"));
            });
        }

        // ------------------------------------------------------------------ Firewall

        /// <summary>
        /// Wirksamer Zustand ueber COM HNetCfg.FwPolicy2 (INetFwPolicy2.FirewallEnabled[profil],
        /// DefaultInboundAction[profil]; NET_FW_PROFILE_TYPE2 1 Domaene, 2 privat, 4 oeffentlich;
        /// NET_FW_ACTION 0 Block, 1 Allow). Rueckfall MSFT_NetFirewallProfile: System.Management
        /// liest dort nur den Persistent Store (DefaultInboundAction 0 NotConfigured, obwohl Block
        /// wirkt - Konzept 4.4), deshalb im Rueckfall nur Enabled und Eingehend = null.
        /// </summary>
        static void Firewall(Systembild s, Sicherheit si)
        {
            bool com = Sammler.Versuch(s, "com.firewall.policy2", () =>
            {
                var liste = MitZeitgrenze(() =>
                {
                    Type t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", false);
                    if (t == null) throw new InvalidOperationException("ProgID HNetCfg.FwPolicy2 nicht registriert");
                    object fw = Activator.CreateInstance(t);
                    try
                    {
                        var l = new List<FirewallProfil>();
                        foreach (int p in new[] { 1, 2, 4 })
                        {
                            object an = t.InvokeMember("FirewallEnabled", BindingFlags.GetProperty, null, fw, new object[] { p });
                            object ein = t.InvokeMember("DefaultInboundAction", BindingFlags.GetProperty, null, fw, new object[] { p });
                            l.Add(new FirewallProfil
                            {
                                Profil = p,
                                An = Convert.ToBoolean(an, CultureInfo.InvariantCulture),
                                Eingehend = ein == null ? (int?)null : Convert.ToInt32(ein, CultureInfo.InvariantCulture),
                            });
                        }
                        return l;
                    }
                    finally { Marshal.ReleaseComObject(fw); }
                }, Wmi.StandardZeitMs, "HNetCfg.FwPolicy2");
                si.Firewall = liste;
            });
            if (com && si.Firewall.Count == 3) return;

            Sammler.Versuch(s, "wmi.firewall.profile", () =>
            {
                var rows = Wmi.Abfrage(StandardCimv2Ns, "SELECT Name, Enabled FROM MSFT_NetFirewallProfile");
                var l = new List<FirewallProfil>();
                foreach (var mo in rows)
                {
                    // Name ist der Instanzschluessel "Domain"/"Private"/"Public", keine Uebersetzung.
                    string name = Wmi.Str(mo, "Name") ?? "";
                    int profil = name.Equals("Domain", StringComparison.OrdinalIgnoreCase) ? 1
                               : name.Equals("Private", StringComparison.OrdinalIgnoreCase) ? 2
                               : name.Equals("Public", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
                    if (profil == 0) continue;
                    // Enabled: 0 False, 1 True, 2 NotConfigured (= Systemvorgabe, und die ist "an").
                    int en = Wmi.Ganz(mo, "Enabled") ?? 2;
                    l.Add(new FirewallProfil { Profil = profil, An = en != 0, Eingehend = null });
                }
                if (l.Count == 0) { Sammler.Fehler(s, "wmi.firewall.profile", Fehler.Fehlt, "MSFT_NetFirewallProfile lieferte kein Profil"); return; }
                si.Firewall = l;
            });
        }

        // ------------------------------------------------------------------ Registry-Teile

        const string PolicySystem = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

        /// <summary>
        /// UAC (Doku: user-account-control/settings-and-configuration). Fehlender Wert heisst
        /// laut Doku "Standard" - im Bild bleibt er null, damit nichts erfunden wird; die Regel
        /// meldet nur gesetzte Nullen.
        /// </summary>
        static void Uac(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "registry.uac", () =>
            {
                using (var k = Hklm().OpenSubKey(PolicySystem))
                {
                    if (k == null) { Sammler.Fehler(s, "registry.uac", Fehler.Fehlt, "Schlüssel Policies\\System fehlt"); return; }
                    si.EnableLua = RegInt(k, "EnableLUA");
                    si.ConsentAdmin = RegInt(k, "ConsentPromptBehaviorAdmin");
                    si.SecureDesktop = RegInt(k, "PromptOnSecureDesktop");
                    if (si.EnableLua == null && si.ConsentAdmin == null && si.SecureDesktop == null)
                        Sammler.Fehler(s, "registry.uac", Fehler.Fehlt, "keiner der drei UAC-Werte vorhanden");
                }
            });
        }

        /// <summary>
        /// SmartScreen: die Richtlinie EnableSmartScreen (Policy CSP SmartScreen, 0 aus / 1 an,
        /// Stufe aus ShellSmartScreenLevel) ueberstimmt den lokalen Wert Explorer\SmartScreenEnabled
        /// (REG_SZ mit den festen Marken Warn / Block / Off; auf aelteren Builds Prompt /
        /// RequireAdmin). Ergebnis im Bild: "Warn", "Block", "Off" oder null.
        /// </summary>
        static void SmartScreen(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "registry.smartscreen", () =>
            {
                using (var p = Hklm().OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System"))
                {
                    int? policy = p == null ? (int?)null : RegInt(p, "EnableSmartScreen");
                    if (policy.HasValue)
                    {
                        if (policy.Value == 0) { si.SmartScreen = "Off"; return; }
                        string stufe = RegStr(p, "ShellSmartScreenLevel");
                        si.SmartScreen = string.Equals(stufe, "Block", StringComparison.OrdinalIgnoreCase) ? "Block" : "Warn";
                        return;
                    }
                }
                using (var k = Hklm().OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"))
                {
                    string v = k == null ? null : RegStr(k, "SmartScreenEnabled");
                    if (string.IsNullOrEmpty(v)) { Sammler.Fehler(s, "registry.smartscreen", Fehler.Fehlt, "weder Richtlinie noch Explorer\\SmartScreenEnabled vorhanden"); return; }
                    if (v.Equals("Off", StringComparison.OrdinalIgnoreCase)) si.SmartScreen = "Off";
                    else if (v.Equals("Block", StringComparison.OrdinalIgnoreCase) || v.Equals("RequireAdmin", StringComparison.OrdinalIgnoreCase)) si.SmartScreen = "Block";
                    else if (v.Equals("Warn", StringComparison.OrdinalIgnoreCase) || v.Equals("Prompt", StringComparison.OrdinalIgnoreCase)) si.SmartScreen = "Warn";
                    else Sammler.Fehler(s, "registry.smartscreen", Fehler.Ausnahme, "unbekannte Marke in SmartScreenEnabled: " + v);
                }
            });
        }

        /// <summary>
        /// SecureBoot\State\UEFISecureBootEnabled (nicht erhoeht lesbar, gemessen). Auf
        /// Legacy-BIOS/CSM-Systemen fehlt der Schluessel: das ist "nicht anwendbar", nicht "aus" -
        /// deshalb null plus "fehlt". Ob UEFI vorliegt, sagt Hardware.Uefi (GetFirmwareType).
        /// </summary>
        static void SecureBoot(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "registry.secureboot", () =>
            {
                using (var k = Hklm().OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                {
                    int? v = k == null ? (int?)null : RegInt(k, "UEFISecureBootEnabled");
                    if (!v.HasValue) { Sammler.Fehler(s, "registry.secureboot", Fehler.Fehlt, "SecureBoot\\State\\UEFISecureBootEnabled fehlt (Legacy-BIOS oder CSM)"); return; }
                    si.SecureBoot = v.Value != 0;
                }
            });
        }

        // ------------------------------------------------------------------ TPM (tbs.dll)

        [StructLayout(LayoutKind.Sequential)]
        struct TPM_DEVICE_INFO
        {
            public uint structVersion;
            public uint tpmVersion;        // TPM_VERSION_12 = 1, TPM_VERSION_20 = 2
            public uint tpmInterfaceType;
            public uint tpmImpRevision;
        }

        [DllImport("tbs.dll", ExactSpelling = true)]
        static extern uint Tbsi_GetDeviceInfo(uint size, ref TPM_DEVICE_INFO info);

        const uint TBS_E_TPM_NOT_FOUND = 0x8028400F;

        /// <summary>
        /// Tbsi_GetDeviceInfo (Doku: tbs/nf-tbs-tbsi_getdeviceinfo) laeuft ohne Rechte und
        /// liefert die TPM-Version (gemessen: rc 0, Version 2). Win32_Tpm dagegen liefert nicht
        /// erhoeht STILL nichts und bleibt dem Helfer vorbehalten. Nur TBS_E_TPM_NOT_FOUND heisst
        /// "kein TPM"; jeder andere Rueckgabewert ist "nicht ermittelbar".
        /// </summary>
        static void Tpm(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "api.tbs.deviceinfo", () =>
            {
                var info = new TPM_DEVICE_INFO { structVersion = 1 };
                uint rc;
                try { rc = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf(typeof(TPM_DEVICE_INFO)), ref info); }
                catch (DllNotFoundException) { Sammler.Fehler(s, "api.tbs.deviceinfo", Fehler.Fehlt, "tbs.dll nicht vorhanden"); return; }
                catch (EntryPointNotFoundException) { Sammler.Fehler(s, "api.tbs.deviceinfo", Fehler.Fehlt, "Tbsi_GetDeviceInfo nicht exportiert"); return; }
                if (rc == 0)
                {
                    si.TpmVorhanden = true;
                    si.TpmVersion = info.tpmVersion == 0 ? (int?)null : (int)info.tpmVersion;
                    return;
                }
                if (rc == TBS_E_TPM_NOT_FOUND) { si.TpmVorhanden = false; return; }
                Sammler.Fehler(s, "api.tbs.deviceinfo", Fehler.Ausnahme, "Tbsi_GetDeviceInfo lieferte 0x" + rc.ToString("X8"));
            });
        }

        // ------------------------------------------------------------------ Konten

        /// <summary>
        /// Win32_UserAccount mit LocalAccount=TRUE und Domain=Rechnername (der Domain-Filter
        /// verhindert, dass ein Domaenenrechner alle Domaenenkonten aufzaehlt - Doku warnt vor
        /// der Last). RID = letzter Teil der SID: 500 eingebauter Administrator, 501 Gast; der
        /// Name "Gast" ist lokalisiert. Administratoren ueber Win32_Group mit SID S-1-5-32-544
        /// (der Gruppenname ist lokalisiert) und ASSOCIATORS ueber Win32_GroupUser; der Abgleich
        /// laeuft ueber die SID, nie ueber Namen. MicrosoftKonto kommt aus LsaLookupUserAccountType.
        /// </summary>
        static void Konten(Systembild s, Sicherheit si)
        {
            string rechner = SafeMachineName();
            // SID je Konto fuer den Abgleich mit der Administratorengruppe (das Bild traegt nur die RID).
            var kontoJeSid = new Dictionary<string, Konto>(StringComparer.OrdinalIgnoreCase);
            Sammler.Versuch(s, "wmi.konten.useraccount", () =>
            {
                string wql = "SELECT Name, SID, Disabled, PasswordRequired FROM Win32_UserAccount WHERE LocalAccount=TRUE"
                           + (rechner != null ? " AND Domain='" + Wql(rechner) + "'" : "");
                var rows = Wmi.Abfrage(Cimv2Ns, wql, 15000);
                foreach (var mo in rows)
                {
                    string sid = Wmi.Str(mo, "SID");
                    int rid = Rid(sid);
                    if (rid < 0) continue;
                    var k = new Konto
                    {
                        Name = Wmi.Str(mo, "Name"),
                        Rid = rid,
                        Deaktiviert = Wmi.Wahr(mo, "Disabled") ?? false,
                        KennwortNoetig = Wmi.Wahr(mo, "PasswordRequired") ?? true,
                        Admin = false,
                        MicrosoftKonto = MitMicrosoftKontoVerbunden(sid),
                    };
                    si.Konten.Add(k);
                    kontoJeSid[sid] = k;
                }
                if (si.Konten.Count == 0) Sammler.Fehler(s, "wmi.konten.useraccount", Fehler.Fehlt, "Win32_UserAccount lieferte kein lokales Konto");
            });
            if (si.Konten.Count == 0) return;

            Sammler.Versuch(s, "wmi.konten.administratoren", () =>
            {
                var gruppen = Wmi.Abfrage(Cimv2Ns, "SELECT Domain, Name FROM Win32_Group WHERE LocalAccount=TRUE AND SID='S-1-5-32-544'", 15000);
                if (gruppen.Count == 0) { Sammler.Fehler(s, "wmi.konten.administratoren", Fehler.Fehlt, "Win32_Group mit SID S-1-5-32-544 nicht gefunden"); return; }
                string dom = Wmi.Str(gruppen[0], "Domain"), name = Wmi.Str(gruppen[0], "Name");
                // Domain und Name kommen aus WMI selbst und gehen nur als Schluessel zurueck - keine Deutung.
                string wql = "ASSOCIATORS OF {Win32_Group.Domain='" + Wql(dom) + "',Name='" + Wql(name) + "'} WHERE AssocClass=Win32_GroupUser Role=GroupComponent";
                var mitglieder = Wmi.Abfrage(Cimv2Ns, wql, 15000);
                // Abgleich ueber die SID: die Mitglieds-Instanz (Win32_UserAccount) traegt dieselbe SID wie das Konto.
                foreach (var mo in mitglieder)
                {
                    string sid = Wmi.Str(mo, "SID");
                    Konto k;
                    if (!string.IsNullOrEmpty(sid) && kontoJeSid.TryGetValue(sid, out k)) k.Admin = true;
                }
            });
        }

        // LsaLookupUserAccountType (Doku: lsalookup/nf-lsalookup-lsalookupuseraccounttype, ab
        // Windows 8; exportiert aus sechost.dll, nicht aus advapi32.dll - gemessen). Liefert
        // LSA_USER_ACCOUNT_TYPE: 1 lokal, 2/3 Domaene, 4 lokales Konto mit Microsoft-Konto
        // verbunden, 5 Entra ID, 6 Internet, 7 Microsoft-Konto. Get-LocalUser zeigt dasselbe als
        // PrincipalSource. Ohne diese Quelle war ein Microsoft-Konto (PasswordRequired=false ist
        // dort normal) ein Fehlalarm "Konto ohne Kennwort" - auf dem Rechner des Betreibers gemessen.
        [DllImport("sechost.dll", ExactSpelling = true)]
        static extern int LsaLookupUserAccountType(byte[] sid, out int accountType);

        const int LsaLocalConnectedUserAccountType = 4;
        const int LsaMsaUserAccountType = 7;

        static bool? MitMicrosoftKontoVerbunden(string sidText)
        {
            try
            {
                var sid = new System.Security.Principal.SecurityIdentifier(sidText);
                var bytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(bytes, 0);
                int typ;
                int status = LsaLookupUserAccountType(bytes, out typ);
                if (status != 0) return null;                       // NTSTATUS ungleich 0: nicht ermittelbar
                if (typ == 0) return null;                          // UnknownUserAccountType
                return typ == LsaLocalConnectedUserAccountType || typ == LsaMsaUserAccountType;
            }
            catch (Exception) { return null; }                      // DLL fehlt (Windows 7) oder SID ungueltig
        }

        static int Rid(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return -1;
            int i = sid.LastIndexOf('-');
            int rid;
            if (i < 0 || !int.TryParse(sid.Substring(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out rid)) return -1;
            return rid;
        }

        // ------------------------------------------------------------------ RDP, SMB1, WER

        const string RdpRichtlinie = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
        const string RdpLokal = @"SYSTEM\CurrentControlSet\Control\Terminal Server";

        /// <summary>
        /// fDenyTSConnections (1 = aus) und UserAuthentication (1 = NLA). Die Richtlinie unter
        /// Policies\...\Terminal Services ueberstimmt die lokalen Werte (Control\Terminal Server
        /// bzw. WinStations\RDP-Tcp): sie wird zuerst gelesen, lokal ist der Rueckfall, und die
        /// Herkunft des Schalters steht als "richtlinie" oder "lokal" im Bild. Beides nicht
        /// erhoeht lesbar (gemessen). Home-Editionen haben keinen Remotedesktop-Host: dort wird
        /// nicht gemessen (Konzept 4.4 "auf Home nicht bewerten"), und das steht als "fehlt" da.
        /// </summary>
        static void Rdp(Systembild s, Sicherheit si)
        {
            if (s.Windows != null && s.Windows.IstHome)
            {
                Sammler.Fehler(s, "registry.rdp", Fehler.Fehlt, "Home-Edition hat keinen Remotedesktop-Host, nicht bewertet");
                return;
            }
            Sammler.Versuch(s, "registry.rdp", () =>
            {
                int? deny = null, nla = null;
                bool denyRichtlinie = false, nlaRichtlinie = false;
                using (var p = Hklm().OpenSubKey(RdpRichtlinie))
                {
                    if (p != null)
                    {
                        deny = RegInt(p, "fDenyTSConnections");
                        denyRichtlinie = deny.HasValue;
                        nla = RegInt(p, "UserAuthentication");
                        nlaRichtlinie = nla.HasValue;
                    }
                }
                using (var k = Hklm().OpenSubKey(RdpLokal))
                {
                    if (k == null && !deny.HasValue) { Sammler.Fehler(s, "registry.rdp", Fehler.Fehlt, "Schlüssel Terminal Server fehlt, keine Richtlinie"); return; }
                    if (k != null)
                    {
                        if (!deny.HasValue) deny = RegInt(k, "fDenyTSConnections");
                        if (!nla.HasValue)
                            using (var w = k.OpenSubKey(@"WinStations\RDP-Tcp")) nla = w == null ? (int?)null : RegInt(w, "UserAuthentication");
                    }
                }
                if (!deny.HasValue) { Sammler.Fehler(s, "registry.rdp", Fehler.Fehlt, "fDenyTSConnections fehlt (weder Richtlinie noch lokal)"); return; }
                si.RdpAn = deny.Value == 0;
                si.RdpHerkunft = denyRichtlinie ? "richtlinie" : "lokal";
                si.RdpNla = nla.HasValue ? nla.Value != 0 : (bool?)null;
                si.RdpNlaHerkunft = nla.HasValue ? (nlaRichtlinie ? "richtlinie" : "lokal") : null;
            });
        }

        /// <summary>
        /// Win32_OptionalFeature (Doku cimwin32prov/win32-optionalfeature, InstallState 1 Enabled,
        /// 2 Disabled, 3 Absent). Nicht erhoeht lesbar (Widerlegungsrunde NS-38); die Cmdlet-Form
        /// Get-WindowsOptionalFeature brauchte Erhoehung. Namen: SMB1Protocol (Eltern),
        /// SMB1Protocol-Client, SMB1Protocol-Server; fehlen die Kinder (aeltere Builds), gilt der Elternwert.
        /// </summary>
        static void Smb1(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "wmi.optionalfeature.smb1", () =>
            {
                var rows = Wmi.Abfrage(Cimv2Ns, "SELECT Name, InstallState FROM Win32_OptionalFeature WHERE Name LIKE 'SMB1Protocol%'", 15000);
                int? eltern = null, client = null, server = null;
                foreach (var mo in rows)
                {
                    string name = Wmi.Str(mo, "Name") ?? "";
                    int? st = Wmi.Ganz(mo, "InstallState");
                    if (name.Equals("SMB1Protocol", StringComparison.OrdinalIgnoreCase)) eltern = st;
                    else if (name.Equals("SMB1Protocol-Client", StringComparison.OrdinalIgnoreCase)) client = st;
                    else if (name.Equals("SMB1Protocol-Server", StringComparison.OrdinalIgnoreCase)) server = st;
                }
                si.Smb1Client = client ?? eltern;
                si.Smb1Server = server ?? eltern;
                if (si.Smb1Client == null && si.Smb1Server == null)
                    Sammler.Fehler(s, "wmi.optionalfeature.smb1", Fehler.Fehlt, "kein Feature SMB1Protocol* in Win32_OptionalFeature");
            });
        }

        const string SmbNs = @"root\Microsoft\Windows\SMB";

        /// <summary>
        /// MSFT_SmbServerConfiguration.GetConfiguration() (die Klasse hinter Get-SmbServerConfiguration,
        /// cdxml): statische Methode, Output ist die Konfigurationsinstanz, ReturnValue 0 = ok.
        /// EnableSMB1Protocol sagt, ob der Server SMB1 wirklich anbietet - das Feature kann
        /// installiert und der Server trotzdem abgeschaltet sein (Set-SmbServerConfiguration
        /// -EnableSMB1Protocol $false). Die Regel warnt nur, wenn Feature UND Server dafuer sprechen.
        /// </summary>
        static void Smb1Konfiguration(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "wmi.smb.serverconfiguration", () =>
            {
                ManagementBaseObject outp;
                try
                {
                    outp = MitZeitgrenze(() =>
                    {
                        var scope = new ManagementScope(SmbNs);
                        scope.Connect();
                        using (var cls = new ManagementClass(scope, new ManagementPath("MSFT_SmbServerConfiguration"), null))
                            return cls.InvokeMethod("GetConfiguration", null, null);
                    }, Wmi.StandardZeitMs, "MSFT_SmbServerConfiguration.GetConfiguration");
                }
                catch (ManagementException ex) when (KlasseFehlt(ex))
                {
                    Sammler.Fehler(s, "wmi.smb.serverconfiguration", Fehler.Fehlt, "MSFT_SmbServerConfiguration nicht vorhanden: " + ex.ErrorCode);
                    return;
                }
                long rv = outp == null ? -1 : (Wmi.Lang(outp, "ReturnValue") ?? -1);
                if (rv != 0) { Sammler.Fehler(s, "wmi.smb.serverconfiguration", Fehler.Ausnahme, "GetConfiguration lieferte " + rv); return; }
                ManagementBaseObject cfg = null;
                try { cfg = outp["Output"] as ManagementBaseObject; } catch (ManagementException) { }
                bool? an = cfg == null ? (bool?)null : Wmi.Wahr(cfg, "EnableSMB1Protocol");
                if (!an.HasValue) { Sammler.Fehler(s, "wmi.smb.serverconfiguration", Fehler.Fehlt, "EnableSMB1Protocol fehlt in GetConfiguration().Output"); return; }
                si.Smb1ServerAktiv = an.Value;
            });
        }

        /// <summary>
        /// Windows Error Reporting\Disabled = 1 (lokal oder als Richtlinie) erklaert fehlende
        /// 1001/1002-Ereignisse in der Stabilitaetsregel; hier nur gemessen, nicht bewertet.
        /// </summary>
        static void Wer(Systembild s, Sicherheit si)
        {
            Sammler.Versuch(s, "registry.wer", () =>
            {
                int? lokal = null, policy = null;
                using (var k = Hklm().OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting")) { if (k != null) lokal = RegInt(k, "Disabled"); else { Sammler.Fehler(s, "registry.wer", Fehler.Fehlt, "Schlüssel Windows Error Reporting fehlt"); return; } }
                using (var p = Hklm().OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting")) { if (p != null) policy = RegInt(p, "Disabled"); }
                si.WerDeaktiviert = (lokal ?? 0) == 1 || (policy ?? 0) == 1;
            });
        }

        // ------------------------------------------------------------------ Helfer

        static RegistryKey Hklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        static int? RegInt(RegistryKey k, string name)
        {
            try
            {
                object v = k.GetValue(name);
                if (v == null) return null;
                if (v is int) return (int)v;
                if (v is long) { long l = (long)v; return l > int.MaxValue || l < int.MinValue ? (int?)null : (int)l; }
                int n;
                if (v is string && int.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
                return null;
            }
            catch (Exception) { return null; }
        }

        static string RegStr(RegistryKey k, string name)
        {
            try { object v = k.GetValue(name); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        /// <summary>Namespace oder Klasse gibt es auf diesem System nicht: "fehlt", nicht "ausnahme".</summary>
        static bool KlasseFehlt(ManagementException ex)
        {
            return ex.ErrorCode == ManagementStatus.InvalidNamespace
                || ex.ErrorCode == ManagementStatus.InvalidClass
                || ex.ErrorCode == ManagementStatus.NotFound;
        }

        /// <summary>WQL-Zeichenkette: Backslash und Apostroph werden maskiert.</summary>
        static string Wql(string s)
        {
            return s == null ? "" : s.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        static string SafeMachineName()
        {
            try { return Environment.MachineName; } catch (Exception) { return null; }
        }

        /// <summary>
        /// COM-Aufruf mit Zeitgrenze, nach dem Muster von Wmi.Abfrage: laeuft die Zeit ab, wird
        /// der Thread aufgegeben und der Aufrufer bekommt eine TimeoutException (fehlerliste:zeit).
        /// </summary>
        static T MitZeitgrenze<T>(Func<T> f, int zeitMs, string was)
        {
            T ergebnis = default(T);
            Exception fehler = null;
            var t = new System.Threading.Thread(() =>
            {
                try { ergebnis = f(); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "com:" + was };
            t.Start();
            if (!t.Join(zeitMs)) throw new TimeoutException("COM-Aufruf überschritt " + zeitMs + " ms: " + was);
            if (fehler != null) throw fehler;
            return ergebnis;
        }
    }
}
