using System.Text;

namespace QrLabelPrinter.Services;

public interface IAppLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullAppLogger : IAppLogger
{
    public static readonly NullAppLogger Instance = new();
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>
/// Einfaches Tageslog unter %LocalAppData%\QrLabelPrinter\Logs. Fehler beim Schreiben werden verschluckt,
/// damit die Protokollierung nie den Druck stört. Personendaten werden vom Aufrufer nicht übergeben.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _directory;
    private readonly object _lock = new();

    public FileAppLogger(string directory, int retentionDays = 30)
    {
        _directory = directory;
        Cleanup(retentionDays);
    }

    public static FileAppLogger CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QrLabelPrinter", "Logs"));

    public void Info(string message) => Write("INFO ", message, null);
    public void Warning(string message) => Write("WARN ", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
            if (ex is not null) sb.AppendLine().Append(ex);
            sb.AppendLine();

            lock (_lock)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, $"{DateTime.Now:yyyy-MM-dd}.log"), sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging darf nie zu Folgefehlern führen.
        }
    }

    private void Cleanup(int retentionDays)
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            var limit = DateTime.Now.AddDays(-retentionDays);
            foreach (var f in Directory.EnumerateFiles(_directory, "*.log"))
                if (File.GetLastWriteTime(f) < limit) File.Delete(f);
        }
        catch { }
    }
}
