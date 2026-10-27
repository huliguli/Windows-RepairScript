using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    // Gemeinsame Hilfen der Meilenstein-3-Massnahmen (docs/M3-ENTWURF.md, Abschnitt 3): die
    // Sicherungsdatei (Werte vor dem Eingriff, Grundlage des Rueckwegs), WMI-Methodenaufrufe
    // mit Zeitgrenze, COM ueber spaetes Binden (kein dynamic, keine Interop-Referenz), Registry
    // in der 64-Bit-Sicht und die Nachweiszeile fuer den Host. Kein Skriptprozess, kein Kindprozess.
    //
    // Die Signaturen sind Vertrag zwischen den Bauabschnitten K3 (Netz, Sicherheit) und K4
    // (Datentraeger, Leistung, Updates, Hardware, Rueckweg): wer sie aendert, aendert beide Seiten.

    /// <summary>
    /// Inhalt einer Sicherungsdatei unter sicherungen\massnahmen: welche Massnahme mit welchen
    /// Parametern lief und welche Werte VORHER galten. Der Rueckweg (Kennung rueckweg) liest sie
    /// und schreibt die Werte zurueck. Format (Vertrag Abschnitt 1):
    /// { "kennung", "zeit", "parameter": {...}, "vorher": {...}, "rueckwegKennung": "rueckweg" }.
    /// </summary>
    [DataContract]
    public class Sicherung
    {
        [DataMember(Name = "kennung", Order = 1)] public string Kennung;
        /// <summary>Zeit des Schreibens, ISO-8601 in UTC (Kern.Zeit.Utc).</summary>
        [DataMember(Name = "zeit", Order = 2)] public string Zeit;
        [DataMember(Name = "parameter", Order = 3)] public Dictionary<string, string> Parameter = new Dictionary<string, string>();
        [DataMember(Name = "vorher", Order = 4)] public Dictionary<string, string> Vorher = new Dictionary<string, string>();
        [DataMember(Name = "rueckwegKennung", Order = 5)] public string RueckwegKennung = "rueckweg";

        // Der Serialisierer ruft keine Konstruktoren auf: Woerterbuecher nach dem Lesen absichern.
        [OnDeserialized]
        void NachDemLesen(StreamingContext c)
        {
            if (Parameter == null) Parameter = new Dictionary<string, string>();
            if (Vorher == null) Vorher = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(RueckwegKennung)) RueckwegKennung = "rueckweg";
        }

        /// <summary>Wert aus "vorher" oder null.</summary>
        public string Wert(string name)
        {
            string v;
            return name != null && Vorher.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>Wert aus "parameter" oder null.</summary>
        public string ParameterWert(string name)
        {
            string v;
            return name != null && Parameter.TryGetValue(name, out v) ? v : null;
        }
    }

    /// <summary>
    /// Schreibt und liest die Sicherungsdatei einer Massnahme. Name:
    /// sicherungen\massnahmen\yyyyMMdd-HHmmss-kennung-xxxx.json (Sekunden plus 4 Hex, CreateNew:
    /// eine Kollision ueberschreibt nie, sondern nimmt den naechsten Namen, wie die .reg-Sicherung
    /// in src/RegistryScan.cs). Geschrieben wird VOR dem Eingriff; scheitert das Schreiben, wirft
    /// Schreiben, und die Massnahme laeuft nicht (Grundsatz 2: erst die Sicherung, dann der Eingriff).
    ///
    /// Keine Sicherung hinter einer Abzweigung: der Helfer schreibt erhoeht, und BUILTIN\Users darf
    /// den Laufzeitordner aendern. Der Ordner wird nur in Schreiben angelegt, nie in Lesen (der
    /// Rueckweg im Trockenlauf schreibt nichts), und er entsteht schon mit der geschuetzten DACL.
    ///
    /// Herkunft statt Ort (Vertrag 8.2, 8.10): der Rueckweg schreibt die Werte aus der Datei erhoeht
    /// zurueck (Firewall aus, EnableLUA 0, Dienst-Startart), also darf nur eine Datei gelten, die
    /// der erhoehte Helfer selbst angelegt hat. Ablage.RechteSichern gibt BUILTIN\Users vererbt
    /// Aenderungsrechte auf den ganzen Laufzeitordner; deshalb bekommt sicherungen\massnahmen bei
    /// jedem Schreiben Besitzer Administratoren und eine eigene, nicht vererbende DACL (SYSTEM und
    /// Administratoren Vollzugriff, Benutzer nur Lesen und Ausfuehren), und jede Datei entsteht mit
    /// derselben geschuetzten DACL. Lesen verlangt: kein Reparse-Punkt am Pfad, und am geoeffneten
    /// Handle Besitzer Administratoren oder SYSTEM, GENAU die geschuetzte DACL (nicht bloss "kein
    /// Schreibrecht fuer fuenf Gruppen": ein Eintrag fuer die eigene Konto-SID faellt sonst durch)
    /// und genau einen Namen (kein Hardlink). Ein nicht erhoehter Prozess (auch ein
    /// Administrator-Konto im gefilterten Token) kann weder Administratoren als Besitzer eintragen
    /// noch diese DACL auf eine Datei setzen, die ihm nicht gehoert; ein HMAC ueber den Inhalt ist
    /// deshalb nicht noetig. Was sich nicht pruefen laesst, gilt als fremd (fail closed).
    ///
    /// Am Handle, nicht am Pfad (zweite Runde 8.10): solange der Ordner noch die geerbten Rechte
    /// traegt, darf ein Benutzer ihn loeschen, umbenennen und als Junction neu anlegen. Eine
    /// Pruefung am Pfad und ein Setzen am Pfad (SetNamedSecurityInfo folgt Junctions) liessen
    /// zwischen beiden ein Fenster, in dem der erhoehte Helfer Besitzer und DACL auf ein fremdes
    /// Ziel setzte. Deshalb oeffnet Schreiben den Ordner einmal (NtOpenFile, exakt die noetigen
    /// Rechte), prueft an diesem Handle, wohin er wirklich fuehrt (GetFinalPathNameByHandle gegen
    /// den Laufzeitordner), liest und setzt Besitzer und DACL an diesem Handle (GetSecurityInfo,
    /// SetSecurityInfo) und legt die Datei RELATIV zu diesem Handle an (NtCreateFile mit
    /// RootDirectory): gemessen am 18.09.2026 laesst ein offenes Ordner-Handle die Eltern nicht
    /// umbenennen, den Ordner selbst aber schon; ueber den Pfad koennte die Datei also noch in
    /// einer untergeschobenen Junction landen, relativ zum Handle nicht. Den Besitz eines fremden
    /// Ordners uebernimmt der Helfer mit SeTakeOwnershipPrivilege (nur fuer diesen Block
    /// eingeschaltet), erst der Besitzer, dann die DACL.
    ///
    /// Exporte (M4-Entwurf 3.2): netsh und pnputil legen ihre Dateien und Ordner unter
    /// sicherungen\massnahmen\export\ selbst an. ExportOrdner legt diesen Unterordner an und
    /// schuetzt ihn wie den Elternordner; ExportSchuetzen setzt danach Besitzer Administratoren
    /// und genau die geschuetzte DACL auf das, was der Fremdprozess angelegt hat (scheitert das,
    /// gilt die Sicherung als nicht angelegt, und die Massnahme bricht VOR dem Eingriff ab);
    /// ExportOeffnen oeffnet eine Exportdatei fuer den Rueckweg nach denselben Regeln wie Lesen
    /// (Name, Ort, kein Reparse-Punkt, Herkunft am Handle) und laesst das Handle offen, damit
    /// netsh advfirewall import den kanonischen Pfad liest, waehrend die Datei nicht getauscht
    /// werden kann. HerkunftPruefen ist dafuer die eine Funktion fuer JSON und Export.
    ///
    /// Trockenlauf: Schreiben schreibt nichts, meldet "(trocken) Sicherung ..." und liefert null.
    /// </summary>
    public static class Sicherungsdatei
    {
        /// <summary>Werte-Schluessel fuer den Host: Pfad der geschriebenen Sicherungsdatei.</summary>
        public const string DateiSchluessel = "sicherung.datei";

        /// <summary>Name des Export-Unterordners unter dem Sicherungsordner (Vertrag M4 1 und 3.2).</summary>
        public const string ExportUnterordner = "export";

        /// <summary>Dateiname einer Sicherung, wie ihn die Kennung rueckweg annimmt (Vertrag Abschnitt 1).</summary>
        // \z statt $: $ trifft in .NET auch vor einem abschliessenden Zeilenumbruch, ein Name mit
        // Zeilenumbruch am Ende waere sonst gueltig (gemessen 19.09.2026, T1); \z verlangt das echte Ende.
        static readonly Regex NameRx = new Regex(@"^[A-Za-z0-9._-]+\.json\z", RegexOptions.Compiled);

        /// <summary>Name eines Exports (Datei oder Ordner) direkt unter export\: kein Pfad, keine Leerzeichen.</summary>
        static readonly Regex ExportNameRx = new Regex(@"^[A-Za-z0-9._-]+\z", RegexOptions.Compiled);

        /// <summary>Art eines Exports im Dateinamen (firewall, treiber): nur Kleinbuchstaben.</summary>
        static readonly Regex ExportArtRx = new Regex(@"^[a-z]+$", RegexOptions.Compiled);

        /// <summary>Kennung einer Massnahme, wie sie in den Dateinamen darf (dieselbe Menge wie Auto.cs).</summary>
        static readonly Regex KennungRx = new Regex(@"^[a-z0-9.]+$", RegexOptions.Compiled);

        // Die drei Eintraege der geschuetzten DACL (Vertrag 8.2); Benutzer lesen, damit der nicht
        // erhoehte Host die Datei anzeigen kann, mehr nicht. Beim Lesen zaehlt nur die genaue
        // Uebereinstimmung (DaclStimmt); eine Liste verbotener Schreib-Eintraege gibt es nicht mehr,
        // weil sie jede SID uebersaehe, die nicht auf ihr steht (Vertrag 8.10).
        static readonly SecurityIdentifier SidSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        static readonly SecurityIdentifier SidAdministratoren = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        static readonly SecurityIdentifier SidBenutzer = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        /// <summary>
        /// Nur fuer Proben: verlegt die Sicherungen in einen Wegwerf-Ordner, damit ein Test weder
        /// ProgramData anlegt noch eine echte Sicherung des Nutzers daneben schreibt (wie
        /// RegistryScan.OrdnerFuerProbe). Im laufenden Programm bleibt das Feld null.
        /// </summary>
        public static string OrdnerFuerProbe;

        /// <summary>
        /// Der Sicherungsordner als Pfad, OHNE ihn anzulegen (Vertrag 8.10: Lesen legt nichts an,
        /// und ein Ordner, der nur ueber Ablage.Unterordner entstuende, truege die geerbten
        /// Aenderungsrechte fuer Benutzer, bis die erste Stufe-2-Massnahme ihn schuetzt). null,
        /// wenn er oder eine Ebene darueber hinter einer Abzweigung liegt: dann wird weder gelesen
        /// noch geschrieben. Angelegt wird er nur in Schreiben, dort gleich mit der geschuetzten DACL.
        /// </summary>
        public static string Ordner()
        {
            string p = OrdnerPfad();
            return OrdnerIstAbzweigung(p) ? null : p;
        }

        // Der Pfad allein, ohne Anlage und ohne Pruefung (fuer Meldungen).
        static string OrdnerPfad()
        {
            return string.IsNullOrEmpty(OrdnerFuerProbe)
                ? Path.Combine(Ablage.Maschinenweit(), "sicherungen", "massnahmen")
                : OrdnerFuerProbe;
        }

        public static bool NameGueltig(string dateiname)
        {
            return !string.IsNullOrEmpty(dateiname) && dateiname.Length <= 200 && NameRx.IsMatch(dateiname);
        }

        /// <summary>
        /// Schreibt die Sicherung, setzt k.Werte["sicherung.datei"], eine Zeile und den
        /// Protokolleintrag (Art sicherung). Rueckgabe: voller Pfad; null im Trockenlauf.
        /// Wirft ArgumentException bei einer Kennung ausserhalb von ^[a-z0-9.]+$ (sie wird
        /// Dateiname) und InvalidOperationException, wenn nicht geschrieben werden kann
        /// (Abzweigung, Ordner nicht beschreibbar, DACL nicht setzbar, weil nicht erhoeht oder
        /// fremder Ordner): der Aufrufer bricht die Massnahme dann ab, und es wird nichts geaendert.
        /// </summary>
        public static string Schreiben(Ausfuehrungskontext k, string kennung, IDictionary<string, string> parameter, IDictionary<string, string> vorher)
        {
            KennungPruefen(kennung);
            var s = new Sicherung { Kennung = kennung, Zeit = Zeit.Utc(DateTime.UtcNow) };
            if (parameter != null) foreach (var p in parameter) s.Parameter[p.Key] = p.Value ?? "";
            if (vorher != null) foreach (var p in vorher) s.Vorher[p.Key] = p.Value ?? "";

            if (k != null && k.Trocken)
            {
                k.Schreibe("(trocken) Sicherung " + kennung + ": " + Kurz(s.Vorher), "dim");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Sicherung, kennung, "(trocken) Sicherung nicht geschrieben", new { vorher = Kurz(s.Vorher), trocken = true });
                return null;
            }

            string ordner = Ordner();
            if (ordner == null)
                throw new InvalidOperationException("Der Sicherungsordner " + OrdnerPfad()
                    + " liegt hinter einer Verknüpfung auf einen anderen Ort oder ist nicht prüfbar; es wird nichts gesichert und nichts geändert.");

            string text = Json.Schreiben(s, true);
            string ziel = NeuerName(ordner, kennung);
            bool geaendert;
            using (Nagel nagel = OrdnerSchuetzen(ordner, true, out geaendert))
            {
                // Die Datei entsteht relativ zum festgenagelten Ordner-Handle, mit der geschuetzten
                // DACL als Sicherheitsbeschreibung: sie existiert keinen Augenblick mit den Rechten
                // eines anderen Ordners und keinen Augenblick mit geerbten Rechten.
                for (int versuch = 0; !DateiSchreiben(nagel, Path.GetFileName(ziel), text); versuch++)
                {
                    if (versuch >= 3)
                        throw new InvalidOperationException("Die Sicherung " + Path.GetFileName(ziel) + " ließ sich nicht schreiben: 4 Namen nacheinander waren schon vergeben.");
                    string neu = NeuerName(ordner, kennung);
                    AppLog.Warn("Sicherung " + Path.GetFileName(ziel) + " gibt es schon; neuer Name " + Path.GetFileName(neu));
                    ziel = neu;
                }
            }
            if (geaendert) AppLog.Info("Sicherungsordner " + ordner + " geschützt (Besitzer Administratoren, Benutzer nur Lesen).");

            if (k != null)
            {
                k.Werte[DateiSchluessel] = ziel;
                k.Schreibe("   Sicherung geschrieben: " + ziel, "dim");
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Sicherung, kennung, "Sicherung geschrieben: " + Path.GetFileName(ziel),
                        new { datei = ziel, vorher = Kurz(s.Vorher), parameter = Kurz(s.Parameter) });
            }
            return ziel;
        }

        // ------------------------------------------------------------------ DACL (Vertrag 8.2)

        /// <summary>
        /// Die geschuetzte DACL fuer Ordner (vererbend) oder Datei: Vererbung aus, SYSTEM und
        /// Administratoren Vollzugriff, Benutzer Lesen und Ausfuehren, sonst niemand.
        /// </summary>
        static FileSystemSecurity GeschuetzteDacl(bool ordner)
        {
            FileSystemSecurity sec = ordner ? (FileSystemSecurity)new DirectorySecurity() : new FileSecurity();
            sec.SetAccessRuleProtection(true, false);
            InheritanceFlags erben = ordner ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
            sec.AddAccessRule(new FileSystemAccessRule(SidSystem, FileSystemRights.FullControl, erben, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(SidAdministratoren, FileSystemRights.FullControl, erben, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(SidBenutzer, FileSystemRights.ReadAndExecute, erben, PropagationFlags.None, AccessControlType.Allow));
            return sec;
        }

        /// <summary>
        /// true, wenn die gelesene DACL genau die geschuetzte ist: Vererbung aus, die drei
        /// Allow-Eintraege mit genau diesen Rechten und Vererbungsmarken, kein weiterer Eintrag.
        /// </summary>
        static bool DaclStimmt(FileSystemSecurity sec, bool ordner)
        {
            if (sec == null || !sec.AreAccessRulesProtected) return false;
            InheritanceFlags erben = ordner ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
            bool system = false, admins = false, benutzer = false;
            int anzahl = 0;
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                anzahl++;
                if (r.AccessControlType != AccessControlType.Allow || r.IsInherited || r.InheritanceFlags != erben || r.PropagationFlags != PropagationFlags.None) return false;
                var sid = r.IdentityReference as SecurityIdentifier;
                if (sid == null) return false;
                if (sid == SidSystem && RechteGleich(r.FileSystemRights, FileSystemRights.FullControl)) system = true;
                else if (sid == SidAdministratoren && RechteGleich(r.FileSystemRights, FileSystemRights.FullControl)) admins = true;
                else if (sid == SidBenutzer && RechteGleich(r.FileSystemRights, FileSystemRights.ReadAndExecute)) benutzer = true;
                else return false;
            }
            return anzahl == 3 && system && admins && benutzer;
        }

        // Ein Allow-Eintrag bekommt beim Schreiben von .NET das Recht Synchronize dazu (ReadAndExecute
        // wird als "ReadAndExecute, Synchronize" zurueckgelesen); fuer den Vergleich zaehlt es nicht.
        static bool RechteGleich(FileSystemRights ist, FileSystemRights soll)
        {
            return (ist | FileSystemRights.Synchronize) == (soll | FileSystemRights.Synchronize);
        }

        /// <summary>true, wenn der Besitzer Administratoren (S-1-5-32-544) oder SYSTEM (S-1-5-18) ist.</summary>
        static bool BesitzerStimmt(SecurityIdentifier besitzer)
        {
            return besitzer != null && (besitzer == SidAdministratoren || besitzer == SidSystem);
        }

        /// <summary>Der Besitzer aus einer gelesenen Sicherheitsbeschreibung; null, wenn keiner eingetragen oder er nicht lesbar ist.</summary>
        static SecurityIdentifier Besitzer(FileSystemSecurity sec)
        {
            try { return sec == null ? null : sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Bringt einen schon vorhandenen Sicherungsordner in Helferbesitz, ohne ihn anzulegen:
        /// fuer den Helferstart (erhoeht), damit der Ordner nicht bis zur ersten Stufe-2-Massnahme
        /// mit den geerbten Aenderungsrechten fuer Benutzer offen bleibt (zweite Runde 8.10; ein
        /// Benutzer koennte ihn bis dahin loeschen und als Junction neu anlegen, und jede Sicherung
        /// bliebe blockiert, bis jemand die Junction von Hand entfernt). Wirft nie; app.log sagt,
        /// was war. Nicht erhoeht oder kein Ordner: nichts zu tun.
        /// </summary>
        public static void OrdnerAbsichern()
        {
            try
            {
                if (!Sammler.Quellen.Rechte.Erhoeht()) return;
                string ordner = Ordner();
                if (ordner == null)
                {
                    AppLog.Warn("Sicherungsordner " + OrdnerPfad() + " liegt hinter einer Verknüpfung auf einen anderen Ort; er wird nicht geschützt und nicht benutzt.");
                    return;
                }
                if (!Directory.Exists(ordner)) return;
                bool geaendert;
                using (OrdnerSchuetzen(ordner, false, out geaendert)) { }
                if (geaendert) AppLog.Info("Sicherungsordner " + ordner + " geschützt (Besitzer Administratoren, Benutzer nur Lesen).");
            }
            catch (Exception ex) { AppLog.Warn("Sicherungsordner nicht geschützt: " + ex.Message); }
        }

        // ------------------------------------------------------------------ Export (Vertrag M4 3.2)

        /// <summary>
        /// Der Pfad des Export-Unterordners OHNE Anlage und ohne Pruefung (fuer Meldungen und fuer
        /// den Ort einer Exportdatei); null, wenn der Sicherungsordner hinter einer Abzweigung liegt.
        /// </summary>
        public static string ExportOrdnerPfad()
        {
            string ordner = Ordner();
            return ordner == null ? null : Path.Combine(ordner, ExportUnterordner);
        }

        /// <summary>
        /// Legt sicherungen\massnahmen\export an (den Elternordner mit, beide gleich mit der
        /// geschuetzten DACL), bringt beide in Helferbesitz und prueft am Handle, dass keine
        /// Abzweigung im Spiel ist (OrdnerSchuetzen). Rueckgabe: der Pfad, an den netsh oder
        /// pnputil schreiben. Wirft InvalidOperationException, wenn Anlegen, Besitz oder DACL
        /// scheitern (nicht erhoeht, Abzweigung): dann wird nichts exportiert und nichts geaendert.
        /// Trockenlauf: der Aufrufer ruft diese Funktion nicht (kein Ordner entsteht trocken).
        /// </summary>
        public static string ExportOrdner()
        {
            string ordner = Ordner();
            if (ordner == null)
                throw new InvalidOperationException("Der Sicherungsordner " + OrdnerPfad()
                    + " liegt hinter einer Verknüpfung auf einen anderen Ort oder ist nicht prüfbar; es wird nichts exportiert und nichts geändert.");
            string export = Path.Combine(ordner, ExportUnterordner);
            bool geaendert;
            // Erst der Elternordner (er haelt waehrend der Anlage des Unterordners die Eltern fest),
            // dann der Unterordner selbst; beide Handles werden danach freigegeben, denn der
            // Fremdprozess schreibt ueber den Pfad, und der Elternordner ist nach dem Schutz fuer
            // Benutzer weder loeschbar noch umbenennbar (nur Lesen und Ausfuehren).
            using (OrdnerSchuetzen(ordner, true, out geaendert))
            {
                if (geaendert) AppLog.Info("Sicherungsordner " + ordner + " geschützt (Besitzer Administratoren, Benutzer nur Lesen).");
                using (OrdnerSchuetzen(export, true, out geaendert)) { }
                if (geaendert) AppLog.Info("Exportordner " + export + " geschützt (Besitzer Administratoren, Benutzer nur Lesen).");
            }
            return export;
        }

        /// <summary>
        /// Name fuer einen neuen Export: yyyyMMdd-HHmmss-&lt;art&gt;-&lt;4hex&gt;&lt;endung&gt; (Vertrag M4 1:
        /// firewall → .wfw; treiber → Ordner ohne Endung). art nur Kleinbuchstaben, endung leer
        /// oder ".xyz"; sonst ArgumentException. Der Name im JSON ist immer nur dieser Name, nie ein Pfad.
        /// </summary>
        public static string ExportName(string art, string endung)
        {
            if (string.IsNullOrEmpty(art) || art.Length > 20 || !ExportArtRx.IsMatch(art))
                throw new ArgumentException("Die Export-Art „" + Massnahmenbau.Kurz(art) + "“ taugt nicht als Dateiname (erlaubt: Kleinbuchstaben, höchstens 20 Zeichen).");
            endung = endung ?? "";
            if (endung.Length > 0 && (endung.Length > 8 || endung[0] != '.' || !ExportNameRx.IsMatch(endung.Substring(1))))
                throw new ArgumentException("Die Export-Endung „" + Massnahmenbau.Kurz(endung) + "“ ist keine Dateiendung.");
            return DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + art + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) + endung;
        }

        /// <summary>true, wenn name ein Exportname ohne Pfadbestandteile ist ([A-Za-z0-9._-]+, hoechstens 200 Zeichen).</summary>
        public static bool ExportNameGueltig(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 200 && ExportNameRx.IsMatch(name)
                && name != "." && name != "..";
        }

        /// <summary>
        /// Setzt Besitzer Administratoren und genau die geschuetzte DACL (Vererbung aus) auf eine
        /// Datei oder einen Ordner, den ein Fremdprozess (netsh, pnputil) unter export\ angelegt
        /// hat, beides am Handle, beides danach nachgelesen; eine Datei muss dazu genau einen
        /// Namen haben. pfad muss direkt unter dem Exportordner liegen (Name nach
        /// ExportNameGueltig) und darf kein Reparse-Punkt sein. Wirft InvalidOperationException,
        /// wenn irgendetwas davon scheitert: dann gilt die Sicherung als nicht angelegt, und die
        /// Massnahme bricht VOR dem Eingriff ab (der Aufrufer loescht den ungeschuetzten Export).
        /// </summary>
        public static void ExportSchuetzen(string pfad)
        {
            string name = ExportPfadPruefen(pfad, "geschützt");
            string export = ExportOrdnerPfad();
            string voll = Path.Combine(export, name);
            FileAttributes attr;
            try { attr = File.GetAttributes(voll); }
            catch (Exception ex) { throw new InvalidOperationException("Der Export " + name + " ist nicht lesbar (" + ex.Message + "); die Sicherung gilt als nicht angelegt.", ex); }
            if ((attr & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Der Export " + name + " ist eine Verknüpfung auf einen anderen Ort; die Sicherung gilt als nicht angelegt.");
            bool ordner = (attr & FileAttributes.Directory) != 0;
            string erwartet = ErwarteterPfad(export) + Path.DirectorySeparatorChar + name;
            bool geaendert;
            SafeFileHandle h = null;
            try
            {
                h = Schuetzen(voll, ordner, erwartet, out geaendert);
                if (!ordner)
                {
                    Native.BY_HANDLE_FILE_INFORMATION info;
                    if (!Native.GetFileInformationByHandle(h, out info))
                        throw new InvalidOperationException("die Zahl der Namen ist nicht lesbar (Win32-Fehler " + Marshal.GetLastWin32Error() + ")");
                    if (info.nNumberOfLinks != 1)
                        throw new InvalidOperationException(info.nNumberOfLinks + " Namen zeigen auf dieselbe Datei (Hardlink)");
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Der Export " + name + " ließ sich nicht gegen andere Konten schützen: " + ex.Message + "; die Sicherung gilt als nicht angelegt, es wird nichts geändert.", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Der Export " + name + " ließ sich nicht gegen andere Konten schützen (" + ex.GetType().Name + ": " + ex.Message + "); die Sicherung gilt als nicht angelegt, es wird nichts geändert.", ex);
            }
            finally
            {
                if (h != null) h.Dispose();
            }
            if (geaendert) AppLog.Info("Export " + name + " geschützt (Besitzer Administratoren, Benutzer nur Lesen).");
        }

        /// <summary>
        /// Oeffnet eine Exportdatei fuer den Rueckweg nach denselben Regeln wie Lesen: name ohne
        /// Pfad (ExportNameGueltig), mit der verlangten Endung (endungOderLeer, Groß/Klein egal;
        /// leer = keine Vorgabe), direkt unter export\, kein Reparse-Punkt am Pfad, genau ein
        /// Oeffnen (FileShare.Read), am Handle Besitzer Administratoren oder SYSTEM, genau die
        /// geschuetzte DACL und genau ein Name. Der Aufrufer liest aus dem Handle oder reicht
        /// KanonischerPfad(fs) an netsh, solange der Stream offen ist, und schliesst ihn danach.
        /// Wirft ArgumentException (Name, Endung, Ort, Herkunft), FileNotFoundException, sonst die Leseausnahme.
        /// </summary>
        public static FileStream ExportOeffnen(string name, string endungOderLeer)
        {
            if (!ExportNameGueltig(name))
                throw new ArgumentException("Der Exportname „" + Massnahmenbau.Kurz(name) + "“ ist kein Name unter dem Exportordner (erlaubt: Buchstaben, Ziffern, Punkt, Unterstrich, Bindestrich).");
            if (!string.IsNullOrEmpty(endungOderLeer) && !name.EndsWith(endungOderLeer, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Der Export „" + name + "“ trägt nicht die Endung " + endungOderLeer + "; er wird nicht zurückgespielt.");
            string export = ExportOrdnerPfad();
            if (export == null)
                throw new ArgumentException("Der Sicherungsordner " + OrdnerPfad() + " liegt hinter einer Verknüpfung auf einen anderen Ort; es wird nichts gelesen.");
            string voll = Path.Combine(export, name);
            if (!File.Exists(voll)) throw new FileNotFoundException("Der Export " + name + " wurde nicht gefunden.", voll);

            FileAttributes attr;
            try { attr = File.GetAttributes(voll); }
            catch (Exception ex) { throw Ablehnung(name, "Attribute nicht lesbar: " + ex.Message, 0); }
            if ((attr & FileAttributes.ReparsePoint) != 0) throw Ablehnung(name, "die Datei ist eine Verknüpfung auf einen anderen Ort", 0);

            FileStream fs = new FileStream(voll, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                int namen;
                string fremd = HerkunftPruefen(fs, out namen);
                if (fremd != null) throw Ablehnung(name, fremd, namen);
            }
            catch (Exception)
            {
                fs.Dispose();
                throw;
            }
            return fs;
        }

        /// <summary>Der Pfad, auf den ein offener Stream wirklich zeigt (Junctions und kurze Namen aufgeloest), fuer netsh advfirewall import.</summary>
        public static string KanonischerPfad(FileStream fs)
        {
            if (fs == null) throw new ArgumentNullException("fs");
            return Native.Kanonisch(fs.SafeFileHandle);
        }

        // Ein Pfad zu einem Export: voller Pfad direkt unter export\ oder nur der Name. Liefert
        // den Namen; wirft InvalidOperationException, wenn Name oder Ort nicht stimmen.
        static string ExportPfadPruefen(string pfad, string wozu)
        {
            if (string.IsNullOrEmpty(pfad)) throw new InvalidOperationException("Kein Export angegeben; nichts wird " + wozu + ".");
            string name = Path.GetFileName(pfad);
            if (!ExportNameGueltig(name))
                throw new InvalidOperationException("Der Exportname „" + Massnahmenbau.Kurz(name) + "“ ist kein Name unter dem Exportordner (erlaubt: Buchstaben, Ziffern, Punkt, Unterstrich, Bindestrich).");
            string export = ExportOrdnerPfad();
            if (export == null)
                throw new InvalidOperationException("Der Sicherungsordner " + OrdnerPfad() + " liegt hinter einer Verknüpfung auf einen anderen Ort; nichts wird " + wozu + ".");
            if (Path.IsPathRooted(pfad) || pfad.IndexOf(Path.DirectorySeparatorChar) >= 0 || pfad.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                string gegeben;
                try { gegeben = Path.GetFullPath(pfad); }
                catch (Exception ex) { throw new InvalidOperationException("Der Exportpfad ist nicht lesbar: " + ex.Message, ex); }
                if (!string.Equals(gegeben.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.Combine(export, name)), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Der Export „" + name + "“ liegt nicht unter " + export + "; nichts wird " + wozu + ".");
            }
            return name;
        }

        // Der festgenagelte Sicherungsordner: ein offenes Handle, geprueft und in Helferbesitz.
        // Solange es offen ist, lassen sich die Eltern weder umbenennen noch loeschen (gemessen
        // 18.09.2026); der Ordner selbst schon, deshalb entsteht die Datei relativ zum Handle
        // (DateiSchreiben), nicht ueber den Pfad.
        sealed class Nagel : IDisposable
        {
            public SafeFileHandle Handle;
            public void Dispose() { if (Handle != null) Handle.Dispose(); }
        }

        /// <summary>
        /// Legt den Sicherungsordner an (anlegen, gleich mit der geschuetzten DACL) oder oeffnet
        /// ihn, prueft am Handle, dass er wirklich unter dem Laufzeitordner liegt (keine Junction
        /// auf irgendeiner Ebene darunter), bringt ihn in Helferbesitz und liefert das offene
        /// Handle (Nagel), das der Aufrufer bis nach dem Schreiben haelt. Besitzer Administratoren
        /// (oder SYSTEM) und die geschuetzte DACL werden nur gesetzt, wenn sie abweichen
        /// (idempotent), beides am Handle, beides danach nachgelesen. geaendert sagt, ob etwas
        /// gesetzt wurde. Scheitert Lesen, Setzen oder Nachlesen (nicht erhoeht, Junction) →
        /// InvalidOperationException, und die Massnahme laeuft nicht.
        ///
        /// Reihenfolge: erst der Besitzer, dann die DACL (Vertrag 8.10). Den Besitzer zu setzen
        /// braucht WRITE_OWNER; das traegt der erhoehte Helfer ueber SeTakeOwnershipPrivilege, das
        /// hier nur fuer diesen Block eingeschaltet wird und WRITE_OWNER auch ohne jeden Eintrag
        /// in der DACL gewaehrt. Ein Ordner, den ein anderes Konto angelegt und auf sich selbst
        /// beschraenkt hat (gemessen 18.09.2026: Besitzer Gaeste, DACL nur Gaeste), laesst sich
        /// mit den ueblichen Rechten nicht einmal oeffnen: CreateFile haengt an jede Anfrage
        /// SYNCHRONIZE und FILE_READ_ATTRIBUTES, und die verweigert die fremde DACL. Deshalb
        /// NtOpenFile mit exakt WRITE_OWNER; als Besitzer hat der Helfer dann READ_CONTROL und
        /// WRITE_DAC ohne Eintrag und setzt die DACL an einem zweiten Handle, waehrend das erste
        /// den Ordner haelt.
        /// </summary>
        static Nagel OrdnerSchuetzen(string ordner, bool anlegen, out bool geaendert)
        {
            geaendert = false;
            string erwartet = ErwarteterPfad(ordner);
            if (anlegen) OrdnerAnlegen(ordner);
            try
            {
                return new Nagel { Handle = Schuetzen(ordner, true, erwartet, out geaendert) };
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Der Sicherungsordner " + ordner + " ließ sich nicht gegen andere Konten schützen: " + ex.Message + "; es wird nichts gesichert und nichts geändert.", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Der Sicherungsordner " + ordner + " ließ sich nicht gegen andere Konten schützen (" + ex.GetType().Name + ": " + ex.Message + "); es wird nichts gesichert und nichts geändert.", ex);
            }
        }

        /// <summary>
        /// Der gemeinsame Kern fuer Ordner (OrdnerSchuetzen) und Exporte (ExportSchuetzen): oeffnet
        /// pfad mit exakt den noetigen Rechten, prueft am Handle, dass er nach erwartet fuehrt,
        /// bringt ihn in Helferbesitz und setzt die geschuetzte DACL (Ordner vererbend, Datei
        /// nicht), beides nur bei Abweichung, beides danach nachgelesen. Liefert das offene
        /// Handle; der Aufrufer gibt es frei. Wirft InvalidOperationException mit dem nackten
        /// Grund (der Aufrufer setzt den Satz davor).
        /// </summary>
        static SafeFileHandle Schuetzen(string pfad, bool ordner, string erwartet, out bool geaendert)
        {
            geaendert = false;
            SafeFileHandle h = null;
            try
            {
                using (var privileg = Privileg.Einschalten(Native.SeTakeOwnershipPrivilege))
                {
                    // Eine Datei braucht zusaetzlich FILE_READ_ATTRIBUTES fuer die Zahl ihrer Namen
                    // (GetFileInformationByHandle in ExportSchuetzen); der Ordner nicht.
                    uint rechte = Native.READ_CONTROL | Native.WRITE_DAC | (privileg.An ? Native.WRITE_OWNER : 0) | (ordner ? 0 : Native.FILE_READ_ATTRIBUTES);
                    int fehler;
                    h = Native.Oeffnen(pfad, rechte, ordner, out fehler);
                    if (h == null && fehler == Native.ERROR_ACCESS_DENIED && privileg.An)
                    {
                        // Fremder Besitzer ohne Eintrag fuer Administratoren: erst den Besitz
                        // uebernehmen (nur WRITE_OWNER, das gewaehrt das Privileg), dann als
                        // Besitzer erneut oeffnen. Das erste Handle bleibt bis dahin offen.
                        using (SafeFileHandle besitz = Native.Oeffnen(pfad, Native.WRITE_OWNER, ordner, out fehler))
                        {
                            if (besitz == null) throw new InvalidOperationException("er ließ sich nicht zum Besitzwechsel öffnen (Win32-Fehler " + fehler + ")");
                            IdentitaetPruefen(besitz, erwartet);
                            BesitzerSetzen(besitz);
                            geaendert = true;
                            h = Native.Oeffnen(pfad, rechte, ordner, out fehler);
                        }
                    }
                    if (h == null)
                        throw new InvalidOperationException("er ließ sich nicht öffnen (Win32-Fehler " + fehler
                            + (privileg.An ? ")" : "; das Konto ist nicht erhöht, SeTakeOwnershipPrivilege fehlt)"));

                    IdentitaetPruefen(h, erwartet);
                    DirectorySecurity ist = SicherheitLesen(h);
                    SecurityIdentifier besitzer = Besitzer(ist);
                    if (!BesitzerStimmt(besitzer))
                    {
                        if (!privileg.An)
                            throw new InvalidOperationException("der Besitzer ist " + (besitzer == null ? "nicht lesbar" : besitzer.Value)
                                + " statt Administratoren, und ohne Erhöhung lässt er sich nicht ändern");
                        BesitzerSetzen(h);
                        geaendert = true;
                    }
                    if (!DaclStimmt(ist, ordner))
                    {
                        DaclSetzen(h, ordner);
                        geaendert = true;
                    }

                    DirectorySecurity danach = SicherheitLesen(h);
                    SecurityIdentifier b = Besitzer(danach);
                    if (!BesitzerStimmt(b))
                        throw new InvalidOperationException("der Besitzer wurde nicht übernommen (" + (b == null ? "keiner lesbar" : b.Value) + " statt Administratoren)");
                    if (!DaclStimmt(danach, ordner))
                        throw new InvalidOperationException("die Rechte wurden nicht übernommen (" + DaclKurz(danach) + ")");
                }
                SafeFileHandle fertig = h;
                h = null;
                return fertig;
            }
            finally
            {
                if (h != null) h.Dispose();
            }
        }

        // Legt den Ordner an, falls er fehlt: die Eltern (sicherungen\) wie bisher mit geerbten
        // Rechten, der Ordner selbst gleich mit der geschuetzten DACL als Sicherheitsattribut, damit
        // er keinen Augenblick mit Aenderungsrechten fuer Benutzer existiert. Ein Ordner, der
        // inzwischen da ist (auch eine untergeschobene Junction), wird hier nicht angefasst; ob er
        // der richtige ist, entscheidet danach IdentitaetPruefen am Handle.
        static void OrdnerAnlegen(string ordner)
        {
            try
            {
                if (Directory.Exists(ordner)) return;
                string eltern = Path.GetDirectoryName(ordner);
                if (!string.IsNullOrEmpty(eltern)) Directory.CreateDirectory(eltern);
                Directory.CreateDirectory(ordner, (DirectorySecurity)GeschuetzteDacl(true));
            }
            catch (Exception ex) { throw new InvalidOperationException("Der Sicherungsordner " + ordner + " ließ sich nicht anlegen: " + ex.Message, ex); }
        }

        /// <summary>
        /// Der Pfad, den Windows fuer den Sicherungsordner nennen muss, wenn keine Junction im
        /// Spiel ist: der kanonische Pfad des Laufzeitordners (am Handle gelesen, damit kurze 8.3-
        /// Namen und Laufwerkszuordnungen auf beiden Seiten gleich aufgeloest sind) plus der Rest
        /// darunter. Fuer einen Probenordner gilt sein Elternordner als Wurzel. Der Laufzeitordner
        /// selbst bleibt ungeprueft, wie bei Ablage.IstAbzweigung.
        /// </summary>
        static string ErwarteterPfad(string ordner)
        {
            string voll = Path.GetFullPath(ordner).TrimEnd(Path.DirectorySeparatorChar);
            string wurzel = string.IsNullOrEmpty(OrdnerFuerProbe) ? Ablage.Maschinenweit() : Path.GetDirectoryName(voll);
            if (string.IsNullOrEmpty(wurzel)) throw new InvalidOperationException("Der Sicherungsordner " + ordner + " hat keinen Elternordner.");
            wurzel = Path.GetFullPath(wurzel).TrimEnd(Path.DirectorySeparatorChar);
            if (!voll.StartsWith(wurzel + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Der Sicherungsordner " + ordner + " liegt nicht unter " + wurzel + ".");
            int fehler;
            using (SafeFileHandle hw = Native.OrdnerOeffnen(wurzel, Native.FILE_READ_ATTRIBUTES, out fehler))
            {
                if (hw == null) throw new InvalidOperationException("Der Laufzeitordner " + wurzel + " ließ sich nicht öffnen (Win32-Fehler " + fehler + ").");
                return Native.Kanonisch(hw) + voll.Substring(wurzel.Length);
            }
        }

        // Fuehrt das Handle wirklich dorthin, wo der Ordner liegen muss? Eine Junction auf einer
        // Ebene unter dem Laufzeitordner (der Ordner selbst oder sicherungen\) laesst Windows am
        // Handle das Ziel nennen, nicht den Pfad, ueber den geoeffnet wurde.
        static void IdentitaetPruefen(SafeFileHandle h, string erwartet)
        {
            string ist = Native.Kanonisch(h);
            if (!string.Equals(ist, erwartet, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("der Pfad führt über eine Verknüpfung nach " + ist + " statt " + erwartet);
        }

        // Besitzer und DACL am Handle lesen (GetSecurityInfo), als DirectorySecurity, damit
        // DaclStimmt und Besitzer dieselben bleiben wie fuer Dateien.
        static DirectorySecurity SicherheitLesen(SafeFileHandle h)
        {
            var am = new HandleSicherheit(h, AccessControlSections.Owner | AccessControlSections.Access);
            var ds = new DirectorySecurity();
            ds.SetSecurityDescriptorBinaryForm(am.GetSecurityDescriptorBinaryForm(), AccessControlSections.Owner | AccessControlSections.Access);
            return ds;
        }

        static void BesitzerSetzen(SafeFileHandle h)
        {
            var am = new HandleSicherheit();
            am.SetOwner(SidAdministratoren);
            am.Speichern(h, AccessControlSections.Owner);
        }

        // Die geschuetzte DACL am Handle setzen: fuer einen Ordner vererbend (die Eintraege gelten
        // dann auch fuer alles, was ein Fremdprozess darunter schon angelegt hat: SetSecurityInfo
        // traegt vererbbare Eintraege in den Teilbaum), fuer eine Datei ohne Vererbungsmarken.
        static void DaclSetzen(SafeFileHandle h, bool ordner)
        {
            var am = new HandleSicherheit();
            am.SetSecurityDescriptorBinaryForm(GeschuetzteDacl(ordner).GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            am.Speichern(h, AccessControlSections.Access);
        }

        /// <summary>
        /// Schreibt text als Datei name IN den festgenagelten Ordner (relativ zum Handle, mit der
        /// geschuetzten DACL als Sicherheitsbeschreibung, nie ueberschreiben) und prueft die
        /// Herkunft am noch offenen Handle wie beim Lesen (Besitzer, genau die DACL, ein Name).
        /// true = geschrieben; false = der Name ist schon vergeben (der Aufrufer nimmt den
        /// naechsten). Stimmt die Herkunft nicht (nicht erhoeht: Besitzer ist das Konto), wird die
        /// Datei beim Schliessen des Handles geloescht, und InvalidOperationException sagt es.
        /// </summary>
        static bool DateiSchreiben(Nagel nagel, string name, string text)
        {
            SafeFileHandle h;
            // Besitzer ausdruecklich in die Sicherheitsbeschreibung: sonst nimmt Windows den
            // Standardbesitzer des Tokens, und der ist nicht in jeder erhoehten Sitzung die
            // Administratorengruppe (gemessen 18.09.2026: eine aus Git-Bash erhoehte Shell trug das
            // Konto als TokenOwner, und Schreiben verwarf jede eigene Datei als fremd). Ein erhoehtes
            // Token darf Administratoren als Besitzer eintragen (SE_GROUP_OWNER); ein gefiltertes
            // Token nicht, dann scheitert NtCreateFile mit STATUS_INVALID_OWNER, und das ist der
            // gewollte fail-closed-Ausgang (nicht erhoeht wird nichts gesichert).
            FileSystemSecurity sd = GeschuetzteDacl(false);
            sd.SetOwner(SidAdministratoren);
            try { h = Native.DateiAnlegen(nagel.Handle, name, sd.GetSecurityDescriptorBinaryForm()); }
            catch (Exception ex) { throw new InvalidOperationException("Die Sicherung " + name + " ließ sich nicht schreiben: " + ex.Message, ex); }
            if (h == null) return false;

            string grund = null;
            FileStream fs = null;
            try
            {
                fs = new FileStream(h, FileAccess.Write, 4096, false);
                var w = new StreamWriter(fs, new UTF8Encoding(false));
                w.Write(text);
                w.Flush();
                int namen;
                grund = HerkunftPruefen(fs, out namen);
            }
            catch (Exception ex) { grund = ex.GetType().Name + ": " + ex.Message; }
            // Das Handle ist noch offen (fs besitzt es, solange fs nicht geschlossen ist): eine
            // Datei, die nicht als Sicherung gilt, verschwindet mit dem Schliessen.
            if (grund != null && !Native.LoeschenBeimSchliessen(h))
                AppLog.Warn("Sicherung " + name + " ohne geschützte Herkunft ließ sich nicht löschen.");
            if (fs != null) fs.Dispose(); else h.Dispose();
            if (grund != null)
                throw new InvalidOperationException("Die Sicherung " + name + " ließ sich nicht gegen andere Konten schützen: " + grund + "; sie wurde verworfen, es wird nichts geändert.");
            return true;
        }

        /// <summary>
        /// Sicherheitsbeschreibung an einem offenen Handle statt an einem Pfad: NativeObjectSecurity
        /// liest sie im Konstruktor ueber GetSecurityInfo und schreibt sie in Speichern ueber
        /// SetSecurityInfo, beides am Handle (DirectoryInfo.SetAccessControl ginge ueber
        /// SetNamedSecurityInfo am Pfad und folgte einer Junction). Regeln werden hier nie
        /// aufgezaehlt; wer sie braucht, wandelt die Binaerform in eine DirectorySecurity
        /// (SicherheitLesen), deshalb liefern die Fabriken nichts.
        /// </summary>
        sealed class HandleSicherheit : NativeObjectSecurity
        {
            public HandleSicherheit(SafeFileHandle h, AccessControlSections teile) : base(true, ResourceType.FileObject, h, teile) { }
            public HandleSicherheit() : base(true, ResourceType.FileObject) { }
            public override Type AccessRightType { get { return typeof(FileSystemRights); } }
            public override Type AccessRuleType { get { return typeof(FileSystemAccessRule); } }
            public override Type AuditRuleType { get { return typeof(FileSystemAuditRule); } }
            public override AccessRule AccessRuleFactory(IdentityReference id, int rechte, bool geerbt, InheritanceFlags erben, PropagationFlags weitergabe, AccessControlType art)
            {
                throw new NotSupportedException("Regeln werden ueber SicherheitLesen als DirectorySecurity gelesen.");
            }
            public override AuditRule AuditRuleFactory(IdentityReference id, int rechte, bool geerbt, InheritanceFlags erben, PropagationFlags weitergabe, AuditFlags marken)
            {
                throw new NotSupportedException("Ueberwachungsregeln werden hier nicht gelesen.");
            }
            public void Speichern(SafeFileHandle h, AccessControlSections teile) { Persist(h, teile); }
        }

        /// <summary>
        /// Schaltet ein Privileg des eigenen Tokens fuer einen Block ein (AdjustTokenPrivileges)
        /// und stellt beim Verlassen den alten Zustand wieder her. An = false, wenn das Token es
        /// nicht traegt (nicht erhoeht): dann laeuft der Block ohne, und die Stelle, die es
        /// gebraucht haette, meldet den Fehler. Ein Thread mit Identitaetswechsel bekommt sein
        /// eigenes Token angepasst, sonst das des Prozesses. Wirft nie. Oeffentlich, weil
        /// System.cs (wiederherstellungspunkt.zurueck) SeShutdownPrivilege denselben Weg braucht.
        /// </summary>
        public sealed class Privileg : IDisposable
        {
            public bool An;
            string _name;
            bool _zurueck;

            public static Privileg Einschalten(string name)
            {
                bool warAn;
                bool ok = Native.PrivilegSetzen(name, true, out warAn);
                return new Privileg { An = ok, _name = name, _zurueck = ok && !warAn };
            }

            public void Dispose()
            {
                if (!_zurueck) return;
                _zurueck = false;
                bool egal;
                Native.PrivilegSetzen(_name, false, out egal);
            }
        }

        /// <summary>Die Eintraege einer DACL als Text fuer eine Fehlermeldung: "S-1-5-32-545 Modify, ...".</summary>
        static string DaclKurz(FileSystemSecurity sec)
        {
            if (sec == null) return "keine DACL";
            var teile = new List<string>();
            try
            {
                foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    teile.Add(r.IdentityReference.Value + " " + r.AccessControlType + " " + r.FileSystemRights + (r.IsInherited ? " geerbt" : ""));
            }
            catch (Exception ex) { teile.Add("nicht lesbar: " + ex.Message); }
            return (sec.AreAccessRulesProtected ? "Vererbung aus; " : "Vererbung an; ") + (teile.Count == 0 ? "kein Eintrag" : string.Join(", ", teile));
        }

        /// <summary>
        /// null, wenn die GEOEFFNETE Datei vom erhoehten Helfer stammt; sonst der Grund fuer app.log.
        /// namen: die Zahl der Namen der Datei, wenn sie nicht 1 ist (dann ist das der Grund), sonst 0.
        /// Alles, was sich nicht lesen laesst, ist ein Grund. Eine Funktion fuer JSON (Lesen,
        /// DateiSchreiben) und Export (ExportOeffnen), Vertrag M4 3.2; oeffentlich fuer die HelferProbe.
        ///
        /// Geprueft wird am offenen Handle, nicht am Pfad, aus drei Gruenden (Vertrag 8.10):
        /// 1. Check-then-read: eine Pruefung ueber den Pfad und ein zweites Oeffnen zum Lesen sind
        ///    zwei Oeffnungen; zwischen beiden kann ein anderer Prozess die Datei austauschen. Am
        ///    Handle gilt die Pruefung fuer genau die Bytes, die danach aus demselben Handle kommen.
        /// 2. Umbenennen im Wechsel: solange das Handle offen ist (FileShare.Read), kann niemand
        ///    die Datei loeschen oder umbenennen und eine andere unter den Namen schieben.
        /// 3. Hardlink: ein zweiter Name auf dieselbe Datei traegt kein ReparsePoint-Attribut und
        ///    zeigt Besitzer und DACL des Ziels; die Zahl der Namen (nNumberOfLinks) gibt es nur
        ///    ueber GetFileInformationByHandle. Ein Nutzer kann so zwar nur auf eine Datei
        ///    verweisen, die er lesen darf, aber die Sicherung muss genau dort liegen und genau
        ///    einen Namen haben; alles andere ist nicht die Datei, die der Helfer geschrieben hat.
        /// Der Reparse-Punkt wird VOR dem Oeffnen am Pfad abgelehnt (Lesen): FileStream folgt ihm,
        /// und das Handle zeigte dann auf das Ziel.
        /// </summary>
        public static string HerkunftPruefen(FileStream fs, out int namen)
        {
            namen = 0;
            FileSecurity sec;
            try { sec = fs.GetAccessControl(); }
            catch (Exception ex) { return "Besitzer und Rechte nicht lesbar: " + ex.Message; }

            SecurityIdentifier besitzer;
            try { besitzer = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier; }
            catch (Exception ex) { return "Besitzer nicht lesbar: " + ex.Message; }
            if (besitzer == null) return "kein Besitzer eingetragen";
            if (!BesitzerStimmt(besitzer)) return "Besitzer " + besitzer.Value + " statt Administratoren oder SYSTEM";

            bool dacl;
            try { dacl = DaclStimmt(sec, false); }
            catch (Exception ex) { return "Rechte nicht lesbar: " + ex.Message; }
            if (!dacl) return "die Rechte sind nicht genau die geschützten (" + DaclKurz(sec) + ")";

            Native.BY_HANDLE_FILE_INFORMATION info;
            if (!Native.GetFileInformationByHandle(fs.SafeFileHandle, out info))
                return "Zahl der Namen nicht lesbar (Win32-Fehler " + Marshal.GetLastWin32Error() + ")";
            if (info.nNumberOfLinks != 1)
            {
                namen = info.nNumberOfLinks > int.MaxValue ? int.MaxValue : (int)info.nNumberOfLinks;
                return namen + " Namen zeigen auf dieselbe Datei (Hardlink)";
            }
            return null;
        }

        // Schreibt den Grund ins app.log und baut die Ausnahme fuer den Aufrufer (Vertrag 8.2, 8.10):
        // bei mehr als einem Namen die Zahl, sonst der feste Satz ohne Einzelheiten (die stehen im app.log).
        static ArgumentException Ablehnung(string name, string grund, int namen)
        {
            AppLog.Warn("Sicherung " + name + " abgelehnt: " + grund);
            if (namen > 1)
                return new ArgumentException("Die Sicherung „" + name + "“ hat mehr als einen Namen (" + namen + " Einträge zeigen auf dieselbe Datei); sie wird nicht zurückgespielt.");
            return new ArgumentException("Die Sicherung „" + name + "“ stammt nicht vom Helfer (Besitzer/Rechte); sie wird nicht zurückgespielt.");
        }

        /// <summary>
        /// Liest eine Sicherung. pfad ist ein Dateiname unter dem Sicherungsordner oder ein voller
        /// Pfad, der dort liegen muss (Name nach NameGueltig, keine Pfadbestandteile ausserhalb):
        /// ein erhoehter Helfer liest sonst jede Datei, die die Oberflaeche ihm nennt. Dazu die
        /// Herkunft (Vertrag 8.2, 8.10): kein Reparse-Punkt am Pfad; dann wird die Datei genau
        /// einmal geoeffnet, und an diesem Handle gelten Besitzer Administratoren oder SYSTEM,
        /// genau die geschuetzte DACL und genau ein Name; der Inhalt kommt aus demselben Handle.
        /// Wirft ArgumentException (Name, Ort oder Herkunft), FileNotFoundException, sonst die Leseausnahme.
        /// </summary>
        public static Sicherung Lesen(string pfad)
        {
            if (string.IsNullOrEmpty(pfad)) throw new ArgumentException("Kein Sicherungspfad angegeben.");
            string name = Path.GetFileName(pfad);
            if (!NameGueltig(name))
                throw new ArgumentException("Der Dateiname „" + name + "“ ist keine Sicherungsdatei (erlaubt: Buchstaben, Ziffern, Punkt, Unterstrich, Bindestrich, Endung .json).");
            string ordner = Ordner();
            if (string.IsNullOrEmpty(ordner))
                throw new ArgumentException("Der Sicherungsordner " + OrdnerPfad() + " liegt hinter einer Verknüpfung auf einen anderen Ort; es wird nichts gelesen.");
            string voll = Path.GetFullPath(Path.Combine(ordner, name));
            if (Path.IsPathRooted(pfad) || pfad.IndexOf(Path.DirectorySeparatorChar) >= 0 || pfad.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                // Ein voller Pfad muss auf dieselbe Datei zeigen wie Ordner + Name.
                string gegeben;
                try { gegeben = Path.GetFullPath(pfad); }
                catch (Exception ex) { throw new ArgumentException("Der Sicherungspfad ist nicht lesbar: " + ex.Message, ex); }
                if (!string.Equals(gegeben, voll, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Die Sicherung „" + name + "“ liegt nicht unter " + ordner + ".");
            }
            if (!File.Exists(voll)) throw new FileNotFoundException("Die Sicherung " + name + " wurde nicht gefunden.", voll);

            // Reparse-Punkt am Pfad, vor dem Oeffnen (siehe HerkunftPruefen).
            FileAttributes attr;
            try { attr = File.GetAttributes(voll); }
            catch (Exception ex) { throw Ablehnung(name, "Attribute nicht lesbar: " + ex.Message, 0); }
            if ((attr & FileAttributes.ReparsePoint) != 0) throw Ablehnung(name, "die Datei ist eine Verknüpfung auf einen anderen Ort", 0);

            // Genau eine Oeffnung: Herkunft und Inhalt aus demselben Handle.
            string text;
            using (var fs = new FileStream(voll, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int namen;
                string fremd = HerkunftPruefen(fs, out namen);
                if (fremd != null) throw Ablehnung(name, fremd, namen);
                using (var r = new StreamReader(fs, new UTF8Encoding(false), true))
                    text = r.ReadToEnd();
            }
            Sicherung s = Json.Lesen<Sicherung>(text);
            if (s == null || string.IsNullOrEmpty(s.Kennung)) throw new InvalidDataException("Die Sicherung " + name + " nennt keine Kennung.");
            if (s.Parameter == null) s.Parameter = new Dictionary<string, string>();
            if (s.Vorher == null) s.Vorher = new Dictionary<string, string>();
            return s;
        }

        /// <summary>Woerterbuch aus Paaren: Werte("a", "1", "b", "2"). null-Werte werden zu "".</summary>
        public static Dictionary<string, string> Werte(params string[] paare)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (paare == null) return d;
            for (int i = 0; i + 1 < paare.Length; i += 2) d[paare[i]] = paare[i + 1] ?? "";
            return d;
        }

        /// <summary>Die Kennung wird Teil des Dateinamens: nur Kleinbuchstaben, Ziffern und Punkt, sonst ArgumentException.</summary>
        static void KennungPruefen(string kennung)
        {
            if (string.IsNullOrEmpty(kennung)) throw new ArgumentException("Sicherung ohne Kennung.");
            if (kennung.Length > 80 || !KennungRx.IsMatch(kennung))
                throw new ArgumentException("Die Kennung „" + Massnahmenbau.Kurz(kennung) + "“ taugt nicht als Dateiname (erlaubt: Kleinbuchstaben, Ziffern, Punkt; höchstens 80 Zeichen).");
        }

        static string NeuerName(string ordner, string kennung)
        {
            KennungPruefen(kennung);
            return Path.Combine(ordner, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                                        + "-" + kennung + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) + ".json");
        }

        // Im Programm entscheidet Ablage.IstAbzweigung (jede Ebene bis zum maschinenweiten Ordner);
        // fuer den Wegwerf-Ordner einer Probe nur das Attribut des Ordners selbst. Wirft die
        // Pruefung, gilt "ja": was sich nicht pruefen laesst, bekommt keinen erhoehten Schreibzugriff.
        static bool OrdnerIstAbzweigung(string ordner)
        {
            try
            {
                if (!string.IsNullOrEmpty(OrdnerFuerProbe))
                    return Directory.Exists(ordner) && (File.GetAttributes(ordner) & FileAttributes.ReparsePoint) != 0;
                return Ablage.IstAbzweigung(ordner);
            }
            catch (Exception) { return true; }
        }

        static string Kurz(Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var p in d)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p.Key).Append('=').Append(p.Value);
                if (sb.Length > 200) { sb.Append('…'); break; }
            }
            return sb.Length == 0 ? "(keine Werte)" : sb.ToString();
        }

        // ------------------------------------------------------------------ P/Invoke

        /// <summary>
        /// Die Windows-Aufrufe, die es in .NET Framework 4.8 nicht am Handle gibt.
        ///
        /// fileapi.h: BOOL GetFileInformationByHandle(HANDLE, LPBY_HANDLE_FILE_INFORMATION). Liefert
        /// zur geoeffneten Datei unter anderem nNumberOfLinks, die Zahl ihrer Namen (Hardlinks);
        /// ueber den Pfad gibt es diese Zahl nicht. Kernel32, braucht nur ein Lesehandle.
        /// FILETIME bleibt das 8-Byte-Paar aus zwei Ganzzahlen (4-Byte-Ausrichtung); ein long
        /// verschoebe die Felder dahinter um 4 Byte.
        ///
        /// winternl.h: NtOpenFile und NtCreateFile (ntdll) statt CreateFile, aus zwei Gruenden:
        /// CreateFile haengt an jede Anfrage SYNCHRONIZE und FILE_READ_ATTRIBUTES, und ein
        /// fremder Ordner, dessen DACL beides verweigert, laesst sich damit auch mit
        /// SeTakeOwnershipPrivilege nicht oeffnen (gemessen 18.09.2026: Win32-Fehler 5, ueber
        /// NtOpenFile mit exakt WRITE_OWNER geoeffnet); und nur NtCreateFile legt eine Datei
        /// RELATIV zu einem Ordner-Handle an (OBJECT_ATTRIBUTES.RootDirectory), also sicher in
        /// dem Ordner, der geprueft wurde. GetFinalPathNameByHandle nennt den Pfad, auf den ein
        /// Handle wirklich zeigt (Junctions aufgeloest). AdjustTokenPrivileges schaltet das
        /// Privileg fuer den Besitzwechsel ein und wieder aus.
        /// </summary>
        static class Native
        {
            public const uint READ_CONTROL = 0x00020000, WRITE_DAC = 0x00040000, WRITE_OWNER = 0x00080000;
            public const uint FILE_READ_ATTRIBUTES = 0x00000080;
            const uint DELETE = 0x00010000, SYNCHRONIZE = 0x00100000, FILE_GENERIC_WRITE = 0x00120116;
            const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
            const uint FILE_DIRECTORY_FILE = 0x00000001, FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020, FILE_NON_DIRECTORY_FILE = 0x00000040, FILE_OPEN_FOR_BACKUP_INTENT = 0x00004000;
            const uint FILE_CREATE = 2;
            const uint FILE_ATTRIBUTE_NORMAL = 0x80;
            const uint OBJ_CASE_INSENSITIVE = 0x40;
            const int STATUS_OBJECT_NAME_COLLISION = unchecked((int)0xC0000035);
            const int FileDispositionInformation = 13;
            public const int ERROR_ACCESS_DENIED = 5, ERROR_NO_TOKEN = 1008, ERROR_NOT_ALL_ASSIGNED = 1300;
            const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x08, SE_PRIVILEGE_ENABLED = 0x02;
            public const string SeTakeOwnershipPrivilege = "SeTakeOwnershipPrivilege";

            [StructLayout(LayoutKind.Sequential)]
            public struct BY_HANDLE_FILE_INFORMATION
            {
                public uint dwFileAttributes;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
                public uint dwVolumeSerialNumber;
                public uint nFileSizeHigh;
                public uint nFileSizeLow;
                public uint nNumberOfLinks;
                public uint nFileIndexHigh;
                public uint nFileIndexLow;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct UNICODE_STRING { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

            [StructLayout(LayoutKind.Sequential)]
            struct OBJECT_ATTRIBUTES
            {
                public int Length;
                public IntPtr RootDirectory;
                public IntPtr ObjectName;
                public uint Attributes;
                public IntPtr SecurityDescriptor;
                public IntPtr SecurityQualityOfService;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct IO_STATUS_BLOCK { public IntPtr Status; public IntPtr Information; }

            // LUID (8 Byte) und LUID_AND_ATTRIBUTES (12 Byte) liegen 4-Byte-ausgerichtet hinter
            // PrivilegeCount: 16 Byte fuer genau ein Privileg, wie im Windows-Header.
            [StructLayout(LayoutKind.Sequential)]
            struct LUID { public uint LowPart; public int HighPart; }

            [StructLayout(LayoutKind.Sequential)]
            struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

            [DllImport("ntdll.dll")]
            static extern int NtOpenFile(out IntPtr FileHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, out IO_STATUS_BLOCK IoStatusBlock, uint ShareAccess, uint OpenOptions);

            [DllImport("ntdll.dll")]
            static extern int NtCreateFile(out IntPtr FileHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, out IO_STATUS_BLOCK IoStatusBlock,
                                           IntPtr AllocationSize, uint FileAttributes, uint ShareAccess, uint CreateDisposition, uint CreateOptions, IntPtr EaBuffer, uint EaLength);

            [DllImport("ntdll.dll")]
            static extern int NtSetInformationFile(SafeFileHandle FileHandle, out IO_STATUS_BLOCK IoStatusBlock, ref byte FileInformation, uint Length, int FileInformationClass);

            [DllImport("ntdll.dll")]
            static extern int RtlNtStatusToDosError(int Status);

            [DllImport("kernel32.dll")]
            static extern IntPtr GetCurrentProcess();

            [DllImport("kernel32.dll")]
            static extern IntPtr GetCurrentThread();

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool CloseHandle(IntPtr hObject);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool OpenThreadToken(IntPtr ThreadHandle, uint DesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool OpenAsSelf, out IntPtr TokenHandle);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool LookupPrivilegeValueW(string lpSystemName, string lpName, out LUID lpLuid);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
                                                     ref TOKEN_PRIVILEGES NewState, uint BufferLength, ref TOKEN_PRIVILEGES PreviousState, out uint ReturnLength);

            // Name und Sicherheitsbeschreibung eines Objekts im unverwalteten Speicher, solange
            // der NT-Aufruf laeuft; wurzel (RootDirectory) macht den Namen relativ zu einem Ordner-Handle.
            sealed class Objekt : IDisposable
            {
                public OBJECT_ATTRIBUTES Oa;
                readonly IntPtr _puffer, _name, _sd;
                readonly SafeFileHandle _wurzel;
                readonly bool _wurzelGehalten;

                public Objekt(string name, SafeFileHandle wurzel, byte[] sd)
                {
                    _puffer = Marshal.StringToHGlobalUni(name);
                    var us = new UNICODE_STRING { Length = (ushort)(name.Length * 2), MaximumLength = (ushort)(name.Length * 2 + 2), Buffer = _puffer };
                    _name = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UNICODE_STRING)));
                    Marshal.StructureToPtr(us, _name, false);
                    if (sd != null)
                    {
                        _sd = Marshal.AllocHGlobal(sd.Length);
                        Marshal.Copy(sd, 0, _sd, sd.Length);
                    }
                    if (wurzel != null)
                    {
                        _wurzel = wurzel;
                        wurzel.DangerousAddRef(ref _wurzelGehalten);
                    }
                    Oa = new OBJECT_ATTRIBUTES
                    {
                        Length = Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES)),
                        RootDirectory = _wurzelGehalten ? wurzel.DangerousGetHandle() : IntPtr.Zero,
                        ObjectName = _name,
                        Attributes = OBJ_CASE_INSENSITIVE,
                        SecurityDescriptor = _sd,
                    };
                }

                public void Dispose()
                {
                    if (_wurzelGehalten) _wurzel.DangerousRelease();
                    if (_sd != IntPtr.Zero) Marshal.FreeHGlobal(_sd);
                    Marshal.FreeHGlobal(_name);
                    Marshal.FreeHGlobal(_puffer);
                }
            }

            // Ein voller Pfad als NT-Pfad: C:\x → \??\C:\x, \\server\share\x → \??\UNC\server\share\x.
            // NtOpenFile normalisiert nichts, deshalb vorher Path.GetFullPath; ein Laufwerkswurzel
            // ("C:\") behaelt ihren Trenner, alles andere gibt ihn ab.
            static string NtPfad(string pfad)
            {
                string p = Path.GetFullPath(pfad);
                if (p.Length > 3) p = p.TrimEnd(Path.DirectorySeparatorChar);
                if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
                else if (p.StartsWith(@"\\", StringComparison.Ordinal)) p = "UNC" + p.Substring(1);
                return @"\??\" + p;
            }

            /// <summary>
            /// Oeffnet den Ordner mit genau den verlangten Rechten (Junctions werden gefolgt; ob das
            /// Ziel stimmt, sagt Kanonisch). null und der Win32-Fehler, wenn es nicht geht.
            /// </summary>
            public static SafeFileHandle OrdnerOeffnen(string pfad, uint rechte, out int win32)
            {
                return Oeffnen(pfad, rechte, true, out win32);
            }

            /// <summary>
            /// Wie OrdnerOeffnen, wahlweise fuer eine Datei (ordner = false: FILE_NON_DIRECTORY_FILE,
            /// ein Ordner unter dem Namen wird abgelehnt) oder einen Ordner (FILE_DIRECTORY_FILE).
            /// Nur fuer Besitzer und Rechte gedacht: kein SYNCHRONIZE, kein Lesen, kein Schreiben.
            /// </summary>
            public static SafeFileHandle Oeffnen(string pfad, uint rechte, bool ordner, out int win32)
            {
                using (var o = new Objekt(NtPfad(pfad), null, null))
                {
                    IntPtr h;
                    IO_STATUS_BLOCK iosb;
                    uint optionen = (ordner ? FILE_DIRECTORY_FILE : FILE_NON_DIRECTORY_FILE) | FILE_OPEN_FOR_BACKUP_INTENT;
                    int status = NtOpenFile(out h, rechte, ref o.Oa, out iosb, FILE_SHARE_READ | FILE_SHARE_WRITE, optionen);
                    if (status < 0) { win32 = RtlNtStatusToDosError(status); return null; }
                    win32 = 0;
                    return new SafeFileHandle(h, true);
                }
            }

            /// <summary>
            /// Legt die Datei name IM Ordner hinter dem Handle an (FILE_CREATE: nie eine vorhandene
            /// oeffnen), mit sd als Sicherheitsbeschreibung, zum synchronen Schreiben, mit
            /// READ_CONTROL und FILE_READ_ATTRIBUTES fuer die Herkunftspruefung und DELETE fuer das
            /// Verwerfen. null, wenn der Name vergeben ist; sonst IOException mit dem Fehler.
            /// </summary>
            public static SafeFileHandle DateiAnlegen(SafeFileHandle ordner, string name, byte[] sd)
            {
                using (var o = new Objekt(name, ordner, sd))
                {
                    IntPtr h;
                    IO_STATUS_BLOCK iosb;
                    int status = NtCreateFile(out h, FILE_GENERIC_WRITE | READ_CONTROL | FILE_READ_ATTRIBUTES | DELETE | SYNCHRONIZE, ref o.Oa, out iosb,
                                              IntPtr.Zero, FILE_ATTRIBUTE_NORMAL, 0, FILE_CREATE, FILE_NON_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT, IntPtr.Zero, 0);
                    if (status == STATUS_OBJECT_NAME_COLLISION) return null;
                    if (status < 0) throw new IOException("Win32-Fehler " + RtlNtStatusToDosError(status) + " (Status 0x" + status.ToString("X8") + ")");
                    return new SafeFileHandle(h, true);
                }
            }

            /// <summary>Markiert die offene Datei zum Loeschen beim Schliessen (braucht DELETE am Handle). false, wenn Windows das ablehnt.</summary>
            public static bool LoeschenBeimSchliessen(SafeFileHandle datei)
            {
                byte ja = 1;
                IO_STATUS_BLOCK iosb;
                return NtSetInformationFile(datei, out iosb, ref ja, 1, FileDispositionInformation) >= 0;
            }

            /// <summary>
            /// Der Pfad, auf den das Handle wirklich zeigt (GetFinalPathNameByHandle, Junctions und
            /// kurze Namen aufgeloest), ohne das Praefix \\?\ bzw. mit \\ statt \\?\UNC\. Wirft, wenn
            /// Windows ihn nicht nennt.
            /// </summary>
            public static string Kanonisch(SafeFileHandle h)
            {
                var sb = new StringBuilder(1024);
                uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n >= sb.Capacity)
                {
                    sb = new StringBuilder((int)n + 1);
                    n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                }
                if (n == 0 || n >= sb.Capacity)
                    throw new InvalidOperationException("der Pfad am Handle ist nicht lesbar (Win32-Fehler " + Marshal.GetLastWin32Error() + ")");
                string p = sb.ToString();
                if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + p.Substring(8);
                if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) return p.Substring(4);
                return p;
            }

            /// <summary>
            /// Schaltet das Privileg im eigenen Token ein oder aus. true, wenn es danach so steht;
            /// false, wenn das Token es nicht traegt oder der Aufruf scheitert. warAn: der Zustand
            /// davor. Ein Thread im Identitaetswechsel bekommt sein eigenes Token angepasst.
            /// </summary>
            public static bool PrivilegSetzen(string name, bool an, out bool warAn)
            {
                warAn = false;
                IntPtr token;
                if (!OpenThreadToken(GetCurrentThread(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, true, out token))
                {
                    if (Marshal.GetLastWin32Error() != ERROR_NO_TOKEN) return false;
                    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return false;
                }
                try
                {
                    LUID luid;
                    if (!LookupPrivilegeValueW(null, name, out luid)) return false;
                    var neu = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = an ? SE_PRIVILEGE_ENABLED : 0 };
                    var alt = new TOKEN_PRIVILEGES();
                    uint laenge;
                    if (!AdjustTokenPrivileges(token, false, ref neu, (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), ref alt, out laenge)) return false;
                    // Die Funktion meldet Erfolg auch, wenn das Privileg gar nicht im Token ist;
                    // erst der letzte Fehler sagt es (ERROR_NOT_ALL_ASSIGNED).
                    if (Marshal.GetLastWin32Error() == ERROR_NOT_ALL_ASSIGNED) return false;
                    // Kein Eintrag im alten Zustand heisst: es stand schon so.
                    warAn = alt.PrivilegeCount == 0 ? an : (alt.Attributes & SE_PRIVILEGE_ENABLED) != 0;
                    return true;
                }
                finally { CloseHandle(token); }
            }
        }
    }

    /// <summary>
    /// Laesst eine Funktion auf einem eigenen Thread mit Zeitbudget laufen (Muster aus
    /// Sammler.Quellen.Wmi.Abfrage und Katalog.PunktAnlegen): WMI-Provider und COM-Dienste
    /// koennen haengen, und ein Helfer, der nie zurueckkommt, hielte den Host fest. Nach Ablauf
    /// wird der Thread aufgegeben (Hintergrund-Thread) und eine TimeoutException geworfen.
    /// </summary>
    public static class Zeitgrenze
    {
        public static T Mit<T>(Func<T> f, int zeitMs, string was)
        {
            T ergebnis = default(T);
            Exception fehler = null;
            var t = new Thread(() =>
            {
                try { ergebnis = f(); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "zeitgrenze:" + (was ?? "").Substring(0, Math.Min(40, (was ?? "").Length)) };
            t.Start();
            if (!t.Join(zeitMs))
                throw new TimeoutException((was ?? "Der Aufruf") + " überschritt " + (zeitMs >= 60000 ? (zeitMs / 60000) + " Minuten" : (zeitMs / 1000) + " Sekunden") + ".");
            if (fehler != null) throw fehler;
            return ergebnis;
        }
    }

    /// <summary>
    /// Laesst ein Werkzeug ueber Werkzeuge.Lauf laufen und sammelt seine Ausgabezeilen (Art
    /// "normal", die Zeilen des Prozesses) als Text, statt sie dem Aufrufer zu zeigen: fuer
    /// Belege wie "netsh winsock show catalog" (mehrere hundert Zeilen, die in die Sicherung
    /// gehoeren, nicht in die Konsole; Werkzeuge.Lauf haelt in tail nur die letzten 6000
    /// Zeichen). Die Werkzeugzeile, ExitCode, Deutung und Fehler gehen weiter an den Aufrufer.
    /// Hoechstens maxZeichen; darueber wird abgeschnitten und gekuerzt = true. Kein eigener
    /// Prozessstart: der Weg bleibt Werkzeuge.Lauf (Trockenlauf: nur dessen (trocken)-Zeile,
    /// Text leer, Rueckgabe 0).
    /// </summary>
    public static class Prozessausgabe
    {
        public static int Sammeln(Ausfuehrungskontext k, string file, string args, int zeitMs, int maxZeichen, out string text, out bool gekuerzt)
        {
            var sb = new StringBuilder();
            var gate = new object();
            bool voll = false;
            Action<string, string, int?> alt = k.Zeile;
            k.Zeile = (t, art, prozent) =>
            {
                if (art == "normal" && !prozent.HasValue)
                {
                    lock (gate)
                    {
                        if (sb.Length >= maxZeichen) { voll = true; return; }
                        sb.Append(t ?? "").Append('\n');
                        if (sb.Length > maxZeichen) { sb.Length = maxZeichen; voll = true; }
                    }
                    return;
                }
                if (alt != null) alt(t, art, prozent);
            };
            try
            {
                string tail;
                int code = Werkzeuge.Lauf(k, file, args, zeitMs, null, false, out tail);
                lock (gate) { text = sb.ToString(); gekuerzt = voll; }
                return code;
            }
            finally { k.Zeile = alt; }
        }
    }

    /// <summary>
    /// WMI-Methoden ueber System.Management, im eigenen Prozess, mit Zeitgrenze. Statisch:
    /// Klassenmethode (MSFT_DNSClientCache.Clear, MSFT_MpSignature.Update). Instanz: Methode auf
    /// der ERSTEN Instanz, die die WQL-Bedingung trifft (Win32_NetworkAdapterConfiguration
    /// WHERE InterfaceIndex=10 → RenewDHCPLease). Argumente werden positionsweise ueber den
    /// ID-Qualifier der Eingabeparameter zugeordnet (die Reihenfolge der Properties-Sammlung ist
    /// nicht die Deklarationsreihenfolge); ein null-Argument bleibt ungesetzt (Standardwert).
    /// Rueckgabe: der ReturnValue der Methode (meist uint32, 0 = angenommen) oder null, wenn die
    /// Methode keinen liefert. Wer Ausgabeparameter braucht (rebootNeeded), nimmt die Voll-Varianten.
    /// </summary>
    public static class Wmi2
    {
        /// <summary>Vorgabe je Aufruf: Signaturupdate und Volume-Reparatur brauchen Minuten, nicht Sekunden.</summary>
        public const int StandardZeitMs = 10 * 60 * 1000;

        public static object Statisch(string ns, string klasse, string methode, params object[] args)
        {
            return StatischMitZeit(StandardZeitMs, ns, klasse, methode, args);
        }

        public static object StatischMitZeit(int zeitMs, string ns, string klasse, string methode, params object[] args)
        {
            using (ManagementBaseObject raus = StatischVoll(zeitMs, ns, klasse, methode, args))
                return Rueckgabe(raus);
        }

        /// <summary>Wie StatischMitZeit, liefert aber das ganze Ausgabeobjekt (ReturnValue plus Ausgabeparameter); der Aufrufer gibt es frei.</summary>
        public static ManagementBaseObject StatischVoll(int zeitMs, string ns, string klasse, string methode, params object[] args)
        {
            return Zeitgrenze.Mit(() =>
            {
                var scope = new ManagementScope(ns);
                scope.Connect();
                using (var cls = new ManagementClass(scope, new ManagementPath(klasse), null))
                using (ManagementBaseObject rein = Parameter(cls, methode, args))
                    return cls.InvokeMethod(methode, rein, null);
            }, zeitMs, "WMI " + klasse + "." + methode);
        }

        public static object Instanz(string ns, string klasse, string bedingung, string methode, params object[] args)
        {
            return InstanzMitZeit(StandardZeitMs, ns, klasse, bedingung, methode, args);
        }

        public static object InstanzMitZeit(int zeitMs, string ns, string klasse, string bedingung, string methode, params object[] args)
        {
            using (ManagementBaseObject raus = InstanzVoll(zeitMs, ns, klasse, bedingung, methode, args))
                return Rueckgabe(raus);
        }

        /// <summary>
        /// Methode auf der ersten Instanz von "SELECT * FROM klasse WHERE bedingung" (bedingung leer =
        /// alle). Keine Instanz → InvalidOperationException. Liefert das Ausgabeobjekt; der Aufrufer gibt es frei.
        /// </summary>
        public static ManagementBaseObject InstanzVoll(int zeitMs, string ns, string klasse, string bedingung, string methode, params object[] args)
        {
            string wql = "SELECT * FROM " + klasse + (string.IsNullOrEmpty(bedingung) ? "" : " WHERE " + bedingung);
            return Zeitgrenze.Mit(() =>
            {
                var scope = new ManagementScope(ns);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(wql)))
                using (ManagementObjectCollection rows = s.Get())
                {
                    foreach (ManagementObject mo in rows)
                    {
                        using (mo)
                        using (ManagementBaseObject rein = Parameter(mo, methode, args))
                            return mo.InvokeMethod(methode, rein, null);
                    }
                }
                throw new InvalidOperationException("Keine Instanz für " + wql + ".");
            }, zeitMs, "WMI " + klasse + "." + methode);
        }

        /// <summary>Erste Instanz der Abfrage oder null (Lesezugriff mit Zeitgrenze; der Aufrufer gibt sie frei).</summary>
        public static ManagementObject Erste(string ns, string wql, int zeitMs = 30000)
        {
            List<ManagementObject> rows = Sammler.Quellen.Wmi.Abfrage(ns, wql, zeitMs);
            ManagementObject erste = null;
            foreach (ManagementObject mo in rows)
            {
                if (erste == null) erste = mo; else mo.Dispose();
            }
            return erste;
        }

        /// <summary>ReturnValue als ganze Zahl; null, wenn keiner da ist oder er nicht zaehlbar ist.</summary>
        public static long? Ganz(object rueckgabe)
        {
            if (rueckgabe == null) return null;
            try
            {
                if (rueckgabe is int) return unchecked((uint)(int)rueckgabe);
                return Convert.ToInt64(rueckgabe, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return null; }
        }

        /// <summary>WQL-Zeichenkette: Backslash und Apostroph werden maskiert (fuer Bedingungen mit Text).</summary>
        public static string Wql(string s)
        {
            return s == null ? "" : s.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        // ------------------------------------------------------------------ innen

        static object Rueckgabe(ManagementBaseObject raus)
        {
            if (raus == null) return null;
            try { return raus["ReturnValue"]; }
            catch (ManagementException) { return null; }
        }

        // Eingabeparameter positionsweise fuellen. Jede Eingabe-Property traegt den Qualifier ID
        // (Position in der Deklaration); ohne ID gilt die Aufzaehlungsreihenfolge als Rueckfall.
        static ManagementBaseObject Parameter(ManagementObject traeger, string methode, object[] args)
        {
            ManagementBaseObject rein;
            try { rein = traeger.GetMethodParameters(methode); }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.MethodNotImplemented || ex.ErrorCode == ManagementStatus.NotFound)
            {
                throw new InvalidOperationException("Die WMI-Methode " + methode + " gibt es auf dieser Klasse nicht (" + ex.ErrorCode + ").", ex);
            }
            if (args == null || args.Length == 0) return rein;
            // GetMethodParameters liefert null, wenn die Methode keine Eingabeparameter hat (bei
            // MSFT_DNSClientCache.Clear gemessen). Uebergebene Argumente stillschweigend zu
            // verwerfen hiesse, eine Methode ohne die bestellten Werte aufzurufen.
            if (rein == null)
                throw new ArgumentException("Die WMI-Methode " + methode + " nimmt keine Eingabeparameter an, übergeben wurden " + args.Length + ".");

            var namen = new string[args.Length];
            var ohneId = new List<string>();
            foreach (PropertyData p in rein.Properties)
            {
                int id = -1;
                try
                {
                    foreach (QualifierData q in p.Qualifiers)
                        if (string.Equals(q.Name, "ID", StringComparison.OrdinalIgnoreCase)) { id = Convert.ToInt32(q.Value, CultureInfo.InvariantCulture); break; }
                }
                catch (Exception) { id = -1; }
                if (id >= 0 && id < namen.Length && namen[id] == null) namen[id] = p.Name;
                else if (id < 0) ohneId.Add(p.Name);
            }
            int r = 0;
            for (int i = 0; i < namen.Length; i++)
            {
                if (namen[i] == null && r < ohneId.Count) namen[i] = ohneId[r++];
                if (namen[i] == null)
                    throw new ArgumentException("Die WMI-Methode " + methode + " hat keinen Eingabeparameter an Stelle " + i + " (übergeben: " + args.Length + ").");
            }
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null) rein[namen[i]] = args[i];
            return rein;
        }
    }

    /// <summary>
    /// COM ueber spaetes Binden: Type.GetTypeFromProgID + InvokeMember, kein dynamic, keine
    /// Interop-Assembly (Vertrag Abschnitt 3). HNetCfg.FwPolicy2 (Firewall), Microsoft.Update.Session
    /// (Updates verbergen). Eine COMException kommt als TargetInvocationException zurueck; hier wird
    /// sie ausgepackt, damit der Aufrufer den HRESULT sieht. Objekte am Ende mit Freigeben loesen;
    /// Anlegen, Nutzen und Freigeben gehoeren auf denselben Thread (Zeitgrenze.Mit um den ganzen Block).
    /// </summary>
    public static class Com
    {
        public static object Neu(string progId)
        {
            Type t = Type.GetTypeFromProgID(progId, false);
            if (t == null) throw new InvalidOperationException("Die COM-Klasse „" + progId + "“ ist auf diesem PC nicht registriert.");
            return Activator.CreateInstance(t);
        }

        /// <summary>Eigenschaft lesen, bei indizierten Eigenschaften mit Index (FirewallEnabled[profil]).</summary>
        public static object Hole(object o, string name, params object[] index)
        {
            return Aufruf(o, name, BindingFlags.GetProperty, index == null || index.Length == 0 ? null : index);
        }

        /// <summary>Eigenschaft setzen; bei indizierten Eigenschaften steht der Index vor dem Wert (IDispatch: der Wert ist das letzte Argument).</summary>
        public static void Setze(object o, string name, object wert, params object[] index)
        {
            int n = index == null ? 0 : index.Length;
            var args = new object[n + 1];
            for (int i = 0; i < n; i++) args[i] = index[i];
            args[n] = wert;
            Aufruf(o, name, BindingFlags.SetProperty, args);
        }

        public static object Rufe(object o, string name, params object[] args)
        {
            return Aufruf(o, name, BindingFlags.InvokeMethod, args == null || args.Length == 0 ? null : args);
        }

        public static void Freigeben(object o)
        {
            if (o == null) return;
            try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
            catch (Exception) { }
        }

        static object Aufruf(object o, string name, BindingFlags art, object[] args)
        {
            if (o == null) throw new ArgumentNullException("o", "Kein COM-Objekt für " + name + ".");
            try { return o.GetType().InvokeMember(name, art, null, o, args, CultureInfo.InvariantCulture); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }

    /// <summary>
    /// Registry in der 64-Bit-Sicht (der Helfer ist x64, die Sammler lesen dieselbe Sicht):
    /// HKLM oeffnen, Zahl und Text lesen. Schreibzugriffe bleiben in der Massnahme selbst,
    /// hinter ihrer Trocken-Weiche.
    /// </summary>
    public static class Reg
    {
        /// <summary>Unterschluessel von HKLM (64-Bit-Sicht) oder null, wenn er fehlt; der Aufrufer gibt ihn frei.</summary>
        public static RegistryKey Hklm(string pfad, bool schreiben)
        {
            using (RegistryKey basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                return basis.OpenSubKey(pfad, schreiben);
        }

        public static int? Ganz(RegistryKey k, string name)
        {
            if (k == null) return null;
            try
            {
                object v = k.GetValue(name);
                if (v == null) return null;
                if (v is int) return (int)v;
                if (v is long) { long l = (long)v; return l > int.MaxValue || l < int.MinValue ? (int?)null : (int)l; }
                int n;
                if (v is string && int.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
                return null;
            }
            catch (Exception) { return null; }
        }

        public static string Text(RegistryKey k, string name)
        {
            if (k == null) return null;
            try { object v = k.GetValue(name); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Die Nachweiszeile des Helfers: was er nach dem Eingriff SELBST gesehen hat (der Kern
    /// weist zusaetzlich ueber dem neuen Systembild nach). Werte fuer den Host: nachweis.satz und
    /// nachweis.bestanden ("1"/"0"); Zeile good/bad; Protokolleintrag Art nachweis mit der Marke
    /// trocken. Nicht bestanden setzt das Problem-Signal: die Massnahme lief, hat ihr Ziel aber
    /// nicht erreicht.
    ///
    /// Im Trockenlauf gibt es keinen bestandenen Nachweis (Vertrag 8.7): Bestanden wird dann zur
    /// (trocken)-Zeile, nachweis.bestanden bleibt leer. Das trifft auch die "schon so"-Zweige, die
    /// ohne Eingriff bestanden melden; im Trockenlauf sagt die Zeile nur, was gemessen wurde.
    /// NichtBestanden bleibt auch trocken ein Problem: eine Vorbedingung, die im Trockenlauf
    /// scheitert, scheiterte im echten Lauf genauso.
    /// </summary>
    public static class Nachweise
    {
        public const string SatzSchluessel = "nachweis.satz";
        public const string BestandenSchluessel = "nachweis.bestanden";

        public static void Bestanden(Ausfuehrungskontext k, string kennung, string satz)
        {
            if (k.Trocken) { Trocken(k, kennung, satz); return; }
            k.Werte[SatzSchluessel] = satz;
            k.Werte[BestandenSchluessel] = "1";
            k.Schreibe("   ✔  " + satz, "good");
            if (k.Protokoll != null) k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Nachweis, kennung, satz, new { bestanden = true, trocken = k.Trocken });
        }

        public static void NichtBestanden(Ausfuehrungskontext k, string kennung, string satz)
        {
            k.Werte[SatzSchluessel] = satz;
            k.Werte[BestandenSchluessel] = "0";
            k.Schreibe("   ✖  " + satz, "bad");
            if (k.Protokoll != null) k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Nachweis, kennung, satz, new { bestanden = false, trocken = k.Trocken });
            Katalog.Problem(k);
        }

        /// <summary>Trockenlauf: der Satz nennt, was geprueft worden waere; kein Problem, kein Nachweis.</summary>
        public static void Trocken(Ausfuehrungskontext k, string kennung, string satz)
        {
            k.Werte[SatzSchluessel] = "(trocken) " + satz;
            k.Schreibe("(trocken) " + satz, "dim");
            if (k.Protokoll != null) k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Nachweis, kennung, "(trocken) " + satz, new { trocken = true });
        }
    }

    /// <summary>
    /// Baut den Helfer-Katalogeintrag einer M3-Massnahme: Titel und Stufe kommen aus dem
    /// Kern-Katalog (kern\Massnahmen.cs) unter derselben Kennung; fehlt der Eintrag dort (Kern
    /// und Helfer entstehen in getrennten Bauabschnitten), gelten die Ersatzwerte, und app.log
    /// sagt es. So bleibt der Helfer lauffaehig, und die Kernprobe (jede Kennung im Katalog)
    /// findet die Luecke trotzdem.
    /// </summary>
    public static class Massnahmenbau
    {
        public static Massnahme Neu(string kennung, string titelErsatz, int stufeErsatz,
                                    Func<PlanSchritt, string> pruefen, Action<PlanSchritt, Ausfuehrungskontext> ausfuehren)
        {
            Kern.Massnahme kern = null;
            try { kern = Kern.Massnahmen.Finde(kennung); }
            catch (Exception ex) { AppLog.Warn("Kern-Katalog für " + kennung + " nicht lesbar: " + ex.Message); }
            if (kern == null) AppLog.Warn("Kennung " + kennung + " fehlt im Kern-Katalog; der Helfer nimmt Titel und Stufe aus dem Ersatz.");
            return new Massnahme
            {
                Kennung = kennung,
                Titel = kern != null && !string.IsNullOrEmpty(kern.Titel) ? kern.Titel : titelErsatz,
                Stufe = kern != null ? kern.Stufe : stufeErsatz,
                Pruefen = pruefen ?? (s => null),
                Ausfuehren = ausfuehren,
            };
        }

        /// <summary>Fremder Text in einer Meldung: eine Zeile, hoechstens 80 Zeichen (wie Katalog.Kurz).</summary>
        public static string Kurz(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }
    }
}
