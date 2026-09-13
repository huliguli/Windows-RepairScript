using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Fuellt s.Datentraeger und s.Volumes - im eigenen Prozess, ohne Fremdprozess und ohne Konsolentext.
    ///
    /// Reihenfolge (aus der Recherche, Abschnitt 4.2 des Konzepts):
    ///   1. MSFT_PhysicalDisk (root\Microsoft\Windows\Storage): Inventar, HealthStatus, OperationalStatus
    ///      als Zahlen. Die Wertetabelle steht fest im Regelwerk, nie aus dem MOF gelesen (das lokale
    ///      MOF hat Tippfehler wie "0xDO1C" mit Buchstabe O).
    ///   2. MSFT_StorageReliabilityCounter ueber die Assoziation - NUR erhoeht. Nicht erhoeht liefert
    ///      die Assoziation still nichts; deshalb traegt der Sammler dann "zugriff" ein, statt eine
    ///      leere Antwort als "keine Zaehler" zu deuten. Bei NVMe fehlen PowerOnHours und alle
    ///      Fehlerzaehler (null, gemessen) - sie bleiben null, nie 0.
    ///   3. NVMe-Gesundheitslog per IOCTL_STORAGE_QUERY_PROPERTY auf \\.\PhysicalDriveN mit
    ///      dwDesiredAccess = 0. Das geht ohne Administratorrechte (gemessen mit Medium-IL-Token).
    ///   4. Temperaturschwellen des Geraets (PropertyId 52) - Schwellen vom Geraet, keine festen Zahlen.
    ///   5. TRIM: Registry DisableDeleteNotification (0 = aktiv) als Feld trim, DEVICE_TRIM_DESCRIPTOR
    ///      je Geraet als eigenes Feld trimGeraet - nie zu einem Wert verrechnet, weil die Regel
    ///      bei "Registry aus" die Registry raet und bei "Geraet meldet kein TRIM" nichts an der
    ///      Registry zu aendern ist (SSD an USB-Bruecke, RAID-Treiber).
    ///   6. Win32_Volume (root\cimv2) als Grundliste - liefert nicht erhoeht auch die EFI-Partition,
    ///      die MSFT_Volume ohne Rechte verschweigt (Widerlegungsrunde). MSFT_Volume ergaenzt
    ///      HealthStatus/OperationalStatus, MSFT_Partition Versteckt/System/Boot/GptTyp.
    ///   7. Dirty-Bit: Win32_Volume.DirtyBitSet ist nicht erhoeht null (kein Fehler!), FSCTL_IS_VOLUME_DIRTY
    ///      braucht GENERIC_READ auf dem Volume und damit Rechte. Nicht erhoeht: "wmi.volume.dirty" zugriff.
    ///
    /// Erfolg eines DeviceIoControl wird NUR am BOOL-Rueckgabewert und an BytesReturned gemessen.
    /// Nach einem erfolgreichen Aufruf stand GetLastWin32Error auf 203 (Altwert, gemessen) - wer den
    /// liest, erfindet Fehler.
    /// </summary>
    public static class DatentraegerQuelle
    {
        const string StorageNs = @"root\Microsoft\Windows\Storage";
        const string Cimv2 = @"root\cimv2";

        public static void Erfassen(Systembild s)
        {
            bool? trimSystemweit = TrimSystemweit(s);
            PhysischeDatentraeger(s, trimSystemweit);
            Volumes(s);
        }

        // ================================================================ Datentraeger

        static void PhysischeDatentraeger(Systembild s, bool? trimSystemweit)
        {
            List<ManagementObject> disks = null;
            if (!Sammler.Versuch(s, "wmi.storage.physicaldisk",
                    () => disks = Wmi.Abfrage(StorageNs, "SELECT * FROM MSFT_PhysicalDisk", 10000)))
                return;
            if (disks == null || disks.Count == 0)
            {
                Sammler.Fehler(s, "wmi.storage.physicaldisk", Fehler.Fehlt, "MSFT_PhysicalDisk lieferte keine Instanz");
                return;
            }

            var paare = new List<KeyValuePair<ManagementObject, Datentraeger>>();
            foreach (var mo in disks)
            {
                var d = new Datentraeger
                {
                    ObjectId = Wmi.Str(mo, "ObjectId"),
                    Name = Wmi.OhnePlatzhalter(Wmi.Str(mo, "FriendlyName")),
                    Nummer = GanzAusText(Wmi.Str(mo, "DeviceId")),
                    MedienTyp = Wmi.Ganz(mo, "MediaType") ?? 0,
                    BusTyp = Wmi.Ganz(mo, "BusType") ?? 0,
                    // Fehlt HealthStatus, gilt laut Doku der Wert 5 (Unknown) - nie 0 (Healthy) annehmen.
                    Health = Wmi.Ganz(mo, "HealthStatus") ?? 5,
                    OpStatus = Wmi.Ganze(mo, "OperationalStatus"),
                    GroesseBytes = Wmi.Lang(mo, "Size") ?? 0,
                    Firmware = Wmi.Str(mo, "FirmwareVersion"),
                };
                s.Datentraeger.Add(d);
                paare.Add(new KeyValuePair<ManagementObject, Datentraeger>(mo, d));
            }

            // Zaehler nur erhoeht: ein Fehlereintrag fuer alle Datentraeger, nicht einer je Platte.
            Sammler.NurErhoeht(s, "wmi.storage.reliability", () =>
            {
                foreach (var p in paare)
                {
                    var mo = p.Key; var d = p.Value;
                    Sammler.Versuch(s, "wmi.storage.reliability", () => Zaehler(s, mo, d));
                }
            });

            foreach (var p in paare)
            {
                var d = p.Value;
                NvmeLog(s, d);
                TempSchwellen(s, d);
                Trim(s, d, trimSystemweit);
            }
        }

        /// <summary>
        /// MSFT_StorageReliabilityCounter ueber mo.GetRelated (Assoziationsklasse
        /// MSFT_PhysicalDiskToStorageReliabilityCounter). Es gibt keine Methode GetReliabilityCounter
        /// (Recherche R1-04); Get-StorageReliabilityCounter ist nur eine cdxml-Huelle dieser Assoziation.
        /// </summary>
        static void Zaehler(Systembild s, ManagementObject disk, Datentraeger d)
        {
            var related = MitZeitgrenze(() =>
            {
                var l = new List<ManagementBaseObject>();
                foreach (ManagementBaseObject r in disk.GetRelated("MSFT_StorageReliabilityCounter")) l.Add(r);
                return l;
            }, Wmi.StandardZeitMs, "GetRelated MSFT_StorageReliabilityCounter Datenträger " + d.Nummer);

            if (related.Count == 0)
            {
                // Erhoeht und trotzdem leer: das Geraet (z. B. USB-Bruecke) liefert keine Zaehler.
                Sammler.Fehler(s, "wmi.storage.reliability", Fehler.Fehlt, "Datenträger " + d.Nummer + ": keine Zähler-Instanz");
                return;
            }
            var r0 = related[0];
            // null bleibt null: bei NVMe sind PowerOnHours und die Fehlerzaehler leer (gemessen).
            d.Zaehler = new Zaehler
            {
                Wear = Wmi.Ganz(r0, "Wear"),
                Temp = Wmi.Ganz(r0, "Temperature"),
                TempMax = Wmi.Ganz(r0, "TemperatureMax"),
                Stunden = Wmi.Ganz(r0, "PowerOnHours"),
                LeseFehlerUnkorr = Wmi.Lang(r0, "ReadErrorsUncorrected"),
                LeseFehlerGesamt = Wmi.Lang(r0, "ReadErrorsTotal"),
                SchreibFehlerUnkorr = Wmi.Lang(r0, "WriteErrorsUncorrected"),
            };
        }

        /// <summary>
        /// NVME_HEALTH_INFO_LOG (nvme.h, SDK 10.0.26100): Byte 0 CriticalWarning, 1-2 Temperature
        /// (Kelvin, UInt16), 3 AvailableSpare, 4 AvailableSpareThreshold, 5 PercentageUsed,
        /// 112-127 PowerCycles, 128-143 PowerOnHours, 144-159 UnsafeShutdowns, 160-175 MediaErrors
        /// (je 16 Byte little endian; die unteren 8 Byte reichen). Nur bei BusType 17 (NVMe).
        /// </summary>
        static void NvmeLog(Systembild s, Datentraeger d)
        {
            const string q = "ioctl.nvme.health";
            if (d.BusTyp != 17)
            {
                Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger " + d.Nummer + ": kein NVMe (BusType " + d.BusTyp + "), Gesundheitslog nicht anwendbar");
                return;
            }
            if (!d.Nummer.HasValue)
            {
                Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger ohne DeviceId, kein \\\\.\\PhysicalDriveN");
                return;
            }
            Sammler.Versuch(s, q, () =>
            {
                using (var h = OeffnePhysicalDrive(d.Nummer.Value, s, q))
                {
                    if (h == null) return;
                    // Ein Puffer fuer Eingabe und Ausgabe: STORAGE_PROPERTY_QUERY (PropertyId, QueryType,
                    // dann AdditionalParameters ab Offset 8) ueberlagert STORAGE_PROTOCOL_DATA_DESCRIPTOR
                    // (Version, Size, dann STORAGE_PROTOCOL_SPECIFIC_DATA ab Offset 8). Das Log folgt bei
                    // 8 + ProtocolDataOffset = 48. Gesamt 560 Byte (gemessen: BytesReturned 560).
                    const int KopfLaenge = 8 + 40, LogLaenge = 512;
                    var puffer = new byte[KopfLaenge + LogLaenge];
                    SchreibeUInt32(puffer, 0, 50);            // StorageDeviceProtocolSpecificProperty
                    SchreibeUInt32(puffer, 4, 0);             // PropertyStandardQuery
                    SchreibeUInt32(puffer, 8, 3);             // ProtocolType = ProtocolTypeNvme
                    SchreibeUInt32(puffer, 12, 2);            // DataType = NVMeDataTypeLogPage
                    SchreibeUInt32(puffer, 16, 2);            // ProtocolDataRequestValue = NVME_LOG_PAGE_HEALTH_INFO
                    SchreibeUInt32(puffer, 20, 0);            // ProtocolDataRequestSubValue (Namespace)
                    SchreibeUInt32(puffer, 24, 40);           // ProtocolDataOffset = sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA)
                    SchreibeUInt32(puffer, 28, (uint)LogLaenge); // ProtocolDataLength

                    uint zurueck;
                    bool ok = Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, puffer, (uint)puffer.Length,
                                                     puffer, (uint)puffer.Length, out zurueck, IntPtr.Zero);
                    if (!ok)
                    {
                        Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger " + d.Nummer + ": IOCTL_STORAGE_QUERY_PROPERTY(50) abgelehnt, Win32-Fehler " + Marshal.GetLastWin32Error());
                        return;
                    }
                    uint datenOffset = LiesUInt32(puffer, 24), datenLaenge = LiesUInt32(puffer, 28);
                    long logStart = 8 + datenOffset;
                    if (datenLaenge < LogLaenge || logStart + LogLaenge > zurueck)
                    {
                        Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger " + d.Nummer + ": Log unvollständig (" + zurueck + " Byte, Offset " + datenOffset + ", Länge " + datenLaenge + ")");
                        return;
                    }
                    int b = (int)logStart;
                    int kelvin = puffer[b + 1] | (puffer[b + 2] << 8);
                    d.Nvme = new NvmeLog
                    {
                        CriticalWarning = puffer[b + 0],
                        // Die Spezifikation rechnet in Kelvin; 312 K sind 39 Grad Celsius (gemessen).
                        TempC = kelvin - 273,
                        Spare = puffer[b + 3],
                        SpareSchwelle = puffer[b + 4],
                        UsedPct = puffer[b + 5],
                        Stunden = LiesUInt64AlsLong(puffer, b + 128),
                        UnsafeShutdowns = LiesUInt64AlsLong(puffer, b + 144),
                        MedienFehler = LiesUInt64AlsLong(puffer, b + 160),
                    };
                }
            });
        }

        /// <summary>
        /// STORAGE_TEMPERATURE_DATA_DESCRIPTOR (winioctl.h, SDK 10.0.26100, gegen die Doku
        /// learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-storage_temperature_data_descriptor
        /// geprueft): Version DWORD @0, Size DWORD @4, CriticalTemperature SHORT @8, WarningTemperature
        /// SHORT @10, InfoCount WORD @12, Reserved0[2] BYTE @14, Reserved1[2] DWORD @16, TemperatureInfo[]
        /// ab @24. STORAGE_TEMPERATURE_INFO: Index WORD @0, Temperature SHORT @2, OverThreshold SHORT @4,
        /// UnderThreshold SHORT @6, drei BOOLEAN @8-10, Reserved0 BYTE @11, Reserved1 DWORD @12 - also
        /// 16 Byte je Sensor, nicht 12 (die Messung mit 3 Sensoren ergab 24 + 3 * 16 = 72 Byte).
        ///
        /// Widerlegungsrunde: nur TemperatureInfo[0] (Composite) traegt echte Schwellen; Sensoren 1 und 2
        /// melden OverThreshold -274 und UnderThreshold -32768 als "nicht gesetzt". Solche Sensoren
        /// zaehlen hier nicht als Schwelle. Die Geraeteschwellen im Kopf (hier 90/94) gehen ins Bild.
        /// </summary>
        static void TempSchwellen(Systembild s, Datentraeger d)
        {
            const string q = "ioctl.storage.temperatur";
            if (!d.Nummer.HasValue) { Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger ohne DeviceId"); return; }
            Sammler.Versuch(s, q, () =>
            {
                using (var h = OeffnePhysicalDrive(d.Nummer.Value, s, q))
                {
                    if (h == null) return;
                    const int Kopf = 24, JeSensor = 16, MaxSensoren = 16;
                    var eingabe = new byte[12];
                    SchreibeUInt32(eingabe, 0, 52);           // StorageDeviceTemperatureProperty
                    SchreibeUInt32(eingabe, 4, 0);            // PropertyStandardQuery
                    var ausgabe = new byte[Kopf + JeSensor * MaxSensoren];
                    uint zurueck;
                    bool ok = Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, eingabe, (uint)eingabe.Length,
                                                     ausgabe, (uint)ausgabe.Length, out zurueck, IntPtr.Zero);
                    if (!ok || zurueck < Kopf)
                    {
                        // USB-Bruecken und aeltere SATA-Controller kennen die Eigenschaft nicht: dann gibt es
                        // keine Geraeteschwelle und die Regel bewertet die Temperatur nicht (unknown).
                        Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger " + d.Nummer + ": Temperatureigenschaft nicht verfügbar" + (ok ? " (" + zurueck + " Byte)" : ", Win32-Fehler " + Marshal.GetLastWin32Error()));
                        return;
                    }
                    short kritisch = BitConverter.ToInt16(ausgabe, 8);
                    short warn = BitConverter.ToInt16(ausgabe, 10);
                    int anzahl = BitConverter.ToUInt16(ausgabe, 12);
                    d.TempKritisch = SchwelleGueltig(kritisch) ? (int?)kritisch : null;
                    d.TempWarn = SchwelleGueltig(warn) ? (int?)warn : null;

                    // Fehlt die Kopfschwelle, gilt die OverThreshold des Composite-Sensors (Index 0), sofern
                    // sie kein Platzhalter ist. Sensoren mit Platzhalter-Schwellen bleiben unberuecksichtigt.
                    if (!d.TempWarn.HasValue)
                    {
                        for (int i = 0; i < anzahl && i < MaxSensoren; i++)
                        {
                            int o = Kopf + i * JeSensor;
                            if (o + JeSensor > zurueck) break;
                            int index = BitConverter.ToUInt16(ausgabe, o);
                            short ueber = BitConverter.ToInt16(ausgabe, o + 4);
                            short unter = BitConverter.ToInt16(ausgabe, o + 6);
                            bool platzhalter = ueber <= -273 || unter == -32768;
                            if (index == 0 && !platzhalter && SchwelleGueltig(ueber)) { d.TempWarn = ueber; break; }
                        }
                    }
                    if (!d.TempWarn.HasValue)
                        Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger " + d.Nummer + ": Gerät nennt keine gültige Warnschwelle (Kopf " + warn + "/" + kritisch + ", " + anzahl + " Sensoren)");
                }
            });
        }

        /// <summary>Eine Temperaturschwelle ist nur plausibel zwischen 1 und 199 Grad; -274 und -32768 sind Platzhalter.</summary>
        static bool SchwelleGueltig(short t) { return t > 0 && t < 200; }

        /// <summary>
        /// TRIM je Geraet: DEVICE_TRIM_DESCRIPTOR (Version @0, Size @4, TrimEnabled BOOLEAN @8) ueber
        /// PropertyId 8 (StorageDeviceTrimProperty). Geht als eigenes Feld trimGeraet ins Bild; das
        /// Feld trim traegt allein die Registry. Frueher wurden beide zu einem Wert verrechnet, und
        /// eine SSD an einer USB-Bruecke (Geraet 0, Registry 0) bekam den Rat, die Registry auf den
        /// Wert zu setzen, den sie schon hatte. Geraet nicht abfragbar: null, nie "unterstuetzt".
        /// </summary>
        static void Trim(Systembild s, Datentraeger d, bool? systemweit)
        {
            const string q = "ioctl.storage.trim";
            d.Trim = systemweit;
            if (!d.Nummer.HasValue) { Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger ohne DeviceId"); return; }
            Sammler.Versuch(s, q, () =>
            {
                using (var h = OeffnePhysicalDrive(d.Nummer.Value, s, q))
                {
                    if (h == null) return;
                    var eingabe = new byte[12];
                    SchreibeUInt32(eingabe, 0, 8);            // StorageDeviceTrimProperty
                    SchreibeUInt32(eingabe, 4, 0);
                    var ausgabe = new byte[16];
                    uint zurueck;
                    bool ok = Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, eingabe, (uint)eingabe.Length,
                                                     ausgabe, (uint)ausgabe.Length, out zurueck, IntPtr.Zero);
                    if (!ok || zurueck < 9)
                    {
                        Sammler.Fehler(s, q, Fehler.Fehlt, "Datenträger " + d.Nummer + ": TRIM-Eigenschaft nicht verfügbar" + (ok ? " (" + zurueck + " Byte)" : ", Win32-Fehler " + Marshal.GetLastWin32Error()));
                        return;
                    }
                    d.TrimGeraet = ausgabe[8] != 0;
                }
            });
        }

        /// <summary>
        /// HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\DisableDeleteNotification: 0 = TRIM aktiv,
        /// 1 = aus (doppelte Verneinung, fsutil-behavior-Doku). Fehlt der Wert, gilt die Voreinstellung
        /// "aktiv" fuer NTFS. Der fsutil-Klammertext ist lokalisiert und wird nicht mehr gelesen.
        /// </summary>
        static bool? TrimSystemweit(Systembild s)
        {
            bool? ergebnis = null;
            Sammler.Versuch(s, "registry.trim", () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem"))
                {
                    if (k == null) { Sammler.Fehler(s, "registry.trim", Fehler.Fehlt, "Schlüssel Control\\FileSystem fehlt"); return; }
                    object v = k.GetValue("DisableDeleteNotification");
                    if (v == null) { ergebnis = true; return; }
                    ergebnis = Convert.ToInt64(v, CultureInfo.InvariantCulture) == 0;
                }
            });
            return ergebnis;
        }

        // ================================================================ Volumes

        static void Volumes(Systembild s)
        {
            List<ManagementObject> vols = null;
            if (!Sammler.Versuch(s, "wmi.volume", () => vols = Wmi.Abfrage(Cimv2, "SELECT * FROM Win32_Volume", 15000))) return;
            if (vols == null || vols.Count == 0)
            {
                Sammler.Fehler(s, "wmi.volume", Fehler.Fehlt, "Win32_Volume lieferte keine Instanz");
                return;
            }
            foreach (var mo in vols)
            {
                var v = new Volume
                {
                    Buchstabe = Buchstabe(Wmi.Str(mo, "DriveLetter")),
                    Pfad = Wmi.Str(mo, "DeviceID"),
                    Dateisystem = Wmi.Str(mo, "FileSystem"),
                    Typ = Wmi.Ganz(mo, "DriveType") ?? 0,
                    GroesseBytes = Wmi.Lang(mo, "Capacity") ?? 0,
                    FreiBytes = Wmi.Lang(mo, "FreeSpace") ?? 0,
                    Label = Wmi.Str(mo, "Label"),
                    // Nicht erhoeht null - und das ist "unbekannt", nie "sauber" (Widerlegungsrunde).
                    Dirty = Wmi.Wahr(mo, "DirtyBitSet"),
                    System = Wmi.Wahr(mo, "SystemVolume") == true,
                    Boot = Wmi.Wahr(mo, "BootVolume") == true,
                };
                s.Volumes.Add(v);
            }

            // MSFT_Volume: Aktion steht in OperationalStatus (0xD00D/0xD00E/0xD00F), HealthStatus ist nur
            // der Schweregrad - Doku und MOF widersprechen sich, das lokale MOF und Get-Volume gewinnen.
            Sammler.Versuch(s, "wmi.storage.volume", () =>
            {
                var liste = Wmi.Abfrage(StorageNs, "SELECT * FROM MSFT_Volume", 10000);
                int treffer = 0;
                foreach (var mo in liste)
                {
                    string pfad = Wmi.Str(mo, "Path");
                    string b = Buchstabe(Wmi.Str(mo, "DriveLetter"));
                    var v = Finde(s, pfad, b);
                    if (v == null) continue;
                    treffer++;
                    v.Health = Wmi.Ganz(mo, "HealthStatus");
                    v.OpStatus = Wmi.Ganze(mo, "OperationalStatus");
                }
                if (liste.Count == 0) Sammler.Fehler(s, "wmi.storage.volume", Fehler.Fehlt, "MSFT_Volume lieferte keine Instanz");
                else if (treffer == 0) Sammler.Fehler(s, "wmi.storage.volume", Fehler.Fehlt, liste.Count + " MSFT_Volume ohne Entsprechung in Win32_Volume");
            });

            // MSFT_Partition ist ohne Erhoehung vollstaendig lesbar (Widerlegungsrunde: "Count=0" war ein
            // Artefakt des SAFER-Tokens). AccessPaths traegt den Volume-GUID-Pfad, der zu Win32_Volume.DeviceID
            // passt - so bekommen auch versteckte Partitionen ohne Buchstaben ihr GptType/IsHidden.
            Sammler.Versuch(s, "wmi.storage.partition", () =>
            {
                var liste = Wmi.Abfrage(StorageNs, "SELECT * FROM MSFT_Partition", 10000);
                int treffer = 0;
                foreach (var mo in liste)
                {
                    string b = Buchstabe(Wmi.Str(mo, "DriveLetter"));
                    Volume v = null;
                    foreach (string ap in Wmi.Strings(mo, "AccessPaths"))
                    {
                        v = Finde(s, ap, null);
                        if (v != null) break;
                    }
                    if (v == null && b != null) v = Finde(s, null, b);
                    if (v == null) continue;
                    treffer++;
                    v.GptTyp = Wmi.Str(mo, "GptType");
                    v.Versteckt = Wmi.Wahr(mo, "IsHidden") == true;
                    if (Wmi.Wahr(mo, "IsSystem") == true) v.System = true;
                    if (Wmi.Wahr(mo, "IsBoot") == true) v.Boot = true;
                }
                if (liste.Count == 0) Sammler.Fehler(s, "wmi.storage.partition", Fehler.Fehlt, "MSFT_Partition lieferte keine Instanz");
                else if (treffer == 0) Sammler.Fehler(s, "wmi.storage.partition", Fehler.Fehlt, liste.Count + " MSFT_Partition ohne Entsprechung in Win32_Volume");
            });

            // Dirty-Bit direkt vom Dateisystem: FSCTL_IS_VOLUME_DIRTY (0x90078) mit GENERIC_READ auf \\.\C:.
            // Mit dwDesiredAccess 0 oder FILE_READ_ATTRIBUTES scheitert der FSCTL mit Fehler 1, GENERIC_READ
            // auf einem Volume braucht Rechte (Fehler 5 nicht erhoeht, gemessen). Deshalb NurErhoeht.
            Sammler.NurErhoeht(s, "wmi.volume.dirty", () =>
            {
                int gelesen = 0;
                foreach (var v in s.Volumes)
                {
                    if (v.Typ != 3 || (string.IsNullOrEmpty(v.Pfad) && string.IsNullOrEmpty(v.Buchstabe))) continue;
                    string pfad = !string.IsNullOrEmpty(v.Buchstabe) ? @"\\.\" + v.Buchstabe + ":" : v.Pfad.TrimEnd('\\');
                    var vv = v;
                    Sammler.Versuch(s, "wmi.volume.dirty", () =>
                    {
                        using (var h = Native.CreateFileW(pfad, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                                                          IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid)
                            {
                                int err = Marshal.GetLastWin32Error();
                                if (err == 5) throw new UnauthorizedAccessException("CreateFile " + pfad + " Fehler 5");
                                Sammler.Fehler(s, "wmi.volume.dirty", Fehler.Fehlt, pfad + ": CreateFile Fehler " + err);
                                return;
                            }
                            var ausgabe = new byte[4];
                            uint zurueck;
                            bool ok = Native.DeviceIoControl(h, Native.FSCTL_IS_VOLUME_DIRTY, null, 0, ausgabe, 4, out zurueck, IntPtr.Zero);
                            if (!ok || zurueck < 4)
                            {
                                Sammler.Fehler(s, "wmi.volume.dirty", Fehler.Fehlt, pfad + ": FSCTL_IS_VOLUME_DIRTY abgelehnt" + (ok ? "" : ", Win32-Fehler " + Marshal.GetLastWin32Error()));
                                return;
                            }
                            // VOLUME_IS_DIRTY = 0x1; VOLUME_UPGRADE_SCHEDULED (0x2) ist laut Doku unbenutzt.
                            vv.Dirty = (LiesUInt32(ausgabe, 0) & 1) != 0;
                            gelesen++;
                        }
                    });
                }
                if (gelesen == 0) Sammler.Fehler(s, "wmi.volume.dirty", Fehler.Fehlt, "kein festes Volume lesbar");
            });
        }

        static Volume Finde(Systembild s, string pfad, string buchstabe)
        {
            foreach (var v in s.Volumes)
            {
                if (!string.IsNullOrEmpty(pfad) && !string.IsNullOrEmpty(v.Pfad)
                    && string.Equals(v.Pfad.TrimEnd('\\'), pfad.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return v;
                if (!string.IsNullOrEmpty(buchstabe) && string.Equals(v.Buchstabe, buchstabe, StringComparison.OrdinalIgnoreCase)) return v;
            }
            return null;
        }

        /// <summary>"C:" (Win32_Volume) oder "C" bzw. "\0" (MSFT_*, Char16) nach "C"; leer nach null.</summary>
        static string Buchstabe(string roh)
        {
            if (string.IsNullOrEmpty(roh)) return null;
            string t = roh.Trim().TrimEnd(':').Trim('\0', ' ');
            return t.Length == 0 ? null : t.ToUpperInvariant();
        }

        // ================================================================ Helfer

        /// <summary>
        /// \\.\PhysicalDriveN mit dwDesiredAccess = 0 - laut CreateFile-Doku reicht das fuer
        /// Geraeteattribute ohne hoehere Rechte (gemessen: geht mit Medium-IL). Bei Fehler 5 wird die
        /// Ausnahme zum Eintrag "zugriff", andere Fehler zu "fehlt". Rueckgabe null = schon eingetragen.
        /// </summary>
        static SafeFileHandle OeffnePhysicalDrive(int nummer, Systembild s, string quelle)
        {
            string pfad = @"\\.\PhysicalDrive" + nummer.ToString(CultureInfo.InvariantCulture);
            var h = Native.CreateFileW(pfad, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (!h.IsInvalid) return h;
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            if (err == 5) throw new UnauthorizedAccessException("CreateFile " + pfad + " Fehler 5");
            Sammler.Fehler(s, quelle, Fehler.Fehlt, pfad + ": CreateFile Fehler " + err);
            return null;
        }

        /// <summary>Wie Wmi.Abfrage, aber fuer beliebige WMI-Aufrufe (GetRelated haengt genauso gern wie ein Searcher).</summary>
        static T MitZeitgrenze<T>(Func<T> f, int zeitMs, string was)
        {
            T ergebnis = default(T);
            Exception fehler = null;
            var t = new System.Threading.Thread(() => { try { ergebnis = f(); } catch (Exception ex) { fehler = ex; } })
            { IsBackground = true, Name = "wmi:" + was };
            t.Start();
            if (!t.Join(zeitMs)) throw new TimeoutException("WMI-Aufruf überschritt " + zeitMs + " ms: " + was);
            if (fehler != null) throw fehler;
            return ergebnis;
        }

        static int? GanzAusText(string t)
        {
            int n;
            return t != null && int.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? (int?)n : null;
        }

        static void SchreibeUInt32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }

        static uint LiesUInt32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }

        /// <summary>16-Byte-Zaehler des NVMe-Logs: die unteren 8 Byte reichen; ueber long.MaxValue wird gedeckelt.</summary>
        static long LiesUInt64AlsLong(byte[] b, int o)
        {
            ulong v = BitConverter.ToUInt64(b, o);
            return v > long.MaxValue ? long.MaxValue : (long)v;
        }

        static class Native
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
            public const uint OPEN_EXISTING = 3;
            // CTL_CODE(FILE_DEVICE_MASS_STORAGE 0x2d, 0x500, METHOD_BUFFERED, FILE_ANY_ACCESS) = 0x2D1400
            public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
            // CTL_CODE(FILE_DEVICE_FILE_SYSTEM 0x9, 30, METHOD_BUFFERED, FILE_ANY_ACCESS) = 0x90078
            public const uint FSCTL_IS_VOLUME_DIRTY = 0x90078;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
                IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
                byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
                out uint lpBytesReturned, IntPtr lpOverlapped);
        }
    }
}
