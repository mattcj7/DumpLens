using System.Collections.ObjectModel;
using System.Globalization;
using DumpLens.Application.SourceReferences;

namespace DumpLens.App.ViewModels;

public sealed class SourceReferenceInspectorViewModel : InspectorViewModelBase
{
    private const string ArtifactEmptyMessage = "No artifact reference is available for this item.";
    private const string MessageEmptyMessage = "No message reference is available for this item.";
    private const string NoSelectionMessage = "No source reference selected.";
    private const string NotAvailable = "Not available";
    private const string SourceLoadFailedMessage = "Source reference could not be loaded.";

    private SourceReferenceInspectorViewModel(string description, string stateMessage)
        : base("Source Reference Inspector", description)
    {
        StateMessage = stateMessage;
        Sections = [];
    }

    public bool HasSections => Sections.Count > 0;

    public ObservableCollection<SourceReferenceSectionViewModel> Sections { get; }

    public string StateMessage { get; }

    public static SourceReferenceInspectorViewModel CreateLoadFailure()
    {
        return new SourceReferenceInspectorViewModel(
            "Source reference details are unavailable.",
            SourceLoadFailedMessage);
    }

    public static SourceReferenceInspectorViewModel CreateActiveCaseMissing()
    {
        return new SourceReferenceInspectorViewModel(
            "Create or open a case to inspect safe source references.",
            "Create or open a case to inspect source references.");
    }

    public static SourceReferenceInspectorViewModel CreateLoading()
    {
        return new SourceReferenceInspectorViewModel(
            "Loading safe source traceability for the selected item.",
            "Loading source reference.");
    }

    public static SourceReferenceInspectorViewModel CreateNoSelection()
    {
        return new SourceReferenceInspectorViewModel(
            "Select a source-backed item to inspect safe traceability to the source import, artifact, and message.",
            NoSelectionMessage);
    }

    public static SourceReferenceInspectorViewModel From(SourceReferenceDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var viewModel = new SourceReferenceInspectorViewModel(
            "Safe traceability to the selected source import, source artifact, and message.",
            "Source reference loaded.");

        viewModel.Sections.Add(new SourceReferenceSectionViewModel(
            "Source Reference",
            [
                CreateField("Case ID", detail.CaseId),
                CreateField("Source Import ID", detail.SourceImportId),
                CreateField("Source Name", detail.SourceName),
                CreateField("Source Type", detail.SourceType),
                CreateField("Platform", FormatOptional(detail.Platform)),
                CreateField("Import Status", detail.ImportStatus),
                CreateField("Original Filename", detail.OriginalFilename),
                CreateField("Stored Relative Path", FormatOptional(detail.StoredRelativePath)),
                CreateField("File Size", FormatBytes(detail.FileSizeBytes)),
                CreateField("SHA-256", detail.FileSha256),
                CreateField("Imported At (UTC)", FormatUtc(detail.ImportedAtUtc)),
                CreateField("Source Metadata", detail.HasSourceMetadata ? "Source metadata present" : "Source metadata not present")
            ]));

        viewModel.Sections.Add(detail.ArtifactReference is null
            ? new SourceReferenceSectionViewModel("Artifact Reference", emptyMessage: ArtifactEmptyMessage)
            : new SourceReferenceSectionViewModel(
                "Artifact Reference",
                [
                    CreateField("Source Artifact ID", detail.ArtifactReference.SourceArtifactId),
                    CreateField("Artifact Type", detail.ArtifactReference.ArtifactType),
                    CreateField("Artifact Locator", FormatOptional(detail.ArtifactReference.ArtifactLocator)),
                    CreateField(
                        "Original Metadata",
                        detail.ArtifactReference.HasOriginalMetadata
                            ? "Original metadata present"
                            : "Original metadata not present")
                ]));

        if (detail.WasMessageReferenceRequested)
        {
            viewModel.Sections.Add(detail.MessageReference is null
                ? new SourceReferenceSectionViewModel("Message Reference", emptyMessage: MessageEmptyMessage)
                : new SourceReferenceSectionViewModel(
                    "Message Reference",
                    [
                        CreateField("Message ID", detail.MessageReference.MessageId),
                        CreateField("Source Artifact ID", FormatOptional(detail.MessageReference.SourceArtifactId)),
                        CreateField("Provider Message ID", FormatOptional(detail.MessageReference.ProviderMessageId)),
                        CreateField("Source Thread ID", FormatOptional(detail.MessageReference.SourceThreadId)),
                        CreateField("Event Time (UTC)", FormatUtc(detail.MessageReference.EventTimeUtc)),
                        CreateField("Deleted Status", FormatOptional(detail.MessageReference.DeletedStatus)),
                        CreateField("Message Hash Prefix", FormatOptional(detail.MessageReference.MessageHashPrefix)),
                        CreateField(
                            "Original Metadata",
                            detail.MessageReference.HasOriginalMetadata
                                ? "Original metadata present"
                                : "Original metadata not present")
                    ]));
        }

        return viewModel;
    }

    private static SourceReferenceFieldViewModel CreateField(string label, string value)
    {
        return new SourceReferenceFieldViewModel(label, value);
    }

    private static string FormatBytes(long? value)
    {
        return value.HasValue
            ? $"{value.Value.ToString("#,0", CultureInfo.InvariantCulture)} bytes"
            : NotAvailable;
    }

    private static string FormatOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? NotAvailable
            : value.Trim();
    }

    private static string FormatUtc(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : NotAvailable;
    }
}
