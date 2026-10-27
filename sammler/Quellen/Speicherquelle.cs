using System;
using System.Runtime.InteropServices;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Fuellt s.Speicher: Groesse und Anzahl der Dateien im Papierkorb ueber SHQueryRecycleBinW
    /// (shell32, P/Invoke; pszRootPath NULL = alle Laufwerke). Laeuft ohne Erhoehung.
    ///
    /// Der Papierkorb ist kontogebunden ($Recycle.Bin\&lt;SID&gt; je Laufwerk): gezaehlt wird der des
    /// Kontos, unter dem der Sammler laeuft. Erhoeht ueber ein fremdes Administratorkonto (UAC mit
    /// anderem Konto) sind das dessen Zahlen, nicht die des angemeldeten Nutzers; Papierkoerbe
    /// fremder Konten liest diese Quelle nicht.
    ///
    /// Leer ohne Fehler gibt es nicht: scheitert der Aufruf, bleiben beide Werte null und
    /// "shell.papierkorb" steht in der Fehlerliste (fehlt: shell32 ohne den Export; ausnahme:
    /// HRESULT ungleich 0 oder unplausible Zahlen; zeit: Zeitgrenze, etwa ein nicht erreichbares
    /// Netzlaufwerk). Die Regel Speicherplatz unterscheidet null (nicht gelesen) von 0 (leer).
    /// </summary>
    public static class Speicherquelle
    {
        const string Quelle = "shell.papierkorb";
        /// <summary>SHQueryRecycleBin liest die Metadaten jeder Datei im Papierkorb; 10 s decken auch grosse Papierkoerbe.</summary>
        const int ZeitMs = 10000;

        public static void Erfassen(Systembild s)
        {
            var sp = s.Speicher;
            Sammler.Versuch(s, Quelle, () =>
            {
                var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                int hr;
                try { hr = MitZeitgrenze(ref info); }
                catch (DllNotFoundException) { Sammler.Fehler(s, Quelle, Fehler.Fehlt, "shell32.dll nicht vorhanden"); return; }
                catch (EntryPointNotFoundException) { Sammler.Fehler(s, Quelle, Fehler.Fehlt, "SHQueryRecycleBinW nicht exportiert"); return; }
                if (hr != 0) { Sammler.Fehler(s, Quelle, Fehler.Ausnahme, "SHQueryRecycleBinW lieferte 0x" + hr.ToString("X8")); return; }
                // Negative Werte kann die Schnittstelle nicht meinen; sie waeren ein erfundener Befund.
                if (info.i64Size < 0 || info.i64NumItems < 0)
                {
                    Sammler.Fehler(s, Quelle, Fehler.Ausnahme, "SHQueryRecycleBinW lieferte unplausible Werte (" + info.i64Size + " Byte, " + info.i64NumItems + " Dateien)");
                    return;
                }
                sp.PapierkorbBytes = info.i64Size;
                sp.PapierkorbDateien = info.i64NumItems;
            });
        }

        /// <summary>
        /// Der Aufruf im eigenen Thread mit Zeitgrenze, wie die COM-Aufrufe der Sicherheitsquelle:
        /// ein haengendes Netzlaufwerk darf die Messung nicht anhalten. Der Thread ist ein
        /// Hintergrundthread; bleibt er stecken, endet er mit dem Prozess.
        /// </summary>
        static int MitZeitgrenze(ref SHQUERYRBINFO info)
        {
            var kopie = info;
            int hr = 0;
            Exception fehler = null;
            var t = new Thread(() =>
            {
                try { hr = SHQueryRecycleBinW(null, ref kopie); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "shell:papierkorb" };
            t.Start();
            if (!t.Join(ZeitMs)) throw new TimeoutException("SHQueryRecycleBinW überschritt " + ZeitMs + " ms");
            if (fehler != null) throw fehler;
            info = kopie;
            return hr;
        }

        // Layout fuer den x64-Bau (beide Bauskripte setzen /platform:x64): DWORD, 4 Byte Fuellung,
        // zwei __int64 = 24 Byte. Ein x86-Bau braeuchte Pack = 1 (shellapi.h packt dort auf 20 Byte).
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
    }
}
