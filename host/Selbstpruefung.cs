using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WartungsToolbox
{
    /// <summary>
    /// Startpruefung (docs\M2-ENTWURF.md, Abschnitt 0 Punkt 8 und Abschnitt 7).
    ///
    /// Seit die EXE ohne Administratorrechte startet (asInvoker) und sich die Rechte erst der
    /// Helfer holt, muss sie wissen, ob sie noch die ist, die ausgeliefert wurde:
    ///
    ///   1. Eigene Signatur ueber WinVerifyTrust (wintrust.dll) - dieselbe Pruefung, die der
    ///      Explorer im Eigenschaften-Dialog macht, nur ohne Dialog und ohne Sperrlisten-Abruf.
    ///   2. Die Oberflaechendateien unter ui\ gegen die SHA-256-Liste, die build.ps1 als
    ///      Ressource "ui-hashes.txt" einbettet. Die EXE-Signatur deckt ui\ nicht ab; ein
    ///      ausgetauschtes app.js liefe sonst mit allem, was der Helfer spaeter darf.
    ///
    /// Entscheidung (BeimStart): signierte EXE mit ungueltiger Signatur oder abweichender
    /// Oberflaeche -> false, der Aufrufer (Program.cs) zeigt Meldung() und beendet mit Exit 5.
    /// Unsignierte EXE (Dev-Bau) mit Abweichung -> nur Warnung im Protokoll, Start geht weiter.
    ///
    /// Gemessen wird Unversehrtheit, nicht Kettenvertrauen: Kettenfehler (nicht vertrauter Stamm,
    /// abgelaufenes Zertifikat, Richtlinie) gelten als "signiert, Signatur intakt" - siehe
    /// IstKettenfehler. Die Bindung an den Herausgeber macht UpdateTrust, nicht diese Klasse.
    ///
    /// Dateien unter ui\, die nicht in der Liste stehen, sind nur ein Hinweis im Protokoll und
    /// kein Grund zum Verweigern: Updater (robocopy /E ohne /PURGE) und Installer spiegeln ui\
    /// nicht, eine in einer neuen Fassung entfernte Datei bliebe also auf jedem Kundenrechner
    /// liegen. Eine Datei, auf die nichts verweist, hat keinen Ausfuehrungsweg.
    ///
    /// Keine MessageBox hier: die Klasse laeuft auch fuer --helfer, --auto und die Kommandozeile.
    /// Keine Ausnahme verlaesst BeimStart oder Kommandozeile: im --helfer-Zweig laeuft BeimStart
    /// vor den Auffangnetzen von AppLog, eine Ausnahme dort bliebe ohne Protokollzeile.
    /// </summary>
    static class Selbstpruefung
    {
        public const string RessourcenName = "ui-hashes.txt";

        public class Ergebnis
        {
            public bool Signiert;
            public bool SignaturGueltig;
            public bool UiStimmt;
            /// <summary>
            /// HRESULT von WinVerifyTrust (0 = gueltig, 0x800B0100 = keine Signatur). Bleibt auch
            /// bei einem Kettenfehler stehen, obwohl SignaturGueltig dann true ist.
            /// </summary>
            public int SignaturCode;
            /// <summary>Grund je Abweichung; nur diese Liste entscheidet ueber UiStimmt und Exit 5.</summary>
            public List<string> Abweichungen = new List<string>();
            /// <summary>
            /// Dateien unter ui\, die nicht in der Liste stehen (relativer Pfad, ohne shot_*.png).
            /// Nur Hinweis fuer das Protokoll, zaehlt nicht als Abweichung.
            /// </summary>
            public List<string> Zusaetzlich = new List<string>();
        }

        // ---------- WinVerifyTrust ----------

        // WINTRUST_ACTION_GENERIC_VERIFY_V2: Authenticode-Pruefung einer Datei.
        static readonly Guid AktionGenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        const uint WTD_UI_NONE = 2;
        const uint WTD_REVOKE_NONE = 0;
        const uint WTD_CHOICE_FILE = 1;
        const uint WTD_STATEACTION_VERIFY = 1;
        const uint WTD_STATEACTION_CLOSE = 2;
        // Sperrlisten ganz aus: fdwRevocationChecks = WTD_REVOKE_NONE allein schaltet die Pruefung
        // unter GENERIC_VERIFY_V2 nicht ab, sie folgt dann dem Software-Publishing-State des
        // Rechners. Auf einem gehaerteten Rechner (kein OFFLINEOK, keine gecachte Sperrliste der
        // Zeitstempelkette) kaeme sonst CERT_E_REVOCATION_FAILURE (0x800B010E) oder
        // CRYPT_E_REVOCATION_OFFLINE (0x80092013), und IstKettenfehler kennt beide nicht: Exit 5
        // bei unveraenderter Datei. Gemessen wird Unversehrtheit, nicht Kettenvertrauen; so auch
        // sammler\Quellen\Autostartquelle.cs (13.09.2026).
        const uint WTD_REVOCATION_CHECK_NONE = 0x10;
        const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;   // kein Netzabruf von Zertifikaten oder Sperrlisten

        public const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);

        // Kettenfehler: die Signatur passt zur Datei, nur die Vertrauenskette dahinter nicht.
        // Fuer den Zweck dieser Klasse (ist die Datei noch die ausgelieferte?) zaehlen sie als
        // "signiert, Signatur intakt"; der Code bleibt in Ergebnis.SignaturCode stehen.
        //   CERT_E_UNTRUSTEDROOT     - selbst ausgestelltes Zertifikat (das Release traegt CN=Jonas);
        //                              auf Kundenrechnern ohne den Stamm kaeme sonst bei jedem
        //                              Start Exit 5.
        //   CERT_E_EXPIRED           - Zertifikat abgelaufen (5 Jahre, tools\make-cert.ps1); sign.ps1
        //                              signiert bei Zeitstempel-Ausfall ohne Zeitstempel, danach
        //                              waere jede unversehrte Fassung "ungueltig".
        //   CRYPT_E_SECURITY_SETTINGS - eine Richtlinie des Rechners sperrt Herausgeber oder Stamm;
        //                              die Datei selbst ist unveraendert.
        // Digest- und Signaturfehler (z. B. TRUST_E_BAD_DIGEST 0x80096010) bleiben ungueltig.
        public const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
        public const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
        public const int CRYPT_E_SECURITY_SETTINGS = unchecked((int)0x80092026);

        /// <summary>true fuer die drei Kettenfehler oben: Signatur intakt, Kette nicht vertraut.</summary>
        public static bool IstKettenfehler(int code)
        {
            return code == CERT_E_UNTRUSTEDROOT || code == CERT_E_EXPIRED || code == CRYPT_E_SECURITY_SETTINGS;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;   // LPCWSTR, von Hand marshalt (Struktur bleibt blittable)
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;             // WINTRUST_FILE_INFO*
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;   // seit Windows 8, bleibt null
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        /// <summary>
        /// HRESULT der Authenticode-Pruefung einer Datei: 0 gueltig, TRUST_E_NOSIGNATURE ohne
        /// Signatur, Kettenfehler (IstKettenfehler) bei intakter Signatur ohne vertraute Kette,
        /// sonst ungueltig. Wirft nur, wenn wintrust.dll selbst fehlt (auf Windows nicht der Fall).
        /// </summary>
        public static int SignaturPruefen(string datei)
        {
            IntPtr pfad = IntPtr.Zero, pInfo = IntPtr.Zero;
            try
            {
                pfad = Marshal.StringToHGlobalUni(datei);
                var info = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                    pcwszFilePath = pfad
                };
                pInfo = Marshal.AllocHGlobal((int)info.cbStruct);
                Marshal.StructureToPtr(info, pInfo, false);

                var daten = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pInfo,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                Guid aktion = AktionGenericVerifyV2;
                // INVALID_HANDLE_VALUE als Fenster: es gibt keinen interaktiven Nutzer fuer Dialoge.
                IntPtr keinFenster = new IntPtr(-1);
                int code = WinVerifyTrust(keinFenster, ref aktion, ref daten);

                // Den Zustand freigeben, sonst bleibt das Handle hWVTStateData offen.
                daten.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(keinFenster, ref aktion, ref daten);
                return code;
            }
            finally
            {
                if (pInfo != IntPtr.Zero) Marshal.FreeHGlobal(pInfo);
                if (pfad != IntPtr.Zero) Marshal.FreeHGlobal(pfad);
            }
        }

        // ---------- Pruefung ----------

        static Ergebnis _letztes;

        /// <summary>Pruefung der laufenden EXE und ihres ui\-Ordners gegen die eingebettete Liste.</summary>
        public static Ergebnis Pruefen()
        {
            string exe = EigeneExe();
            Stream liste = null;
            string listenFehler = null;
            try
            {
                // Immer die eigene Assembly: die Ressource liegt in ihr. GetEntryAssembly waere
                // unter einem Roslyn-Harness der Harness selbst, und die Liste "fehlte" dann.
                liste = typeof(Selbstpruefung).Assembly.GetManifestResourceStream(RessourcenName);
            }
            catch (Exception ex)
            {
                liste = null;               // wird unten zur Abweichung, mit dem Grund
                listenFehler = ex.Message;
            }
            using (liste)
            {
                _letztes = Pruefen(exe, exe == null ? null : Path.Combine(Path.GetDirectoryName(exe), "ui"), liste, listenFehler);
            }
            return _letztes;
        }

        /// <summary>
        /// Kern der Pruefung, ohne Bezug auf den eigenen Prozess - so laesst sie sich in einer
        /// Probe gegen eine beliebige EXE, einen beliebigen ui-Ordner und eine beliebige Liste
        /// fahren. liste == null heisst: Pruefliste fehlt im Programm.
        /// </summary>
        public static Ergebnis Pruefen(string exePfad, string uiOrdner, Stream liste)
        {
            return Pruefen(exePfad, uiOrdner, liste, null);
        }

        // listenFehler: Grund, warum die Liste nicht geladen werden konnte (null = kein Fehler);
        // wandert in die Abweichung "Pruefliste fehlt im Programm", damit er nicht verloren geht.
        static Ergebnis Pruefen(string exePfad, string uiOrdner, Stream liste, string listenFehler)
        {
            var e = new Ergebnis();

            // 1. Signatur
            if (string.IsNullOrEmpty(exePfad) || !File.Exists(exePfad))
            {
                e.Signiert = false;
                e.SignaturCode = TRUST_E_NOSIGNATURE;
                e.Abweichungen.Add("Programmdatei nicht gefunden: " + (exePfad ?? "(kein Pfad)"));
            }
            else
            {
                try
                {
                    int code = SignaturPruefen(exePfad);
                    e.SignaturCode = code;
                    if (code == 0) { e.Signiert = true; e.SignaturGueltig = true; }
                    else if (code == TRUST_E_NOSIGNATURE) { e.Signiert = false; e.SignaturGueltig = false; }
                    else if (IstKettenfehler(code)) { e.Signiert = true; e.SignaturGueltig = true; }   // Signatur intakt, nur die Kette nicht vertraut (Grund bei den Konstanten)
                    else
                    {
                        e.Signiert = true;
                        e.SignaturGueltig = false;
                        e.Abweichungen.Add("Signatur der Programmdatei ungültig (Code 0x" + code.ToString("X8") + ")");
                    }
                }
                catch (Exception ex)
                {
                    // Ohne Ergebnis wie unsigniert behandeln: der Start darf daran nicht scheitern,
                    // aber die Spur bleibt.
                    e.Signiert = false;
                    e.SignaturGueltig = false;
                    e.SignaturCode = ex.HResult;
                    e.Abweichungen.Add("Signaturprüfung nicht möglich: " + ex.Message);
                }
            }

            // 2. Oberflaechendateien
            int vorher = e.Abweichungen.Count;
            UiPruefen(uiOrdner, liste, listenFehler, e.Abweichungen, e.Zusaetzlich);
            e.UiStimmt = e.Abweichungen.Count == vorher;
            return e;
        }

        /// <summary>
        /// Vergleicht ui\ mit der Liste. Jede Zeile der Liste: "sha256hex  relativer/pfad".
        /// Fehlende und veraenderte Dateien landen als je eine Zeile in abweichungen; Dateien,
        /// die nicht in der Liste stehen (ausser shot_*.png), nur in zusaetzlich (Hinweis).
        /// </summary>
        static void UiPruefen(string uiOrdner, Stream liste, string listenFehler, List<string> abweichungen, List<string> zusaetzlich)
        {
            if (liste == null)
            {
                abweichungen.Add("Prüfliste fehlt im Programm" + (string.IsNullOrEmpty(listenFehler) ? "" : " (" + listenFehler + ")"));
                return;
            }

            var erwartet = new List<KeyValuePair<string, string>>();   // pfad -> hash, in Listenreihenfolge
            var bekannt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var r = new StreamReader(liste, new UTF8Encoding(false)))
                {
                    string zeile;
                    while ((zeile = r.ReadLine()) != null)
                    {
                        zeile = zeile.Trim();
                        if (zeile.Length == 0) continue;
                        int trenner = zeile.IndexOf(' ');
                        if (trenner <= 0)
                        {
                            abweichungen.Add("Prüfliste unlesbar: " + zeile);
                            continue;
                        }
                        string hash = zeile.Substring(0, trenner).Trim();
                        string pfad = zeile.Substring(trenner).Trim();
                        if (hash.Length != 64 || pfad.Length == 0 || PfadVerlaesstUi(pfad))
                        {
                            // Ein Eintrag, der aus ui\ hinausfuehrt (absolut, "..") oder den Path
                            // nicht annimmt, wird nicht gelesen: die Liste ist zwar Bauprodukt,
                            // die Methode aber ausdruecklich fuer beliebige Listen probenfaehig.
                            abweichungen.Add("Prüfliste unlesbar: " + zeile);
                            continue;
                        }
                        erwartet.Add(new KeyValuePair<string, string>(pfad, hash));
                        bekannt.Add(pfad);
                    }
                }
            }
            catch (Exception ex)
            {
                abweichungen.Add("Prüfliste nicht lesbar: " + ex.Message);
                return;
            }
            if (erwartet.Count == 0)
            {
                abweichungen.Add("Prüfliste ist leer");   // nie stille Leere: eine leere Liste prueft nichts
                return;
            }

            bool ordnerDa = !string.IsNullOrEmpty(uiOrdner) && Directory.Exists(uiOrdner);
            if (!ordnerDa) abweichungen.Add("Ordner ui fehlt: " + (uiOrdner ?? "(kein Pfad)"));

            foreach (var eintrag in erwartet)
            {
                string datei = ordnerDa ? Path.Combine(uiOrdner, eintrag.Key.Replace('/', Path.DirectorySeparatorChar)) : null;
                if (datei == null || !File.Exists(datei))
                {
                    abweichungen.Add("fehlt: " + eintrag.Key);
                    continue;
                }
                string ist = DateiHash(datei, abweichungen, eintrag.Key);
                if (ist == null) continue;   // Grund steht schon in abweichungen
                if (!string.Equals(ist, eintrag.Value, StringComparison.OrdinalIgnoreCase))
                    abweichungen.Add("verändert: " + eintrag.Key);
            }

            // Nicht gelistete Dateien: nur Hinweis (Ergebnis.Zusaetzlich), keine Abweichung.
            // Deshalb ist auch ein Fehler beim Durchlaufen des Ordners nur ein Hinweis: die
            // gelisteten Dateien sind oben schon einzeln geprueft.
            if (!ordnerDa) return;
            var gefunden = new List<string>();
            try
            {
                string wurzel = uiOrdner.TrimEnd(Path.DirectorySeparatorChar);
                foreach (string f in Directory.GetFiles(wurzel, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (IstBildschirmfoto(name)) continue;
                    string rel = f.Substring(wurzel.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
                    if (!bekannt.Contains(rel)) gefunden.Add(rel);
                }
            }
            catch (Exception ex)
            {
                gefunden.Add("(Ordner ui nicht vollständig lesbar: " + ex.Message + ")");
            }
            gefunden.Sort(StringComparer.Ordinal);
            zusaetzlich.AddRange(gefunden);
        }

        // Screenshots der Oberflaeche (--shot) liegen neben den Quellen und sind kein Programmteil.
        static bool IstBildschirmfoto(string name)
        {
            return name.StartsWith("shot_", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// true, wenn ein Listeneintrag nicht unterhalb von ui\ bleibt: absoluter Pfad
        /// (auch "/x" oder "C:x"), ein Segment "..", oder Zeichen, die Path.Combine ablehnt.
        /// Die Zeichenpruefung kommt zuerst: IsPathRooted wirft sonst selbst bei ihnen.
        /// </summary>
        static bool PfadVerlaesstUi(string pfad)
        {
            if (pfad.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return true;
            if (Path.IsPathRooted(pfad)) return true;
            foreach (string segment in pfad.Split('/', '\\'))
                if (segment == "..") return true;
            return false;
        }

        /// <summary>SHA-256 als kleine Hex-Zeichen; null und ein Eintrag in abweichungen, wenn die Datei nicht lesbar ist.</summary>
        static string DateiHash(string datei, List<string> abweichungen, string anzeige)
        {
            try
            {
                // SHA256.Create statt SHA256Managed: unter FIPS-Richtlinie wirft die Managed-Klasse,
                // Create liefert dann die zugelassene Fassung. Das Ergebnis ist dasselbe.
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] h = sha.ComputeHash(fs);
                    var sb = new StringBuilder(64);
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                abweichungen.Add("nicht lesbar: " + anzeige + " (" + ex.Message + ")");
                return null;
            }
        }

        static string EigeneExe()
        {
            try
            {
                string p = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { }
            try
            {
                Assembly a = Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return a.Location;
            }
            catch { }
            return null;
        }

        // ---------- Einstiege ----------

        /// <summary>
        /// Prueft und protokolliert. false nur, wenn die EXE signiert ist und Signatur oder
        /// Oberflaeche abweichen - dann zeigt der Aufrufer Meldung() und beendet mit Exit 5.
        /// Unsigniert (Dev-Bau) geht es mit einer Warnung im Protokoll weiter.
        /// </summary>
        public static bool BeimStart()
        {
            try
            {
                Ergebnis e = Pruefen();
                AppLog.Info("Startprüfung: " + Zusammenfassung(e, " "));
                Protokollieren(e, "Startprüfung");
                bool verweigern = e.Signiert && (!e.SignaturGueltig || !e.UiStimmt);
                if (verweigern) AppLog.Error("Start verweigert: Programmdateien verändert (signierte Fassung).");
                return !verweigern;
            }
            catch (Exception ex)
            {
                // Offen scheitern: ohne Ergebnis darf die Pruefung den Start nicht sperren,
                // aber die Spur bleibt (wie bei der Signatur in Pruefen).
                AppLog.Error("Startprüfung", ex);
                return true;
            }
        }

        // Abweichungen und Hinweise ins Protokoll; jede Zeile traegt die Anzahl.
        static void Protokollieren(Ergebnis e, string kontext)
        {
            if (e.Abweichungen.Count > 0)
                AppLog.Warn(kontext + ": " + Anzahl(e.Abweichungen.Count, "Abweichung", "Abweichungen") + ": " + string.Join("; ", e.Abweichungen));
            if (e.Zusaetzlich.Count > 0)
                AppLog.Warn(kontext + ": " + Anzahl(e.Zusaetzlich.Count, "Datei", "Dateien") + " unter ui ohne Eintrag in der Prüfliste (nur Hinweis, kein Grund zum Verweigern): " + string.Join("; ", e.Zusaetzlich));
        }

        static string Anzahl(int n, string einzahl, string mehrzahl)
        {
            return n + " " + (n == 1 ? einzahl : mehrzahl);
        }

        /// <summary>Text fuer die Meldung des Aufrufers, mit den Abweichungen der letzten Pruefung.</summary>
        public static string Meldung()
        {
            Ergebnis e = _letztes;
            if (e == null)
            {
                try { e = Pruefen(); }
                catch (Exception ex) { AppLog.Error("Startprüfung", ex); }   // Meldung dann ohne Einzelheiten
            }
            return Meldung(e);
        }

        public static string Meldung(Ergebnis e)
        {
            var sb = new StringBuilder();
            sb.Append("Die Programmdateien wurden verändert. Bitte installieren Sie Windows-Wartung neu.");
            if (e != null && e.Abweichungen.Count > 0)
            {
                sb.Append(Environment.NewLine).Append(Environment.NewLine);
                sb.Append(Anzahl(e.Abweichungen.Count, "Abweichung", "Abweichungen")).Append(':');
                foreach (string a in e.Abweichungen) sb.Append(Environment.NewLine).Append("  ").Append(a);
            }
            return sb.ToString();
        }

        /// <summary>
        /// --selbstpruefung &lt;datei&gt;: schreibt die vier Werte und je Abweichung eine Zeile (UTF-8);
        /// zusaetzliche ui-Dateien stehen nur im Protokoll, nicht in der Datei.
        /// Rueckgabe 0 = Oberflaeche stimmt und Signatur gueltig oder unsigniert,
        /// 5 = signiert mit Abweichung, 6 = unsigniert und Oberflaeche weicht ab,
        /// 3 = Pruefung abgebrochen oder Ausgabedatei nicht schreibbar (Grund im Protokoll).
        /// </summary>
        public static int Kommandozeile(string datei)
        {
            Ergebnis e;
            try
            {
                e = Pruefen();
            }
            catch (Exception ex)
            {
                AppLog.Error("Selbstprüfung abgebrochen", ex);
                return 3;
            }
            Protokollieren(e, "Selbstprüfung");
            int code;
            if (e.Signiert && (!e.SignaturGueltig || !e.UiStimmt)) code = 5;
            else if (!e.Signiert && !e.UiStimmt) code = 6;
            else code = 0;

            var sb = new StringBuilder();
            sb.Append(Zusammenfassung(e, "\n")).Append('\n');
            foreach (string a in e.Abweichungen) sb.Append(a).Append('\n');
            try
            {
                File.WriteAllText(datei, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLog.Error("Selbstprüfung: Ausgabedatei nicht schreibbar (" + datei + ")", ex);
                return 3;
            }
            AppLog.Info("Selbstprüfung geschrieben: " + datei + " (Exit " + code + ")");
            return code;
        }

        // Die vier Werte als "name=wert", durch trenner verbunden. Die Namen stehen als
        // eigene Literale (Schluessel), damit die Sprachprobe sie nicht als Anzeige-Text liest.
        static string Zusammenfassung(Ergebnis e, string trenner)
        {
            return Paar("signiert", e.Signiert) + trenner
                 + Paar("signaturGueltig", e.SignaturGueltig) + trenner
                 + Paar("uiStimmt", e.UiStimmt) + trenner
                 + "code=0x" + e.SignaturCode.ToString("X8");
        }

        static string Paar(string name, bool wert)
        {
            return name + "=" + (wert ? "true" : "false");
        }
    }
}
