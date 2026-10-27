using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Windows-Wartung")]
[assembly: AssemblyProduct("Windows-Wartung")]
[assembly: AssemblyDescription("PC-Reparatur-System: misst, zeigt, fragt, repariert nachvollziehbar")]
[assembly: AssemblyCompany("Jonas")]
[assembly: AssemblyCopyright("© 2026 Jonas")]
[assembly: AssemblyVersion("8.3.0.0")]
[assembly: AssemblyFileVersion("8.3.0.0")]

// DLL-Suchpfad fuer jeden DllImport dieser Assembly (M4-Entwurf 10.3, Pruefbefund L1 der
// Phase 2, Nachbesserung N2): NUR System32. Ohne das Attribut prueft die CLR bei einem
// DllImport ZUERST im Ordner der Assembly (LegacyBehavior, mit vollem Pfad), und
// SetDefaultDllDirectories in host\Program.cs greift dort nicht; gemessen: eine als
// wintrust.dll gepflanzte version.dll neben der EXE wurde geladen. Der Programmordner ist bei
// einer ZIP-Installation benutzerbeschreibbar, der Helfer laeuft aus demselben Ordner erhoeht;
// eine gepflanzte wintrust.dll oder dismapi.dll liefe also mit Administratorrechten und
// schaltete dabei die Startpruefung einer signierten Fassung auf "unsigniert, ok".
//
// Warum nicht System32 | UserDirectories: UserDirectories sind die per AddDllDirectory
// eingetragenen Ordner, und host\Program.cs traegt den Programmordner ein. Der Windows-Lader
// sucht LOAD_LIBRARY_SEARCH_USER_DIRS VOR LOAD_LIBRARY_SEARCH_SYSTEM32, jeder DllImport kaeme
// damit zuerst im Programmordner an. Gemessen (Pruefung N2a): mit der Kombination lud eine
// EXE-Kopie eine als dwmapi.dll gepflanzte version.dll aus dem Programmordner (--shot start,
// Modulliste); eine Sonde mit demselben Attribut und demselben Setup lud tbs.dll, mpr.dll und
// dismapi.dll von dort (EntryPointNotFoundException). Dass die fruehere Messung mit einer
// gepflanzten wintrust.dll System32 traf, beweist keine Suchreihenfolge: wintrust.dll steht
// in \KnownDlls (42 Sektionen gelesen), und der Lader nimmt einen KnownDll-Namen aus der
// Sektion, bevor er sucht. Das gilt fuer 7 der 13 importierten Namen (advapi32, kernel32,
// ntdll, sechost, shell32, user32, wintrust), nicht fuer dismapi, dwmapi, mpr, tbs, winhttp,
// wscapi; ein Beleg fuer die Suchreihenfolge braucht einen dieser sechs Namen.
//
// Mit System32 allein kommen alle 13 importierten DLLs aus %windir%\System32; ein Name, der
// dort fehlt, endet in DllNotFoundException (fail closed), nie in einer DLL aus dem
// Arbeitsverzeichnis oder aus dem Programmordner. WebView2Loader.dll braucht das Attribut
// nicht: Microsoft.Web.WebView2.Core.dll traegt kein DefaultDllImportSearchPaths (per
// Reflection 0 Attribute an 9 DllImports) und laedt sie als LegacyBehavior aus dem Ordner
// der eigenen Assembly, also aus dem Programmordner; das Attribut hier gilt nur fuer diese
// Assembly. Was ein LoadLibrary ohne Flags und die Abhaengigkeiten einer System32-DLL
// (cryptbase.dll fuer wintrust) laden, regelt allein SetDefaultDllDirectories in
// host\Program.cs, nicht dieses Attribut.
// Beleg: EXE-Kopie mit einer als dwmapi.dll und einer als wintrust.dll gepflanzten
// version.dll, --shot --view start Exit 0, Modulliste des laufenden Prozesses
// C:\WINDOWS\SYSTEM32\dwmapi.dll und C:\WINDOWS\System32\wintrust.dll, WebView2Loader.dll
// aus dem Programmordner; --selbstpruefung an derselben Kopie Exit 0.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
