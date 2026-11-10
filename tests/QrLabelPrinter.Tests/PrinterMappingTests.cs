using QrLabelPrinter.Configuration;
using QrLabelPrinter.Services;
using Xunit;

namespace QrLabelPrinter.Tests;

public class PrinterMappingTests
{
    private const string Header = "ComputerName;PrinterName;PrinterIP;Port;DPI;LabelWidthMm;LabelHeightMm";

    private static string Csv(params string[] rows) => Header + "\r\n" + string.Join("\r\n", rows) + "\r\n";

    [Fact]
    public void Finds_row_for_computer()
    {
        var csv = Csv("CLIENT-001;Zebra-Lager;10.10.10.51;9100;203;50;30", "CLIENT-002;Zebra-Büro;10.10.10.52;9100;300;100;60");
        var c = PrinterMappingCsv.FindForComputer(csv, "CLIENT-002");
        Assert.Equal("Zebra-Büro", c.PrinterName);
        Assert.Equal("10.10.10.52", c.PrinterIp);
        Assert.Equal(9100, c.Port);
        Assert.Equal(300, c.Dpi);
        Assert.Equal(100, c.LabelWidthMm);
        Assert.Equal(60, c.LabelHeightMm);
    }

    [Fact]
    public void Computer_name_match_is_case_insensitive_and_trimmed()
    {
        var csv = Csv(" client-001 ;Zebra;10.10.10.51;9100;203;50;30");
        Assert.Equal("Zebra", PrinterMappingCsv.FindForComputer(csv, "CLIENT-001").PrinterName);
    }

    [Fact]
    public void Accepts_bom_comments_blank_lines_and_decimal_comma()
    {
        var csv = "\uFEFF" + Header + "\n# Kommentar\n\nCLIENT-001;Z;10.0.0.5;9100;203;50,5;30,25\n";
        var c = PrinterMappingCsv.FindForComputer(csv, "CLIENT-001");
        Assert.Equal(50.5, c.LabelWidthMm);
        Assert.Equal(30.25, c.LabelHeightMm);
    }

    [Fact]
    public void Only_the_matching_row_is_validated()
    {
        var csv = Csv("OTHER;Bad;not-an-ip;x;y;z;q", "CLIENT-001;Z;10.0.0.5;9100;203;50;30");
        Assert.Equal("Z", PrinterMappingCsv.FindForComputer(csv, "CLIENT-001").PrinterName);
    }

    [Fact]
    public void Missing_mapping_for_computer_throws()
    {
        var ex = Assert.Throws<ConfigException>(() =>
            PrinterMappingCsv.FindForComputer(Csv("OTHER;Z;10.0.0.5;9100;203;50;30"), "CLIENT-001"));
        Assert.Contains("CLIENT-001", ex.Message);
    }

    [Fact]
    public void Duplicate_mapping_throws()
    {
        var csv = Csv("CLIENT-001;A;10.0.0.5;9100;203;50;30", "client-001;B;10.0.0.6;9100;203;50;30");
        Assert.Throws<ConfigException>(() => PrinterMappingCsv.FindForComputer(csv, "CLIENT-001"));
    }

    [Fact]
    public void Missing_column_or_empty_file_throws()
    {
        Assert.Throws<ConfigException>(() =>
            PrinterMappingCsv.FindForComputer("ComputerName;PrinterName\nCLIENT-001;Z", "CLIENT-001"));
        Assert.Throws<ConfigException>(() => PrinterMappingCsv.FindForComputer("", "CLIENT-001"));
    }

    [Theory]
    [InlineData("10.10.10", "9100", "203", "50", "30")]      // unvollständige IP
    [InlineData("1", "9100", "203", "50", "30")]             // Kurzform
    [InlineData("999.1.1.1", "9100", "203", "50", "30")]
    [InlineData("0.0.0.0", "9100", "203", "50", "30")]
    [InlineData("", "9100", "203", "50", "30")]
    [InlineData("10.0.0.5", "0", "203", "50", "30")]         // Port
    [InlineData("10.0.0.5", "70000", "203", "50", "30")]
    [InlineData("10.0.0.5", "abc", "203", "50", "30")]
    [InlineData("10.0.0.5", "", "203", "50", "30")]
    [InlineData("10.0.0.5", "9100", "", "50", "30")]         // DPI
    [InlineData("10.0.0.5", "9100", "20", "50", "30")]
    [InlineData("10.0.0.5", "9100", "abc", "50", "30")]
    [InlineData("10.0.0.5", "9100", "203", "", "30")]        // Maße
    [InlineData("10.0.0.5", "9100", "203", "50", "0")]
    [InlineData("10.0.0.5", "9100", "203", "-5", "30")]
    [InlineData("10.0.0.5", "9100", "203", "50", "abc")]
    [InlineData("10.0.0.5", "9100", "203", "50", "9999")]
    public void Invalid_values_are_rejected(string ip, string port, string dpi, string w, string h)
    {
        Assert.Throws<ConfigException>(() =>
            PrinterConfigValidator.Create("CLIENT-001", "Z", ip, port, dpi, w, h));
    }

    [Fact]
    public void Valid_values_including_ipv6_are_accepted()
    {
        var c = PrinterConfigValidator.Create("PC", "", "fe80::1", "9100", "300", "100", "150");
        Assert.Equal("fe80::1", c.PrinterIp);
        Assert.Equal("fe80::1", c.PrinterName); // Fallback auf IP, wenn kein Name
    }

    [Fact]
    public async Task Csv_source_reports_missing_file_in_plain_language()
    {
        var settings = new AppSettings { PrinterMappingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv") };
        var source = new CsvPrinterConfigSource(() => settings, NullAppLogger.Instance);
        var ex = await Assert.ThrowsAsync<ConfigException>(() => source.GetForComputerAsync("CLIENT-001"));
        Assert.Contains("nicht gefunden", ex.Message);
    }

    [Fact]
    public async Task Csv_source_reads_file_from_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        await File.WriteAllTextAsync(path, Csv("CLIENT-001;Zebra-Lager;10.10.10.51;9100;203;50;30"));
        try
        {
            var settings = new AppSettings { PrinterMappingPath = path };
            var source = new CsvPrinterConfigSource(() => settings, NullAppLogger.Instance);
            var c = await source.GetForComputerAsync("client-001");
            Assert.Equal("10.10.10.51", c.PrinterIp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Settings_parse_and_validate()
    {
        var s = AppSettingsLoader.Parse("""{ "PrinterMappingPath": "\\\\SRV\\Share\\m.csv", "ConnectTimeoutSeconds": 7 }""");
        Assert.Equal(@"\\SRV\Share\m.csv", s.PrinterMappingPath);
        Assert.Equal(7, s.ConnectTimeoutSeconds);
        Assert.True(s.UseUtf8);

        Assert.Throws<ConfigException>(() => AppSettingsLoader.Parse("{}"));
        Assert.Throws<ConfigException>(() => AppSettingsLoader.Parse("not json"));
        Assert.Throws<ConfigException>(() => AppSettingsLoader.Parse("""{ "PrinterMappingPath": "x", "ConnectTimeoutSeconds": 0 }"""));
    }
}
