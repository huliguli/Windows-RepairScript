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

            return l;
        }

        // Stiller, gruendlicher Satz fuer die geplante Wartung (--auto):
        // reparierend + aufraeumend, nichts Destruktives, kein interaktiver Schritt.
        public static List<Step> AutoSet()
        {
            var l = new List<Step>();
            l.Add(Dism("/Online /Cleanup-Image /RestoreHealth"));
            l.Add(Sfc("/scannow"));
            l.Add(Ps("$t=@($env:TEMP,(Join-Path $env:WINDIR 'Temp')); Get-ChildItem $t -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; 'Temp geleert.'"));
            l.Add(Ps("try { Clear-RecycleBin -Force -EA Stop } catch {}; 'Papierkorb geleert.'"));
            return l;
        }
    }
}
