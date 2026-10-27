using System;
using System.Collections.Generic;
using System.Globalization;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Helfer
{
    public static partial class MassnahmenM3
    {
        // Rueckweg (docs/M3-ENTWURF.md, Abschnitt 1 und 8.3): Kennung rueckweg mit dem Pfad einer
        // Sicherungsdatei. Anmeldung hier, Arbeit in RueckwegMassnahmen.
        static partial void Rueckweg(List<Massnahme> l)
        {
            l.Add(Massnahmenbau.Neu("rueckweg", "Rückgängig machen", 2,
                RueckwegMassnahmen.Pruefen, RueckwegMassnahmen.Ausfuehren));
        }
    }

    /// <summary>
    /// Der Rueckweg einer Massnahme mit Sicherung: liest die Sicherungsdatei (nur unter
    /// sicherungen\massnahmen, Name [A-Za-z0-9._-]+.json, Besitzer und Rechte vom Helfer; alles
    /// prueft Sicherungsdatei.Lesen) und schreibt die gemerkten Vorwerte je Kennung zurueck. Eine
    /// Fassung je Kennung (Vertrag 8.3): die vier K3-Kennungen (DNS, Firewall, SmartScreen, UAC)
    /// gehen an MassnahmenM3.ZurueckNetz bzw. ZurueckSicherheit, die neben der Hin-Massnahme
    /// liegen; hier bleiben TRIM (fsutil, nur Bit 0 = NTFS des gesicherten Rohwerts), Startprogramm
    /// (die 12 Byte in StartupApproved), Aufgabe (schtasks /ENABLE), Dienst-Startart
    /// (ChangeStartMode, nur leistung.dienst.startart), Update (IsHidden = false) und Geraet
    /// (Disable), jeweils mit den Bausteinen der Bereichsdatei, damit Hin- und Rueckweg dieselben
    /// Aufrufe machen. update.dienst.aktivieren hat keinen Rueckweg (Katalog Rueckweg keiner,
    /// Vertrag 8.10): die Sicherung ist dort nur der Beleg, InhaltPruefen lehnt sie ab, und der
    /// Helfer meldet der Hin-Massnahme rueckweg.moeglich = "0".
    ///
    /// M4 (docs/M4-ENTWURF.md, Abschnitt 3.3): netz.hosts.bereinigen und netz.ip.zuruecksetzen gehen
    /// an ZurueckNetz*, sicherheit.firewall.eingehend.blockieren und sicherheit.firewall.zuruecksetzen
    /// an ZurueckSicherheit*, leistung.winlogon.zuruecksetzen an ZurueckLeistung* (Leistung.cs);
    /// netz.winsock.zuruecksetzen, update.zuruecksetzen und geraet.treiber.neu haben keinen Rueckweg
    /// (die Datei ist Beleg, die Hin-Massnahme meldet rueckweg.moeglich = "0"), InhaltPruefen lehnt
    /// sie mit eigenem Satz ab.
    ///
    /// Jeder Wert aus der Datei wird vor dem Eingriff so geprueft wie der Parameter der
    /// Hin-Massnahme (InhaltPruefen, fuer die Bereichs-Kennungen ZurueckNetzPruefen,
    /// ZurueckSicherheitPruefen, ZurueckLeistungPruefen).
    /// Dazu die Sperrliste: kein Dienst aus LeistungMassnahmen.NieDeaktivieren wird ueber einen
    /// Vorwert Disabled abgeschaltet (Vertrag 8.2). Eine Datei, die diese Pruefung nicht besteht,
    /// wird abgelehnt (Exit 2), nie teilweise zurueckgespielt. Die Sicherungsdatei bleibt nach dem
    /// Rueckweg liegen (ein zweiter Rueckweg stellt dieselben Werte noch einmal her).
    ///
    /// Profilbindung (Gegenlesen M3): eine Startprogramm-Sicherung mit hive=HKCU muss unter
    /// parameter.profilSid das Profil nennen, aus dem die Bytes stammen, und dieses Profil muss das
    /// eigene Konto des Helfers sein (LeistungMassnahmen.ProfilIstEigenes). Der Verlauf ist
    /// maschinenweit, also sieht Konto B den Knopf von Konto A; ohne diese Pruefung schriebe der
    /// Rueckweg A's Vorwert in B's HKCU und meldete Erfolg. InhaltPruefen lehnt eine solche Datei ab
    /// (Exit 2), AutostartZurueck prueft es vor dem Eingriff noch einmal (ProfilPruefen).
    ///
    /// Werte fuer den Host und den Kern-Nachweis rueckweg (vor der Verteilung gesetzt):
    /// rueckweg.kennung = Kennung der Sicherung, rueckweg.parameter.&lt;name&gt; = jeder Hin-Parameter aus
    /// der Datei; die Rueckwege selbst setzen nachweis.satz, nachweis.bestanden und ggf. neustart.
    ///
    /// Trockenlauf: Datei lesen, Ist-Zustand messen, Zeile mit dem geplanten Eingriff, nichts
    /// schreiben; als Nachweis nur Nachweise.Trocken, auch wenn der Vorwert schon wieder gilt.
    /// </summary>
    internal static class RueckwegMassnahmen
    {
        public const string Kennung = "rueckweg";

        // Kennungen, deren Sicherung dieser Rueckweg selbst zurueckspielt (K4). Die K3-Kennungen
        // kennen MassnahmenM3.ZurueckNetzBekannt und ZurueckSicherheitBekannt.
        public const string Trim = "datentraeger.trim.einschalten";
        public const string Autostart = "leistung.autostart.deaktivieren";
        public const string Aufgabe = "leistung.aufgabe.deaktivieren";
        public const string Dienst = "leistung.dienst.startart";
        public const string Update = "update.verbergen";
        public const string Geraet = "geraet.aktivieren";

        // Kennung mit Sicherung, aber ohne Rueckweg (Katalog Rueckweg keiner, Vertrag 8.10): wird
        // in InhaltPruefen mit eigenem Satz abgelehnt, nicht als "unbekannt".
        public const string UpdateDienst = "update.dienst.aktivieren";

        // M4-Kennungen mit Rueckweg im Bereich (Vertrag M4 3.3) ...
        public const string Hosts = "netz.hosts.bereinigen";
        public const string Ip = "netz.ip.zuruecksetzen";
        public const string FirewallEingehend = "sicherheit.firewall.eingehend.blockieren";
        public const string FirewallReset = "sicherheit.firewall.zuruecksetzen";
        public const string Winlogon = "leistung.winlogon.zuruecksetzen";
        // ... und die drei, deren Sicherung nur Beleg ist (rueckweg.moeglich = "0").
        public const string Winsock = "netz.winsock.zuruecksetzen";
        public const string UpdateReset = "update.zuruecksetzen";
        public const string TreiberNeu = "geraet.treiber.neu";

        const int SchluesselMax = 64;
        const int WertMax = 4096;
        // Vorwerte, die ganze Texte tragen (Vertrag M4, Abschnitt 1): der hosts-Inhalt bis 256 KB,
        // Adapterliste, Winsock-Katalog und Update-Liste bis 64 KB.
        const int TextMax = 256 * 1024;
        const int ListeMax = 64 * 1024;

        // ================================================================ Pruefen

        public static string Pruefen(PlanSchritt s)
        {
            string wert = s.Wert("sicherung");
            if (string.IsNullOrEmpty(wert)) return "Es ist keine Sicherungsdatei angegeben (Parameter sicherung ist leer).";
            if (wert.IndexOf(';') >= 0) return "Der Parameter sicherung nennt genau eine Datei, keine Liste.";
            Sicherung si;
            try { si = Sicherungsdatei.Lesen(wert); }
            catch (Exception ex) { return "Die Sicherung lässt sich nicht lesen: " + ex.Message; }
            return InhaltPruefen(si);
        }

        /// <summary>null = alle Werte der Datei sind gueltig; sonst der Grund (Ablehnung des Plans).</summary>
        public static string InhaltPruefen(Sicherung si)
        {
            if (si == null) return "Die Sicherung ist leer.";
            if (string.IsNullOrEmpty(si.Kennung)) return "Die Sicherung nennt keine Kennung.";
            string form = FormPruefen(si);
            if (form != null) return form;
            switch (si.Kennung)
            {
                case Trim:
                    {
                        // Rohwert des DWORD (Bit 0 NTFS, Bit 1 ReFS); zurueckgestellt wird nur Bit 0.
                        string v = si.Wert(DatentraegerMassnahmen.TrimWert);
                        if (v == null) return "Die Sicherung nennt keinen TRIM-Vorwert (" + DatentraegerMassnahmen.TrimWert + ").";
                        if (DatentraegerMassnahmen.TrimZahl(v) == null) return "Der TRIM-Vorwert muss eine ganze Zahl von 0 bis 255 oder leer sein, nicht „" + Massnahmenbau.Kurz(v) + "“.";
                        return null;
                    }
                case Autostart:
                    {
                        string hive = LeistungMassnahmen.HiveNorm(si.ParameterWert("hive"));
                        if (hive == null) return "Die Sicherung nennt kein gültiges hive (HKCU oder HKLM).";
                        if (LeistungMassnahmen.OrtNorm(si.ParameterWert("ort")) == null) return "Die Sicherung nennt keinen gültigen ort (Run, Run32, StartupFolder).";
                        if (LeistungMassnahmen.NamePruefen(si.ParameterWert("name")) == null) return "Die Sicherung nennt keinen gültigen Wertnamen (name).";
                        if (si.Wert("bytes") == null || LeistungMassnahmen.Bytes(si.Wert("bytes")) == null) return "Die Sicherung nennt keine gültigen Vorher-Bytes (bytes, Hex mit gerader Länge bis 64 Zeichen).";
                        if (hive == "HKCU")
                        {
                            // Profilbindung (Leistung.cs, Klassenkommentar): HKCU ist das Profil des
                            // Helfer-Kontos. Eine Sicherung ohne SID oder aus einem anderen Profil wird
                            // hier abgelehnt (Exit 2), bevor ein Schritt laeuft; sonst schriebe der
                            // Rueckweg den Vorwert eines anderen Kontos in das eigene HKCU.
                            string profil = si.ParameterWert(LeistungMassnahmen.ProfilSchluessel);
                            if (profil == null) return "Die Sicherung nennt kein Profil (" + LeistungMassnahmen.ProfilSchluessel + "); ein Startprogramm unter HKCU wird ohne Profilbindung nicht zurückgestellt.";
                            if (LeistungMassnahmen.ProfilSidNorm(profil) == null) return "Die Sicherung nennt kein gültiges Profil (" + LeistungMassnahmen.ProfilSchluessel + " muss die Form S-1-5-21-… oder S-1-12-1-… haben, nicht „" + Massnahmenbau.Kurz(profil) + "“).";
                            if (!LeistungMassnahmen.ProfilIstEigenes(profil)) return LeistungMassnahmen.ProfilFremdeSicherung;
                        }
                        return null;
                    }
                case Aufgabe:
                    if (LeistungMassnahmen.AufgabePfadNorm(si.ParameterWert("pfad")) == null) return "Die Sicherung nennt keinen gültigen Aufgabenpfad (pfad).";
                    if (Bool(si.Wert("aktiv")) == null) return "Die Sicherung nennt nicht, ob die Aufgabe vorher aktiv war (aktiv).";
                    return null;
                case Dienst:
                    {
                        string name = LeistungMassnahmen.DienstNameNorm(si.ParameterWert("name"));
                        if (name == null) return "Die Sicherung nennt keinen gültigen Dienstnamen (name).";
                        if (LeistungMassnahmen.ZielStartartNorm(si.ParameterWert("startart")) == null) return "Die Sicherung nennt kein gültiges Ziel der Hin-Maßnahme (parameter startart: Automatic, Manual, Disabled).";
                        return StartartPruefen(name, si.Wert("startart"));
                    }
                case UpdateDienst:
                    // Katalog Rueckweg keiner (Vertrag 8.10): die Datei belegt, was vorher galt, und
                    // ist keine Anleitung zum Zurueckstellen. Gilt fuer jeden Vorwert (Disabled wie
                    // Manual); Windows-Update-Dienste werden hier weder angehalten noch umgestellt.
                    return "Für die Maßnahme „update.dienst.aktivieren“ gibt es keinen Rückweg: Windows-Update-Dienste werden nicht wieder angehalten oder umgestellt; die Sicherung bleibt als Beleg liegen.";
                case Update:
                    if (UpdatesMassnahmen.UpdateIdNorm(si.ParameterWert("updateId")) == null) return "Die Sicherung nennt keine gültige UpdateID (updateId).";
                    if (Bool(si.Wert("isHidden")) == null) return "Die Sicherung nennt nicht, ob das Update vorher verborgen war (isHidden).";
                    return null;
                case Geraet:
                    if (HardwareMassnahmen.InstanzIdNorm(si.ParameterWert("instanzId")) == null) return "Die Sicherung nennt keine gültige Instanz-Id (instanzId).";
                    return null;
                case Hosts:
                case Ip:
                    // Inhalt (Zeichen, GUIDs, IPv4-Adressen, Masken, Gateways, DNS) prueft der Bereich (Vertrag M4 3.3).
                    return MassnahmenM3.ZurueckNetzPruefen(si);
                case FirewallEingehend:
                case FirewallReset:
                    // DefaultInboundAction 0|1, Profil 1|2|4, Exportname nach Muster und Herkunft am Handle: der Bereich.
                    return MassnahmenM3.ZurueckSicherheitPruefen(si);
                case Winlogon:
                    return MassnahmenM3.ZurueckLeistungPruefen(si);
                case Winsock:
                    return "Für die Maßnahme „netz.winsock.zuruecksetzen“ gibt es keinen Rückweg: der gesicherte Winsock-Katalog ist ein Beleg, kein Zustand, den Windows zurückschreibt; entfernte Netzwerkkomponenten von Drittprogrammen kommen nur durch deren Neuinstallation zurück.";
                case UpdateReset:
                    return "Für die Maßnahme „update.zuruecksetzen“ gibt es keinen Rückweg: die umbenannten Ordner (SoftwareDistribution.old, catroot2.old) baut Windows neu auf, und die verborgenen Updates hat die Maßnahme selbst wieder verborgen; die Sicherung bleibt als Beleg liegen.";
                case TreiberNeu:
                    {
                        string export = si.Wert("export");
                        string inf = si.Wert("inf");
                        return "Für die Maßnahme „geraet.treiber.neu“ gibt es keinen Rückweg über dieses Programm: "
                            + (string.IsNullOrEmpty(export)
                                ? "der Treiber gehörte zu Windows und liegt im Treiberspeicher; Windows richtet ihn bei der Erkennung selbst ein."
                                : "der bisherige Treiber liegt exportiert unter sicherungen\\massnahmen\\export\\" + Massnahmenbau.Kurz(export) + " und lässt sich mit pnputil /add-driver " + (HardwareMassnahmen.IstOemInf(inf) ? inf : "<inf>") + " /install von Hand einspielen.");
                    }
                default:
                    // Die K3-Kennungen prueft der Bereich selbst (Vertrag 8.3): SettingID, Adapter,
                    // DNS-Liste, FirewallEnabled, Profil, SmartScreenEnabled, EnableLUA, ConsentPromptBehaviorAdmin.
                    if (MassnahmenM3.ZurueckNetzBekannt(si.Kennung)) return MassnahmenM3.ZurueckNetzPruefen(si);
                    if (MassnahmenM3.ZurueckSicherheitBekannt(si.Kennung)) return MassnahmenM3.ZurueckSicherheitPruefen(si);
                    if (MassnahmenM3.ZurueckLeistungBekannt(si.Kennung)) return MassnahmenM3.ZurueckLeistungPruefen(si);
                    return "Für die Kennung „" + Massnahmenbau.Kurz(si.Kennung) + "“ gibt es keinen Rückweg.";
            }
        }

        /// <summary>
        /// Form der beiden Woerterbuecher: jeder Schluessel 1 bis 64 Zeichen aus Buchstaben, Ziffern und
        /// _, jeder Wert vorhanden, hoechstens 4096 Zeichen, ohne Steuerzeichen. Die Schluessel werden
        /// als rueckweg.parameter.&lt;name&gt; an den Host gereicht; ein Schluessel, der kein Name ist, gehoert
        /// nicht in eine Sicherung des Helfers. Ausnahme (M4): die Vorwerte, die ganze Texte tragen
        /// (ZeilenWertMax), duerfen Zeilenumbrueche und Tabulatoren enthalten und sind laenger; jedes
        /// andere Steuerzeichen (auch NUL) bleibt verboten.
        /// </summary>
        static string FormPruefen(Sicherung si)
        {
            string g = WoerterbuchPruefen(si.Parameter, "parameter", null);
            return g ?? WoerterbuchPruefen(si.Vorher, "vorher", si.Kennung);
        }

        /// <summary>Hoechstlaenge eines mehrzeiligen Vorwerts je Kennung und Schluessel (Vertrag M4, Abschnitt 1); null = einzeilig wie alle anderen.</summary>
        static int? ZeilenWertMax(string kennung, string schluessel)
        {
            switch (kennung)
            {
                case Hosts: return schluessel == "inhalt" ? TextMax : (int?)null;        // ganzer alter hosts-Text
                case Ip: return schluessel == "adapter" ? ListeMax : (int?)null;         // je Adapter eine Zeile
                case Winsock: return schluessel == "katalog" ? ListeMax : (int?)null;    // netsh winsock show catalog (Beleg)
                case UpdateReset: return schluessel == "verborgen" ? ListeMax : (int?)null;   // UpdateID|Revision|Titel je Zeile (Beleg)
                default: return null;
            }
        }

        static string WoerterbuchPruefen(Dictionary<string, string> d, string was, string kennung)
        {
            foreach (var p in d)
            {
                if (!SchluesselGueltig(p.Key)) return "Die Sicherung enthält unter „" + was + "“ einen Schlüssel, der kein Name ist („" + Massnahmenbau.Kurz(p.Key) + "“).";
                if (p.Value == null) return "Die Sicherung enthält unter „" + was + "“ für „" + p.Key + "“ keinen Wert.";
                int? zeilen = kennung == null ? null : ZeilenWertMax(kennung, p.Key);
                int max = zeilen ?? WertMax;
                if (p.Value.Length > max) return "Der Wert „" + p.Key + "“ unter „" + was + "“ ist länger als " + max + " Zeichen.";
                foreach (char c in p.Value)
                {
                    if (!char.IsControl(c)) continue;
                    if (zeilen.HasValue && (c == '\n' || c == '\r' || c == '\t')) continue;
                    return "Der Wert „" + p.Key + "“ unter „" + was + "“ enthält Steuerzeichen.";
                }
            }
            return null;
        }

        static bool SchluesselGueltig(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > SchluesselMax) return false;
            foreach (char c in s)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        /// <summary>Vorwert startart eines Dienstes: gueltige Startart, und Disabled nie fuer einen Dienst aus NieDeaktivieren (Vertrag 8.2).</summary>
        static string StartartPruefen(string name, string startart)
        {
            string ziel = LeistungMassnahmen.StartartNorm(startart);
            if (ziel == null) return "Die Sicherung nennt keine gültige Startart (startart: Automatic, Manual, Disabled, Boot, System).";
            if (ziel == "Disabled" && LeistungMassnahmen.NieDeaktiviert(name))
                return "Die Sicherung würde den Dienst „" + name + "“ auf Deaktiviert stellen; er gehört zu Windows-Update, zum Schutz oder zum Kern von Windows und wird nicht zurückgespielt.";
            return null;
        }

        // ================================================================ Ausfuehren

        public static void Ausfuehren(PlanSchritt s, Ausfuehrungskontext k)
        {
            // Frisch lesen: zwischen Pruefung und Ausfuehrung liegt kein Eingriff, aber die Datei
            // ist der Vertrag, nicht ein Zwischenspeicher.
            Sicherung si = Sicherungsdatei.Lesen(s.Wert("sicherung"));
            string grund = InhaltPruefen(si);
            if (grund != null) throw new InvalidOperationException(grund);

            // Werte fuer den Kern-Nachweis rueckweg (Vertrag 8.3), vor der Verteilung: der Host reicht
            // sie in das Parameter-Woerterbuch des Nachweises, ohne die Datei selbst zu lesen.
            k.Werte["rueckweg.kennung"] = si.Kennung;
            foreach (var p in si.Parameter) k.Werte["rueckweg.parameter." + p.Key] = p.Value;

            k.Schreibe("   Sicherung vom " + Massnahmenbau.Kurz(si.Zeit ?? "?") + " für „" + Titel(si.Kennung) + "“ (" + Paare(si.Parameter) + ") wird zurückgespielt.", "dim");
            if (k.Protokoll != null)
                k.Protokoll.Schreibe(Protokoll.Helfer, Protokoll.Rueckweg, si.Kennung, "Rückweg aus Sicherung " + System.IO.Path.GetFileName(s.Wert("sicherung")),
                    new { zeit = si.Zeit, parameter = Paare(si.Parameter), vorher = Paare(si.Vorher) });

            switch (si.Kennung)
            {
                case Trim: TrimZurueck(k, si); break;
                case Autostart: AutostartZurueck(k, si); break;
                case Aufgabe: AufgabeZurueck(k, si); break;
                case Dienst: DienstZurueck(k, si); break;
                case Update: UpdateZurueck(k, si); break;
                case Geraet: GeraetZurueck(k, si); break;
                // M4 (Vertrag 3.3): dieselbe Verteilung wie in InhaltPruefen; die Bereiche pruefen die
                // Werte vor dem Eingriff noch einmal selbst.
                case Hosts:
                case Ip: MassnahmenM3.ZurueckNetz(k, si); break;
                case FirewallEingehend:
                case FirewallReset: MassnahmenM3.ZurueckSicherheit(k, si); break;
                case Winlogon: MassnahmenM3.ZurueckLeistung(k, si); break;
                default:
                    if (MassnahmenM3.ZurueckNetzBekannt(si.Kennung)) MassnahmenM3.ZurueckNetz(k, si);
                    else if (MassnahmenM3.ZurueckSicherheitBekannt(si.Kennung)) MassnahmenM3.ZurueckSicherheit(k, si);
                    else if (MassnahmenM3.ZurueckLeistungBekannt(si.Kennung)) MassnahmenM3.ZurueckLeistung(k, si);
                    else throw new InvalidOperationException("Für die Kennung „" + si.Kennung + "“ gibt es keinen Rückweg.");
                    break;
            }
        }

        // ---------------------------------------------------------------- TRIM

        // Die Sicherung traegt den Rohwert des DWORD; zurueckgestellt wird nur Bit 0 (NTFS), weil
        // fsutil ohne Dateisystem nur NTFS setzt und die Hin-Massnahme nur dieses Bit geaendert hat
        // (Datentraeger.cs TrimEinschalten). Bit 1 (ReFS) bleibt, wie es ist.
        static void TrimZurueck(Ausfuehrungskontext k, Sicherung si)
        {
            int vorherRoh = DatentraegerMassnahmen.TrimZahl(si.Wert(DatentraegerMassnahmen.TrimWert)).Value;   // InhaltPruefen hat es geprueft
            int ziel = DatentraegerMassnahmen.NtfsBit(vorherRoh);
            string zielWort = "NTFS-Bit " + ziel + " (TRIM für NTFS " + (ziel == 0 ? "an" : "aus") + ")";
            int? jetzt;
            bool lesbar = DatentraegerMassnahmen.TrimLesen(out jetzt);
            if (lesbar && DatentraegerMassnahmen.NtfsBit(jetzt) == ziel)
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "DisableDeleteNotification steht für NTFS schon wieder auf dem Vorwert " + zielWort + ", Rohwert vor der Maßnahme " + vorherRoh + "; es wurde nichts geändert.");
                return;
            }
            int exit = DatentraegerMassnahmen.TrimSetzen(k, ziel);
            if (k.Trocken)
            {
                Nachweise.Trocken(k, Kennung, "TRIM würde für NTFS auf den Vorwert gestellt (" + zielWort + ", Rohwert vor der Maßnahme " + vorherRoh + "; jetzt " + DatentraegerMassnahmen.TrimWort(lesbar, jetzt) + ").");
                return;
            }
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, Kennung, "fsutil meldete Exit " + exit + "; " + DatentraegerMassnahmen.TrimWort(lesbar, jetzt) + " bleibt.");
                return;
            }
            lesbar = DatentraegerMassnahmen.TrimLesen(out jetzt);
            if (lesbar && DatentraegerMassnahmen.NtfsBit(jetzt) == ziel) Nachweise.Bestanden(k, Kennung, "DisableDeleteNotification steht für NTFS wieder auf " + zielWort + " wie vor der Maßnahme (" + DatentraegerMassnahmen.TrimWort(true, jetzt) + ").");
            else Nachweise.NichtBestanden(k, Kennung, "fsutil meldete Erfolg, die Registrierung zeigt aber " + DatentraegerMassnahmen.TrimWort(lesbar, jetzt) + ".");
        }

        // ---------------------------------------------------------------- Startprogramm

        static void AutostartZurueck(Ausfuehrungskontext k, Sicherung si)
        {
            string hive = LeistungMassnahmen.HiveNorm(si.ParameterWert("hive"));
            string ort = LeistungMassnahmen.OrtNorm(si.ParameterWert("ort"));
            string name = LeistungMassnahmen.NamePruefen(si.ParameterWert("name"));
            byte[] vorher = LeistungMassnahmen.Bytes(si.Wert("bytes"));
            string vorherWort = vorher.Length == 0 ? "kein Eintrag (gilt als aktiv)" : "Byte 0 = " + vorher[0] + " (" + LeistungMassnahmen.Hex(vorher) + ")";

            // Zwei Tore vor dem Eingriff in HKCU: der Aufrufer muss das Helfer-Konto sein (UAC-Dialog
            // mit fremdem Konto), und die Sicherung muss aus dem Profil des Helfer-Kontos stammen
            // (Verlauf ist maschinenweit, Sicherung eines anderen Kontos). Beides vor dem Trockenlauf.
            if (!LeistungMassnahmen.KontoPruefen(k, hive, "Das Startprogramm gehört zum Benutzerprofil (HKCU).")) return;
            if (!LeistungMassnahmen.ProfilPruefen(k, hive, si.ParameterWert(LeistungMassnahmen.ProfilSchluessel))) return;

            byte[] jetzt = LeistungMassnahmen.ApprovedLesen(hive, ort, name);
            if (BytesGleich(jetzt, vorher))
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "„" + name + "“ steht schon wieder auf dem Vorwert (" + vorherWort + "); es wurde nichts geändert.");
                return;
            }
            if (k.Trocken)
            {
                k.Schreibe("(trocken) Registry " + hive + "\\" + LeistungMassnahmen.ApprovedBasis + ort + " Wert „" + name + "“ = " + (vorher.Length == 0 ? "löschen" : LeistungMassnahmen.Hex(vorher)), "dim");
                Nachweise.Trocken(k, Kennung, "„" + name + "“ würde auf den Vorwert gestellt (" + vorherWort + ").");
                return;
            }
            LeistungMassnahmen.ApprovedSchreiben(hive, ort, name, vorher);
            byte[] nach = LeistungMassnahmen.ApprovedLesen(hive, ort, name);
            if (BytesGleich(nach, vorher))
                Nachweise.Bestanden(k, Kennung, "„" + name + "“ startet wieder mit Windows wie vor der Maßnahme (" + vorherWort + ").");
            else
                Nachweise.NichtBestanden(k, Kennung, "Der Wert „" + name + "“ ließ sich nicht auf den Vorwert setzen.");
        }

        static bool BytesGleich(byte[] a, byte[] b)
        {
            int la = a == null ? 0 : a.Length, lb = b == null ? 0 : b.Length;
            if (la != lb) return false;
            for (int i = 0; i < la; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------------------------------------------------------------- Aufgabe

        static void AufgabeZurueck(Ausfuehrungskontext k, Sicherung si)
        {
            string pfad = LeistungMassnahmen.AufgabePfadNorm(si.ParameterWert("pfad"));
            bool warAktiv = Bool(si.Wert("aktiv")).Value;
            if (!warAktiv)
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "Die Aufgabe „" + pfad + "“ war vor der Maßnahme schon deaktiviert; es gibt nichts zurückzustellen.");
                return;
            }
            LeistungMassnahmen.AufgabeStand stand = LeistungMassnahmen.AufgabeLesen(pfad);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Die Aufgabe „" + pfad + "“ gibt es in der Aufgabenplanung nicht (mehr).", "bad");
                Katalog.Problem(k);
                return;
            }
            if (stand.Aktiv)
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "Die Aufgabe „" + pfad + "“ ist schon wieder aktiv; es wurde nichts geändert.");
                return;
            }
            int exit = LeistungMassnahmen.AufgabeSchalten(k, pfad, true);
            if (k.Trocken)
            {
                Nachweise.Trocken(k, Kennung, "Die Aufgabe „" + pfad + "“ würde wieder aktiviert (schtasks /Change /ENABLE).");
                return;
            }
            if (exit != 0)
            {
                Nachweise.NichtBestanden(k, Kennung, "schtasks meldete Exit " + exit + "; die Aufgabe „" + pfad + "“ bleibt deaktiviert.");
                return;
            }
            LeistungMassnahmen.AufgabeStand nach = LeistungMassnahmen.AufgabeLesen(pfad);
            if (nach.Gefunden && nach.Aktiv) Nachweise.Bestanden(k, Kennung, "Die Aufgabe „" + pfad + "“ ist wieder aktiv wie vor der Maßnahme.");
            else Nachweise.NichtBestanden(k, Kennung, "schtasks meldete Erfolg, die Aufgabenplanung zeigt „" + pfad + "“ aber weiter als deaktiviert.");
        }

        // ---------------------------------------------------------------- Dienst

        // Nur leistung.dienst.startart: ChangeStartMode auf den Vorwert. Die Hin-Massnahme startet
        // und stoppt nichts, der Rueckweg auch nicht; ein laufender Dienst laeuft bis zum naechsten
        // Neustart weiter. update.dienst.aktivieren kommt hier nicht an (InhaltPruefen, Vertrag 8.10).
        static void DienstZurueck(Ausfuehrungskontext k, Sicherung si)
        {
            string name = LeistungMassnahmen.DienstNameNorm(si.ParameterWert("name"));
            string ziel = LeistungMassnahmen.StartartNorm(si.Wert("startart"));
            LeistungMassnahmen.DienstStand stand = LeistungMassnahmen.DienstLesen(name);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Der Dienst „" + name + "“ ist nicht (mehr) vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string jetzt = LeistungMassnahmen.StartartNorm(stand.Startart) ?? stand.Startart ?? "";
            string zielWort = LeistungMassnahmen.StartartWort(ziel);
            if (jetzt == ziel)
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "Der Dienst „" + name + "“ hat schon wieder die Startart " + zielWort + "; es wurde nichts geändert.");
                return;
            }
            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI Win32_Service(" + name + ").ChangeStartMode(" + ziel + ")", "dim");
                Nachweise.Trocken(k, Kennung, "Der Dienst „" + name + "“ würde auf Startart " + zielWort + " zurückgestellt (jetzt " + LeistungMassnahmen.StartartWort(jetzt) + ").");
                return;
            }
            string fehler = LeistungMassnahmen.DienstStartartSetzen(k, name, ziel);
            if (fehler != null)
            {
                Nachweise.NichtBestanden(k, Kennung, fehler + " Die Startart bleibt " + LeistungMassnahmen.StartartWort(jetzt) + ".");
                return;
            }
            Nachweise.Bestanden(k, Kennung, "Der Dienst „" + name + "“ hat wieder die Startart " + zielWort + " wie vor der Maßnahme; ein laufender Dienst läuft bis zum nächsten Neustart weiter.");
        }

        // ---------------------------------------------------------------- Update

        static void UpdateZurueck(Ausfuehrungskontext k, Sicherung si)
        {
            string id = UpdatesMassnahmen.UpdateIdNorm(si.ParameterWert("updateId"));
            bool warVerborgen = Bool(si.Wert("isHidden")).Value;
            UpdatesMassnahmen.UpdateStand stand = UpdatesMassnahmen.ImCache(id, null);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Der Windows-Update-Agent kennt kein Update mit der Kennung " + id + " mehr; es gibt nichts zurückzustellen.", "bad");
                Katalog.Problem(k);
                return;
            }
            string titel = UpdatesMassnahmen.Anzeige(stand, id);
            if (stand.Verborgen == warVerborgen)
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "„" + titel + "“ ist schon wieder " + (warVerborgen ? "verborgen" : "sichtbar") + "; es wurde nichts geändert.");
                return;
            }
            if (k.Trocken)
            {
                k.Schreibe("(trocken) COM Microsoft.Update.Session: IUpdate(" + id + ").IsHidden = " + (warVerborgen ? "true" : "false"), "dim");
                Nachweise.Trocken(k, Kennung, "„" + titel + "“ würde wieder " + (warVerborgen ? "verborgen" : "eingeblendet") + " (IsHidden = " + (warVerborgen ? "true" : "false") + ").");
                return;
            }
            UpdatesMassnahmen.ImCache(id, warVerborgen);
            UpdatesMassnahmen.UpdateStand nach = UpdatesMassnahmen.ImCache(id, null);
            if (nach.Gefunden && nach.Verborgen == warVerborgen)
                Nachweise.Bestanden(k, Kennung, "„" + titel + "“ ist wieder " + (warVerborgen ? "verborgen" : "sichtbar") + " wie vor der Maßnahme; Windows Update bietet es " + (warVerborgen ? "nicht mehr" : "wieder") + " an.");
            else
                Nachweise.NichtBestanden(k, Kennung, "IsHidden ließ sich für „" + titel + "“ nicht auf " + (warVerborgen ? "true" : "false") + " setzen.");
        }

        // ---------------------------------------------------------------- Geraet

        static void GeraetZurueck(Ausfuehrungskontext k, Sicherung si)
        {
            string id = HardwareMassnahmen.InstanzIdNorm(si.ParameterWert("instanzId"));
            HardwareMassnahmen.GeraetStand stand = HardwareMassnahmen.Lesen(id);
            if (!stand.Gefunden)
            {
                k.Schreibe("   Ein Gerät mit der Instanz-Id „" + id + "“ ist nicht (mehr) vorhanden.", "bad");
                Katalog.Problem(k);
                return;
            }
            string name = HardwareMassnahmen.Anzeigename(stand, id);
            if (stand.Code == HardwareMassnahmen.CodeAbgeschaltet)
            {
                LeistungMassnahmen.SchonSo(k, Kennung, "„" + name + "“ ist schon wieder abgeschaltet (Problemcode 22); es wurde nichts geändert.");
                return;
            }
            if (k.Trocken)
            {
                k.Schreibe("(trocken) WMI Win32_PnPEntity(" + id + ").Disable()", "dim");
                Nachweise.Trocken(k, Kennung, "„" + name + "“ würde wieder abgeschaltet (Win32_PnPEntity.Disable).");
                return;
            }
            HardwareMassnahmen.SchaltErgebnis erg = HardwareMassnahmen.Schalten(id, false);
            if (erg.Rueckgabe != 0)
            {
                Nachweise.NichtBestanden(k, Kennung, "Disable meldete " + HardwareMassnahmen.Fehlertext(erg.Rueckgabe) + "; „" + name + "“ bleibt eingeschaltet.");
                return;
            }
            if (erg.Neustart)
            {
                k.Werte["neustart"] = "1";
                k.Schreibe("   Windows braucht 1 Neustart, damit „" + name + "“ abgeschaltet ist (rebootNeeded).", "warn");
            }
            HardwareMassnahmen.GeraetStand nach = HardwareMassnahmen.Lesen(id);
            if (nach.Gefunden && nach.Code == HardwareMassnahmen.CodeAbgeschaltet)
                Nachweise.Bestanden(k, Kennung, "„" + name + "“ ist wieder abgeschaltet (Problemcode 22) wie vor der Maßnahme.");
            else if (erg.Neustart)
                Nachweise.Bestanden(k, Kennung, "Disable wurde angenommen; „" + name + "“ meldet bis zum Neustart noch Code " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + ".");
            else
                Nachweise.NichtBestanden(k, Kennung, "Disable wurde angenommen, „" + name + "“ meldet aber Problemcode " + (nach.Gefunden ? nach.Code.ToString(CultureInfo.InvariantCulture) : "?") + " statt 22.");
        }

        // ---------------------------------------------------------------- Hilfen

        /// <summary>"1"/"true"/"ja" → true, "0"/"false"/"nein" → false, sonst null.</summary>
        static bool? Bool(string s)
        {
            if (s == null) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "1": case "true": case "ja": return true;
                case "0": case "false": case "nein": return false;
                default: return null;
            }
        }

        static string Titel(string kennung)
        {
            try
            {
                Kern.Massnahme m = Kern.Massnahmen.Finde(kennung);
                if (m != null && !string.IsNullOrEmpty(m.Titel)) return m.Titel;
            }
            catch (Exception) { }
            return kennung;
        }

        static string Paare(Dictionary<string, string> d)
        {
            var teile = new List<string>();
            foreach (var p in d) teile.Add(p.Key + "=" + Massnahmenbau.Kurz(p.Value));
            return teile.Count == 0 ? "ohne Parameter" : string.Join(", ", teile);
        }
    }
}
