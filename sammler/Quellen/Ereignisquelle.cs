using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Liest das Ereignisprotokoll im eigenen Prozess (System.Diagnostics.Eventing.Reader),
    /// ohne powershell.exe und ohne wevtutil. Fuellt s.Ereignisse (abonnierte Anbieter und IDs
    /// der letzten 90 Tage, Log-Beginn, gesperrte Logs) und s.Zuverlaessigkeit.
    ///
    /// Drei Lehren aus der Widerlegungsrunde stecken hier drin:
    ///
    /// 1. Ereignisse werden ueber Anbieter + ID + benannte Felder gelesen (ToXml, Data[@Name]),
    ///    nie ueber FormatDescription() und nie ueber feste Indizes: Kernel-Power 41 hat elf
    ///    Manifest-Versionen (8 bis 21 Felder), 6008 traegt U+200E im Datumstext.
    /// 2. v7 zaehlte IDs ohne Anbieter (41, 1001, 6008 werden mehrfach vergeben). Hier ist jeder
    ///    Anbieter ein eigener Versuch mit eigener Quellenkennung, damit ein Fehler einer Quelle
    ///    die anderen nicht mitreisst und in der Fehlerliste zu finden ist.
    /// 3. Ein gesperrtes Log (Diagnostics-Performance/Operational, Kanal-ACL nur fuer SYSTEM und
    ///    Administratoren) meldet ueber Get-WinEvent "0 Ereignisse". Der EventLogReader-Konstruktor
    ///    wirft dagegen UnauthorizedAccessException - genau die wird hier gefangen und als
    ///    "gesperrt" plus fehlerliste:zugriff eingetragen. Nie als "keine Ereignisse".
    ///
    /// Und eine vierte aus der Befundrunde vom 13.09.2026: jede Grenze (je Abonnement, Zeitbudget)
    /// hinterlaesst einen Eintrag "zeit" in der Fehlerliste, sobald sie etwas abschneidet. Ein
    /// still verkuerztes Fenster saehe fuer die Regeln wie "nichts passiert" aus.
    /// </summary>
    public static class Ereignisquelle
    {
        /// <summary>Obergrenze je Abonnement: neueste zuerst; werden mehr abgeschnitten, steht das als "zeit" in der Fehlerliste.</summary>
        public const int MaxJeAbo = 500;
        /// <summary>Zeitbudget fuer alle Logs zusammen; danach werden die restlichen Quellen mit "zeit" markiert.</summary>
        public const int BudgetMs = 15000;
        /// <summary>Zeitgrenze je einzelnem ReadEvent-Aufruf (ein haengender Ereignisdienst darf nicht alles blockieren).</summary>
        const int LeseZeitMs = 5000;
        /// <summary>Startdauer (ID 100) nur der letzten 30 Tage: auf Build 26200 wird sie nicht mehr je Start geschrieben.</summary>
        const int StartdauerTage = 30;

        public const string DiagnosePerfLog = "Microsoft-Windows-Diagnostics-Performance/Operational";
        const string EventNs = "http://schemas.microsoft.com/win/2004/08/events/event";
        /// <summary>Meldungsdatei der Storport-Miniports (storahci, stornvme, amdsata, iaStor*, ...): nur deren 129 ist ein Laufwerks-Reset.</summary>
        const string IoLogMsg = "IoLogMsg.dll";

        /// <summary>
        /// Ein Abonnement: Log, Anbieter (null = anbieterlos, Anbieter wird je Ereignis geprueft),
        /// IDs oder Levels, Quellenkennung. Mit Levels statt IDs (WHEA) kommt jede Warnung und jeder
        /// Fehler des Anbieters, auch IDs, die im Manifest neu sind.
        /// </summary>
        class Abo
        {
            public readonly string Log, Anbieter, Quelle;
            public readonly int[] Ids, Levels;
            public Abo(string log, string anbieter, string quelle, params int[] ids) { Log = log; Anbieter = anbieter; Quelle = quelle; Ids = ids; Levels = new int[0]; }
            public Abo(string log, string anbieter, string quelle, int[] levels, int[] ids) { Log = log; Anbieter = anbieter; Quelle = quelle; Ids = ids; Levels = levels; }
        }

        // Anbieter-Namen sind Manifest- bzw. Quellennamen und nicht lokalisiert. "Ntfs" (klassisch,
        // 55/130/131) ist ein anderer Anbieter als "Microsoft-Windows-Ntfs" (98/140/142/150) - ein
        // XPath mit dem falschen Anbieter findet nie etwas (Widerlegungsrunde R2).
        //
        // Reihenfolge = Rang: was eine Regel liest, steht vorn, damit das Zeitbudget zuerst die
        // reine Historie trifft. Gespraechige informative IDs (Ntfs 98, Kernel-PnP 411, disk 157)
        // haben ein eigenes Abonnement "verlauf.*", damit sie die Fehler-IDs desselben Anbieters
        // nicht aus der 500er-Grenze verdraengen. Quellenkennungen duerfen kein Praefix einer
        // anderen sein: FehlerVon vergleicht per StartsWith.
        static readonly Abo[] Abos =
        {
            // Regeln Stabilitaet
            new Abo("System", "Microsoft-Windows-Kernel-Power", "log.system.kernel-power", 41, 109),
            new Abo("Application", "Application Error", "log.application.application-error", 1000),
            new Abo("System", "Microsoft-Windows-WHEA-Logger", "log.system.whea-logger", new[] { 2, 3 }, new int[0]),
            new Abo("System", "Microsoft-Windows-Kernel-Boot", "log.system.kernel-boot", 20),
            new Abo("System", "EventLog", "log.system.eventlog", 6005, 6006, 6008),
            new Abo("System", "User32", "log.system.user32", 1074),
            new Abo("System", "Microsoft-Windows-WER-SystemErrorReporting", "log.system.wer-systemerrorreporting", 1001),
            new Abo("Application", "Application Hang", "log.application.application-hang", 1002),
            // Regeln Datentraeger
            new Abo("System", "Ntfs", "log.system.ntfs", 55, 130, 131, 132, 133),
            new Abo("System", "Microsoft-Windows-Ntfs", "log.system.microsoft-windows-ntfs", 150),
            new Abo("System", "disk", "log.system.disk", 7, 11, 51, 153),
            // 129 (Reset an das Geraet) schreiben ausser den Speichertreibern auch Hyper-V und der Zeitdienst:
            // nach ID filtern, den Anbieter je Ereignis gegen seine Meldungsdatei (IoLogMsg.dll) pruefen.
            new Abo("System", null, "log.system.id129", 129),
            new Abo("System", "volmgr", "log.system.volmgr", 46, 49),
            // Uebersicht und Historie (keine Regel entscheidet daran)
            new Abo("System", "Microsoft-Windows-Kernel-General", "log.system.kernel-general", 12, 13),
            new Abo("System", "Microsoft-Windows-Kernel-Processor-Power", "log.system.kernel-processor-power", 37),
            new Abo("System", "Microsoft-Windows-Kernel-PnP", "log.system.kernel-pnp", 219),
            new Abo("System", "Microsoft-Windows-Kernel-PnP", "log.system.verlauf.kernel-pnp", 411),
            new Abo("System", "Microsoft-Windows-Ntfs", "log.system.verlauf.microsoft-windows-ntfs", 98, 140, 142),
            new Abo("System", "disk", "log.system.verlauf.disk", 157),
            new Abo("System", "Service Control Manager", "log.system.service-control-manager", 7000, 7009, 7023, 7031, 7034, 7045),
            new Abo("Application", ".NET Runtime", "log.application.dotnet-runtime", 1026),
            new Abo("Application", "Windows Error Reporting", "log.application.windows-error-reporting", 1001),
            new Abo("Application", "Chkdsk", "log.application.chkdsk", 26212, 26214, 26226, 26227, 26228),
            new Abo("Application", "Microsoft-Windows-Wininit", "log.application.wininit", 1001),
        };

        /// <summary>
        /// Obergrenze fuer das ganze Systembild. Sie ist so bemessen, dass jedes Abonnement seine
        /// MaxJeAbo bekommt: die Grenze je Abonnement ist der einzige wirksame Deckel, die globale
        /// faengt nur einen Programmfehler ab. Eine kleinere Zahl traf gemessen das 15. von 22
        /// Abonnements und liess "Application Error" 1000 stumm aus.
        /// </summary>
        public static readonly int MaxEintraege = MaxJeAbo * Abos.Length;

        public static void Erfassen(Systembild s)
        {
            var sw = Stopwatch.StartNew();
            var er = s.Ereignisse;
            if (er.Tage <= 0) er.Tage = Kern.Regeln.Schwellen.EreignisTage;
            int tage = er.Tage;

            // Log-Beginn zuerst: "0 Ereignisse in 90 Tagen" heisst bei einem Log, das 20 Tage alt ist,
            // nur "nichts seit Log-Anfang". Die Regeln brauchen das Datum fuer den Satz.
            Sammler.Versuch(s, "log.system.beginn", () => er.BeginnSystemUtc = Beginn(s, "System", "log.system.beginn"));
            Sammler.Versuch(s, "log.application.beginn", () => er.BeginnApplicationUtc = Beginn(s, "Application", "log.application.beginn"));

            var meldungsdateien = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var abo in Abos)
            {
                if (sw.ElapsedMilliseconds > BudgetMs)
                {
                    Sammler.Fehler(s, abo.Quelle, Fehler.Zeit, "Zeitbudget von " + (BudgetMs / 1000) + " s ausgeschöpft, Abonnement übersprungen");
                    continue;
                }
                if (er.Eintraege.Count >= MaxEintraege)
                {
                    Sammler.Fehler(s, abo.Quelle, Fehler.Zeit, "Obergrenze von " + MaxEintraege + " Einträgen erreicht, Abonnement übersprungen");
                    continue;
                }
                var a = abo;
                Sammler.Versuch(s, a.Quelle, () => Lesen(s, a, tage, sw, meldungsdateien));
            }

            // Ein Zugriffsfehler auf System oder Application bedeutet: das Log ist gesperrt. Die Regeln
            // fragen ctx.LogGesperrt(log), deshalb steht der Name zusaetzlich in der Liste.
            if (s.ZugriffVerweigert("log.system.") && !er.Gesperrt.Contains("System")) er.Gesperrt.Add("System");
            if (s.ZugriffVerweigert("log.application.") && !er.Gesperrt.Contains("Application")) er.Gesperrt.Add("Application");

            DiagnosePerformance(s, sw);
            Zuverlaessigkeit(s);
        }

        // ---------------------------------------------------------------- Lesen mit Zeitgrenze

        /// <summary>
        /// ReadEvent mit Zeitgrenze. Bei Ablauf liefert der Reader null oder wirft eine
        /// EventLogException (ERROR_TIMEOUT aus EvtNext) - beides wird an der gemessenen Dauer
        /// erkannt und zur TimeoutException, damit es in der Fehlerliste als "zeit" steht und
        /// nicht als "fertig" oder als Rechteproblem. Jede andere EventLogException (beschaedigtes
        /// Protokoll, ungueltige Daten) laeuft als "ausnahme" mit Typname weiter.
        /// </summary>
        static EventRecord LeseEreignis(EventLogReader r, string was)
        {
            var t0 = Stopwatch.StartNew();
            try
            {
                var rec = r.ReadEvent(TimeSpan.FromMilliseconds(LeseZeitMs));
                if (rec == null && t0.ElapsedMilliseconds >= LeseZeitMs - 100)
                    throw new TimeoutException(was + ": ReadEvent überschritt " + LeseZeitMs + " ms");
                return rec;
            }
            catch (EventLogException ex)
            {
                if (t0.ElapsedMilliseconds >= LeseZeitMs - 100)
                    throw new TimeoutException(was + ": ReadEvent überschritt " + LeseZeitMs + " ms (" + ex.GetType().Name + ")");
                throw;
            }
        }

        // ---------------------------------------------------------------- Log-Beginn

        /// <summary>Aeltestes Ereignis eines Logs (EventLogReader ohne Filter liest vorwaerts, also vom aeltesten her).</summary>
        static string Beginn(Systembild s, string log, string quelle)
        {
            using (var r = new EventLogReader(new EventLogQuery(log, PathType.LogName)))
            using (var rec = LeseEreignis(r, "Protokoll " + log))
            {
                if (rec == null)
                {
                    // Ein voellig leeres Log ist moeglich (frisch geloescht), aber eine Auffaelligkeit, keine Daten.
                    Sammler.Fehler(s, quelle, Fehler.Fehlt, "Protokoll " + log + " enthält keinen einzigen Eintrag");
                    return null;
                }
                return rec.TimeCreated.HasValue ? Zeit.Utc(rec.TimeCreated.Value) : null;
            }
        }

        // ---------------------------------------------------------------- Abonnements

        static void Lesen(Systembild s, Abo abo, int tage, Stopwatch gesamt, Dictionary<string, string> meldungsdateien)
        {
            string xpath = XPath(abo.Anbieter, abo.Ids, abo.Levels, tage);
            var q = new EventLogQuery(abo.Log, PathType.LogName, xpath) { ReverseDirection = true }; // neueste zuerst
            int n = 0;
            string aeltester = null;
            using (var r = new EventLogReader(q))
            {
                while (true)
                {
                    // Vor jedem Lesen, damit auch uebersprungene 129 fremder Anbieter das Budget nicht still verbrauchen.
                    if (gesamt.ElapsedMilliseconds > BudgetMs)
                        throw new TimeoutException("Abonnement " + abo.Quelle + ": Zeitbudget nach " + n + " Einträgen ausgeschöpft" + (aeltester != null ? ", ältester vom " + aeltester : ""));
                    if (n >= MaxJeAbo || s.Ereignisse.Eintraege.Count >= MaxEintraege)
                    {
                        // Grenze erreicht. Genau MaxJeAbo Treffer sind kein Verlust; erst ein weiteres
                        // Ereignis dahinter heisst, dass aeltere fehlen - und das steht dann in der Fehlerliste.
                        using (var weiter = LeseEreignis(r, "Abonnement " + abo.Quelle))
                            if (weiter != null)
                                Sammler.Fehler(s, abo.Quelle, Fehler.Zeit, "Obergrenze von " + (n >= MaxJeAbo ? MaxJeAbo : MaxEintraege) + " Einträgen erreicht: " + n + " gelesen, ältester vom " + (aeltester ?? "unbekannt") + "; ältere Ereignisse fehlen");
                        break;
                    }
                    EventRecord rec = LeseEreignis(r, "Abonnement " + abo.Quelle);
                    if (rec == null) break;
                    using (rec)
                    {
                        string meldungsdatei = null;
                        if (abo.Anbieter == null && !Speichertreiber(s, abo.Quelle, rec.ProviderName, meldungsdateien, out meldungsdatei)) continue;
                        var e = Umwandeln(rec, abo.Log);
                        if (e != null)
                        {
                            if (meldungsdatei != null) e.Felder["_meldungsdatei"] = meldungsdatei;   // Unterstrich: vom Sammler gesetzt, kein Ereignisfeld
                            s.Ereignisse.Eintraege.Add(e);
                            n++;
                            if (e.ZeitUtc != null) aeltester = e.ZeitUtc;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Ist der Anbieter eines 129 ein Speichertreiber? Entscheidet die Meldungsdatei unter
        /// HKLM\SYSTEM\CurrentControlSet\Services\EventLog\System\[Anbieter]\EventMessageFile:
        /// gemessen am 13.09.2026 fuehren hier 99 Quellen IoLogMsg.dll (auch als Liste mit ";" und
        /// klein geschrieben), der Zeitdienst w32time.dll, Hyper-V hvloader.dll. Eine feste Namensliste
        /// waere unvollstaendig (amdsata, iaStorAVC, UASPStor, pvscsi, megasas ...). Ein Anbieter ohne
        /// Schluessel zaehlt nicht; ein Registry-Fehler steht einmal je Anbieter in der Fehlerliste.
        /// </summary>
        static bool Speichertreiber(Systembild s, string quelle, string anbieter, Dictionary<string, string> cache, out string meldungsdatei)
        {
            meldungsdatei = null;
            if (string.IsNullOrEmpty(anbieter)) return false;
            string datei;
            if (!cache.TryGetValue(anbieter, out datei))
            {
                datei = null;
                try
                {
                    using (var basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = basis.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog\System\" + anbieter, false))
                    {
                        string wert = k == null ? null : k.GetValue("EventMessageFile", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (wert != null)
                            foreach (string teil in wert.Split(';'))
                            {
                                string name = Dateiname(teil.Trim());
                                if (string.Equals(name, IoLogMsg, StringComparison.OrdinalIgnoreCase)) { datei = IoLogMsg; break; }
                                if (datei == null && name.Length > 0) datei = name;
                            }
                    }
                }
                catch (Exception ex)
                {
                    Sammler.Fehler(s, quelle, Fehler.Ausnahme, "Meldungsdatei des Anbieters " + anbieter + " nicht lesbar (" + ex.GetType().Name + "); dessen Ereignisse 129 zählen nicht");
                }
                cache[anbieter] = datei;
            }
            if (!string.Equals(datei, IoLogMsg, StringComparison.OrdinalIgnoreCase)) return false;
            meldungsdatei = datei;
            return true;
        }

        /// <summary>Dateiname ohne Pfad; Path.GetFileName wirft bei ungueltigen Zeichen, die Registry darf sie enthalten.</summary>
        static string Dateiname(string pfad)
        {
            int i = pfad.LastIndexOfAny(new[] { '\\', '/' });
            return i >= 0 ? pfad.Substring(i + 1) : pfad;
        }

        /// <summary>
        /// XPath mit timediff auf N Tage; Anbieter optional; IDs oder Levels (mindestens eines).
        /// Anbieternamen mit Leerzeichen sind in Anfuehrungszeichen unproblematisch.
        /// </summary>
        static string XPath(string anbieter, int[] ids, int[] levels, int tage)
        {
            long ms = (long)tage * 86400000L;
            var teile = new List<string>();
            if (ids != null && ids.Length > 0)
            {
                var idTeil = new List<string>();
                foreach (int id in ids) idTeil.Add("EventID=" + id.ToString(CultureInfo.InvariantCulture));
                teile.Add("(" + string.Join(" or ", idTeil) + ")");
            }
            if (levels != null && levels.Length > 0)
            {
                var lvTeil = new List<string>();
                foreach (int lv in levels) lvTeil.Add("Level=" + lv.ToString(CultureInfo.InvariantCulture));
                teile.Add("(" + string.Join(" or ", lvTeil) + ")");
            }
            // Ein Abonnement ohne ID und ohne Level ergaebe "()" und damit einen ungueltigen XPath.
            if (teile.Count == 0) throw new ArgumentException("Abonnement ohne EventID und ohne Level");
            teile.Add("TimeCreated[timediff(@SystemTime) <= " + ms.ToString(CultureInfo.InvariantCulture) + "]");
            string bedingung = string.Join(" and ", teile);
            if (anbieter != null) bedingung = "Provider[@Name='" + anbieter.Replace("'", "&apos;") + "'] and " + bedingung;
            return "*[System[" + bedingung + "]]";
        }

        // ---------------------------------------------------------------- Umwandlung

        static Ereignis Umwandeln(EventRecord rec, string log)
        {
            var e = new Ereignis
            {
                Log = log,
                Anbieter = rec.ProviderName,
                Id = rec.Id,
                Level = rec.Level.HasValue ? rec.Level.Value : 0,
                ZeitUtc = rec.TimeCreated.HasValue ? Zeit.Utc(rec.TimeCreated.Value) : null,
            };
            string xml = null;
            try { xml = rec.ToXml(); } catch (Exception) { /* Rueckfall unten ueber Properties */ }
            if (xml != null && FelderAusXml(xml, e.Felder)) { }
            else FelderAusProperties(rec, e.Felder);

            // 6008: die Textfelder tragen U+200E und ein lokales Datumsformat; nur das Binaerfeld ist sprachneutral.
            if (e.Id == 6008 && string.Equals(e.Anbieter, "EventLog", StringComparison.OrdinalIgnoreCase)) Zeit6008(e);
            return e;
        }

        /// <summary>
        /// EventData ueber Data[@Name]; unbenannte Felder (klassische Anbieter wie EventLog, User32,
        /// Chkdsk) heissen "0", "1", ...; Binary als Hex-Text. UserData (einige Manifest-Anbieter)
        /// als Blattelemente unter ihrem lokalen Namen.
        /// </summary>
        static bool FelderAusXml(string xml, Dictionary<string, string> felder)
        {
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.LoadXml(xml);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("e", EventNs);
                int unbenannt = 0;
                var daten = doc.SelectNodes("/e:Event/e:EventData/*", ns);
                if (daten != null)
                    foreach (XmlNode k in daten)
                    {
                        var el = k as XmlElement;
                        if (el == null) continue;
                        if (el.LocalName == "Data")
                        {
                            string name = el.GetAttribute("Name");
                            if (string.IsNullOrEmpty(name)) name = (unbenannt++).ToString(CultureInfo.InvariantCulture);
                            felder[name] = el.InnerText;
                        }
                        else if (el.LocalName == "Binary") felder["Binary"] = el.InnerText;
                    }
                var nutzer = doc.SelectNodes("/e:Event/e:UserData//*", ns);
                if (nutzer != null)
                    foreach (XmlNode k in nutzer)
                    {
                        var el = k as XmlElement;
                        if (el == null) continue;
                        bool blatt = true;
                        foreach (XmlNode c in el.ChildNodes) if (c.NodeType == XmlNodeType.Element) { blatt = false; break; }
                        if (blatt && !felder.ContainsKey(el.LocalName)) felder[el.LocalName] = el.InnerText;
                    }
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Rueckfall ohne XML: Properties nach Index. Nur, wenn ToXml scheitert (defektes Manifest).</summary>
        static void FelderAusProperties(EventRecord rec, Dictionary<string, string> felder)
        {
            try
            {
                var props = rec.Properties;
                if (props == null) return;
                for (int i = 0; i < props.Count; i++)
                {
                    object v = props[i].Value;
                    var bytes = v as byte[];
                    felder[i.ToString(CultureInfo.InvariantCulture)] = bytes != null ? Hex(bytes) : (v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture));
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 6008: Binaerfeld = zwei SYSTEMTIME je 16 Byte, die erste in Ortszeit, die zweite in UTC
        /// (gemessen am 08.08.2026: 21:15:08 lokal, 19:15:08 UTC; die zweite ist Byte fuer Byte der
        /// Wert, den Windows unter Reliability\DirtyShutdownTime ablegt, gemessen 13.09.2026).
        /// Die UTC-Struktur wird direkt genommen, ohne Zeitzone: eine Umrechnung der Ortszeit mit
        /// der heutigen Zone liegt nach einem Zonenwechsel und in der doppelten Stunde der
        /// Umstellung eine Stunde daneben. Nur wenn die zweite Struktur fehlt, wird umgerechnet.
        /// </summary>
        static void Zeit6008(Ereignis e)
        {
            string hex = e.Feld("Binary");
            if (string.IsNullOrEmpty(hex) || hex.Length < 32) return;
            byte[] b = AusHex(hex);
            if (b == null || b.Length < 16) return;
            if (b.Length >= 32)
            {
                DateTime? utcDirekt = SystemTime(b, 16);
                if (utcDirekt.HasValue)
                {
                    // SystemTime liefert Kind=Unspecified; Zeit.Utc ruft ToUniversalTime auf und wuerde
                    // Unspecified als Ortszeit deuten - deshalb ausdruecklich Utc.
                    e.Felder["zeitBinaerUtc"] = Zeit.Utc(DateTime.SpecifyKind(utcDirekt.Value, DateTimeKind.Utc));
                    return;
                }
            }
            DateTime? lokal = SystemTime(b, 0);
            if (!lokal.HasValue) return;
            DateTime l = DateTime.SpecifyKind(lokal.Value, DateTimeKind.Unspecified);
            DateTime utc;
            try { utc = TimeZoneInfo.ConvertTimeToUtc(l, TimeZoneInfo.Local); }
            catch (ArgumentException) { utc = l.ToUniversalTime(); }
            e.Felder["zeitBinaerUtc"] = Zeit.Utc(utc);
        }

        static DateTime? SystemTime(byte[] b, int o)
        {
            if (b.Length < o + 16) return null;
            int y = BitConverter.ToUInt16(b, o), m = BitConverter.ToUInt16(b, o + 2), d = BitConverter.ToUInt16(b, o + 6);
            int h = BitConverter.ToUInt16(b, o + 8), mi = BitConverter.ToUInt16(b, o + 10), s = BitConverter.ToUInt16(b, o + 12), ms = BitConverter.ToUInt16(b, o + 14);
            if (y < 1990 || y > 2200 || m < 1 || m > 12 || d < 1 || d > 31 || h > 23 || mi > 59 || s > 59 || ms > 999) return null;
            try { return new DateTime(y, m, d, h, mi, s, ms); } catch (ArgumentOutOfRangeException) { return null; }
        }

        static byte[] AusHex(string hex)
        {
            try
            {
                var b = new byte[hex.Length / 2];
                for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
                return b;
            }
            catch (Exception) { return null; }
        }

        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- gesperrtes Log

        /// <summary>
        /// Diagnostics-Performance/Operational (Startdauer, ID 100). Kanal-ACL nur SYSTEM,
        /// Administratoren, Local/Network Service: nicht erhoeht wirft der Konstruktor
        /// UnauthorizedAccessException. Das ist "gesperrt", nie "keine Startdauer". Jede andere
        /// EventLogException (beschaedigtes Protokoll nach Stromverlust, ungueltige Daten) ist kein
        /// Rechteproblem und steht als "ausnahme" mit Typname in der Fehlerliste.
        /// </summary>
        static void DiagnosePerformance(Systembild s, Stopwatch gesamt)
        {
            const string quelle = "log.diagnostics-performance";
            if (gesamt.ElapsedMilliseconds > BudgetMs) { Sammler.Fehler(s, quelle, Fehler.Zeit, "Zeitbudget ausgeschöpft"); return; }
            try
            {
                string xpath = XPath(null, new[] { 100 }, null, StartdauerTage);
                var q = new EventLogQuery(DiagnosePerfLog, PathType.LogName, xpath) { ReverseDirection = true };
                int n = 0;
                using (var r = new EventLogReader(q))
                {
                    while (n < 50)
                    {
                        EventRecord rec = LeseEreignis(r, "Startdauer-Log");
                        if (rec == null) break;
                        using (rec)
                        {
                            var e = Umwandeln(rec, DiagnosePerfLog);
                            if (e != null) { s.Ereignisse.Eintraege.Add(e); n++; }
                        }
                    }
                }
                // 0 Ereignisse mit erfolgreichem Zugriff sind hier Daten (auf Build 26200 seit 08/2025 nicht mehr geschrieben).
            }
            catch (EventLogNotFoundException ex) { Sammler.Fehler(s, quelle, Fehler.Fehlt, "Kanal nicht vorhanden: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Sperren(s, quelle, ex); }
            catch (TimeoutException ex) { Sammler.Fehler(s, quelle, Fehler.Zeit, ex.Message); }
            catch (Exception ex) { Sammler.Fehler(s, quelle, Fehler.Ausnahme, ex.GetType().Name + ": " + ex.Message); }
        }

        static void Sperren(Systembild s, string quelle, Exception ex)
        {
            if (!s.Ereignisse.Gesperrt.Contains(DiagnosePerfLog)) s.Ereignisse.Gesperrt.Add(DiagnosePerfLog);
            Sammler.Fehler(s, quelle, Fehler.Zugriff, "Protokoll braucht Administratorrechte: " + ex.Message);
        }

        // ---------------------------------------------------------------- Zuverlaessigkeit

        /// <summary>
        /// Win32_ReliabilityStabilityMetrics: Stabilitaetsindex 1..10 je Stunde, 736 Instanzen in
        /// 141 ms gemessen. WQL kennt kein ORDER BY, also die neueste Instanz selbst suchen.
        /// Win32_ReliabilityRecords (5,7 s) bleibt bewusst draussen.
        /// </summary>
        static void Zuverlaessigkeit(Systembild s)
        {
            const string quelle = "wmi.reliability.stability";
            Sammler.Versuch(s, quelle, () =>
            {
                var liste = Wmi.Abfrage(@"root\cimv2", "SELECT SystemStabilityIndex, TimeGenerated FROM Win32_ReliabilityStabilityMetrics");
                if (liste.Count == 0)
                {
                    // Richtlinie "Configure Reliability WMI Providers" aus: Daten werden binnen einer Stunde geloescht.
                    Sammler.Fehler(s, quelle, Fehler.Fehlt, "Win32_ReliabilityStabilityMetrics lieferte keine Instanz (Zuverlässigkeitsanbieter abgeschaltet?)");
                    return;
                }
                string besteZeit = null; double? bester = null;
                foreach (var mo in liste)
                {
                    string z = Wmi.ZeitUtc(mo, "TimeGenerated");
                    if (z == null) continue;
                    // ISO-UTC-Texte sind lexikografisch sortierbar.
                    if (besteZeit == null || string.CompareOrdinal(z, besteZeit) > 0) { besteZeit = z; bester = Wmi.Zahl(mo, "SystemStabilityIndex"); }
                }
                if (besteZeit == null) { Sammler.Fehler(s, quelle, Fehler.Fehlt, "keine Instanz mit TimeGenerated"); return; }
                s.Zuverlaessigkeit.Index = bester.HasValue ? Math.Round(bester.Value, 3) : (double?)null;
                s.Zuverlaessigkeit.ZeitUtc = besteZeit;
            });
        }
    }
}
