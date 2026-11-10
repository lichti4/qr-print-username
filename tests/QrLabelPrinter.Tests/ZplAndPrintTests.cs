using System.Net;
using System.Net.Sockets;
using QrLabelPrinter.Configuration;
using QrLabelPrinter.Models;
using QrLabelPrinter.Services;
using QrLabelPrinter.ViewModels;
using Xunit;

namespace QrLabelPrinter.Tests;

public class DotConverterTests
{
    [Theory]
    [InlineData(50, 203, 400)]
    [InlineData(30, 203, 240)]
    [InlineData(50, 300, 591)]
    [InlineData(30, 300, 354)]
    [InlineData(25.4, 203, 203)]
    [InlineData(0, 203, 0)]
    public void Converts_mm_to_dots(double mm, int dpi, int expected) =>
        Assert.Equal(expected, DotConverter.MmToDots(mm, dpi));

    [Fact]
    public void Rejects_invalid_input()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DotConverter.MmToDots(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DotConverter.MmToDots(-1, 203));
    }
}

public class LayoutAndZplTests
{
    private static readonly QrCodeService Qr = new();

    private static LabelLayout Layout(SapUser user, PrinterConfig printer) =>
        LabelLayoutCalculator.Calculate(user, printer, Qr.CreateMatrix(user.LabelUsername).GetLength(0));

    private static string Zpl(SapUser user, PrinterConfig? printer = null, bool utf8 = true, string font = "0") =>
        ZplGenerator.Generate(Layout(user, printer ?? TestData.Printer()), new ZplOptions(font, utf8));

    private static int Count(string text, string part) =>
        (text.Length - text.Replace(part, "").Length) / part.Length;

    [Fact]
    public void Qr_matrix_is_square_and_has_expected_version_size()
    {
        var m = Qr.CreateMatrix("MMUSTERMANN");
        Assert.Equal(m.GetLength(0), m.GetLength(1));
        Assert.Equal(21, m.GetLength(0)); // Version 1 für 11 alphanumerische Zeichen bei ECC Q
    }

    [Fact]
    public void Layout_size_comes_from_printer_config()
    {
        var l203 = Layout(TestData.User(), TestData.Printer(203, 50, 30));
        Assert.Equal((400, 240), (l203.WidthDots, l203.HeightDots));

        var l300 = Layout(TestData.User(), TestData.Printer(300, 100, 60));
        Assert.Equal((1181, 709), (l300.WidthDots, l300.HeightDots));
    }

    [Fact]
    public void Layout_stays_inside_label()
    {
        var l = Layout(TestData.User(), TestData.Printer());
        Assert.InRange(l.Qr.X + l.Qr.Magnification * l.Qr.ModuleCount, 0, l.WidthDots);
        Assert.InRange(l.Qr.Y + l.Qr.Magnification * l.Qr.ModuleCount, 0, l.HeightDots);
        foreach (var line in l.Lines)
        {
            Assert.True(line.X + line.FieldWidth <= l.WidthDots);
            Assert.True(line.Y + line.FontHeight <= l.HeightDots);
        }
    }

    [Fact]
    public void Too_small_label_throws()
    {
        Assert.Throws<LabelLayoutException>(() => Layout(TestData.User(), TestData.Printer(203, 10, 5)));
    }

    [Fact]
    public void Zpl_contains_expected_commands_and_values()
    {
        string zpl = Zpl(TestData.User());
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ\n", zpl);
        Assert.Contains("^CI28", zpl);
        Assert.Contains("^PW400", zpl);
        Assert.Contains("^LL240", zpl);
        Assert.Contains("^BQN,2,", zpl);
        Assert.Contains("^FDQA,MMUSTERMANN^FS", zpl);   // QR enthält nur den Username in Großbuchstaben
        Assert.Contains("^FDMustermann^FS", zpl);
        Assert.Contains("^FDMax^FS", zpl);
    }

    [Fact]
    public void Zpl_prints_exactly_one_label()
    {
        string zpl = Zpl(TestData.User());
        Assert.Equal(1, Count(zpl, "^XA"));
        Assert.Equal(1, Count(zpl, "^XZ"));
        Assert.Equal(1, Count(zpl, "^PQ"));
        Assert.Contains("^PQ1\n", zpl);
    }

    [Fact]
    public void Umlauts_are_hex_escaped_as_utf8()
    {
        string zpl = Zpl(TestData.User(first: "Jürgen", last: "Müller-Äß"));
        Assert.Contains("^FH_", zpl);
        Assert.Contains("M_C3_BCller-_C3_84_C3_9F", zpl);
        Assert.Contains("J_C3_BCrgen", zpl);
    }

    [Fact]
    public void Latin1_mode_uses_single_byte_escapes()
    {
        string zpl = Zpl(TestData.User(last: "Müller"), utf8: false);
        Assert.Contains("^CI27", zpl);
        Assert.Contains("M_FCller", zpl);
    }

    [Fact]
    public void Zpl_control_characters_in_names_cannot_break_the_command()
    {
        string zpl = Zpl(TestData.User(first: "A^FS^XZ~DG_41\\", last: "x^XA^PQ999"));
        Assert.Equal(1, Count(zpl, "^XA"));
        Assert.Equal(1, Count(zpl, "^XZ"));
        Assert.Equal(1, Count(zpl, "^PQ"));
        Assert.DoesNotContain("~DG", zpl);
        Assert.Contains("A_5EFS_5EXZ_7EDG_5F41_5C", zpl);
    }

    [Fact]
    public void Zpl_is_pure_ascii()
    {
        string zpl = Zpl(TestData.User(first: "Zoë", last: "日本"));
        Assert.All(zpl, ch => Assert.True(ch < 128));
        Assert.Equal(zpl.Length, ZplGenerator.ToBytes(zpl).Length);
    }

