using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
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
    ///
    /// Uebersetzt wird mit allem ausser host\ (Pipe.cs zieht Messung und Ausfuehrung nach sich),
    /// siehe tests\run-tests.ps1. Ausgabe [ok]/[FEHL] je Teilpruefung, Exit 1 bei mindestens einem Rot.
    /// </summary>
    static class HelferProbe
    {
        static int fehler = 0;

        static void Ist(string was, bool bedingung, string detail)
        {
            Console.WriteLine((bedingung ? "         [ok]   " : "         [FEHL] ") + was +
                              (detail.Length > 0 ? "   " + detail : ""));
            if (!bedingung) fehler++;
        }

        static void Main(string[] args)
        {
            string ordner = Path.Combine(Path.GetTempPath(),
                "WW-HelferProbe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(ordner);
            // Das App-Protokoll bleibt im Wegwerf-Ordner: die Probe uebersetzt src\AppLog.cs mit,
            // und ein AppLog-Aufruf aus einer der geprueften Klassen legte sonst
            // %ProgramData%\WindowsWartung\logs an und zoege eine 8.0-Datei aus dem Nutzerprofil nach.
            AppLog.PfadFuerProbe = Path.Combine(ordner, "app.log");

            try
            {
                PruefePipeSicherheit();
                PruefeRechteSichern(ordner);
                PruefeUebernehmen(ordner);
                PruefeUebernahmeAbschliessen(ordner);
                PruefeLaufId(ordner);
            }
            catch (Exception ex)
            {
                Ist("Probe laeuft ohne Ausnahme", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Protokoll.OrdnerFuerProbe = null;
                AppLog.PfadFuerProbe = null;
                try { Directory.Delete(ordner, true); } catch { }
            }

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

        static string Kurz(string sid)
        {
            return sid.Length > 14 ? sid.Substring(0, 14) + "..." : sid;
        }
    }
}
