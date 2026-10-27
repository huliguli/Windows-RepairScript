using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Updates (docs/M3-ENTWURF.md, Abschnitt 1): update.dienst.aktivieren, update.verbergen;
        // M4 (docs/M4-ENTWURF.md, Abschnitte 1 und 3.5): update.zuruecksetzen. Anmeldung hier,
        // Arbeit in UpdatesMassnahmen.
        static partial void Updates(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("update.dienst.aktivieren", "Update-Dienst wieder einschalten", 2,
                UpdatesMassnahmen.DienstPruefen, UpdatesMassnahmen.DienstAktivieren));
            l.Add(Massnahmenbau.Neu("update.verbergen", "Update verbergen", 2,
                UpdatesMassnahmen.VerbergenPruefen, UpdatesMassnahmen.Verbergen));
            l.Add(Massnahmenbau.Neu(UpdatesMassnahmen.KennungReset, "Windows Update zurücksetzen", 3,
                s => null, UpdatesMassnahmen.Zuruecksetzen));
        }
    }

    /// <summary>
    /// Die Update-Massnahmen des Helfers. Dienst: Win32_Service.ChangeStartMode("Automatic") und
    /// StartService (Bausteine aus LeistungMassnahmen), nur fuer die fuenf Dienste in DienstNorm
    /// (wuauserv, bits, cryptsvc, msiserver, trustedinstaller; die Regel kern/Regeln/Updates.cs
    /// haengt die Kennung an genau diese). Die Kennung hat keinen Rueckweg (Katalog Rueckweg
    /// keiner, Vertrag 8.10): die Sicherung ist der Beleg, Rueckweg.cs InhaltPruefen lehnt sie bei
    /// jedem Vorwert ab, und rueckweg.moeglich = "0" geht bei jedem Lauf an den Host. Verbergen: Windows-Update-Agent ueber COM
    /// Microsoft.Update.Session, Suche im lokalen Cache (Online=false, mit Gegenprobe wie der
    /// Sammler), IUpdate.IsHidden = true; der Rueckweg setzt false. Ein Update, das der Agent
    /// nicht kennt, laesst sich nicht verbergen: dann Problem, keine Netzsuche.
    /// Zuruecksetzen (M4, Stufe 3): Sicherung der verborgenen Updates, Dienste stoppen, Ordner
    /// nach .old, Dienste starten, Ausblendungen wiederherstellen; siehe Zuruecksetzen.
    /// </summary>
    internal static class UpdatesMassnahmen
    {
        const int WuaMs = 3 * 60 * 1000;
        const int StartWartenMs = 30000;

        // ================================================================ update.dienst.aktivieren

        /// <summary>Die Dienste von Windows Update (Regel Updates.Dienste: wuauserv, bits, cryptsvc, trustedinstaller; Vertrag zusaetzlich msiserver).</summary>
        static readonly string[] Dienste = { "wuauserv", "bits", "cryptsvc", "msiserver", "trustedinstaller" };

        /// <summary>Kleingeschriebener Dienstname aus der festen Liste, sonst null.</summary>
        public static string DienstNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim().ToLowerInvariant();
            return Array.IndexOf(Dienste, t) >= 0 ? t : null;
        }

        public static string DienstPruefen(PlanSchritt s)
        {
            return DienstNorm(s.Wert("name")) == null
                ? "Der Parameter name muss einer der Update-Dienste sein (" + string.Join(", ", Dienste) + "), nicht „" + Massnahmenbau.Kurz(s.Wert("name")) + "“."
                : null;
        }

        public static void DienstAktivieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "update.dienst.aktivieren";
            string name = DienstNorm(s.Wert("name"));
            LeistungMassnahmen.DienstStand stand = LeistungMassnahmen.DienstLesen(name);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Der Dienst „" + name + "“ ist auf diesem PC nicht vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string vorher = LeistungMassnahmen.StartartNorm(stand.Startart) ?? stand.Startart ?? "";
            string zustand = stand.Zustand ?? "";
            bool auto = vorher == "Automatic";
            bool laeuft = string.Equals(zustand, "Running", StringComparison.OrdinalIgnoreCase);
            if (auto && laeuft)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "Der Dienst „" + name + "“ läuft bereits mit Startart automatisch; es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   Dienst „" + name + "“: Startart " + LeistungMassnahmen.StartartWort(vorher) + ", Zustand " + (zustand.Length == 0 ? "unbekannt" : zustand) + ".", "dim");
            // Kein Rueckweg (Katalog Rueckweg keiner, Vertrag 8.10), gleich ob der Vorwert Disabled
            // (der Regelfall) oder Manual mit gestopptem Dienst war: die Sicherung ist der Beleg, was
            // vorher galt, Rueckweg.cs InhaltPruefen lehnt sie bei jedem Vorwert ab. Die Zeile sagt
            // es dem Nutzer vorher, rueckweg.moeglich = "0" dem Host bei jedem Lauf, damit die
            // Oberflaeche keinen Knopf zeigt, der immer scheitert.
            LeistungMassnahmen.RueckwegAusgeschlossen(k, "Startart " + LeistungMassnahmen.StartartWort(vorher) + " und Zustand werden gesichert, aber nie zurückgespielt: Windows-Update-Dienste werden nicht wieder angehalten oder umgestellt (Rückgängig ist hier nicht möglich).");

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("name", name),
                Sicherungsdatei.Werte("startart", vorher, "zustand", zustand));

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI Win32_Service(" + name + ").ChangeStartMode(Automatic)" + (laeuft ? "" : " + StartService()"), "dim");
                Nachweise.Trocken(k, kennung, "Der Dienst „" + name + "“ würde auf Startart automatisch gestellt" + (laeuft ? "." : " und gestartet."));
                return;
            }

            if (!auto)
            {
                string fehler = LeistungMassnahmen.DienstStartartSetzen(k, name, "Automatic");
                if (fehler != null)
                {
                    Nachweise.NichtBestanden(k, kennung, fehler + " Der Dienst „" + name + "“ bleibt " + LeistungMassnahmen.StartartWort(vorher) + ".");
                    return;
                }
                k.Schreibe("   Startart von „" + name + "“ auf automatisch gestellt.", "dim");
            }
            if (!laeuft)
            {
                long? rc = LeistungMassnahmen.DienstMethode(name, "StartService");
                // 10 = laeuft bereits (zwischen Lesen und Start selbst gestartet): kein Fehler.
                if (rc != 0 && rc != 10)
                {
                    Nachweise.NichtBestanden(k, kennung, "StartService meldete " + LeistungMassnahmen.DienstFehler(rc) + "; die Startart ist automatisch, der Dienst „" + name + "“ läuft aber nicht.");
                    return;
                }
            }
            LeistungMassnahmen.DienstStand nach = LaufendAbwarten(k, name);
            bool nachAuto = nach.Gefunden && LeistungMassnahmen.StartartNorm(nach.Startart) == "Automatic";
            bool nachLaeuft = nach.Gefunden && string.Equals(nach.Zustand, "Running", StringComparison.OrdinalIgnoreCase);
            if (nachAuto && nachLaeuft)
                Nachweise.Bestanden(k, kennung, "Der Dienst „" + name + "“ läuft (Startart automatisch, vorher " + LeistungMassnahmen.StartartWort(vorher) + ").");
            else
                Nachweise.NichtBestanden(k, kennung, "Der Dienst „" + name + "“ zeigt nach dem Aufruf Startart " + LeistungMassnahmen.StartartWort(nach.Startart) + " und Zustand " + (nach.Zustand ?? "unbekannt") + ".");
        }

        /// <summary>Bis 30 s warten, bis Win32_Service.State Running meldet (StartService kehrt vor dem Ende des Starts zurueck).</summary>
        static LeistungMassnahmen.DienstStand LaufendAbwarten(Ausfuehrungskontext k, string name)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LeistungMassnahmen.DienstStand stand = LeistungMassnahmen.DienstLesen(name);
            while (stand.Gefunden && !string.Equals(stand.Zustand, "Running", StringComparison.OrdinalIgnoreCase) && sw.ElapsedMilliseconds < StartWartenMs)
            {
                if (k.IstAbgebrochen) break;
                Thread.Sleep(1000);
                stand = LeistungMassnahmen.DienstLesen(name);
            }
            return stand;
        }

        // ================================================================ update.verbergen

        /// <summary>UpdateID als GUID in Kleinschreibung ohne Klammern (wie kern/Regeln/Updates.cs und der Sammler), sonst null.</summary>
        public static string UpdateIdNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            Guid g;
            return Guid.TryParse(s.Trim(), out g) ? g.ToString("D") : null;
        }

        public static string VerbergenPruefen(PlanSchritt s)
        {
            return UpdateIdNorm(s.Wert("updateId")) == null
                ? "Der Parameter updateId muss eine GUID sein (etwa 12345678-9abc-def0-1234-56789abcdef0), nicht „" + Massnahmenbau.Kurz(s.Wert("updateId")) + "“."
                : null;
        }

        public sealed class UpdateStand
        {
            public bool Gefunden;
            public bool Verborgen;
            public string Titel;
        }

        /// <summary>
        /// Sucht die UpdateID im lokalen Cache des Windows-Update-Agenten (Online=false, Gegenprobe:
        /// laesst sich Online nicht abschalten, wird nicht gesucht) und setzt, wenn setzen einen
        /// Wert traegt, IsHidden darauf. Ein ganzer Block auf einem Thread mit Zeitgrenze, Objekte
        /// am Ende freigegeben. Suche mit "UpdateID='...'" findet verborgene und sichtbare Updates.
        /// </summary>
        public static UpdateStand ImCache(string updateId, bool? setzen)
        {
            return Zeitgrenze.Mit(() =>
            {
                object session = null, searcher = null, res = null, ups = null, up = null;
                try
                {
                    session = Com.Neu("Microsoft.Update.Session");
                    searcher = Com.Rufe(session, "CreateUpdateSearcher");
                    Com.Setze(searcher, "Online", false);
                    if (Convert.ToBoolean(Com.Hole(searcher, "Online"), CultureInfo.InvariantCulture))
                        throw new InvalidOperationException("Online ließ sich nicht auf false setzen; es wird nicht gesucht.");
                    res = Com.Rufe(searcher, "Search", "UpdateID='" + updateId + "'");
                    // OperationResultCode: 2 Succeeded, 3 SucceededWithErrors; alles andere ist keine Antwort.
                    int rc = Convert.ToInt32(Com.Hole(res, "ResultCode"), CultureInfo.InvariantCulture);
                    if (rc != 2 && rc != 3) throw new InvalidOperationException("Die Suche im Update-Cache meldete ResultCode " + rc + ".");
                    ups = Com.Hole(res, "Updates");
                    int n = Convert.ToInt32(Com.Hole(ups, "Count"), CultureInfo.InvariantCulture);
                    if (n == 0) return new UpdateStand { Gefunden = false };
                    up = Com.Hole(ups, "Item", 0);
                    var stand = new UpdateStand
                    {
                        Gefunden = true,
                        Titel = Com.Hole(up, "Title") as string,
                        Verborgen = Convert.ToBoolean(Com.Hole(up, "IsHidden"), CultureInfo.InvariantCulture),
                    };
                    if (setzen.HasValue && stand.Verborgen != setzen.Value)
                    {
                        Com.Setze(up, "IsHidden", setzen.Value);
                        stand.Verborgen = Convert.ToBoolean(Com.Hole(up, "IsHidden"), CultureInfo.InvariantCulture);
                    }
                    return stand;
                }
                finally { Com.Freigeben(up); Com.Freigeben(ups); Com.Freigeben(res); Com.Freigeben(searcher); Com.Freigeben(session); }
            }, WuaMs, "Der Windows-Update-Agent");
        }

        public static string Anzeige(UpdateStand u, string updateId)
        {
            return string.IsNullOrWhiteSpace(u.Titel) ? updateId : u.Titel.Trim();
        }

        public static void Verbergen(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "update.verbergen";
            string id = UpdateIdNorm(s.Wert("updateId"));
            UpdateStand stand = ImCache(id, null);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Der Windows-Update-Agent kennt kein Update mit der Kennung " + id + "; verbergen lässt sich nur, was er in seinem Cache hat (nach der nächsten Update-Suche erneut versuchen).", "bad");
                Katalog.Problem(k);
                return;
            }
            string titel = Anzeige(stand, id);
            if (stand.Verborgen)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "„" + titel + "“ war schon verborgen; es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   Update „" + titel + "“ (" + id + "): sichtbar, wird verborgen.", "dim");

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("updateId", id), Sicherungsdatei.Werte("isHidden", "0", "titel", titel));

            if (k.Trocken)
            {
                k.Schreibe("(trocken) COM Microsoft.Update.Session: IUpdate(" + id + ").IsHidden = true", "dim");
                Nachweise.Trocken(k, kennung, "„" + titel + "“ würde verborgen (IsHidden = true).");
                return;
            }
            UpdateStand gesetzt = ImCache(id, true);
            if (!gesetzt.Gefunden || !gesetzt.Verborgen)
            {
                Nachweise.NichtBestanden(k, kennung, "IsHidden ließ sich für „" + titel + "“ nicht setzen (Pflicht-Updates lassen sich nicht verbergen).");
                return;
            }
            // Frische Suche als Nachweis, nicht das eben beschriebene Objekt.
            UpdateStand nach = ImCache(id, null);
            if (nach.Gefunden && nach.Verborgen)
                Nachweise.Bestanden(k, kennung, "„" + titel + "“ ist verborgen (IsHidden = true); Windows Update bietet es nicht mehr an, bis es wieder eingeblendet wird.");
            else
                Nachweise.NichtBestanden(k, kennung, "IsHidden wurde gesetzt, eine erneute Suche zeigt „" + titel + "“ aber weiter als sichtbar.");
        }

        // ================================================================ update.zuruecksetzen

        /// <summary>
        /// Windows-Update-Reset (Vertrag M4, Abschnitte 1 und 3.5), Stufe 3 ohne Rueckweg (die
        /// Sicherung ist der Beleg, die Ausblendungen setzt die Massnahme selbst wieder):
        /// 1 verborgene Updates lesen (WUA Search("IsHidden=1"), Online=false) und sichern
        ///   (vorher {verborgen} = UpdateID|Revision|Titel je Zeile);
        /// 2 Dienste wuauserv, bits, cryptsvc stoppen (Win32_Service.StopService, je bis 60 s auf
        ///   State Stopped warten); scheitert einer, werden die schon gestoppten wieder gestartet,
        ///   und die Massnahme bricht VOR dem Umbenennen ab (Problem, Exit 1);
        /// 3 %SystemRoot%\SoftwareDistribution und System32\catroot2 nach .old (Directory.Move; ein
        ///   vorhandenes .old wird davor geloescht, noch vor dem Stoppen, weil dort nichts in
        ///   Benutzung ist); scheitert ein Move, geht das schon Umbenannte zurueck, die Dienste
        ///   starten, Problem;
        /// 4 Dienste in umgekehrter Reihenfolge starten (StartService, bis 60 s auf Running);
        /// 5 je gesicherter UpdateID: Search("UpdateID='...'") mit Online=true, IsHidden = true
        ///   (120 s je Suche, 20 min gesamt).
        /// Werte: update.verborgen.vorher (;-Liste der GUIDs), update.verborgen.wieder (Anzahl),
        /// neustart = "1" (immer empfohlen), rueckweg.moeglich = "0". Vorbedingung: wuauserv darf
        /// nicht Disabled sein (sonst Zeile bad, Problem). Trockenlauf: verborgene Updates zaehlen
        /// (lesend), alle Schritte als (trocken)-Zeilen, kein Stop, kein Move, keine Datei.
        /// </summary>
        public const string KennungReset = "update.zuruecksetzen";
        const int StoppWartenMs = 60000;
        const int SucheJeGuidMs = 120000;
        const int ResetGesamtMs = 20 * 60 * 1000;
        static readonly string[] ResetDienste = { "wuauserv", "bits", "cryptsvc" };

        public sealed class Verborgen
        {
            public string Id;
            public string Revision;
            public string Titel;
        }

        public static void Zuruecksetzen(PlanSchritt s, Ausfuehrungskontext k)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Vorbedingung: der Update-Dienst darf nicht deaktiviert sein.
            LeistungMassnahmen.DienstStand wu = LeistungMassnahmen.DienstLesen("wuauserv");
            if (!wu.Gefunden)
            {
                k.Schreibe("   Der Dienst „wuauserv“ ist auf diesem PC nicht vorhanden; Windows Update wird nicht zurückgesetzt.", "bad");
                Katalog.Problem(k);
                return;
            }
            if (LeistungMassnahmen.StartartNorm(wu.Startart) == "Disabled")
            {
                k.Schreibe("   Der Dienst „wuauserv“ ist deaktiviert; erst den Update-Dienst wieder einschalten (Maßnahme „Update-Dienst wieder einschalten“), dann zurücksetzen.", "bad");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, KennungReset, "Vorbedingung: wuauserv ist Disabled", new { startart = wu.Startart });
                Katalog.Problem(k);
                return;
            }

            // 1 Verborgene Updates lesen und sichern.
            k.Melde(1, 5, "Verborgene Updates werden gesichert");
            List<Verborgen> verborgen;
            try { verborgen = VerborgeneLesen(); }
            catch (Exception ex)
            {
                k.Schreibe("   Die verborgenen Updates ließen sich nicht lesen (" + ex.Message + "); ohne diese Sicherung wird Windows Update nicht zurückgesetzt.", "bad");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, KennungReset, "Verborgene Updates nicht lesbar: " + ex.Message, null);
                Katalog.Problem(k);
                return;
            }
            var ids = new List<string>();
            var zeilen = new StringBuilder();
            foreach (Verborgen v in verborgen)
            {
                ids.Add(v.Id);
                if (zeilen.Length > 0) zeilen.Append('\n');
                zeilen.Append(v.Id).Append('|').Append(v.Revision).Append('|').Append(Feld(v.Titel));
            }
            k.Schreibe("   " + Anzahl(verborgen.Count) + " gefunden" + (verborgen.Count == 0 ? "." : verborgen.Count == 1 ? "; es wird gesichert und nach dem Zurücksetzen wieder verborgen." : "; sie werden gesichert und nach dem Zurücksetzen wieder verborgen."), "dim");
            k.Werte["update.verborgen.vorher"] = string.Join(";", ids);
            LeistungMassnahmen.RueckwegAusgeschlossen(k, "Die Sicherung nennt " + (verborgen.Count == 1 ? "das 1 verborgene Update" : "die " + verborgen.Count + " verborgenen Updates") + " als Beleg; ein Rückgängig gibt es nicht, der Update-Verlauf ist nach dem Zurücksetzen leer.");
            Sicherungsdatei.Schreiben(k, KennungReset, new Dictionary<string, string>(), Sicherungsdatei.Werte("verborgen", zeilen.ToString()));
            if (k.IstAbgebrochen) return;

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sd = Path.Combine(windows, "SoftwareDistribution");
            string cr = Path.Combine(Environment.SystemDirectory, "catroot2");
            string[] ordner = { sd, cr };

            if (k.Trocken)
            {
                foreach (string d in ResetDienste) k.Schreibe("(trocken) WMI Win32_Service(" + d + ").StopService()", "dim");
                foreach (string o in ordner) k.Schreibe("(trocken) Directory.Move " + o + " → " + o + ".old" + (Directory.Exists(o) ? "" : " (nicht vorhanden)"), "dim");
                for (int i = ResetDienste.Length - 1; i >= 0; i--) k.Schreibe("(trocken) WMI Win32_Service(" + ResetDienste[i] + ").StartService()", "dim");
                k.Schreibe("(trocken) COM Microsoft.Update.Session: " + verborgen.Count + " Updates erneut verbergen (Online=true)", "dim");
                k.Werte["neustart"] = "1";
                Nachweise.Trocken(k, KennungReset, "Windows Update würde zurückgesetzt (3 Dienste, 2 Ordner nach .old) und " + verborgen.Count + " Updates würden wieder verborgen; danach ist 1 Neustart empfohlen.");
                return;
            }

            // Alte .old-Reste zuerst weg (nichts davon ist in Benutzung): scheitert das, laeuft
            // kein Dienst-Stopp ins Leere.
            foreach (string o in ordner)
            {
                string alt = o + ".old";
                if (!Directory.Exists(alt)) continue;
                try
                {
                    Directory.Delete(alt, true);
                    k.Schreibe("   Rest eines früheren Zurücksetzens entfernt: " + alt, "dim");
                }
                catch (Exception ex)
                {
                    Nachweise.NichtBestanden(k, KennungReset, "Der Ordner " + alt + " aus einem früheren Zurücksetzen ließ sich nicht entfernen (" + ex.Message + "); es wurde nichts angehalten und nichts umbenannt.");
                    return;
                }
            }

            // 2 Dienste stoppen.
            k.Melde(2, 5, "Die Update-Dienste werden angehalten");
            var gestoppt = new List<string>();
            foreach (string d in ResetDienste)
            {
                if (k.IstAbgebrochen) { DiensteStarten(k, gestoppt); return; }
                string grund = DienstStoppen(k, d);
                if (grund != null)
                {
                    DiensteStarten(k, gestoppt);
                    Nachweise.NichtBestanden(k, KennungReset, grund + " Die " + gestoppt.Count + " schon angehaltenen Dienste laufen wieder; es wurde nichts umbenannt.");
                    return;
                }
                gestoppt.Add(d);
            }

            // 3 Ordner umbenennen.
            k.Melde(3, 5, "Der Update-Speicher wird beiseitegelegt");
            var umbenannt = new List<string>();
            foreach (string o in ordner)
            {
                if (!Directory.Exists(o))
                {
                    k.Schreibe("   " + o + " ist nicht vorhanden; es gibt dort nichts beiseitezulegen.", "dim");
                    continue;
                }
                try
                {
                    Directory.Move(o, o + ".old");
                    umbenannt.Add(o);
                    k.Schreibe("   " + Path.GetFileName(o) + " → " + Path.GetFileName(o) + ".old", "dim");
                }
                catch (Exception ex)
                {
                    int zurueck = 0;
                    for (int i = umbenannt.Count - 1; i >= 0; i--)
                    {
                        try { Directory.Move(umbenannt[i] + ".old", umbenannt[i]); zurueck++; }
                        catch (Exception ex2) { AppLog.Warn(KennungReset + ": " + umbenannt[i] + " nicht zurückbenannt: " + ex2.Message); }
                    }
                    DiensteStarten(k, gestoppt);
                    Nachweise.NichtBestanden(k, KennungReset, o + " ließ sich nicht umbenennen (" + ex.Message + "); " + zurueck + " von " + umbenannt.Count + " schon umbenannten Ordnern sind zurück, die Dienste laufen wieder.");
                    return;
                }
            }
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, KennungReset, umbenannt.Count + " Ordner nach .old umbenannt", new { umbenannt });

            // 4 Dienste starten (umgekehrte Reihenfolge).
            k.Melde(4, 5, "Die Update-Dienste werden gestartet");
            int nichtGestartet = DiensteStarten(k, gestoppt);
            k.Werte["neustart"] = "1";

            // 5 Ausblendungen wiederherstellen.
            k.Melde(5, 5, "Verborgene Updates werden wieder verborgen");
            int wieder = 0;
            var offen = new List<string>();
            foreach (Verborgen v in verborgen)
            {
                if (k.IstAbgebrochen || sw.ElapsedMilliseconds > ResetGesamtMs) { offen.Add(v.Id); continue; }
                bool ok = false;
                try { ok = ErneutVerbergen(v.Id); }
                catch (Exception ex) { AppLog.Warn(KennungReset + ": " + v.Id + " nicht erneut verborgen: " + ex.Message); }
                if (ok) wieder++; else offen.Add(v.Id);
                k.Schreibe("   " + (ok ? "wieder verborgen: " : "nicht wieder verborgen: ") + Massnahmenbau.Kurz(string.IsNullOrEmpty(v.Titel) ? v.Id : v.Titel), ok ? "dim" : "warn");
            }
            k.Werte["update.verborgen.wieder"] = wieder.ToString(CultureInfo.InvariantCulture);
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, KennungReset, wieder + " von " + verborgen.Count + " Updates wieder verborgen",
                    new { vorher = verborgen.Count, wieder, offen, nichtGestartet, sekunden = (int)sw.Elapsed.TotalSeconds });

            string satz = "Windows Update ist zurückgesetzt: " + umbenannt.Count + " Ordner liegen als .old beiseite, "
                          + wieder + " von " + verborgen.Count + " Updates sind wieder verborgen"
                          + (nichtGestartet > 0 ? ", " + nichtGestartet + " Dienst(e) laufen nicht" : ", die 3 Dienste laufen")
                          + "; 1 Neustart ist empfohlen.";
            if (nichtGestartet == 0 && offen.Count == 0)
                Nachweise.Bestanden(k, KennungReset, satz);
            else
                Nachweise.NichtBestanden(k, KennungReset, satz + (offen.Count > 0 ? " Nicht wieder verborgen: " + string.Join(", ", offen) + "." : ""));
        }

        static string Anzahl(int n)
        {
            return n == 1 ? "1 verborgenes Update" : n + " verborgene Updates";
        }

        /// <summary>Titel ohne Trenner der Sicherungszeile (| und Zeilenumbruch), hoechstens 200 Zeichen.</summary>
        static string Feld(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
            return t.Length > 200 ? t.Substring(0, 200) : t;
        }

        /// <summary>Alle verborgenen Updates aus dem lokalen Cache des Windows-Update-Agenten (Online=false, mit Gegenprobe).</summary>
        public static List<Verborgen> VerborgeneLesen()
        {
            return Zeitgrenze.Mit(() =>
            {
                var liste = new List<Verborgen>();
                object session = null, searcher = null, res = null, ups = null;
                try
                {
                    session = Com.Neu("Microsoft.Update.Session");
                    searcher = Com.Rufe(session, "CreateUpdateSearcher");
                    Com.Setze(searcher, "Online", false);
                    if (Convert.ToBoolean(Com.Hole(searcher, "Online"), CultureInfo.InvariantCulture))
                        throw new InvalidOperationException("Online ließ sich nicht auf false setzen; es wird nicht gesucht.");
                    res = Com.Rufe(searcher, "Search", "IsHidden=1");
                    int rc = Convert.ToInt32(Com.Hole(res, "ResultCode"), CultureInfo.InvariantCulture);
                    if (rc != 2 && rc != 3) throw new InvalidOperationException("Die Suche im Update-Cache meldete ResultCode " + rc + ".");
                    ups = Com.Hole(res, "Updates");
                    int n = Convert.ToInt32(Com.Hole(ups, "Count"), CultureInfo.InvariantCulture);
                    for (int i = 0; i < n; i++)
                    {
                        object up = null, identity = null;
                        try
                        {
                            up = Com.Hole(ups, "Item", i);
                            identity = Com.Hole(up, "Identity");
                            string id = UpdateIdNorm(Com.Hole(identity, "UpdateID") as string);
                            if (id == null) continue;   // ohne GUID kein Eintrag: nichts Freies in die Sicherung
                            liste.Add(new Verborgen
                            {
                                Id = id,
                                Revision = Convert.ToInt64(Com.Hole(identity, "RevisionNumber"), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                                Titel = Com.Hole(up, "Title") as string,
                            });
                        }
                        finally { Com.Freigeben(identity); Com.Freigeben(up); }
                    }
                    return liste;
                }
                finally { Com.Freigeben(ups); Com.Freigeben(res); Com.Freigeben(searcher); Com.Freigeben(session); }
            }, WuaMs, "Der Windows-Update-Agent");
        }

        /// <summary>Sucht die UpdateID online (der Cache ist nach dem Zurücksetzen leer) und setzt IsHidden = true; true = danach verborgen.</summary>
        static bool ErneutVerbergen(string updateId)
        {
            return Zeitgrenze.Mit(() =>
            {
                object session = null, searcher = null, res = null, ups = null, up = null;
                try
                {
                    session = Com.Neu("Microsoft.Update.Session");
                    searcher = Com.Rufe(session, "CreateUpdateSearcher");
                    Com.Setze(searcher, "Online", true);
                    res = Com.Rufe(searcher, "Search", "UpdateID='" + updateId + "'");
                    int rc = Convert.ToInt32(Com.Hole(res, "ResultCode"), CultureInfo.InvariantCulture);
                    if (rc != 2 && rc != 3) return false;
                    ups = Com.Hole(res, "Updates");
                    int n = Convert.ToInt32(Com.Hole(ups, "Count"), CultureInfo.InvariantCulture);
                    if (n == 0) return false;
                    up = Com.Hole(ups, "Item", 0);
                    if (!Convert.ToBoolean(Com.Hole(up, "IsHidden"), CultureInfo.InvariantCulture))
                        Com.Setze(up, "IsHidden", true);
                    return Convert.ToBoolean(Com.Hole(up, "IsHidden"), CultureInfo.InvariantCulture);
                }
                finally { Com.Freigeben(up); Com.Freigeben(ups); Com.Freigeben(res); Com.Freigeben(searcher); Com.Freigeben(session); }
            }, SucheJeGuidMs, "Die Online-Suche nach " + updateId);
        }

        /// <summary>StopService und bis 60 s auf State Stopped warten. null = gestoppt (oder war schon), sonst der Grund.</summary>
        static string DienstStoppen(Ausfuehrungskontext k, string name)
        {
            LeistungMassnahmen.DienstStand stand = LeistungMassnahmen.DienstLesen(name);
            if (!stand.Gefunden) return "Der Dienst „" + name + "“ ist nicht vorhanden.";
            if (Ist(stand, "Stopped")) { k.Schreibe("   Dienst „" + name + "“ war schon angehalten.", "dim"); return null; }
            long? rc = LeistungMassnahmen.DienstMethode(name, "StopService");
            // 6 = der Dienst ist nicht aktiv (zwischen Lesen und Stopp von selbst beendet).
            if (rc != 0 && rc != 6)
                return "Der Dienst „" + name + "“ ließ sich nicht anhalten: StopService meldete " + LeistungMassnahmen.DienstFehler(rc) + ".";
            stand = ZustandAbwarten(k, name, "Stopped");
            if (!Ist(stand, "Stopped"))
                return "Der Dienst „" + name + "“ meldet " + (StoppWartenMs / 1000) + " Sekunden nach StopService weiterhin " + (stand.Zustand ?? "unbekannt") + ".";
            k.Schreibe("   Dienst „" + name + "“ angehalten.", "dim");
            return null;
        }

        /// <summary>Startet die Dienste in umgekehrter Reihenfolge der Liste; Rueckgabe: wie viele danach nicht Running melden.</summary>
        static int DiensteStarten(Ausfuehrungskontext k, List<string> gestoppt)
        {
            int nicht = 0;
            for (int i = gestoppt.Count - 1; i >= 0; i--)
            {
                string name = gestoppt[i];
                long? rc = LeistungMassnahmen.DienstMethode(name, "StartService");
                // 10 = laeuft bereits.
                if (rc != 0 && rc != 10)
                {
                    nicht++;
                    k.Schreibe("   Dienst „" + name + "“ ließ sich nicht starten: StartService meldete " + LeistungMassnahmen.DienstFehler(rc) + ".", "warn");
                    continue;
                }
                LeistungMassnahmen.DienstStand stand = ZustandAbwarten(k, name, "Running");
                if (Ist(stand, "Running")) k.Schreibe("   Dienst „" + name + "“ läuft wieder.", "dim");
                else
                {
                    nicht++;
                    k.Schreibe("   Dienst „" + name + "“ meldet " + (StoppWartenMs / 1000) + " Sekunden nach StartService " + (stand.Zustand ?? "unbekannt") + ".", "warn");
                }
            }
            return nicht;
        }

        static bool Ist(LeistungMassnahmen.DienstStand stand, string zustand)
        {
            return stand != null && stand.Gefunden && string.Equals(stand.Zustand, zustand, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Bis 60 s in Sekundenschritten nachlesen, bis Win32_Service.State den Zielzustand meldet; ein Abbruch beendet das Warten.</summary>
        static LeistungMassnahmen.DienstStand ZustandAbwarten(Ausfuehrungskontext k, string name, string ziel)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LeistungMassnahmen.DienstStand stand = LeistungMassnahmen.DienstLesen(name);
            while (stand.Gefunden && !Ist(stand, ziel) && sw.ElapsedMilliseconds < StoppWartenMs)
            {
                if (k.IstAbgebrochen) break;
                Thread.Sleep(1000);
                stand = LeistungMassnahmen.DienstLesen(name);
            }
            return stand;
        }
    }
}
