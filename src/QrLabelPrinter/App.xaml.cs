using System.IO;
using System.Windows;
using QrLabelPrinter.Configuration;
using QrLabelPrinter.Services;
using QrLabelPrinter.ViewModels;
using QrLabelPrinter.Views;

namespace QrLabelPrinter;

public partial class App : Application
{
    private IAppLogger _log = NullAppLogger.Instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _log = FileAppLogger.CreateDefault();
        DispatcherUnhandledException += (_, args) =>
        {
            _log.Error("Unbehandelter Fehler in der Oberfläche.", args.Exception);
            MessageBox.Show("Unerwarteter Fehler: " + args.Exception.Message, "QR-Etikettendruck",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _log.Info("Anwendung gestartet.");

        // appsettings.json wird erst bei Bedarf geladen; Fehler erscheinen dann als verständliche Meldung im Fenster.
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settings = new Lazy<AppSettings>(() => AppSettingsLoader.Load(settingsPath));
        Func<AppSettings> getSettings = () => settings.Value;

        var qr = new QrCodeService();
        var vm = new MainViewModel(
            Environment.MachineName,
            _log,
            new CsvPrinterConfigSource(getSettings, _log),
            new LabelPrintService(new TcpPrinterClient(), qr, getSettings, _log),
            qr);

        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        await vm.InitializeAsync(e.Args);
    }
}
