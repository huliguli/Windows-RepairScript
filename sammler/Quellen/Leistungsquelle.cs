using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Arbeitsspeicher und Auslastung: Win32_OperatingSystem (Ausstattung), Leistungszaehler
    /// "Memory" (drei Messungen im Abstand von AbstandMs, Abstand und Quelle stehen im Bild:
    /// Leistung.AbstandMs, Leistung.SpeicherQuelle), Win32_PageFileUsage und die Prozessliste
    /// ueber System.Diagnostics.Process. Alles ohne Erhoehung.
    ///
    /// Widerlegungsrunde R3/SP-27: System.Diagnostics.PerformanceCounter nimmt die ENGLISCHEN
    /// Namen "Memory" / "Available MBytes" auch auf deutschem Windows (die Kategorie heisst dort
    /// "Arbeitsspeicher"; .NET faellt auf den 009-Namenssatz zurueck). Kein PDH-Index noetig.
    /// Scheitert der Zaehler (beschaedigte Zaehlerdatenbank kommt vor), liefert
    /// Win32_PerfFormattedData_PerfOS_Memory dieselben Zahlen per WMI.
    ///
    /// Die Doku-Schwellen ("Troubleshoot performance problems in Windows") gelten fuer Werte, die
    /// laenger als eine Minute anhalten. Der Sammler kann nicht eine Minute warten; er liefert
    /// mehrere Messungen, und die Regel bewertet nur, wenn genug davon da sind.
    ///
    /// Die Messungen laufen in einem eigenen Thread ueber den ganzen Lauf verteilt (Start in
    /// Sammler.Erfassen, Abschluss im Leistungs-Schritt): drei Werte im Abstand von vier Sekunden
    /// statt eines Fensters von drei Sekunden, ohne den Lauf zu verlaengern. Das Konzept nannte
    /// 20 s; das haette den Lauf verdoppelt (Wahl, Schwellen.MessabstandMinMs).
    /// </summary>
    public static class Leistungsquelle
    {
        /// <summary>Abstand der Speichermessungen (siehe Klassenkommentar; Schwellen.MessabstandMinMs ist die Grenze der Regel).</summary>
        const int AbstandMs = 4000;
        /// <summary>Wie lange der Leistungs-Schritt hoechstens auf die letzte Messung wartet.</summary>
        const int WartenMaxMs = 9000;

        /// <summary>Die laufende Hintergrundmessung; null, wenn keine gestartet wurde.</summary>
        static Speichermessung _laufend;

        /// <summary>
        /// Startet die Speichermessung im Hintergrund. Sammler.Erfassen ruft das als Erstes auf;
        /// Erfassen() dieser Klasse holt das Ergebnis ab. Ohne vorherigen Start misst Erfassen()
        /// selbst (dann eng hintereinander, Abstand steht im Bild).
        /// </summary>
        public static void SpeicherMessungStarten()
        {
            var m = new Speichermessung();
            _laufend = m;
            m.Starten();
        }

        sealed class Speichermessung
        {
            public readonly List<int> Frei = new List<int>();
            public readonly List<int> Commit = new List<int>();
            public string Quelle;            // "perf" | "wmi" | null
            public Exception Fehler;
            public readonly Stopwatch Uhr = Stopwatch.StartNew();
            readonly ManualResetEvent _fertig = new ManualResetEvent(false);

            public void Starten()
            {
                var t = new Thread(Lauf) { IsBackground = true, Name = "speichermessung" };
                t.Start();
            }

            public bool Warten(int ms) { return _fertig.WaitOne(ms); }

            void Lauf()
            {
                try
                {
                    int n = Kern.Regeln.Schwellen.MessungenNoetig;
                    try
                    {
                        using (var pcFrei = new PerformanceCounter("Memory", "Available MBytes", true))
                        using (var pcCommit = new PerformanceCounter("Memory", "% Committed Bytes In Use", true))
                        {
                            pcFrei.NextValue(); pcCommit.NextValue();   // erster Wert verworfen, einheitlich mit Ratenzaehlern
                            Quelle = "perf";
                            for (int i = 0; i < n; i++)
                            {
                                if (i > 0) Thread.Sleep(AbstandMs);
                                lock (Frei) { Frei.Add((int)Math.Round(pcFrei.NextValue())); Commit.Add((int)Math.Round(pcCommit.NextValue())); }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Beschaedigte Zaehlerdatenbank: dieselben Zahlen per WMI. Was der Zaehler
                        // schon geliefert hat, wird verworfen, damit die Serie aus EINER Quelle kommt.
                        lock (Frei) { Frei.Clear(); Commit.Clear(); }
                        Quelle = "wmi";
                        Fehler = ex;
                        for (int i = 0; i < n; i++)
                        {
                            if (i > 0) Thread.Sleep(AbstandMs);
                            var m = Wmi.Abfrage(@"root\cimv2", "SELECT AvailableMBytes, PercentCommittedBytesInUse FROM Win32_PerfFormattedData_PerfOS_Memory");
                            if (m.Count == 0) throw new InvalidOperationException("Win32_PerfFormattedData_PerfOS_Memory lieferte keine Instanz");
                            lock (Frei) { Frei.Add(Wmi.Ganz(m[0], "AvailableMBytes") ?? 0); Commit.Add(Wmi.Ganz(m[0], "PercentCommittedBytesInUse") ?? 0); }
                        }
                    }
                }
                catch (Exception ex) { Fehler = ex; }
                finally { _fertig.Set(); }
            }
        }
        /// <summary>Abstand der beiden Prozessorzeit-Messungen je Prozess.</summary>
        const int CpuAbstandMs = 500;
        const int TopProzesse = 10;

        public static void Erfassen(Systembild s)
        {
            var L = s.Leistung;

            Sammler.Versuch(s, "wmi.os.speicher", () =>
            {
                var os = Wmi.Abfrage(@"root\cimv2", "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                if (os.Count == 0) throw new InvalidOperationException("Win32_OperatingSystem lieferte keine Instanz");
                L.RamGesamtKB = Wmi.Lang(os[0], "TotalVisibleMemorySize") ?? 0;
                L.RamFreiKB = Wmi.Lang(os[0], "FreePhysicalMemory") ?? 0;
            });

            // Hintergrundmessung abholen (Start in Sammler.Erfassen); ohne Start hier nachholen.
            var m = _laufend;
            if (m == null) { m = new Speichermessung(); m.Starten(); }
            _laufend = null;
            Sammler.Versuch(s, "perf.speicher", () =>
            {
                m.Warten(WartenMaxMs);
                List<int> frei, commit;
                lock (m.Frei) { frei = new List<int>(m.Frei); commit = new List<int>(m.Commit); }
                if (frei.Count == 0)
                {
                    if (m.Fehler != null) throw m.Fehler;
                    throw new TimeoutException("Speichermessung lieferte in " + WartenMaxMs + " ms keinen Wert");
                }
                L.VerfuegbarMB = frei;
                L.CommitPct = commit;
                L.AbstandMs = frei.Count > 1 ? AbstandMs : 0;
                L.SpeicherQuelle = m.Quelle;
                if (m.Quelle == "wmi" && m.Fehler != null)
                    Sammler.Fehler(s, "perf.speicher", Fehler.Ausnahme, "Leistungszähler nicht lesbar, Werte aus Win32_PerfFormattedData_PerfOS_Memory: " + m.Fehler.Message);
                if (frei.Count < Kern.Regeln.Schwellen.MessungenNoetig)
                    Sammler.Fehler(s, "perf.speicher", Fehler.Zeit, "nur " + frei.Count + " von " + Kern.Regeln.Schwellen.MessungenNoetig + " Speichermessungen im Zeitbudget");
            });

            Sammler.Versuch(s, "wmi.auslagerung", () =>
            {
                var pf = Wmi.Abfrage(@"root\cimv2", "SELECT AllocatedBaseSize FROM Win32_PageFileUsage");
                // Keine Instanz = keine Auslagerungsdatei (abgeschaltet), das ist ein Messwert von 0 MB, kein Fehler.
                long summe = 0;
                foreach (var mo in pf) summe += Wmi.Lang(mo, "AllocatedBaseSize") ?? 0;
                L.AuslagerungMB = summe > int.MaxValue ? int.MaxValue : (int)summe;
            });

            Sammler.Versuch(s, "prozesse", () => Prozesse(L));
        }

        // ---------------------------------------------------------------- Prozesse

        /// <summary>
        /// Top-Verbraucher nach Arbeitsspeicher, CPU aus zwei TotalProcessorTime-Messungen, auf die
        /// Kernzahl normiert (100 % = alle Kerne). Pfad nur fuer die Top-Prozesse und nur, wenn der
        /// Zugriff erlaubt ist: fremde Prozesse werfen ohne Erhoehung Win32Exception, das ist
        /// dann Pfad null, kein Fehler (Widerlegungsrunde SP-29: nur eigene Prozesse ohne Admin).
        /// </summary>
        static void Prozesse(Leistung L)
        {
            var alle = Process.GetProcesses();
            try
            {
                var t0 = new Dictionary<int, TimeSpan>();
                foreach (var p in alle)
                {
                    try { t0[p.Id] = p.TotalProcessorTime; }
                    catch (Exception) { /* Idle, System, geschuetzte Prozesse: keine Prozessorzeit lesbar */ }
                }
                var uhr = Stopwatch.StartNew();
                Thread.Sleep(CpuAbstandMs);
                double wandMs = Math.Max(1, uhr.Elapsed.TotalMilliseconds);
                int kerne = Math.Max(1, Environment.ProcessorCount);

                var liste = new List<KeyValuePair<Process, Prozess>>();
                foreach (var p in alle)
                {
                    try
                    {
                        var e = new Prozess { Pid = p.Id, Name = p.ProcessName, ArbeitsspeicherBytes = p.WorkingSet64 };
                        TimeSpan a;
                        if (t0.TryGetValue(p.Id, out a))
                        {
                            try
                            {
                                double cpu = (p.TotalProcessorTime - a).TotalMilliseconds / wandMs * 100.0 / kerne;
                                e.CpuPct = Math.Round(Math.Max(0, cpu), 1);
                            }
                            catch (Exception) { }
                        }
                        liste.Add(new KeyValuePair<Process, Prozess>(p, e));
                    }
                    catch (Exception) { /* inzwischen beendet */ }
                }

                var top = liste.OrderByDescending(x => x.Value.ArbeitsspeicherBytes).Take(TopProzesse).ToList();
                foreach (var kv in top)
                {
                    try { kv.Value.Pfad = kv.Key.MainModule.FileName; }
                    catch (Exception) { kv.Value.Pfad = null; }
                }
                L.Prozesse = top.Select(x => x.Value).ToList();
            }
            finally
            {
                foreach (var p in alle) { try { p.Dispose(); } catch (Exception) { } }
            }
        }
    }
}
