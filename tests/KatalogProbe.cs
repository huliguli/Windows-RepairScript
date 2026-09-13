using System;
using System.Linq;
namespace WartungsToolbox
{
    // Gibt die Schritte einer Katalog-Aktion aus, damit run-tests.ps1 das eingebettete PowerShell
    // mit dem Parser pruefen kann (ein doppeltes Anfuehrungszeichen im -Command-Text beendet
    // den Befehl; Aktion 6 hatte genau das, Befund 12.09.2026). Nicht Teil des Baus.
    static class KatalogProbe
    {
        static int Main(string[] args)
        {
            if (args.Length == 0) { Console.WriteLine(Catalog.All().Count); return 0; }
            int id = int.Parse(args[0]);
            var a = Catalog.All()[id];
            Console.WriteLine("### " + a.Title);
            foreach (var s in a.Steps)
                Console.WriteLine("<<STEP " + s.File + ">>\n" + s.Args + "\n<<END>>");
            return 0;
        }
    }
}
