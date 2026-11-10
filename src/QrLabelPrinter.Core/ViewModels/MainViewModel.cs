using QrLabelPrinter.Models;
using QrLabelPrinter.Services;

namespace QrLabelPrinter.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public const string Unknown = "–";

    private readonly IAppLogger _log;
    private readonly IPrinterConfigSource _configSource;
    private readonly ILabelPrintService _printService;
    private readonly IQrCodeService _qr;

    private SapUser? _user;
    private PrinterConfig? _printer;

    private string _sapUsername = Unknown;
    private string _firstName = Unknown;
    private string _lastName = Unknown;
    private string _printerName = Unknown;
    private string _printerAddress = Unknown;
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _isPrinting;
    private LabelPreviewModel? _preview;

    public MainViewModel(string machineName, IAppLogger log, IPrinterConfigSource configSource,
        ILabelPrintService printService, IQrCodeService qr)
    {
        ComputerName = machineName;
        _log = log;
        _configSource = configSource;
        _printService = printService;
        _qr = qr;
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => CanPrint);
    }

    public string ComputerName { get; }
    public string SapUsername { get => _sapUsername; private set => SetProperty(ref _sapUsername, value); }
    public string FirstName { get => _firstName; private set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; private set => SetProperty(ref _lastName, value); }
    public string PrinterName { get => _printerName; private set => SetProperty(ref _printerName, value); }
    public string PrinterAddress { get => _printerAddress; private set => SetProperty(ref _printerAddress, value); }
    public LabelPreviewModel? Preview { get => _preview; private set => SetProperty(ref _preview, value); }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsPrinting
    {
        get => _isPrinting;
        private set { if (SetProperty(ref _isPrinting, value)) RefreshCanPrint(); }
    }

    /// <summary>Drucken ist nur mit gültigen Parametern, gültiger Druckerzuordnung und Vorschau möglich.</summary>
    public bool CanPrint => !IsPrinting && _user is not null && _printer is not null && Preview is not null;

    public AsyncRelayCommand PrintCommand { get; }

    public async Task InitializeAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        try
        {
            var parsed = CommandLineParser.Parse(args);
            if (!parsed.IsValid)
            {
                _log.Warning("Ungültige oder fehlende Aufrufparameter: " + string.Join(" | ", parsed.Errors));
                ErrorMessage = "Die Anwendung wurde mit fehlenden oder ungültigen Parametern gestartet. Ein Druck ist nicht möglich.\n\n• "
                               + string.Join("\n• ", parsed.Errors)
                               + "\n\nErwarteter Aufruf: QrLabelPrinter.exe --sap-user \"USER\" --firstname \"Vorname\" --lastname \"Nachname\"";
                return;
            }

            _user = parsed.User!;
            SapUsername = _user.Username;
            FirstName = _user.FirstName;
            LastName = _user.LastName;

            _printer = await _configSource.GetForComputerAsync(ComputerName, ct);
            PrinterName = _printer.PrinterName;
            PrinterAddress = $"{_printer.PrinterIp}:{_printer.Port}";

            var matrix = _qr.CreateMatrix(_user.LabelUsername);
            var layout = LabelLayoutCalculator.Calculate(_user, _printer, matrix.GetLength(0));
            Preview = new LabelPreviewModel(layout, matrix);
        }
        catch (Exception ex) when (ex is ConfigException or LabelLayoutException)
        {
            _log.Warning("Initialisierung fehlgeschlagen: " + ex.Message);
            ErrorMessage = ex.Message + "\n\nEin Druck ist nicht möglich.";
        }
        catch (Exception ex)
        {
            _log.Error("Unerwarteter Fehler bei der Initialisierung.", ex);
            ErrorMessage = "Unerwarteter Fehler bei der Initialisierung: " + ex.Message + "\n\nEin Druck ist nicht möglich.";
        }
        finally
        {
            RefreshCanPrint();
        }
    }

    private async Task PrintAsync()
    {
        if (!CanPrint) return;

        // Synchron gesetzt, bevor das erste await passiert -> kein zweiter Klick möglich.
        IsPrinting = true;
        ErrorMessage = null;
        StatusMessage = "Sende Etikett an den Drucker …";
        try
        {
            await _printService.PrintOneAsync(_user!, _printer!);
            StatusMessage = $"1 Etikett wurde an {PrinterName} ({PrinterAddress}) gesendet. "
                          + "Hinweis: Das bestätigt nur die Übertragung, nicht den tatsächlichen Ausdruck.";
        }
        catch (Exception ex) when (ex is PrintException or ConfigException or LabelLayoutException)
        {
            StatusMessage = null;
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _log.Error("Unerwarteter Fehler beim Drucken.", ex);
            StatusMessage = null;
            ErrorMessage = "Unerwarteter Fehler beim Drucken: " + ex.Message;
        }
        finally
        {
            IsPrinting = false;
        }
    }

    private void RefreshCanPrint()
    {
        OnPropertyChanged(nameof(CanPrint));
        PrintCommand.RaiseCanExecuteChanged();
    }
}
