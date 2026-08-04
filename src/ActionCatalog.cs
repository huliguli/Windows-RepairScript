using System;
using System.Collections.Generic;
using System.Text;

namespace WartungsToolbox
{
    /// <summary>
    /// Der Aktionskatalog - ALLEINIGE Quelle fuer Titel, Beschreibungen und Erklaerungen.
    ///
    /// Frueher standen dieselben Texte ein zweites Mal in ui/app.js. 18 von 28 Beschreibungen
    /// waren dadurch auseinandergelaufen, und der Hinweis zu CHKDSK ("die Rueckfrage mit J
    /// bestaetigen") erreichte den Nutzer nie, weil nur die C#-Fassung ihn enthielt. Jetzt
    /// schickt der Host den Katalog beim Start an die Oberflaeche.
    ///
    /// Sprachregeln fuer alles, was hier steht:
    ///   * Title      = die AUFGABE in Alltagssprache, kein Werkzeugname.
    ///   * TechTitle  = der Fachname, klein im Detailbereich. Darf null sein.
    ///   * Desc       = ein Satz, was es bringt.
    ///   * Info       = die ausfuehrliche Erklaerung, ohne unerklaerte Fachbegriffe.
    ///   * Anrede durchgehend "Sie".
    /// </summary>
    static class Catalog
    {
        public static readonly string[] Categories = { "Reparieren", "Netzwerk", "Aufräumen", "Diagnose" };
        public static readonly string[] CategoryGlyphs = { "E90F", "E774", "E74D", "E9D9" };

        // Kurze Einordnung oben in jeder Werkzeug-Kategorie.
        public static readonly Dictionary<string, string> CategoryNotes = new Dictionary<string, string>
        {
            { "Reparieren", "Werkzeuge für ein Windows, das Probleme macht: Abstürze, Fehlermeldungen, hängende Updates. Ihre persönlichen Dateien werden dabei nicht angerührt. Im Zweifel ist die Rundum-Reparatur die richtige Wahl; sie darf ruhig ein paar Minuten dauern." },
            { "Netzwerk",   "Hilfe bei Internet-Problemen. Sinnvolle Reihenfolge: erst die gemerkten Internet-Adressen verwerfen, dann eine neue Netzwerk-Adresse holen. Erst wenn das nicht hilft, alle Interneteinstellungen zurücksetzen und danach den PC neu starten." },
            { "Aufräumen",  "Schafft Speicherplatz und entfernt Datenmüll. Ihre Dokumente, Bilder und Programme bleiben unberührt. Nur der Papierkorb wird, falls Sie ihn wählen, endgültig geleert." },
            { "Diagnose",   "Nur nachsehen, nichts verändern: Diese Aktionen prüfen den PC und berichten. Ein guter erster Schritt, wenn sich der PC merkwürdig verhält." },
        };

        public static string Glyph(string hex)
        {
            try { return char.ConvertFromUtf32(Convert.ToInt32(hex, 16)); }
            catch { return "?"; }
        }

        static Step Cmd(string c)  { return new Step { File = "cmd.exe", Args = "/c " + c }; }
        static Step CmdBE(string c){ return new Step { File = "cmd.exe", Args = "/c " + c, IgnoreExit = true }; }
        static Step Ps(string c)   { return new Step { File = "powershell.exe", Args = "-NoProfile -ExecutionPolicy Bypass -Command \"" + c + "\"" }; }
        static Step Dism(string a) { return new Step { File = "DISM.exe", Args = a, Progress = true }; }
        static Step Sfc(string a)  { return new Step { File = "sfc.exe", Args = a, Enc = Encoding.Unicode, Progress = true }; }

        static List<MaintenanceAction> _all;

        public static List<MaintenanceAction> All()
        {
            if (_all != null) return _all;
            List<MaintenanceAction> l = Build();
            for (int i = 0; i < l.Count; i++) l[i].Id = i;
            _all = l;
            return l;
        }

