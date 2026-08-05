using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace WartungsToolbox
{
    /// <summary>
    /// Die beiden Suchlaeufe, die neben dem Hauptweg stehen:
    ///
    ///   * "Wo steckt der Platz?"  (src/StorageScan.cs)
    ///   * "Eintraege, die ins Leere zeigen"  (src/RegistryScan.cs)
    ///
    /// Beide suchen rein lesend. Erst ein ausdruecklicher zweiter Klick auf einer
    /// Auswahl entfernt etwas, und vor dem Eingriff in die Registrierung legt der Host
    /// einen Wiederherstellungspunkt an - dasselbe Muster wie Schritt 1 in
    /// CheckFlow.FixWorker.
    ///
    /// Grundsatz fuer beide Loeschwege: Die Oberflaeche schickt KENNUNGEN, niemals
    /// Pfade oder Schluesselnamen. Welcher Ordner und welcher Registrierungs-Eintrag
    /// dahintersteckt, entscheidet allein der Quelltext bzw. das gespeicherte Ergebnis
    /// des eigenen Laufs.
    /// </summary>
    public partial class ShellForm
    {
        Thread _scanThread;
        volatile bool _scanCancel;

        // Laeuft gerade etwas, das ETWAS ENTFERNT? Dann darf das Fenster nicht einfach
        // zugehen und der Hauptweg nicht dazwischenfunken.
        volatile bool _scanEntfernt;

        // Ergebnisse des letzten Laufs. Die Oberflaeche waehlt daraus ueber Kennung bzw.
        // Position aus; sie bekommt nie die Moeglichkeit, einen eigenen Pfad zu nennen.
        // Zuweisung erfolgt immer als ganze neue Liste (Referenzzuweisung ist unteilbar).
        List<RegistryScan.Fund> _lastFunde = new List<RegistryScan.Fund>();
        StorageScan.Befund _lastSpeicher;

        bool ScanRunning { get { return _scanThread != null && _scanThread.IsAlive; } }

        /// <summary>Wird gerade etwas entfernt? Dann ist der Vorgang nicht mehr anzuhalten.</summary>
        public bool ScanEntferntGerade { get { return _scanEntfernt && ScanRunning; } }

        /// <summary>
        /// Stoppt einen laufenden Suchlauf. Wird vom Befehl "cancel" mitbedient.
        ///
        /// Ein laufendes Entfernen wird ausdruecklich NICHT gestoppt: dort sind Sicherung
        /// und Eingriff verzahnt, und die Oberflaeche sagt dem Nutzer auch, dass es sich
        /// nicht mehr anhalten laesst. Vorher stoppte jedes "cancel" auch ein Aufraeumen,
        /// selbst wenn der Nutzer nur den Pruef-Ablauf gemeint hat.
        /// </summary>
        public void CancelScan()
        {
            if (_scanEntfernt) return;
            _scanCancel = true;
        }

        void ScanProgress(string scan, string text)
        {
            UiPost(new { type = "scanProgress", scan = scan, text = text });
        }

        /// <summary>
        /// Startet einen Hintergrund-Lauf. Zwei Laeufe gleichzeitig gibt es nicht: sie
        /// wuerden sich beim Messen gegenseitig verfaelschen.
        /// </summary>
        bool StarteScan(string scan, ThreadStart arbeit, bool entfernt = false)
        {
            if (ScanRunning)
            {
                UiPost(new { type = "scanError", scan = scan,
                             message = "Es läuft bereits eine Suche. Bitte warten Sie, bis sie fertig ist." });
                return false;
            }
            _scanCancel = false;
            _scanEntfernt = entfernt;
            _scanThread = new Thread(delegate ()
            {
                try { arbeit(); }
                finally { _scanEntfernt = false; }
            })
            { IsBackground = true };
            _scanThread.Start();
            return true;
        }

        // ================================================================ Speicherplatz

        void StartStorageScan()
        {
            StarteScan("storage", delegate ()
            {
                try
                {
                    StorageScan.Befund b = StorageScan.Run(
                        t => ScanProgress("storage", t),
                        () => _scanCancel);

                    // Ein abgebrochener Lauf wird NICHT angezeigt und nicht gemerkt. Er
                    // sieht auf dem Bildschirm aus wie ein vollstaendiges Ergebnis, ist
                    // aber nur ein Bruchstueck - und eine Auswahl darauf waere blind.
                    if (_scanCancel) { _lastSpeicher = null; return; }

                    _lastSpeicher = b;
                    UiPost(b.ToJson());
                }
                catch (Exception ex)
                {
                    AppLog.Error("Speichersuche", ex);
                    UiPost(new { type = "scanError", scan = "storage",
                                 message = "Die Suche nach dem belegten Platz ist fehlgeschlagen." });
                }
            });
        }

        /// <summary>
        /// Raeumt die gewaehlten Kategorien weg. Angenommen werden nur Kennungen, die im
        /// zuletzt angezeigten Ergebnis auch wirklich vorkamen - so kann die Oberflaeche
        /// nichts ausloesen, das der Nutzer nie gesehen hat.
        /// </summary>
        void StorageClean(Dictionary<string, object> m)
        {
            StorageScan.Befund letzter = _lastSpeicher;
            if (letzter == null)
            {
                UiPost(new { type = "scanError", scan = "storage",
                             message = "Bitte suchen Sie zuerst nach dem belegten Platz." });
                return;
            }

            var erlaubt = new HashSet<string>(
                letzter.Aufraeumbar.Where(p => p.Aufraeumbar).Select(p => p.Schluessel),
                StringComparer.OrdinalIgnoreCase);

            var gewaehlt = new List<string>();
            object arr;
            if (m.TryGetValue("keys", out arr) && arr is object[])
            {
                foreach (object o in (object[])arr)
                {
                    string k = o == null ? "" : o.ToString();
                    if (erlaubt.Contains(k) && !gewaehlt.Contains(k)) gewaehlt.Add(k);
                }
            }
            if (gewaehlt.Count == 0)
            {
                UiPost(new { type = "scanError", scan = "storage",
                             message = "Es war nichts ausgewählt, das sich aufräumen lässt." });
                return;
            }

            // Waehrend DISM oder SFC laufen, wird nicht zusaetzlich geloescht.
            if (FlowRunning || (_runner != null && _runner.Running))
            {
                UiPost(new { type = "scanError", scan = "storage",
                             message = "Es läuft gerade eine Wartung. Bitte warten Sie, bis sie fertig ist." });
                return;
            }

            StarteScan("storage", delegate ()
            {
                try
                {
                    AppLog.Info("Aufräumen gestartet: " + string.Join(", ", gewaehlt.ToArray()));
                    object bericht = StorageScan.Aufraeumen(gewaehlt,
                        t => ScanProgress("storage", t),
                        () => _scanCancel);
                    UiPost(bericht);

                    // Danach neu messen, damit die Anzeige den Zustand NACH dem Aufraeumen
                    // zeigt und nicht den davor. Bewusst ohne Abbruchpruefung: eine halbe
                    // Messung waere schlechter als gar keine, und sie wuerde als
                    // vollstaendiges Ergebnis angezeigt.
                    StorageScan.Befund b = StorageScan.Run(null, null);
                    _lastSpeicher = b;
                    UiPost(b.ToJson());
                }
                catch (Exception ex)
                {
                    AppLog.Error("Aufräumen", ex);
                    UiPost(new { type = "scanError", scan = "storage", message = ex.Message });
                }
            }, true);
        }

        /// <summary>
        /// Zeigt einen der grossen Brocken im Explorer. Die Oberflaeche nennt nur seine
        /// Position in der zuletzt angezeigten Liste, nie einen Pfad.
        /// </summary>
        void OpenBrocken(int index)
        {
            StorageScan.Befund letzter = _lastSpeicher;
            if (letzter == null || index < 0 || index >= letzter.Brocken.Count) return;
            string pfad = letzter.Brocken[index].Pfad;
            if (string.IsNullOrEmpty(pfad)) return;
            // /select zeigt den Eintrag im uebergeordneten Ordner markiert an, statt ihn zu
            // oeffnen. Bei einer 40-GB-Datei ist das der freundlichere Weg.
            // Der Start laeuft ueber die Oberflaeche des Nutzers, damit der Explorer NICHT
            // die Administratorrechte dieser App erbt.
            Shell.OeffneImNutzerkontext("explorer.exe", "/select,\"" + pfad + "\"");
        }

        // ================================================================ Registrierung

        void StartRegistryScan()
        {
            StarteScan("registry", delegate ()
            {
                try
                {
                    List<RegistryScan.Fund> funde = RegistryScan.Run(
                        t => ScanProgress("registry", t),
                        () => _scanCancel);

                    // Wie beim Speicher: ein Bruchstueck wird weder gezeigt noch gemerkt.
                    if (_scanCancel) { _lastFunde = new List<RegistryScan.Fund>(); return; }

                    _lastFunde = funde;
                    UiPost(new
                    {
                        type = "registryResult",
                        abgebrochen = false,
                        hinweise = RegistryScan.KategorieHinweise,
                        funde = funde.Select(f => f.ToJson()).ToArray(),
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Error("Registrierungs-Pruefung", ex);
                    UiPost(new { type = "scanError", scan = "registry",
                                 message = "Die Prüfung der Registrierung ist fehlgeschlagen." });
                }
            });
        }

        /// <summary>
        /// Entfernt die ausgewaehlten Eintraege. Ablauf, in dieser Reihenfolge:
        ///
        ///   1. Auswahl gegen das gespeicherte Ergebnis pruefen (Kennungen, keine Pfade)
        ///   2. Wiederherstellungspunkt anlegen
        ///   3. .reg-Sicherung schreiben - schlaegt sie fehl, wird nichts angefasst
        ///      (das erledigt RegistryScan.Entferne selbst)
        ///   4. entfernen und melden, wo die Sicherung liegt
        /// </summary>
        void RegistryClean(Dictionary<string, object> m)
        {
            List<RegistryScan.Fund> letzte = _lastFunde;
            if (letzte == null || letzte.Count == 0)
            {
                UiPost(new { type = "scanError", scan = "registry",
                             message = "Bitte lassen Sie zuerst die Registrierung prüfen." });
                return;
            }
            if (FlowRunning || (_runner != null && _runner.Running))
            {
                UiPost(new { type = "scanError", scan = "registry",
                             message = "Es läuft gerade eine Wartung. Bitte warten Sie, bis sie fertig ist." });
                return;
            }

            // Funde ohne Kennung koennen nicht ausgewaehlt worden sein. Sie hier still zu
            // uebergehen ist billiger als ein Absturz mitten im Nachrichten-Empfang.
            var nachId = new Dictionary<string, RegistryScan.Fund>(StringComparer.Ordinal);
            foreach (RegistryScan.Fund f in letzte)
                if (f != null && !string.IsNullOrEmpty(f.Id)) nachId[f.Id] = f;

            var auswahl = new List<RegistryScan.Fund>();
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            object arr;
            if (m.TryGetValue("ids", out arr) && arr is object[])
            {
                foreach (object o in (object[])arr)
                {
                    string id = o == null ? "" : o.ToString();
                    RegistryScan.Fund f;
                    if (!nachId.TryGetValue(id, out f)) continue;
                    if (!gesehen.Add(id)) continue;
                    auswahl.Add(f);
                }
            }
            if (auswahl.Count == 0)
            {
                UiPost(new { type = "scanError", scan = "registry",
                             message = "Es war nichts ausgewählt." });
                return;
            }

            StarteScan("registry", delegate ()
            {
                try
                {
                    ScanProgress("registry", "Sicherungspunkt wird angelegt");
                    bool punkt = LegeSicherungspunktAn("Vor dem Aufräumen der Registrierung");

                    ScanProgress("registry", "Sicherung wird geschrieben");
                    int entfernt, fehlgeschlagen;
                    string sicherung = RegistryScan.Entferne(auswahl, out entfernt, out fehlgeschlagen);
                    int gewaehlt = auswahl.Count;

                    History.Add("Registrierung aufräumen",
                                fehlgeschlagen > 0 ? "warn" : "good",
                                entfernt + " Eintrag/Einträge entfernt", 0);

                    UiPost(new
                    {
                        type = "registryCleaned",
                        gewaehlt = gewaehlt,
                        entfernt = entfernt,
                        fehlgeschlagen = fehlgeschlagen,
                        sicherung = sicherung,
                        sicherungspunkt = punkt,
                    });

                    // Neu prüfen, damit die Liste den Zustand danach zeigt. Ohne
                    // Abbruchpruefung: ein halbes Ergebnis waere hier schlimmer als keines,
                    // weil es wie eine vollstaendige Liste aussieht.
                    List<RegistryScan.Fund> funde = RegistryScan.Run(null, null);
                    _lastFunde = funde;
                    UiPost(new
                    {
                        type = "registryResult",
                        abgebrochen = false,
                        hinweise = RegistryScan.KategorieHinweise,
                        funde = funde.Select(f => f.ToJson()).ToArray(),
                    });
                }
                catch (Exception ex)
                {
                    AppLog.Error("Registrierung aufräumen", ex);
                    UiPost(new { type = "scanError", scan = "registry", message = ex.Message });
                }
            }, true);
        }

        /// <summary>
        /// Legt einen Wiederherstellungspunkt an. Dieselbe Mechanik wie Schritt 1 in
        /// CheckFlow.FixWorker: die Frequenz-Drossel wird kurz aufgehoben, weil Windows
        /// sonst hoechstens einen Punkt pro 24 Stunden anlegt und den Wunsch still
        /// verwirft. Ein uebersprungener Punkt (Systemschutz aus) ist kein Fehler,
        /// wird aber ehrlich an die Oberflaeche gemeldet.
        /// </summary>
        bool LegeSicherungspunktAn(string beschreibung)
        {
            string safe = SanitizeDesc(beschreibung);
            Shell.Result r = Shell.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                "try { Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' " +
                "-Name 'SystemRestorePointCreationFrequency' -Value 0 -EA SilentlyContinue; " +
                "Checkpoint-Computer -Description '" + safe + "' -RestorePointType MODIFY_SETTINGS -EA Stop; " +
                "'ok' } catch { 'nein' }\"",
                5 * 60 * 1000);
            bool ok = r.Ok && (r.Output ?? "").IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0;
            AppLog.Info("Sicherungspunkt vor dem Aufräumen der Registrierung: " + (ok ? "angelegt" : "nicht angelegt"));
            return ok;
        }

        /// <summary>Oeffnet den Ordner mit den .reg-Sicherungen im Explorer.</summary>
        void OpenRegBackup()
        {
            try
            {
                string ordner = RegistryScan.Sicherungsordner();
                Directory.CreateDirectory(ordner);
                Shell.OeffneImNutzerkontext("explorer.exe", "\"" + ordner + "\"");
            }
            catch (Exception ex) { AppLog.Warn("Sicherungsordner liess sich nicht oeffnen: " + ex.Message); }
        }
    }
}
