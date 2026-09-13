using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Der Absichtsspeicher: Antworten des Nutzers auf Fragen des Systems.
    ///
    /// Grundsatz 1 des Konzepts: Absicht ist kein Fehler. Auf dem Rechner des Betreibers ist
    /// eine Grafikeinheit absichtlich deaktiviert (Problemcode 22). Ein naives Werkzeug wuerde
    /// sie "reparieren". Dieses fragt einmal und merkt sich die Antwort - solange sich der
    /// Zustand nicht aendert. Deshalb steckt der Zustand mit in der Kennung der Frage
    /// (z. B. "geraet:PCI\...:22"): tritt derselbe Zustand wieder auf, gilt die Antwort;
    /// ein anderer Code ist eine neue Frage.
    ///
    /// Die Datei liegt maschinenweit (ProgramData), weil die Entscheidung den PC betrifft,
    /// nicht das Konto, das das Werkzeug gerade bedient.
    /// </summary>
    [DataContract]
    public class Entscheidungen
    {
        public const string Absicht = "absicht";
        public const string Reparieren = "reparieren";

        [DataMember(Name = "schema")] public int Schema = 1;
        [DataMember(Name = "antworten")] public Dictionary<string, Antwort> Antworten = new Dictionary<string, Antwort>();

        // Der Serialisierer ruft keinen Konstruktor auf: ohne Feld "antworten" waere das
        // Dictionary null und die naechste Pruefung stuerbe in AntwortAuf.
        [OnDeserialized]
        void NachDemLesen(StreamingContext c)
        {
            if (Antworten == null) Antworten = new Dictionary<string, Antwort>();
        }

        public string AntwortAuf(string frageId)
        {
            Antwort a;
            return frageId != null && Antworten.TryGetValue(frageId, out a) ? a.Wert : null;
        }

        public bool IstAbsicht(string frageId) { return AntwortAuf(frageId) == Absicht; }

        public void Setze(string frageId, string wert, string zeitUtc)
        {
            Antworten[frageId] = new Antwort { Wert = wert, ZeitUtc = zeitUtc };
        }

        public void Vergiss(string frageId) { Antworten.Remove(frageId); }

        // ---------------------------------------------------------------- Ablage

        /// <summary>Testnaht: die Proben lenken die Ablage in einen Wegwerf-Ordner.</summary>
        public static string PfadFuerProbe;

        public static string Pfad()
        {
            if (PfadFuerProbe != null) return PfadFuerProbe;
            return Path.Combine(Ablage.Maschinenweit(), "entscheidungen.json");
        }

        /// <summary>Grund, warum der Speicher leer geladen wurde (unlesbare Datei); sonst null.</summary>
        public static string LadeFehler;

        public static Entscheidungen Laden()
        {
            LadeFehler = null;
            string p = Pfad();
            // Hauptdatei und Sicherung getrennt versuchen: eine unlesbare Hauptdatei darf die
            // intakte .alt-Kopie nicht ueberspringen (sonst waeren alle Antworten still weg).
            foreach (string datei in new[] { p, p + ".alt" })
            {
                if (!File.Exists(datei)) continue;
                try
                {
                    var e = Json.LesenDatei<Entscheidungen>(datei);
                    if (e != null) return e;
                }
                catch (Exception ex) { LadeFehler = datei + ": " + ex.Message; }
            }
            return new Entscheidungen();
        }

        public void Speichern()
        {
            Json.SchreibenDateiSicher(Pfad(), this, true);
        }
    }

    [DataContract]
    public class Antwort
    {
        [DataMember(Name = "wert")] public string Wert;
        [DataMember(Name = "zeit")] public string ZeitUtc;
    }

    /// <summary>
    /// Wo das Werkzeug seine Laufzeitdaten ablegt.
    ///
    /// Maschinenweit (ProgramData) fuer alles, was den PC betrifft: Entscheidungen, Protokolle,
    /// Sicherungen. Nicht LocalAppData: bei einer Erhoehung ueber ein fremdes Konto zeigt das
    /// auf dessen Profil, und der eigentliche Nutzer findet nichts wieder (v7.2.0).
    /// Faellt ProgramData weg (kein Schreibrecht im Dev-Build), weicht es auf LocalAppData aus
    /// und sagt das im Protokoll.
    ///
    /// Abzweigungen (Junction, symbolische Verknuepfung): BUILTIN\Users darf im Laufzeitordner
    /// aendern (RechteSichern). Ein anderer lokaler Nutzer koennte damit logs\, verlauf\ oder
    /// sicherungen\ leeren und als Junction auf einen fremden Ort neu anlegen (mklink /J braucht
    /// kein Sonderrecht); der erhoehte Helfer und die geplante Wartung wuerden dann dort schreiben,
    /// ersetzen und loeschen. Deshalb liefert Unterordner bei einer Abzweigung null, und wer
    /// erhoeht schreibt, prueft vorher IstAbzweigung (13.09.2026).
    /// </summary>
    public static class Ablage
    {
        public const string Ordnername = "WindowsWartung";
        static string _maschinenweit;

        /// <summary>
        /// Warum der Ausweichordner gilt (Pfad und Fehlertext der fehlgeschlagenen Anlage oder
        /// Schreibprobe); null, solange ProgramData traegt. Kern schreibt kein app.log, der Aufrufer
        /// (Host, Helfer, geplante Wartung) sagt es mit diesem Text.
        /// </summary>
        public static string AusweichGrund;

        public static string Maschinenweit()
        {
            if (_maschinenweit != null) return _maschinenweit;
            string pd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Ordnername);
            try
            {
                // Wer den Ordner anlegt, ist sein Besitzer und darf die Rechte setzen, auch ohne
                // Erhoehung. Sonst gehoert die erste Datei dem, der zuerst schreibt (oft der
                // erhoehte Helfer oder die geplante Wartung), und der Host kommt nicht mehr heran.
                bool neu = !Directory.Exists(pd);
                Directory.CreateDirectory(pd);
                if (neu) RechteSichern(pd);
                // Nur das Schreiben entscheidet. Das Loeschen darf scheitern: ein Virenscanner oder
                // der Indexdienst haelt die frische Datei kurz offen, und bis 13.09.2026 schickte
                // schon das den Host fuer die ganze Sitzung ins Profil, waehrend Helfer und geplante
                // Wartung in ProgramData blieben (Antworten galten beim naechsten Start nicht,
                // Zeitplan und Protokolle schienen leer). Eine liegen gebliebene Probe ist leer und
                // stoert nichts; der naechste Start raeumt sie weg.
                string probe = Path.Combine(pd, ".schreibprobe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.WriteAllText(probe, "");
                _maschinenweit = pd;
                AusweichGrund = null;
                ProbenAufraeumen(pd, probe);
            }
            catch (Exception ex)
            {
                string la = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Ordnername);
                Directory.CreateDirectory(la);
                _maschinenweit = la;
                AusweichGrund = pd + ": " + ex.Message;
            }
            return _maschinenweit;
        }

        // Loescht die eigene Schreibprobe (drei Versuche mit kurzer Pause) und liegen gebliebene
        // aus frueheren Sitzungen. Jeder Fehler wird uebergangen: er sagt nichts ueber den Ordner.
        static void ProbenAufraeumen(string ordner, string eigene)
        {
            for (int versuch = 0; versuch < 3; versuch++)
            {
                try { File.Delete(eigene); break; }
                catch (Exception) { if (versuch < 2) System.Threading.Thread.Sleep(50); }
            }
            try
            {
                foreach (string alt in Directory.GetFiles(ordner, ".schreibprobe-*"))
                {
                    if (string.Equals(alt, eigene, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(alt); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        public static bool IstAusweichordner()
        {
            return Maschinenweit().StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                              StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// true, wenn pfad oder eine Ordnerebene zwischen pfad und dem maschinenweiten Ordner eine
        /// Abzweigung traegt (FileAttributes.ReparsePoint: Junction, symbolische Verknuepfung,
        /// Einhaengepunkt). Der maschinenweite Ordner selbst und alles darueber bleiben ungeprueft;
        /// ein Pfad ausserhalb wird nur selbst geprueft. Eine Ebene, die noch nicht existiert, ist
        /// keine Abzweigung; laesst sich ein Attribut nicht lesen, gilt die Ebene als Abzweigung
        /// (im Zweifel nicht hinschreiben). Wirft nie.
        /// </summary>
        public static bool IstAbzweigung(string pfad)
        {
            // Nicht Maschinenweit() rufen: das legte den Ordner an und schriebe die Probe, auch aus
            // einer Testnaht heraus. Solange der Ort nicht bestimmt ist, gilt der ProgramData-Ordner.
            string wurzel = _maschinenweit
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Ordnername);
            return IstAbzweigung(pfad, wurzel);
        }

        /// <summary>
        /// Wie IstAbzweigung(pfad), mit frei gewaehlter Wurzel: geprueft werden pfad und jede Ebene
        /// darueber bis ausschliesslich wurzel; liegt pfad nicht unter wurzel, nur pfad selbst.
        /// wurzel null prueft bis zur Laufwerkswurzel, fuer Ordner, die der Nutzer gewaehlt hat
        /// (z. B. das Ziel der Treibersicherung).
        /// </summary>
        public static bool IstAbzweigung(string pfad, string wurzel)
        {
            if (string.IsNullOrEmpty(pfad)) return false;
            string p, w = null;
            try
            {
                p = VollerPfad(pfad);
                if (!string.IsNullOrEmpty(wurzel)) w = VollerPfad(wurzel);
            }
            catch (Exception) { return true; }   // kein lesbarer Pfad: dorthin wird nicht geschrieben

            bool unterWurzel = w != null && p.StartsWith(w.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            while (true)
            {
                if (w != null && string.Equals(p, w, StringComparison.OrdinalIgnoreCase)) return false;   // Wurzel erreicht
                if (TraegtAbzweigung(p)) return true;
                if (w != null && !unterWurzel) return false;   // ausserhalb der Wurzel: nur der Pfad selbst
                string eltern = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(eltern)) return false;   // Laufwerkswurzel
                p = eltern;
            }
        }

        // Absoluter Pfad ohne Trenner am Ende, damit sich die Ebenen vergleichen lassen. Eine
        // Laufwerks- oder Freigabewurzel ("C:\", "\\server\share\") hat keinen Elternordner und bleibt.
        static string VollerPfad(string pfad)
        {
            string p = Path.GetFullPath(pfad);
            if (string.IsNullOrEmpty(Path.GetDirectoryName(p))) return p;
            return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // Attribut der Ebene selbst (GetAttributes folgt einer Abzweigung nicht). Nicht vorhanden:
        // keine Abzweigung; nicht lesbar: im Zweifel Abzweigung.
        static bool TraegtAbzweigung(string p)
        {
            try { return (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Unterordner des maschinenweiten Ordners, wird angelegt; nie werfen (im Zweifel der Pfad
        /// ohne Anlage). null, wenn der Unterordner oder eine Ebene dazwischen eine Abzweigung ist
        /// (IstAbzweigung): dorthin schreibt kein Aufrufer, er weicht aus oder laesst es und sagt es.
        /// </summary>
        public static string Unterordner(string name)
        {
            string p = Path.Combine(Maschinenweit(), name);
            if (IstAbzweigung(p)) return null;
            try { Directory.CreateDirectory(p); } catch (Exception) { }
            return p;
        }

        public static string Protokolle() { return Unterordner("protokoll"); }
        public static string Verlauf() { return Unterordner("verlauf"); }
        public static string Logs() { return Unterordner("logs"); }
        public static string Sicherungen(string art) { return Unterordner(Path.Combine("sicherungen", art)); }

        /// <summary>Der Nutzerordner (WebView2-Daten, Zoom); bleibt im Profil, weil er das Konto betrifft.</summary>
        public static string Nutzer()
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Ordnername);
            try { Directory.CreateDirectory(p); } catch (Exception) { }
            return p;
        }

        /// <summary>
        /// Gibt BUILTIN\Users Aenderungsrechte (vererbt) auf den maschinenweiten Ordner. Noetig, weil
        /// Dateien, die der erhoehte Helfer anlegt, sonst der Administratorengruppe gehoeren und der
        /// nicht erhoehte Host sie nicht mehr ueberschreiben kann (File.Replace braucht Loeschrecht).
        /// Setzen darf nur, wer erhoeht ist oder den Ordner selbst angelegt hat; sonst false, nie werfen.
        /// </summary>
        public static bool RechteSichern()
        {
            return RechteSichern(Maschinenweit());
        }

        public static bool RechteSichern(string ordner)
        {
            try
            {
                var di = new DirectoryInfo(ordner);
                if (!di.Exists) return false;
                var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
                var acl = di.GetAccessControl();
                var gewuenscht = System.Security.AccessControl.FileSystemRights.Modify | System.Security.AccessControl.FileSystemRights.Synchronize;
                foreach (System.Security.AccessControl.FileSystemAccessRule r in acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)))
                {
                    if (r.IdentityReference == users
                        && r.AccessControlType == System.Security.AccessControl.AccessControlType.Allow
                        && (r.FileSystemRights & gewuenscht) == gewuenscht
                        && (r.InheritanceFlags & System.Security.AccessControl.InheritanceFlags.ContainerInherit) != 0
                        && (r.InheritanceFlags & System.Security.AccessControl.InheritanceFlags.ObjectInherit) != 0)
                        return true;   // schon da
                }
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users, gewuenscht,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
                di.SetAccessControl(acl);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Einmalige Uebernahme einer alten Datei an den neuen Ort (8.0 → 8.1: Verlauf, Zeitplan, app.log
        /// aus dem Profil nach ProgramData). Kopiert nur, wenn die neue fehlt und die alte da ist; nie werfen.
        /// </summary>
        public static bool Uebernehmen(string alterPfad, string neuerPfad)
        {
            try
            {
                if (string.IsNullOrEmpty(alterPfad) || string.IsNullOrEmpty(neuerPfad)) return false;
                if (File.Exists(neuerPfad) || !File.Exists(alterPfad)) return false;
                string ordner = Path.GetDirectoryName(neuerPfad);
                if (!string.IsNullOrEmpty(ordner)) Directory.CreateDirectory(ordner);
                File.Copy(alterPfad, neuerPfad, false);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
