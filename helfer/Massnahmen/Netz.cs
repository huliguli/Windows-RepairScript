using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Netz-Massnahmen des Meilensteins 3 (docs/M3-ENTWURF.md, Abschnitt 1 und 3):
    ///   netz.dnscache.leeren     Stufe 1  MSFT_DNSClientCache.Clear() (root\StandardCimv2, statisch)
    ///   netz.dhcp.erneuern       Stufe 1  Win32_NetworkAdapterConfiguration.RenewDHCPLease(), nur bei DHCPEnabled
    ///   netz.dns.zuruecksetzen   Stufe 2  SetDNSServerSearchOrder() ohne Liste; Sicherung der festen Liste
    /// und des Meilensteins 4 (docs/M4-ENTWURF.md, Abschnitt 1 und 3):
    ///   netz.hosts.bereinigen    Stufe 2  Zeilen mit Regeln.Netz.HostsZeileUmleitung aus %SystemRoot%\System32\drivers\etc\hosts
    ///                                     entfernen (nur bei Standard-DataBasePath); Sicherung: der ganze alte Text (inhalt, bis 256 KB)
    ///   netz.winsock.zuruecksetzen Stufe 3  netsh.exe winsock reset; Sicherung: netsh winsock show catalog als Beleg (katalog); kein Rueckweg
    ///   netz.ip.zuruecksetzen    Stufe 3  netsh.exe int ip reset &lt;Protokoll&gt;; Sicherung: die festen IPv4-Adapter (adapter);
    ///                                     Rueckweg ueber EnableStatic, SetGateways, SetDNSServerSearchOrder
    ///
    /// Der Parameter adapter ist der InterfaceIndex aus dem Systembild (MSFT_NetAdapter.InterfaceIndex,
    /// kern\Systembild.cs Adapter.Index). Win32_NetworkAdapterConfiguration hat daneben ein eigenes
    /// Feld Index (die Nummer der Konfiguration), das nicht dasselbe ist: gesucht wird deshalb immer
    /// ueber InterfaceIndex. Alles ueber System.Management, kein ipconfig, kein Skriptprozess; netsh.exe
    /// nur ueber Werkzeuge.Lauf mit festem Dateinamen aus Environment.SystemDirectory.
    ///
    /// Trockenlauf: Pruefen und Lesezugriffe laufen (der Adapter wird gelesen, die Registry gelesen),
    /// jede Schreibstelle meldet "(trocken) ..." und die Sicherungsdatei bleibt ungeschrieben.
    /// </summary>
    public static partial class MassnahmenM3
    {
        static partial void Netz(List<Massnahme> l) { NetzMassnahmen.Eintragen(l); }

        /// <summary>true, wenn ZurueckNetz die Kennung der Sicherung kennt (netz.dns.zuruecksetzen, netz.hosts.bereinigen, netz.ip.zuruecksetzen).</summary>
        public static bool ZurueckNetzBekannt(string kennung) { return NetzMassnahmen.ZurueckBekannt(kennung); }

        /// <summary>
        /// Prueft die Werte einer Netz-Sicherung, bevor der Rueckweg sie anfasst (Vertrag 8.3, M4 3.3):
        /// null = gueltig, sonst der Grund (Rueckweg.cs lehnt den Plan damit ab). DNS: adapter (ganze
        /// Zahl ab 0, gleich InterfaceIndex, wenn beide da sind), SettingID ({GUID}-Form) und die Listen
        /// NameServer und DNSServerSearchOrder (IPv4-Adressen in Punktschreibweise, durch ; getrennt;
        /// leer = DHCP). hosts: inhalt bis 256 KB, nur druckbare Zeichen, Tabulator und Zeilenumbrueche,
        /// keine NUL. ip: adapter je Zeile SettingID|Beschreibung|IP;IP|Maske;Maske|Gateway;Gateway|DNS;DNS
        /// mit SettingID als {GUID} und allen Adressen als IPv4 (leer = kein fester Adapter). Ein
        /// fehlender oder fremder Wert ist ein Grund.
        /// </summary>
        public static string ZurueckNetzPruefen(Sicherung si) { return NetzMassnahmen.ZurueckPruefen(si); }

        /// <summary>
        /// Rueckweg einer Netz-Sicherung (Kennung rueckweg, Datei ueber Sicherungsdatei.Lesen): stellt die
        /// gemerkten Werte zurueck, schreibt Zeilen und Nachweiszeile selbst. true = zurueckgestellt,
        /// false = nicht (mit Problem); Trockenlauf nur die (trocken)-Zeile; fremde Kennung → ArgumentException.
        /// Prueft die Werte selbst noch einmal ueber ZurueckNetzPruefen (ein Grund → nicht bestanden).
        /// </summary>
        public static bool ZurueckNetz(Ausfuehrungskontext k, Sicherung si) { return NetzMassnahmen.Zurueck(k, si); }
    }

    /// <summary>
    /// Die Netz-Massnahmen selbst. Eigene Klasse statt Teil der partiellen MassnahmenM3, damit private
    /// Hilfen (AdapterLesen, IstApipa ...) nicht mit gleichnamigen Hilfen anderer Bereichsdateien
    /// zusammenstossen; nach aussen zaehlen nur Eintragen, ZurueckBekannt, ZurueckPruefen und Zurueck.
    /// </summary>
    internal static class NetzMassnahmen
    {
        const string NetzCimv2 = @"root\cimv2";
        const string NetzStandardCimv2 = @"root\StandardCimv2";
        const string NetzKlasse = "Win32_NetworkAdapterConfiguration";
        const string TcpipInterfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
        const string TcpipParameter = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
        const int NetzWmiMs = 2 * 60 * 1000;    // RenewDHCPLease wartet selbst auf den Server
        const int NetzLeseMs = 30000;
        const int NetshMs = 5 * 60 * 1000;
        const int HostsMaxBytes = 256 * 1024;   // Vertrag M4 1: groessere Datei → Ablehnung
        const int KatalogMaxZeichen = 64 * 1024; // Vertrag M4 1: Beleg hoechstens 64 KB
        const int AdapterMax = 64;
        const int ListeMax = 10;

        public const string KDnsCache = "netz.dnscache.leeren";
        public const string KDhcp = "netz.dhcp.erneuern";
        public const string KDnsZurueck = "netz.dns.zuruecksetzen";
        public const string KHosts = "netz.hosts.bereinigen";
        public const string KWinsock = "netz.winsock.zuruecksetzen";
        public const string KIpReset = "netz.ip.zuruecksetzen";

        /// <summary>Schluessel der Sicherungen (Vertrag M4 1): hosts vorher {inhalt}, winsock vorher {katalog}, ip vorher {adapter}.</summary>
        public const string HostsInhalt = "inhalt", WinsockKatalog = "katalog", IpAdapter = "adapter";

        public static void Eintragen(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu(KDnsCache, "DNS-Zwischenspeicher leeren", 1, s => null, DnsCacheLeeren));
            l.Add(Massnahmenbau.Neu(KDhcp, "Adresse neu anfordern (DHCP)", 1, s => AdapterPruefen(s), DhcpErneuern));
            l.Add(Massnahmenbau.Neu(KDnsZurueck, "Namensserver auf automatisch zurücksetzen", 2, s => AdapterPruefen(s), DnsZuruecksetzen));
            l.Add(Massnahmenbau.Neu(KHosts, "hosts-Datei bereinigen", 2, s => null, HostsBereinigen));
            l.Add(Massnahmenbau.Neu(KWinsock, "Winsock zurücksetzen", 3, s => null, WinsockZuruecksetzen));
            l.Add(Massnahmenbau.Neu(KIpReset, "IP-Stack zurücksetzen", 3, s => null, IpZuruecksetzen));
        }

        static string Netsh() { return Path.Combine(Environment.SystemDirectory, "netsh.exe"); }

        // ------------------------------------------------------------------ Pruefen

        /// <summary>
        /// adapter: ganze Zahl ab 0, und die Konfiguration mit diesem InterfaceIndex muss existieren,
        /// IP fuehren und DHCP an haben. Beide Adapter-Massnahmen brauchen DHCP: RenewDHCPLease hat
        /// sonst nichts zu erneuern, und SetDNSServerSearchOrder() ohne Liste liesse einen Anschluss
        /// mit fester Adresse ganz ohne Namensserver zurueck (Grundsatz 4: kein DHCP-Renew auf
        /// statischer Adresse). Ein haengender WMI-Dienst wirft; Ausfuehrung lehnt den Plan dann ab.
        /// </summary>
        static string AdapterPruefen(PlanSchritt s)
        {
            int idx;
            string roh = s.Wert("adapter");
            if (!Schritte.GanzeZahl(roh, 0, int.MaxValue, out idx))
                return "Der Adapter muss als ganze Zahl ab 0 angegeben sein (InterfaceIndex), nicht „" + Massnahmenbau.Kurz(roh) + "“.";
            NetzAdapter a = AdapterLesen(idx);
            if (a == null) return "Adapter Nr. " + idx + " gibt es nicht (keine IP-Konfiguration mit InterfaceIndex " + idx + ").";
            if (!a.IpAn) return "Adapter Nr. " + idx + " (" + a.Anzeige + ") führt kein IP; es gibt nichts zu erneuern.";
            if (!a.Dhcp) return "Adapter Nr. " + idx + " (" + a.Anzeige + ") hat eine feste Adresse (DHCP aus); diese Maßnahme gilt nur für Anschlüsse mit automatischer Adresse.";
            return null;
        }

        // ------------------------------------------------------------------ DNS-Cache

        static void DnsCacheLeeren(PlanSchritt s, Ausfuehrungskontext k)
        {
            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI " + NetzStandardCimv2 + " MSFT_DNSClientCache.Clear()", "dim");
                Nachweise.Trocken(k, KDnsCache, "Der DNS-Zwischenspeicher wäre geleert worden; nichts geändert.");
                return;
            }
            // Beleg in zwei Teilen: die Zahl der Eintraege vor und nach dem Aufruf (SELECT * FROM
            // MSFT_DNSClientCache) und der Rueckgabewert. Die Methode ist als uint32 Clear() deklariert,
            // liefert aber auf Windows 11 (Build 26200, gemessen 18.09.2026) KEIN Ausgabeobjekt: Wmi2.Ganz
            // ist dann null, obwohl der Cache geleert wurde. Deshalb zaehlt ein fehlender Rueckgabewert
            // nicht als Ablehnung; ein Rueckgabewert != 0 schon. Der Cache fuellt sich sofort wieder,
            // also gilt: war er vorher nicht leer und ist er danach nicht kleiner, wurde nichts geleert.
            int? vorher = DnsCacheEintraege();
            k.Schreibe("›  WMI " + NetzStandardCimv2 + " MSFT_DNSClientCache.Clear()" + (vorher.HasValue ? "  (" + vorher.Value + " Einträge)" : ""), "dim");
            long? code;
            try { code = Wmi2.Ganz(Wmi2.StatischMitZeit(60000, NetzStandardCimv2, "MSFT_DNSClientCache", "Clear")); }
            catch (Exception ex)
            {
                AppLog.Warn("MSFT_DNSClientCache.Clear: " + ex.Message);
                Nachweise.NichtBestanden(k, KDnsCache, "Der DNS-Zwischenspeicher wurde nicht geleert: " + ex.Message);
                return;
            }
            int? nachher = DnsCacheEintraege();
            string zaehlung = vorher.HasValue && nachher.HasValue ? " (" + vorher.Value + " Einträge vorher, " + nachher.Value + " danach)" : "";
            if (code.HasValue && code.Value != 0)
                Nachweise.NichtBestanden(k, KDnsCache, "Windows hat das Leeren des DNS-Zwischenspeichers abgelehnt (Rückgabewert " + code.Value + ")" + zaehlung + ".");
            else if (vorher.HasValue && nachher.HasValue && vorher.Value > 0 && nachher.Value >= vorher.Value)
                Nachweise.NichtBestanden(k, KDnsCache, "Der DNS-Zwischenspeicher ist nach dem Aufruf nicht kleiner" + zaehlung + "; der Aufruf zählt nicht als geleert.");
            else
                Nachweise.Bestanden(k, KDnsCache, "Der DNS-Zwischenspeicher wurde geleert"
                    + (code.HasValue ? "; Windows meldete Rückgabewert 0" : "; der Aufruf lief ohne Fehler, Windows liefert hier keinen Rückgabewert") + zaehlung + ".");
        }

        /// <summary>Zahl der Eintraege im DNS-Zwischenspeicher (MSFT_DNSClientCache); null, wenn nicht lesbar.</summary>
        static int? DnsCacheEintraege()
        {
            try
            {
                List<ManagementObject> rows = Sammler.Quellen.Wmi.Abfrage(NetzStandardCimv2, "SELECT Entry FROM MSFT_DNSClientCache", 30000);
                int n = rows.Count;
                foreach (ManagementObject mo in rows) mo.Dispose();
                return n;
            }
            catch (Exception ex)
            {
                AppLog.Warn("MSFT_DNSClientCache zählen: " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ DHCP

        static void DhcpErneuern(PlanSchritt s, Ausfuehrungskontext k)
        {
            int idx;
            Schritte.GanzeZahl(s.Wert("adapter"), 0, int.MaxValue, out idx);
            NetzAdapter a = AdapterLesen(idx);
            if (a == null) { Nachweise.NichtBestanden(k, KDhcp, "Adapter Nr. " + idx + " ist nach der Prüfung nicht mehr auffindbar; nichts erneuert."); return; }
            if (!a.Dhcp) { Nachweise.NichtBestanden(k, KDhcp, "Adapter Nr. " + idx + " (" + a.Anzeige + ") hat inzwischen eine feste Adresse; nichts erneuert."); return; }
            string vorher = a.Ipv4();
            k.Schreibe("   Adapter " + idx + " (" + a.Anzeige + "): Adresse vorher " + (vorher ?? "keine IPv4-Adresse")
                       + (string.IsNullOrEmpty(a.DhcpServer) ? "" : ", DHCP-Server " + a.DhcpServer), "dim");

            string aufruf = "WMI " + NetzKlasse + "[InterfaceIndex=" + idx + "].RenewDHCPLease()";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KDhcp, "Die Adresse von Adapter " + idx + " wäre per DHCP neu angefordert worden; sie bleibt " + (vorher ?? "leer") + ".");
                return;
            }
            k.Schreibe("›  " + aufruf, "dim");
            long? code;
            try { code = Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, "InterfaceIndex = " + idx, "RenewDHCPLease")); }
            catch (Exception ex)
            {
                AppLog.Warn("RenewDHCPLease auf InterfaceIndex " + idx + ": " + ex.Message);
                Nachweise.NichtBestanden(k, KDhcp, "Die Adresse von Adapter " + idx + " wurde nicht erneuert: " + ex.Message);
                return;
            }
            if (code.HasValue && code.Value != 0 && code.Value != 1)
            {
                Nachweise.NichtBestanden(k, KDhcp, "Windows hat das Erneuern für Adapter " + idx + " abgelehnt: " + NetzRueckgabe(code.Value) + " (Rückgabewert " + code.Value + ").");
                return;
            }
            if (code == 1) k.Werte["neustart"] = "1";

            // Der Server antwortet meist sofort; die neue Adresse erscheint in WMI mit kurzer
            // Verzoegerung. Bis zu 5 Sekunden nachlesen, dann gilt, was da ist.
            string nachher = vorher;
            for (int i = 0; i < 5; i++)
            {
                NetzAdapter b = AdapterLesen(idx);
                nachher = b == null ? null : b.Ipv4();
                if (nachher != null && !IstApipa(nachher)) break;
                if (k.IstAbgebrochen) return;
                Thread.Sleep(1000);
            }
            if (nachher != null && !IstApipa(nachher))
                Nachweise.Bestanden(k, KDhcp, "Adapter " + idx + " hat nach dem Erneuern die Adresse " + nachher
                    + (code == 1 ? "; Windows bittet um 1 Neustart" : "") + ".");
            else
                Nachweise.NichtBestanden(k, KDhcp, "Adapter " + idx + " hat nach dem Erneuern weiterhin "
                    + (nachher == null ? "keine IPv4-Adresse" : "nur die Notadresse " + nachher) + "; kein DHCP-Server hat geantwortet.");
        }

        // ------------------------------------------------------------------ DNS zuruecksetzen

        /// <summary>
        /// Feste Namensserver auf einem DHCP-Anschluss loeschen: SetDNSServerSearchOrder() ohne Liste
        /// leert Tcpip\Parameters\Interfaces\{GUID}\NameServer, danach gilt DhcpNameServer vom Router
        /// (Konzept 4, "widerlegt: Put() auf MSFT_DNSClientServerAddress"). Vorher wird die feste Liste
        /// in die Sicherung geschrieben (Rueckweg: dieselbe Methode MIT Liste, ZurueckNetz). Nur IPv4:
        /// die Methode kennt kein IPv6; eine feste IPv6-Liste (Tcpip6\...\NameServer) bleibt stehen und
        /// wird als Warnung genannt.
        /// </summary>
        static void DnsZuruecksetzen(PlanSchritt s, Ausfuehrungskontext k)
        {
            int idx;
            Schritte.GanzeZahl(s.Wert("adapter"), 0, int.MaxValue, out idx);
            NetzAdapter a = AdapterLesen(idx);
            if (a == null) { Nachweise.NichtBestanden(k, KDnsZurueck, "Adapter Nr. " + idx + " ist nach der Prüfung nicht mehr auffindbar; nichts geändert."); return; }
            if (!a.Dhcp) { Nachweise.NichtBestanden(k, KDnsZurueck, "Adapter Nr. " + idx + " (" + a.Anzeige + ") hat inzwischen eine feste Adresse; ohne DHCP gäbe es danach keinen Namensserver. Nichts geändert."); return; }

            string fest = NameServerLesen(a.SettingId, false);
            string fest6 = NameServerLesen(a.SettingId, true);
            string liste = string.Join(";", a.DnsServer);
            k.Schreibe("   Adapter " + idx + " (" + a.Anzeige + "): Namensserver vorher " + (liste.Length > 0 ? liste.Replace(";", ", ") : "keine")
                       + (string.IsNullOrWhiteSpace(fest) ? " (keine feste IPv4-Eintragung)" : " (fest eingetragen: " + fest + ")"), "dim");
            if (!string.IsNullOrWhiteSpace(fest6))
                k.Schreibe("   Für IPv6 ist auf diesem Adapter zusätzlich " + fest6 + " fest eingetragen; das bleibt stehen (diese Maßnahme setzt nur IPv4 zurück).", "warn");
            if (string.IsNullOrWhiteSpace(fest))
            {
                if (!string.IsNullOrWhiteSpace(fest6))
                {
                    // Der Befund kam von der festen IPv6-Liste; die kann diese Massnahme nicht
                    // zuruecksetzen. Bestanden ohne Aenderung waere eine Luege (Vertrag 8.7).
                    Nachweise.NichtBestanden(k, KDnsZurueck, "Auf Adapter " + idx + " ist nur IPv6 fest eingetragen (" + fest6 + "); diese Maßnahme setzt nur IPv4 zurück. Nichts geändert.");
                    return;
                }
                // Ziel schon erreicht: kein Eingriff, keine Sicherung, kein Problem.
                Nachweise.Bestanden(k, KDnsZurueck, "Auf Adapter " + idx + " ist kein IPv4-Namensserver fest eingetragen; die Namensserver kommen schon per DHCP"
                    + (liste.Length > 0 ? " (" + liste.Replace(";", ", ") + ")" : "") + ". Nichts geändert.");
                return;
            }

            var vorher = Sicherungsdatei.Werte(
                "DNSServerSearchOrder", liste,
                "NameServer", fest,
                "InterfaceIndex", idx.ToString(CultureInfo.InvariantCulture),
                "SettingID", a.SettingId ?? "",
                "Beschreibung", a.Anzeige);
            try { Sicherungsdatei.Schreiben(k, KDnsZurueck, s.Parameter, vorher); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KDnsZurueck, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }

            string aufruf = "WMI " + NetzKlasse + "[InterfaceIndex=" + idx + "].SetDNSServerSearchOrder() ohne Liste";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KDnsZurueck, "Die feste Liste " + fest + " auf Adapter " + idx + " wäre gelöscht worden; sie bleibt stehen.");
                return;
            }
            k.Schreibe("›  " + aufruf, "dim");
            long? code;
            try { code = Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, "InterfaceIndex = " + idx, "SetDNSServerSearchOrder")); }
            catch (Exception ex)
            {
                AppLog.Warn("SetDNSServerSearchOrder auf InterfaceIndex " + idx + ": " + ex.Message);
                Nachweise.NichtBestanden(k, KDnsZurueck, "Die Namensserver von Adapter " + idx + " wurden nicht zurückgesetzt: " + ex.Message);
                return;
            }
            if (code.HasValue && code.Value != 0 && code.Value != 1)
            {
                Nachweise.NichtBestanden(k, KDnsZurueck, "Windows hat das Zurücksetzen für Adapter " + idx + " abgelehnt: " + NetzRueckgabe(code.Value) + " (Rückgabewert " + code.Value + ").");
                return;
            }
            if (code == 1) k.Werte["neustart"] = "1";

            string festNachher = NameServerLesen(a.SettingId, false);
            NetzAdapter b = AdapterLesen(idx);
            string listeNachher = b == null ? "" : string.Join(", ", b.DnsServer);
            if (string.IsNullOrWhiteSpace(festNachher))
                Nachweise.Bestanden(k, KDnsZurueck, "Die Namensserver von Adapter " + idx + " kommen jetzt per DHCP"
                    + (listeNachher.Length > 0 ? ": " + listeNachher : "; die Liste ist noch leer, bis der Router antwortet")
                    + ". Rückgängig geht über die Sicherung.");
            else
                Nachweise.NichtBestanden(k, KDnsZurueck, "Auf Adapter " + idx + " ist weiterhin " + festNachher + " fest eingetragen; das Zurücksetzen hat nicht gegriffen.");
        }

        // ------------------------------------------------------------------ hosts (Vertrag M4 1)

        /// <summary>
        /// Die hosts-Datei liegt fest unter %SystemRoot%\System32\drivers\etc (Environment.SystemDirectory,
        /// nie ein Parameter). Windows liest sie aber aus Tcpip\Parameters\DataBasePath; weicht der
        /// vom Standard ab, liest der Resolver eine andere Datei, und diese Massnahme aenderte die
        /// falsche. Deshalb vorher pruefen; ein fehlender Wert ist der dokumentierte Standard, ein
        /// unlesbarer Schluessel ist kein bestandener Nachweis (Ablehnung). null = Standard, sonst der Grund.
        /// </summary>
        static string HostsPfadPruefen()
        {
            string soll = Path.Combine(Environment.SystemDirectory, @"drivers\etc");
            string ist;
            try
            {
                using (RegistryKey key = Reg.Hklm(TcpipParameter, false))
                {
                    if (key == null) return "Der Schlüssel Tcpip\\Parameters fehlt in der Registrierung; der Ort der hosts-Datei ist nicht prüfbar.";
                    object roh = key.GetValue("DataBasePath");
                    if (roh == null) return null;
                    ist = Environment.ExpandEnvironmentVariables(Convert.ToString(roh, CultureInfo.InvariantCulture) ?? "");
                }
            }
            catch (Exception ex) { return "Tcpip\\Parameters\\DataBasePath ließ sich nicht lesen (" + ex.Message + "); der Ort der hosts-Datei ist nicht prüfbar."; }
            string a, b;
            try
            {
                a = Path.GetFullPath(ist.Trim()).TrimEnd(Path.DirectorySeparatorChar);
                b = Path.GetFullPath(soll).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch (Exception) { return "Tcpip\\Parameters\\DataBasePath nennt keinen lesbaren Pfad („" + Massnahmenbau.Kurz(ist) + "“); der Ort der hosts-Datei ist nicht prüfbar."; }
            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return "Tcpip\\Parameters\\DataBasePath zeigt auf „" + Massnahmenbau.Kurz(ist) + "“ statt auf " + soll + "; Windows liest die hosts-Datei von dort, diese Maßnahme fasst nur den Standardort an.";
            return null;
        }

        static string HostsPfad() { return Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts"); }

        // Byte-treu lesen und schreiben: Latin-1 bildet jedes Byte auf genau ein Zeichen ab und
        // zurueck, unabhaengig von der Systemcodepage. Ein UTF-8-BOM wird so zu drei Zeichen am
        // Anfang der ersten Zeile (ZeileIstUmleitung ueberliest sie); alles, was bleibt, wird
        // unveraendert zurueckgeschrieben, und die Sicherung traegt den Text ebenso byte-treu.
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        static readonly string Utf8Bom = Latin1.GetString(new byte[] { 0xEF, 0xBB, 0xBF });

        /// <summary>Liest die hosts-Datei (hoechstens 256 KB, kein Reparse-Punkt); wirft mit dem Grund.</summary>
        static string HostsLesen(string pfad, out FileAttributes attr)
        {
            attr = File.GetAttributes(pfad);
            if ((attr & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("die Datei ist eine Verknüpfung auf einen anderen Ort");
            long laenge = new FileInfo(pfad).Length;
            if (laenge > HostsMaxBytes) throw new InvalidOperationException("die Datei ist " + laenge + " Byte groß; mehr als " + HostsMaxBytes + " Byte werden nicht gesichert und nicht angefasst");
            return Latin1.GetString(File.ReadAllBytes(pfad));
        }

        /// <summary>Schreibt den Text byte-treu; ein Schreibschutz wird vorher weggenommen und danach wieder gesetzt (Vertrag M4 1).</summary>
        static void HostsSchreiben(string pfad, string text, FileAttributes attr)
        {
            bool schreibschutz = (attr & FileAttributes.ReadOnly) != 0;
            if (schreibschutz) File.SetAttributes(pfad, attr & ~FileAttributes.ReadOnly);
            try { File.WriteAllBytes(pfad, Latin1.GetBytes(text)); }
            finally
            {
                if (schreibschutz)
                {
                    try { File.SetAttributes(pfad, attr); }
                    catch (Exception ex) { AppLog.Warn("Schreibschutz der hosts-Datei nicht wieder gesetzt: " + ex.Message); }
                }
            }
        }

        /// <summary>Die Zeilen samt ihrem Zeilenende (\n bleibt an der Zeile), damit der Rest byte-treu zurueckgeschrieben wird.</summary>
        static List<string> ZeilenMitEnde(string text)
        {
            var l = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                l.Add(text.Substring(start, i - start + 1));
                start = i + 1;
            }
            if (start < text.Length) l.Add(text.Substring(start));
            return l;
        }

        /// <summary>
        /// Dieselbe Zerlegung wie sammler\Quellen\Netzquelle.cs (Kommentar ab #, Leerraum, Wildcard-
        /// Zeilen wirkungslos, erstes Feld eine IP-Adresse, jedes weitere ein Host); die Entscheidung
        /// je (ip, host) trifft allein Kern.Regeln.Netz.HostsZeileUmleitung, damit Befund und Eingriff
        /// dieselben Zeilen meinen. Eine Zeile mit mehreren Hosts faellt, wenn einer trifft.
        /// </summary>
        static bool ZeileIstUmleitung(string roh, bool ersteZeile)
        {
            string z = roh;
            if (ersteZeile && z.StartsWith(Utf8Bom, StringComparison.Ordinal)) z = z.Substring(Utf8Bom.Length);
            int k = z.IndexOf('#');
            if (k >= 0) z = z.Substring(0, k);
            z = z.Trim();
            if (z.Length == 0 || z.IndexOf('*') >= 0) return false;
            string[] t = z.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2) return false;
            System.Net.IPAddress ip;
            if (!System.Net.IPAddress.TryParse(t[0], out ip)) return false;
            for (int i = 1; i < t.Length; i++)
                if (Kern.Regeln.Netz.HostsZeileUmleitung(t[0], t[i])) return true;
            return false;
        }

        /// <summary>Zaehlt die Umleitungszeilen und baut den Text ohne sie; entfernt nennt bis zu 10 davon fuer die Zeilen.</summary>
        static int HostsSieben(string text, out string neu, List<string> entfernt)
        {
            List<string> zeilen = ZeilenMitEnde(text);
            var sb = new StringBuilder(text.Length);
            int treffer = 0;
            for (int i = 0; i < zeilen.Count; i++)
            {
                if (ZeileIstUmleitung(zeilen[i], i == 0))
                {
                    treffer++;
                    if (entfernt != null && entfernt.Count < 10) entfernt.Add(Massnahmenbau.Kurz(zeilen[i].Trim()));
                }
                else sb.Append(zeilen[i]);
            }
            neu = sb.ToString();
            return treffer;
        }

        static void HostsBereinigen(PlanSchritt s, Ausfuehrungskontext k)
        {
            string grund = HostsPfadPruefen();
            if (grund != null)
            {
                Nachweise.NichtBestanden(k, KHosts, grund + " Nichts geändert.");
                return;
            }
            string pfad = HostsPfad();
            string text;
            FileAttributes attr;
            try { text = HostsLesen(pfad, out attr); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KHosts, "Die hosts-Datei " + pfad + " ließ sich nicht lesen: " + ex.Message + ". Nichts geändert.");
                return;
            }
            string neu;
            var entfernt = new List<string>();
            int treffer = HostsSieben(text, out neu, entfernt);
            int gesamt = ZeilenMitEnde(text).Count;
            k.Schreibe("   hosts-Datei: " + gesamt + " Zeilen, davon " + treffer + " Umleitung" + (treffer == 1 ? "" : "en")
                       + ((attr & FileAttributes.ReadOnly) != 0 ? ", schreibgeschützt" : "") + " (" + text.Length + " Byte)", "dim");
            foreach (string z in entfernt) k.Schreibe("   entfernen: " + z, "dim");
            if (treffer == 0)
            {
                Nachweise.Bestanden(k, KHosts, "Die hosts-Datei leitet keine Update-, Schutz- oder Testadresse um (" + gesamt + " Zeilen geprüft); nichts geändert.");
                return;
            }

            try { Sicherungsdatei.Schreiben(k, KHosts, s.Parameter, Sicherungsdatei.Werte(HostsInhalt, text)); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KHosts, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }

            string aufruf = "Datei " + pfad + " neu schreiben (" + treffer + " von " + gesamt + " Zeilen entfernt)";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, KHosts, treffer + " Zeile" + (treffer == 1 ? "" : "n") + " wäre" + (treffer == 1 ? "" : "n") + " aus der hosts-Datei entfernt worden; sie bleibt unverändert.");
                return;
            }
            k.Schreibe("›  " + aufruf, "dim");
            try { HostsSchreiben(pfad, neu, attr); }
            catch (Exception ex)
            {
                AppLog.Warn("hosts schreiben: " + ex.Message);
                Nachweise.NichtBestanden(k, KHosts, "Die hosts-Datei ließ sich nicht schreiben: " + ex.Message + "; die Sicherung liegt unter den Sicherungen.");
                return;
            }
            string danach;
            FileAttributes attrDanach;
            try { danach = HostsLesen(pfad, out attrDanach); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KHosts, "Die hosts-Datei ließ sich nach dem Schreiben nicht lesen: " + ex.Message);
                return;
            }
            string egal;
            int rest = HostsSieben(danach, out egal, null);
            if (rest == 0)
                Nachweise.Bestanden(k, KHosts, treffer + " Zeile" + (treffer == 1 ? "" : "n") + " aus der hosts-Datei entfernt; sie leitet keine Update-, Schutz- oder Testadresse mehr um ("
                    + ZeilenMitEnde(danach).Count + " Zeilen bleiben). Rückgängig geht über die Sicherung.");
            else
                Nachweise.NichtBestanden(k, KHosts, "Nach dem Schreiben leitet die hosts-Datei weiterhin " + rest + " Adresse" + (rest == 1 ? "" : "n") + " um; das Bereinigen hat nicht gegriffen.");
        }

        // ------------------------------------------------------------------ Winsock (Vertrag M4 1)

        /// <summary>
        /// netsh winsock reset entfernt jede Drittkomponente (LSP) aus dem Katalog; einen Rueckweg
        /// gibt es nicht (rueckweg.moeglich = 0). Vorher wird der Katalog als Beleg gesichert
        /// (netsh winsock show catalog, Text bis 64 KB); ohne Beleg wird nichts zurueckgesetzt.
        /// Wirksam nach 1 Neustart (neustart = 1).
        /// </summary>
        static void WinsockZuruecksetzen(PlanSchritt s, Ausfuehrungskontext k)
        {
            string netsh = Netsh();
            string katalog;
            bool gekuerzt;
            int code = Prozessausgabe.Sammeln(k, netsh, "winsock show catalog", NetshMs, KatalogMaxZeichen, out katalog, out gekuerzt);
            if (!k.Trocken)
            {
                if (code != 0)
                {
                    Nachweise.NichtBestanden(k, KWinsock, "netsh winsock show catalog meldete Exit " + code + "; ohne Beleg des Katalogs wird nichts zurückgesetzt.");
                    return;
                }
                if (katalog.Trim().Length == 0)
                {
                    Nachweise.NichtBestanden(k, KWinsock, "netsh winsock show catalog lieferte keine Ausgabe; ohne Beleg des Katalogs wird nichts zurückgesetzt.");
                    return;
                }
                k.Schreibe("   Winsock-Katalog: " + ZeilenMitEnde(katalog).Count + " Zeilen als Beleg in der Sicherung" + (gekuerzt ? " (auf " + (KatalogMaxZeichen / 1024) + " KB gekürzt)" : ""), "dim");
            }
            try { Sicherungsdatei.Schreiben(k, KWinsock, s.Parameter, Sicherungsdatei.Werte(WinsockKatalog, katalog)); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KWinsock, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }
            // Die Datei ist Beleg, kein Rueckweg (Vertrag M4 3.3): der Host zeigt keinen Knopf.
            k.Werte["rueckweg.moeglich"] = "0";

            string tail;
            code = Werkzeuge.Lauf(k, netsh, "winsock reset", NetshMs, null, false, out tail);
            if (k.Trocken)
            {
                Nachweise.Trocken(k, KWinsock, "Der Winsock-Katalog wäre zurückgesetzt worden (wirksam nach 1 Neustart); nichts geändert.");
                return;
            }
            if (code != 0)
            {
                Nachweise.NichtBestanden(k, KWinsock, "netsh winsock reset meldete Exit " + code + "; der Katalog gilt nicht als zurückgesetzt.");
                return;
            }
            k.Werte["neustart"] = "1";
            k.Schreibe("   Die Änderung wirkt erst nach 1 Neustart.", "warn");
            Nachweise.Bestanden(k, KWinsock, "netsh hat den Winsock-Katalog zurückgesetzt (Exit 0); der alte Katalog steht als Beleg in der Sicherung. Wirksam nach 1 Neustart.");
        }

        // ------------------------------------------------------------------ IP-Stack (Vertrag M4 1)

        /// <summary>Ein Adapter mit fester IPv4-Konfiguration, wie er in der Sicherung steht (eine Zeile).</summary>
        sealed class FesterAdapter
        {
            public string SettingId;
            public string Beschreibung;
            public List<string> Ips = new List<string>();
            public List<string> Masken = new List<string>();
            public List<string> Gateways = new List<string>();
            public List<string> Dns = new List<string>();

            public string Anzeige { get { return string.IsNullOrEmpty(Beschreibung) ? SettingId : Beschreibung; } }

            /// <summary>SettingID|Beschreibung|IP;IP|Maske;Maske|Gateway;Gateway|DNS;DNS (Vertrag M4 1).</summary>
            public string Zeile()
            {
                return SettingId + "|" + Beschreibung + "|" + string.Join(";", Ips) + "|" + string.Join(";", Masken) + "|" + string.Join(";", Gateways) + "|" + string.Join(";", Dns);
            }
        }

        // Die Beschreibung ist nur Anzeige; Trenner und Zeilenumbrueche darin wuerden die Zeile zerlegen.
        static string BeschreibungRein(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c == '|' || c == ';' || char.IsControl(c) ? ' ' : c);
            string t = sb.ToString().Trim();
            return t.Length > 80 ? t.Substring(0, 80) : t;
        }

        static List<string> NurIpv4(IEnumerable<string> liste)
        {
            var l = new List<string>();
            foreach (string ip in liste) if (IstIpv4(ip) && !l.Contains(ip)) l.Add(ip);
            return l;
        }

        /// <summary>
        /// Alle IP-Konfigurationen mit fester Adresse (IPEnabled, DHCPEnabled = false), nur ihre
        /// IPv4-Teile (EnableStatic kennt kein IPv6): Adresse und Maske paarweise nach Index.
        /// uebersprungen zaehlt feste Adapter ohne eine einzige IPv4-Adresse. Wirft die WMI-Ausnahme.
        /// </summary>
        static List<FesterAdapter> FesteAdapterLesen(out int uebersprungen)
        {
            uebersprungen = 0;
            var erg = new List<FesterAdapter>();
            List<ManagementObject> rows = Sammler.Quellen.Wmi.Abfrage(NetzCimv2,
                "SELECT SettingID, Description, IPAddress, IPSubnet, DefaultIPGateway, DNSServerSearchOrder FROM " + NetzKlasse + " WHERE IPEnabled = TRUE AND DHCPEnabled = FALSE", NetzLeseMs);
            foreach (ManagementObject mo in rows)
            {
                using (mo)
                {
                    var a = new FesterAdapter { SettingId = (Sammler.Quellen.Wmi.Str(mo, "SettingID") ?? "").Trim(), Beschreibung = BeschreibungRein(Sammler.Quellen.Wmi.Str(mo, "Description")) };
                    List<string> ips = Sammler.Quellen.Wmi.Strings(mo, "IPAddress");
                    List<string> masken = Sammler.Quellen.Wmi.Strings(mo, "IPSubnet");
                    for (int i = 0; i < ips.Count && i < masken.Count; i++)
                    {
                        if (!IstIpv4(ips[i]) || !IstIpv4(masken[i]) || a.Ips.Contains(ips[i])) continue;
                        a.Ips.Add(ips[i]);
                        a.Masken.Add(masken[i]);
                    }
                    a.Gateways = NurIpv4(Sammler.Quellen.Wmi.Strings(mo, "DefaultIPGateway"));
                    a.Dns = NurIpv4(Sammler.Quellen.Wmi.Strings(mo, "DNSServerSearchOrder"));
                    Guid g;
                    if (a.Ips.Count == 0 || !Guid.TryParseExact(a.SettingId, "B", out g)) { uebersprungen++; continue; }
                    erg.Add(a);
                }
            }
            return erg;
        }

        static string AdapterText(List<FesterAdapter> liste)
        {
            var teile = new List<string>();
            foreach (FesterAdapter a in liste) teile.Add(a.Zeile());
            return string.Join("\n", teile);
        }

        /// <summary>
        /// netsh int ip reset setzt jede feste IPv4-Adresse, jedes Gateway und jede DNS-Liste auf
        /// automatisch; die festen Adapter werden vorher gesichert (Rueckweg ueber EnableStatic,
        /// SetGateways, SetDNSServerSearchOrder). Das Protokoll von netsh liegt im Protokollordner
        /// des Programms unter einem unvorhersehbaren Namen (Datum plus 4 Hex). Wirksam nach 1 Neustart.
        /// </summary>
        static void IpZuruecksetzen(PlanSchritt s, Ausfuehrungskontext k)
        {
            List<FesterAdapter> feste;
            int uebersprungen;
            try { feste = FesteAdapterLesen(out uebersprungen); }
            catch (Exception ex)
            {
                AppLog.Warn("Feste Adapter lesen: " + ex.Message);
                Nachweise.NichtBestanden(k, KIpReset, "Die festen Adressen ließen sich nicht lesen (" + ex.Message + "); ohne Sicherung wird nichts zurückgesetzt.");
                return;
            }
            if (feste.Count == 0)
                k.Schreibe("   Feste IPv4-Adressen: keine (alle Adapter mit DHCP); nach dem Zurücksetzen bleibt alles bei automatisch.", "dim");
            foreach (FesterAdapter a in feste)
                k.Schreibe("   Fest: " + a.Anzeige + " " + string.Join(", ", a.Ips) + (a.Gateways.Count > 0 ? ", Gateway " + string.Join(", ", a.Gateways) : "")
                           + (a.Dns.Count > 0 ? ", DNS " + string.Join(", ", a.Dns) : ""), "dim");
            if (uebersprungen > 0)
                k.Schreibe("   " + uebersprungen + (uebersprungen == 1 ? " fester Adapter ohne IPv4-Adresse steht" : " feste Adapter ohne IPv4-Adresse stehen") + " nicht in der Sicherung (diese Maßnahme stellt nur IPv4 zurück).", "warn");

            string ordner = Protokoll.Ordner();
            if (ordner == null)
            {
                Nachweise.NichtBestanden(k, KIpReset, "Der Protokollordner liegt hinter einer Verknüpfung auf einen anderen Ort; das Protokoll von netsh hätte keinen sicheren Platz. Nichts geändert.");
                return;
            }
            string protokoll = Path.Combine(ordner, "ip-reset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) + ".log");

            try { Sicherungsdatei.Schreiben(k, KIpReset, s.Parameter, Sicherungsdatei.Werte(IpAdapter, AdapterText(feste))); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KIpReset, "Keine Sicherung, nichts geändert: " + ex.Message);
                return;
            }

            string tail;
            int code = Werkzeuge.Lauf(k, Netsh(), "int ip reset \"" + protokoll + "\"", NetshMs, null, false, out tail);
            if (k.Trocken)
            {
                Nachweise.Trocken(k, KIpReset, "Der IP-Stack wäre zurückgesetzt worden (" + feste.Count + " feste Adapter in der Sicherung, wirksam nach 1 Neustart); nichts geändert.");
                return;
            }
            if (code != 0)
            {
                Nachweise.NichtBestanden(k, KIpReset, "netsh int ip reset meldete Exit " + code + "; der IP-Stack gilt nicht als zurückgesetzt (Protokoll " + Path.GetFileName(protokoll) + ").");
                return;
            }
            k.Werte["neustart"] = "1";
            k.Schreibe("   Die Änderung wirkt erst nach 1 Neustart.", "warn");
            Nachweise.Bestanden(k, KIpReset, "netsh hat den IP-Stack zurückgesetzt (Exit 0, Protokoll " + Path.GetFileName(protokoll) + "); "
                + feste.Count + " feste" + (feste.Count == 1 ? "r Adapter steht" : " Adapter stehen") + " in der Sicherung. Wirksam nach 1 Neustart; Rückgängig geht über die Sicherung.");
        }

        // ------------------------------------------------------------------ Rueckweg (Kennung rueckweg, Datei aus der Sicherung)

        public static bool ZurueckBekannt(string kennung)
        {
            return kennung == KDnsZurueck || kennung == KHosts || kennung == KIpReset;
        }

        /// <summary>
        /// Wertpruefung der Sicherung (Vertrag 8.3), Beschreibung an MassnahmenM3.ZurueckNetzPruefen.
        /// Die Datei liegt in einem Ordner, den nur der erhoehte Helfer beschreibt (Gemeinsam.cs);
        /// trotzdem geht kein Wert ungeprueft in WQL oder an SetDNSServerSearchOrder.
        /// </summary>
        public static string ZurueckPruefen(Sicherung si)
        {
            if (si == null) return "Die Sicherung ist leer.";
            switch (si.Kennung)
            {
                case KDnsZurueck: return DnsPruefen(si);
                case KHosts: return HostsPruefen(si);
                case KIpReset: return IpPruefen(si);
                default: return "Die Sicherung gehört zu „" + Massnahmenbau.Kurz(si.Kennung) + "“, nicht zu einer Netz-Maßnahme.";
            }
        }

        static string DnsPruefen(Sicherung si)
        {
            string adapter = si.ParameterWert("adapter");
            string index = si.Wert("InterfaceIndex");
            if (adapter == null && index == null) return "Die Sicherung nennt keinen Adapter (weder adapter noch InterfaceIndex).";
            int idxA = -1, idxI = -1;
            if (adapter != null && !Schritte.GanzeZahl(adapter, 0, int.MaxValue, out idxA))
                return "Der Adapter der Sicherung muss eine ganze Zahl ab 0 sein, nicht „" + Massnahmenbau.Kurz(adapter) + "“.";
            if (index != null && !Schritte.GanzeZahl(index, 0, int.MaxValue, out idxI))
                return "Der InterfaceIndex der Sicherung muss eine ganze Zahl ab 0 sein, nicht „" + Massnahmenbau.Kurz(index) + "“.";
            if (adapter != null && index != null && idxA != idxI)
                return "Die Sicherung nennt zwei verschiedene Adapter (adapter " + idxA + ", InterfaceIndex " + idxI + ").";

            string settingId = si.Wert("SettingID");
            Guid g;
            if (string.IsNullOrEmpty(settingId) || !Guid.TryParseExact(settingId, "B", out g))
                return "Die Sicherung nennt keine SettingID in der Form {GUID}" + (string.IsNullOrEmpty(settingId) ? "" : ", sondern „" + Massnahmenbau.Kurz(settingId) + "“") + ".";

            string nameServer = si.Wert("NameServer");
            if (nameServer == null) return "Die Sicherung nennt keinen Wert NameServer (die feste Liste; leer = DHCP).";
            string grund = ListePruefen(nameServer, "NameServer");
            if (grund != null) return grund;
            string suchfolge = si.Wert("DNSServerSearchOrder");
            if (suchfolge != null)
            {
                grund = ListePruefen(suchfolge, "DNSServerSearchOrder");
                if (grund != null) return grund;
            }
            return null;
        }

        /// <summary>
        /// Jeder Eintrag der Liste (Trenner ; , Leerzeichen) muss eine IPv4-Adresse in der
        /// Punktschreibweise mit vier Zahlen sein; IPAddress.TryParse allein liesse "8.8.8" und "1"
        /// durch, deshalb zusaetzlich der Vergleich mit der kanonischen Schreibweise. Hoechstens 10.
        /// </summary>
        static string ListePruefen(string text, string name)
        {
            string[] liste = ListeAus(text);
            if (liste.Length > 10) return "Die Liste " + name + " der Sicherung nennt " + liste.Length + " Einträge; mehr als 10 sind kein gültiger Vorwert.";
            foreach (string ip in liste)
                if (!IstIpv4(ip)) return "Die Liste " + name + " der Sicherung enthält „" + Massnahmenbau.Kurz(ip) + "“, das ist keine IPv4-Adresse.";
            return null;
        }

        static bool IstIpv4(string ip)
        {
            if (string.IsNullOrEmpty(ip) || ip.Length > 15) return false;
            System.Net.IPAddress adr;
            return System.Net.IPAddress.TryParse(ip, out adr)
                && adr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && string.Equals(adr.ToString(), ip, StringComparison.Ordinal);
        }

        /// <summary>hosts-Sicherung (Vertrag M4 3.3): inhalt vorhanden, hoechstens 256 KB, nur druckbare Zeichen, Tabulator und Zeilenumbrueche, keine NUL.</summary>
        static string HostsPruefen(Sicherung si)
        {
            string inhalt = si.Wert(HostsInhalt);
            if (inhalt == null) return "Die Sicherung nennt keinen Inhalt der hosts-Datei (inhalt).";
            if (inhalt.Length > HostsMaxBytes) return "Der gesicherte Inhalt der hosts-Datei ist " + inhalt.Length + " Zeichen lang; mehr als " + HostsMaxBytes + " werden nicht zurückgeschrieben.";
            for (int i = 0; i < inhalt.Length; i++)
            {
                char c = inhalt[i];
                if (c == '\0') return "Der gesicherte Inhalt der hosts-Datei enthält ein NUL-Zeichen (Stelle " + i + "); er wird nicht zurückgeschrieben.";
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                    return "Der gesicherte Inhalt der hosts-Datei enthält ein Steuerzeichen (U+" + ((int)c).ToString("X4") + " an Stelle " + i + "); er wird nicht zurückgeschrieben.";
            }
            return null;
        }

        /// <summary>
        /// ip-Sicherung (Vertrag M4 3.3): adapter vorhanden; leer = kein fester Adapter (gueltig, nichts
        /// zurueckzustellen); sonst je Zeile genau sechs Felder, SettingID als {GUID}, 1 bis 10
        /// IPv4-Adressen mit ebenso vielen IPv4-Masken, 0 bis 10 IPv4-Gateways, 0 bis 10 IPv4-DNS,
        /// hoechstens 64 Zeilen. Jeder Wert geht in WQL oder an EnableStatic: nichts Ungeprueftes.
        /// </summary>
        static string IpPruefen(Sicherung si)
        {
            string text = si.Wert(IpAdapter);
            if (text == null) return "Die Sicherung nennt keine festen Adapter (adapter; leer = keiner).";
            List<FesterAdapter> egal;
            return AdapterLesenAusText(text, out egal);
        }

        /// <summary>Ein Feld der Adapter-Zeile: Eintraege durch ; getrennt, in ihrer Reihenfolge, ohne Dedup, leere weggelassen.</summary>
        static List<string> FelderAus(string text)
        {
            var l = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return l;
            foreach (string t in text.Split(';'))
            {
                string x = t.Trim();
                if (x.Length > 0) l.Add(x);
            }
            return l;
        }

        /// <summary>Jeder Eintrag eine IPv4-Adresse in Punktschreibweise, hoechstens 10; null = in Ordnung, sonst der Grund.</summary>
        static string FelderPruefen(List<string> liste, string name)
        {
            if (liste.Count > ListeMax) return "die Liste " + name + " nennt " + liste.Count + " Einträge; mehr als " + ListeMax + " sind kein gültiger Vorwert.";
            foreach (string ip in liste)
                if (!IstIpv4(ip)) return "die Liste " + name + " enthält „" + Massnahmenbau.Kurz(ip) + "“, das ist keine IPv4-Adresse.";
            return null;
        }

        /// <summary>Zerlegt den adapter-Text der Sicherung; null = gueltig (liste gefuellt), sonst der Grund.</summary>
        static string AdapterLesenAusText(string text, out List<FesterAdapter> liste)
        {
            liste = new List<FesterAdapter>();
            if (text.Trim().Length == 0) return null;
            string[] zeilen = text.Replace("\r", "").Split('\n');
            if (zeilen.Length > AdapterMax) return "Die Sicherung nennt " + zeilen.Length + " feste Adapter; mehr als " + AdapterMax + " sind kein gültiger Vorwert.";
            int nr = 0;
            foreach (string roh in zeilen)
            {
                nr++;
                if (roh.Trim().Length == 0) continue;
                string[] f = roh.Split('|');
                if (f.Length != 6) return "Zeile " + nr + " der Adapter-Sicherung hat " + f.Length + " Felder statt 6 (SettingID|Beschreibung|IP|Maske|Gateway|DNS).";
                var a = new FesterAdapter { SettingId = f[0].Trim(), Beschreibung = BeschreibungRein(f[1]) };
                Guid g;
                if (!Guid.TryParseExact(a.SettingId, "B", out g)) return "Zeile " + nr + " der Adapter-Sicherung nennt keine SettingID in der Form {GUID}, sondern „" + Massnahmenbau.Kurz(a.SettingId) + "“.";
                // Masken wiederholen sich (zwei Adressen im selben Netz): keine Dedup wie in ListeAus,
                // die Stellen von Adresse und Maske muessen zueinander passen.
                a.Ips = FelderAus(f[2]);
                a.Masken = FelderAus(f[3]);
                a.Gateways = FelderAus(f[4]);
                a.Dns = FelderAus(f[5]);
                if (a.Ips.Count == 0) return "Zeile " + nr + " der Adapter-Sicherung nennt keine IP-Adresse.";
                if (a.Ips.Count != a.Masken.Count) return "Zeile " + nr + " der Adapter-Sicherung nennt " + a.Ips.Count + " Adressen, aber " + a.Masken.Count + " Masken.";
                string grund = FelderPruefen(a.Ips, "IP") ?? FelderPruefen(a.Masken, "Maske") ?? FelderPruefen(a.Gateways, "Gateway") ?? FelderPruefen(a.Dns, "DNS");
                if (grund != null) return "Zeile " + nr + " der Adapter-Sicherung: " + grund;
                foreach (FesterAdapter b in liste)
                    if (string.Equals(b.SettingId, a.SettingId, StringComparison.OrdinalIgnoreCase)) return "Die Adapter-Sicherung nennt die SettingID " + a.SettingId + " zweimal.";
                liste.Add(a);
            }
            return null;
        }

        /// <summary>
        /// Stellt die gemerkten Werte einer Netz-Sicherung zurueck (Beschreibung an
        /// MassnahmenM3.ZurueckNetz). Erst ZurueckPruefen (ein Grund → nicht bestanden), dann je Kennung:
        /// DNS (DnsZurueck), hosts (HostsZurueck), IP-Stack (IpZurueck).
        /// </summary>
        public static bool Zurueck(Ausfuehrungskontext k, Sicherung si)
        {
            if (si == null) throw new ArgumentNullException("si");
            if (!ZurueckBekannt(si.Kennung)) throw new ArgumentException("Die Sicherung gehört zu „" + si.Kennung + "“, nicht zu einer Netz-Maßnahme.");
            const string kennung = "rueckweg";

            string grund = ZurueckPruefen(si);
            if (grund != null)
            {
                Nachweise.NichtBestanden(k, kennung, grund + " Nichts zurückgestellt.");
                return false;
            }
            switch (si.Kennung)
            {
                case KHosts: return HostsZurueck(k, kennung, si);
                case KIpReset: return IpZurueck(k, kennung, si);
                default: return DnsZurueck(k, kennung, si);
            }
        }

        /// <summary>
        /// Rueckweg der hosts-Bereinigung: der gesicherte Text wird byte-treu zurueckgeschrieben
        /// (Latin-1 wie beim Lesen), nur am Standardort (HostsPfadPruefen), Schreibschutz wie bei der
        /// Hin-Massnahme. Nachweis: die Datei liest sich danach zeichengleich wie die Sicherung.
        /// </summary>
        static bool HostsZurueck(Ausfuehrungskontext k, string kennung, Sicherung si)
        {
            string inhalt = si.Wert(HostsInhalt);
            string grund = HostsPfadPruefen();
            if (grund != null)
            {
                Nachweise.NichtBestanden(k, kennung, grund + " Nichts zurückgestellt.");
                return false;
            }
            string pfad = HostsPfad();
            string jetzt;
            FileAttributes attr;
            try { jetzt = HostsLesen(pfad, out attr); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Die hosts-Datei " + pfad + " ließ sich nicht lesen: " + ex.Message + ". Nichts zurückgestellt.");
                return false;
            }
            int zeilenJetzt = ZeilenMitEnde(jetzt).Count, zeilenAlt = ZeilenMitEnde(inhalt).Count;
            k.Schreibe("   hosts-Datei jetzt " + zeilenJetzt + " Zeilen (" + jetzt.Length + " Byte), gesichert " + zeilenAlt + " Zeilen (" + inhalt.Length + " Byte)", "dim");
            if (string.Equals(jetzt, inhalt, StringComparison.Ordinal))
            {
                Nachweise.Bestanden(k, kennung, "Die hosts-Datei hat schon wieder den gesicherten Inhalt (" + zeilenAlt + " Zeilen); nichts geändert.");
                return true;
            }
            string aufruf = "Datei " + pfad + " mit dem gesicherten Inhalt neu schreiben (" + zeilenAlt + " Zeilen)";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, kennung, "Die hosts-Datei wäre auf den gesicherten Inhalt (" + zeilenAlt + " Zeilen) zurückgestellt worden; sie bleibt bei " + zeilenJetzt + " Zeilen.");
                return true;
            }
            k.Schreibe("›  " + aufruf, "dim");
            try { HostsSchreiben(pfad, inhalt, attr); }
            catch (Exception ex)
            {
                AppLog.Warn("Rückweg hosts schreiben: " + ex.Message);
                Nachweise.NichtBestanden(k, kennung, "Die hosts-Datei ließ sich nicht zurückschreiben: " + ex.Message);
                return false;
            }
            string danach;
            FileAttributes egal;
            try { danach = HostsLesen(pfad, out egal); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, kennung, "Die hosts-Datei ließ sich nach dem Zurückschreiben nicht lesen: " + ex.Message);
                return false;
            }
            if (string.Equals(danach, inhalt, StringComparison.Ordinal))
            {
                Nachweise.Bestanden(k, kennung, "Die hosts-Datei hat wieder den gesicherten Inhalt (" + zeilenAlt + " Zeilen, " + inhalt.Length + " Byte), wie vor der Maßnahme.");
                return true;
            }
            Nachweise.NichtBestanden(k, kennung, "Nach dem Zurückschreiben unterscheidet sich die hosts-Datei (" + danach.Length + " Byte) vom gesicherten Inhalt (" + inhalt.Length + " Byte); das Zurückstellen hat nicht gegriffen.");
            return false;
        }

        /// <summary>Die Konfiguration mit dieser SettingID, gelesen wie AdapterLesen; null = nicht vorhanden.</summary>
        static FesterAdapter AdapterLesenNachSettingId(string settingId, out bool dhcp)
        {
            dhcp = false;
            using (ManagementObject mo = Wmi2.Erste(NetzCimv2,
                "SELECT SettingID, Description, DHCPEnabled, IPAddress, IPSubnet, DefaultIPGateway, DNSServerSearchOrder FROM " + NetzKlasse
                + " WHERE SettingID = '" + Wmi2.Wql(settingId) + "'", NetzLeseMs))
            {
                if (mo == null) return null;
                dhcp = Sammler.Quellen.Wmi.Wahr(mo, "DHCPEnabled") ?? false;
                var a = new FesterAdapter { SettingId = settingId, Beschreibung = BeschreibungRein(Sammler.Quellen.Wmi.Str(mo, "Description")) };
                List<string> ips = Sammler.Quellen.Wmi.Strings(mo, "IPAddress");
                List<string> masken = Sammler.Quellen.Wmi.Strings(mo, "IPSubnet");
                for (int i = 0; i < ips.Count && i < masken.Count; i++)
                {
                    if (!IstIpv4(ips[i]) || !IstIpv4(masken[i]) || a.Ips.Contains(ips[i])) continue;
                    a.Ips.Add(ips[i]);
                    a.Masken.Add(masken[i]);
                }
                a.Gateways = NurIpv4(Sammler.Quellen.Wmi.Strings(mo, "DefaultIPGateway"));
                a.Dns = NurIpv4(Sammler.Quellen.Wmi.Strings(mo, "DNSServerSearchOrder"));
                return a;
            }
        }

        /// <summary>true, wenn der Adapter genau die gesicherte feste Konfiguration traegt (DHCP aus, Adressen, Masken, Gateways, DNS, Reihenfolge egal).</summary>
        static bool AdapterGleich(FesterAdapter ist, bool dhcp, FesterAdapter soll)
        {
            if (ist == null || dhcp) return false;
            if (!MengenGleich(ist.Gateways, soll.Gateways) || !MengenGleich(ist.Dns, soll.Dns)) return false;
            if (ist.Ips.Count != soll.Ips.Count) return false;
            for (int i = 0; i < soll.Ips.Count; i++)
            {
                int j = ist.Ips.IndexOf(soll.Ips[i]);
                if (j < 0 || !string.Equals(ist.Masken[j], soll.Masken[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        static bool MengenGleich(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (string x in a) if (!b.Contains(x)) return false;
            return true;
        }

        /// <summary>
        /// Rueckweg des IP-Resets: je gesichertem Adapter (ueber seine SettingID gesucht) EnableStatic
        /// mit Adressen und Masken, SetGateways mit den Gateways (nur wenn es welche gab) und
        /// SetDNSServerSearchOrder mit der DNS-Liste (leer = ohne Liste); danach eine zweite Lesung
        /// als Nachweis. Ein Adapter, der schon so steht, wird nicht angefasst; ein fehlender oder
        /// gescheiterter Adapter macht den Rueckweg nicht bestanden, die uebrigen laufen trotzdem.
        /// </summary>
        static bool IpZurueck(Ausfuehrungskontext k, string kennung, Sicherung si)
        {
            List<FesterAdapter> soll;
            string grund = AdapterLesenAusText(si.Wert(IpAdapter), out soll);
            if (grund != null)
            {
                Nachweise.NichtBestanden(k, kennung, grund + " Nichts zurückgestellt.");
                return false;
            }
            if (soll.Count == 0)
            {
                if (k.Trocken) Nachweise.Trocken(k, kennung, "Die Sicherung nennt keinen festen Adapter; es gäbe nichts zurückzustellen.");
                else Nachweise.Bestanden(k, kennung, "Die Sicherung nennt keinen festen Adapter (alle Adapter hatten DHCP); nichts zurückzustellen, nichts geändert.");
                return true;
            }
            int schonSo = 0, gestellt = 0;
            var fehler = new List<string>();
            var trockenSaetze = new List<string>();
            foreach (FesterAdapter a in soll)
            {
                if (k.IstAbgebrochen) break;
                FesterAdapter ist;
                bool dhcp;
                try { ist = AdapterLesenNachSettingId(a.SettingId, out dhcp); }
                catch (Exception ex)
                {
                    fehler.Add(a.Anzeige + ": nicht lesbar (" + ex.Message + ")");
                    continue;
                }
                if (ist == null)
                {
                    fehler.Add(a.Anzeige + ": Adapter mit SettingID " + a.SettingId + " gibt es nicht mehr");
                    continue;
                }
                string zielWort = string.Join(", ", a.Ips) + (a.Gateways.Count > 0 ? ", Gateway " + string.Join(", ", a.Gateways) : "") + (a.Dns.Count > 0 ? ", DNS " + string.Join(", ", a.Dns) : ", DNS automatisch");
                k.Schreibe("   " + ist.Anzeige + ": jetzt " + (dhcp ? "DHCP" : "fest") + " " + (ist.Ips.Count > 0 ? string.Join(", ", ist.Ips) : "ohne IPv4-Adresse") + "; gesichert fest " + zielWort, "dim");
                if (AdapterGleich(ist, dhcp, a)) { schonSo++; continue; }

                string aufruf = "WMI " + NetzKlasse + "[SettingID=" + a.SettingId + "].EnableStatic(" + string.Join(", ", a.Ips) + ")"
                    + (a.Gateways.Count > 0 ? ", SetGateways(" + string.Join(", ", a.Gateways) + ")" : "")
                    + ", SetDNSServerSearchOrder(" + (a.Dns.Count > 0 ? string.Join(", ", a.Dns) : "ohne Liste") + ")";
                if (k.Trocken)
                {
                    k.Schreibe("(trocken) " + aufruf, "dim");
                    trockenSaetze.Add(ist.Anzeige + " auf " + zielWort);
                    continue;
                }
                k.Schreibe("›  " + aufruf, "dim");
                string bedingung = "SettingID = '" + Wmi2.Wql(a.SettingId) + "'";
                try
                {
                    long? c1 = Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, bedingung, "EnableStatic", (object)a.Ips.ToArray(), (object)a.Masken.ToArray()));
                    if (c1.HasValue && c1.Value != 0 && c1.Value != 1) throw new InvalidOperationException("EnableStatic: " + NetzRueckgabe(c1.Value) + " (Rückgabewert " + c1.Value + ")");
                    if (c1 == 1) k.Werte["neustart"] = "1";
                    if (a.Gateways.Count > 0)
                    {
                        long? c2 = Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, bedingung, "SetGateways", (object)a.Gateways.ToArray()));
                        if (c2.HasValue && c2.Value != 0 && c2.Value != 1) throw new InvalidOperationException("SetGateways: " + NetzRueckgabe(c2.Value) + " (Rückgabewert " + c2.Value + ")");
                        if (c2 == 1) k.Werte["neustart"] = "1";
                    }
                    long? c3 = a.Dns.Count == 0
                        ? Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, bedingung, "SetDNSServerSearchOrder"))
                        : Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, bedingung, "SetDNSServerSearchOrder", (object)a.Dns.ToArray()));
                    if (c3.HasValue && c3.Value != 0 && c3.Value != 1) throw new InvalidOperationException("SetDNSServerSearchOrder: " + NetzRueckgabe(c3.Value) + " (Rückgabewert " + c3.Value + ")");
                    if (c3 == 1) k.Werte["neustart"] = "1";
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Rückweg IP auf " + a.SettingId + ": " + ex.Message);
                    fehler.Add(ist.Anzeige + ": " + ex.Message);
                    continue;
                }
                // Zweite Lesung als Nachweis (Vertrag M4 1): die Werte muessen jetzt stehen.
                FesterAdapter danach;
                bool dhcpDanach;
                try { danach = AdapterLesenNachSettingId(a.SettingId, out dhcpDanach); }
                catch (Exception ex) { fehler.Add(ist.Anzeige + ": nach dem Eintragen nicht lesbar (" + ex.Message + ")"); continue; }
                if (AdapterGleich(danach, dhcpDanach, a)) gestellt++;
                else fehler.Add(ist.Anzeige + ": nach dem Eintragen steht " + (danach == null ? "kein Adapter" : (dhcpDanach ? "DHCP" : "fest ") + string.Join(", ", danach.Ips)) + " statt " + string.Join(", ", a.Ips));
            }
            if (k.Trocken)
            {
                Nachweise.Trocken(k, kennung, (trockenSaetze.Count > 0 ? string.Join("; ", trockenSaetze) + " wäre" + (trockenSaetze.Count == 1 ? "" : "n") + " fest eingetragen worden" : "")
                    + (trockenSaetze.Count > 0 && schonSo > 0 ? "; " : "") + (schonSo > 0 ? schonSo + " Adapter steh" + (schonSo == 1 ? "t" : "en") + " schon so" : "")
                    + (fehler.Count > 0 ? "; nicht möglich: " + string.Join("; ", fehler) : "") + ".");
                return fehler.Count == 0;
            }
            string neustartWort = k.Werte.ContainsKey("neustart") ? "; Windows bittet um 1 Neustart" : "";
            if (fehler.Count == 0)
            {
                Nachweise.Bestanden(k, kennung, (gestellt > 0 ? gestellt + " Adapter wieder fest eingetragen wie vor der Maßnahme" : "")
                    + (gestellt > 0 && schonSo > 0 ? "; " : "") + (schonSo > 0 ? schonSo + " Adapter stand" + (schonSo == 1 ? "" : "en") + " schon so" : "") + neustartWort + ".");
                return true;
            }
            Nachweise.NichtBestanden(k, kennung, (gestellt + schonSo) + " von " + soll.Count + " Adaptern stehen wie gesichert; nicht zurückgestellt: " + string.Join("; ", fehler) + neustartWort + ".");
            return false;
        }

        /// <summary>
        /// Rueckweg von netz.dns.zuruecksetzen: SetDNSServerSearchOrder mit der gemerkten NameServer-Liste
        /// auf demselben InterfaceIndex; leere Liste = DHCP. Die SettingID muss noch zu diesem Index
        /// gehoeren, sonst traefe die Liste eine andere Verbindung. Gemessen wird der Registrierungswert
        /// NameServer (der feste Eintrag), nicht die wirksame Liste: sind die DHCP-Server zufaellig gleich
        /// der alten festen Liste, muss der feste Eintrag trotzdem wieder stehen. neustart nur, wenn Windows 1 meldet.
        /// </summary>
        static bool DnsZurueck(Ausfuehrungskontext k, string kennung, Sicherung si)
        {
            int idx;
            Schritte.GanzeZahl(si.ParameterWert("adapter") ?? si.Wert("InterfaceIndex"), 0, int.MaxValue, out idx);
            string[] liste = ListeAus(si.Wert("NameServer"));
            string gemerkt = si.Wert("SettingID");
            string zielWort = liste.Length == 0 ? "automatisch (DHCP)" : string.Join(", ", liste);

            NetzAdapter a = AdapterLesen(idx);
            if (a == null)
            {
                Nachweise.NichtBestanden(k, kennung, "Adapter Nr. " + idx + " aus der Sicherung gibt es nicht mehr; nichts zurückgestellt.");
                return false;
            }
            if (string.IsNullOrEmpty(a.SettingId) || !string.Equals(gemerkt, a.SettingId, StringComparison.OrdinalIgnoreCase))
            {
                Nachweise.NichtBestanden(k, kennung, "InterfaceIndex " + idx + " gehört inzwischen zu einer anderen Verbindung (" + a.Anzeige
                    + (string.IsNullOrEmpty(a.SettingId) ? ", ohne SettingID" : "") + "); die Liste wird dort nicht eingetragen.");
                return false;
            }
            string festJetzt = NameServerLesen(a.SettingId, false);
            k.Schreibe("   Adapter " + idx + " (" + a.Anzeige + "): fester Namensserver jetzt " + (festJetzt ?? "keiner") + ", gesichert " + zielWort, "dim");
            if (ListenGleich(ListeAus(festJetzt), liste))
            {
                Nachweise.Bestanden(k, kennung, "Auf Adapter " + idx + " steht der feste Namensserver schon wieder auf " + zielWort + "; nichts geändert.");
                return true;
            }

            string aufruf = "WMI " + NetzKlasse + "[InterfaceIndex=" + idx + "].SetDNSServerSearchOrder(" + (liste.Length == 0 ? "ohne Liste" : string.Join(", ", liste)) + ")";
            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + aufruf, "dim");
                Nachweise.Trocken(k, kennung, "Die Namensserver von Adapter " + idx + " wären auf " + zielWort + " zurückgestellt worden; es bleibt " + (festJetzt ?? "automatisch") + ".");
                return true;
            }
            k.Schreibe("›  " + aufruf, "dim");
            long? code;
            try
            {
                code = liste.Length == 0
                    ? Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, "InterfaceIndex = " + idx, "SetDNSServerSearchOrder"))
                    : Wmi2.Ganz(Wmi2.InstanzMitZeit(NetzWmiMs, NetzCimv2, NetzKlasse, "InterfaceIndex = " + idx, "SetDNSServerSearchOrder", (object)liste));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Rückweg SetDNSServerSearchOrder auf InterfaceIndex " + idx + ": " + ex.Message);
                Nachweise.NichtBestanden(k, kennung, "Die Namensserver wurden nicht zurückgestellt: " + ex.Message);
                return false;
            }
            if (code.HasValue && code.Value != 0 && code.Value != 1)
            {
                Nachweise.NichtBestanden(k, kennung, "Windows hat das Eintragen abgelehnt: " + NetzRueckgabe(code.Value) + " (Rückgabewert " + code.Value + ").");
                return false;
            }
            if (code == 1) k.Werte["neustart"] = "1";
            string festNachher = NameServerLesen(a.SettingId, false);
            if (ListenGleich(ListeAus(festNachher), liste))
            {
                Nachweise.Bestanden(k, kennung, "Auf Adapter " + idx + " steht der feste Namensserver wieder auf " + zielWort + ", wie vor der Maßnahme"
                    + (code == 1 ? "; Windows bittet um 1 Neustart" : "") + ".");
                return true;
            }
            Nachweise.NichtBestanden(k, kennung, "Nach dem Eintragen steht auf Adapter " + idx + " " + (string.IsNullOrWhiteSpace(festNachher) ? "kein fester Namensserver" : festNachher.Replace(",", ", "))
                + " statt " + zielWort + "; das Zurückstellen hat nicht gegriffen.");
            return false;
        }

        static bool ListenGleich(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // ------------------------------------------------------------------ Lesen

        /// <summary>Die IP-Konfiguration eines Adapters, gelesen aus Win32_NetworkAdapterConfiguration ueber InterfaceIndex.</summary>
        sealed class NetzAdapter
        {
            public int InterfaceIndex;
            public string Beschreibung;
            public string SettingId;
            public bool IpAn;
            public bool Dhcp;
            public string DhcpServer;
            public List<string> IpAdressen = new List<string>();
            public List<string> DnsServer = new List<string>();

            public string Anzeige { get { return string.IsNullOrEmpty(Beschreibung) ? "Adapter " + InterfaceIndex : Beschreibung; } }

            /// <summary>Erste IPv4-Adresse oder null.</summary>
            public string Ipv4()
            {
                foreach (string ip in IpAdressen)
                {
                    System.Net.IPAddress adr;
                    if (System.Net.IPAddress.TryParse(ip, out adr) && adr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip;
                }
                return null;
            }
        }

        static NetzAdapter AdapterLesen(int interfaceIndex)
        {
            using (ManagementObject mo = Wmi2.Erste(NetzCimv2,
                "SELECT InterfaceIndex, Description, SettingID, IPEnabled, DHCPEnabled, DHCPServer, IPAddress, DNSServerSearchOrder FROM "
                + NetzKlasse + " WHERE InterfaceIndex = " + interfaceIndex.ToString(CultureInfo.InvariantCulture), NetzLeseMs))
            {
                if (mo == null) return null;
                var a = new NetzAdapter
                {
                    InterfaceIndex = interfaceIndex,
                    Beschreibung = Sammler.Quellen.Wmi.Str(mo, "Description"),
                    SettingId = Sammler.Quellen.Wmi.Str(mo, "SettingID"),
                    IpAn = Sammler.Quellen.Wmi.Wahr(mo, "IPEnabled") ?? false,
                    Dhcp = Sammler.Quellen.Wmi.Wahr(mo, "DHCPEnabled") ?? false,
                    DhcpServer = Sammler.Quellen.Wmi.Str(mo, "DHCPServer"),
                    IpAdressen = Sammler.Quellen.Wmi.Strings(mo, "IPAddress"),
                    DnsServer = Sammler.Quellen.Wmi.Strings(mo, "DNSServerSearchOrder"),
                };
                return a;
            }
        }

        /// <summary>
        /// Der feste Eintrag NameServer unter Tcpip(6)\Parameters\Interfaces\{GUID}: gesetzt heisst
        /// "von Hand eingetragen, ueberschreibt DHCP" (dieselbe Quelle wie sammler\Quellen\Netzquelle.cs).
        /// null = kein Eintrag oder Schluessel nicht lesbar.
        /// </summary>
        static string NameServerLesen(string settingId, bool ipv6)
        {
            if (string.IsNullOrEmpty(settingId)) return null;
            string pfad = (ipv6 ? TcpipInterfaces.Replace(@"\Tcpip\", @"\Tcpip6\") : TcpipInterfaces) + settingId;
            try
            {
                using (RegistryKey key = Reg.Hklm(pfad, false))
                {
                    string v = Reg.Text(key, "NameServer");
                    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("NameServer unter " + pfad + " nicht lesbar: " + ex.Message);
                return null;
            }
        }

        static string[] ListeAus(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            var l = new List<string>();
            foreach (string t in text.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string x = t.Trim();
                if (x.Length > 0 && !l.Contains(x)) l.Add(x);
            }
            return l.ToArray();
        }

        static bool IstApipa(string ip)
        {
            return ip != null && ip.StartsWith("169.254.", StringComparison.Ordinal);
        }

        /// <summary>Rueckgabewerte von Win32_NetworkAdapterConfiguration (Doku), die hier vorkommen koennen; sonst der nackte Wert.</summary>
        static string NetzRueckgabe(long code)
        {
            switch (code)
            {
                case 64: return "die Methode wird auf dieser Plattform nicht unterstützt";
                case 65: return "unbekannter Fehler";
                case 68: return "ungültiger Eingabeparameter";
                case 70: return "ungültige IP-Adresse";
                case 72: return "Fehler beim Zugriff auf die Registrierung";
                case 82: return "die DHCP-Zuteilung ließ sich nicht erneuern";
                case 83: return "die DHCP-Zuteilung ließ sich nicht freigeben";
                case 84: return "IP ist auf diesem Adapter nicht aktiv";
                case 91: return "Zugriff verweigert";
                case 92: return "zu wenig Arbeitsspeicher";
                case 94: return "Objekt nicht gefunden";
                case 95: return "der Dienst ließ sich nicht benachrichtigen";
                case 96: return "der DNS-Dienst ließ sich nicht benachrichtigen";
                case 97: return "die Schnittstelle ist nicht konfigurierbar";
                case 98: return "nicht alle DHCP-Zuteilungen ließen sich erneuern";
                case 100: return "DHCP ist auf diesem Adapter nicht aktiv";
                case 2147786788: return "der Adapter ist gerade gesperrt (Schreibsperre nicht aktiv)";
                default: return "Fehlercode " + code;
            }
        }
    }
}
