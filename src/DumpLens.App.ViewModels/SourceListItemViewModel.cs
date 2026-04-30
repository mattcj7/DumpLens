using System.Globalization;
using DumpLens.Application.Sources;

namespace DumpLens.App.ViewModels;

public sealed class SourceListItemViewModel
{
    public SourceListItemViewModel(SourceImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        SourceImportId = summary.SourceImportId;
        SourceName = summary.SourceName;
        SourceType = summary.SourceType;
        Platform = summary.Platform;
        ImportStatus = summary.ImportStatus;
        RecordCount = summary.RecordCount;
        WarningCount = summary.WarningCount;
        ImportedAtUtc = summary.ImportedAtUtc;
        OriginalFilename = summary.OriginalFilename;
        FileSizeBytes = summary.FileSizeBytes;
        FileSha256 = summary.FileSha256;
        HashPrefix = GetHashPrefix(summary.FileSha256);
        ImportedAtDisplay = FormatUtc(summary.ImportedAtUtc);
        FileSizeDisplay = FormatBytes(summary.FileSizeBytes);
        PlatformDisplay = string.IsNullOrWhiteSpace(summary.Platform) ? "-" : summary.Platform!;
    }

    public string FileSha256 { get; }

    public long? FileSizeBytes { get; }

    public string FileSizeDisplay { get; }

    public string HashPrefix { get; }

    public DateTimeOffset ImportedAtUtc { get; }

    public string ImportedAtDisplay { get; }

    public string ImportStatus { get; }

    public string OriginalFilename { get; }

    public string PlatformDisplay { get; }

    public int RecordCount { get; }

    public string? Platform { get; }

    public string SourceImportId { get; }

    public string SourceName { get; }

    public string SourceType { get; }

    public int WarningCount { get; }

    private static string FormatBytes(long? value)
    {
        return value.HasValue
            ? value.Value.ToString("#,0", CultureInfo.InvariantCulture)
            : "-";
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    private static string GetHashPrefix(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return "-";
        }

        var trimmed = hash.Trim();
        return trimmed.Length <= 12
            ? trimmed
            : trimmed[..12];
    }
}
