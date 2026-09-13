using System;
using System.Collections.Generic;
using System.Management;
using WartungsToolbox.Kern;

namespace WartungsToolbox.Sammler.Quellen
{
    /// <summary>
    /// Fuellt s.Geraete aus Win32_PnPEntity (root\cimv2) - ohne Skript-Interpreter, ohne Prozessstart, ohne
    /// Get-PnpDevice. Das Cmdlet ist nur eine cdxml-Huelle derselben Klasse.
    ///
    /// Zwei Stufen, weil Zeit zaehlt: die Grundabfrage liefert alle praesenten Geraete
    /// (hier 258 in 125 ms) mit DeviceID, Klasse, Name, Present und ConfigManagerErrorCode.
    /// Nur fuer Geraete mit ConfigManagerErrorCode != 0 folgt GetDeviceProperties mit den
    /// neun DEVPKEYs (Parent, ProblemCode, ProblemStatus, DevNodeStatus, Treiber, ConfigFlags);
    /// gesunden Geraeten reicht die Grundabfrage.
    ///
    /// Fallen aus der Recherche (R1-17/18/21/22) und der Widerlegungsrunde:
    ///  - WMI liefert nur praesente Geraete. Phantome (Code 45) sieht man hier nie - also
    ///    keine Code-45-Fehlalarme, aber auch keine Karteileichen. Wer die will, braucht
    ///    CfgMgr32 (CM_Locate_DevNodeW mit PHANTOM-Flag), nicht diese Quelle.
    ///  - Elternschaft NUR aus DEVPKEY_Device_Parent. Der Instanzpfad verraet sie nicht:
    ///    die iGPU dieses PCs (...\4&amp;1EBE6A9C&amp;0&amp;0041) haengt am PCI-Root-Port
    ///    PCI\VEN_1022&amp;DEV_14DD...\3&amp;11583659&amp;0&amp;41, nichts davon steckt im Kindpfad.
    ///  - PNPClass fehlt auf aelteren Builds trotz MOF (Doku-Warnung): null-tolerant lesen.
    ///  - DEVPKEY_Device_ProblemCode ist UINT32 (Typcode 7), nicht INT32 wie die Doku sagt.
    ///  - DriverDate kommt als CIM-Datum (Typcode 16, DMTF-String), nicht als Text.
    ///  - Ein Schluessel ohne Wert (Children bei einem Blatt, Typcode 0) wirft beim Zugriff auf
    ///    Data "Nicht gefunden": Feld fehlt, kein Fehler.
    ///  - Die Kernel-PnP-Historie (411/219) liest die Ereignisquelle, nicht diese.
    /// </summary>
    public static class Geraete
    {
        const string Cimv2 = @"root\cimv2";
        /// <summary>Viele Geraete, langsamer Provider: die Grundabfrage bekommt mehr Zeit als 5 s.</summary>
        const int GrundabfrageZeitMs = 15000;
        const int EigenschaftenZeitMs = 5000;

        // DEVPROP_TYPE aus devpropdef.h - die WMI-Methode liefert die Zahl, nicht den Namen.
        const int TypLeer = 0;          // DEVPROP_TYPE_EMPTY
        const int TypUInt32 = 7;        // DEVPROP_TYPE_UINT32 (auch ProblemCode, DevNodeStatus, ConfigFlags)
        const int TypFileTime = 16;     // DEVPROP_TYPE_FILETIME (DriverDate als CIM-Datum)
        const int TypString = 18;       // DEVPROP_TYPE_STRING
        const int TypNtStatus = 24;     // DEVPROP_TYPE_NTSTATUS (ProblemStatus)

        static readonly string[] Schluessel =
        {
            "DEVPKEY_Device_Parent", "DEVPKEY_Device_ProblemCode", "DEVPKEY_Device_ProblemStatus",
            "DEVPKEY_Device_DevNodeStatus", "DEVPKEY_Device_DriverVersion", "DEVPKEY_Device_DriverDate",
            "DEVPKEY_Device_DriverProvider", "DEVPKEY_Device_DriverInfPath", "DEVPKEY_Device_ConfigFlags",
        };

        public static void Erfassen(Systembild s)
        {
            List<ManagementObject> liste = null;
            if (!Sammler.Versuch(s, "wmi.pnp", () =>
            {
                liste = Wmi.Abfrage(Cimv2, "SELECT DeviceID,ClassGuid,PNPClass,Name,Present,ConfigManagerErrorCode FROM Win32_PnPEntity", GrundabfrageZeitMs);
                // Ein PC ohne ein einziges PnP-Geraet gibt es nicht; eine leere Liste ist ein
                // Providerfehler und darf nicht als "alle Geraete gesund" durchgehen.
                if (liste.Count == 0) throw new InvalidOperationException("Win32_PnPEntity lieferte keine Geräte");
            })) return;

            foreach (var mo in liste)
            {
                var g = new Geraet
                {
                    InstanzId = Wmi.Str(mo, "DeviceID"),
                    KlasseGuid = Wmi.Str(mo, "ClassGuid"),
                    Klasse = Wmi.Str(mo, "PNPClass"),        // null auf Builds ohne die Eigenschaft
                    Name = Wmi.Str(mo, "Name"),              // lokalisiert, nur Anzeige
                    // Present gibt es ab Windows 10; davor liefert WMI ohnehin nur Praesente.
                    Present = Wmi.Wahr(mo, "Present") ?? true,
                    ProblemCode = Wmi.Ganz(mo, "ConfigManagerErrorCode") ?? 0,
                };
                if (string.IsNullOrEmpty(g.InstanzId)) continue;

                if (g.ProblemCode != 0)
                {
                    // Je Geraet mit Problem ein eigener Versuch: scheitert einer (Zeit, Provider),
                    // bleibt das Geraet mit seinem Code in der Liste, nur ohne Einzelheiten.
                    Sammler.Versuch(s, "wmi.pnp.eigenschaften", () => EigenschaftenLesen(mo, g));
                }
                s.Geraete.Add(g);
            }
        }

