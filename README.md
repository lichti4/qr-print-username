# QrLabelPrinter

Windows-Anwendung (C#, WPF, .NET 10) zur Etikettenvorschau und zum Druck von QR-Code-Etiketten auf Zebra-Netzwerkdruckern (ZPL über TCP/IP, Port 9100).
Sie wird vom bestehenden SAP-ECC-Aufruf auf dem Frontend-PC gestartet. Es gibt **keine** SAP-Rückverbindung, keine AD-Abfrage und keine Änderung am SAP-System.

## Aufruf

```
QrLabelPrinter.exe --sap-user "MMUSTERMANN" --firstname "Max" --lastname "Mustermann"
```

* Auch `--sap-user=MMUSTERMANN` ist möglich. Unbekannte Zusatzargumente werden ignoriert.
* Werte werden unverändert übernommen (nur Trim und Unicode-Normalisierung, damit Umlaute einheitlich sind). Leerzeichen und Sonderzeichen in Namen bleiben erhalten.
* Prüfung: alle drei Parameter Pflicht; SAP-Username 1–12 sichtbare ASCII-Zeichen ohne Leerzeichen; Namen max. 40 Zeichen ohne Steuerzeichen. Bei Fehlern erscheint eine Meldung, **Drucken ist dann gesperrt**.

## Ablauf und Verhalten

1. Parameter lesen → PC-Name (`Environment.MachineName`) → Druckerzuordnung aus der zentralen CSV lesen (einmal beim Start).
2. Fenster zeigt SAP-Username, Vorname, Nachname (nur Anzeige), PC-Name, Drucker, IP:Port und die Etikettenvorschau (mit echtem QR-Code).
3. Gedruckt wird **nur** nach Klick auf **„Ein Etikett drucken“**. Pro Klick genau ein Etikett: ZPL enthält `^PQ1`, es wird ein einziger Sendevorgang ohne automatischen Retry ausgeführt, der Button ist währenddessen gesperrt.
4. Etikett: Username (Großbuchstaben), Nachname, Vorname, QR-Code mit **ausschließlich** dem Username in Großbuchstaben (`MMUSTERMANN`).

> **Wichtig:** Eine erfolgreiche TCP-Übertragung bestätigt nur, dass die Daten an den Drucker gesendet wurden – nicht, dass ein Etikett physisch gedruckt wurde (Papierende, Pause, Kopf offen usw.). Die Anwendung weist im Fenster darauf hin.

## Konfiguration

### `appsettings.json` (liegt neben der EXE)

```json
{
  "PrinterMappingPath": "\\\\SERVER\\Freigabe\\QrLabelPrinter\\PrinterMapping.csv",
  "ConnectTimeoutSeconds": 5,
  "MappingReadTimeoutSeconds": 10,
  "ZplFont": "0",
  "UseUtf8": false
}
```

| Schlüssel | Bedeutung |
|---|---|
| `PrinterMappingPath` | UNC-Pfad (oder lokaler Pfad) zur CSV. Pflicht. JSON: Backslashes verdoppeln. |
| `ConnectTimeoutSeconds` | Timeout für Verbindungsaufbau und Senden an den Drucker (1–120). |
| `MappingReadTimeoutSeconds` | Timeout beim Lesen der CSV vom Share (1–300). |
| `ZplFont` | Ein Zeichen = eingebaute Zebra-Schrift (Standard `0`, skalierbar). Länger = Schriftdatei auf dem Drucker, z. B. `E:ARIAL.TTF` (dann `^A@N`). |
| `UseUtf8` | `true` → `^CI28` (UTF-8), `false` → `^CI27` (Windows-1252/Latin-1) für Drucker/Firmware ohne UTF-8. |

### `PrinterMapping.csv` (zentral auf dem Netzwerkshare)

Trennzeichen `;`, UTF-8, Kopfzeile Pflicht (Beispiel: [samples/PrinterMapping.csv](samples/PrinterMapping.csv)):

```
ComputerName;PrinterName;PrinterIP;Port;DPI;LabelWidthMm;LabelHeightMm
CLIENT-001;Zebra-Lager;10.10.10.51;9100;203;50;30
```

* Zeilen mit `#` am Anfang und Leerzeilen werden ignoriert; Dezimalkomma bei Maßen ist erlaubt (`50,5`).
* PC-Name: Vergleich ohne Beachtung der Groß-/Kleinschreibung. Mehrere Zeilen für denselben PC → Fehler (nicht eindeutig).
* Validiert wird nur die Zeile des aktuellen PCs: IP (volle IPv4-/IPv6-Adresse), Port 1–65535, DPI 100–1200, Breite/Höhe > 0 und ≤ 500 mm.
* Fehlt die Datei, der PC-Eintrag oder ist ein Wert ungültig, erscheint eine verständliche Meldung und der Druck ist gesperrt.
* Leseberechtigung auf den Share für alle Anwender nötig; Schreibrecht nur für Administratoren.

## Etikett und ZPL

* Etikettengröße, DPI → Dots: `dots = mm / 25,4 × dpi` (kaufmännisch gerundet). Beispiel 50×30 mm bei 203 dpi = 400×240 Dots.
* Vorschau und ZPL werden aus **demselben Layout** (`LabelLayoutCalculator`) erzeugt. Die Schrifthöhe wird automatisch verkleinert, wenn lange Namen nicht in die Textspalte passen.
* QR-Code: `^BQN,2,<Magnification>` mit Fehlerkorrektur Q (`^FDQA,<Username>`).
* Alle Textwerte werden mit `^FH_` und Hex-Escapes (`_C3_BC` für „ü“) kodiert. Dadurch ist der ZPL-Text reines ASCII; `^`, `~`, `_`, `\` und Steuerzeichen in Namen können den Befehl nicht beschädigen.

Beispiel (50×30 mm, 203 dpi):

```
^XA
^CI28
^PW400
^LL240
^LH0,0
^FO16,36^BQN,2,8^FH_^FDQA,MMUSTERMANN^FS
^FO200,..^A0N,27^FH_^FDMMUSTERMANN^FS
^FO200,..^A0N,36^FH_^FDMustermann^FS
^FO200,..^A0N,36^FH_^FDMax^FS
^PQ1
^XZ
```

## Noch am echten Drucker zu klären

Diese Punkte lassen sich nur am konkreten Zebra-Modell prüfen und ggf. über `appsettings.json` bzw. die CSV anpassen:

* Druckmodell, DPI und Etikettenmaße → CSV.
* Schrift: Standard ist die eingebaute Schrift `0`. Umlaute und andere Zeichen mit `^CI28` testen; falls die Firmware kein UTF-8 kann, `UseUtf8: false` setzen. Für nicht lateinische Zeichen ist eine TrueType-Schrift auf dem Drucker nötig (`ZplFont`).
* Die Vorschau-Schrift (Arial) ist nur eine Näherung der Druckerschrift; Zeichenbreiten werden für die automatische Verkleinerung mit 0,6 × Schrifthöhe geschätzt.
* Der Drucker wählt die QR-Version selbst; bei kurzen Usernames (≤ 11 Zeichen) ist das Version 1 (21 Module), wie in der Vorschau. Bei Abweichung ändert sich nur die Codegröße geringfügig.
* Ggf. Medientyp/Sensor (`^MN`, `^MT`) und Dunkelheit (`~SD`) – bewusst nicht gesetzt, damit die Druckerkonfiguration maßgeblich bleibt.

## Protokollierung

Tägliche Logdateien unter `%LocalAppData%\QrLabelPrinter\Logs\yyyy-MM-dd.log` (30 Tage Aufbewahrung). Protokolliert werden Start, Konfigurationsfehler, Zieladresse/Port und Sendeergebnis. **Keine** Namen oder SAP-Usernames.

## Projektstruktur

```
QrLabelPrinter.slnx
src/QrLabelPrinter.Core      plattformneutral (net10.0), vollständig testbar
  Models/                    SapUser, PrinterConfig, Layout-Modelle
  Configuration/             AppSettings + Loader
  Services/                  CommandLineParser, PrinterMapping (CSV, Validator, Source),
                             QrCodeService (QRCoder), LabelLayoutCalculator, ZplGenerator/ZplEncoder,
                             TcpPrinterClient, LabelPrintService, Logging
  ViewModels/                MainViewModel, AsyncRelayCommand
src/QrLabelPrinter           WPF-Exe (net10.0-windows)
  Views/                     MainWindow, LabelPreviewControl
  Resources/                 Styles.xaml
  appsettings.json
tests/QrLabelPrinter.Tests   xUnit (Mock-Drucker FakePrinterClient + echter Loopback-TCP-Test)
samples/PrinterMapping.csv
```

## Bauen, Testen, Veröffentlichen

Voraussetzung: .NET 10 SDK (Visual Studio 2022 17.13+/2026 öffnet `QrLabelPrinter.slnx`; alternativ per CLI).

```bash
dotnet build QrLabelPrinter.slnx -c Release
dotnet test tests/QrLabelPrinter.Tests
```

Die Tests laufen auch unter macOS/Linux, die WPF-Anwendung nur unter Windows.

### Eigenständige EXE

```bash
dotnet publish src/QrLabelPrinter/QrLabelPrinter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

oder in Visual Studio: Rechtsklick auf das Projekt → Veröffentlichen → Profil `SingleFile-win-x64`.

Ergebnis in `publish/win-x64/`: `QrLabelPrinter.exe` (enthält die .NET-Laufzeit, keine Installation nötig) und **daneben** `appsettings.json`. Beides auf den Ziel-PC bzw. eine Softwareverteilung kopieren; den Pfad in `appsettings.json` anpassen. Für framework-abhängige Variante (kleiner, .NET-10-Desktop-Runtime nötig): `--self-contained false`.

### Test ohne SAP

```
QrLabelPrinter.exe --sap-user "mmustermann" --firstname "Max" --lastname "Mustermann"
```

Für einen ersten Test ohne Drucker kann die CSV auf einen lokalen Pfad zeigen und die IP auf einen beliebigen Rechner mit offenem Port 9100 (z. B. `ncat -l 9100`) – oder auf eine nicht erreichbare IP, um die Fehlermeldung zu sehen.
