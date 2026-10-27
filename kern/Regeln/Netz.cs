using System;
using System.Collections.Generic;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Netzwerk-Regeln: reine Funktion ueber dem Systembild. Kein Windows-Zugriff, keine Uhr.
    ///
    /// Das Stufenmodell der Verbindung: (1) physischer Adapter Up, (2) Adresse nicht APIPA,
    /// (3) Router antwortet, (4) Namensdienst antwortet richtig, (5) Internet-Testseite,
    /// (6) TCP 443. Gemeldet wird die ERSTE Stufe, die versagt; alles dahinter ist Folge und
    /// bekommt keinen eigenen Befund (sonst stuenden bei einem gezogenen Kabel vier Alarme).
    /// Versagen (5) und (6) zusammen, waehrend (3) und (4) antworten und kein Proxy im Weg
    /// steht, ist das der gestoerte Netzwerk-Stack (netz.stack.gestoert, seit M4 mit Winsock-
    /// und IP-Reset), nicht die Testseite allein.
    ///
    /// Unabhaengig davon: fest eingetragene DNS-Server auf einem DHCP-Anschluss (Info, kann
    /// gewollt sein), Proxy auf dem eigenen Rechner (Warnung), Proxy-Skript (Warnung mit
    /// Frage; ok nur im Domaenennetz oder nach "so lassen"), hosts-Umleitungen von Update-,
    /// Defender-, NCSI- und Virenschutz-Domains (Fehler, Folge je Domainklasse).
    ///
    /// Grundsatz "keine erfundenen Diagnosen": ein Router, der nicht auf Ping antwortet,
    /// waehrend die Internet-Testseite antwortet, ist kein Fehler, sondern ein Router, der
    /// Ping blockiert. Ein fehlender Router-Eintrag bei antwortender Testseite ist eine
    /// Direktverbindung (PPPoE, Mobilfunk, VPN), kein Fehler. Eine nicht gelesene Routen-
    /// tabelle ist "nicht geprueft", nie "kein Router". Eine Netzwerkkategorie "oeffentlich"
    /// im Heimnetz ist kein Befund.
    /// </summary>
    public static class Netz
    {
        // Massnahmen-Kennungen (Katalog kern/Massnahmen.cs seit Meilenstein 3; Stufe 3 seit M4,
        // docs/M4-ENTWURF.md, Abschnitt 1 und 2.4).
        public const string MDhcpErneuern = "netz.dhcp.erneuern";
        public const string MDnsCacheLeeren = "netz.dnscache.leeren";
        public const string MDnsZuruecksetzen = "netz.dns.zuruecksetzen";
        public const string MHostsBereinigen = "netz.hosts.bereinigen";
        public const string MWinsockZuruecksetzen = "netz.winsock.zuruecksetzen";
        public const string MIpZuruecksetzen = "netz.ip.zuruecksetzen";

        /// <summary>
        /// Parameter fuer den Plan (docs/M3-ENTWURF.md, Abschnitt 1): der Adapter-Index
        /// (MSFT_NetAdapter.InterfaceIndex = Win32_NetworkAdapterConfiguration.InterfaceIndex) als
        /// Text. netz.dhcp.erneuern und netz.dns.zuruecksetzen brauchen ihn; der Helfer prueft
        /// ihn erneut gegen sein eigenes Bild, die Oberflaeche schickt nie eigene Werte.
        /// </summary>
        public const string ParamAdapter = "adapter";

        /// <summary>
        /// Domains, deren Umleitung in der hosts-Datei ein Angriffs- oder Schadsoftware-Muster
        /// ist, je Klasse: Windows Update, Defender-Cloud und -Updates (Virenschutz), die
        /// NCSI-Sonden (Internet-Erkennung). Treffer = Host gleich oder Unterdomain. Quelle:
        /// learn.microsoft.com "Connections to Windows Update" (windowsupdate.com,
        /// update.microsoft.com, delivery.mp.microsoft.com), "Microsoft Defender Antivirus
        /// cloud service connections" (wdcp.microsoft.com, wd.microsoft.com), "NCSI FAQ"
        /// (msftconnecttest.com, msftncsi.com).
        /// </summary>
        public const string HostsUpdate = "update", HostsVirenschutz = "virenschutz", HostsNcsi = "ncsi";

        static readonly string[] UpdateDomains = { "windowsupdate.com", "update.microsoft.com", "delivery.mp.microsoft.com" };
        static readonly string[] VirenschutzDomains = { "wdcp.microsoft.com", "wd.microsoft.com" };
        static readonly string[] NcsiDomains = { "msftconnecttest.com", "msftncsi.com" };

        /// <summary>
        /// Bekannte Virenschutz-Hersteller: ein Label des Hostnamens, das so beginnt. Dieselbe Liste
        /// entscheidet in Regeln/Leistung.cs, ob ein Dienst eine Schutzsoftware ist (Signierer oder
        /// Anzeigename, siehe IstAvHersteller): der bekommt keine Startart-Massnahme. Zweiteilige
        /// Namen stehen zusammengezogen (trendmicro, fsecure, gdata), IstAvHersteller zieht
        /// benachbarte Woerter ebenso zusammen. Ein Produkt, das hier fehlt, faengt Leistung.cs
        /// ueber die im Sicherheitscenter registrierten Namen (Sicherheit.DrittAv) ab.
        /// </summary>
        static readonly string[] AvHersteller =
        {
            "avast", "avg", "avira", "bitdefender", "comodo", "crowdstrike", "cylance", "emsisoft", "eset",
            "fsecure", "gdata", "kaspersky", "malwarebytes", "mcafee", "norton", "panda", "sentinelone",
            "sophos", "symantec", "trendmicro", "webroot",
        };

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var n = s.Netz;
            var e = new BereichErgebnis { Bereich = Bereich.Netz };

            e.DatenVorhanden = n.Adapter.Count > 0;
            if (!e.DatenVorhanden)
            {
                e.Fehlend.Add(MitArt(s, "wmi.netz.adapter", "Netzwerkkarten"));
                return e;
            }
            var so = n.Sonden ?? new Sonden();
            var physischUp = n.Adapter.Where(a => a.Physisch && a.OpStatus == 1).ToList();
            bool ursacheGefunden = false;   // eine versagende Stufe erklaert die folgenden
            bool routenGelesen = !s.FehlerVon("wmi.netz.route").Any();

            // Was nebenbei fehlte, in Alltagssprache fuer die Zeile "liess sich nicht pruefen".
            // Jede Quelle, die nichts lieferte, steht hier, damit eine leere Liste nie wie ein
            // Messwert aussieht (Grundsatz: Daten ODER Fehlereintrag).
            if (s.FehlerVon("wmi.netz.ipadresse").Any()) e.Fehlend.Add(MitArt(s, "wmi.netz.ipadresse", "IP-Adressen"));
            if (!routenGelesen) e.Fehlend.Add(MitArt(s, "wmi.netz.route", "Standardgateway (Routentabelle)"));
            if (s.FehlerVon("wmi.netz.dns").Any()) e.Fehlend.Add(MitArt(s, "wmi.netz.dns", "Namensserver"));
            // DHCP-Status: null entsteht auch ohne Fehlereintrag, wenn zum Adapter keine IPv4-Zeile kommt.
            if (physischUp.Any(a => a.Dhcp == null)) e.Fehlend.Add(MitArt(s, "wmi.netz.ipinterface", "DHCP-Status"));
            if (physischUp.Count > 0 && s.FehlerVon("wmi.netz.profil").Any()) e.Fehlend.Add(MitArt(s, "wmi.netz.profil", "Verbindungsprofil"));
            if (s.FehlerVon("sonde.netz").Any()) e.Fehlend.Add("Teile der Verbindungsprobe");
            if (s.FehlerVon("datei.netz.hosts").Any()) e.Fehlend.Add("hosts-Datei");
            // DataBasePath (seit M4): scheitert das Lesen, bleibt HostsPfadAbweichend null, und null
            // saehe sonst wie "Standardort" aus; deshalb hier nennen und unten keine Massnahme anhaengen.
            bool hostsPfadGelesen = !s.FehlerVon("registry.netz.hostspfad").Any();
            if (!hostsPfadGelesen) e.Fehlend.Add("Ort der hosts-Datei (DataBasePath)");
            if (s.FehlerVon("api.netz.proxy").Any()) e.Fehlend.Add("Proxy-Einstellungen");
            if (s.FehlerVon("registry.netz.tcpip").Any()) e.Fehlend.Add("DNS-Herkunft (statisch oder automatisch)");

            // ---------------------------------------------------------- (1) Adapter
            if (physischUp.Count == 0)
            {
                var b = Neu("netz.verbindung.keine", "MSFT_NetAdapter.InterfaceOperationalStatus");
                b.Messwert = Messwert.Von(0, "verbundene Anschlüsse", "mindestens 1");
                foreach (var a in n.Adapter.Where(a => a.Physisch))
                    b.Detail.Add(Anzeige(a) + ": Zustand " + OpStatusText(a.OpStatus) + ", Medium " + MedienText(a.Medien));
                if (n.Adapter.Count(a => a.Physisch) == 0) b.Detail.Add("Kein Adapter mit Anschluss (ConnectorPresent) im Inventar; nur virtuelle: " + n.Adapter.Count);
                if (so.Ncsi == true)
                {
                    // Internet geht trotzdem (virtueller Adapter, VPN, Tethering ohne Anschluss-Flag).
                    b.Zustand = Zustand.Ok;
                    b.Titel = "Internet über einen virtuellen Anschluss";
                    b.Satz = "Kein Anschluss mit Kabel oder WLAN ist verbunden, aber die Internet-Testseite hat geantwortet.";
                    b.Detail.Add("Die Verbindung läuft über einen Adapter ohne physischen Anschluss (z. B. VPN oder virtueller Switch).");
                }
                else
                {
                    b.Zustand = Zustand.Bad;
                    b.Titel = "Keine Netzwerkverbindung";
                    b.Satz = n.Adapter.Any(a => a.Physisch)
                        ? "Keiner der " + n.Adapter.Count(a => a.Physisch) + " Netzwerkanschlüsse ist verbunden."
                        : "Kein Netzwerkanschluss mit Kabel oder WLAN ist verbunden.";
                    b.Rat = "Netzwerkkabel an PC und Router prüfen oder WLAN einschalten und mit dem Netz verbinden.";
                }
                if (n.NachgemessenMs.HasValue)
                    b.Detail.Add("Nach " + Sekunden(n.NachgemessenMs.Value) + " erneut gemessen: weiterhin kein Anschluss verbunden.");
                e.Befunde.Add(b);
                ursacheGefunden = true;
            }
            else if (n.NachgemessenMs.HasValue)
            {
                // Die erste Messung fand keinen Anschluss Up, die Nachmessung schon: die Verbindung war
                // nur kurz weg (Linkabriss, z. B. bei der Anmeldung). Kein Problem, aber sichtbar, damit
                // sich haeufende Abrisse auffallen.
                var kurz = Neu("netz.verbindung.kurzweg", "MSFT_NetAdapter.InterfaceOperationalStatus (zweite Messung)");
                kurz.Zustand = Zustand.Ok;
                kurz.Titel = "Verbindung war kurz unterbrochen";
                kurz.Satz = "Beim ersten Messen war keiner der " + n.Adapter.Count(a => a.Physisch) + " Netzwerkanschlüsse verbunden; nach "
                            + Sekunden(n.NachgemessenMs.Value) + " stand die Verbindung wieder.";
                kurz.Messwert = Messwert.Von(n.NachgemessenMs.Value, "ms bis zur Verbindung", null);
                kurz.Detail.Add("Häufen sich solche Unterbrechungen, lohnt ein Blick auf Kabel, Router-Anschluss und den Treiber des Netzwerkadapters.");
                e.Befunde.Add(kurz);
            }

            // ---------------------------------------------------------- (2) APIPA
            if (!ursacheGefunden)
            {
                // Dhcp null (Status nicht ermittelbar) zaehlt mit: eine 169.254-Adresse mit
                // PrefixOrigin 2 entsteht nur auf einem DHCP-aktiven Anschluss und ist selbst
                // der Beleg. Die Massnahme prueft DHCPEnabled ohnehin als zweites Schloss.
                foreach (var a in physischUp.Where(a => a.Dhcp != false))
                {
                    var apipa = n.IpAdressen.FirstOrDefault(ip => ip.AdapterIndex == a.Index && ip.Familie == 2
                                                                  && ip.Herkunft == 2 && IstApipa(ip.Adresse));
                    if (apipa == null) continue;
                    // Eine echte DHCP-Adresse daneben (Herkunft 3) heisst: der Anschluss ist versorgt.
                    if (n.IpAdressen.Any(ip => ip.AdapterIndex == a.Index && ip.Familie == 2 && ip.Herkunft == 3)) continue;
                    var b = Neu("netz.apipa." + a.Index, "MSFT_NetIPAddress.PrefixOrigin");
                    b.Zustand = Zustand.Bad;
                    b.Titel = "Kein Zuteilungsserver (DHCP) erreichbar";
                    b.Satz = Anzeige(a) + " hat nur eine Notadresse (" + apipa.Adresse + "), weil kein Router oder Server eine Adresse zugeteilt hat.";
                    b.Rat = "Router neu starten und die Verbindung prüfen; danach die Adresse neu anfordern lassen.";
                    b.Messwert = Messwert.Von(apipa.Adresse, "IPv4-Adresse", "nicht 169.254.x.x");
                    b.Detail.Add("PrefixOrigin 2 (WellKnown) = automatische private Adresse (APIPA), AddressState " + apipa.Zustand);
                    b.Detail.Add("Adapter " + a.Index + " (" + (a.Beschreibung ?? a.Name) + "), DHCP " + (a.Dhcp == true ? "aktiv" : "Status nicht ermittelbar (APIPA belegt DHCP)") + ", Zustand Up");
                    b.Massnahmen.Add(MDhcpErneuern);
                    b.Parameter[ParamAdapter] = IndexText(a);
                    e.Befunde.Add(b);
                    ursacheGefunden = true;
                }
            }

            // ---------------------------------------------------------- (2b) kein Gateway
            // Nur aus einer gelesenen Routentabelle: wurde sie nicht gelesen, steht das in
            // Fehlend, und die Sonden (4)/(5) urteilen aus dem, was gemessen wurde.
            if (!ursacheGefunden && routenGelesen && n.Gateways.Count == 0)
            {
                var a = physischUp[0];
                bool onLink = n.StandardrouteOnLink == true;
                if (so.Ncsi == true)
                {
                    // Internet geht ohne Router-Eintrag: Punkt-zu-Punkt-Anschluss (DSL-Einwahl,
                    // Mobilfunk) oder ein VPN, das die Standardroute anders setzt.
                    var b = Neu("netz.gateway.direkt", "MSFT_NetRoute 0.0.0.0/0, Sonden");
                    b.Zustand = Zustand.Ok;
                    b.Titel = "Internet ohne Router-Eintrag";
                    b.Satz = onLink
                        ? Anzeige(a) + " ist über eine Direktverbindung ohne Router verbunden (z. B. DSL-Einwahl oder Mobilfunk), und die Internet-Testseite hat geantwortet."
                        : Anzeige(a) + " ist verbunden, und die Internet-Testseite hat geantwortet, obwohl kein Router (Standardgateway) eingetragen ist (z. B. über ein VPN).";
                    b.Messwert = Messwert.Von(onLink ? "On-Link" : "keine", "Standardroute", null);
                    b.Detail.Add(onLink ? "Standardroute 0.0.0.0/0 mit NextHop 0.0.0.0 (On-Link): kein Router zum Anpingen, Stufe 3 entfällt." : "Keine Standardroute 0.0.0.0/0 in MSFT_NetRoute; die Verbindung läuft über einen anderen Weg (VPN-Routen 0.0.0.0/1 und 128.0.0.0/1 zählen hier nicht).");
                    e.Befunde.Add(b);
                }
                else if (onLink && so.Ncsi == null)
                {
                    // Direktverbindung eingetragen, Internet nicht gemessen: kein Urteil moeglich.
                    e.Fehlend.Add("Internet-Erreichbarkeit der Direktverbindung (Testseite nicht gemessen)");
                }
                else
                {
                    var b = Neu("netz.gateway.fehlt", "MSFT_NetRoute 0.0.0.0/0");
                    b.Zustand = Zustand.Bad;
                    if (onLink)
                    {
                        b.Titel = "Direktverbindung ohne Antwort aus dem Internet";
                        b.Satz = Anzeige(a) + " hat eine Standardroute ohne Router (Direktverbindung, z. B. DSL-Einwahl oder Mobilfunk), aber die Internet-Testseite antwortet nicht.";
                        b.Rat = "Die Einwahl- oder Mobilfunkverbindung trennen und neu aufbauen; auf einem Kabel- oder WLAN-Anschluss gehört hier die Adresse des Routers hin.";
                        b.Messwert = Messwert.Von("On-Link", "Standardroute", "Router-Adresse oder antwortende Testseite");
                        b.Detail.Add("Standardroute 0.0.0.0/0 mit NextHop 0.0.0.0 (On-Link) auf einem Anschluss, dessen Testseite (HTTP) nicht antwortet.");
                    }
                    else
                    {
                        b.Titel = "Kein Router eingetragen";
                        b.Satz = Anzeige(a) + " ist verbunden, aber kein Router (Standardgateway) ist eingetragen; ohne ihn führt kein Weg ins Internet.";
                        // Dreiwertig: DHCP an, feste Adresse, oder Status unbekannt (dann nichts erfinden).
                        b.Rat = a.Dhcp == true
                            ? "Router neu starten; bleibt es dabei, die Adresse per DHCP neu anfordern."
                            : a.Dhcp == false
                                ? "Die feste Adresskonfiguration prüfen: das Feld „Standardgateway“ muss die Adresse des Routers enthalten."
                                : "Die Adresszuteilung (DHCP oder fest) in den Adaptereinstellungen prüfen; bei fester Adresse muss das Feld „Standardgateway“ die Adresse des Routers enthalten.";
                        b.Messwert = Messwert.Von(0, "Standardrouten", "mindestens 1");
                        // Der Befund handelt von diesem Anschluss: der Index steht auch ohne
                        // Massnahme dabei (Dhcp false oder null), damit Nachmessung und Verlauf
                        // wissen, welcher Adapter gemeint war.
                        b.Parameter[ParamAdapter] = IndexText(a);
                        if (a.Dhcp == true) b.Massnahmen.Add(MDhcpErneuern);
                    }
                    e.Befunde.Add(b);
                    ursacheGefunden = true;
                }
            }

            // ---------------------------------------------------------- (3) Router antwortet
            if (!ursacheGefunden && so.Gateway == false)
            {
                string gw = n.Gateways.FirstOrDefault(g => g.IndexOf(':') < 0) ?? n.Gateways[0];
                var b = Neu("netz.sonde.gateway", "Ping.Send(Standardgateway)");
                b.Messwert = Messwert.Von(gw, "Router", "antwortet");
                b.Detail.Add("Ping-Antwort vom Standardgateway: keine (Sonde " + so.DauerMs + " ms gesamt)");
                if (so.Ncsi == true)
                {
                    b.Zustand = Zustand.Ok;
                    b.Titel = "Router antwortet nicht auf Ping, Internet erreichbar";
                    b.Satz = "Der Router (" + gw + ") antwortet nicht auf eine Anfrage, die Internet-Testseite aber schon; er blockiert nur die Anfrage.";
                }
                else
                {
                    b.Zustand = Zustand.Bad;
                    b.Titel = "Router antwortet nicht";
                    b.Satz = "Der Router (" + gw + ") antwortet nicht auf eine Anfrage, und die Internet-Testseite ist nicht erreichbar.";
                    b.Rat = "Router neu starten (Strom für 10 Sekunden trennen) und prüfen, ob Kabel oder WLAN richtig verbunden sind.";
                    ursacheGefunden = true;
                }
                e.Befunde.Add(b);
            }

            // ---------------------------------------------------------- (4) Namensdienst
            if (!ursacheGefunden && so.Dns == false)
            {
                string art = so.DnsFehler ?? "andere";
                // Der Anschluss mit fest eingetragenen IPv4-Servern ist das Ziel von netz.dns.zuruecksetzen:
                // nur ein verbundener physischer DHCP-Anschluss (die Massnahme setzt nur IPv4 zurueck
                // und lehnt feste Adressen ab). Ein fester Eintrag anderswo (IPv6, fester Anschluss,
                // Adapter ohne Verbindung) bleibt ein Rat, nie eine Massnahme, die der Helfer ablehnt.
                var statisch = StatischerDnsAdapter(n, physischUp);
                bool statischDa = statisch.HasValue;
                bool festIrgendwo = n.Dns.Any(d => d.Statisch == true && d.Server.Any(x => !IstDnsPlatzhalter(x)));
                if (art == "nxdomain" || art == "timeout")
                {
                    var b = Neu("netz.sonde.dns", "Dns.GetHostAddresses(ActiveDnsProbeHost)");
                    b.Zustand = Zustand.Bad;
                    b.Titel = "Namensdienst (DNS) antwortet nicht richtig";
                    b.Satz = art == "nxdomain"
                        ? "Der Namensdienst meldet die Windows-Testadresse als unbekannt, obwohl sie immer existiert; Webseiten sind so nicht erreichbar."
                        : "Der Namensdienst hat innerhalb der Wartezeit nicht geantwortet; Webseiten sind so nicht erreichbar.";
                    b.Rat = statischDa
                        ? "DNS-Zwischenspeicher leeren; die fest eingetragenen Namensserver auf automatisch zurücksetzen."
                        : festIrgendwo
                            ? "DNS-Zwischenspeicher leeren; die fest eingetragenen Namensserver in den Adaptereinstellungen von Hand prüfen (das Programm setzt nur IPv4 auf einem DHCP-Anschluss zurück)."
                            : "DNS-Zwischenspeicher leeren; hilft das nicht, den Router neu starten.";
                    b.Messwert = Messwert.Von(art, "Fehlerart", "Antwort mit der erwarteten Adresse");
                    b.Detail.Add("SocketErrorCode: " + (art == "nxdomain" ? "HostNotFound (NXDOMAIN)" : "TimedOut/TryAgain oder Zeitgrenze der Sonde"));
                    foreach (var d in n.Dns)
                        b.Detail.Add("Namensserver Adapter " + d.AdapterIndex + " (" + FamilieText(d.Familie) + "): " + string.Join(", ", d.Server.Where(x => !IstDnsPlatzhalter(x)))
                                     + (d.Statisch == true ? " [fest eingetragen]" : d.Statisch == false ? " [automatisch]" : ""));
                    b.Massnahmen.Add(MDnsCacheLeeren);
                    if (statischDa)
                    {
                        b.Massnahmen.Add(MDnsZuruecksetzen);
                        b.Parameter[ParamAdapter] = statisch.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    e.Befunde.Add(b);
                    ursacheGefunden = true;
                }
                else if (art == "abweichend")
                {
                    // Aufgeloest, aber nicht auf die dokumentierte Adresse: ein anderer Server
                    // hat geantwortet (Anmeldeportal, Filter-DNS oder umgebogener Resolver).
                    // Das ist die erste versagende Stufe: eine Testseite mit fremdem Inhalt
                    // dahinter ist dasselbe Portal, kein zweiter Befund (nur Detail).
                    var b = Neu("netz.sonde.dns.abweichend", "Dns.GetHostAddresses(ActiveDnsProbeHost)");
                    b.Zustand = Zustand.Warn;
                    b.Titel = "Namensdienst antwortet anders als erwartet";
                    b.Satz = "Die Windows-Testadresse wird nicht auf die dokumentierte Adresse aufgelöst, sondern auf eine andere; ein Anmeldeportal oder ein Filter-DNS kann dazwischen sitzen.";
                    b.Rat = "Im Browser eine Seite öffnen: erscheint eine Anmeldeseite, dort anmelden; sonst die Namensserver prüfen.";
                    b.Messwert = Messwert.Von("abweichend", "DNS-Antwort", "erwartete Adresse");
                    if (so.Ncsi == true && so.NcsiInhaltStimmt == false)
                    {
                        b.Detail.Add("Testseite antwortet mit fremdem Inhalt (HTTP 200, ungleich ActiveWebProbeContent): zweiter Beleg für ein Portal; TCP 443: " + JaNein(so.Tcp443));
                        if (n.Proxy.WinInetAktiv == true) b.Detail.Add("WinINet-Proxy aktiv: " + n.Proxy.WinInetServer);
                    }
                    else b.Detail.Add("Testseite: " + JaNein(so.Ncsi) + " (Inhalt " + JaNein(so.NcsiInhaltStimmt) + "), TCP 443: " + JaNein(so.Tcp443));
                    b.Massnahmen.Add(MDnsCacheLeeren);
                    e.Befunde.Add(b);
                    ursacheGefunden = true;
                }
                else
                {
                    e.Fehlend.Add("Namensauflösung (Fehlerart „andere“)");
                }
            }

            // ---------------------------------------------------------- (5) Internet-Testseite
            // Gemessen "keine Testseite" zaehlt, wenn der Namensdienst antwortet ODER nur einen
            // unbekannten Fehler meldet (Fehlerart "andere", die keinen eigenen Befund hat).
            // Bei nxdomain/timeout/abweichend ist die Ursache schon oben benannt.
            bool dnsAndere = so.Dns == false && (so.DnsFehler ?? "andere") == "andere";
            if (!ursacheGefunden && so.Ncsi == true && so.NcsiInhaltStimmt == false)
            {
                var b = Neu("netz.ncsi.inhalt", "HTTP GET ActiveWebProbeHost/ActiveWebProbePath");
                b.Zustand = Zustand.Warn;
                b.Titel = "Anmeldeportal oder Proxy vor dem Internet";
                b.Satz = "Die Internet-Testseite antwortet, liefert aber nicht den erwarteten Inhalt; so verhalten sich Anmeldeseiten von Hotels, Cafés und Firmen oder ein Proxy.";
                b.Rat = "Im Browser eine beliebige Seite öffnen: erscheint eine Anmeldeseite, dort anmelden; sonst die Proxy-Einstellungen prüfen.";
                b.Messwert = Messwert.Von("abweichend", "Inhalt der Testseite", "„Microsoft Connect Test“");
                b.Detail.Add("HTTP 200, Inhalt ungleich ActiveWebProbeContent; TCP 443: " + JaNein(so.Tcp443));
                if (n.Proxy.WinInetAktiv == true) b.Detail.Add("WinINet-Proxy aktiv: " + n.Proxy.WinInetServer);
                e.Befunde.Add(b);
            }
            else if (!ursacheGefunden && !s.FehlerVon("api.netz.proxy").Any() && StackGestoert(so, n.Proxy) && physischUp.Count > 0)
            {
                // Router und Namensdienst antworten, Testseite (HTTP) und TCP 443 scheitern beide,
                // kein Proxy sitzt dazwischen: der Netzwerk-Stack selbst (Winsock-Katalog, IP-Stack)
                // ist die einzige Stufe, die noch uebrig ist. Beide Kennungen in der Reihenfolge, in
                // der sie zu versuchen sind; die Karte ersetzt netz.sonde.ncsi und netz.verbindung.
                // Ein Fehlereintrag der Proxy-Quelle (api.netz.proxy.*) sperrt den Zweig: dann ist
                // "kein Proxy" nicht belegt, und es bleibt bei netz.sonde.ncsi (warn), dessen Satz
                // nichts ueber den Proxy behauptet.
                var b = Neu("netz.stack.gestoert", "Sonden (Ping, DNS, HTTP, TCP 443), WinHttpGetIEProxyConfigForCurrentUser");
                b.Zustand = Zustand.Bad;
                b.Titel = "Netzwerk-Stack von Windows gestört";
                b.Satz = "Der Router antwortet und der Namensdienst antwortet, aber die Internet-Testseite (HTTP) und der verschlüsselte Zugriff (TCP 443) scheitern beide, obwohl kein Proxy eingerichtet ist; so verhält sich ein beschädigter Netzwerk-Stack (Winsock oder IP), nicht der Anschluss.";
                b.Rat = "Erst Winsock zurücksetzen (braucht einen Neustart); bleibt es dabei, den IP-Stack zurücksetzen.";
                b.Messwert = Messwert.Von(2, "von 4 Sonden bestanden", "4");
                b.Detail.Add("Sonden: Router ja, Namensdienst ja, Testseite nein, TCP 443 nein (" + so.DauerMs + " ms)");
                b.Detail.Add("Proxy: WinINet " + (n.Proxy.WinInetAktiv == true ? "aktiv" : n.Proxy.WinInetAktiv == false ? "aus" : "nicht gelesen") + ", WinHTTP-Typ " + (n.Proxy.WinHttpTyp.HasValue ? n.Proxy.WinHttpTyp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "nicht gelesen") + ", AutoConfigURL " + (string.IsNullOrEmpty(n.Proxy.PacUrl) ? "keine" : n.Proxy.PacUrl) + "; ein Proxy wäre die einfachere Erklärung, es gibt keinen");
                b.Detail.Add("Winsock-Reset entfernt Netzwerkkomponenten von Drittprogrammen (LSP); der IP-Reset setzt feste Adressen auf Automatisch, die gemerkten Werte stehen in der Sicherung.");
                b.Massnahmen.Add(MWinsockZuruecksetzen);
                b.Massnahmen.Add(MIpZuruecksetzen);
                e.Befunde.Add(b);
                ursacheGefunden = true;
            }
            else if (!ursacheGefunden && so.Ncsi == false && (so.Dns == true || dnsAndere))
            {
                var b = Neu("netz.sonde.ncsi", "HTTP GET ActiveWebProbeHost/ActiveWebProbePath");
                b.Zustand = Zustand.Warn;
                b.Titel = "Internet-Testseite nicht erreichbar";
                // Der Router ist hier nicht belegt (Sonde true oder null): nur nennen, was gemessen ist.
                b.Satz = so.Dns == true
                    ? "Der Namensdienst antwortet, aber die Internet-Testseite (HTTP) nicht; TCP 443: " + JaNein(so.Tcp443) + "."
                    : "Der Namensdienst meldet einen unbekannten Fehler, und die Internet-Testseite (HTTP) antwortet nicht; TCP 443: " + JaNein(so.Tcp443) + ".";
                b.Rat = "Proxy-Einstellungen sowie Firewall oder Virenscanner prüfen; in Firmennetzen den Administrator fragen.";
                b.Messwert = Messwert.Von("keine Antwort", "HTTP-Testseite", "HTTP 200");
                if (n.Proxy.WinInetAktiv == true) b.Detail.Add("WinINet-Proxy aktiv: " + n.Proxy.WinInetServer);
                if (n.Proxy.WinHttpTyp == 3) b.Detail.Add("WinHTTP-Proxy (Windows Update): " + n.Proxy.WinHttpServer);
                e.Befunde.Add(b);
            }

            // ---------------------------------------------------------- Zusammenfassung ok
            if (physischUp.Count > 0 && !e.Befunde.Any(b => b.IstProblem))
            {
                var a = physischUp.OrderByDescending(x => x.EmpfangBitS ?? 0).First();
                var b = Neu("netz.verbindung", "MSFT_NetAdapter, Sonden");
                b.Zustand = Zustand.Ok;
                b.Titel = "Verbunden";
                string tempo = a.EmpfangBitS.HasValue && a.EmpfangBitS.Value > 0 ? " mit " + Tempo(a.EmpfangBitS.Value) : "";
                b.Satz = Anzeige(a) + " ist" + tempo + " verbunden; " + SondenSatz(so);
                b.Messwert = Messwert.Von(a.EmpfangBitS, "Bit/s", null);
                foreach (var x in physischUp) b.Detail.Add(Anzeige(x) + ": " + (x.Beschreibung ?? "") + ", " + (x.EmpfangBitS.HasValue ? Tempo(x.EmpfangBitS.Value) : "Tempo unbekannt") + ", DHCP " + JaNein(x.Dhcp));
                if (n.Gateways.Count > 0) b.Detail.Add("Standardgateway: " + string.Join(", ", n.Gateways));
                else if (n.StandardrouteOnLink == true) b.Detail.Add("Standardroute: On-Link (Direktverbindung, kein Router-Eintrag)");
                foreach (var d in n.Dns.Where(d => physischUp.Any(x => x.Index == d.AdapterIndex)))
                    b.Detail.Add("Namensserver (" + FamilieText(d.Familie) + "): " + string.Join(", ", d.Server.Where(x => !IstDnsPlatzhalter(x))) + (d.Statisch == true ? " [fest]" : d.Statisch == false ? " [automatisch]" : ""));
                foreach (var p in n.Profile) b.Detail.Add("Profil " + (p.Name ?? "") + ": " + KategorieText(p.Kategorie) + ", IPv4-Konnektivität " + p.Ipv4 + ", IPv6 " + p.Ipv6);
                b.Detail.Add("Sonden: Router " + JaNein(so.Gateway) + ", DNS " + JaNein(so.Dns) + ", Testseite " + JaNein(so.Ncsi) + " (Inhalt " + JaNein(so.NcsiInhaltStimmt) + "), TCP 443 " + JaNein(so.Tcp443) + ", " + so.DauerMs + " ms");
                e.Befunde.Add(b);
            }

            // ---------------------------------------------------------- DNS fest auf DHCP-Anschluss (Info)
            foreach (var a in n.Adapter.Where(a => a.Dhcp == true))
            {
                var fest = n.Dns.Where(d => d.AdapterIndex == a.Index && d.Statisch == true)
                                .SelectMany(d => d.Server).Where(x => !IstDnsPlatzhalter(x)).Distinct().ToList();
                if (fest.Count == 0) continue;
                var b = Neu("netz.dns.statisch." + a.Index, "Registry Tcpip\\Parameters\\Interfaces\\{GUID}\\NameServer");
                b.Zustand = Zustand.Ok;
                b.Titel = "Namensserver fest eingetragen";
                b.Satz = "Auf " + Anzeige(a) + " sind die Namensserver " + string.Join(", ", fest) + " fest eingetragen, obwohl die Adresse automatisch kommt.";
                b.Rat = "Nichts zu tun, wenn Sie das selbst eingestellt haben (z. B. ein Werbefilter-DNS); sonst auf automatisch zurückstellen.";
                b.Messwert = Messwert.Von(string.Join(", ", fest), "NameServer", null);
                var vomRouter = n.Dns.Where(d => d.AdapterIndex == a.Index).SelectMany(d => d.DhcpServer).Distinct().ToList();
                if (vomRouter.Count > 0) b.Detail.Add("Vom Router (DHCP) vorgeschlagen: " + string.Join(", ", vomRouter));
                b.Detail.Add("Feste Server überschreiben die DHCP-Vorgabe; ein Zurücksetzen (Stufe 2) merkt sich die Liste als Rückweg.");
                e.Befunde.Add(b);
            }

            // ---------------------------------------------------------- Proxy
            if (n.Proxy.WinInetAktiv == true && !string.IsNullOrEmpty(n.Proxy.WinInetServer))
            {
                if (IstLokalerProxy(n.Proxy.WinInetServer))
                {
                    var b = Neu("netz.proxy.lokal", "WinHttpGetIEProxyConfigForCurrentUser.lpszProxy");
                    b.Zustand = Zustand.Warn;
                    b.Titel = "Proxy zeigt auf diesen PC";
                    b.Satz = "Der Browser-Proxy zeigt auf diesen PC selbst (" + n.Proxy.WinInetServer + "), nicht auf einen Server im Netz.";
                    b.Rat = "Wenn kein Werbeblocker, Virenscanner oder Entwicklungswerkzeug diesen Proxy eingerichtet hat, den Proxy in den Windows-Einstellungen abschalten.";
                    b.Messwert = Messwert.Von(n.Proxy.WinInetServer, "WinINet-Proxy", "kein lokaler Proxy");
                    b.Detail.Add("Unbekannter lokaler Proxy: Schutzsoftware nutzt das, Schadsoftware auch.");
                    // Gemessen: Get-NetTCPConnection liefert nur die Prozess-ID, den Namen erst Get-Process.
                    string port = LokalerProxyPort(n.Proxy.WinInetServer) ?? "<Port>";
                    b.Detail.Add("Welches Programm auf Port " + port + " lauscht: in PowerShell Get-NetTCPConnection -LocalPort " + port + " -State Listen liefert die Prozess-ID (Spalte OwningProcess), Get-Process -Id <PID> den Programmnamen.");
                    e.Befunde.Add(b);
                }
                else
                {
                    var b = Neu("netz.proxy.wininet", "WinHttpGetIEProxyConfigForCurrentUser.lpszProxy");
                    b.Zustand = Zustand.Ok;
                    b.Titel = "Proxy eingerichtet";
                    b.Satz = "Der Browser geht über den Proxy " + n.Proxy.WinInetServer + " ins Internet, wenn er eine Seite lädt.";
                    b.Messwert = Messwert.Von(n.Proxy.WinInetServer, "WinINet-Proxy", null);
                    e.Befunde.Add(b);
                }
            }
            if (!string.IsNullOrEmpty(n.Proxy.PacUrl))
            {
                // Konzept 4.4: unbekannte AutoConfigURL => warn. Ein PAC leitet den gesamten
                // Browser-Verkehr um; Schadsoftware nutzt genau das fuer Bank- und Update-
                // Seiten. Ok nur im Domaenennetz (Kategorie 2, dort setzt es der Administrator)
                // oder wenn der Nutzer "so lassen" geantwortet hat (Grundsatz 1: fragen, nicht
                // erfinden). Die Frage-ID traegt die URL: eine andere URL entwertet die Antwort.
                var b = Neu("netz.proxy.pac", "WinHttpGetIEProxyConfigForCurrentUser.lpszAutoConfigUrl");
                b.Messwert = Messwert.Von(n.Proxy.PacUrl, "AutoConfigURL", "keine AutoConfigURL");
                b.Detail.Add("Ein Proxy-Skript (PAC) ist in Firmennetzen normal; zu Hause ist es ein Hinweis auf eine fremde Einrichtung.");
                string frageId = "netz:proxy.pac:" + n.Proxy.PacUrl;
                if (n.Profile.Any(p => p.Kategorie == 2))
                {
                    b.Zustand = Zustand.Ok;
                    b.Titel = "Proxy-Regeln aus einem Skript (Firmennetz)";
                    b.Satz = "Der Browser lädt seine Proxy-Regeln von „" + n.Proxy.PacUrl + "“; in einem Domänennetz richtet das der Administrator ein, hier gibt es nichts zu tun.";
                    b.Detail.Add("Verbindungsprofil mit NetworkCategory 2 (Domäne) vorhanden.");
                }
                else if (ctx.E.IstAbsicht(frageId))
                {
                    b.Zustand = Zustand.Absicht;
                    b.Titel = "Proxy-Skript absichtlich eingerichtet";
                    b.Satz = "Der Browser lädt seine Proxy-Regeln von „" + n.Proxy.PacUrl + "“, so wie Sie es festgelegt haben; hier gibt es nichts zu tun.";
                    b.Detail.Add("Ihre Antwort auf die Frage " + frageId + ": so lassen.");
                }
                else if (ctx.E.AntwortAuf(frageId) == Entscheidungen.Reparieren)
                {
                    b.Zustand = Zustand.Bad;
                    b.Titel = "Unbekanntes Proxy-Skript";
                    b.Satz = "Der Browser lädt seine Proxy-Regeln von „" + n.Proxy.PacUrl + "“, und Sie haben festgelegt, dass Sie dieses Skript nicht eingerichtet haben.";
                    b.Rat = "In den Windows-Proxy-Einstellungen „Setupskript verwenden“ abschalten und die Adresse entfernen, danach einen Virenscan laufen lassen.";
                    b.Detail.Add("Ihre Antwort auf die Frage " + frageId + ": nicht eingerichtet.");
                }
                else
                {
                    b.Zustand = Zustand.Warn;
                    b.Titel = "Proxy-Regeln aus einem Skript";
                    b.Satz = "Der Browser lädt seine Proxy-Regeln von „" + n.Proxy.PacUrl + "“; wenn weder Sie noch Ihr Arbeitgeber das eingerichtet haben, lenkt ein fremdes Skript Ihren Internetverkehr um.";
                    b.Rat = "In den Windows-Proxy-Einstellungen „Setupskript verwenden“ prüfen; bei unbekannter Adresse abschalten und einen Virenscan laufen lassen.";
                    b.Frage = new Frage
                    {
                        Id = frageId,
                        JaHeisst = Entscheidungen.Absicht,
                        Text = "Der Browser lädt seine Proxy-Regeln von „" + n.Proxy.PacUrl + "“. Haben Sie oder Ihr Arbeitgeber dieses Skript eingerichtet?",
                        Ja = "Ja, so lassen",
                        Nein = "Nein, das kenne ich nicht",
                    };
                }
                e.Befunde.Add(b);
            }

            // ---------------------------------------------------------- hosts
            // Titel und Folge kommen aus den tatsaechlich getroffenen Klassen: eine umgeleitete
            // NCSI-Sonde (in Privatsphaere-Sperrlisten ueblich) blockiert keinen Update-Server,
            // sie laesst Windows faelschlich "Kein Internet" anzeigen.
            var treffer = n.Hosts.Where(h => HostsZeileUmleitung(h.Ip, h.Host)).ToList();
            if (treffer.Count > 0)
            {
                var klassen = treffer.Select(h => HostsKlasse(h.Host)).Distinct().ToList();
                bool update = klassen.Contains(HostsUpdate), schutz = klassen.Contains(HostsVirenschutz), ncsi = klassen.Contains(HostsNcsi);
                var dienste = new List<string>();
                if (update) dienste.Add("Windows Update");
                if (schutz) dienste.Add("den Virenschutz");
                if (ncsi) dienste.Add("die Internet-Erkennung");
                string folge = update && schutz ? "Windows Update und der Virenschutz erreichen so ihre Server nicht"
                             : update ? "Windows Update erreicht so seine Server nicht"
                             : schutz ? "der Virenschutz erreicht so seine Server nicht" : "";
                if (ncsi) folge += (folge.Length > 0 ? "; außerdem kann Windows" : "Windows kann") + " so nicht erkennen, ob Internet besteht, und zeigt „Kein Internet“, obwohl die Verbindung steht";

                var b = Neu("netz.hosts.umleitung", "hosts-Datei");
                b.Zustand = Zustand.Bad;
                b.Titel = "hosts-Datei leitet " + Aufzaehlung(dienste) + " um";
                b.Satz = treffer.Count == 1
                    ? "Die Adresse " + treffer[0].Host + " wird in der hosts-Datei auf " + treffer[0].Ip + " umgeleitet; " + folge + "."
                    : treffer.Count + " Einträge in der hosts-Datei leiten Adressen für " + Aufzaehlung(dienste) + " um (z. B. " + treffer[0].Host + " auf " + treffer[0].Ip + "); " + folge + ".";
                // Die Massnahme (netz.hosts.bereinigen, Stufe 2) entfernt genau die Zeilen, fuer die
                // HostsZeileUmleitung wahr ist, und merkt den alten Text als Rueckweg. Sie haengt nur
                // an der Karte, wenn die hosts-Datei nachweislich am Standardpfad liegt: bei abweichendem
                // DataBasePath lehnt der Helfer ab, und ein Knopf, der mit Exit 2 endet, ist keiner.
                // Ein nicht gelesener DataBasePath (Fehlerliste registry.netz.hostspfad) zaehlt nicht als
                // Standard: null hiesse dann "nicht gemessen", nicht "am Standardort" (fail closed).
                bool abweichend = !string.IsNullOrEmpty(n.HostsPfadAbweichend);
                bool standardpfad = !abweichend && hostsPfadGelesen;
                b.Rat = standardpfad
                    ? "Die Zeilen aus der hosts-Datei entfernen lassen (Beheben legt vorher eine Kopie des Inhalts in der Sicherung ab) und danach einen Virenscan laufen lassen."
                    : abweichend
                    ? "Die Zeilen von Hand aus der hosts-Datei unter „" + n.HostsPfadAbweichend + "“ entfernen (vorher eine Kopie sichern) und danach einen Virenscan laufen lassen; das Programm bereinigt nur die Datei am Standardpfad."
                    : "Die Zeilen von Hand aus der hosts-Datei entfernen (vorher eine Kopie sichern) und danach einen Virenscan laufen lassen; das Programm bereinigt nur, wenn der Ort der Datei (DataBasePath) lesbar ist und dem Standard entspricht.";
                b.Messwert = Messwert.Von(treffer.Count, "umgeleitete Einträge", "0");
                foreach (var h in treffer) b.Detail.Add(h.Ip + "  " + h.Host + "  [" + KlasseText(HostsKlasse(h.Host)) + "]");
                if (standardpfad)
                {
                    b.Detail.Add("Beheben entfernt genau die " + treffer.Count + " Zeilen oben und legt vorher eine Kopie des ganzen Inhalts in der Sicherung ab; Rückgängig schreibt sie zurück.");
                    b.Massnahmen.Add(MHostsBereinigen);
                }
                else if (abweichend) b.Detail.Add("DataBasePath weicht vom Standard ab (" + n.HostsPfadAbweichend + "): keine Maßnahme, der Helfer schreibt nur unter %SystemRoot%\\System32\\drivers\\etc.");
                else b.Detail.Add("Ort der hosts-Datei nicht prüfbar (DataBasePath nicht gelesen): keine Maßnahme, nur der Rat; der Helfer schreibt nur unter %SystemRoot%\\System32\\drivers\\etc.");
                e.Befunde.Add(b);
            }

            return e;
        }

        // ---------------------------------------------------------------- Helfer

        static Befund Neu(string schluessel, string quelle)
        {
            return new Befund { Bereich = Bereich.Netz, Schluessel = schluessel, Quelle = quelle };
        }

        /// <summary>"1 Sekunde" / "3 Sekunden" aus Millisekunden, mindestens 1 (nie "0 Sekunden").</summary>
        static string Sekunden(int ms)
        {
            int s = Math.Max(1, (int)Math.Round(ms / 1000.0));
            return s == 1 ? "1 Sekunde" : s + " Sekunden";
        }

        /// <summary>Adapter-Index als Planparameter: immer invariant, nie mit Tausenderzeichen.</summary>
        static string IndexText(Adapter a)
        {
            return a.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Index des Anschlusses, den netz.dns.zuruecksetzen zuruecksetzen kann: fest eingetragene
        /// IPv4-Namensserver (Familie 2, Statisch true, mindestens ein Server, der kein Platzhalter
        /// ist) auf einem verbundenen physischen Anschluss mit DHCP (Dhcp true). Ein nur unter IPv6
        /// fester Eintrag zaehlt nicht (die Massnahme setzt nur IPv4 zurueck, der Nachweis liest nur
        /// Familie 2), ein Anschluss mit fester Adresse nicht (der Helfer lehnt ihn ab), ein nicht
        /// verbundener nicht (sein Namensserver erklaert die versagende Sonde nicht). Der erste
        /// Treffer in der Reihenfolge des Bilds; null = kein Treffer.
        /// </summary>
        public static int? StatischerDnsAdapter(Kern.Netz n, List<Adapter> physischUp)
        {
            foreach (var d in n.Dns)
            {
                if (d.Familie != 2 || d.Statisch != true || !d.Server.Any(x => !IstDnsPlatzhalter(x))) continue;
                if (physischUp.Any(a => a.Index == d.AdapterIndex && a.Dhcp == true)) return d.AdapterIndex;
            }
            return null;
        }

        /// <summary>
        /// Ob ein Text (Signierer-Subject oder Anzeigename eines Dienstes) einen der bekannten
        /// Virenschutz-Hersteller nennt. Geprueft werden die Woerter des Texts (Buchstaben und
        /// Ziffern, alles andere trennt) einzeln und je zwei benachbarte zusammengezogen ("Trend
        /// Micro Inc." trifft trendmicro, "F-Secure" fsecure, "G DATA" gdata), mit derselben Regel
        /// wie fuer hosts-Labels. Reine Funktion; null oder leer ist kein Treffer.
        /// </summary>
        public static bool IstAvHersteller(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var woerter = new List<string>();
            var aktuell = new System.Text.StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) { aktuell.Append(c); continue; }
                if (aktuell.Length > 0) { woerter.Add(aktuell.ToString()); aktuell.Length = 0; }
            }
            if (aktuell.Length > 0) woerter.Add(aktuell.ToString());
            for (int i = 0; i < woerter.Count; i++)
            {
                foreach (string av in AvHersteller)
                {
                    if (LabelPasst(woerter[i], av)) return true;
                    if (i + 1 < woerter.Count && LabelPasst(woerter[i] + woerter[i + 1], av)) return true;
                }
            }
            return false;
        }

        public static bool IstApipa(string adresse)
        {
            return adresse != null && adresse.StartsWith("169.254.", StringComparison.Ordinal);
        }

        /// <summary>Die drei IPv6-Platzhalter, die Windows auf jeden Anschluss setzt (auch hier gefiltert, falls ein Bild sie traegt).</summary>
        public static bool IstDnsPlatzhalter(string server)
        {
            if (string.IsNullOrEmpty(server)) return true;
            string t = server.Trim().ToLowerInvariant();
            return t == "fec0:0:0:ffff::1" || t == "fec0:0:0:ffff::2" || t == "fec0:0:0:ffff::3";
        }

        /// <summary>"127.0.0.1:8080", "http=127.0.0.1:8080;https=localhost:8081", "[::1]:3128".</summary>
        public static bool IstLokalerProxy(string server)
        {
            if (string.IsNullOrEmpty(server)) return false;
            foreach (string teil in server.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = teil;
                int gleich = t.IndexOf('=');
                if (gleich >= 0) t = t.Substring(gleich + 1);
                int schema = t.IndexOf("://", StringComparison.Ordinal);
                if (schema >= 0) t = t.Substring(schema + 3);
                t = t.Trim().ToLowerInvariant();
                if (t.StartsWith("127.") || t.StartsWith("localhost") || t.StartsWith("[::1]") || t == "::1" || t.StartsWith("::1:")) return true;
            }
            return false;
        }

        /// <summary>Host gleich oder Unterdomain einer geschuetzten Domain, Microsoft-Update-/Download-Hosts, oder ein AV-Hersteller-Label.</summary>
        public static bool IstGeschuetzt(string host)
        {
            return HostsKlasse(host) != null;
        }

        /// <summary>
        /// Ob eine hosts-Zeile (IP, Hostname) eine Umleitung ist, die netz.hosts.umleitung zaehlt und
        /// netz.hosts.bereinigen entfernt (docs/M4-ENTWURF.md, Abschnitt 1 und 2.4): genau dann, wenn
        /// der Host eine Klasse hat (HostsKlasse ungleich null). Die Zieladresse entscheidet nicht:
        /// ein geschuetzter Host gehoert in keine hosts-Datei, gleich ob er auf 0.0.0.0, 127.0.0.1
        /// oder einen fremden Server zeigt. Der Helfer ruft diese Funktion, keine Kopie; eine Zeile
        /// ohne Host (Kommentar, Leerzeile) ist nie eine Umleitung. Reine Funktion.
        /// </summary>
        public static bool HostsZeileUmleitung(string ip, string host)
        {
            return !string.IsNullOrWhiteSpace(host) && HostsKlasse(host) != null;
        }

        /// <summary>
        /// Netzwerk-Stack gestoert (docs/M4-ENTWURF.md, Abschnitt 2.4): Router-Sonde wahr, DNS-Sonde
        /// wahr, Testseite (HTTP) falsch UND TCP 443 falsch, und ein GELESENER Proxy-Stand ohne Proxy
        /// (WinINet aktiv == false, WinHTTP-Typ gelesen und nicht 3, keine AutoConfigURL). Mit Proxy
        /// erklaert der Proxy die beiden versagenden Sonden, dann ist es netz.sonde.ncsi. Ein nicht
        /// gemessener Wert (null) zaehlt nie als Beleg: ohne Proxy-Objekt oder mit WinINet null bzw.
        /// WinHTTP-Typ null ist "kein Proxy" nicht belegt, und die Funktion liefert false (bis
        /// 18.09.2026 lieferte sie hier true und schloss "kein Proxy" aus dem Fehlen der Messung).
        /// Reine Funktion.
        /// </summary>
        public static bool StackGestoert(Sonden so, Proxy proxy)
        {
            if (so == null || proxy == null) return false;
            if (so.Gateway != true || so.Dns != true || so.Ncsi != false || so.Tcp443 != false) return false;
            return proxy.WinInetAktiv == false && proxy.WinHttpTyp.HasValue && proxy.WinHttpTyp.Value != 3 && string.IsNullOrEmpty(proxy.PacUrl);
        }

        /// <summary>
        /// Klasse eines hosts-Treffers: HostsUpdate, HostsVirenschutz, HostsNcsi; null = kein
        /// Treffer. Die Reihenfolge entscheidet bei Mehrdeutigkeit: erst die dokumentierten
        /// Listen, dann Microsoft-Hosts mit update/download (Update), dann AV-Hersteller.
        /// </summary>
        public static string HostsKlasse(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            string h = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (UpdateDomains.Any(d => DomainPasst(h, d))) return HostsUpdate;
            if (VirenschutzDomains.Any(d => DomainPasst(h, d))) return HostsVirenschutz;
            if (NcsiDomains.Any(d => DomainPasst(h, d))) return HostsNcsi;
            if ((h == "microsoft.com" || h.EndsWith(".microsoft.com", StringComparison.Ordinal))
                && (h.Contains("update") || h.Contains("download"))) return HostsUpdate;
            foreach (string label in h.Split('.'))
                foreach (string av in AvHersteller)
                    if (LabelPasst(label, av)) return HostsVirenschutz;
            return null;
        }

        static bool DomainPasst(string host, string domain)
        {
            return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
        }

        static string KlasseText(string klasse)
        {
            return klasse == HostsUpdate ? "Windows Update" : klasse == HostsVirenschutz ? "Virenschutz" : klasse == HostsNcsi ? "Internet-Erkennung (NCSI)" : "";
        }

        /// <summary>"a", "a und b", "a, b und c".</summary>
        static string Aufzaehlung(List<string> teile)
        {
            if (teile.Count <= 1) return teile.Count == 1 ? teile[0] : "";
            return string.Join(", ", teile.Take(teile.Count - 1)) + " und " + teile[teile.Count - 1];
        }

        /// <summary>Fehlend-Text mit Fehlerart der Quelle: "Standardgateway (zeit)", "Netzwerkkarten (keine Rechte)".</summary>
        static string MitArt(Systembild s, string quelle, string was)
        {
            var f = s.FehlerVon(quelle).FirstOrDefault();
            return was + (f == null ? "" : f.Art == Fehler.Zugriff ? " (keine Rechte)" : " (" + f.Art + ")");
        }

        /// <summary>
        /// Der Verbunden-Satz aus den einzelnen Sondenwerten: nur Stufen nennen, die wirklich
        /// true sind; null ist "nicht gemessen", false wird benannt (Router blockiert Ping,
        /// Namensdienst mit unbekanntem Fehler). Keine Stufe darf aus einer anderen erraten werden.
        /// </summary>
        static string SondenSatz(Sonden so)
        {
            var ja = new List<string>(); var offen = new List<string>(); var nein = new List<string>();
            (so.Gateway == true ? ja : so.Gateway == null ? offen : nein).Add("Router");
            (so.Dns == true ? ja : so.Dns == null ? offen : nein).Add("Namensdienst");
            bool testseite = so.Ncsi == true && so.NcsiInhaltStimmt == true;
            (testseite ? ja : so.Ncsi == null ? offen : nein).Add("Internet-Testseite");
            if (ja.Count == 3) return "alle 3 Stufen der Internet-Probe (Router, Namensdienst, Testseite) haben geantwortet.";
            if (offen.Count == 3) return "die Internet-Probe wurde nicht ausgeführt.";
            var teile = new List<string>();
            if (ja.Count > 0) teile.Add(Aufzaehlung(ja) + (ja.Count == 1 ? " hat" : " haben") + " geantwortet");
            if (nein.Contains("Router")) teile.Add("der Router antwortet nicht auf Ping");
            if (nein.Contains("Namensdienst")) teile.Add("der Namensdienst ist nicht bewertbar");
            if (nein.Contains("Internet-Testseite")) teile.Add(so.Ncsi == true ? "die Internet-Testseite antwortet mit fremdem Inhalt" : "die Internet-Testseite antwortet nicht");
            if (offen.Count > 0) teile.Add(Aufzaehlung(offen) + (offen.Count == 1 ? " wurde" : " wurden") + " nicht gemessen");
            return string.Join("; ", teile) + ".";
        }

        /// <summary>Erster Port eines lokalen Proxy-Eintrags ("http=127.0.0.1:8080;https=..." => "8080", "[::1]:3128" => "3128"); null, wenn keiner lesbar.</summary>
        public static string LokalerProxyPort(string server)
        {
            if (string.IsNullOrEmpty(server)) return null;
            foreach (string teil in server.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IstLokalerProxy(teil)) continue;
                string t = teil;
                int gleich = t.IndexOf('=');
                if (gleich >= 0) t = t.Substring(gleich + 1);
                int schema = t.IndexOf("://", StringComparison.Ordinal);
                if (schema >= 0) t = t.Substring(schema + 3);
                t = t.Trim().TrimEnd('/');
                // "[::1]:3128" => nach "]:"; "127.0.0.1:8080" => nach dem einzigen ":"; "::1" ohne Klammern hat keinen Port.
                int pos = t.StartsWith("[") ? t.IndexOf("]:", StringComparison.Ordinal) + 1
                        : t.Count(c => c == ':') == 1 ? t.IndexOf(':') : -1;
                if (pos <= 0 || pos >= t.Length - 1) continue;
                string port = t.Substring(pos + 1);
                if (port.All(char.IsDigit)) return port;
            }
            return null;
        }

        // "avast", "avast-update", "nortonlifelock" passen; "esetting" oder "avgold" nicht:
        // kurze Herstellernamen (unter fuenf Zeichen) muessen exakt oder mit "-"/Ziffer weitergehen.
        static bool LabelPasst(string label, string hersteller)
        {
            if (label == hersteller) return true;
            if (!label.StartsWith(hersteller, StringComparison.Ordinal)) return false;
            char c = label[hersteller.Length];
            return c == '-' || char.IsDigit(c) || hersteller.Length >= 5;
        }

        static string Anzeige(Adapter a)
        {
            string name = !string.IsNullOrEmpty(a.Name) ? a.Name : !string.IsNullOrEmpty(a.Beschreibung) ? a.Beschreibung : "Adapter " + a.Index;
            return "„" + name + "“";
        }

        static string Tempo(long bitS)
        {
            if (bitS >= 1000000000L) return (bitS / 1000000000.0).ToString("0.#", Text.De) + " Gbit/s";
            return (bitS / 1000000.0).ToString("0.#", Text.De) + " Mbit/s";
        }

        static string JaNein(bool? v) { return v == true ? "ja" : v == false ? "nein" : "nicht gemessen"; }

        static string OpStatusText(int s)
        {
            switch (s) { case 1: return "Up (1)"; case 2: return "Down (2)"; case 6: return "nicht vorhanden (6)"; default: return s.ToString(); }
        }

        static string MedienText(int m)
        {
            switch (m) { case 1: return "verbunden (1)"; case 2: return "getrennt (2)"; default: return "unbekannt (" + m + ")"; }
        }

        static string FamilieText(int f) { return f == 23 ? "IPv6" : f == 2 ? "IPv4" : "Familie " + f; }

        static string KategorieText(int k)
        {
            switch (k) { case 0: return "öffentlich (0)"; case 1: return "privat (1)"; case 2: return "Domäne (2)"; default: return "Kategorie " + k; }
        }
    }
}
