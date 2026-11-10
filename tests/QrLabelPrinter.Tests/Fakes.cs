using QrLabelPrinter.Models;
using QrLabelPrinter.Services;

namespace QrLabelPrinter.Tests;

/// <summary>Simulierte Druckerschnittstelle: merkt sich alle Sendungen, ohne Netzwerk.</summary>
public sealed class FakePrinterClient : IPrinterClient
{
    public List<(string Host, int Port, byte[] Data)> Sent { get; } = new();
    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(string host, int port, byte[] data, TimeSpan timeout, CancellationToken ct = default)
    {
        if (ThrowOnSend is not null) throw ThrowOnSend;
        Sent.Add((host, port, data));
        return Task.CompletedTask;
    }
}

public sealed class FakePrintService : ILabelPrintService
{
    public int Calls { get; private set; }
    public Exception? ThrowOnPrint { get; set; }

    public Task PrintOneAsync(SapUser user, PrinterConfig printer, CancellationToken ct = default)
    {
        Calls++;
        if (ThrowOnPrint is not null) throw ThrowOnPrint;
        return Task.CompletedTask;
    }
}

public sealed class FakeConfigSource : IPrinterConfigSource
{
    public PrinterConfig? Config { get; set; } = TestData.Printer();
    public Exception? Error { get; set; }

    public Task<PrinterConfig> GetForComputerAsync(string computerName, CancellationToken ct = default)
    {
        if (Error is not null) throw Error;
        return Task.FromResult(Config!);
    }
}

public static class TestData
{
    public static PrinterConfig Printer(int dpi = 203, double w = 50, double h = 30) =>
        new("CLIENT-001", "Zebra-Lager", "10.10.10.51", 9100, dpi, w, h);

    public static SapUser User(string user = "mmustermann", string first = "Max", string last = "Mustermann") =>
        new(user, first, last);

    public static string[] Args(string user = "mmustermann", string first = "Max", string last = "Mustermann") =>
        ["--sap-user", user, "--firstname", first, "--lastname", last];
}
