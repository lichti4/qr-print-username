namespace QrLabelPrinter.Services;

/// <summary>Fehler in Konfiguration (appsettings.json, CSV). Meldung ist für Anwender gedacht.</summary>
public class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
    public ConfigException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Fehler beim Senden an den Drucker. Meldung ist für Anwender gedacht.</summary>
public class PrintException : Exception
{
    public PrintException(string message) : base(message) { }
    public PrintException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Etikett kann mit den konfigurierten Maßen nicht sinnvoll gelayoutet werden.</summary>
public class LabelLayoutException : Exception
{
    public LabelLayoutException(string message) : base(message) { }
}
