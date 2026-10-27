using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Stabilitaet (docs/M4-ENTWURF.md, Abschnitt 1): stabilitaet.speichertest.planen (Stufe 3).
        // Anmeldung hier, Arbeit in StabilitaetMassnahmen.
        static partial void Stabilitaet(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("stabilitaet.speichertest.planen", "Arbeitsspeicher-Test planen", 3,
                s => null, StabilitaetMassnahmen.SpeichertestPlanen));
        }
    }

    /// <summary>
    /// bcdedit.exe /bootsequence {memdiag} ueber Werkzeuge.Lauf (Exit 0): der naechste Start geht
    /// genau einmal in die Windows-Speicherdiagnose, danach loescht sich die Startfolge von selbst
    /// (Konzept 4.4; Sicherung keine, Rueckweg von selbst). Vorher prueft der Helfer, dass die
    /// Startkonfiguration den Eintrag {memdiag} kennt, nachher liest er {bootmgr} nach: steht die
    /// Zeile "bootsequence {memdiag}" nicht darin, ist der Nachweis nicht bestanden, auch wenn
    /// bcdedit 0 meldete; der Neustartbedarf gilt ab Exit 0 von /bootsequence, weil die Startfolge
    /// dann gesetzt ist, ob das Nachlesen gelingt oder nicht.
    /// Trockenlauf: nur die (trocken)-Zeile fuer /bootsequence, kein Prozess, kein Lesen ueber bcdedit.
    /// </summary>
    internal static class StabilitaetMassnahmen
    {
        public const string KSpeichertest = "stabilitaet.speichertest.planen";
        const int BcdeditMs = 2 * 60 * 1000;
        const string Memdiag = "{memdiag}";

        // Genau die Zeile "bootsequence {memdiag}" in bcdedit /enum {bootmgr}: der Elementname ist
        // nicht lokalisiert (nur Kopfzeile und Bezeichner sind es). {memdiag} allein steht in jedem
        // Boot-Manager unter toolsdisplayorder und belegt die Startfolge nicht.
        static readonly Regex BootsequenceRx = new Regex(@"(?im)^\s*bootsequence\s+.*\{memdiag\}", RegexOptions.Compiled);

        static string Bcdedit()
        {
            return Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
        }

        public static void SpeichertestPlanen(PlanSchritt s, Ausfuehrungskontext k)
        {
            string exe = Bcdedit();
            if (!File.Exists(exe))
            {
                k.Schreibe("   " + exe + " fehlt; die Speicherdiagnose lässt sich ohne bcdedit nicht vormerken.", "bad");
                Katalog.Problem(k);
                return;
            }
            k.Schreibe("   Der nächste Start geht in die Windows-Speicherdiagnose (10 bis 30 Minuten, der PC ist solange nicht benutzbar); die Startfolge gilt genau für 1 Start.", "dim");
            string tail;
            if (k.Trocken)
            {
                Werkzeuge.Lauf(k, exe, "/bootsequence " + Memdiag, BcdeditMs, null, false, out tail);
                Nachweise.Trocken(k, KSpeichertest, "Die Speicherdiagnose würde für den nächsten Start vorgemerkt; 1 Neustart wäre nötig.");
                return;
            }
            // Vorbedingung: den Eintrag gibt es (sonst meldet /bootsequence nur einen lokalisierten Fehlertext).
            int exit = Werkzeuge.Lauf(k, exe, "/enum " + Memdiag, BcdeditMs, null, false, out tail);
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, KSpeichertest, "Die Startkonfiguration kennt keinen Eintrag " + Memdiag + " (bcdedit /enum meldete Exit " + exit + "); die Speicherdiagnose wurde nicht vorgemerkt.");
                return;
            }
            exit = Werkzeuge.Lauf(k, exe, "/bootsequence " + Memdiag, BcdeditMs, null, false, out tail);
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, KSpeichertest, "bcdedit /bootsequence meldete Exit " + exit + "; die Speicherdiagnose ist nicht vorgemerkt.");
                return;
            }
            // Ab hier ist die Startfolge gesetzt: der naechste Start geht in die Diagnose, auch wenn das
            // Nachlesen gleich scheitert. Host und Nachweiskarte muessen das in jedem Fall erfahren.
            k.Werte["neustart"] = "1";
            k.Schreibe("   1 Neustart nötig; das Programm startet ihn nicht selbst.", "warn");
            // Nachlesen: {bootmgr} traegt jetzt eine Zeile "bootsequence {memdiag}" (BootsequenceRx).
            // Nicht lesbar oder nicht enthalten ist nicht bestanden; das Nachlesen entscheidet nur
            // noch ueber bestanden oder nicht, nicht mehr ueber den Neustartbedarf.
            exit = Werkzeuge.Lauf(k, exe, "/enum {bootmgr}", BcdeditMs, null, false, out tail);
            bool steht = exit == 0 && tail != null && BootsequenceRx.IsMatch(tail);
            if (!steht)
            {
                Nachweise.NichtBestanden(k, KSpeichertest, "bcdedit nahm die Startfolge an (Exit 0), die Zeile „bootsequence " + Memdiag + "“ steht in der Startkonfiguration ({bootmgr}) danach aber nicht" + (exit == 0 ? "" : " (bcdedit /enum meldete Exit " + exit + ")") + "; die Startfolge ist gesetzt, aber nicht belegt.");
                return;
            }
            Nachweise.Bestanden(k, KSpeichertest, "Die Speicherdiagnose ist für genau den nächsten Start vorgemerkt (die Zeile „bootsequence " + Memdiag + "“ steht in {bootmgr}); danach startet Windows normal.");
        }
    }
}
