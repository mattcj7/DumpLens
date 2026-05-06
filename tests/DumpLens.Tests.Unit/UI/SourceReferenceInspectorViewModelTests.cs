using System.Collections;
using System.Reflection;
using DumpLens.Application.SourceReferences;

namespace DumpLens.Tests.Unit.UI;

public sealed class SourceReferenceInspectorViewModelTests
{
    [Fact]
    public void CreateNoSelection_Returns_Required_Empty_State()
    {
        var inspector = InvokeStaticFactory("CreateNoSelection");

        Assert.Equal("No source reference selected.", GetStringProperty(inspector, "StateMessage"));
        Assert.False(GetBooleanProperty(inspector, "HasSections"));
    }

    [Fact]
    public void CreateLoadFailure_Returns_Required_Failure_State()
    {
        var inspector = InvokeStaticFactory("CreateLoadFailure");

        Assert.Equal("Source reference could not be loaded.", GetStringProperty(inspector, "StateMessage"));
        Assert.False(GetBooleanProperty(inspector, "HasSections"));
    }

    [Fact]
    public void From_Builds_Source_Artifact_And_Message_Sections()
    {
        var inspector = InvokeFrom(CreateDetail(includeArtifact: true, includeMessage: true));

        Assert.True(GetBooleanProperty(inspector, "HasSections"));
        Assert.Equal("case-ui-001", GetFieldValue(inspector, "Source Reference", "Case ID"));
        Assert.Equal("src-ui-001", GetFieldValue(inspector, "Source Reference", "Source Import ID"));
        Assert.Equal("art-ui-001", GetFieldValue(inspector, "Artifact Reference", "Source Artifact ID"));
        Assert.Equal("row:2", GetFieldValue(inspector, "Artifact Reference", "Artifact Locator"));
        Assert.Equal("msg-ui-001", GetFieldValue(inspector, "Message Reference", "Message ID"));
        Assert.Equal("provider-ui-001", GetFieldValue(inspector, "Message Reference", "Provider Message ID"));
        Assert.Equal("thread-ui-001", GetFieldValue(inspector, "Message Reference", "Source Thread ID"));
        Assert.Equal("abcdef123456", GetFieldValue(inspector, "Message Reference", "Message Hash Prefix"));
    }

    [Fact]
    public void From_Uses_Safe_Artifact_Empty_State_When_Artifact_Is_Missing()
    {
        var inspector = InvokeFrom(CreateDetail(includeArtifact: false, includeMessage: false));

        var sections = GetCollection(inspector, "Sections");
        var artifactSection = sections.Single(item => string.Equals(GetStringProperty(item, "Title"), "Artifact Reference", StringComparison.Ordinal));
        Assert.Equal("No artifact reference is available for this item.", GetStringProperty(artifactSection, "EmptyMessage"));
    }

    private static SourceReferenceDetail CreateDetail(bool includeArtifact, bool includeMessage)
    {
        return new SourceReferenceDetail
        {
            CaseId = "case-ui-001",
            SourceImportId = "src-ui-001",
            SourceName = "Synthetic UI Source",
            SourceType = "csv_messages",
            Platform = "sms",
            ImportStatus = "imported",
            OriginalFilename = "src-ui-001.csv",
            StoredRelativePath = "imports/source_src-ui-001/original/src-ui-001.csv",
            FileSizeBytes = 2048,
            FileSha256 = "abcdef123456abcdef123456abcdef123456abcdef123456abcdef123456abcd",
            ImportedAtUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z"),
            HasSourceMetadata = true,
            WasArtifactReferenceRequested = true,
            WasMessageReferenceRequested = includeMessage,
            ArtifactReference = includeArtifact
                ? new SourceArtifactReferenceDetail
                {
                    SourceArtifactId = "art-ui-001",
                    ArtifactType = "message_row",
                    ArtifactLocator = "row:2",
                    HasOriginalMetadata = true
                }
                : null,
            MessageReference = includeMessage
                ? new MessageSourceReferenceDetail
                {
                    MessageId = "msg-ui-001",
                    SourceArtifactId = includeArtifact ? "art-ui-001" : null,
                    ProviderMessageId = "provider-ui-001",
                    SourceThreadId = "thread-ui-001",
                    EventTimeUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z"),
                    DeletedStatus = "present",
                    MessageHashPrefix = "abcdef123456",
                    HasOriginalMetadata = true
                }
                : null
        };
    }

    private static object InvokeStaticFactory(string methodName)
    {
        var type = LoadInspectorType();
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var value = method!.Invoke(null, null);
        Assert.NotNull(value);
        return value!;
    }

    private static object InvokeFrom(SourceReferenceDetail detail)
    {
        var type = LoadInspectorType();
        var method = type.GetMethod("From", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var value = method!.Invoke(null, [detail]);
        Assert.NotNull(value);
        return value!;
    }

    private static Type LoadInspectorType()
    {
        var assembly = ViewModelAssemblyLoader.Load();
        return assembly.GetType("DumpLens.App.ViewModels.SourceReferenceInspectorViewModel", throwOnError: true)!;
    }

    private static List<object> GetCollection(object instance, string propertyName)
    {
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(GetPropertyValue(instance, propertyName));
        return enumerable.Cast<object>().ToList();
    }

    private static bool GetBooleanProperty(object instance, string propertyName)
    {
        return Assert.IsType<bool>(GetPropertyValue(instance, propertyName));
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
        return Assert.IsType<string>(GetPropertyValue(instance, propertyName));
    }

    private static string GetFieldValue(object inspector, string sectionTitle, string fieldLabel)
    {
        var sections = GetCollection(inspector, "Sections");
        var section = sections.Single(item => string.Equals(GetStringProperty(item, "Title"), sectionTitle, StringComparison.Ordinal));
        var fields = GetCollection(section, "Fields");
        var field = fields.Single(item => string.Equals(GetStringProperty(item, "Label"), fieldLabel, StringComparison.Ordinal));
        return GetStringProperty(field, "Value");
    }
}
