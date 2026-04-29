using DumpLens.App.ViewModels;
using DumpLens.Application.Imports;
using DumpLens.Ingestion.Csv;
using DumpLens.Ingestion.Xlsx;
using DumpLens.Persistence.Cases;

namespace DumpLens.App;

public partial class App : System.Windows.Application
{
    private readonly ShellSessionLogger _logger = new();
    private readonly string _startupCorrelationId = Guid.NewGuid().ToString("N");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _logger.LogInformation(
            operation: "app_startup",
            correlationId: _startupCorrelationId,
            message: "DumpLens shell startup requested.");

        base.OnStartup(e);

        MainWindow = new MainWindow
        {
            DataContext = new MainShellViewModel(
                new SqliteCaseService(),
                CreateSourceImporters(),
                _logger.LogInformation)
        };

        MainWindow.Loaded += MainWindowOnLoaded;
        MainWindow.Closed += MainWindowOnClosed;
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _logger.Dispose();
        base.OnExit(e);
    }

    private void MainWindowOnLoaded(object? sender, System.Windows.RoutedEventArgs e)
    {
        if (MainWindow?.DataContext is MainShellViewModel shellViewModel)
        {
            _logger.LogInformation(
                operation: "shell_ready",
                correlationId: _startupCorrelationId,
                message: "Main shell loaded.",
                fields: new Dictionary<string, string>
                {
                    ["screen"] = shellViewModel.SelectedNavigationItem.Label
                });
        }
    }

    private void MainWindowOnClosed(object? sender, EventArgs e)
    {
        _logger.LogInformation(
            operation: "app_shutdown",
            correlationId: _startupCorrelationId,
            message: "DumpLens shell closed.");
    }

    private static IReadOnlyList<ISourceImporter> CreateSourceImporters()
    {
        return
        [
            new CsvSourceImporter(),
            new XlsxSourceImporter()
        ];
    }
}
