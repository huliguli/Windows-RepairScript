using System.Collections.Generic;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;
using NetzRegel = WartungsToolbox.Kern.Regeln.Netz;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer den Bereich Netzwerk: die Regeln aus kern/Regeln/Netz.cs gegen gepflanzte
    /// Systembilder in tests/aufzeichnungen/gepflanzt-netz-*.json.
    ///
    /// Jede Regel hat hier ihre Probe UND ihre Gegenprobe (ein Bild, in dem die Bedingung
    /// fehlt, und der Nachweis, dass dann KEIN Befund entsteht). Wirksamkeitsproben vom
    /// 12.09.2026: jede Regel wurde einmal absichtlich kaputt gemacht (Vergleich vertauscht,
    /// Schluessel geaendert), die zugehoerige Probe wurde rot, danach zurueckgebaut - die
    /// Stellen stehen an den einzelnen Proben.
    /// </summary>
    public static class NetzProben
    {
        public static void Laufen(Harness h)
        {
            var alle = new List<Befund>();

            Apipa(h, alle);
            ApipaEnglisch(h, alle);
            Hosts(h, alle);
            HostsNcsi(h, alle);
            HostsDocker(h, alle);
            Fec0(h, alle);
            DnsNxdomainStatisch(h, alle);
            DnsTimeoutDhcp(h, alle);
            NcsiPortal(h, alle);
            Gesund(h, alle);
            GatewayTot(h, alle);
            RouteNichtGelesen(h, alle);
            OnLink(h, alle);
            KeinAdapterUp(h, alle);
            ProxyLokal(h, alle);
            ProxyFirma(h, alle);
            KeineDaten(h);

            h.Gruppe("Netz: Saetze tragen Zahl, Bedingung oder Absage");
            // Grundmenge: faellt jedes AddRange oben weg, liefe die Satzpruefung ueber eine
            // leere Liste mit null Zusicherungen und bliebe gruen.
            h.Ist("Grundmenge: mindestens 30 Befunde gesammelt", alle.Count >= 30, alle.Count.ToString());
            h.SaetzeTragen(alle);
        }

        // ------------------------------------------------------------ (a) APIPA auf DHCP-Anschluss

        // Wirksamkeitsprobe 12.09.2026: Herkunft-Vergleich in Netz.cs auf "ip.Herkunft == 3"
        // vertauscht -> "genau ein Befund netz.apipa" wurde rot; zurueckgebaut.
        static void Apipa(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (a): APIPA auf DHCP-Anschluss => bad mit netz.dhcp.erneuern");
            var s = h.Bild("gepflanzt-netz-apipa.json"); if (s == null) return;
            h.Ist("Grundmenge: ein physischer Adapter Up mit DHCP", s.Netz.Adapter.Any(a => a.Physisch && a.OpStatus == 1 && a.Dhcp == true));
            h.Ist("Grundmenge: eine 169.254-Adresse mit Herkunft 2 auf diesem Adapter",
                  s.Netz.IpAdressen.Any(ip => ip.AdapterIndex == 8 && ip.Herkunft == 2 && ip.Adresse.StartsWith("169.254.")));
            var erg = h.Pruefen(s);
            var netz = Harness.Bereich(erg, Bereich.Netz);
            h.Ist("Daten vorhanden", netz.DatenVorhanden);
            var apipa = Harness.AlleMit(erg, "netz.apipa.").ToList();
            h.Ist("genau ein Befund netz.apipa", apipa.Count == 1, "gefunden: " + apipa.Count);
            if (apipa.Count == 1)
            {
                h.Ist("Zustand bad", apipa[0].Zustand == Zustand.Bad, apipa[0].Zustand);
                h.Ist("Massnahme netz.dhcp.erneuern", apipa[0].Massnahmen.Contains(NetzRegel.MDhcpErneuern));
                h.Ist("Satz nennt die Notadresse", apipa[0].Satz.Contains("169.254.34.9"), apipa[0].Satz);
                h.Ist("Schluessel traegt den Adapterindex", apipa[0].Schluessel == "netz.apipa.8", apipa[0].Schluessel);
            }
            h.Ist("Folgestufen schweigen: kein netz.gateway.fehlt", Harness.Einer(erg, "netz.gateway.fehlt") == null);
            h.Ist("Folgestufen schweigen: kein netz.sonde.dns", Harness.Einer(erg, "netz.sonde.dns") == null);
            h.Ist("Bereich Netz ist bad", netz.Zustand == Zustand.Bad, netz.Zustand);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe im selben Bild: der Down-Adapter (WLAN, Index 7) bekommt keinen Befund,
            // auch wenn er spaeter eine 169.254-Adresse tragen sollte.
            s.Netz.IpAdressen.Add(new IpAdresse { AdapterIndex = 7, Adresse = "169.254.98.5", Familie = 2, Herkunft = 2, Zustand = 1 });
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: APIPA auf Down-Adapter (OpStatus 2) => kein netz.apipa.7", Harness.Einer(erg, "netz.apipa.7") == null);

            // Gegenprobe: dieselbe Adresse mit Herkunft 3 (DHCP) ist keine Notadresse.
            s.Netz.IpAdressen.Clear();
            s.Netz.IpAdressen.Add(new IpAdresse { AdapterIndex = 8, Adresse = "169.254.34.9", Familie = 2, Herkunft = 3, Zustand = 4 });
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: 169.254 mit Herkunft 3 => kein netz.apipa", Harness.Einer(erg, "netz.apipa.") == null);

            // Gegenprobe: Adapter ohne DHCP (Dhcp=false) mit 169.254 => kein netz.apipa.
            s.Netz.IpAdressen.Clear();
            s.Netz.IpAdressen.Add(new IpAdresse { AdapterIndex = 8, Adresse = "169.254.34.9", Familie = 2, Herkunft = 2, Zustand = 4 });
            s.Netz.Adapter[0].Dhcp = false;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: 169.254 auf Adapter ohne DHCP => kein netz.apipa", Harness.Einer(erg, "netz.apipa.") == null);
            h.Ist("Gegenprobe: Dhcp=false => kein Fehlend „DHCP-Status“", !Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("DHCP-Status")));

            // Gegenprobe: DHCP-Status nicht ermittelbar (Dhcp=null, MSFT_NetIPInterface ohne Zeile):
            // die 169.254-Adresse mit PrefixOrigin 2 ist selbst der Beleg fuer DHCP => Befund
            // bleibt, Massnahme bleibt, und Fehlend nennt den DHCP-Status mit Fehlerart.
            s.Netz.Adapter[0].Dhcp = null;
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.netz.ipinterface", Art = Fehler.Fehlt, Text = "gepflanzt" });
            erg = h.Pruefen(s);
            var apipaNull = Harness.Einer(erg, "netz.apipa.8");
            h.Ist("Gegenprobe: Dhcp=null mit APIPA => netz.apipa.8 bad", apipaNull != null && apipaNull.Zustand == Zustand.Bad);
            h.Ist("Gegenprobe: Dhcp=null => Massnahme netz.dhcp.erneuern bleibt (Vorbedingung prueft DHCPEnabled)", apipaNull != null && apipaNull.Massnahmen.Contains(NetzRegel.MDhcpErneuern));
            h.Ist("Gegenprobe: Dhcp=null => Detail behauptet nicht „DHCP aktiv“", apipaNull != null && !apipaNull.Detail.Any(d => d.Contains("DHCP aktiv")), apipaNull == null ? "null" : string.Join(" | ", apipaNull.Detail));
            h.Ist("Gegenprobe: Dhcp=null => Fehlend nennt „DHCP-Status (fehlt)“", Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("DHCP-Status") && f.Contains("fehlt")), string.Join("; ", Harness.Bereich(erg, Bereich.Netz).Fehlend));
        }

        // ------------------------------------------------------------ (g) englisches Bild von (a)

        static void ApipaEnglisch(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (g): englisches Bild (lcid 1033) liefert dieselben Befunde wie (a)");
            var de = h.Bild("gepflanzt-netz-apipa.json");
            var en = h.Bild("gepflanzt-netz-apipa-en.json");
            if (de == null || en == null) return;
            h.Ist("Grundmenge: englisches Bild traegt lcid 1033", en.Sprache.Lcid == 1033);
            h.Ist("Grundmenge: englisches Bild hat englische Anzeigetexte", en.Netz.Adapter.Any(a => a.Name == "Wi-Fi"));
            var ergDe = h.Pruefen(de);
            var ergEn = h.Pruefen(en);
            var de1 = Harness.Befunde(ergDe, Bereich.Netz).Select(b => b.Schluessel + "=" + b.Zustand).OrderBy(x => x).ToList();
            var en1 = Harness.Befunde(ergEn, Bereich.Netz).Select(b => b.Schluessel + "=" + b.Zustand).OrderBy(x => x).ToList();
            h.Ist("gleiche Schluessel und Zustaende", de1.SequenceEqual(en1), string.Join(" | ", de1) + "  vs  " + string.Join(" | ", en1));
            var deM = Harness.Befunde(ergDe, Bereich.Netz).SelectMany(b => b.Massnahmen).OrderBy(x => x).ToList();
            var enM = Harness.Befunde(ergEn, Bereich.Netz).SelectMany(b => b.Massnahmen).OrderBy(x => x).ToList();
            h.Ist("gleiche Massnahmen", deM.SequenceEqual(enM));
            h.Ist("Bereichszustand gleich", Harness.Bereich(ergDe, Bereich.Netz).Zustand == Harness.Bereich(ergEn, Bereich.Netz).Zustand);
            alle.AddRange(Harness.Befunde(ergEn, Bereich.Netz));
        }

        // ------------------------------------------------------------ (b) hosts-Umleitung

        // Wirksamkeitsprobe 12.09.2026: IstGeschuetzt auf "immer false" gesetzt -> "genau ein
        // Befund netz.hosts.umleitung", "Bereich bad" und alle IstGeschuetzt-Proben wurden rot;
        // zurueckgebaut. (Nur "windowsupdate.com" zu streichen reichte nicht: das Bild trifft
        // auch ueber die Regel "microsoft.com mit update" - deshalb die Einzelproben unten.)
        static void Hosts(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (b): hosts leitet windowsupdate.microsoft.com um => bad, Docker-Zeilen zaehlen nicht");
            var s = h.Bild("gepflanzt-netz-hosts.json"); if (s == null) return;
            h.Ist("Grundmenge: mindestens 5 hosts-Zeilen im Bild", s.Netz.Hosts.Count >= 5, s.Netz.Hosts.Count.ToString());
            h.Ist("Grundmenge: eine Zeile mit windowsupdate.microsoft.com", s.Netz.Hosts.Any(x => x.Host == "windowsupdate.microsoft.com"));
            var erg = h.Pruefen(s);
            var treffer = Harness.AlleMit(erg, "netz.hosts.umleitung").ToList();
            h.Ist("genau ein Befund netz.hosts.umleitung", treffer.Count == 1, treffer.Count.ToString());
            if (treffer.Count == 1)
            {
                var b = treffer[0];
                h.Ist("Zustand bad", b.Zustand == Zustand.Bad, b.Zustand);
                h.Ist("Massnahme netz.hosts.pruefen", b.Massnahmen.Contains(NetzRegel.MHostsPruefen));
                h.Ist("Detail nennt windowsupdate.microsoft.com", b.Detail.Any(d => d.Contains("windowsupdate.microsoft.com")));
                h.Ist("Detail nennt download.eset.com (AV-Hersteller)", b.Detail.Any(d => d.Contains("download.eset.com")));
                h.Ist("Detail nennt KEINE Docker-Zeile", !b.Detail.Any(d => d.Contains("docker.internal")));
                h.Ist("Detail nennt KEINE Fremdsperrliste (fake-download.example)", !b.Detail.Any(d => d.Contains("fake-download.example")));
                h.Ist("Messwert = 2 umgeleitete Eintraege", b.Messwert != null && b.Messwert.Wert == "2", b.Messwert == null ? "null" : b.Messwert.Wert);
                // Folge je Klasse: Update + Virenschutz getroffen, keine NCSI-Sonde => der Satz
                // nennt beide Dienste und behauptet nichts ueber die Internet-Erkennung.
                h.Ist("Titel nennt Windows Update und Virenschutz", b.Titel.Contains("Windows Update") && b.Titel.Contains("Virenschutz"), b.Titel);
                h.Ist("Satz nennt die Folge fuer Update und Virenschutz", b.Satz.Contains("Windows Update und der Virenschutz erreichen so ihre Server nicht"), b.Satz);
                h.Ist("Satz behauptet nichts ueber „Kein Internet“ (keine NCSI-Domain getroffen)", !b.Satz.Contains("Kein Internet"), b.Satz);
                h.Ist("Detail ordnet die Klasse zu (Windows Update, Virenschutz)", b.Detail.Any(d => d.Contains("windowsupdate.microsoft.com") && d.Contains("Windows Update")) && b.Detail.Any(d => d.Contains("download.eset.com") && d.Contains("Virenschutz")));
            }
            h.Ist("Bereich Netz ist bad", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Bad);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Klassenzuordnung direkt.
            h.Ist("HostsKlasse: download.windowsupdate.com => update", NetzRegel.HostsKlasse("download.windowsupdate.com") == NetzRegel.HostsUpdate);
            h.Ist("HostsKlasse: wdcp.microsoft.com => virenschutz", NetzRegel.HostsKlasse("wdcp.microsoft.com") == NetzRegel.HostsVirenschutz);
            h.Ist("HostsKlasse: dns.msftncsi.com => ncsi", NetzRegel.HostsKlasse("dns.msftncsi.com") == NetzRegel.HostsNcsi);
            h.Ist("HostsKlasse: update.avast.com => virenschutz", NetzRegel.HostsKlasse("update.avast.com") == NetzRegel.HostsVirenschutz);
            h.Ist("HostsKlasse: example.org => null", NetzRegel.HostsKlasse("example.org") == null);

            // Direkte Proben der Domainpruefung (Unterdomain, Endung, Microsoft-Update/Download, AV-Label).
            h.Ist("IstGeschuetzt: dl.delivery.mp.microsoft.com", NetzRegel.IstGeschuetzt("dl.delivery.mp.microsoft.com"));
            h.Ist("IstGeschuetzt: www.msftconnecttest.com", NetzRegel.IstGeschuetzt("www.msftconnecttest.com"));
            h.Ist("IstGeschuetzt: download.windowsupdate.com", NetzRegel.IstGeschuetzt("download.windowsupdate.com"));
            h.Ist("IstGeschuetzt: go.microsoft.com (kein update/download) => nein", !NetzRegel.IstGeschuetzt("go.microsoft.com"));
            h.Ist("IstGeschuetzt: definitionupdates.microsoft.com => ja", NetzRegel.IstGeschuetzt("definitionupdates.microsoft.com"));
            h.Ist("IstGeschuetzt: avast.com => ja", NetzRegel.IstGeschuetzt("avast.com"));
            h.Ist("IstGeschuetzt: nortonlifelock.com => ja", NetzRegel.IstGeschuetzt("nortonlifelock.com"));
            h.Ist("IstGeschuetzt: notwindowsupdate.com => nein (keine Unterdomain)", !NetzRegel.IstGeschuetzt("notwindowsupdate.com"));
            h.Ist("IstGeschuetzt: esetting.example => nein", !NetzRegel.IstGeschuetzt("esetting.example"));
            h.Ist("IstGeschuetzt: avgold.example => nein", !NetzRegel.IstGeschuetzt("avgold.example"));
        }

        // Nur die NCSI-Sonde umgeleitet (Privatsphaere-Sperrlisten tun das): rot bleibt rot,
        // aber die Folge ist ein falsches "Kein Internet"-Symbol, kein blockierter Update-Server.
        static void HostsNcsi(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (b) Variante: nur www.msftconnecttest.com umgeleitet => bad, Folge ist die Internet-Erkennung");
            var s = h.Bild("gepflanzt-netz-hosts-ncsi.json"); if (s == null) return;
            h.Ist("Grundmenge: genau eine hosts-Zeile, www.msftconnecttest.com", s.Netz.Hosts.Count == 1 && s.Netz.Hosts[0].Host == "www.msftconnecttest.com");
            var erg = h.Pruefen(s);
            var b = Harness.Einer(erg, "netz.hosts.umleitung");
            h.Ist("Befund netz.hosts.umleitung vorhanden", b != null);
            if (b != null)
            {
                h.Ist("Zustand bad (unveraendert)", b.Zustand == Zustand.Bad, b.Zustand);
                h.Ist("Titel nennt die Internet-Erkennung", b.Titel.Contains("Internet-Erkennung"), b.Titel);
                h.Ist("Titel nennt weder Windows Update noch Virenschutz", !b.Titel.Contains("Windows Update") && !b.Titel.Contains("Virenschutz"), b.Titel);
                h.Ist("Satz nennt die Folge „Kein Internet“", b.Satz.Contains("„Kein Internet“"), b.Satz);
                h.Ist("Satz behauptet nicht, Update oder Virenschutz erreichten ihre Server nicht", !b.Satz.Contains("Server nicht"), b.Satz);
                h.Ist("Satz nennt Adresse und Ziel", b.Satz.Contains("www.msftconnecttest.com") && b.Satz.Contains("0.0.0.0"), b.Satz);
                h.Ist("Massnahme netz.hosts.pruefen bleibt", b.Massnahmen.Contains(NetzRegel.MHostsPruefen));
                h.Ist("Messwert = 1", b.Messwert != null && b.Messwert.Wert == "1");
            }
            h.Ist("Bereich Netz ist bad", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Bad);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: dazu eine Update-Domain => beide Folgen im Satz, Titel nennt beides.
            s.Netz.Hosts.Add(new HostsZeile { Ip = "0.0.0.0", Host = "download.windowsupdate.com" });
            erg = h.Pruefen(s);
            b = Harness.Einer(erg, "netz.hosts.umleitung");
            h.Ist("Gegenprobe: Update + NCSI => Satz nennt beide Folgen", b != null && b.Satz.Contains("Windows Update erreicht so seine Server nicht") && b.Satz.Contains("„Kein Internet“"), b == null ? "null" : b.Satz);
            h.Ist("Gegenprobe: Titel nennt Windows Update und die Internet-Erkennung", b != null && b.Titel.Contains("Windows Update") && b.Titel.Contains("Internet-Erkennung"), b == null ? "null" : b.Titel);
        }

        static void HostsDocker(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (b) Gegenprobe: nur Docker-, Sperrlisten- und Nichttreffer-Zeilen => kein hosts-Befund");
            var s = h.Bild("gepflanzt-netz-hosts-docker.json"); if (s == null) return;
            h.Ist("Grundmenge: mindestens 6 hosts-Zeilen im Bild", s.Netz.Hosts.Count >= 6, s.Netz.Hosts.Count.ToString());
            h.Ist("Grundmenge: Docker-Zeile host.docker.internal vorhanden", s.Netz.Hosts.Any(x => x.Host == "host.docker.internal"));
            var erg = h.Pruefen(s);
            h.Ist("kein netz.hosts.umleitung", Harness.Einer(erg, "netz.hosts.umleitung") == null);
            h.Ist("keine Probleme im Bereich Netz", !Harness.Befunde(erg, Bereich.Netz).Any(b => b.IstProblem));
            h.Ist("Netzwerkkategorie 0 (oeffentlich) ist kein Befund", Harness.Befunde(erg, Bereich.Netz).All(b => !b.Schluessel.Contains("kategorie") && !b.Schluessel.Contains("profil")));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));
        }

        // ------------------------------------------------------------ (c) fec0-Platzhalter

        // Wirksamkeitsprobe 12.09.2026: Platzhalterfilter in der Regel "DNS fest" entfernt ->
        // "kein netz.dns.statisch" wurde rot (fec0 auf Ethernet gilt dann als fest); zurueckgebaut.
        static void Fec0(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (c): fec0:0:0:ffff::1-3 sind Platzhalter, kein Befund");
            var s = h.Bild("gepflanzt-netz-fec0.json"); if (s == null) return;
            h.Ist("Grundmenge: fec0-Server auf Loopback (Index 1) im Bild",
                  s.Netz.Dns.Any(d => d.AdapterIndex == 1 && d.Server.Any(x => x.StartsWith("fec0:"))));
            h.Ist("Grundmenge: fec0-Server auf Ethernet mit statisch=true im Bild",
                  s.Netz.Dns.Any(d => d.AdapterIndex == 8 && d.Statisch == true && d.Server.All(x => x.StartsWith("fec0:"))));
            var erg = h.Pruefen(s);
            h.Ist("kein netz.dns.statisch (nur Platzhalter)", Harness.Einer(erg, "netz.dns.statisch") == null);
            h.Ist("keine Probleme im Bereich Netz", !Harness.Befunde(erg, Bereich.Netz).Any(b => b.IstProblem));
            h.Ist("Bereich Netz ist ok", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Ok);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: ein echter Server neben den Platzhaltern => Info-Befund entsteht.
            s.Netz.Dns.First(d => d.AdapterIndex == 8 && d.Familie == 23).Server.Add("2001:db8::53");
            erg = h.Pruefen(s);
            var fest = Harness.Einer(erg, "netz.dns.statisch.8");
            h.Ist("Gegenprobe: echter fester IPv6-Server => netz.dns.statisch.8", fest != null);
            if (fest != null)
            {
                h.Ist("Gegenprobe: Zustand ok (Info, kein Fehler)", fest.Zustand == Zustand.Ok, fest.Zustand);
                h.Ist("Gegenprobe: Satz nennt nur den echten Server, keinen Platzhalter", fest.Satz.Contains("2001:db8::53") && !fest.Satz.Contains("fec0"), fest.Satz);
            }
        }

        // ------------------------------------------------------------ (d) DNS-Sonde

        // Wirksamkeitsprobe 12.09.2026: Bedingung auf 'art == "nxdomain" && art == "timeout"'
        // (nie wahr) geaendert -> "genau ein Befund netz.sonde.dns" wurde rot; zurueckgebaut.
        static void DnsNxdomainStatisch(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (d): DNS-Sonde nxdomain + fester Server => bad mit beiden Massnahmen");
            var s = h.Bild("gepflanzt-netz-dns-nxdomain.json"); if (s == null) return;
            h.Ist("Grundmenge: Sonde dns=false mit nxdomain", s.Netz.Sonden.Dns == false && s.Netz.Sonden.DnsFehler == "nxdomain");
            h.Ist("Grundmenge: ein DnsKonfig mit statisch=true", s.Netz.Dns.Any(d => d.Statisch == true));
            var erg = h.Pruefen(s);
            var dns = Harness.AlleMit(erg, "netz.sonde.dns").Where(b => b.Schluessel == "netz.sonde.dns").ToList();
            h.Ist("genau ein Befund netz.sonde.dns", dns.Count == 1, dns.Count.ToString());
            if (dns.Count == 1)
            {
                h.Ist("Zustand bad", dns[0].Zustand == Zustand.Bad, dns[0].Zustand);
                h.Ist("Massnahme netz.dnscache.leeren", dns[0].Massnahmen.Contains(NetzRegel.MDnsCacheLeeren));
                h.Ist("Massnahme netz.dns.zuruecksetzen (fester Server vorhanden)", dns[0].Massnahmen.Contains(NetzRegel.MDnsZuruecksetzen));
                h.Ist("Satz sagt 'unbekannt' (nxdomain)", dns[0].Satz.Contains("unbekannt"), dns[0].Satz);
            }
            var fest = Harness.Einer(erg, "netz.dns.statisch.8");
            h.Ist("Info-Befund netz.dns.statisch.8 vorhanden", fest != null);
            if (fest != null)
            {
                h.Ist("Info ist ok, kein Problem", fest.Zustand == Zustand.Ok, fest.Zustand);
                h.Ist("Info-Satz nennt 8.8.8.8 und 'fest eingetragen'", fest.Satz.Contains("8.8.8.8") && fest.Satz.Contains("fest eingetragen"), fest.Satz);
                h.Ist("Info-Detail nennt den DHCP-Vorschlag 192.168.1.1", fest.Detail.Any(d => d.Contains("192.168.1.1")));
            }
            h.Ist("Folgestufe schweigt: kein netz.sonde.ncsi", Harness.Einer(erg, "netz.sonde.ncsi") == null);
            h.Ist("kein Zusammenfassungs-ok bei bad", Harness.Befunde(erg, Bereich.Netz).All(b => b.Schluessel != "netz.verbindung"));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: dns=true => kein netz.sonde.dns.
            s.Netz.Sonden.Dns = true; s.Netz.Sonden.DnsFehler = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: dns=true => kein netz.sonde.dns", Harness.Einer(erg, "netz.sonde.dns") == null);
            h.Ist("Gegenprobe: Info netz.dns.statisch.8 bleibt", Harness.Einer(erg, "netz.dns.statisch.8") != null);

            // Gegenprobe: Fehlerart "andere" => kein bad, sondern Eintrag in Fehlend.
            s.Netz.Sonden.Dns = false; s.Netz.Sonden.DnsFehler = "andere";
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: dnsFehler=andere => kein netz.sonde.dns", Harness.Einer(erg, "netz.sonde.dns") == null);
            h.Ist("Gegenprobe: dnsFehler=andere => steht in Fehlend", Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("Namensauflösung")));

            // Gegenprobe: Fehlerart "abweichend" => warn, nie bad.
            s.Netz.Sonden.DnsFehler = "abweichend";
            erg = h.Pruefen(s);
            var ab = Harness.Einer(erg, "netz.sonde.dns.abweichend");
            h.Ist("Gegenprobe: dnsFehler=abweichend => netz.sonde.dns.abweichend warn", ab != null && ab.Zustand == Zustand.Warn);
            h.Ist("Gegenprobe: abweichend => kein bad im Bereich", !Harness.Befunde(erg, Bereich.Netz).Any(b => b.Zustand == Zustand.Bad));
        }

        static void DnsTimeoutDhcp(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (d) Variante: DNS timeout ohne festen Server => bad, nur dnscache.leeren");
            var s = h.Bild("gepflanzt-netz-dns-timeout-dhcp.json"); if (s == null) return;
            h.Ist("Grundmenge: Sonde dns=false mit timeout", s.Netz.Sonden.Dns == false && s.Netz.Sonden.DnsFehler == "timeout");
            h.Ist("Grundmenge: kein DnsKonfig mit statisch=true", !s.Netz.Dns.Any(d => d.Statisch == true));
            var erg = h.Pruefen(s);
            var dns = Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.sonde.dns").ToList();
            h.Ist("genau ein Befund netz.sonde.dns", dns.Count == 1, dns.Count.ToString());
            if (dns.Count == 1)
            {
                h.Ist("Zustand bad", dns[0].Zustand == Zustand.Bad);
                h.Ist("Massnahme netz.dnscache.leeren", dns[0].Massnahmen.Contains(NetzRegel.MDnsCacheLeeren));
                h.Ist("KEINE Massnahme netz.dns.zuruecksetzen (nichts fest eingetragen)", !dns[0].Massnahmen.Contains(NetzRegel.MDnsZuruecksetzen));
                h.Ist("Satz sagt 'nicht geantwortet' (timeout)", dns[0].Satz.Contains("nicht geantwortet"), dns[0].Satz);
            }
            h.Ist("kein netz.dns.statisch", Harness.Einer(erg, "netz.dns.statisch") == null);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));
        }

        // ------------------------------------------------------------ (e) NCSI-Inhalt

        // Wirksamkeitsprobe 12.09.2026: Bedingung auf "NcsiInhaltStimmt == true" vertauscht ->
        // "genau ein warn netz.ncsi.inhalt" wurde rot und (f) bekam einen falschen warn; zurueckgebaut.
        static void NcsiPortal(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (e): Testseite HTTP 200 mit falschem Inhalt => warn (Anmeldeportal oder Proxy)");
            var s = h.Bild("gepflanzt-netz-ncsi-portal.json"); if (s == null) return;
            h.Ist("Grundmenge: ncsi=true, ncsiInhaltStimmt=false", s.Netz.Sonden.Ncsi == true && s.Netz.Sonden.NcsiInhaltStimmt == false);
            var erg = h.Pruefen(s);
            var w = Harness.AlleMit(erg, "netz.ncsi.inhalt").ToList();
            h.Ist("genau ein Befund netz.ncsi.inhalt", w.Count == 1, w.Count.ToString());
            if (w.Count == 1)
            {
                h.Ist("Zustand warn", w[0].Zustand == Zustand.Warn, w[0].Zustand);
                h.Ist("Rat nennt die Anmeldeseite", w[0].Rat != null && w[0].Rat.Contains("Anmeldeseite"), w[0].Rat);
            }
            h.Ist("kein bad im Bereich", !Harness.Befunde(erg, Bereich.Netz).Any(b => b.Zustand == Zustand.Bad));
            h.Ist("Bereich Netz ist warn", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Warn);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: Inhalt stimmt => kein netz.ncsi.inhalt, Bereich ok.
            s.Netz.Sonden.NcsiInhaltStimmt = true;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: Inhalt stimmt => kein netz.ncsi.inhalt", Harness.Einer(erg, "netz.ncsi.inhalt") == null);
            h.Ist("Gegenprobe: Bereich ok", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Ok);

            // Gegenprobe: ncsi=false bei dns=true => netz.sonde.ncsi warn (HTTP blockiert), nicht ncsi.inhalt.
            s.Netz.Sonden.Ncsi = false; s.Netz.Sonden.NcsiInhaltStimmt = false;
            erg = h.Pruefen(s);
            var nc = Harness.Einer(erg, "netz.sonde.ncsi");
            h.Ist("Gegenprobe: ncsi=false, dns=true => netz.sonde.ncsi warn", nc != null && nc.Zustand == Zustand.Warn);
            h.Ist("Gegenprobe: dabei kein netz.ncsi.inhalt", Harness.Einer(erg, "netz.ncsi.inhalt") == null);
            h.Ist("Gegenprobe: Satz behauptet nichts ueber den Router (nur Namensdienst und Testseite)", nc != null && !nc.Satz.Contains("Router") && nc.Satz.Contains("Namensdienst antwortet"), nc == null ? "null" : nc.Satz);

            // Gegenprobe: ncsi=false bei dns=false/andere => gemessen "keine Testseite" bleibt
            // nicht stumm: warn netz.sonde.ncsi mit DNS-Stand im Satz, Bereich warn, kein Verbunden-ok.
            s.Netz.Sonden.Dns = false; s.Netz.Sonden.DnsFehler = "andere";
            erg = h.Pruefen(s);
            nc = Harness.Einer(erg, "netz.sonde.ncsi");
            h.Ist("Gegenprobe: dns=false/andere + ncsi=false => netz.sonde.ncsi warn", nc != null && nc.Zustand == Zustand.Warn);
            h.Ist("Gegenprobe: Satz nennt den unbekannten DNS-Fehler", nc != null && nc.Satz.Contains("unbekannten Fehler") && nc.Satz.Contains("antwortet nicht"), nc == null ? "null" : nc.Satz);
            h.Ist("Gegenprobe: Bereich warn, kein netz.verbindung", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Warn && Harness.Einer(erg, "netz.verbindung") == null);
            h.Ist("Gegenprobe: Fehlend nennt die Namensaufloesung", Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("Namensauflösung")));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.sonde.ncsi"));

            // Gegenprobe: dns=null + ncsi=false => kein netz.sonde.ncsi (Namensdienst nicht gemessen,
            // die Testseite kann daran scheitern), aber der Verbunden-Satz sagt es ehrlich.
            s.Netz.Sonden.Dns = null; s.Netz.Sonden.DnsFehler = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: dns=null + ncsi=false => kein netz.sonde.ncsi", Harness.Einer(erg, "netz.sonde.ncsi") == null);
            var vb = Harness.Einer(erg, "netz.verbindung");
            h.Ist("Gegenprobe: Verbunden-Satz nennt Testseite nicht erreichbar und Namensdienst nicht gemessen", vb != null && vb.Satz.Contains("Internet-Testseite antwortet nicht") && vb.Satz.Contains("Namensdienst wurde nicht gemessen"), vb == null ? "null" : vb.Satz);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.verbindung"));

            // Gegenprobe (Portal ueber DNS): dns=false/abweichend UND Testseite mit fremdem Inhalt
            // ist EIN Portal, nicht zwei Befunde: genau ein warn (dns.abweichend), kein ncsi.inhalt,
            // der zweite Beleg steht im Detail.
            s.Netz.Sonden.Dns = false; s.Netz.Sonden.DnsFehler = "abweichend";
            s.Netz.Sonden.Ncsi = true; s.Netz.Sonden.NcsiInhaltStimmt = false;
            erg = h.Pruefen(s);
            var warns = Harness.Befunde(erg, Bereich.Netz).Where(b => b.Zustand == Zustand.Warn).ToList();
            h.Ist("Gegenprobe Portal-DNS: genau ein warn im Bereich", warns.Count == 1, string.Join(", ", warns.Select(b => b.Schluessel)));
            h.Ist("Gegenprobe Portal-DNS: der warn ist netz.sonde.dns.abweichend", warns.Count == 1 && warns[0].Schluessel == "netz.sonde.dns.abweichend");
            h.Ist("Gegenprobe Portal-DNS: kein netz.ncsi.inhalt", Harness.Einer(erg, "netz.ncsi.inhalt") == null);
            h.Ist("Gegenprobe Portal-DNS: Detail traegt den Testseiten-Beleg (fremder Inhalt, TCP 443)", warns.Count == 1 && warns[0].Detail.Any(d => d.Contains("fremdem Inhalt") && d.Contains("TCP 443")), warns.Count == 1 ? string.Join(" | ", warns[0].Detail) : "");
            h.Ist("Gegenprobe Portal-DNS: kein bad im Bereich", !Harness.Befunde(erg, Bereich.Netz).Any(b => b.Zustand == Zustand.Bad));
            alle.AddRange(warns);
        }

        // ------------------------------------------------------------ (f) alles gesund

        // Wirksamkeitsprobe 12.09.2026: Zusammenfassung an "e.Befunde.Any(b => b.IstProblem)"
        // (ohne Verneinung) gehaengt -> "ok-Befund netz.verbindung vorhanden" wurde rot; zurueckgebaut.
        static void Gesund(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (f): alles gesund => keine Probleme, Bereich ok, ein ok-Befund mit Tempo");
            var s = h.Bild("gepflanzt-netz-gesund.json"); if (s == null) return;
            h.Ist("Grundmenge: 3 Adapter, davon 2 physisch, einer Up", s.Netz.Adapter.Count == 3 && s.Netz.Adapter.Count(a => a.Physisch) == 2 && s.Netz.Adapter.Count(a => a.Physisch && a.OpStatus == 1) == 1);
            h.Ist("Grundmenge: alle Sonden true", s.Netz.Sonden.Gateway == true && s.Netz.Sonden.Dns == true && s.Netz.Sonden.Ncsi == true && s.Netz.Sonden.NcsiInhaltStimmt == true && s.Netz.Sonden.Tcp443 == true);
            h.Ist("Grundmenge: APIPA nur auf dem Down-Adapter (WLAN)", s.Netz.IpAdressen.Any(ip => ip.AdapterIndex == 7 && ip.Adresse.StartsWith("169.254.")));
            var erg = h.Pruefen(s);
            var netz = Harness.Bereich(erg, Bereich.Netz);
            h.Ist("Daten vorhanden", netz.DatenVorhanden);
            h.Ist("keine Probleme", !netz.Befunde.Any(b => b.IstProblem), string.Join(", ", netz.Befunde.Where(b => b.IstProblem).Select(b => b.Schluessel)));
            h.Ist("Bereich ok", netz.Zustand == Zustand.Ok, netz.Zustand);
            var ok = Harness.Einer(erg, "netz.verbindung");
            h.Ist("ok-Befund netz.verbindung vorhanden", ok != null && ok.Schluessel == "netz.verbindung");
            if (ok != null)
            {
                h.Ist("Satz nennt „Ethernet“ und 1 Gbit/s", ok.Satz.Contains("„Ethernet“") && ok.Satz.Contains("1 Gbit/s"), ok.Satz);
                h.Ist("Satz nennt keinen virtuellen Adapter als Traeger", !ok.Satz.Contains("vEthernet"), ok.Satz);
                h.Ist("Satz: alle 3 Stufen haben geantwortet (alle Sonden true)", ok.Satz.Contains("alle 3 Stufen") && ok.Satz.Contains("haben geantwortet"), ok.Satz);
                h.Ist("Detail nennt das Standardgateway", ok.Detail.Any(d => d.Contains("192.168.1.1")));
            }

            // Gegenprobe: Router-Sonde nicht gemessen (null) => der Satz zaehlt nur zwei Stufen
            // und nennt den Router als nicht gemessen, statt ihn aus dem Ncsi-Ergebnis zu erraten.
            s.Netz.Sonden.Gateway = null;
            erg = h.Pruefen(s);
            ok = Harness.Einer(erg, "netz.verbindung");
            h.Ist("Gegenprobe: gateway=null => Satz nennt „Router wurde nicht gemessen“", ok != null && ok.Satz.Contains("Router wurde nicht gemessen") && ok.Satz.Contains("Namensdienst und Internet-Testseite haben geantwortet"), ok == null ? "null" : ok.Satz);
            h.Ist("Gegenprobe: gateway=null => Satz behauptet nicht „alle 3 Stufen“", ok != null && !ok.Satz.Contains("alle 3 Stufen"));
            h.Ist("kein netz.apipa (Down-Adapter zaehlt nicht)", Harness.Einer(erg, "netz.apipa.") == null);
            h.Ist("kein netz.verbindung.keine (Ethernet ist Up)", Harness.Einer(erg, "netz.verbindung.keine") == null);
            h.Ist("Netzwerkkategorie 0 im Bild, aber kein Befund dazu", s.Netz.Profile[0].Kategorie == 0 && !netz.Befunde.Any(b => b.Titel.Contains("ffentlich")));
            alle.AddRange(netz.Befunde);
        }

        // ------------------------------------------------------------ Gateway antwortet nicht

        // Wirksamkeitsprobe 12.09.2026: "so.Gateway == false" auf "== true" vertauscht ->
        // "netz.sonde.gateway bad" wurde rot und (f) bekam einen falschen bad; zurueckgebaut.
        static void GatewayTot(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz: Router antwortet nicht (Sonde gateway=false) => bad mit Router-Rat");
            var s = h.Bild("gepflanzt-netz-gateway-tot.json"); if (s == null) return;
            h.Ist("Grundmenge: Gateway eingetragen und Sonde gateway=false", s.Netz.Gateways.Count == 1 && s.Netz.Sonden.Gateway == false);
            var erg = h.Pruefen(s);
            var gw = Harness.Einer(erg, "netz.sonde.gateway");
            h.Ist("Befund netz.sonde.gateway vorhanden", gw != null);
            if (gw != null)
            {
                h.Ist("Zustand bad", gw.Zustand == Zustand.Bad, gw.Zustand);
                h.Ist("Rat nennt den Router", gw.Rat != null && gw.Rat.Contains("Router"), gw.Rat);
                h.Ist("Satz nennt die Gateway-Adresse", gw.Satz.Contains("192.168.1.1"), gw.Satz);
            }
            h.Ist("Folgestufe schweigt: kein netz.sonde.dns", Harness.Einer(erg, "netz.sonde.dns") == null);
            h.Ist("genau ein Problem im Bereich", Harness.Befunde(erg, Bereich.Netz).Count(b => b.IstProblem) == 1);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: gateway=false, aber Testseite antwortet => ok-Info, kein bad
            // (Router blockiert nur Ping; "keine erfundenen Diagnosen").
            s.Netz.Sonden.Ncsi = true; s.Netz.Sonden.NcsiInhaltStimmt = true; s.Netz.Sonden.Dns = true; s.Netz.Sonden.DnsFehler = null; s.Netz.Sonden.Tcp443 = true;
            erg = h.Pruefen(s);
            gw = Harness.Einer(erg, "netz.sonde.gateway");
            h.Ist("Gegenprobe: Ping-Block bei erreichbarer Testseite => netz.sonde.gateway ok", gw != null && gw.Zustand == Zustand.Ok);
            h.Ist("Gegenprobe: Bereich ok", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Ok);
            // Zwei Befunde eines Laufs duerfen sich nicht widersprechen: der Verbunden-Satz darf
            // nicht "Router hat geantwortet" sagen, waehrend der Nachbar "antwortet nicht auf Ping" meldet.
            var vb = Harness.Einer(erg, "netz.verbindung");
            h.Ist("Gegenprobe: Verbunden-Satz behauptet nicht, der Router habe geantwortet", vb != null && !vb.Satz.Contains("Router, Namensdienst") && !vb.Satz.Contains("alle 3 Stufen"), vb == null ? "null" : vb.Satz);
            h.Ist("Gegenprobe: Verbunden-Satz nennt den Ping-Block", vb != null && vb.Satz.Contains("der Router antwortet nicht auf Ping") && vb.Satz.Contains("Namensdienst und Internet-Testseite haben geantwortet"), vb == null ? "null" : vb.Satz);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.sonde.gateway" || b.Schluessel == "netz.verbindung"));

            // Gegenprobe: gateway=true => kein netz.sonde.gateway.
            s.Netz.Sonden.Gateway = true;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: gateway=true => kein netz.sonde.gateway", Harness.Einer(erg, "netz.sonde.gateway") == null);
        }

        // ------------------------------------------------------------ Routentabelle nicht gelesen

        // Eine nicht gelesene Routentabelle ist "nicht geprueft", nie "kein Router": die
        // Sonden koennen DNS und Testseite trotzdem messen, und ein bad aus einer leeren Liste
        // ohne Messung waere eine erfundene Diagnose.
        static void RouteNichtGelesen(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz: wmi.netz.route in der Fehlerliste => kein netz.gateway.fehlt, Fehlend nennt das Standardgateway");
            var s = h.Bild("gepflanzt-netz-gesund.json"); if (s == null) return;
            s.Netz.Gateways.Clear();
            s.Netz.StandardrouteOnLink = null;
            s.Netz.Sonden.Gateway = null;
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.netz.route", Art = Fehler.Zeit, Text = "gepflanzt" });
            h.Ist("Grundmenge: Gateways leer, Fehlereintrag wmi.netz.route (zeit), dns und ncsi true",
                  s.Netz.Gateways.Count == 0 && s.Fehlerliste.Any(f => f.Quelle == "wmi.netz.route") && s.Netz.Sonden.Dns == true && s.Netz.Sonden.Ncsi == true);
            var erg = h.Pruefen(s);
            var netz = Harness.Bereich(erg, Bereich.Netz);
            h.Ist("kein netz.gateway.fehlt (nichts erfinden)", Harness.Einer(erg, "netz.gateway.fehlt") == null);
            h.Ist("kein netz.gateway.direkt (die Tabelle wurde nicht gelesen, kein Urteil ueber die Route)", Harness.Einer(erg, "netz.gateway.direkt") == null);
            h.Ist("Fehlend nennt „Standardgateway“ mit Fehlerart zeit", netz.Fehlend.Any(f => f.Contains("Standardgateway") && f.Contains("zeit")), string.Join("; ", netz.Fehlend));
            h.Ist("kein bad im Bereich", !netz.Befunde.Any(b => b.Zustand == Zustand.Bad), string.Join(", ", netz.Befunde.Where(b => b.Zustand == Zustand.Bad).Select(b => b.Schluessel)));
            var vb = Harness.Einer(erg, "netz.verbindung");
            h.Ist("Verbunden-Befund vorhanden, Satz nennt Router als nicht gemessen", vb != null && vb.Satz.Contains("Router wurde nicht gemessen"), vb == null ? "null" : vb.Satz);
            alle.AddRange(netz.Befunde);

            // Gegenprobe: gemessen leer (kein Fehlereintrag), Testseite antwortet => ok-Info netz.gateway.direkt.
            s.Fehlerliste.Clear();
            s.Netz.StandardrouteOnLink = false;
            erg = h.Pruefen(s);
            var direkt = Harness.Einer(erg, "netz.gateway.direkt");
            h.Ist("Gegenprobe: Routen gelesen, leer, ncsi=true => netz.gateway.direkt ok", direkt != null && direkt.Zustand == Zustand.Ok, direkt == null ? "null" : direkt.Zustand);
            h.Ist("Gegenprobe: dabei kein netz.gateway.fehlt und kein Fehlend „Standardgateway“", Harness.Einer(erg, "netz.gateway.fehlt") == null && !Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("Standardgateway")));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.gateway.direkt"));

            // Gegenprobe: gemessen leer und Testseite tot => bad netz.gateway.fehlt (die alte Regel).
            s.Netz.Sonden.Ncsi = false; s.Netz.Sonden.NcsiInhaltStimmt = false; s.Netz.Sonden.Dns = false; s.Netz.Sonden.DnsFehler = "timeout"; s.Netz.Sonden.Tcp443 = false;
            erg = h.Pruefen(s);
            var fehlt = Harness.Einer(erg, "netz.gateway.fehlt");
            h.Ist("Gegenprobe: Routen gelesen, leer, ncsi=false => netz.gateway.fehlt bad", fehlt != null && fehlt.Zustand == Zustand.Bad);
            h.Ist("Gegenprobe: Folgestufe schweigt (kein netz.sonde.dns)", Harness.Einer(erg, "netz.sonde.dns") == null);

            // Gegenprobe: Routentabelle nicht gelesen UND Testseite tot => die Sonden urteilen,
            // nicht die leere Liste: netz.sonde.dns (timeout) statt netz.gateway.fehlt.
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.netz.route", Art = Fehler.Ausnahme, Text = "gepflanzt" });
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: Route nicht gelesen + dns timeout => netz.sonde.dns bad, kein netz.gateway.fehlt",
                  Harness.Einer(erg, "netz.gateway.fehlt") == null && Harness.Einer(erg, "netz.sonde.dns") != null && Harness.Einer(erg, "netz.sonde.dns").Zustand == Zustand.Bad);
        }

        // ------------------------------------------------------------ On-Link-Standardroute

        // PPPoE-Einwahl oder Mobilfunk: Standardroute mit NextHop 0.0.0.0, kein Router, aber
        // Internet. Vorher wurde die Route verworfen und "Kein Router eingetragen" gemeldet.
        static void OnLink(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz: On-Link-Standardroute (PPPoE/WWAN) mit antwortender Testseite => ok, kein „Kein Router“");
            var s = h.Bild("gepflanzt-netz-onlink.json"); if (s == null) return;
            h.Ist("Grundmenge: Gateways leer, standardrouteOnLink=true, Sonde gateway nicht gemessen, ncsi=true",
                  s.Netz.Gateways.Count == 0 && s.Netz.StandardrouteOnLink == true && s.Netz.Sonden.Gateway == null && s.Netz.Sonden.Ncsi == true);
            var erg = h.Pruefen(s);
            var netz = Harness.Bereich(erg, Bereich.Netz);
            h.Ist("kein netz.gateway.fehlt", Harness.Einer(erg, "netz.gateway.fehlt") == null);
            var direkt = Harness.Einer(erg, "netz.gateway.direkt");
            h.Ist("ok-Info netz.gateway.direkt vorhanden", direkt != null && direkt.Zustand == Zustand.Ok, direkt == null ? "null" : direkt.Zustand);
            if (direkt != null)
            {
                h.Ist("Satz nennt die Direktverbindung und die antwortende Testseite", direkt.Satz.Contains("Direktverbindung") && direkt.Satz.Contains("Testseite hat geantwortet"), direkt.Satz);
                h.Ist("Messwert On-Link", direkt.Messwert != null && direkt.Messwert.Wert == "On-Link");
            }
            h.Ist("keine Probleme, Bereich ok", !netz.Befunde.Any(b => b.IstProblem) && netz.Zustand == Zustand.Ok, netz.Zustand);
            h.Ist("Fehlend leer (die Router-Sonde entfaellt ohne Fehlereintrag)", netz.Fehlend.Count == 0, string.Join("; ", netz.Fehlend));
            var vb = Harness.Einer(erg, "netz.verbindung");
            h.Ist("Verbunden-Detail nennt die On-Link-Standardroute", vb != null && vb.Detail.Any(d => d.Contains("On-Link")), vb == null ? "null" : string.Join(" | ", vb.Detail));
            alle.AddRange(netz.Befunde);

            // Gegenprobe: Direktverbindung, aber Testseite tot => bad mit eigenem Satz (keine Router-Adresse verlangen).
            s.Netz.Sonden.Ncsi = false; s.Netz.Sonden.NcsiInhaltStimmt = false; s.Netz.Sonden.Dns = false; s.Netz.Sonden.DnsFehler = "timeout"; s.Netz.Sonden.Tcp443 = false;
            erg = h.Pruefen(s);
            var fehlt = Harness.Einer(erg, "netz.gateway.fehlt");
            h.Ist("Gegenprobe: On-Link + ncsi=false => netz.gateway.fehlt bad", fehlt != null && fehlt.Zustand == Zustand.Bad);
            h.Ist("Gegenprobe: Satz nennt die Direktverbindung und „antwortet nicht“", fehlt != null && fehlt.Satz.Contains("Direktverbindung") && fehlt.Satz.Contains("antwortet nicht"), fehlt == null ? "null" : fehlt.Satz);
            h.Ist("Gegenprobe: Rat verlangt keine DHCP-Erneuerung (keine Massnahme)", fehlt != null && fehlt.Massnahmen.Count == 0);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.gateway.fehlt"));
            h.Ist("Gegenprobe: Folgestufe schweigt (kein netz.sonde.dns)", Harness.Einer(erg, "netz.sonde.dns") == null);

            // Gegenprobe: Direktverbindung, Testseite nicht gemessen => kein Urteil, Fehlend sagt es.
            s.Netz.Sonden.Ncsi = null; s.Netz.Sonden.Dns = null; s.Netz.Sonden.DnsFehler = null; s.Netz.Sonden.Tcp443 = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: On-Link + ncsi=null => weder netz.gateway.fehlt noch netz.gateway.direkt", Harness.Einer(erg, "netz.gateway.") == null);
            h.Ist("Gegenprobe: Fehlend nennt die Direktverbindung", Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("Direktverbindung")), string.Join("; ", Harness.Bereich(erg, Bereich.Netz).Fehlend));
        }

        // ------------------------------------------------------------ kein physischer Adapter Up

        // Wirksamkeitsprobe 12.09.2026: "physischUp.Count == 0" auf "> 0" vertauscht ->
        // "netz.verbindung.keine bad" wurde rot; zurueckgebaut.
        static void KeinAdapterUp(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz: kein physischer Adapter Up (nur virtueller) => bad 'keine Netzwerkverbindung'");
            var s = h.Bild("gepflanzt-netz-kein-adapter-up.json"); if (s == null) return;
            h.Ist("Grundmenge: 2 physische Adapter Down, 1 virtueller Up",
                  s.Netz.Adapter.Count(a => a.Physisch && a.OpStatus != 1) == 2 && s.Netz.Adapter.Any(a => !a.Physisch && a.OpStatus == 1));
            var erg = h.Pruefen(s);
            var k = Harness.Einer(erg, "netz.verbindung.keine");
            h.Ist("Befund netz.verbindung.keine vorhanden", k != null);
            if (k != null)
            {
                h.Ist("Zustand bad", k.Zustand == Zustand.Bad, k.Zustand);
                h.Ist("Satz nennt die Zahl der Anschluesse (2)", k.Satz.Contains("2"), k.Satz);
                h.Ist("Rat nennt Kabel und WLAN", k.Rat != null && k.Rat.IndexOf("kabel", System.StringComparison.OrdinalIgnoreCase) >= 0 && k.Rat.Contains("WLAN"), k.Rat);
            }
            h.Ist("Folgestufen schweigen: kein netz.sonde.dns trotz nxdomain", Harness.Einer(erg, "netz.sonde.dns") == null);
            h.Ist("Folgestufen schweigen: kein netz.apipa (WLAN ist Down)", Harness.Einer(erg, "netz.apipa.") == null);
            h.Ist("Folgestufen schweigen: kein netz.gateway.fehlt", Harness.Einer(erg, "netz.gateway.fehlt") == null);
            h.Ist("genau ein Problem im Bereich", Harness.Befunde(erg, Bereich.Netz).Count(b => b.IstProblem) == 1);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: Testseite antwortet trotzdem (VPN, Tethering) => ok-Info statt bad.
            s.Netz.Sonden.Ncsi = true; s.Netz.Sonden.NcsiInhaltStimmt = true;
            erg = h.Pruefen(s);
            k = Harness.Einer(erg, "netz.verbindung.keine");
            h.Ist("Gegenprobe: Internet ueber virtuellen Adapter => netz.verbindung.keine ok", k != null && k.Zustand == Zustand.Ok);

            // Gegenprobe: Ethernet Up => kein netz.verbindung.keine.
            s.Netz.Sonden.Ncsi = false; s.Netz.Sonden.NcsiInhaltStimmt = false;
            s.Netz.Adapter[0].OpStatus = 1; s.Netz.Adapter[0].Medien = 1;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: Ethernet Up => kein netz.verbindung.keine", Harness.Einer(erg, "netz.verbindung.keine") == null);
            var fehlt = Harness.Einer(erg, "netz.gateway.fehlt");
            h.Ist("Gegenprobe: dann greift Stufe 2b: netz.gateway.fehlt (keine Route)", fehlt != null);
            h.Ist("Gegenprobe: Dhcp=true => Rat nennt DHCP und Massnahme netz.dhcp.erneuern", fehlt != null && fehlt.Rat.Contains("DHCP") && fehlt.Massnahmen.Contains(NetzRegel.MDhcpErneuern), fehlt == null ? "null" : fehlt.Rat);

            // Dreiwertiger Rat in Stufe 2b: feste Adresse => Feld Standardgateway; Status unbekannt
            // => neutraler Rat, der keine feste Konfiguration erfindet, und Fehlend nennt den DHCP-Status.
            s.Netz.Adapter[0].Dhcp = false;
            erg = h.Pruefen(s);
            fehlt = Harness.Einer(erg, "netz.gateway.fehlt");
            h.Ist("Gegenprobe: Dhcp=false => Rat nennt die feste Adresskonfiguration, keine Massnahme", fehlt != null && fehlt.Rat.Contains("feste Adresskonfiguration") && fehlt.Massnahmen.Count == 0, fehlt == null ? "null" : fehlt.Rat);
            s.Netz.Adapter[0].Dhcp = null;
            erg = h.Pruefen(s);
            fehlt = Harness.Einer(erg, "netz.gateway.fehlt");
            h.Ist("Gegenprobe: Dhcp=null => neutraler Rat „Adresszuteilung“, nicht „feste Adresskonfiguration“", fehlt != null && fehlt.Rat.Contains("Adresszuteilung") && !fehlt.Rat.Contains("feste Adresskonfiguration"), fehlt == null ? "null" : fehlt.Rat);
            h.Ist("Gegenprobe: Dhcp=null => Fehlend nennt den DHCP-Status", Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("DHCP-Status")), string.Join("; ", Harness.Bereich(erg, Bereich.Netz).Fehlend));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.gateway.fehlt"));
        }

        // ------------------------------------------------------------ Proxy

        // Wirksamkeitsprobe 12.09.2026: IstLokalerProxy auf "StartsWith(\"128.\")" geaendert ->
        // "netz.proxy.lokal warn" wurde rot; zurueckgebaut.
        static void ProxyLokal(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz: WinINet-Proxy auf 127.0.0.1 => warn, PAC-URL => warn mit Frage (Konzept 4.4)");
            var s = h.Bild("gepflanzt-netz-proxy-lokal.json"); if (s == null) return;
            h.Ist("Grundmenge: winInetAktiv mit 127.0.0.1 und PacUrl im Bild", s.Netz.Proxy.WinInetAktiv == true && s.Netz.Proxy.WinInetServer.StartsWith("127.0.0.1") && s.Netz.Proxy.PacUrl != null);
            h.Ist("Grundmenge: kein Domaenenprofil (Kategorie 2) im Bild", !s.Netz.Profile.Any(p => p.Kategorie == 2));
            var erg = h.Pruefen(s);
            var p = Harness.Einer(erg, "netz.proxy.lokal");
            h.Ist("netz.proxy.lokal vorhanden", p != null);
            if (p != null)
            {
                h.Ist("Zustand warn", p.Zustand == Zustand.Warn, p.Zustand);
                h.Ist("Satz nennt 127.0.0.1:8080", p.Satz.Contains("127.0.0.1:8080"), p.Satz);
                h.Ist("Detail spricht vom unbekannten lokalen Proxy", p.Detail.Any(d => d.Contains("lokaler Proxy")));
                // Der Werkzeugkasten kennt keinen Eintrag "Verbindungen": das Detail nennt den echten Weg mit dem Port aus dem Messwert.
                h.Ist("Detail verweist nicht auf den Werkzeugkasten", !p.Detail.Any(d => d.Contains("Werkzeugkasten")), string.Join(" | ", p.Detail));
                h.Ist("Detail nennt Get-NetTCPConnection -LocalPort 8080 und Get-Process", p.Detail.Any(d => d.Contains("Get-NetTCPConnection -LocalPort 8080") && d.Contains("Get-Process")), string.Join(" | ", p.Detail));
            }
            var pac = Harness.Einer(erg, "netz.proxy.pac");
            h.Ist("netz.proxy.pac vorhanden und warn (unbekannte AutoConfigURL)", pac != null && pac.Zustand == Zustand.Warn, pac == null ? "null" : pac.Zustand);
            if (pac != null)
            {
                h.Ist("PAC: Frage mit JaHeisst=absicht, ID traegt die URL", pac.Frage != null && pac.Frage.JaHeisst == Entscheidungen.Absicht && pac.Frage.Id.Contains(s.Netz.Proxy.PacUrl), pac.Frage == null ? "null" : pac.Frage.Id);
                h.Ist("PAC: Rat nennt Setupskript und Virenscan", pac.Rat != null && pac.Rat.Contains("Setupskript") && pac.Rat.Contains("Virenscan"), pac.Rat);
                h.Ist("PAC: Messwert-Erwartung „keine AutoConfigURL“", pac.Messwert != null && pac.Messwert.Schwelle == "keine AutoConfigURL");
            }
            h.Ist("kein netz.proxy.wininet (der lokale Fall ersetzt die Info)", Harness.Einer(erg, "netz.proxy.wininet") == null);
            h.Ist("Bereich warn", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Warn);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: Antwort "so lassen" => absicht, Befund bleibt sichtbar, keine Frage mehr, kein Problem.
            var e = new Entscheidungen();
            e.Setze("netz:proxy.pac:" + s.Netz.Proxy.PacUrl, Entscheidungen.Absicht, "2026-09-13T10:00:00Z");
            erg = h.Pruefen(s, e);
            pac = Harness.Einer(erg, "netz.proxy.pac");
            h.Ist("Gegenprobe: Antwort absicht => netz.proxy.pac absicht, nicht ok", pac != null && pac.Zustand == Zustand.Absicht, pac == null ? "null" : pac.Zustand);
            h.Ist("Gegenprobe: absicht => keine Frage, kein Problem", pac != null && pac.Frage == null && !pac.IstProblem);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.proxy.pac"));

            // Gegenprobe: andere URL entwertet die Antwort => wieder warn mit Frage.
            s.Netz.Proxy.PacUrl = "http://203.0.113.9/proxy.pac";
            erg = h.Pruefen(s, e);
            pac = Harness.Einer(erg, "netz.proxy.pac");
            h.Ist("Gegenprobe: geaenderte PAC-URL => Antwort gilt nicht mehr, warn mit Frage", pac != null && pac.Zustand == Zustand.Warn && pac.Frage != null, pac == null ? "null" : pac.Zustand);

            // Gegenprobe: Antwort "nicht eingerichtet" (reparieren) => bad mit Rat entfernen + Virenscan.
            e.Setze("netz:proxy.pac:" + s.Netz.Proxy.PacUrl, Entscheidungen.Reparieren, "2026-09-13T10:00:00Z");
            erg = h.Pruefen(s, e);
            pac = Harness.Einer(erg, "netz.proxy.pac");
            h.Ist("Gegenprobe: Antwort reparieren => netz.proxy.pac bad", pac != null && pac.Zustand == Zustand.Bad && pac.Frage == null, pac == null ? "null" : pac.Zustand);
            h.Ist("Gegenprobe: bad-Rat nennt entfernen und Virenscan", pac != null && pac.Rat != null && pac.Rat.Contains("entfernen") && pac.Rat.Contains("Virenscan"), pac == null ? "null" : pac.Rat);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.proxy.pac"));

            // Gegenprobe: Domaenenprofil (Kategorie 2) => ok ohne Frage, der Administrator hat es gesetzt.
            s.Netz.Profile.Add(new Profil { Name = "firma.example", Kategorie = 2, Ipv4 = 4, Ipv6 = 1 });
            erg = h.Pruefen(s);
            pac = Harness.Einer(erg, "netz.proxy.pac");
            h.Ist("Gegenprobe: Domaenenprofil + PAC => netz.proxy.pac ok, keine Frage", pac != null && pac.Zustand == Zustand.Ok && pac.Frage == null, pac == null ? "null" : pac.Zustand);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.proxy.pac"));

            h.Ist("IstLokalerProxy: http=127.0.0.1:8080;https=127.0.0.1:8080", NetzRegel.IstLokalerProxy("http=127.0.0.1:8080;https=127.0.0.1:8080"));
            h.Ist("IstLokalerProxy: localhost:3128", NetzRegel.IstLokalerProxy("localhost:3128"));
            h.Ist("IstLokalerProxy: [::1]:3128", NetzRegel.IstLokalerProxy("[::1]:3128"));
            h.Ist("IstLokalerProxy: proxy.firma.example:3128 => nein", !NetzRegel.IstLokalerProxy("proxy.firma.example:3128"));
            h.Ist("IstLokalerProxy: 127.0.0.1 als Teil einer fremden Adresse (10.127.0.0.1) => nein", !NetzRegel.IstLokalerProxy("10.127.0.0.1:80"));
            h.Ist("LokalerProxyPort: http=127.0.0.1:8080;https=127.0.0.1:8080 => 8080", NetzRegel.LokalerProxyPort("http=127.0.0.1:8080;https=127.0.0.1:8080") == "8080");
            h.Ist("LokalerProxyPort: [::1]:3128 => 3128", NetzRegel.LokalerProxyPort("[::1]:3128") == "3128");
            h.Ist("LokalerProxyPort: http://localhost:9000/ => 9000", NetzRegel.LokalerProxyPort("http://localhost:9000/") == "9000");
            h.Ist("LokalerProxyPort: ::1 ohne Port => null", NetzRegel.LokalerProxyPort("::1") == null);
            h.Ist("LokalerProxyPort: proxy.firma.example:3128 (nicht lokal) => null", NetzRegel.LokalerProxyPort("proxy.firma.example:3128") == null);
        }

        static void ProxyFirma(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz Gegenprobe: Firmen-Proxy (kein lokaler) => Info ok, kein warn");
            var s = h.Bild("gepflanzt-netz-proxy-firma.json"); if (s == null) return;
            h.Ist("Grundmenge: winInetAktiv, Server nicht lokal, kein PAC", s.Netz.Proxy.WinInetAktiv == true && !s.Netz.Proxy.WinInetServer.Contains("127.0.0.1") && s.Netz.Proxy.PacUrl == null);
            var erg = h.Pruefen(s);
            h.Ist("kein netz.proxy.lokal", Harness.Einer(erg, "netz.proxy.lokal") == null);
            h.Ist("kein netz.proxy.pac", Harness.Einer(erg, "netz.proxy.pac") == null);
            var info = Harness.Einer(erg, "netz.proxy.wininet");
            h.Ist("Info netz.proxy.wininet ok", info != null && info.Zustand == Zustand.Ok);
            h.Ist("Bereich ok", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Ok);
            h.Ist("Domaenen-Profil (Kategorie 2) erzeugt keinen Befund", !Harness.Befunde(erg, Bereich.Netz).Any(b => b.Schluessel.Contains("profil")));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));
        }

        // ------------------------------------------------------------ keine Daten

        // Wirksamkeitsprobe 12.09.2026: DatenVorhanden fest auf true gesetzt -> "Bereich unknown"
        // wurde rot; zurueckgebaut.
        static void KeineDaten(Harness h)
        {
            h.Gruppe("Netz: ohne Adapter-Daten ist der Bereich unknown, nie ok und nie bad");
            var s = new Systembild { AufgezeichnetUtc = "2026-09-11T16:00:00Z" };
            s.Fehlerliste.Add(new Fehler { Quelle = "wmi.netz.adapter", Art = Fehler.Zeit, Text = "gepflanzt" });
            h.Ist("Grundmenge: leeres Bild mit einem Fehlereintrag fuer wmi.netz.adapter", s.Netz.Adapter.Count == 0 && s.Fehlerliste.Count == 1);
            var erg = h.Pruefen(s);
            var netz = Harness.Bereich(erg, Bereich.Netz);
            h.Ist("DatenVorhanden = false", !netz.DatenVorhanden);
            h.Ist("keine Befunde", netz.Befunde.Count == 0);
            h.Ist("Bereich unknown", netz.Zustand == Zustand.Unknown, netz.Zustand);
            h.Ist("Fehlend nennt die Netzwerkkarten mit Fehlerart", netz.Fehlend.Any(f => f.Contains("Netzwerkkarten") && f.Contains("zeit")), string.Join("; ", netz.Fehlend));

            // Gegenprobe: Zugriff verweigert wird als "keine Rechte" benannt.
            s.Fehlerliste[0].Art = Fehler.Zugriff;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe: Art zugriff => 'keine Rechte' in Fehlend", Harness.Bereich(erg, Bereich.Netz).Fehlend.Any(f => f.Contains("keine Rechte")));
        }
    }
}
