using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WartungsToolbox.Kern;
using WartungsToolbox.Kern.Regeln;
using NetzRegel = WartungsToolbox.Kern.Regeln.Netz;
using SicherheitRegel = WartungsToolbox.Kern.Regeln.Sicherheit;
using LeistungRegel = WartungsToolbox.Kern.Regeln.Leistung;
using HardwareRegel = WartungsToolbox.Kern.Regeln.Hardware;
using SpeicherRegel = WartungsToolbox.Kern.Regeln.Speicherplatz;
using UpdatesRegel = WartungsToolbox.Kern.Regeln.Updates;
using DatentraegerRegel = WartungsToolbox.Kern.Regeln.Datentraeger;

namespace WartungsToolbox.Proben
{
    /// <summary>
    /// Proben fuer den Massnahmenkatalog des Kerns (kern/Massnahmen.cs, kern/Massnahmen.Katalog.cs),
    /// docs/M3-ENTWURF.md Abschnitt 6 und 8.1:
    ///
    ///   (a) jede Kennung, die eine Regel an Befund.Massnahmen haengt, existiert im Katalog:
    ///       einmal ueber die abgelesene Konstantenliste (Stand 18.09.2026), einmal dynamisch ueber
    ///       alle Aufzeichnungen unter tests/aufzeichnungen (eine neue Kennung in einer Regel
    ///       faellt hier auf, ohne dass jemand die Liste pflegt);
    ///   (b) jeder Eintrag: Stufe 0 bis 3, Art, Texte mit Zahl/Bedingung/Absage, Nachweis-Funktion;
    ///       Beziehungen (M3-Entwurf 8.10): Stufe 2 (ausfuehren, ausser rueckweg) hat eine Sicherung,
    ///       Rueckweg "sicherung" hat eine Sicherung, und genau eine Stufe-2-Massnahme kommt ohne
    ///       Rueckweg (update.dienst.aktivieren: Rueckweg keiner, Sicherung werte-merken);
    ///   (c) Nachweise gegen gepflanzte Bilder, im Speicher zum Vorher/Nachher gedreht
    ///       (Signaturalter 0 -> bestanden, 10 -> nicht, und so weiter, mindestens 10 Nachweise),
    ///       mit den drei Ergebnisarten: Ja, Nein (Objekt gefunden, Zustand nachweislich alt) und
    ///       Unlesbar (Parameter fehlt, Objekt nicht im Bild, Wert nicht lesbar: Unbestimmt = true,
    ///       nie bestanden); je Kennung mit Unlesbar-Zweig mindestens eine Unbestimmt-Probe, und
    ///       zwar ausschliesslich ueber committete gepflanzt-*.json (die CI hat nichts anderes); die
    ///       echte Aufzeichnung dieses PCs (dieser-pc-de.json, nur lokal) laeuft als Zusatzgruppe
    ///       nach der Deckungspruefung und wird ohne Datei gemeldet uebersprungen, nie rot; die 14
    ///       Nachweise ohne messbaren Zustand (13 Ja plus wiederherstellungspunkt.zurueck) sind Nein,
    ///       sobald der Helfer nachweis.bestanden = 0 (oder einen anderen Wert als 1) meldet;
    ///   (c, TRIM) eine Deutung des Rohwerts DisableDeleteNotification (Massnahmen.TrimAn, Bit 0 =
    ///       NTFS): mit gepflanztem Rohwert 2 sagen Regel und Nachweis beide "an", mit 3 beide "aus",
    ///       und alle Texte der Kennung nennen "NTFS-Bit von DisableDeleteNotification";
    ///   (c2) die 16 Paare gepflanzt-massnahmen-&lt;kennung&gt;-vorher/-nachher.json: Regeln ueber
    ///       VORHER haengen die Kennung mit den Parametern der Tabelle (K6-Bericht 4) an, der
    ///       Nachweis ist vorher Nein und nachher Ja, und ueber NACHHER haengt keine Regel die
    ///       Kennung mit diesen Parametern mehr an; dazu ein 17. Paar im Speicher (Aufgabe mit
    ///       laufender Instanz: Zustand 4 bleibt nach schtasks /DISABLE, nur Enabled kippt);
    ///   (c, Aufgabe) der Nachweis leistung.aufgabe.deaktivieren liest Enabled (Feld Aktiv), nicht
    ///       TASK_STATE: Aktiv false ist Ja bei jedem Zustand, Aktiv true Nein bei jedem Zustand,
    ///       ohne Feld zaehlt der Zustand (1 Ja, 3 Nein, 0/2/4 Unlesbar);
    ///   (b, Texte) leistung.dienst.startart nennt im Risiko Manuell UND Deaktiviert (die Regel
    ///       uebergibt nur Manual) und in WasPassiert den Zielwert der Fachzeile; volume.scan
    ///       verspricht keine Reparatur "als eigene Massnahme" (spotfix/reparatur sind Rat bis M4);
    ///   (d) Freischaltbare() sind genau die sechs Stufe-1-Massnahmen aus M3-Entwurf 8.1;
    ///   (e) jeder Befund mit Kennung traegt die Pflichtparameter der Kennung (ueber alle Bilder,
    ///       auch ein Befund mit leerem Parameter-Woerterbuch wird geprueft).
    ///
    /// Wirksamkeitsproben 13.09.2026: Signatur-Nachweis auf "&gt;=" gedreht -> (c) rot; Freischaltbar bei
    /// netz.dhcp.erneuern gesetzt -> (d) rot; Kennung volume.scan im Katalog umbenannt -> (a) rot.
    /// 18.09.2026: Unlesbar in DhcpErneuert (Parameter fehlt) zurueck auf Nein gedreht -> (c) rot
    /// ("Unbestimmt" verlangt); Gateway-Bedingung entfernt -> Gegenprobe "Gateway fehlt" rot.
    /// 18.09.2026 (Nachbesserung): HelferFehler liefert immer null -> 81 rot; UAC ohne Standard fuer
    /// einen nicht gesetzten Wert -> 9 rot; Rueckweg bei Hin-Ja und neustart=1 wieder Nein -> 2 rot;
    /// volume.scan mit Dirty null wieder Ja -> 2 rot. Jeweils zurueckgebaut, Hash identisch.
    /// 18.09.2026 (zweite Runde, 8.10): Rueckweg von update.dienst.aktivieren zurueck auf "sicherung"
    /// gedreht -> "Stufe 2 ohne Rueckweg 'sicherung' sind genau: update.dienst.aktivieren" und
    /// "update.dienst.aktivieren: Rueckweg keiner, Sicherung werte-merken" rot (2 von 7669);
    /// zurueckgebaut, Hash identisch.
    /// 18.09.2026 (CI): Nachweise Netz, Leistung und Updates von dieser-pc-de.json auf die Paare
    /// gepflanzt-massnahmen-*-vorher/-nachher umgezogen (Adapter 8, Fremd01/Fremd02/Fremd03/OneDrive,
    /// \FremdUpdater, FremdManuell, HP-USB-Update); die drei lokalen Dateien weggelegt -> 7122 bestanden,
    /// 0 rot (davor 9 rot: dreimal "Aufzeichnung vorhanden", sechs Deckungsluecken), zurueckgelegt ->
    /// 7930 bestanden, 0 rot; verschwunden sind nur Zusicherungen ueber die lokale Datei.
    /// </summary>
    public static class MassnahmenProben
    {
        /// <summary>
        /// Abgelesen aus kern/Regeln/*.cs (grep "Massnahmen.Add" und "Massnahmen = new List"), 18.09.2026.
        /// Oeffentliche Konstanten werden direkt referenziert, private Literale stehen hier als Text.
        /// Die Sammelkarte leistung.autostart.anzahl traegt keine Kennung mehr, der Winlogon-Rat heisst
        /// leistung.winlogon.zuruecksetzen (M3-Entwurf 8.1/8.5).
        /// </summary>
        static readonly string[] KennungenAusRegeln =
        {
            // Datentraeger.cs
            "datentraeger.trim.einschalten", "volume.reparatur", "volume.spotfix", "volume.scan", "systemschutz.punkt.anlegen",
            // Hardware.cs
            HardwareRegel.MTreiberNeu, HardwareRegel.MAktivieren,
            // Leistung.cs (je Eintrag/Aufgabe/Dienst die drei Konstanten, Winlogon als Rat)
            LeistungRegel.MAutostartDeaktivieren, LeistungRegel.MAufgabeDeaktivieren, LeistungRegel.MDienstStartart,
            "leistung.winlogon.zuruecksetzen",
            // Netz.cs
            NetzRegel.MDhcpErneuern, NetzRegel.MDnsCacheLeeren, NetzRegel.MDnsZuruecksetzen, NetzRegel.MHostsPruefen,
            // Sicherheit.cs (auch Stabilitaet.cs: MassnahmeSignaturen)
            SicherheitRegel.MassnahmeSignaturen, SicherheitRegel.MassnahmeSchnellscan, SicherheitRegel.MassnahmeFirewall,
            SicherheitRegel.MassnahmeUac, SicherheitRegel.MassnahmeSmartScreen,
            // Speicherplatz.cs
            SpeicherRegel.MAufraeumen,
            // Stabilitaet.cs
            "stabilitaet.speichertest.planen",
            // Updates.cs
            UpdatesRegel.Verbergen, UpdatesRegel.EinstellungenOeffnen, UpdatesRegel.DienstAktivieren,
        };

        /// <summary>M3-Entwurf 8.1: die sechs Kaestchen der Freischaltung (volume.optimieren haengt an keiner Regel).</summary>
        static readonly string[] Freischaltbar =
        {
            "netz.dnscache.leeren", "sicherheit.signaturen.aktualisieren", "sicherheit.schnellscan",
            "sicherheit.firewall.einschalten", "systemschutz.punkt.anlegen", "volume.scan",
        };

        /// <summary>
        /// M3-Entwurf 8.10: Stufe-2-Massnahmen (Art ausfuehren, ohne rueckweg selbst), deren Rueckweg
        /// nicht "sicherung" ist. Genau eine: die Update-Dienste stehen im Helfer in NieDeaktivieren,
        /// der Rueckweg wird dort abgelehnt, der Katalog sagt das mit Rueckweg keiner. Jede weitere
        /// Kennung ohne Rueckweg muss hier eingetragen werden, sonst faellt sie in (b) auf.
        /// </summary>
        static readonly string[] Stufe2OhneRueckweg = { "update.dienst.aktivieren" };

        /// <summary>M3-Entwurf Abschnitt 1: Kennung -> Pflichtparameter, wie sie die Regeln setzen muessen.</summary>
        static readonly Dictionary<string, string[]> Pflichtparameter = new Dictionary<string, string[]>
        {
            { "netz.dhcp.erneuern", new[] { "adapter" } },
            { "netz.dns.zuruecksetzen", new[] { "adapter" } },
            { "sicherheit.firewall.einschalten", new[] { "profil" } },
            { "volume.scan", new[] { "buchstabe" } },
            { "volume.optimieren", new[] { "buchstabe" } },
            { "leistung.autostart.deaktivieren", new[] { "hive", "ort", "name" } },
            { "leistung.aufgabe.deaktivieren", new[] { "pfad" } },
            { "leistung.dienst.startart", new[] { "name", "startart" } },
            { "update.dienst.aktivieren", new[] { "name" } },
            { "update.verbergen", new[] { "updateId" } },
            { "geraet.aktivieren", new[] { "instanzId" } },
            { "rueckweg", new[] { "sicherung" } },
        };

        /// <summary>
        /// Parametertabelle der 16 Paare (docs/werkzeuge/m3-phase1-berichte.md, K6 Abschnitt 4): mit
        /// genau diesen Werten muss der Befund ueber dem Vorher-Bild die Kennung tragen.
        /// </summary>
        static readonly Dictionary<string, Dictionary<string, string>> PaarParameter = new Dictionary<string, Dictionary<string, string>>
        {
            { "sicherheit.signaturen.aktualisieren", P() },
            { "sicherheit.firewall.einschalten", P("profil", "4") },
            { "sicherheit.smartscreen.warn", P() },
            { "sicherheit.uac.standard", P() },
            { "sicherheit.schnellscan", P() },
            { "datentraeger.trim.einschalten", P() },
            { "volume.scan", P("buchstabe", "C") },
            { "systemschutz.punkt.anlegen", P() },
            { "netz.dhcp.erneuern", P("adapter", "8") },
            { "netz.dns.zuruecksetzen", P("adapter", "8") },
            { "leistung.autostart.deaktivieren", P("hive", "HKLM", "ort", "Run", "name", "Fremd01") },
            { "leistung.aufgabe.deaktivieren", P("pfad", "\\FremdUpdater") },
            { "leistung.dienst.startart", P("name", "FremdDienst3", "startart", "Manual") },
            { "update.verbergen", P("updateId", "4340b239-4b90-4305-9c2e-6a24dfcb6757") },
            { "update.dienst.aktivieren", P("name", "wuauserv") },
            { "geraet.aktivieren", P("instanzId", "PCI\\VEN_1002&DEV_13C0&SUBSYS_88771043&REV_C1\\4&1EBE6A9C&0&0041") },
        };

        /// <summary>Frage-Id des Code-22-Geraets im Paar geraet.aktivieren; ohne Antwort "reparieren" ist es eine Frage, keine Massnahme.</summary>
        const string GeraetFrage = "geraet:PCI\\VEN_1002&DEV_13C0&SUBSYS_88771043&REV_C1\\4&1EBE6A9C&0&0041:22";

        /// <summary>
        /// Kennungen, deren Nachweis einen Unlesbar-Zweig hat: fuer jede muss (c) mindestens einen
        /// Nachweis mit Unbestimmt == true und Bestanden == false liefern (M3-Entwurf 8.1).
        /// </summary>
        static readonly string[] MitUnlesbarZweig =
        {
            "netz.dhcp.erneuern", "netz.dns.zuruecksetzen",
            "sicherheit.signaturen.aktualisieren", "sicherheit.schnellscan", "sicherheit.firewall.einschalten",
            "sicherheit.smartscreen.warn", "sicherheit.uac.standard",
            "datentraeger.trim.einschalten", "volume.scan", "systemschutz.punkt.anlegen", "wiederherstellungspunkt.anlegen",
            "leistung.autostart.deaktivieren", "leistung.aufgabe.deaktivieren", "leistung.dienst.startart",
            "update.dienst.aktivieren", "update.verbergen", "geraet.aktivieren", "rueckweg",
            "zeitplan.anlegen", "zeitplan.loeschen",
        };

        static readonly SortedSet<string> UnbestimmtGesehen = new SortedSet<string>(StringComparer.Ordinal);

        public static void Laufen(Harness h)
        {
            var saetze = new List<KeyValuePair<string, string>>();   // (woher, Satz) fuer die Satzpruefung am Ende
            UnbestimmtGesehen.Clear();

            KennungenDerRegeln(h);
            Eintraege(h, saetze);
            Freischaltung(h);
            NachweiseNetz(h, saetze);
            NachweiseSicherheit(h, saetze);
            NachweiseDatentraeger(h, saetze);
            TrimRohwert(h, saetze);
            NachweiseLeistung(h, saetze);
            NachweiseUpdates(h, saetze);
            NachweiseHardware(h, saetze);
            NachweiseRueckweg(h, saetze);
            NachweiseOhneZustand(h, saetze);
            NachweisePaare(h, saetze);
            Parameterpflicht(h);

            h.Gruppe("Massnahmen (c): jede Kennung mit Unlesbar-Zweig hat eine Unbestimmt-Probe");
            h.Ist("Grundmenge: mindestens 15 Kennungen mit Unlesbar-Zweig gelistet", MitUnlesbarZweig.Length >= 15, MitUnlesbarZweig.Length.ToString());
            foreach (string k in MitUnlesbarZweig)
                h.Ist(k + ": mindestens ein Nachweis war Unbestimmt (nicht bestanden)", UnbestimmtGesehen.Contains(k));

            // Erst nach der Deckungspruefung: die echte Aufzeichnung dieses PCs liegt nur lokal, in
            // der CI fehlt sie, und die Deckung darf nicht an ihr haengen.
            NachweiseDieserPc(h, saetze);

            h.Gruppe("Massnahmen: Saetze tragen Zahl, Bedingung oder Absage");
            h.Ist("Grundmenge: mindestens 150 Saetze gesammelt", saetze.Count >= 150, saetze.Count.ToString());
            foreach (var s in saetze)
            {
                h.Ist("Satz traegt Zahl, Bedingung oder Absage: " + s.Key, Traegt(s.Value), s.Value);
                h.Ist("kein Geviert-/Halbgeviertstrich, keine geraden Anfuehrungszeichen: " + s.Key, Sauber(s.Value), s.Value);
            }
        }

        // ------------------------------------------------------------ Hilfen

