using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DumpLens.Application.Imports;
using DumpLens.Ingestion.Tabular;

namespace DumpLens.Ingestion.Xlsx;

public sealed class XlsxSourceImporter : ISourceImporter
{
    private readonly ImportFieldMappingSuggester _fieldMappingSuggester;
    private readonly TabularHeaderDetector _headerDetector;

    public XlsxSourceImporter()
    {
        _fieldMappingSuggester = new ImportFieldMappingSuggester();
        _headerDetector = new TabularHeaderDetector(_fieldMappingSuggester);
    }

    public ImportSourceKind SourceKind => ImportSourceKind.Xlsx;

    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ImportProbeResult> ProbeAsync(
        ImportProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);

        if (!Path.IsPathRooted(request.FilePath))
        {
            throw new ArgumentException("The file path must be absolute.", nameof(request.FilePath));
        }

        if (request.PreviewRowCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PreviewRowCount), "The preview row count must be positive.");
        }

        var correlationId = TabularImportUtilities.ResolveCorrelationId(request.CorrelationId);
        var analysis = AnalyzeWorkbook(request.FilePath, request.PreviewRowCount, worksheetName: null, correlationId, cancellationToken);

        return Task.FromResult(new ImportProbeResult
        {
            CorrelationId = correlationId,
            SourceKind = SourceKind,
            FilePath = analysis.FilePath,
            FileName = analysis.FileName,
            FileExtension = analysis.FileExtension,
            IsSupported = analysis.IsSupported,
            IsTabular = analysis.IsTabular,
            WorksheetNames = analysis.WorksheetNames,
            SelectedWorksheetName = analysis.SelectedWorksheetName,
            HasHeaderRow = analysis.HasHeaderRow,
            RequestedPreviewRowCount = request.PreviewRowCount,
            ReturnedPreviewRowCount = analysis.PreviewRows.Count,
            Columns = analysis.Columns,
            PreviewRows = analysis.PreviewRows,
            FieldMappingSuggestions = analysis.FieldMappingSuggestions,
            Warnings = analysis.Warnings
        });
    }

    public Task<ImportPreviewResult> PreviewAsync(
        ImportPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);

        if (!Path.IsPathRooted(request.FilePath))
        {
            throw new ArgumentException("The file path must be absolute.", nameof(request.FilePath));
        }

        if (request.RowCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.RowCount), "The preview row count must be positive.");
        }

        var correlationId = TabularImportUtilities.ResolveCorrelationId(request.CorrelationId);
        var analysis = AnalyzeWorkbook(request.FilePath, request.RowCount, request.WorksheetName, correlationId, cancellationToken);

        return Task.FromResult(new ImportPreviewResult
        {
            CorrelationId = correlationId,
            SourceKind = SourceKind,
            FilePath = analysis.FilePath,
            FileName = analysis.FileName,
            FileExtension = analysis.FileExtension,
            IsSupported = analysis.IsSupported,
            IsTabular = analysis.IsTabular,
            WorksheetNames = analysis.WorksheetNames,
            SelectedWorksheetName = analysis.SelectedWorksheetName,
            HasHeaderRow = analysis.HasHeaderRow,
            RequestedRowCount = request.RowCount,
            ReturnedRowCount = analysis.PreviewRows.Count,
            Columns = analysis.Columns,
            Rows = analysis.PreviewRows,
            FieldMappingSuggestions = analysis.FieldMappingSuggestions,
            Warnings = analysis.Warnings
        });
    }

    public Task<ImportTabularDataResult> ReadTabularDataAsync(
        ImportTabularDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);

        if (!Path.IsPathRooted(request.FilePath))
        {
            throw new ArgumentException("The file path must be absolute.", nameof(request.FilePath));
        }

        if (request.RowLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.RowLimit), "The row limit must be positive when provided.");
        }

        var correlationId = TabularImportUtilities.ResolveCorrelationId(request.CorrelationId);
        var analysis = ReadAllRows(request.FilePath, request.WorksheetName, request.RowLimit, correlationId, cancellationToken);

        return Task.FromResult(new ImportTabularDataResult
        {
            CorrelationId = correlationId,
            SourceKind = SourceKind,
            FilePath = analysis.FilePath,
            FileName = analysis.FileName,
            FileExtension = analysis.FileExtension,
            IsSupported = analysis.IsSupported,
            IsTabular = analysis.IsTabular,
            WorksheetNames = analysis.WorksheetNames,
            SelectedWorksheetName = analysis.SelectedWorksheetName,
            HasHeaderRow = analysis.HasHeaderRow,
            ReturnedRowCount = analysis.PreviewRows.Count,
            Columns = analysis.Columns,
            Rows = analysis.PreviewRows,
            Warnings = analysis.Warnings
        });
    }

    private WorkbookAnalysisResult AnalyzeWorkbook(
        string filePath,
        int previewRowCount,
        string? worksheetName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var extension = Path.GetExtension(fullPath);
        var warnings = new List<ImportWarning>();

        if (!CanHandle(fullPath))
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnsupportedFileExtension,
                "Only .xlsx workbooks are supported for XLSX probing in T0018."));

            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }

        if (!File.Exists(fullPath))
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.FileNotFound,
                "The requested file could not be found."));

            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workbookMetadata = ReadWorkbookMetadata(fullPath);
            if (workbookMetadata.WorksheetNames.Count == 0)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.NoWorksheets,
                    "The workbook does not contain any worksheets."));
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.EmptyWorkbook,
                    "The workbook does not contain any non-empty worksheets."));

                return BuildTerminalResult(
                    fullPath,
                    extension,
                    correlationId,
                    warnings,
                    isSupported: true,
                    worksheetNames: workbookMetadata.WorksheetNames);
            }

            using var workbook = new XLWorkbook(fullPath);

            var selectedWorksheet = ResolveSelectedWorksheet(workbook, workbookMetadata.WorksheetNames, worksheetName, warnings, cancellationToken);
            if (selectedWorksheet is null)
            {
                return BuildTerminalResult(
                    fullPath,
                    extension,
                    correlationId,
                    warnings,
                    isSupported: true,
                    worksheetNames: workbookMetadata.WorksheetNames);
            }

            var previewRecords = ReadPreviewRecords(selectedWorksheet, previewRowCount + 2, cancellationToken);
            if (previewRecords.Count == 0)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.EmptyWorksheet,
                    "The selected worksheet does not contain any non-empty rows.",
                    selectedWorksheet.Name));

                if (IsWorkbookEmpty(workbook, cancellationToken))
                {
                    warnings.Add(TabularImportUtilities.CreateWarning(
                        ImportWarningCodes.EmptyWorkbook,
                        "The workbook does not contain any non-empty worksheets."));
                }

                return BuildTerminalResult(
                    fullPath,
                    extension,
                    correlationId,
                    warnings,
                    isSupported: true,
                    worksheetNames: workbookMetadata.WorksheetNames,
                    selectedWorksheetName: selectedWorksheet.Name);
            }

            var headerDecision = _headerDetector.DetectHeaderRow(previewRecords);
            if (!headerDecision.HasHeaderRow)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    headerDecision.IsAmbiguous ? ImportWarningCodes.AmbiguousHeaderRow : ImportWarningCodes.MissingHeaderRow,
                    headerDecision.IsAmbiguous
                        ? "The first row appears header-like but could not be mapped confidently, so generic column names were generated."
                        : "The worksheet does not appear to contain a reliable header row, so generic column names were generated.",
                    selectedWorksheet.Name));
            }

            var dataRecords = previewRecords
                .Skip(headerDecision.HasHeaderRow ? 1 : 0)
                .ToList();
            var isPreviewTruncated = dataRecords.Count > previewRowCount;
            var displayedRecords = dataRecords.Take(previewRowCount).ToList();
            var maxDisplayedWidth = Math.Max(
                headerDecision.HeaderValues.Count,
                displayedRecords.Count == 0 ? 0 : displayedRecords.Max(static record => record.Values.Count));
            var columns = TabularImportUtilities.BuildColumns(headerDecision, maxDisplayedWidth);
            var previewRows = TabularImportUtilities.BuildPreviewRows(displayedRecords, columns.Count);

            TabularImportUtilities.AddRowWidthWarnings(headerDecision, displayedRecords, warnings, selectedWorksheet.Name);

            var fieldMappingSuggestions = _fieldMappingSuggester.Suggest(columns);
            TabularImportUtilities.AddFieldMappingWarnings(fieldMappingSuggestions, warnings, selectedWorksheet.Name);

            if (isPreviewTruncated)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.PreviewTruncated,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Preview was limited to the first {0} rows.",
                        previewRowCount),
                    selectedWorksheet.Name));
            }

            return new WorkbookAnalysisResult
            {
                CorrelationId = correlationId,
                FilePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                FileExtension = extension,
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = workbookMetadata.WorksheetNames,
                SelectedWorksheetName = selectedWorksheet.Name,
                HasHeaderRow = headerDecision.HasHeaderRow,
                Columns = columns,
                PreviewRows = previewRows,
                FieldMappingSuggestions = fieldMappingSuggestions,
                Warnings = warnings
            };
        }
        catch (IOException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
        catch (InvalidDataException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
        catch (OpenXmlPackageException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
    }

    private WorkbookAnalysisResult ReadAllRows(
        string filePath,
        string? worksheetName,
        int? rowLimit,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var extension = Path.GetExtension(fullPath);
        var warnings = new List<ImportWarning>();

        if (!CanHandle(fullPath))
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnsupportedFileExtension,
                "Only .xlsx workbooks are supported for XLSX message import."));

            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }

        if (!File.Exists(fullPath))
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.FileNotFound,
                "The requested file could not be found."));

            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workbookMetadata = ReadWorkbookMetadata(fullPath);
            if (workbookMetadata.WorksheetNames.Count == 0)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.NoWorksheets,
                    "The workbook does not contain any worksheets."));
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.EmptyWorkbook,
                    "The workbook does not contain any non-empty worksheets."));

                return BuildTerminalResult(
                    fullPath,
                    extension,
                    correlationId,
                    warnings,
                    isSupported: true,
                    worksheetNames: workbookMetadata.WorksheetNames);
            }

            using var workbook = new XLWorkbook(fullPath);

            var selectedWorksheet = ResolveSelectedWorksheet(workbook, workbookMetadata.WorksheetNames, worksheetName, warnings, cancellationToken);
            if (selectedWorksheet is null)
            {
                return BuildTerminalResult(
                    fullPath,
                    extension,
                    correlationId,
                    warnings,
                    isSupported: true,
                    worksheetNames: workbookMetadata.WorksheetNames);
            }

            var previewRecords = ReadPreviewRecords(selectedWorksheet, rowLimit, cancellationToken);
            if (previewRecords.Count == 0)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.EmptyWorksheet,
                    "The selected worksheet does not contain any non-empty rows.",
                    selectedWorksheet.Name));

                if (IsWorkbookEmpty(workbook, cancellationToken))
                {
                    warnings.Add(TabularImportUtilities.CreateWarning(
                        ImportWarningCodes.EmptyWorkbook,
                        "The workbook does not contain any non-empty worksheets."));
                }

                return BuildTerminalResult(
                    fullPath,
                    extension,
                    correlationId,
                    warnings,
                    isSupported: true,
                    worksheetNames: workbookMetadata.WorksheetNames,
                    selectedWorksheetName: selectedWorksheet.Name);
            }

            var headerDecision = _headerDetector.DetectHeaderRow(previewRecords);
            if (!headerDecision.HasHeaderRow)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    headerDecision.IsAmbiguous ? ImportWarningCodes.AmbiguousHeaderRow : ImportWarningCodes.MissingHeaderRow,
                    headerDecision.IsAmbiguous
                        ? "The first row appears header-like but could not be mapped confidently, so generic column names were generated."
                        : "The worksheet does not appear to contain a reliable header row, so generic column names were generated.",
                    selectedWorksheet.Name));
            }

            var dataRecords = previewRecords
                .Skip(headerDecision.HasHeaderRow ? 1 : 0)
                .ToList();
            var maxDisplayedWidth = Math.Max(
                headerDecision.HeaderValues.Count,
                dataRecords.Count == 0 ? 0 : dataRecords.Max(static record => record.Values.Count));
            var columns = TabularImportUtilities.BuildColumns(headerDecision, maxDisplayedWidth);
            var previewRows = TabularImportUtilities.BuildPreviewRows(dataRecords, columns.Count);

            TabularImportUtilities.AddRowWidthWarnings(headerDecision, dataRecords, warnings, selectedWorksheet.Name);

            if (rowLimit.HasValue && dataRecords.Count >= rowLimit.Value)
            {
                warnings.Add(TabularImportUtilities.CreateWarning(
                    ImportWarningCodes.PreviewTruncated,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Row reading was limited to the first {0} rows.",
                        rowLimit.Value),
                    selectedWorksheet.Name));
            }

            return new WorkbookAnalysisResult
            {
                CorrelationId = correlationId,
                FilePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                FileExtension = extension,
                IsSupported = true,
                IsTabular = true,
                WorksheetNames = workbookMetadata.WorksheetNames,
                SelectedWorksheetName = selectedWorksheet.Name,
                HasHeaderRow = headerDecision.HasHeaderRow,
                Columns = columns,
                PreviewRows = previewRows,
                Warnings = warnings
            };
        }
        catch (IOException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
        catch (InvalidDataException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
        catch (OpenXmlPackageException)
        {
            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The workbook could not be read safely."));
            return BuildTerminalResult(fullPath, extension, correlationId, warnings);
        }
    }

    private static WorkbookMetadata ReadWorkbookMetadata(string fullPath)
    {
        using var document = SpreadsheetDocument.Open(fullPath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("The workbook is missing its workbook part.");
        var worksheetNames = workbookPart.Workbook.Sheets?
            .Elements<Sheet>()
            .Select(static sheet => sheet.Name?.Value)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .ToList()
            ?? [];

        return new WorkbookMetadata(worksheetNames);
    }

    private static IXLWorksheet? ResolveSelectedWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<string> worksheetNames,
        string? requestedWorksheetName,
        ICollection<ImportWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedWorksheetName))
        {
            var trimmedRequestedWorksheetName = requestedWorksheetName.Trim();
            var requestedWorksheet = workbook.Worksheets.FirstOrDefault(worksheet =>
                string.Equals(worksheet.Name, trimmedRequestedWorksheetName, StringComparison.OrdinalIgnoreCase));
            if (requestedWorksheet is not null)
            {
                return requestedWorksheet;
            }

            warnings.Add(TabularImportUtilities.CreateWarning(
                ImportWarningCodes.SelectedWorksheetNotFound,
                $"The worksheet '{trimmedRequestedWorksheetName}' was not found in the workbook.",
                trimmedRequestedWorksheetName));
            return null;
        }

        foreach (var worksheetName in worksheetNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var worksheet = workbook.Worksheet(worksheetName);
            if (ReadPreviewRecords(worksheet, 1, cancellationToken).Count > 0)
            {
                return worksheet;
            }
        }

        return worksheetNames.Count == 0
            ? null
            : workbook.Worksheet(worksheetNames[0]);
    }

    private static bool IsWorkbookEmpty(XLWorkbook workbook, CancellationToken cancellationToken)
    {
        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReadPreviewRecords(worksheet, 1, cancellationToken).Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static List<TabularPreviewRecord> ReadPreviewRecords(
        IXLWorksheet worksheet,
        int? maxRecords,
        CancellationToken cancellationToken)
    {
        var records = new List<TabularPreviewRecord>(maxRecords ?? 32);
        var usedRange = worksheet.RangeUsed(XLCellsUsedOptions.AllContents);
        if (usedRange is null)
        {
            return records;
        }

        foreach (var row in usedRange.Rows())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var values = ExtractRowValues(row);
            if (values.Count == 0 || values.All(static value => string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            records.Add(new TabularPreviewRecord(row.RowNumber(), values));
            if (maxRecords.HasValue && records.Count >= maxRecords.Value)
            {
                break;
            }
        }

        return records;
    }

    private static IReadOnlyList<string?> ExtractRowValues(IXLRangeRow row)
    {
        var usedCells = row.CellsUsed(XLCellsUsedOptions.AllContents)
            .OrderBy(static cell => cell.Address.ColumnNumber)
            .ToArray();
        if (usedCells.Length == 0)
        {
            return Array.Empty<string?>();
        }

        var lastColumnNumber = usedCells[^1].Address.ColumnNumber;
        var values = new string?[lastColumnNumber];

        for (var columnNumber = 1; columnNumber <= lastColumnNumber; columnNumber++)
        {
            values[columnNumber - 1] = NormalizeCellValue(row.Cell(columnNumber));
        }

        return values;
    }

    private static string? NormalizeCellValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        return cell.DataType switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
            XLDataType.DateTime => cell.GetDateTime().ToString("O", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => cell.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
            XLDataType.Number => cell.GetDouble().ToString("0.###############################", CultureInfo.InvariantCulture),
            XLDataType.Text => cell.GetString(),
            XLDataType.Error => cell.Value.ToString(CultureInfo.InvariantCulture),
            _ => cell.GetString()
        };
    }

    private static WorkbookAnalysisResult BuildTerminalResult(
        string fullPath,
        string extension,
        string correlationId,
        IReadOnlyList<ImportWarning> warnings,
        bool isSupported = false,
        IReadOnlyList<string>? worksheetNames = null,
        string? selectedWorksheetName = null)
    {
        return new WorkbookAnalysisResult
        {
            CorrelationId = correlationId,
            FilePath = fullPath,
            FileName = Path.GetFileName(fullPath),
            FileExtension = extension,
            IsSupported = isSupported,
            IsTabular = isSupported,
            WorksheetNames = worksheetNames ?? Array.Empty<string>(),
            SelectedWorksheetName = selectedWorksheetName,
            Warnings = warnings
        };
    }

    private sealed record WorkbookMetadata(IReadOnlyList<string> WorksheetNames);

    private sealed record WorkbookAnalysisResult
    {
        public string CorrelationId { get; init; } = string.Empty;

        public string FilePath { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string FileExtension { get; init; } = string.Empty;

        public bool IsSupported { get; init; }

        public bool IsTabular { get; init; }

        public IReadOnlyList<string> WorksheetNames { get; init; } = Array.Empty<string>();

        public string? SelectedWorksheetName { get; init; }

        public bool HasHeaderRow { get; init; }

        public IReadOnlyList<ImportPreviewColumn> Columns { get; init; } = Array.Empty<ImportPreviewColumn>();

        public IReadOnlyList<ImportPreviewRow> PreviewRows { get; init; } = Array.Empty<ImportPreviewRow>();

        public IReadOnlyList<ImportFieldMappingSuggestion> FieldMappingSuggestions { get; init; } = Array.Empty<ImportFieldMappingSuggestion>();

        public IReadOnlyList<ImportWarning> Warnings { get; init; } = Array.Empty<ImportWarning>();
    }
}
