using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Leistung (docs/M3-ENTWURF.md, Abschnitt 1): leistung.autostart.deaktivieren,
        // leistung.aufgabe.deaktivieren, leistung.dienst.startart. Anmeldung hier, Arbeit in
        // LeistungMassnahmen.
        static partial void Leistung(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("leistung.autostart.deaktivieren", "Startprogramm deaktivieren", 2,
                LeistungMassnahmen.AutostartPruefen, LeistungMassnahmen.AutostartDeaktivieren));
            l.Add(Massnahmenbau.Neu("leistung.aufgabe.deaktivieren", "Geplante Aufgabe deaktivieren", 2,
                LeistungMassnahmen.AufgabePruefen, LeistungMassnahmen.AufgabeDeaktivieren));
            l.Add(Massnahmenbau.Neu("leistung.dienst.startart", "Startart eines Dienstes ändern", 2,
                LeistungMassnahmen.DienstPruefen, LeistungMassnahmen.DienstStartart));
        }
    }

    /// <summary>
    /// Die Leistungs-Massnahmen des Helfers: Startprogramm ueber StartupApproved (Registry,
    /// 12 Byte wie der Task-Manager), Aufgabe ueber schtasks.exe /Change (Zustand vorher und
    /// nachher ueber COM Schedule.Service gelesen, nie aus lokalisiertem Text), Dienst-Startart
    /// ueber Win32_Service.ChangeStartMode. Jede Massnahme sichert den Vorwert in die
    /// Sicherungsdatei, bevor sie eingreift; der Rueckweg (Rueckweg.cs) ruft dieselben
    /// Bausteine (ApprovedSchreiben, AufgabeSchalten, DienstStartartSetzen) mit den Vorwerten.
    /// Die Zeichenmengen fuer Dienst- und Wertnamen und Aufgabenpfade fuehrt der Kern
    /// (Regeln.Leistung.DienstNameZulaessig, WertnameZulaessig, AufgabenpfadZulaessig), damit Regel
    /// und Helfer dieselbe Grenze ziehen. Zwei Sperrlisten: Dienste
    /// (NieDeaktivieren) werden nie auf Disabled gestellt, Aufgaben unter den Praefixen in
    /// AufgabenGesperrt nie deaktiviert.
    ///
    /// HKCU liegt im Profil des Helfer-Kontos. Massnahme.Profilgebunden gilt je Kennung, nicht je
    /// Schritt; deshalb prueft die Ausfuehrung selbst bei hive=HKCU das Konto
    /// (Ausfuehrung.AufruferIstEigenesKonto) und fasst bei fremdem Konto nichts an. HKLM ist
    /// maschinenweit und davon nicht betroffen.
    ///
    /// Profilbindung der Sicherung (Gegenlesen M3, 18.09.2026): ein HKCU-Vorwert gehoert zu genau
    /// einem Profil, der Verlauf (src/History.cs) ist aber maschinenweit, also sieht jedes Konto den
    /// Knopf "Rueckgaengig" jedes anderen. Der Rueckweg schreibt in das HKCU des Helfer-Kontos; ohne
    /// Bindung landete der Vorwert von Konto A im Profil von Konto B, und A bliebe deaktiviert. Deshalb
    /// traegt die Sicherung bei hive=HKCU unter parameter.profilSid die SID des eigenen Kontos, und
    /// der Rueckweg (Rueckweg.cs InhaltPruefen, AutostartZurueck ueber ProfilPruefen) stellt nur
    /// zurueck, wenn diese SID das eigene Profil ist. Bei HKLM gibt es keine Bindung.
    /// </summary>
    internal static class LeistungMassnahmen
    {
        public const string ApprovedBasis = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const string Cimv2 = @"root\cimv2";
        const int SchtasksMs = 2 * 60 * 1000;
        const int ComMs = 60000;
        const int DienstMs = 60000;

        // ================================================================ Profilbindung (HKCU)

        /// <summary>Schluessel unter "parameter" der Sicherung: SID des Profils, aus dem ein HKCU-Vorwert stammt.</summary>
        public const string ProfilSchluessel = "profilSid";

        /// <summary>Grund, wenn eine HKCU-Sicherung aus dem Profil eines anderen Kontos stammt (InhaltPruefen und ProfilPruefen).</summary>
        public const string ProfilFremdeSicherung = "Die Sicherung gehört zum Profil eines anderen Kontos; ein Startprogramm unter HKCU wird nur in dem Konto zurückgestellt, in dem es deaktiviert wurde.";

        // SID eines Benutzerprofils: lokales oder Domaenenkonto S-1-5-21-a-b-c-rid, Azure-AD-Konto
        // S-1-12-1-a-b-c-d (je 4 Zahlen). SYSTEM (S-1-5-18), Dienstkonten und Gruppen tragen kein
        // Profil mit Startprogrammen; sie fallen hier durch, und die Hin-Massnahme sichert dann nicht.
        static readonly Regex ProfilSidRx = new Regex(@"^S-1-(?:5-21|12-1)(?:-\d{1,10}){4}$", RegexOptions.Compiled);

        /// <summary>Getrimmte SID in Grossschreibung, wenn sie die Form eines Benutzerprofils hat; sonst null.</summary>
        public static string ProfilSidNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim().ToUpperInvariant();
            return ProfilSidRx.IsMatch(t) ? t : null;
        }

        /// <summary>SID des eigenen Kontos (das Profil, in dem HKCU dieses Prozesses liegt); null, wenn nicht lesbar.</summary>
        public static string EigeneSid()
        {
            try
            {
                using (WindowsIdentity wi = WindowsIdentity.GetCurrent())
                    return wi.User == null ? null : wi.User.Value;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Eigenes Konto nicht lesbar: " + ex.Message);
                return null;
            }
        }

        /// <summary>true, wenn profilSid das eigene Profil nennt; nicht lesbar oder leer zaehlt als fremd (nie stille Leere).</summary>
        public static bool ProfilIstEigenes(string profilSid)
        {
            string eigene = ProfilSidNorm(EigeneSid());
            string fremd = ProfilSidNorm(profilSid);
            return eigene != null && fremd != null && string.Equals(eigene, fremd, StringComparison.Ordinal);
        }

        /// <summary>
        /// true = weiter; false = hive HKCU, aber die SID der Sicherung ist nicht das eigene Profil:
        /// Zeile bad, Protokoll (abgelehnt) und Problem sind gesetzt. Zweites Tor neben KontoPruefen:
        /// KontoPruefen misst den Aufrufer gegen den Helfer, ProfilPruefen die Sicherung gegen den Helfer.
        /// </summary>
        public static bool ProfilPruefen(Ausfuehrungskontext k, string hive, string profilSid)
        {
            if (hive != "HKCU" || ProfilIstEigenes(profilSid)) return true;
            k.Schreibe("   " + ProfilFremdeSicherung, "bad");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Abgelehnt, "autostart", "HKCU nicht angefasst: Sicherung aus dem Profil eines anderen Kontos",
                    new { profilSid = profilSid ?? "", aufruferSid = k.AufruferSid ?? "" });
            Katalog.Problem(k);
            return false;
        }

        // ================================================================ Startprogramm

        /// <summary>"hkcu"/"HKLM" → "HKCU"/"HKLM"; sonst null.</summary>
        public static string HiveNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            switch (s.Trim().ToUpperInvariant())
            {
                case "HKCU": return "HKCU";
                case "HKLM": return "HKLM";
                default: return null;
            }
        }

        /// <summary>Run | Run32 | StartupFolder (Unterschluessel von StartupApproved), sonst null.</summary>
        public static string OrtNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            switch (s.Trim().ToUpperInvariant())
            {
                case "RUN": return "Run";
                case "RUN32": return "Run32";
                case "STARTUPFOLDER": return "StartupFolder";
                default: return null;
            }
        }

        /// <summary>
        /// Wertname im StartupApproved-Schluessel (Registry-Wertname des Run-Eintrags bzw.
        /// Dateiname im Autostart-Ordner). Die Zeichenmenge (1 bis 255 Zeichen, keine
        /// Steuerzeichen, kein ';') fuehrt der Kern in Regeln.Leistung.WertnameZulaessig; die Regel
        /// haengt die Massnahme nur an Namen, die hier auch durchkommen. Sonst null.
        /// </summary>
        public static string NamePruefen(string s)
        {
            return Kern.Regeln.Leistung.WertnameZulaessig(s) ? s : null;
        }

        public static string AutostartPruefen(PlanSchritt s)
        {
            if (HiveNorm(s.Wert("hive")) == null) return "Der Parameter hive muss HKCU oder HKLM sein, nicht „" + Massnahmenbau.Kurz(s.Wert("hive")) + "“.";
            if (OrtNorm(s.Wert("ort")) == null) return "Der Parameter ort muss Run, Run32 oder StartupFolder sein, nicht „" + Massnahmenbau.Kurz(s.Wert("ort")) + "“.";
            if (NamePruefen(s.Wert("name")) == null) return "Der Parameter name muss 1 bis 255 Zeichen ohne Steuerzeichen und ohne Semikolon haben.";
            return null;
        }

        const string RunBasis = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Gibt es den Eintrag, den StartupApproved schalten soll? Run/Run32: der Wert name unter
        /// CurrentVersion\Run in der 64- bzw. 32-Bit-Sicht (Wow6432Node); StartupFolder: die Datei
        /// name im Autostart-Ordner des Profils (HKCU) bzw. aller Benutzer (HKLM). Ohne diesen Eintrag
        /// entstuende nur ein verwaister StartupApproved-Wert, den das Bild nie zeigt. Ein Name mit
        /// Pfadzeichen ist im Ordner keine Datei: dann false, nie ein Blick ausserhalb des Ordners.
        /// </summary>
        public static bool EintragVorhanden(string hive, string ort, string name)
        {
            if (ort == "StartupFolder")
            {
                if (name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || name == "." || name == ".." || name.Trim().Length == 0) return false;
                string dir = Environment.GetFolderPath(hive == "HKCU" ? Environment.SpecialFolder.Startup : Environment.SpecialFolder.CommonStartup);
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return false;
                return System.IO.File.Exists(System.IO.Path.Combine(dir, name));
            }
            RegistryView sicht = ort == "Run32" ? RegistryView.Registry32 : RegistryView.Registry64;
            using (RegistryKey basis = RegistryKey.OpenBaseKey(hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, sicht))
            using (RegistryKey key = basis.OpenSubKey(RunBasis, false))
            {
                if (key == null) return false;
                foreach (string n in key.GetValueNames())
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        static string EintragOrtWort(string hive, string ort)
        {
            return ort == "StartupFolder"
                ? (hive == "HKCU" ? "im Autostart-Ordner des Profils" : "im Autostart-Ordner aller Benutzer")
                : "unter " + hive + "\\...\\CurrentVersion\\Run" + (ort == "Run32" ? " (32-Bit-Sicht)" : "");
        }

        /// <summary>
        /// StartupApproved\&lt;ort&gt;, Wert &lt;name&gt;: Byte 0 = 3 und FILETIME (UTC) in Byte 4..11, insgesamt
        /// 12 Byte, genau wie der Task-Manager beim Deaktivieren (Sammler: 2 aktiv, 3 deaktiviert).
        /// Der Run-Eintrag selbst bleibt stehen und muss vor der Sicherung existieren (sonst Zeile
        /// bad + Problem). Vorwert (die alten Bytes oder "" fuer "kein Wert") in die Sicherung; bei
        /// HKCU dazu parameter.profilSid = eigene SID (Profilbindung, siehe Klassenkommentar).
        /// </summary>
        public static void AutostartDeaktivieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "leistung.autostart.deaktivieren";
            string hive = HiveNorm(s.Wert("hive"));
            string ort = OrtNorm(s.Wert("ort"));
            string name = NamePruefen(s.Wert("name"));

            if (!KontoPruefen(k, hive, "Das Startprogramm gehört zum Benutzerprofil (HKCU).")) return;

            // Profilbindung: die Sicherung eines HKCU-Vorwerts nennt das Profil, aus dem er stammt.
            // Ohne lesbare Profil-SID (SYSTEM, Dienstkonto) gibt es kein Profil, das ein Rueckweg
            // spaeter wiederfinden koennte: dann wird weder gesichert noch geaendert.
            string profilSid = null;
            if (hive == "HKCU")
            {
                profilSid = ProfilSidNorm(EigeneSid());
                if (profilSid == null)
                {
                    k.Schreibe("   Das Konto des Helfers hat keine Profil-SID der Form S-1-5-21-… oder S-1-12-1-…; ein Startprogramm unter HKCU wird ohne Profilbindung weder gesichert noch geändert.", "bad");
                    Katalog.Problem(k);
                    return;
                }
            }

            // Erst der Eintrag selbst: ohne Run-Wert bzw. Datei im Autostart-Ordner gibt es nichts zu
            // deaktivieren, und ein StartupApproved-Wert ohne Eintrag waere eine Leiche in der Registrierung.
            if (!EintragVorhanden(hive, ort, name))
            {
                k.Schreibe("   Ein Startprogramm „" + name + "“ gibt es " + EintragOrtWort(hive, ort) + " nicht (mehr); ohne den Eintrag wird kein StartupApproved-Wert gesetzt.", "bad");
                Katalog.Problem(k);
                return;
            }
            byte[] alt = ApprovedLesen(hive, ort, name);
            if (alt != null && alt[0] == 3)
            {
                SchonSo(k, kennung, "„" + name + "“ war schon deaktiviert (StartupApproved Byte 0 = 3); es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   " + hive + "\\...\\StartupApproved\\" + ort + ", Wert „" + name + "“: " + (alt == null ? "kein Eintrag (gilt als aktiv)" : "Byte 0 = " + alt[0] + " (" + Hex(alt) + ")") + "; der Eintrag „" + name + "“ ist " + EintragOrtWort(hive, ort) + " vorhanden.", "dim");

            var parameter = Sicherungsdatei.Werte("hive", hive, "ort", ort, "name", name);
            if (profilSid != null) parameter[ProfilSchluessel] = profilSid;
            Sicherungsdatei.Schreiben(k, kennung, parameter, Sicherungsdatei.Werte("bytes", alt == null ? "" : Hex(alt)));

            if (k.Trocken)
            {
                k.Schreibe("(trocken) Registry " + hive + "\\" + ApprovedBasis + ort + " Wert „" + name + "“ = 03 00 00 00 + FILETIME (12 Byte)", "dim");
                Nachweise.Trocken(k, kennung, "„" + name + "“ würde im Task-Manager auf „Deaktiviert“ gesetzt.");
                return;
            }

            ApprovedSchreiben(hive, ort, name, Deaktiviert(DateTime.UtcNow));
            byte[] jetzt = ApprovedLesen(hive, ort, name);
            if (jetzt != null && jetzt.Length >= 1 && jetzt[0] == 3)
                Nachweise.Bestanden(k, kennung, "„" + name + "“ startet nicht mehr mit Windows (StartupApproved Byte 0 = 3, im Task-Manager „Deaktiviert“); der Eintrag selbst bleibt stehen.");
            else
                Nachweise.NichtBestanden(k, kennung, "Der Wert „" + name + "“ ließ sich nicht setzen; das Startprogramm bleibt aktiv.");
        }

        /// <summary>true = weiter; false = fremdes Konto bei HKCU, Zeile bad und Problem sind gesetzt.</summary>
        public static bool KontoPruefen(Ausfuehrungskontext k, string hive, string zusatz)
        {
            if (hive != "HKCU" || Ausfuehrung.AufruferIstEigenesKonto(k.AufruferSid)) return true;
            k.Schreibe("   " + Ausfuehrung.ProfilFremdesKonto + " " + zusatz, "bad");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Abgelehnt, "autostart", "HKCU nicht angefasst: anderes Konto im Dialog angemeldet", new { aufruferSid = k.AufruferSid ?? "" });
            Katalog.Problem(k);
            return false;
        }

        /// <summary>Die 12 Byte des Task-Managers fuer "deaktiviert": 03 00 00 00 + FILETIME (UTC).</summary>
        public static byte[] Deaktiviert(DateTime utc)
        {
            var b = new byte[12];
            b[0] = 3;
            Array.Copy(BitConverter.GetBytes(utc.ToFileTimeUtc()), 0, b, 4, 8);
            return b;
        }

        static RegistryKey Basis(string hive)
        {
            return RegistryKey.OpenBaseKey(hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        /// <summary>Die Bytes des Werts oder null (Schluessel oder Wert fehlt, kein Binaerwert, leerer Binaerwert: der Task-Manager liest beides als "aktiv").</summary>
        public static byte[] ApprovedLesen(string hive, string ort, string name)
        {
            using (RegistryKey basis = Basis(hive))
            using (RegistryKey key = basis.OpenSubKey(ApprovedBasis + ort, false))
            {
                if (key == null) return null;
                byte[] b = key.GetValue(name) as byte[];
                return b == null || b.Length == 0 ? null : b;
            }
        }

        /// <summary>
        /// "War schon so"-Zweig: gemessen wurde nur gelesen, nichts geaendert. Im echten Lauf ist das
        /// ein bestandener Nachweis; im Trockenlauf bleibt es eine (trocken)-Zeile, damit
        /// nachweis.bestanden dort nie "1" traegt (Vertrag docs/M3-ENTWURF.md 8.7).
        /// </summary>
        public static void SchonSo(Ausfuehrungskontext k, string kennung, string satz)
        {
            if (k.Trocken) Nachweise.Trocken(k, kennung, satz);
            else Nachweise.Bestanden(k, kennung, satz);
        }

        /// <summary>Setzt die Bytes (Binaerwert); bytes null oder leer = Wert loeschen (Rueckweg zu "kein Eintrag"). Legt den Schluessel an, wie es der Task-Manager tut.</summary>
        public static void ApprovedSchreiben(string hive, string ort, string name, byte[] bytes)
        {
            using (RegistryKey basis = Basis(hive))
            using (RegistryKey key = basis.CreateSubKey(ApprovedBasis + ort, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (key == null) throw new InvalidOperationException("Der Schlüssel StartupApproved\\" + ort + " ließ sich nicht öffnen.");
                if (bytes == null || bytes.Length == 0) key.DeleteValue(name, false);
                else key.SetValue(name, bytes, RegistryValueKind.Binary);
            }
        }

        public static string Hex(byte[] b)
        {
            if (b == null) return "";
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Hex-Text (gerade Laenge, hoechstens 64 Zeichen) → Bytes; "" → leer; ungueltig → null.</summary>
        public static byte[] Bytes(string hex)
        {
            if (hex == null) return null;
            if (hex.Length == 0) return new byte[0];
            if (hex.Length % 2 != 0 || hex.Length > 64) return null;
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                int wert;
                if (!int.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out wert)) return null;
                b[i] = (byte)wert;
            }
            return b;
        }

        // ================================================================ Aufgabe

        /// <summary>
        /// Aufgabenpfad, wie er als eigenes Argument in schtasks.exe /Change /TN "pfad" geht (Vertrag
        /// Abschnitt 1 und 8.10). Die Zeichenmenge und die Laenge (hoechstens 260) fuehrt allein der
        /// Kern in Regeln.Leistung.AufgabenpfadZulaessig: Buchstaben, Ziffern, Leerzeichen und
        /// _ . - \ ( ) { }; die geschweiften Klammern brauchen die Updater-Aufgaben
        /// (MicrosoftEdgeUpdateTaskMachineCore{GUID}), und Regel und Helfer ziehen so dieselbe Grenze.
        /// Hier bleibt nur die Struktur: fuehrender Backslash (wird ergaenzt), kein doppelter und kein
        /// abschliessender Backslash, jedes Segment mit Buchstabe oder Ziffer. Sonst null.
        /// Zur Argumentzeile: ein Anfuehrungszeichen kaeme nie hinein (die Regel laesst keines zu), ein
        /// Backslash nie vor das schliessende Anfuehrungszeichen (die Struktur verbietet ihn am Ende;
        /// die C-Laufzeit von schtasks laese \" sonst als Anfuehrungszeichen), und { } haben ohne
        /// cmd.exe keine Sonderbedeutung: Werkzeuge.Lauf startet ueber CreateProcess (UseShellExecute = false).
        /// </summary>
        public static string AufgabePfadNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            if (t.Length == 0) return null;
            if (t[0] != '\\') t = "\\" + t;
            // Zeichen und Laenge prueft der Kern am fertigen Pfad (mit dem ergaenzten Backslash).
            if (!Kern.Regeln.Leistung.AufgabenpfadZulaessig(t)) return null;
            if (t.Length < 2 || t.EndsWith("\\", StringComparison.Ordinal) || t.IndexOf("\\\\", StringComparison.Ordinal) >= 0) return null;
            // Jedes Segment braucht einen Buchstaben oder eine Ziffer: "\Fremd\..\Update" oder "\{}" ist kein Aufgabenpfad.
            foreach (string teil in t.Substring(1).Split('\\'))
                if (!HatBuchstabeOderZiffer(teil)) return null;
            return t;
        }

        static bool HatBuchstabeOderZiffer(string s)
        {
            foreach (char c in s)
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) return true;
            return false;
        }

        /// <summary>
        /// Aufgaben, die der Helfer nie deaktiviert (Gegenstueck zu NieDeaktivieren bei den
        /// Diensten): die Defender-, Windows-Update- und Update-Orchestrator-Ordner und die eigenen
        /// Aufgaben des Programms. Vergleich als Praefix auf dem normierten Pfad, ohne Gross-/Kleinschreibung.
        /// </summary>
        static readonly string[] AufgabenGesperrt =
        {
            @"\Microsoft\Windows\Windows Defender\",
            @"\Microsoft\Windows\WindowsUpdate\",
            @"\Microsoft\Windows\UpdateOrchestrator\",
            @"\WindowsWartung-",
        };

        /// <summary>true, wenn der (normierte) Pfad unter einem gesperrten Praefix liegt.</summary>
        public static bool AufgabeGesperrt(string pfad)
        {
            if (string.IsNullOrEmpty(pfad)) return true;
            foreach (string p in AufgabenGesperrt)
                if (pfad.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string AufgabePruefen(PlanSchritt s)
        {
            string pfad = AufgabePfadNorm(s.Wert("pfad"));
            if (pfad == null)
                return "Ungültiger Aufgabenpfad „" + Massnahmenbau.Kurz(s.Wert("pfad")) + "“: erlaubt sind bis zu 260 Zeichen aus Buchstaben, Ziffern, Leerzeichen und _ . - \\ ( ) { }, ohne doppelten oder abschließenden Backslash.";
            if (AufgabeGesperrt(pfad))
                return "Die Aufgabe „" + pfad + "“ gehört zu Windows Defender, Windows Update oder zu diesem Programm; sie wird nicht deaktiviert.";
            return null;
        }

        public sealed class AufgabeStand
        {
            public bool Gefunden;
            public bool Aktiv;      // IRegisteredTask.Enabled
            public int Zustand;     // TASK_STATE: 1 deaktiviert, 3 bereit, 4 laeuft
        }

        /// <summary>
        /// Zustand der Aufgabe ueber COM Schedule.Service (wie der Sammler, in-process, nicht
        /// lokalisiert): GetFolder(ordner).GetTask(name). Nicht vorhanden → Gefunden=false
        /// (COM 0x80070002). Mit Zeitgrenze; Anlegen, Nutzen und Freigeben auf demselben Thread.
        /// </summary>
        public static AufgabeStand AufgabeLesen(string pfad)
        {
            int i = pfad.LastIndexOf('\\');
            string ordner = i <= 0 ? "\\" : pfad.Substring(0, i);
            string name = pfad.Substring(i + 1);
            return Zeitgrenze.Mit(() =>
            {
                object svc = null, folder = null, task = null;
                try
                {
                    svc = Com.Neu("Schedule.Service");
                    // Connect hat vier optionale Parameter; ueber IDispatch als Type.Missing (Sammler, Autostartquelle).
                    Com.Rufe(svc, "Connect", Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                    folder = Com.Rufe(svc, "GetFolder", ordner);
                    try { task = Com.Rufe(folder, "GetTask", name); }
                    catch (System.IO.FileNotFoundException) { return new AufgabeStand { Gefunden = false }; }
                    catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return new AufgabeStand { Gefunden = false }; }
                    return new AufgabeStand
                    {
                        Gefunden = true,
                        Aktiv = Convert.ToBoolean(Com.Hole(task, "Enabled"), CultureInfo.InvariantCulture),
                        Zustand = Convert.ToInt32(Com.Hole(task, "State"), CultureInfo.InvariantCulture),
                    };
                }
                catch (System.IO.DirectoryNotFoundException) { return new AufgabeStand { Gefunden = false }; }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070002) || ex.HResult == unchecked((int)0x80070003))
                {
                    return new AufgabeStand { Gefunden = false };
                }
                finally { Com.Freigeben(task); Com.Freigeben(folder); Com.Freigeben(svc); }
            }, ComMs, "Die Aufgabenplanung (Schedule.Service)");
        }

        /// <summary>schtasks.exe /Change /TN "pfad" /DISABLE|/ENABLE ueber Werkzeuge.Lauf (Trockenlauf: nur die Zeile). Rueckgabe Exit-Code.</summary>
        public static int AufgabeSchalten(Ausfuehrungskontext k, string pfad, bool aktiv)
        {
            string tail;
            return Werkzeuge.Lauf(k, "schtasks.exe", "/Change /TN \"" + pfad + "\" " + (aktiv ? "/ENABLE" : "/DISABLE"), SchtasksMs, null, false, out tail);
        }

        public static void AufgabeDeaktivieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "leistung.aufgabe.deaktivieren";
            string pfad = AufgabePfadNorm(s.Wert("pfad"));
            AufgabeStand stand = AufgabeLesen(pfad);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Die Aufgabe „" + pfad + "“ gibt es in der Aufgabenplanung nicht (mehr).", "bad");
                Katalog.Problem(k);
                return;
            }
            if (!stand.Aktiv)
            {
                SchonSo(k, kennung, "Die Aufgabe „" + pfad + "“ war schon deaktiviert; es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   Aufgabe „" + pfad + "“: aktiv (Zustand " + stand.Zustand + ").", "dim");

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("pfad", pfad), Sicherungsdatei.Werte("aktiv", "1"));

            int exit = AufgabeSchalten(k, pfad, false);
            if (k.Trocken)
            {
                Nachweise.Trocken(k, kennung, "Die Aufgabe „" + pfad + "“ würde deaktiviert (schtasks /Change /DISABLE).");
                return;
            }
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "schtasks meldete Exit " + exit + "; die Aufgabe „" + pfad + "“ bleibt aktiv.");
                return;
            }
            AufgabeStand nach = AufgabeLesen(pfad);
            if (nach.Gefunden && !nach.Aktiv)
                Nachweise.Bestanden(k, kennung, "Die Aufgabe „" + pfad + "“ ist deaktiviert (Enabled = false); sie läuft bei keiner Anmeldung und keinem Start mehr.");
            else
                Nachweise.NichtBestanden(k, kennung, "schtasks meldete Erfolg, die Aufgabenplanung zeigt „" + pfad + "“ aber weiter als aktiv.");
        }

        // ================================================================ Dienst

        /// <summary>
        /// Win32_Service.Name, geprueft mit derselben Menge wie die Regel (Regeln.Leistung.DienstNameZulaessig:
        /// 1 bis 256 Zeichen, verboten nur \ / ' " ; und Steuerzeichen, $ erlaubt wie bei MSSQL$SQLEXPRESS);
        /// sonst null. In der WQL-Bedingung steht der Name nur ueber Wmi2.Wql maskiert.
        /// </summary>
        public static string DienstNameNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            return Kern.Regeln.Leistung.DienstNameZulaessig(t) ? t : null;
        }

        /// <summary>Automatic | Manual | Disabled (auch "Auto" aus Win32_Service.StartMode → Automatic); Boot und System bleiben (Treiber); sonst null.</summary>
        public static string StartartNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            switch (s.Trim().ToUpperInvariant())
            {
                case "AUTO":
                case "AUTOMATIC": return "Automatic";
                case "MANUAL": return "Manual";
                case "DISABLED": return "Disabled";
                case "BOOT": return "Boot";
                case "SYSTEM": return "System";
                default: return null;
            }
        }

        /// <summary>Zielwerte der Massnahme leistung.dienst.startart: nur die drei fuer Dienste; Boot/System sind Treiberwerte.</summary>
        public static string ZielStartartNorm(string s)
        {
            string n = StartartNorm(s);
            return n == "Automatic" || n == "Manual" || n == "Disabled" ? n : null;
        }

        public static string StartartWort(string startart)
        {
            switch (StartartNorm(startart))
            {
                case "Automatic": return "automatisch";
                case "Manual": return "manuell";
                case "Disabled": return "deaktiviert";
                case "Boot": return "Boot";
                case "System": return "System";
                default: return startart ?? "unbekannt";
            }
        }

        /// <summary>
        /// Dienste, ohne die Windows-Update, der Schutz, die Anmeldung oder die Bedienung von Windows
        /// nicht laufen: die Massnahme leistung.dienst.startart stellt sie nie auf Disabled, und der
        /// Rueckweg spielt fuer sie keinen Vorwert Disabled zurueck (Netz gegen einen verfaelschten
        /// Plan oder eine untergeschobene Sicherung; die Regeln schlagen nur Manual vor, Disabled
        /// erreicht den Helfer nur ueber die Pipe). Manual bleibt erlaubt. Jeder Name steht so in
        /// tests/aufzeichnungen/dieser-pc-de.json (65 Namen, am 18.09.2026 gegen die Aufzeichnung
        /// geprueft); die Probe dazu ist Phase 3.
        /// </summary>
        static readonly HashSet<string> NieDeaktivieren = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Schutz und Update
            "WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc", "mpssvc", "BFE",
            "wuauserv", "bits", "cryptsvc", "msiserver", "TrustedInstaller", "UsoSvc", "DoSvc",
            // Kern: ohne sie startet oder laeuft Windows nicht
            "EventLog", "RpcSs", "RpcEptMapper", "DcomLaunch", "Winmgmt", "Schedule", "PlugPlay", "Power",
            "ProfSvc", "SamSs", "LSM", "gpsvc", "nsi", "Dhcp", "Dnscache", "LanmanWorkstation", "UserManager",
            "BrokerInfrastructure", "CoreMessagingRegistrar", "StateRepository", "SystemEventsBroker", "VaultSvc",
            "KeyIso", "sppsvc", "Themes", "AudioSrv", "AudioEndpointBuilder", "TimeBrokerSvc", "VSS",
            // Bedienung und Reparatur: Appinfo ist die UAC-Erhoehung (ohne sie laesst sich nichts
            // mehr erhoeht reparieren, auch dieser Helfer nicht), EventSystem/SENS tragen die
            // Anmelde- und Netzereignisse, seclogon "Als Administrator ausfuehren".
            "Appinfo", "EventSystem", "SENS", "seclogon", "DeviceInstall", "ShellHWDetection",
            // Netz: Verbindungsstatus, Profile, WLAN, Freigaben, Zeit, Fernzugriff
            "NlaSvc", "Wcmsvc", "netprofm", "Netman", "WlanSvc", "LanmanServer", "W32Time", "TermService",
            // Anmeldung: Microsoft-Konto, Token, PIN (Windows Hello), Fingerabdruck
            "wlidsvc", "TokenBroker", "NgcSvc", "NgcCtnrSvc", "WbioSrvc",
            // Store-Apps: Lizenzen, Bereitstellung, Installation
            "ClipSVC", "AppXSvc", "InstallService",
        };

        /// <summary>true, wenn der Dienst in NieDeaktivieren steht (Gross-/Kleinschreibung zaehlt nicht).</summary>
        public static bool NieDeaktiviert(string name)
        {
            return !string.IsNullOrEmpty(name) && NieDeaktivieren.Contains(name);
        }

        /// <summary>
        /// Werte-Schluessel fuer den Host: "0", wenn die Sicherung dieser Massnahme nie zurueckgespielt
        /// wird. Zwei Faelle: Vorwert Disabled eines Dienstes aus NieDeaktivieren (der Rueckweg lehnt
        /// die Datei mit Exit 2 ab, Vertrag 8.2), und update.dienst.aktivieren bei jedem Vorwert
        /// (Katalog Rueckweg keiner, Vertrag 8.10). sicherung.datei bleibt gesetzt, die Datei ist der
        /// Beleg; die Oberflaeche zeigt statt "Rueckgaengig" den Satz, dass es hier nicht geht. Fehlt
        /// der Schluessel, ist der Rueckweg moeglich.
        /// </summary>
        public const string RueckwegMoeglichSchluessel = "rueckweg.moeglich";

        /// <summary>Meldet dem Host, dass der Rueckweg dieser Sicherung nicht moeglich ist (Vorwert Disabled eines Dienstes aus NieDeaktivieren; Zeile warn + Wert rueckweg.moeglich = "0").</summary>
        public static void RueckwegUnmoeglich(Ausfuehrungskontext k, string name)
        {
            RueckwegAusgeschlossen(k, "Der Vorwert „deaktiviert“ wird gesichert, aber nie zurückgespielt: der Rückweg stellt „" + name + "“ nicht auf Deaktiviert (Rückgängig ist hier nicht möglich).");
        }

        /// <summary>Wert rueckweg.moeglich = "0" plus die Zeile (warn), die dem Nutzer sagt, warum es hier kein Rueckgaengig gibt.</summary>
        public static void RueckwegAusgeschlossen(Ausfuehrungskontext k, string satz)
        {
            k.Werte[RueckwegMoeglichSchluessel] = "0";
            k.Schreibe("   " + satz, "warn");
        }

        public static string DienstPruefen(PlanSchritt s)
        {
            string name = DienstNameNorm(s.Wert("name"));
            if (name == null) return "Ungültiger Dienstname „" + Massnahmenbau.Kurz(s.Wert("name")) + "“: erlaubt sind 1 bis 256 Zeichen ohne \\ / ' \" ; und ohne Steuerzeichen.";
            string ziel = ZielStartartNorm(s.Wert("startart"));
            if (ziel == null) return "Der Parameter startart muss Manual, Disabled oder Automatic sein, nicht „" + Massnahmenbau.Kurz(s.Wert("startart")) + "“.";
            if (ziel == "Disabled" && NieDeaktiviert(name))
                return "Der Dienst „" + name + "“ gehört zu Windows-Update, zum Schutz oder zum Kern von Windows; er wird nicht deaktiviert (Manual ist erlaubt).";
            return null;
        }

        public sealed class DienstStand
        {
            public bool Gefunden;
            public string Startart;   // Win32_Service.StartMode: Boot, System, Auto, Manual, Disabled
            public string Zustand;    // Win32_Service.State: Running, Stopped, ...
        }

        public static DienstStand DienstLesen(string name)
        {
            ManagementObject mo = null;
            try
            {
                mo = Wmi2.Erste(Cimv2, "SELECT Name, StartMode, State FROM Win32_Service WHERE Name='" + Wmi2.Wql(name) + "'", 30000);
                if (mo == null) return new DienstStand { Gefunden = false };
                return new DienstStand
                {
                    Gefunden = true,
                    Startart = Sammler.Quellen.Wmi.Str(mo, "StartMode"),
                    Zustand = Sammler.Quellen.Wmi.Str(mo, "State"),
                };
            }
            finally { if (mo != null) mo.Dispose(); }
        }

        /// <summary>Methode auf Win32_Service Name='name' (ChangeStartMode, StartService, StopService); Rueckgabe der ReturnValue.</summary>
        public static long? DienstMethode(string name, string methode, params object[] args)
        {
            return Wmi2.Ganz(Wmi2.InstanzMitZeit(DienstMs, Cimv2, "Win32_Service", "Name='" + Wmi2.Wql(name) + "'", methode, args));
        }

        /// <summary>Win32_Service-Rueckgabewerte (Doku "Win32_Service class", Methoden): 0 angenommen.</summary>
        public static string DienstFehler(long? rc)
        {
            if (!rc.HasValue) return "kein Rückgabewert";
            switch (rc.Value)
            {
                case 0: return "angenommen";
                case 1: return "nicht unterstützt (1)";
                case 2: return "Zugriff verweigert (2)";
                case 3: return "abhängige Dienste laufen (3)";
                case 4: return "ungültige Steueranforderung (4)";
                case 5: return "der Dienst nimmt gerade keine Anforderung an (5)";
                case 6: return "der Dienst ist nicht aktiv (6)";
                case 7: return "Zeitüberschreitung (7)";
                case 8: return "unbekannter Fehler (8)";
                case 9: return "Pfad nicht gefunden (9)";
                case 10: return "der Dienst läuft bereits (10)";
                case 11: return "Dienstdatenbank gesperrt (11)";
                case 12: return "eine Abhängigkeit wurde gelöscht (12)";
                case 13: return "eine Abhängigkeit konnte nicht gestartet werden (13)";
                case 14: return "der Dienst ist deaktiviert (14)";
                case 15: return "Anmeldung des Dienstkontos fehlgeschlagen (15)";
                case 16: return "der Dienst ist zum Löschen vorgemerkt (16)";
                case 17: return "kein Thread für den Dienst (17)";
                case 21: return "ungültiger Parameter (21)";
                case 22: return "ungültiges Dienstkonto (22)";
                default: return "Fehlercode " + rc.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// ChangeStartMode(ziel) und Nachmessung. Rueckgabe: null = Ziel steht, sonst der Satz, warum
        /// nicht (fuer NichtBestanden). Vom Rueckweg mit dem Vorwert gerufen.
        /// </summary>
        public static string DienstStartartSetzen(Ausfuehrungskontext k, string name, string ziel)
        {
            long? rc = DienstMethode(name, "ChangeStartMode", ziel);
            if (rc != 0) return "ChangeStartMode(" + ziel + ") meldete " + DienstFehler(rc) + ".";
            DienstStand nach = DienstLesen(name);
            if (!nach.Gefunden) return "Der Dienst „" + name + "“ ist nach dem Aufruf nicht mehr auffindbar.";
            if (StartartNorm(nach.Startart) != ziel) return "ChangeStartMode meldete Erfolg, Windows zeigt für „" + name + "“ aber weiter die Startart " + StartartWort(nach.Startart) + ".";
            return null;
        }

        public static void DienstStartart(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "leistung.dienst.startart";
            string name = DienstNameNorm(s.Wert("name"));
            string ziel = ZielStartartNorm(s.Wert("startart"));
            DienstStand stand = DienstLesen(name);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Der Dienst „" + name + "“ ist nicht vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string vorher = StartartNorm(stand.Startart) ?? stand.Startart ?? "";
            if (vorher == ziel)
            {
                SchonSo(k, kennung, "Der Dienst „" + name + "“ hatte schon die Startart " + StartartWort(ziel) + "; es wurde nichts geändert.");
                return;
            }
            k.Schreibe("   Dienst „" + name + "“: Startart " + StartartWort(vorher) + ", Zustand " + (stand.Zustand ?? "unbekannt") + ".", "dim");
            if (vorher == "Disabled" && NieDeaktiviert(name)) RueckwegUnmoeglich(k, name);

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("name", name, "startart", ziel),
                Sicherungsdatei.Werte("startart", vorher, "zustand", stand.Zustand ?? ""));

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI Win32_Service(" + name + ").ChangeStartMode(" + ziel + ")", "dim");
                Nachweise.Trocken(k, kennung, "Der Dienst „" + name + "“ würde auf Startart " + StartartWort(ziel) + " gestellt (vorher " + StartartWort(vorher) + ").");
                return;
            }
            string fehler = DienstStartartSetzen(k, name, ziel);
            if (fehler != null) Nachweise.NichtBestanden(k, kennung, fehler + " Die Startart bleibt " + StartartWort(vorher) + ".");
            else Nachweise.Bestanden(k, kennung, "Der Dienst „" + name + "“ hat jetzt die Startart " + StartartWort(ziel) + " (vorher " + StartartWort(vorher) + "); ein laufender Dienst läuft bis zum nächsten Neustart weiter.");
        }
    }
}
