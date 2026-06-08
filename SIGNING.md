# Code-Signing

Die `.exe` lässt sich Authenticode-signieren – komplett mit PowerShell, **ohne SDK**.

## Was Signieren bringt – und was nicht

| | Signatur | SmartScreen-Warnung weg |
|---|---|---|
| **Selbst-signiert** | ✔ Integrität + Herausgebername | nur auf Rechnern, die das Zertifikat kennen |
| **OV-Zertifikat (gekauft)** | ✔ | erst nach aufgebauter Reputation |
| **EV-Zertifikat (gekauft, HSM/Token)** | ✔ | **sofort für alle** |

> **Wichtig:** SmartScreen ist *reputationsbasiert*, nicht signaturbasiert. Ein selbst-signiertes Zertifikat entfernt die SmartScreen-Warnung **nicht** für fremde Downloader – nur auf deinen eigenen Maschinen (nachdem das Zertifikat dort vertraut wird). Für sofortiges Vertrauen bei allen braucht es ein **EV-Code-Signing-Zertifikat**.

Der Aufwand ist trotzdem nicht umsonst: Die Pipeline steht. Wenn du später ein echtes Zertifikat kaufst, tauschst du nur die PFX – sonst ändert sich nichts.

## Einmalig: Dev-Zertifikat erzeugen

```powershell
# erzeugt cert\WindowsWartung.pfx und macht es auf DIESEM Rechner vertrauenswürdig
.\tools\make-cert.ps1 -Trust
```

`cert\` ist über `.gitignore` ausgeschlossen – PFX/Schlüssel landen **nie** im Repo.

## Bauen + signieren

```powershell
.\build.ps1 -Release -Sign
```

oder eine vorhandene Exe direkt signieren:

```powershell
.\sign.ps1
```

Prüfen:

```powershell
Get-AuthenticodeSignature .\bin\WindowsWartung.exe | Format-List Status, SignerCertificate
```

## In der GitHub-Action

Der Release-Workflow signiert automatisch, **wenn** zwei Repository-Secrets gesetzt sind:

- `CODESIGN_PFX_BASE64` – die PFX als Base64
- `CODESIGN_PASSWORD` – das PFX-Passwort

PFX nach Base64 (lokal):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert\WindowsWartung.pfx")) | Set-Clipboard
```

Ohne Secrets baut der Workflow einfach unsigniert weiter.

## Dev-Zertifikat wieder entfernen

```powershell
Get-ChildItem Cert:\CurrentUser\My, Cert:\CurrentUser\Root, Cert:\CurrentUser\TrustedPublisher |
  Where-Object Subject -like "*Windows-Wartung*" | Remove-Item
```
