using System;
using System.Collections.Generic;
using System.Text;

namespace WartungsToolbox
{
    static class Catalog
    {
        public static readonly string[] Categories = { "Reparieren", "Netzwerk", "Aufräumen", "Diagnose" };
        // Segoe-MDL2-Glyphen je Kategorie (Hex-Codepoints)
        public static readonly string[] CategoryGlyphs = { "E90F", "E774", "E74D", "E9D9" };

        // Hex-Codepoint -> Zeichen
        public static string Glyph(string hex)
        {
            try { return char.ConvertFromUtf32(Convert.ToInt32(hex, 16)); }
            catch { return "?"; }
        }

        static Step Cmd(string c)  { return new Step { File = "cmd.exe", Args = "/c " + c }; }
        static Step CmdBE(string c){ return new Step { File = "cmd.exe", Args = "/c " + c, IgnoreExit = true }; } // best-effort
        static Step Ps(string c)   { return new Step { File = "powershell.exe", Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + c + "\"" }; }
        static Step Dism(string a) { return new Step { File = "DISM.exe", Args = a, Progress = true }; }
        static Step Sfc(string a)  { return new Step { File = "sfc.exe", Args = a, Enc = Encoding.Unicode, Progress = true }; }

        public static List<MaintenanceAction> All()
        {
            var l = new List<MaintenanceAction>();

            // ---------- Reparieren ----------
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E90F", IsRepair = true,
                Title = "Komplett-Reparatur",
                Desc = "DISM ScanHealth + RestoreHealth, danach SFC. Der Rundum-Sorglos-Lauf.",
                Steps = {
                    Dism("/Online /Cleanup-Image /ScanHealth"),
                    Dism("/Online /Cleanup-Image /RestoreHealth"),
                    Sfc("/scannow"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E895", IsRepair = true,
                Title = "DISM RestoreHealth",
                Desc = "Repariert den Windows-Komponentenspeicher über Windows Update.",
                Steps = { Dism("/Online /Cleanup-Image /RestoreHealth") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E73E", IsRepair = true,
                Title = "SFC scannow",
                Desc = "Prüft und repariert geschützte Systemdateien.",
                Steps = { Sfc("/scannow") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E721",
                Title = "SFC nur prüfen",
                Desc = "Sucht nach beschädigten Systemdateien, ohne etwas zu ändern.",
                Steps = { Sfc("/verifyonly") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E74D",
                Title = "WinSxS aufräumen",
                Desc = "Entfernt veraltete Komponenten – macht oft mehrere GB frei.",
                Steps = { Dism("/Online /Cleanup-Image /StartComponentCleanup") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E9D9",
                Title = "Komponentenspeicher analysieren",
                Desc = "Zeigt, ob sich ein WinSxS-Cleanup überhaupt lohnt.",
                Steps = { Dism("/Online /Cleanup-Image /AnalyzeComponentStore") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E777", IsRepair = true,
                Title = "Windows-Update reparieren",
                Desc = "Setzt die Update-Komponenten zurück (SoftwareDistribution + catroot2).",
                // Best-effort: einzelne Schritte dürfen fehlschlagen (z. B. catroot2 gesperrt), ohne den ganzen Lauf als Fehler zu werten.
                Steps = {
                    CmdBE("net stop wuauserv"),
                    CmdBE("net stop bits"),
                    CmdBE("net stop cryptsvc"),
                    CmdBE("if exist \"%windir%\\SoftwareDistribution.old\" rd /s /q \"%windir%\\SoftwareDistribution.old\""),
                    CmdBE("ren \"%windir%\\SoftwareDistribution\" SoftwareDistribution.old"),
                    CmdBE("if exist \"%windir%\\System32\\catroot2.old\" rd /s /q \"%windir%\\System32\\catroot2.old\""),
                    CmdBE("ren \"%windir%\\System32\\catroot2\" catroot2.old"),
                    CmdBE("net start cryptsvc"),
                    CmdBE("net start bits"),
                    CmdBE("net start wuauserv"),
                    CmdBE("echo Windows-Update-Komponenten wurden zurueckgesetzt - bitte den PC neu starten."),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E7BA", Danger = true,
                Title = "CHKDSK planen",
                Desc = "Plant eine Datenträgerprüfung beim nächsten Neustart. Öffnet ein Fenster – dort die Rückfrage mit J bzw. Y bestätigen.",
                // chkdsk ist interaktiv (Rückfrage) und liest die Konsole, nicht die Pipe -> in eigenem,
                // sichtbarem (elevated) Fenster ausführen, damit die Rückfrage beantwortet werden kann.
                Steps = { new Step { File = "cmd.exe", Args = "/k chkdsk %SystemDrive% /f /r", Detached = true } }
            });

            // ---------- Netzwerk ----------
            l.Add(new MaintenanceAction {
                Category = "Netzwerk", Glyph = "E774", Danger = true,
                Title = "Netzwerk-Reset (komplett)",
                Desc = "DNS leeren, Winsock + IP-Stack zurücksetzen. Neustart danach empfohlen.",
                Steps = {
                    Cmd("ipconfig /flushdns"),
                    Cmd("netsh winsock reset"),
                    Cmd("netsh int ip reset"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Netzwerk", Glyph = "E774",
                Title = "DNS-Cache leeren",
                Desc = "Löscht den DNS-Auflösungscache (ipconfig /flushdns).",
                Steps = { Cmd("ipconfig /flushdns") }
            });
            l.Add(new MaintenanceAction {
                Category = "Netzwerk", Glyph = "E72C",
                Title = "IP-Adresse erneuern",
                Desc = "Gibt die IP frei und fordert eine neue an (release / renew).",
                Steps = {
                    Cmd("ipconfig /release"),
                    Cmd("ipconfig /renew"),
                }
            });

            // ---------- Aufräumen ----------
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E74D",
                Title = "Temp-Dateien löschen",
                Desc = "Leert den Benutzer- und Windows-Temp-Ordner.",
                Steps = { Ps("$t=@($env:TEMP,(Join-Path $env:WINDIR 'Temp')); $b=(Get-ChildItem $t -Recurse -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; Get-ChildItem $t -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; $a=(Get-ChildItem $t -Recurse -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; 'Temp geleert - ca. ' + [math]::Round((([double]$b-[double]$a)/1MB),1) + ' MB freigegeben (genutzte Dateien bleiben).'") }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E896",
                Title = "Update-Cache leeren",
                Desc = "Löscht heruntergeladene Update-Dateien (SoftwareDistribution\\Download).",
                Steps = {
                    CmdBE("net stop wuauserv"),
                    Ps("$p=(Join-Path $env:WINDIR 'SoftwareDistribution\\Download'); $b=(Get-ChildItem $p -Recurse -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; Get-ChildItem $p -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; $a=(Get-ChildItem $p -Recurse -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; 'Update-Cache geleert - ca. ' + [math]::Round((([double]$b-[double]$a)/1MB),1) + ' MB freigegeben.'"),
                    CmdBE("net start wuauserv"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E74D",
                Title = "Papierkorb leeren",
                Desc = "Leert den Papierkorb aller Laufwerke.",
                Steps = { Ps("try { Clear-RecycleBin -Force -EA Stop; 'Papierkorb geleert.' } catch { if ($_.Exception.Message -match 'leer|empty') { 'Papierkorb war bereits leer.' } else { 'Papierkorb: ' + $_.Exception.Message } }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E713",
                Title = "Datenträgerbereinigung",
                Desc = "Öffnet das Windows-Tool cleanmgr in eigenem Fenster.",
                Steps = { new Step { File = "cleanmgr.exe", Detached = true } }
            });

            // ---------- Diagnose ----------
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E9D9",
                Title = "System-Übersicht",
                Desc = "Modell, Windows-Version, RAM und Laufzeit auf einen Blick.",
                Steps = { Ps("$os=Get-CimInstance Win32_OperatingSystem; $cs=Get-CimInstance Win32_ComputerSystem; Write-Output ('Computer : ' + $cs.Manufacturer + ' ' + $cs.Model); Write-Output ('Windows  : ' + $os.Caption + ' (Build ' + $os.BuildNumber + ')'); Write-Output ('RAM      : ' + [math]::Round($cs.TotalPhysicalMemory/1GB,1) + ' GB'); $u=(Get-Date)-$os.LastBootUpTime; Write-Output ('Laufzeit : ' + [int]$u.TotalHours + ' h ' + $u.Minutes + ' min')") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E9D9",
                Title = "Festplatten-Gesundheit",
                Desc = "SMART-Status und Typ aller physischen Datenträger.",
                Steps = { Ps("Get-PhysicalDisk | Sort-Object DeviceId | Format-Table -AutoSize DeviceId, FriendlyName, MediaType, HealthStatus, @{n='GB';e={[int]($_.Size/1GB)}} | Out-String") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E713",
                Title = "Akkubericht erstellen",
                Desc = "Erzeugt powercfg-Akkubericht auf dem Desktop und öffnet ihn.",
                Steps = {
                    Cmd("powercfg /batteryreport /output \"%USERPROFILE%\\Desktop\\Akkubericht.html\""),
                    Cmd("if exist \"%USERPROFILE%\\Desktop\\Akkubericht.html\" start \"\" \"%USERPROFILE%\\Desktop\\Akkubericht.html\""),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E730",
                Title = "Defender-Schnellscan",
                Desc = "Startet einen schnellen Microsoft-Defender-Scan.",
                Steps = { Ps("if (Get-Command Start-MpScan -EA SilentlyContinue) { try { Start-MpScan -ScanType QuickScan -EA Stop; 'Defender-Schnellscan abgeschlossen.' } catch { 'Defender-Scan nicht moeglich: ' + $_.Exception.Message } } else { 'Microsoft Defender ist nicht verfuegbar (evtl. ist ein anderes Antivirus aktiv).' }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E7BA", Danger = true,
                Title = "RAM-Diagnose planen",
                Desc = "Öffnet die Windows-Speicherdiagnose (Neustart erforderlich).",
                Steps = { new Step { File = "mdsched.exe", Detached = true } }
            });

            // ---------- Nachtraege v6.4 (IDs 20-27) ----------
            // WICHTIG: Neue Aktionen IMMER ans Listenende anhaengen - die IDs sind Indizes
            // und werden 1:1 in ui/app.js (ACTIONS) gespiegelt; Einfuegen mittendrin
            // verschiebt alle folgenden IDs (u. a. Dashboard-Empfehlung -> id 11).
            l.Add(new MaintenanceAction {                     // id 20
                Category = "Reparieren", Glyph = "E749", IsRepair = true,
                Title = "Drucker reparieren",
                Desc = "Leert hängende Druckaufträge und startet die Druckerwarteschlange neu.",
                Steps = {
                    CmdBE("net stop spooler"),
                    Ps("$p=Join-Path $env:WINDIR 'System32\\spool\\PRINTERS'; $n=(Get-ChildItem $p -EA SilentlyContinue | Measure-Object).Count; Remove-Item (Join-Path $p '*') -Force -EA SilentlyContinue; 'Druckerwarteschlange geleert (' + $n + ' Datei(en) entfernt).'"),
                    CmdBE("net start spooler"),
                }
            });
            l.Add(new MaintenanceAction {                     // id 21
                Category = "Reparieren", Glyph = "E823",
                Title = "Uhrzeit synchronisieren",
                Desc = "Gleicht die Systemzeit mit dem Zeitserver ab (behebt Zertifikats-/Anmeldefehler).",
                Steps = {
                    CmdBE("sc config w32time start= demand"),
                    CmdBE("net start w32time"),
                    Cmd("w32tm /resync /force"),
                    CmdBE("w32tm /query /status"),
                }
            });
            l.Add(new MaintenanceAction {                     // id 22
                Category = "Reparieren", Glyph = "E721", Danger = true,
                Title = "Windows-Suche reparieren",
                Desc = "Setzt den Suchindex zurück – er wird danach im Hintergrund neu aufgebaut.",
                Steps = {
                    CmdBE("net stop wsearch"),
                    Ps("$d=Join-Path $env:ProgramData 'Microsoft\\Search\\Data\\Applications\\Windows'; Remove-Item (Join-Path $d 'Windows.edb') -Force -EA SilentlyContinue; Remove-Item (Join-Path $d 'Windows.db') -Force -EA SilentlyContinue; 'Suchindex zurueckgesetzt - er wird im Hintergrund neu aufgebaut (Suche kann voruebergehend unvollstaendig sein).'"),
                    CmdBE("net start wsearch"),
                }
            });
            l.Add(new MaintenanceAction {                     // id 23
                Category = "Diagnose", Glyph = "E7BA",
                Title = "Absturz-Historie",
                Desc = "Zeigt unerwartete Neustarts und Bluescreens der letzten Zeit.",
                Steps = { Ps("try { Get-WinEvent -FilterHashtable @{LogName='System'; Id=41,1074,6008,1001} -MaxEvents 12 -EA Stop | ForEach-Object { $w=''; if($_.Id -eq 41){$w='Unerwartet ausgeschaltet (Strom/Absturz)'} elseif($_.Id -eq 6008){$w='Unerwartetes Herunterfahren'} elseif($_.Id -eq 1074){$w='Geplanter Neustart/Herunterfahren'} else {$w='Bluescreen (Bugcheck)'}; ('{0}  {1}' -f $_.TimeCreated.ToString('yyyy-MM-dd HH:mm'), $w) } } catch { 'Keine Eintraege gefunden - sieht gut aus!' }") }
            });
            l.Add(new MaintenanceAction {                     // id 24
                Category = "Diagnose", Glyph = "E774",
                Title = "Netzwerk-Übersicht",
                Desc = "IP-Adresse, Gateway und DNS aller aktiven Adapter.",
                Steps = { Ps("Get-NetIPConfiguration | Where-Object {$_.IPv4Address} | ForEach-Object { Write-Output ('Adapter : ' + $_.InterfaceAlias); Write-Output ('  IPv4    : ' + ($_.IPv4Address.IPAddress -join ', ')); if($_.IPv4DefaultGateway){Write-Output ('  Gateway : ' + $_.IPv4DefaultGateway.NextHop)}; if($_.DNSServer){Write-Output ('  DNS     : ' + (($_.DNSServer | ForEach-Object {$_.ServerAddresses}) -join ', '))}; Write-Output '' }") }
            });
            l.Add(new MaintenanceAction {                     // id 25
                Category = "Diagnose", Glyph = "E7C4",
                Title = "Startzeit-Analyse",
                Desc = "Wie lange die letzten Windows-Starts gedauert haben.",
                Steps = { Ps("try { Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational'; Id=100} -MaxEvents 5 -EA Stop | ForEach-Object { $x=[xml]$_.ToXml(); $ms=[int64](($x.Event.EventData.Data | Where-Object {$_.Name -eq 'BootTime'}).'#text'); ('{0}  Start dauerte {1:0.0} s' -f $_.TimeCreated.ToString('yyyy-MM-dd HH:mm'), ($ms/1000)) } } catch { 'Keine Startzeit-Daten vorhanden (Protokoll evtl. deaktiviert).' }") }
            });
            l.Add(new MaintenanceAction {                     // id 26
                Category = "Aufräumen", Glyph = "EB9F", Danger = true,
                Title = "Miniaturansichten-Cache leeren",
                Desc = "Behebt falsche/fehlende Vorschaubilder. Die Taskleiste startet dabei kurz neu.",
                Steps = {
                    CmdBE("taskkill /f /im explorer.exe"),
                    Ps("$p=Join-Path $env:LOCALAPPDATA 'Microsoft\\Windows\\Explorer'; $b=(Get-ChildItem (Join-Path $p 'thumbcache_*') -EA SilentlyContinue | Measure-Object Length -Sum).Sum; Remove-Item (Join-Path $p 'thumbcache_*') -Force -EA SilentlyContinue; 'Miniaturansichten-Cache geleert - ca. ' + [math]::Round([double]$b/1MB,1) + ' MB.'"),
                    CmdBE("start explorer.exe"),
                }
            });
            l.Add(new MaintenanceAction {                     // id 27
                Category = "Aufräumen", Glyph = "E719",
                Title = "Store-Cache leeren",
                Desc = "Setzt den Microsoft-Store-Cache zurück (öffnet kurz ein eigenes Fenster).",
                Steps = { new Step { File = "wsreset.exe", Detached = true } }
            });

            return l;
        }

        // ---------- Geplante Wartung (--auto) ----------
        // Katalog der Aufgaben, die still/unbeaufsichtigt sicher sind: reparierend + aufraeumend,
        // nichts Destruktives, kein interaktiver Schritt, nichts, was die Verbindung kappt.
        // Std=true markiert den Standard-Satz (gilt, solange der Nutzer nichts anderes waehlt).
        public static List<AutoItem> AutoCatalog()
        {
            var l = new List<AutoItem>();
            l.Add(new AutoItem { Key = "dism", Std = true,
                Title = "Windows reparieren (DISM)",
                Desc = "Repariert den Komponentenspeicher über Windows Update.",
                Steps = { Dism("/Online /Cleanup-Image /RestoreHealth") } });
            l.Add(new AutoItem { Key = "sfc", Std = true,
                Title = "Systemdateien prüfen (SFC)",
                Desc = "Prüft und repariert geschützte Systemdateien.",
                Steps = { Sfc("/scannow") } });
            l.Add(new AutoItem { Key = "temp", Std = true,
                Title = "Temp-Dateien löschen",
                Desc = "Leert Benutzer- und Windows-Temp-Ordner.",
                Steps = { Ps("$t=@($env:TEMP,(Join-Path $env:WINDIR 'Temp')); Get-ChildItem $t -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; 'Temp geleert.'") } });
            l.Add(new AutoItem { Key = "bin", Std = true,
                Title = "Papierkorb leeren",
                Desc = "Leert den Papierkorb aller Laufwerke.",
                Steps = { Ps("try { Clear-RecycleBin -Force -EA Stop } catch {}; 'Papierkorb geleert.'") } });
            l.Add(new AutoItem { Key = "winsxs",
                Title = "WinSxS aufräumen",
                Desc = "Entfernt veraltete Update-Komponenten (schafft Platz, dauert länger).",
                Steps = { Dism("/Online /Cleanup-Image /StartComponentCleanup") } });
            l.Add(new AutoItem { Key = "updcache",
                Title = "Update-Cache leeren",
                Desc = "Löscht heruntergeladene Update-Dateien.",
                Steps = {
                    CmdBE("net stop wuauserv"),
                    Ps("$p=(Join-Path $env:WINDIR 'SoftwareDistribution\\Download'); Get-ChildItem $p -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; 'Update-Cache geleert.'"),
                    CmdBE("net start wuauserv"),
                } });
            l.Add(new AutoItem { Key = "dns",
                Title = "DNS-Cache leeren",
                Desc = "Löscht den DNS-Auflösungscache.",
                Steps = { Cmd("ipconfig /flushdns") } });
            l.Add(new AutoItem { Key = "defender",
                Title = "Defender-Schnellscan",
                Desc = "Kurzer Virenscan der wichtigsten Bereiche.",
                Steps = { Ps("if (Get-Command Start-MpScan -EA SilentlyContinue) { try { Start-MpScan -ScanType QuickScan -EA Stop; 'Defender-Schnellscan abgeschlossen.' } catch { 'Defender-Scan nicht moeglich: ' + $_.Exception.Message } } else { 'Microsoft Defender ist nicht verfuegbar.' }") } });
            return l;
        }

        // Schritte fuer den --auto-Lauf. keys = vom Nutzer gewaehlte Aufgaben-Schluessel
        // (nur bekannte Schluessel zaehlen); null/leer/unbekannt => Standard-Satz (Std=true).
        public static List<Step> AutoSet(string[] keys)
        {
            var cat = AutoCatalog();
            var steps = new List<Step>();
            if (keys != null)
            {
                foreach (AutoItem it in cat)      // Katalogreihenfolge = sinnvolle Ausfuehrungsreihenfolge
                    if (Array.IndexOf(keys, it.Key) >= 0) steps.AddRange(it.Steps);
            }
            if (steps.Count == 0)                 // fail-safe: nichts (Gueltiges) gewaehlt -> Standard
            {
                foreach (AutoItem it in cat)
                    if (it.Std) steps.AddRange(it.Steps);
            }
            return steps;
        }
        public static List<Step> AutoSet() { return AutoSet(null); }
    }
}
