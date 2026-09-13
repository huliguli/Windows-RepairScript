using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Regeln fuer Windows Update: reine Funktion ueber dem Systembild, kein Windows-Zugriff,
    /// kein DateTime.Now (ctx.Jetzt ist die Aufzeichnungszeit), jede Schwelle aus Schwellen.cs.
    ///
    /// Grundsaetze dieses Bereichs:
    /// - Der COM-Verlauf beginnt nach jedem Update-Reset neu; das System-Protokoll
    ///   (WindowsUpdateClient 19/20) reicht weiter zurueck. Beide Quellen koennen denselben
    ///   Vorgang sehen, deshalb zaehlt je Kennung das MAXIMUM aus beiden, nie die Summe.
    /// - Ein verborgenes Update installiert sich nicht mehr: seine alte Schleife oder seine
    ///   alten Fehlschlaege sind kein offener Befund, sondern ein Hinweis beim Info-Befund
    ///   "verborgen" (der HP-USB-Fall aus der Memory, am 04.09.2026 verborgen).
    /// - Fehlercodes werden nur gedeutet, wenn sie in der eingebetteten Tabelle stehen;
    ///   Unbekanntes bleibt ohne Deutung (keine erfundenen Diagnosen).
    /// - Titel sind Anzeige. Entschieden wird ueber UpdateID, ResultCode, HResult, Datum.
    /// - Der Store-Dienst installiert dieselbe Kennung regulaer immer wieder (gemessen auf
    ///   diesem Rechner: Microsoft.WindowsAppRuntime.2 sechsmal in 9 Tagen); seine Eintraege
    ///   sind keine Schleife. Eine Deinstallation (Operation 2) ist keine Installation.
    /// </summary>
    public static class Updates
    {
        const string QuelleCom = "com.windowsupdate";
        const string Verbergen = "update.verbergen";
        const string EinstellungenOeffnen = "update.einstellungen.oeffnen";
        /// <summary>ServiceID / serviceGuid des Microsoft Store (Doku "Windows Update service IDs"; gemessen am 13.09.2026 in Verlauf und Ereignis 19/20).</summary>
        const string StoreDienst = "855e8a7c-ecb4-4ca3-b045-1dfa50104289";
        /// <summary>IUpdateHistoryEntry.Operation (UpdateOperation): 1 Installation, 2 Deinstallation; 0 = Feld nicht ueberliefert (aeltere Bilder), zaehlt wie Installation.</summary>
        const int VorgangDeinstallation = 2;

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var u = s.WindowsUpdate;
            var e = new BereichErgebnis { Bereich = Bereich.Updates };

            bool comGesperrt = s.ZugriffVerweigert(QuelleCom);
            bool verlaufDa = u.Verlauf.Count > 0;
            bool autoDa = u.LetzteSucheUtc != null || u.LetzteInstallationUtc != null;
            bool registryDa = !s.FehlerVon("registry.windowsupdate.neustart").Any();
            e.DatenVorhanden = !comGesperrt && (verlaufDa || autoDa || registryDa);
            Fehlend(s, u, e, comGesperrt);

            var verborgen = new HashSet<string>(u.Verborgen.Select(v => Norm(v.UpdateId)).Where(x => x != null));
            var schleifen = Schleifen(ctx, u);
            var fehlschlaege = Fehlschlaege(ctx, u);

            Neustart(u, e);
            foreach (var f in fehlschlaege.Values.Where(f => !verborgen.Contains(f.Id))) e.Befunde.Add(FehlschlagBefund(ctx, f));
            foreach (var l in schleifen.Values.Where(l => !verborgen.Contains(l.Id))) e.Befunde.Add(SchleifeBefund(l));
            Sicherheitsalter(ctx, u, e);
            LetzteSuche(ctx, s, u, e);
            VerborgeneUpdates(u, e, schleifen, fehlschlaege);
            AusstehendeUpdates(u, e);
            Dienste(u, e);
            Verwaltet(u, e);
            return e;
        }

        // ---------------------------------------------------------------- was fehlte

        static void Fehlend(Systembild s, WindowsUpdate u, BereichErgebnis e, bool comGesperrt)
        {
            if (comGesperrt) e.Fehlend.Add("Windows-Update-Verlauf und -Suche (Zugriff verweigert)");
            foreach (var f in s.FehlerVon("com.windowsupdate.verlauf"))
            {
                if (f.Art == Fehler.Zugriff) continue;
                if (f.Art == Fehler.Fehlt && u.Verlauf.Count == 0)
                    // Der Sammler schreibt "fehlt" in zwei Faellen: wirklich leer (nach einem Reset
                    // normal) oder QueryHistory liefert nichts, obwohl es Eintraege gibt (Fehler).
                    e.Fehlend.Add((f.Text ?? "").StartsWith("QueryHistory")
                        ? "Update-Verlauf nicht lesbar (Windows nennt " + f.Text.Replace("QueryHistory lieferte 0 Einträge, obwohl GetTotalHistoryCount ", "").Replace(" meldet", "") + " Einträge, liefert aber keinen)"
                        : "Update-Verlauf ist leer (0 Einträge; nach einem Update-Reset normal, das System-Protokoll ergänzt)");
                else if (u.Verlauf.Count > 0)
                    // Der Sammler liest je Eintrag: die lesbaren stehen im Bild, die anderen im Fehlertext.
                    e.Fehlend.Add("Update-Verlauf unvollständig (einzelne Einträge nicht lesbar)");
                else
                    e.Fehlend.Add("Update-Verlauf (" + f.Art + ")");
            }
            if (s.FehlerVon("com.windowsupdate.suche.verborgen").Any(f => f.Art != Fehler.Zugriff)) e.Fehlend.Add("Liste der verborgenen Updates");
            if (s.FehlerVon("com.windowsupdate.suche.ausstehend").Any(f => f.Art != Fehler.Zugriff)) e.Fehlend.Add("Liste der ausstehenden Updates");
            if (s.FehlerVon("com.windowsupdate.autoupdate").Any(f => f.Art != Fehler.Zugriff && f.Art != Fehler.Fehlt)) e.Fehlend.Add("Zeitpunkt der letzten Update-Suche");
            if (s.FehlerVon("registry.windowsupdate.neustart").Any()) e.Fehlend.Add("Neustart-Kennzeichen (Registry)");
            // Daten schlagen den Fehlereintrag: ein aelteres Bild traegt neben der Dienstliste noch
            // "nicht registriert: DoSvc", und unter log.system.windowsupdateclient schreibt auch
            // die Ereignisquelle (Budget, Obergrenze), obwohl die Updatequelle das Log gelesen hat.
            if (u.Dienste.Count == 0 && s.FehlerVon("wmi.service.windowsupdate").Any()) e.Fehlend.Add("Zustand der Update-Dienste");
            if (u.FehlschlaegeLog.Count == 0 && u.ErfolgeLog.Count == 0 && s.FehlerVon("log.system.windowsupdateclient").Any()) e.Fehlend.Add("Update-Ereignisse im System-Protokoll");
            if (u.LetztesSicherheitsupdateUtc == null) e.Fehlend.Add("Alter des letzten Sicherheitsupdates" + SicherheitsupdateGrund(s));
        }

        /// <summary>
        /// Warum das Datum fehlt, nach Art des wmi.qfe-Fehlers; der Rohtext des Sammlers wird
        /// nicht durchgereicht. "fehlt" deckt alle drei Sammler-Faelle (0 Einträge, kein
        /// "Security Update", InstalledOn unlesbar) mit einem wahren Satz ab.
        /// </summary>
        static string SicherheitsupdateGrund(Systembild s)
        {
            var f = s.FehlerVon("wmi.qfe").FirstOrDefault();
            if (f == null) return "";
            switch (f.Art)
            {
                case Fehler.Zugriff: return " (Liste installierter Updates: Zugriff verweigert)";
                case Fehler.Zeit: return " (Liste installierter Updates antwortete nicht rechtzeitig)";
                case Fehler.Fehlt: return " (die Liste installierter Updates nennt kein Sicherheitsupdate mit lesbarem Datum; sie ist ein Indiz, kein Beweis)";
                default: return " (Liste installierter Updates nicht lesbar)";
            }
        }

        // ---------------------------------------------------------------- Neustart

        static void Neustart(WindowsUpdate u, BereichErgebnis e)
        {
            // PendingFileRenameOperations allein zaehlt nicht: Installer und Virenschutz nutzen
            // den Wert ebenso (hier vier Eintraege ohne jedes Update). Nur Detail.
            if (!u.NeustartWu && !u.NeustartCbs) return;
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.neustart",
                Zustand = Zustand.Warn,
                Messwert = Messwert.Von((u.NeustartWu ? 1 : 0) + (u.NeustartCbs ? 1 : 0), "Kennzeichen", "≥ 1"),
                Quelle = u.NeustartWu ? "Registry WindowsUpdate\\Auto Update\\RebootRequired" : "Registry Component Based Servicing\\RebootPending",
                Titel = "Ein Neustart steht aus",
                Satz = "Windows wartet auf einen Neustart, um ein Update abzuschließen; ohne ihn schlagen weitere Updates und Reparaturen oft fehl.",
                Rat = "Starten Sie den PC neu, bevor Sie Updates oder Reparaturen anstoßen.",
            };
            b.Detail.Add("Windows Update (Auto Update\\RebootRequired): " + (u.NeustartWu ? "vorhanden" : "fehlt"));
            b.Detail.Add("Komponentenspeicher (Component Based Servicing\\RebootPending): " + (u.NeustartCbs ? "vorhanden" : "fehlt"));
            b.Detail.Add("Dateiumbenennungen beim Start (PendingFileRenameOperations): " + (u.PendingRenames ? "vorhanden" : "keine") + " (allein kein Neustart-Signal)");
            e.Befunde.Add(b);
        }

        // ---------------------------------------------------------------- Fehlschlaege

        class Fall
        {
            public string Id;
            public string Titel;
            public int Verlauf, Log;
            public readonly List<long> Codes = new List<long>();
            public readonly List<string> Zeiten = new List<string>();
            public void Hinzu(string zeit, long code, string titel, bool ausLog)
            {
                if (ausLog) Log++; else Verlauf++;
                if (code != 0 && !Codes.Contains(code)) Codes.Add(code);
                if (zeit != null) Zeiten.Add(zeit);
                if (Titel == null && !string.IsNullOrWhiteSpace(titel)) Titel = titel;
            }
            public int Anzahl { get { return Math.Max(Verlauf, Log); } }
            public string Letzte { get { return Zeiten.Count == 0 ? null : Zeiten.Max(); } }
        }

        static Dictionary<string, Fall> Fehlschlaege(Kontext ctx, WindowsUpdate u)
        {
            var faelle = new Dictionary<string, Fall>();
            foreach (var v in u.Verlauf)
                if (v.Ergebnis == 4 && v.Vorgang != VorgangDeinstallation && ctx.Innerhalb(v.ZeitUtc, Schwellen.UpdateFehlschlagTage))
                    Holen(faelle, v.UpdateId).Hinzu(v.ZeitUtc, v.HResult, v.Titel, false);
            foreach (var f in u.FehlschlaegeLog)
                if (ctx.Innerhalb(f.ZeitUtc, Schwellen.UpdateFehlschlagTage))
                    Holen(faelle, f.UpdateId).Hinzu(f.ZeitUtc, f.FehlerCode, f.Titel, true);
            return faelle.Where(kv => kv.Value.Anzahl >= Schwellen.UpdateFehlschlagMin).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        static Befund FehlschlagBefund(Kontext ctx, Fall f)
        {
            // Der Zeitraum gehoert zur zaehlenden Quelle (dieselbe Wahl wie bei Quelle): das
            // Protokoll reicht nur bis zu seinem Anfang, der Verlauf ist auf das Fenster der Regel gefiltert.
            bool ausLog = f.Log >= f.Verlauf;
            string zeitraum = ausLog ? ctx.ZeitraumText("System") : "in den letzten " + Schwellen.UpdateFehlschlagTage + " Tagen";
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.fehlschlag." + f.Id,
                Zustand = Zustand.Warn,
                Messwert = Messwert.Von(f.Anzahl, "Fehlschläge", "≥ " + Schwellen.UpdateFehlschlagMin + " in " + Schwellen.UpdateFehlschlagTage + " Tagen"),
                Quelle = ausLog ? "WindowsUpdateClient 20.errorCode" : "IUpdateHistoryEntry.ResultCode 4",
                Titel = "Ein Update schlägt immer wieder fehl",
                Satz = "„" + Anzeige(f) + "“ ist " + zeitraum + " " + Text.Mal(f.Anzahl) + " fehlgeschlagen.",
                Rat = "Wenn dieses Update nicht gebraucht wird (etwa ein alter Hersteller-Treiber), lässt es sich verbergen; sonst hilft der Fehlercode im Detail weiter.",
            };
            b.Detail.Add("Kennung " + f.Id + (f.Letzte != null ? ", letzter Fehlschlag " + f.Letzte : ""));
            foreach (long code in f.Codes)
            {
                string deutung = Fehlercodes.Deutung(code);
                b.Detail.Add("Fehlercode 0x" + Hex(code) + ": " + (deutung ?? "keine Deutung hinterlegt"));
            }
            if (f.Codes.Count == 0) b.Detail.Add("Kein Fehlercode überliefert");
            b.Detail.Add("Zählung: Verlauf " + f.Verlauf + ", System-Protokoll " + f.Log + " (es zählt das Maximum, weil beide Quellen denselben Fehlschlag sehen können)");
            b.Massnahmen.Add(Verbergen);
            return b;
        }

        // ---------------------------------------------------------------- Schleife

        /// <summary>
        /// Dieselbe Kennung mehrfach erfolgreich installiert, in BEIDEN Quellen ohne den Store
        /// (der installiert App-Pakete regulaer unter derselben Kennung) und ohne
        /// Deinstallationen. Ein fehlendes Dienst-Feld (aeltere Bilder) zaehlt.
        /// </summary>
        static Dictionary<string, Fall> Schleifen(Kontext ctx, WindowsUpdate u)
        {
            var faelle = new Dictionary<string, Fall>();
            foreach (var v in u.Verlauf)
                if (v.Ergebnis == 2 && v.Vorgang != VorgangDeinstallation && !Store(v.DienstId) && ctx.Innerhalb(v.ZeitUtc, Schwellen.UpdateSchleifeTage))
                    Holen(faelle, v.UpdateId).Hinzu(v.ZeitUtc, 0, v.Titel, false);
            foreach (var f in u.ErfolgeLog)
                if (!Store(f.DienstId) && ctx.Innerhalb(f.ZeitUtc, Schwellen.UpdateSchleifeTage))
                    Holen(faelle, f.UpdateId).Hinzu(f.ZeitUtc, 0, f.Titel, true);
            return faelle.Where(kv => kv.Value.Anzahl >= Schwellen.UpdateSchleifeMin).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        static bool Store(string dienstId)
        {
            return Norm(dienstId) == StoreDienst;
        }

        static Befund SchleifeBefund(Fall l)
        {
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.schleife." + l.Id,
                Zustand = Zustand.Warn,
                Messwert = Messwert.Von(l.Anzahl, "Installationen", "≥ " + Schwellen.UpdateSchleifeMin + " in " + Schwellen.UpdateSchleifeTage + " Tagen"),
                Quelle = l.Log >= l.Verlauf ? "WindowsUpdateClient 19.updateGuid" : "IUpdateHistoryEntry.ResultCode 2",
                Titel = "Ein Update installiert sich immer wieder",
                Satz = "„" + Anzeige(l) + "“ wurde in den letzten " + Schwellen.UpdateSchleifeTage + " Tagen " + Text.Mal(l.Anzahl) + " erfolgreich installiert und kommt trotzdem immer wieder.",
                Rat = "Meist ist das ein alter Hersteller-Treiber, den Windows immer wieder anbietet; wenn das Gerät funktioniert, verbergen Sie das Update.",
            };
            b.Detail.Add("Kennung " + l.Id);
            foreach (string z in l.Zeiten.OrderByDescending(x => x).Take(6)) b.Detail.Add("Installiert " + z);
            b.Detail.Add("Zählung: Verlauf " + l.Verlauf + ", System-Protokoll " + l.Log + " (es zählt das Maximum)");
            b.Detail.Add("Ein Windows-Update-Reset löscht die Markierung „verborgen“; das System sichert verborgene Updates vorher und verbirgt sie danach erneut.");
            b.Massnahmen.Add(Verbergen);
            return b;
        }

        // ---------------------------------------------------------------- Sicherheitsupdate-Alter

        static void Sicherheitsalter(Kontext ctx, WindowsUpdate u, BereichErgebnis e)
        {
            var tage = ctx.TageSeit(u.LetztesSicherheitsupdateUtc);
            if (!tage.HasValue) return;   // steht schon in Fehlend
            if (tage.Value < 0) { e.Fehlend.Add("Alter des letzten Sicherheitsupdates (Datum liegt in der Zukunft, Uhr prüfen)"); return; }
            // Verglichen wird der ganze Tag, den Messwert und Satz zeigen: 45,3 Tage sind "45",
            // und 45 ueberschreitet die Schwelle 45 nicht.
            int t = (int)Math.Round(tage.Value);
            string zustand;
            if (t > Schwellen.SicherheitsupdateBadTage) zustand = Zustand.Bad;
            else if (t > Schwellen.SicherheitsupdateWarnTage) zustand = Zustand.Warn;
            else return;
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.sicherheitsalter",
                Zustand = zustand,
                Messwert = Messwert.Von(t, "Tage", "> " + Schwellen.SicherheitsupdateWarnTage + " warn, > " + Schwellen.SicherheitsupdateBadTage + " bad"),
                Quelle = "Win32_QuickFixEngineering.InstalledOn (Description 'Security Update')",
                Titel = zustand == Zustand.Bad ? "Sicherheitsupdates fehlen seit Monaten" : "Das letzte Sicherheitsupdate ist lange her",
                Satz = "Das letzte Sicherheitsupdate ist " + Text.Tage(t) + " her; Windows bekommt normalerweise jeden Monat eines.",
                Rat = "Öffnen Sie „Einstellungen > Windows Update“ und lassen Sie nach Updates suchen; bleibt das hängen, zeigt der Update-Verlauf den Fehlercode.",
            };
            b.Detail.Add("Letztes Sicherheitsupdate laut Liste installierter Updates: " + u.LetztesSicherheitsupdateUtc);
            b.Detail.Add("Indiz, kein Beweis: .NET-Sicherheitsupdates tragen dort nur „Update“, und die Liste kennt nur Pakete mit KB-Nummer.");
            if (u.LetzteInstallationUtc != null) b.Detail.Add("Letzte erfolgreiche Installation laut Windows Update: " + u.LetzteInstallationUtc);
            if (u.Verwaltet == true) b.Detail.Add("Updates kommen von einem Firmen-Update-Server; die Verzögerung kann an dessen Freigabe liegen.");
            b.Massnahmen.Add(EinstellungenOeffnen);
            e.Befunde.Add(b);
        }

        // ---------------------------------------------------------------- letzte Suche

        static void LetzteSuche(Kontext ctx, Systembild s, WindowsUpdate u, BereichErgebnis e)
        {
            var tage = ctx.TageSeit(u.LetzteSucheUtc);
            // "nie" gilt nur, wenn AutoUpdate gelesen wurde (kein Zeit-/Ausnahmefehler) und es
            // trotzdem einen Verlauf gibt; ohne Verlauf ist ein frisch aufgesetzter PC denkbar.
            bool autoGelesen = !s.FehlerVon("com.windowsupdate.autoupdate").Any(f => f.Art != Fehler.Fehlt);
            bool nie = !tage.HasValue && autoGelesen && u.Verlauf.Count > 0;
            // Ganze Tage wie im Messwert und im Satz (30,3 Tage sind "30" und nicht "> 30").
            int t = tage.HasValue ? (int)Math.Round(tage.Value) : 0;
            bool alt = tage.HasValue && t > Schwellen.UpdateSucheWarnTage;
            if (!nie && !alt) return;
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.suche.alt",
                Zustand = Zustand.Warn,
                Messwert = Messwert.Von(alt ? (object)t : "nie", "Tage", "> " + Schwellen.UpdateSucheWarnTage),
                Quelle = "IAutomaticUpdatesResults.LastSearchSuccessDate",
                Titel = "Lange nicht nach Updates gesucht",
                Satz = alt
                    ? "Die letzte erfolgreiche Suche nach Updates ist " + Text.Tage(t) + " her."
                    : "Windows hat noch nie erfolgreich nach Updates gesucht, obwohl der Verlauf " + u.Verlauf.Count + " Einträge hat.",
                Rat = "Öffnen Sie Windows Update und suchen Sie von Hand; schlägt das fehl, liegt es meist am Netz oder an einem abgeschalteten Dienst.",
            };
            if (u.LetzteSucheUtc != null) b.Detail.Add("Letzte erfolgreiche Suche: " + u.LetzteSucheUtc);
            if (u.LetzteInstallationUtc != null) b.Detail.Add("Letzte erfolgreiche Installation: " + u.LetzteInstallationUtc);
            if (u.Verwaltet == true) b.Detail.Add("Updates kommen von einem Firmen-Update-Server; auch dort muss die Suche gelingen.");
            b.Massnahmen.Add(EinstellungenOeffnen);
            e.Befunde.Add(b);
        }

        // ---------------------------------------------------------------- verborgen, ausstehend

        static void VerborgeneUpdates(WindowsUpdate u, BereichErgebnis e, Dictionary<string, Fall> schleifen, Dictionary<string, Fall> fehlschlaege)
        {
            int n = u.Verborgen.Count;
            if (n == 0) return;
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.verborgen",
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von(n, "Updates", null),
                Quelle = "IUpdateSearcher.Search(\"IsHidden=1\")",
                Titel = "Verborgene Updates",
                Satz = n == 1
                    ? "1 Update ist absichtlich verborgen und wird nicht installiert."
                    : n + " Updates sind absichtlich verborgen und werden nicht installiert.",
                Rat = null,
            };
            foreach (var v in u.Verborgen)
            {
                string id = Norm(v.UpdateId);
                string zeile = (string.IsNullOrWhiteSpace(v.Titel) ? "Update" : v.Titel) + " (" + (v.Typ == 2 ? "Treiber" : "Software") + ", Kennung " + (id ?? "?") + ")";
                Fall l, f;
                if (id != null && schleifen.TryGetValue(id, out l)) zeile += "; hatte sich " + Text.Mal(l.Anzahl) + " in " + Schwellen.UpdateSchleifeTage + " Tagen installiert";
                if (id != null && fehlschlaege.TryGetValue(id, out f)) zeile += "; war " + Text.Mal(f.Anzahl) + " fehlgeschlagen";
                b.Detail.Add(zeile);
            }
            b.Detail.Add("Vor einem Windows-Update-Reset werden diese Einträge gesichert und danach wieder verborgen.");
            e.Befunde.Add(b);
        }

        static void AusstehendeUpdates(WindowsUpdate u, BereichErgebnis e)
        {
            int n = u.Ausstehend.Count;
            if (n == 0) return;
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.ausstehend",
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von(n, "Updates", null),
                Quelle = "IUpdateSearcher.Search(\"IsInstalled=0 and IsHidden=0\")",
                Titel = "Updates warten auf die Installation",
                Satz = n + (n == 1 ? " Update ist" : " Updates sind") + " bekannt, aber noch nicht installiert (Stand der letzten Suche, ohne neue Online-Suche).",
                Rat = "Öffnen Sie Windows Update, um sie zu installieren.",
            };
            foreach (var a in u.Ausstehend)
                b.Detail.Add((string.IsNullOrWhiteSpace(a.Titel) ? "Update" : a.Titel) + " (" + (a.Typ == 2 ? "Treiber" : "Software") + ", Kennung " + (Norm(a.UpdateId) ?? "?") + ")");
            e.Befunde.Add(b);
        }

        // ---------------------------------------------------------------- Dienste, verwaltet

        static void Dienste(WindowsUpdate u, BereichErgebnis e)
        {
            string wu = DienstWert(u, "wuauserv");
            if (Deaktiviert(wu))
            {
                var b = new Befund
                {
                    Bereich = Bereich.Updates,
                    Schluessel = "update.dienst.wuauserv",
                    Zustand = Zustand.Bad,
                    Messwert = Messwert.Von(wu, "StartMode/State", "≠ Disabled"),
                    Quelle = "Win32_Service.StartMode (wuauserv)",
                    Titel = "Der Windows-Update-Dienst ist abgeschaltet",
                    Satz = "Der Dienst „Windows Update“ ist deaktiviert; so kann Windows keine Updates mehr installieren.",
                    Rat = "Stellen Sie den Dienst wieder auf „Manuell“ (Standard); meist hat ein „Update-Blocker“-Programm ihn abgeschaltet.",
                };
                DiensteDetail(u, b);
                b.Massnahmen.Add("update.dienst.aktivieren");
                e.Befunde.Add(b);
            }
            // Die Helfer von Windows Update: ohne sie scheitern Download (BITS), Signaturpruefung
            // (CryptSvc) und Installation (TrustedInstaller). Doku: "Windows Update - additional
            // resources", Dienste, die laufen muessen.
            string[][] helfer =
            {
                new[] { "bits", "BITS", "ohne ihn kann Windows Update nichts herunterladen." },
                new[] { "cryptsvc", "Kryptografiedienste", "ohne ihn kann Windows Update keine Signaturen prüfen." },
                new[] { "trustedinstaller", "Windows Modules Installer", "ohne ihn lässt sich kein Update installieren." },
            };
            foreach (var h in helfer)
            {
                string wert = DienstWert(u, h[0]);
                if (!Deaktiviert(wert)) continue;
                var b = new Befund
                {
                    Bereich = Bereich.Updates,
                    Schluessel = "update.dienst." + h[0],
                    Zustand = Zustand.Warn,
                    Messwert = Messwert.Von(wert, "StartMode/State", "≠ Disabled"),
                    Quelle = "Win32_Service.StartMode (" + h[0] + ")",
                    Titel = "Ein Hilfsdienst von Windows Update ist abgeschaltet",
                    Satz = "Der Dienst „" + h[1] + "“ ist deaktiviert; " + h[2],
                    Rat = "Stellen Sie den Dienst wieder auf seine Standard-Startart; ein deaktivierter Hilfsdienst ist fast nie Absicht.",
                };
                DiensteDetail(u, b);
                b.Massnahmen.Add("update.dienst.aktivieren");
                e.Befunde.Add(b);
            }
        }

        static void DiensteDetail(WindowsUpdate u, Befund b)
        {
            foreach (var kv in u.Dienste.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) b.Detail.Add(kv.Key + ": " + kv.Value);
        }

        static void Verwaltet(WindowsUpdate u, BereichErgebnis e)
        {
            if (u.Verwaltet != true) return;
            var b = new Befund
            {
                Bereich = Bereich.Updates,
                Schluessel = "update.verwaltet",
                Zustand = Zustand.Ok,
                Messwert = Messwert.Von(true, "IsManaged", null),
                Quelle = "IUpdateService.IsManaged (Standard-AU-Dienst) / Policy UseWUServer",
                Titel = "Updates kommen von einem Firmenserver",
                Satz = "Die Updates dieses PCs steuert ein Firmen-Update-Server (z. B. WSUS); was installiert wird, entscheidet dessen Administrator, nicht Windows Update im Internet.",
                Rat = null,
            };
            b.Detail.Add("Fehlende oder späte Updates sind hier oft Freigabe-Entscheidungen des Servers, kein Defekt dieses PCs.");
            e.Befunde.Add(b);
        }

        // ---------------------------------------------------------------- Helfer

        static Fall Holen(Dictionary<string, Fall> faelle, string updateId)
        {
            string id = Norm(updateId) ?? "?";
            Fall f;
            if (!faelle.TryGetValue(id, out f)) { f = new Fall { Id = id }; faelle[id] = f; }
            return f;
        }

        /// <summary>UpdateID vergleichbar machen: klein, ohne Klammern und Leerraum.</summary>
        public static string Norm(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return id.Trim().Trim('{', '}').ToLowerInvariant();
        }

        static string Anzeige(Fall f)
        {
            return string.IsNullOrWhiteSpace(f.Titel) ? "Update " + f.Id : f.Titel.Trim();
        }

        static string Hex(long code)
        {
            return (code & 0xFFFFFFFFL).ToString("X8", CultureInfo.InvariantCulture);
        }

        static string DienstWert(WindowsUpdate u, string name)
        {
            foreach (var kv in u.Dienste)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        /// <summary>"Disabled/Stopped" ist abgeschaltet; "Unknown" (nicht erhoeht bei manchen Diensten gemessen) ist nicht bewertbar.</summary>
        static bool Deaktiviert(string wert)
        {
            if (string.IsNullOrEmpty(wert)) return false;
            string start = wert.Split('/')[0].Trim();
            return string.Equals(start, "Disabled", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Die 15 haeufigsten Windows-Update-Fehlercodes mit Deutung. Quelle: learn.microsoft.com
    /// "Windows Update error codes by component" (WU_E_*) und winerror.h (0x8007xxxx =
    /// Win32-Fehler). Nur was hier steht, wird gedeutet; alles andere bleibt "keine Deutung".
    /// </summary>
    public static class Fehlercodes
    {
        static readonly Dictionary<long, string> Tabelle = new Dictionary<long, string>
        {
            { 0x80070020, "Eine Datei ist durch ein anderes Programm gesperrt (ERROR_SHARING_VIOLATION); oft Virenschutz, Sicherung oder ein laufender Installer" },
            { 0x80070422, "Der Windows-Update-Dienst ist deaktiviert (ERROR_SERVICE_DISABLED)" },
            { 0x8024402C, "Der Name des Update-Servers oder Proxys ließ sich nicht auflösen (WU_E_PT_WINHTTP_NAME_NOT_RESOLVED); Namensauflösung, DNS oder Proxy prüfen" },
            { 0x8024001E, "Der Vorgang brach ab, weil Dienst oder System heruntergefahren wurden (WU_E_SERVICE_STOP)" },
            { 0x80240034, "Der Download des Updates ist fehlgeschlagen (WU_E_DOWNLOAD_FAILED)" },
            { 0x80244022, "Der Update-Server meldete HTTP 503, er ist vorübergehend überlastet (WU_E_PT_HTTP_STATUS_SERVICE_UNAVAIL)" },
            { 0x8024000B, "Der Vorgang wurde abgebrochen (WU_E_CALL_CANCELLED)" },
            { 0x80244007, "Der Update-Server antwortete mit einem SOAP-Fehler (WU_E_PT_SOAPCLIENT_SOAPFAULT); Protokollfehler zwischen Client und Server" },
            { 0x80240022, "Kein einziges der angeforderten Updates ließ sich verarbeiten (WU_E_ALL_UPDATES_FAILED)" },
            { 0x80244019, "Der Update-Server meldete HTTP 404, die angeforderte Adresse fehlt (WU_E_PT_HTTP_STATUS_NOT_FOUND)" },
            { 0x80246007, "Das Update wurde nicht heruntergeladen (WU_E_DM_NOTDOWNLOADED)" },
            { 0x80240016, "Installation nicht erlaubt, weil gerade eine andere Installation läuft oder ein Neustart aussteht (WU_E_INSTALL_NOT_ALLOWED)" },
            { 0x80240017, "Das Update ist für dieses System nicht anwendbar (WU_E_NOT_APPLICABLE)" },
            { 0x8024200D, "Der Installer braucht das Update noch einmal; der Download muss wiederholt werden (WU_E_UH_NEEDANOTHERDOWNLOAD)" },
            { 0x80248007, "Im Datenspeicher von Windows Update fehlen die Angaben zum Update (WU_E_DS_NODATA)" },
        };

        public static int Anzahl { get { return Tabelle.Count; } }

        /// <summary>Deutung oder null. Vorzeichenbehaftete und vorzeichenlose Schreibweise sind gleich.</summary>
        public static string Deutung(long code)
        {
            string d;
            return Tabelle.TryGetValue(code & 0xFFFFFFFFL, out d) ? d : null;
        }
    }
}
