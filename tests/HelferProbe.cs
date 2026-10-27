using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using WartungsToolbox.Kern;

namespace WartungsToolbox
{
    /// <summary>
    /// Probe fuer den Helfer und die Laufzeitablage (docs/M2-ENTWURF.md, Abschnitt 8, Punkte 7 und 9).
    /// Laeuft ohne Rechte, ohne Pipe, ohne ProgramData: alles in einem Wegwerf-Ordner unter %TEMP%.
    ///
    /// Was hier belegt wird:
    ///   (a) Pipe.PipeSicherheit(sid): genau zwei Regeln (die SID und SYSTEM, beide Vollzugriff),
    ///       keine fuer "Jeder", "Authentifizierte Benutzer" oder "Benutzer". Ein Fehler hier
    ///       hiesse: jedes Programm auf dem Rechner koennte dem erhoehten Helfer Auftraege geben.
    ///   (b) Ablage.RechteSichern(ordner): BUILTIN\Users bekommt Aendern, vererbt auf Unterordner
    ///       und Dateien. Ohne das gehoeren die Dateien des erhoehten Helfers der Administratoren-
    ///       gruppe, und die Oberflaeche ohne Rechte kann den Verlauf nicht mehr fortschreiben.
    ///   (c) Ablage.Uebernehmen(alt, neu) kopiert NUR, wenn am neuen Ort nichts liegt.
    ///   (d) AppLog.UebernahmeAbschliessen(alt) benennt die alte Datei auf *.uebernommen um -
    ///       erst damit ist die Uebernahme einmalig (sonst kaeme nach "Verlauf leeren" der alte
    ///       Stand beim naechsten Start zurueck).
    ///   (e) Protokoll.LaufIdGueltig und der Konstruktor: eine Lauf-Id wird Dateiname; eine Id
    ///       mit Pfadzeichen darf den erhoehten Helfer nie an eine fremde Stelle schreiben lassen.
    ///   (f) Sicherungsdatei, Herkunft (docs/M3-ENTWURF.md 8.2, 8.10, 10.1 Punkt 3): der Rueckweg
    ///       schreibt die Werte aus der Datei erhoeht zurueck (Firewall aus, EnableLUA 0), also darf
    ///       nur eine Datei gelten, die der erhoehte Helfer selbst geschrieben hat. Erhoeht (CI):
    ///       Schreiben legt die Datei mit Besitzer Administratoren, Vererbung aus und genau drei
    ///       Eintraegen an, Lesen nimmt sie; ein anderer Besitzer, ein vierter Eintrag oder ein
    ///       zweiter Name (Hardlink) wird abgelehnt. Nicht erhoeht: Schreiben wirft, und eine Datei
    ///       des Nutzers wird abgelehnt. Namen mit Pfadzeichen, Leerzeichen oder falscher Endung
    ///       enden in ArgumentException; der Trockenlauf schreibt nichts.
    ///   (g) Rueckweg-Inhalt (10.1 Punkt 4): RueckwegMassnahmen.InhaltPruefen und die beiden
    ///       Bereichspruefungen lehnen untergeschobene Werte ab (Dienst aus NieDeaktivieren auf
    ///       Disabled, FirewallEnabled 2, SmartScreen "Off;calc", EnableLUA 7, DNS "8.8.8.8;evil",
    ///       Instanz-Id mit Apostroph, update.dienst.aktivieren bei jedem Vorwert) und nehmen die
    ///       gueltigen Gegenstuecke an.
    ///   (h) Kennungen zur Laufzeit (10.1 Punkt 1): MassnahmenM3.Alle() wird aufgerufen, und jede
    ///       Kennung geht als Zeile "kennung: ..." nach stdout; die Suite haelt diese Menge gegen ihre
    ///       feste Liste. Ein Grep ueber Massnahmenbau.Neu("...") sieht eine Massnahme nicht, die
    ///       wie wartung.massnahmen per Objektinitialisierer angemeldet wird (Gegenlesen 18.09.2026:
    ///       eine so eingebaute Kennung blieb der Suite verborgen); die Laufzeitmenge ist genau das,
    ///       was der Helfer kennt.
    ///   (i) Export-Herkunft (docs/M4-ENTWURF.md 3.2, 8.2 Punkt 4): eine Datei oder ein Ordner, den
    ///       netsh oder pnputil unter sicherungen\massnahmen\export angelegt hat, gilt erst nach
    ///       Sicherungsdatei.ExportSchuetzen (Besitzer Administratoren, genau die geschuetzte DACL);
    ///       ExportOeffnen prueft wie Lesen am Handle (Besitzer, DACL, genau ein Name, kein
    ///       Reparse-Punkt, Name ohne Pfad, verlangte Endung). Erhoeht (CI) wird das an echten
    ///       Dateien gemessen (frisch angelegt = abgelehnt, geschuetzt = angenommen, fremder Besitzer,
    ///       vierter Eintrag, Hardlink, Symlink = abgelehnt); nicht erhoeht daran, dass ExportOrdner
    ///       wirft. Dazu die Firewall-Sicherung: export nur nach Muster und nur mit vorhandener,
    ///       geschuetzter Datei (ZurueckSicherheitPruefen), sonst abgelehnt.
    ///   (j) Rueckweg-Inhalt M4 (8.2 Punkt 4 und Punkt 2): eine Startprogramm-Sicherung unter HKCU
    ///       mit fremder oder fehlender profilSid wird abgelehnt, mit der eigenen angenommen; die
    ///       hosts-Sicherung nur mit druckbarem Inhalt bis 256 KB (NUL, Steuerzeichen, zu lang =
    ///       abgelehnt); die IP-Sicherung nur mit GUID, IPv4-Adressen, Masken, Gateways und DNS je
    ///       Zeile (IP=evil, 5 Felder, doppelte SettingID = abgelehnt); Firewall eingehend nur mit
    ///       DefaultInboundAction 0|1 und Profil 1|2|4; Winlogon nur mit beiden Vorwerten ohne
    ///       Steuerzeichen; Winsock, Update-Reset und Treiber-Neuinstallation haben keinen Rueckweg.
    ///
    /// Uebersetzt wird mit allem ausser host\ (Pipe.cs zieht Messung und Ausfuehrung nach sich),
    /// siehe tests\run-tests.ps1. Ausgabe [ok]/[FEHL] je Teilpruefung, Exit 1 bei mindestens einem Rot.
    /// </summary>
    static class HelferProbe
    {
        static int fehler = 0;
        static int geprueft = 0;

        // Die Gruppen (a) bis (j) mit ihrem Namen auf der Kommandozeile. Ohne Argument laufen alle
        // (Probe 7 der Suite); mit Namen nur diese, damit die Suite Sicherungsdatei, Rueckweg-Inhalt,
        // Kennungen, Export-Herkunft und den M4-Rueckweg-Inhalt in eigenen Pruefungen zaehlen kann
        // (docs/M3-ENTWURF.md 10.1, Punkte 1, 3 und 4; docs/M4-ENTWURF.md 8.2, Punkt 4).
        static readonly string[] Gruppen = { "pipe", "rechte", "uebernahme", "abschluss", "laufid", "sicherung", "rueckweg", "kennungen", "export", "m4rueckweg" };

        static void Ist(string was, bool bedingung, string detail)
        {
            Console.WriteLine((bedingung ? "         [ok]   " : "         [FEHL] ") + was +
                              (detail.Length > 0 ? "   " + detail : ""));
            geprueft++;
            if (!bedingung) fehler++;
        }

