using System.Collections.Generic;

namespace WartungsToolbox.Helfer
{
    /// <summary>
    /// Sammelstelle der Meilenstein-3- und Meilenstein-4-Massnahmen (Stufen 1 bis 3). Jede Datei
    /// unter helfer\Massnahmen\ liefert ihre Eintraege ueber eine statische Methode; hier werden sie
    /// zusammengefuehrt, damit Katalog.cs eine einzige Aufrufstelle hat und die Dateien unabhaengig
    /// voneinander entstehen.
    ///
    /// Rueckweg je Bereich (Vertrag 8.3): Rueckweg.cs verteilt eine gelesene Sicherung ueber
    /// ZurueckNetzBekannt / ZurueckNetzPruefen / ZurueckNetz (Netz.cs) und ZurueckSicherheitBekannt /
    /// ZurueckSicherheitPruefen / ZurueckSicherheit (Sicherheit.cs). Pruefen liefert null oder den
    /// Grund der Ablehnung, Zurueck schreibt Zeilen, nachweis.* und ggf. neustart selbst und liefert
    /// true (zurueckgestellt) oder false (nicht, mit Problem).
    ///
    /// Meilenstein 4 (docs/M4-ENTWURF.md, Abschnitt 3.1): Speicher.cs (Papierkorb), Stabilitaet.cs
    /// (Speichertest), System.cs (DISM-API, Tiefenpruefung, Reparatur, Punkt zurueck) und Programm.cs
    /// (update.anwenden) kommen dazu. Die Teilmethode System teilt ihren Namen mit dem Namensraum
    /// System: innerhalb dieser Klasse bindet der einfache Name System an die Methode, deshalb
    /// steht in den Anmelde-Teilen kein System.-qualifizierter Typ (sonst global::System).
    /// </summary>
    public static partial class MassnahmenM3
    {
        public static IEnumerable<Massnahme> Alle()
        {
            var l = new List<Massnahme>();
            Netz(l);          // helfer\Massnahmen\Netz.cs
            Sicherheit(l);    // helfer\Massnahmen\Sicherheit.cs
            Datentraeger(l);  // helfer\Massnahmen\Datentraeger.cs
            Leistung(l);      // helfer\Massnahmen\Leistung.cs
            Updates(l);       // helfer\Massnahmen\Updates.cs
            Hardware(l);      // helfer\Massnahmen\Hardware.cs
            Speicher(l);      // helfer\Massnahmen\Speicher.cs (M4)
            Stabilitaet(l);   // helfer\Massnahmen\Stabilitaet.cs (M4)
            System(l);        // helfer\Massnahmen\System.cs (M4)
            Programm(l);      // helfer\Massnahmen\Programm.cs (M4)
            Rueckweg(l);      // helfer\Massnahmen\Rueckweg.cs
            Auto(l);          // helfer\Massnahmen\Auto.cs (wartung.auto mit Freigaben)
            return l;
        }

        // Jede Datei implementiert genau eine dieser Teilmethoden; fehlt sie, tut der Aufruf nichts.
        static partial void Netz(List<Massnahme> l);
        static partial void Sicherheit(List<Massnahme> l);
        static partial void Datentraeger(List<Massnahme> l);
        static partial void Leistung(List<Massnahme> l);
        static partial void Updates(List<Massnahme> l);
        static partial void Hardware(List<Massnahme> l);
        static partial void Speicher(List<Massnahme> l);
        static partial void Stabilitaet(List<Massnahme> l);
        static partial void System(List<Massnahme> l);
        static partial void Programm(List<Massnahme> l);
        static partial void Rueckweg(List<Massnahme> l);
        static partial void Auto(List<Massnahme> l);
    }
}
