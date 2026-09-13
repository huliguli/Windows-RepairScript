using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Fuellt s.Systemschutz: Konfiguration des Systemschutzes, Richtlinie, Wiederherstellungspunkte
    /// und Schattenspeicher.
    ///
    /// Ohne Rechte lesbar: SystemRestoreConfig (root\default) und die Registry (Frequenz, Richtlinie,
    /// Ein/Aus). Nur erhoeht: die SystemRestore-Instanzen (die Punkte selbst) und Win32_ShadowStorage -
    /// nicht erhoeht meldet WMI "Initialisierung fehlgeschlagen" bzw. nichts. Deshalb bleibt Punkte null
    /// (nicht "leere Liste"), und die Regel unterscheidet "kein Punkt" (erhoeht gemessen, warn) von
    /// "nicht lesbar" (Fehlend, kein Befund).
    ///
    /// Ein/Aus (Feld aktiv): RPSessionInterval 0 heisst "Systemschutz aus" (haeufige Werkseinstellung
    /// auf Windows 10/11), ebenso DisableSR = 1 im Nicht-Policy-Schluessel. Bei "aus" liefert
    /// SystemRestore erhoeht eine leere Liste - das ist dann kein "fehlender Punkt", den man anlegen
    /// koennte, sondern ein abgeschalteter Schutz; die Regel braucht den Unterschied.
    ///
    /// Die Drossel SystemRestorePointCreationFrequency (Minuten, Voreinstellung 1440) gehoert ins Bild,
    /// weil CreateRestorePoint auch bei Drosselung S_OK liefert (Widerlegungsrunde): eine Massnahme
    /// "Punkt anlegen" muss den Erfolg ueber die SequenceNumber vorher/nachher pruefen.
    /// </summary>
    public static class Systemschutzquelle
    {
        const string DefaultNs = @"root\default";
        const string Cimv2 = @"root\cimv2";

        public static void Erfassen(Systembild s)
        {
            var z = s.Systemschutz;
            // Drei Quellen fuer "aus": WMI RPSessionInterval, Registry RPSessionInterval (Rueckfall,
            // derselbe Wert), Registry DisableSR ohne Policy. Ein "aus" gewinnt immer; "an" braucht ein
            // gelesenes RPSessionInterval > 0, sonst bleibt aktiv null (nicht ermittelbar).
            bool? intervallAn = null;
            bool disableSr = false;

            Sammler.Versuch(s, "wmi.systemrestore.config", () =>
            {
                var liste = Wmi.Abfrage(DefaultNs, "SELECT * FROM SystemRestoreConfig");
                if (liste.Count == 0) { Sammler.Fehler(s, "wmi.systemrestore.config", Fehler.Fehlt, "SystemRestoreConfig lieferte keine Instanz"); return; }
                z.DiskPercent = Wmi.Ganz(liste[0], "DiskPercent");
                z.SessionInterval = Wmi.Ganz(liste[0], "RPSessionInterval");
                if (z.SessionInterval.HasValue) intervallAn = z.SessionInterval.Value > 0;
            });

            Sammler.Versuch(s, "registry.systemrestore", () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                {
                    if (k == null) { Sammler.Fehler(s, "registry.systemrestore", Fehler.Fehlt, "Schlüssel SystemRestore fehlt"); return; }
                    // Fehlt der Wert, gilt die Voreinstellung von 24 Stunden; das Bild traegt dann null,
                    // die Regel sagt "Voreinstellung". 0 heisst: keine Drossel.
                    object v = k.GetValue("SystemRestorePointCreationFrequency");
                    z.Frequenz = v == null ? (int?)null : Convert.ToInt32(v, CultureInfo.InvariantCulture);

                    // Derselbe Wert wie SystemRestoreConfig.RPSessionInterval, als Rueckfall, wenn WMI schwieg.
                    object rp = k.GetValue("RPSessionInterval");
                    if (rp != null && !z.SessionInterval.HasValue)
                    {
                        z.SessionInterval = Convert.ToInt32(rp, CultureInfo.InvariantCulture);
                        intervallAn = z.SessionInterval.Value > 0;
                    }
                    // DisableSR ausserhalb der Policy: Windows setzt 1, wenn der Nutzer den Schutz abschaltet.
                    object d = k.GetValue("DisableSR");
                    if (d != null) disableSr = Convert.ToInt64(d, CultureInfo.InvariantCulture) == 1;
                }
            });

            if (disableSr || intervallAn == false) z.Aktiv = false;
            else if (intervallAn == true) z.Aktiv = true;
            else z.Aktiv = null;

            // Richtlinie DisableSR: 1 = Systemschutz per Richtlinie aus. Fehlt der Schluessel oder der
            // Wert, greift keine Richtlinie (PolicyAus = false) - das ist der Normalfall auf Privat-PCs.
            Sammler.Versuch(s, "registry.systemrestore.policy", () =>
            {
                z.PolicyAus = false;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore"))
                {
                    if (k == null) return;
                    object v = k.GetValue("DisableSR");
                    if (v != null) z.PolicyAus = Convert.ToInt64(v, CultureInfo.InvariantCulture) == 1;
                }
            });

            Sammler.NurErhoeht(s, "wmi.systemrestore.punkte", () =>
            {
                var liste = Wmi.Abfrage(DefaultNs, "SELECT * FROM SystemRestore", 10000);
                var punkte = new List<Punkt>();
                foreach (var mo in liste)
                {
                    punkte.Add(new Punkt
                    {
                        Nummer = Wmi.Ganz(mo, "SequenceNumber") ?? 0,
                        Beschreibung = Wmi.Str(mo, "Description"),
                        Typ = Wmi.Ganz(mo, "RestorePointType") ?? 0,
                        // CreationTime kommt als DMTF-Text ("20260911102030.000000-000"), nicht als Datum.
                        ZeitUtc = Wmi.ZeitUtc(mo, "CreationTime"),
                    });
                }
                // Erhoeht gemessen und leer: das ist "kein Wiederherstellungspunkt", ein Befund. Deshalb
                // hier eine leere Liste statt null.
                z.Punkte = punkte;
            });

            Sammler.NurErhoeht(s, "wmi.shadowstorage", () =>
            {
                var liste = Wmi.Abfrage(Cimv2, "SELECT AllocatedSpace, MaxSpace, UsedSpace FROM Win32_ShadowStorage", 10000);
                if (liste.Count == 0) { Sammler.Fehler(s, "wmi.shadowstorage", Fehler.Fehlt, "Win32_ShadowStorage lieferte keine Instanz"); return; }
                long belegt = 0, max = 0;
                bool unbegrenzt = false, maxBekannt = true;
                foreach (var mo in liste)
                {
                    belegt += Wmi.Lang(mo, "AllocatedSpace") ?? 0;
                    // MaxSpace ist uint64; "vssadmin resize shadowstorage /maxsize=UNBOUNDED" schreibt
                    // 18446744073709551615, das Wmi.Lang als Ueberlauf zu null macht. Aus null darf
                    // keine 0 werden ("von hoechstens 0,0 GB"): der Rohwert entscheidet.
                    if (IstUnbegrenzt(mo)) { unbegrenzt = true; continue; }
                    long? m = Wmi.Lang(mo, "MaxSpace");
                    if (m.HasValue) max += m.Value; else maxBekannt = false;
                }
                z.SchattenBelegt = belegt;
                z.SchattenUnbegrenzt = unbegrenzt ? true : (maxBekannt ? (bool?)false : null);
                z.SchattenMax = unbegrenzt || !maxBekannt ? (long?)null : max;
            });
        }

        /// <summary>MaxSpace ueber long.MaxValue (in der Praxis nur UNBOUNDED = 2^64 - 1) heisst "ohne Obergrenze".</summary>
        static bool IstUnbegrenzt(System.Management.ManagementBaseObject mo)
        {
            try
            {
                object v = mo["MaxSpace"];
                if (v is ulong) return (ulong)v > long.MaxValue;
                if (v is decimal) return (decimal)v > long.MaxValue;
                if (v is string) { ulong u; return ulong.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out u) && u > long.MaxValue; }
                return false;
            }
            catch (Exception) { return false; }
        }
    }
}