        static void Main(string[] args)
        {
            var nur = new HashSet<string>(args ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (string g in nur)
                if (Array.IndexOf(Gruppen, g.ToLowerInvariant()) < 0)
                    Ist("Gruppe \"" + g + "\" gibt es nicht (bekannt: " + string.Join(", ", Gruppen) + ")", false, "");
            Func<string, bool> laeuft = g => nur.Count == 0 || nur.Contains(g);

            string ordner = Path.Combine(Path.GetTempPath(),
                "WW-HelferProbe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(ordner);
            // Das App-Protokoll bleibt im Wegwerf-Ordner: die Probe uebersetzt src\AppLog.cs mit,
            // und ein AppLog-Aufruf aus einer der geprueften Klassen legte sonst
            // %ProgramData%\WindowsWartung\logs an und zoege eine 8.0-Datei aus dem Nutzerprofil nach.
            AppLog.PfadFuerProbe = Path.Combine(ordner, "app.log");

            try
            {
                if (laeuft("pipe")) PruefePipeSicherheit();
                if (laeuft("rechte")) PruefeRechteSichern(ordner);
                if (laeuft("uebernahme")) PruefeUebernehmen(ordner);
                if (laeuft("abschluss")) PruefeUebernahmeAbschliessen(ordner);
                if (laeuft("laufid")) PruefeLaufId(ordner);
                if (laeuft("sicherung")) PruefeSicherungsdatei(ordner);
                if (laeuft("rueckweg")) PruefeRueckwegInhalt();
                if (laeuft("kennungen")) PruefeKennungen(nur.Contains("kennungen"));
                if (laeuft("export")) PruefeExportHerkunft(ordner);
                if (laeuft("m4rueckweg")) PruefeRueckwegInhaltM4(ordner);
            }
            catch (Exception ex)
            {
                Ist("Probe laeuft ohne Ausnahme", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Protokoll.OrdnerFuerProbe = null;
                Helfer.Sicherungsdatei.OrdnerFuerProbe = null;
                AppLog.PfadFuerProbe = null;
                // Der Sicherungsordner der Probe traegt nach (f) erhoeht die geschuetzte DACL (Benutzer nur
                // Lesen); Administratoren duerfen ihn trotzdem loeschen. Nicht erhoeht blieb er, wie er war.
                try { Directory.Delete(ordner, true); } catch { }
            }

            // Eine leere Antwort ist kein bestandener Lauf: ohne eine einzige Teilpruefung Exit 1.
            if (geprueft == 0) Ist("mindestens eine Teilpruefung ist gelaufen", false, "keine");
            Environment.Exit(fehler == 0 ? 0 : 1);
        }

        // ---- (a) Sicherheitsbeschreibung der Pipe ----------------------------------------

        static void PruefePipeSicherheit()
        {
            // Eine Konto-SID, die sicher nicht SYSTEM ist (sonst fielen beide Regeln zusammen).
            string eigene;
            using (WindowsIdentity wi = WindowsIdentity.GetCurrent()) eigene = wi.User.Value;
            string[] kandidaten = { eigene, "S-1-5-21-1111111111-2222222222-3333333333-1001" };

            foreach (string sid in kandidaten)
            {
                PipeSecurity ps = Helfer.Pipe.PipeSicherheit(sid);
                AuthorizationRuleCollection regeln = ps.GetAccessRules(true, true, typeof(SecurityIdentifier));
                var namen = new List<string>();
                bool alleVoll = true, alleErlauben = true;
                foreach (PipeAccessRule r in regeln)
                {
                    namen.Add(r.IdentityReference.Value);
                    if ((r.PipeAccessRights & PipeAccessRights.FullControl) != PipeAccessRights.FullControl) alleVoll = false;
                    if (r.AccessControlType != AccessControlType.Allow) alleErlauben = false;
                }
                string liste = string.Join(", ", namen.ToArray());
                string system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;

                Ist("PipeSicherheit(" + Kurz(sid) + "): genau zwei Regeln", regeln.Count == 2, regeln.Count + " Regeln: " + liste);
                Ist("PipeSicherheit(" + Kurz(sid) + "): die SID selbst hat Vollzugriff",
                    namen.Contains(sid) && alleVoll && alleErlauben, liste);
                Ist("PipeSicherheit(" + Kurz(sid) + "): SYSTEM hat Vollzugriff", namen.Contains(system), liste);

                foreach (WellKnownSidType fremd in new[] { WellKnownSidType.WorldSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.BuiltinUsersSid })
                {
                    string f = new SecurityIdentifier(fremd, null).Value;
                    Ist("PipeSicherheit(" + Kurz(sid) + "): keine Regel fuer " + fremd, !namen.Contains(f), liste);
                }
            }

            bool geworfen = false;
            try { Helfer.Pipe.PipeSicherheit(""); } catch (ArgumentException) { geworfen = true; }
            Ist("PipeSicherheit ohne SID wirft (kein stiller Rueckfall auf Jeder)", geworfen, "");
        }

        // ---- (b) Rechte auf dem Laufzeitordner ---------------------------------------------

        static void PruefeRechteSichern(string wurzel)
        {
            string ordner = Path.Combine(wurzel, "rechte");
            Directory.CreateDirectory(ordner);

            bool erster = Ablage.RechteSichern(ordner);
            Ist("RechteSichern auf einem frischen Ordner liefert true", erster, ordner);

            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            bool gefunden = false;
            string gesehen = "";
            // Nur die ausdruecklichen Regeln (nicht geerbte): die Vererbung vom Temp-Ordner soll
            // die Probe nicht schoenrechnen.
            foreach (FileSystemAccessRule r in new DirectoryInfo(ordner).GetAccessControl()
                         .GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                gesehen += r.IdentityReference.Value + ":" + r.FileSystemRights + "/" + r.InheritanceFlags + "; ";
                if (r.IdentityReference != users) continue;
                if (r.AccessControlType != AccessControlType.Allow) continue;
                if ((r.FileSystemRights & FileSystemRights.Modify) != FileSystemRights.Modify) continue;
                if ((r.InheritanceFlags & InheritanceFlags.ContainerInherit) == 0) continue;
                if ((r.InheritanceFlags & InheritanceFlags.ObjectInherit) == 0) continue;
                gefunden = true;
            }
            Ist("danach hat BUILTIN\\Users Aendern, vererbt auf Ordner und Dateien (CI+OI)", gefunden, gesehen);

            // Ein zweiter Aufruf findet die Regel vor und bleibt true, ohne sie doppelt anzulegen.
            bool zweiter = Ablage.RechteSichern(ordner);
            int anzahl = 0;
            foreach (FileSystemAccessRule r in new DirectoryInfo(ordner).GetAccessControl()
                         .GetAccessRules(true, false, typeof(SecurityIdentifier)))
                if (r.IdentityReference == users) anzahl++;
            Ist("zweiter Aufruf liefert true und legt die Regel nicht doppelt an", zweiter && anzahl == 1, anzahl + " Regel(n) fuer Users");

            Ist("RechteSichern auf einem fehlenden Ordner liefert false, ohne zu werfen",
                !Ablage.RechteSichern(Path.Combine(wurzel, "gibt-es-nicht")), "");
        }

        // ---- (c) Uebernahme kopiert nur, wenn neu fehlt ---------------------------------

        static void PruefeUebernehmen(string wurzel)
        {
            string altOrdner = Path.Combine(wurzel, "alt");
            string neuOrdner = Path.Combine(wurzel, "neu", "tiefer");   // neu/tiefer gibt es noch nicht: wird angelegt
            Directory.CreateDirectory(altOrdner);
            string alt = Path.Combine(altOrdner, "history.json");
            string neu = Path.Combine(neuOrdner, "history.json");
            File.WriteAllText(alt, "[{\"a\":1}]", new UTF8Encoding(false));

            bool kopiert = Ablage.Uebernehmen(alt, neu);
            Ist("Uebernehmen kopiert, wenn die neue Datei fehlt", kopiert && File.Exists(neu), "");
            Ist("die Kopie hat denselben Inhalt", File.Exists(neu) && File.ReadAllText(neu) == "[{\"a\":1}]", "");
            Ist("die alte Datei bleibt dabei liegen (Umbenennen macht UebernahmeAbschliessen)", File.Exists(alt), "");

            File.WriteAllText(neu, "[{\"b\":2}]", new UTF8Encoding(false));
            bool nochmal = Ablage.Uebernehmen(alt, neu);
            Ist("Uebernehmen kopiert NICHT, wenn die neue Datei schon da ist", !nochmal, "");
            Ist("die vorhandene neue Datei bleibt unangetastet", File.ReadAllText(neu) == "[{\"b\":2}]", "");

            string neu2 = Path.Combine(neuOrdner, "zeitplan.json");
            bool ohneAlt = Ablage.Uebernehmen(Path.Combine(altOrdner, "fehlt.json"), neu2);
            Ist("Uebernehmen ohne alte Datei liefert false und legt nichts an", !ohneAlt && !File.Exists(neu2), "");

            Ist("Uebernehmen mit leeren Pfaden liefert false, ohne zu werfen",
                !Ablage.Uebernehmen(null, neu2) && !Ablage.Uebernehmen(alt, ""), "");
        }

        // ---- (d) Uebernahme abschliessen: alt -> alt.uebernommen -------------------------

        static void PruefeUebernahmeAbschliessen(string wurzel)
        {
            string ordner = Path.Combine(wurzel, "abschluss");
            Directory.CreateDirectory(ordner);
            string alt = Path.Combine(ordner, "app.log");
            File.WriteAllText(alt, "zeile 1\n", new UTF8Encoding(false));

            bool ok = AppLog.UebernahmeAbschliessen(alt);
            Ist("UebernahmeAbschliessen liefert true", ok, "");
            Ist("die alte Datei ist danach weg", !File.Exists(alt), "");
            Ist("sie heisst jetzt *.uebernommen", File.Exists(alt + ".uebernommen"), "");

            // Eine liegen gebliebene *.uebernommen aus einem frueheren Lauf wird ersetzt.
            File.WriteAllText(alt, "zeile 2\n", new UTF8Encoding(false));
            bool ersetzt = AppLog.UebernahmeAbschliessen(alt);
            Ist("eine vorhandene *.uebernommen wird ersetzt",
                ersetzt && !File.Exists(alt) && File.ReadAllText(alt + ".uebernommen") == "zeile 2\n", "");

            Ist("UebernahmeAbschliessen ohne Datei liefert false, ohne zu werfen",
                !AppLog.UebernahmeAbschliessen(alt) && !AppLog.UebernahmeAbschliessen(null), "");
        }

        // ---- (e) Lauf-Id als Dateiname ------------------------------------------------------

        static void PruefeLaufId(string wurzel)
        {
            Ist("LaufIdGueltig: 20260913-131335-2b55a2", Protokoll.LaufIdGueltig("20260913-131335-2b55a2"), "");
            Ist("LaufIdGueltig: probe-1", Protokoll.LaufIdGueltig("probe-1"), "");
            Ist("LaufIdGueltig: die eigene NeueLaufId", Protokoll.LaufIdGueltig(Protokoll.NeueLaufId()), "");

            string[] ungueltig = { "", "a\\b", "a/b", "x:stream", "..", ".", new string('a', 65) };
            foreach (string id in ungueltig)
                Ist("LaufIdGueltig lehnt ab: " + (id.Length > 20 ? id.Length + " Zeichen" : "\"" + id + "\""), !Protokoll.LaufIdGueltig(id), "");
            Ist("LaufIdGueltig: 64 Zeichen sind noch erlaubt", Protokoll.LaufIdGueltig(new string('a', 64)), "");
            Ist("LaufIdGueltig lehnt null ab", !Protokoll.LaufIdGueltig(null), "");

            // Der Konstruktor ersetzt eine Id mit Pfadzeichen und schreibt unter den Probenordner.
            string protokolle = Path.Combine(wurzel, "protokoll");
            Protokoll.OrdnerFuerProbe = protokolle;
            var p = new Protokoll("x\\..\\evil");
            Ist("Protokoll(\"x\\..\\evil\"): die Lauf-Id wurde ersetzt", p.LaufId != "x\\..\\evil" && Protokoll.LaufIdGueltig(p.LaufId), p.LaufId);
            string erwartet = Path.Combine(protokolle, "lauf-" + p.LaufId + ".jsonl");
            Ist("die Datei liegt unter dem Protokollordner", string.Equals(p.Pfad, erwartet, StringComparison.OrdinalIgnoreCase), p.Pfad);

            p.Schreibe(Protokoll.Helfer, Protokoll.Anfang, "probe", "Probe");
            Ist("nach Schreibe existiert genau diese Datei", File.Exists(erwartet), erwartet);
            Ist("keine Datei evil.jsonl im Protokollordner", !File.Exists(Path.Combine(protokolle, "evil.jsonl")), "");
            Ist("keine Datei evil.jsonl eine Ebene darueber", !File.Exists(Path.Combine(wurzel, "evil.jsonl")), "");
            Ist("kein Ordner lauf-x", !Directory.Exists(Path.Combine(protokolle, "lauf-x")), "");
            Ist("Protokoll.Ordner() zeigt auf die Testnaht", string.Equals(Protokoll.Ordner(), protokolle, StringComparison.OrdinalIgnoreCase), Protokoll.Ordner());

            var gut = new Protokoll("probe-1");
            Ist("eine gueltige Id bleibt erhalten", gut.LaufId == "probe-1", gut.LaufId);
        }

        // ---- (f) Sicherungsdatei: Herkunft statt Ort ---------------------------------------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        /// <summary>Lesen muss mit ArgumentException enden; liefert die Meldung ("" = nicht geworfen, sonst Typ und Text).</summary>
        static string LesenAbgelehnt(string name)
        {
            try { Helfer.Sicherungsdatei.Lesen(name); return ""; }
            catch (ArgumentException ex) { return ex.Message; }
            catch (Exception ex) { return "(" + ex.GetType().Name + ") " + ex.Message; }
        }

        /// <summary>Lesen muss gelingen; null = gelungen mit der erwarteten Kennung, sonst der Grund.</summary>
        static string LesenGelingt(string name)
        {
            try
            {
                Helfer.Sicherung s = Helfer.Sicherungsdatei.Lesen(name);
                return s != null && s.Kennung == "probe.kennung" && s.Wert("x") == "1" ? null : "Inhalt weicht ab";
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        static void PruefeSicherungsdatei(string wurzel)
        {
            // Der Ordner heisst wie im Programm (sicherungen\massnahmen unter einer Wurzel), damit
            // ErwarteterPfad denselben Elternordner sieht; angelegt wird er hier plain, mit den
            // geerbten Rechten des Temp-Ordners - genau der Zustand, den Schreiben vorfindet, wenn
            // ein Benutzer den Ordner vor der ersten Stufe-2-Massnahme angelegt hat.
            string ordner = Path.Combine(wurzel, "sicherungen", "massnahmen");
            Directory.CreateDirectory(ordner);
            Helfer.Sicherungsdatei.OrdnerFuerProbe = ordner;
            bool erhoeht = Sammler.Quellen.Rechte.Erhoeht();
            string eigene, tokenBesitzer;
            using (WindowsIdentity wi = WindowsIdentity.GetCurrent()) { eigene = wi.User.Value; tokenBesitzer = wi.Owner == null ? "?" : wi.Owner.Value; }
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            // Der Standardbesitzer des Tokens (TokenOwner) wird Besitzer jeder neuen Datei, die ohne
            // ausdruecklichen Besitzer angelegt wird; ein UAC-erhoehter Token traegt Administratoren.
            // Gemessen 18.09.2026: eine erhoehte Shell aus Git-Bash trug das Konto selbst, und
            // Schreiben verwarf dort jede Datei ("Besitzer ... statt Administratoren"). Die Zeile
            // nennt ihn, damit ein Rot in dieser Gruppe sofort einzuordnen ist.
            Console.WriteLine("         Sicherungsdatei-Probe laeuft " + (erhoeht ? "erhoeht" : "nicht erhoeht") + " (Standardbesitzer des Tokens " + tokenBesitzer + "): " + ordner);

            // Eine Datei, die nicht der Helfer geschrieben hat: gueltiger Name, gueltiger Inhalt,
            // aber Besitzer und Rechte des Aufrufers (nicht erhoeht: Besitzer das Konto; erhoeht:
            // Besitzer Administratoren, aber geerbte statt der geschuetzten Rechte). Beides ist fremd.
            string fremdName = "20260918-000000-probe.kennung-frmd.json";
            var fremd = new Helfer.Sicherung { Kennung = "probe.kennung", Zeit = "2026-09-18T00:00:00Z" };
            fremd.Vorher["x"] = "1";
            File.WriteAllText(Path.Combine(ordner, fremdName), Json.Schreiben(fremd, true), new UTF8Encoding(false));
            string grund = LesenAbgelehnt(fremdName);
            Ist("Lesen lehnt eine Datei ab, die nicht der Helfer geschrieben hat (Besitzer/Rechte)", grund.Length > 0 && grund.Contains("stammt nicht vom Helfer"), grund);

            // Ein zweiter Name auf diese fremde Datei: beide Namen bleiben abgelehnt (nicht erhoeht
            // schon am Besitzer, erhoeht an den Rechten oder an der Zahl der Namen).
            string fremdLink = "20260918-000000-probe.kennung-frml.json";
            bool linkOk = CreateHardLinkW(Path.Combine(ordner, fremdLink), Path.Combine(ordner, fremdName), IntPtr.Zero);
            Ist("Hardlink auf die fremde Datei liess sich anlegen (sonst misst die naechste Zeile nichts)", linkOk, "Win32-Fehler " + Marshal.GetLastWin32Error());
            if (linkOk)
            {
                Ist("beide Namen der fremden Datei werden abgelehnt",
                    LesenAbgelehnt(fremdName).Length > 0 && LesenAbgelehnt(fremdLink).Length > 0, "");
                File.Delete(Path.Combine(ordner, fremdLink));
            }

            // Namen, die keine Sicherungsdatei sind: Pfadzeichen, Elternverweis, Leerzeichen, falsche
            // Endung, leer. Jeder endet in ArgumentException, bevor irgendetwas geoeffnet wird.
            foreach (string name in new[] { "a\\b.json", "..\\x.json", "a b.json", "x.txt", "", "C:\\Windows\\win.ini" })
            {
                string g = LesenAbgelehnt(name);
                Ist("Lesen lehnt den Namen \"" + name + "\" mit ArgumentException ab", g.Length > 0 && !g.StartsWith("("), g);
            }
            bool fehlt = false;
            try { Helfer.Sicherungsdatei.Lesen("gibt-es-nicht-0123.json"); } catch (FileNotFoundException) { fehlt = true; } catch (Exception) { }
            Ist("Lesen einer fehlenden Datei wirft FileNotFoundException", fehlt, "");

            // Der Trockenlauf schreibt keine Datei und liefert null (Plan A der Suite zaehlt zusaetzlich
            // den echten Ordner vorher und nachher).
            int vorher = Directory.GetFiles(ordner).Length;
            var trocken = new Helfer.Ausfuehrungskontext { Trocken = true };
            string trockenPfad = Helfer.Sicherungsdatei.Schreiben(trocken, "probe.kennung", null, fremd.Vorher);
            Ist("Schreiben im Trockenlauf liefert null und legt keine Datei an",
                trockenPfad == null && Directory.GetFiles(ordner).Length == vorher, Directory.GetFiles(ordner).Length + " Dateien");

            // Eine Kennung, die kein Dateiname sein darf, wird VOR dem Trocken-Zweig abgelehnt.
            bool kennungGeworfen = false;
            try { Helfer.Sicherungsdatei.Schreiben(trocken, "Probe Kennung", null, null); } catch (ArgumentException) { kennungGeworfen = true; }
            Ist("Schreiben mit einer Kennung ausserhalb von [a-z0-9.] wirft ArgumentException", kennungGeworfen, "");

            // Jetzt der echte Schreibweg. Erhoeht: die Datei entsteht mit Besitzer Administratoren und
            // der geschuetzten DACL. Nicht erhoeht: der Ordner gehoert dem Konto, SeTakeOwnershipPrivilege
            // fehlt, Schreiben wirft InvalidOperationException; kaeme dennoch eine Datei zustande,
            // gehoerte sie dem Nutzer, und Lesen muesste sie ablehnen (beides gilt, Vertrag 10.1 Punkt 3).
            string pfad = null;
            string schreibFehler = null;
            try { pfad = Helfer.Sicherungsdatei.Schreiben(null, "probe.kennung", null, fremd.Vorher); }
            catch (InvalidOperationException ex) { schreibFehler = ex.Message; }
            catch (Exception ex) { schreibFehler = "(" + ex.GetType().Name + ") " + ex.Message; }

            if (!erhoeht)
            {
                if (pfad == null)
                    Ist("nicht erhoeht: Schreiben wirft InvalidOperationException (nichts gesichert)",
                        schreibFehler != null && !schreibFehler.StartsWith("("), schreibFehler ?? "");
                else
                    Ist("nicht erhoeht: die geschriebene Datei gehoert dem Nutzer, Lesen lehnt sie ab",
                        LesenAbgelehnt(Path.GetFileName(pfad)).Length > 0, pfad);
                return;
            }

            // ---- ab hier nur erhoeht (CI-Runner, erhoehte Werkzeug-Shell) ----
            string hinweis = schreibFehler ?? pfad ?? "";
            if (pfad == null && tokenBesitzer != admins.Value && tokenBesitzer != system.Value)
                hinweis += " [der Token traegt " + tokenBesitzer + " als Standardbesitzer; Sicherungsdatei.DateiSchreiben setzt den Besitzer nicht selbst]";
            Ist("erhoeht: Schreiben liefert einen Pfad im Probenordner", pfad != null && File.Exists(pfad) && string.Equals(Path.GetDirectoryName(pfad), ordner, StringComparison.OrdinalIgnoreCase), hinweis);
            if (pfad == null) return;
            string name1 = Path.GetFileName(pfad);

            FileSecurity sec = File.GetAccessControl(pfad, AccessControlSections.Owner | AccessControlSections.Access);
            var besitzer = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            int eintraege = 0;
            string gesehen = "";
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                eintraege++;
                gesehen += r.IdentityReference.Value + ":" + r.FileSystemRights + (r.IsInherited ? " geerbt" : "") + "; ";
            }
            Ist("die Datei gehoert Administratoren oder SYSTEM", besitzer != null && (besitzer == admins || besitzer == system), besitzer == null ? "kein Besitzer" : besitzer.Value);
            Ist("Vererbung aus, genau drei Eintraege (SYSTEM, Administratoren, Benutzer)", sec.AreAccessRulesProtected && eintraege == 3, gesehen);
            Ist("Lesen nimmt die Datei des Helfers an", LesenGelingt(name1) == null, LesenGelingt(name1) ?? "");

            // Besitzer auf das eigene Konto: dieselbe Datei, dieselben Rechte, anderer Besitzer - fremd.
            FileSecurity wechsel = File.GetAccessControl(pfad, AccessControlSections.Owner);
            wechsel.SetOwner(new SecurityIdentifier(eigene));
            File.SetAccessControl(pfad, wechsel);
            grund = LesenAbgelehnt(name1);
            Ist("Besitzer auf das eigene Konto gesetzt: Lesen lehnt ab (stammt nicht vom Helfer)", grund.Contains("stammt nicht vom Helfer"), grund);
            wechsel = File.GetAccessControl(pfad, AccessControlSections.Owner);
            wechsel.SetOwner(admins);
            File.SetAccessControl(pfad, wechsel);
            Ist("Besitzer wieder Administratoren: Lesen nimmt die Datei wieder an", LesenGelingt(name1) == null, LesenGelingt(name1) ?? "");

            // Ein vierter Eintrag (Lesen fuer das eigene Konto): nicht mehr genau die geschuetzte DACL.
            var zusatz = new FileSystemAccessRule(new SecurityIdentifier(eigene), FileSystemRights.Read, AccessControlType.Allow);
            FileSecurity dacl = File.GetAccessControl(pfad, AccessControlSections.Access);
            dacl.AddAccessRule(zusatz);
            File.SetAccessControl(pfad, dacl);
            grund = LesenAbgelehnt(name1);
            Ist("ein zusaetzlicher Eintrag fuer das eigene Konto: Lesen lehnt ab", grund.Contains("stammt nicht vom Helfer"), grund);
            dacl = File.GetAccessControl(pfad, AccessControlSections.Access);
            dacl.RemoveAccessRule(zusatz);
            File.SetAccessControl(pfad, dacl);
            Ist("Eintrag wieder entfernt: Lesen nimmt die Datei wieder an", LesenGelingt(name1) == null, LesenGelingt(name1) ?? "");

            // Ein zweiter Name auf die Datei des Helfers: Besitzer und Rechte stimmen an beiden Namen,
            // nur die Zahl der Namen am Handle verraet es. Beide Namen werden abgelehnt.
            string linkName = "20260918-000000-probe.kennung-link.json";
            string linkPfad = Path.Combine(ordner, linkName);
            bool link2 = CreateHardLinkW(linkPfad, pfad, IntPtr.Zero);
            Ist("Hardlink auf die Datei des Helfers liess sich anlegen", link2, "Win32-Fehler " + Marshal.GetLastWin32Error());
            if (link2)
            {
                string g1 = LesenAbgelehnt(name1), g2 = LesenAbgelehnt(linkName);
                Ist("Hardlink: der Originalname wird abgelehnt (mehr als ein Name)", g1.Contains("mehr als einen Namen"), g1);
                Ist("Hardlink: der zweite Name wird abgelehnt", g2.Contains("mehr als einen Namen"), g2);
                File.Delete(linkPfad);
                Ist("Hardlink entfernt: Lesen nimmt die Datei wieder an", LesenGelingt(name1) == null, LesenGelingt(name1) ?? "");
            }

            // Ein zweites Schreiben ueberschreibt nie: neuer Name, beide Dateien bleiben.
            string pfad2 = Helfer.Sicherungsdatei.Schreiben(null, "probe.kennung", null, fremd.Vorher);
            Ist("ein zweites Schreiben bekommt einen eigenen Namen, die erste Datei bleibt",
                pfad2 != null && !string.Equals(pfad2, pfad, StringComparison.OrdinalIgnoreCase) && File.Exists(pfad) && File.Exists(pfad2), (pfad2 ?? "") + " neben " + pfad);

            // Der Ordner selbst: Besitzer Administratoren, Vererbung aus, Benutzer nur Lesen.
            DirectorySecurity ds = new DirectoryInfo(ordner).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            var ordnerBesitzer = ds.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            bool benutzerSchreiben = false;
            var benutzer = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            foreach (FileSystemAccessRule r in ds.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (r.IdentityReference == benutzer && r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & FileSystemRights.WriteData) != 0) benutzerSchreiben = true;
            Ist("der Sicherungsordner gehoert Administratoren oder SYSTEM, Vererbung aus, Benutzer ohne Schreibrecht",
                ordnerBesitzer != null && (ordnerBesitzer == admins || ordnerBesitzer == system) && ds.AreAccessRulesProtected && !benutzerSchreiben,
                (ordnerBesitzer == null ? "kein Besitzer" : ordnerBesitzer.Value) + ", geschuetzt=" + ds.AreAccessRulesProtected + ", BenutzerSchreiben=" + benutzerSchreiben);
        }

        // ---- (g) Rueckweg-Inhalt: untergeschobene Werte -------------------------------------

        static Helfer.Sicherung SicherungAus(string kennung, string[] parameter, string[] vorher)
        {
            var s = new Helfer.Sicherung { Kennung = kennung, Zeit = "2026-09-18T00:00:00Z" };
            for (int i = 0; parameter != null && i + 1 < parameter.Length; i += 2) s.Parameter[parameter[i]] = parameter[i + 1];
            for (int i = 0; vorher != null && i + 1 < vorher.Length; i += 2) s.Vorher[vorher[i]] = vorher[i + 1];
            return s;
        }

        static void Abgelehnt(string was, Helfer.Sicherung s)
        {
            string g = Helfer.RueckwegMassnahmen.InhaltPruefen(s);
            Ist("InhaltPruefen lehnt ab: " + was, !string.IsNullOrEmpty(g), g ?? "(angenommen)");
        }

        static void Angenommen(string was, Helfer.Sicherung s)
        {
            string g = Helfer.RueckwegMassnahmen.InhaltPruefen(s);
            Ist("InhaltPruefen nimmt an: " + was, g == null, g ?? "");
        }

        static void PruefeRueckwegInhalt()
        {
            // Dienste: ein Vorwert Disabled fuer einen Dienst aus NieDeaktivieren spielt der Rueckweg
            // nie zurueck (die Firewall waere sonst ueber eine untergeschobene Datei abschaltbar);
            // update.dienst.aktivieren hat gar keinen Rueckweg (Vertrag 8.10), bei jedem Vorwert.
            Abgelehnt("update.dienst.aktivieren mit vorher.startart=Disabled",
                SicherungAus("update.dienst.aktivieren", new[] { "name", "wuauserv" }, new[] { "startart", "Disabled", "zustand", "Stopped" }));
            Abgelehnt("update.dienst.aktivieren mit vorher.startart=Manual (kein Rueckweg, gleich welcher Vorwert)",
                SicherungAus("update.dienst.aktivieren", new[] { "name", "wuauserv" }, new[] { "startart", "Manual", "zustand", "Stopped" }));
            Abgelehnt("leistung.dienst.startart mpssvc mit vorher.startart=Disabled",
                SicherungAus("leistung.dienst.startart", new[] { "name", "mpssvc", "startart", "Manual" }, new[] { "startart", "Disabled", "zustand", "Running" }));
            Angenommen("leistung.dienst.startart Spooler mit vorher.startart=Automatic",
                SicherungAus("leistung.dienst.startart", new[] { "name", "Spooler", "startart", "Manual" }, new[] { "startart", "Automatic", "zustand", "Stopped" }));
            Abgelehnt("leistung.dienst.startart mit Dienstnamen a'b",
                SicherungAus("leistung.dienst.startart", new[] { "name", "a'b", "startart", "Manual" }, new[] { "startart", "Automatic" }));

            // Firewall: FirewallEnabled nur 0 oder 1, Profil nur 1, 2, 4.
            Abgelehnt("sicherheit.firewall.einschalten mit FirewallEnabled=2",
                SicherungAus("sicherheit.firewall.einschalten", new[] { "profil", "2" }, new[] { "FirewallEnabled", "2", "Profil", "2" }));
            Angenommen("sicherheit.firewall.einschalten mit FirewallEnabled=0, Profil=2",
                SicherungAus("sicherheit.firewall.einschalten", new[] { "profil", "2" }, new[] { "FirewallEnabled", "0", "Profil", "2" }));
            Abgelehnt("sicherheit.firewall.einschalten mit Profil=3",
                SicherungAus("sicherheit.firewall.einschalten", new[] { "profil", "3" }, new[] { "FirewallEnabled", "0", "Profil", "3" }));

            // SmartScreen: nur Buchstaben, hoechstens 20, leer erlaubt (= Wert loeschen).
            Abgelehnt("sicherheit.smartscreen.warn mit SmartScreenEnabled=Off;calc",
                SicherungAus("sicherheit.smartscreen.warn", null, new[] { "SmartScreenEnabled", "Off;calc" }));
            Angenommen("sicherheit.smartscreen.warn mit SmartScreenEnabled=Off",
                SicherungAus("sicherheit.smartscreen.warn", null, new[] { "SmartScreenEnabled", "Off" }));
            Angenommen("sicherheit.smartscreen.warn mit SmartScreenEnabled= (leer)",
                SicherungAus("sicherheit.smartscreen.warn", null, new[] { "SmartScreenEnabled", "" }));

            // UAC: 0 bis 5 oder leer.
            Abgelehnt("sicherheit.uac.standard mit EnableLUA=7",
                SicherungAus("sicherheit.uac.standard", null, new[] { "EnableLUA", "7", "ConsentPromptBehaviorAdmin", "5" }));
            Angenommen("sicherheit.uac.standard mit EnableLUA=0, ConsentPromptBehaviorAdmin=0",
                SicherungAus("sicherheit.uac.standard", null, new[] { "EnableLUA", "0", "ConsentPromptBehaviorAdmin", "0" }));
            Abgelehnt("sicherheit.uac.standard ohne beide Werte",
                SicherungAus("sicherheit.uac.standard", null, null));

            // DNS: nur IPv4-Adressen in der Liste, leer = DHCP; SettingID als {GUID}.
            Abgelehnt("netz.dns.zuruecksetzen mit NameServer=8.8.8.8;evil",
                SicherungAus("netz.dns.zuruecksetzen", new[] { "adapter", "9" }, new[] { "NameServer", "8.8.8.8;evil", "InterfaceIndex", "9", "SettingID", "{3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD}" }));
            Angenommen("netz.dns.zuruecksetzen mit NameServer= (leer)",
                SicherungAus("netz.dns.zuruecksetzen", new[] { "adapter", "9" }, new[] { "NameServer", "", "InterfaceIndex", "9", "SettingID", "{3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD}" }));
            Abgelehnt("netz.dns.zuruecksetzen ohne SettingID",
                SicherungAus("netz.dns.zuruecksetzen", new[] { "adapter", "9" }, new[] { "NameServer", "", "InterfaceIndex", "9" }));
            Abgelehnt("netz.dns.zuruecksetzen mit adapter=x",
                SicherungAus("netz.dns.zuruecksetzen", new[] { "adapter", "x" }, new[] { "NameServer", "", "SettingID", "{3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD}" }));

            // Geraet: die Instanz-Id nur aus der Zeichenmenge des Kerns (kein Apostroph, keine WQL-Einschleusung).
            Abgelehnt("geraet.aktivieren mit instanzId=a'b",
                SicherungAus("geraet.aktivieren", new[] { "instanzId", "a'b" }, new[] { "code", "22" }));
            Angenommen("geraet.aktivieren mit einer PCI-Instanz-Id",
                SicherungAus("geraet.aktivieren", new[] { "instanzId", "PCI\\VEN_1002&DEV_13C0&SUBSYS_88771043&REV_C1\\4&1EBE6A9C&0&0041" }, new[] { "code", "22" }));

            // Uebrige K4-Kennungen: TRIM-Rohwert, Startprogramm-Bytes, Aufgabenpfad, Update-Id.
            Angenommen("datentraeger.trim.einschalten mit DisableDeleteNotification=1",
                SicherungAus("datentraeger.trim.einschalten", null, new[] { "DisableDeleteNotification", "1" }));
            Abgelehnt("datentraeger.trim.einschalten mit DisableDeleteNotification=abc",
                SicherungAus("datentraeger.trim.einschalten", null, new[] { "DisableDeleteNotification", "abc" }));
            Abgelehnt("leistung.autostart.deaktivieren mit bytes=zz",
                SicherungAus("leistung.autostart.deaktivieren", new[] { "hive", "HKLM", "ort", "Run", "name", "a" }, new[] { "bytes", "zz" }));
            Angenommen("leistung.autostart.deaktivieren mit bytes=020000000000000000000000",
                SicherungAus("leistung.autostart.deaktivieren", new[] { "hive", "HKLM", "ort", "Run", "name", "a" }, new[] { "bytes", "020000000000000000000000" }));
            Abgelehnt("leistung.aufgabe.deaktivieren mit pfad=\\Fremd\\..\\Update",
                SicherungAus("leistung.aufgabe.deaktivieren", new[] { "pfad", "\\Fremd\\..\\Update" }, new[] { "aktiv", "1" }));
            Abgelehnt("update.verbergen mit updateId=abc",
                SicherungAus("update.verbergen", new[] { "updateId", "abc" }, new[] { "isHidden", "0" }));

            // Form und Fremdes: Schluessel mit Leerzeichen, Steuerzeichen im Wert, unbekannte Kennung, leer.
            Abgelehnt("Schluessel mit Leerzeichen unter vorher",
                SicherungAus("sicherheit.smartscreen.warn", null, new[] { "Smart Screen", "Off" }));
            Abgelehnt("Steuerzeichen im Wert",
                SicherungAus("sicherheit.smartscreen.warn", null, new[] { "SmartScreenEnabled", "Off\n" }));
            Abgelehnt("unbekannte Kennung format.c", SicherungAus("format.c", null, new[] { "x", "1" }));
            Abgelehnt("Sicherung null", (Helfer.Sicherung)null);
            Abgelehnt("Sicherung ohne Kennung", SicherungAus("", null, null));

            // Dieselben Pruefungen ueber die Bereichs-Einstiege, die Rueckweg.cs benutzt (Vertrag 8.3).
            Ist("ZurueckSicherheitPruefen lehnt EnableLUA=7 ab",
                Helfer.MassnahmenM3.ZurueckSicherheitPruefen(SicherungAus("sicherheit.uac.standard", null, new[] { "EnableLUA", "7" })) != null, "");
            Ist("ZurueckSicherheitPruefen nimmt FirewallEnabled=0, Profil=2 an",
                Helfer.MassnahmenM3.ZurueckSicherheitPruefen(SicherungAus("sicherheit.firewall.einschalten", new[] { "profil", "2" }, new[] { "FirewallEnabled", "0", "Profil", "2" })) == null, "");
            Ist("ZurueckSicherheitPruefen lehnt eine Netz-Kennung ab",
                Helfer.MassnahmenM3.ZurueckSicherheitPruefen(SicherungAus("netz.dns.zuruecksetzen", null, null)) != null, "");
            Ist("ZurueckNetzPruefen lehnt NameServer=8.8.8.8;evil ab",
                Helfer.MassnahmenM3.ZurueckNetzPruefen(SicherungAus("netz.dns.zuruecksetzen", new[] { "adapter", "9" }, new[] { "NameServer", "8.8.8.8;evil", "SettingID", "{3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD}" })) != null, "");
            Ist("ZurueckNetzPruefen nimmt eine leere Liste mit SettingID an",
                Helfer.MassnahmenM3.ZurueckNetzPruefen(SicherungAus("netz.dns.zuruecksetzen", new[] { "adapter", "9" }, new[] { "NameServer", "", "SettingID", "{3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD}" })) == null, "");
            Ist("ZurueckNetzBekannt/ZurueckSicherheitBekannt kennen genau ihre Kennungen",
                Helfer.MassnahmenM3.ZurueckNetzBekannt("netz.dns.zuruecksetzen") && !Helfer.MassnahmenM3.ZurueckNetzBekannt("netz.dnscache.leeren")
                && Helfer.MassnahmenM3.ZurueckSicherheitBekannt("sicherheit.uac.standard") && !Helfer.MassnahmenM3.ZurueckSicherheitBekannt("sicherheit.schnellscan"), "");
        }

        // ---- (h) Kennungen der M3-Massnahmen zur Laufzeit ------------------------------------

        /// <summary>
        /// Je Eintrag aus MassnahmenM3.Alle() eine Teilpruefung: Form [a-z0-9.], Pruefen und
        /// Ausfuehren gesetzt, kein zweites Vorkommen. Mit listen (Gruppe ausdruecklich verlangt)
        /// geht jede Kennung zusaetzlich als Zeile "kennung: ..." hinaus, die die Suite mit
        /// ^\s*kennung:\s*([a-z0-9.]+)$ einliest; ohne Argument (Probe 7) bleiben die Zeilen weg.
        /// Alle() meldet nur an (keine Messung, kein Eingriff); Massnahmenbau.Neu liest dabei den
        /// Kern-Katalog und schreibt bei einer Luecke eine Warnung in das App-Protokoll der Probe.
        /// </summary>
        static void PruefeKennungen(bool listen)
        {
            var alle = new List<Helfer.Massnahme>(Helfer.MassnahmenM3.Alle());
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            Ist("MassnahmenM3.Alle() liefert mindestens eine Massnahme", alle.Count > 0, alle.Count + " Eintraege");
            foreach (Helfer.Massnahme m in alle)
            {
                string k = m.Kennung ?? "";
                bool form = k.Length > 0;
                foreach (char c in k) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.')) form = false;
                bool einmalig = gesehen.Add(k);
                Ist("Kennung \"" + k + "\": Form [a-z0-9.], Pruefen und Ausfuehren gesetzt, einmalig",
                    form && m.Pruefen != null && m.Ausfuehren != null && einmalig,
                    (form ? "" : "Form; ") + (m.Pruefen == null ? "Pruefen fehlt; " : "") + (m.Ausfuehren == null ? "Ausfuehren fehlt; " : "") + (einmalig ? "" : "doppelt"));
                if (form && listen) Console.WriteLine("         kennung: " + k);
            }
        }

        // ---- (i) Export-Herkunft: Datei oder Ordner eines Fremdprozesses unter export\ ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, uint dwFlags);

        const uint SymlinkOrdner = 1;

        /// <summary>ExportOeffnen muss ablehnen; "" = angenommen, sonst Typ (bei Nicht-ArgumentException in Klammern) und Text.</summary>
        static string OeffnenAbgelehnt(string name, string endung)
        {
            try { using (Helfer.Sicherungsdatei.ExportOeffnen(name, endung)) { } return ""; }
            catch (ArgumentException ex) { return ex.Message; }
            catch (Exception ex) { return "(" + ex.GetType().Name + ") " + ex.Message; }
        }

        /// <summary>ExportSchuetzen muss ablehnen; "" = gelungen, sonst Typ (bei Nicht-InvalidOperationException in Klammern) und Text.</summary>
        static string SchuetzenAbgelehnt(string pfad)
        {
            try { Helfer.Sicherungsdatei.ExportSchuetzen(pfad); return ""; }
            catch (InvalidOperationException ex) { return ex.Message; }
            catch (Exception ex) { return "(" + ex.GetType().Name + ") " + ex.Message; }
        }

        /// <summary>ExportName muss mit ArgumentException enden.</summary>
        static bool ExportNameWirft(string art, string endung)
        {
            try { Helfer.Sicherungsdatei.ExportName(art, endung); return false; }
            catch (ArgumentException) { return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Besitzer, Schutz und Eintraege einer Datei oder eines Ordners als Zeile (fuer Details).</summary>
        static string RechteKurz(string pfad, bool ordner, out bool gut)
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            FileSystemSecurity sec = ordner
                ? (FileSystemSecurity)new DirectoryInfo(pfad).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : File.GetAccessControl(pfad, AccessControlSections.Owner | AccessControlSections.Access);
            var besitzer = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            int eintraege = 0;
            string gesehen = "";
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                eintraege++;
                gesehen += r.IdentityReference.Value + ":" + r.FileSystemRights + (r.IsInherited ? " geerbt" : "") + "; ";
            }
            gut = besitzer != null && (besitzer == admins || besitzer == system) && sec.AreAccessRulesProtected && eintraege == 3;
            return (besitzer == null ? "kein Besitzer" : besitzer.Value) + ", geschuetzt=" + sec.AreAccessRulesProtected + ", " + eintraege + " Eintraege: " + gesehen;
        }

        static void PruefeExportHerkunft(string wurzel)
        {
            string ordner = Path.Combine(wurzel, "sicherungen", "massnahmen");
            Directory.CreateDirectory(ordner);
            Helfer.Sicherungsdatei.OrdnerFuerProbe = ordner;
            bool erhoeht = Sammler.Quellen.Rechte.Erhoeht();
            string eigene;
            using (WindowsIdentity wi = WindowsIdentity.GetCurrent()) eigene = wi.User.Value;
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            Console.WriteLine("         Export-Probe laeuft " + (erhoeht ? "erhoeht" : "nicht erhoeht") + ": " + ordner);

            // Namen: nur [A-Za-z0-9._-], kein Pfad, nicht "." oder "..".
            Ist("ExportNameGueltig nimmt 20260919-081500-firewall-1a2b.wfw und einen Ordnernamen an",
                Helfer.Sicherungsdatei.ExportNameGueltig("20260919-081500-firewall-1a2b.wfw") && Helfer.Sicherungsdatei.ExportNameGueltig("20260919-081500-treiber-1a2b"), "");
            // Gemessen 19.09.2026: "x.wfw\n" (Zeilenumbruch am Ende) nahm ExportNameGueltig an, weil $ im
            // .NET-Regex auch vor einem Schluss-Zeilenumbruch trifft; seitdem endet das Muster auf \z, und
            // der Fall steht in der Liste (fuer Sicherungsnamen ebenso, NameGueltig unten).
            foreach (string name in new[] { "a\\b.wfw", "a/b.wfw", "..", ".", "a b.wfw", "", "..\\hosts", "C:\\Windows\\win.ini", "x\ty.wfw", "x.wfw\n" })
                Ist("ExportNameGueltig lehnt \"" + name.Replace("\t", "\\t").Replace("\n", "\\n") + "\" ab", !Helfer.Sicherungsdatei.ExportNameGueltig(name), "");
            Ist("NameGueltig lehnt \"x.json\\n\" ab (Zeilenumbruch am Ende)", !Helfer.Sicherungsdatei.NameGueltig("x.json\n"), "");
            Ist("NameGueltig nimmt 20260919-081500-netz.dns.zuruecksetzen-1a2b.json an",
                Helfer.Sicherungsdatei.NameGueltig("20260919-081500-netz.dns.zuruecksetzen-1a2b.json"), "");
            string nameFw = Helfer.Sicherungsdatei.ExportName("firewall", ".wfw");
            Ist("ExportName(firewall, .wfw) folgt dem Muster yyyyMMdd-HHmmss-firewall-xxxx.wfw (Vertrag M4 1)",
                Regex.IsMatch(nameFw, @"^[0-9]{8}-[0-9]{6}-firewall-[0-9a-f]{4}\.wfw$"), nameFw);
            string nameTr = Helfer.Sicherungsdatei.ExportName("treiber", "");
            Ist("ExportName(treiber, leer) folgt dem Muster yyyyMMdd-HHmmss-treiber-xxxx (Ordner ohne Endung)",
                Regex.IsMatch(nameTr, @"^[0-9]{8}-[0-9]{6}-treiber-[0-9a-f]{4}$"), nameTr);
            Ist("ExportName mit Leerzeichen in der Art wirft ArgumentException", ExportNameWirft("fire wall", ".wfw"), "");
            Ist("ExportName mit Endung ohne Punkt wirft ArgumentException", ExportNameWirft("firewall", "wfw"), "");
            Ist("ExportName mit Pfadzeichen in der Endung wirft ArgumentException", ExportNameWirft("firewall", ".w\\f"), "");
            Ist("ExportOrdnerPfad ist sicherungen\\massnahmen\\export, ohne Anlage",
                string.Equals(Helfer.Sicherungsdatei.ExportOrdnerPfad(), Path.Combine(ordner, "export"), StringComparison.OrdinalIgnoreCase) && !Directory.Exists(Path.Combine(ordner, "export")),
                Helfer.Sicherungsdatei.ExportOrdnerPfad() ?? "(null)");

            // Oeffnen und Schuetzen VOR jedem Handle: Name, Endung, Ort; eine fehlende Datei ist FileNotFound.
            string g = OeffnenAbgelehnt("..\\hosts", ".wfw");
            Ist("ExportOeffnen lehnt ..\\hosts mit ArgumentException ab", g.Length > 0 && !g.StartsWith("("), g);
            g = OeffnenAbgelehnt("20260919-081500-firewall-1a2b.txt", ".wfw");
            Ist("ExportOeffnen lehnt die falsche Endung .txt mit ArgumentException ab", g.Length > 0 && !g.StartsWith("(") && g.Contains(".wfw"), g);
            g = OeffnenAbgelehnt("20260919-081500-firewall-0000.wfw", ".wfw");
            Ist("ExportOeffnen einer fehlenden Datei wirft FileNotFoundException", g.StartsWith("(FileNotFoundException)"), g);
            g = SchuetzenAbgelehnt("..\\hosts");
            Ist("ExportSchuetzen lehnt ..\\hosts mit InvalidOperationException ab", g.Length > 0 && !g.StartsWith("("), g);
            g = SchuetzenAbgelehnt("");
            Ist("ExportSchuetzen ohne Namen wirft InvalidOperationException", g.Length > 0 && !g.StartsWith("("), g);
            string fremdPfad = Path.Combine(wurzel, "20260919-081500-firewall-1a2b.wfw");
            File.WriteAllText(fremdPfad, "fremd", new UTF8Encoding(false));
            g = SchuetzenAbgelehnt(fremdPfad);
            Ist("ExportSchuetzen lehnt einen vollen Pfad ausserhalb von export\\ ab", g.Length > 0 && !g.StartsWith("(") && g.Contains("liegt nicht unter"), g);
            g = SchuetzenAbgelehnt(Path.Combine(ordner, "20260919-081500-firewall-1a2b.wfw"));
            Ist("ExportSchuetzen lehnt eine Datei direkt im Sicherungsordner (nicht unter export\\) ab", g.Length > 0 && !g.StartsWith("("), g);

            // Der Exportordner selbst: erhoeht entsteht er mit Besitzer Administratoren und der
            // geschuetzten DACL; nicht erhoeht wirft ExportOrdner, und es wird nichts exportiert.
            string export = null, ordnerFehler = null;
            try { export = Helfer.Sicherungsdatei.ExportOrdner(); }
            catch (InvalidOperationException ex) { ordnerFehler = ex.Message; }
            catch (Exception ex) { ordnerFehler = "(" + ex.GetType().Name + ") " + ex.Message; }
            if (!erhoeht)
            {
                if (export == null)
                    Ist("nicht erhoeht: ExportOrdner wirft InvalidOperationException (kein Export, nichts geaendert)", ordnerFehler != null && !ordnerFehler.StartsWith("("), ordnerFehler ?? "");
                else
                {
                    string n0 = "20260919-081500-firewall-fff0.wfw";
                    File.WriteAllText(Path.Combine(export, n0), "nutzer", new UTF8Encoding(false));
                    Ist("nicht erhoeht: eine Datei des Nutzers unter export\\ wird von ExportOeffnen abgelehnt", OeffnenAbgelehnt(n0, ".wfw").Length > 0, export);
                }
                return;
            }

            // ---- ab hier nur erhoeht (CI-Runner, erhoehte Werkzeug-Shell) ----
            Ist("erhoeht: ExportOrdner legt sicherungen\\massnahmen\\export an", export != null && Directory.Exists(export) && string.Equals(export, Path.Combine(ordner, "export"), StringComparison.OrdinalIgnoreCase), ordnerFehler ?? export);
            if (export == null) return;
            bool gut;
            string rechte = RechteKurz(export, true, out gut);
            Ist("erhoeht: der Exportordner gehoert Administratoren oder SYSTEM, Vererbung aus, genau drei Eintraege", gut, rechte);
            rechte = RechteKurz(ordner, true, out gut);
            Ist("erhoeht: der Sicherungsordner darueber ist ebenso geschuetzt", gut, rechte);
            Ist("erhoeht: ein zweiter Aufruf von ExportOrdner liefert denselben Ordner", string.Equals(Helfer.Sicherungsdatei.ExportOrdner(), export, StringComparison.OrdinalIgnoreCase), "");

            // Eine Datei, wie netsh sie anlegt: Besitzer aus dem Token, Rechte geerbt. Sie ist noch
            // keine Sicherung (die DACL ist nicht genau die geschuetzte); erst ExportSchuetzen macht sie dazu.
            string name1 = "20260919-081500-firewall-1a2b.wfw";
            string pfad1 = Path.Combine(export, name1);
            File.WriteAllText(pfad1, "probe-export", new UTF8Encoding(false));
            g = OeffnenAbgelehnt(name1, ".wfw");
            Ist("frisch angelegte Datei (geerbte Rechte, wie von netsh): ExportOeffnen lehnt ab (stammt nicht vom Helfer)", g.Length > 0 && g.Contains("stammt nicht vom Helfer"), g);
            g = SchuetzenAbgelehnt(name1);
            Ist("ExportSchuetzen(Name) gelingt", g.Length == 0, g);
            rechte = RechteKurz(pfad1, false, out gut);
            Ist("danach: Besitzer Administratoren oder SYSTEM, Vererbung aus, genau drei Eintraege", gut, rechte);
            Ist("ExportOeffnen nimmt die geschuetzte Datei an", OeffnenAbgelehnt(name1, ".wfw").Length == 0, OeffnenAbgelehnt(name1, ".wfw"));
            Ist("ExportOeffnen ohne Endungsvorgabe nimmt sie an", OeffnenAbgelehnt(name1, "").Length == 0, "");
            Ist("ExportOeffnen mit .WFW (Gross/Klein egal) nimmt sie an", OeffnenAbgelehnt(name1, ".WFW").Length == 0, "");
            g = OeffnenAbgelehnt(name1, ".reg");
            Ist("ExportOeffnen mit verlangter Endung .reg lehnt sie ab", g.Length > 0 && !g.StartsWith("("), g);
            Ist("ExportSchuetzen mit vollem Pfad unter export\\ gelingt ebenso (idempotent)", SchuetzenAbgelehnt(pfad1).Length == 0, "");
            using (FileStream fs = Helfer.Sicherungsdatei.ExportOeffnen(name1, ".wfw"))
            {
                string kanonisch = Helfer.Sicherungsdatei.KanonischerPfad(fs);
                Ist("KanonischerPfad zeigt auf die Datei unter export\\ (fuer netsh advfirewall import)",
                    kanonisch.EndsWith("\\export\\" + name1, StringComparison.OrdinalIgnoreCase), kanonisch);
                Ist("der Inhalt kommt aus demselben Handle", new StreamReader(fs).ReadToEnd() == "probe-export", "");
            }

            // Besitzer auf das eigene Konto: abgelehnt; zurueck: angenommen.
            FileSecurity wechsel = File.GetAccessControl(pfad1, AccessControlSections.Owner);
            wechsel.SetOwner(new SecurityIdentifier(eigene));
            File.SetAccessControl(pfad1, wechsel);
            g = OeffnenAbgelehnt(name1, ".wfw");
            Ist("Besitzer auf das eigene Konto gesetzt: ExportOeffnen lehnt ab", g.Contains("stammt nicht vom Helfer"), g);
            Ist("ExportSchuetzen holt die Datei zurueck (Besitzer wieder Administratoren)", SchuetzenAbgelehnt(name1).Length == 0 && OeffnenAbgelehnt(name1, ".wfw").Length == 0, "");
            wechsel = File.GetAccessControl(pfad1, AccessControlSections.Owner);
            var besitzer = wechsel.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            Ist("der Besitzer ist danach Administratoren", besitzer == admins, besitzer == null ? "kein Besitzer" : besitzer.Value);

            // Ein vierter Eintrag (Lesen fuer das eigene Konto): abgelehnt; ExportSchuetzen setzt die DACL neu.
            var zusatz = new FileSystemAccessRule(new SecurityIdentifier(eigene), FileSystemRights.Read, AccessControlType.Allow);
            FileSecurity dacl = File.GetAccessControl(pfad1, AccessControlSections.Access);
            dacl.AddAccessRule(zusatz);
            File.SetAccessControl(pfad1, dacl);
            g = OeffnenAbgelehnt(name1, ".wfw");
            Ist("ein zusaetzlicher Eintrag fuer das eigene Konto: ExportOeffnen lehnt ab", g.Contains("stammt nicht vom Helfer"), g);
            Ist("ExportSchuetzen setzt genau die geschuetzte DACL wieder, ExportOeffnen nimmt an", SchuetzenAbgelehnt(name1).Length == 0 && OeffnenAbgelehnt(name1, ".wfw").Length == 0, "");
            rechte = RechteKurz(pfad1, false, out gut);
            Ist("danach wieder genau drei Eintraege", gut, rechte);

            // Ein zweiter Name (Hardlink): beide Namen abgelehnt, ExportSchuetzen wirft (mehr als ein Name).
            string linkName = "20260919-081500-firewall-2c3d.wfw";
            string linkPfad = Path.Combine(export, linkName);
            bool link = CreateHardLinkW(linkPfad, pfad1, IntPtr.Zero);
            Ist("Hardlink auf den Export liess sich anlegen (sonst messen die naechsten Zeilen nichts)", link, "Win32-Fehler " + Marshal.GetLastWin32Error());
            if (link)
            {
                string g1 = OeffnenAbgelehnt(name1, ".wfw"), g2 = OeffnenAbgelehnt(linkName, ".wfw");
                Ist("Hardlink: der Originalname wird abgelehnt (mehr als einen Namen)", g1.Contains("mehr als einen Namen"), g1);
                Ist("Hardlink: der zweite Name wird abgelehnt", g2.Contains("mehr als einen Namen"), g2);
                g = SchuetzenAbgelehnt(name1);
                Ist("Hardlink: ExportSchuetzen wirft InvalidOperationException (Hardlink)", g.Length > 0 && !g.StartsWith("(") && g.Contains("Hardlink"), g);
                File.Delete(linkPfad);
                Ist("Hardlink entfernt: ExportOeffnen nimmt die Datei wieder an", OeffnenAbgelehnt(name1, ".wfw").Length == 0, "");
            }

            // Ein Reparse-Punkt (Symlink auf die geschuetzte Datei): am Pfad abgelehnt, bevor geoeffnet wird.
            string symName = "20260919-081500-firewall-3e4f.wfw";
            string symPfad = Path.Combine(export, symName);
            bool sym = CreateSymbolicLinkW(symPfad, pfad1, 0);
            Ist("Symlink auf den Export liess sich anlegen (erhoeht: SeCreateSymbolicLinkPrivilege)", sym, "Win32-Fehler " + Marshal.GetLastWin32Error());
            if (sym)
            {
                g = OeffnenAbgelehnt(symName, ".wfw");
                Ist("Symlink: ExportOeffnen lehnt die Verknuepfung ab", g.Length > 0 && !g.StartsWith("("), g);
                g = SchuetzenAbgelehnt(symName);
                Ist("Symlink: ExportSchuetzen wirft InvalidOperationException (Verknuepfung)", g.Length > 0 && !g.StartsWith("(") && g.Contains("Verknüpfung"), g);
                File.Delete(symPfad);
            }

            // Ein Ordner-Export (Treiber, pnputil /export-driver): ExportSchuetzen schuetzt den Ordner
            // samt Inhalt; ein Ordner-Symlink wird abgelehnt.
            string trPfad = Path.Combine(export, nameTr);
            Directory.CreateDirectory(trPfad);
            File.WriteAllText(Path.Combine(trPfad, "oem42.inf"), "[Version]", new UTF8Encoding(false));
            g = SchuetzenAbgelehnt(nameTr);
            Ist("Ordner-Export: ExportSchuetzen(Ordnername) gelingt", g.Length == 0, g);
            rechte = RechteKurz(trPfad, true, out gut);
            Ist("Ordner-Export: Besitzer Administratoren oder SYSTEM, Vererbung aus, genau drei Eintraege", gut, rechte);
            string symOrdner = Path.Combine(export, "20260919-081500-treiber-5a6b");
            bool symDir = CreateSymbolicLinkW(symOrdner, trPfad, SymlinkOrdner);
            Ist("Ordner-Symlink liess sich anlegen", symDir, "Win32-Fehler " + Marshal.GetLastWin32Error());
            if (symDir)
            {
                g = SchuetzenAbgelehnt("20260919-081500-treiber-5a6b");
                Ist("Ordner-Symlink: ExportSchuetzen wirft InvalidOperationException (Verknuepfung)", g.Length > 0 && !g.StartsWith("(") && g.Contains("Verknüpfung"), g);
                Directory.Delete(symOrdner);
            }

            // Die Firewall-Sicherung (sicherheit.firewall.zuruecksetzen): export nur nach Muster, nur
            // vorhanden und geschuetzt; der Rueckweg oeffnet dieselbe Datei erneut (ZurueckSicherheitPruefen).
            const string Fr = "sicherheit.firewall.zuruecksetzen";
            Abgelehnt(Fr + " mit export=..\\hosts", SicherungAus(Fr, null, new[] { "export", "..\\hosts" }));
            Abgelehnt(Fr + " mit export=x.wfw (nicht nach Muster)", SicherungAus(Fr, null, new[] { "export", "x.wfw" }));
            Abgelehnt(Fr + " mit export=20260919-081500-firewall-0000.wfw (Datei fehlt)", SicherungAus(Fr, null, new[] { "export", "20260919-081500-firewall-0000.wfw" }));
            Abgelehnt(Fr + " ohne export", SicherungAus(Fr, null, new[] { "zustand", "1=an,0,1;2=an,0,1;4=an,0,1" }));
            Angenommen(Fr + " mit export=" + name1 + " (vorhanden, geschuetzt)", SicherungAus(Fr, null, new[] { "export", name1 }));
            Angenommen(Fr + " mit export und zustand nach Muster", SicherungAus(Fr, null, new[] { "export", name1, "zustand", "1=an,0,1;2=aus,-,-;4=an,0,1" }));
            Abgelehnt(Fr + " mit zustand=foo", SicherungAus(Fr, null, new[] { "export", name1, "zustand", "foo" }));
            File.Delete(pfad1);
            Abgelehnt(Fr + " mit export=" + name1 + ", nachdem die Datei geloescht ist", SicherungAus(Fr, null, new[] { "export", name1 }));
        }

        // ---- (j) Rueckweg-Inhalt M4: Profilbindung, hosts, IP-Stack, Firewall eingehend, Winlogon ----

        static void PruefeRueckwegInhaltM4(string wurzel)
        {
            // Der Sicherungsordner der Probe (fuer die Firewall-Faelle, die eine Datei suchen).
            string ordner = Path.Combine(wurzel, "sicherungen", "massnahmen");
            Directory.CreateDirectory(ordner);
            Helfer.Sicherungsdatei.OrdnerFuerProbe = ordner;

            // Profilbindung (Vertrag M4 0.4, 3.7): eine HKCU-Sicherung gilt nur im eigenen Konto.
            const string Auto = "leistung.autostart.deaktivieren";
            string eigene = Helfer.LeistungMassnahmen.ProfilSidNorm(Helfer.LeistungMassnahmen.EigeneSid());
            Ist("die eigene SID hat die Form eines Benutzerprofils (S-1-5-21-... oder S-1-12-1-...)", eigene != null, Helfer.LeistungMassnahmen.EigeneSid() ?? "(null)");
            Abgelehnt(Auto + " HKCU mit fremder profilSid S-1-5-21-1-2-3-1001",
                SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a", "profilSid", "S-1-5-21-1-2-3-1001" }, new[] { "bytes", "020000000000000000000000" }));
            string grund = Helfer.RueckwegMassnahmen.InhaltPruefen(SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a", "profilSid", "S-1-5-21-1-2-3-1001" }, new[] { "bytes", "020000000000000000000000" }));
            Ist("der Grund ist der feste Satz ProfilFremdeSicherung", grund == Helfer.LeistungMassnahmen.ProfilFremdeSicherung, grund ?? "(angenommen)");
            Abgelehnt(Auto + " HKCU ohne profilSid",
                SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a" }, new[] { "bytes", "020000000000000000000000" }));
            Abgelehnt(Auto + " HKCU mit profilSid S-1-5-18 (SYSTEM, kein Profil)",
                SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a", "profilSid", "S-1-5-18" }, new[] { "bytes", "020000000000000000000000" }));
            Abgelehnt(Auto + " HKCU mit profilSid abc",
                SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a", "profilSid", "abc" }, new[] { "bytes", "020000000000000000000000" }));
            if (eigene != null)
            {
                Angenommen(Auto + " HKCU mit der eigenen profilSid",
                    SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a", "profilSid", eigene }, new[] { "bytes", "020000000000000000000000" }));
                Angenommen(Auto + " HKCU mit der eigenen profilSid in Kleinschreibung",
                    SicherungAus(Auto, new[] { "hive", "HKCU", "ort", "Run", "name", "a", "profilSid", eigene.ToLowerInvariant() }, new[] { "bytes", "020000000000000000000000" }));
            }
            Angenommen(Auto + " HKLM ohne profilSid (maschinenweit, keine Bindung)",
                SicherungAus(Auto, new[] { "hive", "HKLM", "ort", "Run", "name", "a" }, new[] { "bytes", "020000000000000000000000" }));

            // hosts (Vertrag M4 3.3): inhalt bis 256 KB, druckbare Zeichen, Tabulator und Zeilenumbrueche.
            const string Ho = "netz.hosts.bereinigen";
            Angenommen(Ho + " mit gewoehnlichem Inhalt (CRLF, Tabulator, Kommentar)",
                SicherungAus(Ho, null, new[] { "inhalt", "# Copyright (c) 1993-2009 Microsoft Corp.\r\n127.0.0.1\tlocalhost\r\n0.0.0.0 windowsupdate.microsoft.com\r\n" }));
            Angenommen(Ho + " mit leerem Inhalt (die Datei war leer)", SicherungAus(Ho, null, new[] { "inhalt", "" }));
            Angenommen(Ho + " mit Inhalt von genau 262144 Zeichen", SicherungAus(Ho, null, new[] { "inhalt", new string('a', 262144) }));
            Abgelehnt(Ho + " mit Inhalt von 262145 Zeichen (mehr als 256 KB)", SicherungAus(Ho, null, new[] { "inhalt", new string('a', 262145) }));
            Abgelehnt(Ho + " mit NUL im Inhalt", SicherungAus(Ho, null, new[] { "inhalt", "127.0.0.1 localhost\0evil" }));
            Abgelehnt(Ho + " mit Steuerzeichen U+0001 im Inhalt", SicherungAus(Ho, null, new[] { "inhalt", "127.0.0.1 localhost\u0001" }));
            Abgelehnt(Ho + " mit Escape U+001B im Inhalt", SicherungAus(Ho, null, new[] { "inhalt", "\u001b[31m127.0.0.1 localhost" }));
            Abgelehnt(Ho + " ohne inhalt", SicherungAus(Ho, null, new[] { "x", "1" }));
            Ist("ZurueckNetzPruefen lehnt NUL im hosts-Inhalt ab",
                Helfer.MassnahmenM3.ZurueckNetzPruefen(SicherungAus(Ho, null, new[] { "inhalt", "a\0b" })) != null, "");
            Ist("ZurueckNetzBekannt kennt netz.hosts.bereinigen und netz.ip.zuruecksetzen, nicht netz.winsock.zuruecksetzen",
                Helfer.MassnahmenM3.ZurueckNetzBekannt(Ho) && Helfer.MassnahmenM3.ZurueckNetzBekannt("netz.ip.zuruecksetzen") && !Helfer.MassnahmenM3.ZurueckNetzBekannt("netz.winsock.zuruecksetzen"), "");

            // IP-Stack (Vertrag M4 3.3): je Zeile SettingID|Beschreibung|IP;IP|Maske;Maske|Gateway|DNS, alles IPv4.
            const string Ip = "netz.ip.zuruecksetzen";
            const string Guid1 = "{3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD}", Guid2 = "{7D2B5F10-0C1E-4E0F-9B4D-1A2B3C4D5E6F}";
            string gueltig = Guid1 + "|Ethernet|192.168.1.20|255.255.255.0|192.168.1.1|192.168.1.1;8.8.8.8";
            Angenommen(Ip + " mit einem festen Adapter", SicherungAus(Ip, null, new[] { "adapter", gueltig }));
            Angenommen(Ip + " mit zwei Adressen und zwei Masken, ohne Gateway und DNS", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20;192.168.1.21|255.255.255.0;255.255.255.0||" }));
            Angenommen(Ip + " mit leerem adapter (kein fester Adapter)", SicherungAus(Ip, null, new[] { "adapter", "" }));
            Angenommen(Ip + " mit zwei Zeilen (LF)", SicherungAus(Ip, null, new[] { "adapter", gueltig + "\n" + Guid2 + "|WLAN|10.0.0.5|255.0.0.0||" }));
            Abgelehnt(Ip + " mit IP=evil", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|evil|255.255.255.0||" }));
            Abgelehnt(Ip + " mit Maske 255.255.255", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20|255.255.255||" }));
            Abgelehnt(Ip + " mit Gateway fe80::1 (IPv6)", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20|255.255.255.0|fe80::1|" }));
            Abgelehnt(Ip + " mit DNS 8.8.8.8;evil", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20|255.255.255.0||8.8.8.8;evil" }));
            Abgelehnt(Ip + " mit 5 statt 6 Feldern", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20|255.255.255.0|" }));
            Abgelehnt(Ip + " mit 2 Adressen und 1 Maske", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20;192.168.1.21|255.255.255.0||" }));
            Abgelehnt(Ip + " ohne Adresse", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet||||" }));
            Abgelehnt(Ip + " mit SettingID ohne Klammern", SicherungAus(Ip, null, new[] { "adapter", "3C49F2BC-A43A-4295-9CE6-E8BA3F74FCAD|Ethernet|192.168.1.20|255.255.255.0||" }));
            Abgelehnt(Ip + " mit derselben SettingID in 2 Zeilen", SicherungAus(Ip, null, new[] { "adapter", gueltig + "\n" + gueltig }));
            Abgelehnt(Ip + " mit 11 DNS-Eintraegen", SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|192.168.1.20|255.255.255.0||1.1.1.1;1.1.1.2;1.1.1.3;1.1.1.4;1.1.1.5;1.1.1.6;1.1.1.7;1.1.1.8;1.1.1.9;1.1.1.10;1.1.1.11" }));
            var viele = new StringBuilder();
            for (int i = 0; i < 65; i++) viele.Append("{" + i.ToString("D8") + "-0000-0000-0000-000000000000}|A|10.0.0.1|255.0.0.0||\n");
            Abgelehnt(Ip + " mit 65 Zeilen (mehr als 64 Adapter)", SicherungAus(Ip, null, new[] { "adapter", viele.ToString() }));
            Abgelehnt(Ip + " ohne adapter", SicherungAus(Ip, null, new[] { "x", "1" }));
            Ist("ZurueckNetzPruefen lehnt IP=evil ab",
                Helfer.MassnahmenM3.ZurueckNetzPruefen(SicherungAus(Ip, null, new[] { "adapter", Guid1 + "|Ethernet|evil|255.255.255.0||" })) != null, "");

            // Firewall eingehend (Vertrag M4 3.3): DefaultInboundAction 0|1, Profil 1|2|4.
            const string Fe = "sicherheit.firewall.eingehend.blockieren";
            Angenommen(Fe + " mit DefaultInboundAction=1, Profil=2", SicherungAus(Fe, new[] { "profil", "2" }, new[] { "DefaultInboundAction", "1", "Profil", "2" }));
            Angenommen(Fe + " mit DefaultInboundAction=0, Profil=4", SicherungAus(Fe, new[] { "profil", "4" }, new[] { "DefaultInboundAction", "0", "Profil", "4" }));
            Abgelehnt(Fe + " mit DefaultInboundAction=2", SicherungAus(Fe, new[] { "profil", "2" }, new[] { "DefaultInboundAction", "2", "Profil", "2" }));
            Abgelehnt(Fe + " mit Profil=3", SicherungAus(Fe, new[] { "profil", "3" }, new[] { "DefaultInboundAction", "1", "Profil", "3" }));
            Abgelehnt(Fe + " ohne DefaultInboundAction", SicherungAus(Fe, new[] { "profil", "2" }, new[] { "Profil", "2" }));
            Ist("ZurueckSicherheitPruefen lehnt DefaultInboundAction=2 ab",
                Helfer.MassnahmenM3.ZurueckSicherheitPruefen(SicherungAus(Fe, new[] { "profil", "2" }, new[] { "DefaultInboundAction", "2", "Profil", "2" })) != null, "");
            Ist("ZurueckSicherheitBekannt kennt eingehend.blockieren und firewall.zuruecksetzen",
                Helfer.MassnahmenM3.ZurueckSicherheitBekannt(Fe) && Helfer.MassnahmenM3.ZurueckSicherheitBekannt("sicherheit.firewall.zuruecksetzen"), "");

            // Winlogon (Vertrag M4 3.3): beide Vorwerte, je hoechstens 260 Zeichen ohne Steuerzeichen; leer = war nicht gesetzt.
            const string Wl = "leistung.winlogon.zuruecksetzen";
            Angenommen(Wl + " mit Shell=explorer.exe und Userinit mit Anhang", SicherungAus(Wl, null, new[] { "Shell", "explorer.exe", "Userinit", "C:\\WINDOWS\\system32\\userinit.exe,C:\\Users\\Public\\x.exe," }));
            Angenommen(Wl + " mit leeren Vorwerten (waren nicht gesetzt)", SicherungAus(Wl, null, new[] { "Shell", "", "Userinit", "" }));
            Angenommen(Wl + " mit ShellArt=expand", SicherungAus(Wl, null, new[] { "Shell", "%SystemRoot%\\explorer.exe", "Userinit", "", "ShellArt", "expand" }));
            Abgelehnt(Wl + " ohne Userinit", SicherungAus(Wl, null, new[] { "Shell", "explorer.exe" }));
            Abgelehnt(Wl + " mit Steuerzeichen in Shell", SicherungAus(Wl, null, new[] { "Shell", "explorer.exe\u0001", "Userinit", "" }));
            Abgelehnt(Wl + " mit Shell von 261 Zeichen", SicherungAus(Wl, null, new[] { "Shell", new string('x', 261), "Userinit", "" }));
            Abgelehnt(Wl + " mit ShellArt=binary", SicherungAus(Wl, null, new[] { "Shell", "explorer.exe", "Userinit", "", "ShellArt", "binary" }));
            Ist("ZurueckLeistungPruefen nimmt zwei leere Vorwerte an",
                Helfer.MassnahmenM3.ZurueckLeistungPruefen(SicherungAus(Wl, null, new[] { "Shell", "", "Userinit", "" })) == null, "");
            Ist("ZurueckLeistungBekannt kennt nur leistung.winlogon.zuruecksetzen",
                Helfer.MassnahmenM3.ZurueckLeistungBekannt(Wl) && !Helfer.MassnahmenM3.ZurueckLeistungBekannt(Auto), "");

            // Kennungen ohne Rueckweg (Vertrag M4 3.3): die Datei ist Beleg, der Plan wird abgelehnt.
            Abgelehnt("netz.winsock.zuruecksetzen (kein Rueckweg, Beleg)", SicherungAus("netz.winsock.zuruecksetzen", null, new[] { "katalog", "Winsock Catalog Provider Entry" }));
            Abgelehnt("update.zuruecksetzen (kein Rueckweg, Beleg)", SicherungAus("update.zuruecksetzen", null, new[] { "verborgen", "4340b239-4b90-4305-9c2e-6a24dfcb6757|1|Hewlett-Packard - USB - 1.0.0.0" }));
            grund = Helfer.RueckwegMassnahmen.InhaltPruefen(SicherungAus("geraet.treiber.neu", new[] { "instanzId", "PCI\\VEN_8086&DEV_125C\\4&3B2C1D0E&0&00E4" }, new[] { "inf", "oem42.inf", "export", "20260919-081500-treiber-1a2b", "name", "Probe", "code", "10" }));
            Ist("geraet.treiber.neu: kein Rueckweg, der Grund nennt den Exportordner und pnputil /add-driver",
                grund != null && grund.Contains("20260919-081500-treiber-1a2b") && grund.Contains("pnputil /add-driver oem42.inf"), grund ?? "(angenommen)");
            grund = Helfer.RueckwegMassnahmen.InhaltPruefen(SicherungAus("geraet.treiber.neu", new[] { "instanzId", "PCI\\VEN_8086&DEV_125C\\4&3B2C1D0E&0&00E4" }, new[] { "inf", "usbhub3.inf", "export", "", "name", "Probe", "code", "10" }));
            Ist("geraet.treiber.neu ohne Export (Windows-Treiber): kein Rueckweg, der Grund nennt den Treiberspeicher",
                grund != null && grund.Contains("Treiberspeicher"), grund ?? "(angenommen)");
            // Mehrzeilige Vorwerte nur dort, wo der Vertrag sie vorsieht: ein Zeilenumbruch in Shell bleibt verboten.
            Abgelehnt(Wl + " mit Zeilenumbruch in Shell", SicherungAus(Wl, null, new[] { "Shell", "explorer.exe\ncalc.exe", "Userinit", "" }));
        }

        static string Kurz(string sid)
        {
            return sid.Length > 14 ? sid.Substring(0, 14) + "..." : sid;
        }
    }
}