    [Fact]
    public void Truetype_font_uses_a_at_command()
    {
        string zpl = Zpl(TestData.User(), font: "E:ARIAL.TTF");
        Assert.Contains("^A@N,", zpl);
        Assert.Contains("E:ARIAL.TTF", zpl);
    }
}

public class PrintServiceTests
{
    private static LabelPrintService Create(FakePrinterClient client) =>
        new(client, new QrCodeService(), () => new AppSettings { PrinterMappingPath = "x" }, NullAppLogger.Instance);

    [Fact]
    public async Task Sends_exactly_one_job_to_configured_ip_and_port()
    {
        var client = new FakePrinterClient();
        await Create(client).PrintOneAsync(TestData.User(), TestData.Printer());

        var sent = Assert.Single(client.Sent);
        Assert.Equal("10.10.10.51", sent.Host);
        Assert.Equal(9100, sent.Port);
        string zpl = System.Text.Encoding.ASCII.GetString(sent.Data);
        Assert.Contains("^PQ1", zpl);
        Assert.Contains("^FDQA,MMUSTERMANN^FS", zpl);
    }

    [Fact]
    public async Task Network_error_is_passed_on_as_PrintException_without_retry()
    {
        var client = new FakePrinterClient { ThrowOnSend = new PrintException("Drucker nicht erreichbar") };
        await Assert.ThrowsAsync<PrintException>(() => Create(client).PrintOneAsync(TestData.User(), TestData.Printer()));
        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task Tcp_client_delivers_bytes_to_listener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = Task.Run(async () =>
            {
                using var c = await listener.AcceptTcpClientAsync();
                using var ms = new MemoryStream();
                await c.GetStream().CopyToAsync(ms);
                return ms.ToArray();
            });

            byte[] payload = "^XA^FDtest^FS^XZ"u8.ToArray();
            await new TcpPrinterClient().SendAsync("127.0.0.1", port, payload, TimeSpan.FromSeconds(5));

            Assert.Equal(payload, await accept.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Tcp_client_reports_connection_refused_as_PrintException()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop(); // Port ist jetzt frei -> Verbindung wird abgelehnt

        await Assert.ThrowsAsync<PrintException>(() =>
            new TcpPrinterClient().SendAsync("127.0.0.1", port, [1], TimeSpan.FromSeconds(2)));
    }
}

public class MainViewModelTests
{
    private static (MainViewModel Vm, FakePrintService Print) Create(FakeConfigSource? source = null, FakePrintService? print = null)
    {
        print ??= new FakePrintService();
        var vm = new MainViewModel("CLIENT-001", NullAppLogger.Instance, source ?? new FakeConfigSource(), print, new QrCodeService());
        return (vm, print);
    }

    [Fact]
    public async Task Valid_start_shows_data_and_preview_but_does_not_print_automatically()
    {
        var (vm, print) = Create();
        await vm.InitializeAsync(TestData.Args());

        Assert.Equal("mmustermann", vm.SapUsername);
        Assert.Equal("Max", vm.FirstName);
        Assert.Equal("Mustermann", vm.LastName);
        Assert.Equal("CLIENT-001", vm.ComputerName);
        Assert.Equal("Zebra-Lager", vm.PrinterName);
        Assert.Equal("10.10.10.51:9100", vm.PrinterAddress);
        Assert.NotNull(vm.Preview);
        Assert.Equal("MMUSTERMANN", vm.Preview!.Layout.Qr.Data);
        Assert.True(vm.CanPrint);
        Assert.Equal(0, print.Calls);
    }

    [Fact]
    public async Task Each_print_command_prints_exactly_one_label()
    {
        var (vm, print) = Create();
        await vm.InitializeAsync(TestData.Args());

        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(1, print.Calls);
        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(2, print.Calls);
        Assert.True(vm.StatusMessage!.Contains("nicht den tatsächlichen Ausdruck"));
    }

    [Fact]
    public async Task Invalid_parameters_block_printing()
    {
        var (vm, print) = Create();
        await vm.InitializeAsync(["--sap-user", "X"]);

        Assert.True(vm.HasError);
        Assert.False(vm.CanPrint);
        Assert.False(vm.PrintCommand.CanExecute(null));
        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(0, print.Calls);
    }

    [Fact]
    public async Task Missing_printer_mapping_blocks_printing_but_keeps_person_data_visible()
    {
        var (vm, print) = Create(new FakeConfigSource { Error = new ConfigException("Kein Drucker hinterlegt") });
        await vm.InitializeAsync(TestData.Args());

        Assert.Contains("Kein Drucker hinterlegt", vm.ErrorMessage);
        Assert.Equal("Max", vm.FirstName);
        Assert.False(vm.CanPrint);
        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(0, print.Calls);
    }

    [Fact]
    public async Task Label_too_small_blocks_printing()
    {
        var (vm, _) = Create(new FakeConfigSource { Config = TestData.Printer(203, 10, 5) });
        await vm.InitializeAsync(TestData.Args());
        Assert.True(vm.HasError);
        Assert.False(vm.CanPrint);
    }

    [Fact]
    public async Task Print_error_is_shown_and_printing_stays_possible()
    {
        var print = new FakePrintService { ThrowOnPrint = new PrintException("Keine Verbindung zum Zebra-Drucker") };
        var (vm, _) = Create(print: print);
        await vm.InitializeAsync(TestData.Args());

        await vm.PrintCommand.ExecuteAsync();
        Assert.Contains("Keine Verbindung", vm.ErrorMessage);
        Assert.False(vm.IsPrinting);
        Assert.True(vm.CanPrint);
        Assert.Equal(1, print.Calls); // kein automatischer Retry
    }
}
