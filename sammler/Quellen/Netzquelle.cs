using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Netzwerk: Adapter, Adressen, Standardgateway, DNS-Server, Verbindungsprofile, Proxy,
    /// hosts-Datei und vier Sonden (Router, Namensdienst, Internet-Testseite, TCP 443).
    ///
    /// Alles im eigenen Prozess: WMI (root\StandardCimv2, dieselben Klassen, die Get-NetAdapter
    /// und Co. als cdxml-Huellen benutzen), Registry, WinHTTP-API per P/Invoke, System.Net.
    /// Kein Skript-Interpreter, kein netsh, kein ipconfig - deren Ausgaben sind lokalisiert.
    ///
    /// Fallen aus der Widerlegungsrunde, die hier abgefangen werden:
    ///  - MSFT_NetAdapter fuehrt Wi-Fi-Direct-Hilfseintraege und Reste alter Konfigurationen
    ///    ("WLAN 3", "WLAN 4" mit derselben PnPDeviceID, InterfaceOperationalStatus 6 und
    ///    MediaConnectState 0). Reste fliegen raus, der Rest wird nach PnPDeviceID dedupliziert.
    ///  - ReceiveLinkSpeed ist bei Down-Adaptern NULL (nicht 0); das Feld bleibt dann null.
    ///  - Die Cmdlet-Felder Status/LinkSpeed/MediaConnectionState gibt es in der Klasse nicht;
    ///    echt sind InterfaceOperationalStatus, ReceiveLinkSpeed, MediaConnectState.
    ///  - MSFT_DNSClientServerAddress traegt auf jedem Anschluss die IPv6-Platzhalter
    ///    fec0:0:0:ffff::1-3; die sind keine Konfiguration und werden herausgefiltert.
    ///  - Ob DNS-Server statisch oder per DHCP kamen, sagt die WMI-Klasse nicht; das steht
    ///    nur in der Registry (NameServer gegen DhcpNameServer je InterfaceGuid).
    ///  - Einen Registry-Wert "AutoDetect" gibt es nicht; das Flag kommt nur sauber aus
    ///    WinHttpGetIEProxyConfigForCurrentUser.fAutoDetect.
    ///  - Dns.GetHostEntry auf eine IP-Adresse laeuft 9 s in die Rueckwaertsaufloesung;
    ///    deshalb Ping.Send auf die geparste Adresse und Dns.GetHostAddresses nur auf Namen.
    ///  - hosts-Zeilen mit "*" sind Wildcards, die der Resolver ignoriert: keine Umleitung.
    /// </summary>
    public static class Netzquelle
    {
        const string Ns = @"root\StandardCimv2";

        // Geduld des Sammlers fuer die Sonden (keine Regel-Schwellen): Router 1,5 s, jede
        // weitere Stufe 3 s, alle Stufen zusammen hoechstens 8 s. Wer das Budget aufbraucht,
        // bekommt fuer die restlichen Stufen einen Fehlereintrag "zeit", nie ein "ok".
        const int SondenBudgetMs = 8000;
        const int PingMs = 1500;
        const int StufeMs = 3000;

        // Rueckfallwerte der Windows-Konnektivitaetspruefung (NCSI), falls die Registry sie
        // nicht nennt. Quelle: learn.microsoft.com "NCSI frequently asked questions".
        const string NcsiRegistry = @"SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet";
        const string NcsiWebHost = "www.msftconnecttest.com";
        const string NcsiWebPfad = "connecttest.txt";
        const string NcsiWebInhalt = "Microsoft Connect Test";
        const string NcsiDnsHost = "dns.msftncsi.com";
        const string NcsiDnsInhalt = "131.107.255.255";

        public static void Erfassen(Systembild s)
        {
            var n = s.Netz;
            // InterfaceIndex -> InterfaceGuid: die Registry kennt Anschluesse nur ueber die GUID.
            var guids = new Dictionary<int, string>();

            Sammler.Versuch(s, "wmi.netz.adapter", () => Adapter(s, n, guids));
            Sammler.Versuch(s, "wmi.netz.ipadresse", () => IpAdressen(s, n));
            Sammler.Versuch(s, "wmi.netz.route", () => Routen(s, n));
            Sammler.Versuch(s, "wmi.netz.dns", () => DnsServer(s, n));
            Sammler.Versuch(s, "wmi.netz.ipinterface", () => Dhcp(s, n));
            Sammler.Versuch(s, "wmi.netz.profil", () => Profile(s, n));
            Sammler.Versuch(s, "registry.netz.tcpip", () => DnsRegistry(s, n, guids));
            Sammler.Versuch(s, "api.netz.proxy.wininet", () => ProxyWinInet(n));
            Sammler.Versuch(s, "api.netz.proxy.winhttp", () => ProxyWinHttp(n));
            Sammler.Versuch(s, "datei.netz.hosts", () => Hosts(s, n));
            Sammler.Versuch(s, "sonde.netz", () => Sonden(s, n));
        }

        // ---------------------------------------------------------------- Adapter

        static void Adapter(Systembild s, Netz n, Dictionary<int, string> guids)
        {
            var roh = new List<Adapter>();
            var guidJeIndex = new Dictionary<int, string>();
            foreach (var mo in Wmi.Abfrage(Ns, "SELECT * FROM MSFT_NetAdapter"))
            {
                // Hidden = interne Adapter (WAN-Miniports, Wi-Fi-Direct-Hilfsadapter). Sie
                // tragen APIPA-Adressen und wuerden ohne diesen Filter Fehlalarme erzeugen.
                if (Wmi.Wahr(mo, "Hidden") == true) continue;
                var a = new Adapter
                {
                    Name = Wmi.Str(mo, "Name"),
                    Beschreibung = Wmi.Str(mo, "InterfaceDescription"),
                    PnpId = Wmi.Str(mo, "PnPDeviceID"),
                    Index = Wmi.Ganz(mo, "InterfaceIndex") ?? 0,
                    OpStatus = Wmi.Ganz(mo, "InterfaceOperationalStatus") ?? 0,
                    Medien = Wmi.Ganz(mo, "MediaConnectState") ?? 0,
                    EmpfangBitS = Wmi.Lang(mo, "ReceiveLinkSpeed"),
                    Physisch = Wmi.Wahr(mo, "ConnectorPresent") == true,
                    Virtuell = Wmi.Wahr(mo, "Virtual") == true,
                    TreiberVersion = Wmi.Str(mo, "DriverVersionString"),
                    TreiberDatumUtc = TreiberDatum(mo),
                };
                // Reste alter Konfigurationen: nicht vorhanden UND Medienzustand unbekannt.
                if (a.OpStatus == 6 && a.Medien == 0) continue;
                roh.Add(a);
                string g = Wmi.Str(mo, "InterfaceGuid");
                if (!string.IsNullOrEmpty(g)) guidJeIndex[a.Index] = g;
            }

            // Deduplizieren nach PnPDeviceID: Wi-Fi Direct erzeugt drei Eintraege je Karte.
            // Der Eintrag mit dem besten Zustand (Up vor Down vor allem anderen, verbunden vor
            // getrennt) vertritt die Karte; er ist auch der, an dem die Adressen haengen.
            var gesehen = new Dictionary<string, Adapter>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in roh)
            {
                if (string.IsNullOrEmpty(a.PnpId)) { n.Adapter.Add(a); continue; }
                Adapter alt;
                if (!gesehen.TryGetValue(a.PnpId, out alt)) { gesehen[a.PnpId] = a; n.Adapter.Add(a); continue; }
                if (Rang(a) < Rang(alt)) { n.Adapter[n.Adapter.IndexOf(alt)] = a; gesehen[a.PnpId] = a; }
            }
            foreach (var a in n.Adapter)
            {
                string g;
                if (guidJeIndex.TryGetValue(a.Index, out g)) guids[a.Index] = g;
            }

            if (n.Adapter.Count == 0)
                Sammler.Fehler(s, "wmi.netz.adapter", Fehler.Fehlt, "MSFT_NetAdapter lieferte keinen sichtbaren Adapter");
        }

        static int Rang(Adapter a)
        {
            int r = a.OpStatus == 1 ? 0 : a.OpStatus == 2 ? 10 : 20;
            return r + (a.Medien == 1 ? 0 : 1);
        }

        /// <summary>
        /// DriverDate ist in MSFT_NetAdapter ein Text "YYYY-MM-DD", kein CIM-Datum; daneben
        /// gibt es DriverDateData als FILETIME. Erst der Text, dann die Zahl, sonst null.
        /// </summary>
        static string TreiberDatum(System.Management.ManagementBaseObject mo)
        {
            string t = Wmi.Str(mo, "DriverDate");
            DateTime d;
            if (!string.IsNullOrEmpty(t) &&
                DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return Zeit.Utc(d);
            long? ft = Wmi.Lang(mo, "DriverDateData");
            if (ft.HasValue && ft.Value > 0)
            {
                try { return Zeit.Utc(DateTime.FromFileTimeUtc(ft.Value)); }
                catch (ArgumentOutOfRangeException) { }
            }
            return null;
        }

        // ---------------------------------------------------------------- Adressen, Routen, DNS

        static void IpAdressen(Systembild s, Netz n)
        {
            int zeilen = 0;
            foreach (var mo in Wmi.Abfrage(Ns, "SELECT InterfaceIndex, IPAddress, AddressFamily, PrefixOrigin, AddressState FROM MSFT_NetIPAddress"))
            {
                zeilen++;
                string adr = Wmi.Str(mo, "IPAddress");
                if (string.IsNullOrEmpty(adr)) continue;
                n.IpAdressen.Add(new IpAdresse
                {
                    AdapterIndex = Wmi.Ganz(mo, "InterfaceIndex") ?? 0,
                    Adresse = adr,
                    Familie = Wmi.Ganz(mo, "AddressFamily") ?? 0,
                    Herkunft = Wmi.Ganz(mo, "PrefixOrigin") ?? 0,
                    Zustand = Wmi.Ganz(mo, "AddressState") ?? 0,
                });
            }
            // Mindestens die Loopback-Adressen sind immer da; null Zeilen heisst "Klasse leer".
            if (zeilen == 0) Sammler.Fehler(s, "wmi.netz.ipadresse", Fehler.Fehlt, "MSFT_NetIPAddress lieferte keine Zeile");
        }

        static void Routen(Systembild s, Netz n)
        {
            // Die ganze Tabelle lesen und selbst filtern: "keine Zeile" von der Klasse ist ein
            // Fehler, "keine Standardroute" dagegen ein Messwert (Gateways bleibt leer).
            // Eine Standardroute mit NextHop 0.0.0.0 bzw. :: ist On-Link (PPPoE-Einwahl,
            // Mobilfunk-WWAN, manche VPNs): kein Router, aber ein Weg ins Internet. Sie steht
            // nicht in Gateways (dort nur anpingbare Hops), sondern als StandardrouteOnLink.
            int zeilen = 0;
            bool onLink = false;
            foreach (var mo in Wmi.Abfrage(Ns, "SELECT InterfaceIndex, DestinationPrefix, NextHop, AddressFamily FROM MSFT_NetRoute"))
            {
                zeilen++;
                string ziel = Wmi.Str(mo, "DestinationPrefix");
                if (ziel != "0.0.0.0/0" && ziel != "::/0") continue;
                string hop = Wmi.Str(mo, "NextHop");
                if (string.IsNullOrEmpty(hop) || hop == "0.0.0.0" || hop == "::") { onLink = true; continue; }
                if (!n.Gateways.Contains(hop)) n.Gateways.Add(hop);
            }
            if (zeilen == 0) { Sammler.Fehler(s, "wmi.netz.route", Fehler.Fehlt, "MSFT_NetRoute lieferte keine Zeile"); return; }
            n.StandardrouteOnLink = onLink;
        }

        /// <summary>Windows setzt diese drei Site-Local-Platzhalter auf jeden Anschluss; sie sind keine Konfiguration.</summary>
        public static bool IstDnsPlatzhalter(string server)
        {
            if (string.IsNullOrEmpty(server)) return true;
            string t = server.Trim().ToLowerInvariant();
            return t == "fec0:0:0:ffff::1" || t == "fec0:0:0:ffff::2" || t == "fec0:0:0:ffff::3";
        }

        static void DnsServer(Systembild s, Netz n)
        {
            int zeilen = 0;
            foreach (var mo in Wmi.Abfrage(Ns, "SELECT InterfaceIndex, AddressFamily, ServerAddresses FROM MSFT_DNSClientServerAddress"))
            {
                zeilen++;
                var server = Wmi.Strings(mo, "ServerAddresses").Where(x => !IstDnsPlatzhalter(x)).ToList();
                // Anschluesse ohne echte Server sind kein Eintrag: nur konfigurierte Listen zaehlen.
                if (server.Count == 0) continue;
                n.Dns.Add(new DnsKonfig
                {
                    AdapterIndex = Wmi.Ganz(mo, "InterfaceIndex") ?? 0,
                    Familie = Wmi.Ganz(mo, "AddressFamily") ?? 0,
                    Server = server,
                });
            }
            if (zeilen == 0) Sammler.Fehler(s, "wmi.netz.dns", Fehler.Fehlt, "MSFT_DNSClientServerAddress lieferte keine Zeile");
        }

        static void Dhcp(Systembild s, Netz n)
        {
            int zeilen = 0;
            foreach (var mo in Wmi.Abfrage(Ns, "SELECT InterfaceIndex, AddressFamily, Dhcp FROM MSFT_NetIPInterface WHERE AddressFamily=2"))
            {
                zeilen++;
                int idx = Wmi.Ganz(mo, "InterfaceIndex") ?? -1;
                int? dhcp = Wmi.Ganz(mo, "Dhcp");   // 1 aktiv, 0 aus (Cmdlet: Enabled/Disabled)
                foreach (var a in n.Adapter)
                    if (a.Index == idx && dhcp.HasValue) a.Dhcp = dhcp.Value == 1;
            }
            if (zeilen == 0) Sammler.Fehler(s, "wmi.netz.ipinterface", Fehler.Fehlt, "MSFT_NetIPInterface lieferte keine IPv4-Zeile");
        }

        static void Profile(Systembild s, Netz n)
        {
            foreach (var mo in Wmi.Abfrage(Ns, "SELECT Name, InterfaceIndex, NetworkCategory, IPv4Connectivity, IPv6Connectivity FROM MSFT_NetConnectionProfile"))
            {
                n.Profile.Add(new Profil
                {
                    Name = Wmi.Str(mo, "Name"),
                    Kategorie = Wmi.Ganz(mo, "NetworkCategory") ?? 0,
                    Ipv4 = Wmi.Ganz(mo, "IPv4Connectivity") ?? 0,
                    Ipv6 = Wmi.Ganz(mo, "IPv6Connectivity") ?? 0,
                });
            }
            // Ohne verbundenen Adapter gibt es kein Profil; das steht dann ehrlich als "fehlt"
            // in der Liste, statt dass eine leere Liste wie ein Messwert aussieht.
            if (n.Profile.Count == 0) Sammler.Fehler(s, "wmi.netz.profil", Fehler.Fehlt, "kein Verbindungsprofil (kein verbundener Adapter oder Klasse leer)");
        }

        /// <summary>
        /// Statisch oder DHCP? Registry Tcpip\Parameters\Interfaces\{GUID}: NameServer (von Hand
        /// gesetzt, ueberschreibt DHCP) gegen DhcpNameServer (vom Router). Auf dem Rechner des
        /// Betreibers: NameServer 8.8.8.8,8.8.4.4 auf einem DHCP-Anschluss - der Fall, den
        /// die Regel "fest eingetragen, obwohl automatisch" meldet. IPv6 liegt unter Tcpip6.
        /// </summary>
        static void DnsRegistry(Systembild s, Netz n, Dictionary<int, string> guids)
        {
            if (n.Dns.Count == 0) { Sammler.Fehler(s, "registry.netz.tcpip", Fehler.Fehlt, "keine DNS-Konfiguration zum Nachschlagen"); return; }
            int gelesen = 0;
            foreach (var d in n.Dns)
            {
                string guid;
                if (!guids.TryGetValue(d.AdapterIndex, out guid)) continue;
                string pfad = (d.Familie == 23 ? @"SYSTEM\CurrentControlSet\Services\Tcpip6" : @"SYSTEM\CurrentControlSet\Services\Tcpip")
                              + @"\Parameters\Interfaces\" + guid;
                using (var k = Registry.LocalMachine.OpenSubKey(pfad, false))
                {
                    if (k == null) continue;
                    gelesen++;
                    string statisch = k.GetValue("NameServer") as string;
                    d.Statisch = !string.IsNullOrWhiteSpace(statisch);
                    string dhcp = k.GetValue("DhcpNameServer") as string;
                    if (!string.IsNullOrWhiteSpace(dhcp))
                        d.DhcpServer = dhcp.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
            if (gelesen == 0) Sammler.Fehler(s, "registry.netz.tcpip", Fehler.Fehlt, "kein Interfaces-Schlüssel zu den DNS-Anschlüssen gefunden");
        }

        // ---------------------------------------------------------------- Proxy (WinHTTP-API)

        // WinINet (Browser) und WinHTTP (Windows Update, Defender-Cloud) sind zwei getrennte
        // Speicher; NCSI hat einen dritten (NlaSvc\Parameters\Internet\ManualProxies). Die
        // API liefert auch fAutoDetect, das in der Registry nur als Bit in einem Blob steckt.

        [StructLayout(LayoutKind.Sequential)]
        struct WINHTTP_CURRENT_USER_IE_PROXY_CONFIG
        {
            public int fAutoDetect;          // BOOL
            public IntPtr lpszAutoConfigUrl; // LPWSTR, mit GlobalFree freizugeben
            public IntPtr lpszProxy;
            public IntPtr lpszProxyBypass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINHTTP_PROXY_INFO
        {
            public uint dwAccessType;        // 1 NO_PROXY, 3 NAMED_PROXY
            public IntPtr lpszProxy;
            public IntPtr lpszProxyBypass;
        }

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool WinHttpGetIEProxyConfigForCurrentUser(ref WINHTTP_CURRENT_USER_IE_PROXY_CONFIG cfg);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool WinHttpGetDefaultProxyConfiguration(ref WINHTTP_PROXY_INFO info);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalFree(IntPtr h);

        static string NimmUndGibFrei(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            try { string t = Marshal.PtrToStringUni(p); return string.IsNullOrWhiteSpace(t) ? null : t.Trim(); }
            finally { GlobalFree(p); }
        }

        static void ProxyWinInet(Netz n)
        {
            var cfg = new WINHTTP_CURRENT_USER_IE_PROXY_CONFIG();
            if (!WinHttpGetIEProxyConfigForCurrentUser(ref cfg))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WinHttpGetIEProxyConfigForCurrentUser");
            n.Proxy.AutoDetect = cfg.fAutoDetect != 0;
            n.Proxy.PacUrl = NimmUndGibFrei(cfg.lpszAutoConfigUrl);
            // lpszProxy ist nur gefuellt, wenn der Proxy im Nutzerprofil eingeschaltet ist.
            n.Proxy.WinInetServer = NimmUndGibFrei(cfg.lpszProxy);
            n.Proxy.WinInetAktiv = n.Proxy.WinInetServer != null;
            NimmUndGibFrei(cfg.lpszProxyBypass);
        }

        static void ProxyWinHttp(Netz n)
        {
            var info = new WINHTTP_PROXY_INFO();
            if (!WinHttpGetDefaultProxyConfiguration(ref info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WinHttpGetDefaultProxyConfiguration");
            n.Proxy.WinHttpTyp = (int)info.dwAccessType;
            n.Proxy.WinHttpServer = NimmUndGibFrei(info.lpszProxy);
            NimmUndGibFrei(info.lpszProxyBypass);
        }

        // ---------------------------------------------------------------- hosts

        static void Hosts(Systembild s, Netz n)
        {
            string pfad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            string[] zeilen;
            // Nicht File.Exists (luegt bei ACL-Problemen, Lehre hiberfil.sys): lesen und die
            // beiden "gibt es nicht"-Ausnahmen als "fehlt" fuehren; Zugriff verweigert faengt
            // Sammler.Versuch als "zugriff".
            try { zeilen = File.ReadAllLines(pfad); }
            catch (FileNotFoundException) { Sammler.Fehler(s, "datei.netz.hosts", Fehler.Fehlt, pfad); return; }
            catch (DirectoryNotFoundException) { Sammler.Fehler(s, "datei.netz.hosts", Fehler.Fehlt, pfad); return; }

            foreach (string roh in zeilen)
            {
                string z = roh;
                int k = z.IndexOf('#');
                if (k >= 0) z = z.Substring(0, k);
                z = z.Trim();
                if (z.Length == 0) continue;
                // Wildcards ("*.beispiel.xyz") kennt der Windows-Resolver nicht: wirkungslos.
                if (z.IndexOf('*') >= 0) continue;
                string[] t = z.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 2) continue;
                IPAddress ip;
                if (!IPAddress.TryParse(t[0], out ip)) continue;
                for (int i = 1; i < t.Length; i++)
                    n.Hosts.Add(new HostsZeile { Ip = t[0], Host = t[i] });
            }
            // Eine hosts-Datei ohne aktive Zeile ist der Auslieferungszustand, kein Fehler:
            // die Datei wurde gelesen, der Fehlereintrag bleibt aus.
        }

        // ---------------------------------------------------------------- Sonden

        /// <summary>
        /// Stufenmodell: (3) Router per Ping, (4) Namensdienst gegen die Windows-eigene
        /// DNS-Sonde, (5) HTTP auf die Windows-eigene Testseite, (6) TCP 443 auf denselben
        /// Host. Keine anderen Ziele, keine Telemetrie: das sind exakt die Adressen, die
        /// Windows selbst fuer das Netzwerksymbol abfragt (Registry NlaSvc\Parameters\Internet).
        /// Ping auf Internet-Hosts ist kein Kriterium (manche antworten nicht), nur der Router.
        /// </summary>
        static void Sonden(Systembild s, Netz n)
        {
            var so = n.Sonden;
            var uhr = Stopwatch.StartNew();
            Func<int, int> rest = wunsch => Math.Max(0, Math.Min(wunsch, SondenBudgetMs - (int)uhr.ElapsedMilliseconds));

            string dnsHost = NcsiDnsHost, dnsInhalt = NcsiDnsInhalt, webHost = NcsiWebHost, webPfad = NcsiWebPfad, webInhalt = NcsiWebInhalt;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(NcsiRegistry, false))
                {
                    if (k != null)
                    {
                        dnsHost = (k.GetValue("ActiveDnsProbeHost") as string) ?? dnsHost;
                        dnsInhalt = (k.GetValue("ActiveDnsProbeContent") as string) ?? dnsInhalt;
                        webHost = (k.GetValue("ActiveWebProbeHost") as string) ?? webHost;
                        webPfad = (k.GetValue("ActiveWebProbePath") as string) ?? webPfad;
                        webInhalt = (k.GetValue("ActiveWebProbeContent") as string) ?? webInhalt;
                    }
                }
            }
            catch (Exception) { /* Rueckfallwerte gelten */ }

            // (3) Router: nur IPv4-Gateway; ein IPv6-Link-Local-Hop kommt aus MSFT_NetRoute
            // ohne Zonenkennung und liesse sich nicht sicher anpingen. Bei einer On-Link-
            // Standardroute gibt es nichts anzupingen: Stufe bleibt null, ohne Fehlereintrag.
            string gw = n.Gateways.FirstOrDefault(g => g.IndexOf(':') < 0);
            if (gw == null && n.StandardrouteOnLink == true) { /* Direktverbindung, kein Router */ }
            else if (gw == null) Sammler.Fehler(s, "sonde.netz.gateway", Fehler.Fehlt, "kein IPv4-Standardgateway bekannt");
            else
            {
                int t = rest(PingMs);
                if (t == 0) Sammler.Fehler(s, "sonde.netz.gateway", Fehler.Zeit, "Sondenbudget aufgebraucht");
                else Sammler.Versuch(s, "sonde.netz.gateway", () =>
                {
                    IPAddress ziel = IPAddress.Parse(gw);   // nie ein Name: keine Rueckwaertsaufloesung
                    try
                    {
                        using (var p = new Ping())
                            so.Gateway = p.Send(ziel, t).Status == IPStatus.Success;
                    }
                    catch (PingException) { so.Gateway = false; throw; }
                });
            }

            // (4) Namensdienst
            {
                int t = rest(StufeMs);
                if (t == 0) Sammler.Fehler(s, "sonde.netz.dns", Fehler.Zeit, "Sondenbudget aufgebraucht");
                else Sammler.Versuch(s, "sonde.netz.dns", () => DnsSonde(so, dnsHost, dnsInhalt, t));
            }

            // (5) Internet-Testseite
            {
                int t = rest(StufeMs);
                if (t == 0) Sammler.Fehler(s, "sonde.netz.ncsi", Fehler.Zeit, "Sondenbudget aufgebraucht");
                else Sammler.Versuch(s, "sonde.netz.ncsi", () => NcsiSonde(so, webHost, webPfad, webInhalt, t));
            }

            // (6) TCP 443
            {
                int t = rest(StufeMs);
                if (t == 0) Sammler.Fehler(s, "sonde.netz.tcp443", Fehler.Zeit, "Sondenbudget aufgebraucht");
                else Sammler.Versuch(s, "sonde.netz.tcp443", () => TcpSonde(so, webHost, t));
            }

            so.DauerMs = (int)uhr.ElapsedMilliseconds;
        }

        /// <summary>
        /// Dns.GetHostAddresses kennt keine Zeitgrenze, deshalb in einem eigenen Thread mit
        /// Join. Die Fehlerart kommt aus SocketErrorCode, nie aus dem (lokalisierten) Text:
        /// HostNotFound = "nxdomain", TimedOut/TryAgain/Join-Ablauf = "timeout", sonst "andere".
        /// TryAgain (11002) ist die Antwort des Resolvers auf einen Server, der nicht antwortet.
        /// Eine Antwort ohne die erwartete Adresse heisst "abweichend" (anderer Server hat
        /// geantwortet, z. B. ein Anmeldeportal oder ein umgebogener DNS).
        /// </summary>
        static void DnsSonde(Sonden so, string host, string erwartet, int zeitMs)
        {
            IPAddress[] adressen = null;
            Exception fehler = null;
            var t = new Thread(() =>
            {
                try { adressen = Dns.GetHostAddresses(host); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "sonde:dns" };
            t.Start();
            if (!t.Join(zeitMs)) { so.Dns = false; so.DnsFehler = "timeout"; return; }
            if (fehler != null)
            {
                var se = fehler as SocketException;
                so.Dns = false;
                if (se == null) so.DnsFehler = "andere";
                else if (se.SocketErrorCode == SocketError.HostNotFound) so.DnsFehler = "nxdomain";
                else if (se.SocketErrorCode == SocketError.TimedOut || se.SocketErrorCode == SocketError.TryAgain) so.DnsFehler = "timeout";
                else so.DnsFehler = "andere";
                return;
            }
            bool stimmt = adressen != null && adressen.Any(a => string.Equals(a.ToString(), erwartet, StringComparison.OrdinalIgnoreCase));
            so.Dns = stimmt;
            so.DnsFehler = stimmt ? null : "abweichend";
        }

        /// <summary>
        /// HTTP GET auf die Windows-Testseite. Umleitungen werden verfolgt: ein Anmeldeportal
        /// antwortet oft mit 302 auf seine eigene Seite, und genau die liefert dann "200 mit
        /// falschem Inhalt" - das Signal, das die Regel als Portal oder Proxy deutet.
        /// Proxy: die Voreinstellung des Prozesses (WebRequest.DefaultWebProxy), kein eigener
        /// WPAD-Aufwand. Kein Cache, damit eine alte Antwort nicht als Verbindung gilt.
        /// </summary>
        static void NcsiSonde(Sonden so, string host, string pfad, string erwartet, int zeitMs)
        {
            so.Ncsi = false; so.NcsiInhaltStimmt = false;
            var req = (HttpWebRequest)WebRequest.Create("http://" + host + "/" + pfad.TrimStart('/'));
            req.Method = "GET";
            req.Timeout = zeitMs;
            req.ReadWriteTimeout = zeitMs;
            req.Proxy = WebRequest.DefaultWebProxy;
            req.UserAgent = "Microsoft NCSI";
            req.KeepAlive = false;
            req.AllowAutoRedirect = true;
            req.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    so.Ncsi = resp.StatusCode == HttpStatusCode.OK;
                    if (so.Ncsi == true)
                    {
                        using (var st = resp.GetResponseStream())
                        using (var r = new StreamReader(st))
                        {
                            char[] puffer = new char[4096];
                            int n = r.Read(puffer, 0, puffer.Length);
                            string body = n > 0 ? new string(puffer, 0, n) : "";
                            so.NcsiInhaltStimmt = string.Equals(body.Trim(), erwartet, StringComparison.Ordinal);
                        }
                    }
                }
            }
            catch (WebException)
            {
                // Zeitablauf, Namensaufloesung, Verbindung verweigert, 4xx/5xx: alles "keine
                // Testseite". Die Ursache steht in den vorigen Stufen, nicht hier.
                so.Ncsi = false; so.NcsiInhaltStimmt = false;
            }
        }

        static void TcpSonde(Sonden so, string host, int zeitMs)
        {
            so.Tcp443 = false;
            var client = new TcpClient();
            try
            {
                IAsyncResult ar = client.BeginConnect(host, 443, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(zeitMs)) { so.Tcp443 = false; return; }
                client.EndConnect(ar);
                so.Tcp443 = client.Connected;
            }
            catch (SocketException) { so.Tcp443 = false; }
            finally { try { client.Close(); } catch (Exception) { } }
        }
    }
}