        static bool Traegt(string satz)
        {
            if (string.IsNullOrWhiteSpace(satz)) return false;
            // Dieselbe Regel wie Harness.SaetzeTragen fuer Befunde: Zahl, Bedingung oder Absage.
            return satz.Any(char.IsDigit) || satz.Contains("nicht") || satz.Contains("kein") || satz.Contains("wenn")
                   || satz.Contains("falls") || satz.Contains("ohne") || satz.Contains("nie");
        }

        static bool Sauber(string text)
        {
            return text != null && text.IndexOf('—') < 0 && text.IndexOf('–') < 0 && text.IndexOf('"') < 0;
        }

        static Dictionary<string, string> P(params string[] paare)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < paare.Length; i += 2) d[paare[i]] = paare[i + 1];
            return d;
        }

        /// <summary>
        /// Nachweis ausfuehren, Satz sammeln, Erwartung pruefen. Ein bestandener Nachweis ist nie
        /// Unbestimmt (Invariante); mit "unbestimmt" wird die Ergebnisart zusaetzlich festgelegt
        /// (true = Unlesbar, false = Nein bei erwartet == false).
        /// </summary>
        static Nachweis N(Harness h, List<KeyValuePair<string, string>> saetze, string was, string kennung, Systembild bild, Dictionary<string, string> p, bool erwartet, bool? neustart = null, bool? unbestimmt = null)
        {
            Massnahme m = Massnahmen.Finde(kennung);
            if (m == null) { h.Ist(was + ": Katalogeintrag " + kennung + " vorhanden", false); return null; }
            Nachweis n = m.Pruefen(bild, p);
            if (n == null) { h.Ist(was + ": Nachweis nicht null", false); return null; }
            saetze.Add(new KeyValuePair<string, string>(kennung + " / " + was, n.Satz));
            h.Ist(was + (erwartet ? " => bestanden" : " => NICHT bestanden"), n.Bestanden == erwartet, n.Satz);
            if (n.Bestanden) h.Ist(was + ": bestanden ist nie Unbestimmt", !n.Unbestimmt);
            if (neustart.HasValue) h.Ist(was + ": Neustart-Flag " + (neustart.Value ? "gesetzt" : "nicht gesetzt"), n.Neustart == neustart.Value);
            if (unbestimmt.HasValue) h.Ist(was + (unbestimmt.Value ? ": Unbestimmt (nicht messbar)" : ": bestimmt (Nein, nicht Unlesbar)"), n.Unbestimmt == unbestimmt.Value, n.Satz);
            if (n.Unbestimmt && !n.Bestanden) UnbestimmtGesehen.Add(kennung);
            return n;
        }

        static List<Systembild> AlleBilder(Harness h)
        {
            var l = new List<Systembild>();
            foreach (string pfad in Directory.GetFiles(h.Aufzeichnungen, "*.json").OrderBy(x => x))
            {
                var s = h.Bild(Path.GetFileName(pfad));
                if (s != null) l.Add(s);
            }
            return l;
        }

        // ------------------------------------------------------------ (a) Kennungen der Regeln

        // Wirksamkeitsprobe 13.09.2026: "volume.scan" im Katalog zu "volume.scannen" umbenannt -> beide
        // Zusicherungen unten rot (Liste und dynamisch ueber gepflanzt-datentraeger-c-dirty.json).
        static void KennungenDerRegeln(Harness h)
        {
            h.Gruppe("Massnahmen (a): jede Kennung aus den Regeln existiert im Katalog");
            h.Ist("Grundmenge: mindestens 20 Kennungen abgelesen", KennungenAusRegeln.Length >= 20, KennungenAusRegeln.Length.ToString());
            h.Ist("Grundmenge: Katalog hat mindestens 35 Eintraege", Massnahmen.Alle().Count() >= 35, Massnahmen.Alle().Count().ToString());
            foreach (string k in KennungenAusRegeln)
                h.Ist("Katalog kennt " + k, Massnahmen.Finde(k) != null);
            // Die beiden alten Kennungen sind weg (M3-Entwurf 8.1): die Sammelkarte fuehrt keine
            // Kennung mehr, der Winlogon-Rat traegt den Bereich im Namen.
            h.Ist("Katalog kennt autostart.deaktivieren NICHT mehr", Massnahmen.Finde("autostart.deaktivieren") == null);
            h.Ist("Katalog kennt autostart.winlogon.zuruecksetzen NICHT mehr", Massnahmen.Finde("autostart.winlogon.zuruecksetzen") == null);
            // Die Parameternamen der Regeln sind die des Vertrags (M3-Entwurf 1), sonst findet der
            // Nachweis den Wert nicht, obwohl der Befund ihn traegt.
            h.Ist("Regeln nennen den Parameter adapter", NetzRegel.ParamAdapter == "adapter");
            h.Ist("Regeln nennen den Parameter profil", SicherheitRegel.ParamProfil == "profil");
            h.Ist("Regeln nennen den Parameter buchstabe", DatentraegerRegel.ParamBuchstabe == "buchstabe" && SpeicherRegel.ParamBuchstabe == "buchstabe");
            h.Ist("Regeln nennen die Parameter hive, ort, name, pfad, startart", LeistungRegel.ParamHive == "hive" && LeistungRegel.ParamOrt == "ort" && LeistungRegel.ParamName == "name" && LeistungRegel.ParamPfad == "pfad" && LeistungRegel.ParamStartart == "startart");
            h.Ist("Regeln nennen die Parameter updateId und name", UpdatesRegel.ParamUpdateId == "updateId" && UpdatesRegel.ParamName == "name");
            h.Ist("Regeln nennen den Parameter instanzId", HardwareRegel.ParamInstanzId == "instanzId");
            h.Ist("Finde(null) ist null", Massnahmen.Finde(null) == null);
            h.Ist("Finde(unbekannt) ist null", Massnahmen.Finde("gibt.es.nicht") == null);
            h.Ist("Finde ist schreibungsgenau (Ordinal)", Massnahmen.Finde("Volume.Scan") == null);

            // Dynamisch: alles, was die Regeln ueber die Aufzeichnungen wirklich anhaengen.
            var gesehen = new SortedSet<string>(StringComparer.Ordinal);
            var bilder = AlleBilder(h);
            h.Ist("Grundmenge: mindestens 100 Aufzeichnungen geladen", bilder.Count >= 100, bilder.Count.ToString());
            foreach (var s in bilder)
                foreach (var b in Harness.Befunde(h.Pruefen(s)))
                    foreach (string k in b.Massnahmen) gesehen.Add(k);
            h.Ist("Grundmenge: mindestens 12 verschiedene Kennungen ueber alle Bilder angehaengt (" + string.Join(", ", gesehen) + ")", gesehen.Count >= 12, gesehen.Count.ToString());
            foreach (string k in gesehen)
                h.Ist("Katalog kennt die angehaengte Kennung " + k, Massnahmen.Finde(k) != null);
            // Gegenprobe zur Liste: was die Bilder anhaengen, steht auch in der abgelesenen Liste.
            foreach (string k in gesehen)
                h.Ist("abgelesene Liste enthaelt " + k + " (sonst Liste nachziehen)", KennungenAusRegeln.Contains(k));
        }

        // ------------------------------------------------------------ (b) jeder Eintrag

        static void Eintraege(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (b): jeder Katalogeintrag ist vollstaendig");
            var alle = Massnahmen.Alle().ToList();
            var bereiche = new HashSet<string>(Kern.Bereich.Reihenfolge);
            var arten = new[] { Massnahme.ArtAusfuehren, Massnahme.ArtAnsicht, Massnahme.ArtRat };
            var sicherungen = new[] { Massnahme.SicherungKeine, Massnahme.SicherungWerte, Massnahme.SicherungPunkt, Massnahmen.SicherungRegExport };
            var rueckwege = new[] { Massnahme.RueckwegVonSelbst, Massnahme.RueckwegSicherung, Massnahme.RueckwegKeiner };
            h.Ist("Kennungen sind eindeutig", alle.Select(m => m.Kennung).Distinct().Count() == alle.Count);
            foreach (var m in alle)
            {
                string k = m.Kennung;
                h.Ist(k + ": Stufe 0 bis 3", m.Stufe >= 0 && m.Stufe <= 3, m.Stufe.ToString());
                h.Ist(k + ": Art bekannt", arten.Contains(m.Art), m.Art);
                h.Ist(k + ": Bereich ist eine Kern.Bereich-Konstante", m.Bereich != null && bereiche.Contains(m.Bereich), m.Bereich);
                h.Ist(k + ": Titel, WasPassiert, WasBleibt, Risiko nicht leer",
                      !string.IsNullOrWhiteSpace(m.Titel) && !string.IsNullOrWhiteSpace(m.WasPassiert) && !string.IsNullOrWhiteSpace(m.WasBleibt) && !string.IsNullOrWhiteSpace(m.Risiko));
                h.Ist(k + ": Titel ohne Strich und ohne gerade Anfuehrungszeichen", Sauber(m.Titel), m.Titel);
                h.Ist(k + ": Pruefen != null", m.Pruefen != null);
                h.Ist(k + ": Sicherung bekannt", sicherungen.Contains(m.Sicherung), m.Sicherung);
                h.Ist(k + ": Rueckweg bekannt", rueckwege.Contains(m.Rueckweg), m.Rueckweg);
                h.Ist(k + ": Parameter nicht null", m.Parameter != null);
                h.Ist(k + ": Dauer >= 0", m.DauerSekunden >= 0, m.DauerSekunden.ToString());
                h.Ist(k + ": Kennung nach Schema (Kleinbuchstaben, Ziffern, Punkte)", k.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.'), k);
                saetze.Add(new KeyValuePair<string, string>(k + " / WasPassiert", m.WasPassiert));
                saetze.Add(new KeyValuePair<string, string>(k + " / WasBleibt", m.WasBleibt));
                saetze.Add(new KeyValuePair<string, string>(k + " / Risiko", m.Risiko));

                // Beziehungen zwischen den Feldern (Konzept 2.4, M3-Entwurf 8.10): Stufe 2 heisst
                // "das System legt vorher eine Sicherung an" (ausser beim Rueckweg selbst), und wer
                // den Rueckweg "sicherung" verspricht, braucht eine Sicherung. Stufe 2 verlangt den
                // Rueckweg "sicherung" NICHT mehr: update.dienst.aktivieren sichert die alte
                // Startart (werte-merken, die Datei ist der Beleg), der Helfer lehnt das
                // Zurueckspielen aber ab (NieDeaktivieren), also sagt der Katalog Rueckweg keiner.
                if (m.Art == Massnahme.ArtAusfuehren && m.Stufe == 2 && k != "rueckweg")
                {
                    h.Ist(k + ": Stufe 2 hat eine Sicherung", m.Sicherung != Massnahme.SicherungKeine, m.Sicherung);
                    h.Ist(k + ": Stufe 2 hat den Rueckweg 'sicherung' oder steht in Stufe2OhneRueckweg",
                          m.Rueckweg == Massnahme.RueckwegSicherung || Stufe2OhneRueckweg.Contains(k), m.Rueckweg);
                }
                if (m.Rueckweg == Massnahme.RueckwegSicherung)
                    h.Ist(k + ": Rueckweg 'sicherung' nur mit Sicherung", m.Sicherung != Massnahme.SicherungKeine, m.Sicherung);
                if (m.Freischaltbar)
                    h.Ist(k + ": Freischaltbar nur bei Stufe 1 und Art ausfuehren", m.Stufe == 1 && m.Art == Massnahme.ArtAusfuehren);
                if (m.Art == Massnahme.ArtRat)
                    h.Ist(k + ": Rat ist Stufe 3 ohne Parameter", m.Stufe == 3 && m.Parameter.Length == 0);
                string[] pflicht;
                if (Pflichtparameter.TryGetValue(k, out pflicht))
                    h.Ist(k + ": Pflichtparameter wie M3-Entwurf 1 (" + string.Join(",", pflicht) + ")", pflicht.All(m.Parameter.Contains) && m.Parameter.All(pflicht.Contains), string.Join(",", m.Parameter));

                // Ein Nachweis ohne Parameter und mit leerem Bild darf nie werfen: die Nachmessung kann
                // ein duennes Bild liefern (Sammler ohne Rechte), der Host ruft trotzdem. Und er darf
                // dort nie "bestanden UND unbestimmt" liefern.
                bool wirft = false; Nachweis leer = null;
                try { leer = m.Pruefen(new Systembild(), new Dictionary<string, string>()); } catch (Exception) { wirft = true; }
                h.Ist(k + ": Nachweis wirft nicht bei leerem Bild und leeren Parametern", !wirft && leer != null && !string.IsNullOrWhiteSpace(leer.Satz));
                if (leer != null) h.Ist(k + ": leeres Bild ist nie bestanden und unbestimmt zugleich", !(leer.Bestanden && leer.Unbestimmt));
                // Wo der Nachweis das Bild wirklich misst (MitUnlesbarZweig), macht ein fehlender
                // Pflichtparameter ihn Unbestimmt; Nachweise ohne messbaren Zustand lesen keine Parameter.
                if (leer != null && m.Parameter.Length > 0 && MitUnlesbarZweig.Contains(k))
                    h.Ist(k + ": fehlende Pflichtparameter machen den Nachweis Unbestimmt, nicht Nein", leer.Unbestimmt && !leer.Bestanden, leer.Satz);
                try { leer = m.Pruefen(new Systembild(), null); } catch (Exception) { wirft = true; }
                h.Ist(k + ": Nachweis wirft nicht bei Parameter null", !wirft && leer != null);
                if (leer != null) saetze.Add(new KeyValuePair<string, string>(k + " / Nachweis leeres Bild", leer.Satz));
            }

            // Stufe 2 ohne Rueckweg (M3-Entwurf 8.10): genau die Liste Stufe2OhneRueckweg, nicht mehr
            // und nicht weniger. Mehr hiesse: eine Massnahme hat ihren Rueckweg still verloren;
            // weniger hiesse: der Katalog verspricht "Rueckgaengig" fuer einen Dienst, den der Helfer
            // nie wieder deaktiviert.
            var ohne = alle.Where(m => m.Art == Massnahme.ArtAusfuehren && m.Stufe == 2 && m.Kennung != "rueckweg" && m.Rueckweg != Massnahme.RueckwegSicherung)
                           .Select(m => m.Kennung).OrderBy(x => x, StringComparer.Ordinal).ToList();
            h.Ist("Grundmenge: Stufe2OhneRueckweg nennt 1 Kennung", Stufe2OhneRueckweg.Length == 1);
            h.Ist("Stufe 2 ohne Rueckweg 'sicherung' sind genau: " + string.Join(", ", Stufe2OhneRueckweg),
                  ohne.SequenceEqual(Stufe2OhneRueckweg.OrderBy(x => x, StringComparer.Ordinal)), string.Join(", ", ohne));
            var ud = Massnahmen.Finde("update.dienst.aktivieren");
            h.Ist("update.dienst.aktivieren: Rueckweg keiner, Sicherung werte-merken",
                  ud != null && ud.Rueckweg == Massnahme.RueckwegKeiner && ud.Sicherung == Massnahme.SicherungWerte,
                  ud == null ? "(fehlt)" : ud.Rueckweg + " / " + ud.Sicherung);
            h.Ist("update.dienst.aktivieren: Stufe 2, Art ausfuehren", ud != null && ud.Stufe == 2 && ud.Art == Massnahme.ArtAusfuehren);
            // Der Risiko-Text sagt es dem Nutzer (Wortlaut M3-Entwurf 8.10), und kein Text der
            // Kennung verspricht mehr ein "Rueckgaengig", das die alte Startart wiederherstellt.
            h.Ist("update.dienst.aktivieren: Risiko sagt, dass es kein „Rückgängig“ gibt",
                  ud != null && ud.Risiko.Contains("„Rückgängig“ gibt es hier nicht") && ud.Risiko.Contains("nie wieder deaktiviert") && ud.Risiko.Contains("Dienstverwaltung von Hand"),
                  ud == null ? "(fehlt)" : ud.Risiko);
            h.Ist("update.dienst.aktivieren: kein Text verspricht die alte Startart zurueck",
                  ud != null && !ud.Risiko.Contains("stellt die alte Startart wieder her") && !ud.WasPassiert.Contains("Rückgängig") && !ud.WasBleibt.Contains("Rückgängig"));

            // leistung.dienst.startart: die Regel uebergibt immer Manual (kern/Regeln/Leistung.cs
            // StartartManuell), nie Disabled. Das Risiko, der letzte Text vor "Jetzt ausfuehren", muss
            // die Folge von Manuell nennen (kein Start beim Hochfahren), nicht nur die von Deaktiviert,
            // und WasPassiert nennt den Zielwert aus der Fachzeile statt einer Dreierliste zur Auswahl.
            var ds = Massnahmen.Finde("leistung.dienst.startart");
            h.Ist("leistung.dienst.startart: Regel uebergibt Manual", LeistungRegel.StartartManuell == "Manual");
            h.Ist("leistung.dienst.startart: Risiko nennt „Manuell“ und „Deaktiviert“ und die Folge (nicht mehr von selbst)",
                  ds != null && ds.Risiko.Contains("„Manuell“") && ds.Risiko.Contains("„Deaktiviert“") && ds.Risiko.Contains("nicht mehr von selbst"),
                  ds == null ? "(fehlt)" : ds.Risiko);
            h.Ist("leistung.dienst.startart: Risiko nennt Programme, die den Dienst brauchen, und den Rueckweg unter 10 Sekunden",
                  ds != null && ds.Risiko.Contains("Bluetooth") && ds.Risiko.Contains("„Rückgängig“") && ds.Risiko.Contains("unter 10 Sekunden"),
                  ds == null ? "(fehlt)" : ds.Risiko);
            h.Ist("leistung.dienst.startart: Risiko beginnt nicht mehr nur mit Deaktiviert", ds != null && !ds.Risiko.StartsWith("Auf „Deaktiviert“ gestellt"), ds == null ? "(fehlt)" : ds.Risiko);
            h.Ist("leistung.dienst.startart: WasPassiert nennt den Zielwert aus der Fachzeile (startart=) und Manual, keine Dreierliste",
                  ds != null && ds.WasPassiert.Contains("„Fachlich“") && ds.WasPassiert.Contains("startart=") && ds.WasPassiert.Contains("(Manual)") && !ds.WasPassiert.Contains("Automatisch, Manuell oder Deaktiviert"),
                  ds == null ? "(fehlt)" : ds.WasPassiert);
            h.Ist("leistung.dienst.startart: WasPassiert nennt die Folge von Manuell (nur noch auf Anforderung)",
                  ds != null && ds.WasPassiert.Contains("nicht mehr von selbst") && ds.WasPassiert.Contains("anfordert"), ds == null ? "(fehlt)" : ds.WasPassiert);

            // volume.scan: die Reparatur danach ist bis M4 Rat (volume.spotfix, volume.reparatur: Art rat,
            // Stufe 3, kein Knopf). Das Risiko darf keine "eigene Massnahme" versprechen, die es nicht gibt.
            var vs = Massnahmen.Finde("volume.scan");
            h.Ist("volume.scan: Risiko verspricht keine Reparatur als eigene Massnahme", vs != null && !vs.Risiko.Contains("eigene Maßnahme"), vs == null ? "(fehlt)" : vs.Risiko);
            h.Ist("volume.scan: Risiko sagt, dass die Reparatur bis auf Weiteres selbst auszufuehren ist und es keinen Knopf gibt",
                  vs != null && vs.Risiko.Contains("bis auf Weiteres selbst") && vs.Risiko.Contains("keinen Knopf"), vs == null ? "(fehlt)" : vs.Risiko);
            foreach (string k in new[] { "volume.spotfix", "volume.reparatur" })
            {
                var r = Massnahmen.Finde(k);
                h.Ist(k + ": bleibt Rat (Stufe 3, Art rat), also kein Knopf nach dem Scan", r != null && r.Art == Massnahme.ArtRat && r.Stufe == 3, r == null ? "(fehlt)" : r.Art + "/" + r.Stufe);
                h.Ist(k + ": WasPassiert sagt, dass dieses Programm es bis auf Weiteres nicht ausfuehrt", r != null && r.WasPassiert.Contains("bis auf Weiteres nicht aus"), r == null ? "(fehlt)" : r.WasPassiert);
            }

            // leistung.aufgabe.deaktivieren: WasPassiert sagt, dass eine laufende Instanz nicht beendet
            // wird (der Nachweis liest Enabled, nicht den Zustand; NachweiseLeistung misst das).
            var ad = Massnahmen.Finde("leistung.aufgabe.deaktivieren");
            h.Ist("leistung.aufgabe.deaktivieren: WasPassiert nennt die laufende Instanz, die nicht beendet wird",
                  ad != null && ad.WasPassiert.Contains("wird nicht beendet") && ad.WasPassiert.Contains("startet nicht neu"), ad == null ? "(fehlt)" : ad.WasPassiert);
        }

        // ------------------------------------------------------------ (d) Freischaltung

        // Wirksamkeitsprobe 13.09.2026: Freischaltbar = true bei netz.dhcp.erneuern -> "genau die sechs" rot.
        static void Freischaltung(Harness h)
        {
            h.Gruppe("Massnahmen (d): Freischaltbare() sind genau die sechs Stufe-1-Massnahmen");
            var f = Massnahmen.Freischaltbare().ToList();
            h.Ist("Grundmenge: 6 erwartete Kennungen", Freischaltbar.Length == 6);
            h.Ist("genau 6 freischaltbare Massnahmen", f.Count == 6, string.Join(", ", f.Select(m => m.Kennung)));
            foreach (var m in f)
            {
                h.Ist(m.Kennung + " ist Stufe 1", m.Stufe == 1, m.Stufe.ToString());
                h.Ist(m.Kennung + " ist Art ausfuehren", m.Art == Massnahme.ArtAusfuehren, m.Art);
                h.Ist(m.Kennung + " steht in der Sechs-Liste aus M3-Entwurf 8.1", Freischaltbar.Contains(m.Kennung));
            }
            foreach (string k in Freischaltbar)
                h.Ist(k + " ist freischaltbar", f.Any(m => m.Kennung == k));
            foreach (var m in Massnahmen.Alle())
                if (!Freischaltbar.Contains(m.Kennung))
                    h.Ist(m.Kennung + " ist NICHT freischaltbar", !m.Freischaltbar);
            // volume.optimieren bleibt Stufe 1 und ausfuehrbar, nur nicht freischaltbar: keine Regel
            // haengt es an, das Kaestchen wuerde nie feuern (M3-Entwurf 8.1).
            var opt = Massnahmen.Finde("volume.optimieren");
            h.Ist("volume.optimieren ist Stufe 1, ausfuehren, nicht freischaltbar", opt != null && opt.Stufe == 1 && opt.Art == Massnahme.ArtAusfuehren && !opt.Freischaltbar);
            // Gegenprobe ueber die Stufe: eine Stufe-2-Massnahme darf nie in der Liste stehen.
            h.Ist("keine Stufe-2-Massnahme freischaltbar", !f.Any(m => m.Stufe >= 2));
        }

        // ------------------------------------------------------------ (c) Nachweise: Netz

        static void NachweiseNetz(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise Netz (netz.dhcp.erneuern, netz.dns.zuruecksetzen, netz.dnscache.leeren)");
            var s = h.Bild("gepflanzt-netz-apipa.json"); if (s == null) return;
            h.Ist("Grundmenge: Adapter 8 mit APIPA 169.254.34.9 (Herkunft 2), kein Gateway im Bild", s.Netz.IpAdressen.Any(ip => ip.AdapterIndex == 8 && ip.Herkunft == 2 && ip.Adresse == "169.254.34.9") && s.Netz.Gateways.Count == 0 && s.Netz.StandardrouteOnLink != true);

            var p = P("adapter", "8");
            N(h, saetze, "vorher: Notadresse auf Adapter 8", "netz.dhcp.erneuern", s, p, false, null, false);
            // Nachher: die Notadresse ist weg, eine DHCP-Adresse da, aber noch kein Router: Nein
            // (der Fall netz.gateway.fehlt waere sonst ohne Aenderung bestanden, M3-Entwurf 8.1).
            s.Netz.IpAdressen.RemoveAll(ip => ip.AdapterIndex == 8 && ip.Familie == 2);
            s.Netz.IpAdressen.Add(new IpAdresse { AdapterIndex = 8, Adresse = "192.168.178.23", Familie = 2, Herkunft = 3, Zustand = 4 });
            var n = N(h, saetze, "nachher: DHCP-Adresse 192.168.178.23, aber Gateway fehlt", "netz.dhcp.erneuern", s, p, false, null, false);
            h.Ist("Satz nennt den fehlenden Router", n != null && n.Satz.Contains("keinen Router"), n == null ? "" : n.Satz);
            // Standardroute ohne Gateway (PPPoE, Mobilfunk) zaehlt als Weg nach draussen.
            s.Netz.StandardrouteOnLink = true;
            N(h, saetze, "nachher: DHCP-Adresse mit Standardroute On-Link (kein Gateway)", "netz.dhcp.erneuern", s, p, true);
            s.Netz.StandardrouteOnLink = null;
            s.Netz.Gateways.Add("192.168.178.1");
            n = N(h, saetze, "nachher: DHCP-Adresse 192.168.178.23 und Gateway 192.168.178.1", "netz.dhcp.erneuern", s, p, true);
            h.Ist("Satz nennt die neue Adresse", n != null && n.Satz.Contains("192.168.178.23"), n == null ? "" : n.Satz);
            // Notadresse UND DHCP-Adresse nebeneinander: versorgt.
            s.Netz.IpAdressen.Add(new IpAdresse { AdapterIndex = 8, Adresse = "169.254.34.9", Familie = 2, Herkunft = 2, Zustand = 4 });
            N(h, saetze, "nachher: DHCP-Adresse neben alter Notadresse", "netz.dhcp.erneuern", s, p, true);
            // Gar keine IPv4-Adresse fuer den Anschluss: weder alt noch neu belegt.
            s.Netz.IpAdressen.RemoveAll(ip => ip.AdapterIndex == 8 && ip.Familie == 2);
            N(h, saetze, "nachher: keine IPv4-Adresse auf Adapter 8", "netz.dhcp.erneuern", s, p, false, null, true);
            N(h, saetze, "Adapter 99 nicht im Bild", "netz.dhcp.erneuern", s, P("adapter", "99"), false, null, true);
            N(h, saetze, "Parameter adapter = x", "netz.dhcp.erneuern", s, P("adapter", "x"), false, null, true);
            N(h, saetze, "Parameter adapter fehlt", "netz.dhcp.erneuern", s, P(), false, null, true);
            s.Netz.IpAdressen.Clear();
            N(h, saetze, "Adressliste im Bild leer (nicht gelesen)", "netz.dhcp.erneuern", s, p, false, null, true);

            // DNS zuruecksetzen: das Vorher-Bild des Paars (Adapter 8, Namensserver 10.0.0.53 fest
            // eingetragen), im Speicher zum Nachher gedreht. Die echte Aufzeichnung dieses PCs
            // (Adapter 10) laeuft nur noch als Zusatz in NachweiseDieserPc, wenn sie da ist.
            const string Dns = "netz.dns.zuruecksetzen";
            var d = h.Bild("gepflanzt-massnahmen-" + Dns + "-vorher.json"); if (d == null) return;
            var fest = d.Netz.Dns.FirstOrDefault(x => x.AdapterIndex == 8 && x.Familie == 2);
            h.Ist("Grundmenge: Adapter 8 im Bild, mit fest eingetragenen IPv4-Namensservern", d.Netz.Adapter.Any(a => a.Index == 8) && fest != null && fest.Statisch == true && fest.Server.Count >= 1);
            if (fest == null) return;
            var p8 = P("adapter", "8");
            n = N(h, saetze, "vorher: Namensserver fest eingetragen", Dns, d, p8, false, null, false);
            h.Ist("Satz nennt den festen Server", n != null && n.Satz.Contains(fest.Server[0]), n == null ? "" : n.Satz);
            fest.Statisch = false;
            n = N(h, saetze, "nachher: Namensserver automatisch (Statisch false)", Dns, d, p8, true);
            h.Ist("Satz sagt automatisch", n != null && n.Satz.Contains("automatisch"), n == null ? "" : n.Satz);
            fest.Statisch = null;
            N(h, saetze, "nachher: Statisch nicht lesbar (null)", Dns, d, p8, false, null, true);
            // Ein zweiter Eintrag fuer denselben Anschluss, der noch fest ist, zaehlt: Nein, nicht Ja.
            fest.Statisch = false;
            d.Netz.Dns.Add(new DnsKonfig { AdapterIndex = 8, Familie = 2, Server = new List<string> { "10.0.0.54" }, Statisch = true });
            n = N(h, saetze, "nachher: ein Eintrag automatisch, ein zweiter noch fest (10.0.0.54)", Dns, d, p8, false, null, false);
            h.Ist("Satz nennt den noch festen Server", n != null && n.Satz.Contains("10.0.0.54"), n == null ? "" : n.Satz);
            d.Netz.Dns.RemoveAll(x => x.AdapterIndex == 8);
            n = N(h, saetze, "nachher: keine DNS-Angaben fuer Adapter 8", Dns, d, p8, false, null, true);
            h.Ist("Satz nennt 0 Eintraege", n != null && n.Satz.Contains("0 Einträge"), n == null ? "" : n.Satz);
            N(h, saetze, "Adapter 99 nicht im Bild", Dns, d, P("adapter", "99"), false, null, true);
            N(h, saetze, "Parameter adapter = x", Dns, d, P("adapter", "x"), false, null, true);
            N(h, saetze, "Parameter adapter fehlt", Dns, d, P(), false, null, true);
            d.Netz.Adapter.Clear();
            N(h, saetze, "Adapterliste im Bild leer (nicht gelesen)", Dns, d, p8, false, null, true);

            // DNS-Cache: kein messbarer Zustand, Satz sagt das.
            n = N(h, saetze, "DNS-Cache geleert (kein messbarer Zustand)", "netz.dnscache.leeren", d, P(), true);
            h.Ist("Satz sagt ehrlich, dass der Nachweis am Bild nicht moeglich ist", n != null && n.Satz.Contains("nicht möglich"), n == null ? "" : n.Satz);
        }

        // ------------------------------------------------------------ (c) Nachweise: Sicherheit

        // Wirksamkeitsprobe 13.09.2026: Vergleich in SignaturenAktuell auf ">= NachweisSignaturTage"
        // gedreht -> "vorher: 10 Tage alt => NICHT bestanden" rot; zurueckgebaut.
        static void NachweiseSicherheit(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise Sicherheit (Signaturen, Schnellscan, Firewall, SmartScreen, UAC)");
            var s = h.Bild("gepflanzt-sicherheit-signaturen-alt.json"); if (s == null) return;
            h.Ist("Grundmenge: Signaturen 10 Tage, Schnellscan 2 Tage, 3 Firewall-Profile an, EnableLUA 1, SmartScreen Warn",
                  s.Sicherheit.Defender != null && s.Sicherheit.Defender.SignaturAlterTage == 10 && s.Sicherheit.Defender.SchnellscanAlterTage == 2
                  && s.Sicherheit.Firewall.Count == 3 && s.Sicherheit.EnableLua == 1 && s.Sicherheit.SmartScreen == "Warn");
            var p = P();
            const string Sig = "sicherheit.signaturen.aktualisieren";
            N(h, saetze, "vorher: Signaturen 10 Tage alt", Sig, s, p, false, null, false);
            s.Sicherheit.Defender.SignaturAlterTage = 0;
            var n = N(h, saetze, "nachher: Signaturen 0 Tage alt", Sig, s, p, true);
            h.Ist("Satz nennt 0 Tage", n != null && n.Satz.Contains("0 Tage"), n == null ? "" : n.Satz);
            s.Sicherheit.Defender.SignaturAlterTage = Schwellen.NachweisSignaturTage;
            N(h, saetze, "nachher: Signaturen genau an der Schwelle (" + Schwellen.NachweisSignaturTage + " Tag)", Sig, s, p, true);
            s.Sicherheit.Defender.SignaturAlterTage = Schwellen.NachweisSignaturTage + 1;
            N(h, saetze, "nachher: Signaturen 1 Tag ueber der Schwelle", Sig, s, p, false, null, false);
            s.Sicherheit.Defender.SignaturAlterTage = Schwellen.NieWert;
            N(h, saetze, "nachher: Signaturen nie (65535)", Sig, s, p, false, null, false);
            s.Sicherheit.Defender.SignaturAlterTage = -1;
            N(h, saetze, "nachher: Signaturalter nicht lesbar (-1)", Sig, s, p, false, null, true);

            const string Scan = "sicherheit.schnellscan";
            s.Sicherheit.Defender.SchnellscanAlterTage = 2;
            N(h, saetze, "vorher: Schnellscan 2 Tage her", Scan, s, p, false, null, false);
            s.Sicherheit.Defender.SchnellscanAlterTage = 0;
            N(h, saetze, "nachher: Schnellscan heute (0)", Scan, s, p, true);
            s.Sicherheit.Defender.Funde.Add(new Fund { Name = "Trojan:Win32/Probe", Schwere = 5, Aktiv = true, ZeitUtc = s.AufgezeichnetUtc });
            n = N(h, saetze, "nachher: Schnellscan heute mit 1 aktivem Fund", Scan, s, p, true);
            h.Ist("Satz nennt den aktiven Fund", n != null && n.Satz.Contains("1 aktive"), n == null ? "" : n.Satz);
            s.Sicherheit.Defender.SchnellscanAlterTage = Schwellen.NieWert;
            N(h, saetze, "nachher: Schnellscan nie (65535)", Scan, s, p, false, null, false);
            s.Sicherheit.Defender.SchnellscanAlterTage = -1;
            N(h, saetze, "nachher: Schnellscanalter nicht lesbar (-1)", Scan, s, p, false, null, true);
            s.Sicherheit.Defender = null;
            N(h, saetze, "nachher: Defender-Status fehlt (Signaturen)", Sig, s, p, false, null, true);
            N(h, saetze, "nachher: Defender-Status fehlt (Schnellscan)", Scan, s, p, false, null, true);

            // Firewall: das oeffentliche Profil ist aus.
            var f = h.Bild("gepflanzt-sicherheit-firewall-oeffentlich-aus.json"); if (f == null) return;
            var oeff = f.Sicherheit.Firewall.FirstOrDefault(x => x.Profil == 4);
            h.Ist("Grundmenge: Profil 4 aus, Profile 1 und 2 an", oeff != null && !oeff.An && f.Sicherheit.Firewall.Where(x => x.Profil != 4).All(x => x.An));
            if (oeff == null) return;
            const string Fw = "sicherheit.firewall.einschalten";
            n = N(h, saetze, "vorher: Profil 4 (Oeffentlich) aus", Fw, f, P("profil", "4"), false, null, false);
            h.Ist("Satz nennt das Profil „Öffentlich“", n != null && n.Satz.Contains("Öffentlich"), n == null ? "" : n.Satz);
            N(h, saetze, "Profil 2 (Privat) war schon an", Fw, f, P("profil", "2"), true);
            oeff.An = true;
            N(h, saetze, "nachher: Profil 4 an", Fw, f, P("profil", "4"), true);
            N(h, saetze, "Parameter profil = 3 (ungueltig)", Fw, f, P("profil", "3"), false, null, true);
            N(h, saetze, "Parameter profil fehlt", Fw, f, P(), false, null, true);
            f.Sicherheit.Firewall.RemoveAll(x => x.Profil == 1);
            N(h, saetze, "Profil 1 fehlt im Bild", Fw, f, P("profil", "1"), false, null, true);

            // SmartScreen und UAC (beide Werte: EnableLUA und die Nachfragestufe, M3-Entwurf 8.4).
            var u = h.Bild("gepflanzt-sicherheit-uac-aus.json"); if (u == null) return;
            h.Ist("Grundmenge: EnableLUA 0, ConsentAdmin 5, SmartScreen Warn", u.Sicherheit.EnableLua == 0 && u.Sicherheit.ConsentAdmin == 5 && u.Sicherheit.SmartScreen == "Warn");
            const string Uac = "sicherheit.uac.standard";
            N(h, saetze, "vorher: EnableLUA 0, Nachfragestufe 5", Uac, u, p, false, false, false);
            u.Sicherheit.EnableLua = 1;
            n = N(h, saetze, "nachher: EnableLUA 1, Nachfragestufe 5 (gilt nach Neustart)", Uac, u, p, true, true);
            h.Ist("Satz nennt die Nachfragestufe 5", n != null && n.Satz.Contains("Nachfragestufe auf 5"), n == null ? "" : n.Satz);
            u.Sicherheit.ConsentAdmin = 0;
            n = N(h, saetze, "nachher: EnableLUA 1, Nachfragestufe weiterhin 0", Uac, u, p, false, null, false);
            h.Ist("Satz nennt die Nachfragestufe 0", n != null && n.Satz.Contains("weiterhin auf 0"), n == null ? "" : n.Satz);
            u.Sicherheit.EnableLua = 0;
            N(h, saetze, "vorher: EnableLUA 0 und Nachfragestufe 0", Uac, u, p, false, false, false);
            // Ein Wert, der im Bild null ist, heisst "nicht gesetzt = Windows-Standard" (Sicherheitsquelle.cs:
            // der Sammler erfindet nichts), nicht "unlesbar": PC mit EnableLUA 0 und ohne
            // ConsentPromptBehaviorAdmin-Wert, Helfer setzt EnableLUA 1, Nachmessung (1, null) ist bestanden.
            u.Sicherheit.ConsentAdmin = null;
            N(h, saetze, "EnableLUA 0, Nachfragestufe nicht gesetzt: der alte Wert bleibt Nein", Uac, u, p, false, null, false);
            u.Sicherheit.EnableLua = 1;
            n = N(h, saetze, "EnableLUA 1, Nachfragestufe nicht gesetzt (Standard 5): bestanden nach Neustart", Uac, u, p, true, true);
            h.Ist("Satz nennt den Standard fuer die nicht gesetzte Nachfragestufe", n != null && n.Satz.Contains("5 (nicht gesetzt = Standard)"), n == null ? "" : n.Satz);
            u.Sicherheit.EnableLua = null;
            u.Sicherheit.ConsentAdmin = 5;
            n = N(h, saetze, "EnableLUA nicht gesetzt (Standard 1), Nachfragestufe 5: bestanden nach Neustart", Uac, u, p, true, true);
            h.Ist("Satz nennt den Standard fuer das nicht gesetzte EnableLUA", n != null && n.Satz.Contains("1 (nicht gesetzt = Standard)"), n == null ? "" : n.Satz);
            u.Sicherheit.ConsentAdmin = 0;
            N(h, saetze, "EnableLUA nicht gesetzt, Nachfragestufe 0: nachweislich alt", Uac, u, p, false, null, false);
            u.Sicherheit.ConsentAdmin = null;
            N(h, saetze, "nachher: EnableLUA und Nachfragestufe beide nicht vorhanden (M3-Entwurf 8.1)", Uac, u, p, false, null, true);
            // Steht die Quelle registry.uac in der Fehlerliste (Schluessel fehlt, Zugriff, Ausnahme), ist
            // null wirklich unlesbar, und der Standard darf nicht eingesetzt werden.
            u.Fehlerliste.Add(new Fehler { Quelle = "registry.uac", Art = Fehler.Fehlt, Text = "Probe" });
            u.Sicherheit.EnableLua = 1;
            n = N(h, saetze, "EnableLUA 1, Nachfragestufe null bei Fehler registry.uac", Uac, u, p, false, null, true);
            h.Ist("Satz nennt die Fehlerart", n != null && n.Satz.Contains("(" + Fehler.Fehlt + ")"), n == null ? "" : n.Satz);
            u.Sicherheit.EnableLua = null;
            u.Sicherheit.ConsentAdmin = 5;
            N(h, saetze, "EnableLUA null bei Fehler registry.uac, Nachfragestufe 5", Uac, u, p, false, null, true);
            u.Sicherheit.EnableLua = 0;
            N(h, saetze, "EnableLUA 0 bei Fehler registry.uac: nachweislich alt", Uac, u, p, false, null, false);
            u.Fehlerliste.RemoveAll(f => f.Text == "Probe");
            u.Sicherheit.EnableLua = 1;
            u.Sicherheit.ConsentAdmin = 5;

            const string Ss = "sicherheit.smartscreen.warn";
            N(h, saetze, "SmartScreen Warn", Ss, u, p, true);
            u.Sicherheit.SmartScreen = "Off";
            N(h, saetze, "vorher: SmartScreen Off", Ss, u, p, false, null, false);
            u.Sicherheit.SmartScreen = "Block";
            N(h, saetze, "nachher: SmartScreen Block (nicht Off)", Ss, u, p, true);
            u.Sicherheit.SmartScreen = null;
            N(h, saetze, "nachher: SmartScreen nicht lesbar", Ss, u, p, false, null, true);
            u.Sicherheit.SmartScreen = "";
            N(h, saetze, "nachher: SmartScreen leer", Ss, u, p, false, null, true);
        }

        // ------------------------------------------------------------ (c) Nachweise: Datentraeger

        static void NachweiseDatentraeger(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise Datentraeger (volume.scan, TRIM, Wiederherstellungspunkt, volume.optimieren)");
            var s = h.Bild("gepflanzt-datentraeger-c-dirty.json"); if (s == null) return;
            var c = s.Volumes.FirstOrDefault(v => v.Buchstabe == "C");
            h.Ist("Grundmenge: C dirty, 2 Punkte (41, 40), 1 SSD mit Trim true", c != null && c.Dirty == true && s.Systemschutz.Punkte != null && s.Systemschutz.Punkte.Count == 2 && s.Datentraeger.Count == 1 && s.Datentraeger[0].Trim == true);
            if (c == null) return;

            const string Scan = "volume.scan";
            N(h, saetze, "vorher: C: dirty", Scan, s, P("buchstabe", "C"), false, null, false);
            N(h, saetze, "Parameter buchstabe = c: (Kleinschreibung, Doppelpunkt)", Scan, s, P("buchstabe", "c:"), false, null, false);
            c.Dirty = false;
            N(h, saetze, "nachher: C: nicht mehr dirty, Status OK", Scan, s, P("buchstabe", "C"), true);
            c.OpStatus = new List<int> { 0xD00D };
            N(h, saetze, "nachher: Status 0xD00D bleibt", Scan, s, P("buchstabe", "C"), false, null, false);
            c.OpStatus = new List<int> { 0xD00E };
            var n = N(h, saetze, "nachher: Scan fand Fehler (0xD00E)", Scan, s, P("buchstabe", "C"), false, null, false);
            h.Ist("Satz sagt, dass eine Reparatur noetig ist", n != null && n.Satz.Contains("Reparatur"), n == null ? "" : n.Satz);
            // Status OK war bei einem Befund allein wegen Dirty == true schon vorher wahr; ohne das
            // gelesene Kennzeichen belegt er nichts (M3-Entwurf 8.1: Wert nicht lesbar ist nie Ja).
            c.OpStatus = new List<int> { 2 };
            c.Dirty = null;
            n = N(h, saetze, "nachher: Dirty nicht lesbar, Status OK", Scan, s, P("buchstabe", "C"), false, null, true);
            h.Ist("Satz nennt das ungelesene Dirty-Kennzeichen, nicht die Rechte", n != null && n.Satz.Contains("Dirty-Kennzeichen") && !n.Satz.Contains("Administratorrechte"), n == null ? "" : n.Satz);
            // Weder OperationalStatus noch Dirty: kein Wert, aus dem sich "OK" ableiten liesse.
            c.OpStatus = new List<int>();
            N(h, saetze, "nachher: OperationalStatus leer und Dirty nicht lesbar", Scan, s, P("buchstabe", "C"), false, null, true);
            c.Dirty = false;
            N(h, saetze, "nachher: OperationalStatus leer, Dirty false", Scan, s, P("buchstabe", "C"), true);
            c.Dirty = true;
            N(h, saetze, "nachher: OperationalStatus leer, Dirty true", Scan, s, P("buchstabe", "C"), false, null, false);
            c.OpStatus = new List<int> { 2 };
            c.Dirty = false;
            N(h, saetze, "Laufwerk Z: nicht im Bild", Scan, s, P("buchstabe", "Z"), false, null, true);
            N(h, saetze, "Parameter buchstabe = C:\\Windows (kein Buchstabe)", Scan, s, P("buchstabe", "C:\\Windows"), false, null, true);
            N(h, saetze, "Parameter buchstabe fehlt", Scan, s, P(), false, null, true);

            const string TrimK = "datentraeger.trim.einschalten";
            var trim = Massnahmen.Finde(TrimK);
            n = N(h, saetze, "nachher: Trim true", TrimK, s, P(), true);
            // Ein Name an allen Stellen (M3-Entwurf 8.1, zweite Runde): Vorschau, Nachweis und der
            // Helfer (Datentraeger.cs, Rueckweg.cs) sagen "NTFS-Bit von DisableDeleteNotification".
            // Der fsutil-Name DisableDeleteNotify kommt in keinem Text mehr vor: er klingt nach dem
            // ganzen Wert, gemeint ist nur Bit 0 (TrimRohwert unten).
            const string TrimName = "NTFS-Bit von DisableDeleteNotification";
            h.Ist("Ja-Satz nennt " + TrimName + " 0", n != null && n.Satz.Contains(TrimName + " 0"), n == null ? "" : n.Satz);
            h.Ist("WasPassiert nennt denselben Namen wie der Nachweis", trim != null && trim.WasPassiert.Contains(TrimName + " auf 0"), trim == null ? "" : trim.WasPassiert);
            h.Ist("Risiko nennt das NTFS-Bit fuer den Rueckweg", trim != null && trim.Risiko.Contains("NTFS-Bit"), trim == null ? "" : trim.Risiko);
            h.Ist("kein Text der Kennung nennt den fsutil-Namen DisableDeleteNotify",
                  trim != null && !trim.WasPassiert.Contains("DisableDeleteNotify") && !trim.WasBleibt.Contains("DisableDeleteNotify") && !trim.Risiko.Contains("DisableDeleteNotify"));
            s.Datentraeger[0].Trim = false;
            n = N(h, saetze, "vorher: Trim false", TrimK, s, P(), false, null, false);
            h.Ist("Nein-Satz nennt " + TrimName + " 1, nicht den fsutil-Namen", n != null && n.Satz.Contains(TrimName + " 1") && !n.Satz.Contains("DisableDeleteNotify"), n == null ? "" : n.Satz);
            s.Datentraeger[0].Trim = null;
            N(h, saetze, "Trim nicht lesbar", TrimK, s, P(), false, null, true);
            s.Datentraeger.Add(new Kern.Datentraeger { Nummer = 1, MedienTyp = 4, Trim = true });
            N(h, saetze, "ein zweiter Datentraeger mit Trim true (systemweit)", TrimK, s, P(), true);
            s.Datentraeger.RemoveAll(d => d.Nummer == 1);
            s.Datentraeger[0].Trim = true;

            // Wiederherstellungspunkt: ueber die Folgenummer (vorher, auch als sicherung.vorher) und ueber die Zeit.
            const string Pkt = "systemschutz.punkt.anlegen";
            N(h, saetze, "vorher=41, hoechste Nummer 41 (kein neuer Punkt)", Pkt, s, P("vorher", "41"), false, null, false);
            s.Systemschutz.Punkte.Add(new Punkt { Nummer = 42, Beschreibung = "Windows-Wartung", Typ = 12, ZeitUtc = s.AufgezeichnetUtc });
            n = N(h, saetze, "vorher=41, neuer Punkt 42", Pkt, s, P("vorher", "41"), true);
            h.Ist("Satz nennt die Folgenummer 42", n != null && n.Satz.Contains("42"), n == null ? "" : n.Satz);
            N(h, saetze, "sicherung.vorher=41 (Name des Helfers), neuer Punkt 42", Pkt, s, P("sicherung.vorher", "41"), true);
            N(h, saetze, "sicherung.vorher=42, hoechste Nummer 42", Pkt, s, P("sicherung.vorher", "42"), false, null, false);
            N(h, saetze, "ohne vorher: Punkt 42 zur Aufzeichnungszeit (0 Minuten alt)", Pkt, s, P(), true);
            N(h, saetze, "wiederherstellungspunkt.anlegen teilt den Nachweis", "wiederherstellungspunkt.anlegen", s, P("beschreibung", "Vor Wartung"), true);
            s.Systemschutz.Punkte.RemoveAll(x => x.Nummer == 42);
            N(h, saetze, "ohne vorher: juengster Punkt 3 Tage alt", Pkt, s, P(), false, null, false);
            var alt = s.Systemschutz.Punkte;
            s.Systemschutz.Punkte = new List<Punkt>();
            N(h, saetze, "0 Punkte", Pkt, s, P(), false, null, false);
            s.Systemschutz.Punkte = null;
            N(h, saetze, "Punkte nicht lesbar (null)", Pkt, s, P(), false, null, true);
            N(h, saetze, "wiederherstellungspunkt.anlegen: Punkte nicht lesbar (null)", "wiederherstellungspunkt.anlegen", s, P("beschreibung", "Vor Wartung"), false, null, true);
            s.Systemschutz.Punkte = alt;
            // Zeitfenster: NachweisPunktMinuten - 1 gilt, + 1 nicht.
            DateTime jetzt = Zeit.Lesen(s.AufgezeichnetUtc).Value;
            s.Systemschutz.Punkte.Add(new Punkt { Nummer = 43, ZeitUtc = Zeit.Utc(jetzt.AddMinutes(-(Schwellen.NachweisPunktMinuten - 1))) });
            N(h, saetze, "Punkt " + (Schwellen.NachweisPunktMinuten - 1) + " Minuten vor der Nachmessung", Pkt, s, P(), true);
            s.Systemschutz.Punkte.RemoveAll(x => x.Nummer == 43);
            s.Systemschutz.Punkte.Add(new Punkt { Nummer = 43, ZeitUtc = Zeit.Utc(jetzt.AddMinutes(-(Schwellen.NachweisPunktMinuten + 1))) });
            N(h, saetze, "Punkt " + (Schwellen.NachweisPunktMinuten + 1) + " Minuten vor der Nachmessung", Pkt, s, P(), false, null, false);
            s.Systemschutz.Punkte.RemoveAll(x => x.Nummer == 43);
            s.Systemschutz.Punkte.Add(new Punkt { Nummer = 44, ZeitUtc = "kein Datum" });
            N(h, saetze, "juengster Punkt mit unlesbarer Zeit", Pkt, s, P(), false, null, true);
            s.Systemschutz.Punkte.RemoveAll(x => x.Nummer == 44);
            // Texte der Punkt-Kennungen ohne die 24-Stunden-Begruendung (der Helfer hebt die Drossel auf).
            foreach (string k in new[] { Pkt, "wiederherstellungspunkt.anlegen" })
            {
                var m = Massnahmen.Finde(k);
                h.Ist(k + ": Risiko nennt keine 24-Stunden-Sperre mehr", m != null && !m.Risiko.Contains("24 Stunden"), m == null ? "" : m.Risiko);
                h.Ist(k + ": WasPassiert nennt die dauerhaft aufgehobene 24-Stunden-Sperre", m != null && m.WasPassiert.Contains("24-Stunden-Sperre") && m.WasPassiert.Contains("dauerhaft"), m == null ? "" : m.WasPassiert);
            }
            h.Ist("Nachweis-Saetze der Punkt-Kennung ohne 24-Stunden-Begruendung", saetze.Where(x => x.Key.StartsWith(Pkt + " /")).All(x => !x.Value.Contains("24 Stunden")));

            n = N(h, saetze, "volume.optimieren: kein messbarer Zustand", "volume.optimieren", s, P("buchstabe", "C"), true);
            h.Ist("Satz nennt das Laufwerk und die Grenze des Bildes", n != null && n.Satz.Contains("Laufwerk C:") && n.Satz.Contains("nicht möglich"), n == null ? "" : n.Satz);
        }

        // ------------------------------------------------------------ (c) TRIM: eine Deutung des Rohwerts

        // Control\FileSystem\DisableDeleteNotification traegt zwei Bits (0 NTFS, 1 ReFS), und fsutil
        // ohne Dateisystem setzt nur Bit 0. Sammler (Feld Trim), Helfer (NtfsBit) und Nachweis lesen
        // deshalb alle nur Bit 0; der Kern haelt die Deutung als Massnahmen.TrimAn. Der Rohwert 2
        // (nur das ReFS-Bit) ist der Fall, in dem "ganzer Wert == 0" und "Bit 0 == 0" auseinander-
        // laufen: sieht die Regel ihn als "aus" und der Helfer als "schon so", haengt die Regel eine
        // Massnahme an, die nichts aendert, und der Befund kommt bei jeder Messung wieder.
        // Wirksamkeitsprobe 18.09.2026: TrimAn auf "ganzer Wert == 0" gedreht -> 6 rot ("Rohwert 2
        // (nur ReFS-Bit) = fuer NTFS an", "Rohwert 4294967294", und die vier Rohwert-2-Zusicherungen
        // ueber Feld, Befund, Nachweis und Satz); zurueckgebaut, Hash identisch.
        static void TrimRohwert(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): TRIM, eine Deutung des Rohwerts DisableDeleteNotification (Bit 0 = NTFS)");
            h.Ist("Rohwert fehlt (null) = Voreinstellung 0 = an", Massnahmen.TrimAn(null));
            h.Ist("Rohwert 0 = an", Massnahmen.TrimAn(0));
            h.Ist("Rohwert 1 (NTFS-Bit) = aus", !Massnahmen.TrimAn(1));
            h.Ist("Rohwert 2 (nur ReFS-Bit) = fuer NTFS an", Massnahmen.TrimAn(2));
            h.Ist("Rohwert 3 (beide Bits) = aus", !Massnahmen.TrimAn(3));
            h.Ist("Rohwert 4294967294 (volles DWORD ohne Bit 0) = an", Massnahmen.TrimAn(4294967294L));
            h.Ist("Rohwert 4294967295 (volles DWORD) = aus", !Massnahmen.TrimAn(4294967295L));

            // Gepflanzt: die SSD aus dem Vorher-Bild (Rohwert 1) mit dem Rohwert 2 und 3, jeweils so,
            // wie der Sammler das Feld Trim daraus bildet. Regel und Nachweis muessen bei 2 beide
            // "an" sagen (kein Befund, Nachweis Ja) und bei 3 beide "aus".
            const string TrimK = "datentraeger.trim.einschalten";
            const string Schluessel = "datentraeger.0.trim";
            var s = h.Bild("gepflanzt-massnahmen-datentraeger.trim.einschalten-vorher.json"); if (s == null) return;
            var d0 = s.Datentraeger.FirstOrDefault();
            h.Ist("Grundmenge: 1 SSD mit Trim false (Rohwert 1), Geraet nicht abgefragt", d0 != null && s.Datentraeger.Count == 1 && d0.IstSsd && d0.Trim == false && !d0.TrimGeraet.HasValue);
            if (d0 == null) return;
            Func<List<Befund>> mitMassnahme = () => Harness.Befunde(h.Pruefen(s)).Where(b => b.Massnahmen.Contains(TrimK)).ToList();
            Func<Befund> trimBefund = () => Harness.Befunde(h.Pruefen(s)).FirstOrDefault(b => b.Schluessel == Schluessel);

            var b1 = trimBefund();
            h.Ist("Rohwert 1: Befund " + Schluessel + " (warn) mit " + TrimK, b1 != null && b1.Zustand == Zustand.Warn && b1.Massnahmen.Contains(TrimK) && mitMassnahme().Count == 1);
            N(h, saetze, "Rohwert 1: Nachweis Nein", TrimK, s, P(), false, null, false);

            d0.Trim = Massnahmen.TrimAn(2);
            h.Ist("Rohwert 2: das Feld Trim ist true", d0.Trim == true);
            h.Ist("Rohwert 2: kein Befund " + Schluessel + ", keine Massnahme " + TrimK, trimBefund() == null && mitMassnahme().Count == 0, string.Join("; ", mitMassnahme().Select(b => b.Schluessel)));
            var n = N(h, saetze, "Rohwert 2: Nachweis Ja (NTFS-Bit 0)", TrimK, s, P(), true);
            h.Ist("Rohwert 2: der Satz nennt das NTFS-Bit 0", n != null && n.Satz.Contains("NTFS-Bit von DisableDeleteNotification 0"), n == null ? "" : n.Satz);

            d0.Trim = Massnahmen.TrimAn(3);
            h.Ist("Rohwert 3: das Feld Trim ist false", d0.Trim == false);
            var b3 = trimBefund();
            h.Ist("Rohwert 3: Befund " + Schluessel + " mit " + TrimK, b3 != null && b3.Massnahmen.Contains(TrimK) && mitMassnahme().Count == 1);
            N(h, saetze, "Rohwert 3: Nachweis Nein", TrimK, s, P(), false, null, false);

            d0.Trim = Massnahmen.TrimAn(null);
            h.Ist("Rohwert fehlt: kein Befund, Nachweis Ja", trimBefund() == null && Massnahmen.Finde(TrimK).Pruefen(s, P()).Bestanden);
        }

        // ------------------------------------------------------------ (c) Nachweise: Leistung

        // Das Nachher-Bild des Autostart-Paars traegt alle vier Zustaende, die der Nachweis liest:
        // Fremd01 (HKLM\Run) deaktiviert, Fremd02 aktiv, Fremd03 (HKLM\Run32) ohne StartupApproved-
        // Wert, OneDrive (HKCU\Run) deaktiviert, dazu die Aufgabe \FremdUpdater (bereit, ohne Feld
        // aktiv), die Dienste FremdManuell (Manual) und FremdDienst1 (Auto). Die Aufgabenliste ist
        // dort ohne Rechte gelesen (AufgabenVollstaendig false); wo der Nachweis das unterscheidet,
        // dreht die Probe das Kennzeichen im Speicher. Die echte Aufzeichnung dieses PCs laeuft nur
        // noch als Zusatz in NachweiseDieserPc.
        static void NachweiseLeistung(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise Leistung (Autostart, Aufgabe, Dienst-Startart, Zeitplan)");
            var s = h.Bild("gepflanzt-massnahmen-leistung.autostart.deaktivieren-nachher.json"); if (s == null) return;
            var aus = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKLM\\Run" && e.Name == "Fremd01");
            var an = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKLM\\Run" && e.Name == "Fremd02");
            var hkcu = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKCU\\Run" && e.Name == "OneDrive");
            var run32 = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKLM\\Run32");
            h.Ist("Grundmenge: Fremd01 deaktiviert, Fremd02 aktiv, OneDrive (HKCU) deaktiviert, ein Run32-Eintrag ohne Status, Aufgabenliste unvollstaendig, >= 5 Dienste",
                  aus != null && aus.Aktiviert == false && an != null && an.Aktiviert == true && hkcu != null && hkcu.Aktiviert == false && run32 != null && !run32.Aktiviert.HasValue
                  && !s.Autostart.AufgabenVollstaendig && s.Autostart.Dienste.Count >= 5);
            if (aus == null || an == null || hkcu == null || run32 == null) return;

            const string Auto = "leistung.autostart.deaktivieren";
            var n = N(h, saetze, "nachher: HKLM/Run Fremd01 deaktiviert", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "Fremd01"), true);
            h.Ist("Satz nennt den Eintrag und den Ort", n != null && n.Satz.Contains("Fremd01") && n.Satz.Contains("HKLM\\Run"), n == null ? "" : n.Satz);
            N(h, saetze, "Name ohne Ruecksicht auf Gross-/Kleinschreibung", Auto, s, P("hive", "hklm", "ort", "run", "name", "fremd01"), true);
            N(h, saetze, "vorher: HKLM/Run Fremd02 aktiv", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "Fremd02"), false, null, false);
            an.Aktiviert = false;
            N(h, saetze, "nachher: Fremd02 deaktiviert", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "Fremd02"), true);
            an.Aktiviert = null;
            N(h, saetze, "Fremd02 ohne StartupApproved-Status (gilt als aktiv, Schalter nicht geschrieben)", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "Fremd02"), false, null, false);
            N(h, saetze, "nachher: HKCU/Run OneDrive deaktiviert", Auto, s, P("hive", "HKCU", "ort", "Run", "name", "OneDrive"), true);
            n = N(h, saetze, "HKLM/Run32 " + run32.Name + " ohne StartupApproved-Status (gilt als aktiv)", Auto, s, P("hive", "HKLM", "ort", "Run32", "name", run32.Name), false, null, false);
            h.Ist("Satz sagt, dass der Schalter nicht geschrieben wurde", n != null && n.Satz.Contains("nicht geschrieben"), n == null ? "" : n.Satz);
            // Unlesbar: Eintrag nicht im Bild (falscher Ort, unbekannter Name) oder Parameter ohne Sinn.
            N(h, saetze, "Eintrag am falschen Ort gesucht (HKCU statt HKLM)", Auto, s, P("hive", "HKCU", "ort", "Run", "name", "Fremd01"), false, null, true);
            N(h, saetze, "unbekannter Name", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "GibtEsNicht"), false, null, true);
            N(h, saetze, "ort = Startup (nicht StartupFolder)", Auto, s, P("hive", "HKCU", "ort", "Startup", "name", "x"), false, null, true);
            N(h, saetze, "hive = HKU (ungueltig)", Auto, s, P("hive", "HKU", "ort", "Run", "name", "x"), false, null, true);
            N(h, saetze, "name fehlt", Auto, s, P("hive", "HKLM", "ort", "Run"), false, null, true);
            s.Autostart.Eintraege.Clear();
            N(h, saetze, "Eintragsliste im Bild leer (nicht gelesen)", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "Fremd01"), false, null, true);
            // StartupFolder: HKCU -> "Startup", HKLM -> "CommonStartup".
            s.Autostart.Eintraege.Add(new AutostartEintrag { Quelle = "Startup", Name = "probe.lnk", Aktiviert = false });
            s.Autostart.Eintraege.Add(new AutostartEintrag { Quelle = "CommonStartup", Name = "alle.lnk", Aktiviert = true });
            N(h, saetze, "HKCU/StartupFolder probe.lnk deaktiviert", Auto, s, P("hive", "HKCU", "ort", "StartupFolder", "name", "probe.lnk"), true);
            N(h, saetze, "HKLM/StartupFolder alle.lnk aktiv", Auto, s, P("hive", "HKLM", "ort", "StartupFolder", "name", "alle.lnk"), false, null, false);

            // Aufgabe deaktivieren. Der Schalter ist Enabled (Feld Aktiv), nicht TASK_STATE: eine
            // deaktivierte Aufgabe mit noch laufender Instanz meldet Zustand 4, nicht 1 (Microsoft-Doku
            // TASK_STATE_DISABLED = "no instances queued or running"). Erst ohne das Feld (Aktiv null,
            // Bild einer aelteren Fassung) zaehlt der Zustand allein.
            const string Aufg = "leistung.aufgabe.deaktivieren", Pfad = "\\FremdUpdater";
            var hid = s.Autostart.Aufgaben.FirstOrDefault(a => a.Pfad == Pfad);
            h.Ist("Grundmenge: Aufgabe " + Pfad + " bereit (3), ohne Feld aktiv", hid != null && hid.Zustand == 3 && !hid.Aktiv.HasValue);
            if (hid == null) return;
            // Die Liste gilt fuer diese Faelle als vollstaendig gelesen; der Unterschied wird unten gemessen.
            s.Autostart.AufgabenVollstaendig = true;
            N(h, saetze, "vorher: " + Pfad + " bereit (3), ohne Enabled im Bild", Aufg, s, P("pfad", Pfad), false, null, false);
            hid.Zustand = 1;
            n = N(h, saetze, "nachher: " + Pfad + " deaktiviert (1), ohne Enabled im Bild", Aufg, s, P("pfad", Pfad), true);
            h.Ist("Satz sagt, dass das Bild Enabled nicht fuehrt", n != null && n.Satz.Contains("Enabled"), n == null ? "" : n.Satz);
            N(h, saetze, "Pfad ohne fuehrenden Backslash", Aufg, s, P("pfad", "FremdUpdater"), true);
            N(h, saetze, "Pfad in anderer Schreibung", Aufg, s, P("pfad", "\\fremdupdater"), true);
            hid.Zustand = 0;
            N(h, saetze, "Zustand 0 (unbekannt), ohne Enabled", Aufg, s, P("pfad", Pfad), false, null, true);
            // Ohne Enabled belegt Zustand 4 weder alt noch neu (deaktiviert mit laufender Instanz sieht
            // genauso aus): Unlesbar, nie Nein. Dasselbe fuer 2 (Warteschlange).
            hid.Zustand = 4;
            n = N(h, saetze, "Zustand 4 (laeuft), ohne Enabled: nicht belegbar", Aufg, s, P("pfad", Pfad), false, null, true);
            h.Ist("Satz nennt die laufende Instanz und das fehlende Enabled", n != null && n.Satz.Contains("läuft noch") && n.Satz.Contains("Enabled"), n == null ? "" : n.Satz);
            hid.Zustand = 2;
            N(h, saetze, "Zustand 2 (Warteschlange), ohne Enabled: nicht belegbar", Aufg, s, P("pfad", Pfad), false, null, true);
            // Mit Enabled entscheidet Enabled: false ist Ja bei jedem Zustand, true ist Nein bei jedem Zustand.
            hid.Aktiv = false;
            hid.Zustand = 4;
            n = N(h, saetze, "nachher: Enabled false, Instanz laeuft noch (Zustand 4)", Aufg, s, P("pfad", Pfad), true);
            h.Ist("Satz nennt Enabled = false und die Instanz, die von selbst endet", n != null && n.Satz.Contains("Enabled = false") && n.Satz.Contains("endet von selbst"), n == null ? "" : n.Satz);
            hid.Zustand = 2;
            N(h, saetze, "nachher: Enabled false, Instanz in der Warteschlange (Zustand 2)", Aufg, s, P("pfad", Pfad), true);
            hid.Zustand = 1;
            n = N(h, saetze, "nachher: Enabled false, Zustand 1", Aufg, s, P("pfad", Pfad), true);
            h.Ist("Satz nennt Enabled = false und Zustand 1", n != null && n.Satz.Contains("Enabled = false") && n.Satz.Contains("Zustand 1"), n == null ? "" : n.Satz);
            hid.Zustand = 0;
            N(h, saetze, "nachher: Enabled false, Zustand 0 (Enabled zaehlt, nicht der Zustand)", Aufg, s, P("pfad", Pfad), true);
            hid.Aktiv = true;
            hid.Zustand = 3;
            n = N(h, saetze, "vorher: Enabled true, bereit (3)", Aufg, s, P("pfad", Pfad), false, null, false);
            h.Ist("Satz nennt Enabled = true", n != null && n.Satz.Contains("Enabled = true"), n == null ? "" : n.Satz);
            hid.Zustand = 4;
            N(h, saetze, "vorher: Enabled true, laeuft (4): nachweislich alt, nicht Unlesbar", Aufg, s, P("pfad", Pfad), false, null, false);
            hid.Zustand = 1;
            N(h, saetze, "Enabled true, Zustand 1 (Widerspruch): Enabled zaehlt, Nein", Aufg, s, P("pfad", Pfad), false, null, false);
            hid.Aktiv = null;
            hid.Zustand = 1;
            n = N(h, saetze, "unbekannte Aufgabe", Aufg, s, P("pfad", "\\GibtEsNicht"), false, null, true);
            h.Ist("Satz nennt bei vollstaendiger Liste keine Luecke", n != null && !n.Satz.Contains("nicht vollständig"), n == null ? "" : n.Satz);
            s.Autostart.AufgabenVollstaendig = false;
            n = N(h, saetze, "unbekannte Aufgabe bei unvollstaendiger Liste (wie im Bild aufgezeichnet)", Aufg, s, P("pfad", "\\GibtEsNicht"), false, null, true);
            h.Ist("Satz nennt die unvollstaendige Liste", n != null && n.Satz.Contains("nicht vollständig"), n == null ? "" : n.Satz);
            s.Autostart.AufgabenVollstaendig = true;
            N(h, saetze, "pfad fehlt", Aufg, s, P(), false, null, true);

            // Dienst-Startart.
            const string St = "leistung.dienst.startart";
            var man = s.Autostart.Dienste.FirstOrDefault(d => d.Name == "FremdManuell");
            var amd = s.Autostart.Dienste.FirstOrDefault(d => d.Startart == "Auto");
            h.Ist("Grundmenge: Dienst FremdManuell Manual, ein Dienst mit Startart Auto", man != null && man.Startart == "Manual" && amd != null);
            if (man == null || amd == null) return;
            N(h, saetze, "FremdManuell: Ziel Manual, ist Manual", St, s, P("name", "FremdManuell", "startart", "Manual"), true);
            N(h, saetze, "FremdManuell: Ziel Disabled, ist Manual", St, s, P("name", "FremdManuell", "startart", "Disabled"), false, null, false);
            man.Startart = "Disabled";
            N(h, saetze, "nachher: FremdManuell Disabled", St, s, P("name", "fremdmanuell", "startart", "Disabled"), true);
            man.Startart = "";
            N(h, saetze, "FremdManuell: Startart im Bild leer", St, s, P("name", "FremdManuell", "startart", "Disabled"), false, null, true);
            man.Startart = "?";
            N(h, saetze, "FremdManuell: Startart im Bild ?", St, s, P("name", "FremdManuell", "startart", "Disabled"), false, null, true);
            man.Startart = "fehlt";
            N(h, saetze, "FremdManuell: Startart im Bild fehlt (Marke des Sammlers)", St, s, P("name", "FremdManuell", "startart", "Disabled"), false, null, true);
            man.Startart = "Disabled";
            N(h, saetze, amd.Name + ": Ziel Automatic, ist Auto (Marke des Bildes)", St, s, P("name", amd.Name, "startart", "Automatic"), true);
            N(h, saetze, "Ziel Boot (ungueltig)", St, s, P("name", "FremdManuell", "startart", "Boot"), false, null, true);
            N(h, saetze, "unbekannter Dienst", St, s, P("name", "GibtEsNicht", "startart", "Manual"), false, null, true);
            N(h, saetze, "name fehlt", St, s, P("startart", "Manual"), false, null, true);

            // Zeitplan anlegen/loeschen ueber die eigene Aufgabe. Das Bild fuehrt nur die Selbststart-
            // Aufgabe (\WindowsWartung-Autostart, eigen); die Wartungsaufgabe kommt im Speicher dazu,
            // damit der Nachweis sie nicht mit der Selbststart-Aufgabe verwechselt.
            const string Wartung = "\\WindowsWartung-AutoWartung";
            var pz = P("modus", "daily", "stunde", "3", "minute", "0");
            h.Ist("Grundmenge: eigene Selbststart-Aufgabe im Bild, Wartungsaufgabe " + Wartung + " nicht", s.Autostart.Aufgaben.Any(a => a.Eigen) && !s.Autostart.Aufgaben.Any(a => a.Pfad == Wartung));
            n = N(h, saetze, "zeitplan.anlegen: Aufgabe fehlt (nur die Selbststart-Aufgabe ist da)", "zeitplan.anlegen", s, pz, false, null, false);
            h.Ist("Satz sagt, dass die Liste vollstaendig gelesen war", n != null && n.Satz.Contains("vollständig gelesen"), n == null ? "" : n.Satz);
            N(h, saetze, "zeitplan.loeschen: Aufgabe weg", "zeitplan.loeschen", s, P(), true);
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = Wartung, Eigen = true, Zustand = 3, Autor = "RECHNER\\NUTZER" });
            n = N(h, saetze, "zeitplan.anlegen: Aufgabe vorhanden", "zeitplan.anlegen", s, pz, true);
            h.Ist("Satz nennt den Zustand 3", n != null && n.Satz.Contains("Zustand 3"), n == null ? "" : n.Satz);
            N(h, saetze, "zeitplan.loeschen: Aufgabe noch vorhanden", "zeitplan.loeschen", s, P(), false, null, false);
            s.Autostart.Aufgaben.RemoveAll(a => a.Pfad == Wartung);
            s.Autostart.AufgabenVollstaendig = false;
            n = N(h, saetze, "zeitplan.loeschen: Aufgabe weg, Liste unvollstaendig (belegt nichts)", "zeitplan.loeschen", s, P(), false, null, true);
            h.Ist("Satz sagt, dass die unvollstaendige Liste kein Loeschen belegt", n != null && n.Satz.Contains("nicht vollständig"), n == null ? "" : n.Satz);
            N(h, saetze, "zeitplan.anlegen: Aufgabe fehlt, Liste unvollstaendig", "zeitplan.anlegen", s, pz, false, null, true);
            // Ist die Aufgabe trotz Luecke im Bild, zaehlt sie: Ja fuer anlegen, Nein fuer loeschen.
            s.Autostart.Aufgaben.Add(new Aufgabe { Pfad = Wartung, Eigen = true, Zustand = 3 });
            N(h, saetze, "zeitplan.anlegen: Aufgabe vorhanden, Liste unvollstaendig", "zeitplan.anlegen", s, pz, true);
            N(h, saetze, "zeitplan.loeschen: Aufgabe noch vorhanden, Liste unvollstaendig", "zeitplan.loeschen", s, P(), false, null, false);
            s.Autostart.Aufgaben.RemoveAll(a => a.Pfad == Wartung);
        }

        // ------------------------------------------------------------ (c) Nachweise: Updates

        // Wirksamkeitsprobe 18.09.2026: strenge Lesart (nur Auto/Running) im Kern auf "nicht Disabled"
        // gelockert -> "Manual/Running => NICHT bestanden" rot; zurueckgebaut.
        static void NachweiseUpdates(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise Updates (update.dienst.aktivieren, update.verbergen)");
            var s = h.Bild("gepflanzt-updates-dienst-aus.json"); if (s == null) return;
            string wu;
            h.Ist("Grundmenge: wuauserv Disabled/Stopped in der Dienstetabelle", s.WindowsUpdate.Dienste.TryGetValue("wuauserv", out wu) && wu == "Disabled/Stopped");

            const string D = "update.dienst.aktivieren";
            N(h, saetze, "vorher: wuauserv Disabled/Stopped", D, s, P("name", "wuauserv"), false, null, false);
            s.WindowsUpdate.Dienste["wuauserv"] = "Auto/Running";
            var n = N(h, saetze, "nachher: wuauserv Auto/Running", D, s, P("name", "wuauserv"), true);
            h.Ist("Satz sagt automatisch", n != null && n.Satz.Contains("automatisch"), n == null ? "" : n.Satz);
            s.WindowsUpdate.Dienste["wuauserv"] = "Auto/Stopped";
            N(h, saetze, "nachher: wuauserv Auto, aber nicht gestartet", D, s, P("name", "wuauserv"), false, null, false);
            s.WindowsUpdate.Dienste["wuauserv"] = "Manual/Running";
            n = N(h, saetze, "nachher: wuauserv Manual/Running (Startart Manuell statt Automatisch)", D, s, P("name", "wuauserv"), false, null, false);
            h.Ist("Satz nennt Manuell statt Automatisch", n != null && n.Satz.Contains("Manuell") && n.Satz.Contains("Automatisch"), n == null ? "" : n.Satz);
            s.WindowsUpdate.Dienste["wuauserv"] = "Auto/Running";
            N(h, saetze, "Name in anderer Schreibung (WUAUSERV)", D, s, P("name", "WUAUSERV"), true);
            N(h, saetze, "bits Auto/Running", D, s, P("name", "bits"), true);
            N(h, saetze, "msiserver Manual/Stopped", D, s, P("name", "msiserver"), false, null, false);
            // Unlesbare Marken des Sammlers: "fehlt" (Dienst nicht gefunden), "?" (WMI ohne Wert), leer.
            s.WindowsUpdate.Dienste["wuauserv"] = "fehlt";
            N(h, saetze, "wuauserv mit Marke fehlt", D, s, P("name", "wuauserv"), false, null, true);
            s.WindowsUpdate.Dienste["wuauserv"] = "?/?";
            N(h, saetze, "wuauserv mit Marke ?/?", D, s, P("name", "wuauserv"), false, null, true);
            s.WindowsUpdate.Dienste["wuauserv"] = "Auto/?";
            N(h, saetze, "wuauserv Auto, Zustand ?", D, s, P("name", "wuauserv"), false, null, true);
            s.WindowsUpdate.Dienste["wuauserv"] = "Auto/Running";
            // Zweite Quelle: die Diensteliste, wenn die Tabelle den Namen nicht kennt.
            s.Autostart.Dienste.Add(new Dienst { Name = "trustedinstaller", Startart = "Manual", Zustand = "Running" });
            s.WindowsUpdate.Dienste.Remove("trustedinstaller");
            N(h, saetze, "trustedinstaller nur in der Diensteliste (Manual/Running): nicht Automatisch", D, s, P("name", "trustedinstaller"), false, null, false);
            s.Autostart.Dienste.First(x => x.Name == "trustedinstaller").Startart = "Auto";
            N(h, saetze, "trustedinstaller nur in der Diensteliste (Auto/Running)", D, s, P("name", "trustedinstaller"), true);
            N(h, saetze, "unbekannter Dienst", D, s, P("name", "gibtesnicht"), false, null, true);
            N(h, saetze, "name fehlt", D, s, P(), false, null, true);

            // Verbergen: das Nachher-Bild des Paars traegt genau das verborgene HP-USB-Update (die
            // Kennung aus der Uebergabe, dieselbe wie auf diesem PC). Die echte Aufzeichnung laeuft
            // nur noch als Zusatz in NachweiseDieserPc.
            const string Vb = "update.verbergen", HpId = "4340b239-4b90-4305-9c2e-6a24dfcb6757";
            var v = h.Bild("gepflanzt-massnahmen-" + Vb + "-nachher.json"); if (v == null) return;
            h.Ist("Grundmenge: genau 1 verborgenes Update mit Kennung 4340b239-... und Titel", v.WindowsUpdate.Verborgen.Count == 1 && v.WindowsUpdate.Verborgen[0].UpdateId == HpId && !string.IsNullOrEmpty(v.WindowsUpdate.Verborgen[0].Titel));
            n = N(h, saetze, "nachher: Kennung in der Liste der verborgenen Updates", Vb, v, P("updateId", HpId), true);
            h.Ist("Satz nennt den Titel des Updates", n != null && n.Satz.Contains("Hewlett-Packard"), n == null ? "" : n.Satz);
            N(h, saetze, "Kennung in Grossschreibung", Vb, v, P("updateId", HpId.ToUpperInvariant()), true);
            N(h, saetze, "vorher: andere Kennung nicht verborgen", Vb, v, P("updateId", "00000000-0000-0000-0000-000000000001"), false, null, false);
            // Ohne Titel im Bild nennt der Satz die Kennung.
            v.WindowsUpdate.Verborgen[0].Titel = null;
            n = N(h, saetze, "nachher: verborgen, Titel im Bild leer", Vb, v, P("updateId", HpId), true);
            h.Ist("Satz nennt die Kennung statt des Titels", n != null && n.Satz.Contains(HpId), n == null ? "" : n.Satz);
            v.WindowsUpdate.Verborgen.Clear();
            n = N(h, saetze, "nachher: Liste leer", Vb, v, P("updateId", HpId), false, null, false);
            h.Ist("Satz nennt 0 Eintraege", n != null && n.Satz.Contains("0 Einträge"), n == null ? "" : n.Satz);
            v.Fehlerliste.Add(new Fehler { Quelle = "com.windowsupdate.suche.verborgen", Art = Fehler.Zeit, Text = "Probe" });
            n = N(h, saetze, "nachher: Liste leer, weil die Suche im Bild als Fehler vermerkt ist", Vb, v, P("updateId", HpId), false, null, true);
            h.Ist("Satz nennt die Fehlerart der Suche", n != null && n.Satz.Contains("(" + Fehler.Zeit + ")"), n == null ? "" : n.Satz);
            v.Fehlerliste.RemoveAll(f => f.Text == "Probe");
            N(h, saetze, "updateId fehlt", Vb, v, P(), false, null, true);
        }

        // ------------------------------------------------------------ (c) Nachweise: Hardware

        static void NachweiseHardware(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise Hardware (geraet.aktivieren)");
            var s = h.Bild("gepflanzt-hardware-code22.json"); if (s == null) return;
            var g = s.Geraete.FirstOrDefault(x => x.ProblemCode == 22);
            h.Ist("Grundmenge: genau 1 Geraet mit Code 22", g != null && s.Geraete.Count(x => x.ProblemCode == 22) == 1);
            if (g == null) return;
            const string K = "geraet.aktivieren";
            var p = P("instanzId", g.InstanzId);
            N(h, saetze, "vorher: Code 22", K, s, p, false, false, false);
            N(h, saetze, "vorher: Code 22, Windows verlangt Neustart (neustart=1)", K, s, P("instanzId", g.InstanzId, "neustart", "1"), true, true);
            g.ProblemCode = 0;
            var n = N(h, saetze, "nachher: Code 0", K, s, p, true, false);
            h.Ist("Satz nennt den Geraetenamen", n != null && n.Satz.Contains(g.Name), n == null ? "" : n.Satz);
            g.ProblemCode = 28;
            n = N(h, saetze, "nachher: Code 28 (eingeschaltet, anderes Problem)", K, s, p, true);
            h.Ist("Satz nennt Code 28 als anderes Problem", n != null && n.Satz.Contains("Code 28"), n == null ? "" : n.Satz);
            N(h, saetze, "Instanz-Id in anderer Schreibung", K, s, P("instanzId", g.InstanzId.ToLowerInvariant()), true);
            N(h, saetze, "unbekannte Instanz-Id", K, s, P("instanzId", "PCI\\VEN_0000&DEV_0000\\0"), false, null, true);
            N(h, saetze, "instanzId fehlt", K, s, P(), false, null, true);
        }

        // ------------------------------------------------------------ (c) Nachweise: Rueckweg

        // Wirksamkeitsprobe 18.09.2026: "kennung fehlt" in RueckwegGelungen wieder auf Ja gedreht ->
        // "Rueckweg ohne kennung => NICHT bestanden" und "Unbestimmt" rot; zurueckgebaut.
        static void NachweiseRueckweg(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweis rueckweg dreht den Nachweis der Hin-Massnahme");
            var f = h.Bild("gepflanzt-sicherheit-firewall-oeffentlich-aus.json"); if (f == null) return;
            var oeff = f.Sicherheit.Firewall.FirstOrDefault(x => x.Profil == 4);
            h.Ist("Grundmenge: Profil 4 aus", oeff != null && !oeff.An);
            if (oeff == null) return;
            const string R = "rueckweg";
            // Rueckweg zu "Firewall einschalten": Profil 4 ist wieder aus -> Rueckweg gelungen.
            var n = N(h, saetze, "Rueckweg Firewall: Profil 4 wieder aus", R, f, P("sicherung", "x.json", "kennung", "sicherheit.firewall.einschalten", "profil", "4"), true, false);
            h.Ist("Satz nennt den Titel der Hin-Massnahme", n != null && n.Satz.Contains("Firewall einschalten"), n == null ? "" : n.Satz);
            // Die Form des Helfers: rueckweg.kennung und rueckweg.parameter.<name> (M3-Entwurf 8.3).
            N(h, saetze, "Rueckweg Firewall in Helfer-Schreibweise (rueckweg.kennung, rueckweg.parameter.profil)", R, f, P("sicherung", "x.json", "rueckweg.kennung", "sicherheit.firewall.einschalten", "rueckweg.parameter.profil", "4"), true, false);
            // Kennung gesetzt, Pflichtparameter der Hin-Massnahme fehlt: nicht messbar, nie bestanden.
            N(h, saetze, "Rueckweg Firewall: kennung gesetzt, profil fehlt", R, f, P("sicherung", "x.json", "kennung", "sicherheit.firewall.einschalten"), false, null, true);
            N(h, saetze, "Rueckweg Firewall: kennung gesetzt, profil = 9 (Profil nicht im Bild)", R, f, P("sicherung", "x.json", "kennung", "sicherheit.firewall.einschalten", "profil", "9"), false, null, true);
            oeff.An = true;
            N(h, saetze, "Rueckweg Firewall: Profil 4 noch an (nicht zurueckgeschrieben)", R, f, P("sicherung", "x.json", "kennung", "sicherheit.firewall.einschalten", "profil", "4"), false, null, false);
            // UAC: die Hin-Massnahme braucht einen Neustart, der Rueckweg auch.
            f.Sicherheit.EnableLua = 0;
            f.Sicherheit.ConsentAdmin = 5;
            N(h, saetze, "Rueckweg UAC: EnableLUA wieder 0 (gilt nach Neustart)", R, f, P("sicherung", "x.json", "kennung", "sicherheit.uac.standard"), true, true);
            f.Sicherheit.EnableLua = 1;
            N(h, saetze, "Rueckweg UAC: EnableLUA noch 1, Nachfragestufe 5", R, f, P("sicherung", "x.json", "kennung", "sicherheit.uac.standard"), false, null, false);
            f.Sicherheit.ConsentAdmin = null;
            N(h, saetze, "Rueckweg UAC: EnableLUA noch 1, Nachfragestufe nicht gesetzt (Standard): Hin-Nachweis bestanden", R, f, P("sicherung", "x.json", "kennung", "sicherheit.uac.standard"), false, null, false);
            f.Sicherheit.EnableLua = null;
            N(h, saetze, "Rueckweg UAC: beide Werte nicht vorhanden (Hin-Nachweis unbestimmt)", R, f, P("sicherung", "x.json", "kennung", "sicherheit.uac.standard"), false, null, true);
            f.Sicherheit.EnableLua = 1;
            f.Sicherheit.ConsentAdmin = 5;
            // SmartScreen zurueck auf Off.
            f.Sicherheit.SmartScreen = "Off";
            N(h, saetze, "Rueckweg SmartScreen: wieder Off", R, f, P("sicherung", "x.json", "kennung", "sicherheit.smartscreen.warn"), true);
            // Geraet: der Rueckweg (Disable) mit Neustart-Verlangen; ohne neustart-Parameter sofort.
            var g = h.Bild("gepflanzt-hardware-code22.json");
            if (g != null)
            {
                var geraet = g.Geraete.First(x => x.ProblemCode == 22);
                N(h, saetze, "Rueckweg Geraet: wieder Code 22, sofort", R, g, P("sicherung", "x.json", "kennung", "geraet.aktivieren", "instanzId", geraet.InstanzId), true, false);
                N(h, saetze, "Rueckweg Geraet: wieder Code 22, Windows verlangt Neustart (neustart=1 gilt nicht als Hin-Parameter)", R, g, P("sicherung", "x.json", "rueckweg.kennung", "geraet.aktivieren", "rueckweg.parameter.instanzId", geraet.InstanzId, "neustart", "1"), true, true);
                geraet.ProblemCode = 0;
                N(h, saetze, "Rueckweg Geraet: noch Code 0 (nicht zurueckgeschrieben)", R, g, P("sicherung", "x.json", "kennung", "geraet.aktivieren", "instanzId", geraet.InstanzId), false, null, false);
                // Disable angenommen, rebootNeeded (helfer/Massnahmen/Rueckweg.cs): das Geraet meldet bis
                // zum Neustart noch Code 0, der Helfer schreibt neustart = 1 -> bestanden nach Neustart.
                n = N(h, saetze, "Rueckweg Geraet: noch Code 0, aber Windows verlangt Neustart (neustart=1, Helfer-Schreibweise)", R, g, P("sicherung", "x.json", "rueckweg.kennung", "geraet.aktivieren", "rueckweg.parameter.instanzId", geraet.InstanzId, "neustart", "1"), true, true);
                h.Ist("Satz sagt, dass der alte Zustand bis zum Neustart noch gilt", n != null && n.Satz.Contains("bis zum Neustart"), n == null ? "" : n.Satz);
                N(h, saetze, "Rueckweg Geraet: noch Code 0, neustart=0 (kein Neustart verlangt)", R, g, P("sicherung", "x.json", "kennung", "geraet.aktivieren", "instanzId", geraet.InstanzId, "neustart", "0"), false, null, false);
                N(h, saetze, "Rueckweg Geraet: instanzId fehlt", R, g, P("sicherung", "x.json", "kennung", "geraet.aktivieren"), false, null, true);
            }
            // update.dienst.aktivieren traegt im Katalog Rueckweg keiner (M3-Entwurf 8.10), bei jedem
            // Vorwert: der Helfer meldet rueckweg.moeglich = "0" ohne Bedingung und lehnt jede
            // Sicherung dieser Kennung im Rueckweg ab (auch Vorwert Manual oder Automatic/Stopped).
            // Der Kern sperrt nichts: kommt doch ein Rueckweg-Ergebnis mit dieser Hin-Kennung an,
            // dreht RueckwegGelungen den Hin-Nachweis wie bei jeder anderen Kennung und meldet,
            // was das Bild zeigt; ein Riegel hier wuerde einen Eingriff verdecken, den der Helfer
            // entgegen seiner Ablehnung doch getan hat.
            var w = h.Bild("gepflanzt-updates-dienst-aus.json");
            if (w != null)
            {
                string wu;
                h.Ist("Grundmenge: wuauserv Disabled/Stopped fuer den Rueckweg-Nachweis", w.WindowsUpdate.Dienste.TryGetValue("wuauserv", out wu) && wu == "Disabled/Stopped");
                const string Ud = "update.dienst.aktivieren";
                n = N(h, saetze, "Rueckweg Update-Dienst: wuauserv wieder Disabled/Stopped (Kern wertet aus, sperrt nicht)", R, w, P("sicherung", "x.json", "kennung", Ud, "name", "wuauserv"), true, false);
                h.Ist("Satz nennt den Titel der Hin-Massnahme Update-Dienst", n != null && n.Satz.Contains("Update-Dienst wieder einschalten"), n == null ? "" : n.Satz);
                N(h, saetze, "Rueckweg Update-Dienst in Helfer-Schreibweise (rueckweg.kennung, rueckweg.parameter.name)", R, w, P("sicherung", "x.json", "rueckweg.kennung", Ud, "rueckweg.parameter.name", "wuauserv"), true, false);
                w.WindowsUpdate.Dienste["wuauserv"] = "Manual/Stopped";
                N(h, saetze, "Rueckweg Update-Dienst: wuauserv Manual/Stopped (Hin-Nachweis Nein, Rueckweg gelungen)", R, w, P("sicherung", "x.json", "kennung", Ud, "name", "wuauserv"), true, false);
                w.WindowsUpdate.Dienste["wuauserv"] = "Auto/Running";
                N(h, saetze, "Rueckweg Update-Dienst: wuauserv noch Auto/Running (nicht zurueckgeschrieben)", R, w, P("sicherung", "x.json", "kennung", Ud, "name", "wuauserv"), false, null, false);
                w.WindowsUpdate.Dienste["wuauserv"] = "fehlt";
                N(h, saetze, "Rueckweg Update-Dienst: Startart mit Marke fehlt (Hin-Nachweis unbestimmt)", R, w, P("sicherung", "x.json", "kennung", Ud, "name", "wuauserv"), false, null, true);
                w.WindowsUpdate.Dienste["wuauserv"] = "Disabled/Stopped";
                N(h, saetze, "Rueckweg Update-Dienst: name fehlt", R, w, P("sicherung", "x.json", "kennung", Ud), false, null, true);
            }
            // Ohne Hin-Kennung: nicht messbar, nie bestanden (M3-Entwurf 8.1).
            n = N(h, saetze, "Rueckweg ohne kennung", R, f, P("sicherung", "x.json"), false, null, true);
            h.Ist("Satz sagt, dass die Hin-Massnahme nicht genannt ist", n != null && n.Satz.Contains("nicht genannt"), n == null ? "" : n.Satz);
            N(h, saetze, "Rueckweg mit unbekannter kennung", R, f, P("sicherung", "x.json", "kennung", "gibt.es.nicht"), false, null, true);
            N(h, saetze, "Rueckweg mit kennung rueckweg (keine Endlosschleife)", R, f, P("sicherung", "x.json", "kennung", "rueckweg"), false, null, true);
            N(h, saetze, "Rueckweg mit leerer kennung", R, f, P("sicherung", "x.json", "kennung", ""), false, null, true);
        }

        // ------------------------------------------------------------ (c) Nachweise ohne messbaren Zustand

        static void NachweiseOhneZustand(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c): Nachweise ohne messbaren Zustand sagen das ehrlich");
            var s = h.Bild("gepflanzt-sicherheit-gesund.json"); if (s == null) return;
            string[] ehrlich = { "netz.dnscache.leeren", "volume.optimieren", "tiefenpruefung", "dateien.reparieren", "werkzeug", "wartung.auto",
                                 "apps.entfernen", "netz.diagnose", "treiber.sichern", "selbststart.loeschen", "registrierung.entfernen",
                                 "speicher.aufraeumen", "update.einstellungen.oeffnen" };
            h.Ist("Grundmenge: 13 Kennungen ohne messbaren Zustand", ehrlich.Length == 13);
            foreach (string k in ehrlich)
            {
                var n = N(h, saetze, k + ": bestanden mit ehrlichem Satz", k, s, P("buchstabe", "C"), true);
                h.Ist(k + ": Satz nennt Ausgabe, Bericht, Protokoll oder das Bild als Grenze",
                      n != null && (n.Satz.Contains("Systembild") || n.Satz.Contains("Ausgabe") || n.Satz.Contains("Bericht") || n.Satz.Contains("Protokoll")), n == null ? "" : n.Satz);
                // Der Kern sieht die Ausfuehrung nicht: meldet der Helfer nachweis.bestanden = 0 (Werte-
                // Schluessel aus helfer/Massnahmen/Gemeinsam.cs), ist der Nachweis Nein (bestimmt, nicht
                // Unbestimmt) und zitiert nachweis.satz; ohne Satz nennt er das fehlende Ergebnis.
                string titel = Massnahmen.Finde(k).Titel;
                n = N(h, saetze, k + ": Helfer meldet nachweis.bestanden = 0 mit Satz", k, s, P("buchstabe", "C", "nachweis.bestanden", "0", "nachweis.satz", "Probe: der Aufruf warf 0x80041001."), false, false, false);
                h.Ist(k + ": Satz beginnt mit dem Titel und nennt den Fehler des Helfers",
                      n != null && n.Satz.StartsWith(titel + " lief mit Fehler; ") && n.Satz.Contains("0x80041001"), n == null ? "" : n.Satz);
                n = N(h, saetze, k + ": Helfer meldet nachweis.bestanden = 0 ohne Satz", k, s, P("buchstabe", "C", "nachweis.bestanden", "0"), false, false, false);
                h.Ist(k + ": Satz nennt das fehlende Ergebnis des Helfers", n != null && n.Satz.Contains("kein Ergebnis"), n == null ? "" : n.Satz);
                N(h, saetze, k + ": Helfer meldet nachweis.bestanden = 1", k, s, P("buchstabe", "C", "nachweis.bestanden", "1"), true);
                N(h, saetze, k + ": nachweis.bestanden leer (nichts gemeldet, wie im Trockenlauf)", k, s, P("buchstabe", "C", "nachweis.bestanden", ""), true);
                // Ein unlesbarer Wert ist kein bestandener Lauf (fail closed), aber auch kein Unbestimmt.
                n = N(h, saetze, k + ": Helfer meldet nachweis.bestanden = x (unlesbar)", k, s, P("buchstabe", "C", "nachweis.bestanden", "x"), false, false, false);
                h.Ist(k + ": Satz nennt den unlesbaren Wert", n != null && n.Satz.Contains("„x“"), n == null ? "" : n.Satz);
            }
            N(h, saetze, "wiederherstellungspunkt.zurueck: erst nach Neustart", "wiederherstellungspunkt.zurueck", s, P("folge", "41"), true, true);
            // Auch der NachNeustart-Nachweis ohne Zustand faellt bei einem Fehler des Helfers auf Nein, ohne Neustart-Flag.
            var z = N(h, saetze, "wiederherstellungspunkt.zurueck: Helfer meldet nachweis.bestanden = 0", "wiederherstellungspunkt.zurueck", s, P("folge", "41", "nachweis.bestanden", "0", "nachweis.satz", "Probe: SystemRestore.Restore lieferte 1."), false, false, false);
            h.Ist("wiederherstellungspunkt.zurueck: Satz beginnt mit dem Titel und zitiert den Helfer", z != null && z.Satz.StartsWith(Massnahmen.Finde("wiederherstellungspunkt.zurueck").Titel + " lief mit Fehler; ") && z.Satz.Contains("SystemRestore.Restore"), z == null ? "" : z.Satz);
            // Nachweise mit messbarem Zustand lesen den Schluessel nicht: der Zustand im Bild entscheidet
            // (die Sperre fuer Exit 1 sitzt dort beim Aufrufer, M3-Entwurf 8.6).
            N(h, saetze, "sicherheit.smartscreen.warn: Bild Warn, nachweis.bestanden = 0 ohne Wirkung", "sicherheit.smartscreen.warn", s, P("nachweis.bestanden", "0"), s.Sicherheit.SmartScreen != "Off" && !string.IsNullOrEmpty(s.Sicherheit.SmartScreen));
            // Rat: nie ausgefuehrt, darum nie bestanden und nie unbestimmt (eine klare Absage).
            foreach (var m in Massnahmen.Alle().Where(x => x.Art == Massnahme.ArtRat))
                N(h, saetze, m.Kennung + " (Rat): nicht bestanden", m.Kennung, s, P(), false, null, false);
            h.Ist("apps.entfernen: WasPassiert ohne Fachjargon Remove-AppxPackage", !Massnahmen.Finde("apps.entfernen").WasPassiert.Contains("Remove-AppxPackage"));
        }

        // ------------------------------------------------------------ (c, dieser PC) Zusatz, nur lokal

        /// <summary>
        /// Dieselben Nachweise noch einmal ueber die echte, redigierte Aufzeichnung dieses Rechners
        /// (Konzept 7.1/10: die Datei liegt nur lokal, in der CI fehlt sie). Fehlt sie, wird die
        /// Gruppe laut uebersprungen wie DieserPcProben, nicht rot. Sie laeuft erst NACH der
        /// Deckungspruefung "jede Kennung mit Unlesbar-Zweig hat eine Unbestimmt-Probe" und steuert
        /// dort nichts bei: die Deckung muss aus den committeten Bildern kommen.
        /// </summary>
        static void NachweiseDieserPc(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            const string Datei = "dieser-pc-de.json";
            if (!File.Exists(Path.Combine(h.Aufzeichnungen, Datei)))
            {
                h.Uebersprungen("MassnahmenProben (dieser PC)", Datei + " liegt nur lokal (docs/werkzeuge/aufzeichnen-dieser-pc.ps1 erzeugt sie)");
                return;
            }
            h.Gruppe("Massnahmen (c, dieser PC): Nachweise ueber die echte Aufzeichnung (nur lokal, Zusatz)");
            var s = h.Bild(Datei); if (s == null) return;

            // Netz: Adapter 10 bezieht seine Namensserver fest (Uebergabe: DNS fest).
            const string Dns = "netz.dns.zuruecksetzen";
            var fest = s.Netz.Dns.FirstOrDefault(x => x.AdapterIndex == 10 && x.Familie == 2);
            h.Ist("Grundmenge: Adapter 10 mit fest eingetragenen IPv4-Namensservern", fest != null && fest.Statisch == true && fest.Server.Count >= 1);
            if (fest != null)
            {
                var n = N(h, saetze, "vorher: Namensserver fest eingetragen (Adapter 10)", Dns, s, P("adapter", "10"), false, null, false);
                h.Ist("Satz nennt den festen Server", n != null && n.Satz.Contains(fest.Server[0]), n == null ? "" : n.Satz);
                fest.Statisch = false;
                N(h, saetze, "nachher: Namensserver automatisch (Statisch false)", Dns, s, P("adapter", "10"), true);
                fest.Statisch = true;
            }

            // Leistung: echte Eintraege mit echtem Task-Manager-Status, echte Aufgabe, echte Dienste.
            const string Auto = "leistung.autostart.deaktivieren";
            var rtk = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKLM\\Run" && e.Name == "RtkAudUService");
            var razer = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKLM\\Run" && e.Name == "RazerCortex");
            var opera = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKCU\\Run" && e.Name == "Opera GX Stable");
            var maus = s.Autostart.Eintraege.FirstOrDefault(e => e.Quelle == "HKLM\\Run32");
            h.Ist("Grundmenge: RtkAudUService deaktiviert, RazerCortex aktiv, Opera (HKCU) deaktiviert, ein Run32-Eintrag, Aufgaben vollstaendig, >= 50 Dienste",
                  rtk != null && rtk.Aktiviert == false && razer != null && razer.Aktiviert == true && opera != null && opera.Aktiviert == false && maus != null
                  && s.Autostart.AufgabenVollstaendig && s.Autostart.Dienste.Count >= 50);
            if (rtk != null && razer != null && opera != null && maus != null)
            {
                var n = N(h, saetze, "nachher: HKLM/Run RtkAudUService deaktiviert", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "RtkAudUService"), true);
                h.Ist("Satz nennt den Eintrag und den Ort", n != null && n.Satz.Contains("RtkAudUService") && n.Satz.Contains("HKLM\\Run"), n == null ? "" : n.Satz);
                N(h, saetze, "Name ohne Ruecksicht auf Gross-/Kleinschreibung", Auto, s, P("hive", "hklm", "ort", "run", "name", "rtkaudUservice"), true);
                N(h, saetze, "vorher: HKLM/Run RazerCortex aktiv", Auto, s, P("hive", "HKLM", "ort", "Run", "name", "RazerCortex"), false, null, false);
                N(h, saetze, "nachher: HKCU/Run Opera deaktiviert", Auto, s, P("hive", "HKCU", "ort", "Run", "name", "Opera GX Stable"), true);
                N(h, saetze, "HKLM/Run32 " + maus.Name + " (Status wie im Bild: " + (maus.Aktiviert == false ? "deaktiviert" : "aktiv") + ")", Auto, s, P("hive", "HKLM", "ort", "Run32", "name", maus.Name), maus.Aktiviert == false, null, false);
            }
            const string Aufg = "leistung.aufgabe.deaktivieren";
            var hid = s.Autostart.Aufgaben.FirstOrDefault(a => a.Pfad == "\\HidHide_Updater");
            h.Ist("Grundmenge: Aufgabe \\HidHide_Updater bereit (3), nicht deaktiviert", hid != null && hid.Zustand == 3 && hid.Aktiv != false);
            if (hid != null)
                N(h, saetze, "vorher: HidHide_Updater bereit (3), Enabled wie im Bild", Aufg, s, P("pfad", "\\HidHide_Updater"), false, null, false);
            const string St = "leistung.dienst.startart";
            var alg = s.Autostart.Dienste.FirstOrDefault(d => d.Name == "ALG");
            var amd = s.Autostart.Dienste.FirstOrDefault(d => d.Startart == "Auto");
            h.Ist("Grundmenge: Dienst ALG Manual, ein Dienst mit Startart Auto", alg != null && alg.Startart == "Manual" && amd != null);
            if (alg != null && amd != null)
            {
                N(h, saetze, "ALG: Ziel Manual, ist Manual", St, s, P("name", "ALG", "startart", "Manual"), true);
                N(h, saetze, "ALG: Ziel Disabled, ist Manual", St, s, P("name", "ALG", "startart", "Disabled"), false, null, false);
                N(h, saetze, amd.Name + ": Ziel Automatic, ist Auto (Marke des Bildes)", St, s, P("name", amd.Name, "startart", "Automatic"), true);
            }
            // Zeitplan: die eigene Wartungsaufgabe steht wirklich in der Aufgabenplanung dieses PCs.
            const string Wartung = "\\WindowsWartung-AutoWartung";
            var pz = P("modus", "daily", "stunde", "3", "minute", "0");
            var wartung = s.Autostart.Aufgaben.FirstOrDefault(a => a.Eigen && a.Pfad == Wartung);
            h.Ist("Grundmenge: eigene Aufgabe " + Wartung + " im Bild, Liste vollstaendig", wartung != null && s.Autostart.AufgabenVollstaendig);
            if (wartung != null)
            {
                N(h, saetze, "zeitplan.anlegen: Aufgabe vorhanden", "zeitplan.anlegen", s, pz, true);
                N(h, saetze, "zeitplan.loeschen: Aufgabe noch vorhanden", "zeitplan.loeschen", s, P(), false, null, false);
                s.Autostart.Aufgaben.RemoveAll(a => a.Pfad == Wartung);
                N(h, saetze, "zeitplan.loeschen: Aufgabe weg", "zeitplan.loeschen", s, P(), true);
                N(h, saetze, "zeitplan.anlegen: Aufgabe fehlt", "zeitplan.anlegen", s, pz, false, null, false);
                s.Autostart.Aufgaben.Add(wartung);
            }

            // Updates: das verborgene HP-USB-Update (Kennung aus der Uebergabe).
            const string Vb = "update.verbergen", HpId = "4340b239-4b90-4305-9c2e-6a24dfcb6757";
            h.Ist("Grundmenge: genau 1 verborgenes Update mit Kennung 4340b239-...", s.WindowsUpdate.Verborgen.Count == 1 && string.Equals(s.WindowsUpdate.Verborgen[0].UpdateId, HpId, StringComparison.OrdinalIgnoreCase));
            var nv = N(h, saetze, "nachher: Kennung in der Liste der verborgenen Updates", Vb, s, P("updateId", HpId), true);
            h.Ist("Satz nennt den Titel des Updates", nv != null && nv.Satz.Contains("Hewlett-Packard"), nv == null ? "" : nv.Satz);
            N(h, saetze, "vorher: andere Kennung nicht verborgen", Vb, s, P("updateId", "00000000-0000-0000-0000-000000000001"), false, null, false);
        }

        // ------------------------------------------------------------ (c2) die 16 Paare

        /// <summary>
        /// Dynamisch ueber gepflanzt-massnahmen-*-vorher.json (Kennung aus dem Dateinamen). Die
        /// Einzelkarten der Leistung sind absichtlich "ok" (Kandidat, kein Fehler), deshalb zaehlt
        /// ueber VORHER jeder Befund mit Kennung und passenden Parametern, ueber NACHHER darf es keinen
        /// mehr geben (gleich welchen Zustands). Sonderfall geraet.aktivieren: ohne Antwort
        /// "reparieren" ist der Befund eine Frage ohne Massnahme.
        /// </summary>
        static void NachweisePaare(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c2): 16 Paare gepflanzt-massnahmen-<kennung>-vorher/-nachher");
            const string Praefix = "gepflanzt-massnahmen-", Vorher = "-vorher.json", Nachher = "-nachher.json";
            var dateien = Directory.GetFiles(h.Aufzeichnungen, Praefix + "*" + Vorher).Select(Path.GetFileName).OrderBy(x => x).ToList();
            h.Ist("Grundmenge: genau 16 Paare (Vorher-Dateien)", dateien.Count == 16, dateien.Count.ToString());
            h.Ist("Grundmenge: genau 16 Nachher-Dateien", Directory.GetFiles(h.Aufzeichnungen, Praefix + "*" + Nachher).Length == 16);
            h.Ist("Grundmenge: Parametertabelle mit 16 Kennungen", PaarParameter.Count == 16);
            int gelaufen = 0;
            foreach (string datei in dateien)
            {
                string kennung = datei.Substring(Praefix.Length, datei.Length - Praefix.Length - Vorher.Length);
                Dictionary<string, string> erwartet;
                h.Ist(kennung + ": Parametertabelle kennt die Kennung", PaarParameter.TryGetValue(kennung, out erwartet));
                if (erwartet == null) continue;
                var m = Massnahmen.Finde(kennung);
                h.Ist(kennung + ": Katalog kennt die Kennung", m != null);
                if (m == null) continue;
                h.Ist(kennung + ": Tabelle nennt genau die Pflichtparameter", m.Parameter.All(erwartet.ContainsKey) && erwartet.Keys.All(m.Parameter.Contains), string.Join(",", erwartet.Keys));
                var vorher = h.Bild(datei);
                var nachher = h.Bild(Praefix + kennung + Nachher);
                if (vorher == null || nachher == null) continue;
                gelaufen++;

                var e = new Entscheidungen();
                if (kennung == "geraet.aktivieren") e.Setze(GeraetFrage, Entscheidungen.Reparieren, vorher.AufgezeichnetUtc);

                var traeger = Harness.Befunde(h.Pruefen(vorher, e)).Where(b => b.Massnahmen.Contains(kennung) && Passt(b.Parameter, erwartet)).ToList();
                h.Ist(kennung + ": Regeln ueber VORHER haengen die Kennung mit den Tabellen-Parametern an einen Befund (" + string.Join(",", erwartet.Select(kv => kv.Key + "=" + kv.Value)) + ")",
                      traeger.Count >= 1, string.Join("; ", Harness.Befunde(h.Pruefen(vorher, e)).Where(b => b.Massnahmen.Contains(kennung)).Select(b => b.Schluessel + " [" + string.Join(",", b.Parameter.Select(kv => kv.Key + "=" + kv.Value)) + "]")));
                if (traeger.Count > 0)
                    h.Ist(kennung + ": der Befund " + traeger[0].Schluessel + " traegt alle Pflichtparameter", m.Parameter.All(n => traeger[0].Parameter.ContainsKey(n) && !string.IsNullOrEmpty(traeger[0].Parameter[n])));

                // Der Nachweis laeuft mit den Tabellen-Parametern, damit er auch dann geprueft wird,
                // wenn die Regel-Seite die Kennung (noch) nicht anhaengt.
                var p = new Dictionary<string, string>(erwartet);
                N(h, saetze, kennung + " VORHER", kennung, vorher, p, false, null, false);
                N(h, saetze, kennung + " NACHHER", kennung, nachher, p, true);

                var danach = Harness.Befunde(h.Pruefen(nachher, e)).Where(b => b.Massnahmen.Contains(kennung) && Passt(b.Parameter, erwartet)).ToList();
                h.Ist(kennung + ": Regeln ueber NACHHER haengen die Kennung mit diesen Parametern an keinen Befund mehr", danach.Count == 0, string.Join("; ", danach.Select(b => b.Schluessel + " (" + b.Zustand + ")")));
            }
            h.Ist("Grundmenge: 16 Paare gelaufen", gelaufen == 16, gelaufen.ToString());
            AufgabePaarLaufend(h, saetze);
        }

        /// <summary>
        /// Das 17. Paar, im Speicher aus dem Aufgaben-Paar gedreht (die Dateien decken nur 3 -> 1 ab):
        /// eine Anmelde-Aufgabe, die ein residentes Programm startet, laeuft bis zur Abmeldung
        /// (Zustand 4) und meldet nach schtasks /DISABLE weiter 4, nicht 1 (Microsoft-Doku TASK_STATE:
        /// DISABLED = "no instances queued or running", RUNNING = "one or more instances running"; auf
        /// dieser-pc-de.json 4 von 12 fremden Logon-/Boot-Aufgaben). VORHER: Zustand 4, Enabled true ->
        /// Regel haengt die Kennung an, Nachweis Nein. NACHHER: Zustand 4, Enabled false -> Nachweis Ja,
        /// und die Regel zaehlt die Aufgabe nicht mehr (Aktiv != false), sonst bliebe die Einzelkarte
        /// mit Knopf stehen und der zweite Klick endete mit "war schon deaktiviert".
        /// </summary>
        static void AufgabePaarLaufend(Harness h, List<KeyValuePair<string, string>> saetze)
        {
            h.Gruppe("Massnahmen (c2): 17. Paar im Speicher, Aufgabe mit laufender Instanz (Zustand 4) und Enabled");
            const string Kennung = "leistung.aufgabe.deaktivieren", Pfad = "\\FremdUpdater";
            var erwartet = P("pfad", Pfad);
            var s = h.Bild("gepflanzt-massnahmen-" + Kennung + "-vorher.json"); if (s == null) return;
            var t = s.Autostart.Aufgaben.FirstOrDefault(a => a.Pfad == Pfad);
            h.Ist("Grundmenge: " + Pfad + " im Vorher-Bild, Logon, Zustand 3, ohne Feld aktiv", t != null && t.Logon && t.Zustand == 3 && !t.Aktiv.HasValue);
            if (t == null) return;
            Func<List<Befund>> traeger = () => Harness.Befunde(h.Pruefen(s)).Where(b => b.Massnahmen.Contains(Kennung) && Passt(b.Parameter, erwartet)).ToList();

            // VORHER: die Instanz laeuft, der Schalter steht auf Enabled = true.
            t.Zustand = 4;
            t.Aktiv = true;
            h.Ist("VORHER (Zustand 4, Enabled true): die Regel haengt " + Kennung + " mit pfad=" + Pfad + " an", traeger().Count >= 1);
            var n = N(h, saetze, Kennung + " VORHER (Zustand 4, Enabled true)", Kennung, s, erwartet, false, null, false);
            h.Ist("VORHER: Satz nennt Enabled = true", n != null && n.Satz.Contains("Enabled = true"), n == null ? "" : n.Satz);

            // NACHHER: schtasks /DISABLE ist durch, die Instanz laeuft noch bis zur Abmeldung.
            t.Aktiv = false;
            n = N(h, saetze, Kennung + " NACHHER (Zustand 4, Enabled false)", Kennung, s, erwartet, true);
            h.Ist("NACHHER: Satz nennt Enabled = false und die Instanz, die von selbst endet", n != null && n.Satz.Contains("Enabled = false") && n.Satz.Contains("endet von selbst"), n == null ? "" : n.Satz);
            var danach = traeger();
            h.Ist("NACHHER (Zustand 4, Enabled false): die Regel haengt " + Kennung + " mit pfad=" + Pfad + " an keinen Befund mehr (Aktiv != false zaehlt, nicht Zustand != 1)",
                  danach.Count == 0, string.Join("; ", danach.Select(b => b.Schluessel + " (" + b.Zustand + ")")));

            // Gegenprobe: ohne das Feld (aelteres Bild) bleibt der Zustand 4 nicht belegbar, nie Ja, nie Nein.
            t.Aktiv = null;
            N(h, saetze, Kennung + " NACHHER ohne Feld aktiv (Zustand 4): nicht belegbar", Kennung, s, erwartet, false, null, true);
            t.Zustand = 3;
        }

        /// <summary>Alle Tabellen-Parameter stehen mit demselben Wert am Befund (Schreibung des Werts frei).</summary>
        static bool Passt(Dictionary<string, string> befund, Dictionary<string, string> erwartet)
        {
            if (befund == null) return erwartet.Count == 0;
            foreach (var kv in erwartet)
            {
                string v;
                if (!befund.TryGetValue(kv.Key, out v) || !string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------ (e) Pflichtparameter an den Befunden

        // Ohne Auslassung: auch ein Befund mit leerem Parameter-Woerterbuch wird geprueft, denn genau
        // der Fehlerfall "Kennung ohne Parameter" (volume.scan ohne buchstabe) hat ein leeres.
        static void Parameterpflicht(Harness h)
        {
            h.Gruppe("Massnahmen (e): Befunde mit Kennung tragen die Pflichtparameter");
            int geprueft = 0, mitParametern = 0, mitKennung = 0;
            foreach (var s in AlleBilder(h))
                foreach (var b in Harness.Befunde(h.Pruefen(s)))
                {
                    if (b.Parameter != null && b.Parameter.Count > 0) mitParametern++;
                    if (b.Massnahmen.Count > 0) mitKennung++;
                    foreach (string k in b.Massnahmen)
                    {
                        var m = Massnahmen.Finde(k);
                        if (m == null || m.Parameter.Length == 0) continue;
                        geprueft++;
                        h.Ist(b.Schluessel + " traegt " + string.Join(",", m.Parameter) + " fuer " + k,
                              b.Parameter != null && m.Parameter.All(n => b.Parameter.ContainsKey(n) && !string.IsNullOrEmpty(b.Parameter[n])),
                              b.Parameter == null ? "(Parameter null)" : string.Join(",", b.Parameter.Keys));
                    }
                }
            h.Ist("Grundmenge: mindestens 100 Befunde mit Kennung ueber alle Bilder", mitKennung >= 100, mitKennung.ToString());
            h.Ist("Grundmenge: mindestens 50 Befunde mit Parametern", mitParametern >= 50, mitParametern.ToString());
            h.Ist("Befunde mit Parametern: " + mitParametern + ", davon " + geprueft + " Kennungen mit Pflichtparametern geprueft (mindestens 50)", geprueft >= 50, geprueft.ToString());
        }
    }
}
