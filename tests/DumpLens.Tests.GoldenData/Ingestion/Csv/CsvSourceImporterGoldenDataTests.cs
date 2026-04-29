using DumpLens.Application.Imports;
using DumpLens.Ingestion.Csv;

namespace DumpLens.Tests.GoldenData.Ingestion.Csv;

public sealed class CsvSourceImporterGoldenDataTests
{
    private readonly CsvSourceImporter _importer = new();

    [Theory]
    [InlineData("messages_comma.csv", ',', true, 2, "Meet at lot C, 5pm")]
    [InlineData("calls_tab.txt", '\t', true, 2, "180")]
    [InlineData("messages_semicolon.csv", ';', true, 1, "Synthetic message")]
    [InlineData("calls_pipe.csv", '|', true, 1, "42")]
    [InlineData("messages_quoted.csv", ',', true, 1, "He said \"stand by\", then left.")]
    public async Task ProbeAsync_ParsesSyntheticGoldenFixtures(
        string fixtureName,
        char expectedDelimiter,
        bool expectedHeaderRow,
        int expectedPreviewRows,
        string expectedValue)
    {
        var fixturePath = Path.Combine(GetFixtureRoot(), fixtureName);

        var result = await _importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = fixturePath,
            PreviewRowCount = 5,
            CorrelationId = $"golden-{fixtureName}"
        });

        Assert.True(result.IsSupported);
        Assert.True(result.IsTabular);
        Assert.Equal(expectedDelimiter, result.DetectedDelimiter);
        Assert.Equal(expectedHeaderRow, result.HasHeaderRow);
        Assert.Equal(expectedPreviewRows, result.PreviewRows.Count);
        Assert.Contains(result.PreviewRows.SelectMany(static row => row.Values), value => string.Equals(value, expectedValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_PreservesExpectedMappingsForSyntheticMessageFixture()
    {
        var fixturePath = Path.Combine(GetFixtureRoot(), "messages_comma.csv");

        var result = await _importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = fixturePath,
            PreviewRowCount = 5
        });

        Assert.Equal("timestamp", GetMappedColumn(result, ImportFieldNames.Timestamp));
        Assert.Equal("sender", GetMappedColumn(result, ImportFieldNames.Sender));
        Assert.Equal("recipient", GetMappedColumn(result, ImportFieldNames.Recipient));
        Assert.Equal("message_body", GetMappedColumn(result, ImportFieldNames.MessageBody));
        Assert.Equal("platform", GetMappedColumn(result, ImportFieldNames.Platform));
        Assert.Equal("direction", GetMappedColumn(result, ImportFieldNames.Direction));
        Assert.Equal("thread_id", GetMappedColumn(result, ImportFieldNames.ThreadId));
        Assert.Equal("message_id", GetMappedColumn(result, ImportFieldNames.MessageId));
        Assert.Equal("attachment", GetMappedColumn(result, ImportFieldNames.Attachment));
    }

    [Fact]
    public async Task ProbeAsync_PreservesExpectedMappingsForSyntheticCallFixture()
    {
        var fixturePath = Path.Combine(GetFixtureRoot(), "calls_tab.txt");

        var result = await _importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = fixturePath,
            PreviewRowCount = 5
        });

        Assert.Equal("date", GetMappedColumn(result, ImportFieldNames.Timestamp));
        Assert.Equal("from_number", GetMappedColumn(result, ImportFieldNames.Caller));
        Assert.Equal("to_number", GetMappedColumn(result, ImportFieldNames.Callee));
        Assert.Equal("duration_seconds", GetMappedColumn(result, ImportFieldNames.Duration));
        Assert.Equal("call_type", GetMappedColumn(result, ImportFieldNames.CallType));
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == ImportWarningCodes.NoLikelyMessageBodyColumn);
    }

    private static string GetMappedColumn(ImportProbeResult result, string fieldName)
    {
        return Assert.Single(result.FieldMappingSuggestions, suggestion => suggestion.DumpLensFieldName == fieldName).SourceColumnName!;
    }

    private static string GetFixtureRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            var rootCandidate = Path.Combine(currentDirectory.FullName, "tests", "DumpLens.Tests.GoldenData", "Ingestion", "Csv", "Fixtures");
            if (Directory.Exists(rootCandidate))
            {
                return rootCandidate;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not locate the golden-data fixture directory.");
    }
}
