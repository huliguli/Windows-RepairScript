using System;
using System.Collections.Generic;

namespace WartungsToolbox
{
    // Liest vorhandene System-Wiederherstellungspunkte.
    // Der Prozessstart laeuft ueber Shell: dort wird die Ausgabe in der Konsolen-Codepage
    // dekodiert (frueher UTF-8, wodurch Umlaute in den Beschreibungen als Fragezeichen
    // ankamen) und stderr wird mitgelesen (sonst Blockade bis zum Zeitlimit).
    static class RestorePoints
    {
        // Synchroner Aufruf -> nur aus einem Hintergrund-Thread verwenden.
        public static List<object> List()
        {
            var raw = Shell.PsJson(
                "$ErrorActionPreference='SilentlyContinue';" +
                "@(Get-ComputerRestorePoint | ForEach-Object { [pscustomobject]@{ " +
                "seq=[int]$_.SequenceNumber; desc=[string]$_.Description; rtype=[int]$_.RestorePointType; " +
                "time=$_.ConvertToDateTime($_.CreationTime).ToString('dd.MM.yyyy HH:mm') } }) | ConvertTo-Json -Compress",
                25000);

            var list = new List<object>();
            foreach (var d in raw) list.Add(d);

            // Neueste zuerst (hoechste Sequenznummer oben)
            list.Sort((a, b) => SeqOf(b).CompareTo(SeqOf(a)));
            return list;
        }

        static int SeqOf(object o)
        {
            try
            {
                var d = o as Dictionary<string, object>;
                if (d != null && d.ContainsKey("seq")) return Convert.ToInt32(d["seq"]);
            }
            catch { }
            return 0;
        }
    }
}
