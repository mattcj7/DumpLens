using System.Globalization;
using DumpLens.Application.Sources;

namespace DumpLens.App.ViewModels;

public sealed class SourceDetailViewModel : InspectorViewModelBase
{
    private SourceDetailViewModel(string description)
        : base("Source Detail", description)
    {
        StateMessage = description;
        WarningSummary = new SourceWarningSummaryViewModel(new SourceWarningSummary());
        NotesIndicator = "Not present";
        SourceMetadataIndicator = "Not present";
        PlatformDisplay = "-";
        ImportedByUserIdDisplay = "-";
        StoredFilePathDisplay = "-";
        FileSizeDisplay = "-";
    }

    public string FileSha256 { get; private init; } = "-";

    public string FileSizeDisplay { get; private init; }

    public bool HasError => StateKind == SourceDetailStateKind.Error;

    public bool HasSource => StateKind == SourceDetailStateKind.Source;

    public string ImportedAtDisplay { get; private init; } = "-";

    public string ImportedByUserIdDisplay { get; private init; }

    public string ImportStatus { get; private init; } = "-";

    public bool IsEmptyState => StateKind == SourceDetailStateKind.Empty;

    public bool IsLoading => StateKind == SourceDetailStateKind.Loading;

    public string NotesIndicator { get; private init; }

    public string OriginalFilename { get; private init; } = "-";

    public string PlatformDisplay { get; private init; }

    public string RecordCountDisplay { get; private init; } = "0";

    public string SourceImportId { get; private init; } = "-";

    public string SourceMetadataIndicator { get; private init; }

    public string SourceName { get; private init; } = "-";

    public string SourceType { get; private init; } = "-";

    public string StateMessage { get; private init; }

    public string StoredFilePathDisplay { get; private init; }

    public string WarningCountDisplay { get; private init; } = "0";

    public SourceWarningSummaryViewModel WarningSummary { get; private init; }

    public static SourceDetailViewModel CreateActiveCaseMissing()
    {
        return new SourceDetailViewModel("Create or open a case to inspect source details.");
    }

    public static SourceDetailViewModel CreateLoadFailure()
    {
        return new SourceDetailViewModel("Source details could not be loaded safely.")
        {
            StateKind = SourceDetailStateKind.Error
        };
    }

    public static SourceDetailViewModel CreateLoading(string sourceImportId)
    {
        return new SourceDetailViewModel("Loading safe source details.")
        {
            StateKind = SourceDetailStateKind.Loading,
            SourceImportId = string.IsNullOrWhiteSpace(sourceImportId) ? "-" : sourceImportId.Trim()
        };
    }

    public static SourceDetailViewModel CreateNoSelection()
    {
        return new SourceDetailViewModel("Select a source to inspect safe metadata, hash details, and warning summary.");
    }

    public static SourceDetailViewModel From(SourceImportDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new SourceDetailViewModel(detail.SourceName)
        {
            StateKind = SourceDetailStateKind.Source,
            StateMessage = "Safe source details loaded.",
            SourceImportId = detail.SourceImportId,
            SourceName = detail.SourceName,
            SourceType = detail.SourceType,
            PlatformDisplay = string.IsNullOrWhiteSpace(detail.Platform) ? "-" : detail.Platform!,
            OriginalFilename = detail.OriginalFilename,
            StoredFilePathDisplay = string.IsNullOrWhiteSpace(detail.StoredFilePath) ? "-" : detail.StoredFilePath!,
            FileSizeDisplay = FormatBytes(detail.FileSizeBytes),
            FileSha256 = detail.FileSha256,
            ImportedAtDisplay = FormatUtc(detail.ImportedAtUtc),
            ImportedByUserIdDisplay = string.IsNullOrWhiteSpace(detail.ImportedByUserId) ? "-" : detail.ImportedByUserId!,
            ImportStatus = detail.ImportStatus,
            RecordCountDisplay = detail.RecordCount.ToString("#,0", CultureInfo.InvariantCulture),
            WarningCountDisplay = detail.WarningCount.ToString("#,0", CultureInfo.InvariantCulture),
            NotesIndicator = detail.HasNotes ? "Present" : "Not present",
            SourceMetadataIndicator = detail.HasSourceMetadata ? "Present" : "Not present",
            WarningSummary = new SourceWarningSummaryViewModel(detail.WarningSummary)
        };
    }

    private SourceDetailStateKind StateKind { get; init; } = SourceDetailStateKind.Empty;

    private static string FormatBytes(long? value)
    {
        return value.HasValue
            ? $"{value.Value.ToString("#,0", CultureInfo.InvariantCulture)} bytes"
            : "-";
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    private enum SourceDetailStateKind
    {
        Empty,
        Loading,
        Error,
        Source
    }
}
