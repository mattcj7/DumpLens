using System.Collections;
using System.Reflection;
using DumpLens.Application.Imports;

namespace DumpLens.Tests.Unit.UI;

public sealed class ImportWizardViewModelTests
{
    [Fact]
    public void ImportWizardView_Xaml_Uses_OneWay_For_ReadOnly_Display_Bindings()
    {
        var xaml = File.ReadAllText(GetImportWizardViewPath());

        Assert.Contains("Text=\"{Binding StepNumber, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Title, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Description, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentStepTitle, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentStepDescription, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DetectedSourceKindText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FileSupportStatusText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProbeDetailsText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding WarningSummaryText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SummaryText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding NextButtonText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportWizardViewModel_Initializes_On_First_Step()
    {
        var viewModel = CreateViewModel(
            [new FakeSourceImporter(ImportSourceKind.Csv)],
            out _,
            out _);

        Assert.Equal(0, GetIntProperty(viewModel, "CurrentStepIndex"));
        Assert.Equal("Choose source type", GetStringProperty(viewModel, "CurrentStepTitle"));
    }

    [Fact]
    public void ImportWizardViewModel_Source_Type_Can_Be_Selected()
    {
        var viewModel = CreateViewModel(
            [new FakeSourceImporter(ImportSourceKind.Csv), new FakeSourceImporter(ImportSourceKind.Xlsx)],
            out _,
            out _);
        var csvOption = GetCollection(viewModel, "SourceTypeOptions")
            .Single(option => string.Equals(GetStringProperty(option, "Label"), "CSV", StringComparison.Ordinal));

        SetPropertyValue(viewModel, "SelectedSourceTypeOption", csvOption);

        var selectedOption = GetPropertyValue(viewModel, "SelectedSourceTypeOption");
        Assert.Equal("CSV", GetStringProperty(selectedOption, "Label"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Missing_File_Path_Blocks_Preview_With_Safe_Error()
    {
        var viewModel = CreateViewModel(
            [new FakeSourceImporter(ImportSourceKind.Csv)],
            out _,
            out _);
        SelectSourceType(viewModel, "CSV");

        await InvokeAsync(viewModel, "RefreshPreviewAsync");

        Assert.Equal("Enter an absolute file path before requesting a preview.", GetStringProperty(viewModel, "GeneralErrorMessage"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Unsupported_File_Produces_Warning_State()
    {
        var importer = new FakeSourceImporter(ImportSourceKind.Csv)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-unsupported",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = false,
                IsTabular = false,
                Warnings =
                [
                    new ImportWarning
                    {
                        Code = ImportWarningCodes.UnsupportedFileExtension,
                        Message = "Only supported CSV exports can be previewed."
                    }
                ]
            }
        };

        var viewModel = CreateViewModel([importer], out _, out _);
        SelectSourceType(viewModel, "CSV");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\unsupported.xlsx");

        await InvokeAsync(viewModel, "ProbeFileAsync");

        Assert.True(GetBooleanProperty(viewModel, "HasProbeResult"));
        Assert.Equal("That file could not be previewed as the selected source type. Review the warnings and choose a supported CSV or XLSX file.", GetStringProperty(viewModel, "GeneralErrorMessage"));

        var warnings = GetCollection(viewModel, "Warnings").ToList();
        Assert.Single(warnings);
        Assert.Equal(ImportWarningCodes.UnsupportedFileExtension, GetStringProperty(warnings[0], "Code"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Successful_Csv_Preview_Populates_Rows_And_Mappings()
    {
        var importer = CreateSuccessfulCsvImporter();
        var viewModel = CreateViewModel([importer], out _, out _);
        SelectSourceType(viewModel, "CSV");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\preview.csv");

        await InvokeAsync(viewModel, "ProbeFileAsync");
        await InvokeAsync(viewModel, "RefreshPreviewAsync");

        var previewGrid = GetPropertyValue(viewModel, "PreviewGrid");
        Assert.Equal(2, GetIntProperty(previewGrid, "RowCount"));

        var mappings = GetCollection(viewModel, "ColumnMappings");
        var timestampMapping = mappings.Single(mapping => string.Equals(GetStringProperty(mapping, "DumpLensFieldName"), ImportFieldNames.Timestamp, StringComparison.Ordinal));
        Assert.Equal("timestamp", GetStringProperty(timestampMapping, "SelectedSourceColumnName"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Successful_Xlsx_Probe_Shows_Worksheet_Options()
    {
        var importer = new FakeSourceImporter(ImportSourceKind.Xlsx)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-xlsx",
                SourceKind = ImportSourceKind.Xlsx,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = ["Messages", "Calls"],
                SelectedWorksheetName = "Messages",
                HasHeaderRow = true,
                Warnings = Array.Empty<ImportWarning>()
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-xlsx",
                SourceKind = ImportSourceKind.Xlsx,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = ["Messages", "Calls"],
                SelectedWorksheetName = request.WorksheetName ?? "Messages",
                HasHeaderRow = true,
                Columns = [new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" }],
                Rows = [new ImportPreviewRow { RowNumber = 2, Values = ["2026-04-28T10:00:00Z"] }],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion
                    {
                        DumpLensFieldName = ImportFieldNames.Timestamp,
                        SourceColumnName = "timestamp"
                    }
                ],
                Warnings = Array.Empty<ImportWarning>()
            }
        };

        var viewModel = CreateViewModel([importer], out _, out _);
        SelectSourceType(viewModel, "XLSX");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\preview.xlsx");

        await InvokeAsync(viewModel, "ProbeFileAsync");

        var worksheetOptions = GetCollection(viewModel, "WorksheetOptions")
            .Select(static option => Assert.IsType<string>(option))
            .ToList();

        Assert.Equal(["Messages", "Calls"], worksheetOptions);
        Assert.Equal("Messages", GetStringProperty(viewModel, "SelectedWorksheetName"));
    }

    [Fact]
    public async Task ImportWizardViewModel_Selecting_Worksheet_Updates_Preview_Request()
    {
        var importer = new FakeSourceImporter(ImportSourceKind.Xlsx)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-xlsx-sheet",
                SourceKind = ImportSourceKind.Xlsx,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = ["Messages", "Calls"],
                SelectedWorksheetName = "Messages",
                HasHeaderRow = true
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-xlsx-sheet",
                SourceKind = ImportSourceKind.Xlsx,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = ["Messages", "Calls"],
                SelectedWorksheetName = request.WorksheetName,
                HasHeaderRow = true,
                Columns = [new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" }],
                Rows = [new ImportPreviewRow { RowNumber = 2, Values = ["2026-04-28T10:00:00Z"] }],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion
                    {
                        DumpLensFieldName = ImportFieldNames.Timestamp,
                        SourceColumnName = "timestamp"
                    }
                ]
            }
        };

        var viewModel = CreateViewModel([importer], out _, out _);
        SelectSourceType(viewModel, "XLSX");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\preview.xlsx");

        await InvokeAsync(viewModel, "ProbeFileAsync");
        SetPropertyValue(viewModel, "SelectedWorksheetName", "Calls");
        await InvokeAsync(viewModel, "RefreshPreviewAsync");

        Assert.NotNull(importer.LastPreviewRequest);
        Assert.Equal("Calls", importer.LastPreviewRequest!.WorksheetName);
    }

    [Fact]
    public async Task ImportWizardViewModel_Final_Summary_Remains_Preview_Only()
    {
        var importer = CreateSuccessfulCsvImporter();
        var viewModel = CreateViewModel([importer], out _, out var onCloseCallCount);
        SelectSourceType(viewModel, "CSV");
        SetPropertyValue(viewModel, "FilePath", @"O:\Synthetic\preview.csv");

        await InvokeAsync(viewModel, "ProbeFileAsync");
        await InvokeAsync(viewModel, "NextAsync");
        await InvokeAsync(viewModel, "NextAsync");
        await InvokeAsync(viewModel, "NextAsync");
        await InvokeAsync(viewModel, "NextAsync");
        await InvokeAsync(viewModel, "NextAsync");
        await InvokeAsync(viewModel, "NextAsync");
        await InvokeAsync(viewModel, "NextAsync");

        Assert.Equal(7, GetIntProperty(viewModel, "CurrentStepIndex"));
        Assert.Equal(0, onCloseCallCount.Value);

        var summaryText = GetStringProperty(viewModel, "SummaryText");
        Assert.Contains("Preview complete. Persistence will be added in a later ticket.", summaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("import completed", summaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportWizardViewModel_Logs_Do_Not_Include_Preview_Values()
    {
        var sensitiveName = "Sensitive Person";
        var sensitivePhone = "555-000-9999";
        var sensitiveBody = "TOP SECRET MESSAGE BODY";
        var importer = new FakeSourceImporter(ImportSourceKind.Csv)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-log-redaction",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "sender" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "recipient" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "message_body" }
                ],
                PreviewRows =
                [
                    new ImportPreviewRow
                    {
                        RowNumber = 2,
                        Values = ["2026-04-28T10:00:00Z", sensitiveName, sensitivePhone, sensitiveBody]
                    }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion
                    {
                        DumpLensFieldName = ImportFieldNames.Timestamp,
                        SourceColumnName = "timestamp"
                    }
                ]
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-log-redaction",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "sender" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "recipient" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "message_body" }
                ],
                Rows =
                [
                    new ImportPreviewRow
                    {
                        RowNumber = 2,
                        Values = ["2026-04-28T10:00:00Z", sensitiveName, sensitivePhone, sensitiveBody]
                    }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion
                    {
                        DumpLensFieldName = ImportFieldNames.Timestamp,
                        SourceColumnName = "timestamp"
                    }
                ]
            }
        };

        var viewModel = CreateViewModel([importer], out var logs, out _);
        SelectSourceType(viewModel, "CSV");
        SetPropertyValue(viewModel, "FilePath", $@"O:\Synthetic\{sensitiveName}.csv");

        await InvokeAsync(viewModel, "ProbeFileAsync");
        await InvokeAsync(viewModel, "RefreshPreviewAsync");

        var flattenedLogs = string.Join(
            Environment.NewLine,
            logs.Select(log =>
            {
                var fields = log.Fields is null
                    ? string.Empty
                    : string.Join(";", log.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
                return $"{log.Operation}|{log.Message}|{fields}";
            }));

        Assert.DoesNotContain(sensitiveName, flattenedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitivePhone, flattenedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveBody, flattenedLogs, StringComparison.Ordinal);
    }

    private static object CreateViewModel(
        IEnumerable<ISourceImporter> importers,
        out List<UiLogEntry> logs,
        out Counter onCloseCallCount)
    {
        var capturedLogs = new List<UiLogEntry>();
        var capturedCounter = new Counter();
        var assembly = ViewModelAssemblyLoader.Load();
        var viewModelType = assembly.GetType("DumpLens.App.ViewModels.ImportWizardViewModel", throwOnError: true)!;
        Action onClose = () => capturedCounter.Value++;
        Action<string, string, string, IReadOnlyDictionary<string, string>?> logAction =
            (operation, correlationId, message, fields) =>
                capturedLogs.Add(new UiLogEntry(operation, correlationId, message, fields));

        logs = capturedLogs;
        onCloseCallCount = capturedCounter;

        return Activator.CreateInstance(
            viewModelType,
            importers,
            onClose,
            logAction,
            "Eastern Standard Time")!;
    }

    private static FakeSourceImporter CreateSuccessfulCsvImporter()
    {
        return new FakeSourceImporter(ImportSourceKind.Csv)
        {
            ProbeResultFactory = request => new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-csv-success",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "sender" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "recipient" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "message_body" }
                ],
                PreviewRows =
                [
                    new ImportPreviewRow { RowNumber = 2, Values = ["2026-04-28T10:00:00Z", "Alpha", "Bravo", "First preview row"] },
                    new ImportPreviewRow { RowNumber = 3, Values = ["2026-04-28T10:05:00Z", "Bravo", "Alpha", "Second preview row"] }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Timestamp, SourceColumnName = "timestamp" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Sender, SourceColumnName = "sender" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Recipient, SourceColumnName = "recipient" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.MessageBody, SourceColumnName = "message_body" }
                ]
            },
            PreviewResultFactory = request => new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-csv-success",
                SourceKind = ImportSourceKind.Csv,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true,
                DetectedDelimiter = ',',
                HasHeaderRow = true,
                Columns =
                [
                    new ImportPreviewColumn { Ordinal = 0, SourceColumnName = "timestamp" },
                    new ImportPreviewColumn { Ordinal = 1, SourceColumnName = "sender" },
                    new ImportPreviewColumn { Ordinal = 2, SourceColumnName = "recipient" },
                    new ImportPreviewColumn { Ordinal = 3, SourceColumnName = "message_body" }
                ],
                Rows =
                [
                    new ImportPreviewRow { RowNumber = 2, Values = ["2026-04-28T10:00:00Z", "Alpha", "Bravo", "First preview row"] },
                    new ImportPreviewRow { RowNumber = 3, Values = ["2026-04-28T10:05:00Z", "Bravo", "Alpha", "Second preview row"] }
                ],
                FieldMappingSuggestions =
                [
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Timestamp, SourceColumnName = "timestamp" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Sender, SourceColumnName = "sender" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.Recipient, SourceColumnName = "recipient" },
                    new ImportFieldMappingSuggestion { DumpLensFieldName = ImportFieldNames.MessageBody, SourceColumnName = "message_body" }
                ],
                Warnings =
                [
                    new ImportWarning
                    {
                        Code = ImportWarningCodes.PreviewTruncated,
                        Message = "Preview was limited to the first 10 rows."
                    }
                ]
            }
        };
    }

    private static IEnumerable<object> GetCollection(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>();
    }

    private static bool GetBooleanProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<bool>(value);
    }

    private static int GetIntProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<int>(value);
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.NotNull(value);
        return value!;
    }

    private static string GetStringProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<string>(value);
    }

