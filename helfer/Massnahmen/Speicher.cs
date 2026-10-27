using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Speicher (docs/M4-ENTWURF.md, Abschnitt 1): speicher.papierkorb.leeren (Stufe 3). Der
        // Papierkorb ist kontogebunden ($Recycle.Bin\<SID> je Laufwerk): die Massnahme arbeitet im
        // Profil des Helfer-Kontos, deshalb Profilgebunden (Ausfuehrung lehnt ein fremdes Konto im
        // UAC-Dialog mit Exit 2 ab). Anmeldung hier, Arbeit in SpeicherMassnahmen.
        static partial void Speicher(List<Massnahme> l)
        {
            Massnahme m = Massnahmenbau.Neu("speicher.papierkorb.leeren", "Papierkorb endgültig leeren", 3,
                s => null, SpeicherMassnahmen.PapierkorbLeeren);
            m.Profilgebunden = true;
            l.Add(m);
        }
    }

    /// <summary>
    /// Papierkorb des eigenen Kontos ueber shell32 (P/Invoke, kein Skriptprozess): SHQueryRecycleBinW
    /// liefert Anzahl und Groesse (pszRootPath NULL = alle Laufwerke), SHEmptyRecycleBinW leert ihn
    /// ohne Nachfrage, Fortschrittsfenster und Ton. Vorher wird gezaehlt (leer = "schon so"), nachher
    /// wieder (0 Dateien = bestanden). Keine Sicherung, kein Rueckweg: was im Papierkorb lag, ist
    /// danach weg (Verlust-Satz im Kern-Katalog). Beide Aufrufe laufen mit Zeitgrenze im eigenen
    /// Thread wie die Speicherquelle des Sammlers: ein haengendes Netzlaufwerk darf den Helfer nicht
    /// festhalten. Trockenlauf: zaehlen, (trocken)-Zeile, nichts loeschen.
    /// </summary>
    internal static class SpeicherMassnahmen
    {
        public const string KPapierkorb = "speicher.papierkorb.leeren";

        // shellapi.h: SHERB_NOCONFIRMATION 1, SHERB_NOPROGRESSUI 2, SHERB_NOSOUND 4.
        const uint SherbNoConfirmation = 0x1, SherbNoProgressUi = 0x2, SherbNoSound = 0x4;
        const int ZaehlenMs = 30000;
        const int LeerenMs = 10 * 60 * 1000;

        public sealed class PapierkorbStand
        {
            public long Dateien;
            public long Bytes;
        }

        public static void PapierkorbLeeren(PlanSchritt s, Ausfuehrungskontext k)
        {
            if (IntPtr.Size != 8)
            {
                // Das Layout von SHQUERYRBINFO unten gilt fuer den x64-Bau (beide Bauskripte setzen /platform:x64).
                k.Schreibe("   Der Helfer läuft nicht als 64-Bit-Prozess; der Papierkorb wird nicht gezählt und nicht geleert.", "bad");
                Katalog.Problem(k);
                return;
            }
            PapierkorbStand vorher;
            string fehler = Zaehlen(out vorher);
            if (fehler != null)
            {
                k.Schreibe("   Der Papierkorb ließ sich nicht zählen (" + fehler + "); er wird nicht geleert.", "bad");
                Katalog.Problem(k);
                return;
            }
            k.Werte["papierkorb.dateien"] = vorher.Dateien.ToString(CultureInfo.InvariantCulture);
            k.Werte["papierkorb.bytes"] = vorher.Bytes.ToString(CultureInfo.InvariantCulture);
            if (vorher.Dateien == 0)
            {
                LeistungMassnahmen.SchonSo(k, KPapierkorb, "Der Papierkorb war schon leer (0 Dateien); es wurde nichts gelöscht.");
                return;
            }
            k.Schreibe("   Im Papierkorb liegen " + Zahl(vorher.Dateien) + " (" + Groesse(vorher.Bytes) + "); sie werden endgültig gelöscht.", "dim");
            if (k.Trocken)
            {
                k.Schreibe("(trocken) shell32 SHEmptyRecycleBinW(NULL, NULL, SHERB_NOCONFIRMATION|SHERB_NOPROGRESSUI|SHERB_NOSOUND)", "dim");
                Nachweise.Trocken(k, KPapierkorb, Zahl(vorher.Dateien) + " (" + Groesse(vorher.Bytes) + ") würden endgültig gelöscht.");
                return;
            }
            k.Schreibe("›  shell32 SHEmptyRecycleBinW(NULL, NULL, 7)", "dim");
            int hr;
            try { hr = Leeren(); }
            catch (Exception ex)
            {
                Nachweise.NichtBestanden(k, KPapierkorb, "SHEmptyRecycleBin scheiterte (" + Massnahmenbau.Kurz(ex.Message) + "); der Papierkorb ist nicht geleert.");
                return;
            }
            if (hr != 0)
            {
                Nachweise.NichtBestanden(k, KPapierkorb, "SHEmptyRecycleBin meldete 0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + "; der Papierkorb ist nicht oder nur teilweise geleert.");
                return;
            }
            PapierkorbStand nachher;
            fehler = Zaehlen(out nachher);
            if (fehler != null)
            {
                Nachweise.NichtBestanden(k, KPapierkorb, "SHEmptyRecycleBin meldete Erfolg, der Papierkorb ließ sich danach aber nicht zählen (" + fehler + ").");
                return;
            }
            if (nachher.Dateien == 0)
                Nachweise.Bestanden(k, KPapierkorb, "Der Papierkorb ist leer (0 Dateien, vorher " + Zahl(vorher.Dateien) + " mit " + Groesse(vorher.Bytes) + ").");
            else
                Nachweise.NichtBestanden(k, KPapierkorb, "SHEmptyRecycleBin meldete Erfolg, im Papierkorb liegen aber noch " + Zahl(nachher.Dateien) + " (" + Groesse(nachher.Bytes) + ").");
        }

        /// <summary>Anzahl und Groesse ueber SHQueryRecycleBinW; null = gezaehlt, sonst der Grund (kein Wert ist kein Beleg).</summary>
        static string Zaehlen(out PapierkorbStand stand)
        {
            stand = null;
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            int hr;
            try
            {
                var kopie = info;
                hr = MitZeitgrenze(() => SHQueryRecycleBinW(null, ref kopie), ZaehlenMs, "SHQueryRecycleBinW");
                info = kopie;
            }
            catch (DllNotFoundException) { return "shell32.dll nicht vorhanden"; }
            catch (EntryPointNotFoundException) { return "SHQueryRecycleBinW nicht exportiert"; }
            catch (Exception ex) { return Massnahmenbau.Kurz(ex.Message); }
            if (hr != 0) return "SHQueryRecycleBinW lieferte 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
            if (info.i64Size < 0 || info.i64NumItems < 0) return "unplausible Werte (" + info.i64Size + " Byte, " + info.i64NumItems + " Dateien)";
            stand = new PapierkorbStand { Dateien = info.i64NumItems, Bytes = info.i64Size };
            return null;
        }

        static int Leeren()
        {
            return MitZeitgrenze(() => SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound), LeerenMs, "SHEmptyRecycleBinW");
        }

        // Eigener Hintergrundthread mit Zeitgrenze (wie Zeitgrenze.Mit, hier mit ref-Struktur im
        // Lambda): bleibt der Aufruf stecken, endet der Thread mit dem Prozess.
        static int MitZeitgrenze(Func<int> f, int zeitMs, string was)
        {
            int hr = 0;
            Exception fehler = null;
            var t = new Thread(() =>
            {
                try { hr = f(); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "shell:" + was };
            t.Start();
            if (!t.Join(zeitMs)) throw new TimeoutException(was + " überschritt " + (zeitMs >= 60000 ? (zeitMs / 60000) + " Minuten" : (zeitMs / 1000) + " Sekunden") + ".");
            if (fehler != null) throw fehler;
            return hr;
        }

        static string Zahl(long dateien)
        {
            return dateien == 1 ? "1 Datei" : dateien.ToString(CultureInfo.InvariantCulture) + " Dateien";
        }

        static string Groesse(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        // Layout fuer den x64-Bau: DWORD, 4 Byte Fuellung, zwei __int64 = 24 Byte (shellapi.h packt
        // auf x86 auf 20 Byte; deshalb die Groessenpruefung oben).
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBinW(IntPtr hwnd, string pszRootPath, uint dwFlags);
    }
}
