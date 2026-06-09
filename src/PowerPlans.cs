using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WartungsToolbox
{
    // Liest und schaltet Windows-Energiesparplaene (powercfg).
    static class PowerPlans
    {
        static readonly Regex GuidRx = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        // Vorhandene Plaene (aktiver markiert). Synchron -> aus Hintergrund-Thread aufrufen.
        public static List<object> List()
        {
            List<object> list = new List<object>();
            string outp = RunNative("powercfg.exe", "/list", 8000);
            if (string.IsNullOrEmpty(outp)) return list;

            string[] lines = outp.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                Match mg = GuidRx.Match(line);
                if (!mg.Success) continue;

                string guid = mg.Value;
                string name = "";
                int o = line.IndexOf('(');
                int c = line.LastIndexOf(')');
                if (o >= 0 && c > o) name = line.Substring(o + 1, c - o - 1).Trim();
                bool active = line.TrimEnd().EndsWith("*");

                Dictionary<string, object> d = new Dictionary<string, object>();
                d["guid"] = guid;
                d["name"] = name.Length > 0 ? name : guid;
                d["active"] = active;
                list.Add(d);
            }
            return list;
        }

        // Aktiven Plan setzen. GUID wird strikt geprueft und normalisiert -> keine Injektion moeglich.
        public static void SetActive(string guid)
        {
            Guid x;
            if (string.IsNullOrEmpty(guid) || !Guid.TryParse(guid, out x)) return;
            RunNative("powercfg.exe", "/setactive " + x.ToString(), 8000);
        }

        static string RunNative(string file, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Encoding enc;
                try { enc = Encoding.GetEncoding((int)Native.GetOEMCP()); } catch { enc = Encoding.Default; }
                psi.StandardOutputEncoding = enc;

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
