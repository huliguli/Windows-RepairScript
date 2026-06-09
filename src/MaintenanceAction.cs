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
        public string Title;
        public string Desc;
        public string Glyph;      // Segoe MDL2 Assets
        public string Category;
        public bool Danger;       // Sicherheitsabfrage vor Ausfuehrung
        public bool IsRepair;     // optionaler Wiederherstellungspunkt davor
        public List<Step> Steps = new List<Step>();
    }
}
