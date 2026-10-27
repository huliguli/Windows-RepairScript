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
    /// Der Papierkorb des eigenen Kontos (SHQueryRecycleBin, seit M4) ist ab PapierkorbWarnBytes
    /// ein warn-Befund mit der Massnahme "leeren" (Stufe 3, kein Rueckweg), darunter eine Zahl.
    /// </summary>
    public static class Speicherplatz
    {
        /// <summary>Kennung der Ansicht "Wo steckt der Platz?" (kern/Massnahmen.cs, Art ansicht; Helfer-Kennung fuer das Aufraeumen).</summary>
        public const string MAufraeumen = "speicher.aufraeumen";
        /// <summary>Papierkorb des eigenen Kontos leeren (docs/M4-ENTWURF.md, Abschnitt 1 und 2.4): Stufe 3, profilgebunden, kein Rueckweg.</summary>
        public const string MPapierkorbLeeren = "speicher.papierkorb.leeren";

        /// <summary>
        /// Der Befund handelt von einem Laufwerk: der Buchstabe steht als Parameter dabei (derselbe
        /// Name wie bei den Volume-Massnahmen, docs/M3-ENTWURF.md, Abschnitt 1), damit die Ansicht
        /// weiss, welches Laufwerk gemeint war. Die Massnahme selbst braucht keinen Parameter.
        /// </summary>
        public const string ParamBuchstabe = Datentraeger.ParamBuchstabe;

        /// <summary>
        /// Parameter des Papierkorb-Befunds (docs/M4-ENTWURF.md, Abschnitt 1): Anzahl der Dateien und
        /// Bytes, beide als ganze Zahl invariant. Die Massnahme braucht sie nicht zum Ausfuehren; der
        /// Verlust-Satz der Vorschau ("N Dateien, X GB") wird daraus gefuellt.
        /// </summary>
        public const string ParamDateien = "dateien", ParamBytes = "bytes";

        /// <summary>Quelle des Sammlers fuer den Papierkorb (sammler/Quellen/Speicherquelle.cs, Fehlereintrag bei Misserfolg).</summary>
        public const string QuellePapierkorb = "shell.papierkorb";

        public static BereichErgebnis Pruefen(Kontext ctx)
        {
            var s = ctx.S;
            var e = new BereichErgebnis { Bereich = Bereich.Speicherplatz };
            // Der Papierkorb haengt an keinem Laufwerk: er wird vor der Laufwerksliste bewertet, damit
            // ein voller Papierkorb auch ohne lesbare Laufwerke gemeldet wird (warn ist belegt; ein
            // ok-Befund allein macht den Bereich ohne Laufwerksdaten nicht gruen, BereichErgebnis).
            Papierkorb(s, e);
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
                    b.Massnahmen.Add(MAufraeumen);
                    b.Parameter[ParamBuchstabe] = v.Buchstabe;
                }
                else if (freiPct < Schwellen.PlatzWarnPct)
                {
                    b = Neu("speicherplatz." + v.Buchstabe, Zustand.Warn, "Speicherplatz wird knapper",
                        "Auf " + lw + " sind noch " + zahlen + "; unter " + Text.Pct(Schwellen.PlatzWarnPct) + " wird es knapp.",
                        "Räumen Sie " + lw + " bei Gelegenheit auf, bevor der Platz ernsthaft fehlt.",
                        "Win32_Volume.FreeSpace", Messwert.Von(Math.Round(freiPct, 1), "% frei", ">= " + Text.Pct(Schwellen.PlatzWarnPct)));
                    b.Massnahmen.Add(MAufraeumen);
                    b.Parameter[ParamBuchstabe] = v.Buchstabe;
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

        /// <summary>
        /// Papierkorb des eigenen Kontos (docs/M4-ENTWURF.md, Abschnitt 2.4): Bytes ab
        /// Schwellen.PapierkorbWarnBytes = warn mit speicher.papierkorb.leeren und den Parametern
        /// dateien/bytes; darunter und ueber 0 = ok-Befund mit Zahl, ohne Kennung; 0 Byte oder nicht
        /// gelesen (null) = kein Befund, Fehlend "Papierkorb" nur, wenn die Fehlerliste die Quelle
        /// nennt. Die Dateizahl muss mitgelesen sein, sonst bleibt die Karte ohne Kennung: der
        /// Verlust-Satz der Massnahme nennt beide Zahlen, und eine erfundene Null ist keine.
        /// </summary>
        static void Papierkorb(Systembild s, BereichErgebnis e)
        {
            var sp = s.Speicher;
            if (sp == null || !sp.PapierkorbBytes.HasValue)
            {
                if (s.FehlerVon(QuellePapierkorb).Any()) e.Fehlend.Add("Papierkorb (" + Grund(s, QuellePapierkorb) + ")");
                return;
            }
            long bytes = sp.PapierkorbBytes.Value;
            if (bytes <= 0) return;
            bool dateienDa = sp.PapierkorbDateien.HasValue && sp.PapierkorbDateien.Value >= 0;
            long dateien = dateienDa ? sp.PapierkorbDateien.Value : 0;
            string zahlen = (dateienDa ? dateien.ToString("N0", Text.De) + (dateien == 1 ? " Datei mit " : " Dateien mit ") : "") + Text.Gb1(bytes);
            bool warn = bytes >= Schwellen.PapierkorbWarnBytes;
            var b = Neu("speicherplatz.papierkorb", warn ? Zustand.Warn : Zustand.Ok,
                warn ? "Der Papierkorb belegt viel Platz" : "Der Papierkorb belegt wenig Platz",
                warn
                    ? "Im Papierkorb Ihres Kontos liegen " + zahlen + "; ab " + Text.Gb(Schwellen.PapierkorbWarnBytes) + " lohnt sich das Leeren, die Dateien sind danach endgültig weg."
                    : "Im Papierkorb Ihres Kontos liegen " + zahlen + "; unter " + Text.Gb(Schwellen.PapierkorbWarnBytes) + " ist das kein Platzproblem.",
                warn
                    ? (dateienDa ? "Den Papierkorb leeren lassen (Maßnahme an dieser Karte); vorher nachsehen, ob noch etwas gebraucht wird, denn es gibt keinen Rückweg."
                                 : "Den Papierkorb im Explorer leeren; die Anzahl der Dateien ließ sich nicht lesen, deshalb gibt es hier keine Maßnahme.")
                    : null,
                "SHQueryRecycleBin", Messwert.Von(Math.Round(bytes / 1073741824.0, 1), "GB im Papierkorb", ">= " + Text.Gb(Schwellen.PapierkorbWarnBytes) + " warn"));
            b.Detail.Add("SHQUERYRBINFO: i64Size " + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Byte, i64NumItems " + (dateienDa ? dateien.ToString(System.Globalization.CultureInfo.InvariantCulture) : "nicht gelesen") + "; nur der Papierkorb des angemeldeten Kontos, andere Konten zählen nicht.");
            if (warn && dateienDa)
            {
                b.Massnahmen.Add(MPapierkorbLeeren);
                b.Parameter[ParamDateien] = dateien.ToString(System.Globalization.CultureInfo.InvariantCulture);
                b.Parameter[ParamBytes] = bytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            e.Befunde.Add(b);
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
