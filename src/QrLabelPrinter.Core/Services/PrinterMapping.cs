using System.Globalization;
using System.Net;
using System.Net.Sockets;
using QrLabelPrinter.Configuration;
using QrLabelPrinter.Models;

namespace QrLabelPrinter.Services;

/// <summary>Validiert die Rohwerte einer CSV-Zeile und erzeugt eine <see cref="PrinterConfig"/>.</summary>
public static class PrinterConfigValidator
{
    public const int MinDpi = 100;
    public const int MaxDpi = 1200;
    public const double MaxLabelMm = 500;

    public static PrinterConfig Create(
        string computerName, string printerName, string ip, string port, string dpi, string widthMm, string heightMm)
    {
        string pc = computerName.Trim();
        string who = $"PC '{pc}'";

        string ipText = ip.Trim();
        if (!TryParseIp(ipText, out var address))
            throw new ConfigException($"Ungültige Drucker-IP-Adresse '{ipText}' in der Druckerzuordnung für {who}.");

        if (!int.TryParse(port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int portValue)
            || portValue is < 1 or > 65535)
            throw new ConfigException($"Ungültiger Port '{port.Trim()}' in der Druckerzuordnung für {who} (erlaubt: 1–65535).");

        if (!int.TryParse(dpi.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int dpiValue)
            || dpiValue is < MinDpi or > MaxDpi)
            throw new ConfigException($"Ungültige oder fehlende DPI '{dpi.Trim()}' in der Druckerzuordnung für {who} (z. B. 203 oder 300).");

        double w = ParseMm(widthMm, "LabelWidthMm", who);
        double h = ParseMm(heightMm, "LabelHeightMm", who);

        string name = string.IsNullOrWhiteSpace(printerName) ? address.ToString() : printerName.Trim();
        return new PrinterConfig(pc, name, address.ToString(), portValue, dpiValue, w, h);
    }

    private static double ParseMm(string text, string column, string who)
    {
        string t = text.Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            || double.IsNaN(v) || double.IsInfinity(v) || v <= 0 || v > MaxLabelMm)
            throw new ConfigException($"Ungültiger oder fehlender Wert '{text.Trim()}' für {column} in der Druckerzuordnung für {who} (erwartet: Millimeter, größer 0).");
        return v;
    }

    private static bool TryParseIp(string text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(text) || !IPAddress.TryParse(text, out var parsed)) return false;

        if (parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPAddress.TryParse akzeptiert auch Kurzformen wie "1" – hier nur volle Dotted-Quad-Notation.
            if (text.Count(c => c == '.') != 3) return false;
            if (parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.Broadcast)) return false;
        }
        else if (parsed.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (parsed.Equals(IPAddress.IPv6Any)) return false;
        }
        else return false;

        address = parsed;
        return true;
    }
}

/// <summary>Liest die zentrale CSV (Trennzeichen ';') und findet die Zeile des aktuellen PCs.</summary>
public static class PrinterMappingCsv
{
    public static readonly string[] RequiredColumns =
        ["ComputerName", "PrinterName", "PrinterIP", "Port", "DPI", "LabelWidthMm", "LabelHeightMm"];

    public static PrinterConfig FindForComputer(string csvText, string computerName, string? sourceName = null)
    {
        string where = sourceName is null ? "in der Druckerzuordnung" : $"in '{sourceName}'";

        var lines = csvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .ToList();

        if (lines.Count == 0)
            throw new ConfigException($"Die Druckerzuordnung {where} ist leer.");

        var header = lines[0].TrimStart('﻿').Split(';').Select(h => h.Trim()).ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++) index.TryAdd(header[i], i);

        var missing = RequiredColumns.Where(c => !index.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new ConfigException($"Die Kopfzeile der Druckerzuordnung {where} ist unvollständig. Es fehlen die Spalten: {string.Join(", ", missing)}.");

        string wanted = computerName.Trim();
        var matches = new List<string[]>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(';');
            string pc = Cell(cells, index["ComputerName"]).Trim();
            if (string.Equals(pc, wanted, StringComparison.OrdinalIgnoreCase)) matches.Add(cells);
        }

        if (matches.Count == 0)
            throw new ConfigException($"Für diesen PC ('{wanted}') ist {where} kein Zebra-Drucker hinterlegt. Bitte den Administrator kontaktieren.");
        if (matches.Count > 1)
            throw new ConfigException($"Für diesen PC ('{wanted}') gibt es {where} mehrere Einträge. Die Zuordnung ist nicht eindeutig.");

        var row = matches[0];
        return PrinterConfigValidator.Create(
            Cell(row, index["ComputerName"]),
            Cell(row, index["PrinterName"]),
            Cell(row, index["PrinterIP"]),
            Cell(row, index["Port"]),
            Cell(row, index["DPI"]),
            Cell(row, index["LabelWidthMm"]),
            Cell(row, index["LabelHeightMm"]));
    }

    private static string Cell(string[] cells, int i) => i < cells.Length ? cells[i] : "";
}

public interface IPrinterConfigSource
{
    Task<PrinterConfig> GetForComputerAsync(string computerName, CancellationToken ct = default);
}

/// <summary>Lädt die CSV vom konfigurierten Pfad (Netzwerkshare) mit Timeout.</summary>
public sealed class CsvPrinterConfigSource : IPrinterConfigSource
{
    private readonly Func<AppSettings> _getSettings;
    private readonly IAppLogger _log;

    public CsvPrinterConfigSource(Func<AppSettings> getSettings, IAppLogger log)
    {
        _getSettings = getSettings;
        _log = log;
    }

    public async Task<PrinterConfig> GetForComputerAsync(string computerName, CancellationToken ct = default)
    {
        var settings = _getSettings();
        string path = settings.PrinterMappingPath;

        // Dateizugriffe auf nicht erreichbare Shares können blockieren und sind nicht abbrechbar -> Task.Run + Timeout.
        var read = Task.Run(() => File.ReadAllText(path, System.Text.Encoding.UTF8), ct);
        var timeout = Task.Delay(TimeSpan.FromSeconds(settings.MappingReadTimeoutSeconds), ct);
        var finished = await Task.WhenAny(read, timeout).ConfigureAwait(false);

        if (finished != read)
        {
            ct.ThrowIfCancellationRequested();
            _ = read.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            _log.Warning($"Timeout beim Lesen der Druckerzuordnung '{path}'.");
            throw new ConfigException($"Die Druckerzuordnung '{path}' ist nicht erreichbar (Zeitüberschreitung nach {settings.MappingReadTimeoutSeconds} s). Bitte Netzwerkverbindung und Freigabe prüfen.");
        }

        string text;
        try
        {
            text = await read.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _log.Error($"Druckerzuordnung nicht gefunden: '{path}'.", ex);
            throw new ConfigException($"Die Druckerzuordnung '{path}' wurde nicht gefunden. Bitte Netzwerkverbindung und Pfad in appsettings.json prüfen.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _log.Error($"Druckerzuordnung nicht lesbar: '{path}'.", ex);
            throw new ConfigException($"Die Druckerzuordnung '{path}' kann nicht gelesen werden: {ex.Message}", ex);
        }

        var config = PrinterMappingCsv.FindForComputer(text, computerName, path);
        _log.Info($"Druckerzuordnung geladen: PC '{computerName}' -> {config.PrinterIp}:{config.Port}, {config.Dpi} dpi, {config.LabelWidthMm}x{config.LabelHeightMm} mm.");
        return config;
    }
}
