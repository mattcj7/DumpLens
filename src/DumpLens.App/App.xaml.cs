using DumpLens.App.ViewModels;
using DumpLens.Application.Audit;
using DumpLens.Application.Imports;
using DumpLens.Ingestion.Csv;
using DumpLens.Ingestion.Xlsx;
using DumpLens.Normalization.Identities;
using DumpLens.Normalization.Timestamps;
using DumpLens.Persistence.Audit;
using DumpLens.Persistence.CallImports;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Imports;
using DumpLens.Persistence.MessageImports;
using DumpLens.Persistence.Sources;
using DumpLens.Security.FileHashing;

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

        var sourceImporters = CreateSourceImporters();
        var sourceImportRepository = new SqliteSourceImportRepository();
        var fileHashService = new Sha256FileHashService();
        var sourceRegistrationService = new SqliteSourceRegistrationService(fileHashService, sourceImportRepository);
        var identityNormalizer = new IdentityNormalizer();
        var timestampNormalizer = new TimestampNormalizer();
        var messageImportService = new SqliteMessageImportService(sourceImporters, identityNormalizer, timestampNormalizer);
        var callImportService = new SqliteCallImportService(sourceImporters, identityNormalizer, timestampNormalizer);
        var importWarningSummaryReader = new SqliteImportWarningSummaryReader();
        Func<string, IAuditLogger> auditLoggerFactory = connectionString => new SqliteAuditLogger(connectionString);

        MainWindow = new MainWindow
        {
            DataContext = new MainShellViewModel(
                new SqliteCaseService(),
                sourceImporters,
                sourceRegistrationService,
                messageImportService,
                callImportService,
                importWarningSummaryReader,
                auditLoggerFactory,
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
