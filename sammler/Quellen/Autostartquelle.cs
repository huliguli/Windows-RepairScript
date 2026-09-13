using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Autostart vollstaendig, ohne Win32_StartupCommand (laesst WOW6432Node aus und kennt den
    /// Deaktiviert-Status nicht, gemessen):
    ///
    ///   Registry Run/RunOnce (HKLM + HKCU, je Registry64 und Registry32)
    ///   Startup-Ordner (Nutzer und Alle Benutzer): jede Datei ausser desktop.ini, denn Explorer
    ///     startet dort alles (.bat, .vbs, .ps1, .url, ...), nicht nur .exe und .lnk
    ///   StartupApproved (Byte 0: 2 aktiv, 3 deaktiviert mit FILETIME; 0 und 6 beobachtet, ungeklaert -> null)
    ///   Aufgaben ueber COM Schedule.Service (nicht erhoeht 217 von 284; AufgabenVollstaendig = Erhoehung)
    ///   Dienste ueber Win32_Service
    ///   Winlogon Shell/Userinit, AppInit_DLLs mit Hauptschalter LoadAppInit_DLLs (beide Sichten)
    ///
    /// "Microsoft oder fremd" entscheidet der Authenticode-Signierer dessen, was wirklich startet:
    /// bei "rundll32.exe fremd.dll,Start" die DLL, nicht der Windows-Host (siehe Hosts). Die
    /// Signatur wird mit WinVerifyTrust GEPRUEFT, nicht nur gelesen: ein selbst ausgestelltes
    /// "CN=Microsoft Windows" ohne gueltige Kette zaehlt als unsigniert.
    ///
    /// Gemessen am 12.09.2026: viele System32-EXEs (spoolsv, msiexec, SearchIndexer,
    /// SecurityHealthSystray) tragen KEINE eingebettete Signatur, sondern stehen nur in einem
    /// Katalog (CatRoot). Wer nur X509Certificate.CreateFromSignedFile fragt, haelt halb Windows
    /// fuer Fremdsoftware. Deshalb zweiter Schritt ueber die Katalogdatenbank (wintrust:
    /// CryptCATAdmin* plus WinVerifyTrust mit WTD_CHOICE_CATALOG), ohne Erhoehung.
    /// </summary>
    public static class Autostartquelle
    {
        const string RunBasis = @"SOFTWARE\Microsoft\Windows\CurrentVersion\";
        const string ApprovedBasis = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const int AufgabenBudgetMs = 10000;
        /// <summary>Eigene Aufgaben des Programms (src/Scheduler.cs: WindowsWartung-Autostart, WindowsWartung-AutoWartung).</summary>
        const string EigeneAufgabenPraefix = "WindowsWartung-";

        public static void Erfassen(Systembild s)
        {
            var A = s.Autostart;
            Sammler.Versuch(s, "registry.autostart.run", () => RunSchluessel(s, A));
            Sammler.Versuch(s, "ordner.autostart", () => StartupOrdner(A));
            Sammler.Versuch(s, "registry.autostart.startupapproved", () => StartupApproved(A));
            Sammler.Versuch(s, "com.aufgaben", () => Aufgaben(A));
            A.AufgabenVollstaendig = s.Erhoeht;
            Sammler.Versuch(s, "wmi.dienste", () => Dienste(A));
            Sammler.Versuch(s, "registry.winlogon", () => Winlogon(A));
            Sammler.Versuch(s, "registry.appinit", () => AppInit(A));
        }

        // ---------------------------------------------------------------- Registry Run / RunOnce

        static void RunSchluessel(Systembild s, Kern.Autostart A)
        {
            int geoeffnet = 0;
            // HKCU\SOFTWARE ist nicht WOW-umgeleitet (nur HKCU\Software\Classes): die 32-Bit-Sicht zeigt
            // denselben Schluessel wie die 64-Bit-Sicht. Ohne diese Menge staende jeder HKCU-Eintrag doppelt da.
            var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                string hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                foreach (string unter in new[] { "Run", "RunOnce" })
                    foreach (var sicht in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    {
                        string quelle = hiveName + "\\" + unter + (sicht == RegistryView.Registry32 ? "32" : "");
                        using (var basis = RegistryKey.OpenBaseKey(hive, sicht))
                        using (var k = basis.OpenSubKey(RunBasis + unter, false))
                        {
                            if (k == null) continue;
                            geoeffnet++;
                            foreach (string name in k.GetValueNames())
                            {
                                if (string.IsNullOrEmpty(name)) continue; // Standardwert ist kein Autostart
                                object roh = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                string befehl = roh == null ? null : Convert.ToString(roh, CultureInfo.InvariantCulture);
                                if (!gesehen.Add(hiveName + "|" + unter + "|" + name + "|" + befehl)) continue;
                                var sig = ZielSignatur(befehl);
                                A.Eintraege.Add(new AutostartEintrag
                                {
                                    Quelle = quelle,
                                    Name = name,
                                    Befehl = befehl,
                                    Signierer = sig.Subject,
                                    Microsoft = sig.Microsoft,
                                    DateiVorhanden = sig.DateiVorhanden,
                                });
                            }
                        }
                    }
            }
            if (geoeffnet == 0) Sammler.Fehler(s, "registry.autostart.run", Fehler.Fehlt, "kein Run-/RunOnce-Schlüssel lesbar");
        }

        // ---------------------------------------------------------------- Startup-Ordner

        /// <summary>
        /// Explorer startet beim Anmelden JEDE Datei im Startup-Ordner ueber ihre Verknuepfung
        /// (.bat, .cmd, .vbs, .js, .ps1, .url, .scr, sogar .txt). Nur desktop.ini (versteckt +
        /// system) beschreibt den Ordner selbst. Eine .lnk wird ueber WScript.Shell aufgeloest,
        /// damit Signierer und Herkunft vom Ziel kommen; scheitert das, bleibt sie sichtbar als
        /// "nicht pruefbar, nie Microsoft".
        /// </summary>
        static void StartupOrdner(Kern.Autostart A)
        {
            var ordner = new[]
            {
                new KeyValuePair<Environment.SpecialFolder, string>(Environment.SpecialFolder.Startup, "Startup"),
                new KeyValuePair<Environment.SpecialFolder, string>(Environment.SpecialFolder.CommonStartup, "CommonStartup"),
            };
            const FileAttributes Unsichtbar = FileAttributes.Hidden | FileAttributes.System;
            foreach (var o in ordner)
            {
                string dir = Environment.GetFolderPath(o.Key);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(f);
                    FileAttributes attr = 0;
                    try { attr = File.GetAttributes(f); } catch (Exception) { }
                    if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase) || (attr & Unsichtbar) == Unsichtbar) continue;
                    bool lnk = string.Equals(Path.GetExtension(f), ".lnk", StringComparison.OrdinalIgnoreCase);
                    var sig = lnk ? Verknuepfung(f) : Authenticode.Pruefen(f);
                    A.Eintraege.Add(new AutostartEintrag
                    {
                        Quelle = o.Value,
                        Name = name,
                        Befehl = f,
                        Signierer = sig.Subject,
                        Microsoft = sig.Microsoft,
                        DateiVorhanden = sig.DateiVorhanden,
                    });
                }
            }
        }

        /// <summary>
        /// Ziel einer .lnk ueber IShellLinkW/IPersistFile (Doku: shobjidl_core/nn-shobjidl_core-ishelllinkw),
        /// bewusst NICHT ueber WScript.Shell: der Skript-Host kann Prozesse starten, und das
        /// Prozessverbot des Sammlers ist ein Test, kein Vorsatz. Ziel plus Argumente laufen durch ZielSignatur.
        /// </summary>
        static Signatur Verknuepfung(string lnk)
        {
            string ziel = null, args = null;
            try
            {
                var link = (IShellLinkW)new ShellLink();
                try
                {
                    ((IPersistFile)link).Load(lnk, 0);
                    var sb = new System.Text.StringBuilder(1024);
                    var fd = new WIN32_FIND_DATAW();
                    link.GetPath(sb, sb.Capacity, ref fd, SLGP_RAWPATH);
                    ziel = Environment.ExpandEnvironmentVariables(sb.ToString());
                    sb.Length = 0;
                    link.GetArguments(sb, sb.Capacity);
                    args = sb.ToString();
                }
                finally { Marshal.ReleaseComObject(link); }
            }
            catch (Exception) { ziel = null; }
            if (string.IsNullOrWhiteSpace(ziel)) return Signatur.NichtPruefbar();
            return ZielSignatur("\"" + ziel.Trim() + "\"" + (string.IsNullOrWhiteSpace(args) ? "" : " " + args));
        }

        const uint SLGP_RAWPATH = 0x4;   // Pfad mit Umgebungsvariablen, wie er in der .lnk steht

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, ref WIN32_FIND_DATAW pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        // ---------------------------------------------------------------- StartupApproved

        /// <summary>
        /// Der Task-Manager-Status. Undokumentiert; lokal belegt: Byte 0 = 0x02 aktiv, 0x03
        /// deaktiviert mit FILETIME in Byte 4..11 (OneDrive 2025-01-08, Steam 2025-01-16).
        /// 0x00 und 0x06 beobachtet, ungeklaert -> Aktiviert bleibt null ("unbekannt").
        /// Common-Startup-Eintraege stehen unter HKLM\...\StartupFolder (simplicheck.lnk deaktiviert seit 2026-05-19).
        /// </summary>
        static void StartupApproved(Kern.Autostart A)
        {
            var zuordnung = new Dictionary<string, KeyValuePair<RegistryHive, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "HKLM\\Run", new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, "Run") },
                { "HKLM\\Run32", new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, "Run32") },
                { "HKCU\\Run", new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, "Run") },
                { "HKCU\\Run32", new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, "Run32") },
                { "Startup", new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, "StartupFolder") },
                { "CommonStartup", new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, "StartupFolder") },
            };
            var werte = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (var z in zuordnung)
            {
                var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                using (var basis = RegistryKey.OpenBaseKey(z.Value.Key, RegistryView.Registry64))
                using (var k = basis.OpenSubKey(ApprovedBasis + z.Value.Value, false))
                {
                    if (k != null)
                        foreach (string name in k.GetValueNames())
                        {
                            var b = k.GetValue(name) as byte[];
                            if (b != null) d[name] = b;
                        }
                }
                werte[z.Key] = d;
            }
            foreach (var e in A.Eintraege)
            {
                Dictionary<string, byte[]> d;
                byte[] b;
                if (e.Quelle == null || e.Name == null || !werte.TryGetValue(e.Quelle, out d) || !d.TryGetValue(e.Name, out b) || b.Length < 1) continue;
                if (b[0] == 2) e.Aktiviert = true;
                else if (b[0] == 3)
                {
                    e.Aktiviert = false;
                    if (b.Length >= 12)
                    {
                        long ft = BitConverter.ToInt64(b, 4);
                        if (ft > 0) { try { e.DeaktiviertSeitUtc = Zeit.Utc(DateTime.FromFileTimeUtc(ft)); } catch (ArgumentException) { } }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- Aufgaben (COM)

        /// <summary>
        /// Schedule.Service per spaeter Bindung (Type.GetTypeFromProgID + InvokeMember). Connect()
        /// hat vier optionale Parameter; ueber IDispatch muessen sie als Type.Missing mitgegeben
        /// werden, sonst DISP_E_BADPARAMCOUNT. GetTasks(1) = TASK_ENUM_HIDDEN. Einzelne Aufgaben
        /// ohne Leserecht werden uebersprungen; nicht erhoeht fehlen rund 70 (Widerlegung SP-20).
        /// </summary>
        static void Aufgaben(Kern.Autostart A)
        {
            Type t = Type.GetTypeFromProgID("Schedule.Service", false);
            if (t == null) throw new InvalidOperationException("Schedule.Service ist nicht registriert");
            object svc = Activator.CreateInstance(t);
            try
            {
                Com.Aufruf(svc, "Connect", Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                object wurzel = Com.Aufruf(svc, "GetFolder", "\\");
                try { Ordner(A, wurzel, Stopwatch.StartNew()); }
                finally { Com.Frei(wurzel); }
            }
            finally { Com.Frei(svc); }
        }

        static void Ordner(Kern.Autostart A, object ordner, Stopwatch uhr)
        {
            if (uhr.ElapsedMilliseconds > AufgabenBudgetMs) throw new TimeoutException("Aufgabenplanung: " + (AufgabenBudgetMs / 1000) + " s überschritten");
            object tasks = Com.Aufruf(ordner, "GetTasks", 1);
            try
            {
                int n = Convert.ToInt32(Com.Wert(tasks, "Count"), CultureInfo.InvariantCulture);
                for (int i = 1; i <= n; i++)
                {
                    object task = null;
                    try { task = Com.Wert(tasks, "Item", i); A.Aufgaben.Add(Aufgabe(task)); }
                    catch (Exception) { /* Aufgabe ohne Leserecht oder inzwischen geloescht */ }
                    finally { Com.Frei(task); }
                }
            }
            finally { Com.Frei(tasks); }

            object unter = Com.Aufruf(ordner, "GetFolders", 0);
            try
            {
                int m = Convert.ToInt32(Com.Wert(unter, "Count"), CultureInfo.InvariantCulture);
                for (int i = 1; i <= m; i++)
                {
                    object f = null;
                    try { f = Com.Wert(unter, "Item", i); Ordner(A, f, uhr); }
                    catch (TimeoutException) { throw; }
                    catch (Exception) { /* Unterordner ohne Leserecht */ }
                    finally { Com.Frei(f); }
                }
            }
            finally { Com.Frei(unter); }
        }

        static Aufgabe Aufgabe(object task)
        {
            var a = new Aufgabe();
            a.Pfad = Com.Wert(task, "Path") as string;
            a.Zustand = Convert.ToInt32(Com.Wert(task, "State"), CultureInfo.InvariantCulture);
            object lr = Com.Wert(task, "LastRunTime");
            // VT_DATE 0 = 30.12.1899 = nie gelaufen.
            if (lr is DateTime && ((DateTime)lr).Year > 1900) a.LetzterLaufUtc = Zeit.Utc((DateTime)lr);
            object erg = Com.Wert(task, "LastTaskResult");
            // HRESULT kommt als Int32 mit Vorzeichen; als vorzeichenlose Zahl bleibt 0x80070002 lesbar.
            if (erg != null) a.LetztesErgebnis = erg is int ? unchecked((uint)(int)erg) : Convert.ToInt64(erg, CultureInfo.InvariantCulture);

            string aktionPfad = null;
            object def = null, reg = null, trigger = null, aktionen = null;
            try
            {
                def = Com.Wert(task, "Definition");
                reg = Com.Wert(def, "RegistrationInfo");
                a.Autor = Com.Wert(reg, "Author") as string;
                trigger = Com.Wert(def, "Triggers");
                int n = Convert.ToInt32(Com.Wert(trigger, "Count"), CultureInfo.InvariantCulture);
                for (int i = 1; i <= n; i++)
                {
                    object tr = null;
                    try
                    {
                        tr = Com.Wert(trigger, "Item", i);
                        int typ = Convert.ToInt32(Com.Wert(tr, "Type"), CultureInfo.InvariantCulture);
                        if (typ == 9) a.Logon = true;  // TASK_TRIGGER_LOGON
                        if (typ == 8) a.Boot = true;   // TASK_TRIGGER_BOOT
                    }
                    finally { Com.Frei(tr); }
                }
                // Erste Exec-Aktion (TASK_ACTION_EXEC = 0): nur fuer die Frage "ist das unsere eigene EXE".
                try
                {
                    aktionen = Com.Wert(def, "Actions");
                    int m = Convert.ToInt32(Com.Wert(aktionen, "Count"), CultureInfo.InvariantCulture);
                    for (int i = 1; i <= m && aktionPfad == null; i++)
                    {
                        object ak = null;
                        try
                        {
                            ak = Com.Wert(aktionen, "Item", i);
                            if (Convert.ToInt32(Com.Wert(ak, "Type"), CultureInfo.InvariantCulture) == 0) aktionPfad = Com.Wert(ak, "Path") as string;
                        }
                        finally { Com.Frei(ak); }
                    }
                }
                catch (Exception) { /* Aktionen nicht lesbar: dann entscheidet nur der Name */ }
            }
            finally { Com.Frei(aktionen); Com.Frei(trigger); Com.Frei(reg); Com.Frei(def); }
            a.Eigen = IstEigeneAufgabe(a.Pfad, aktionPfad);
            return a;
        }

        /// <summary>
        /// Die Anmelde- und Wartungsaufgabe dieses Programms (Name "WindowsWartung-*" oder Aktion =
        /// eigene EXE) ist kein Fremd-Autostart; sonst meldet sich das Werkzeug selbst als Bremse.
        /// </summary>
        static bool IstEigeneAufgabe(string taskPfad, string aktionPfad)
        {
            if (!string.IsNullOrEmpty(taskPfad))
            {
                int i = taskPfad.LastIndexOf('\\');
                string name = i >= 0 ? taskPfad.Substring(i + 1) : taskPfad;
                if (name.StartsWith(EigeneAufgabenPraefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (string.IsNullOrWhiteSpace(aktionPfad)) return false;
            string eigene = EigeneExe();
            if (eigene == null) return false;
            string p = aktionPfad.Trim().Trim('"').Trim();
            try { p = Environment.ExpandEnvironmentVariables(p); } catch (Exception) { }
            return string.Equals(p, eigene, StringComparison.OrdinalIgnoreCase);
        }

        static string EigeneExe()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly();
                return asm == null || string.IsNullOrEmpty(asm.Location) ? null : asm.Location;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Spaete Bindung an IDispatch-Objekte ohne Interop-Assembly. InvokeMember verpackt jeden
        /// COM-Fehler in TargetInvocationException; ausgepackt traegt die fehlerliste HRESULT und
        /// Text des echten Fehlers, und 0x80070005 wird als "zugriff" statt "ausnahme" erkannt.
        /// </summary>
        static class Com
        {
            public static object Aufruf(object o, string name, params object[] args)
            {
                try { return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args, CultureInfo.InvariantCulture); }
                catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
            }
            public static object Wert(object o, string name, params object[] args)
            {
                try { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, args, CultureInfo.InvariantCulture); }
                catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
            }
            public static void Frei(object o)
            {
                if (o != null && Marshal.IsComObject(o)) { try { Marshal.ReleaseComObject(o); } catch (Exception) { } }
            }
        }

        // ---------------------------------------------------------------- Dienste

        static void Dienste(Kern.Autostart A)
        {
            var liste = Wmi.Abfrage(@"root\cimv2", "SELECT Name, DisplayName, StartMode, State, PathName, DelayedAutoStart FROM Win32_Service", 10000);
            if (liste.Count == 0) throw new InvalidOperationException("Win32_Service lieferte keine Instanz");
            foreach (var mo in liste)
            {
                string pfad = Wmi.Str(mo, "PathName");
                var sig = ZielSignatur(pfad);
                A.Dienste.Add(new Dienst
                {
                    Name = Wmi.Str(mo, "Name"),
                    Anzeige = Wmi.Str(mo, "DisplayName"),
                    // StartMode/State sind feste englische Werte; "Unknown" kommt nicht erhoeht vor (LSM, NetSetupSvc).
                    Startart = Wmi.Str(mo, "StartMode"),
                    Zustand = Wmi.Str(mo, "State"),
                    Pfad = pfad,
                    Verzoegert = Wmi.Wahr(mo, "DelayedAutoStart") ?? false,
                    Signierer = sig.Subject,
                    Microsoft = sig.Microsoft,
                });
            }
        }

        // ---------------------------------------------------------------- Winlogon, AppInit

        static void Winlogon(Kern.Autostart A)
        {
            using (var basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", false))
            {
                if (k == null) throw new InvalidOperationException("Winlogon-Schlüssel nicht lesbar");
                A.Shell = Convert.ToString(k.GetValue("Shell", null, RegistryValueOptions.DoNotExpandEnvironmentNames), CultureInfo.InvariantCulture);
                A.Userinit = Convert.ToString(k.GetValue("Userinit", null, RegistryValueOptions.DoNotExpandEnvironmentNames), CultureInfo.InvariantCulture);
                if (A.Shell == "") A.Shell = null;
                if (A.Userinit == "") A.Userinit = null;
            }
        }

        /// <summary>
        /// AppInit_DLLs in beiden Sichten (64 Bit und WOW6432Node) samt Hauptschalter
        /// LoadAppInit_DLLs (REG_DWORD; 0 = aus, Vorgabe seit Windows 8; fehlender Wert = 0).
        /// Bei Secure Boot ist der Mechanismus ohnehin abgeschaltet (Doku "Secure Boot and
        /// AppInit_DLLs"); die Regel prueft das. Das Modell hat einen Schalter fuer beide
        /// Sichten: er gilt als "an", wenn eine Sicht mit eingetragener Liste ihren Schalter auf
        /// 1 hat; sind beide Listen leer, zaehlt die 64-Bit-Sicht.
        /// </summary>
        static void AppInit(Kern.Autostart A)
        {
            string dlls64, dlls32;
            bool load64, load32;
            Lies(RegistryView.Registry64, out dlls64, out load64);
            try { Lies(RegistryView.Registry32, out dlls32, out load32); }
            catch (Exception) { dlls32 = null; load32 = false; }
            A.AppInitDlls = dlls64;
            A.AppInitDlls32 = dlls32;
            bool da64 = !string.IsNullOrWhiteSpace(dlls64), da32 = !string.IsNullOrWhiteSpace(dlls32);
            A.LoadAppInitDlls = da64 || da32 ? (da64 && load64) || (da32 && load32) : load64;
        }

        static void Lies(RegistryView sicht, out string dlls, out bool load)
        {
            using (var basis = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, sicht))
            using (var k = basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", false))
            {
                if (k == null) throw new InvalidOperationException("Windows-Schlüssel (AppInit, " + (sicht == RegistryView.Registry32 ? "32" : "64") + " Bit) nicht lesbar");
                object v = k.GetValue("AppInit_DLLs", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                // "" = Wert vorhanden und leer (Normalfall), null = Wert fehlt; beides ist "nichts eingetragen".
                dlls = v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
                object l = k.GetValue("LoadAppInit_DLLs", null);
                load = l is int && (int)l != 0;
            }
        }

        // ---------------------------------------------------------------- Ziel einer Befehlszeile

        /// <summary>
        /// Windows-Programme, die nur ausfuehren, was ihnen als Argument uebergeben wird. Steht so
        /// eines aus %SystemRoot% am Anfang der Befehlszeile, entscheidet das Argument (DLL, Skript,
        /// EXE) ueber Signierer und Herkunft, nie der Host: "rundll32.exe fremd.dll,Start" ist
        /// Fremdsoftware, auch wenn rundll32.exe von Microsoft signiert ist.
        /// </summary>
        static readonly string[] Hosts = { "rundll32", "regsvr32", "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "explorer", "msiexec", "conhost", "forfiles", "wmic" };   // Namensliste, kein Prozessstart

        /// <summary>Endungen, hinter denen ein Pfad ohne Anfuehrungszeichen endet (Leerzeichen im Pfad kommen vor).</summary>
        static readonly string[] Endungen = { ".exe", ".dll", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".ps1", ".hta", ".msi", ".msp", ".scr", ".cpl", ".ocx", ".ax", ".com", ".pif", ".url", ".lnk", ".sys" };

        /// <summary>
        /// Signatur dessen, was eine Befehlszeile wirklich startet: die erste Binaerdatei, oder
        /// bei einem Host (siehe Hosts) ihr erstes Pfad-Argument. Ein Host mit Nutzlast ohne Pfad
        /// ("mshta vbscript:...", "powershell -enc ...") ist nicht pruefbar, aber nie Microsoft;
        /// ein Host nur mit Schaltern ("msiexec.exe /V", gemessen beim Dienst msiserver) laeuft selbst.
        /// </summary>
        public static Signatur ZielSignatur(string befehl)
        {
            string erste = Aufloesen(PfadAusBefehl(befehl));
            if (erste == null) return string.IsNullOrWhiteSpace(befehl) ? new Signatur() : Signatur.NichtPruefbar();
            if (!IstHost(erste)) return Authenticode.Pruefen(erste);
            string arg = Aufloesen(ErstesPfadArgument(befehl));
            if (arg != null) return Authenticode.Pruefen(arg);
            return NurSchalter(befehl) ? Authenticode.Pruefen(erste) : Signatur.NichtPruefbar();
        }

        /// <summary>true, wenn hinter dem Host nichts oder nur Schalter ("/V", "-Embedding") stehen.</summary>
        static bool NurSchalter(string befehl)
        {
            string t = befehl.Trim();
            int start;
            if (t.StartsWith("\"", StringComparison.Ordinal)) { int e = t.IndexOf('"', 1); start = e < 0 ? t.Length : e + 1; }
            else { int sp = t.IndexOf(' '); start = sp < 0 ? t.Length : sp; }
            foreach (string wort in t.Substring(start).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!wort.StartsWith("/", StringComparison.Ordinal) && !wort.StartsWith("-", StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// Ein Host bleibt ein Host, wo immer er liegt: pwsh.exe unter Program Files ist von
        /// Microsoft signiert und startet trotzdem nur, was ihm uebergeben wird. Deshalb zaehlt
        /// allein der Name, nicht der Ordner.
        /// </summary>
        static bool IstHost(string pfad)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(pfad).ToLowerInvariant();
                return Array.IndexOf(Hosts, name) >= 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Ein Name ohne Verzeichnis ("rundll32.exe", "powershell") wird wie von CreateProcess ueber
        /// System32, den Windows-Ordner und PATH gesucht (powershell.exe liegt unter
        /// System32\WindowsPowerShell\v1.0, nur ueber PATH), nicht ueber File.Exists relativ zum
        /// Arbeitsverzeichnis. Nicht auffindbar -> null (nicht pruefbar, nie "startet nichts").
        /// </summary>
        static string Aufloesen(string kandidat)
        {
            if (string.IsNullOrEmpty(kandidat)) return null;
            if (kandidat.IndexOf('\\') >= 0 || kandidat.IndexOf('/') >= 0 || kandidat.IndexOf(':') >= 0) return kandidat;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windows)) return null;
            bool ohneEndung;
            try { ohneEndung = string.IsNullOrEmpty(Path.GetExtension(kandidat)); } catch (Exception) { return null; }
            var ordner = new List<string> { Path.Combine(windows, "System32"), windows };
            try { ordner.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)); } catch (Exception) { }
            foreach (string dir in ordner)
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), kandidat);
                    if (File.Exists(p)) return p;
                    if (ohneEndung && File.Exists(p + ".exe")) return p + ".exe";
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>
        /// Das erste Argument hinter dem Host, das wie ein Dateipfad aussieht (Laufwerk, %VAR% oder
        /// UNC am Anfang, oder ein Wort mit bekannter Endung): endet hinter der ersten bekannten
        /// Endung an einer Grenze (Leerzeichen, Komma, Anfuehrungszeichen, Ende), sonst am
        /// schliessenden Anfuehrungszeichen bzw. am naechsten Leerzeichen oder Komma. Schalter
        /// ("/s", "-File") und Woerter ohne Pfadmerkmal ("start", "Hidden") werden uebergangen.
        /// </summary>
        public static string ErstesPfadArgument(string befehl)
        {
            if (string.IsNullOrWhiteSpace(befehl)) return null;
            string t = befehl.Trim();
            int start;
            if (t.StartsWith("\"", StringComparison.Ordinal)) { int e = t.IndexOf('"', 1); start = e < 0 ? t.Length : e + 1; }
            else { int sp = t.IndexOf(' '); start = sp < 0 ? t.Length : sp; }
            string rest = t.Substring(start);

            for (int p = 0; p < rest.Length; p++)
            {
                if (!PfadBeginn(rest, p)) continue;
                char vor = p > 0 ? rest[p - 1] : ' ';
                if (vor != ' ' && vor != '"' && vor != '\'' && vor != '=' && vor != ',') continue;
                // Ein Pfad innerhalb eines Schalters ("/i:"C:\x\y"") ist ein Parameter, nicht das Ziel.
                int tokenStart = rest.LastIndexOf(' ', p) + 1;
                if (tokenStart < p && (rest[tokenStart] == '/' || rest[tokenStart] == '-')) continue;
                // In Anfuehrungszeichen endet der Pfad spaetestens am schliessenden Zeichen.
                bool zitiert = vor == '"' || vor == '\'';
                int grenze = zitiert ? rest.IndexOf(vor, p) : -1;
                if (grenze < 0) grenze = rest.Length;
                int ende = EndungsEnde(rest, p, grenze);
                if (ende < 0)
                {
                    if (zitiert) ende = grenze;
                    else { int sp = rest.IndexOf(' ', p), ko = rest.IndexOf(',', p); ende = sp < 0 ? ko : (ko < 0 ? sp : Math.Min(sp, ko)); }
                    if (ende < 0) ende = rest.Length;
                }
                string kandidat = rest.Substring(p, ende - p).Trim();
                if (kandidat.Length == 0) continue;
                try { kandidat = Environment.ExpandEnvironmentVariables(kandidat); } catch (Exception) { }
                return kandidat;
            }
            // Kein Pfad, aber ein Wort mit bekannter Endung ("rundll32 fremd.dll,Start"): Aufloesen sucht es in System32.
            foreach (string wort in rest.Split(new[] { ' ', ',', '"' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (wort.StartsWith("/", StringComparison.Ordinal) || wort.StartsWith("-", StringComparison.Ordinal)) continue;
                string endung;
                try { endung = Path.GetExtension(wort).ToLowerInvariant(); } catch (Exception) { continue; }
                if (Array.IndexOf(Endungen, endung) >= 0) return wort;
            }
            return null;
        }

        static bool PfadBeginn(string s, int p)
        {
            if (s[p] == '%') return true;
            if (s[p] == '\\' && p + 1 < s.Length && s[p + 1] == '\\') return true;
            return char.IsLetter(s[p]) && p + 2 < s.Length && s[p + 1] == ':' && (s[p + 2] == '\\' || s[p + 2] == '/');
        }

        /// <summary>Position hinter der ersten bekannten Endung in s[p..grenze), wenn dort eine Grenze folgt; sonst -1.</summary>
        static int EndungsEnde(string s, int p, int grenze)
        {
            int best = -1;
            foreach (string e in Endungen)
            {
                int i = s.IndexOf(e, p, grenze - p, StringComparison.OrdinalIgnoreCase);
                while (i >= 0)
                {
                    int nach = i + e.Length;
                    if (nach >= grenze || s[nach] == ' ' || s[nach] == ',' || s[nach] == '"' || s[nach] == '\'')
                    {
                        if (best < 0 || nach < best) best = nach;
                        break;
                    }
                    i = nach < grenze ? s.IndexOf(e, nach, grenze - nach, StringComparison.OrdinalIgnoreCase) : -1;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- Pfad aus Befehl

        /// <summary>
        /// Erste Binaerdatei aus einer Befehlszeile: in Anfuehrungszeichen bis zum schliessenden,
        /// sonst bis hinter das erste ".exe" (Pfade mit Leerzeichen ohne Anfuehrungszeichen kommen
        /// in Dienstpfaden vor), sonst das erste Token. Umgebungsvariablen werden aufgeloest.
        /// </summary>
        public static string PfadAusBefehl(string befehl)
        {
            if (string.IsNullOrWhiteSpace(befehl)) return null;
            string t = befehl.Trim();
            string kandidat;
            if (t.StartsWith("\"", StringComparison.Ordinal))
            {
                int e = t.IndexOf('"', 1);
                kandidat = e > 1 ? t.Substring(1, e - 1) : t.Substring(1);
            }
            else
            {
                int i = t.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i > 0) kandidat = t.Substring(0, i + 4);
                else
                {
                    int sp = t.IndexOf(' ');
                    kandidat = sp > 0 ? t.Substring(0, sp) : t;
                }
            }
            kandidat = kandidat.Trim();
            try { kandidat = Environment.ExpandEnvironmentVariables(kandidat); } catch (Exception) { }
            // "\??\C:\..." (NT-Namensraum), "\\?\D:\..." (erweiterter Win32-Pfad, gemessen bei GOG Galaxy) und
            // "\SystemRoot\..." auf gewoehnliche Win32-Pfade abbilden; File.Exists mag die Praefixe nicht.
            if (kandidat.StartsWith(@"\??\", StringComparison.Ordinal) || kandidat.StartsWith(@"\\?\", StringComparison.Ordinal)) kandidat = kandidat.Substring(4);
            if (kandidat.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                kandidat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), kandidat.Substring(12));
            else if (kandidat.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                kandidat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), kandidat);
            return kandidat.Length == 0 ? null : kandidat;
        }

        // ---------------------------------------------------------------- Authenticode

        public class Signatur
        {
            /// <summary>Subject des Signierers (eingebettet oder aus dem Katalog), nur nach bestandener Pruefung; null = keine gueltige Signatur.</summary>
            public string Subject;
            /// <summary>false = Datei nicht auffindbar (startet nichts); null = nicht geprueft (kein Pfad ermittelbar).</summary>
            public bool? DateiVorhanden;
            /// <summary>true = gueltige Microsoft-Signatur, false = fremd, unsigniert oder nicht pruefbar, null = Datei nicht auffindbar.</summary>
            public bool? Microsoft;

            /// <summary>Kein Pfad ermittelbar (Inline-Skript ueber einen Host, .lnk ohne Ziel): sichtbar als fremd, nie still verschwunden.</summary>
            public static Signatur NichtPruefbar() { return new Signatur { Subject = null, DateiVorhanden = null, Microsoft = false }; }
        }

        /// <summary>
        /// Signierer einer Datei nach GEPRUEFTER Signatur: erst eingebettet (WinVerifyTrust mit
        /// WTD_CHOICE_FILE, dann CreateFromSignedFile fuer das Subject), dann ueber die
        /// Katalogdatenbank (CryptCATAdminCalcHashFromFileHandle2 + CryptCATAdminEnumCatalogFromHash
        /// + WinVerifyTrust mit WTD_CHOICE_CATALOG; SHA-256, Rueckfall SHA-1 fuer alte Kataloge).
        /// Nur S_OK zaehlt: eine abgelaufene, aber zeitgestempelte Signatur liefert WinVerifyTrust
        /// ohne WTD_LIFETIME_SIGNING_FLAG selbst als S_OK; CERT_E_EXPIRED bleibt damit "ungueltig".
        /// Keine Sperrlistenabfrage im Netz (WTD_REVOKE_NONE, WTD_CACHE_ONLY_URL_RETRIEVAL).
        /// Ergebnisse werden je Pfad zwischengespeichert: 232 von 343 Diensten laufen in svchost.exe.
        /// </summary>
        public static class Authenticode
        {
            static readonly Dictionary<string, Signatur> Cache = new Dictionary<string, Signatur>(StringComparer.OrdinalIgnoreCase);

            public static Signatur Pruefen(string pfad)
            {
                if (string.IsNullOrEmpty(pfad)) return new Signatur();
                Signatur sig;
                lock (Cache) if (Cache.TryGetValue(pfad, out sig)) return sig;
                sig = new Signatur();
                bool da = false;
                try { da = File.Exists(pfad); } catch (Exception) { }
                sig.DateiVorhanden = da;
                if (da) sig.Subject = Eingebettet(pfad) ?? AusKatalog(pfad);
                sig.Microsoft = IstMicrosoft(sig);
                lock (Cache) Cache[pfad] = sig;
                return sig;
            }

            /// <summary>
            /// "CN=Microsoft Windows", "CN=Microsoft Windows Publisher", "CN=Microsoft Corporation".
            /// NICHT "Microsoft Windows Hardware Compatibility Publisher": damit signiert Microsoft
            /// die Treiberpakete FREMDER Hersteller (WHQL); ein ASUS-Dienst aus so einem Paket ist
            /// Fremdsoftware. Datei vorhanden, aber ohne gueltige Signatur = fremd; Datei nicht auffindbar = unbekannt.
            /// </summary>
            static bool? IstMicrosoft(Signatur sig)
            {
                if (sig.Subject != null)
                {
                    if (sig.Subject.IndexOf("Hardware Compatibility Publisher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    return sig.Subject.IndexOf("CN=Microsoft Windows", StringComparison.OrdinalIgnoreCase) >= 0
                        || sig.Subject.IndexOf("CN=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                return sig.DateiVorhanden == true ? (bool?)false : null;
            }

            /// <summary>Subject der eingebetteten Signatur, nur wenn WinVerifyTrust sie als gueltig bestaetigt.</summary>
            static string Eingebettet(string pfad)
            {
                if (VerifyDatei(pfad) != 0) return null;
                return Subject(pfad);
            }

            static string Subject(string pfad)
            {
                try { using (var c = X509Certificate.CreateFromSignedFile(pfad)) return c.Subject; }
                catch (Exception) { return null; }
            }

            static string AusKatalog(string pfad)
            {
                return AusKatalog(pfad, "SHA256") ?? AusKatalog(pfad, "SHA1");
            }

            /// <summary>
            /// Hash der Datei, Kataloge dazu aufzaehlen und den ersten nehmen, dessen Mitglied
            /// WinVerifyTrust bestaetigt. Der Katalog ist eine PKCS#7-Signatur; sein Signierer
            /// (gemessen: CN=Microsoft Windows fuer Windows-Kataloge, CN=Microsoft Windows Hardware
            /// Compatibility Publisher fuer WHQL-Treiberpakete) ist der Signierer der Datei.
            /// </summary>
            static string AusKatalog(string pfad, string algorithmus)
            {
                IntPtr admin = IntPtr.Zero, info = IntPtr.Zero;
                try
                {
                    if (!CryptCATAdminAcquireContext2(out admin, IntPtr.Zero, algorithmus, IntPtr.Zero, 0)) return null;
                    byte[] hash;
                    using (var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                        uint groesse = 0;
                        IntPtr h = fs.SafeFileHandle.DangerousGetHandle();
                        // Erster Aufruf liefert nur die Puffergroesse.
                        CryptCATAdminCalcHashFromFileHandle2(admin, h, ref groesse, null, 0);
                        if (groesse == 0 || groesse > 128) return null;
                        hash = new byte[groesse];
                        if (!CryptCATAdminCalcHashFromFileHandle2(admin, h, ref groesse, hash, 0)) return null;
                    }
                    string tag = BitConverter.ToString(hash).Replace("-", "");
                    IntPtr vorher = IntPtr.Zero;
                    for (int versuch = 0; versuch < 5; versuch++)
                    {
                        info = CryptCATAdminEnumCatalogFromHash(admin, hash, (uint)hash.Length, 0, ref vorher);
                        if (info == IntPtr.Zero) return null;
                        vorher = info;
                        var ci = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(CATALOG_INFO)) };
                        if (!CryptCATCatalogInfoFromContext(info, ref ci, 0) || string.IsNullOrEmpty(ci.wszCatalogFile)) continue;
                        if (VerifyKatalog(ci.wszCatalogFile, tag, pfad, hash, admin) != 0) continue;
                        return Subject(ci.wszCatalogFile) ?? ("Katalog " + Path.GetFileName(ci.wszCatalogFile));
                    }
                    return null;
                }
                catch (Exception) { return null; }
                finally
                {
                    // Enum gibt den vorherigen Kontext selbst frei; nur der letzte bleibt offen.
                    if (info != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(admin, info, 0);
                    if (admin != IntPtr.Zero) CryptCATAdminReleaseContext(admin, 0);
                }
            }

            // ------------------------------------------------------------ WinVerifyTrust

            const uint WTD_UI_NONE = 2;
            const uint WTD_REVOKE_NONE = 0;
            const uint WTD_CHOICE_FILE = 1;
            const uint WTD_CHOICE_CATALOG = 2;
            const uint WTD_STATEACTION_VERIFY = 1;
            const uint WTD_STATEACTION_CLOSE = 2;
            const uint WTD_REVOCATION_CHECK_NONE = 0x10;
            const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
            static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

            static int VerifyDatei(string pfad)
            {
                var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = pfad };
                IntPtr pfi = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                try
                {
                    Marshal.StructureToPtr(fi, pfi, false);
                    return Verify(WTD_CHOICE_FILE, pfi);
                }
                finally { Marshal.DestroyStructure(pfi, typeof(WINTRUST_FILE_INFO)); Marshal.FreeHGlobal(pfi); }
            }

            static int VerifyKatalog(string katalog, string tag, string mitglied, byte[] hash, IntPtr admin)
            {
                IntPtr phash = Marshal.AllocHGlobal(hash.Length);
                IntPtr pci = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_CATALOG_INFO)));
                try
                {
                    Marshal.Copy(hash, 0, phash, hash.Length);
                    var ci = new WINTRUST_CATALOG_INFO
                    {
                        cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_CATALOG_INFO)),
                        pcwszCatalogFilePath = katalog,
                        pcwszMemberTag = tag,
                        pcwszMemberFilePath = mitglied,
                        pbCalculatedFileHash = phash,
                        cbCalculatedFileHash = (uint)hash.Length,
                        hCatAdmin = admin,
                    };
                    Marshal.StructureToPtr(ci, pci, false);
                    return Verify(WTD_CHOICE_CATALOG, pci);
                }
                finally { Marshal.DestroyStructure(pci, typeof(WINTRUST_CATALOG_INFO)); Marshal.FreeHGlobal(pci); Marshal.FreeHGlobal(phash); }
            }

            /// <summary>0 = S_OK; sonst der HRESULT (TRUST_E_NOSIGNATURE 0x800B0100, CERT_E_UNTRUSTEDROOT 0x800B0109, TRUST_E_BAD_DIGEST 0x80096010, ...).</summary>
            static int Verify(uint wahl, IntPtr pUnion)
            {
                var wd = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = wahl,
                    pUnion = pUnion,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                };
                IntPtr pwd = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
                try
                {
                    Marshal.StructureToPtr(wd, pwd, false);
                    int hr;
                    try { hr = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pwd); }
                    catch (Exception) { return unchecked((int)0x80004005); } // E_FAIL: wintrust nicht ladbar
                    // Zustand freigeben (VERIFY oeffnet ihn), Ergebnis davon unabhaengig.
                    wd = (WINTRUST_DATA)Marshal.PtrToStructure(pwd, typeof(WINTRUST_DATA));
                    wd.dwStateAction = WTD_STATEACTION_CLOSE;
                    Marshal.StructureToPtr(wd, pwd, false);
                    try { WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pwd); } catch (Exception) { }
                    return hr;
                }
                finally { Marshal.FreeHGlobal(pwd); }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct CATALOG_INFO
            {
                public uint cbStruct;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string wszCatalogFile;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct WINTRUST_FILE_INFO
            {
                public uint cbStruct;
                [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
                public IntPtr hFile;
                public IntPtr pgKnownSubject;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct WINTRUST_CATALOG_INFO
            {
                public uint cbStruct;
                public uint dwCatalogVersion;
                [MarshalAs(UnmanagedType.LPWStr)] public string pcwszCatalogFilePath;
                [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberTag;
                [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberFilePath;
                public IntPtr hMemberFile;
                public IntPtr pbCalculatedFileHash;
                public uint cbCalculatedFileHash;
                public IntPtr pcCatalogContext;
                public IntPtr hCatAdmin;
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
                public IntPtr pUnion;
                public uint dwStateAction;
                public IntPtr hWVTStateData;
                public IntPtr pwszURLReference;
                public uint dwProvFlags;
                public uint dwUIContext;
                public IntPtr pSignatureSettings;
            }

            [DllImport("wintrust.dll", ExactSpelling = true)]
            static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);
            [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool CryptCATAdminAcquireContext2(out IntPtr phCatAdmin, IntPtr pgSubsystem, string pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);
            [DllImport("wintrust.dll", SetLastError = true)]
            static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin, IntPtr hFile, ref uint pcbHash, byte[] pbHash, uint dwFlags);
            [DllImport("wintrust.dll", SetLastError = true)]
            static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, byte[] pbHash, uint cbHash, uint dwFlags, ref IntPtr phPrevCatInfo);
            [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, ref CATALOG_INFO psCatInfo, uint dwFlags);
            [DllImport("wintrust.dll", SetLastError = true)]
            static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);
            [DllImport("wintrust.dll", SetLastError = true)]
            static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);
        }
    }
}
