using System.Net.Sockets;
using System.Text;
using QrLabelPrinter.Configuration;
using QrLabelPrinter.Models;

namespace QrLabelPrinter.Services;

public sealed record ZplOptions(string Font, bool UseUtf8);

/// <summary>Sichere Kodierung von Textwerten für ZPL-Felder (^FH-Hex-Escapes).</summary>
public static class ZplEncoder
{
    /// <summary>
    /// Alles außer druckbarem ASCII sowie die ZPL-Sonderzeichen _ ^ ~ \ wird als _XX (Hex pro Byte) kodiert.
    /// Der Rückgabewert enthält daher nur harmloses ASCII und kann den ZPL-Befehl nicht beschädigen.
    /// Voraussetzung im ZPL: ^FH_ vor ^FD.
    /// </summary>
    public static string EscapeField(string text, bool utf8)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var rune in text.EnumerateRunes())
        {
            int v = rune.Value;
            if (v < 0x20 || v == 0x7F) { sb.Append(' '); continue; }
            if (v <= 0x7E && "_^~\\".IndexOf((char)v) < 0) { sb.Append((char)v); continue; }

            byte[] bytes = utf8
                ? Encoding.UTF8.GetBytes(rune.ToString())
                : Encoding.Latin1.GetBytes(rune.ToString());   // nicht darstellbar -> '?'
            foreach (byte b in bytes) sb.Append('_').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}

public static class ZplGenerator
{
    /// <summary>Erzeugt genau ein Etikett (^PQ1).</summary>
    public static string Generate(LabelLayout layout, ZplOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("^XA\n");
        sb.Append(options.UseUtf8 ? "^CI28\n" : "^CI27\n");
        sb.Append("^PW").Append(layout.WidthDots).Append('\n');
        sb.Append("^LL").Append(layout.HeightDots).Append('\n');
        sb.Append("^LH0,0\n");

        var q = layout.Qr;
        sb.Append("^FO").Append(q.X).Append(',').Append(q.Y)
          .Append("^BQN,2,").Append(q.Magnification)
          .Append("^FH_^FDQA,").Append(ZplEncoder.EscapeField(q.Data, options.UseUtf8)).Append("^FS\n");

        foreach (var line in layout.Lines)
        {
            sb.Append("^FO").Append(line.X).Append(',').Append(line.Y)
              .Append(FontCommand(options.Font, line.FontHeight))
              .Append("^FH_^FD").Append(ZplEncoder.EscapeField(line.Text, options.UseUtf8)).Append("^FS\n");
        }

        sb.Append("^PQ1\n");
        sb.Append("^XZ\n");
        return sb.ToString();
    }

    /// <summary>Bytes für die Übertragung. Der ZPL-Text ist reines ASCII (siehe EscapeField).</summary>
    public static byte[] ToBytes(string zpl) => Encoding.ASCII.GetBytes(zpl);

    private static string FontCommand(string font, int height) =>
        font.Length == 1
            ? $"^A{font}N,{height}"
            : $"^A@N,{height},{height},{font}";
}

public interface IPrinterClient
{
    Task SendAsync(string host, int port, byte[] data, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Sendet Rohdaten per TCP (Standard: Port 9100). Eine Verbindung = ein Auftrag, kein Retry.</summary>
public sealed class TcpPrinterClient : IPrinterClient
{
    public async Task SendAsync(string host, int port, byte[] data, TimeSpan timeout, CancellationToken ct = default)
    {
        string target = $"{host}:{port}";
        try
        {
            using var client = new TcpClient { NoDelay = true };

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(timeout);
                await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
            }

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(timeout);
            var stream = client.GetStream();
            await stream.WriteAsync(data, sendCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(sendCts.Token).ConfigureAwait(false);

            try { client.Client.Shutdown(SocketShutdown.Send); } catch (SocketException) { }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PrintException($"Der Zebra-Drucker ({target}) antwortet nicht (Zeitüberschreitung nach {timeout.TotalSeconds:0} s). Bitte Netzwerk, Drucker-IP und Druckerstatus prüfen.");
        }
        catch (SocketException ex)
        {
            throw new PrintException($"Keine Verbindung zum Zebra-Drucker ({target}): {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new PrintException($"Fehler beim Senden an den Zebra-Drucker ({target}): {ex.Message}", ex);
        }
    }
}

public interface ILabelPrintService
{
    /// <summary>Sendet genau ein Etikett an den Drucker.</summary>
    Task PrintOneAsync(SapUser user, PrinterConfig printer, CancellationToken ct = default);
}

public sealed class LabelPrintService : ILabelPrintService
{
    private readonly IPrinterClient _client;
    private readonly IQrCodeService _qr;
    private readonly Func<AppSettings> _getSettings;
    private readonly IAppLogger _log;

    public LabelPrintService(IPrinterClient client, IQrCodeService qr, Func<AppSettings> getSettings, IAppLogger log)
    {
        _client = client;
        _qr = qr;
        _getSettings = getSettings;
        _log = log;
    }

    public async Task PrintOneAsync(SapUser user, PrinterConfig printer, CancellationToken ct = default)
    {
        var settings = _getSettings();

        byte[] data;
        try
        {
            var matrix = _qr.CreateMatrix(user.LabelUsername);
            var layout = LabelLayoutCalculator.Calculate(user, printer, matrix.GetLength(0));
            string zpl = ZplGenerator.Generate(layout, new ZplOptions(settings.ZplFont, settings.UseUtf8));
            data = ZplGenerator.ToBytes(zpl);
        }
        catch (LabelLayoutException) { throw; }
        catch (Exception ex)
        {
            _log.Error("Fehler bei der ZPL-Erzeugung.", ex);
            throw new PrintException("Der Druckauftrag (ZPL) konnte nicht erzeugt werden: " + ex.Message, ex);
        }

        try
        {
            await _client.SendAsync(printer.PrinterIp, printer.Port, data,
                TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds), ct).ConfigureAwait(false);
        }
        catch (PrintException ex)
        {
            _log.Error($"Senden an {printer.PrinterIp}:{printer.Port} fehlgeschlagen.", ex);
            throw;
        }

        _log.Info($"1 Etikett ({data.Length} Bytes ZPL) an {printer.PrinterIp}:{printer.Port} gesendet.");
    }
}
