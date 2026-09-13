using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler
{
    /// <summary>
    /// Schreibt ein Systembild als Datei und redigiert es vorher: Rechnername, Benutzername,
    /// Seriennummern, MAC-Adressen und der Endteil privater IP-Adressen verschwinden.
    /// Aufgezeichnete Systembilder gehen ins Repository (die Proben brauchen sie), und das
    /// Repository ist oeffentlich. Die Redaktion ist deshalb ein Test, keine Hoeflichkeit.
    /// </summary>
    public static class Aufzeichnung
    {
        public static void Schreiben(Systembild s, string pfad, bool redigieren)
        {
            if (redigieren) Redigieren(s);
            Json.SchreibenDateiSicher(pfad, s, true);
        }

        public static Systembild Lesen(string pfad)
        {
            return Json.LesenDatei<Systembild>(pfad);
        }

        // Private Bereiche nach RFC 1918, immer vier Oktette: "10.0.11" in ".NET 10.0.11" ist eine Version, keine Adresse.
        static readonly Regex Ipv4 = new Regex(@"\b(10\.\d{1,3}|172\.(1[6-9]|2\d|3[01])|192\.168)\.(\d{1,3})\.(\d{1,3})\b", RegexOptions.Compiled);
        static readonly Regex Mac = new Regex(@"\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);
        // Globale (2000::/3) und private (fc00::/7) IPv6-Adressen: das Praefix verraet den
        // Anschluss, die Interface-Kennung oft die MAC (EUI-64, "ff:fe" in der Mitte).
        static readonly Regex Ipv6 = new Regex(@"(?<![0-9a-f:])([23][0-9a-f]{3}|f[cd][0-9a-f]{2}):[0-9a-f:]*[0-9a-f]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex Eui64 = new Regex(@"\bfe80::?[0-9a-f:]*ff:fe[0-9a-f:]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Konto-SIDs (Maschinen-SID plus RID) identifizieren die Installation eindeutig.
        static readonly Regex Sid = new Regex(@"S-1-5-21-\d+-\d+-\d+(-\d+)?", RegexOptions.Compiled);
        // Instanzpfade: Seriennummer in WPD-/Volume-Knoten (#SERIAL&0#), MAC in Bluetooth-LE-Knoten
        // (DEV_<12 Hex>) und als letztes Segment ohne "&" (Intel-Netzwerkkarten tragen dort die
        // MAC mit eingeschobenem FFFF, gemessen 12.09.2026: PCI\...\60CF84FFFF84249500).
        static readonly Regex WpdSerial = new Regex(@"#([^#&\\]{4,})&0#", RegexOptions.Compiled);
        static readonly Regex BthMac = new Regex(@"DEV_([0-9A-Fa-f]{12})", RegexOptions.Compiled);
        static readonly Regex Instanzpfad = new Regex(@"\b(USB|USBSTOR|SCSI|HID|BTHENUM|BTHLE|PCI|SWD|STORAGE|DISPLAY)\\[^\s""',;)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "RECHNER\PC": der Kontoteil hinter dem redigierten Rechnernamen, auch unter drei Zeichen.
        static readonly Regex RechnerKonto = new Regex(@"RECHNER\\[^\s\\""']+", RegexOptions.Compiled);

        public static void Redigieren(Systembild s)
        {
            string rechner = SafeMachine();
            string nutzer = SafeUser();

            Func<string, string> r = t =>
            {
                if (string.IsNullOrEmpty(t)) return t;
                if (!string.IsNullOrEmpty(rechner)) t = Regex.Replace(t, Regex.Escape(rechner), "RECHNER", RegexOptions.IgnoreCase);
                if (!string.IsNullOrEmpty(nutzer)) t = Regex.Replace(t, @"(?<=\\Users\\)" + Regex.Escape(nutzer) + @"(?=\\|$)", "NUTZER", RegexOptions.IgnoreCase);
                if (!string.IsNullOrEmpty(nutzer) && nutzer.Length >= 3) t = Regex.Replace(t, @"(?<![A-Za-z0-9])" + Regex.Escape(nutzer) + @"(?![A-Za-z0-9])", "NUTZER", RegexOptions.IgnoreCase);
                t = Ipv4.Replace(t, m => m.Groups[1].Value + "." + m.Groups[3].Value + ".x");
                t = Mac.Replace(t, "MAC");
                t = Eui64.Replace(t, "fe80::MAC");
                t = Sid.Replace(t, m => "S-1-5-21-SID" + m.Groups[1].Value);
                t = Instanzpfad.Replace(t, m => InstanzOhneSeriennummer(m.Value));
                t = RechnerKonto.Replace(t, "RECHNER\\NUTZER");
                return t;
            };
            // Eigene Adressen und Router: das IPv6-Praefix bleibt bis zum zweiten Block erhalten,
            // der Rest wird "x". Namensserver bleiben stehen (oeffentliche Resolver sind Befund).
            Func<string, string> ip6 = t =>
            {
                if (string.IsNullOrEmpty(t)) return t;
                t = r(t);
                return Ipv6.Replace(t, m => m.Groups[1].Value + ":x");
            };

            s.Hardware.Board = r(s.Hardware.Board);
            foreach (var g in s.Geraete) { g.Name = r(g.Name); g.InstanzId = InstanzOhneSeriennummer(g.InstanzId); g.Parent = InstanzOhneSeriennummer(g.Parent); }
            foreach (var d in s.Datentraeger) { d.Name = r(d.Name); d.ObjectId = null; }
            foreach (var v in s.Volumes) { v.Label = r(v.Label); }
            foreach (var e in s.Ereignisse.Eintraege)
            {
                var keys = new List<string>(e.Felder.Keys);
                foreach (var k in keys) e.Felder[k] = r(e.Felder[k]);
            }
            foreach (var a in s.Autostart.Eintraege) { a.Name = r(a.Name); a.Befehl = r(a.Befehl); }
            foreach (var a in s.Autostart.Aufgaben) { a.Pfad = r(a.Pfad); a.Autor = r(a.Autor); }
            foreach (var d in s.Autostart.Dienste) { d.Pfad = r(d.Pfad); d.Anzeige = r(d.Anzeige); }
            foreach (var p in s.Leistung.Prozesse) { p.Pfad = r(p.Pfad); }
            foreach (var u in s.WindowsUpdate.Verlauf) u.Titel = r(u.Titel);
            foreach (var u in s.WindowsUpdate.Verborgen) u.Titel = r(u.Titel);
            foreach (var u in s.WindowsUpdate.Ausstehend) u.Titel = r(u.Titel);
            foreach (var u in s.WindowsUpdate.FehlschlaegeLog) u.Titel = r(u.Titel);
            foreach (var u in s.WindowsUpdate.ErfolgeLog) u.Titel = r(u.Titel);
            foreach (var a in s.Netz.Adapter) { a.Name = r(a.Name); a.PnpId = InstanzOhneSeriennummer(a.PnpId); }
            foreach (var ip in s.Netz.IpAdressen) ip.Adresse = ip6(ip.Adresse);
            for (int i = 0; i < s.Netz.Gateways.Count; i++) s.Netz.Gateways[i] = ip6(s.Netz.Gateways[i]);
            foreach (var d in s.Netz.Dns) { for (int i = 0; i < d.Server.Count; i++) d.Server[i] = r(d.Server[i]); for (int i = 0; i < d.DhcpServer.Count; i++) d.DhcpServer[i] = ip6(d.DhcpServer[i]); }
            // Profilnamen sind bei WLAN die SSID.
            for (int i = 0; i < s.Netz.Profile.Count; i++) s.Netz.Profile[i].Name = "PROFIL" + (i + 1);
            s.Netz.Proxy.WinInetServer = r(s.Netz.Proxy.WinInetServer); s.Netz.Proxy.PacUrl = r(s.Netz.Proxy.PacUrl); s.Netz.Proxy.WinHttpServer = r(s.Netz.Proxy.WinHttpServer);
            // hosts: nur Zeilen, die die Netz-Regel als Treffer wertet (Konzept 7.1: "hosts-Zeilen
            // ausser Treffern"); Sperrlisten und Docker-Eintraege verraten sonst Server und Gewohnheiten.
            int hostsVorher = s.Netz.Hosts.Count;
            s.Netz.Hosts.RemoveAll(h => string.IsNullOrEmpty(h.Host) || !Kern.Regeln.Netz.IstGeschuetzt(h.Host));
            s.Netz.HostsEntfernt = hostsVorher - s.Netz.Hosts.Count;
            foreach (var h in s.Netz.Hosts) { h.Ip = r(h.Ip); h.Host = r(h.Host); }
            if (s.Sicherheit.Defender != null)
            {
                var d = s.Sicherheit.Defender;
                for (int i = 0; i < d.AusschlussPfade.Count; i++) d.AusschlussPfade[i] = r(d.AusschlussPfade[i]);
                for (int i = 0; i < d.AusschlussProzesse.Count; i++) d.AusschlussProzesse[i] = r(d.AusschlussProzesse[i]);
            }
            foreach (var k in s.Sicherheit.Konten) k.Name = "KONTO" + k.Rid;   // auch 500/501: umbenannte eingebaute Konten tragen oft Personennamen
            if (s.Systemschutz.Punkte != null) foreach (var p in s.Systemschutz.Punkte) p.Beschreibung = r(p.Beschreibung);
            foreach (var f in s.Fehlerliste) f.Text = r(f.Text);
        }

        /// <summary>
        /// Instanzpfade ohne Seriennummer und MAC: WPD-/Volume-Knoten (#SERIAL&amp;0#), Bluetooth-LE
        /// (DEV_MAC) und jedes letzte Segment ohne "&amp;" ab sechs Zeichen (Bus-Adressen wie
        /// "6&amp;3e0cc91&amp;0&amp;0038" bleiben, Seriennummern und MACs verschwinden).
        /// </summary>
        public static string InstanzOhneSeriennummer(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            id = WpdSerial.Replace(id, "#SERIENNUMMER&0#");
            id = BthMac.Replace(id, "DEV_MAC");
            int i = id.LastIndexOf('\\');
            if (i > 0 && i < id.Length - 1)
            {
                string letzt = id.Substring(i + 1);
                if (letzt.IndexOf('&') < 0 && letzt.Length >= 6 && letzt != "SERIENNUMMER")
                    return id.Substring(0, i + 1) + "SERIENNUMMER";
            }
            return id;
        }

        static string SafeMachine() { try { return Environment.MachineName; } catch (Exception) { return null; } }
        static string SafeUser() { try { return Environment.UserName; } catch (Exception) { return null; } }

        /// <summary>Prueft, dass eine Datei nichts Persoenliches mehr enthaelt (Test und Sperre vor dem Commit).</summary>
        public static List<string> Verstoesse(string pfad)
        {
            var hits = new List<string>();
            string text = File.ReadAllText(pfad);
            string rechner = SafeMachine(), nutzer = SafeUser();
            if (!string.IsNullOrEmpty(rechner) && text.IndexOf(rechner, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add("Rechnername");
            if (!string.IsNullOrEmpty(nutzer) && nutzer.Length >= 3 && Regex.IsMatch(text, @"(?<![A-Za-z0-9])" + Regex.Escape(nutzer) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase)) hits.Add("Benutzername");
            if (Mac.IsMatch(text)) hits.Add("MAC-Adresse");
            if (Ipv4.IsMatch(text)) hits.Add("private IP-Adresse vollstaendig");
            if (Eui64.IsMatch(text)) hits.Add("EUI-64-Adresse (MAC in IPv6)");
            if (Sid.IsMatch(text)) hits.Add("Konto-SID");
            if (Regex.IsMatch(text, @"\b[0-9A-F]{6}FFFF[0-9A-F]{8}\b")) hits.Add("MAC in einem Instanzpfad");
            if (Regex.IsMatch(text, @"RECHNER(?>\\+)(?!NUTZER)")) hits.Add("Kontoname hinter dem Rechnernamen");   // JSON schreibt den Backslash doppelt; atomare Gruppe, sonst gibt der Rueckwaertslauf einen zurueck
            return hits;
        }
    }
}
