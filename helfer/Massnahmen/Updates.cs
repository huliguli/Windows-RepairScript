using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Updates (docs/M3-ENTWURF.md, Abschnitt 1): update.dienst.aktivieren, update.verbergen.
        // Anmeldung hier, Arbeit in UpdatesMassnahmen.
        static partial void Updates(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("update.dienst.aktivieren", "Update-Dienst wieder einschalten", 2,
                UpdatesMassnahmen.DienstPruefen, UpdatesMassnahmen.DienstAktivieren));
            l.Add(Massnahmenbau.Neu("update.verbergen", "Update verbergen", 2,
                UpdatesMassnahmen.VerbergenPruefen, UpdatesMassnahmen.Verbergen));
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
    }
}
