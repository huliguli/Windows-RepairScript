using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Die Tabelle des Maßnahmenkatalogs (docs/M3-ENTWURF.md Abschnitt 1, docs/M4-ENTWURF.md
    /// Abschnitt 1): je Kennung Stufe, Bereich, die Laientexte, Sicherung, Rückweg, Dauer,
    /// Pflichtparameter und der Nachweis als reine Funktion über dem NEUEN Systembild. Die
    /// Kennungen sind dieselben, die die Regeln an Befunde hängen (kern/Regeln/*.cs) und die der
    /// Helfer ausführt (helfer/Katalog.cs, helfer/Massnahmen/*.cs). Wer eine Kennung hier nicht
    /// findet, hat keine Texte für die Vorschau: die Probe MassnahmenProben (a) hält beide Seiten
    /// zusammen.
    ///
    /// Fünf Regeln für jeden Eintrag:
    ///
    /// 1. Jeder Satz trägt Zahl, Bedingung oder Absage; kein Geviert- oder Halbgeviertstrich,
    ///    Anführung nur „…“. Die Probe MassnahmenProben (b) misst das.
    /// 2. Der Nachweis liest nur das Bild und die Parameter, nie Windows. Wo das Bild den
    ///    Zustand nicht führt (DNS-Cache, Fragmentierung, Winsock), sagt der Satz das ehrlich, und
    ///    der Nachweis gilt nur dann als bestanden, wenn der Helfer keinen Fehler gemeldet hat: er
    ///    liest den optionalen Parameter „nachweis.bestanden“ (Werte-Schlüssel des Helfers,
    ///    helfer/Massnahmen/Gemeinsam.cs Nachweise); steht dort „0“ oder ein anderer Wert als „1“,
    ///    ist der Nachweis Nein und zitiert „nachweis.satz“ (EintragenOhneZustand). Fehlt der
    ///    Parameter, ist die Ausführung ohne Fehler das Einzige, was sich belegen lässt. Was nach
    ///    einem Neustart erst prüfbar ist, kommt als NachNeustart. Die M4-Nachweise mit messbarem
    ///    Zustand lesen den Schlüssel ebenfalls zuerst (EintragenHelferZuerst): ein Eingriff, den
    ///    der Helfer als gescheitert meldet, ist auch dann nicht bestanden, wenn das Bild zufällig
    ///    den Zielzustand zeigt.
    /// 3. Drei Ergebnisarten (M3-Entwurf 8.1): Ja, Nein und Unlesbar. Ein fehlender Parameter, ein
    ///    Objekt, das im neuen Bild nicht vorkommt, und ein Wert, der leer, „?“, „fehlt“ oder null
    ///    ist, liefern immer Nachweis.Unlesbar (nicht bestanden, Unbestimmt = true); der Satz nennt,
    ///    was fehlte. Nein heißt: das Objekt wurde gefunden, und sein Zustand ist nachweislich der
    ///    alte. Grundsatz: eine leere Antwort ist kein bestandener Lauf, aber auch kein Beleg.
    /// 4. Freischaltbar sind genau die sechs Stufe-1-Maßnahmen aus M3-Entwurf Abschnitt 8.1
    ///    (volume.optimieren hängt an keiner Regel und ist deshalb nicht freischaltbar); Stufe 2
    ///    läuft nie ohne Vorschau und Klick.
    /// 5. Stufe 3 (Massnahme.StufeUnumkehrbar, M4-Entwurf 2.1) trägt immer das Feld Verlust: ein
    ///    bis zwei Sätze, was konkret verloren geht; die Vorschau verlangt dafür das Häkchen, der
    ///    Host lehnt ohne Bestätigung ab. Eintragen wirft, wenn es fehlt. Die Sicherungsmarke
    ///    „export“ (Datei oder Ordner unter sicherungen\massnahmen\export) hat nie den Rückweg
    ///    „von selbst“.
    /// </summary>
    public static partial class Massnahmen
    {
        // Die Sicherung von registrierung.entfernen ist keine der drei Marken aus Massnahme
        // (keine, werte-merken, wiederherstellungspunkt): der Helfer schreibt eine .reg-Datei
        // (Konzept 3.3: "reg-export"). Die Oberfläche zeigt den Text, den Rückweg macht der Nutzer
        // per Doppelklick auf die Datei.
        public const string SicherungRegExport = "reg-export";

        // Aufgabennamen dieses Programms (src/Scheduler.cs TaskName / StartTaskName); der Kern darf
        // src\ nicht referenzieren, deshalb stehen sie hier noch einmal. Die Aufgabe im Bild trägt
        // den Pfad mit führendem Backslash ("\WindowsWartung-AutoWartung").
        const string AufgabeWartung = "WindowsWartung-AutoWartung";
        const string AufgabeSelbststart = "WindowsWartung-Autostart";

        // MSFT_Volume.OperationalStatus (wie kern/Regeln/Datentraeger.cs): 0xD00D Scan nötig,
        // 0xD00E Spot-Fix nötig, 0xD00F Reparatur nötig.
        const int VolumeScanNoetig = 0xD00D, VolumeSpotFixNoetig = 0xD00E, VolumeReparaturNoetig = 0xD00F;

        // Windows-Firewall-Profile (NET_FW_PROFILE_TYPE2): 1 Domaene, 2 privat, 4 oeffentlich; der
        // Nachweis von sicherheit.firewall.zuruecksetzen verlangt alle drei im Bild.
        static readonly int[] FirewallProfile = { 1, 2, 4 };

        static partial void Tabelle()
        {
            NetzTabelle();
            SicherheitTabelle();
            DatentraegerTabelle();
            LeistungTabelle();
            UpdatesTabelle();
            HardwareTabelle();
            SpeicherTabelle();
            StabilitaetTabelle();
            RueckwegTabelle();
            AnsichtUndRat();
            Meilenstein2();
        }

        // ================================================================== Netz

        static void NetzTabelle()
        {
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "netz.dnscache.leeren", Stufe = 1, Bereich = Kern.Bereich.Netz, Freischaltbar = true,
                Titel = "DNS-Cache leeren",
                WasPassiert = "Windows vergisst alle gemerkten Zuordnungen von Namen zu Adressen; die nächste Anfrage holt sie in unter 1 Sekunde neu vom Namensserver.",
                WasBleibt = "Es ändert sich keine Einstellung: Adapter, Namensserver und gespeicherte Netzwerke bleiben, wie sie sind.",
                Risiko = "Keines: der Zwischenspeicher füllt sich von selbst wieder, und nichts geht verloren.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 5,
            }, "Der DNS-Cache wurde geleert; ein Nachweis am Systembild ist nicht möglich, weil Windows den Inhalt des Zwischenspeichers nicht im Bild führt.");

            Eintragen(new Massnahme
            {
                Kennung = "netz.dhcp.erneuern", Stufe = 1, Bereich = Kern.Bereich.Netz,
                Titel = "Adresse neu vom Router anfordern",
                WasPassiert = "Der Anschluss fordert seine Adresse beim Router (DHCP) neu an; meist bleibt sie gleich, sonst ist die Verbindung unter 10 Sekunden weg.",
                WasBleibt = "Fest eingetragene Adressen bleiben unangetastet: die Maßnahme läuft nur auf einem Anschluss, der seine Adresse ohnehin automatisch bezieht.",
                Risiko = "Antwortet kein Router, bleibt die Notadresse 169.254.x.x wie vorher; schlechter als vorher wird es nicht.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 15,
                Parameter = new[] { "adapter" },
                Pruefen = DhcpErneuert,
            });

            Eintragen(new Massnahme
            {
                Kennung = "netz.dns.zuruecksetzen", Stufe = 2, Bereich = Kern.Bereich.Netz,
                Titel = "Namensserver auf automatisch zurücksetzen",
                WasPassiert = "Die fest eingetragenen Namensserver (DNS) des Anschlusses werden entfernt; Windows nimmt danach die Server, die der Router per DHCP nennt, und keine anderen.",
                WasBleibt = "Adresse, Router und alle anderen Anschlüsse werden nicht angefasst; nur die DNS-Liste dieses einen Anschlusses ändert sich.",
                Risiko = "Nennt der Router keinen Namensserver, löst der Anschluss keine Namen mehr auf; dann stellt „Rückgängig“ die alte Liste in unter 10 Sekunden wieder her.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 10,
                Parameter = new[] { "adapter" },
                Pruefen = DnsZurueckgesetzt,
            });

            // M4: ersetzt den Rat netz.hosts.pruefen. Der Helfer entfernt genau die Zeilen, fuer die
            // Regeln.Netz.HostsZeileUmleitung wahr ist (dieselbe Funktion zaehlt im Befund die Treffer),
            // und merkt vorher den ganzen alten Text; der Rueckweg schreibt ihn zurueck.
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "netz.hosts.bereinigen", Stufe = 2, Bereich = Kern.Bereich.Netz,
                Titel = "hosts-Datei bereinigen",
                WasPassiert = "Aus der hosts-Datei (System32\\drivers\\etc\\hosts) werden genau die Zeilen entfernt, die Adressen von Windows Update, dem Virenschutz oder der Internet-Erkennung umleiten; alle anderen Zeilen bleiben Wort für Wort stehen, und die Änderung wirkt ohne Neustart.",
                WasBleibt = "Eigene Einträge (Lizenzserver, Entwicklungsumgebung, Werbesperren ohne Bezug zu Windows) und Kommentare werden nicht angefasst; nur Zeilen mit einer bekannten Umleitung fallen weg.",
                Risiko = "Ein Schutzprogramm, das die Zeilen selbst gesetzt hat, schreibt sie womöglich wieder hinein; „Rückgängig“ schreibt den alten Inhalt der Datei aus der Sicherung in unter 5 Sekunden zurück.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
            }, HostsBereinigt);

            // Ohne messbaren Zustand (M4-Entwurf 1): das Bild fuehrt den Winsock-Katalog nicht, die
            // Aenderung gilt erst nach dem Neustart. Die Sicherung ist der alte Katalog als Text
            // (Beleg), ein Rueckweg gibt es nicht; der Helfer meldet rueckweg.moeglich = "0".
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "netz.winsock.zuruecksetzen", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Netz, Neustart = true,
                Titel = "Winsock zurücksetzen",
                WasPassiert = "Der Winsock-Katalog (die Liste der Netzwerkkomponenten, über die jedes Programm ins Netz geht) wird auf den Auslieferungszustand von Windows zurückgesetzt (netsh winsock reset); die Änderung gilt nach 1 Neustart.",
                WasBleibt = "Adressen, Namensserver, WLAN-Kennwörter und Firewall-Regeln bleiben unverändert; der alte Katalog wird vorher als Text in der Sicherung abgelegt, als Beleg, nicht als Rückweg.",
                Risiko = "Programme, die sich in den Katalog eingehängt hatten (VPN, Schutzprogramme, manche Spiele-Starter), verlieren ihre Netzwerkkomponente und laufen ohne Neuinstallation nicht mehr; bis zum Neustart ist das Netz womöglich gestört.",
                Verlust = "Zusätzliche Netzwerkkomponenten von Drittprogrammen (LSP, etwa von VPN- oder Schutzprogrammen) werden entfernt; diese Programme arbeiten danach nicht mehr, bis sie neu installiert sind.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 10,
            }, p => Nachweis.NachNeustart("Der Winsock-Katalog wurde zurückgesetzt; er gilt erst nach dem Neustart, und das Systembild führt ihn nicht, messbar ist erst danach, ob Testseite und TCP 443 wieder erreichbar sind."));

            // Ohne messbaren Zustand: die Sicherung merkt je Anschluss mit fester Adresse die Werte,
            // der Rueckweg (Helfer, EnableStatic/SetGateways/SetDNSServerSearchOrder) schreibt sie
            // zurueck; RueckwegGelungen liest dafuer nachweis.bestanden statt des Bilds.
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "netz.ip.zuruecksetzen", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Netz, Neustart = true,
                Titel = "IP-Stack zurücksetzen",
                WasPassiert = "Die TCP/IP-Einstellungen aller Anschlüsse werden auf den Auslieferungszustand zurückgesetzt (netsh int ip reset); feste Adressen, Gateways und Namensserver stehen danach auf Automatisch, und die Änderung gilt nach 1 Neustart.",
                WasBleibt = "WLAN-Kennwörter, Firewall-Regeln und der Winsock-Katalog werden nicht angefasst; je Anschluss mit fester Adresse werden Adresse, Maske, Gateway und Namensserver vorher in der Sicherung gemerkt.",
                Risiko = "Ein Anschluss mit fester Adresse (Server, Drucker-Netz, Heimnetz ohne DHCP) ist nach dem Neustart erst wieder erreichbar, wenn „Rückgängig“ die gemerkten Werte zurückschreibt oder der Router eine Adresse zuteilt.",
                Verlust = "Alle festen IP-Adressen, Gateways und DNS-Einträge werden auf Automatisch gesetzt; ohne die Sicherung wären sie weg, mit ihr schreibt „Rückgängig“ sie je Anschluss zurück.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 15,
            }, p => Nachweis.NachNeustart("Der IP-Stack wurde zurückgesetzt; die Einstellungen gelten erst nach dem Neustart, und das Systembild belegt bis dahin nicht, ob die Anschlüsse ihre Adressen wieder automatisch beziehen."));
        }

        /// <summary>
        /// Keine umleitende Zeile mehr (M4-Entwurf 1): dasselbe Prädikat wie im Befund
        /// netz.hosts.umleitung und im Helfer (Regeln.Netz.HostsZeileUmleitung, eine Zeichenmenge
        /// an einer Stelle). Ein abweichender DataBasePath macht den Nachweis unlesbar: Sammler und
        /// Helfer arbeiten nur am Standardort, die Datei, die Windows wirklich liest, ist eine andere.
        /// </summary>
        static Nachweis HostsBereinigt(Systembild bild, Dictionary<string, string> p)
        {
            if (!string.IsNullOrEmpty(bild.Netz.HostsPfadAbweichend))
                return Nachweis.Unlesbar("Die hosts-Datei liegt laut Registrierung (DataBasePath) unter „" + bild.Netz.HostsPfadAbweichend + "“, nicht am Standardort; Systembild und Maßnahme kennen nur den Standardort, ein Nachweis ist so nicht möglich.");
            Fehler fehler = bild.FehlerVon("datei.netz.hosts").FirstOrDefault();
            if (fehler != null)
                return Nachweis.Unlesbar("Die hosts-Datei ließ sich im neuen Systembild nicht lesen (" + fehler.Art + "); kein Nachweis.");
            var treffer = bild.Netz.Hosts.Where(h => h != null && Regeln.Netz.HostsZeileUmleitung(h.Ip, h.Host)).ToList();
            if (treffer.Count > 0)
                return Nachweis.Nein(treffer.Count + (treffer.Count == 1 ? " umleitende Zeile steht" : " umleitende Zeilen stehen") + " weiterhin in der hosts-Datei (z. B. " + treffer[0].Ip + " " + treffer[0].Host + "); die Bereinigung hat sie nicht entfernt.");
            return Nachweis.Ja("Die hosts-Datei enthält keine umleitende Zeile mehr; " + bild.Netz.Hosts.Count + " aktive " + (bild.Netz.Hosts.Count == 1 ? "Zeile bleibt" : "Zeilen bleiben") + " stehen, keine davon leitet Windows Update, den Virenschutz oder die Internet-Erkennung um.");
        }

        /// <summary>
        /// Keine Notadresse mehr UND ein Weg nach draußen: bei „netz.gateway.fehlt“ hat der Anschluss
        /// schon vor der Maßnahme eine gültige Adresse, nur keinen Router; ohne die zweite Bedingung
        /// wäre der Nachweis dort ohne Änderung bestanden (M3-Entwurf 8.1).
        /// </summary>
        static Nachweis DhcpErneuert(Systembild bild, Dictionary<string, string> p)
        {
            int index;
            if (!Ganz(p, "adapter", out index)) return Nachweis.Unlesbar("Der Parameter „adapter“ fehlt oder ist keine ganze Zahl; ohne ihn gibt es keinen Nachweis.");
            Adapter a = bild.Netz.Adapter.FirstOrDefault(x => x.Index == index);
            if (a == null) return Nachweis.Unlesbar("Anschluss " + index + " ist im neuen Systembild nicht vorhanden; ohne ihn gibt es keinen Nachweis.");
            string name = AdapterName(a);
            if (bild.Netz.IpAdressen.Count == 0) return Nachweis.Unlesbar("Das neue Systembild führt keine einzige IP-Adresse (Adressliste nicht gelesen); für " + name + " gibt es keinen Nachweis.");
            var v4 = bild.Netz.IpAdressen.Where(ip => ip.AdapterIndex == index && ip.Familie == 2).ToList();
            IpAdresse apipa = v4.FirstOrDefault(ip => Regeln.Netz.IstApipa(ip.Adresse));
            // Herkunft 3 DHCP, 1 manuell, 4 Router: alles, was nicht die Notadresse ist, versorgt den Anschluss.
            IpAdresse versorgt = v4.FirstOrDefault(ip => !Regeln.Netz.IstApipa(ip.Adresse) && (ip.Herkunft == 3 || ip.Herkunft == 1 || ip.Herkunft == 4));
            if (versorgt != null)
            {
                bool router = bild.Netz.Gateways.Count > 0 || bild.Netz.StandardrouteOnLink == true;
                if (!router)
                    return Nachweis.Nein(name + " hat eine Adresse (" + versorgt.Adresse + "), aber weiterhin keinen Router (Standardgateway); ohne ihn geht kein Paket nach draußen.");
                return Nachweis.Ja(name + " hat jetzt die Adresse " + versorgt.Adresse + (versorgt.Herkunft == 3 ? " vom Router (DHCP)" : " (fest eingetragen)") + " und einen Router (" + (bild.Netz.Gateways.Count > 0 ? Liste(bild.Netz.Gateways) : "Standardroute ohne Gateway") + "), keine Notadresse mehr.");
            }
            if (apipa != null)
                return Nachweis.Nein(name + " hat weiterhin nur die Notadresse " + apipa.Adresse + "; kein Router hat eine Adresse zugeteilt.");
            // Weder Notadresse noch versorgte Adresse: der Anschluss ist da, seine IPv4-Adressen
            // fehlen im Bild (getrennt oder nicht gelesen). Das belegt weder alt noch neu.
            return Nachweis.Unlesbar(name + " hat im neuen Systembild keine IPv4-Adresse (0 Einträge); ob der Router eine zugeteilt hat, lässt sich so nicht belegen.");
        }

        static Nachweis DnsZurueckgesetzt(Systembild bild, Dictionary<string, string> p)
        {
            int index;
            if (!Ganz(p, "adapter", out index)) return Nachweis.Unlesbar("Der Parameter „adapter“ fehlt oder ist keine ganze Zahl; ohne ihn gibt es keinen Nachweis.");
            Adapter a = bild.Netz.Adapter.FirstOrDefault(x => x.Index == index);
            if (a == null) return Nachweis.Unlesbar("Anschluss " + index + " ist im neuen Systembild nicht vorhanden; ohne ihn gibt es keinen Nachweis.");
            string name = AdapterName(a);
            var dns = bild.Netz.Dns.Where(d => d.AdapterIndex == index && d.Familie == 2).ToList();
            if (dns.Count == 0) return Nachweis.Unlesbar("Für " + name + " gibt es im neuen Systembild keine DNS-Angaben (0 Einträge); ohne sie gibt es keinen Nachweis.");
            DnsKonfig fest = dns.FirstOrDefault(d => d.Statisch == true);
            if (fest != null)
                return Nachweis.Nein(name + " hat weiterhin fest eingetragene Namensserver (" + Liste(fest.Server.Where(x => !Regeln.Netz.IstDnsPlatzhalter(x))) + ").");
            if (dns.All(d => d.Statisch == null))
                return Nachweis.Unlesbar("Ob " + name + " seine Namensserver fest oder automatisch bezieht, ließ sich im neuen Bild nicht lesen; kein Nachweis.");
            var server = dns.SelectMany(d => d.Server).Where(x => !Regeln.Netz.IstDnsPlatzhalter(x)).Distinct().ToList();
            return Nachweis.Ja(name + " bezieht seine Namensserver jetzt automatisch (" + server.Count + " Server" + (server.Count > 0 ? ": " + Liste(server) : ", noch keiner gemeldet") + ").");
        }

        // ================================================================== Sicherheit

        static void SicherheitTabelle()
        {
            Eintragen(new Massnahme
            {
                Kennung = "sicherheit.signaturen.aktualisieren", Stufe = 1, Bereich = Kern.Bereich.Sicherheit, Freischaltbar = true,
                Titel = "Virensignaturen aktualisieren",
                WasPassiert = "Windows Defender lädt die neuesten Virensignaturen von Microsoft; das dauert meist 1 bis 3 Minuten und braucht eine Internetverbindung.",
                WasBleibt = "Einstellungen, Ausschlüsse und der Echtzeitschutz bleiben unverändert; es wird nichts gescannt.",
                Risiko = "Keines: ohne Internetverbindung bleibt der alte Stand, und schlechter als vorher wird der Schutz nie.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 120,
                Pruefen = SignaturenAktuell,
            });

            Eintragen(new Massnahme
            {
                Kennung = "sicherheit.schnellscan", Stufe = 1, Bereich = Kern.Bereich.Sicherheit, Freischaltbar = true,
                Titel = "Schnellscan ausführen",
                WasPassiert = "Windows Defender prüft die Bereiche, in denen Schadsoftware am häufigsten sitzt (Autostart, Systemordner, laufende Programme); das dauert meist 5 bis 15 Minuten.",
                WasBleibt = "Ihre Dateien werden nur gelesen; gelöscht oder verschoben wird nichts, solange Defender keinen Fund meldet.",
                Risiko = "Während des Scans ist der PC spürbar langsamer; einen Fund behandelt Defender nach seinen eigenen Regeln (Quarantäne), nicht dieses Programm.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 600,
                Pruefen = SchnellscanHeute,
            });

            Eintragen(new Massnahme
            {
                Kennung = "sicherheit.firewall.einschalten", Stufe = 1, Bereich = Kern.Bereich.Sicherheit, Freischaltbar = true,
                Titel = "Firewall einschalten",
                WasPassiert = "Die Windows-Firewall wird für das betroffene Netzwerkprofil (Domäne, Privat oder Öffentlich) eingeschaltet; das wirkt sofort und dauert unter 5 Sekunden.",
                WasBleibt = "Alle Firewall-Regeln und die beiden anderen Profile bleiben, wie sie sind; es wird keine Regel angelegt oder gelöscht.",
                Risiko = "Ein Programm, das bisher nur wegen der ausgeschalteten Firewall erreichbar war, braucht danach eine Regel, ohne sie ist es von außen nicht mehr erreichbar; „Rückgängig“ schaltet das Profil wieder aus.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
                Parameter = new[] { "profil" },
                Pruefen = FirewallAn,
            });

            Eintragen(new Massnahme
            {
                Kennung = "sicherheit.smartscreen.warn", Stufe = 2, Bereich = Kern.Bereich.Sicherheit,
                Titel = "SmartScreen auf „Warnen“ stellen",
                WasPassiert = "Der Filter SmartScreen wird auf „Warnen“ gestellt: unbekannte Programme aus dem Internet zeigen vor dem Start einen Hinweis, blockiert wird nichts.",
                WasBleibt = "Der bisherige Wert wird gesichert; Browser-Einstellungen und Defender-Ausschlüsse werden nicht angefasst.",
                Risiko = "Vor dem Start unbekannter Programme erscheint 1 Hinweis mehr; wer ihn nicht will, geht über „Rückgängig“ zum alten Wert zurück.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
                Pruefen = (bild, p) =>
                {
                    string ss = bild.Sicherheit.SmartScreen;
                    if (string.IsNullOrEmpty(ss)) return Nachweis.Unlesbar("Der SmartScreen-Wert ließ sich im neuen Systembild nicht lesen; kein Nachweis.");
                    if (ss == "Off") return Nachweis.Nein("SmartScreen ist weiterhin aus (Off); der Wert wurde nicht übernommen.");
                    return Nachweis.Ja("SmartScreen steht jetzt auf „" + ss + "“; aus (Off) ist er nicht mehr.");
                },
            });

            Eintragen(new Massnahme
            {
                Kennung = "sicherheit.uac.standard", Stufe = 2, Bereich = Kern.Bereich.Sicherheit, Neustart = true,
                Titel = "Benutzerkontensteuerung wieder einschalten",
                WasPassiert = "Der Schalter EnableLUA wird auf 1 gesetzt, wenn er 0 ist, und die Nachfragestufe (ConsentPromptBehaviorAdmin) auf 5, wenn sie 0 ist; die Nachfragestufe wirkt sofort, der Schalter ab dem nächsten Neustart, dann fragt Windows wieder nach, bevor ein Programm volle Rechte bekommt.",
                WasBleibt = "Eine Nachfragestufe, die nicht 0 ist, bleibt, wie sie eingestellt ist; der abgedunkelte Bildschirm wird nicht angefasst, und ein Wert, der schon in Ordnung ist, wird nicht neu geschrieben.",
                Risiko = "Programme, die bisher ohne Nachfrage mit vollen Rechten liefen, zeigen danach 1 Abfrage je Start; „Rückgängig“ setzt beide Werte zurück (der Schalter wieder mit Neustart).",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
                Pruefen = UacStandard,
            });

            // M4: die Standardaktion eingehend eines Profils (COM HNetCfg.FwPolicy2 DefaultInboundAction)
            // zurueck auf Blockieren; die Sicherung merkt den alten Wert und das Profil.
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "sicherheit.firewall.eingehend.blockieren", Stufe = 2, Bereich = Kern.Bereich.Sicherheit,
                Titel = "Eingehende Verbindungen wieder blockieren",
                WasPassiert = "Die Standardaktion der Windows-Firewall für eingehende Verbindungen wird im betroffenen Profil (Domäne, Privat oder Öffentlich) auf „Blockieren“ gestellt; das wirkt sofort und dauert unter 5 Sekunden.",
                WasBleibt = "Alle Regeln bleiben, wie sie sind: ein Programm mit eigener Erlaubnis-Regel ist weiter erreichbar; nur Verbindungen ohne Regel werden wieder abgewiesen, wie es Windows ab Werk tut.",
                Risiko = "Ein Programm, das bisher nur über die offene Standardaktion erreichbar war (Fernwartung, Spiele-Server, Freigaben ohne Regel), braucht danach 1 eigene Regel; „Rückgängig“ stellt die alte Standardaktion wieder her.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
                Parameter = new[] { "profil" },
            }, FirewallEingehendBlockiert);

            // M4, Stufe 3 mit Export: der Helfer exportiert die Richtlinie als .wfw unter
            // sicherungen\massnahmen\export (Besitzer Administratoren, geschuetzte DACL), dann
            // netsh advfirewall reset; der Rueckweg importiert die Datei nach der Herkunftspruefung
            // am Handle. Bei registrierter Fremd-Firewall lehnt der Helfer ab (Exit 2), und die
            // Regel haengt die Kennung dann an keinen Befund.
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "sicherheit.firewall.zuruecksetzen", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Sicherheit,
                Titel = "Firewall auf Werkseinstellung zurücksetzen",
                WasPassiert = "Vorher exportiert das Programm die gesamte Firewall-Richtlinie als .wfw-Datei unter „sicherungen\\massnahmen\\export“; dann setzt es die Windows-Firewall auf die Werkseinstellung zurück (netsh advfirewall reset): alle 3 Profile an, eingehend blockieren, ausgehend zulassen, nur noch die Regeln von Windows.",
                WasBleibt = "Netzwerkprofile, Adressen und ein registriertes Fremdprodukt werden nicht angefasst; ist eine fremde Firewall registriert, lehnt das Programm die Maßnahme ab, weil das Zurücksetzen der Windows-Firewall daneben nichts bewirkt.",
                Risiko = "Jede selbst oder von einem Programm angelegte Regel ist danach weg, bis sie neu erlaubt oder die Sicherung zurückgespielt wird; ein Programm, das ohne seine Regel nicht arbeitet (Fernwartung, Spiele, Drucker im Netz), meldet sich beim nächsten Start neu.",
                Verlust = "Alle selbst angelegten Firewall-Regeln (Programme, Ports, Spiele, Fernwartung) sind danach weg und müssen neu erlaubt werden, wenn sie nicht über „Rückgängig“ aus der Sicherung zurückgespielt werden.",
                Sicherung = Massnahme.SicherungExport, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 20,
            }, FirewallWerkseinstellung);
        }

        /// <summary>DefaultInboundAction des Profils ist 0 (Block): Ja; 1 (Allow): Nein; null oder ein anderer Wert: Unlesbar (M4-Entwurf 1).</summary>
        static Nachweis FirewallEingehendBlockiert(Systembild bild, Dictionary<string, string> p)
        {
            int profil;
            if (!Ganz(p, "profil", out profil) || Array.IndexOf(FirewallProfile, profil) < 0)
                return Nachweis.Unlesbar("Der Parameter „profil“ fehlt oder ist nicht 1, 2 oder 4; ohne ihn gibt es keinen Nachweis.");
            FirewallProfil f = bild.Sicherheit.Firewall.FirstOrDefault(x => x.Profil == profil);
            string wer = "das Profil „" + ProfilName(profil) + "“";
            if (f == null) return Nachweis.Unlesbar("Das Profil „" + ProfilName(profil) + "“ fehlt im neuen Systembild; ohne es gibt es keinen Nachweis.");
            if (!f.Eingehend.HasValue) return Nachweis.Unlesbar("Die Standardaktion für eingehende Verbindungen ließ sich für " + wer + " im neuen Systembild nicht lesen; kein Nachweis.");
            if (f.Eingehend.Value == 0) return Nachweis.Ja("Eingehende Verbindungen ohne Regel werden für " + wer + " jetzt blockiert (DefaultInboundAction 0).");
            if (f.Eingehend.Value == 1) return Nachweis.Nein("Eingehende Verbindungen ohne Regel werden für " + wer + " weiterhin zugelassen (DefaultInboundAction 1); der Wert wurde nicht übernommen.");
            return Nachweis.Unlesbar("Die Standardaktion für eingehende Verbindungen meldet für " + wer + " den unbekannten Wert " + f.Eingehend.Value + " (weder 0 noch 1); kein Nachweis.");
        }

        /// <summary>
        /// Werkseinstellung heißt: alle drei Profile an, eingehend Block (0), ausgehend Allow (1).
        /// Ein Profil, das lesbar abweicht, ist Nein (nachweislich alt oder seither geändert); ein
        /// fehlendes Profil oder ein ungelesener Wert ohne lesbare Abweichung ist Unlesbar.
        /// </summary>
        static Nachweis FirewallWerkseinstellung(Systembild bild, Dictionary<string, string> p)
        {
            var profile = bild.Sicherheit.Firewall;
            if (profile.Count == 0) return Nachweis.Unlesbar("Das neue Systembild führt kein Firewall-Profil (0 von 3); kein Nachweis.");
            var fehlt = new List<string>();
            var unlesbar = new List<string>();
            var abweichend = new List<string>();
            foreach (int nr in FirewallProfile)
            {
                FirewallProfil f = profile.FirstOrDefault(x => x.Profil == nr);
                string name = "„" + ProfilName(nr) + "“";
                if (f == null) { fehlt.Add(name); continue; }
                var gruende = new List<string>();
                if (!f.An) gruende.Add("Firewall aus");
                if (f.Eingehend == 1) gruende.Add("eingehend „Zulassen“ (1) statt „Blockieren“ (0)");
                if (f.Ausgehend == 0) gruende.Add("ausgehend „Blockieren“ (0) statt „Zulassen“ (1)");
                if (gruende.Count > 0) { abweichend.Add(name + ": " + string.Join(", ", gruende)); continue; }
                if (!f.Eingehend.HasValue || f.Eingehend.Value != 0) unlesbar.Add(name + " eingehend " + (f.Eingehend.HasValue ? "Wert " + f.Eingehend.Value : "nicht gelesen"));
                if (!f.Ausgehend.HasValue || f.Ausgehend.Value != 1) unlesbar.Add(name + " ausgehend " + (f.Ausgehend.HasValue ? "Wert " + f.Ausgehend.Value : "nicht gelesen"));
            }
            if (abweichend.Count > 0)
                return Nachweis.Nein(abweichend.Count + " von 3 Profilen " + (abweichend.Count == 1 ? "weicht" : "weichen") + " weiterhin von der Werkseinstellung ab: " + string.Join("; ", abweichend) + ".");
            if (fehlt.Count > 0)
                return Nachweis.Unlesbar("Im neuen Systembild " + (fehlt.Count == 1 ? "fehlt das Profil " : "fehlen die Profile ") + string.Join(", ", fehlt) + " (" + (3 - fehlt.Count) + " von 3 gelesen); ohne alle 3 gibt es keinen Nachweis.");
            if (unlesbar.Count > 0)
                return Nachweis.Unlesbar("Die Standardaktionen ließen sich nicht vollständig lesen (" + string.Join(", ", unlesbar) + "); ohne beide Werte je Profil gibt es keinen Nachweis.");
            return Nachweis.Ja("Alle 3 Profile stehen auf Werkseinstellung: Firewall an, eingehend „Blockieren“ (0), ausgehend „Zulassen“ (1).");
        }

        /// <summary>
        /// Beide Werte gehören zur Maßnahme (M3-Entwurf 8.4): EnableLUA 1 und eine Nachfragestufe
        /// ungleich 0. Ein Wert, der im Bild null ist, ist nicht „unlesbar“, sondern „nicht gesetzt“
        /// (sammler/Quellen/Sicherheitsquelle.cs: der Sammler erfindet nichts, Windows nimmt dann den
        /// Standard EnableLUA 1 und Stufe 5; Regel und Helfer lesen ihn genauso). Unlesbar ist der
        /// Wert nur, wenn die Quelle registry.uac in der Fehlerliste steht (Schlüssel fehlt, Zugriff,
        /// Ausnahme) oder beide Werte fehlen (M3-Entwurf 8.1). Ein Wert, der nachweislich noch alt
        /// ist (0), ist Nein.
        /// </summary>
        static Nachweis UacStandard(Systembild bild, Dictionary<string, string> p)
        {
            int? lua = bild.Sicherheit.EnableLua, consent = bild.Sicherheit.ConsentAdmin;
            bool fehler = bild.FehlerVon("registry.uac").Any();
            if (!lua.HasValue && !consent.HasValue) return Nachweis.Unlesbar("EnableLUA und die Nachfragestufe (ConsentPromptBehaviorAdmin) ließen sich im neuen Systembild nicht lesen; kein Nachweis.");
            if (fehler && !lua.HasValue) return Nachweis.Unlesbar("EnableLUA ließ sich im neuen Systembild nicht lesen (" + bild.FehlerVon("registry.uac").First().Art + "); die Nachfragestufe " + consent.Value + " allein ist kein Nachweis.");
            if (fehler && !consent.HasValue) return Nachweis.Unlesbar("Die Nachfragestufe (ConsentPromptBehaviorAdmin) ließ sich im neuen Systembild nicht lesen (" + bild.FehlerVon("registry.uac").First().Art + "); EnableLUA " + lua.Value + " allein ist kein Nachweis.");
            // Nicht gesetzt heißt Windows-Standard: 1 und 5 (nur ohne Fehler der Quelle).
            string luaWort = lua.HasValue ? lua.Value.ToString(CultureInfo.InvariantCulture) : "1 (nicht gesetzt = Standard)";
            string consentWort = consent.HasValue ? consent.Value.ToString(CultureInfo.InvariantCulture) : "5 (nicht gesetzt = Standard)";
            if (!lua.HasValue) lua = 1;
            if (!consent.HasValue) consent = 5;
            if (lua.Value != 1) return Nachweis.Nein("EnableLUA steht weiterhin auf " + lua.Value + "; die Benutzerkontensteuerung ist nicht wieder an.");
            if (consent.Value == 0) return Nachweis.Nein("Die Nachfragestufe (ConsentPromptBehaviorAdmin) steht weiterhin auf 0; ohne Nachfrage bekommt jedes Programm volle Rechte.");
            return Nachweis.NachNeustart("EnableLUA steht auf " + luaWort + " und die Nachfragestufe auf " + consentWort + "; die Benutzerkontensteuerung ist nach dem nächsten Neustart wieder an.");
        }

        static Nachweis SignaturenAktuell(Systembild bild, Dictionary<string, string> p)
        {
            Defender d = bild.Sicherheit.Defender;
            if (d == null) return Nachweis.Unlesbar("Der Defender-Status fehlt im neuen Systembild; ohne ihn gibt es keinen Nachweis.");
            int t = d.SignaturAlterTage;
            if (t < 0) return Nachweis.Unlesbar("Das Alter der Virensignaturen ließ sich nicht lesen; kein Nachweis.");
            if (t == Regeln.Schwellen.NieWert) return Nachweis.Nein("Windows Defender hat weiterhin keine Virensignaturen geladen (nie); das Update hat nichts geliefert.");
            if (t <= Regeln.Schwellen.NachweisSignaturTage)
                return Nachweis.Ja("Die Virensignaturen sind jetzt " + TageWort(t) + " alt; bis " + TageWort(Regeln.Schwellen.NachweisSignaturTage) + " gilt das als aktuell.");
            return Nachweis.Nein("Die Virensignaturen sind weiterhin " + TageWort(t) + " alt; das Update hat keinen neueren Stand geladen.");
        }

        static Nachweis SchnellscanHeute(Systembild bild, Dictionary<string, string> p)
        {
            Defender d = bild.Sicherheit.Defender;
            if (d == null) return Nachweis.Unlesbar("Der Defender-Status fehlt im neuen Systembild; ohne ihn gibt es keinen Nachweis.");
            int t = d.SchnellscanAlterTage;
            if (t < 0) return Nachweis.Unlesbar("Das Alter des letzten Schnellscans ließ sich nicht lesen; kein Nachweis.");
            if (t == Regeln.Schwellen.NieWert) return Nachweis.Nein("Windows Defender hat weiterhin keinen Schnellscan verzeichnet (nie); der Scan wurde nicht gezählt.");
            if (t <= Regeln.Schwellen.NachweisSchnellscanTage)
            {
                int aktiv = d.Funde.Count(f => f.Aktiv);
                return Nachweis.Ja("Der letzte Schnellscan war heute (" + t + " Tage alt)" + (aktiv > 0 ? "; Defender meldet " + aktiv + " aktive Funde, die er selbst behandelt." : "; Defender meldet keinen aktiven Fund."));
            }
            return Nachweis.Nein("Der letzte Schnellscan ist weiterhin " + TageWort(t) + " alt; ein neuer wurde nicht verzeichnet.");
        }

        static Nachweis FirewallAn(Systembild bild, Dictionary<string, string> p)
        {
            int profil;
            if (!Ganz(p, "profil", out profil) || (profil != 1 && profil != 2 && profil != 4))
                return Nachweis.Unlesbar("Der Parameter „profil“ fehlt oder ist nicht 1, 2 oder 4; ohne ihn gibt es keinen Nachweis.");
            FirewallProfil f = bild.Sicherheit.Firewall.FirstOrDefault(x => x.Profil == profil);
            if (f == null) return Nachweis.Unlesbar("Das Profil „" + ProfilName(profil) + "“ fehlt im neuen Systembild; ohne es gibt es keinen Nachweis.");
            if (!f.An) return Nachweis.Nein("Die Firewall ist für das Profil „" + ProfilName(profil) + "“ weiterhin aus (FirewallEnabled 0).");
            return Nachweis.Ja("Die Firewall ist für das Profil „" + ProfilName(profil) + "“ jetzt an (FirewallEnabled 1).");
        }

        // ================================================================== Datenträger und Systemschutz

        /// <summary>
        /// Die eine Deutung des Registry-Werts Control\FileSystem\DisableDeleteNotification für
        /// Sammler, Helfer und Nachweis: das DWORD trägt seit ReFS v2 zwei Bits (Bit 0 NTFS, Bit 1
        /// ReFS), und „fsutil behavior set DisableDeleteNotify“ ohne Dateisystem setzt nur Bit 0.
        /// Deshalb heißt „TRIM an“ hier: Bit 0 ist 0; ein fehlender Wert ist die Voreinstellung 0.
        /// Der Rohwert 2 (nur das ReFS-Bit) ist damit „an“, und die Regel hängt an ihm keine
        /// Maßnahme an, die der Helfer sofort als „schon so“ abtun müsste. Wer den Wert liest
        /// (sammler/Quellen/DatentraegerQuelle.cs, Feld Trim) oder setzt (helfer/Massnahmen/
        /// Datentraeger.cs, NtfsBit), deutet ihn genau so; die Probe MassnahmenProben (c) misst es
        /// mit dem gepflanzten Rohwert 2.
        /// </summary>
        public static bool TrimAn(long? disableDeleteNotification)
        {
            return ((disableDeleteNotification ?? 0) & 1) == 0;
        }

        static void DatentraegerTabelle()
        {
            Eintragen(new Massnahme
            {
                Kennung = "datentraeger.trim.einschalten", Stufe = 2, Bereich = Kern.Bereich.Datentraeger,
                Titel = "TRIM einschalten",
                WasPassiert = "Windows meldet gelöschte Bereiche wieder an die SSD (NTFS-Bit von DisableDeleteNotification auf 0); die Einstellung gilt für alle NTFS-Laufwerke und wirkt ohne Neustart.",
                WasBleibt = "Daten, Partitionen und der Inhalt des Papierkorbs bleiben unberührt; TRIM löscht nichts, was nicht schon gelöscht ist.",
                Risiko = "Keines auf einer SSD; „Rückgängig“ setzt das NTFS-Bit auf den alten Wert zurück, falls ein Hersteller-Werkzeug TRIM absichtlich abgeschaltet hatte.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
                Pruefen = (bild, p) =>
                {
                    // Trim im Bild ist das NTFS-Bit (TrimAn), nicht der ganze Wert; der Name im Satz
                    // ist derselbe wie in WasPassiert und im Helfer („NTFS-Bit von
                    // DisableDeleteNotification“), damit Vorschau, Ablauf und Nachweis von einer
                    // Sache sprechen. Der Wert gilt systemweit, jeder Datenträger trägt ihn gleich.
                    if (bild.Datentraeger.Any(d => d.Trim == true)) return Nachweis.Ja("TRIM ist in Windows für NTFS jetzt an (NTFS-Bit von DisableDeleteNotification 0).");
                    if (bild.Datentraeger.Any(d => d.Trim == false)) return Nachweis.Nein("TRIM ist in Windows für NTFS weiterhin aus (NTFS-Bit von DisableDeleteNotification 1); der Wert wurde nicht übernommen.");
                    return Nachweis.Unlesbar("Ob TRIM an ist, ließ sich im neuen Systembild nicht lesen (" + bild.Datentraeger.Count + " Datenträger ohne Wert); kein Nachweis.");
                },
            });

            Eintragen(new Massnahme
            {
                Kennung = "volume.scan", Stufe = 1, Bereich = Kern.Bereich.Datentraeger, Freischaltbar = true,
                Titel = "Laufwerk prüfen (nur Scan)",
                WasPassiert = "Windows prüft das Dateisystem des Laufwerks im laufenden Betrieb und merkt gefundene Fehler nur vor; das dauert je nach Größe 1 bis 30 Minuten.",
                WasBleibt = "Es wird nichts repariert und nichts gelöscht; das Laufwerk bleibt die ganze Zeit benutzbar.",
                // Die Reparatur danach ist seit M4 eine eigene Massnahme der Stufe 3 (volume.spotfix,
                // volume.reparatur) mit Knopf, Verlust-Text und Haekchen; der Scan selbst startet sie nie.
                Risiko = "Keines: der Scan liest nur und ändert nichts; findet er Fehler, zeigt das Ergebnis danach einen Befund mit dem Knopf für die nötige Reparatur (Spot-Fix in Sekunden oder vollständige Reparatur, auf dem Windows-Laufwerk beim Neustart), und die läuft nie ohne Ihre Bestätigung.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 300,
                Parameter = new[] { "buchstabe" },
                Pruefen = VolumeSauber,
            });

            // M4, Stufe 3: MSFT_Volume.Repair(SpotFix) nimmt das Laufwerk Sekunden offline; auf dem
            // Windows-Laufwerk plant der Helfer stattdessen OfflineScanAndFix fuer den naechsten Start
            // und meldet neustart = "1".
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "volume.spotfix", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Datentraeger,
                Titel = "Dateisystem kurz reparieren (Spot-Fix)",
                WasPassiert = "Windows repariert die beim Scan vermerkten Fehler im Dateisystem des Laufwerks (chkdsk /spotfix) und nimmt es dafür für wenige Sekunden offline; ist es das Windows-Laufwerk, plant Windows die Reparatur stattdessen für den nächsten Start (bis 60 Minuten).",
                WasBleibt = "Dateien außerhalb der vermerkten Fehlerstellen werden nicht angefasst; ein Laufwerk ohne Vermerk wird nicht angerührt.",
                Risiko = "Geöffnete Dateien auf dem Laufwerk werden für die Sekunden offline geschlossen, ungespeicherte Änderungen darin sind weg; beschädigte Dateien kann die Reparatur verwerfen, ohne Datensicherung sind sie dann verloren, einen Rückweg gibt es nicht.",
                Verlust = "Dateien in beschädigten Bereichen können verworfen oder nach found.000 verschoben werden; ohne eigene Datensicherung sind sie dann verloren.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 60,
                Parameter = new[] { "buchstabe" },
            }, VolumeRepariert);

            // Neustart nur beim Windows-Laufwerk (M4-Entwurf 1: "ja (Windows-Laufwerk)"): ein anderes
            // festes Laufwerk repariert der Helfer sofort und offline. Die Marke Neustart gilt fuer
            // jedes Laufwerk, deshalb bleibt sie aus wie bei volume.spotfix; der bedingte Neustart
            // kommt aus dem Wert neustart = "1", den der Helfer nur beim Windows-Laufwerk schreibt
            // (helfer/Massnahmen/Datentraeger.cs OfflineVormerken) und VolumeRepariert liest.
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "volume.reparatur", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Datentraeger,
                Titel = "Dateisystem vollständig reparieren",
                WasPassiert = "Windows prüft und repariert das ganze Dateisystem des Laufwerks (chkdsk /f): das Windows-Laufwerk beim nächsten Start, ein anderes festes Laufwerk sofort und offline; das dauert je nach Größe 5 bis 60 Minuten.",
                WasBleibt = "Alle Daten außerhalb der beschädigten Bereiche bleiben erhalten; die Reparatur fasst keine gesunde Datei an.",
                Risiko = "Das Laufwerk ist während der Reparatur nicht benutzbar, beim Windows-Laufwerk der ganze PC; beschädigte Dateien kann die Reparatur verwerfen oder nach found.000 verschieben, ohne Datensicherung sind sie dann verloren, einen Rückweg gibt es nicht.",
                Verlust = "Dateien in beschädigten Bereichen können verworfen oder nach found.000 verschoben werden; ohne eigene Datensicherung sind sie dann verloren. Das Laufwerk ist während der Reparatur nicht benutzbar (5 bis 60 Minuten).",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 1800,
                Parameter = new[] { "buchstabe" },
            }, VolumeRepariert);

            // Nicht freischaltbar (M3-Entwurf 8.1): keine Regel hängt die Kennung an einen Befund, die
            // geplante Wartung führt nur vorgeschlagene Maßnahmen aus; ein Kästchen, das nie feuert,
            // wäre eine Lüge in den Einstellungen.
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "volume.optimieren", Stufe = 1, Bereich = Kern.Bereich.Datentraeger, Freischaltbar = false,
                Titel = "Laufwerk optimieren (TRIM)",
                WasPassiert = "Bei einer SSD meldet Windows alle freien Bereiche an das Laufwerk (ReTrim), bei einer Festplatte ordnet es die Dateien zusammenhängend an (Defragmentierung); das dauert 1 bis 45 Minuten.",
                WasBleibt = "Dateien und Einstellungen werden nicht verändert; der PC bleibt benutzbar, ist aber währenddessen langsamer.",
                Risiko = "Keines bei einer SSD; eine Festplatte wird währenddessen stark beansprucht, was bei einem Laufwerk mit Fehlern nicht ratsam ist (dann erst prüfen).",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 900,
                Parameter = new[] { "buchstabe" },
            }, p =>
            {
                string b = Buchstabe(p);
                return Nachweis.Ja((b != null ? "Laufwerk " + b + ": wurde" : "Das Laufwerk wurde") + " optimiert; ein Nachweis am Systembild ist nicht möglich, weil Windows den Grad der Fragmentierung nicht im Bild führt.");
            });

            Eintragen(new Massnahme
            {
                Kennung = "systemschutz.punkt.anlegen", Stufe = 1, Bereich = Kern.Bereich.Datentraeger, Freischaltbar = true,
                Titel = "Wiederherstellungspunkt anlegen",
                WasPassiert = "Windows sichert den Zustand der Systemdateien, Treiber und der Registrierung als Wiederherstellungspunkt; das dauert meist unter 1 Minute. Vorher hebt das Programm die 24-Stunden-Sperre von Windows dauerhaft auf (SystemRestorePointCreationFrequency 0), sonst entstünde nach einem Punkt vom selben Tag kein zweiter.",
                WasBleibt = "Eigene Dateien werden nicht gesichert und nicht angefasst; ältere Punkte bleiben, bis Windows den Platz braucht.",
                Risiko = "Keines: ist der Systemschutz für das Laufwerk aus, entsteht kein Punkt, und der Nachweis sagt das.",
                Sicherung = Massnahme.SicherungPunkt, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 60,
                Pruefen = PunktAngelegt,
            });
        }

        static Nachweis VolumeSauber(Systembild bild, Dictionary<string, string> p)
        {
            string b = Buchstabe(p);
            if (b == null) return Nachweis.Unlesbar("Der Parameter „buchstabe“ fehlt oder ist kein Laufwerksbuchstabe; ohne ihn gibt es keinen Nachweis.");
            Volume v = bild.Volumes.FirstOrDefault(x => string.Equals(x.Buchstabe, b, StringComparison.OrdinalIgnoreCase));
            string lw = "Laufwerk " + b + ":";
            if (v == null) return Nachweis.Unlesbar(lw + " ist im neuen Systembild nicht vorhanden; ohne es gibt es keinen Nachweis.");
            // Weder OperationalStatus noch Dirty-Bit gelesen: ein Satz „Status OK“ käme aus einem
            // nicht gemessenen Wert (die Regel wertet den leeren Status als „?“).
            if (v.OpStatus.Count == 0 && !v.Dirty.HasValue)
                return Nachweis.Unlesbar(lw + " meldet im neuen Systembild weder OperationalStatus noch Dirty-Bit; ohne beide gibt es keinen Nachweis.");
            if (v.OpStatus.Contains(VolumeReparaturNoetig) || v.OpStatus.Contains(VolumeSpotFixNoetig))
                return Nachweis.Nein(lw + " meldet nach dem Scan Fehler im Dateisystem (Status " + Hex(v.OpStatus.First(o => o == VolumeReparaturNoetig || o == VolumeSpotFixNoetig)) + "); eine Reparatur ist nötig, der Scan allein behebt sie nicht.");
            if (v.OpStatus.Contains(VolumeScanNoetig))
                return Nachweis.Nein(lw + " ist weiterhin zur Prüfung vorgemerkt (Status 0xD00D); der Scan hat den Vermerk nicht aufgelöst.");
            if (v.Dirty == true)
                return Nachweis.Nein(lw + " trägt weiterhin das Kennzeichen für eine ausstehende Prüfung (Dirty-Bit); der Scan hat es nicht gelöscht.");
            // Die Regel hängt volume.scan auch allein wegen Dirty == true an; ein „Status OK“ war dann
            // schon vorher wahr und belegt nichts, solange das Kennzeichen selbst nicht gelesen wurde
            // (in der erhöhten Nachmessung heißt null: FSCTL scheiterte, nicht „keine Rechte“).
            if (!v.Dirty.HasValue)
                return Nachweis.Unlesbar(lw + " meldet Status OK, aber das Dirty-Kennzeichen, das die Prüfung auslösen kann, ließ sich nicht lesen; kein Nachweis.");
            if (v.OpStatus.Count == 0)
                return Nachweis.Ja(lw + " trägt das Kennzeichen für eine ausstehende Prüfung nicht mehr (Dirty-Bit 0); der OperationalStatus wurde nicht gelesen.");
            return Nachweis.Ja(lw + " ist nicht mehr zur Prüfung vorgemerkt (Status OK, Kennzeichen nicht gesetzt).");
        }

        /// <summary>
        /// Reparatur gelaufen (M4-Entwurf 1, volume.spotfix und volume.reparatur): kein 0xD00E/0xD00F
        /// mehr und das Dirty-Bit nicht gesetzt ist Ja; einer der Vermerke steht noch ist Nein.
        /// Hat der Helfer die Reparatur für den nächsten Start eingetragen (neustart = „1“, Windows-
        /// Laufwerk), trägt das Bild die Vermerke bis dahin zu Recht: NachNeustart, nicht Nein.
        /// Die Regel (kern/Regeln/Datentraeger.cs) hängt beide Kennungen allein an den
        /// OperationalStatus 0xD00E/0xD00F, nie an das Dirty-Bit: ein leerer Status (MSFT_Volume im
        /// neuen Bild nicht gelesen) belegt deshalb nichts, auch wenn das Dirty-Bit 0 ist; das war
        /// schon vor der Maßnahme so. Der Vertragssatz „OpStatus ohne 0xD00E/0xD00F“ meint einen
        /// gelesenen Status (Regel 3 im Dateikopf).
        /// </summary>
        static Nachweis VolumeRepariert(Systembild bild, Dictionary<string, string> p)
        {
            string b = Buchstabe(p);
            if (b == null) return Nachweis.Unlesbar("Der Parameter „buchstabe“ fehlt oder ist kein Laufwerksbuchstabe; ohne ihn gibt es keinen Nachweis.");
            string lw = "Laufwerk " + b + ":";
            if (Wert(p, "neustart") == "1")
                return Nachweis.NachNeustart(lw + " wird beim nächsten Start repariert (Windows-Laufwerk, chkdsk vor dem Anmelden); bis dahin trägt es die Vermerke zu Recht, der Nachweis ist erst nach 1 Neustart möglich, vorher nicht.");
            Volume v = bild.Volumes.FirstOrDefault(x => string.Equals(x.Buchstabe, b, StringComparison.OrdinalIgnoreCase));
            if (v == null) return Nachweis.Unlesbar(lw + " ist im neuen Systembild nicht vorhanden; ohne es gibt es keinen Nachweis.");
            if (v.OpStatus.Count == 0 && !v.Dirty.HasValue)
                return Nachweis.Unlesbar(lw + " meldet im neuen Systembild weder OperationalStatus noch Dirty-Bit; ohne beide gibt es keinen Nachweis.");
            if (v.OpStatus.Contains(VolumeReparaturNoetig) || v.OpStatus.Contains(VolumeSpotFixNoetig))
                return Nachweis.Nein(lw + " meldet weiterhin Fehler im Dateisystem (Status " + Hex(v.OpStatus.First(o => o == VolumeReparaturNoetig || o == VolumeSpotFixNoetig)) + "); die Reparatur hat den Vermerk nicht aufgelöst.");
            if (v.Dirty == true)
                return Nachweis.Nein(lw + " trägt weiterhin das Kennzeichen für eine ausstehende Prüfung (Dirty-Bit); die Reparatur hat es nicht gelöscht.");
            if (v.OpStatus.Count == 0)
            {
                Fehler fehler = bild.FehlerVon("wmi.storage.volume").FirstOrDefault();
                return Nachweis.Unlesbar(lw + " meldet im neuen Systembild keinen OperationalStatus (MSFT_Volume nicht gelesen" + (fehler != null ? ", " + fehler.Art : "") + "); der Vermerk 0xD00E/0xD00F, der die Reparatur ausgelöst hat, lässt sich so nicht prüfen, kein Nachweis.");
            }
            if (v.OpStatus.Contains(VolumeScanNoetig))
                return Nachweis.Ja(lw + " meldet keine Reparatur mehr nötig (kein 0xD00E/0xD00F, Dirty-Bit nicht gesetzt); Windows möchte es noch einmal prüfen (Status 0xD00D), das ist ein Scan, keine Reparatur.");
            return Nachweis.Ja(lw + " meldet keine Reparatur mehr nötig (Status gelesen, kein 0xD00E/0xD00F" + (v.Dirty.HasValue ? ", Dirty-Bit 0" : ", Dirty-Bit nicht gelesen") + ").");
        }

        /// <summary>
        /// Folgenummer gestiegen: mit Parameter „vorher“ oder „sicherung.vorher“ (höchste Nummer vor
        /// der Maßnahme, der Helfer liefert den zweiten Namen) über die Nummern, sonst über die Zeit
        /// (ein Punkt, der höchstens NachweisPunktMinuten vor der Nachmessung liegt). Ist der
        /// Systemschutz aus, legt Windows still keinen Punkt an: genau das wird hier als „nicht
        /// bestanden“ sichtbar; die 24-Stunden-Drossel hebt der Helfer vorher auf.
        /// </summary>
        static Nachweis PunktAngelegt(Systembild bild, Dictionary<string, string> p)
        {
            List<Punkt> punkte = bild.Systemschutz.Punkte;
            if (punkte == null) return Nachweis.Unlesbar("Die Wiederherstellungspunkte ließen sich im neuen Systembild nicht lesen; ohne sie gibt es keinen Nachweis.");
            if (punkte.Count == 0) return Nachweis.Nein("Es gibt weiterhin keinen Wiederherstellungspunkt (0 Punkte); Windows hat keinen angelegt.");
            int vorher;
            if (Ganz(p, "vorher", out vorher) || Ganz(p, "sicherung.vorher", out vorher))
            {
                int max = punkte.Max(x => x.Nummer);
                if (max > vorher) return Nachweis.Ja("Ein neuer Wiederherstellungspunkt ist da: Folgenummer " + max + " (vorher " + vorher + ").");
                return Nachweis.Nein("Die höchste Folgenummer ist weiterhin " + max + "; Windows hat keinen neuen Punkt angelegt.");
            }
            Punkt juengster = punkte.OrderByDescending(x => x.ZeitUtc).First();
            double? tage = Zeit.TageZwischen(juengster.ZeitUtc, bild.AufgezeichnetUtc);
            if (!tage.HasValue) return Nachweis.Unlesbar("Die Zeit des jüngsten Wiederherstellungspunkts (Nr. " + juengster.Nummer + ") ist nicht lesbar; kein Nachweis.");
            double minuten = tage.Value * 24 * 60;
            if (minuten >= 0 && minuten <= Regeln.Schwellen.NachweisPunktMinuten)
                return Nachweis.Ja("Ein neuer Wiederherstellungspunkt ist da: Nr. " + juengster.Nummer + ", angelegt " + Minuten(minuten) + " vor der Nachmessung.");
            return Nachweis.Nein("Der jüngste Wiederherstellungspunkt (Nr. " + juengster.Nummer + ") ist älter als " + Regeln.Schwellen.NachweisPunktMinuten + " Minuten; Windows hat keinen neuen angelegt.");
        }

        // ================================================================== Leistung

        static void LeistungTabelle()
        {
            Eintragen(new Massnahme
            {
                Kennung = "leistung.autostart.deaktivieren", Stufe = 2, Bereich = Kern.Bereich.Leistung,
                Titel = "Startprogramm deaktivieren",
                WasPassiert = "Der Eintrag wird im Task-Manager-Status auf „deaktiviert“ gesetzt, genau wie über den Task-Manager; das Programm startet ab der nächsten Anmeldung nicht mehr von selbst.",
                WasBleibt = "Das Programm bleibt installiert und lässt sich weiter von Hand starten; der Eintrag selbst wird nicht gelöscht.",
                Risiko = "Ein Programm, das im Hintergrund gebraucht wird (Cloud-Speicher, Maus-Software), startet nicht mehr mit; „Rückgängig“ schaltet den Eintrag in unter 5 Sekunden wieder ein.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
                Parameter = new[] { "hive", "ort", "name" },
                Pruefen = AutostartDeaktiviert,
            });

            Eintragen(new Massnahme
            {
                Kennung = "leistung.aufgabe.deaktivieren", Stufe = 2, Bereich = Kern.Bereich.Leistung,
                Titel = "Geplante Aufgabe deaktivieren",
                WasPassiert = "Die Aufgabe wird in der Aufgabenplanung deaktiviert (schtasks /Change /DISABLE); sie läuft ab sofort nicht mehr zu ihren Zeiten oder bei der Anmeldung. Eine Instanz, die gerade läuft, wird nicht beendet; sie endet von selbst und startet nicht neu.",
                WasBleibt = "Die Aufgabe bleibt mit allen Einstellungen erhalten und wird nicht gelöscht; das zugehörige Programm bleibt installiert.",
                Risiko = "Eine Aufgabe, die Updates oder Sicherungen anstößt, tut das danach nicht mehr; „Rückgängig“ aktiviert sie wieder (/ENABLE).",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 10,
                Parameter = new[] { "pfad" },
                Pruefen = AufgabeDeaktiviert,
            });

            Eintragen(new Massnahme
            {
                Kennung = "leistung.dienst.startart", Stufe = 2, Bereich = Kern.Bereich.Leistung,
                Titel = "Startart eines Dienstes ändern",
                // Der Zielwert steht im Parameter „startart“ (die Vorschau zeigt ihn in der Zeile
                // „Fachlich“ als startart=Manual); die Regel (kern/Regeln/Leistung.cs, StartartManuell)
                // übergibt nie einen anderen Wert als Manual, Disabled erreicht den Helfer nur über die
                // Pipe. Das Risiko beschreibt deshalb die Folge von Manuell UND Deaktiviert: beide
                // heißen „startet beim Hochfahren nicht mehr von selbst“, nicht nur Deaktiviert.
                WasPassiert = "Die Startart des Dienstes wird auf den Wert gestellt, der unten in der Zeile „Fachlich“ hinter „startart=“ steht; aus der Prüfung heraus ist das immer „Manuell“ (Manual): der Dienst startet dann nicht mehr von selbst beim Hochfahren, sondern nur noch, wenn ein Programm ihn anfordert. Ein laufender Dienst wird nicht beendet und läuft bis zum nächsten Neustart weiter.",
                WasBleibt = "Der Dienst wird weder gestartet noch beendet, und keine Datei wird gelöscht; nur die Startart ändert sich.",
                Risiko = "Auf „Manuell“ oder „Deaktiviert“ gestellt, läuft der Dienst nach dem nächsten Neustart nicht mehr von selbst; ein Programm, das ihn braucht und ihn nicht selbst startet (Audio-, Grafik-, Stift- oder Bluetooth-Software), arbeitet dann nicht, bis „Rückgängig“ die alte Startart in unter 10 Sekunden wiederherstellt.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 10,
                Parameter = new[] { "name", "startart" },
                Pruefen = DienstStartart,
            });

            // M4: war Rat der Stufe 3, jetzt Stufe 2 mit Sicherung. Der Helfer schreibt nur den Wert,
            // der nicht dem Standard entspricht (Regeln.Leistung.WinlogonShellStandard /
            // WinlogonUserinitStandard, dieselben Praedikate wie in der Regel und hier im Nachweis).
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "leistung.winlogon.zuruecksetzen", Stufe = 2, Bereich = Kern.Bereich.Leistung,
                Titel = "Anmeldeprogramm zurücksetzen",
                WasPassiert = "Die 2 Winlogon-Werte werden auf den Windows-Standard gesetzt: Shell auf „explorer.exe“ und Userinit auf „<Windows>\\System32\\userinit.exe,“; ein Wert, der schon dem Standard entspricht, wird nicht geschrieben, und die Änderung wirkt bei der nächsten Anmeldung.",
                WasBleibt = "Alle anderen Einträge der Registrierung, Autostarts und Programme bleiben unverändert; betroffen sind nur die 2 Werte Shell und Userinit.",
                Risiko = "Ein Programm, das sich in Winlogon eingehängt hatte (Kiosk-Oberfläche, Schadsoftware), startet bei der Anmeldung nicht mehr; „Rückgängig“ schreibt die alten Werte aus der Sicherung zurück, sinnvoll ist das nur nach einem Virenscan ohne Fund.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 5,
            }, WinlogonStandard);
        }

        /// <summary>
        /// Beide Winlogon-Werte im Standard (M4-Entwurf 1). Fehlen beide im Bild, ist das nicht
        /// „Standard“, sondern nicht gelesen (die Regel hängt die Kennung nie an zwei null-Werte,
        /// nach der Maßnahme steht mindestens ein Wert ausdrücklich da): Unlesbar.
        /// </summary>
        static Nachweis WinlogonStandard(Systembild bild, Dictionary<string, string> p)
        {
            string shell = bild.Autostart.Shell, userinit = bild.Autostart.Userinit;
            if (shell == null && userinit == null)
            {
                Fehler fehler = bild.FehlerVon("registry.winlogon").FirstOrDefault();
                return Nachweis.Unlesbar("Die Winlogon-Werte Shell und Userinit fehlen beide im neuen Systembild" + (fehler != null ? " (" + fehler.Art + ")" : " (nicht gelesen oder nicht gesetzt)") + "; kein Nachweis.");
            }
            bool s = Regeln.Leistung.WinlogonShellStandard(shell), u = Regeln.Leistung.WinlogonUserinitStandard(userinit);
            string shellWort = shell == null ? "nicht gesetzt (Standard explorer.exe)" : "„" + shell + "“";
            string userinitWort = userinit == null ? "nicht gesetzt (Standard userinit.exe)" : "„" + userinit + "“";
            if (s && u) return Nachweis.Ja("Shell steht auf " + shellWort + " und Userinit auf " + userinitWort + "; beide Werte entsprechen dem Windows-Standard, kein fremder Teil startet mehr bei der Anmeldung.");
            if (!s && !u) return Nachweis.Nein("Shell (" + shellWort + ") und Userinit (" + userinitWort + ") weichen weiterhin vom Windows-Standard ab; die Werte wurden nicht übernommen.");
            if (!s) return Nachweis.Nein("Shell steht weiterhin auf " + shellWort + " statt „explorer.exe“; der Wert wurde nicht übernommen.");
            return Nachweis.Nein("Userinit steht weiterhin auf " + userinitWort + " statt „<Windows>\\System32\\userinit.exe,“; der Wert wurde nicht übernommen.");
        }

        static Nachweis AutostartDeaktiviert(Systembild bild, Dictionary<string, string> p)
        {
            string quelle = AutostartQuelle(Wert(p, "hive"), Wert(p, "ort"));
            string name = Wert(p, "name");
            if (quelle == null) return Nachweis.Unlesbar("Die Parameter „hive“ und „ort“ passen zu keinem Autostart-Ort (HKLM/HKCU mit Run, Run32 oder StartupFolder); kein Nachweis.");
            if (string.IsNullOrEmpty(name)) return Nachweis.Unlesbar("Der Parameter „name“ fehlt; ohne ihn gibt es keinen Nachweis.");
            AutostartEintrag e = bild.Autostart.Eintraege.FirstOrDefault(x =>
                string.Equals(x.Quelle, quelle, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            string wer = "Der Eintrag „" + name + "“ (" + quelle + ")";
            if (e == null) return Nachweis.Unlesbar(wer + " ist im neuen Systembild nicht zu finden; ob er deaktiviert ist, lässt sich so nicht belegen.");
            if (e.Aktiviert == false) return Nachweis.Ja(wer + " ist jetzt deaktiviert und startet nicht mehr mit Windows.");
            if (e.Aktiviert == true) return Nachweis.Nein(wer + " ist weiterhin aktiv; der Task-Manager-Status wurde nicht übernommen.");
            // null hat drei Ursachen: die Quelle StartupApproved scheiterte (dann steht sie in der
            // Fehlerliste, und nichts ist nachweislich), Byte 0 traegt einen ungedeuteten Wert, oder
            // es gibt keinen Wert. Nur die letzten beiden sind ein Nein: Windows zaehlt den Eintrag
            // dann als aktiv, und der Helfer haette Byte 3 angelegt (Gegenlesen 18.09.2026, wie
            // UacStandard und UpdateVerborgen ihre Quelle pruefen).
            var fehler = bild.FehlerVon("registry.autostart.startupapproved").FirstOrDefault();
            if (fehler != null)
                return Nachweis.Unlesbar(wer + ": der Task-Manager-Status (StartupApproved) ließ sich im neuen Systembild nicht lesen (" + fehler.Art + "); kein Nachweis.");
            return Nachweis.Nein(wer + " hat keinen deutbaren Task-Manager-Status (Byte 0 nicht 2 oder 3); ohne ihn gilt er als aktiv, der Schalter wurde nicht geschrieben.");
        }

        // TASK_STATE (taskschd.h): 0 unbekannt, 1 deaktiviert, 2 in der Warteschlange, 3 bereit, 4 läuft.
        const int AufgabeUnbekannt = 0, AufgabeDeaktiviertZustand = 1, AufgabeWartet = 2, AufgabeBereit = 3, AufgabeLaeuft = 4;

        /// <summary>
        /// Der Schalter ist IRegisteredTask.Enabled (Feld Aktiv im Bild), nicht TASK_STATE: laut
        /// Microsoft-Doku heißt Zustand 1 „disabled and no instances of the task are queued or
        /// running“ und Zustand 4 „one or more instances of the task is running“. Eine mit
        /// schtasks /DISABLE deaktivierte Aufgabe, deren Instanz noch läuft, meldet also 4, nicht 1;
        /// genau das sind die Anmelde-Aufgaben, die ein residentes Programm starten (auf der
        /// Aufzeichnung dieses PCs 4 von 12 fremden Logon-/Boot-Aufgaben). Der Helfer prüft Enabled
        /// (helfer/Massnahmen/Leistung.cs, AufgabeStand.Aktiv); der Kern tut es hier genauso, sonst
        /// widersprechen sich Helfer und Host im selben Lauf. Ohne das Feld (Bild einer älteren
        /// Fassung, Aktiv null) bleibt nur der Zustand: 1 ist Ja, 3 ist Nein, 2 und 4 sind Unlesbar
        /// (die Instanz wartet oder läuft, ob der Schalter umgelegt ist, steht nicht im Bild), 0 ist
        /// Unlesbar.
        /// </summary>
        static Nachweis AufgabeDeaktiviert(Systembild bild, Dictionary<string, string> p)
        {
            string pfad = Wert(p, "pfad");
            if (string.IsNullOrEmpty(pfad)) return Nachweis.Unlesbar("Der Parameter „pfad“ fehlt; ohne ihn gibt es keinen Nachweis.");
            Aufgabe a = AufgabeFinden(bild, pfad);
            string wer = "Die Aufgabe „" + pfad + "“";
            if (a == null)
                return Nachweis.Unlesbar(wer + " ist im neuen Systembild nicht zu finden" + (bild.Autostart.AufgabenVollstaendig ? "" : "; die Aufgabenliste war nicht vollständig lesbar") + "; kein Nachweis.");
            if (a.Aktiv == false)
            {
                if (a.Zustand == AufgabeLaeuft || a.Zustand == AufgabeWartet)
                    return Nachweis.Ja(wer + " ist jetzt deaktiviert (Enabled = false); die Instanz, die noch läuft oder wartet (Zustand " + a.Zustand + "), endet von selbst und startet nicht neu.");
                return Nachweis.Ja(wer + " ist jetzt deaktiviert (Enabled = false, Zustand " + a.Zustand + "); sie läuft bei keiner Anmeldung und zu keiner Zeit mehr.");
            }
            if (a.Aktiv == true)
                return Nachweis.Nein(wer + " ist weiterhin aktiv (Enabled = true, Zustand " + a.Zustand + "); der Schalter wurde nicht übernommen.");
            // Aktiv null: das Bild führt Enabled nicht (ältere Fassung des Sammlers), nur der Zustand bleibt.
            if (a.Zustand == AufgabeDeaktiviertZustand) return Nachweis.Ja(wer + " ist jetzt deaktiviert (Zustand 1); das Feld Enabled führt dieses Bild nicht.");
            if (a.Zustand == AufgabeUnbekannt) return Nachweis.Unlesbar(wer + " meldet den Zustand 0 (unbekannt); ob sie deaktiviert ist, lässt sich so nicht belegen.");
            if (a.Zustand == AufgabeLaeuft || a.Zustand == AufgabeWartet)
                return Nachweis.Unlesbar(wer + " hat den Zustand " + a.Zustand + " (" + (a.Zustand == AufgabeLaeuft ? "läuft noch" : "wartet in der Warteschlange") + "), und dieses Bild führt Enabled nicht; eine deaktivierte Aufgabe mit laufender Instanz meldet denselben Zustand, das belegt weder alt noch neu.");
            return Nachweis.Nein(wer + " hat weiterhin den Zustand " + a.Zustand + (a.Zustand == AufgabeBereit ? " (bereit)" : "") + "; deaktiviert wäre 1.");
        }

        static Nachweis DienstStartart(Systembild bild, Dictionary<string, string> p)
        {
            string name = Wert(p, "name");
            string ziel = StartartMarke(Wert(p, "startart"));
            if (string.IsNullOrEmpty(name)) return Nachweis.Unlesbar("Der Parameter „name“ fehlt; ohne ihn gibt es keinen Nachweis.");
            if (ziel == null) return Nachweis.Unlesbar("Der Parameter „startart“ ist nicht Automatic, Manual oder Disabled; kein Nachweis.");
            Dienst d = bild.Autostart.Dienste.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (d == null) return Nachweis.Unlesbar("Der Dienst „" + name + "“ ist im neuen Systembild nicht zu finden; kein Nachweis.");
            if (StartartUnlesbar(d.Startart)) return Nachweis.Unlesbar("Die Startart des Dienstes „" + name + "“ ließ sich im neuen Systembild nicht lesen; kein Nachweis.");
            string ist = StartartMarke(d.Startart) ?? d.Startart;
            if (string.Equals(ist, ziel, StringComparison.Ordinal))
                return Nachweis.Ja("Der Dienst „" + name + "“ hat jetzt die Startart „" + StartartWort(ziel) + "“; die Änderung gilt ohne Neustart.");
            return Nachweis.Nein("Der Dienst „" + name + "“ hat weiterhin die Startart „" + StartartWort(ist) + "“ statt „" + StartartWort(ziel) + "“; die Änderung wurde nicht übernommen.");
        }

        // ================================================================== Updates

        static void UpdatesTabelle()
        {
            // Stufe 2 ohne Rückweg (M3-Entwurf 8.10), und zwar bei jedem Vorwert: ob der Dienst
            // vorher Disabled, Manual oder Automatic/Stopped war, ein Update-Dienst wird nie wieder
            // angehalten oder in der Startart zurückgestellt. Der Helfer meldet für diese Kennung
            // rueckweg.moeglich = "0" ohne Bedingung, und sein Rückweg lehnt eine Sicherung dieser
            // Kennung ab, egal welche Startart darin steht (helfer/Massnahmen/Updates.cs,
            // Rueckweg.cs InhaltPruefen). Die Sicherung bleibt werte-merken: die Datei ist der Beleg,
            // was vorher galt, kein Rückfahrschein. Der Kern sperrt nichts: RueckwegGelungen bleibt
            // für die Hin-Kennung auswertbar und meldet, was das Bild zeigt (Probe NachweiseRueckweg).
            // Ein Riegel im Kern würde genau dann „nicht zurückgestellt“ melden, wenn der Helfer den
            // Dienst entgegen seiner Ablehnung doch angehalten hat, und den Eingriff verdecken.
            Eintragen(new Massnahme
            {
                Kennung = "update.dienst.aktivieren", Stufe = 2, Bereich = Kern.Bereich.Updates,
                Titel = "Update-Dienst wieder einschalten",
                WasPassiert = "Der Dienst wird auf die Startart „Automatisch“ gestellt und sofort gestartet; das dauert unter 15 Sekunden.",
                WasBleibt = "Andere Dienste, die Update-Einstellungen und der Update-Verlauf bleiben unverändert; es wird kein Update installiert.",
                Risiko = "Ein „Update-Blocker“-Programm, das den Dienst abgeschaltet hatte, schaltet ihn womöglich erneut ab; ohne dessen Deinstallation hält die Änderung nicht. „Rückgängig“ gibt es hier nicht: Windows-Update-Dienste werden nie wieder deaktiviert, wer das will, tut es in der Dienstverwaltung von Hand.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 15,
                Parameter = new[] { "name" },
                Pruefen = UpdateDienstLaeuft,
            });

            Eintragen(new Massnahme
            {
                Kennung = "update.verbergen", Stufe = 2, Bereich = Kern.Bereich.Updates,
                Titel = "Update verbergen",
                WasPassiert = "Das Update wird in Windows Update als „verborgen“ markiert; Windows bietet es nicht mehr an und versucht nicht mehr, es zu installieren.",
                WasBleibt = "Alle anderen Updates kommen weiter; bereits installierte Fassungen des Updates werden nicht entfernt.",
                Risiko = "Ein verborgenes Sicherheitsupdate fehlt, bis es wieder sichtbar gemacht wird; „Rückgängig“ zeigt es wieder an, und ein Update-Reset löscht die Markierung ohnehin.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 30,
                Parameter = new[] { "updateId" },
                Pruefen = UpdateVerborgen,
            });

            // M4, Stufe 3: Reihenfolge und Abbruchregeln stehen in docs/M4-ENTWURF.md 1 und 3.5. Die
            // Sicherung (UpdateID|Revision|Titel je Zeile) ist der Beleg, kein Rueckweg: die
            // Ausblendungen setzt die Massnahme selbst wieder, der Helfer meldet rueckweg.moeglich = "0".
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "update.zuruecksetzen", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Updates, Neustart = true,
                Titel = "Windows Update zurücksetzen",
                WasPassiert = "Vorher merkt das Programm alle verborgenen Updates in der Sicherung; dann hält es die 3 Dienste wuauserv, bits und cryptsvc an, benennt die Ordner SoftwareDistribution und catroot2 in „.old“ um, startet die Dienste wieder und verbirgt die gemerkten Updates erneut; das dauert 1 bis 5 Minuten, danach ist 1 Neustart empfohlen.",
                WasBleibt = "Installierte Updates, Programme und eigene Dateien werden nicht angefasst; die Ordner „.old“ bleiben liegen, bis Windows sie selbst aufräumt.",
                Risiko = "Lässt sich ein Dienst nicht anhalten, bricht das Programm vor dem Umbenennen ab und ändert nichts; scheitert das Umbenennen, benennt es zurück und startet die Dienste wieder. Ein „Rückgängig“ gibt es nicht, die Sicherung ist der Beleg.",
                Verlust = "Der Update-Verlauf in den Windows-Einstellungen ist danach leer (0 Einträge); laufende Downloads beginnen von vorn. Verborgene Updates werden vorher gesichert und danach wieder verborgen, keines geht verloren.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 300,
            }, UpdateZurueckgesetzt);
        }

        /// <summary>
        /// Jede GUID aus „update.verborgen.vorher“ (Werte-Schlüssel des Helfers, „;“-Liste) steht
        /// wieder in der Liste der verborgenen Updates: NachNeustart (der Neustart ist immer
        /// empfohlen). Fehlt eine, ist es Nein und der Satz nennt sie. Ohne die Liste des Helfers
        /// gibt es den Nachweis nur mit nachweis.bestanden = „1“ und leerer Bild-Liste („es war kein
        /// Update verborgen“); alles andere ist nicht messbar. Die Bild-Liste kommt aus einer eigenen
        /// COM-Suche: steht sie in der Fehlerliste, belegt eine leere Liste nichts.
        /// </summary>
        static Nachweis UpdateZurueckgesetzt(Systembild bild, Dictionary<string, string> p)
        {
            var guids = (Wert(p, "update.verborgen.vorher") ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var liste = bild.WindowsUpdate.Verborgen;
            Fehler fehler = bild.FehlerVon("com.windowsupdate.suche.verborgen").FirstOrDefault();
            if (guids.Count == 0)
            {
                if (Wert(p, HelferBestanden) != "1")
                    return Nachweis.Unlesbar("Der Helfer hat weder die Liste der vorher verborgenen Updates (update.verborgen.vorher) noch ein Ergebnis (nachweis.bestanden) gemeldet; ohne beides gibt es keinen Nachweis.");
                if (fehler != null)
                    return Nachweis.Unlesbar("Die Liste der verborgenen Updates ließ sich im neuen Systembild nicht lesen (" + fehler.Art + "); kein Nachweis.");
                if (liste.Count == 0)
                    return Nachweis.NachNeustart("Es war kein Update verborgen (0 gesichert), und nach dem Zurücksetzen ist keines verborgen (0 Einträge); der Update-Speicher ist neu aufgebaut, der empfohlene Neustart steht noch aus.");
                return Nachweis.Unlesbar("Die Sicherung nennt kein vorher verborgenes Update, das neue Systembild führt aber " + liste.Count + "; ob sie zur Sicherung passen, lässt sich so nicht belegen.");
            }
            if (fehler != null)
                return Nachweis.Unlesbar("Die Liste der verborgenen Updates ließ sich im neuen Systembild nicht lesen (" + fehler.Art + "); ob die " + guids.Count + " gesicherten Updates wieder verborgen sind, lässt sich so nicht belegen.");
            var fehlende = guids.Where(g => !liste.Any(u => string.Equals(u.UpdateId, g, StringComparison.OrdinalIgnoreCase))).ToList();
            if (fehlende.Count == 0)
                return Nachweis.NachNeustart("Alle " + guids.Count + " von " + guids.Count + " vorher verborgenen Updates sind wieder verborgen; der Update-Speicher ist neu aufgebaut, der empfohlene Neustart steht noch aus.");
            return Nachweis.Nein((guids.Count - fehlende.Count) + " von " + guids.Count + " vorher verborgenen Updates sind wieder verborgen; es " + (fehlende.Count == 1 ? "fehlt" : "fehlen") + ": " + Liste(fehlende) + ". Windows bietet sie wieder an, bis „Update verbergen“ sie erneut verbirgt.");
        }

        static Nachweis UpdateVerborgen(Systembild bild, Dictionary<string, string> p)
        {
            string id = Wert(p, "updateId");
            if (string.IsNullOrEmpty(id)) return Nachweis.Unlesbar("Der Parameter „updateId“ fehlt; ohne ihn gibt es keinen Nachweis.");
            UpdateKennung u = bild.WindowsUpdate.Verborgen.FirstOrDefault(x => string.Equals(x.UpdateId, id, StringComparison.OrdinalIgnoreCase));
            if (u != null) return Nachweis.Ja("Das Update " + (string.IsNullOrEmpty(u.Titel) ? "mit der Kennung " + id : "„" + u.Titel + "“") + " ist jetzt verborgen.");
            // Die Liste der verborgenen Updates kommt aus einer eigenen COM-Suche; ist sie im Bild als
            // Fehler vermerkt, ist "steht nicht in der Liste" keine Aussage ueber das Update.
            if (bild.FehlerVon("com.windowsupdate.suche.verborgen").Any())
                return Nachweis.Unlesbar("Die Liste der verborgenen Updates ließ sich im neuen Systembild nicht lesen (" + bild.FehlerVon("com.windowsupdate.suche.verborgen").First().Art + "); für die Kennung " + id + " gibt es keinen Nachweis.");
            return Nachweis.Nein("Das Update mit der Kennung " + id + " steht nicht in der Liste der verborgenen Updates (" + bild.WindowsUpdate.Verborgen.Count + " Einträge).");
        }

        /// <summary>
        /// Streng (M3-Entwurf 8.1): nur Startart Automatisch UND laufend ist bestanden, weil der
        /// Helfer genau das setzt (ChangeStartMode Automatic + StartService). Manuell/laufend ist
        /// Nein; eine Startart „fehlt“, „?“ oder leer (Sammler: Dienst nicht gefunden oder WMI
        /// ohne Wert) ist Unlesbar.
        /// </summary>
        static Nachweis UpdateDienstLaeuft(Systembild bild, Dictionary<string, string> p)
        {
            string name = Wert(p, "name");
            if (string.IsNullOrEmpty(name)) return Nachweis.Unlesbar("Der Parameter „name“ fehlt; ohne ihn gibt es keinen Nachweis.");
            string start = null, zustand = null;
            bool gefunden = false;
            // Erste Quelle: die Dienstetabelle von Windows Update ("Auto/Running"); zweite: die Diensteliste.
            foreach (var kv in bild.WindowsUpdate.Dienste)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kv.Value))
                {
                    string[] teile = kv.Value.Split('/');
                    start = teile[0].Trim();
                    zustand = teile.Length > 1 ? teile[1].Trim() : null;
                    gefunden = true;
                    break;
                }
            if (!gefunden)
            {
                Dienst d = bild.Autostart.Dienste.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (d != null) { start = d.Startart; zustand = d.Zustand; gefunden = true; }
            }
            if (!gefunden) return Nachweis.Unlesbar("Der Dienst „" + name + "“ fehlt im neuen Systembild; ohne ihn gibt es keinen Nachweis.");
            if (StartartUnlesbar(start)) return Nachweis.Unlesbar("Die Startart des Dienstes „" + name + "“ ließ sich im neuen Systembild nicht lesen (" + (start ?? "leer") + "); kein Nachweis.");
            string marke = StartartMarke(start) ?? start;
            if (marke == "Disabled") return Nachweis.Nein("Der Dienst „" + name + "“ ist weiterhin deaktiviert (" + start + "/" + (zustand ?? "?") + "); die Änderung wurde nicht übernommen.");
            if (marke != "Auto") return Nachweis.Nein("Der Dienst „" + name + "“ hat die Startart „" + StartartWort(marke) + "“ statt „Automatisch“" + (string.Equals(zustand, "Running", StringComparison.OrdinalIgnoreCase) ? ", läuft aber" : "") + "; die Änderung wurde nicht übernommen.");
            if (StartartUnlesbar(zustand)) return Nachweis.Unlesbar("Der Dienst „" + name + "“ hat die Startart „Automatisch“, sein Zustand ließ sich aber nicht lesen; kein Nachweis.");
            if (!string.Equals(zustand, "Running", StringComparison.OrdinalIgnoreCase))
                return Nachweis.Nein("Der Dienst „" + name + "“ hat die Startart „Automatisch“, läuft aber nicht (" + zustand + ").");
            return Nachweis.Ja("Der Dienst „" + name + "“ läuft und startet automatisch (Auto/Running); deaktiviert ist er nicht mehr.");
        }

        // ================================================================== Hardware

        static void HardwareTabelle()
        {
            Eintragen(new Massnahme
            {
                Kennung = "geraet.aktivieren", Stufe = 2, Bereich = Kern.Bereich.Hardware,
                Titel = "Gerät wieder einschalten",
                WasPassiert = "Das Gerät wird wie im Geräte-Manager eingeschaltet (Win32_PnPEntity.Enable); meist ist es in unter 15 Sekunden wieder da, manche Geräte verlangen 1 Neustart.",
                WasBleibt = "Treiber und Einstellungen des Geräts bleiben, wie sie sind; andere Geräte werden nicht angefasst.",
                Risiko = "Wurde das Gerät abgeschaltet, weil es Abstürze verursachte, kommen die zurück; „Rückgängig“ schaltet es in unter 15 Sekunden wieder aus.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 15,
                Parameter = new[] { "instanzId" },
                Pruefen = GeraetAn,
            });

            // M4, Stufe 3 mit Export: war Rat. Der Helfer exportiert einen Hersteller-Treiber
            // (oemNN.inf) vorher nach sicherungen\massnahmen\export, entfernt das Geraet
            // (pnputil /remove-device) und laesst Windows neu suchen (/scan-devices). Der Export ist
            // Beleg und Handwerkszeug, kein Rueckweg: pnputil /add-driver <inf> /install von Hand.
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "geraet.treiber.neu", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Hardware,
                Titel = "Treiber neu installieren",
                WasPassiert = "Das Gerät wird aus dem System entfernt (pnputil /remove-device) und Windows sucht es sofort neu (pnputil /scan-devices), sodass der Treiber frisch eingerichtet wird; ist der Treiber vom Hersteller (oemNN.inf), exportiert das Programm ihn vorher in 1 Ordner unter „sicherungen\\massnahmen\\export“.",
                WasBleibt = "Andere Geräte und ihre Treiber werden nicht angefasst; der Treiber selbst bleibt im Treiberspeicher von Windows, er wird nur neu an das Gerät gebunden.",
                Risiko = "Verlangt Windows danach einen Neustart (3010), ist das Gerät bis dahin weg; findet Windows keinen passenden Treiber, bleibt es ohne, dann hilft der Export über „pnputil /add-driver <inf> /install“ von Hand, einen Knopf „Rückgängig“ gibt es hier nicht.",
                Verlust = "Das Gerät ist während der Neuinstallation 5 bis 30 Sekunden weg (Bild, Netz, Ton); eine Hersteller-Einstellung des Treibers (Profile, Farben) kann zurückgesetzt sein.",
                Sicherung = Massnahme.SicherungExport, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 60,
                Parameter = new[] { "instanzId" },
            }, TreiberNeu);
        }

        /// <summary>
        /// Code 0 nach der Neuinstallation ist Ja; ein von Windows verlangter Neustart (neustart = „1“)
        /// ist NachNeustart, auch wenn das Gerät bis dahin fehlt oder noch den Code trägt; ein Gerät,
        /// das ohne Neustart-Verlangen fehlt, ist Unlesbar (Windows richtet es womöglich gerade ein);
        /// ein nicht gelesener Code (ProblemCode null, M4-Entwurf 10.2) ist Unlesbar, nie Ja;
        /// ein Code ungleich 0 ist Nein (M4-Entwurf 1).
        /// </summary>
        static Nachweis TreiberNeu(Systembild bild, Dictionary<string, string> p)
        {
            string id = Wert(p, "instanzId");
            if (string.IsNullOrEmpty(id)) return Nachweis.Unlesbar("Der Parameter „instanzId“ fehlt; ohne ihn gibt es keinen Nachweis.");
            Geraet g = bild.Geraete.FirstOrDefault(x => string.Equals(x.InstanzId, id, StringComparison.OrdinalIgnoreCase));
            string name = g == null ? "Das Gerät mit der Instanz-Id " + id : "„" + (string.IsNullOrEmpty(g.Name) ? id : g.Name) + "“";
            // Nur eine gemessene 0 ist Ja; null (nicht gelesen) faellt nicht hierunter.
            if (g != null && g.ProblemCode == 0) return Nachweis.Ja(name + " ist neu eingerichtet und meldet keinen Fehlercode (0).");
            if (Wert(p, "neustart") == "1")
                return Nachweis.NachNeustart(name + ": Windows hat für die Neuinstallation einen Neustart verlangt (3010); bis dahin "
                    + (g == null ? "fehlt es im Systembild" : g.ProblemCode.HasValue ? "meldet es noch Code " + g.ProblemCode.Value : "wurde sein Fehlercode nicht gelesen")
                    + ", erst danach ist der Nachweis möglich.");
            if (g == null) return Nachweis.Unlesbar(name + " ist im neuen Systembild nicht zu finden; richtet Windows es gerade neu ein, hilft eine erneute Prüfung, belegen lässt sich so nichts.");
            if (!g.ProblemCode.HasValue) return Nachweis.Unlesbar(name + " steht im neuen Systembild, sein Fehlercode wurde aber nicht gelesen; ohne Code gibt es keinen Nachweis, eine erneute Prüfung liest ihn noch einmal.");
            int code = g.ProblemCode.Value;
            return Nachweis.Nein(name + " meldet weiterhin Code " + code + (code == 22 ? " (abgeschaltet)" : "") + "; die Neuinstallation hat den Fehler nicht behoben.");
        }

        /// <summary>
        /// Code 22 ist Nein (mit neustart = „1“ NachNeustart), Code 0 Ja, jeder andere Code Ja mit
        /// Hinweis (eingeschaltet, anderes Problem); ein nicht gelesener Code (ProblemCode null,
        /// M4-Entwurf 10.2) ist Unlesbar, weil ohne Code weder das Einschalten noch das Gegenteil belegt ist.
        /// </summary>
        static Nachweis GeraetAn(Systembild bild, Dictionary<string, string> p)
        {
            string id = Wert(p, "instanzId");
            if (string.IsNullOrEmpty(id)) return Nachweis.Unlesbar("Der Parameter „instanzId“ fehlt; ohne ihn gibt es keinen Nachweis.");
            Geraet g = bild.Geraete.FirstOrDefault(x => string.Equals(x.InstanzId, id, StringComparison.OrdinalIgnoreCase));
            if (g == null) return Nachweis.Unlesbar("Das Gerät mit der Instanz-Id " + id + " ist im neuen Systembild nicht zu finden; richtet Windows es gerade neu ein, hilft eine erneute Prüfung.");
            string name = "„" + (string.IsNullOrEmpty(g.Name) ? id : g.Name) + "“";
            if (!g.ProblemCode.HasValue) return Nachweis.Unlesbar(name + " steht im neuen Systembild, sein Fehlercode wurde aber nicht gelesen; ohne Code gibt es keinen Nachweis, eine erneute Prüfung liest ihn noch einmal.");
            int code = g.ProblemCode.Value;
            if (code == 22)
            {
                if (Wert(p, "neustart") == "1") return Nachweis.NachNeustart(name + " meldet noch Code 22; Windows hat einen Neustart verlangt, erst danach ist der Nachweis möglich.");
                return Nachweis.Nein(name + " meldet weiterhin Code 22 (abgeschaltet); das Einschalten wurde nicht übernommen.");
            }
            if (code == 0) return Nachweis.Ja(name + " ist eingeschaltet und meldet keinen Fehlercode (0).");
            return Nachweis.Ja(name + " ist eingeschaltet und meldet jetzt Code " + code + "; das ist ein anderes Problem als das Abschalten.");
        }

        // ================================================================== Speicher

        static void SpeicherTabelle()
        {
            // M4, Stufe 3: SHEmptyRecycleBin fuer das eigene Konto (Profilgebunden im Helfer). Die
            // Zahlen im Verlust-Text kommen aus Befund.Parameter dateien/bytes (Regel
            // speicherplatz.papierkorb), der Host setzt sie ueber VerlustMit ein; Pflichtparameter
            // fuer den Plan gibt es keine.
            EintragenHelferZuerst(new Massnahme
            {
                Kennung = "speicher.papierkorb.leeren", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Speicherplatz,
                Titel = "Papierkorb leeren",
                WasPassiert = "Der Papierkorb Ihres Kontos wird auf allen Laufwerken endgültig geleert (SHEmptyRecycleBin), ohne Nachfrage und ohne Fortschrittsfenster; das dauert je nach Menge 1 bis 60 Sekunden.",
                WasBleibt = "Dateien, die nicht im Papierkorb liegen, und die Papierkörbe anderer Konten werden nicht angefasst.",
                Risiko = "Was im Papierkorb lag, ist danach ohne Datenrettungsprogramm nicht mehr zu holen; einen Rückweg gibt es nicht.",
                Verlust = "Alle Dateien im Papierkorb werden endgültig gelöscht ({dateien} Dateien, {bytes} GB); es gibt keinen Rückweg.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 30,
            }, PapierkorbLeer);
        }

        /// <summary>
        /// PapierkorbBytes 0 ist Ja, größer 0 Nein, null (Quelle shell.papierkorb nicht gelesen) Unlesbar
        /// (M4-Entwurf 1). SHQueryRecycleBin zählt leere Dateien mit 0 Byte: 0 Byte bei gelesenen
        /// Dateien größer 0 ist deshalb Nein, der Korb ist dann nicht leer.
        /// </summary>
        static Nachweis PapierkorbLeer(Systembild bild, Dictionary<string, string> p)
        {
            Speicher sp = bild.Speicher;
            long? bytes = sp == null ? null : sp.PapierkorbBytes;
            long? dateien = sp == null ? null : sp.PapierkorbDateien;
            if (!bytes.HasValue)
            {
                Fehler fehler = bild.FehlerVon("shell.papierkorb").FirstOrDefault();
                return Nachweis.Unlesbar("Die Größe des Papierkorbs ließ sich im neuen Systembild nicht lesen" + (fehler != null ? " (" + fehler.Art + ")" : "") + "; kein Nachweis.");
            }
            if (bytes.Value < 0) return Nachweis.Unlesbar("Der Papierkorb meldet die unlesbare Größe " + bytes.Value + " Byte; kein Nachweis.");
            string zahl = dateien.HasValue ? dateien.Value.ToString(CultureInfo.InvariantCulture) + (dateien.Value == 1 ? " Datei" : " Dateien") : "Anzahl nicht gelesen";
            if (bytes.Value == 0)
            {
                if (dateien.HasValue && dateien.Value > 0) return Nachweis.Nein("Im Papierkorb liegen weiterhin " + zahl + " (0 Byte); geleert wurde er nicht.");
                return Nachweis.Ja("Der Papierkorb ist leer (0 Byte, " + zahl + ").");
            }
            return Nachweis.Nein("Im Papierkorb liegen weiterhin " + GbWort(bytes.Value) + " (" + zahl + "); er wurde nicht geleert.");
        }

        // ================================================================== Stabilität

        static void StabilitaetTabelle()
        {
            // M4, Stufe 3: bcdedit /bootsequence {memdiag} gilt genau fuer den naechsten Start und
            // loescht sich dann selbst (Rueckweg von selbst); das Ergebnis des Tests fuehrt das Bild nicht.
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "stabilitaet.speichertest.planen", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Stabilitaet, Neustart = true,
                Titel = "Arbeitsspeicher-Test planen",
                WasPassiert = "Der nächste Start geht in die Windows-Speicherdiagnose (bcdedit /bootsequence {memdiag}); sie prüft den Arbeitsspeicher 10 bis 30 Minuten lang, danach startet Windows von selbst normal, und der Eintrag löscht sich nach genau 1 Start.",
                WasBleibt = "Dateien, Einstellungen und die Startkonfiguration werden nicht angefasst; der Test schreibt nur Testmuster in den Arbeitsspeicher und liest sie zurück, auf dem Datenträger ändert er nichts.",
                Risiko = "Keines für die Daten: der PC ist während des Tests nicht benutzbar; wer den Test doch nicht will, wartet 1 Durchlauf ab, ein Rückweg ist nicht nötig.",
                Verlust = "Der nächste Start geht in die Speicherdiagnose (10 bis 30 Minuten), der PC ist solange nicht benutzbar; danach startet Windows normal.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 5,
            }, p => Nachweis.NachNeustart("Der Speichertest ist für den nächsten Start eingetragen; sein Ergebnis zeigt Windows danach in der Ereignisanzeige (MemoryDiagnostics-Results), das Systembild führt es nicht."));
        }

        // ================================================================== Rückweg

        static void RueckwegTabelle()
        {
            Eintragen(new Massnahme
            {
                Kennung = "rueckweg", Stufe = 2, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Rückgängig machen",
                WasPassiert = "Die vor der Maßnahme gesicherten Werte werden aus der Sicherungsdatei zurückgeschrieben; das dauert unter 15 Sekunden.",
                WasBleibt = "Alles, was die Maßnahme nicht angefasst hat, bleibt unverändert; die Sicherungsdatei bleibt liegen.",
                Risiko = "Wurde der Wert seit der Maßnahme von Hand geändert, überschreibt der Rückweg diese Änderung; für den Rückweg selbst gibt es keine Sicherung, die Maßnahme lässt sich aber erneut ausführen.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 15,
                Parameter = new[] { "sicherung" },
                Pruefen = RueckwegGelungen,
            });
        }

        /// <summary>
        /// „Wie die Hin-Maßnahme, umgekehrt“ (M3-Entwurf 8.1): die Hin-Kennung kommt als „kennung“
        /// oder „rueckweg.kennung“, ihre Pflichtparameter als „&lt;name&gt;“ oder
        /// „rueckweg.parameter.&lt;name&gt;“ (der Helfer schreibt die zweite Form in die Werte).
        /// Fehlt die Kennung, ist sie „rueckweg“ oder fehlt ein Pflichtparameter, ist der Rückweg
        /// nicht messbar (Unlesbar), nie bestanden: ein „Nein wegen fehlender Daten“ der
        /// Hin-Maßnahme darf nicht zu „Rückweg gelungen“ werden. Vor dem Drehen zählt die Meldung des
        /// Helfers zum Rückweg selbst („nachweis.bestanden“ = „0“ ist Nein); der Hin-Nachweis läuft
        /// nur mit den Pflichtparametern, damit „neustart“ und „nachweis.bestanden“ des Rückwegs
        /// nicht als Hin-Parameter wirken.
        /// Gilt der Zustand der Hin-Maßnahme noch (Hin-Nachweis Ja) und hat der Helfer
        /// „neustart“ = „1“ gemeldet, ist der Rückweg geschrieben und wirkt erst nach dem Neustart
        /// (Win32_PnPEntity.Disable mit rebootNeeded: das Gerät meldet bis dahin Code 0); ohne diese
        /// Meldung ist es Nein, weil das Bild den alten Zustand nicht belegt.
        /// Hin-Maßnahmen ohne messbaren Zustand (M4-Entwurf 2.2: netz.ip.zuruecksetzen,
        /// netz.winsock.zuruecksetzen) lassen sich nicht drehen, ihr Hin-Nachweis ist immer
        /// NachNeustart: dort zählt allein die Meldung des Helfers, „1“ ist Ja, „0“ ist Nein mit
        /// seinem Satz, ohne Meldung ist es nicht messbar.
        /// </summary>
        static Nachweis RueckwegGelungen(Systembild bild, Dictionary<string, string> p)
        {
            string kennung = Wert(p, "kennung") ?? Wert(p, "rueckweg.kennung");
            if (string.IsNullOrEmpty(kennung))
                return Nachweis.Unlesbar("Die Hin-Maßnahme ist nicht genannt (Parameter „kennung“ fehlt); ohne sie lässt sich nicht belegen, welcher Zustand jetzt gilt.");
            if (kennung == "rueckweg")
                return Nachweis.Unlesbar("Die Hin-Maßnahme ist als „rueckweg“ genannt; ein Rückweg des Rückwegs hat keinen Nachweis.");
            Massnahme hin = Finde(kennung);
            if (hin == null)
                return Nachweis.Unlesbar("Die Hin-Maßnahme „" + kennung + "“ steht nicht im Katalog; ohne sie gibt es keinen Nachweis.");
            if (Array.IndexOf(RueckwegOhneZustand, kennung) >= 0)
            {
                Nachweis fehler = HelferFehler(p, "Rückgängig: " + hin.Titel);
                if (fehler != null) return fehler;
                string satz = Wert(p, HelferSatz);
                if (Wert(p, HelferBestanden) == "1")
                {
                    string ja = "Der Zustand vor „" + hin.Titel + "“ ist zurückgeschrieben; der Helfer meldete das ohne Fehler" + (string.IsNullOrEmpty(satz) ? "." : ": " + satz);
                    return Wert(p, "neustart") == "1" ? Nachweis.NachNeustart(ja) : Nachweis.Ja(ja);
                }
                return Nachweis.Unlesbar("Der Rückweg von „" + hin.Titel + "“ ist nicht am Systembild messbar; der Helfer meldete " + (string.IsNullOrEmpty(satz) ? "kein Ergebnis (nachweis.bestanden fehlt)" : "„" + satz + "“ ohne Ergebnis (nachweis.bestanden fehlt)") + ".");
            }
            // HelferFehler zuerst (M4-Entwurf 2.2), auch bei messbarem Zustand: meldet der Helfer den
            // Rueckweg als gescheitert (nachweis.bestanden = "0"), ist er es, gleich was das Bild
            // zeigt. Der Hin-Nachweis unten laeuft nur mit den Pflichtparametern (hp), damit die
            // Meldung des Rueckwegs dort nicht als Meldung der Hin-Massnahme wirkt.
            Nachweis helfer = HelferFehler(p, "Rückgängig: " + hin.Titel);
            if (helfer != null) return helfer;
            var hp = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in hin.Parameter)
            {
                string v = Wert(p, name) ?? Wert(p, "rueckweg.parameter." + name);
                if (string.IsNullOrEmpty(v))
                    return Nachweis.Unlesbar("Der Parameter „" + name + "“ der Hin-Maßnahme „" + hin.Titel + "“ fehlt; ohne ihn gibt es keinen Nachweis.");
                hp[name] = v;
            }
            Nachweis n = hin.Pruefen(bild, hp);
            if (n.Unbestimmt)
                return Nachweis.Unlesbar("Der Nachweis der Hin-Maßnahme „" + hin.Titel + "“ ist nicht messbar, also auch der Rückweg nicht: " + n.Satz);
            if (n.Bestanden)
            {
                if (Wert(p, "neustart") == "1")
                    return Nachweis.NachNeustart("Der Zustand vor „" + hin.Titel + "“ ist zurückgeschrieben; bis zum Neustart gilt noch: " + n.Satz);
                return Nachweis.Nein("Der Zustand nach „" + hin.Titel + "“ gilt noch, der Rückweg hat ihn nicht geändert: " + n.Satz);
            }
            if (hin.Neustart || Wert(p, "neustart") == "1")
                return Nachweis.NachNeustart("Der Zustand vor „" + hin.Titel + "“ ist zurückgeschrieben und gilt nach dem nächsten Neustart (Nachweis der Maßnahme: nicht mehr erfüllt).");
            return Nachweis.Ja("Der Zustand vor „" + hin.Titel + "“ gilt wieder (Nachweis der Maßnahme: nicht mehr erfüllt).");
        }

        // ================================================================== Ansicht und Rat

        static void AnsichtUndRat()
        {
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "speicher.aufraeumen", Stufe = 2, Art = Massnahme.ArtAnsicht, Bereich = Kern.Bereich.Speicherplatz,
                Titel = "Speicherplatz aufräumen",
                WasPassiert = "Die Ansicht „Wo steckt der Platz?“ zeigt je Kategorie (Papierkorb, temporäre Dateien, Update-Reste, Vorschaubilder, Browser-Cache, Spiele-Reste), wie viele MB frei würden; ohne Ihre Auswahl wird nichts gelöscht.",
                WasBleibt = "Dokumente, Bilder und Programme werden nie angefasst; nur die gewählten Kategorien werden geleert.",
                Risiko = "Ein geleerter Papierkorb ist endgültig weg; alles andere legt Windows bei Bedarf neu an, ohne dass etwas fehlt.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 120,
            }, "Die gewählten Kategorien wurden geleert; die freigegebene Menge steht im Aufräum-Bericht, das Systembild führt sie nicht.");

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "update.einstellungen.oeffnen", Stufe = 0, Art = Massnahme.ArtAnsicht, Bereich = Kern.Bereich.Updates,
                Titel = "Windows Update öffnen",
                WasPassiert = "Die Windows-Seite „Einstellungen > Windows Update“ wird geöffnet; die Suche nach Updates starten Sie dort selbst, das Programm startet keine.",
                WasBleibt = "Es wird nichts installiert und nichts geändert, solange Sie dort nicht klicken.",
                Risiko = "Keines: das Öffnen einer Einstellungsseite verändert nichts.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 5,
            }, "Die Seite Windows Update wurde geöffnet; ob Updates gefunden wurden, zeigt Windows dort, das Systembild führt es nicht.");

            // M4: die sechs Raete aus M3 sind ausfuehrbar geworden (netz.hosts.pruefen ist durch
            // netz.hosts.bereinigen ersetzt). Einziger Rat der Stufe 3 bleibt der vorherige Treiber,
            // bis DiRollbackDriver gebaut ist (M4-Entwurf 0, "Nicht in 8.3.0").
            Rat("geraet.treiber.zurueck", Kern.Bereich.Hardware, "Vorherigen Treiber wiederherstellen",
                "Im Geräte-Manager (Eigenschaften des Geräts, Reiter „Treiber“) stellt der Knopf „Vorheriger Treiber“ die zuletzt installierte Fassung wieder her; dieses Programm führt das bis auf Weiteres nicht selbst aus.",
                "Andere Geräte werden nicht angefasst; der Knopf ist nur aktiv, wenn Windows noch 1 ältere Fassung des Treibers hat.",
                "Die ältere Fassung kann Fehler haben, die die neue behoben hatte; vorher 1 Wiederherstellungspunkt anlegen, dann ist der Weg zurück offen.");
        }

        /// <summary>Stufe-3-Rat: nur Text (was das Programm nicht selbst ausführt), nie ausführbar, darum ohne Nachweis am Bild.</summary>
        static void Rat(string kennung, string bereich, string titel, string wasPassiert, string wasBleibt, string risiko)
        {
            Eintragen(new Massnahme
            {
                Kennung = kennung, Stufe = 3, Art = Massnahme.ArtRat, Bereich = bereich,
                Titel = titel, WasPassiert = wasPassiert, WasBleibt = wasBleibt, Risiko = risiko,
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 0,
                Pruefen = (bild, p) => Nachweis.Nein("Dieser Rat wird von diesem Programm nicht ausgeführt; einen Nachweis gibt es erst, wenn Sie ihn von Hand erledigt haben und neu prüfen."),
            });
        }

        // ================================================================== Nachweise ohne messbaren Zustand

        // Werte-Schlüssel des Helfers (helfer/Massnahmen/Gemeinsam.cs, Nachweise.BestandenSchluessel /
        // SatzSchluessel); der Kern darf helfer\ nicht referenzieren, deshalb stehen sie hier noch einmal.
        const string HelferBestanden = "nachweis.bestanden";
        const string HelferSatz = "nachweis.satz";

        // Hin-Massnahmen, deren Rueckweg sich nicht am Bild messen laesst (M4-Entwurf 2.2):
        // RueckwegGelungen liest fuer sie allein die Meldung des Helfers.
        static readonly string[] RueckwegOhneZustand = { "netz.ip.zuruecksetzen", "netz.winsock.zuruecksetzen" };

        /// <summary>
        /// Eintrag, dessen Zustand das Systembild nicht führt (Regel 2 im Dateikopf): der Nachweis ist
        /// Ja mit dem ehrlichen Satz, sofern der Helfer keinen Fehler gemeldet hat. Der Kern sieht die
        /// Ausführung nicht; ohne diese Sperre wäre „MSFT_DNSClientCache.Clear wirft“ trotzdem ein
        /// bestandener Nachweis „Der DNS-Cache wurde geleert“.
        /// </summary>
        static void EintragenOhneZustand(Massnahme m, string satzJa)
        {
            EintragenOhneZustand(m, p => Nachweis.Ja(satzJa));
        }

        /// <summary>Wie oben, wenn der Ja-Satz von den Parametern abhängt oder NachNeustart ist.</summary>
        static void EintragenOhneZustand(Massnahme m, Func<Dictionary<string, string>, Nachweis> ja)
        {
            m.Pruefen = (bild, p) => HelferFehler(p, m.Titel) ?? ja(p);
            Eintragen(m);
        }

        /// <summary>
        /// Eintrag mit messbarem Zustand, dessen Nachweis trotzdem zuerst die Meldung des Helfers liest
        /// (M4-Entwurf 2.2, „HelferFehler zuerst“): meldet der Helfer nachweis.bestanden = „0“, ist der
        /// Eingriff gescheitert, auch wenn das Bild den Zielzustand zeigt (etwa, weil er schon vorher
        /// galt). Fehlt die Meldung oder ist sie „1“, entscheidet das Bild.
        /// </summary>
        static void EintragenHelferZuerst(Massnahme m, Func<Systembild, Dictionary<string, string>, Nachweis> pruefen)
        {
            m.Pruefen = (bild, p) => HelferFehler(p, m.Titel) ?? pruefen(bild, p);
            Eintragen(m);
        }

        /// <summary>
        /// Liest den optionalen Parameter „nachweis.bestanden“ (Werte-Schlüssel des Helfers, vom Host und
        /// von wartung.massnahmen in das Wörterbuch gereicht). Fehlt er oder ist er leer, hat der Helfer
        /// nichts gemeldet (M2-Kennungen, Trockenlauf): null, der Nachweis bleibt beim Aufrufer. „0“ ist
        /// Nein und zitiert „nachweis.satz“; jeder andere Wert als „1“ ist ebenfalls Nein, weil ein
        /// unlesbares Ergebnis kein bestandener Lauf ist (fail closed).
        /// </summary>
        static Nachweis HelferFehler(Dictionary<string, string> p, string titel)
        {
            string bestanden = Wert(p, HelferBestanden);
            if (string.IsNullOrEmpty(bestanden) || bestanden == "1") return null;
            if (bestanden == "0")
            {
                string satz = Wert(p, HelferSatz);
                return Nachweis.Nein(titel + " lief mit Fehler; " + (string.IsNullOrEmpty(satz) ? "der Helfer meldete kein Ergebnis." : satz));
            }
            return Nachweis.Nein(titel + " gilt nicht als ausgeführt; der Helfer meldete das unlesbare Ergebnis „" + bestanden + "“ statt 0 oder 1.");
        }

        // ================================================================== Meilenstein 2

        // Die Kennungen aus docs/M2-ENTWURF.md, Abschnitt 3, brauchen Texte für Vorschau und Verlauf.
        // Ihre Nachweise sind ehrlich: DISM, SFC, pnputil und die Werkzeuge liefern ihr Ergebnis in der
        // Ausgabe, das Systembild führt es nicht. Seit M4 laeuft DISM ueber die DISM-API und meldet
        // dism.zustand, SFC meldet sfc.zustand (Werte-Schluessel, docs/M4-ENTWURF.md 3.4).
        static void Meilenstein2()
        {
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "tiefenpruefung", Stufe = 0, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Tiefenprüfung der Windows-Dateien",
                WasPassiert = "DISM prüft den Komponentenspeicher über die Windows-Schnittstelle (CheckImageHealth mit Scan), dann vergleicht SFC alle Windows-Dateien mit den Vorlagen (sfc /verifyonly); das dauert 10 bis 50 Minuten und verändert nichts.",
                WasBleibt = "Es wird keine Datei ersetzt; das Ergebnis steht in der Ausgabe, die Reparatur ist eine eigene Maßnahme.",
                Risiko = "Keines: beide Werkzeuge lesen nur und ändern nichts, der PC ist währenddessen etwas langsamer.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 1800,
            }, "Die Tiefenprüfung verändert nichts; ihr Ergebnis steht in der Ausgabe von DISM und SFC, nicht im Systembild.");

            // Stufe 3 seit M4 an Karten (Updates mit Komponentenspeicher-Fehlercode, Programmabsturz in
            // einem Windows-Modul). Der Wiederherstellungspunkt ist die Sicherung, ein Rueckweg per
            // Knopf gibt es nicht (der Systemschutz holt den Punkt von Hand zurueck). Der Nachweis liest
            // dism.zustand und sfc.zustand; ein Wert, der nicht "gut" heisst, ist Nein, ein fehlender
            // Wert neben einem vorhandenen ebenfalls (fail closed). Fehlen beide, ist es Unlesbar:
            // eine leere Antwort ist kein bestandener Lauf.
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "dateien.reparieren", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Reparatur der Windows-Dateien",
                WasPassiert = "Zuerst wird 1 Wiederherstellungspunkt angelegt, dann repariert DISM den Komponentenspeicher über die Windows-Schnittstelle (RestoreHealth, Quelle Windows Update) und prüft ihn erneut, danach ersetzt SFC beschädigte Systemdateien (sfc /scannow); das dauert 20 bis 80 Minuten.",
                WasBleibt = "Eigene Dateien, Programme und Einstellungen werden nicht angefasst; ersetzt werden nur Dateien, die zu Windows gehören.",
                Risiko = "Bricht DISM ohne Internet oder mit einem Fehler ab, nennt das Protokoll den Code, und SFC läuft trotzdem; verlangt SFC einen Neustart, sagt es der Nachweis. Ein „Rückgängig“ gibt es nicht, den Wiederherstellungspunkt holt der Systemschutz von Hand zurück.",
                Verlust = "Beschädigte Windows-Dateien werden durch Kopien aus dem Komponentenspeicher oder von Windows Update ersetzt; eigene Änderungen an Systemdateien gehen dabei verloren und kommen nicht zurück.",
                Sicherung = Massnahme.SicherungPunkt, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 2700,
            }, DateienRepariert);

            Eintragen(new Massnahme
            {
                Kennung = "wiederherstellungspunkt.anlegen", Stufe = 1, Bereich = Kern.Bereich.Datentraeger,
                Titel = "Wiederherstellungspunkt anlegen",
                WasPassiert = "Windows sichert den Zustand der Systemdateien, Treiber und der Registrierung unter der angegebenen Beschreibung; das dauert meist unter 1 Minute. Vorher hebt das Programm die 24-Stunden-Sperre von Windows dauerhaft auf (SystemRestorePointCreationFrequency 0), sonst entstünde nach einem Punkt vom selben Tag kein zweiter.",
                WasBleibt = "Eigene Dateien werden nicht gesichert und nicht angefasst; ältere Punkte bleiben, bis Windows den Platz braucht.",
                Risiko = "Keines: ist der Systemschutz für das Laufwerk aus, entsteht kein Punkt, und der Nachweis sagt das.",
                Sicherung = Massnahme.SicherungPunkt, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 60,
                Parameter = new[] { "beschreibung" },
                Pruefen = PunktAngelegt,
            });

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "wiederherstellungspunkt.zurueck", Stufe = Massnahme.StufeUnumkehrbar, Bereich = Kern.Bereich.Datentraeger, Neustart = true,
                Titel = "Windows auf einen früheren Stand zurücksetzen",
                WasPassiert = "Windows setzt Systemdateien, Treiber und Registrierung auf den gewählten Wiederherstellungspunkt zurück und startet dafür neu; das dauert 5 bis 20 Minuten.",
                WasBleibt = "Dokumente, Bilder und andere eigene Dateien werden nicht angefasst; Programme, die nach dem Punkt installiert wurden, fehlen danach.",
                Risiko = "Programme und Treiber seit dem Punkt gehen verloren; Windows legt vorher selbst 1 Punkt an, über den sich das Zurücksetzen rückgängig machen lässt.",
                Verlust = "Programme und Treiber, die nach dem gewählten Punkt installiert wurden, sind danach weg und müssen neu installiert werden; eigene Dateien (Dokumente, Bilder) bleiben, keine davon geht verloren.",
                Sicherung = Massnahme.SicherungPunkt, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 600,
                Parameter = new[] { "folge" },
            }, p => Nachweis.NachNeustart("Windows setzt den Stand beim Neustart zurück; ohne diesen Neustart gibt es keinen Nachweis."));

            // M4 (docs/M4-ENTWURF.md 3.6): das ZIP-Update laeuft als Plan ueber den erhoehten Helfer
            // statt ueber eine cmd-Batch per runas. Die Sicherung sind die .vorher-Dateien im
            // Programmordner (kein JSON), der naechste Helferstart raeumt sie weg; Rueckweg von Hand.
            EintragenOhneZustand(new Massnahme
            {
                Kennung = "update.anwenden", Stufe = 1, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Programm-Update einspielen",
                WasPassiert = "Der Helfer prüft die heruntergeladene ZIP-Datei (Prüfsumme, Herausgeber mit Name und Schlüssel, Version nie kleiner als die laufende, Startprüfung der neuen Fassung) und tauscht dann die Programmdateien; jede ersetzte Datei bleibt bis zum nächsten Start mit Administratorrechten als „.vorher“ liegen.",
                WasBleibt = "Einstellungen, Verlauf, Sicherungen und Protokolle werden nicht angefasst; nur die Dateien aus dem Paket werden ersetzt.",
                Risiko = "Scheitert ein Schritt, rollt der Helfer alle Umbenennungen zurück und ändert nichts; ein „Rückgängig“ über den Knopf gibt es nicht, die „.vorher“-Dateien lassen sich von Hand zurückbenennen.",
                Sicherung = Massnahme.SicherungWerte, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 60,
                Parameter = new[] { "sha256" },
            }, UpdateAngewendet);

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "werkzeug", Stufe = 1, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Werkzeug aus dem Werkzeugkasten",
                WasPassiert = "Das gewählte Werkzeug führt seine Schritte aus dem Werkzeugkasten aus; die Stufe hängt am Eintrag (1 leicht, 2 Eingriff, 3 tief) und steht in der Vorschau.",
                WasBleibt = "Was das Werkzeug nicht anfasst, bleibt unverändert; die Einzelheiten stehen in der Beschreibung des Eintrags.",
                Risiko = "Werkzeuge der Stufe 3 sind nicht umkehrbar; bei ihnen wird vorher ein Wiederherstellungspunkt angeboten, wenn der Eintrag das vorsieht.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegKeiner, DauerSekunden = 300,
                Parameter = new[] { "id" },
            }, "Das Werkzeug ist gelaufen; sein Ergebnis steht in der Ausgabe, ein Nachweis am Systembild ist dafür nicht hinterlegt.");

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "wartung.auto", Stufe = 1, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Geplante Wartung",
                WasPassiert = "Die geplante Wartung misst den PC, führt die freigeschalteten Maßnahmen der Stufe 1 aus, die ein Befund vorschlägt, und danach die gewählten Werkzeugsätze; ohne Freischaltung führt sie keine Maßnahme aus.",
                WasBleibt = "Maßnahmen der Stufen 2 und 3 laufen nie von selbst; sie brauchen immer Vorschau und Klick.",
                Risiko = "Jede freigeschaltete Maßnahme trägt ihr eigenes, kleines Risiko (siehe deren Vorschau); die Wartung selbst fügt keines hinzu.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 1800,
            }, "Die geplante Wartung ist gelaufen; die Nachweise je Maßnahme stehen im Protokoll (auto.nachweise), nicht in einem einzelnen Satz.");

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "apps.entfernen", Stufe = 2, Bereich = Kern.Bereich.Speicherplatz,
                Titel = "Vorinstallierte Apps entfernen",
                WasPassiert = "Die gewählten Apps werden für Ihr Konto deinstalliert; je App dauert das 1 bis 10 Sekunden.",
                WasBleibt = "Apps anderer Konten und alles, was nicht auf der Liste steht, bleibt; Windows selbst wird nicht verändert.",
                Risiko = "Eine entfernte App fehlt, bis sie aus dem Store neu installiert wird; ein Wiederherstellungspunkt vorher ist wählbar, holt Store-Apps aber nicht immer zurück.",
                Sicherung = Massnahme.SicherungPunkt, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 120,
                Parameter = new[] { "pakete" },
            }, "Die gewählten Apps wurden entfernt; das Systembild führt keine Store-Apps, der Nachweis steht in der Ausgabe.");

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "netz.diagnose", Stufe = 0, Bereich = Kern.Bereich.Netz,
                Titel = "Netzwerk-Diagnose",
                WasPassiert = "Das Ziel wird 4-mal angepingt und der Weg dorthin mit höchstens 20 Stationen verfolgt (tracert); das dauert bis zu 1 Minute.",
                WasBleibt = "Es wird nichts verändert; die Diagnose liest nur.",
                Risiko = "Keines: es gehen nur wenige Testpakete ins Netz, verändert wird nichts.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 60,
                Parameter = new[] { "ziel" },
            }, "Die Diagnose liest nur; ihr Ergebnis steht in der Ausgabe, nicht im Systembild.");

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "treiber.sichern", Stufe = 0, Bereich = Kern.Bereich.Hardware,
                Titel = "Treiber-Backup",
                WasPassiert = "Alle installierten Treiber werden mit pnputil in den gewählten Ordner kopiert; das dauert 1 bis 5 Minuten und braucht je nach PC 1 bis 5 GB.",
                WasBleibt = "Die Treiber im System werden nicht verändert; es wird nur kopiert.",
                Risiko = "Keines: ist der Zielordner voll, bricht der Export ab, und die Ausgabe nennt den Fehler, ohne dass im System etwas fehlt.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 180,
                Parameter = new[] { "ordner" },
            }, "Die Treiber wurden in den Ordner kopiert; ein Nachweis am Systembild ist nicht möglich, die Ausgabe von pnputil nennt die Zahl.");

            Eintragen(new Massnahme
            {
                Kennung = "zeitplan.anlegen", Stufe = 1, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Zeitplan für die geplante Wartung anlegen",
                WasPassiert = "In der Aufgabenplanung wird die Aufgabe „" + AufgabeWartung + "“ mit Ihrem Konto angelegt; sie startet die Wartung zur gewählten Zeit (täglich, wöchentlich oder monatlich), ohne dass Sie das Programm selbst starten müssen.",
                WasBleibt = "Andere Aufgaben bleiben unverändert; ein vorhandener Zeitplan dieses Programms wird ersetzt, nicht verdoppelt.",
                Risiko = "Keines: die Aufgabe lässt sich jederzeit mit 1 Klick wieder löschen.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 10,
                Parameter = new[] { "modus", "stunde", "minute" },
                Pruefen = (bild, p) =>
                {
                    Aufgabe a = AufgabeFinden(bild, AufgabeWartung);
                    if (a != null) return Nachweis.Ja("Die Aufgabe „" + AufgabeWartung + "“ ist in der Aufgabenplanung angelegt (Zustand " + a.Zustand + ").");
                    if (!bild.Autostart.AufgabenVollstaendig)
                        return Nachweis.Unlesbar("Die Aufgabe „" + AufgabeWartung + "“ ist im neuen Systembild nicht zu finden; die Aufgabenliste war nicht vollständig lesbar, das belegt nichts.");
                    return Nachweis.Nein("Die Aufgabe „" + AufgabeWartung + "“ ist im neuen Systembild nicht zu finden (Aufgabenliste vollständig gelesen).");
                },
            });

            Eintragen(new Massnahme
            {
                Kennung = "zeitplan.loeschen", Stufe = 1, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Zeitplan löschen",
                WasPassiert = "Die Aufgabe „" + AufgabeWartung + "“ wird aus der Aufgabenplanung entfernt; die geplante Wartung läuft danach nicht mehr von selbst.",
                WasBleibt = "Alle Einstellungen und Freischaltungen bleiben gespeichert, nichts davon geht verloren; ein neuer Zeitplan übernimmt sie wieder.",
                Risiko = "Keines: ohne Zeitplan prüft das Programm nur noch, wenn Sie es starten.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 10,
                Pruefen = (bild, p) =>
                {
                    Aufgabe a = AufgabeFinden(bild, AufgabeWartung);
                    if (a != null) return Nachweis.Nein("Die Aufgabe „" + AufgabeWartung + "“ ist weiterhin in der Aufgabenplanung (Zustand " + a.Zustand + ").");
                    // Eine unvollstaendig gelesene Liste (ohne Rechte fehlt rund ein Viertel) belegt
                    // kein Loeschen: die eigene Aufgabe koennte im ungelesenen Teil stehen.
                    if (!bild.Autostart.AufgabenVollstaendig)
                        return Nachweis.Unlesbar("Die Aufgabe „" + AufgabeWartung + "“ ist im neuen Systembild nicht zu finden; die Aufgabenliste war nicht vollständig lesbar, das belegt kein Löschen.");
                    return Nachweis.Ja("Die Aufgabe „" + AufgabeWartung + "“ steht nicht mehr in der Aufgabenplanung (Aufgabenliste vollständig gelesen).");
                },
            });

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "selbststart.loeschen", Stufe = 1, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Selbststart-Aufgabe entfernen",
                WasPassiert = "Die alte Selbststart-Aufgabe „" + AufgabeSelbststart + "“ aus Version 8.0, die noch mit Administratorrechten startete, wird gelöscht; das Programm legt sie danach neu mit Ihren Nutzerrechten an.",
                WasBleibt = "Ihre Einstellung „Mit Windows starten“ wird nicht angerührt; nur die Rechte der Aufgabe ändern sich.",
                Risiko = "Keines: schlägt das Neuanlegen fehl, startet das Programm nicht mehr von selbst mit, bis Sie den Schalter erneut setzen.",
                Sicherung = Massnahme.SicherungKeine, Rueckweg = Massnahme.RueckwegVonSelbst, DauerSekunden = 10,
            }, "Die alte Aufgabe wurde gelöscht; ob die neue mit Nutzerrechten läuft, zeigt das Systembild nicht (kein RunLevel im Bild).");

            EintragenOhneZustand(new Massnahme
            {
                Kennung = "registrierung.entfernen", Stufe = 2, Bereich = Kern.Bereich.Stabilitaet,
                Titel = "Registrierung aufräumen",
                WasPassiert = "Die gewählten verwaisten Einträge werden aus der Registrierung entfernt; vorher wird jeder als .reg-Datei unter „sicherungen\\registrierung“ gesichert, ohne diese Sicherung wird kein Eintrag entfernt.",
                WasBleibt = "Einträge, die nicht auf der Liste stehen, bleiben; Programme und Dateien werden nicht angefasst.",
                Risiko = "Ein entfernter Eintrag, den ein Programm doch noch brauchte, fehlt; die .reg-Datei bringt ihn mit 1 Doppelklick zurück.",
                Sicherung = SicherungRegExport, Rueckweg = Massnahme.RueckwegSicherung, DauerSekunden = 60,
                Parameter = new[] { "kennungen" },
            }, "Die gewählten Einträge wurden entfernt; die Zahl steht in der Ausgabe (registrierung.entfernt), das Systembild führt sie nicht.");
        }

        /// <summary>
        /// Satz aus dism.zustand (healthy|repairable|nonrepairable|fehler nach RestoreHealth und
        /// erneutem CheckImageHealth) und sfc.zustand (ohneBefund|repariert|nichtRepariert|fehler).
        /// „Gut“ sind healthy, ohneBefund und repariert; alles andere, auch ein unlesbarer oder
        /// neben dem anderen fehlender Wert, ist Nein. Verlangt SFC einen Neustart (neustart = „1“,
        /// Exit 3010 oder CBS „pending“), ist es NachNeustart. Fehlen beide Werte, ist es Unlesbar:
        /// der Helfer (helfer/Massnahmen/System.cs) schreibt beide vor jeder Meldung, ein Paar ohne
        /// beide ist ein Lauf, der nicht bis dorthin kam, keine Reparatur (Regel 3 im Dateikopf).
        /// </summary>
        static Nachweis DateienRepariert(Dictionary<string, string> p)
        {
            string dism = Wert(p, "dism.zustand"), sfc = Wert(p, "sfc.zustand");
            if (string.IsNullOrEmpty(dism) && string.IsNullOrEmpty(sfc))
                return Nachweis.Unlesbar("Weder DISM noch SFC haben ein Ergebnis gemeldet (dism.zustand und sfc.zustand fehlen); ohne Ergebnis gibt es keinen Nachweis.");
            string dismSatz, sfcSatz;
            bool dismGut = DismGut(dism, Wert(p, "dism.repariert") == "1", Wert(p, "dism.hresult"), out dismSatz);
            bool sfcGut = SfcGut(sfc, Wert(p, "sfc.exit"), out sfcSatz);
            string satz = dismSatz + " " + sfcSatz;
            if (!dismGut || !sfcGut) return Nachweis.Nein(satz);
            if (Wert(p, "neustart") == "1") return Nachweis.NachNeustart(satz + " SFC verlangt 1 Neustart, erst danach gelten alle ersetzten Dateien.");
            return Nachweis.Ja(satz);
        }

        static bool DismGut(string zustand, bool repariert, string hresult, out string satz)
        {
            switch (zustand)
            {
                case "healthy": satz = "DISM: der Komponentenspeicher ist in Ordnung (healthy" + (repariert ? ", durch RestoreHealth repariert, nichts blieb beschädigt" : ", nichts zu ersetzen") + ")."; return true;
                case "repairable": satz = "DISM: der Komponentenspeicher ist weiterhin beschädigt, aber reparierbar (repairable); RestoreHealth hat ihn nicht vollständig repariert."; return false;
                case "nonrepairable": satz = "DISM: der Komponentenspeicher ist beschädigt und nicht reparierbar (nonrepairable); es hilft nur eine Reparaturinstallation von Windows."; return false;
                case "fehler": satz = "DISM: der Aufruf scheiterte (" + (string.IsNullOrEmpty(hresult) ? "ohne Code" : "HRESULT " + hresult) + "); nichts wurde repariert."; return false;
                case null: case "": satz = "DISM: kein Ergebnis gemeldet (dism.zustand fehlt); ohne Ergebnis gilt nichts als repariert."; return false;
                default: satz = "DISM: unlesbares Ergebnis „" + zustand + "“ (weder healthy, repairable, nonrepairable noch fehler); ohne lesbares Ergebnis gilt nichts als repariert."; return false;
            }
        }

        static bool SfcGut(string zustand, string exit, out string satz)
        {
            switch (zustand)
            {
                case "ohneBefund": satz = "SFC: keine beschädigte Systemdatei gefunden (0 ersetzt)."; return true;
                case "repariert": satz = "SFC: beschädigte Systemdateien wurden ersetzt (repariert, keine blieb beschädigt)."; return true;
                case "nichtRepariert": satz = "SFC: beschädigte Systemdateien konnten nicht ersetzt werden (nichtRepariert); Einzelheiten stehen in CBS.log."; return false;
                case "fehler": satz = "SFC: der Aufruf scheiterte (Exit " + (string.IsNullOrEmpty(exit) ? "unbekannt" : exit) + "); nichts wurde ersetzt."; return false;
                case null: case "": satz = "SFC: kein Ergebnis gemeldet (sfc.zustand fehlt); ohne Ergebnis gilt nichts als repariert."; return false;
                default: satz = "SFC: unlesbares Ergebnis „" + zustand + "“ (weder ohneBefund, repariert, nichtRepariert noch fehler); ohne lesbares Ergebnis gilt nichts als repariert."; return false;
            }
        }

        /// <summary>
        /// „Fassung X ist eingerichtet, davor Y“ aus update.version und update.vorher; fehlen die Werte,
        /// bleibt der ehrliche Satz (der Nachweis ist nachweis.bestanden des Helfers, M4-Entwurf 1).
        /// Sind beide Werte da, müssen beide als Versionsnummer lesbar sein, sonst Unlesbar: ein
        /// unlesbarer Wert ist kein bestandener Nachweis. Ist die neue kleiner, ist es Nein: ein
        /// Rückschritt ist kein eingespieltes Update, gleich was der Helfer meldete.
        /// </summary>
        static Nachweis UpdateAngewendet(Dictionary<string, string> p)
        {
            string neu = Wert(p, "update.version"), alt = Wert(p, "update.vorher");
            if (string.IsNullOrEmpty(neu) || string.IsNullOrEmpty(alt))
                return Nachweis.Ja("Das Update ist eingespielt; die Fassungen stehen in der Ausgabe des Helfers (update.version, update.vorher), nicht im Systembild.");
            Version vNeu, vAlt;
            if (!Version.TryParse(neu, out vNeu) || !Version.TryParse(alt, out vAlt))
                return Nachweis.Unlesbar("Die Fassungen (update.version „" + neu + "“, update.vorher „" + alt + "“) sind nicht als Versionsnummer lesbar; kein Nachweis.");
            if (vNeu < vAlt)
                return Nachweis.Nein("Fassung " + neu + " ist kleiner als die vorherige " + alt + "; ein Rückschritt gilt nicht als eingespieltes Update.");
            return Nachweis.Ja("Fassung " + neu + " ist eingerichtet, davor " + alt + "; das Programm startet mit der neuen Fassung neu, das Systembild führt die Fassung nicht.");
        }

        // ================================================================== Hilfen

        /// <summary>Bytes als GB mit einer Nachkommastelle, invariant (dieselbe Form wie Massnahme.VerlustMit fuer {bytes}).</summary>
        static string GbWort(long bytes)
        {
            return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        static string Wert(Dictionary<string, string> p, string name)
        {
            string v;
            return p != null && name != null && p.TryGetValue(name, out v) && v != null ? v.Trim() : null;
        }

        static bool Ganz(Dictionary<string, string> p, string name, out int wert)
        {
            return int.TryParse(Wert(p, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out wert);
        }

        /// <summary>„C“, „c“ oder „C:“ → „C“; alles andere null.</summary>
        static string Buchstabe(Dictionary<string, string> p)
        {
            string v = Wert(p, "buchstabe");
            if (string.IsNullOrEmpty(v)) return null;
            if (v.Length == 2 && v[1] == ':') v = v.Substring(0, 1);
            if (v.Length != 1) return null;
            char c = char.ToUpperInvariant(v[0]);
            return c >= 'A' && c <= 'Z' ? c.ToString() : null;
        }

        static string AdapterName(Adapter a)
        {
            string n = !string.IsNullOrEmpty(a.Name) ? a.Name : a.Beschreibung;
            return "Anschluss " + a.Index + (string.IsNullOrEmpty(n) ? "" : " („" + n + "“)");
        }

        static string ProfilName(int profil)
        {
            switch (profil)
            {
                case 1: return "Domäne";
                case 2: return "Privat";
                case 4: return "Öffentlich";
                default: return profil.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>hive + ort → Quelle im Bild (sammler/Quellen/Autostartquelle.cs, StartupApproved-Zuordnung).</summary>
        static string AutostartQuelle(string hive, string ort)
        {
            if (hive == null || ort == null) return null;
            bool hklm = string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase);
            bool hkcu = string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase);
            if (!hklm && !hkcu) return null;
            if (string.Equals(ort, "Run", StringComparison.OrdinalIgnoreCase)) return hklm ? "HKLM\\Run" : "HKCU\\Run";
            if (string.Equals(ort, "Run32", StringComparison.OrdinalIgnoreCase)) return hklm ? "HKLM\\Run32" : "HKCU\\Run32";
            if (string.Equals(ort, "StartupFolder", StringComparison.OrdinalIgnoreCase)) return hklm ? "CommonStartup" : "Startup";
            return null;
        }

        /// <summary>Aufgabe über den Pfad, mit oder ohne führenden Backslash, ohne Rücksicht auf Groß- und Kleinschreibung.</summary>
        static Aufgabe AufgabeFinden(Systembild bild, string pfad)
        {
            string gesucht = pfad.TrimStart('\\');
            foreach (Aufgabe a in bild.Autostart.Aufgaben)
                if (a.Pfad != null && string.Equals(a.Pfad.TrimStart('\\'), gesucht, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        /// <summary>
        /// Startart oder Zustand ohne Aussage: leer, „?“ (WMI ohne Wert, sammler/Quellen/Updatequelle.cs)
        /// oder „fehlt“ (Dienst nicht gefunden). Ein solcher Wert ist weder alt noch neu.
        /// </summary>
        static bool StartartUnlesbar(string s)
        {
            return string.IsNullOrWhiteSpace(s) || s.Trim() == "?" || string.Equals(s.Trim(), "fehlt", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Automatic/Auto → Auto, Manual → Manual, Disabled → Disabled; sonst null (Boot, System, Unknown).</summary>
        static string StartartMarke(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (string.Equals(s, "Automatic", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase)) return "Auto";
            if (string.Equals(s, "Manual", StringComparison.OrdinalIgnoreCase)) return "Manual";
            if (string.Equals(s, "Disabled", StringComparison.OrdinalIgnoreCase)) return "Disabled";
            return null;
        }

        static string StartartWort(string marke)
        {
            switch (marke)
            {
                case "Auto": return "Automatisch";
                case "Manual": return "Manuell";
                case "Disabled": return "Deaktiviert";
                default: return marke ?? "unbekannt";
            }
        }

        static string TageWort(int t) { return t == 1 ? "1 Tag" : t + " Tage"; }

        static string Minuten(double m)
        {
            int n = (int)Math.Round(m);
            return n <= 0 ? "unter 1 Minute" : n == 1 ? "1 Minute" : n + " Minuten";
        }

        static string Hex(int v) { return "0x" + v.ToString("X", CultureInfo.InvariantCulture); }

        static string Liste(IEnumerable<string> teile)
        {
            var l = teile.Where(x => !string.IsNullOrEmpty(x)).ToList();
            return l.Count == 0 ? "keine" : string.Join(", ", l);
        }
    }
}
