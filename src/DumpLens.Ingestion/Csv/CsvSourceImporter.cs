using System.Globalization;
using System.Text;
using DumpLens.Application.Imports;

namespace DumpLens.Ingestion.Csv;

public sealed class CsvSourceImporter : ISourceImporter
{
    private static readonly StringComparer ColumnNameComparer = StringComparer.Ordinal;

    private readonly CsvDelimiterDetector _delimiterDetector;
    private readonly CsvFieldMappingSuggester _fieldMappingSuggester;

    public CsvSourceImporter(
        CsvDelimiterDetector? delimiterDetector = null,
        CsvFieldMappingSuggester? fieldMappingSuggester = null)
    {
        _delimiterDetector = delimiterDetector ?? new CsvDelimiterDetector();
        _fieldMappingSuggester = fieldMappingSuggester ?? new CsvFieldMappingSuggester();
    }

    public ImportSourceKind SourceKind => ImportSourceKind.Csv;

    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
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

        var correlationId = ResolveCorrelationId(request.CorrelationId);
        var analysis = AnalyzeFile(request.FilePath, request.PreviewRowCount, correlationId, cancellationToken);

        return Task.FromResult(new ImportProbeResult
        {
            CorrelationId = correlationId,
            SourceKind = SourceKind,
            FilePath = analysis.FilePath,
            FileName = analysis.FileName,
            FileExtension = analysis.FileExtension,
            IsSupported = analysis.IsSupported,
            IsTabular = analysis.IsTabular,
            DetectedDelimiter = analysis.Delimiter,
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

        var correlationId = ResolveCorrelationId(request.CorrelationId);
        var analysis = AnalyzeFile(request.FilePath, request.RowCount, correlationId, cancellationToken);

        return Task.FromResult(new ImportPreviewResult
        {
            CorrelationId = correlationId,
            SourceKind = SourceKind,
            FilePath = analysis.FilePath,
            FileName = analysis.FileName,
            FileExtension = analysis.FileExtension,
            IsSupported = analysis.IsSupported,
            IsTabular = analysis.IsTabular,
            DetectedDelimiter = analysis.Delimiter,
            HasHeaderRow = analysis.HasHeaderRow,
            RequestedRowCount = request.RowCount,
            ReturnedRowCount = analysis.PreviewRows.Count,
            Columns = analysis.Columns,
            Rows = analysis.PreviewRows,
            FieldMappingSuggestions = analysis.FieldMappingSuggestions,
            Warnings = analysis.Warnings
        });
    }

    private CsvAnalysisResult AnalyzeFile(
        string filePath,
        int previewRowCount,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var extension = Path.GetExtension(fullPath);
        var warnings = new List<ImportWarning>();

        if (!CanHandle(fullPath))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.UnsupportedFileExtension,
                "Only .csv files and tabular .txt files are supported for CSV probing in T0017."));

