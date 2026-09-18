using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Hardware (docs/M3-ENTWURF.md, Abschnitt 1): geraet.aktivieren. Anmeldung hier, Arbeit in
        // HardwareMassnahmen. Ob der Nutzer die Frage mit "reparieren" beantwortet hat, prueft der
        // Host (Entscheidungen); der Helfer prueft Parameter und Zustand.
        static partial void Hardware(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("geraet.aktivieren", "Gerät wieder einschalten", 2,
                HardwareMassnahmen.Pruefen, HardwareMassnahmen.Aktivieren));
        }
    }

    /// <summary>
    /// Win32_PnPEntity.Enable() auf der Instanz mit PNPDeviceID = instanzId (Konzept 4.1): nur,
    /// wenn das Geraet gerade Code 22 (abgeschaltet) meldet; rebootNeeded wird als Wert neustart
    /// und als Zeile gemeldet. Der Rueckweg ruft Disable() auf derselben Instanz.
    /// </summary>
    internal static class HardwareMassnahmen
    {
        const string Cimv2 = @"root\cimv2";
        const int LeseMs = 30000;
        const int SchaltMs = 2 * 60 * 1000;

        /// <summary>Problemcode "Geraet abgeschaltet" (CM_PROB_DISABLED), wie kern/Regeln/Hardware.cs.</summary>
        public const int CodeAbgeschaltet = 22;

        /// <summary>
        /// PnP-Instanz-Id (PCI\VEN_8086&amp;DEV_...\3&amp;11583659&amp;0&amp;C8, USB\VID_...\5&amp;..., SWD\MMDEVAPI\{...}.{...}).
        /// Die Zeichenmenge (1 bis 400 Zeichen aus Buchstaben, Ziffern und \ &amp; _ { } . # ( ) + , = @ ~ ! -
        /// Leerzeichen; kein doppelter, kein fuehrender oder abschliessender Backslash) fuehrt der Kern in
        /// Regeln.Hardware.InstanzIdZulaessig; die Regel haengt geraet.aktivieren nur an Ids, die hier
        /// auch durchkommen. Sonst null. In der WQL-Bedingung steht die Id nur ueber Wmi2.Wql maskiert.
        /// </summary>
        public static string InstanzIdNorm(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            return Kern.Regeln.Hardware.InstanzIdZulaessig(t) ? t : null;
        }

        public static string Pruefen(PlanSchritt s)
        {
            return InstanzIdNorm(s.Wert("instanzId")) == null
                ? "Ungültige Instanz-Id „" + Massnahmenbau.Kurz(s.Wert("instanzId")) + "“: erlaubt sind bis zu 400 Zeichen aus Buchstaben, Ziffern und \\ & _ { } . # ( ) + , = @ ~ ! -."
                : null;
        }

        public sealed class GeraetStand
        {
            public bool Gefunden;
            public string Name;
            public int Code;          // ConfigManagerErrorCode
            public bool Vorhanden;    // Present
        }

        public static GeraetStand Lesen(string instanzId)
        {
            ManagementObject mo = null;
            try
            {
                mo = Wmi2.Erste(Cimv2, "SELECT Name, ConfigManagerErrorCode, Present FROM Win32_PnPEntity WHERE PNPDeviceID='" + Wmi2.Wql(instanzId) + "'", LeseMs);
                if (mo == null) return new GeraetStand { Gefunden = false };
                return new GeraetStand
                {
                    Gefunden = true,
                    Name = Sammler.Quellen.Wmi.Str(mo, "Name"),
                    Code = Sammler.Quellen.Wmi.Ganz(mo, "ConfigManagerErrorCode") ?? -1,
                    Vorhanden = Sammler.Quellen.Wmi.Wahr(mo, "Present") ?? true,
                };
            }
            finally { if (mo != null) mo.Dispose(); }
        }

        public sealed class SchaltErgebnis
        {
            public long? Rueckgabe;
            public bool Neustart;
        }

        /// <summary>Enable() oder Disable() auf Win32_PnPEntity PNPDeviceID='instanzId'; liefert ReturnValue und rebootNeeded.</summary>
        public static SchaltErgebnis Schalten(string instanzId, bool einschalten)
        {
            using (ManagementBaseObject raus = Wmi2.InstanzVoll(SchaltMs, Cimv2, "Win32_PnPEntity", "PNPDeviceID='" + Wmi2.Wql(instanzId) + "'", einschalten ? "Enable" : "Disable"))
            {
                var e = new SchaltErgebnis();
                if (raus != null)
                {
                    try { e.Rueckgabe = Wmi2.Ganz(raus["ReturnValue"]); } catch (ManagementException) { }
                    e.Neustart = Sammler.Quellen.Wmi.Wahr(raus, "rebootNeeded") ?? false;
                }
                return e;
            }
        }

        public static string Anzeigename(GeraetStand g, string instanzId)
        {
            return string.IsNullOrWhiteSpace(g.Name) ? instanzId : g.Name.Trim();
        }

        public static void Aktivieren(PlanSchritt s, Ausfuehrungskontext k)
        {
            const string kennung = "geraet.aktivieren";
            string id = InstanzIdNorm(s.Wert("instanzId"));
            GeraetStand stand = Lesen(id);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Ein Gerät mit der Instanz-Id „" + id + "“ ist nicht (mehr) vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string name = Anzeigename(stand, id);
            if (stand.Code == 0)
            {
                LeistungMassnahmen.SchonSo(k, kennung, "„" + name + "“ läuft schon (Problemcode 0); es wurde nichts geändert.");
                return;
            }
            if (stand.Code != CodeAbgeschaltet)
            {
                k.Schreibe("   „" + name + "“ meldet Problemcode " + stand.Code + ", nicht 22 (abgeschaltet); Einschalten hilft hier nicht.", "bad");
                Katalog.Problem(k);
                return;
            }
            k.Schreibe("   „" + name + "“ ist abgeschaltet (Problemcode 22" + (stand.Vorhanden ? "" : ", nicht angeschlossen") + ").", "dim");

            Sicherungsdatei.Schreiben(k, kennung, Sicherungsdatei.Werte("instanzId", id),
                Sicherungsdatei.Werte("code", stand.Code.ToString(CultureInfo.InvariantCulture), "name", name));

            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI Win32_PnPEntity(" + id + ").Enable()", "dim");
                Nachweise.Trocken(k, kennung, "„" + name + "“ würde eingeschaltet (Win32_PnPEntity.Enable).");
                return;
            }
            SchaltErgebnis erg = Schalten(id, true);
            if (erg.Rueckgabe != 0)
            {
                Nachweise.NichtBestanden(k, kennung, "Enable meldete " + Fehlertext(erg.Rueckgabe) + "; „" + name + "“ bleibt abgeschaltet.");
                return;
            }
            if (erg.Neustart)
            {
                k.Werte["neustart"] = "1";
                k.Schreibe("   Windows braucht 1 Neustart, damit „" + name + "“ wieder läuft (rebootNeeded).", "warn");
            }
            GeraetStand nach = Lesen(id);
            if (nach.Gefunden && nach.Code == 0)
                Nachweise.Bestanden(k, kennung, "„" + name + "“ ist eingeschaltet (Problemcode 0, vorher 22)" + (erg.Neustart ? "; vollständig wirksam nach 1 Neustart" : "") + ".");
            else if (erg.Neustart)
                Nachweise.Bestanden(k, kennung, "Enable wurde angenommen; „" + name + "“ meldet bis zum Neustart noch Code " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + ".");
            else
                Nachweise.NichtBestanden(k, kennung, "Enable wurde angenommen, „" + name + "“ meldet aber weiter Problemcode " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + ".");
        }

        public static string Fehlertext(long? rc)
        {
            if (!rc.HasValue) return "keinen Rückgabewert";
            return "Fehlercode " + rc.Value.ToString(CultureInfo.InvariantCulture) + " (0x" + unchecked((uint)rc.Value).ToString("X8", CultureInfo.InvariantCulture) + ")";
        }
    }
}
