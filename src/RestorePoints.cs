using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;

namespace WartungsToolbox
{
    // Liest vorhandene System-Wiederherstellungspunkte (ueber Get-ComputerRestorePoint).
    static class RestorePoints
    {
        // Synchroner Aufruf -> nur aus einem Hintergrund-Thread verwenden.
        public static List<object> List()
        {
            List<object> list = new List<object>();
            string ps =
                "$ErrorActionPreference='SilentlyContinue';" +
                "$rp = Get-ComputerRestorePoint;" +
                "$out = @($rp | ForEach-Object { [pscustomobject]@{ " +
                "seq=[int]$_.SequenceNumber; desc=[string]$_.Description; rtype=[int]$_.RestorePointType; " +
                "time=$_.ConvertToDateTime($_.CreationTime).ToString('yyyy-MM-dd HH:mm') } });" +
                "$out | ConvertTo-Json -Compress";

            string json = RunPs(ps, 25000);
            if (string.IsNullOrEmpty(json)) return list;

            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                object parsed = js.DeserializeObject(json);
                if (parsed is object[])
                {
                    foreach (object o in (object[])parsed) list.Add(o);
                }
                else if (parsed is Dictionary<string, object>)
                {
                    list.Add(parsed); // ConvertTo-Json liefert bei genau einem Eintrag ein Einzelobjekt
                }
            }
            catch { }

            // Neueste zuerst (hoechste Sequenznummer oben)
            list.Sort(delegate(object a, object b) { return SeqOf(b).CompareTo(SeqOf(a)); });
            return list;
        }

        static int SeqOf(object o)
        {
            try
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d != null && d.ContainsKey("seq")) return Convert.ToInt32(d["seq"]);
            }
            catch { }
            return 0;
        }

        static string RunPs(string command, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> rd = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                    try { return rd.Result; } catch { return ""; }
                }
            }
            catch { return ""; }
        }
    }
}
