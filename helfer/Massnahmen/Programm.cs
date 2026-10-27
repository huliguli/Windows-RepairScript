using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Programm (docs/M4-ENTWURF.md, Abschnitt 3.6): update.anwenden ersetzt die Austausch-Batch
        // per runas. Profilgebunden: der feste Quellpfad liegt im Profil des Helferkontos.
        static partial void Programm(List<Massnahme> l)
        {
            Massnahme m = Massnahmenbau.Neu(ProgrammMassnahmen.Kennung, "Programm-Update einrichten", 1,
                ProgrammMassnahmen.Pruefen, ProgrammMassnahmen.Anwenden);
            m.Profilgebunden = true;
            l.Add(m);
        }
    }

    /// <summary>
    /// Das ZIP-Update des Programms ueber den erhoehten Helfer (Vertrag M4, Abschnitt 3.6). Fester
    /// Quellpfad (kein Parameter): &lt;LocalApplicationData&gt;\WindowsWartung\update\update.zip des
    /// Helferkontos; Profilgebunden = true, ein fremdes Konto im UAC-Dialog wird vorher abgelehnt.
    ///
    /// Vorab: WindowsWartung.exe im Programmordner muss vorhanden sein und ihren Signaturstand
    /// eindeutig zeigen (WinVerifyTrust: 0 oder Kettenfehler = signiert, TRUST_E_NOSIGNATURE =
    /// Entwicklungsbau; jeder andere Code, ein Lesefehler oder eine fehlende Datei ist kein
    /// Nachweis, und ohne Nachweis gibt es keine Bindung und kein Update).
    ///
    /// Schritte: 1 die Quelle wird genau einmal geoeffnet (Handle mit FILE_FLAG_OPEN_REPARSE_POINT)
    /// und am Handle geprueft: kein Reparse-Punkt, keine Ordner, genau ein Name (kein Hardlink), der
    /// wirkliche Pfad ist der erwartete (eine Verknuepfung im Elternpfad faellt so auf), Laenge 1 Byte
    /// bis 256 MB; aus diesem Handle wird nach &lt;ProgramData&gt;\WindowsWartung\update\ kopiert
    /// (Ordner nur Administratoren und SYSTEM, Besitzer Administratoren, ueber ein offenes Handle
    /// festgenagelt, siehe Arbeitsordner) und dabei gehasht, 2 SHA-256 der kopierten Bytes ==
    /// Parameter sha256, 3 sicheres Entpacken in einen frischen Unterordner neu-&lt;4hex&gt; (kein
    /// Eintrag mit "..", absolutem Pfad oder ausserhalb des Ziels; jede entpackte Datei bleibt bis
    /// zum Tausch offen, damit niemand sie zwischen Pruefung und Kopie austauscht), 4
    /// WindowsWartung.exe vorhanden und UpdateTrust.PruefeHerausgeber (Name und Schluessel gegen die
    /// laufende EXE), 5 Version nie kleiner als die laufende, 6 die Liste ui-hashes.txt aus der
    /// NEUEN EXE (ReflectionOnlyLoadFrom in einer eigenen, danach entladenen AppDomain, nie LoadFrom;
    /// siehe ListenLeser) und Selbstpruefung.Pruefen(neueExe, neu\ui, liste) ohne Abweichung und
    /// ohne zusaetzliche Datei, dazu die Erlaubnisliste (PaketPruefen): im Stamm nur
    /// WindowsWartung.exe und die drei WebView2-DLLs mit gueltiger Microsoft-Signatur oder dem
    /// Schluessel der neuen EXE, unter ui\ nur die gelisteten Dateien, alles andere lehnt das Paket
    /// ab, 7 Tausch je Datei im Programmordner (Ziel → &lt;name&gt;.vorher, dann Kopie; scheitert
    /// etwas, kommt erst die Kopie beiseite, dann das Original zurueck, dann die Kopie weg: das
    /// Original ist nie ohne Namen), 8 Werte update.version, update.vorher, nachweis.bestanden = "1".
    /// Trockenlauf: Schritte 1 bis 6 laufen (ohne Pruefung gibt es keine Aussage), Schritt 7 nur
    /// als "(trocken)"-Zeilen je Datei, kein Move, keine Kopie.
    ///
    /// Signaturregel wie im Host: ist die laufende EXE signiert, muss die neue vom selben
    /// Herausgeber mit demselben Schluessel signiert und ihre Signatur intakt sein; ist die laufende
    /// EXE unsigniert (Entwicklungsbau), entfaellt die Bindung, und die Startpruefung verlangt nur
    /// UiStimmt. Die laufende EXE laesst sich umbenennen (Windows erlaubt MoveFile auf ein
    /// ausgefuehrtes Abbild, nicht das Loeschen); Helfer und Host laufen danach aus
    /// WindowsWartung.exe.vorher weiter, der naechste Helferstart raeumt die .vorher-Dateien weg
    /// (Aufraeumen). Ein Fehler in der Pruefsumme oder im Inhalt ist hier ein Problem (Exit 1) mit
    /// klarer Zeile: Exit 2 gibt es nur fuer den Parameter selbst (Pruefen, vor dem Lauf).
    /// </summary>
    internal static class ProgrammMassnahmen
    {
        public const string Kennung = "update.anwenden";
        const string ExeName = "WindowsWartung.exe";
        const string ZipName = "update.zip";
        const string NeuOrdner = "neu";
        const string UiOrdner = "ui";
        const string VorherEndung = ".vorher";
        const string NeuEndung = ".neu";
        const long ZipMaxBytes = 256L << 20;        // 256 MB: das Release wiegt unter 10 MB
        const long EntpacktMaxBytes = 1L << 30;     // 1 GB entpackt, Schutz gegen ein Zip-Bomben-Paket
        const int EintraegeMax = 5000;
        const string MicrosoftOrganisation = "O=Microsoft Corporation";

        // Was ausser WindowsWartung.exe im Stamm des Pakets liegen darf (release.yml: bin\*.dll).
        static readonly string[] StammDlls = { "WebView2Loader.dll", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll" };

        static readonly Regex Sha256Rx = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
        static readonly Regex AltVorherRx = new Regex(@"\.vorher\.[0-9a-f]{4}$", RegexOptions.Compiled);
        static readonly Regex AltNeuRx = new Regex(@"\.neu\.[0-9a-f]{4}$", RegexOptions.Compiled);

        public static string Pruefen(PlanSchritt s)
        {
            string v = s.Wert("sha256");
            if (string.IsNullOrEmpty(v) || !Sha256Rx.IsMatch(v.Trim()))
                return "Der Parameter sha256 muss die Prüfsumme des Pakets als 64 Hex-Zeichen enthalten, nicht „" + Massnahmenbau.Kurz(v) + "“.";
            return null;
        }

        /// <summary>Fester Quellpfad im Profil des Helferkontos (kein Parameter).</summary>
        public static string Quelle()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Ablage.Ordnername, "update", ZipName);
        }

        /// <summary>Arbeitsordner des Helfers: &lt;ProgramData&gt;\WindowsWartung\update.</summary>
        public static string Arbeitsordner()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Ablage.Ordnername, "update");
        }

        static string Programmordner()
        {
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }

        static string Hex4()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        // ================================================================ update.anwenden

        public static void Anwenden(PlanSchritt s, Ausfuehrungskontext k)
        {
            string erwartet = (s.Wert("sha256") ?? "").Trim().ToLowerInvariant();
            if (!Sha256Rx.IsMatch(erwartet)) throw new InvalidOperationException("Der Parameter sha256 ist nach der Prüfung nicht mehr gültig.");

            // Nur die ZIP-Installation tauscht Dateien; eine Installer-Fassung bekommt ihr Update
            // ueber setup.exe (Vertrag 3.6), sonst bliebe der Eintrag unter "Apps und Features" alt.
            string installOrt;
            if (UpdateTrust.IstPerInstallerInstalliert(out installOrt))
            {
                Fehler(k, "Diese Fassung wurde über den Installer eingerichtet (" + installOrt + "); ihr Update läuft über setup.exe, nicht über den Dateitausch. Es wurde nichts geändert.", new { installOrt });
                return;
            }

            // Die laufende EXE muss da sein und ihren Signaturstand eindeutig zeigen, sonst gibt es
            // nichts, woran der Herausgeber gebunden wird (ein nicht lesbarer Wert ist kein Nachweis).
            string programm = Programmordner();
            string laufendeExe = Path.Combine(programm, ExeName);
            string grundLaufend;
            bool? signaturLaufend = LaufendSigniert(laufendeExe, out grundLaufend);
            if (signaturLaufend == null)
            {
                Fehler(k, grundLaufend, new { laufendeExe });
                return;
            }
            bool laufendSigniert = signaturLaufend.Value;
            string versionAlt = Version(laufendeExe) ?? typeof(ProgrammMassnahmen).Assembly.GetName().Version.ToString();

            var offen = new List<FileStream>();
            Nagel ordner = null;
            FileStream quelle = null;
            FileStream zip = null;
            try
            {
                // 1 Quelle am Handle pruefen und in den festgenagelten Arbeitsordner kopieren.
                k.Melde(1, 4, "Das Paket wird geprüft");
                quelle = QuelleOeffnen(k, Quelle());
                if (quelle == null) return;
                ordner = Arbeitsordner(k);
                if (ordner == null) return;
                string lauf = Hex4();
                string kopie = Path.Combine(ordner.Pfad, "update-" + lauf + ".zip");
                zip = new FileStream(kopie, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                string ist = Kopieren(quelle, zip);
                quelle.Dispose();
                quelle = null;
                k.Schreibe("   Paket kopiert: " + kopie + " (" + (zip.Length / 1024) + " KB).", "dim");

                // 2 Pruefsumme der kopierten Bytes (dieselben, aus denen gleich entpackt wird).
                if (!string.Equals(ist, erwartet, StringComparison.OrdinalIgnoreCase))
                {
                    Fehler(k, "Prüfsumme: das Paket hat die SHA-256 " + ist.Substring(0, 16) + "…, erwartet war " + erwartet.Substring(0, 16) + "…; es wurde nichts geändert.", new { ist = ist.Substring(0, 16), erwartet });
                    return;
                }
                k.Schreibe("   Prüfsumme stimmt (SHA-256 " + ist.Substring(0, 16) + "…).", "dim");

                // 3 Entpacken in einen frischen Unterordner, jede Datei bleibt offen.
                k.Melde(2, 4, "Das Paket wird entpackt");
                string neu = Path.Combine(ordner.Pfad, NeuOrdner + "-" + lauf);
                zip.Position = 0;
                string grund = Entpacken(zip, neu, offen);
                if (grund != null)
                {
                    Fehler(k, "Das Paket wird nicht entpackt: " + grund + " Es wurde nichts geändert.", new { grund });
                    return;
                }
                k.Schreibe("   " + offen.Count + " Dateien entpackt nach " + neu + ".", "dim");

                // 4 Programmdatei und Herausgeber.
                k.Melde(3, 4, "Herausgeber, Version und Inhalt werden geprüft");
                string neueExe = Path.Combine(neu, ExeName);
                if (!File.Exists(neueExe))
                {
                    Fehler(k, "Das Paket enthält keine " + ExeName + "; es wurde nichts geändert.", null);
                    return;
                }
                string herkunft = UpdateTrust.PruefeHerausgeber(neueExe);
                if (herkunft != null)
                {
                    Fehler(k, herkunft, new { neueExe });
                    return;
                }
                k.Schreibe(laufendSigniert
                    ? "   Herausgeber bestätigt: Name und öffentlicher Schlüssel der neuen Fassung stimmen mit der laufenden überein."
                    : "   Entwicklungsbau ohne Bindung: die laufende Fassung ist nicht signiert, ein Herausgeber wird nicht verlangt.", "dim");

                // 5 Version nie kleiner.
                string versionNeu = Version(neueExe);
                if (versionNeu == null)
                {
                    Fehler(k, "Die neue " + ExeName + " trägt keine lesbare Version; es wurde nichts geändert.", new { neueExe });
                    return;
                }
                if (new System.Version(versionNeu) < new System.Version(versionAlt))
                {
                    Fehler(k, "Kein Rückschritt: das Paket enthält Fassung " + versionNeu + ", installiert ist " + versionAlt + "; es wurde nichts geändert.", new { versionNeu, versionAlt });
                    return;
                }
                k.Schreibe("   Fassung im Paket: " + versionNeu + ", installiert: " + versionAlt + ".", "dim");

                // 6 Startpruefung der neuen Fassung gegen ihre eigene Liste, dann die Erlaubnisliste.
                string pruefung = Startpruefung(neueExe, Path.Combine(neu, UiOrdner), laufendSigniert);
                if (pruefung != null)
                {
                    Fehler(k, "Startprüfung der neuen Fassung: " + pruefung + " Es wurde nichts geändert.", new { pruefung });
                    return;
                }
                k.Schreibe("   Startprüfung der neuen Fassung bestanden (Signatur " + (laufendSigniert ? "intakt" : "nicht verlangt") + ", Oberflächendateien stimmen mit der Liste überein).", "dim");
                string fremd = PaketPruefen(neu, offen, neueExe);
                if (fremd != null)
                {
                    Fehler(k, fremd, new { fremd });
                    return;
                }
                k.Schreibe("   Inhalt bestätigt: " + offen.Count + " Dateien, alle gehören zum Programm (Programmdatei, WebView2-Bibliotheken mit Signatur, gelistete Oberflächendateien).", "dim");

                // 7 Tausch.
                k.Melde(4, 4, "Die Programmdateien werden getauscht");
                var dateien = new List<string>();
                foreach (FileStream fs in offen) dateien.Add(fs.Name);
                if (k.Trocken)
                {
                    foreach (string pfad in dateien)
                    {
                        string rel = Relativ(neu, pfad);
                        string ziel = Path.Combine(programm, rel);
                        k.Schreibe("(trocken) " + rel + (File.Exists(ziel) ? " → " + rel + VorherEndung + ", dann Kopie" : " neu kopieren"), "dim");
                    }
                    if (k.Protokoll != null)
                        k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, Kennung, "(trocken) " + dateien.Count + " Dateien würden getauscht", new { dateien = dateien.Count, versionNeu, versionAlt, trocken = true });
                    Nachweise.Trocken(k, Kennung, "Fassung " + versionNeu + " würde eingerichtet, davor " + versionAlt + " (" + dateien.Count + " Dateien).");
                    return;
                }
                string tausch = Tauschen(k, neu, programm, offen);
                if (tausch != null)
                {
                    Fehler(k, tausch, new { versionNeu, versionAlt });
                    return;
                }

                // 8 Werte und Nachweis.
                k.Werte["update.version"] = versionNeu;
                k.Werte["update.vorher"] = versionAlt;
                if (k.Protokoll != null)
                    k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Schritt, Kennung, "Fassung " + versionNeu + " eingerichtet, davor " + versionAlt, new { dateien = dateien.Count, versionNeu, versionAlt, programm });
                Nachweise.Bestanden(k, Kennung, "Fassung " + versionNeu + " ist eingerichtet, davor " + versionAlt + "; " + dateien.Count + " Dateien getauscht, die bisherigen liegen als " + VorherEndung + " daneben bis zum nächsten Start mit Administratorrechten.");
            }
            finally
            {
                foreach (FileStream fs in offen) { try { fs.Dispose(); } catch (Exception) { } }
                if (zip != null) { try { zip.Dispose(); } catch (Exception) { } }
                if (quelle != null) { try { quelle.Dispose(); } catch (Exception) { } }
                if (ordner != null) ordner.Dispose();
            }
        }

        static void Fehler(Ausfuehrungskontext k, string satz, object daten)
        {
            k.Schreibe("   " + satz, "bad");
            if (k.Protokoll != null) k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.FehlerArt, Kennung, satz, daten);
            AppLog.Warn("update.anwenden: " + satz);
            Katalog.Problem(k);
        }

        // ---------------------------------------------------------------- laufende EXE

        /// <summary>
        /// Signaturstand der laufenden EXE: true = signiert (WinVerifyTrust 0 oder Kettenfehler,
        /// Herausgeber und Schluessel lesbar), false = unsigniert (genau TRUST_E_NOSIGNATURE). Eine
        /// fehlende Datei, ein Lesefehler oder jeder andere Code ist kein Nachweis: null und grund.
        /// </summary>
        static bool? LaufendSigniert(string laufendeExe, out string grund)
        {
            grund = null;
            if (!File.Exists(laufendeExe))
            {
                grund = ExeName + " fehlt im Programmordner, der Herausgeber lässt sich nicht binden; es wurde nichts geändert.";
                return null;
            }
            int code;
            try { code = Selbstpruefung.SignaturPruefen(laufendeExe); }
            catch (Exception ex)
            {
                grund = "Die Signatur der laufenden " + ExeName + " ließ sich nicht prüfen (" + ex.Message + "); der Herausgeber lässt sich nicht binden, es wurde nichts geändert.";
                return null;
            }
            if (code == Selbstpruefung.TRUST_E_NOSIGNATURE) return false;
            if (code != 0 && !Selbstpruefung.IstKettenfehler(code))
            {
                grund = "Die Signatur der laufenden " + ExeName + " ist beschädigt (Code 0x" + code.ToString("X8") + "); der Herausgeber lässt sich nicht binden, es wurde nichts geändert.";
                return null;
            }
            // PruefeHerausgeber liest Name und Schluessel ueber das Zertifikat; liefert das nichts,
            // liesse die Funktion die Bindung still entfallen. Deshalb hier verlangen.
            if (UpdateTrust.Signierer(laufendeExe) == null || UpdateTrust.OeffentlicherSchluessel(laufendeExe) == null)
            {
                grund = "Der Herausgeber der laufenden " + ExeName + " ist nicht lesbar, obwohl sie signiert ist; es wurde nichts geändert.";
                return null;
            }
            return true;
        }

        // ---------------------------------------------------------------- Quelle

        /// <summary>
        /// Oeffnet update.zip genau einmal (Reparse-Punkt selbst statt Ziel, Freigabe nur Lesen) und
        /// prueft am Handle: kein Reparse-Punkt, kein Ordner, genau ein Name, der wirkliche Pfad ist
        /// der erwartete (eine Verknuepfung im Elternpfad fuehrt woandershin), 1 Byte bis 256 MB.
        /// Rueckgabe der Lesestrom auf diesem Handle; null nach einer bad-Zeile.
        /// </summary>
        static FileStream QuelleOeffnen(Ausfuehrungskontext k, string quelle)
        {
            int fehler;
            SafeFileHandle h = Native.DateiOeffnen(quelle, out fehler);
            if (h == null)
            {
                if (fehler == Native.ERROR_FILE_NOT_FOUND || fehler == Native.ERROR_PATH_NOT_FOUND)
                    Fehler(k, "update.zip fehlt: " + quelle + " ist nicht vorhanden; es wurde nichts geändert.", new { quelle });
                else
                    Fehler(k, "update.zip ließ sich nicht öffnen (Win32-Fehler " + fehler + "); es wurde nichts geändert.", new { quelle, fehler });
                return null;
            }
            try
            {
                Native.BY_HANDLE_FILE_INFORMATION info;
                if (!Native.GetFileInformationByHandle(h, out info))
                    throw new InvalidOperationException("ihre Attribute sind nicht lesbar (Win32-Fehler " + Marshal.GetLastWin32Error() + ")");
                if ((info.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("sie ist eine Verknüpfung auf einen anderen Ort");
                if ((info.dwFileAttributes & (uint)FileAttributes.Directory) != 0)
                    throw new InvalidOperationException("dort liegt ein Ordner statt einer Datei");
                if (info.nNumberOfLinks != 1)
                    throw new InvalidOperationException("sie hat " + info.nNumberOfLinks + " Namen (Hardlink) statt genau einem");
                string wirklich = Native.Kanonisch(h);
                string erwartet = Path.GetFullPath(quelle);
                if (wirklich == null || !string.Equals(wirklich, erwartet, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("sie führt nach „" + (wirklich ?? "?") + "“ statt nach „" + erwartet + "“ (eine Verknüpfung im Pfad)");
                long laenge = ((long)info.nFileSizeHigh << 32) | info.nFileSizeLow;
                if (laenge == 0 || laenge > ZipMaxBytes)
                    throw new InvalidOperationException("sie hat " + laenge + " Byte; erlaubt sind 1 Byte bis " + (ZipMaxBytes >> 20) + " MB");
                var fs = new FileStream(h, FileAccess.Read);
                h = null;
                return fs;
            }
            catch (Exception ex)
            {
                Fehler(k, "update.zip wird nicht gelesen: " + ex.Message + "; es wurde nichts geändert.", new { quelle, fehler = ex.Message });
                return null;
            }
            finally { if (h != null) h.Dispose(); }
        }

        /// <summary>Kopiert quelle nach ziel und hasht genau die kopierten Bytes; Rueckgabe SHA-256 als kleine Hex-Zeichen.</summary>
        static string Kopieren(Stream quelle, FileStream ziel)
        {
            var puffer = new byte[81920];
            using (var sha = SHA256.Create())
            {
                int n;
                while ((n = quelle.Read(puffer, 0, puffer.Length)) > 0)
                {
                    sha.TransformBlock(puffer, 0, n, null, 0);
                    ziel.Write(puffer, 0, n);
                }
                sha.TransformFinalBlock(puffer, 0, 0);
                ziel.Flush();
                var sb = new StringBuilder(64);
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Dateiversion (FileVersionInfo) als "a.b.c.d" oder null, wenn keine lesbar ist.</summary>
        static string Version(string exe)
        {
            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exe);
                if (fvi == null || string.IsNullOrEmpty(fvi.FileVersion)) return null;
                return new System.Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart).ToString();
            }
            catch (Exception) { return null; }
        }

        static string Relativ(string wurzel, string pfad)
        {
            string w = wurzel.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return pfad.StartsWith(w, StringComparison.OrdinalIgnoreCase) ? pfad.Substring(w.Length) : Path.GetFileName(pfad);
        }

        // ---------------------------------------------------------------- Arbeitsordner

        /// <summary>Der festgenagelte Arbeitsordner: Pfad plus offenes Handle (FILE_LIST_DIRECTORY, Freigabe nur Lesen und Schreiben, kein Loeschen).</summary>
        sealed class Nagel : IDisposable
        {
            public string Pfad;
            public SafeFileHandle Handle;
            public void Dispose() { if (Handle != null) Handle.Dispose(); }
        }

        /// <summary>
        /// Legt &lt;ProgramData&gt;\WindowsWartung\update an (Besitzer Administratoren, eigene DACL nur
        /// Administratoren und SYSTEM, Vererbung aus) und nagelt ihn fest: ein Handle mit
        /// FILE_LIST_DIRECTORY und Freigabe ohne Loeschen laesst den Ordner und seine Eltern nicht
        /// umbenennen (gemessen 18.09.2026). Das Handle wird mit FILE_FLAG_OPEN_REPARSE_POINT
        /// geoeffnet, damit eine untergeschobene Verknuepfung als solche auffaellt; am Handle werden
        /// Attribute (kein Reparse-Punkt, Ordner) und der wirkliche Pfad (GetFinalPathNameByHandle)
        /// geprueft, danach Besitzer und DACL. Ein vorhandener Ordner, der nicht diese Herkunft traegt
        /// (Verknuepfung, fremder Besitzer, andere Rechte), wird nicht angefasst und nicht benutzt:
        /// die Zeile nennt ihn, der Betreiber entfernt ihn. Ein eigener wird geleert; was sich nicht
        /// loeschen laesst (eine Datei aus einem frueheren Lauf ist noch in Benutzung), bleibt liegen
        /// und stoert nicht, weil jeder Lauf seinen eigenen Unterordner bekommt. null = abgelehnt.
        /// </summary>
        static Nagel Arbeitsordner(Ausfuehrungskontext k)
        {
            string pfad = Arbeitsordner();
            SafeFileHandle h = null;
            try
            {
                // Der Laufzeitordner darueber bleibt fuer Benutzer beschreibbar (Ablage.RechteSichern);
                // CreateDirectory mit Sicherheitsbeschreibung legte ihn sonst gleich mit an, nur fuer
                // Administratoren, und der nicht erhoehte Host kaeme nicht mehr an seine Dateien.
                string eltern = Path.GetDirectoryName(pfad);
                if (!string.IsNullOrEmpty(eltern) && !Directory.Exists(eltern))
                {
                    Directory.CreateDirectory(eltern);
                    Ablage.RechteSichern(eltern);
                }
                bool vorhanden = Directory.Exists(pfad) || File.Exists(pfad);
                if (!vorhanden) Directory.CreateDirectory(pfad, Sicherheit());

                int fehler;
                h = Native.OrdnerOeffnen(pfad, out fehler);
                if (h == null || h.IsInvalid)
                    throw new InvalidOperationException("er ließ sich nicht öffnen (Win32-Fehler " + fehler + ")");

                Native.BY_HANDLE_FILE_INFORMATION info;
                if (!Native.GetFileInformationByHandle(h, out info))
                    throw new InvalidOperationException("seine Attribute sind nicht lesbar (Win32-Fehler " + Marshal.GetLastWin32Error() + ")");
                if ((info.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("er ist eine Verknüpfung auf einen anderen Ort");
                if ((info.dwFileAttributes & (uint)FileAttributes.Directory) == 0)
                    throw new InvalidOperationException("dort liegt eine Datei statt eines Ordners");

                string wirklich = Native.Kanonisch(h);
                string erwartet = Path.GetFullPath(pfad).TrimEnd(Path.DirectorySeparatorChar);
                if (wirklich == null || !string.Equals(wirklich.TrimEnd(Path.DirectorySeparatorChar), erwartet, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("er führt nach „" + (wirklich ?? "?") + "“ statt nach „" + erwartet + "“");

                // Festgenagelt: ab hier kann der Ordner weder umbenannt noch ersetzt werden; die
                // Rechte lassen sich deshalb ueber den Pfad lesen.
                DirectorySecurity sec = Directory.GetAccessControl(pfad, AccessControlSections.Owner | AccessControlSections.Access);
                var besitzer = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (besitzer == null || !(besitzer.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || besitzer.IsWellKnown(WellKnownSidType.LocalSystemSid)))
                    throw new InvalidOperationException("sein Besitzer ist " + (besitzer == null ? "nicht lesbar" : besitzer.Value) + " statt Administratoren");
                if (!DaclStimmt(sec))
                    throw new InvalidOperationException("seine Rechte sind nicht genau Administratoren und SYSTEM (Vererbung aus)");

                int geblieben = Leeren(pfad);
                if (geblieben > 0)
                    k.Schreibe("   " + geblieben + (geblieben == 1 ? " Eintrag" : " Einträge") + " aus einem früheren Lauf im Arbeitsordner " + pfad + " noch in Benutzung; " + (geblieben == 1 ? "er bleibt" : "sie bleiben") + " liegen, bis der Helfer beendet (Programm geschlossen) und erneut gestartet wird.", "dim");
                var nagel = new Nagel { Pfad = pfad, Handle = h };
                h = null;
                return nagel;
            }
            catch (Exception ex)
            {
                Fehler(k, "Der Arbeitsordner " + pfad + " wird nicht benutzt: " + ex.Message + ". Bitte den Ordner entfernen und das Update erneut starten; es wurde nichts geändert.", new { pfad, fehler = ex.Message });
                return null;
            }
            finally { if (h != null) h.Dispose(); }
        }

        static readonly SecurityIdentifier SidAdministratoren = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        static readonly SecurityIdentifier SidSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        /// <summary>Besitzer Administratoren, Vererbung aus, Administratoren und SYSTEM Vollzugriff (vererbend), sonst niemand.</summary>
        static DirectorySecurity Sicherheit()
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            InheritanceFlags erben = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(SidAdministratoren, FileSystemRights.FullControl, erben, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(SidSystem, FileSystemRights.FullControl, erben, PropagationFlags.None, AccessControlType.Allow));
            sec.SetOwner(SidAdministratoren);
            return sec;
        }

        /// <summary>Genau die DACL aus Sicherheit(): geschuetzt, zwei Allow-Eintraege (Administratoren, SYSTEM) mit Vollzugriff, nichts Geerbtes, nichts sonst.</summary>
        static bool DaclStimmt(DirectorySecurity sec)
        {
            if (!sec.AreAccessRulesProtected) return false;
            bool admins = false, system = false;
            int n = 0;
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                n++;
                if (r.IsInherited || r.AccessControlType != AccessControlType.Allow) return false;
                if ((r.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl) return false;
                var sid = r.IdentityReference as SecurityIdentifier;
                if (sid == null) return false;
                if (sid.Equals(SidAdministratoren)) admins = true;
                else if (sid.Equals(SidSystem)) system = true;
                else return false;
            }
            return n == 2 && admins && system;
        }

        /// <summary>
        /// Leert den eigenen (festgenagelten, nur Administratoren beschreibbaren) Ordner; eine
        /// Verknuepfung darin wird nur selbst entfernt, nie verfolgt. Was sich nicht loeschen laesst
        /// (noch in Benutzung), bleibt mit einer Zeile im app.log liegen. Rueckgabe: wie viele
        /// Eintraege geblieben sind.
        /// </summary>
        static int Leeren(string ordner)
        {
            int geblieben = 0;
            foreach (string eintrag in Directory.GetFileSystemEntries(ordner))
            {
                try
                {
                    FileAttributes fa = File.GetAttributes(eintrag);
                    if ((fa & FileAttributes.Directory) != 0)
                    {
                        if ((fa & FileAttributes.ReparsePoint) != 0) Directory.Delete(eintrag);
                        else Directory.Delete(eintrag, true);
                    }
                    else
                    {
                        File.SetAttributes(eintrag, FileAttributes.Normal);
                        File.Delete(eintrag);
                    }
                }
                catch (Exception ex)
                {
                    geblieben++;
                    AppLog.Warn("Update-Arbeitsordner: " + eintrag + " bleibt liegen (noch in Benutzung): " + ex.Message);
                }
            }
            return geblieben;
        }

        // ---------------------------------------------------------------- Entpacken

        /// <summary>
        /// Entpackt das Archiv nach ziel. Jeder Eintrag muss unter ziel bleiben: kein absoluter Pfad,
        /// kein Segment "." oder "..", keine ungueltigen Zeichen, kein Doppelpunkt (Datenstrom), kein
        /// Segment mit Punkt oder Leerzeichen am Ende, hoechstens 5000 Eintraege und 1 GB. Jede Datei
        /// wird nach dem Schreiben lesend geoeffnet (FileShare.Read) und in offen gehalten.
        /// Rueckgabe null = alles entpackt, sonst der Grund der Ablehnung.
        /// </summary>
        static string Entpacken(Stream zip, string ziel, List<FileStream> offen)
        {
            string wurzel = Path.GetFullPath(ziel).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(ziel);
            long gesamt = 0;
            int eintraege = 0;
            var puffer = new byte[81920];
            using (var archiv = new ZipArchive(zip, ZipArchiveMode.Read, true))
            {
                foreach (ZipArchiveEntry e in archiv.Entries)
                {
                    if (++eintraege > EintraegeMax) return "das Paket hat mehr als " + EintraegeMax + " Einträge.";
                    string name = (e.FullName ?? "").Replace('\\', '/');
                    if (name.Length == 0) return "ein Eintrag ohne Namen.";
                    bool ordner = name.EndsWith("/", StringComparison.Ordinal);
                    string grund = NamePruefen(name.TrimEnd('/'));
                    if (grund != null) return "der Eintrag „" + Massnahmenbau.Kurz(name) + "“ " + grund;
                    string voll = Path.GetFullPath(Path.Combine(ziel, name.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar)));
                    if (!voll.StartsWith(wurzel, StringComparison.OrdinalIgnoreCase)) return "der Eintrag „" + Massnahmenbau.Kurz(name) + "“ läge außerhalb des Zielordners.";
                    if (ordner)
                    {
                        Directory.CreateDirectory(voll);
                        continue;
                    }
                    if (e.Length < 0 || e.Length > EntpacktMaxBytes) return "der Eintrag „" + Massnahmenbau.Kurz(name) + "“ meldet " + e.Length + " Byte.";
                    Directory.CreateDirectory(Path.GetDirectoryName(voll));
                    long geschrieben = 0;
                    using (Stream rein = e.Open())
                    using (var raus = new FileStream(voll, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        int n;
                        while ((n = rein.Read(puffer, 0, puffer.Length)) > 0)
                        {
                            geschrieben += n;
                            gesamt += n;
                            if (gesamt > EntpacktMaxBytes) return "das Paket entpackt sich auf mehr als " + (EntpacktMaxBytes >> 20) + " MB.";
                            raus.Write(puffer, 0, n);
                        }
                    }
                    if (geschrieben != e.Length) return "der Eintrag „" + Massnahmenbau.Kurz(name) + "“ ist " + geschrieben + " statt " + e.Length + " Byte lang.";
                    offen.Add(new FileStream(voll, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
            }
            if (offen.Count == 0) return "das Paket enthält keine Datei.";
            return null;
        }

        static readonly char[] UngueltigeZeichen = Path.GetInvalidFileNameChars();

        /// <summary>null, wenn der relative Name (mit "/") unter dem Ziel bleibt; sonst der Grund (Satz ohne Subjekt).</summary>
        static string NamePruefen(string name)
        {
            if (name.Length == 0) return "hat keinen Namen.";
            if (name.Length > 1000) return "ist länger als 1000 Zeichen.";
            if (name.StartsWith("/", StringComparison.Ordinal)) return "beginnt mit einem Trenner (absoluter Pfad).";
            if (name.IndexOf(':') >= 0) return "enthält einen Doppelpunkt (Laufwerk oder Datenstrom).";
            foreach (string segment in name.Split('/'))
            {
                if (segment.Length == 0) return "enthält einen leeren Pfadteil.";
                if (segment == "." || segment == "..") return "enthält „" + segment + "“ als Pfadteil.";
                if (segment.IndexOfAny(UngueltigeZeichen) >= 0) return "enthält ein Zeichen, das in Dateinamen nicht erlaubt ist.";
                if (segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)) return "hat einen Pfadteil, der mit Punkt oder Leerzeichen endet.";
            }
            return null;
        }

        // ---------------------------------------------------------------- Startpruefung

        /// <summary>
        /// Liest die Ressource ui-hashes.txt einer EXE in einer eigenen AppDomain. Grund:
        /// ReflectionOnlyLoadFrom haelt die Datei bis zum Ende der Domaene gemappt (gemessen: die
        /// entpackte EXE liess sich danach nicht loeschen) und nimmt dieselbe Assembly-Identitaet
        /// je Domaene nur einmal an (eine zweite Fassung mit gleicher Versionsnummer wirft
        /// FileLoadException, auch von einem anderen Pfad). Eine Domaene je Lesen, danach entladen,
        /// loest beides. Kein Code der neuen Fassung laeuft: ReflectionOnlyLoadFrom, nie LoadFrom.
        /// </summary>
        sealed class ListenLeser : MarshalByRefObject
        {
            public byte[] Lesen(string exe)
            {
                Assembly a = Assembly.ReflectionOnlyLoadFrom(exe);
                using (Stream s = a.GetManifestResourceStream(Selbstpruefung.RessourcenName))
                {
                    if (s == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
        }

        /// <summary>Die Liste aus der EXE (siehe ListenLeser); null, wenn die EXE keine traegt. Wirft bei einer EXE, die sich nicht als Programm lesen laesst.</summary>
        static byte[] ListeLesen(string exe)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = Path.GetDirectoryName(typeof(ListenLeser).Assembly.Location),
                DisallowCodeDownload = true
            };
            AppDomain d = AppDomain.CreateDomain("update.liste", null, setup);
            try
            {
                var leser = (ListenLeser)d.CreateInstanceAndUnwrap(typeof(ListenLeser).Assembly.FullName, typeof(ListenLeser).FullName, false,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance, null, null, null, null);
                return leser.Lesen(exe);
            }
            finally
            {
                try { AppDomain.Unload(d); }
                catch (Exception ex) { AppLog.Warn("update.anwenden: die Lesedomäne ließ sich nicht entladen: " + ex.Message); }
            }
        }

        /// <summary>
        /// Liste ui-hashes.txt aus der NEUEN EXE (ListeLesen) und Selbstpruefung.Pruefen(neueExe,
        /// uiOrdner, liste). null = ohne Abweichung, ohne Datei unter ui\ ausserhalb der Liste und,
        /// wenn die laufende Fassung signiert ist, Signatur vorhanden und intakt; sonst der Grund.
        /// </summary>
        static string Startpruefung(string neueExe, string uiOrdner, bool signaturVerlangt)
        {
            byte[] liste;
            try { liste = ListeLesen(neueExe); }
            catch (Exception ex)
            {
                return "die neue " + ExeName + " ließ sich nicht als Programm lesen (" + ex.Message + ").";
            }
            if (liste == null) return "die neue " + ExeName + " trägt keine Prüfliste " + Selbstpruefung.RessourcenName + ".";
            Selbstpruefung.Ergebnis e;
            using (var ms = new MemoryStream(liste, false)) { e = Selbstpruefung.Pruefen(neueExe, uiOrdner, ms); }
            if (!e.UiStimmt || e.Abweichungen.Count > 0)
                return e.Abweichungen.Count + " Abweichung(en): " + Massnahmenbau.Kurz(string.Join("; ", e.Abweichungen)) + ".";
            if (e.Zusaetzlich.Count > 0)
                return e.Zusaetzlich.Count + " Datei(en) unter ui, die nicht in der Liste der neuen Fassung stehen: " + Massnahmenbau.Kurz(string.Join("; ", e.Zusaetzlich)) + ".";
            if (signaturVerlangt && !(e.Signiert && e.SignaturGueltig))
                return "die Signatur der neuen Fassung ist " + (e.Signiert ? "beschädigt (Code 0x" + e.SignaturCode.ToString("X8") + ")" : "nicht vorhanden") + ", die laufende Fassung ist signiert.";
            return null;
        }

        // ---------------------------------------------------------------- Erlaubnisliste

        /// <summary>
        /// Was in den Programmordner darf: im Stamm nur WindowsWartung.exe (an Herausgeber und
        /// Startpruefung gebunden) und die drei WebView2-DLLs, jede nur mit gueltiger
        /// Authenticode-Kette und Microsoft als Organisation im Zertifikat oder mit intakter Signatur
        /// und demselben oeffentlichen Schluessel wie die neue EXE; unter ui\ nur Dateien, die die
        /// Liste der neuen Fassung nennt (Zusaetzlich ist leer, Startpruefung) und keine
        /// Bildschirmfotos (die Selbstpruefung zaehlt shot_*.png nicht als zusaetzlich, hier zaehlen
        /// sie als fremd). Jeder andere Eintrag: der Grund, das Paket bleibt liegen, nichts wird
        /// getauscht. Warum: eine DLL im Stamm laedt der naechste erhoehte Start aus dem
        /// Programmordner (wintrust, profapi, cryptbase sind keine KnownDLLs), und eine Datei
        /// ausserhalb der Liste haette keinen Nachweis.
        /// </summary>
        static string PaketPruefen(string neu, List<FileStream> offen, string neueExe)
        {
            string schluesselNeu = UpdateTrust.OeffentlicherSchluessel(neueExe);
            string uiPraefix = UiOrdner + Path.DirectorySeparatorChar;
            foreach (FileStream fs in offen)
            {
                string rel = Relativ(neu, fs.Name);
                if (rel.IndexOf(Path.DirectorySeparatorChar) < 0)
                {
                    if (string.Equals(rel, ExeName, StringComparison.OrdinalIgnoreCase)) continue;
                    bool bekannt = false;
                    foreach (string dll in StammDlls) if (string.Equals(dll, rel, StringComparison.OrdinalIgnoreCase)) { bekannt = true; break; }
                    if (!bekannt) return Fremd(rel);
                    string grund = DllPruefen(fs.Name, rel, schluesselNeu);
                    if (grund != null) return grund;
                    continue;
                }
                if (rel.StartsWith(uiPraefix, StringComparison.OrdinalIgnoreCase) && !IstBildschirmfoto(Path.GetFileName(rel))) continue;
                return Fremd(rel);
            }
            return null;
        }

        static string Fremd(string rel)
        {
            return "Das Paket enthält eine Datei, die nicht zum Programm gehört: " + Massnahmenbau.Kurz(rel) + "; es wurde nichts geändert.";
        }

        // Wie Selbstpruefung.IstBildschirmfoto (dort privat): Belegaufnahmen sind kein Programmteil.
        static bool IstBildschirmfoto(string name)
        {
            return name.StartsWith("shot_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// null, wenn die DLL vertrauenswuerdig ist: WinVerifyTrust 0 und O=Microsoft Corporation im
        /// Zertifikat, oder Signatur intakt (0 oder Kettenfehler) mit dem Schluessel der neuen EXE.
        /// Ein Kettenfehler allein reicht fuer Microsoft nicht; ein nicht lesbarer Schluessel zaehlt
        /// nie als gleich. Sonst der Grund.
        /// </summary>
        static string DllPruefen(string pfad, string rel, string schluesselNeu)
        {
            int code;
            try { code = Selbstpruefung.SignaturPruefen(pfad); }
            catch (Exception ex)
            {
                return "Die Signatur von " + rel + " im Paket ließ sich nicht prüfen (" + ex.Message + "); es wurde nichts geändert.";
            }
            if (code == 0 && IstMicrosoft(UpdateTrust.Signierer(pfad))) return null;
            bool intakt = code == 0 || Selbstpruefung.IstKettenfehler(code);
            if (intakt && schluesselNeu != null)
            {
                string schluessel = UpdateTrust.OeffentlicherSchluessel(pfad);
                if (schluessel != null && string.Equals(schluessel, schluesselNeu, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return "Die Datei " + rel + " im Paket trägt weder eine gültige Signatur von Microsoft noch den Schlüssel der neuen " + ExeName + " (Signaturcode 0x" + code.ToString("X8") + "); es wurde nichts geändert.";
        }

        // Organisation im Antragstellernamen des Zertifikats, Bestandteil fuer Bestandteil (kein Teilstring).
        static bool IstMicrosoft(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return false;
            foreach (string teil in subject.Split(','))
                if (string.Equals(teil.Trim(), MicrosoftOrganisation, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---------------------------------------------------------------- Tausch

        sealed class Tausch
        {
            public string Ziel;
            public string Vorher;      // umbenanntes Original oder null
            public bool Kopiert;
        }

        /// <summary>
        /// Tauscht jede entpackte Datei in den Programmordner: vorhandenes Ziel → Ziel.vorher (ein
        /// altes .vorher wird geloescht; geht das nicht, weil noch ein Prozess daraus laeuft, wird es
        /// nach .vorher.&lt;4hex&gt; umbenannt und beim naechsten Neustart geloescht), dann die Kopie aus
        /// dem offenen Handle. Scheitert ein Schritt, geht es rueckwaerts: die neue Kopie erst nach
        /// &lt;name&gt;.neu.&lt;4hex&gt; beiseite, dann das Original von .vorher zurueck, dann die Kopie
        /// weg; so ist das Original nie ohne Namen, auch wenn ein Prozess des Nutzers eine Datei
        /// haelt. Rueckgabe null = getauscht, sonst der Satz fuer die bad-Zeile ("nichts geändert"
        /// oder "zurückgerollt").
        /// </summary>
        static string Tauschen(Ausfuehrungskontext k, string neu, string programm, List<FileStream> offen)
        {
            string wurzel = Path.GetFullPath(programm).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var getan = new List<Tausch>();
            var ordnerNeu = new List<string>();
            string fehler = null;
            string datei = null;
            try
            {
                foreach (FileStream fs in offen)
                {
                    if (k.IstAbgebrochen) { fehler = "abgebrochen"; break; }
                    string rel = Relativ(neu, fs.Name);
                    datei = rel;
                    string ziel = Path.GetFullPath(Path.Combine(programm, rel));
                    if (!ziel.StartsWith(wurzel, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("„" + rel + "“ läge außerhalb des Programmordners.");
                    string zielOrdner = Path.GetDirectoryName(ziel);
                    OhneAbzweigung(zielOrdner, wurzel, rel);
                    OrdnerAnlegen(zielOrdner, wurzel, ordnerNeu);

                    var t = new Tausch { Ziel = ziel };
                    if (File.Exists(ziel))
                    {
                        string vorher = ziel + VorherEndung;
                        AltesVorherWeg(vorher);
                        File.Move(ziel, vorher);
                        t.Vorher = vorher;
                    }
                    getan.Add(t);
                    fs.Position = 0;
                    using (var raus = new FileStream(ziel, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        fs.CopyTo(raus);
                    t.Kopiert = true;
                    if (new FileInfo(ziel).Length != fs.Length)
                        throw new IOException("„" + rel + "“ wurde unvollständig kopiert.");
                    k.Schreibe("   " + rel + (t.Vorher != null ? " getauscht (bisherige Fassung als " + Path.GetFileName(t.Vorher) + ")" : " neu angelegt") + ".", "dim");
                }
            }
            catch (Exception ex)
            {
                fehler = ex.Message;
                AppLog.Error("update.anwenden: Tausch von " + (datei ?? "?") + " scheiterte", ex);
            }
            if (fehler == null) return null;

            // Rueckrollen in umgekehrter Reihenfolge; jeder Schritt wird versucht, Fehler stehen im app.log.
            bool geaendert = getan.Count > 0;
            int nichtZurueck = 0;
            for (int i = getan.Count - 1; i >= 0; i--)
            {
                Tausch t = getan[i];
                // Was jetzt am Ziel liegt, ist die Kopie oder ihr Rest. Sie kommt erst beiseite
                // (.neu.<4hex>), damit das Original sofort zurueck kann; geloescht wird zuletzt.
                string beiseite = null;
                bool zielFrei = true;
                if (File.Exists(t.Ziel))
                {
                    if (t.Vorher != null)
                    {
                        string name = t.Ziel + NeuEndung + "." + Hex4();
                        try { File.Move(t.Ziel, name); beiseite = name; }
                        catch (Exception ex)
                        {
                            AppLog.Warn("Rückrollen: " + t.Ziel + " nicht beiseite benannt: " + ex.Message);
                            try { File.Delete(t.Ziel); }
                            catch (Exception ex2) { zielFrei = false; nichtZurueck++; AppLog.Warn("Rückrollen: " + t.Ziel + " nicht gelöscht: " + ex2.Message); }
                        }
                    }
                    else
                    {
                        try { File.Delete(t.Ziel); }
                        catch (Exception ex) { nichtZurueck++; AppLog.Warn("Rückrollen: " + t.Ziel + " nicht gelöscht: " + ex.Message); }
                    }
                }
                if (t.Vorher != null && zielFrei)
                {
                    try { File.Move(t.Vorher, t.Ziel); }
                    catch (Exception ex) { nichtZurueck++; AppLog.Warn("Rückrollen: " + t.Vorher + " nicht zurückbenannt: " + ex.Message); }
                }
                if (beiseite != null)
                {
                    try { File.Delete(beiseite); }
                    catch (Exception ex) { AppLog.Warn("Rückrollen: " + beiseite + " bleibt liegen, der nächste Helferstart räumt es weg: " + ex.Message); }
                }
            }
            for (int i = ordnerNeu.Count - 1; i >= 0; i--)
            {
                try { if (Directory.GetFileSystemEntries(ordnerNeu[i]).Length == 0) Directory.Delete(ordnerNeu[i]); }
                catch (Exception) { }
            }
            string satz = "Tausch von „" + (datei ?? "?") + "“ scheiterte: " + fehler + " ";
            if (!geaendert) return satz + "Es wurde nichts geändert.";
            return satz + (nichtZurueck == 0
                ? "Die " + getan.Count + " bis dahin getauschten Dateien wurden zurückgerollt; die bisherige Fassung gilt."
                : "Zurückgerollt, aber " + nichtZurueck + " Schritt(e) davon scheiterten (app.log); bitte die bisherige Fassung neu installieren.");
        }

        /// <summary>
        /// Wirft, wenn ein vorhandener Ordner zwischen dem Programmordner (ausschliesslich) und ordner
        /// eine Verknuepfung (Abzweigung, Reparse-Punkt) ist: der Helfer schreibt erhoeht, und ein
        /// ui\, das woandershin zeigt, liesse ihn Dateien an einem fremden Ort umbenennen und anlegen.
        /// Gilt nur fuer Unterordner; der Programmordner selbst ist der Ort, aus dem der Helfer laeuft.
        /// </summary>
        static void OhneAbzweigung(string ordner, string wurzel, string rel)
        {
            string d = ordner;
            while (!string.IsNullOrEmpty(d) && (d + Path.DirectorySeparatorChar).StartsWith(wurzel, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(d + Path.DirectorySeparatorChar, wurzel, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(d) && (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("der Ordner „" + Relativ(wurzel, d) + "“ im Programmordner ist eine Verknüpfung auf einen anderen Ort; „" + rel + "“ wird dort nicht geschrieben.");
                d = Path.GetDirectoryName(d);
            }
        }

        static void OrdnerAnlegen(string ordner, string wurzel, List<string> angelegt)
        {
            if (string.IsNullOrEmpty(ordner) || Directory.Exists(ordner)) return;
            if (!(ordner + Path.DirectorySeparatorChar).StartsWith(wurzel, StringComparison.OrdinalIgnoreCase)) return;
            OrdnerAnlegen(Path.GetDirectoryName(ordner), wurzel, angelegt);
            Directory.CreateDirectory(ordner);
            angelegt.Add(ordner);
        }

        /// <summary>Ein altes .vorher loeschen; laeuft noch ein Prozess daraus, umbenennen nach .vorher.&lt;4hex&gt; und beim naechsten Start loeschen lassen.</summary>
        static void AltesVorherWeg(string vorher)
        {
            if (!File.Exists(vorher)) return;
            try { File.SetAttributes(vorher, FileAttributes.Normal); File.Delete(vorher); return; }
            catch (Exception ex) { AppLog.Warn("Altes " + vorher + " nicht löschbar, wird umbenannt: " + ex.Message); }
            string alt = vorher + "." + Hex4();
            File.Move(vorher, alt);
            if (!Native.MoveFileExW(alt, null, Native.MOVEFILE_DELAY_UNTIL_REBOOT))
                AppLog.Warn(alt + " wird beim Neustart nicht gelöscht (Win32-Fehler " + Marshal.GetLastWin32Error() + "); der nächste Helferstart räumt es weg.");
        }

        // ================================================================ Aufraeumen (Helferstart)

        /// <summary>
        /// Beim Start des erhoehten Helfers (helfer/Helfer.cs): loescht die Reste des Tauschs (.vorher,
        /// .vorher.&lt;4hex&gt;, .neu.&lt;4hex&gt;), und zwar nur im Stamm fuer WindowsWartung.exe und die drei
        /// WebView2-DLLs und unter ui\ (rekursiv ohne Verknuepfungen zu folgen); was sich nicht loeschen
        /// laesst (ein Prozess laeuft noch daraus), bleibt ohne Fehler. Leert den eigenen Arbeitsordner unter ProgramData, aber nur, wenn er
        /// die Herkunft des Helfers traegt (kein Reparse-Punkt, Besitzer Administratoren, eigene
        /// DACL); ein fremder Ordner wird nicht angefasst. Wirft nie; app.log sagt, was war.
        /// </summary>
        public static void Aufraeumen()
        {
            int weg = 0, geblieben = 0;
            try
            {
                // Nur, was der Tausch selbst anlegt: im Stamm die Reste von WindowsWartung.exe und den
                // drei WebView2-DLLs, unter ui\ alles mit Rest-Endung. Der Programmordner kann ein
                // gemeinsamer Ordner sein (etwa D:\Tools); fremde Dateien und Unterordner bleiben unberuehrt.
                string programm = Programmordner();
                var kandidaten = new List<string>();
                foreach (string f in Directory.GetFiles(programm))
                {
                    string basis = RestBasis(Path.GetFileName(f));
                    if (basis == null) continue;
                    bool eigen = string.Equals(basis, ExeName, StringComparison.OrdinalIgnoreCase);
                    foreach (string dll in StammDlls) if (string.Equals(basis, dll, StringComparison.OrdinalIgnoreCase)) eigen = true;
                    if (eigen) kandidaten.Add(f);
                }
                string ui = Path.Combine(programm, UiOrdner);
                if (Directory.Exists(ui) && (File.GetAttributes(ui) & FileAttributes.ReparsePoint) == 0)
                    foreach (string f in DateienOhneAbzweigung(ui))
                        if (RestBasis(Path.GetFileName(f)) != null) kandidaten.Add(f);
                foreach (string f in kandidaten)
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); weg++; }
                    catch (Exception) { geblieben++; }
                }
            }
            catch (Exception ex) { AppLog.Warn("Programmordner nicht aufgeräumt: " + ex.Message); }
            if (weg > 0 || geblieben > 0)
                AppLog.Info("Programm-Update aufgeräumt: " + weg + " Dateien gelöscht, " + geblieben + " noch in Benutzung.");

            string ordner = Arbeitsordner();
            try
            {
                if (!Directory.Exists(ordner)) return;
                FileAttributes fa = File.GetAttributes(ordner);
                if ((fa & FileAttributes.ReparsePoint) != 0)
                {
                    AppLog.Warn("Update-Arbeitsordner " + ordner + " ist eine Verknüpfung auf einen anderen Ort; er wird nicht geleert und nicht benutzt.");
                    return;
                }
                DirectorySecurity sec = Directory.GetAccessControl(ordner, AccessControlSections.Owner | AccessControlSections.Access);
                var besitzer = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                bool eigen = besitzer != null && (besitzer.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || besitzer.IsWellKnown(WellKnownSidType.LocalSystemSid)) && DaclStimmt(sec);
                if (!eigen)
                {
                    AppLog.Warn("Update-Arbeitsordner " + ordner + " stammt nicht vom Helfer (Besitzer/Rechte); er wird nicht geleert und nicht benutzt.");
                    return;
                }
                int eintraege = Directory.GetFileSystemEntries(ordner).Length;
                if (eintraege == 0) return;
                int rest = Leeren(ordner);
                AppLog.Info("Update-Arbeitsordner " + ordner + " geleert (" + (eintraege - rest) + " von " + eintraege + " Einträgen" + (rest > 0 ? ", " + rest + " noch in Benutzung" : "") + ").");
            }
            catch (Exception ex) { AppLog.Warn("Update-Arbeitsordner " + ordner + " nicht geleert: " + ex.Message); }
        }

        /// <summary>
        /// Name ohne die Rest-Endung des Tauschs (.vorher, .vorher.&lt;4hex&gt;, .neu.&lt;4hex&gt;); null, wenn der
        /// Name keine dieser Endungen traegt.
        /// </summary>
        static string RestBasis(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.EndsWith(VorherEndung, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - VorherEndung.Length);
            Match m = AltVorherRx.Match(name);
            if (!m.Success) m = AltNeuRx.Match(name);
            return m.Success ? name.Substring(0, m.Index) : null;
        }

        /// <summary>Alle Dateien unter wurzel, rekursiv; Ordner mit Reparse-Punkt werden nicht betreten.</summary>
        static IEnumerable<string> DateienOhneAbzweigung(string wurzel)
        {
            var stapel = new Stack<string>();
            stapel.Push(wurzel);
            while (stapel.Count > 0)
            {
                string d = stapel.Pop();
                string[] dateien;
                try { dateien = Directory.GetFiles(d); }
                catch (Exception) { continue; }
                foreach (string f in dateien) yield return f;
                string[] unter;
                try { unter = Directory.GetDirectories(d); }
                catch (Exception) { continue; }
                foreach (string u in unter)
                {
                    try { if ((File.GetAttributes(u) & FileAttributes.ReparsePoint) == 0) stapel.Push(u); }
                    catch (Exception) { }
                }
            }
        }

        // ---------------------------------------------------------------- Win32

        static class Native
        {
            const uint FILE_LIST_DIRECTORY = 0x0001, READ_CONTROL = 0x00020000, GENERIC_READ = 0x80000000;
            const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
            const uint OPEN_EXISTING = 3;
            const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
            public const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
            public const int ERROR_FILE_NOT_FOUND = 2, ERROR_PATH_NOT_FOUND = 3;

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

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

            [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetFileInformationByHandle(SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION info);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder pfad, uint laenge, uint flags);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool MoveFileExW(string von, string nach, uint flags);

            /// <summary>Ordner-Handle: FILE_LIST_DIRECTORY (damit die Freigabe zaehlt), Freigabe Lesen und Schreiben ohne Loeschen, Reparse-Punkt selbst statt Ziel. null bei Fehler (win32).</summary>
            public static SafeFileHandle OrdnerOeffnen(string pfad, out int win32)
            {
                win32 = 0;
                SafeFileHandle h = CreateFileW(pfad, FILE_LIST_DIRECTORY | READ_CONTROL, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                                               OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
                if (h == null || h.IsInvalid)
                {
                    win32 = Marshal.GetLastWin32Error();
                    if (h != null) h.Dispose();
                    return null;
                }
                return h;
            }

            /// <summary>Datei-Handle zum Lesen: Freigabe nur Lesen (kein Schreiben, kein Loeschen, kein Umbenennen daneben), Reparse-Punkt selbst statt Ziel, ein Ordner wird nicht geoeffnet. null bei Fehler (win32).</summary>
            public static SafeFileHandle DateiOeffnen(string pfad, out int win32)
            {
                win32 = 0;
                SafeFileHandle h = CreateFileW(pfad, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
                if (h == null || h.IsInvalid)
                {
                    win32 = Marshal.GetLastWin32Error();
                    if (h != null) h.Dispose();
                    return null;
                }
                return h;
            }

            /// <summary>Der wirkliche Pfad hinter dem Handle (GetFinalPathNameByHandle, ohne \\?\), null wenn nicht lesbar.</summary>
            public static string Kanonisch(SafeFileHandle h)
            {
                var sb = new StringBuilder(1024);
                uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n == 0) return null;
                if (n > sb.Capacity)
                {
                    sb = new StringBuilder((int)n + 1);
                    n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                    if (n == 0 || n > sb.Capacity) return null;
                }
                string p = sb.ToString();
                if (p.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + p.Substring(8);
                if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) return p.Substring(4);
                return p;
            }
        }
    }
}
