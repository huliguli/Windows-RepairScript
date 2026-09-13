using System;
using System.Collections.Generic;
using System.Linq;

namespace WartungsToolbox.Kern.Regeln
{
    /// <summary>
    /// Freier Speicherplatz je Nutzer-Laufwerk (Buchstabe, fest eingebaut, nicht versteckt).
    /// Schwellen aus v7 (bewaehrt, nie zurueckgenommen): unter 10 % oder unter 10 GB ist es ernst,
    /// unter 20 % wird es knapp. Jeder Satz nennt die GB-Zahl (Grundsatz 9: "Auf C: sind noch
    /// 91 GB frei, das reicht").
    ///
    /// Reine Funktion ueber dem Systembild; Schattenspeicher (nur erhoeht) und Auslagerungsdatei
    /// erscheinen als Detail am Windows-Laufwerk, nie als eigener Befund - beides sind
    /// Vorschlagskandidaten mit Folgen (Wiederherstellungspunkte, Schnellstart), keine Fehler.
    /// </summary>
    public static class Speicherplatz
    {
        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var e = new BereichErgebnis { Bereich = Bereich.Speicherplatz };
            var nutzer = s.Volumes.Where(v => v.IstNutzerVolume).ToList();
            e.DatenVorhanden = nutzer.Count > 0;
            if (!e.DatenVorhanden)
            {
                e.Fehlend.Add(s.Volumes.Count == 0
                    ? "Liste der Laufwerke (" + Grund(s, "wmi.volume") + ")"
                    : "kein Laufwerk mit Buchstaben, das fest eingebaut ist");
                return e;
            }

            // Das Windows-Laufwerk traegt die Zusatzzeilen (Auslagerung, Schattenkopien, versteckte Partitionen).
            var windowsLaufwerk = nutzer.FirstOrDefault(v => v.Boot) ?? nutzer.FirstOrDefault(v => v.Buchstabe == "C") ?? nutzer[0];

            foreach (var v in nutzer.OrderBy(v => v.Buchstabe, StringComparer.Ordinal))
            {
                double freiPct = v.GroesseBytes > 0 ? v.FreiBytes * 100.0 / v.GroesseBytes : 0;
                double freiGb = v.FreiBytes / 1073741824.0;
                string lw = v.Buchstabe + ":";
                string zahlen = Text.Gb(v.FreiBytes) + " von " + Text.Gb(v.GroesseBytes) + " frei (" + Text.Pct(freiPct) + ")";

                Befund b;
                if (v.GroesseBytes <= 0)
                {
                    b = Neu("speicherplatz." + v.Buchstabe, Zustand.Unknown, "Größe des Laufwerks unbekannt",
                        "Für " + lw + " wurde keine Größe gemeldet, der freie Platz lässt sich nicht bewerten.",
                        null, "Win32_Volume.Capacity", Messwert.Von(v.GroesseBytes, "Bytes", "> 0"));
                }
                else if (freiPct < Schwellen.PlatzBadPct || freiGb < Schwellen.PlatzBadGb)
                {
                    b = Neu("speicherplatz." + v.Buchstabe, Zustand.Bad, "Speicherplatz wird knapp",
                        "Auf " + lw + " sind nur noch " + zahlen + "; unter " + Text.Pct(Schwellen.PlatzBadPct) + " oder " + Text.Gb((long)(Schwellen.PlatzBadGb * 1073741824)) + " arbeitet Windows unzuverlässig.",
                        "Räumen Sie " + lw + " auf: Papierkorb, Downloads und temporäre Dateien leeren, große Dateien auf ein anderes Laufwerk verschieben.",
                        "Win32_Volume.FreeSpace", Messwert.Von(Math.Round(freiGb, 1), "GB frei", ">= " + Schwellen.PlatzBadGb + " GB und >= " + Text.Pct(Schwellen.PlatzBadPct)));
                    b.Massnahmen.Add("speicher.aufraeumen");
                }
                else if (freiPct < Schwellen.PlatzWarnPct)
                {
                    b = Neu("speicherplatz." + v.Buchstabe, Zustand.Warn, "Speicherplatz wird knapper",
                        "Auf " + lw + " sind noch " + zahlen + "; unter " + Text.Pct(Schwellen.PlatzWarnPct) + " wird es knapp.",
                        "Räumen Sie " + lw + " bei Gelegenheit auf, bevor der Platz ernsthaft fehlt.",
                        "Win32_Volume.FreeSpace", Messwert.Von(Math.Round(freiPct, 1), "% frei", ">= " + Text.Pct(Schwellen.PlatzWarnPct)));
                    b.Massnahmen.Add("speicher.aufraeumen");
                }
                else
                {
                    b = Neu("speicherplatz." + v.Buchstabe, Zustand.Ok, "Genug Speicherplatz",
                        "Auf " + lw + " sind noch " + zahlen + ", das reicht.",
                        null, "Win32_Volume.FreeSpace", Messwert.Von(Math.Round(freiPct, 1), "% frei", ">= " + Text.Pct(Schwellen.PlatzWarnPct)));
                }

                b.Detail.Add(lw + " " + (v.Dateisystem ?? "?") + (v.Label != null ? " „" + v.Label + "“" : "") + ": " + zahlen);
                if (v == windowsLaufwerk) Zusatz(ctx, b);
                e.Befunde.Add(b);
            }
            return e;
        }

        /// <summary>Was sonst noch Platz belegt oder erklaert - als Detail, nicht als Befund.</summary>
        static void Zusatz(Kontext ctx, Befund b)
        {
            var s = ctx.S;
            foreach (var v in s.Volumes.Where(v => !v.IstNutzerVolume && v.GroesseBytes > 0))
                b.Detail.Add(Datentraeger.VolumeName(v) + ": " + Text.Mb(v.FreiBytes) + " von " + Text.Mb(v.GroesseBytes) + " frei");

            if (s.Leistung != null && s.Leistung.AuslagerungMB.HasValue)
                b.Detail.Add("Auslagerungsdatei: " + s.Leistung.AuslagerungMB.Value.ToString("N0", Text.De) + " MB");

            var z = s.Systemschutz;
            if (z.SchattenBelegt.HasValue)
                b.Detail.Add("Schattenkopien (Wiederherstellungspunkte) belegen " + Text.Gb1(z.SchattenBelegt.Value)
                             + Datentraeger.SchattenGrenze(z) + "; Verkleinern löscht Wiederherstellungspunkte");
            else if (s.ZugriffVerweigert("wmi.shadowstorage"))
                b.Detail.Add("Schattenkopien: nicht lesbar (braucht Administratorrechte)");
        }

        static Befund Neu(string schluessel, string zustand, string titel, string satz, string rat, string quelle, Messwert mw)
        {
            return new Befund
            {
                Bereich = Bereich.Speicherplatz, Schluessel = schluessel, Zustand = zustand,
                Titel = titel, Satz = satz, Rat = rat, Quelle = quelle, Messwert = mw,
            };
        }

        static string Grund(Systembild s, string quelle)
        {
            var f = s.FehlerVon(quelle).FirstOrDefault();
            if (f == null) return "keine Daten";
            switch (f.Art)
            {
                case Fehler.Zugriff: return "braucht Administratorrechte";
                case Fehler.Zeit: return "Zeitüberschreitung";
                case Fehler.Fehlt: return "nicht verfügbar";
                default: return "Fehler beim Lesen";
            }
        }
    }
}
