using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WartungsToolbox.Kern
{
    /// <summary>
    /// Das Systembild: eine Momentaufnahme des PCs aus Zahlen, Aufzaehlungen, Kennungen und
    /// Zeitstempeln. Es ist der Vertrag zwischen Sammler (fuellt es), Regeln (lesen es) und
    /// Aufzeichnung (schreibt und liest es als JSON fuer die Proben).
    ///
    /// Drei Regeln, die den Unterschied zu v7 ausmachen:
    ///
    /// 1. Kein lokalisierter Text wird ausgewertet. Felder wie "name" oder "titel" sind reine
    ///    Anzeige. Entschieden wird ueber Zahlen, Enums, GUIDs, IDs und Ereignisfelder.
    /// 2. Jede Quelle liefert entweder Daten oder einen Eintrag in "fehlerliste". Eine leere
    ///    Liste ohne Fehlereintrag ist ein Fehler im Sammler, kein Befund ueber den PC.
    ///    Gemessen: Win32_Tpm und Win32_EncryptableVolume liefern nicht erhoeht STILL nichts,
    ///    Win32_Volume.DirtyBitSet ist null, ein gesperrtes Ereignisprotokoll meldet
    ///    "0 Ereignisse" statt "Zugriff verweigert".
    /// 3. Alle Zeiten sind ISO-8601 in UTC als Text ("2026-09-11T16:20:31Z"). Damit sind sie
    ///    sprachneutral, im JSON lesbar und frei von den Fallen der Serialisierer.
    ///
    /// Die Klasse darf von nichts abhaengen ausser mscorlib, System, System.Core und
    /// System.Runtime.Serialization. Kein WinForms, kein WMI, kein Prozessstart - das ist die
    /// Bedingung dafuer, dass die Regeln ohne Rechte und ohne Oberflaeche pruefbar sind.
    /// </summary>
    [DataContract]
    public class Systembild
    {
        public const int AktuellesSchema = 1;

        [DataMember(Name = "schema")] public int Schema = AktuellesSchema;
        [DataMember(Name = "aufgezeichnet")] public string AufgezeichnetUtc;
        [DataMember(Name = "erhoeht")] public bool Erhoeht;
        [DataMember(Name = "sprache")] public Sprache Sprache = new Sprache();
        [DataMember(Name = "windows")] public Windows Windows = new Windows();
        [DataMember(Name = "hardware")] public Hardware Hardware = new Hardware();
        [DataMember(Name = "geraete")] public List<Geraet> Geraete = new List<Geraet>();
        [DataMember(Name = "datentraeger")] public List<Datentraeger> Datentraeger = new List<Datentraeger>();
        [DataMember(Name = "volumes")] public List<Volume> Volumes = new List<Volume>();
        [DataMember(Name = "ereignisse")] public Ereignisse Ereignisse = new Ereignisse();
        [DataMember(Name = "zuverlaessigkeit")] public Zuverlaessigkeit Zuverlaessigkeit = new Zuverlaessigkeit();
        [DataMember(Name = "autostart")] public Autostart Autostart = new Autostart();
        [DataMember(Name = "leistung")] public Leistung Leistung = new Leistung();
        [DataMember(Name = "windowsUpdate")] public WindowsUpdate WindowsUpdate = new WindowsUpdate();
        [DataMember(Name = "netz")] public Netz Netz = new Netz();
        [DataMember(Name = "sicherheit")] public Sicherheit Sicherheit = new Sicherheit();
        [DataMember(Name = "systemschutz")] public Systemschutz Systemschutz = new Systemschutz();
        [DataMember(Name = "fehlerliste")] public List<Fehler> Fehlerliste = new List<Fehler>();

        /// <summary>Alle Fehler einer Quelle (Praefix-Vergleich, z. B. "wmi.storage").</summary>
        public IEnumerable<Fehler> FehlerVon(string quellenPraefix)
        {
            foreach (var f in Fehlerliste)
                if (f.Quelle != null && f.Quelle.StartsWith(quellenPraefix)) yield return f;
        }

        /// <summary>true, wenn eine Quelle wegen fehlender Rechte nichts geliefert hat.</summary>
        public bool ZugriffVerweigert(string quellenPraefix)
        {
            foreach (var f in FehlerVon(quellenPraefix))
                if (f.Art == Fehler.Zugriff) return true;
            return false;
        }
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Sprache == null) Sprache = new Sprache();
            if (Windows == null) Windows = new Windows();
            if (Hardware == null) Hardware = new Hardware();
            if (Geraete == null) Geraete = new List<Geraet>();
            if (Datentraeger == null) Datentraeger = new List<Datentraeger>();
            if (Volumes == null) Volumes = new List<Volume>();
            if (Ereignisse == null) Ereignisse = new Ereignisse();
            if (Zuverlaessigkeit == null) Zuverlaessigkeit = new Zuverlaessigkeit();
            if (Autostart == null) Autostart = new Autostart();
            if (Leistung == null) Leistung = new Leistung();
            if (WindowsUpdate == null) WindowsUpdate = new WindowsUpdate();
            if (Netz == null) Netz = new Netz();
            if (Sicherheit == null) Sicherheit = new Sicherheit();
            if (Systemschutz == null) Systemschutz = new Systemschutz();
            if (Fehlerliste == null) Fehlerliste = new List<Fehler>();
        }

    }

    // ------------------------------------------------------------------ Kopf

    [DataContract]
    public class Sprache
    {
        /// <summary>LCID der installierten Systemsprache (1031 = deutsch, 1033 = englisch).</summary>
        [DataMember(Name = "lcid")] public int Lcid;
        [DataMember(Name = "mui")] public List<string> Mui = new List<string>();
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Mui == null) Mui = new List<string>();
        }

    }

    [DataContract]
    public class Windows
    {
        /// <summary>Anzeigetext, z. B. "Microsoft Windows 11 Pro". Nur Anzeige.</summary>
        [DataMember(Name = "caption")] public string Caption;
        /// <summary>EditionID aus der Registry, z. B. "Professional", "Core".</summary>
        [DataMember(Name = "edition")] public string Edition;
        [DataMember(Name = "build")] public int Build;
        [DataMember(Name = "ubr")] public int Ubr;
        [DataMember(Name = "displayVersion")] public string DisplayVersion;
        /// <summary>1 Workstation, 2 Domaenencontroller, 3 Server.</summary>
        [DataMember(Name = "produktTyp")] public int ProduktTyp;
        [DataMember(Name = "x64")] public bool X64;
        [DataMember(Name = "letzterStart")] public string LetzterStartUtc;

        public bool IstHome { get { return Edition != null && (Edition == "Core" || Edition.StartsWith("Core")); } }
        public bool IstWindows11 { get { return Build >= 22000; } }
    }

    // ------------------------------------------------------------------ Hardware

    [DataContract]
    public class Hardware
    {
        [DataMember(Name = "hersteller")] public string Hersteller;
        [DataMember(Name = "modell")] public string Modell;
        /// <summary>Win32_ComputerSystem.PCSystemType: 1 Desktop, 2 Mobile, 8 Slate.</summary>
        [DataMember(Name = "systemTyp")] public int SystemTyp;
        [DataMember(Name = "board")] public string Board;
        [DataMember(Name = "biosVersion")] public string BiosVersion;
        [DataMember(Name = "biosDatum")] public string BiosDatumUtc;
        /// <summary>null = nicht ermittelbar; true = UEFI; false = Legacy-BIOS.</summary>
        [DataMember(Name = "uefi")] public bool? Uefi;
        [DataMember(Name = "cpu")] public string Cpu;
        [DataMember(Name = "kerne")] public int Kerne;
        [DataMember(Name = "threads")] public int Threads;
        /// <summary>Win32_ComputerSystem.TotalPhysicalMemory: fuer das Betriebssystem sichtbar (Bezugsgroesse der Zaehler).</summary>
        [DataMember(Name = "ramGesamtKB")] public long RamGesamtKB;
        /// <summary>Summe der Module (verbaut); null = Module nicht lesbar. Kann ueber RamGesamtKB liegen (Grafikspeicher, Reserven).</summary>
        [DataMember(Name = "ramInstalliertKB")] public long? RamInstalliertKB;
        [DataMember(Name = "ramModule")] public List<RamModul> RamModule = new List<RamModul>();
        [DataMember(Name = "gpus")] public List<Gpu> Gpus = new List<Gpu>();
        /// <summary>Win32_SystemEnclosure.ChassisTypes; 8-14 und 30-32 sind mobil.</summary>
        [DataMember(Name = "gehaeuseTypen")] public List<int> GehaeuseTypen = new List<int>();
        [DataMember(Name = "akku")] public Akku Akku;

        /// <summary>
        /// SMBIOS-Gehaeusetypen (Win32_SystemEnclosure.ChassisTypes, DMTF System Enclosure Type):
        /// mobil sind 8 Portable, 9 Laptop, 10 Notebook, 11 Hand Held, 12 Docking Station, 14 Sub Notebook,
        /// 30 Tablet, 31 Convertible, 32 Detachable. Alles andere mit Netzteil zaehlt als Desktop: 3 bis 7,
        /// 13 All-in-One, 15 Space-saving, 16 Lunch Box, 17 Main Server Chassis, 23 Rack Mount, 24 Sealed-case,
        /// 34 Embedded, 35 Mini PC, 36 Stick PC. Unbekannte Werte (1, 2, 25 bis 29 usw.) entscheiden nichts.
        /// An EINER Stelle, damit Sammler und Regeln dieselbe Einteilung nehmen.
        /// </summary>
        public static readonly int[] GehaeuseMobil = { 8, 9, 10, 11, 12, 14, 30, 31, 32 };
        public static readonly int[] GehaeuseDesktop = { 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 34, 35, 36 };

        public bool GehaeuseSagtMobil { get { foreach (int t in GehaeuseTypen) if (Array.IndexOf(GehaeuseMobil, t) >= 0) return true; return false; } }
        public bool GehaeuseSagtDesktop { get { foreach (int t in GehaeuseTypen) if (Array.IndexOf(GehaeuseDesktop, t) >= 0) return true; return false; } }

        /// <summary>Laptop-Erkennung aus drei Quellen; null, wenn sie sich widersprechen.</summary>
        public bool? IstMobil
        {
            get
            {
                bool gehaeuseMobil = GehaeuseSagtMobil, gehaeuseDesktop = GehaeuseSagtDesktop;
                bool typMobil = SystemTyp == 2;
                bool akkuDa = Akku != null && Akku.Vorhanden == true;
                int ja = (gehaeuseMobil ? 1 : 0) + (typMobil ? 1 : 0) + (akkuDa ? 1 : 0);
                int nein = (gehaeuseDesktop ? 1 : 0) + (SystemTyp == 1 ? 1 : 0) + (Akku != null && Akku.Vorhanden == false ? 1 : 0);
                if (ja > 0 && nein == 0) return true;
                if (nein > 0 && ja == 0) return false;
                return null;
            }
        }
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (RamModule == null) RamModule = new List<RamModul>();
            if (Gpus == null) Gpus = new List<Gpu>();
            if (GehaeuseTypen == null) GehaeuseTypen = new List<int>();
        }

    }

    [DataContract]
    public class RamModul
    {
        [DataMember(Name = "kapazitaetBytes")] public long KapazitaetBytes;
        /// <summary>MT/s (die Doku sagt Nanosekunden, die Live-Klasse liefert MT/s).</summary>
        [DataMember(Name = "takt")] public int Takt;
        /// <summary>SMBIOSMemoryType: 26 DDR4, 34 DDR5.</summary>
        [DataMember(Name = "typSmbios")] public int TypSmbios;
    }

    [DataContract]
    public class Gpu
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "pnpId")] public string PnpId;
        [DataMember(Name = "treiberVersion")] public string TreiberVersion;
        [DataMember(Name = "treiberDatum")] public string TreiberDatumUtc;
        [DataMember(Name = "vramBytes")] public long? VramBytes;
        [DataMember(Name = "problemCode")] public int ProblemCode;
    }

    [DataContract]
    public class Akku
    {
        [DataMember(Name = "vorhanden")] public bool? Vorhanden;
        [DataMember(Name = "designMWh")] public int? DesignMWh;
        [DataMember(Name = "vollMWh")] public int? VollMWh;
        [DataMember(Name = "ladungPct")] public int? LadungPct;
    }

    // ------------------------------------------------------------------ Geraete

    [DataContract]
    public class Geraet
    {
        [DataMember(Name = "instanzId")] public string InstanzId;
        [DataMember(Name = "klasseGuid")] public string KlasseGuid;
        /// <summary>PNPClass, z. B. "Display", "Net". Feste englische Werte.</summary>
        [DataMember(Name = "klasse")] public string Klasse;
        /// <summary>Anzeigename, lokalisiert. Nur Anzeige.</summary>
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "present")] public bool Present;
        /// <summary>CM_PROB_* (0 = kein Problem, 22 = deaktiviert, 43 = Ausfall gemeldet).</summary>
        [DataMember(Name = "problemCode")] public int ProblemCode;
        [DataMember(Name = "problemStatus")] public long? ProblemStatus;
        [DataMember(Name = "devNodeStatus")] public long? DevNodeStatus;
        /// <summary>DEVPKEY_Device_Parent. Nie aus dem Instanzpfad ableiten.</summary>
        [DataMember(Name = "parent")] public string Parent;
        [DataMember(Name = "treiberVersion")] public string TreiberVersion;
        [DataMember(Name = "treiberDatum")] public string TreiberDatumUtc;
        [DataMember(Name = "treiberAnbieter")] public string TreiberAnbieter;
        [DataMember(Name = "treiberInf")] public string TreiberInf;
        /// <summary>CONFIGFLAG_DISABLED = 1.</summary>
        [DataMember(Name = "configFlags")] public long? ConfigFlags;
    }

    // ------------------------------------------------------------------ Datentraeger

    [DataContract]
    public class Datentraeger
    {
        [DataMember(Name = "objectId")] public string ObjectId;
        [DataMember(Name = "nummer")] public int? Nummer;
        [DataMember(Name = "name")] public string Name;
        /// <summary>MSFT_PhysicalDisk.MediaType: 0 unbekannt, 3 HDD, 4 SSD, 5 SCM.</summary>
        [DataMember(Name = "medienTyp")] public int MedienTyp;
        /// <summary>BusType: 7 USB, 11 SATA, 17 NVMe.</summary>
        [DataMember(Name = "busTyp")] public int BusTyp;
        /// <summary>HealthStatus: 0 gesund, 1 Warnung, 2 ungesund, 5 unbekannt.</summary>
        [DataMember(Name = "health")] public int Health;
        [DataMember(Name = "opStatus")] public List<int> OpStatus = new List<int>();
        [DataMember(Name = "groesseBytes")] public long GroesseBytes;
        [DataMember(Name = "firmware")] public string Firmware;
        [DataMember(Name = "zaehler")] public Zaehler Zaehler;
        [DataMember(Name = "nvme")] public NvmeLog Nvme;
        [DataMember(Name = "tempWarn")] public int? TempWarn;
        [DataMember(Name = "tempKritisch")] public int? TempKritisch;
        /// <summary>Registry DisableDeleteNotification: true = TRIM-Benachrichtigung von Windows aktiv (0), false = abgeschaltet (1), null = nicht lesbar. Gilt systemweit.</summary>
        [DataMember(Name = "trim")] public bool? Trim;
        /// <summary>StorageDeviceTrimProperty.TrimEnabled des Geraets; false bei SSDs hinter USB-Bruecken oder RAID-Controllern (kein Registry-Rat!), null = nicht abgefragt.</summary>
        [DataMember(Name = "trimGeraet")] public bool? TrimGeraet;

        public bool IstSsd { get { return MedienTyp == 4 || MedienTyp == 5 || BusTyp == 17; } }
        public bool IstHdd { get { return MedienTyp == 3; } }
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (OpStatus == null) OpStatus = new List<int>();
        }

    }

    /// <summary>MSFT_StorageReliabilityCounter (nur erhoeht). null-Felder = vom Geraet nicht geliefert.</summary>
    [DataContract]
    public class Zaehler
    {
        [DataMember(Name = "wear")] public int? Wear;
        [DataMember(Name = "temp")] public int? Temp;
        [DataMember(Name = "tempMax")] public int? TempMax;
        [DataMember(Name = "stunden")] public int? Stunden;
        [DataMember(Name = "leseFehlerUnkorr")] public long? LeseFehlerUnkorr;
        [DataMember(Name = "leseFehlerGesamt")] public long? LeseFehlerGesamt;
        [DataMember(Name = "schreibFehlerUnkorr")] public long? SchreibFehlerUnkorr;
    }

    /// <summary>NVMe-Gesundheitslog (IOCTL, geht ohne Rechte).</summary>
    [DataContract]
    public class NvmeLog
    {
        [DataMember(Name = "criticalWarning")] public int CriticalWarning;
        [DataMember(Name = "spare")] public int Spare;
        [DataMember(Name = "spareSchwelle")] public int SpareSchwelle;
        [DataMember(Name = "usedPct")] public int UsedPct;
        [DataMember(Name = "tempC")] public int TempC;
        [DataMember(Name = "stunden")] public long Stunden;
        [DataMember(Name = "unsafeShutdowns")] public long UnsafeShutdowns;
        [DataMember(Name = "medienFehler")] public long MedienFehler;
    }

    [DataContract]
    public class Volume
    {
        /// <summary>"C" ohne Doppelpunkt; null bei versteckten Partitionen.</summary>
        [DataMember(Name = "buchstabe")] public string Buchstabe;
        [DataMember(Name = "pfad")] public string Pfad;
        [DataMember(Name = "dateisystem")] public string Dateisystem;
        /// <summary>DriveType: 3 fest, 2 wechselbar, 4 Netz.</summary>
        [DataMember(Name = "typ")] public int Typ;
        [DataMember(Name = "groesseBytes")] public long GroesseBytes;
        [DataMember(Name = "freiBytes")] public long FreiBytes;
        [DataMember(Name = "health")] public int? Health;
        /// <summary>MSFT_Volume.OperationalStatus: 0xD00D Scan, 0xD00E Spot-Fix, 0xD00F Reparatur noetig.</summary>
        [DataMember(Name = "opStatus")] public List<int> OpStatus = new List<int>();
        /// <summary>null = nicht lesbar (braucht Rechte).</summary>
        [DataMember(Name = "dirty")] public bool? Dirty;
        [DataMember(Name = "gptTyp")] public string GptTyp;
        [DataMember(Name = "versteckt")] public bool Versteckt;
        [DataMember(Name = "system")] public bool System;
        [DataMember(Name = "boot")] public bool Boot;
        [DataMember(Name = "label")] public string Label;

        public bool IstNutzerVolume { get { return !string.IsNullOrEmpty(Buchstabe) && Typ == 3 && !Versteckt; } }
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (OpStatus == null) OpStatus = new List<int>();
        }

    }

    // ------------------------------------------------------------------ Ereignisse

    [DataContract]
    public class Ereignisse
    {
        [DataMember(Name = "beginnSystem")] public string BeginnSystemUtc;
        [DataMember(Name = "beginnApplication")] public string BeginnApplicationUtc;
        [DataMember(Name = "tage")] public int Tage = Regeln.Schwellen.EreignisTage;
        [DataMember(Name = "eintraege")] public List<Ereignis> Eintraege = new List<Ereignis>();
        /// <summary>Logs, die wegen fehlender Rechte nicht lesbar waren.</summary>
        [DataMember(Name = "gesperrt")] public List<string> Gesperrt = new List<string>();

        public IEnumerable<Ereignis> Von(string anbieter, params int[] ids)
        {
            foreach (var e in Eintraege)
            {
                if (!string.Equals(e.Anbieter, anbieter, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (ids == null || ids.Length == 0) { yield return e; continue; }
                foreach (int id in ids) if (e.Id == id) { yield return e; break; }
            }
        }
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Eintraege == null) Eintraege = new List<Ereignis>();
            if (Gesperrt == null) Gesperrt = new List<string>();
            // Ein handgeschriebenes Testbild ohne "tage" bekaeme sonst 0 und jeder Zeitraum waere leer.
            if (Tage <= 0) Tage = Regeln.Schwellen.EreignisTage;
        }

    }

    [DataContract]
    public class Ereignis
    {
        [DataMember(Name = "log")] public string Log;
        [DataMember(Name = "anbieter")] public string Anbieter;
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "zeit")] public string ZeitUtc;
        /// <summary>EventData ueber den Feldnamen; unbenannte Felder heissen "0", "1", ...</summary>
        [DataMember(Name = "felder")] public Dictionary<string, string> Felder = new Dictionary<string, string>();

        public string Feld(string name)
        {
            string v;
            return Felder != null && Felder.TryGetValue(name, out v) ? v : null;
        }

        public long FeldZahl(string name, long fallback)
        {
            string v = Feld(name);
            if (v == null) return fallback;
            long n;
            if (long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n)) return n;
            if (v.StartsWith("0x") && long.TryParse(v.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Felder == null) Felder = new Dictionary<string, string>();
        }

    }

    [DataContract]
    public class Zuverlaessigkeit
    {
        [DataMember(Name = "index")] public double? Index;
        [DataMember(Name = "zeit")] public string ZeitUtc;
    }

    // ------------------------------------------------------------------ Autostart

    [DataContract]
    public class Autostart
    {
        [DataMember(Name = "eintraege")] public List<AutostartEintrag> Eintraege = new List<AutostartEintrag>();
        [DataMember(Name = "aufgaben")] public List<Aufgabe> Aufgaben = new List<Aufgabe>();
        [DataMember(Name = "aufgabenVollstaendig")] public bool AufgabenVollstaendig;
        [DataMember(Name = "dienste")] public List<Dienst> Dienste = new List<Dienst>();
        [DataMember(Name = "shell")] public string Shell;
        [DataMember(Name = "userinit")] public string Userinit;
        [DataMember(Name = "appInitDlls")] public string AppInitDlls;
        /// <summary>Windows\AppInit_DLLs aus der 32-Bit-Sicht (WOW6432Node); null = nicht gelesen.</summary>
        [DataMember(Name = "appInitDlls32")] public string AppInitDlls32;
        /// <summary>LoadAppInit_DLLs (REG_DWORD): true = Mechanismus an, false = aus (Vorgabe seit Windows 8), null = nicht gelesen.</summary>
        [DataMember(Name = "loadAppInitDlls")] public bool? LoadAppInitDlls;
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Eintraege == null) Eintraege = new List<AutostartEintrag>();
            if (Aufgaben == null) Aufgaben = new List<Aufgabe>();
            if (Dienste == null) Dienste = new List<Dienst>();
        }

    }

    [DataContract]
    public class AutostartEintrag
    {
        /// <summary>"HKLM\Run", "HKCU\Run", "HKLM\Run32", "Startup", "CommonStartup", ...</summary>
        [DataMember(Name = "quelle")] public string Quelle;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "befehl")] public string Befehl;
        /// <summary>null = kein StartupApproved-Eintrag (gilt als aktiv).</summary>
        [DataMember(Name = "aktiviert")] public bool? Aktiviert;
        [DataMember(Name = "deaktiviertSeit")] public string DeaktiviertSeitUtc;
        [DataMember(Name = "signierer")] public string Signierer;
        /// <summary>true = Microsoft-signiert, false = fremd oder unsigniert, null = nicht pruefbar (Datei fehlt, .lnk, Host wie rundll32 mit fremdem Ziel).</summary>
        [DataMember(Name = "microsoft")] public bool? Microsoft;
        /// <summary>false = die gestartete Datei existiert nicht (Rest einer Deinstallation, startet nichts); null = nicht geprueft.</summary>
        [DataMember(Name = "dateiVorhanden")] public bool? DateiVorhanden;
    }

    [DataContract]
    public class Aufgabe
    {
        [DataMember(Name = "pfad")] public string Pfad;
        [DataMember(Name = "autor")] public string Autor;
        /// <summary>TASK_STATE: 1 deaktiviert, 3 bereit, 4 laeuft.</summary>
        [DataMember(Name = "zustand")] public int Zustand;
        [DataMember(Name = "logon")] public bool Logon;
        [DataMember(Name = "boot")] public bool Boot;
        [DataMember(Name = "letzterLauf")] public string LetzterLaufUtc;
        [DataMember(Name = "letztesErgebnis")] public long? LetztesErgebnis;
        /// <summary>true = Aufgabe dieses Programms selbst (WindowsWartung-Autostart, -AutoWartung); zaehlt nie als Fremd-Autostart.</summary>
        [DataMember(Name = "eigen")] public bool Eigen;
    }

    [DataContract]
    public class Dienst
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "anzeige")] public string Anzeige;
        /// <summary>Boot, System, Auto, Manual, Disabled, Unknown (feste englische Werte).</summary>
        [DataMember(Name = "startart")] public string Startart;
        [DataMember(Name = "zustand")] public string Zustand;
        [DataMember(Name = "pfad")] public string Pfad;
        [DataMember(Name = "verzoegert")] public bool Verzoegert;
        [DataMember(Name = "signierer")] public string Signierer;
        [DataMember(Name = "microsoft")] public bool? Microsoft;
    }

    // ------------------------------------------------------------------ Leistung

    [DataContract]
    public class Leistung
    {
        [DataMember(Name = "ramGesamtKB")] public long RamGesamtKB;
        [DataMember(Name = "ramFreiKB")] public long RamFreiKB;
        /// <summary>Mehrere Messungen im Abstand; eine allein ist eine Momentaufnahme.</summary>
        [DataMember(Name = "verfuegbarMB")] public List<int> VerfuegbarMB = new List<int>();
        [DataMember(Name = "commitPct")] public List<int> CommitPct = new List<int>();
        /// <summary>Abstand der Messungen in Millisekunden (0 = eine Messung oder unbekannt). Unter 20 s ist die Serie eine Momentaufnahme (Konzept 4.3).</summary>
        [DataMember(Name = "abstandMs")] public int AbstandMs;
        /// <summary>Woher die Speicherwerte kommen: "perf" (PerformanceCounter) oder "wmi" (Win32_PerfFormattedData_PerfOS_Memory); null = keine.</summary>
        [DataMember(Name = "speicherQuelle")] public string SpeicherQuelle;
        [DataMember(Name = "auslagerungMB")] public int? AuslagerungMB;
        [DataMember(Name = "prozesse")] public List<Prozess> Prozesse = new List<Prozess>();
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (VerfuegbarMB == null) VerfuegbarMB = new List<int>();
            if (CommitPct == null) CommitPct = new List<int>();
            if (Prozesse == null) Prozesse = new List<Prozess>();
        }

    }

    [DataContract]
    public class Prozess
    {
        [DataMember(Name = "pid")] public int Pid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "pfad")] public string Pfad;
        [DataMember(Name = "arbeitsspeicherBytes")] public long ArbeitsspeicherBytes;
        [DataMember(Name = "cpuPct")] public double CpuPct;
    }

    // ------------------------------------------------------------------ Windows Update

    [DataContract]
    public class WindowsUpdate
    {
        [DataMember(Name = "verlauf")] public List<UpdateEintrag> Verlauf = new List<UpdateEintrag>();
        [DataMember(Name = "letzteSuche")] public string LetzteSucheUtc;
        [DataMember(Name = "letzteInstallation")] public string LetzteInstallationUtc;
        [DataMember(Name = "verborgen")] public List<UpdateKennung> Verborgen = new List<UpdateKennung>();
        [DataMember(Name = "ausstehend")] public List<UpdateKennung> Ausstehend = new List<UpdateKennung>();
        /// <summary>Aus dem System-Log (WindowsUpdateClient 20): Fehlschlaege, die der COM-Verlauf nicht kennt.</summary>
        [DataMember(Name = "fehlschlaegeLog")] public List<UpdateFehler> FehlschlaegeLog = new List<UpdateFehler>();
        [DataMember(Name = "erfolgeLog")] public List<UpdateFehler> ErfolgeLog = new List<UpdateFehler>();
        [DataMember(Name = "neustartWu")] public bool NeustartWu;
        [DataMember(Name = "neustartCbs")] public bool NeustartCbs;
        [DataMember(Name = "pendingRenames")] public bool PendingRenames;
        [DataMember(Name = "letztesSicherheitsupdate")] public string LetztesSicherheitsupdateUtc;
        [DataMember(Name = "verwaltet")] public bool? Verwaltet;
        [DataMember(Name = "dienste")] public Dictionary<string, string> Dienste = new Dictionary<string, string>();
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Verlauf == null) Verlauf = new List<UpdateEintrag>();
            if (Verborgen == null) Verborgen = new List<UpdateKennung>();
            if (Ausstehend == null) Ausstehend = new List<UpdateKennung>();
            if (FehlschlaegeLog == null) FehlschlaegeLog = new List<UpdateFehler>();
            if (ErfolgeLog == null) ErfolgeLog = new List<UpdateFehler>();
            if (Dienste == null) Dienste = new Dictionary<string, string>();
        }

    }

    [DataContract]
    public class UpdateEintrag
    {
        [DataMember(Name = "zeit")] public string ZeitUtc;
        [DataMember(Name = "titel")] public string Titel;
        /// <summary>OperationResultCode: 2 Erfolg, 3 Erfolg mit Fehlern, 4 Fehlschlag, 5 abgebrochen.</summary>
        [DataMember(Name = "ergebnis")] public int Ergebnis;
        [DataMember(Name = "hresult")] public long HResult;
        [DataMember(Name = "updateId")] public string UpdateId;
        [DataMember(Name = "revision")] public int Revision;
        /// <summary>IUpdateHistoryEntry.Operation: 1 Installation, 2 Deinstallation; 0 = unbekannt.</summary>
        [DataMember(Name = "vorgang")] public int Vorgang;
        /// <summary>IUpdateHistoryEntry.ServiceID (GUID des Update-Dienstes; Store: 855e8a7c-ecb4-4ca3-b045-1dfa50104289). null = unbekannt.</summary>
        [DataMember(Name = "dienstId")] public string DienstId;
    }

    [DataContract]
    public class UpdateKennung
    {
        [DataMember(Name = "updateId")] public string UpdateId;
        [DataMember(Name = "revision")] public int Revision;
        [DataMember(Name = "titel")] public string Titel;
        /// <summary>UpdateType: 1 Software, 2 Treiber.</summary>
        [DataMember(Name = "typ")] public int Typ;
    }

    [DataContract]
    public class UpdateFehler
    {
        [DataMember(Name = "zeit")] public string ZeitUtc;
        [DataMember(Name = "updateId")] public string UpdateId;
        [DataMember(Name = "titel")] public string Titel;
        [DataMember(Name = "fehlerCode")] public long FehlerCode;
        /// <summary>serviceGuid aus WindowsUpdateClient 19/20 (Store: 855e8a7c-ecb4-4ca3-b045-1dfa50104289); null = Feld fehlt.</summary>
        [DataMember(Name = "dienstId")] public string DienstId;
    }

    // ------------------------------------------------------------------ Netz

    [DataContract]
    public class Netz
    {
        [DataMember(Name = "adapter")] public List<Adapter> Adapter = new List<Adapter>();
        [DataMember(Name = "ipAdressen")] public List<IpAdresse> IpAdressen = new List<IpAdresse>();
        [DataMember(Name = "gateways")] public List<string> Gateways = new List<string>();
        /// <summary>true = es gibt eine Standardroute ohne NextHop (On-Link, z. B. PPPoE oder Mobilfunk): kein Router, aber ein Weg ins Internet; null = Routen nicht gelesen.</summary>
        [DataMember(Name = "standardrouteOnLink")] public bool? StandardrouteOnLink;
        [DataMember(Name = "dns")] public List<DnsKonfig> Dns = new List<DnsKonfig>();
        [DataMember(Name = "profile")] public List<Profil> Profile = new List<Profil>();
        [DataMember(Name = "proxy")] public Proxy Proxy = new Proxy();
        [DataMember(Name = "hosts")] public List<HostsZeile> Hosts = new List<HostsZeile>();
        /// <summary>Redaktion: so viele hosts-Zeilen ohne Treffer wurden aus der Aufzeichnung entfernt.</summary>
        [DataMember(Name = "hostsEntfernt")] public int HostsEntfernt;
        /// <summary>Tcpip\Parameters\DataBasePath, wenn er vom Standard (%SystemRoot%\System32\drivers\etc) abweicht; sonst null.</summary>
        [DataMember(Name = "hostsPfadAbweichend")] public string HostsPfadAbweichend;
        [DataMember(Name = "sonden")] public Sonden Sonden = new Sonden();
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Adapter == null) Adapter = new List<Adapter>();
            if (IpAdressen == null) IpAdressen = new List<IpAdresse>();
            if (Gateways == null) Gateways = new List<string>();
            if (Dns == null) Dns = new List<DnsKonfig>();
            if (Profile == null) Profile = new List<Profil>();
            if (Proxy == null) Proxy = new Proxy();
            if (Hosts == null) Hosts = new List<HostsZeile>();
            if (Sonden == null) Sonden = new Sonden();
        }

    }

    [DataContract]
    public class Adapter
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "beschreibung")] public string Beschreibung;
        [DataMember(Name = "pnpId")] public string PnpId;
        [DataMember(Name = "index")] public int Index;
        /// <summary>InterfaceOperationalStatus: 1 Up, 2 Down, 6 nicht vorhanden.</summary>
        [DataMember(Name = "opStatus")] public int OpStatus;
        /// <summary>MediaConnectState: 0 unbekannt, 1 verbunden, 2 getrennt.</summary>
        [DataMember(Name = "medien")] public int Medien;
        [DataMember(Name = "empfangBitS")] public long? EmpfangBitS;
        [DataMember(Name = "physisch")] public bool Physisch;
        [DataMember(Name = "virtuell")] public bool Virtuell;
        [DataMember(Name = "treiberVersion")] public string TreiberVersion;
        [DataMember(Name = "treiberDatum")] public string TreiberDatumUtc;
        [DataMember(Name = "dhcp")] public bool? Dhcp;
    }

    [DataContract]
    public class IpAdresse
    {
        [DataMember(Name = "adapterIndex")] public int AdapterIndex;
        [DataMember(Name = "adresse")] public string Adresse;
        /// <summary>2 IPv4, 23 IPv6.</summary>
        [DataMember(Name = "familie")] public int Familie;
        /// <summary>PrefixOrigin: 1 manuell, 2 WellKnown (APIPA), 3 DHCP, 4 Router.</summary>
        [DataMember(Name = "herkunft")] public int Herkunft;
        /// <summary>AddressState: 4 bevorzugt.</summary>
        [DataMember(Name = "zustand")] public int Zustand;
    }

    [DataContract]
    public class DnsKonfig
    {
        [DataMember(Name = "adapterIndex")] public int AdapterIndex;
        [DataMember(Name = "familie")] public int Familie;
        [DataMember(Name = "server")] public List<string> Server = new List<string>();
        /// <summary>Aus der Registry: NameServer gesetzt = statisch. null = nicht ermittelbar.</summary>
        [DataMember(Name = "statisch")] public bool? Statisch;
        [DataMember(Name = "dhcpServer")] public List<string> DhcpServer = new List<string>();
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (Server == null) Server = new List<string>();
            if (DhcpServer == null) DhcpServer = new List<string>();
        }

    }

    [DataContract]
    public class Profil
    {
        [DataMember(Name = "name")] public string Name;
        /// <summary>0 oeffentlich, 1 privat, 2 Domaene.</summary>
        [DataMember(Name = "kategorie")] public int Kategorie;
        /// <summary>IPv4Connectivity: 4 Internet.</summary>
        [DataMember(Name = "ipv4")] public int Ipv4;
        [DataMember(Name = "ipv6")] public int Ipv6;
    }

    [DataContract]
    public class Proxy
    {
        [DataMember(Name = "winInetAktiv")] public bool? WinInetAktiv;
        [DataMember(Name = "winInetServer")] public string WinInetServer;
        [DataMember(Name = "pacUrl")] public string PacUrl;
        [DataMember(Name = "autoDetect")] public bool? AutoDetect;
        /// <summary>WINHTTP_ACCESS_TYPE: 1 kein Proxy, 3 benannter Proxy.</summary>
        [DataMember(Name = "winHttpTyp")] public int? WinHttpTyp;
        [DataMember(Name = "winHttpServer")] public string WinHttpServer;
        /// <summary>WinINet-Proxy des ANGEMELDETEN Nutzers (HKEY_USERS\&lt;SID&gt;), wenn das Programm unter einem anderen Konto laeuft; sonst null (dann gilt WinInet* oben).</summary>
        [DataMember(Name = "nutzerWinInetAktiv")] public bool? NutzerWinInetAktiv;
        [DataMember(Name = "nutzerWinInetServer")] public string NutzerWinInetServer;
        [DataMember(Name = "nutzerPacUrl")] public string NutzerPacUrl;
    }

    [DataContract]
    public class HostsZeile
    {
        [DataMember(Name = "ip")] public string Ip;
        [DataMember(Name = "host")] public string Host;
    }

    [DataContract]
    public class Sonden
    {
        /// <summary>null = nicht gemessen; sonst Ergebnis der Stufe.</summary>
        [DataMember(Name = "gateway")] public bool? Gateway;
        [DataMember(Name = "dns")] public bool? Dns;
        /// <summary>Fehlerart bei DNS: "nxdomain", "timeout", "andere".</summary>
        [DataMember(Name = "dnsFehler")] public string DnsFehler;
        [DataMember(Name = "ncsi")] public bool? Ncsi;
        [DataMember(Name = "ncsiInhaltStimmt")] public bool? NcsiInhaltStimmt;
        [DataMember(Name = "tcp443")] public bool? Tcp443;
        [DataMember(Name = "dauerMs")] public int DauerMs;
    }

    // ------------------------------------------------------------------ Sicherheit

    [DataContract]
    public class Sicherheit
    {
        [DataMember(Name = "defender")] public Defender Defender;
        [DataMember(Name = "drittAv")] public List<string> DrittAv = new List<string>();
        /// <summary>WscGetSecurityProviderHealth(WSC_SECURITY_PROVIDER_ANTIVIRUS): 0 GOOD, 1 NOTMONITORED, 2 POOR, 3 SNOOZE; null = nicht abgefragt oder Fehler.</summary>
        [DataMember(Name = "virenschutzGesundheit")] public int? VirenschutzGesundheit;
        /// <summary>BitLocker je Volume (Win32_EncryptableVolume, nur erhoeht); leer nicht erhoeht (fehlerliste: zugriff).</summary>
        [DataMember(Name = "bitlocker")] public List<BitlockerVolume> Bitlocker = new List<BitlockerVolume>();
        [DataMember(Name = "firewall")] public List<FirewallProfil> Firewall = new List<FirewallProfil>();
        [DataMember(Name = "enableLua")] public int? EnableLua;
        [DataMember(Name = "consentAdmin")] public int? ConsentAdmin;
        [DataMember(Name = "secureDesktop")] public int? SecureDesktop;
        /// <summary>"Warn", "Block", "Off" oder null.</summary>
        [DataMember(Name = "smartScreen")] public string SmartScreen;
        [DataMember(Name = "secureBoot")] public bool? SecureBoot;
        [DataMember(Name = "tpmVorhanden")] public bool? TpmVorhanden;
        [DataMember(Name = "tpmVersion")] public int? TpmVersion;
        [DataMember(Name = "konten")] public List<Konto> Konten = new List<Konto>();
        [DataMember(Name = "rdpAn")] public bool? RdpAn;
        [DataMember(Name = "rdpNla")] public bool? RdpNla;
        /// <summary>"richtlinie" (SOFTWARE\Policies\...\Terminal Services) oder "lokal"; null = nicht gelesen.</summary>
        [DataMember(Name = "rdpHerkunft")] public string RdpHerkunft;
        /// <summary>Herkunft von UserAuthentication (NLA): "richtlinie" | "lokal"; null = nicht gelesen. Kann von RdpHerkunft abweichen.</summary>
        [DataMember(Name = "rdpNlaHerkunft")] public string RdpNlaHerkunft;
        /// <summary>Win32_OptionalFeature.InstallState: 1 aktiv, 2 aus, 3 nicht installiert.</summary>
        [DataMember(Name = "smb1Client")] public int? Smb1Client;
        [DataMember(Name = "smb1Server")] public int? Smb1Server;
        /// <summary>MSFT_SmbServerConfiguration.EnableSMB1Protocol: der Server bietet SMB1 wirklich an; null = nicht lesbar.</summary>
        [DataMember(Name = "smb1ServerAktiv")] public bool? Smb1ServerAktiv;
        [DataMember(Name = "werDeaktiviert")] public bool? WerDeaktiviert;
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (DrittAv == null) DrittAv = new List<string>();
            if (Firewall == null) Firewall = new List<FirewallProfil>();
            if (Konten == null) Konten = new List<Konto>();
            if (Bitlocker == null) Bitlocker = new List<BitlockerVolume>();
        }

    }

    [DataContract]
    public class Defender
    {
        [DataMember(Name = "dienstAn")] public bool DienstAn;
        [DataMember(Name = "echtzeit")] public bool Echtzeit;
        [DataMember(Name = "verhalten")] public bool Verhalten;
        /// <summary>65535 = nie.</summary>
        [DataMember(Name = "signaturAlterTage")] public int SignaturAlterTage;
        [DataMember(Name = "schnellscanAlterTage")] public int SchnellscanAlterTage;
        [DataMember(Name = "vollscanAlterTage")] public int VollscanAlterTage;
        [DataMember(Name = "tamper")] public bool Tamper;
        /// <summary>"Normal", "Passive", "EDR Block", "SxS Passive Mode". Feste englische Werte.</summary>
        [DataMember(Name = "modus")] public string Modus;
        [DataMember(Name = "ausschluesseSichtbar")] public bool AusschluesseSichtbar;
        [DataMember(Name = "ausschlussPfade")] public List<string> AusschlussPfade = new List<string>();
        [DataMember(Name = "ausschlussProzesse")] public List<string> AusschlussProzesse = new List<string>();
        [DataMember(Name = "ausschlussEndungen")] public List<string> AusschlussEndungen = new List<string>();
        [DataMember(Name = "funde")] public List<Fund> Funde = new List<Fund>();
        [OnDeserialized]
        void NachDemLesen(StreamingContext ctx)
        {
            if (AusschlussPfade == null) AusschlussPfade = new List<string>();
            if (AusschlussProzesse == null) AusschlussProzesse = new List<string>();
            if (AusschlussEndungen == null) AusschlussEndungen = new List<string>();
            if (Funde == null) Funde = new List<Fund>();
        }

    }

    [DataContract]
    public class Fund
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "schwere")] public int Schwere;
        [DataMember(Name = "kategorie")] public int Kategorie;
        [DataMember(Name = "aktiv")] public bool Aktiv;
        [DataMember(Name = "zeit")] public string ZeitUtc;
    }

    [DataContract]
    public class FirewallProfil
    {
        /// <summary>1 Domaene, 2 privat, 4 oeffentlich.</summary>
        [DataMember(Name = "profil")] public int Profil;
        [DataMember(Name = "an")] public bool An;
        /// <summary>NET_FW_ACTION: 0 Block, 1 Allow; null = nicht gelesen.</summary>
        [DataMember(Name = "eingehend")] public int? Eingehend;
    }

    [DataContract]
    public class Konto
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "rid")] public int Rid;
        [DataMember(Name = "deaktiviert")] public bool Deaktiviert;
        [DataMember(Name = "kennwortNoetig")] public bool KennwortNoetig;
        [DataMember(Name = "admin")] public bool Admin;
        [DataMember(Name = "microsoftKonto")] public bool? MicrosoftKonto;
    }

    // ------------------------------------------------------------------ Systemschutz

    /// <summary>Win32_EncryptableVolume (root\CIMV2\Security\MicrosoftVolumeEncryption), nur erhoeht.</summary>
    [DataContract]
    public class BitlockerVolume
    {
        [DataMember(Name = "buchstabe")] public string Buchstabe;
        /// <summary>GetProtectionStatus: 0 aus, 1 an, 2 unbekannt.</summary>
        [DataMember(Name = "schutz")] public int? Schutz;
        /// <summary>ConversionStatus: 0 vollstaendig entschluesselt, 1 vollstaendig verschluesselt, 2 wird verschluesselt, 3 wird entschluesselt, 4/5 angehalten.</summary>
        [DataMember(Name = "umwandlung")] public int? Umwandlung;
    }

    [DataContract]
    public class Systemschutz
    {
        [DataMember(Name = "diskPercent")] public int? DiskPercent;
        [DataMember(Name = "sessionInterval")] public int? SessionInterval;
        [DataMember(Name = "frequenz")] public int? Frequenz;
        [DataMember(Name = "policyAus")] public bool? PolicyAus;
        /// <summary>true = Systemschutz fuer das Windows-Laufwerk eingeschaltet (RPSessionInterval &gt; 0 und nicht DisableSR); false = aus (haeufige Werkseinstellung); null = nicht ermittelbar.</summary>
        [DataMember(Name = "aktiv")] public bool? Aktiv;
        /// <summary>null = nicht lesbar (braucht Rechte).</summary>
        [DataMember(Name = "punkte")] public List<Punkt> Punkte;
        [DataMember(Name = "schattenBelegt")] public long? SchattenBelegt;
        /// <summary>null = nicht lesbar ODER unbegrenzt (dann SchattenUnbegrenzt = true).</summary>
        [DataMember(Name = "schattenMax")] public long? SchattenMax;
        /// <summary>true = MaxSpace ist UNBOUNDED (18446744073709551615); dann darf kein "von hoechstens 0 GB" entstehen.</summary>
        [DataMember(Name = "schattenUnbegrenzt")] public bool? SchattenUnbegrenzt;
    }

    [DataContract]
    public class Punkt
    {
        [DataMember(Name = "nummer")] public int Nummer;
        [DataMember(Name = "beschreibung")] public string Beschreibung;
        [DataMember(Name = "typ")] public int Typ;
        [DataMember(Name = "zeit")] public string ZeitUtc;
    }

    // ------------------------------------------------------------------ Fehler

    [DataContract]
    public class Fehler
    {
        public const string Zugriff = "zugriff";
        public const string Zeit = "zeit";
        public const string Fehlt = "fehlt";
        public const string Ausnahme = "ausnahme";

        /// <summary>Quellenkennung, z. B. "wmi.storage.reliability", "log.diagnostics-performance".</summary>
        [DataMember(Name = "quelle")] public string Quelle;
        /// <summary>zugriff | zeit | fehlt | ausnahme</summary>
        [DataMember(Name = "art")] public string Art;
        [DataMember(Name = "text")] public string Text;
    }
}