        /// <summary>
        /// Win32_PnPEntity.GetDeviceProperties(devicePropertyKeys[]) -> deviceProperties[] als
        /// ManagementBaseObject[] mit KeyName, Type und Data (gemessen ohne Rechte, ReturnValue 0).
        /// </summary>
        static void EigenschaftenLesen(ManagementObject mo, Geraet g)
        {
            var inParams = mo.GetMethodParameters("GetDeviceProperties");
            inParams["devicePropertyKeys"] = Schluessel;
            var outParams = MitZeitgrenze(() => mo.InvokeMethod("GetDeviceProperties", inParams, null), EigenschaftenZeitMs, g.InstanzId);

            long rueckgabe = Wmi.Lang(outParams, "ReturnValue") ?? -1;
            if (rueckgabe != 0) throw new InvalidOperationException("GetDeviceProperties lieferte " + rueckgabe + " für " + g.InstanzId);
            var eigenschaften = outParams["deviceProperties"] as ManagementBaseObject[];
            if (eigenschaften == null) throw new InvalidOperationException("GetDeviceProperties ohne deviceProperties für " + g.InstanzId);

            foreach (var p in eigenschaften)
            {
                string key = Wmi.Str(p, "KeyName");
                int typ = Wmi.Ganz(p, "Type") ?? TypLeer;
                if (key == null || typ == TypLeer) continue;   // Schluessel ohne Wert: Feld bleibt leer
                switch (key)
                {
                    case "DEVPKEY_Device_Parent": if (typ == TypString) g.Parent = Wmi.Str(p, "Data"); break;
                    case "DEVPKEY_Device_ProblemCode":
                        // Der DEVPKEY ist die Quelle des Konfigurations-Managers selbst; der Wert
                        // aus der Grundabfrage bleibt, falls der Schluessel fehlt.
                        if (typ == TypUInt32) { int? c = Wmi.Ganz(p, "Data"); if (c.HasValue) g.ProblemCode = c.Value; }
                        break;
                    case "DEVPKEY_Device_ProblemStatus": if (typ == TypNtStatus || typ == TypUInt32) g.ProblemStatus = Wmi.Lang(p, "Data"); break;
                    case "DEVPKEY_Device_DevNodeStatus": if (typ == TypUInt32) g.DevNodeStatus = Wmi.Lang(p, "Data"); break;
                    case "DEVPKEY_Device_DriverVersion": if (typ == TypString) g.TreiberVersion = Wmi.Str(p, "Data"); break;
                    case "DEVPKEY_Device_DriverDate": if (typ == TypFileTime) g.TreiberDatumUtc = Wmi.ZeitUtc(p, "Data"); break;
                    case "DEVPKEY_Device_DriverProvider": if (typ == TypString) g.TreiberAnbieter = Wmi.Str(p, "Data"); break;
                    case "DEVPKEY_Device_DriverInfPath": if (typ == TypString) g.TreiberInf = Wmi.Str(p, "Data"); break;
                    case "DEVPKEY_Device_ConfigFlags": if (typ == TypUInt32) g.ConfigFlags = Wmi.Lang(p, "Data"); break;
                }
            }
        }

        /// <summary>
        /// InvokeMethod hat keine Zeitgrenze; derselbe Kunstgriff wie in Wmi.Abfrage: eigener
        /// Hintergrund-Thread, nach Ablauf TimeoutException (fehlerliste:zeit), der Thread
        /// darf im Hintergrund verhungern.
        /// </summary>
        static ManagementBaseObject MitZeitgrenze(Func<ManagementBaseObject> f, int zeitMs, string wofuer)
        {
            ManagementBaseObject ergebnis = null;
            Exception fehler = null;
            var t = new System.Threading.Thread(() =>
            {
                try { ergebnis = f(); }
                catch (Exception ex) { fehler = ex; }
            }) { IsBackground = true, Name = "wmi:GetDeviceProperties" };
            t.Start();
            if (!t.Join(zeitMs)) throw new TimeoutException("GetDeviceProperties überschritt " + zeitMs + " ms für " + wofuer);
            if (fehler != null) throw fehler;
            return ergebnis;
        }
    }
}
