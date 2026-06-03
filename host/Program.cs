using System;
using System.Windows.Forms;

namespace WartungsToolbox
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string shot = null, view = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--view" && i + 1 < args.Length) view = args[++i];
            }

            Application.Run(new ShellForm(shot, view));
        }
    }
}
