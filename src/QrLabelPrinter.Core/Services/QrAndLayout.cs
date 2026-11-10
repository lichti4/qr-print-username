using QRCoder;
using QrLabelPrinter.Models;

namespace QrLabelPrinter.Services;

public static class DotConverter
{
    /// <summary>Millimeter -> Drucker-Dots (dots = mm / 25,4 * dpi, kaufmännisch gerundet).</summary>
    public static int MmToDots(double mm, int dpi)
    {
        if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (mm < 0) throw new ArgumentOutOfRangeException(nameof(mm));
        return (int)Math.Round(mm / 25.4 * dpi, MidpointRounding.AwayFromZero);
    }
}

public interface IQrCodeService
{
    /// <summary>Liefert die QR-Module ohne Ruhezone (true = dunkel), quadratisch.</summary>
    bool[,] CreateMatrix(string data);
}

public sealed class QrCodeService : IQrCodeService
{
    public bool[,] CreateMatrix(string data)
    {
        if (string.IsNullOrEmpty(data)) throw new ArgumentException("QR-Code-Inhalt fehlt.", nameof(data));

        using var generator = new QRCodeGenerator();
        // Fehlerkorrektur Q – entspricht dem 'Q' in ^FDQA, des ZPL-Codes.
        using var qr = generator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);

        int size = 17 + 4 * qr.Version;                       // Module ohne Ruhezone
        int offset = (qr.ModuleMatrix.Count - size) / 2;      // Ruhezone, falls in der Matrix enthalten
        if (offset < 0) { offset = 0; size = qr.ModuleMatrix.Count; }

        var result = new bool[size, size];
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                result[r, c] = qr.ModuleMatrix[r + offset][c + offset];
        return result;
    }
}

/// <summary>Berechnet das Etikettenlayout in Dots – Grundlage für Vorschau und ZPL.</summary>
public static class LabelLayoutCalculator
{
    private const double CharWidthFactor = 0.6;   // grobe Zeichenbreite relativ zur Schrifthöhe
    private const int MinFontHeight = 6;

    public static LabelLayout Calculate(SapUser user, PrinterConfig printer, int qrModuleCount)
    {
        if (qrModuleCount <= 0) throw new ArgumentOutOfRangeException(nameof(qrModuleCount));

        int dpi = printer.Dpi;
        int w = DotConverter.MmToDots(printer.LabelWidthMm, dpi);
        int h = DotConverter.MmToDots(printer.LabelHeightMm, dpi);
        if (w <= 0 || h <= 0)
            throw new LabelLayoutException("Die Etikettenmaße ergeben keine druckbare Fläche.");

        int margin = Math.Max(4, DotConverter.MmToDots(2.0, dpi));
        // Ruhezone: 4 Module rund um den QR-Code müssen auf das Etikett passen.
        const int QuietModules = 4;
        int totalModules = qrModuleCount + 2 * QuietModules;
        int maxBlock = Math.Min(h, (int)(w * 0.5));
        int mag = Math.Clamp(maxBlock / totalModules, 1, 10);
        if (mag * qrModuleCount > h - 2)
            throw new LabelLayoutException("Das Etikett ist zu klein für den QR-Code. Bitte Etikettenmaße und DPI in der Druckerzuordnung prüfen.");

        int quiet = mag * QuietModules;
        int qrSize = mag * qrModuleCount;
        int qrX = Math.Max(margin, quiet);
        int qrY = (h - qrSize) / 2;

        int textX = qrX + qrSize + quiet;
        int textWidth = w - textX - margin;
        if (textWidth < 40)
            throw new LabelLayoutException("Auf dem Etikett bleibt neben dem QR-Code zu wenig Platz für den Text.");

        string[] texts = [user.LabelUsername, user.LastName, user.FirstName];
        double[] weights = [0.22, 0.16, 0.16];

        int gap = Math.Max(2, (int)(h * 0.04));
        int available = h - 2 * margin - 2 * gap;
        if (available < texts.Length * MinFontHeight)
            throw new LabelLayoutException("Das Etikett ist zu niedrig für drei Textzeilen.");

        var desired = weights.Select(x => (int)(x * h)).ToArray();
        int sum = desired.Sum();
        if (sum > available)
            desired = desired.Select(d => Math.Max(MinFontHeight, (int)((long)d * available / sum))).ToArray();

        var heights = new int[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            int fit = (int)(textWidth / (Math.Max(1, texts[i].Length) * CharWidthFactor));
            heights[i] = Math.Max(MinFontHeight, Math.Min(desired[i], fit));
        }

        int total = heights.Sum() + 2 * gap;
        int y = Math.Max(margin, (h - total) / 2);

        var lines = new List<LabelTextLine>();
        for (int i = 0; i < texts.Length; i++)
        {
            lines.Add(new LabelTextLine(texts[i], textX, y, heights[i], textWidth));
            y += heights[i] + gap;
        }

        return new LabelLayout(w, h, new LabelQr(user.LabelUsername, qrX, qrY, mag, qrModuleCount), lines);
    }
}
