namespace QrLabelPrinter.Models;

/// <summary>Von SAP übergebene Personendaten (werden nur angezeigt, nie verändert).</summary>
public sealed record SapUser(string Username, string FirstName, string LastName)
{
    /// <summary>SAP-Username in Großbuchstaben (Anzeige auf dem Etikett und QR-Code-Inhalt).</summary>
    public string LabelUsername => Username.ToUpperInvariant();
}

/// <summary>Druckerzuordnung eines PCs aus der zentralen CSV-Datei.</summary>
public sealed record PrinterConfig(
    string ComputerName,
    string PrinterName,
    string PrinterIp,
    int Port,
    int Dpi,
    double LabelWidthMm,
    double LabelHeightMm);

/// <summary>Beschriftungszeile auf dem Etikett (Positionen und Höhe in Drucker-Dots).</summary>
public sealed record LabelTextLine(string Text, int X, int Y, int FontHeight, int FieldWidth);

/// <summary>QR-Code auf dem Etikett (Position in Dots, Magnification = Dots pro Modul).</summary>
public sealed record LabelQr(string Data, int X, int Y, int Magnification, int ModuleCount);

/// <summary>
/// Gemeinsame Layout-Beschreibung für Vorschau und ZPL-Druck – so stimmen beide überein.
/// Alle Maße in Drucker-Dots.
/// </summary>
public sealed record LabelLayout(int WidthDots, int HeightDots, LabelQr Qr, IReadOnlyList<LabelTextLine> Lines);

/// <summary>Daten für die Etikettenvorschau.</summary>
public sealed record LabelPreviewModel(LabelLayout Layout, bool[,] QrMatrix);
