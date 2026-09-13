using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Fuellt s.Windows, s.Sprache.Mui und s.Hardware - im eigenen Prozess ueber WMI
    /// (root\cimv2), die Registry und einen P/Invoke (GetFirmwareType). Kein Skript-Interpreter, kein Prozessstart.
    ///
    /// Jede Teilabfrage laeuft in Sammler.Versuch(...): scheitert sie, steht der Grund in der
    /// fehlerliste und die uebrigen laufen weiter. Die Reihenfolge ist nicht beliebig: das
    /// Gehaeuse (ChassisTypes) muss vor dem Akku gelesen sein, weil die Deutung "kein Akku"
    /// nur auf einem Desktop-Gehaeuse erlaubt ist (Konzept 9: auf diesem PC wirft die
    /// Akku-Klasse "Ungueltige Klasse", und das ist hier "kein Akku", auf einem Laptop waere
    /// es "unbekannt").
    ///
    /// Fallen aus der Recherche (R1) und der Widerlegungsrunde, die hier abgefangen werden:
    ///  - ProductName sagt auf Windows 11 "Windows 10 Pro": nie lesen, Build aus CurrentBuild.
    ///  - OSArchitecture ist lokalisiert ("64-Bit"): Environment.Is64BitOperatingSystem.
    ///  - SMBIOS-Platzhalter ("System Product Name", "To be filled by O.E.M.") sind kein
    ///    Modell: Wmi.OhnePlatzhalter, Rueckfall auf Win32_BaseBoard.Product.
    ///  - AdapterRAM ist uint32 und deckelt bei 4 GB (4293918720 fuer 24 GB): VRAM aus dem
    ///    Registry-QWORD HardwareInformation.qwMemorySize, AdapterRAM nur als Rueckfall.
    ///  - MemoryType ist bei DDR5 0: Typ aus SMBIOSMemoryType (26 DDR4, 34 DDR5).
    ///  - Speed steht in MT/s, nicht in Nanosekunden wie die Doku sagt.
    ///  - BIOS.ReleaseDate ist UTC-Mitternacht: ueber den DMTF-Konverter und in UTC halten,
    ///    sonst rutscht das Datum westlich von UTC auf den Vortag.
    /// </summary>
    public static class Grundlagen
    {
        const string Cimv2 = @"root\cimv2";

        public static void Erfassen(Systembild s)
        {
            WindowsErfassen(s);
            HardwareErfassen(s);
        }

        // ------------------------------------------------------------------ Windows + Sprache

        static void WindowsErfassen(Systembild s)
        {
            var w = s.Windows;

            // Build, UBR, DisplayVersion und EditionID gibt es nur in der Registry vollstaendig:
            // Win32_OperatingSystem.Version traegt keinen UBR (Widerlegungsrunde).
            Sammler.Versuch(s, "registry.currentversion", () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k == null) throw new InvalidOperationException("Schlüssel CurrentVersion fehlt");
                    int build;
                    if (int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build)) w.Build = build;
                    object ubr = k.GetValue("UBR");
                    if (ubr != null) w.Ubr = unchecked((int)Convert.ToInt64(ubr));
                    w.DisplayVersion = Convert.ToString(k.GetValue("DisplayVersion"));
                    if (string.IsNullOrEmpty(w.DisplayVersion)) w.DisplayVersion = Convert.ToString(k.GetValue("ReleaseId"));
                    w.Edition = Convert.ToString(k.GetValue("EditionID"));
                    if (w.Build == 0) throw new InvalidOperationException("CurrentBuild nicht lesbar");
                }
            });

            // OSArchitecture ist lokalisiert ("64-Bit"); die Laufzeit weiss es ohne Text.
            w.X64 = Environment.Is64BitOperatingSystem;

            Sammler.Versuch(s, "wmi.os", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT Caption,ProductType,LastBootUpTime,MUILanguages FROM Win32_OperatingSystem");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_OperatingSystem lieferte keine Instanz");
                var mo = liste[0];
                w.Caption = Wmi.Str(mo, "Caption");           // nur Anzeige
                w.ProduktTyp = Wmi.Ganz(mo, "ProductType") ?? 0;
                w.LetzterStartUtc = Wmi.ZeitUtc(mo, "LastBootUpTime");
                // Sprache.Lcid setzt der Sammler aus CultureInfo.InstalledUICulture; hier nur die
                // installierten Sprachpakete als Tags (de-DE, en-US), keine Anzeigenamen.
                s.Sprache.Mui = Wmi.Strings(mo, "MUILanguages");
            });
        }

        // ------------------------------------------------------------------ Hardware

        static void HardwareErfassen(Systembild s)
        {
            var h = s.Hardware;

            Sammler.Versuch(s, "wmi.computersystem", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT Manufacturer,Model,PCSystemType,TotalPhysicalMemory FROM Win32_ComputerSystem");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_ComputerSystem lieferte keine Instanz");
                var mo = liste[0];
                h.Hersteller = Wmi.OhnePlatzhalter(Wmi.Str(mo, "Manufacturer"));
                h.Modell = Wmi.OhnePlatzhalter(Wmi.Str(mo, "Model"));
                h.SystemTyp = Wmi.Ganz(mo, "PCSystemType") ?? 0;
                // Bezugsgroesse der Zaehler (gemessen identisch mit TotalVisibleMemorySize):
                // Leistung.cs teilt den freien Speicher dadurch, wenn der eigene Wert fehlt.
                // Die Modulsumme (verbaut, mit Grafikreserve) geht nach RamInstalliertKB.
                long? gesamt = Wmi.Lang(mo, "TotalPhysicalMemory");
                if (gesamt.HasValue) h.RamGesamtKB = gesamt.Value / 1024;
            });

            Sammler.Versuch(s, "wmi.baseboard", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT Product FROM Win32_BaseBoard");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_BaseBoard lieferte keine Instanz");
                h.Board = Wmi.OhnePlatzhalter(Wmi.Str(liste[0], "Product"));
                // Selbstbau-PC: Model ist "System Product Name", das Board sagt, was es ist.
                if (h.Modell == null) h.Modell = h.Board;
            });

            Sammler.Versuch(s, "wmi.bios", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_BIOS lieferte keine Instanz");
                h.BiosVersion = Wmi.OhnePlatzhalter(Wmi.Str(liste[0], "SMBIOSBIOSVersion"));
                h.BiosDatumUtc = Wmi.ZeitUtc(liste[0], "ReleaseDate");
            });

            Sammler.Versuch(s, "wmi.processor", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_Processor lieferte keine Instanz");
                int kerne = 0, threads = 0;
                foreach (var mo in liste)
                {
                    // Der SMBIOS-String traegt Fuell-Leerzeichen am Ende (gemessen: 12 Stueck).
                    if (h.Cpu == null) { string n = Wmi.Str(mo, "Name"); h.Cpu = n == null ? null : n.Trim(); }
                    kerne += Wmi.Ganz(mo, "NumberOfCores") ?? 0;
                    threads += Wmi.Ganz(mo, "NumberOfLogicalProcessors") ?? 0;
                }
                h.Kerne = kerne; h.Threads = threads;
            });

            Sammler.Versuch(s, "wmi.physicalmemory", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT Capacity,Speed,SMBIOSMemoryType FROM Win32_PhysicalMemory");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_PhysicalMemory lieferte keine Module");
                long summe = 0;
                foreach (var mo in liste)
                {
                    var m = new RamModul
                    {
                        KapazitaetBytes = Wmi.Lang(mo, "Capacity") ?? 0,
                        Takt = Wmi.Ganz(mo, "Speed") ?? 0,
                        TypSmbios = Wmi.Ganz(mo, "SMBIOSMemoryType") ?? 0,
                    };
                    summe += m.KapazitaetBytes;
                    h.RamModule.Add(m);
                }
                if (summe > 0) h.RamInstalliertKB = summe / 1024;
            });

            GrafikErfassen(s);

            Sammler.Versuch(s, "wmi.systemenclosure", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT ChassisTypes FROM Win32_SystemEnclosure");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_SystemEnclosure lieferte keine Instanz");
                // Mehrere Instanzen (Docking) und mehrere Werte je Array sind erlaubt; alle sammeln,
                // die Deutung (Hardware.GehaeuseMobil: 8-12, 14, 30-32; GehaeuseDesktop: 3-7, 13,
                // 15-17, 23, 24, 34-36) macht der Kern in Systembild.cs, hier wird nichts gedeutet.
                foreach (var mo in liste)
                    foreach (int t in Wmi.Ganze(mo, "ChassisTypes"))
                        if (!h.GehaeuseTypen.Contains(t)) h.GehaeuseTypen.Add(t);
            });

            Sammler.Versuch(s, "pinvoke.firmwaretype", () =>
            {
                uint typ;
                if (!GetFirmwareType(out typ)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                // FIRMWARE_TYPE (winnt.h): 0 Unknown, 1 BIOS, 2 UEFI. Alles andere bleibt null.
                if (typ == 1) h.Uefi = false;
                else if (typ == 2) h.Uefi = true;
                else h.Uefi = null;
            });

            AkkuErfassen(s);
        }

        // ------------------------------------------------------------------ Grafik

        /// <summary>GUID der Geraeteklasse "Display" (devguid.h GUID_DEVCLASS_DISPLAY).</summary>
        const string DisplayKlasse = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        static readonly Regex VierZiffern = new Regex(@"^\d{4}$", RegexOptions.Compiled);

        static void GrafikErfassen(Systembild s)
        {
            var h = s.Hardware;
            List<ManagementObject> liste = null;
            if (!Sammler.Versuch(s, "wmi.videocontroller", () =>
            {
                liste = Wmi.Abfrage(Cimv2, "SELECT Name,DriverVersion,DriverDate,PNPDeviceID,ConfigManagerErrorCode,AdapterRAM FROM Win32_VideoController");
                if (liste.Count == 0) throw new InvalidOperationException("Win32_VideoController lieferte keine Instanz");
            })) return;

            // VRAM aus der Registry: der Display-Klassenschluessel hat Unterschluessel 0000..NNNN
            // (hier beginnt er bei 0001) mit MatchingDeviceId und dem undokumentierten QWORD
            // HardwareInformation.qwMemorySize. Einmal einlesen, dann je Karte zuordnen.
            var vram = new List<KeyValuePair<string, long>>();
            Sammler.Versuch(s, "registry.vram", () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(DisplayKlasse))
                {
                    if (k == null) throw new InvalidOperationException("Display-Klassenschlüssel nicht lesbar");
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        if (!VierZiffern.IsMatch(sub)) continue;   // "Properties" verweigert den Zugriff, "Configuration" ist keine Karte
                        try
                        {
                            using (var sk = k.OpenSubKey(sub))
                            {
                                if (sk == null) continue;
                                string match = Convert.ToString(sk.GetValue("MatchingDeviceId"));
                                if (string.IsNullOrEmpty(match)) continue;
                                object qw = sk.GetValue("HardwareInformation.qwMemorySize");
                                long bytes = 0;
                                if (qw != null) bytes = Convert.ToInt64(qw);
                                else
                                {
                                    // Nur der DWORD vorhanden (aeltere Treiber): REG_DWORD kommt als Int32 mit
                                    // Vorzeichen (-1048576 fuer 4293918720), deshalb unchecked nach uint.
                                    object dw = sk.GetValue("HardwareInformation.MemorySize");
                                    if (dw is int) bytes = unchecked((uint)(int)dw);
                                    else if (dw != null) bytes = Convert.ToInt64(dw);
                                }
                                if (bytes > 0) vram.Add(new KeyValuePair<string, long>(match, bytes));
                            }
                        }
                        catch (Exception) { /* ein einzelner gesperrter Unterschluessel ist kein Grund, die anderen zu verlieren */ }
                    }
                }
            });

            foreach (var mo in liste)
            {
                var g = new Gpu
                {
                    Name = Wmi.Str(mo, "Name"),
                    PnpId = Wmi.Str(mo, "PNPDeviceID"),
                    TreiberVersion = Wmi.Str(mo, "DriverVersion"),
                    TreiberDatumUtc = Wmi.ZeitUtc(mo, "DriverDate"),
                    ProblemCode = Wmi.Ganz(mo, "ConfigManagerErrorCode") ?? 0,
                };
                // MatchingDeviceId ist bei NVIDIA kleingeschrieben und auf "pci\ven_10de&dev_2684"
                // gekuerzt, bei AMD vollstaendig: Praefix-Vergleich ohne Gross/Klein. Zwei baugleiche
                // Karten bekommen denselben Wert, was fuer den VRAM stimmt.
                if (g.PnpId != null)
                    foreach (var kv in vram)
                        if (g.PnpId.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) { g.VramBytes = kv.Value; break; }
                if (!g.VramBytes.HasValue)
                {
                    // Rueckfall: uint32, deckelt bei 4 GB - als Untergrenze besser als nichts.
                    long? ram = Wmi.Lang(mo, "AdapterRAM");
                    if (ram.HasValue && ram.Value > 0) g.VramBytes = ram.Value;
                }
                h.Gpus.Add(g);
            }
        }

        // ------------------------------------------------------------------ Akku

        static void AkkuErfassen(Systembild s)
        {
            var h = s.Hardware;
            // Dieselbe Einteilung wie Hardware.IstMobil im Kern (Systembild.cs), damit ein
            // All-in-One (13), Mini-PC (35) oder Stick-PC (36) ohne Akku nicht als "unbekannt" endet.
            bool sicherDesktop = h.GehaeuseSagtDesktop && !h.GehaeuseSagtMobil;

            int anzahl = -1;
            int? ladung = null, design = null, voll = null;
            if (!Sammler.Versuch(s, "wmi.battery", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT EstimatedChargeRemaining,DesignCapacity,FullChargeCapacity FROM Win32_Battery");
                anzahl = liste.Count;
                if (liste.Count > 0)
                {
                    ladung = Wmi.Ganz(liste[0], "EstimatedChargeRemaining");
                    design = Wmi.Ganz(liste[0], "DesignCapacity");
                    voll = Wmi.Ganz(liste[0], "FullChargeCapacity");
                }
            })) return;   // Fehler steht in der Liste; Akku bleibt null = unbekannt

            var akku = new Akku();
            if (anzahl > 0)
            {
                akku.Vorhanden = true;
                akku.LadungPct = ladung;
                akku.DesignMWh = design;
                akku.VollMWh = voll;
                // Win32_Battery laesst Design-/FullChargeCapacity oft leer; root\wmi hat die
                // Werte des Akku-Treibers. Auf einem Desktop wirft die Klasse "Ungueltige Klasse",
                // hier fragen wir nur, wenn es einen Akku gibt - also nie ins Leere.
                Sammler.Versuch(s, "wmi.batterystatic", () =>
                {
                    var st = Wmi.Abfrage(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData");
                    if (st.Count > 0) { int? d = Wmi.Ganz(st[0], "DesignedCapacity"); if (d.HasValue && d.Value > 0) akku.DesignMWh = d; }
                    var fc = Wmi.Abfrage(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
                    if (fc.Count > 0) { int? v = Wmi.Ganz(fc[0], "FullChargedCapacity"); if (v.HasValue && v.Value > 0) akku.VollMWh = v; }
                });
            }
            else if (sicherDesktop)
            {
                // Null Instanzen auf einem Desktop-Gehaeuse ist eine Antwort, keine leere Liste:
                // dieser PC hat keinen Akku (Konzept, Abschnitt 9).
                akku.Vorhanden = false;
            }
            else
            {
                // Null Instanzen ohne Desktop-Gehaeuse (Laptop, Tablet, unbekanntes Gehaeuse):
                // kann ein abgezogener Akku sein, ein fehlender Treiber oder eine VM. Unbekannt,
                // und das steht als "fehlt" in der Liste, damit es nicht still "kein Akku" wird.
                akku.Vorhanden = null;
                Sammler.Fehler(s, "wmi.battery", Fehler.Fehlt,
                    h.GehaeuseTypen.Count == 0
                        ? "Win32_Battery lieferte 0 Instanzen, Gehäusetyp nicht gelesen"
                        : "Win32_Battery lieferte 0 Instanzen, Gehäuse nicht als Desktop erkannt (ChassisTypes " + string.Join(",", h.GehaeuseTypen) + ")");
            }
            h.Akku = akku;
        }

        // ------------------------------------------------------------------ P/Invoke

        /// <summary>winbase.h: BOOL GetFirmwareType(PFIRMWARE_TYPE). Kernel32, ab Windows 8, ohne Rechte.</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFirmwareType(out uint firmwareType);
    }
}