    private static string GetImportWizardViewPath()
    {
        return Path.Combine(FindRepositoryRoot(), "src", "DumpLens.App", "ImportWizardView.xaml");
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "DumpLens.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not locate the DumpLens repository root.");
    }

    private static async Task InvokeAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        var task = method!.Invoke(instance, null);
        var awaitedTask = Assert.IsAssignableFrom<Task>(task);
        await awaitedTask;
    }

    private static void SelectSourceType(object viewModel, string label)
    {
        var option = GetCollection(viewModel, "SourceTypeOptions")
            .Single(sourceType => string.Equals(GetStringProperty(sourceType, "Label"), label, StringComparison.Ordinal));
        SetPropertyValue(viewModel, "SelectedSourceTypeOption", option);
    }

    private static void SetPropertyValue(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private sealed class FakeSourceImporter : ISourceImporter
    {
        public FakeSourceImporter(ImportSourceKind sourceKind)
        {
            SourceKind = sourceKind;
        }

        public int PreviewCallCount { get; private set; }

        public Func<ImportPreviewRequest, ImportPreviewResult>? PreviewResultFactory { get; init; }

        public int ProbeCallCount { get; private set; }

        public Func<ImportProbeRequest, ImportProbeResult>? ProbeResultFactory { get; init; }

        public ImportPreviewRequest? LastPreviewRequest { get; private set; }

        public ImportProbeRequest? LastProbeRequest { get; private set; }

        public ImportSourceKind SourceKind { get; }

        public bool CanHandle(string filePath)
        {
            return true;
        }

        public Task<ImportPreviewResult> PreviewAsync(ImportPreviewRequest request, CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            LastPreviewRequest = request;
            var result = PreviewResultFactory?.Invoke(request) ?? new ImportPreviewResult
            {
                CorrelationId = request.CorrelationId ?? "preview-default",
                SourceKind = SourceKind,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true
            };

            return Task.FromResult(result);
        }

        public Task<ImportProbeResult> ProbeAsync(ImportProbeRequest request, CancellationToken cancellationToken = default)
        {
            ProbeCallCount++;
            LastProbeRequest = request;
            var result = ProbeResultFactory?.Invoke(request) ?? new ImportProbeResult
            {
                CorrelationId = request.CorrelationId ?? "probe-default",
                SourceKind = SourceKind,
                FilePath = request.FilePath,
                FileName = Path.GetFileName(request.FilePath),
                FileExtension = Path.GetExtension(request.FilePath),
                IsSupported = true,
                IsTabular = true
            };

            return Task.FromResult(result);
        }
    }

    private sealed record UiLogEntry(
        string Operation,
        string CorrelationId,
        string Message,
        IReadOnlyDictionary<string, string>? Fields);

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
