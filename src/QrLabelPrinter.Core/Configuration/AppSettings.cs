using System.Text.Json;
using System.Text.RegularExpressions;
using QrLabelPrinter.Services;

namespace QrLabelPrinter.Configuration;

/// <summary>Einstellungen aus appsettings.json (liegt neben der EXE).</summary>
public sealed class AppSettings
{
    /// <summary>UNC-Pfad zur zentralen PrinterMapping.csv.</summary>
    public string PrinterMappingPath { get; set; } = "";

    /// <summary>Timeout für den TCP-Verbindungsaufbau und das Senden.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>Timeout für das Lesen der CSV vom Netzwerkshare.</summary>
    public int MappingReadTimeoutSeconds { get; set; } = 10;

    /// <summary>ZPL-Schrift: einzelnes Zeichen (z. B. "0") oder Dateiname (z. B. "E:ARIAL.TTF").</summary>
    public string ZplFont { get; set; } = "0";

    /// <summary>true = ^CI28 (UTF-8), false = ^CI27 (Windows-1252/Latin-1).</summary>
    public bool UseUtf8 { get; set; } = true;
}

public static class AppSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            throw new ConfigException($"Die Konfigurationsdatei '{path}' wurde nicht gefunden.");

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Die Konfigurationsdatei '{path}' kann nicht gelesen werden: {ex.Message}", ex);
        }

        return Parse(json, path);
    }

    public static AppSettings Parse(string json, string sourceName = "appsettings.json")
    {
        AppSettings? s;
        try { s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions); }
        catch (JsonException ex)
        {
            throw new ConfigException($"'{sourceName}' ist keine gültige JSON-Datei: {ex.Message}", ex);
        }

        if (s is null) throw new ConfigException($"'{sourceName}' ist leer.");

        if (string.IsNullOrWhiteSpace(s.PrinterMappingPath))
            throw new ConfigException($"In '{sourceName}' fehlt der Eintrag 'PrinterMappingPath'.");
        if (s.ConnectTimeoutSeconds is < 1 or > 120)
            throw new ConfigException("'ConnectTimeoutSeconds' muss zwischen 1 und 120 liegen.");
        if (s.MappingReadTimeoutSeconds is < 1 or > 300)
            throw new ConfigException("'MappingReadTimeoutSeconds' muss zwischen 1 und 300 liegen.");
        if (string.IsNullOrWhiteSpace(s.ZplFont) || !Regex.IsMatch(s.ZplFont, @"^[A-Za-z0-9:._\-]{1,40}$"))
            throw new ConfigException("'ZplFont' ist ungültig (erlaubt: ein Zeichen wie \"0\" oder ein Dateiname wie \"E:ARIAL.TTF\").");

        s.PrinterMappingPath = s.PrinterMappingPath.Trim();
        s.ZplFont = s.ZplFont.Trim();
        return s;
    }
}
