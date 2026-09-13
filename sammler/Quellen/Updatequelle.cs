using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Windows Update, komplett im eigenen Prozess: COM (Windows Update Agent ueber die
    /// ProgIDs Microsoft.Update.Session / AutoUpdate / ServiceManager, spaet gebunden per
    /// InvokeMember, damit keine Interop-Assembly noetig ist), Registry, Win32_Service,
    /// Win32_QuickFixEngineering und das System-Protokoll (WindowsUpdateClient 19/20).
    ///
    /// Warum zwei Quellen fuer Fehlschlaege: der COM-Verlauf beginnt nach jedem Reset von
    /// SoftwareDistribution neu (hier am 04.09.2026) und zeigte 0 Fehlschlaege, waehrend das
    /// System-Protokoll 13 in 90 Tagen kannte, neunmal denselben HP-USB-Treiber. Das Protokoll
    /// wird deshalb hier selbst gelesen, nicht ueber die Ereignisquelle.
    ///
    /// Zeiten: IUpdateHistoryEntry.Date kommt mit Kind=Unspecified. Gemessen am 12.09.2026 auf
    /// diesem Rechner (Zeitzone UTC+2): 16 von 18 Verlaufseintraegen decken sich sekundengenau
    /// mit der SystemTime (UTC) des Ereignisses 19 derselben UpdateID, wenn man Date als UTC
    /// liest; als Ortszeit gelesen laegen sie zwei Stunden daneben. Date wird deshalb als UTC
    /// gedeutet. AutomaticUpdatesResults sind laut Doku UTC.
    ///
    /// Es wird nie online gesucht: der Searcher bekommt Online=false, und die Suche bricht ab,
    /// wenn sich das nicht setzen laesst. Nichts hier veraendert das System.
    /// </summary>
    public static class Updatequelle
    {
        /// <summary>
        /// Eine COM-Stufe dauerte hier 0,4 bis 1,6 s (Suche im Cache, Verlauf mit 26 Eintraegen);
        /// 5 s ist die Reissleine, wie Wmi.StandardZeitMs. Antwortet der Update-Agent einmal
        /// nicht, werden die uebrigen COM-Stufen nicht mehr versucht (derselbe Dienst wuauserv).
        /// </summary>
        const int ComZeitMs = 5000;
        const int LogZeitMs = 15000;
        /// <summary>Gesamtbudget des Schritts (Sammler.Schritt setzt keins): danach wird jede weitere Quelle mit "zeit" eingetragen.</summary>
        const int BudgetMs = 30000;
        /// <summary>Mehr Verlauf braucht keine Regel; auf alten Rechnern ohne Reset sind es Tausende.</summary>
        const int VerlaufMax = 1000;
        public const int LogTage = 90;
        /// <summary>Wert in WindowsUpdate.Dienste fuer einen der sieben Dienste, den Win32_Service nicht kennt (Debloat-Skripte entfernen DoSvc).</summary>
        public const string DienstFehlt = "fehlt";

        static readonly string[] DienstNamen = { "wuauserv", "bits", "cryptsvc", "msiserver", "TrustedInstaller", "UsoSvc", "DoSvc" };

        /// <summary>Ein Lauf des Schritts: Budget-Uhr und ob der Update-Agent schon einmal nicht antwortete.</summary>
        class Lauf
        {
            public readonly Stopwatch Uhr = Stopwatch.StartNew();
            public bool ComHaengt;
        }

        public static void Erfassen(Systembild s)
        {
            var u = s.WindowsUpdate;
            var lauf = new Lauf();
            Com(s, lauf, "com.windowsupdate.verlauf", () => Verlauf(s, u));
            Com(s, lauf, "com.windowsupdate.suche.ausstehend", () => Suche(s, "com.windowsupdate.suche.ausstehend", "IsInstalled=0 and IsHidden=0", u.Ausstehend));
            Com(s, lauf, "com.windowsupdate.suche.verborgen", () => Suche(s, "com.windowsupdate.suche.verborgen", "IsHidden=1", u.Verborgen));
            Com(s, lauf, "com.windowsupdate.autoupdate", () => AutoUpdate(s, u));
            Com(s, lauf, "com.windowsupdate.dienste", () => ServiceManager(s, u));
            Quelle(s, lauf, "registry.windowsupdate.neustart", () => NeustartKennzeichen(u));
            Quelle(s, lauf, "registry.windowsupdate.policy", () => Policy(u));
            Quelle(s, lauf, "wmi.service.windowsupdate", () => Dienste(s, u));
            Quelle(s, lauf, "wmi.qfe", () => Qfe(s, u));
            Quelle(s, lauf, "log.system.windowsupdateclient", () => LogEreignisse(s, u));
        }

        /// <summary>Wie Sammler.Versuch, aber erst nach Blick auf das Budget: ist es aufgebraucht, gibt es "zeit" statt eines weiteren Versuchs.</summary>
        static void Quelle(Systembild s, Lauf lauf, string quelle, Action a)
        {
            if (lauf.Uhr.ElapsedMilliseconds > BudgetMs)
            {
                Sammler.Fehler(s, quelle, Fehler.Zeit, "Zeitbudget von " + (BudgetMs / 1000) + " s für Windows Update ausgeschöpft, Abfrage übersprungen");
                return;
            }
            Sammler.Versuch(s, quelle, a);
        }

        /// <summary>Eine COM-Stufe: nach dem ersten Zeitablauf werden die uebrigen nicht mehr versucht (jede haette wieder ComZeitMs gekostet).</summary>
        static void Com(Systembild s, Lauf lauf, string quelle, Action a)
        {
            if (lauf.ComHaengt)
            {
                Sammler.Fehler(s, quelle, Fehler.Zeit, "Windows-Update-Agent antwortete zuvor nicht innerhalb von " + ComZeitMs + " ms, Abfrage übersprungen");
                return;
            }
            Quelle(s, lauf, quelle, () =>
            {
                try { a(); }
                catch (TimeoutException) { lauf.ComHaengt = true; throw; }
            });
        }

        // ---------------------------------------------------------------- COM: Verlauf

        static void Verlauf(Systembild s, WindowsUpdate u)
        {
            Type typ = ProgId(s, "com.windowsupdate.verlauf", "Microsoft.Update.Session");
            if (typ == null) return;
            var liste = new List<UpdateEintrag>();
            int gesamt = 0, n = 0, unlesbar = 0;
            Exception ersterFehler = null;
            MitZeitgrenze("verlauf", ComZeitMs, () =>
            {
                object session = null, searcher = null, hist = null;
                try
                {
                    session = Activator.CreateInstance(typ);
                    searcher = Ruf(session, "CreateUpdateSearcher");
                    gesamt = Ganz(Ruf(searcher, "GetTotalHistoryCount"));
                    if (gesamt <= 0) return;
                    // QueryHistory liefert absteigend chronologisch (Doku); die juengsten zuerst.
                    hist = Ruf(searcher, "QueryHistory", 0, Math.Min(gesamt, VerlaufMax));
                    n = Ganz(Hol(hist, "Count"));
                    for (int i = 0; i < n; i++)
                    {
                        object e = null, id = null;
                        try
                        {
                            e = Item(hist, i);
                            id = Hol(e, "UpdateIdentity");
                            liste.Add(new UpdateEintrag
                            {
                                ZeitUtc = ComZeitUtc(Hol(e, "Date")),
                                Titel = Hol(e, "Title") as string,
                                Ergebnis = Ganz(Hol(e, "ResultCode")),
                                HResult = Unsigned(Hol(e, "HResult")),
                                UpdateId = Kennung(Hol(id, "UpdateID")),
                                Revision = Ganz(Hol(id, "RevisionNumber")),
                                // UpdateOperation: 1 Installation, 2 Deinstallation. Nur Installationen
                                // zaehlen als Schleife oder Fehlschlag (eine gelungene Deinstallation
                                // traegt ebenfalls ResultCode 2).
                                Vorgang = Ganz(Hol(e, "Operation")),
                                // ServiceID: der Update-Dienst; der Store (855e8a7c-...) installiert
                                // dieselbe Kennung regulaer immer wieder und ist keine Schleife.
                                DienstId = Kennung(Hol(e, "ServiceID")),
                            });
                        }
                        // Ein einzelner unlesbarer Eintrag (UpdateIdentity null, defekter Datensatz)
                        // darf die schon gelesenen nicht mit sich reissen.
                        catch (Exception ex) { unlesbar++; if (ersterFehler == null) ersterFehler = Auspacken(ex); }
                        finally { Frei(id); Frei(e); }
                    }
                }
                finally { Frei(hist); Frei(searcher); Frei(session); }
            });
            if (gesamt <= 0)
            {
                // Ein leerer Verlauf ist ein echter Zustand (direkt nach einem Reset), aber die
                // Regeln muessen ihn von "nicht gelesen" unterscheiden koennen.
                Sammler.Fehler(s, "com.windowsupdate.verlauf", Fehler.Fehlt, "Verlauf leer (0 Einträge); nach einem Update-Reset normal");
                return;
            }
            if (n <= 0)
            {
                Sammler.Fehler(s, "com.windowsupdate.verlauf", Fehler.Fehlt, "QueryHistory lieferte 0 Einträge, obwohl GetTotalHistoryCount " + gesamt + " meldet");
                return;
            }
            // Kein einziger Eintrag lesbar: der erste Fehler sagt, warum (Zugriff, Ausnahme).
            if (liste.Count == 0 && ersterFehler != null) throw ersterFehler;
            u.Verlauf = liste;
            if (unlesbar > 0)
                Sammler.Fehler(s, "com.windowsupdate.verlauf", Fehler.Ausnahme, unlesbar + " von " + n + " Verlaufseinträgen unlesbar (" + ersterFehler.GetType().Name + ": " + ersterFehler.Message + ")");
        }

        // ---------------------------------------------------------------- COM: Suche im Cache

        static void Suche(Systembild s, string quelle, string kriterium, List<UpdateKennung> ziel)
        {
            Type typ = ProgId(s, quelle, "Microsoft.Update.Session");
            if (typ == null) return;
            var liste = new List<UpdateKennung>();
            MitZeitgrenze("suche", ComZeitMs, () =>
            {
                object session = null, searcher = null, res = null, ups = null;
                try
                {
                    session = Activator.CreateInstance(typ);
                    searcher = Ruf(session, "CreateUpdateSearcher");
                    Setz(searcher, "Online", false);
                    // Gegenprobe vor der Suche: nie online gehen. Laesst sich Online nicht
                    // abschalten, ist "keine Antwort" besser als eine Netzsuche.
                    if (Convert.ToBoolean(Hol(searcher, "Online"), CultureInfo.InvariantCulture))
                        throw new InvalidOperationException("Online ließ sich nicht auf false setzen; Suche abgebrochen");
                    res = Ruf(searcher, "Search", kriterium);
                    // OperationResultCode: 2 Succeeded, 3 SucceededWithErrors - alles andere ist keine Antwort.
                    int rc = Ganz(Hol(res, "ResultCode"));
                    if (rc != 2 && rc != 3) throw new InvalidOperationException("Suche „" + kriterium + "“ meldete ResultCode " + rc);
                    ups = Hol(res, "Updates");
                    int n = Ganz(Hol(ups, "Count"));
                    for (int i = 0; i < n; i++)
                    {
                        object up = null, idn = null;
                        try
                        {
                            up = Item(ups, i);
                            idn = Hol(up, "Identity");
                            liste.Add(new UpdateKennung
                            {
                                UpdateId = Kennung(Hol(idn, "UpdateID")),
                                Revision = Ganz(Hol(idn, "RevisionNumber")),
                                Titel = Hol(up, "Title") as string,
                                Typ = Ganz(Hol(up, "Type")),   // UpdateType: 1 Software, 2 Treiber
                            });
                        }
                        finally { Frei(idn); Frei(up); }
                    }
                }
                finally { Frei(ups); Frei(res); Frei(searcher); Frei(session); }
            });
            // Eine leere Liste nach ResultCode 2 ist eine Antwort ("nichts ausstehend"), kein Fehler.
            ziel.Clear();
            ziel.AddRange(liste);
        }

        // ---------------------------------------------------------------- COM: AutoUpdate, ServiceManager

        static void AutoUpdate(Systembild s, WindowsUpdate u)
        {
            Type typ = ProgId(s, "com.windowsupdate.autoupdate", "Microsoft.Update.AutoUpdate");
            if (typ == null) return;
            string suche = null, install = null;
            MitZeitgrenze("autoupdate", ComZeitMs, () =>
            {
                object au = null, r = null;
                try
                {
                    au = Activator.CreateInstance(typ);
                    r = Hol(au, "Results");
                    // IAutomaticUpdatesResults: beide Zeiten sind laut Doku UTC.
                    suche = ComZeitUtc(Hol(r, "LastSearchSuccessDate"));
                    install = ComZeitUtc(Hol(r, "LastInstallationSuccessDate"));
                }
                finally { Frei(r); Frei(au); }
            });
            u.LetzteSucheUtc = suche;
            u.LetzteInstallationUtc = install;
            if (suche == null && install == null)
                Sammler.Fehler(s, "com.windowsupdate.autoupdate", Fehler.Fehlt, "AutoUpdate.Results ohne Datum (noch nie erfolgreich gesucht oder installiert)");
        }

        static void ServiceManager(Systembild s, WindowsUpdate u)
        {
            Type typ = ProgId(s, "com.windowsupdate.dienste", "Microsoft.Update.ServiceManager");
            if (typ == null) return;
            bool? verwaltet = null;
            MitZeitgrenze("servicemanager", ComZeitMs, () =>
            {
                object sm = null, svcs = null;
                try
                {
                    sm = Activator.CreateInstance(typ);
                    svcs = Hol(sm, "Services");
                    int n = Ganz(Hol(svcs, "Count"));
                    for (int i = 0; i < n; i++)
                    {
                        object sv = null;
                        try
                        {
                            sv = Item(svcs, i);
                            // Nur der Standard-AU-Dienst zaehlt: IsManaged sagt, ob ein
                            // Verwaltungsserver (WSUS) die Updates vorgibt.
                            if (Convert.ToBoolean(Hol(sv, "IsDefaultAUService"), CultureInfo.InvariantCulture))
                                verwaltet = Convert.ToBoolean(Hol(sv, "IsManaged"), CultureInfo.InvariantCulture);
                        }
                        finally { Frei(sv); }
                    }
                }
                finally { Frei(svcs); Frei(sm); }
            });
            if (verwaltet.HasValue) u.Verwaltet = verwaltet;
            else Sammler.Fehler(s, "com.windowsupdate.dienste", Fehler.Fehlt, "kein Update-Dienst mit IsDefaultAUService registriert");
        }

        // ---------------------------------------------------------------- Registry

        static void NeustartKennzeichen(WindowsUpdate u)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    u.NeustartWu = SchluesselDa(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                    u.NeustartCbs = SchluesselDa(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                    using (var k = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
                    {
                        if (k == null) throw new InvalidOperationException("Schlüssel Session Manager fehlt");
                        // REG_MULTI_SZ mit Paaren Quelle/Ziel (MoveFileEx MOVEFILE_DELAY_UNTIL_REBOOT).
                        // Gemessen: hier 4 Eintraege mit Praefix "*1" ohne Update im Spiel -
                        // Installer und Virenschutz nutzen den Wert ebenfalls, allein ist er kein
                        // Neustart-Signal (die Regel wertet ihn nur als Detail).
                        object v = k.GetValue("PendingFileRenameOperations");
                        var arr = v as string[];
                        if (arr != null) u.PendingRenames = Array.Exists(arr, x => !string.IsNullOrEmpty(x));
                        else u.PendingRenames = !string.IsNullOrEmpty(v as string);
                    }
                }
            }
            catch (System.Security.SecurityException ex) { throw new UnauthorizedAccessException(ex.Message, ex); }
        }

        /// <summary>
        /// Gruppenrichtlinie "Internen Pfad fuer den Microsoft Updatedienst angeben": ein
        /// zweites, vom Update-Agenten unabhaengiges Signal fuer "verwaltet". Fehlt der Schluessel,
        /// bleibt der Wert aus dem ServiceManager stehen; hier fehlen beide Policy-Schluessel.
        /// </summary>
        static void Policy(WindowsUpdate u)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var au = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"))
                using (var wu = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate"))
                {
                    if (au == null || wu == null) return;
                    object use = au.GetValue("UseWUServer");
                    string server = wu.GetValue("WUServer") as string;
                    if (use is int && (int)use == 1 && !string.IsNullOrWhiteSpace(server)) u.Verwaltet = true;
                }
            }
            catch (System.Security.SecurityException ex) { throw new UnauthorizedAccessException(ex.Message, ex); }
        }

        static bool SchluesselDa(RegistryKey basis, string pfad)
        {
            using (var k = basis.OpenSubKey(pfad)) return k != null;
        }

        // ---------------------------------------------------------------- WMI: Dienste, QFE

        static void Dienste(Systembild s, WindowsUpdate u)
        {
            var bedingungen = new List<string>();
            foreach (string n in DienstNamen) bedingungen.Add("Name='" + n + "'");
            var liste = Wmi.Abfrage(@"root\cimv2", "SELECT Name, StartMode, State FROM Win32_Service WHERE " + string.Join(" OR ", bedingungen));
            var gefunden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mo in liste)
            {
                string name = Wmi.Str(mo, "Name");
                if (string.IsNullOrEmpty(name)) continue;
                gefunden.Add(name);
                // StartMode (Boot/System/Auto/Manual/Disabled, gemessen auch 'Unknown') und State
                // sind englische Enum-Texte der Klasse, keine Lokalisierung.
                u.Dienste[name.ToLowerInvariant()] = (Wmi.Str(mo, "StartMode") ?? "?") + "/" + (Wmi.Str(mo, "State") ?? "?");
                mo.Dispose();
            }
            if (gefunden.Count == 0) { Sammler.Fehler(s, "wmi.service.windowsupdate", Fehler.Fehlt, "Win32_Service lieferte keinen der " + DienstNamen.Length + " Update-Dienste"); return; }
            // Ein nicht registrierter Einzeldienst ist ein Datum ueber diesen PC, kein Fehler der
            // Quelle: er steht als "fehlt" in der Liste, die Quelle liefert Daten ODER einen Fehler.
            foreach (string n in DienstNamen) if (!gefunden.Contains(n)) u.Dienste[n.ToLowerInvariant()] = DienstFehlt;
        }

        static void Qfe(Systembild s, WindowsUpdate u)
        {
            // Die Klasse ist langsam (450 ms hier, auf alten Rechnern mehrere Sekunden).
            var liste = Wmi.Abfrage(@"root\cimv2", "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering", 15000);
            if (liste.Count == 0) { Sammler.Fehler(s, "wmi.qfe", Fehler.Fehlt, "Win32_QuickFixEngineering lieferte 0 Einträge"); return; }
            DateTime? max = null;
            int sicherheit = 0, unlesbar = 0;
            foreach (var mo in liste)
            {
                // "Security Update" ist ein englisches Literal der Klasse, auch auf deutschem
                // Windows (gemessen). Es ist ein Indiz: .NET-Sicherheitsupdates tragen nur "Update".
                if (string.Equals(Wmi.Str(mo, "Description"), "Security Update", StringComparison.OrdinalIgnoreCase))
                {
                    sicherheit++;
                    var d = QfeDatum(Wmi.Str(mo, "InstalledOn"));
                    if (!d.HasValue) unlesbar++;
                    else if (!max.HasValue || d.Value > max.Value) max = d;
                }
                mo.Dispose();
            }
            if (max.HasValue) u.LetztesSicherheitsupdateUtc = Zeit.Utc(DateTime.SpecifyKind(max.Value, DateTimeKind.Utc));
            else if (sicherheit == 0) Sammler.Fehler(s, "wmi.qfe", Fehler.Fehlt, "kein Eintrag mit Description 'Security Update' unter " + liste.Count + " Einträgen");
            else Sammler.Fehler(s, "wmi.qfe", Fehler.Fehlt, "InstalledOn bei allen " + sicherheit + " Sicherheitsupdates unlesbar (" + unlesbar + ")");
        }

        /// <summary>
        /// InstalledOn kommt ueber System.Management als Text "M/d/yyyy" (gemessen: "9/11/2026";
        /// DateTime.Parse mit deutscher Kultur machte daraus den 9. November). Rueckfaelle: ein
        /// CIM-Datum, ein 16-stelliges Hex-FILETIME (aeltere Windows-Versionen) und zuletzt der
        /// invariante Parser. Das Datum ist tagesgenau; es wird als UTC-Mitternacht gefuehrt.
        /// </summary>
        static DateTime? QfeDatum(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            DateTime d;
            if (DateTime.TryParseExact(t, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            if (t.Length == 16)
            {
                long ft;
                if (long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ft) && ft > 0)
                {
                    try { return DateTime.FromFileTimeUtc(ft).Date; } catch (Exception) { }
                }
            }
            if (t.Length >= 14 && char.IsDigit(t[0]) && t.IndexOf('/') < 0)
            {
                try { return ManagementDateTimeConverter.ToDateTime(t).ToUniversalTime().Date; } catch (Exception) { }
            }
            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d)) return d.Date;
            return null;
        }

        // ---------------------------------------------------------------- System-Protokoll

        /// <summary>
        /// Microsoft-Windows-WindowsUpdateClient 19 (Installation erfolgreich) und 20
        /// (Installationsfehler) der letzten 90 Tage. Felder ueber Data[@Name], nie ueber den
        /// Meldungstext: updateGuid ist sprachneutral und passt zur UpdateID des Verlaufs;
        /// serviceGuid nennt den Update-Dienst (Store 855e8a7c-..., gemessen am 13.09.2026);
        /// errorCode kommt als Int32 mit Vorzeichen (gemessen -2145116149) und wird als
        /// vorzeichenlose 32-Bit-Zahl gefuehrt. Das System-Protokoll ist ohne Erhoehung lesbar;
        /// ein gesperrtes Log wirft schon der EventLogReader-Konstruktor (nicht "0 Ereignisse").
        /// </summary>
        static void LogEreignisse(Systembild s, WindowsUpdate u)
        {
            var fehl = new List<UpdateFehler>();
            var erfolg = new List<UpdateFehler>();
            bool logLeer = false;
            MitZeitgrenze("log", LogZeitMs, () =>
            {
                long ms = LogTage * 86400000L;
                string xp = "*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=19 or EventID=20)"
                          + " and TimeCreated[timediff(@SystemTime) <= " + ms.ToString(CultureInfo.InvariantCulture) + "]]]";
                var sel = new EventLogPropertySelector(new[]
                {
                    "Event/EventData/Data[@Name='errorCode']",
                    "Event/EventData/Data[@Name='updateGuid']",
                    "Event/EventData/Data[@Name='updateTitle']",
                    "Event/EventData/Data[@Name='serviceGuid']",
                });
                int gesamt = 0;
                using (var rd = new EventLogReader(new EventLogQuery("System", PathType.LogName, xp)))
                {
                    EventRecord rec;
                    while ((rec = rd.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            gesamt++;
                            long code = 0; string guid = null, titel = null, dienst = null;
                            var elr = rec as EventLogRecord;
                            if (elr != null)
                            {
                                IList<object> v = null;
                                try { v = elr.GetPropertyValues(sel); } catch (Exception) { v = null; }
                                if (v != null && v.Count >= 4)
                                {
                                    code = Unsigned(v[0]);
                                    guid = Kennung(v[1]);
                                    titel = v[2] as string;
                                    dienst = Kennung(v[3]);
                                }
                            }
                            if (guid == null) AusXml(rec, ref code, ref guid, ref titel, ref dienst);
                            var f = new UpdateFehler
                            {
                                ZeitUtc = rec.TimeCreated.HasValue ? Zeit.Utc(rec.TimeCreated.Value) : null,
                                UpdateId = guid,
                                Titel = titel,
                                FehlerCode = code,
                                DienstId = dienst,
                            };
                            if (rec.Id == 20) fehl.Add(f);
                            else if (rec.Id == 19) erfolg.Add(f);
                        }
                    }
                }
                // Gegenprobe bei 0 Treffern: hat das Log ueberhaupt Eintraege im Zeitraum? Sonst
                // ist "0 Fehlschlaege" nur "Log leer oder frisch" und wird als "fehlt" gefuehrt.
                if (gesamt == 0)
                {
                    var q2 = new EventLogQuery("System", PathType.LogName,
                        "*[System[TimeCreated[timediff(@SystemTime) <= " + ms.ToString(CultureInfo.InvariantCulture) + "]]]") { ReverseDirection = true };
                    using (var rd2 = new EventLogReader(q2))
                    using (var einer = rd2.ReadEvent())
                        logLeer = einer == null;
                }
            });
            u.FehlschlaegeLog = fehl;
            u.ErfolgeLog = erfolg;
            if (logLeer) Sammler.Fehler(s, "log.system.windowsupdateclient", Fehler.Fehlt, "System-Protokoll enthält keine Ereignisse der letzten " + LogTage + " Tage");
        }

        /// <summary>Rueckfall, wenn der Eigenschaftswaehler nichts liefert: dieselben Felder aus dem XML.</summary>
        static void AusXml(EventRecord rec, ref long code, ref string guid, ref string titel, ref string dienst)
        {
            string xml;
            try { xml = rec.ToXml(); } catch (Exception) { return; }
            if (string.IsNullOrEmpty(xml)) return;
            try
            {
                var doc = new System.Xml.XmlDocument { XmlResolver = null };
                doc.LoadXml(xml);
                var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
                var g = doc.SelectSingleNode("//e:EventData/e:Data[@Name='updateGuid']", ns);
                var t = doc.SelectSingleNode("//e:EventData/e:Data[@Name='updateTitle']", ns);
                var c = doc.SelectSingleNode("//e:EventData/e:Data[@Name='errorCode']", ns);
                var d = doc.SelectSingleNode("//e:EventData/e:Data[@Name='serviceGuid']", ns);
                if (g != null) guid = Kennung(g.InnerText);
                if (t != null) titel = t.InnerText;
                if (c != null) code = Unsigned(c.InnerText);
                if (d != null) dienst = Kennung(d.InnerText);
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- COM-Helfer

        static Type ProgId(Systembild s, string quelle, string progId)
        {
            Type t = null;
            try { t = Type.GetTypeFromProgID(progId, false); } catch (Exception) { t = null; }
            if (t == null) Sammler.Fehler(s, quelle, Fehler.Fehlt, "ProgID " + progId + " nicht registriert");
            return t;
        }

        /// <summary>
        /// Wie Wmi.Abfrage: die COM-Stufe laeuft auf einem eigenen Thread und wird nach Ablauf
        /// aufgegeben (der Thread ist Hintergrund, seine COM-Objekte gibt er selbst frei).
        /// Eine Ausnahme aus InvokeMember steckt in TargetInvocationException und wird
        /// ausgepackt, damit der Sammler "Zugriff verweigert" (0x80070005) als solches erkennt.
        /// </summary>
        static void MitZeitgrenze(string name, int zeitMs, Action a)
        {
            Exception fehler = null;
            var t = new Thread(() => { try { a(); } catch (Exception ex) { fehler = ex; } }) { IsBackground = true, Name = "wu:" + name };
            t.Start();
            if (!t.Join(zeitMs)) throw new TimeoutException("Windows-Update-Abfrage „" + name + "“ überschritt " + zeitMs + " ms");
            if (fehler != null) throw Auspacken(fehler);
        }

        static Exception Auspacken(Exception ex)
        {
            var ti = ex as TargetInvocationException;
            return ti != null && ti.InnerException != null ? ti.InnerException : ex;
        }

        static object Ruf(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args, CultureInfo.InvariantCulture);
        }

        static object Hol(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null, CultureInfo.InvariantCulture);
        }

        static void Setz(object o, string name, object wert)
        {
            o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { wert }, CultureInfo.InvariantCulture);
        }

        static object Item(object sammlung, int index)
        {
            return sammlung.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, sammlung, new object[] { index }, CultureInfo.InvariantCulture);
        }

        static void Frei(object o)
        {
            if (o == null) return;
            try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- Wert-Helfer

        static int Ganz(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }

        /// <summary>HRESULT und errorCode als vorzeichenlose 32-Bit-Zahl (0x8024402C statt -2145107924).</summary>
        static long Unsigned(object o)
        {
            if (o == null) return 0;
            if (o is int) return unchecked((uint)(int)o);
            if (o is uint) return (uint)o;
            if (o is long) return (long)o & 0xFFFFFFFFL;
            if (o is ulong) return (long)((ulong)o & 0xFFFFFFFFUL);
            string t = o as string;
            if (t != null)
            {
                t = t.Trim();
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                uint hex;
                if (uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex)) return hex;
                long dez;
                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out dez)) return dez & 0xFFFFFFFFL;
                return 0;
            }
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture) & 0xFFFFFFFFL; } catch (Exception) { return 0; }
        }

        /// <summary>UpdateID als kleingeschriebene GUID ohne Klammern - Verlauf, Suche und Log liefern verschiedene Schreibweisen.</summary>
        static string Kennung(object o)
        {
            if (o == null) return null;
            if (o is Guid) return ((Guid)o).ToString("D");
            string t = Convert.ToString(o, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(t)) return null;
            Guid g;
            if (Guid.TryParse(t.Trim(), out g)) return g.ToString("D");
            return t.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// COM-Datum nach ISO-UTC. Kind=Unspecified wird als UTC gelesen (Messung im
        /// Klassenkommentar). Ein VT_DATE von 0 ergibt den 30.12.1899: "nie".
        /// </summary>
        static string ComZeitUtc(object o)
        {
            if (!(o is DateTime)) return null;
            var d = (DateTime)o;
            if (d.Year < 1980) return null;
            if (d.Kind == DateTimeKind.Local) return Zeit.Utc(d);
            return Zeit.Utc(DateTime.SpecifyKind(d, DateTimeKind.Utc));
        }
    }
}