            return BuildTerminalResult(fullPath, extension, warnings, correlationId);
        }

        if (!File.Exists(fullPath))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.FileNotFound,
                "The requested file could not be found."));

            return BuildTerminalResult(fullPath, extension, warnings, correlationId);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var delimiterResult = _delimiterDetector.Detect(fullPath);
            if (delimiterResult.SampleRecords.Count == 0)
            {
                warnings.Add(CreateWarning(
                    ImportWarningCodes.EmptyFile,
                    "The file is empty or contains only blank rows."));

                return BuildTerminalResult(fullPath, extension, warnings, correlationId);
            }

            if (!delimiterResult.IsTabular || delimiterResult.Delimiter is null)
            {
                warnings.Add(CreateWarning(
                    ImportWarningCodes.UnsupportedFileExtension,
                    "The file does not appear to contain supported tabular CSV-style data."));

                return BuildTerminalResult(fullPath, extension, warnings, correlationId);
            }

            var parsedRecords = ReadPreviewRecords(fullPath, delimiterResult.Delimiter.Value, previewRowCount + 2, cancellationToken);
            if (parsedRecords.Count == 0)
            {
                warnings.Add(CreateWarning(
                    ImportWarningCodes.EmptyFile,
                    "The file is empty or contains only blank rows."));

                return BuildTerminalResult(fullPath, extension, warnings, correlationId, delimiterResult.Delimiter, isTabular: true);
            }

            var headerDecision = DetectHeaderRow(parsedRecords);
            if (!headerDecision.HasHeaderRow)
            {
                warnings.Add(CreateWarning(
                    headerDecision.IsAmbiguous ? ImportWarningCodes.AmbiguousHeaderRow : ImportWarningCodes.MissingHeaderRow,
                    headerDecision.IsAmbiguous
                        ? "The first row appears header-like but could not be mapped confidently, so generic column names were generated."
                        : "The file does not appear to contain a reliable header row, so generic column names were generated."));
            }

            var dataRecords = parsedRecords
                .Skip(headerDecision.HasHeaderRow ? 1 : 0)
                .ToList();
            var isPreviewTruncated = dataRecords.Count > previewRowCount;
            var displayedRecords = dataRecords.Take(previewRowCount).ToList();
            var maxDisplayedWidth = Math.Max(
                headerDecision.HeaderValues.Count,
                displayedRecords.Count == 0 ? 0 : displayedRecords.Max(static record => record.Fields.Length));
            var columns = BuildColumns(headerDecision, maxDisplayedWidth);
            var previewRows = BuildPreviewRows(displayedRecords, columns.Count);

            AddRowWidthWarnings(headerDecision, displayedRecords, warnings);

            var fieldMappingSuggestions = _fieldMappingSuggester.Suggest(columns);
            AddFieldMappingWarnings(fieldMappingSuggestions, warnings);

            if (isPreviewTruncated)
            {
                warnings.Add(CreateWarning(
                    ImportWarningCodes.PreviewTruncated,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Preview was limited to the first {0} rows.",
                        previewRowCount)));
            }

            return new CsvAnalysisResult
            {
                CorrelationId = correlationId,
                FilePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                FileExtension = extension,
                IsSupported = true,
                IsTabular = true,
                Delimiter = delimiterResult.Delimiter,
                HasHeaderRow = headerDecision.HasHeaderRow,
                Columns = columns,
                PreviewRows = previewRows,
                FieldMappingSuggestions = fieldMappingSuggestions,
                Warnings = warnings
            };
        }
        catch (DecoderFallbackException)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.UnsupportedEncoding,
                "The file encoding could not be read as UTF-8 or UTF-8 with BOM."));

            return BuildTerminalResult(fullPath, extension, warnings, correlationId);
        }
        catch (IOException)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The file could not be read safely."));

            return BuildTerminalResult(fullPath, extension, warnings, correlationId);
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The file could not be read safely."));

            return BuildTerminalResult(fullPath, extension, warnings, correlationId);
        }
        catch (FormatException)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.UnreadableFile,
                "The file contains malformed quoted CSV data and could not be previewed safely."));

            return BuildTerminalResult(fullPath, extension, warnings, correlationId);
        }
    }

    private HeaderDecision DetectHeaderRow(IReadOnlyList<CsvParsedRecord> records)
    {
        var firstRecord = records[0].Fields;
        var secondRecord = records.Count > 1 ? records[1].Fields : Array.Empty<string>();
        var nonEmptyCount = firstRecord.Count(static value => !string.IsNullOrWhiteSpace(value));
        var distinctCount = firstRecord
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var knownHeaderMatches = _fieldMappingSuggester.CountKnownHeaderMatches(firstRecord);
        var firstRowDataLikeCount = firstRecord.Count(LooksLikeDataValue);
        var secondRowDataLikeCount = secondRecord.Count(LooksLikeDataValue);
        var missingNamesCount = firstRecord.Length - nonEmptyCount;
        var duplicateNamesCount = nonEmptyCount - distinctCount;
        var headerScore =
            (knownHeaderMatches * 3)
            + (nonEmptyCount == firstRecord.Length ? 1 : 0)
            + (distinctCount == nonEmptyCount ? 1 : 0)
            - (missingNamesCount * 2)
            - duplicateNamesCount
            - firstRowDataLikeCount
            + (secondRowDataLikeCount > firstRowDataLikeCount ? 1 : 0);

        if (knownHeaderMatches >= 1 && headerScore >= 2)
        {
            return new HeaderDecision(true, false, firstRecord);
        }

        var appearsHeaderLike =
            nonEmptyCount >= Math.Max(1, firstRecord.Length / 2)
            && distinctCount >= Math.Max(1, nonEmptyCount / 2)
            && firstRowDataLikeCount <= Math.Max(1, firstRecord.Length / 2)
            && secondRowDataLikeCount > firstRowDataLikeCount;

        return new HeaderDecision(false, appearsHeaderLike, Array.Empty<string>());
    }

    private static List<CsvParsedRecord> ReadPreviewRecords(
        string filePath,
        char delimiter,
        int maxRecords,
        CancellationToken cancellationToken)
    {
        var records = new List<CsvParsedRecord>(maxRecords);

        using var reader = new CsvRecordReader(filePath, delimiter);
        while (records.Count < maxRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fields = reader.ReadRecord();
            if (fields is null)
            {
                break;
            }

            if (fields.All(static value => string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            records.Add(new CsvParsedRecord(reader.RecordNumber, fields));
        }

        return records;
    }

    private static bool LooksLikeDataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmedValue = value.Trim();
        if (DateTimeOffset.TryParse(trimmedValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return true;
        }

        if (Guid.TryParse(trimmedValue, out _))
        {
            return true;
        }

        if (double.TryParse(trimmedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        var digitCount = trimmedValue.Count(char.IsDigit);
        if (digitCount >= 7)
        {
            return true;
        }

        return trimmedValue.Length > 40;
    }

    private static List<ImportPreviewColumn> BuildColumns(HeaderDecision headerDecision, int columnCount)
    {
        var columns = new List<ImportPreviewColumn>(columnCount);

        for (var index = 0; index < columnCount; index++)
        {
            var headerValue = headerDecision.HasHeaderRow && index < headerDecision.HeaderValues.Count
                ? headerDecision.HeaderValues[index]
                : null;
            var hasUsableHeaderName = !string.IsNullOrWhiteSpace(headerValue);

            columns.Add(new ImportPreviewColumn
            {
                Ordinal = index,
                SourceColumnName = hasUsableHeaderName ? headerValue! : $"Column{index + 1}",
                IsGenerated = !hasUsableHeaderName
            });
        }

        return columns;
    }

    private static List<ImportPreviewRow> BuildPreviewRows(IReadOnlyList<CsvParsedRecord> displayedRecords, int columnCount)
    {
        return displayedRecords
            .Select(record => new ImportPreviewRow
            {
                RowNumber = record.RecordNumber,
                Values = PadValues(record.Fields, columnCount)
            })
            .ToList();
    }

    private static IReadOnlyList<string?> PadValues(IReadOnlyList<string> fields, int columnCount)
    {
        var values = new string?[columnCount];

        for (var index = 0; index < columnCount; index++)
        {
            values[index] = index < fields.Count ? fields[index] : null;
        }

        return values;
    }

    private static void AddRowWidthWarnings(
        HeaderDecision headerDecision,
        IReadOnlyList<CsvParsedRecord> displayedRecords,
        ICollection<ImportWarning> warnings)
    {
        if (displayedRecords.Count == 0)
        {
            return;
        }

        var expectedWidth = headerDecision.HasHeaderRow
            ? headerDecision.HeaderValues.Count
            : displayedRecords[0].Fields.Length;
        var firstInconsistentRecord = displayedRecords.FirstOrDefault(record => record.Fields.Length != expectedWidth);
        if (firstInconsistentRecord is null)
        {
            return;
        }

        warnings.Add(CreateWarning(
            ImportWarningCodes.InconsistentRowWidth,
            string.Format(
                CultureInfo.InvariantCulture,
                "One or more preview rows have a different column count than expected ({0}).",
                expectedWidth),
            firstInconsistentRecord.RecordNumber));
    }

    private static void AddFieldMappingWarnings(
        IReadOnlyList<ImportFieldMappingSuggestion> suggestions,
        ICollection<ImportWarning> warnings)
    {
        var suggestionByField = suggestions.ToDictionary(static suggestion => suggestion.DumpLensFieldName, ColumnNameComparer);

        if (suggestions.Any(static suggestion => suggestion.IsAmbiguous))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.AmbiguousFieldMapping,
                "One or more field mappings are ambiguous and need review."));
        }

        if (!HasMapping(suggestionByField, ImportFieldNames.Timestamp))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelyTimestampColumn,
                "No likely timestamp column was detected."));
        }

        var hasSenderOrCaller = HasMapping(suggestionByField, ImportFieldNames.Sender)
            || HasMapping(suggestionByField, ImportFieldNames.Caller);
        if (!hasSenderOrCaller)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelySenderOrCallerColumn,
                "No likely sender or caller column was detected."));
        }

        var hasRecipientOrCallee = HasMapping(suggestionByField, ImportFieldNames.Recipient)
            || HasMapping(suggestionByField, ImportFieldNames.Callee);
        if (!hasRecipientOrCallee)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelyRecipientOrCalleeColumn,
                "No likely recipient or callee column was detected."));
        }

        var looksMessageLike =
            HasMapping(suggestionByField, ImportFieldNames.Sender)
            || HasMapping(suggestionByField, ImportFieldNames.Recipient)
            || HasMapping(suggestionByField, ImportFieldNames.ThreadId)
            || HasMapping(suggestionByField, ImportFieldNames.MessageId);
        if (looksMessageLike && !HasMapping(suggestionByField, ImportFieldNames.MessageBody))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelyMessageBodyColumn,
                "No likely message body column was detected."));
        }
    }

    private static bool HasMapping(
        IReadOnlyDictionary<string, ImportFieldMappingSuggestion> suggestions,
        string fieldName)
    {
        return suggestions.TryGetValue(fieldName, out var suggestion)
            && !string.IsNullOrWhiteSpace(suggestion.SourceColumnName);
    }

    private CsvAnalysisResult BuildTerminalResult(
        string fullPath,
        string extension,
        IReadOnlyList<ImportWarning> warnings,
        string correlationId,
        char? delimiter = null,
        bool isTabular = false)
    {
        return new CsvAnalysisResult
        {
            CorrelationId = correlationId,
            FilePath = fullPath,
            FileName = Path.GetFileName(fullPath),
            FileExtension = extension,
            IsSupported = false,
            IsTabular = isTabular,
            Delimiter = delimiter,
            Warnings = warnings
        };
    }

    private static ImportWarning CreateWarning(
        string code,
        string message,
        int? rowNumber = null,
        string? columnName = null)
    {
        return new ImportWarning
        {
            Code = code,
            Message = message,
            RowNumber = rowNumber,
            ColumnName = columnName
        };
    }

    private static string ResolveCorrelationId(string? correlationId)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();
    }

    private sealed record CsvAnalysisResult
    {
        public string CorrelationId { get; init; } = string.Empty;

        public string FilePath { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string FileExtension { get; init; } = string.Empty;

        public bool IsSupported { get; init; }

        public bool IsTabular { get; init; }

        public char? Delimiter { get; init; }

        public bool HasHeaderRow { get; init; }

        public IReadOnlyList<ImportPreviewColumn> Columns { get; init; } = Array.Empty<ImportPreviewColumn>();

        public IReadOnlyList<ImportPreviewRow> PreviewRows { get; init; } = Array.Empty<ImportPreviewRow>();

        public IReadOnlyList<ImportFieldMappingSuggestion> FieldMappingSuggestions { get; init; } = Array.Empty<ImportFieldMappingSuggestion>();

        public IReadOnlyList<ImportWarning> Warnings { get; init; } = Array.Empty<ImportWarning>();
    }

    private sealed record CsvParsedRecord(int RecordNumber, string[] Fields);

    private sealed record HeaderDecision(
        bool HasHeaderRow,
        bool IsAmbiguous,
        IReadOnlyList<string> HeaderValues);
}
