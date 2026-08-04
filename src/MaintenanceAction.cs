using System.Collections.Generic;
using System.Text;

namespace WartungsToolbox
{
    enum LogKind { Normal, Header, Good, Bad, Dim, Warn }

    class Step
    {
        public string File;
        public string Args = "";
        public Encoding Enc;      // null => OEM-Codepage
        public bool Detached;     // im eigenen Fenster starten, nicht abwarten/mitschneiden
        public bool IgnoreExit;   // ExitCode != 0 ist hier erwartbar/harmlos -> nicht als Problem werten
        public bool Progress;     // zeichenweise lesen und Prozent (DISM/SFC: \r-Fortschritt) ans UI melden
    }

    class Job
    {
        public string Title;
        public System.Collections.Generic.List<Step> Steps;
    }

    class MaintenanceAction
    {
        public int Id;            // Listenindex, von Catalog.All() vergeben
        public string Title;      // Alltagssprache - das, was der Nutzer liest
        public string TechTitle;  // Fachname, nur klein im Detailbereich (null = keiner)
        public string Desc;       // ein Satz Alltagssprache
        public string Info;       // ausfuehrliche Erklaerung fuer das Fragezeichen
        public string Icon;       // Name aus dem Symbolsatz der Oberflaeche
        // Sonderaktion, die vorher eine Eingabe braucht (Ziel, Zielordner). Sie hat keine
        // Steps und laeuft ueber einen eigenen Befehl statt ueber "run".
        public string Special;
        public string Glyph;      // Segoe MDL2 Assets (Altbestand)
        public string Category;
        public bool Danger;       // Sicherheitsabfrage vor Ausfuehrung
        public bool IsRepair;     // reparierende Aktion (steuert auch die Einordnung im UI)
        public bool NeedsRestore; // ausdruecklich einen Sicherungspunkt davor anlegen

        /// <summary>
        /// Bekommt diese Aktion einen Sicherungspunkt, wenn der Nutzer das Haekchen gesetzt hat?
        ///
        /// Frueher galt hier nur IsRepair. Dadurch liefen ausgerechnet die als riskant
        /// markierten Aktionen (Netzwerk-Reset, Suchindex zuruecksetzen, CHKDSK, RAM-Diagnose,
        /// Miniaturansichten) OHNE Sicherungspunkt - das Haekchen war dort eine leere Zusage.
        /// Jetzt gilt: reparierend ODER riskant ODER ausdruecklich angefordert.
        /// Abgesichert durch tests/run-tests.ps1.
        /// </summary>
        public bool WantsRestorePoint
        {
            get { return IsRepair || Danger || NeedsRestore; }
        }

        public List<Step> Steps = new List<Step>();
    }

    // Eine still/unbeaufsichtigt sichere Aufgabe der geplanten Wartung (--auto).
    class AutoItem
    {
        public string Key;        // stabiler Schluessel (Whitelist fuer UI/Config)
        public string Title;
        public string Desc;
        public bool Std;          // Teil des Standard-Satzes
        public List<Step> Steps = new List<Step>();
    }
}
