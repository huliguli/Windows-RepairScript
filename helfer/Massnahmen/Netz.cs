using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
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
    ///
    /// Der Parameter adapter ist der InterfaceIndex aus dem Systembild (MSFT_NetAdapter.InterfaceIndex,
    /// kern\Systembild.cs Adapter.Index). Win32_NetworkAdapterConfiguration hat daneben ein eigenes
    /// Feld Index (die Nummer der Konfiguration), das nicht dasselbe ist: gesucht wird deshalb immer
    /// ueber InterfaceIndex. Alles ueber System.Management, kein ipconfig, kein Skriptprozess.
    ///
    /// Trockenlauf: Pruefen und Lesezugriffe laufen (der Adapter wird gelesen, die Registry gelesen),
    /// jede Schreibstelle meldet "(trocken) ..." und die Sicherungsdatei bleibt ungeschrieben.
    /// </summary>
    public static partial class MassnahmenM3
    {
        static partial void Netz(List<Massnahme> l) { NetzMassnahmen.Eintragen(l); }

        /// <summary>true, wenn ZurueckNetz die Kennung der Sicherung kennt (heute: netz.dns.zuruecksetzen).</summary>
        public static bool ZurueckNetzBekannt(string kennung) { return NetzMassnahmen.ZurueckBekannt(kennung); }

        /// <summary>
        /// Prueft die Werte einer Netz-Sicherung, bevor der Rueckweg sie anfasst (Vertrag 8.3):
        /// null = gueltig, sonst der Grund (Rueckweg.cs lehnt den Plan damit ab). Geprueft werden
        /// adapter (ganze Zahl ab 0, gleich InterfaceIndex, wenn beide da sind), SettingID
        /// ({GUID}-Form) und die Listen NameServer und DNSServerSearchOrder (IPv4-Adressen in
        /// Punktschreibweise, durch ; getrennt; leer = DHCP). Ein fehlender oder fremder Wert ist ein Grund.
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
        const int NetzWmiMs = 2 * 60 * 1000;    // RenewDHCPLease wartet selbst auf den Server
        const int NetzLeseMs = 30000;

        public const string KDnsCache = "netz.dnscache.leeren";
        public const string KDhcp = "netz.dhcp.erneuern";
        public const string KDnsZurueck = "netz.dns.zuruecksetzen";

        public static void Eintragen(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu(KDnsCache, "DNS-Zwischenspeicher leeren", 1, s => null, DnsCacheLeeren));
            l.Add(Massnahmenbau.Neu(KDhcp, "Adresse neu anfordern (DHCP)", 1, s => AdapterPruefen(s), DhcpErneuern));
            l.Add(Massnahmenbau.Neu(KDnsZurueck, "Namensserver auf automatisch zurücksetzen", 2, s => AdapterPruefen(s), DnsZuruecksetzen));
        }

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

        // ------------------------------------------------------------------ Rueckweg (Kennung rueckweg, Datei aus der Sicherung)

        public static bool ZurueckBekannt(string kennung)
        {
            return kennung == KDnsZurueck;
        }

        /// <summary>
        /// Wertpruefung der Sicherung (Vertrag 8.3), Beschreibung an MassnahmenM3.ZurueckNetzPruefen.
        /// Die Datei liegt in einem Ordner, den nur der erhoehte Helfer beschreibt (Gemeinsam.cs);
        /// trotzdem geht kein Wert ungeprueft in WQL oder an SetDNSServerSearchOrder.
        /// </summary>
        public static string ZurueckPruefen(Sicherung si)
        {
            if (si == null) return "Die Sicherung ist leer.";
            if (si.Kennung != KDnsZurueck) return "Die Sicherung gehört zu „" + Massnahmenbau.Kurz(si.Kennung) + "“, nicht zu einer Netz-Maßnahme.";

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

        /// <summary>
        /// Stellt die gemerkten Werte einer Netz-Sicherung zurueck (heute nur netz.dns.zuruecksetzen:
        /// SetDNSServerSearchOrder mit der gemerkten NameServer-Liste auf demselben InterfaceIndex;
        /// leere Liste = DHCP). Die SettingID muss noch zu diesem Index gehoeren, sonst traefe die
        /// Liste eine andere Verbindung. Gemessen wird der Registrierungswert NameServer (der feste
        /// Eintrag), nicht die wirksame Liste: sind die DHCP-Server zufaellig gleich der alten festen
        /// Liste, muss der feste Eintrag trotzdem wieder stehen. neustart nur, wenn Windows 1 meldet.
        /// </summary>
        public static bool Zurueck(Ausfuehrungskontext k, Sicherung si)
        {
            if (si == null) throw new ArgumentNullException("si");
            if (si.Kennung != KDnsZurueck) throw new ArgumentException("Die Sicherung gehört zu „" + si.Kennung + "“, nicht zu einer Netz-Maßnahme.");
            const string kennung = "rueckweg";

            string grund = ZurueckPruefen(si);
            if (grund != null)
            {
                Nachweise.NichtBestanden(k, kennung, grund + " Nichts zurückgestellt.");
                return false;
            }
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
