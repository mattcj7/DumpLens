using System.Globalization;
using DumpLens.Application.Imports;

namespace DumpLens.Ingestion.Tabular;

internal static class TabularImportUtilities
{
    private static readonly StringComparer ColumnNameComparer = StringComparer.Ordinal;

    public static IReadOnlyList<ImportPreviewColumn> BuildColumns(TabularHeaderDecision headerDecision, int columnCount)
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

    public static IReadOnlyList<ImportPreviewRow> BuildPreviewRows(
        IReadOnlyList<TabularPreviewRecord> displayedRecords,
        int columnCount)
    {
        return displayedRecords
            .Select(record => new ImportPreviewRow
            {
                RowNumber = record.RowNumber,
                Values = PadValues(record.Values, columnCount)
            })
            .ToList();
    }

    public static void AddRowWidthWarnings(
        TabularHeaderDecision headerDecision,
        IReadOnlyList<TabularPreviewRecord> displayedRecords,
        ICollection<ImportWarning> warnings,
        string? worksheetName = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        if (displayedRecords.Count == 0)
        {
            return;
        }

        var expectedWidth = headerDecision.HasHeaderRow
            ? headerDecision.HeaderValues.Count
            : displayedRecords[0].Values.Count;
        var firstInconsistentRecord = displayedRecords.FirstOrDefault(record => record.Values.Count != expectedWidth);
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
            worksheetName,
            firstInconsistentRecord.RowNumber));
    }

    public static void AddFieldMappingWarnings(
        IReadOnlyList<ImportFieldMappingSuggestion> suggestions,
        ICollection<ImportWarning> warnings,
        string? worksheetName = null)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        ArgumentNullException.ThrowIfNull(warnings);

        var suggestionByField = suggestions.ToDictionary(static suggestion => suggestion.DumpLensFieldName, ColumnNameComparer);

        if (suggestions.Any(static suggestion => suggestion.IsAmbiguous))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.AmbiguousFieldMapping,
                "One or more field mappings are ambiguous and need review.",
                worksheetName));
        }

        if (!HasMapping(suggestionByField, ImportFieldNames.Timestamp))
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelyTimestampColumn,
                "No likely timestamp column was detected.",
                worksheetName));
        }

        var hasSenderOrCaller = HasMapping(suggestionByField, ImportFieldNames.Sender)
            || HasMapping(suggestionByField, ImportFieldNames.Caller);
        if (!hasSenderOrCaller)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelySenderOrCallerColumn,
                "No likely sender or caller column was detected.",
                worksheetName));
        }

        var hasRecipientOrCallee = HasMapping(suggestionByField, ImportFieldNames.Recipient)
            || HasMapping(suggestionByField, ImportFieldNames.Callee);
        if (!hasRecipientOrCallee)
        {
            warnings.Add(CreateWarning(
                ImportWarningCodes.NoLikelyRecipientOrCalleeColumn,
                "No likely recipient or callee column was detected.",
                worksheetName));
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
                "No likely message body column was detected.",
                worksheetName));
        }
    }

    public static ImportWarning CreateWarning(
        string code,
        string message,
        string? worksheetName = null,
        int? rowNumber = null,
        string? columnName = null)
    {
        return new ImportWarning
        {
            Code = code,
            Message = message,
            WorksheetName = worksheetName,
            RowNumber = rowNumber,
            ColumnName = columnName
        };
    }

    public static string ResolveCorrelationId(string? correlationId)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();
    }

    private static bool HasMapping(
        IReadOnlyDictionary<string, ImportFieldMappingSuggestion> suggestions,
        string fieldName)
    {
        return suggestions.TryGetValue(fieldName, out var suggestion)
            && !string.IsNullOrWhiteSpace(suggestion.SourceColumnName);
    }

    private static IReadOnlyList<string?> PadValues(IReadOnlyList<string?> fields, int columnCount)
    {
        var values = new string?[columnCount];

        for (var index = 0; index < columnCount; index++)
        {
            values[index] = index < fields.Count ? fields[index] : null;
        }

        return values;
    }
}
