using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Eine Massnahme des Helfer-Katalogs: Kennung, Titel, Stufe (0 nur lesen, 1 leicht,
    /// 2 Eingriff, 3 tief), die Parameterpruefung und die Ausfuehrung. Pruefen liefert null
    /// (in Ordnung) oder den Grund, mit dem der ganze Plan abgelehnt wird.
    /// </summary>
    public class Massnahme
    {
        public string Kennung;
        public string Titel;
        /// <summary>
        /// Grundwert der Massnahme. Die Stufe eines konkreten Schritts liefert Katalog.StufeVon(m, s):
        /// bei werkzeug haengt sie am Katalogeintrag (Danger 3, IsRepair 2, sonst 1), hier steht
        /// dann nur 1. Wer eine Stufe anzeigt oder protokolliert, nimmt StufeVon, nie dieses Feld.
        /// </summary>
        public int Stufe;
        /// <summary>
        /// Arbeitet im Profil des Helfer-Kontos (HKCU, %TEMP%, %LOCALAPPDATA%, Papierkorb,
        /// Aufgabenplanung ohne /RU). Ausfuehrung lehnt den Plan mit Exit 2 ab, wenn der
        /// Aufrufer nicht das eigene Konto ist (anderes Konto im UAC-Dialog; Nachtraege B5/B39).
        /// registrierung.entfernen traegt es nicht: dort werden nur HKCU-Funde uebersprungen.
        /// </summary>
        public bool Profilgebunden;
        public Func<PlanSchritt, string> Pruefen;
        public Action<PlanSchritt, Ausfuehrungskontext> Ausfuehren;
    }

    /// <summary>
    /// Kennungen -> Massnahme (docs/M2-ENTWURF.md, Abschnitt 3). Der Helfer baut jeden
    /// Schritt aus Kennung und geprueften Parametern SELBST (src/Schritte.cs); File+Args
    /// von aussen gibt es nicht. Das Verhalten der Tiefenpruefung und der Reparatur ist
    /// 1:1 aus host/CheckFlow.cs (DeepWorker, FixWorker, LetzteSicherungsnummer) uebernommen,
    /// das der Werkzeuge aus host/ShellForm.cs (ueber Schritte).
    ///
    /// Trockenlauf (kontext.Trocken): Pruefen laeuft, Lesezugriffe (SequenceNumber, Registry-
    /// Suche) laufen, kein Prozess, kein WMI- oder Registry-Schreibzugriff; jede uebersprungene
    /// Stelle meldet "(trocken) ...". Werkzeuge.Lauf und Werkzeuge.Schritt liefern dann 0.
    ///
    /// Ein Problem (ExitCode != 0 ohne IgnoreExit, Zeitgrenze und "nicht gestartet" immer,
    /// fehlgeschlagene Eintraege) meldet die Massnahme ueber den internen Schluessel
    /// Werte["problem"] = "1"; Ausfuehrung macht daraus Exit 1 und entfernt den Schluessel vor
    /// der Rueckgabe wieder (nach aussen zaehlt PlanErgebnis.Problem). Ausnahme: tiefenpruefung
    /// und dateien.reparieren liefern einen DISM-/SFC-Fehlercode nur als Wert dism.exit/sfc.exit,
    /// der Host deutet die Ausgabe wie bisher (siehe Werte()); -1/-2 zaehlen auch dort als Problem.
    /// </summary>
    public static class Katalog
    {
        /// <summary>Signal zwischen Massnahme und Ausfuehrung, nie Teil des Ergebnisses fuer den Host.</summary>
        public const string ProblemSchluessel = "problem";

        // Zeitgrenzen wie bisher in host/CheckFlow.cs.
        const int DismScanMs = 20 * 60 * 1000;
        const int SfcVerifyMs = 30 * 60 * 1000;
        const int DismRestoreMs = 40 * 60 * 1000;
        const int SfcScanMs = 40 * 60 * 1000;
        const int RestorePointMs = 5 * 60 * 1000;
        const int RestoreComputerMs = 10 * 60 * 1000;

        static readonly List<Massnahme> _alle = Bauen();

        public static Massnahme Finde(string kennung)
        {
            if (string.IsNullOrEmpty(kennung)) return null;
            foreach (Massnahme m in _alle)
                if (string.Equals(m.Kennung, kennung, StringComparison.Ordinal)) return m;
            return null;
        }

        public static IEnumerable<Massnahme> Alle()
        {
            return _alle.AsReadOnly();
        }

        /// <summary>
        /// Titel fuer Ablaufbalken und Protokoll: bei werkzeug der Name des Katalogeintrags,
        /// bei Netz/Treiber mit Ziel bzw. Ordner (wie die bisherigen Job-Titel), sonst der Massnahmen-Titel.
        /// </summary>
        public static string TitelVon(Massnahme m, PlanSchritt s)
        {
            if (m == null) return "?";
            if (s != null)
            {
                switch (m.Kennung)
                {
                    case "werkzeug":
                        {
                            MaintenanceAction a = AktionVon(s);
                            if (a != null) return a.Title;
                            break;
                        }
                    case "netz.diagnose":
                        {
                            string z = Schritte.HostPruefen(s.Wert("ziel"));
                            if (z != null) return "Netzwerk-Diagnose: " + z;
                            break;
                        }
                    case "treiber.sichern":
                        {
                            string o = Schritte.OrdnerPruefen(s.Wert("ordner"));
                            if (o != null) return "Treiber-Backup nach " + o;
                            break;
                        }
                    case "apps.entfernen":
                        return "Bloatware entfernen (" + s.Liste("pakete").Count + ")";
                }
            }
            return m.Titel;
        }

        /// <summary>Stufe fuer den Protokolleintrag: bei werkzeug Danger 3, IsRepair 2, sonst 1; sonst die feste Stufe.</summary>
        public static int StufeVon(Massnahme m, PlanSchritt s)
        {
            if (m == null) return 0;
            if (m.Kennung == "werkzeug" && s != null)
            {
                MaintenanceAction a = AktionVon(s);
                if (a != null) return a.Danger ? 3 : (a.IsRepair ? 2 : 1);
            }
            return m.Stufe;
        }

        // ------------------------------------------------------------------ Aufbau

        static List<Massnahme> Bauen()
        {
            var l = new List<Massnahme>();

            l.Add(new Massnahme
            {
                Kennung = "tiefenpruefung", Titel = "Tiefenprüfung der Windows-Dateien", Stufe = 0,
                Pruefen = s => null,
                Ausfuehren = Tiefenpruefung,
            });
            l.Add(new Massnahme
            {
                Kennung = "dateien.reparieren", Titel = "Reparatur der Windows-Dateien", Stufe = 3,
                Pruefen = s => null,
                Ausfuehren = DateienReparieren,
            });
            l.Add(new Massnahme
            {
                Kennung = "wiederherstellungspunkt.anlegen", Titel = "Wiederherstellungspunkt anlegen", Stufe = 1,
                Pruefen = s => Schritte.BeschreibungPruefen(s.Wert("beschreibung")) == null
                    ? "Die Beschreibung darf höchstens 60 Zeichen haben und nur Buchstaben, Ziffern, Leerzeichen, Punkt, Unterstrich und Bindestrich enthalten."
                    : null,
                Ausfuehren = WiederherstellungspunktAnlegen,
            });
            l.Add(new Massnahme
            {
                Kennung = "wiederherstellungspunkt.zurueck", Titel = "Windows auf einen früheren Stand zurücksetzen", Stufe = 3,
                Pruefen = s =>
                {
                    int folge;
                    return Schritte.GanzeZahl(s.Wert("folge"), 1, int.MaxValue, out folge)
                        ? null
                        : "Die Folgenummer muss eine ganze Zahl größer 0 sein, nicht „" + (s.Wert("folge") ?? "") + "“.";
                },
                Ausfuehren = WiederherstellungspunktZurueck,
            });
            l.Add(new Massnahme
            {
                Kennung = "werkzeug", Titel = "Werkzeug", Stufe = 1, Profilgebunden = true,   // Steps mit %TEMP%, %USERPROFILE%, $env:LOCALAPPDATA
                Pruefen = s =>
                {
                    int id;
                    string roh = s.Wert("id");
                    if (!Schritte.GanzeZahl(roh, 0, int.MaxValue, out id))
                        return "Die Werkzeug-Nummer muss eine ganze Zahl ab 0 sein, nicht „" + (roh ?? "") + "“.";
                    MaintenanceAction a = Schritte.Aktion(id);
                    if (a == null) return "Werkzeug Nr. " + id + " gibt es nicht (Katalog hat " + Catalog.All().Count + " Einträge).";
                    if (a.Special != null) return "Werkzeug Nr. " + id + " („" + a.Title + "“) braucht eine Eingabe und läuft über seine eigene Kennung.";
                    if (Schritte.Werkzeug(id) == null) return "Werkzeug Nr. " + id + " hat 0 Schritte.";
                    return FlagPruefen(s, "sicherung");
                },
                Ausfuehren = Werkzeug,
            });
            l.Add(new Massnahme
            {
                Kennung = "wartung.auto", Titel = "Geplante Wartung", Stufe = 1, Profilgebunden = true,
                Pruefen = s =>
                {
                    List<AutoItem> katalog = Catalog.AutoCatalog();
                    foreach (string key in s.Liste("schluessel"))
                    {
                        bool bekannt = false;
                        foreach (AutoItem it in katalog) if (it.Key == key) { bekannt = true; break; }
                        if (!bekannt) return "Unbekannte Aufgabe „" + key + "“ für die geplante Wartung.";
                    }
                    return null;
                },
                Ausfuehren = WartungAuto,
            });
            l.Add(new Massnahme
            {
                Kennung = "apps.entfernen", Titel = "Bloatware entfernen", Stufe = 2, Profilgebunden = true,   // Remove-AppxPackage ohne -AllUsers
                Pruefen = s =>
                {
                    List<string> pakete = s.Liste("pakete");
                    if (pakete.Count == 0) return "Es ist kein Paket angegeben (Parameter pakete ist leer).";
                    foreach (string full in pakete)
                        if (!AppxCleaner.IsRemovable(full))
                            return "Das Paket „" + Kurz(full) + "“ steht nicht in der Liste der entfernbaren Apps.";
                    return FlagPruefen(s, "sicherung");
                },
                Ausfuehren = AppsEntfernen,
            });
            l.Add(new Massnahme
            {
                Kennung = "netz.diagnose", Titel = "Netzwerk-Diagnose", Stufe = 0,
                Pruefen = s => Schritte.HostPruefen(s.Wert("ziel")) == null
                    ? "Ungültiges Ziel „" + Kurz(s.Wert("ziel")) + "“: erlaubt sind 1 bis 253 Zeichen aus Buchstaben, Ziffern, Punkt, Doppelpunkt und Bindestrich, kein Bindestrich am Anfang."
                    : null,
                Ausfuehren = NetzDiagnose,
            });
            l.Add(new Massnahme
            {
                Kennung = "treiber.sichern", Titel = "Treiber-Backup", Stufe = 0,
                Pruefen = s =>
                {
                    string ordner = s.Wert("ordner");
                    if (Schritte.OrdnerPruefen(ordner) != null) return null;
                    if (Schritte.IstLaufwerkswurzel(ordner))
                        return "Der Zielordner „" + Kurz(ordner) + "“ ist eine Laufwerkswurzel; bitte einen Unterordner wählen.";
                    return "Ungültiger Zielordner „" + Kurz(ordner) + "“: er muss ein absoluter Pfad sein und darf nicht im Windows-Ordner liegen.";
                },
                Ausfuehren = TreiberSichern,
            });
            l.Add(new Massnahme
            {
                Kennung = "zeitplan.anlegen", Titel = "Zeitplan anlegen", Stufe = 1, Profilgebunden = true,   // Aufgabe ohne /RU gehoert dem anlegenden Konto (B39)
                Pruefen = s =>
                {
                    string modus, dSpec; string[] tage, aktionen; int dom, hh, mm;
                    return Schritte.ZeitplanPruefen(s, out modus, out dSpec, out tage, out dom, out hh, out mm, out aktionen);
                },
                Ausfuehren = ZeitplanAnlegen,
            });
            l.Add(new Massnahme
            {
                Kennung = "zeitplan.loeschen", Titel = "Zeitplan löschen", Stufe = 1,
                Pruefen = s => null,
                Ausfuehren = ZeitplanLoeschen,
            });
            l.Add(new Massnahme
            {
                Kennung = "selbststart.loeschen", Titel = "Selbststart-Aufgabe entfernen", Stufe = 1,
                Pruefen = s => null,
                Ausfuehren = SelbststartLoeschen,
            });
            l.Add(new Massnahme
            {
                Kennung = "speicher.aufraeumen", Titel = "Speicher aufräumen", Stufe = 2, Profilgebunden = true,   // Papierkorb, GetTempPath, LocalApplicationData
                Pruefen = s =>
                {
                    List<string> keys = s.Liste("schluessel");
                    if (keys.Count == 0) return "Es ist keine Kategorie angegeben (Parameter schluessel ist leer).";
                    foreach (string key in keys)
                        if (!StorageScan.IstBekannterSchluessel(key))
                            return "Unbekannte Kategorie „" + Kurz(key) + "“; bekannt sind " + string.Join(", ", StorageScan.BekannteSchluessel) + ".";
                    return null;
                },
                Ausfuehren = SpeicherAufraeumen,
            });
            l.Add(new Massnahme
            {
                Kennung = "registrierung.entfernen", Titel = "Registrierung aufräumen", Stufe = 2,
                Pruefen = s =>
                {
                    List<string> ids = s.Liste("kennungen");
                    if (ids.Count == 0) return "Es ist kein Eintrag angegeben (Parameter kennungen ist leer).";
                    foreach (string id in ids)
                    {
                        if (id.Length > 64) return "Eine Kennung ist länger als 64 Zeichen.";
                        foreach (char c in id)
                        {
                            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                            if (!ok) return "Die Kennung „" + Kurz(id) + "“ enthält andere Zeichen als Buchstaben und Ziffern.";
                        }
                    }
                    return FlagPruefen(s, "sicherung");
                },
                Ausfuehren = RegistrierungEntfernen,
            });

            return l;
        }

        // ------------------------------------------------------------------ Tiefenpruefung / Reparatur

        static void Tiefenpruefung(PlanSchritt s, Ausfuehrungskontext k)
        {
            string tail;
            k.Melde(1, 2, "Die Grundbestandteile von Windows werden geprüft");
            int dism = Werkzeuge.Lauf(k, "DISM.exe", "/Online /Cleanup-Image /ScanHealth", DismScanMs, null, true, out tail);
            Werte(k, "dism", dism, tail);
            if (k.IstAbgebrochen) return;

            k.Melde(2, 2, "Die Dateien von Windows werden auf Beschädigungen geprüft");
            int sfc = Werkzeuge.Lauf(k, "sfc.exe", "/verifyonly", SfcVerifyMs, Encoding.Unicode, true, out tail);
            Werte(k, "sfc", sfc, tail);
        }

        static void DateienReparieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            string tail;
            // Sicherungspunkt zuerst (Grundsatz 2). Erfolg wird an der Folgenummer gemessen,
            // nicht am Rueckgabewert: CreateRestorePoint meldet 0 auch dann, wenn die
            // 24-Stunden-Drossel nichts anlegt (Widerlegungsrunde). Die Drossel bleibt hier
            // bewusst stehen, deshalb passt der zweite der drei Saetze.
            k.Melde(1, 4, "Ein Sicherungspunkt wird angelegt");
            Wiederherstellungspunkt(k, "Vor der Wartung", false, "die Reparatur läuft ohne Rückweg über einen Punkt");
            if (k.IstAbgebrochen) return;

            k.Melde(2, 4, "Fehlende Windows-Bausteine werden neu geholt");
            int dism = Werkzeuge.Lauf(k, "DISM.exe", "/Online /Cleanup-Image /RestoreHealth", DismRestoreMs, null, true, out tail);
            Werte(k, "dism", dism, tail);
            if (k.IstAbgebrochen) return;

            k.Melde(3, 4, "Beschädigte Windows-Dateien werden ersetzt");
            int sfc = Werkzeuge.Lauf(k, "sfc.exe", "/scannow", SfcScanMs, Encoding.Unicode, true, out tail);
            Werte(k, "sfc", sfc, tail);
            // Schritt 4 von 4 ("Das Ergebnis wird zusammengestellt") ist die Messung im Host.
        }

        // Exit-Code und Ausgabe fuer den Host. Bewusst KEIN Problem-Schluessel bei einem
        // Windows-Exit-Code != 0: wie bisher in CheckFlow entscheidet der Host ueber
        // EvaluateFiles(dism.ausgabe, sfc.ausgabe), ob die Windows-Dateien in Ordnung sind; ein
        // DISM-Fehlercode steht in dism.exit/sfc.exit und in der Ausgabe. Nur -1 (nicht gestartet)
        // und -2 (Zeitgrenze) setzt Werkzeuge.Starten selbst als Problem (Nachtrag B6): der Plan
        // endet dann mit Exit 1, der Host wertet die Werte trotzdem aus (Exit 0 und 1).
        static void Werte(Ausfuehrungskontext k, string praefix, int exit, string tail)
        {
            k.Werte[praefix + ".exit"] = exit.ToString(CultureInfo.InvariantCulture);
            k.Werte[praefix + ".ausgabe"] = tail ?? "";
        }

        // ------------------------------------------------------------------ Wiederherstellungspunkt

        static void WiederherstellungspunktAnlegen(PlanSchritt s, Ausfuehrungskontext k)
        {
            string beschreibung = Schritte.BeschreibungPruefen(s.Wert("beschreibung"));
            bool angelegt = Wiederherstellungspunkt(k, beschreibung, true, null);
            // Hier IST der Punkt die Massnahme: ohne neuen Punkt hat sie nichts bewirkt.
            if (!angelegt && !k.Trocken) Problem(k);
        }

        static void WiederherstellungspunktZurueck(PlanSchritt s, Ausfuehrungskontext k)
        {
            int folge;
            Schritte.GanzeZahl(s.Wert("folge"), 1, int.MaxValue, out folge);
            // Folgenummer ist eine reine Zahl -> keine Einschleusung moeglich. PowerShell bleibt hier bis M4.
            // Der catch endet mit exit 1: sonst meldete PowerShell 0, der Plan endete "Fertig",
            // und die fehlgeschlagene Wiederherstellung stuende nur als Text in der Ausgabe.
            string cmd =
                "try { Restore-Computer -RestorePoint " + folge + " -Confirm:$false -EA Stop; 'Wiederherstellung gestartet, der PC startet neu.' } " +
                "catch { 'Wiederherstellung fehlgeschlagen: ' + $_.Exception.Message; exit 1 }";
            string tail;
            int exit = Werkzeuge.Lauf(k, "powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"",
                                      RestoreComputerMs, null, false, out tail);
            if (exit != 0)
            {
                k.Schreibe("   Windows wurde nicht auf den Stand Nr. " + folge + " zurückgesetzt (Exit " + exit + ").", "bad");
                Problem(k);
            }
        }

        /// <summary>
        /// Legt einen Wiederherstellungspunkt ueber WMI an (root\default, SystemRestore.CreateRestorePoint,
        /// Typ 12 MODIFY_SETTINGS, Ereignis 100 BEGIN_SYSTEM_CHANGE) und weist ihn ueber die
        /// Folgenummer vorher/nachher nach. Drei Faelle, drei Saetze wie in CheckFlow.FixWorker:
        /// neuer Punkt; kein neuer, aber ein vorhandener; gar keiner. Werte sicherung.vorher,
        /// sicherung.nachher, sicherung.satz. drosselAufheben setzt vorher den Schluessel
        /// SystemRestorePointCreationFrequency auf 0 (Registry, kein PowerShell).
        /// Trockenlauf: nur lesen, nichts anlegen, nichts setzen.
        /// </summary>
        static bool Wiederherstellungspunkt(Ausfuehrungskontext k, string beschreibung, bool drosselAufheben, string ohneNachsatz)
        {
            int? vorher = LetzteSicherungsnummer();
            string vorherText = vorher.HasValue ? vorher.Value.ToString(CultureInfo.InvariantCulture) : "";

            if (k.Trocken)
            {
                if (drosselAufheben) k.Schreibe("(trocken) Registry SystemRestorePointCreationFrequency = 0", "dim");
                k.Schreibe("(trocken) WMI SystemRestore.CreateRestorePoint(„" + beschreibung + "“, 12, 100)", "dim");
                string satzT = "(trocken) Kein Wiederherstellungspunkt angelegt; höchste Nr. vorher: " + (vorher.HasValue ? vorherText : "nicht lesbar");
                k.Werte["sicherung.vorher"] = vorherText;
                k.Werte["sicherung.nachher"] = vorherText;
                k.Werte["sicherung.satz"] = satzT;
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Sicherung, "wiederherstellungspunkt", satzT,
                        new { vorher, nachher = vorher, trocken = true });
                return false;
            }

            if (drosselAufheben) DrosselAufheben(k);

            uint? rueckgabe = null;
            string fehler = null;
            try { rueckgabe = PunktAnlegen(beschreibung, RestorePointMs); }
            catch (Exception ex)
            {
                fehler = ex.Message;
                AppLog.Warn("CreateRestorePoint: " + ex.Message);
            }

            int? nachher = LetzteSicherungsnummer();
            bool punktAngelegt = vorher.HasValue && nachher.HasValue && nachher.Value > vorher.Value;
            bool einerVorhanden = nachher.HasValue && nachher.Value > 0;
            string satz;
            if (punktAngelegt)
                satz = "Wiederherstellungspunkt Nr. " + nachher + " angelegt";
            else if (einerVorhanden)
                satz = drosselAufheben
                    ? "Kein neuer Wiederherstellungspunkt, obwohl die 24-Stunden-Drossel aufgehoben war; der vorhandene Nr. " + nachher + " gilt"
                    : "Kein neuer Wiederherstellungspunkt (Windows legt höchstens einen je 24 Stunden an); der vorhandene Nr. " + nachher + " gilt";
            else
                satz = "Kein Wiederherstellungspunkt vorhanden (Systemschutz aus oder nicht lesbar)"
                       + (string.IsNullOrEmpty(ohneNachsatz) ? "" : "; " + ohneNachsatz);

            k.Werte["sicherung.vorher"] = vorherText;
            k.Werte["sicherung.nachher"] = nachher.HasValue ? nachher.Value.ToString(CultureInfo.InvariantCulture) : "";
            k.Werte["sicherung.satz"] = satz;
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Sicherung, "wiederherstellungspunkt", satz,
                    new { vorher, nachher, rueckgabe, fehler, beschreibung });
            k.Schreibe(satz + ".", punktAngelegt ? "good" : "warn");
            return punktAngelegt;
        }

        /// <summary>Hoechste Folgenummer der Wiederherstellungspunkte (nur erhoeht lesbar), sonst null.</summary>
        static int? LetzteSicherungsnummer()
        {
            try
            {
                int max = -1;
                foreach (var mo in Sammler.Quellen.Wmi.Abfrage(@"root\default", "SELECT SequenceNumber FROM SystemRestore", 10000))
                {
                    var n = Sammler.Quellen.Wmi.Ganz(mo, "SequenceNumber");
                    if (n.HasValue && n.Value > max) max = n.Value;
                }
                return max < 0 ? 0 : max;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// WMI-Aufruf auf eigenem Thread mit Zeitbudget (der Systemschutz kann Minuten brauchen).
        /// Rueckgabe: ReturnValue der Methode (0 = angenommen), null wenn WMI nichts zurueckgab.
        /// </summary>
        static uint? PunktAnlegen(string beschreibung, int zeitMs)
        {
            object rueck = null;
            Exception fehler = null;
            var t = new Thread(() =>
            {
                try
                {
                    var scope = new ManagementScope(@"root\default");
                    scope.Connect();
                    using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                    using (ManagementBaseObject rein = cls.GetMethodParameters("CreateRestorePoint"))
                    {
                        rein["Description"] = beschreibung;
                        rein["RestorePointType"] = 12u;   // MODIFY_SETTINGS
                        rein["EventType"] = 100u;          // BEGIN_SYSTEM_CHANGE
                        using (ManagementBaseObject raus = cls.InvokeMethod("CreateRestorePoint", rein, null))
                            rueck = raus == null ? null : raus["ReturnValue"];
                    }
                }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "wmi:CreateRestorePoint" };
            t.Start();
            if (!t.Join(zeitMs))
                throw new TimeoutException("CreateRestorePoint überschritt " + (zeitMs / 60000) + " Minuten.");
            if (fehler != null) throw fehler;
            if (rueck == null) return null;
            return Convert.ToUInt32(rueck, CultureInfo.InvariantCulture);
        }

        /// <summary>Drossel-Schluessel auf 0: Windows legt sonst hoechstens einen Punkt je 24 Stunden an und verwirft den Wunsch still.</summary>
        static void DrosselAufheben(Ausfuehrungskontext k)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", true))
                {
                    if (key == null)
                    {
                        k.Schreibe("   Der Schlüssel SystemRestore fehlt in der Registrierung; die 24-Stunden-Drossel bleibt.", "warn");
                        return;
                    }
                    key.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Drossel-Schlüssel ließ sich nicht setzen: " + ex.Message);
                k.Schreibe("   Die 24-Stunden-Drossel ließ sich nicht aufheben: " + ex.Message, "warn");
            }
        }

        // ------------------------------------------------------------------ Werkzeuge, Wartung, Apps, Netz, Treiber

        static void Werkzeug(PlanSchritt s, Ausfuehrungskontext k)
        {
            MaintenanceAction a = AktionVon(s);
            if (a == null) throw new InvalidOperationException("Werkzeug-Nummer „" + (s.Wert("id") ?? "") + "“ ist nach der Prüfung nicht mehr auffindbar.");
            List<Step> steps = Schritte.Werkzeug(a.Id);
            if (steps == null) throw new InvalidOperationException("Werkzeug Nr. " + a.Id + " hat 0 Schritte.");

            if (Flag(s, "sicherung") && a.WantsRestorePoint)
            {
                Wiederherstellungspunkt(k, Beschreibung("Vor " + a.Title), false, "das Werkzeug läuft ohne Rückweg über einen Punkt");
                if (k.IstAbgebrochen) return;
            }
            SchritteLaufen(k, steps);
        }

        static void WartungAuto(PlanSchritt s, Ausfuehrungskontext k)
        {
            List<Step> steps = Schritte.Auto(s.Liste("schluessel"));
            if (steps == null || steps.Count == 0) throw new InvalidOperationException("Die geplante Wartung hat 0 Schritte.");
            SchritteLaufen(k, steps);
        }

        static void AppsEntfernen(PlanSchritt s, Ausfuehrungskontext k)
        {
            List<Step> steps = Schritte.AppsEntfernen(s.Liste("pakete"), Flag(s, "sicherung"));
            if (steps == null) throw new InvalidOperationException("Die Paketliste ist nach der Prüfung nicht mehr gültig.");
            SchritteLaufen(k, steps);
        }

        static void NetzDiagnose(PlanSchritt s, Ausfuehrungskontext k)
        {
            List<Step> steps = Schritte.NetzDiagnose(s.Wert("ziel"));
            if (steps == null) throw new InvalidOperationException("Das Ziel ist nach der Prüfung nicht mehr gültig.");
            SchritteLaufen(k, steps);
        }

        static void TreiberSichern(PlanSchritt s, Ausfuehrungskontext k)
        {
            string ordner = Schritte.OrdnerPruefen(s.Wert("ordner"));
            List<Step> steps = ordner == null ? null : Schritte.TreiberSichern(ordner);
            if (steps == null) throw new InvalidOperationException("Der Zielordner ist nach der Prüfung nicht mehr gültig.");
            // pnputil schreibt ERHOEHT in den Ordner. OrdnerPruefen vergleicht nur den Text mit
            // %WINDIR%; eine Abzweigung (Junction, symbolische Verknuepfung) im Zielpfad oder in
            // einem Elternordner fuehrte den Schreibzugriff woandershin, etwa nach System32
            // (Nachtrag B1). Deshalb hier zweimal pruefen: vor dem Anlegen (CreateDirectory ist
            // auf einer bestehenden Abzweigung ein No-op) und unmittelbar vor pnputil noch einmal.
            if (AbzweigungAblehnen(k, ordner)) return;
            if (k.Trocken) k.Schreibe("(trocken) Ordner anlegen: " + ordner, "dim");
            else
            {
                try { Directory.CreateDirectory(ordner); }
                catch (Exception ex)
                {
                    k.Schreibe("   Der Zielordner ließ sich nicht anlegen: " + ex.Message, "bad");
                    Problem(k);
                    return;
                }
                if (AbzweigungAblehnen(k, ordner)) return;
            }
            SchritteLaufen(k, steps);
        }

        /// <summary>
        /// true = der Pfad oder ein Elternordner ist eine Abzweigung (FileAttributes.ReparsePoint)
        /// oder seine Attribute sind nicht lesbar; dann Zeile bad und Problem. Noch nicht
        /// vorhandene Komponenten gelten als in Ordnung (sie werden erst angelegt).
        /// </summary>
        static bool AbzweigungAblehnen(Ausfuehrungskontext k, string ordner)
        {
            string stelle = Abzweigung(ordner);
            if (stelle == null) return false;
            k.Schreibe("   Der Zielordner wird nicht beschrieben: „" + stelle + "“ ist eine Abzweigung (Verknüpfung auf einen anderen Ort) oder nicht prüfbar. Bitte einen Ordner ohne Verknüpfung im Pfad wählen.", "bad");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Abgelehnt, "treiber.sichern", "Zielordner abgelehnt: Abzweigung im Pfad", new { ordner, stelle });
            Problem(k);
            return true;
        }

        /// <summary>Erste Komponente von unten nach oben, die eine Abzweigung ist oder nicht lesbar; null = keine.</summary>
        static string Abzweigung(string pfad)
        {
            string p = pfad;
            while (!string.IsNullOrEmpty(p))
            {
                try
                {
                    if (Directory.Exists(p) || File.Exists(p))
                    {
                        if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return p;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Attribute von „" + p + "“ nicht lesbar, zählt als Abzweigung: " + ex.Message);
                    return p;
                }
                string eltern;
                try { eltern = Path.GetDirectoryName(p); }   // null an der Wurzel (C:\ oder \\server\freigabe)
                catch (Exception) { return p; }
                if (string.Equals(eltern, p, StringComparison.OrdinalIgnoreCase)) break;
                p = eltern;
            }
            return null;
        }

        /// <summary>
        /// Steps der Reihe nach ueber Werkzeuge.Schritt; ExitCode != 0 ohne IgnoreExit = Problem,
        /// ein negativer Code (nicht gestartet -1, Zeitgrenze -2, Absturz) immer, auch bei IgnoreExit
        /// (Nachtrag B6: "net stop wsearch" nach 45 Minuten abgeschossen ist nicht erledigt).
        /// Abbruch zwischen den Steps.
        /// </summary>
        static void SchritteLaufen(Ausfuehrungskontext k, List<Step> steps)
        {
            foreach (Step st in steps)
            {
                if (k.IstAbgebrochen) return;
                string tail;
                int code = Werkzeuge.Schritt(k, st, out tail);
                if (code < 0 || (code != 0 && !st.IgnoreExit)) Problem(k);
            }
        }

        // ------------------------------------------------------------------ Zeitplan

        static void ZeitplanAnlegen(PlanSchritt s, Ausfuehrungskontext k)
        {
            string modus, dSpec; string[] tage, aktionen; int dom, hh, mm;
            string grund = Schritte.ZeitplanPruefen(s, out modus, out dSpec, out tage, out dom, out hh, out mm, out aktionen);
            if (grund != null) throw new InvalidOperationException(grund);
            string hhs = hh.ToString("00", CultureInfo.InvariantCulture);
            string mms = mm.ToString("00", CultureInfo.InvariantCulture);
            string exe = ExePfad();
            string beschreibung = modus + (dSpec.Length > 0 ? " " + dSpec : "") + " um " + hhs + ":" + mms
                                  + (aktionen == null ? " (Standardsatz)" : " (" + string.Join(", ", aktionen) + ")");

            if (k.Trocken)
            {
                k.Schreibe("(trocken) schtasks.exe /Create /TN " + Scheduler.TaskName + " " + beschreibung, "dim");
                return;
            }
            string warnung;
            bool ok = Scheduler.Create(modus, dSpec, hhs, mms, exe, out warnung);
            if (!ok)
            {
                k.Schreibe("   Die Aufgabe „" + Scheduler.TaskName + "“ ließ sich nicht anlegen (schtasks meldete einen Fehler).", "bad");
                Problem(k);
                return;
            }
            // Die Aufgabe steht, aber die Akku-/Nachhol-Einstellungen (PowerShell-Nachstellung, 90 s)
            // liessen sich nicht setzen: dann laeuft sie nur am Netzteil und holt verpasste Termine
            // nicht nach. Der Nutzer sieht das hier, nicht nur app.log (Nachtrag B7). Kein Problem:
            // die Aufgabe bleibt angelegt.
            if (!string.IsNullOrEmpty(warnung))
            {
                k.Schreibe("   " + warnung, "warn");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, "zeitplan", "Zeitplan angelegt, Einstellungen unvollständig: " + warnung, new { warnung });
            }
            // Die Aufgabe steht; die Datei zeitplan.json merkt sich nur die Auswahl der Aufgaben.
            // Scheitert sie, laeuft die geplante Wartung mit dem Standardsatz: Warnung, kein Problem.
            bool gespeichert = Scheduler.Write(modus, tage, dom, hhs + ":" + mms, aktionen);
            if (!gespeichert)
            {
                k.Schreibe("   Zeitplan angelegt, die Auswahl der Aufgaben konnte aber nicht gespeichert werden; bis dahin läuft der Standardsatz.", "warn");
                return;
            }
            k.Schreibe("   Zeitplan angelegt: " + beschreibung + ".", "good");
        }

        static void ZeitplanLoeschen(PlanSchritt s, Ausfuehrungskontext k)
        {
            if (k.Trocken)
            {
                k.Schreibe("(trocken) schtasks.exe /Delete /TN " + Scheduler.TaskName + " /F", "dim");
                return;
            }
            Scheduler.Delete();
            Scheduler.Clear();
            if (Scheduler.Exists())
            {
                k.Schreibe("   Die Aufgabe „" + Scheduler.TaskName + "“ besteht nach dem Löschen noch.", "bad");
                Problem(k);
                return;
            }
            k.Schreibe("   Zeitplan gelöscht: Aufgabe „" + Scheduler.TaskName + "“ ist nicht mehr vorhanden.", "good");
        }

        // ------------------------------------------------------------------ Selbststart

        // Die Selbststart-Aufgabe aus 8.0 (/RL HIGHEST, Besitzer Administratoren) kann der
        // nicht erhoehte Host weder ersetzen noch loeschen (src/Scheduler.cs, StartTaskAuffrischen).
        // Hier wird sie erhoeht nur GELOESCHT; neu angelegt (Besitzer Nutzer, LeastPrivilege)
        // wird sie danach vom Host ohne Rechte ueber Scheduler.StartTaskSet(true, exe). Legte
        // der Helfer sie selbst neu an, gehoerte sie wieder der Administratorengruppe, und
        // der Host koennte sie nie mehr abschalten (M2-Entwurf, Abschnitt 13).
        static void SelbststartLoeschen(PlanSchritt s, Ausfuehrungskontext k)
        {
            if (k.Trocken)
            {
                k.Schreibe("(trocken) schtasks.exe /Delete /TN " + Scheduler.StartTaskName + " /F", "dim");
                return;
            }
            if (!Scheduler.StartTaskExists())
            {
                // Kein Problem: das Ziel (keine Aufgabe mit Besitzer Administratoren) ist erreicht,
                // der Host legt seine eigene gleich neu an.
                k.Schreibe("   Die Aufgabe „" + Scheduler.StartTaskName + "“ ist nicht vorhanden; es gibt nichts zu entfernen.", "good");
                return;
            }
            bool ok = Scheduler.StartTaskLoeschen();
            if (Scheduler.StartTaskExists())
            {
                k.Schreibe("   Die Aufgabe „" + Scheduler.StartTaskName + "“ besteht nach dem Löschen noch"
                           + (ok ? "." : " (schtasks meldete einen Fehler)."), "bad");
                Problem(k);
                return;
            }
            k.Schreibe("   Selbststart-Aufgabe entfernt: „" + Scheduler.StartTaskName + "“ ist nicht mehr vorhanden.", "good");
        }

        // ------------------------------------------------------------------ Speicher

        static void SpeicherAufraeumen(PlanSchritt s, Ausfuehrungskontext k)
        {
            List<string> keys = s.Liste("schluessel");
            if (k.Trocken)
            {
                k.Schreibe("(trocken) aufräumen: " + string.Join(", ", keys), "dim");
                return;
            }
            // Anfang steht schon im Laufprotokoll (Ausfuehrung) und im app.log des Hosts (mit Plan-Id).
            object bericht = StorageScan.Aufraeumen(keys, t => k.Schreibe(t, "dim"), () => k.IstAbgebrochen);
            // Anonymes Objekt fuer die Oberflaeche -> Text; der Host macht mit
            // JavaScriptSerializer.DeserializeObject wieder ein Objekt daraus.
            k.Werte["speicher.bericht"] = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(bericht);
        }

        // ------------------------------------------------------------------ Registrierung

        static void RegistrierungEntfernen(PlanSchritt s, Ausfuehrungskontext k)
        {
            // Doppelt gesendete Kennungen zaehlen einmal: gewaehlt, gefunden und "nicht
            // wiedergefunden" rechnen sonst nicht auf.
            var gesucht = new HashSet<string>(s.Liste("kennungen"), StringComparer.Ordinal);
            var ids = new List<string>(gesucht);

            // Der Helfer scannt selbst: der Host hat nur angezeigt, entschieden wird hier ueber
            // die stabile Kennung (RegistryScan.StabileId), nie ueber Pfade von aussen.
            //
            // HKCU ist im Helfer das Profil des Kontos, das im UAC-Dialog angemeldet wurde.
            // Meldet ein Standardnutzer dort ein anderes Administratorkonto an, ist das ein
            // fremdes Profil: die Kennung (HKCU + Pfad) faende den gleichnamigen Eintrag des
            // anderen Kontos. Deshalb werden HKCU-Funde nur angefasst, wenn das eigene Konto
            // dem Aufrufer gleicht; sonst zaehlen sie als fehlgeschlagen. HKLM und HKCR sind
            // maschinenweit und davon nicht betroffen.
            bool eigenesKonto = Ausfuehrung.AufruferIstEigenesKonto(k.AufruferSid);
            List<RegistryScan.Fund> funde = RegistryScan.Run(t => k.Schreibe(t, "dim"), () => k.IstAbgebrochen);
            var auswahl = new List<RegistryScan.Fund>();
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            int fremdesKonto = 0;
            foreach (RegistryScan.Fund f in funde)
            {
                if (f == null || f.Id == null || !gesucht.Contains(f.Id) || !gesehen.Add(f.Id)) continue;
                if (!eigenesKonto && string.Equals(f.Hive, "HKCU", StringComparison.OrdinalIgnoreCase)) { fremdesKonto++; continue; }
                auswahl.Add(f);
            }
            int gefunden = auswahl.Count + fremdesKonto;
            int nichtGefunden = ids.Count - gefunden;

            // gewaehlt = die gesendeten Kennungen, nicht die wiedergefundenen (Nachtrag B9): der
            // Host zeigt "entfernt von gewaehlt", und 5 gewaehlt / 3 gefunden darf nicht als
            // "3 von 3" erscheinen. Nicht wiedergefundene zaehlen weder als entfernt noch als
            // fehlgeschlagen; der Host errechnet sie aus gewaehlt - entfernt - fehlgeschlagen.
            k.Werte["registrierung.gewaehlt"] = ids.Count.ToString(CultureInfo.InvariantCulture);
            k.Werte["registrierung.entfernt"] = "0";
            k.Werte["registrierung.fehlgeschlagen"] = fremdesKonto.ToString(CultureInfo.InvariantCulture);
            k.Werte["registrierung.sicherung"] = "";
            if (nichtGefunden > 0 && gefunden > 0)
            {
                k.Schreibe("   " + nichtGefunden + " der " + ids.Count + " gewählten Einträge wurden beim erneuten Prüfen nicht mehr gefunden (inzwischen entfernt oder verändert); sie werden übersprungen.", "warn");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, "registrierung",
                        nichtGefunden + " von " + ids.Count + " gewählten Einträgen beim erneuten Prüfen nicht wiedergefunden",
                        new { gewaehlt = ids.Count, gefunden, nichtGefunden });
            }
            if (fremdesKonto > 0)
            {
                k.Schreibe("   " + fremdesKonto + " von " + gefunden + " Einträgen übersprungen: im Dialog ist ein anderes Konto angemeldet, und diese Einträge gehören zum Benutzerprofil.", "warn");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, "registrierung",
                        fremdesKonto + " Einträge des Benutzerprofils übersprungen: anderes Konto im Dialog angemeldet",
                        new { fremdesKonto, aufruferSid = k.AufruferSid ?? "" });
                if (!k.Trocken) Problem(k);
            }
            if (k.IstAbgebrochen) return;

            if (Flag(s, "sicherung"))
            {
                Wiederherstellungspunkt(k, "Vor dem Aufräumen der Registrierung", true, "das Entfernen läuft ohne Rückweg über einen Punkt");
                if (k.IstAbgebrochen) return;
            }

            if (k.Trocken)
            {
                k.Schreibe("(trocken) " + gefunden + " von " + ids.Count + " gewählten Einträgen beim Prüfen wiedergefunden; nichts entfernt.", "dim");
                return;
            }
            if (auswahl.Count == 0)
            {
                k.Schreibe(fremdesKonto > 0
                    ? "   Nichts entfernt: die " + fremdesKonto + " wiedergefundenen Einträge gehören zum Benutzerprofil, und im Dialog ist ein anderes Konto angemeldet."
                    : "   Keiner der " + ids.Count + " gewählten Einträge wurde beim erneuten Prüfen gefunden; es wurde nichts entfernt.", "warn");
                return;
            }

            k.Schreibe("Sicherung wird geschrieben", "dim");
            int entfernt, fehlgeschlagen;
            string sicherung = RegistryScan.Entferne(auswahl, out entfernt, out fehlgeschlagen);
            fehlgeschlagen += fremdesKonto;
            k.Werte["registrierung.entfernt"] = entfernt.ToString(CultureInfo.InvariantCulture);
            k.Werte["registrierung.fehlgeschlagen"] = fehlgeschlagen.ToString(CultureInfo.InvariantCulture);
            k.Werte["registrierung.sicherung"] = sicherung ?? "";
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Sicherung, "registrierung",
                    (sicherung == null ? "Keine .reg-Sicherung geschrieben" : "Sicherung geschrieben: " + sicherung),
                    new { gewaehlt = ids.Count, gefunden, nichtGefunden, entfernt, fehlgeschlagen, fremdesKonto, sicherung });
            k.Schreibe("   " + entfernt + " von " + ids.Count + " gewählten Einträgen entfernt, " + fehlgeschlagen + " übersprungen"
                       + (nichtGefunden > 0 ? ", " + nichtGefunden + " nicht wiedergefunden" : "")
                       + (sicherung == null ? "." : "; Sicherung: " + sicherung), fehlgeschlagen > 0 || nichtGefunden > 0 ? "warn" : "good");
            if (fehlgeschlagen > 0) Problem(k);
        }

        // ------------------------------------------------------------------ Hilfen

        internal static void Problem(Ausfuehrungskontext k)
        {
            k.Werte[ProblemSchluessel] = "1";
        }

        static MaintenanceAction AktionVon(PlanSchritt s)
        {
            int id;
            if (!Schritte.GanzeZahl(s.Wert("id"), 0, int.MaxValue, out id)) return null;
            MaintenanceAction a = Schritte.Aktion(id);
            return a == null || a.Special != null ? null : a;
        }

        /// <summary>Schalter-Parameter: fehlt, leer, "0" oder "1"; alles andere ist ein Grund.</summary>
        static string FlagPruefen(PlanSchritt s, string name)
        {
            string v = s.Wert(name);
            if (string.IsNullOrEmpty(v) || v == "0" || v == "1") return null;
            return "Der Parameter " + name + " muss 0 oder 1 sein, nicht „" + Kurz(v) + "“.";
        }

        static bool Flag(PlanSchritt s, string name)
        {
            return s.Wert(name) == "1";
        }

        /// <summary>Beschreibung fuer WMI aus einem Titel: hoechstens 60 Zeichen.</summary>
        static string Beschreibung(string s)
        {
            if (string.IsNullOrEmpty(s)) return Schritte.StandardBeschreibung;
            return s.Length > 60 ? s.Substring(0, 60).TrimEnd() : s;
        }

        static string Kurz(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }

        static string ExePfad()
        {
            try
            {
                Assembly a = Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return a.Location;
            }
            catch (Exception) { }
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch (Exception) { return typeof(Katalog).Assembly.Location; }
        }
    }
}
