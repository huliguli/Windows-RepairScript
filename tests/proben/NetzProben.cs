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
            DnsZuruecksetzenGrenzen(h, alle);
            NcsiPortal(h, alle);
            StackGestoert(h, alle);
            Gesund(h, alle);
            GatewayTot(h, alle);
            RouteNichtGelesen(h, alle);
            OnLink(h, alle);
            KeinAdapterUp(h, alle);
            KurzUnterbrochen(h, alle);
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
                // M3: der Plan braucht den Index als Parameter (docs/M3-ENTWURF.md, Abschnitt 1).
                h.Ist("Befund traegt Parameter adapter = 8", apipa[0].Parameter.ContainsKey(NetzRegel.ParamAdapter) && apipa[0].Parameter[NetzRegel.ParamAdapter] == "8", string.Join(",", apipa[0].Parameter.Select(kv => kv.Key + "=" + kv.Value)));
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
            h.Ist("Gegenprobe: Dhcp=null => Parameter adapter bleibt 8", apipaNull != null && apipaNull.Parameter.ContainsKey(NetzRegel.ParamAdapter) && apipaNull.Parameter[NetzRegel.ParamAdapter] == "8");
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
                // M4 (docs/M4-ENTWURF.md, Abschnitt 2.4): netz.hosts.bereinigen (Stufe 2, werte-merken) statt
                // des Rats netz.hosts.pruefen; die Detailzeile und der Rat sagen jetzt, dass Beheben die Kopie
                // in der Sicherung ablegt und Rueckgaengig sie zurueckschreibt. Bis M3 stand dort das Gegenteil
                // (der Nutzer legt die Kopie selbst an), und die Probe sicherte genau das.
                h.Ist("Massnahme netz.hosts.bereinigen, nicht mehr netz.hosts.pruefen", b.Massnahmen.Contains(NetzRegel.MHostsBereinigen) && !b.Massnahmen.Contains("netz.hosts.pruefen"), string.Join(",", b.Massnahmen));
                h.Ist("genau eine Kennung, keine Parameter", b.Massnahmen.Count == 1 && b.Parameter.Count == 0);
                h.Ist("Detail sagt: Beheben legt vorher eine Kopie in der Sicherung ab, Rückgängig schreibt sie zurück", b.Detail.Any(d => d.Contains("legt vorher eine Kopie") && d.Contains("Sicherung") && d.Contains("Rückgängig schreibt sie zurück")), string.Join(" | ", b.Detail));
                h.Ist("Detail nennt die Zahl der Zeilen, die Beheben entfernt", b.Detail.Any(d => d.Contains("entfernt genau die 2 Zeilen")), string.Join(" | ", b.Detail));
                h.Ist("kein Detail verlangt die Kopie vom Nutzer (kein 'selbst an', kein 'schreibt keine')", !b.Detail.Any(d => d.Contains("selbst an") || d.Contains("schreibt keine")), string.Join(" | ", b.Detail));
                h.Ist("Rat nennt Beheben mit Kopie in der Sicherung und den Virenscan", (b.Rat ?? "").Contains("Beheben legt vorher eine Kopie") && b.Rat.Contains("Virenscan"), b.Rat);
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

            // Gegenprobe M4: DataBasePath weicht vom Standard ab => der Helfer lehnt ab, also keine Kennung
            // (die Karte bleibt bad, der Rat nennt den Pfad und die Handarbeit).
            s.Netz.HostsPfadAbweichend = "D:\\etc";
            var ab = Harness.Einer(h.Pruefen(s), "netz.hosts.umleitung");
            h.Ist("Gegenprobe: hostsPfadAbweichend gesetzt => Befund bad ohne Kennung, Rat nennt den Pfad", ab != null && ab.Zustand == Zustand.Bad && ab.Massnahmen.Count == 0 && (ab.Rat ?? "").Contains("D:\\etc") && ab.Rat.Contains("von Hand"), ab == null ? "null" : ab.Rat);
            h.Ist("Gegenprobe: Detail nennt den abweichenden DataBasePath", ab != null && ab.Detail.Any(d => d.Contains("DataBasePath") && d.Contains("D:\\etc")));
            if (ab != null) alle.Add(ab);
            s.Netz.HostsPfadAbweichend = null;

            // Gegenprobe M4 (Nachzug Phase 2): DataBasePath liess sich nicht lesen (Fehlerliste
            // registry.netz.hostspfad), das Feld bleibt null. Null heisst dann "nicht gemessen", nicht
            // "Standardort": keine Kennung (der Helfer lehnte sonst erst nach dem UAC-Dialog ab), die
            // Karte bleibt bad, der Rat nennt die Handarbeit ohne Pfad, Fehlend nennt den Ort.
            // Wirksamkeitsprobe 18.09.2026: "&& hostsPfadGelesen" aus der Weiche in Netz.cs entfernt
            // -> 4 der Zusicherungen unten rot (Kennung, Rat, Detail, Beheben-Versprechen); zurueckgebaut.
            h.Ist("Grundmenge: hostsPfadAbweichend null, kein Fehlereintrag registry.netz.hostspfad", s.Netz.HostsPfadAbweichend == null && !s.Fehlerliste.Any(f => f.Quelle == "registry.netz.hostspfad"));
            s.Fehlerliste.Add(new Fehler { Quelle = "registry.netz.hostspfad", Art = Fehler.Ausnahme, Text = "gepflanzt" });
            var ergNg = h.Pruefen(s);
            var ng = Harness.Einer(ergNg, "netz.hosts.umleitung");
            h.Ist("Gegenprobe: Fehlereintrag registry.netz.hostspfad => Befund bad ohne Kennung", ng != null && ng.Zustand == Zustand.Bad && ng.Massnahmen.Count == 0, ng == null ? "null" : string.Join(",", ng.Massnahmen));
            h.Ist("Gegenprobe: Rat nennt die Handarbeit und die Bedingung DataBasePath, keinen Pfad „(leer)“ oder „D:\\etc“", ng != null && (ng.Rat ?? "").Contains("von Hand") && ng.Rat.Contains("DataBasePath") && !ng.Rat.Contains("(leer)") && !ng.Rat.Contains("D:\\etc"), ng == null ? "null" : ng.Rat);
            h.Ist("Gegenprobe: Detail sagt „nicht prüfbar“ und „keine Maßnahme“", ng != null && ng.Detail.Any(d => d.Contains("nicht prüfbar") && d.Contains("keine Maßnahme")), ng == null ? "null" : string.Join(" | ", ng.Detail));
            h.Ist("Gegenprobe: kein Detail verspricht Beheben mit Kopie in der Sicherung", ng != null && !ng.Detail.Any(d => d.Contains("Beheben entfernt")), ng == null ? "null" : string.Join(" | ", ng.Detail));
            h.Ist("Gegenprobe: Fehlend nennt „Ort der hosts-Datei (DataBasePath)“", Harness.Bereich(ergNg, Bereich.Netz).Fehlend.Any(f => f.Contains("Ort der hosts-Datei") && f.Contains("DataBasePath")), string.Join("; ", Harness.Bereich(ergNg, Bereich.Netz).Fehlend));
            h.Ist("Gegenprobe: die Treffer selbst bleiben (Messwert 2)", ng != null && ng.Messwert != null && ng.Messwert.Wert == "2", ng == null || ng.Messwert == null ? "null" : ng.Messwert.Wert);
            if (ng != null) alle.Add(ng);
            s.Fehlerliste.RemoveAll(f => f.Quelle == "registry.netz.hostspfad");
            // Rueckweg: ohne den Eintrag haengt die Kennung wieder (die Probe misst die Weiche, nicht den Zufall).
            var zurueck = Harness.Einer(h.Pruefen(s), "netz.hosts.umleitung");
            h.Ist("Gegenprobe: Fehlereintrag entfernt => Kennung netz.hosts.bereinigen wieder da, Fehlend ohne den Ort", zurueck != null && zurueck.Massnahmen.Contains(NetzRegel.MHostsBereinigen) && !Harness.Bereich(h.Pruefen(s), Bereich.Netz).Fehlend.Any(f => f.Contains("Ort der hosts-Datei")), zurueck == null ? "null" : string.Join(",", zurueck.Massnahmen));

            // Das Praedikat, das Regel und Helfer teilen (docs/M4-ENTWURF.md, Abschnitt 2.4): dieselben
            // Treffer wie HostsKlasse, die Zieladresse entscheidet nicht, ohne Host nie wahr.
            h.Ist("HostsZeileUmleitung: 0.0.0.0 windowsupdate.microsoft.com => ja", NetzRegel.HostsZeileUmleitung("0.0.0.0", "windowsupdate.microsoft.com"));
            h.Ist("HostsZeileUmleitung: 127.0.0.1 download.eset.com => ja (AV-Hersteller)", NetzRegel.HostsZeileUmleitung("127.0.0.1", "download.eset.com"));
            h.Ist("HostsZeileUmleitung: 203.0.113.9 www.msftconnecttest.com => ja (fremde Zieladresse zaehlt ebenso)", NetzRegel.HostsZeileUmleitung("203.0.113.9", "www.msftconnecttest.com"));
            h.Ist("HostsZeileUmleitung: 127.0.0.1 host.docker.internal => nein (harmlos)", !NetzRegel.HostsZeileUmleitung("127.0.0.1", "host.docker.internal"));
            h.Ist("HostsZeileUmleitung: 0.0.0.0 fake-download.example => nein (Fremdsperrliste)", !NetzRegel.HostsZeileUmleitung("0.0.0.0", "fake-download.example"));
            h.Ist("HostsZeileUmleitung: 0.0.0.0 go.microsoft.com => nein (kein update/download)", !NetzRegel.HostsZeileUmleitung("0.0.0.0", "go.microsoft.com"));
            h.Ist("HostsZeileUmleitung: ohne Host => nein", !NetzRegel.HostsZeileUmleitung("0.0.0.0", null) && !NetzRegel.HostsZeileUmleitung("0.0.0.0", "") && !NetzRegel.HostsZeileUmleitung("0.0.0.0", "   "));
            h.Ist("HostsZeileUmleitung: ohne IP, aber geschuetzter Host => ja (die Zeile gehoert in keine hosts-Datei)", NetzRegel.HostsZeileUmleitung(null, "wdcp.microsoft.com"));
            h.Ist("HostsZeileUmleitung deckt sich mit HostsKlasse fuer jede Zeile des Bilds", s.Netz.Hosts.All(x => NetzRegel.HostsZeileUmleitung(x.Ip, x.Host) == (NetzRegel.HostsKlasse(x.Host) != null)));

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
                h.Ist("Massnahme netz.hosts.bereinigen auch bei nur einer NCSI-Zeile", b.Massnahmen.Contains(NetzRegel.MHostsBereinigen));
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
                // M3: netz.dns.zuruecksetzen braucht den Adapter mit dem festen Eintrag (hier Index 8, siehe netz.dns.statisch.8).
                h.Ist("Befund traegt Parameter adapter = 8 (der Anschluss mit festem Namensserver)", dns[0].Parameter.ContainsKey(NetzRegel.ParamAdapter) && dns[0].Parameter[NetzRegel.ParamAdapter] == "8", string.Join(",", dns[0].Parameter.Select(kv => kv.Key + "=" + kv.Value)));
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
                h.Ist("Gegenprobe: ohne festen Server kein Parameter adapter", !dns[0].Parameter.ContainsKey(NetzRegel.ParamAdapter), string.Join(",", dns[0].Parameter.Keys));
                h.Ist("Satz sagt 'nicht geantwortet' (timeout)", dns[0].Satz.Contains("nicht geantwortet"), dns[0].Satz);
            }
            h.Ist("kein netz.dns.statisch", Harness.Einer(erg, "netz.dns.statisch") == null);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));
        }

        /// <summary>
        /// M3-Entwurf 8.5: netz.dns.zuruecksetzen nur fuer einen festen IPv4-Eintrag (Familie 2) auf einem
        /// verbundenen physischen DHCP-Anschluss. Nur IPv6 fest, feste Adresse (Dhcp false) oder ein
        /// nicht verbundener Adapter: Karte ohne diese Massnahme, der Rat nennt den festen Eintrag weiter.
        /// Dasselbe Bild wie (d), im Speicher gedreht.
        /// </summary>
        static void DnsZuruecksetzenGrenzen(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (d) Grenzen: nur IPv6 fest, feste Adresse, Adapter ohne Verbindung => kein netz.dns.zuruecksetzen");
            var s = h.Bild("gepflanzt-netz-dns-nxdomain.json"); if (s == null) return;
            var fest = s.Netz.Dns.FirstOrDefault(d => d.Statisch == true && d.Familie == 2);
            var a = s.Netz.Adapter.FirstOrDefault(x => fest != null && x.Index == fest.AdapterIndex);
            h.Ist("Grundmenge: fester IPv4-Eintrag auf Adapter 8, der physisch, Up und DHCP ist", fest != null && a != null && a.Index == 8 && a.Physisch && a.OpStatus == 1 && a.Dhcp == true);
            if (fest == null || a == null) return;
            var physischUp = s.Netz.Adapter.Where(x => x.Physisch && x.OpStatus == 1).ToList();
            h.Ist("Grundmenge: StatischerDnsAdapter liefert 8", NetzRegel.StatischerDnsAdapter(s.Netz, physischUp) == 8, NetzRegel.StatischerDnsAdapter(s.Netz, physischUp).ToString());

            // Nur IPv6 fest: die Massnahme setzt nur IPv4 zurueck, der Nachweis liest nur Familie 2.
            fest.Familie = 23;
            fest.Server.Clear(); fest.Server.Add("2001:db8::53");
            var erg = h.Pruefen(s);
            var dns = Harness.Befunde(erg, Bereich.Netz).FirstOrDefault(b => b.Schluessel == "netz.sonde.dns");
            h.Ist("nur IPv6 fest: netz.sonde.dns bleibt bad mit netz.dnscache.leeren", dns != null && dns.Zustand == Zustand.Bad && dns.Massnahmen.Contains(NetzRegel.MDnsCacheLeeren));
            h.Ist("nur IPv6 fest: KEIN netz.dns.zuruecksetzen, kein Parameter adapter", dns != null && !dns.Massnahmen.Contains(NetzRegel.MDnsZuruecksetzen) && !dns.Parameter.ContainsKey(NetzRegel.ParamAdapter), dns == null ? "null" : string.Join(",", dns.Massnahmen));
            h.Ist("nur IPv6 fest: Rat nennt die festen Namensserver weiter (von Hand)", dns != null && dns.Rat.Contains("fest eingetragenen Namensserver") && dns.Rat.Contains("von Hand"), dns == null ? null : dns.Rat);
            h.Ist("nur IPv6 fest: StatischerDnsAdapter liefert null", NetzRegel.StatischerDnsAdapter(s.Netz, physischUp) == null);
            h.Ist("nur IPv6 fest: Info netz.dns.statisch.8 bleibt (Familie egal)", Harness.Einer(erg, "netz.dns.statisch.8") != null);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Feste Adresse: der Helfer lehnt einen Adapter ohne DHCP ab, die Karte verspricht es nicht.
            fest.Familie = 2;
            fest.Server.Clear(); fest.Server.Add("8.8.8.8");
            a.Dhcp = false;
            erg = h.Pruefen(s);
            dns = Harness.Befunde(erg, Bereich.Netz).FirstOrDefault(b => b.Schluessel == "netz.sonde.dns");
            h.Ist("feste Adresse (Dhcp false): KEIN netz.dns.zuruecksetzen, kein Parameter adapter", dns != null && !dns.Massnahmen.Contains(NetzRegel.MDnsZuruecksetzen) && !dns.Parameter.ContainsKey(NetzRegel.ParamAdapter), dns == null ? "null" : string.Join(",", dns.Massnahmen));
            h.Ist("feste Adresse: StatischerDnsAdapter liefert null", NetzRegel.StatischerDnsAdapter(s.Netz, physischUp) == null);
            a.Dhcp = null;
            h.Ist("DHCP-Status unbekannt (null): StatischerDnsAdapter liefert null (nicht eindeutig = keine Massnahme)", NetzRegel.StatischerDnsAdapter(s.Netz, physischUp) == null);

            // Zurueck auf DHCP, dann der Anschluss selbst getrennt: sein Namensserver erklaert die Sonde nicht.
            a.Dhcp = true;
            h.Ist("Gegenprobe: wieder DHCP => StatischerDnsAdapter liefert 8", NetzRegel.StatischerDnsAdapter(s.Netz, physischUp) == 8);
            h.Ist("Adapter nicht in physischUp: StatischerDnsAdapter liefert null", NetzRegel.StatischerDnsAdapter(s.Netz, new List<Adapter>()) == null);
            // Ein zweiter, fester IPv4-Eintrag auf einem fremden Index (nicht verbunden) zaehlt nicht, der verbundene gewinnt.
            s.Netz.Dns.Insert(0, new DnsKonfig { AdapterIndex = 99, Familie = 2, Statisch = true, Server = new List<string> { "1.1.1.1" } });
            h.Ist("fremder fester Eintrag (Index 99, nicht verbunden) vor dem echten: StatischerDnsAdapter liefert weiter 8", NetzRegel.StatischerDnsAdapter(s.Netz, physischUp) == 8, NetzRegel.StatischerDnsAdapter(s.Netz, physischUp).ToString());
            erg = h.Pruefen(s);
            dns = Harness.Befunde(erg, Bereich.Netz).FirstOrDefault(b => b.Schluessel == "netz.sonde.dns");
            h.Ist("Gegenprobe: mit DHCP und IPv4 fest => netz.dns.zuruecksetzen mit adapter = 8", dns != null && dns.Massnahmen.Contains(NetzRegel.MDnsZuruecksetzen) && dns.Parameter.ContainsKey(NetzRegel.ParamAdapter) && dns.Parameter[NetzRegel.ParamAdapter] == "8", dns == null ? "null" : string.Join(",", dns.Parameter.Select(kv => kv.Key + "=" + kv.Value)));
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

        // ------------------------------------------------------------ (e2) Netzwerk-Stack gestoert (M4)

        // Gepflanzt im Speicher aus dem gesunden Bild (docs/M4-ENTWURF.md, Abschnitt 2.4): Router und
        // Namensdienst antworten, Testseite und TCP 443 scheitern, kein Proxy => genau ein bad
        // netz.stack.gestoert mit Winsock- und IP-Reset in dieser Reihenfolge, kein netz.sonde.ncsi,
        // kein netz.verbindung. Jede der vier Sonden und jede der drei Proxy-Sichten ist eine Gegenprobe.
        static void StackGestoert(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz (e2): Router ja, DNS ja, Testseite nein, TCP 443 nein, kein Proxy => bad netz.stack.gestoert mit Winsock- und IP-Reset");
            var s = h.Bild("gepflanzt-netz-gesund.json"); if (s == null) return;
            h.Ist("Grundmenge: ein physischer Adapter Up, kein Proxy (WinINet aus, WinHTTP 1, keine PAC)",
                  s.Netz.Adapter.Any(a => a.Physisch && a.OpStatus == 1) && s.Netz.Proxy.WinInetAktiv == false && s.Netz.Proxy.WinHttpTyp == 1 && string.IsNullOrEmpty(s.Netz.Proxy.PacUrl));
            var so = s.Netz.Sonden;
            so.Gateway = true; so.Dns = true; so.DnsFehler = null; so.Ncsi = false; so.NcsiInhaltStimmt = false; so.Tcp443 = false;
            h.Ist("Grundmenge: StackGestoert(Sonden, Proxy) ist wahr", NetzRegel.StackGestoert(so, s.Netz.Proxy));
            var erg = h.Pruefen(s);
            var st = Harness.AlleMit(erg, "netz.stack.gestoert").ToList();
            h.Ist("genau ein Befund netz.stack.gestoert", st.Count == 1, st.Count.ToString());
            if (st.Count == 1)
            {
                var b = st[0];
                h.Ist("Zustand bad", b.Zustand == Zustand.Bad, b.Zustand);
                h.Ist("Kennungen: erst netz.winsock.zuruecksetzen, dann netz.ip.zuruecksetzen, sonst nichts", b.Massnahmen.Count == 2 && b.Massnahmen[0] == NetzRegel.MWinsockZuruecksetzen && b.Massnahmen[1] == NetzRegel.MIpZuruecksetzen, string.Join(",", b.Massnahmen));
                h.Ist("Konstanten tragen die Katalog-Kennungen", NetzRegel.MWinsockZuruecksetzen == "netz.winsock.zuruecksetzen" && NetzRegel.MIpZuruecksetzen == "netz.ip.zuruecksetzen" && NetzRegel.MHostsBereinigen == "netz.hosts.bereinigen");
                h.Ist("keine Parameter", b.Parameter.Count == 0, string.Join(",", b.Parameter.Keys));
                h.Ist("Satz nennt die vier Sonden (Router, Namensdienst, Testseite, TCP 443) und den fehlenden Proxy", b.Satz.Contains("Router antwortet") && b.Satz.Contains("Namensdienst antwortet") && b.Satz.Contains("Internet-Testseite") && b.Satz.Contains("TCP 443") && b.Satz.Contains("kein Proxy"), b.Satz);
                h.Ist("Rat: erst Winsock (Neustart), bleibt es, den IP-Stack", b.Rat != null && b.Rat.StartsWith("Erst Winsock") && b.Rat.Contains("Neustart") && b.Rat.Contains("bleibt es") && b.Rat.Contains("IP-Stack"), b.Rat);
                h.Ist("Messwert 2 von 4 Sonden, Schwelle 4", b.Messwert != null && b.Messwert.Wert == "2" && b.Messwert.Schwelle == "4");
                h.Ist("Detail nennt alle vier Sonden mit Ergebnis und den Proxy-Stand", b.Detail.Any(d => d.Contains("Router ja") && d.Contains("Namensdienst ja") && d.Contains("Testseite nein") && d.Contains("TCP 443 nein")) && b.Detail.Any(d => d.StartsWith("Proxy:") && d.Contains("AutoConfigURL keine")), string.Join(" | ", b.Detail));
            }
            h.Ist("kein netz.sonde.ncsi daneben (eine Ursache, ein Befund)", Harness.Einer(erg, "netz.sonde.ncsi") == null);
            h.Ist("kein netz.verbindung (ok) daneben", Harness.Einer(erg, "netz.verbindung") == null);
            h.Ist("kein netz.sonde.gateway, kein netz.sonde.dns (beide antworten)", Harness.Einer(erg, "netz.sonde.gateway") == null && Harness.Einer(erg, "netz.sonde.dns") == null);
            h.Ist("Bereich Netz ist bad", Harness.Bereich(erg, Bereich.Netz).Zustand == Zustand.Bad);
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Gegenprobe: WinINet-Proxy aktiv => der Proxy erklaert die versagenden Sonden: netz.sonde.ncsi
            // warn statt stack.gestoert (nur ohne Proxy, docs/M4-ENTWURF.md, Abschnitt 2.4).
            s.Netz.Proxy.WinInetAktiv = true; s.Netz.Proxy.WinInetServer = "proxy.firma.example:3128";
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe WinINet-Proxy aktiv: kein netz.stack.gestoert, stattdessen netz.sonde.ncsi warn", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.ncsi") != null && Harness.Einer(erg, "netz.sonde.ncsi").Zustand == Zustand.Warn);
            h.Ist("Gegenprobe WinINet-Proxy: StackGestoert ist falsch", !NetzRegel.StackGestoert(so, s.Netz.Proxy));
            s.Netz.Proxy.WinInetAktiv = false; s.Netz.Proxy.WinInetServer = null;
            // Gegenprobe: WinHTTP-Proxy (Typ 3, Windows Update) => ebenfalls kein stack.gestoert.
            s.Netz.Proxy.WinHttpTyp = 3; s.Netz.Proxy.WinHttpServer = "proxy.firma.example:3128";
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe WinHTTP-Typ 3: kein netz.stack.gestoert", Harness.Einer(erg, "netz.stack.gestoert") == null && !NetzRegel.StackGestoert(so, s.Netz.Proxy));
            s.Netz.Proxy.WinHttpTyp = 1; s.Netz.Proxy.WinHttpServer = null;
            // Gegenprobe: Proxy-Skript (PAC) => kein stack.gestoert.
            s.Netz.Proxy.PacUrl = "http://wpad.example/wpad.dat";
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe AutoConfigURL: kein netz.stack.gestoert", Harness.Einer(erg, "netz.stack.gestoert") == null && !NetzRegel.StackGestoert(so, s.Netz.Proxy));
            s.Netz.Proxy.PacUrl = null;
            // Gegenprobe (Nachbesserung 18.09.2026): "kein Proxy" muss GELESEN sein. WinHTTP-Typ null ist
            // kein Beleg fuer "kein Proxy", also kein bad-Urteil: es bleibt bei netz.sonde.ncsi (warn),
            // dessen Satz nichts ueber den Proxy behauptet.
            s.Netz.Proxy.WinHttpTyp = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe WinHTTP-Typ null: kein netz.stack.gestoert, netz.sonde.ncsi warn", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.ncsi") != null && Harness.Einer(erg, "netz.sonde.ncsi").Zustand == Zustand.Warn && !NetzRegel.StackGestoert(so, s.Netz.Proxy));
            s.Netz.Proxy.WinHttpTyp = 1;
            // Gegenprobe: WinINet nicht gelesen (null) ebenso.
            s.Netz.Proxy.WinInetAktiv = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe WinINet null: kein netz.stack.gestoert, netz.sonde.ncsi warn", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.ncsi") != null && !NetzRegel.StackGestoert(so, s.Netz.Proxy));
            s.Netz.Proxy.WinInetAktiv = false;
            h.Ist("StackGestoert(so, null) ist falsch (kein Proxy-Objekt, nichts belegt)", !NetzRegel.StackGestoert(so, null));
            h.Ist("StackGestoert(so, new Proxy()) ist falsch (alle Felder null, nichts belegt)", !NetzRegel.StackGestoert(so, new Proxy()));
            // Gegenprobe: die Proxy-Quelle meldet einen Fehler (Felder wie gelesen, aber Fehlereintrag):
            // der Zweig ist gesperrt, weil Daten ODER Fehlereintrag gilt, nie beides.
            s.Fehlerliste.Add(new Fehler { Quelle = "api.netz.proxy.winhttp", Art = Fehler.Ausnahme, Text = "Win32Exception: WinHttpGetDefaultProxyConfiguration" });
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe Fehlereintrag api.netz.proxy.*: kein netz.stack.gestoert, netz.sonde.ncsi warn, 'Proxy-Einstellungen' unter fehlend", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.ncsi") != null && Harness.Bereich(erg, Bereich.Netz).Fehlend.Contains("Proxy-Einstellungen"), string.Join(" | ", Harness.Bereich(erg, Bereich.Netz).Fehlend));
            s.Fehlerliste.RemoveAll(f => f.Quelle == "api.netz.proxy.winhttp");
            erg = h.Pruefen(s);
            h.Ist("Grundmenge wiederhergestellt: netz.stack.gestoert wieder da", Harness.Einer(erg, "netz.stack.gestoert") != null);

            // Gegenproben je Sonde: fehlt eine der vier Bedingungen, gibt es keinen stack.gestoert.
            so.Tcp443 = true;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe TCP 443 ja: kein netz.stack.gestoert, netz.sonde.ncsi warn", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.ncsi") != null);
            so.Tcp443 = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe TCP 443 nicht gemessen: kein netz.stack.gestoert (null ist kein Beleg)", Harness.Einer(erg, "netz.stack.gestoert") == null);
            so.Tcp443 = false;
            so.Gateway = null;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe Router nicht gemessen: kein netz.stack.gestoert", Harness.Einer(erg, "netz.stack.gestoert") == null);
            so.Gateway = false;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe Router nein: netz.sonde.gateway bad, kein netz.stack.gestoert", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.gateway") != null);
            so.Gateway = true;
            so.Dns = false; so.DnsFehler = "timeout";
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe DNS timeout: netz.sonde.dns bad, kein netz.stack.gestoert", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.sonde.dns") != null);
            so.Dns = true; so.DnsFehler = null;
            so.Ncsi = true; so.NcsiInhaltStimmt = true;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe Testseite ja (TCP 443 weiter nein): kein netz.stack.gestoert, netz.verbindung ok", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.verbindung") != null);
            so.Ncsi = false; so.NcsiInhaltStimmt = false;
            // Gegenprobe: kein physischer Adapter Up => Stufe 1 meldet, der Stack nicht.
            foreach (var a in s.Netz.Adapter.Where(a => a.Physisch)) a.OpStatus = 2;
            erg = h.Pruefen(s);
            h.Ist("Gegenprobe kein Adapter Up: netz.verbindung.keine, kein netz.stack.gestoert", Harness.Einer(erg, "netz.stack.gestoert") == null && Harness.Einer(erg, "netz.verbindung.keine") != null);
            h.Ist("StackGestoert(null, null) ist falsch", !NetzRegel.StackGestoert(null, null));
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
            h.Ist("ohne Nachmessung (aeltere Aufzeichnung) keine Nachmess-Zeile", k != null && !k.Detail.Any(d => d.Contains("erneut gemessen")));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz));

            // Nachmessung (seit 01.10.2026): der Sammler hat 10 s nachgemessen und weiter nichts gefunden.
            // Wirksamkeit 2026-10-01: die Detailzeile im bad-Zweig entfernt -> diese Zusicherung rot; zurueck.
            s.Netz.NachgemessenMs = 10040;
            k = Harness.Einer(h.Pruefen(s), "netz.verbindung.keine");
            h.Ist("Nachmessung ohne Erfolg: bad bleibt, Detail 'Nach 10 Sekunden erneut gemessen'", k != null && k.Zustand == Zustand.Bad && k.Detail.Any(d => d.StartsWith("Nach 10 Sekunden erneut gemessen")), k == null ? null : string.Join(" | ", k.Detail));
            h.Ist("Nachmessung ohne Erfolg: kein netz.verbindung.kurzweg daneben", Harness.Einer(h.Pruefen(s), "netz.verbindung.kurzweg") == null);
            s.Netz.NachgemessenMs = null;

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
            h.Ist("Gegenprobe: netz.gateway.fehlt traegt Parameter adapter = Index des Ethernet-Adapters", fehlt != null && fehlt.Parameter.ContainsKey(NetzRegel.ParamAdapter) && fehlt.Parameter[NetzRegel.ParamAdapter] == s.Netz.Adapter[0].Index.ToString(System.Globalization.CultureInfo.InvariantCulture), fehlt == null ? "null" : string.Join(",", fehlt.Parameter.Select(kv => kv.Key + "=" + kv.Value)));
            h.Ist("Gegenprobe: netz.verbindung.keine (kein Anschluss) traegt keinen Parameter adapter", k != null && !k.Parameter.ContainsKey(NetzRegel.ParamAdapter));

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
        // ------------------------------------------------------------ Verbindung nur kurz weg

        // Gemessen im PC-Audit vom 30.09.2026: der Link riss bei der Anmeldung 3 s ab, genau waehrend
        // des Pruefens, und v8.2 meldete "Keine Netzwerkverbindung" (bad). Seit 01.10.2026 misst der
        // Sammler nach; steht dann ein Anschluss Up, ist das ein ok-Hinweis.
        // Wirksamkeit 2026-10-01: "else if (n.NachgemessenMs.HasValue)" auf "else if (false)" gesetzt ->
        // "netz.verbindung.kurzweg ok" rot; zurueck.
        static void KurzUnterbrochen(Harness h, List<Befund> alle)
        {
            h.Gruppe("Netz: erste Messung ohne Anschluss, Nachmessung mit Anschluss => ok 'Verbindung war kurz unterbrochen'");
            var s = h.Bild("gepflanzt-netz-gesund.json"); if (s == null) return;
            int physisch = s.Netz.Adapter.Count(a => a.Physisch);
            h.Ist("Grundmenge: ein physischer Anschluss Up, Nachmessung nicht gesetzt", s.Netz.Adapter.Any(a => a.Physisch && a.OpStatus == 1) && !s.Netz.NachgemessenMs.HasValue);
            h.Ist("Grundmenge: ohne Nachmessung kein netz.verbindung.kurzweg", Harness.Einer(h.Pruefen(s), "netz.verbindung.kurzweg") == null);

            s.Netz.NachgemessenMs = 2960;
            var erg = h.Pruefen(s);
            var k = Harness.Einer(erg, "netz.verbindung.kurzweg");
            h.Ist("Nachmessung erfolgreich: netz.verbindung.kurzweg ok, kein Problem", k != null && k.Zustand == Zustand.Ok && !k.IstProblem, k == null ? null : k.Zustand);
            h.Ist("Satz nennt die Zahl der Anschluesse und 'nach 3 Sekunden'", k != null && k.Satz.Contains("keiner der " + physisch + " ") && k.Satz.Contains("nach 3 Sekunden"), k == null ? null : k.Satz);
            h.Ist("kein netz.verbindung.keine daneben", Harness.Einer(erg, "netz.verbindung.keine") == null);
            h.Ist("Messwert und Quelle gesetzt", k != null && k.Messwert != null && !string.IsNullOrEmpty(k.Quelle));
            alle.AddRange(Harness.Befunde(erg, Bereich.Netz).Where(b => b.Schluessel == "netz.verbindung.kurzweg"));

            s.Netz.NachgemessenMs = 400;
            k = Harness.Einer(h.Pruefen(s), "netz.verbindung.kurzweg");
            h.Ist("unter einer Sekunde: 'nach 1 Sekunde' (Einzahl, nie 0)", k != null && k.Satz.Contains("nach 1 Sekunde ") , k == null ? null : k.Satz);
        }

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