        static List<MaintenanceAction> Build()
        {
            var l = new List<MaintenanceAction>();

            // ---------- Reparieren ----------
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E90F", Icon = "wrench", IsRepair = true,
                Title = "Rundum-Reparatur",
                TechTitle = "DISM ScanHealth + RestoreHealth, danach SFC",
                Desc = "Prüft Windows in drei Schritten durch und ersetzt beschädigte Dateien automatisch.",
                Info = "Lässt Windows seine eigenen Dateien überprüfen und beschädigte automatisch ersetzen. Das ist die gute Erste Hilfe, wenn der PC spinnt, abstürzt oder sich merkwürdig verhält. Dauert ein paar Minuten. Ihre eigenen Dateien werden dabei nicht angerührt.",
                Steps = {
                    Dism("/Online /Cleanup-Image /ScanHealth"),
                    Dism("/Online /Cleanup-Image /RestoreHealth"),
                    Sfc("/scannow"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E895", Icon = "refresh", IsRepair = true,
                Title = "Windows über das Internet reparieren",
                TechTitle = "DISM RestoreHealth",
                Desc = "Lädt fehlende oder beschädigte Windows-Bausteine neu herunter.",
                Info = "Repariert die Grundbestandteile von Windows über das Internet. Hilft besonders, wenn sich Updates nicht installieren lassen oder Windows beschädigt ist. Dafür wird eine Internetverbindung gebraucht.",
                Steps = { Dism("/Online /Cleanup-Image /RestoreHealth") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E73E", Icon = "shieldCheck", IsRepair = true,
                Title = "Windows-Dateien prüfen und reparieren",
                TechTitle = "SFC scannow",
                Desc = "Sucht beschädigte Windows-Dateien und ersetzt sie durch heile Kopien.",
                Info = "Prüft die wichtigen Dateien von Windows und repariert beschädigte. Ein Klassiker bei Fehlermeldungen und Abstürzen. Läuft ein paar Minuten.",
                Steps = { Sfc("/scannow") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E721", Icon = "search",
                Title = "Nur nachsehen, nichts ändern",
                TechTitle = "SFC verifyonly",
                Desc = "Sucht beschädigte Windows-Dateien und meldet nur den Befund.",
                Info = "Schaut nur nach, ob Windows-Dateien beschädigt sind, und verändert dabei nichts. Gut, um erst einmal zu sehen, ob überhaupt etwas zu tun ist.",
                Steps = { Sfc("/verifyonly") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E74D", Icon = "trash", NeedsRestore = true,
                Title = "Alte Update-Reste löschen",
                TechTitle = "DISM StartComponentCleanup (WinSxS)",
                Desc = "Entfernt Update-Bausteine, die Windows nicht mehr braucht. Macht oft mehrere Gigabyte frei.",
                Info = "Windows hebt bei jedem Update die alten Bausteine auf, für den Fall, dass etwas rückgängig gemacht werden muss. Irgendwann braucht die niemand mehr. Diese Aktion räumt sie auf dem offiziellen, sicheren Weg weg. Das schafft oft mehrere Gigabyte, ohne dass etwas kaputtgeht. Bereits installierte Updates lassen sich danach weiterhin deinstallieren.",
                Steps = { Dism("/Online /Cleanup-Image /StartComponentCleanup") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E9D9", Icon = "activity",
                Title = "Nachsehen, ob Aufräumen lohnt",
                TechTitle = "DISM AnalyzeComponentStore",
                Desc = "Zeigt, wie viel Platz die alten Update-Reste belegen. Verändert nichts.",
                Info = "Windows rechnet selbst aus, wie viel Platz die alten Update-Reste belegen, und empfiehlt, ob sich das Aufräumen lohnt. Diese Aktion sieht nur nach und ändert nichts.",
                Steps = { Dism("/Online /Cleanup-Image /AnalyzeComponentStore") }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E777", Icon = "rotate", IsRepair = true,
                Title = "Windows-Update von vorn starten",
                TechTitle = "SoftwareDistribution + catroot2 zurücksetzen",
                Desc = "Setzt die Update-Funktion zurück, wenn Updates hängen bleiben oder abbrechen.",
                Info = "Setzt die Update-Funktion von Windows in den Ausgangszustand zurück. Hilft, wenn Updates hängen bleiben, immer wieder mit einem Fehler abbrechen oder gar nicht erst starten. Nach dieser Aktion sollten Sie den PC neu starten.",
                // Best-effort bei den Diensten, aber das Umbenennen MUSS gelingen - sonst
                // meldete die Aktion frueher Erfolg, obwohl nichts zurueckgesetzt wurde.
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
                    // Ergebnis wirklich pruefen statt Erfolg zu behaupten.
                    Ps("$sd = Test-Path (Join-Path $env:WINDIR 'SoftwareDistribution.old'); "
                     + "$cr = Test-Path (Join-Path $env:WINDIR 'System32\\catroot2.old'); "
                     + "if ($sd -and $cr) { 'Die Update-Funktion wurde zurueckgesetzt. Bitte starten Sie den PC neu.' } "
                     + "elseif ($sd -or $cr) { 'Nur ein Teil konnte zurueckgesetzt werden. Bitte starten Sie den PC neu und fuehren Sie die Aktion noch einmal aus.'; exit 1 } "
                     + "else { 'Das Zurücksetzen hat nicht geklappt. Vermutlich hält ein anderes Programm die Dateien fest. Bitte starten Sie den PC neu und versuchen Sie es erneut.'; exit 1 }"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E7BA", Icon = "alert", Danger = true,
                Title = "Festplatte beim nächsten Neustart prüfen",
                TechTitle = "CHKDSK",
                Desc = "Plant eine gründliche Festplattenprüfung. Es öffnet sich ein Fenster mit einer Rückfrage.",
                Info = "Prüft die Festplatte auf Fehler. Das geht nur, während Windows nicht läuft, also beim nächsten Neustart. Sinnvoll bei merkwürdigen Datei- oder Festplattenproblemen.\n\nWichtig: Es öffnet sich ein schwarzes Fenster mit einer Rückfrage. Tippen Sie dort J (oder Y) und drücken Sie die Eingabetaste. Der nächste Start dauert dann deutlich länger als sonst, das ist normal.",
                Steps = { new Step { File = "cmd.exe", Args = "/k chkdsk %SystemDrive% /f /r", Detached = true } }
            });

            // ---------- Netzwerk ----------
            l.Add(new MaintenanceAction {
                Category = "Netzwerk", Glyph = "E774", Icon = "globe", Danger = true,
                Title = "Alle Interneteinstellungen zurücksetzen",
                TechTitle = "DNS, Winsock und IP-Stack zurücksetzen",
                Desc = "Setzt sämtliche Netzwerkeinstellungen auf den Werkszustand. Danach ist ein Neustart nötig.",
                Info = "Der Notnagel, wenn gar nichts mehr ins Internet kommt. Setzt alle Netzwerkeinstellungen des PCs auf den Auslieferungszustand zurück. Ihr WLAN-Passwort bleibt gespeichert. Danach müssen Sie den PC neu starten.",
                Steps = {
                    Cmd("ipconfig /flushdns"),
                    Cmd("netsh winsock reset"),
                    Cmd("netsh int ip reset"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Netzwerk", Glyph = "E774", Icon = "globe",
                Title = "Gemerkte Internet-Adressen verwerfen",
                TechTitle = "ipconfig /flushdns",
                Desc = "Hilft, wenn einzelne Webseiten nicht laden, obwohl das Internet sonst geht.",
                Info = "Ihr PC merkt sich, hinter welcher Nummer eine Webadresse steckt. Ist ein solcher Merkzettel veraltet, lädt eine einzelne Seite nicht mehr, während alles andere funktioniert. Diese Aktion wirft die Merkzettel weg, sie werden beim nächsten Aufruf neu geholt.",
                Steps = { Cmd("ipconfig /flushdns") }
            });
            l.Add(new MaintenanceAction {
                Category = "Netzwerk", Glyph = "E72C", Icon = "refresh",
                Title = "Neue Netzwerk-Adresse vom Router holen",
                TechTitle = "ipconfig /release + /renew",
                Desc = "Hilft bei Verbindungsproblemen im Heimnetz.",
                Info = "Ihr PC bekommt vom Router eine Adresse zugeteilt, damit die Geräte im Haus sich finden. Klemmt es dabei, holt diese Aktion eine frische Adresse. Die Verbindung ist dabei einen Moment lang weg.",
                Steps = {
                    Cmd("ipconfig /release"),
                    Cmd("ipconfig /renew"),
                }
            });

            // ---------- Aufräumen ----------
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E74D", Icon = "trash",
                Title = "Temporäre Dateien löschen",
                Desc = "Entfernt Zwischendateien, die Programme liegen gelassen haben.",
                Info = "Programme legen beim Arbeiten Zwischendateien an und räumen sie oft nicht wieder weg. Diese Aktion entfernt sie. Das schafft Platz und schadet nichts. Dateien, die gerade in Benutzung sind, bleiben liegen.",
                Steps = { Ps("$t=@($env:TEMP,(Join-Path $env:WINDIR 'Temp')); $b=(Get-ChildItem $t -Recurse -File -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; Get-ChildItem $t -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; $a=(Get-ChildItem $t -Recurse -File -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; 'Aufgeraeumt: ca. ' + [math]::Round((([double]$b-[double]$a)/1MB),1) + ' MB frei gemacht (Dateien in Benutzung bleiben liegen).'") }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E896", Icon = "download",
                Title = "Heruntergeladene Update-Dateien löschen",
                Desc = "Hilft, wenn Updates klemmen, und schafft Platz.",
                Info = "Windows lädt Updates herunter und behält die Dateien danach noch eine Weile. Sind sie beschädigt, klemmt das nächste Update. Diese Aktion wirft sie weg. Windows lädt bei Bedarf neu.",
                Steps = {
                    CmdBE("net stop wuauserv"),
                    Ps("$p=(Join-Path $env:WINDIR 'SoftwareDistribution\\Download'); $b=(Get-ChildItem $p -Recurse -File -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; Get-ChildItem $p -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; $a=(Get-ChildItem $p -Recurse -File -Force -EA SilentlyContinue | Measure-Object Length -Sum).Sum; 'Aufgeraeumt: ca. ' + [math]::Round((([double]$b-[double]$a)/1MB),1) + ' MB frei gemacht.'"),
                    CmdBE("net start wuauserv"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E74D", Icon = "trash", Danger = true,
                Title = "Papierkorb endgültig leeren",
                Desc = "Löscht den Inhalt des Papierkorbs unwiderruflich.",
                Info = "Leert den Papierkorb aller Laufwerke. Achtung: Was darin liegt, ist danach endgültig weg und lässt sich nicht mehr zurückholen. Schauen Sie vorher kurz nach, ob noch etwas Wichtiges darin ist.",
                Steps = { Ps("try { Clear-RecycleBin -Force -EA Stop; 'Der Papierkorb wurde geleert.' } catch { if ($_.Exception.Message -match 'leer|empty') { 'Der Papierkorb war bereits leer.' } else { 'Papierkorb: ' + $_.Exception.Message } }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E713", Icon = "server",
                Title = "Windows-Aufräumfenster öffnen",
                TechTitle = "cleanmgr",
                Desc = "Öffnet das Aufräum-Werkzeug von Windows. Dort wählen Sie selbst aus, was gelöscht wird.",
                Info = "Öffnet das Aufräum-Fenster von Windows in einem eigenen Fenster. Dort setzen Sie selbst Häkchen bei dem, was gelöscht werden soll, und sehen jeweils, wie viel Platz das bringt.",
                Steps = { new Step { File = "cleanmgr.exe", Detached = true } }
            });

            // ---------- Diagnose ----------
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E9D9", Icon = "cpu",
                Title = "Überblick über diesen PC",
                Desc = "Modell, Windows-Fassung, Arbeitsspeicher und Laufzeit auf einen Blick.",
                Info = "Zeigt die Eckdaten Ihres PCs: welches Gerät es ist, welche Windows-Fassung darauf läuft, wie viel Arbeitsspeicher eingebaut ist und wie lange der PC schon durchgehend an ist. Praktisch, wenn Sie jemandem am Telefon beschreiben sollen, was Sie haben.",
                Steps = { Ps("$os=Get-CimInstance Win32_OperatingSystem; $cs=Get-CimInstance Win32_ComputerSystem; Write-Output ('Geraet          : ' + $cs.Manufacturer + ' ' + $cs.Model); Write-Output ('Windows         : ' + $os.Caption + ' (Build ' + $os.BuildNumber + ')'); Write-Output ('Arbeitsspeicher : ' + [math]::Round($cs.TotalPhysicalMemory/1GB,1) + ' GB'); $u=(Get-Date)-$os.LastBootUpTime; Write-Output ('Eingeschaltet   : seit ' + [int]$u.TotalHours + ' Std ' + $u.Minutes + ' Min')") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E9D9", Icon = "hdd",
                Title = "Festplatten auf Verschleiß prüfen",
                TechTitle = "SMART-Selbstdiagnose",
                Desc = "Zeigt, ob sich Ihre Festplatten und SSDs selbst noch für gesund halten.",
                Info = "Festplatten und SSDs überwachen sich selbst und merken, wenn sie schwächer werden. Diese Aktion fragt diesen Selbstbefund ab. Steht dort etwas anderes als in Ordnung, sollten Sie zeitnah Ihre wichtigen Dateien sichern.",
                Steps = { Ps("Get-PhysicalDisk | Sort-Object DeviceId | ForEach-Object { $art = if ($_.MediaType -eq 'SSD') { 'SSD' } elseif ($_.MediaType -eq 'HDD') { 'Festplatte' } else { 'unbekannt' }; $zust = if ($_.HealthStatus -eq 'Healthy') { 'in Ordnung' } else { $_.HealthStatus }; Write-Output ('Laufwerk ' + $_.DeviceId + ' : ' + $_.FriendlyName); Write-Output ('    Art     : ' + $art + ', ' + [int]($_.Size/1GB) + ' GB'); Write-Output ('    Zustand : ' + $zust); Write-Output '' }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E713", Icon = "battery",
                Title = "Akkubericht erstellen",
                Desc = "Erzeugt eine Übersicht zum Akku und legt sie auf den Schreibtisch.",
                Info = "Erstellt einen Bericht über den Akku Ihres Laptops und öffnet ihn. Darin steht unter anderem, wie viel Kapazität der Akku im Vergleich zum Neuzustand noch hat. Bei einem Desktop-PC ohne Akku bleibt der Bericht leer.",
                Steps = {
                    Cmd("powercfg /batteryreport /output \"%USERPROFILE%\\Desktop\\Akkubericht.html\""),
                    Cmd("if exist \"%USERPROFILE%\\Desktop\\Akkubericht.html\" start \"\" \"%USERPROFILE%\\Desktop\\Akkubericht.html\""),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E730", Icon = "shield",
                Title = "Kurzen Virenscan starten",
                TechTitle = "Microsoft Defender Schnellscan",
                Desc = "Prüft die Stellen, an denen sich Schädlinge typischerweise einnisten.",
                Info = "Lässt den in Windows eingebauten Virenschutz die wichtigsten Stellen auf Schädlinge prüfen. Das dauert meist ein paar Minuten. Für eine gründliche Prüfung des ganzen PCs nutzen Sie den vollständigen Scan in der Windows-Sicherheit.",
                Steps = { Ps("if (Get-Command Start-MpScan -EA SilentlyContinue) { try { Start-MpScan -ScanType QuickScan -EA Stop; 'Der Virenscan ist abgeschlossen. Es wurde nichts gemeldet.' } catch { 'Der Virenscan war nicht durchfuehrbar: ' + $_.Exception.Message } } else { 'Auf diesem PC ist der Windows-Virenschutz nicht aktiv. Vermutlich laeuft ein anderes Schutzprogramm.' }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E7BA", Icon = "cpu", Danger = true,
                Title = "Arbeitsspeicher beim Neustart prüfen",
                TechTitle = "Windows-Speicherdiagnose",
                Desc = "Öffnet die Speicherprüfung. Sie läuft beim nächsten Neustart.",
                Info = "Prüft den Arbeitsspeicher auf Fehler. Das geht nur, während Windows nicht läuft, also beim nächsten Neustart. Sinnvoll bei häufigen Abstürzen oder blauen Bildschirmen. Die Prüfung dauert einige Minuten, in denen der PC nicht benutzbar ist.",
                Steps = { new Step { File = "mdsched.exe", Detached = true } }
            });

            // ---------- Nachtraege (Reihenfolge = ID, niemals mittendrin einfuegen) ----------
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E749", Icon = "printer", IsRepair = true,
                Title = "Drucker wieder zum Laufen bringen",
                Desc = "Wirft hängende Druckaufträge weg und startet das Drucksystem neu.",
                Info = "Wenn der Drucker nicht mehr druckt, hängt oft ein alter Druckauftrag fest und blockiert alle weiteren. Diese Aktion wirft alle wartenden Aufträge weg und startet das Drucksystem neu. Danach klappt das Drucken meist wieder. Aufträge, die Sie noch brauchen, müssen Sie erneut abschicken.",
                Steps = {
                    CmdBE("net stop spooler"),
                    Ps("$p=Join-Path $env:WINDIR 'System32\\spool\\PRINTERS'; $n=(Get-ChildItem $p -EA SilentlyContinue | Measure-Object).Count; Remove-Item (Join-Path $p '*') -Force -EA SilentlyContinue; 'Die Warteschlange wurde geleert (' + $n + ' Auftrag/Auftraege entfernt).'"),
                    CmdBE("net start spooler"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E823", Icon = "clock",
                Title = "Uhrzeit richtig stellen",
                Desc = "Gleicht die Uhr des PCs mit der offiziellen Zeit im Internet ab.",
                Info = "Stellt die Uhr des PCs über das Internet richtig. Eine falsche Uhrzeit verursacht überraschend viele Probleme: Webseiten melden Sicherheitsfehler, Anmeldungen schlagen fehl, Termine verrutschen. Ein Abgleich dauert nur einen Moment.",
                Steps = {
                    CmdBE("sc config w32time start= demand"),
                    CmdBE("net start w32time"),
                    Cmd("w32tm /resync /force"),
                    CmdBE("w32tm /query /status"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Reparieren", Glyph = "E721", Icon = "search", Danger = true,
                Title = "Windows-Suche neu aufbauen",
                Desc = "Hilft, wenn die Suche im Startmenü nichts oder Falsches findet.",
                Info = "Windows führt ein Verzeichnis Ihrer Dateien, damit die Suche schnell ist. Ist dieses Verzeichnis beschädigt, findet die Suche nichts oder das Falsche. Diese Aktion baut es neu auf. Der Aufbau läuft im Hintergrund und kann je nach Datenmenge einige Stunden dauern. In dieser Zeit ist die Suche unvollständig.",
                Steps = {
                    CmdBE("net stop wsearch"),
                    Ps("$d=Join-Path $env:ProgramData 'Microsoft\\Search\\Data\\Applications\\Windows'; Remove-Item (Join-Path $d 'Windows.edb') -Force -EA SilentlyContinue; Remove-Item (Join-Path $d 'Windows.db') -Force -EA SilentlyContinue; 'Das Suchverzeichnis wurde geleert. Es wird jetzt im Hintergrund neu aufgebaut.'"),
                    CmdBE("net start wsearch"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E7BA", Icon = "alert",
                Title = "Abstürze der letzten Zeit anzeigen",
                Desc = "Zeigt, wann der PC unerwartet ausging oder einen blauen Bildschirm hatte.",
                Info = "Windows notiert, wenn der PC unerwartet ausgeht oder abstürzt. Diese Aktion zeigt diese Notizen der letzten Zeit in verständlicher Form. Häufen sich die Einträge, lohnt ein Blick auf Arbeitsspeicher und Festplatte.",
                Steps = { Ps("try { $e = Get-WinEvent -FilterHashtable @{LogName='System'; Id=41,1074,6008,1001} -MaxEvents 12 -EA Stop; if (-not $e) { 'Es sind keine Abstuerze verzeichnet. Das sieht gut aus.' } else { $e | ForEach-Object { $w=''; if($_.Id -eq 41){$w='Unerwartet ausgegangen (Strom weg oder Absturz)'} elseif($_.Id -eq 6008){$w='Unerwartet heruntergefahren'} elseif($_.Id -eq 1074){$w='Normaler Neustart, von Hand oder durch ein Update'} else {$w='Blauer Bildschirm'}; ('{0}  {1}' -f $_.TimeCreated.ToString('dd.MM.yyyy HH:mm'), $w) } } } catch { 'Es sind keine Abstuerze verzeichnet. Das sieht gut aus.' }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E774", Icon = "globe",
                Title = "Netzwerk-Daten dieses PCs",
                Desc = "Adresse im Netz, Weg ins Internet und Namensauflösung je Anschluss.",
                Info = "Zeigt die wichtigsten Netzwerk-Daten Ihres PCs: seine Adresse im Heimnetz, über welches Gerät er ins Internet geht und wer für ihn Webadressen auflöst. Praktisch für die Fehlersuche oder wenn der Support am Telefon danach fragt.",
                Steps = { Ps("Get-NetIPConfiguration | Where-Object {$_.IPv4Address} | ForEach-Object { Write-Output ('Anschluss          : ' + $_.InterfaceAlias); Write-Output ('  Adresse im Netz  : ' + ($_.IPv4Address.IPAddress -join ', ')); if($_.IPv4DefaultGateway){Write-Output ('  Weg ins Internet : ' + $_.IPv4DefaultGateway.NextHop)}; if($_.DNSServer){Write-Output ('  Namensaufloesung : ' + (($_.DNSServer | ForEach-Object {$_.ServerAddresses}) -join ', '))}; Write-Output '' }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Diagnose", Glyph = "E7C4", Icon = "clock",
                Title = "Wie lange der PC zum Starten braucht",
                Desc = "Zeigt die Dauer der letzten Windows-Starts.",
                Info = "Zeigt, wie lange die letzten Starts von Windows gedauert haben. Wird der Start immer langsamer, lohnt ein Blick in die Liste der Programme, die beim Einschalten automatisch mitstarten.",
                // Windows schreibt das Start-Ereignis auf vielen Systemen nicht mehr. Dann
                // greift der Rueckfallweg ueber die Ereignisse "Windows faehrt hoch/runter".
                Steps = { Ps("$ok=$false; try { $e = Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Diagnostics-Performance/Operational'; Id=100} -MaxEvents 5 -EA Stop; foreach ($x in $e) { $d=[xml]$x.ToXml(); $ms=[int64](($d.Event.EventData.Data | Where-Object {$_.Name -eq 'BootTime'}).'#text'); Write-Output ('{0}  Der Start dauerte {1:0} Sekunden' -f $x.TimeCreated.ToString('dd.MM.yyyy HH:mm'), ($ms/1000)); $ok=$true } } catch {}; if (-not $ok) { try { $b = Get-WinEvent -FilterHashtable @{LogName='System'; Id=6005} -MaxEvents 5 -EA Stop; Write-Output 'Windows misst die Startdauer auf diesem PC nicht mit. Ersatzweise die letzten Startzeitpunkte:'; Write-Output ''; foreach ($x in $b) { Write-Output ('  ' + $x.TimeCreated.ToString('dd.MM.yyyy HH:mm') + '  Windows wurde hochgefahren') } } catch { 'Zur Startdauer liegen auf diesem PC keine Daten vor.' } }") }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "EB9F", Icon = "image", Danger = true,
                Title = "Vorschaubilder neu erzeugen lassen",
                Desc = "Behebt falsche oder fehlende Vorschaubilder in Ordnern. Die Taskleiste startet kurz neu.",
                Info = "Windows legt für Fotos und Dateien kleine Vorschaubilder an, damit Ordner schnell aufgehen. Ist dieser Vorrat beschädigt, zeigen Ordner falsche oder gar keine Bilder. Diese Aktion wirft ihn weg, Windows legt ihn neu an.\n\nWichtig: Der Bildschirm wird dabei für einen Moment leer und die Taskleiste verschwindet kurz. Das ist normal, es kommt von allein zurück. Bitte speichern Sie vorher Ihre Arbeit.",
                Steps = {
                    CmdBE("taskkill /f /im explorer.exe"),
                    Ps("$p=Join-Path $env:LOCALAPPDATA 'Microsoft\\Windows\\Explorer'; $b=(Get-ChildItem (Join-Path $p 'thumbcache_*') -EA SilentlyContinue | Measure-Object Length -Sum).Sum; Remove-Item (Join-Path $p 'thumbcache_*') -Force -EA SilentlyContinue; 'Die Vorschaubilder wurden geloescht - ca. ' + [math]::Round([double]$b/1MB,1) + ' MB frei gemacht. Windows legt sie bei Bedarf neu an.'"),
                }
            });
            l.Add(new MaintenanceAction {
                Category = "Aufräumen", Glyph = "E719", Icon = "package",
                Title = "Microsoft Store zurücksetzen",
                TechTitle = "wsreset",
                Desc = "Hilft, wenn der Store nicht öffnet, hängt oder Apps sich nicht installieren lassen.",
                Info = "Leert den Zwischenspeicher des Microsoft Store. Hilft, wenn der Store nicht aufgeht, hängen bleibt oder Apps sich nicht installieren lassen. Es öffnet sich kurz ein schwarzes Fenster, danach geht der Store von allein auf. Ihre gekauften Apps bleiben erhalten.",
                Steps = { new Step { File = "wsreset.exe", Detached = true } }
            });

            return l;
        }

        // ---------- Geplante Wartung (--auto) ----------
        public static List<AutoItem> AutoCatalog()
        {
            var l = new List<AutoItem>();
            l.Add(new AutoItem { Key = "dism", Std = true,
                Title = "Windows reparieren",
                Desc = "Ersetzt beschädigte Windows-Bausteine über das Internet.",
                Steps = { Dism("/Online /Cleanup-Image /RestoreHealth") } });
            l.Add(new AutoItem { Key = "sfc", Std = true,
                Title = "Windows-Dateien prüfen",
                Desc = "Sucht beschädigte Windows-Dateien und ersetzt sie.",
                Steps = { Sfc("/scannow") } });
            l.Add(new AutoItem { Key = "temp", Std = true,
                Title = "Temporäre Dateien löschen",
                Desc = "Entfernt Zwischendateien, die Programme liegen gelassen haben.",
                Steps = { Ps("$t=@($env:TEMP,(Join-Path $env:WINDIR 'Temp')); Get-ChildItem $t -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; 'Aufgeraeumt.'") } });
            l.Add(new AutoItem { Key = "bin", Std = true,
                Title = "Papierkorb leeren",
                Desc = "Leert den Papierkorb aller Laufwerke.",
                Steps = { Ps("try { Clear-RecycleBin -Force -EA Stop } catch {}; 'Der Papierkorb wurde geleert.'") } });
            l.Add(new AutoItem { Key = "winsxs",
                Title = "Alte Update-Reste löschen",
                Desc = "Schafft Platz, dauert dafür länger.",
                Steps = { Dism("/Online /Cleanup-Image /StartComponentCleanup") } });
            l.Add(new AutoItem { Key = "updcache",
                Title = "Heruntergeladene Update-Dateien löschen",
                Desc = "Hilft, wenn Updates klemmen.",
                Steps = {
                    CmdBE("net stop wuauserv"),
                    Ps("$p=(Join-Path $env:WINDIR 'SoftwareDistribution\\Download'); Get-ChildItem $p -Force -EA SilentlyContinue | Remove-Item -Recurse -Force -EA SilentlyContinue; 'Aufgeraeumt.'"),
                    CmdBE("net start wuauserv"),
                } });
            l.Add(new AutoItem { Key = "dns",
                Title = "Gemerkte Internet-Adressen verwerfen",
                Desc = "Hilft, wenn einzelne Webseiten nicht laden.",
                Steps = { Cmd("ipconfig /flushdns") } });
            l.Add(new AutoItem { Key = "defender",
                Title = "Kurzen Virenscan starten",
                Desc = "Prüft die wichtigsten Stellen auf Schädlinge.",
                Steps = { Ps("if (Get-Command Start-MpScan -EA SilentlyContinue) { try { Start-MpScan -ScanType QuickScan -EA Stop; 'Der Virenscan ist abgeschlossen.' } catch { 'Der Virenscan war nicht durchfuehrbar: ' + $_.Exception.Message } } else { 'Auf diesem PC ist der Windows-Virenschutz nicht aktiv.' }") } });
            return l;
        }

        public static List<Step> AutoSet(string[] keys)
        {
            var cat = AutoCatalog();
            var steps = new List<Step>();
            if (keys != null)
            {
                foreach (AutoItem it in cat)
                    if (Array.IndexOf(keys, it.Key) >= 0) steps.AddRange(it.Steps);
            }
            if (steps.Count == 0)
            {
                foreach (AutoItem it in cat)
                    if (it.Std) steps.AddRange(it.Steps);
            }
            return steps;
        }
        public static List<Step> AutoSet() { return AutoSet(null); }
    }
}
