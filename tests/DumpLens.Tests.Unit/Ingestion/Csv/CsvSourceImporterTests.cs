using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using DumpLens.Application.Imports;

namespace DumpLens.Tests.Unit.Ingestion.Csv;

public sealed class CsvSourceImporterTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task ProbeAsync_DetectsCommaDelimitedMessageExportsAndSuggestsMappings()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "messages.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            timestamp,sender,recipient,message_body,platform,direction,thread_id,message_id,attachment
            2026-01-01T12:00:00Z,Alice,Bob,"Meet at lot C, 5pm",sms,outgoing,thread-1,msg-1,photo1.jpg
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath,
            PreviewRowCount = 5,
            CorrelationId = "comma-message"
        });

        Assert.True(result.IsSupported);
        Assert.True(result.IsTabular);
        Assert.Equal(',', result.DetectedDelimiter);
        Assert.True(result.HasHeaderRow);
        Assert.Equal("timestamp", result.Columns[0].SourceColumnName);
        Assert.Equal("Meet at lot C, 5pm", result.PreviewRows[0].Values[3]);
        Assert.Equal("sender", GetMappedColumn(result, ImportFieldNames.Sender));
        Assert.Equal("recipient", GetMappedColumn(result, ImportFieldNames.Recipient));
        Assert.Equal("message_body", GetMappedColumn(result, ImportFieldNames.MessageBody));
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == ImportWarningCodes.MissingHeaderRow);
    }

    [Fact]
    public async Task ProbeAsync_DetectsTabDelimitedTxtCallLogs()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "calls.txt");
        await File.WriteAllTextAsync(
            filePath,
            "date\tfrom_number\tto_number\tduration_seconds\tcall_type\tdirection\n2026-01-02T09:00:00Z\t+15550000001\t+15550000002\t180\tvoice\toutgoing\n",
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath,
            PreviewRowCount = 5
        });

        Assert.True(result.IsSupported);
        Assert.Equal('\t', result.DetectedDelimiter);
        Assert.Equal("date", GetMappedColumn(result, ImportFieldNames.Timestamp));
        Assert.Equal("from_number", GetMappedColumn(result, ImportFieldNames.Caller));
        Assert.Equal("to_number", GetMappedColumn(result, ImportFieldNames.Callee));
        Assert.Equal("duration_seconds", GetMappedColumn(result, ImportFieldNames.Duration));
        Assert.Equal("call_type", GetMappedColumn(result, ImportFieldNames.CallType));
        Assert.DoesNotContain(result.Warnings, warning => warning.Code == ImportWarningCodes.NoLikelyMessageBodyColumn);
    }

    [Fact]
    public async Task ProbeAsync_DetectsSemicolonDelimitedFiles()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "messages-semicolon.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            created_at;author;destination;content;source_app
            2026-01-03T10:30:00Z;Casey;Jordan;Synthetic message;telegram
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.True(result.IsSupported);
        Assert.Equal(';', result.DetectedDelimiter);
        Assert.Equal("created_at", GetMappedColumn(result, ImportFieldNames.Timestamp));
        Assert.Equal("author", GetMappedColumn(result, ImportFieldNames.Sender));
        Assert.Equal("destination", GetMappedColumn(result, ImportFieldNames.Recipient));
        Assert.Equal("content", GetMappedColumn(result, ImportFieldNames.MessageBody));
    }

    [Fact]
    public async Task ProbeAsync_DetectsPipeDelimitedFiles()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "calls-pipe.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            timestamp|caller|callee|duration|type
            2026-01-04T08:00:00Z|+15550000003|+15550000004|42|voice
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.True(result.IsSupported);
        Assert.Equal('|', result.DetectedDelimiter);
        Assert.Equal("caller", GetMappedColumn(result, ImportFieldNames.Caller));
        Assert.Equal("callee", GetMappedColumn(result, ImportFieldNames.Callee));
        Assert.Equal("duration", GetMappedColumn(result, ImportFieldNames.Duration));
        Assert.Equal("type", GetMappedColumn(result, ImportFieldNames.CallType));
    }

    [Fact]
    public async Task ProbeAsync_HandlesQuotedFieldsWithEmbeddedCommasAndEscapedQuotes()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "quoted.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            timestamp,sender,recipient,message_body
            2026-01-05T07:15:00Z,Alice,Bob,"He said ""stand by"", then left."
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.True(result.IsSupported);
        Assert.Equal("He said \"stand by\", then left.", result.PreviewRows[0].Values[3]);
    }

    [Fact]
    public async Task ProbeAsync_GeneratesGenericColumnsWhenHeaderRowIsMissing()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "missing-header.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            2026-01-06T06:00:00Z,Alice,Bob,Hello
            2026-01-06T06:01:00Z,Bob,Alice,Copy that
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.True(result.IsSupported);
        Assert.False(result.HasHeaderRow);
        Assert.Equal("Column1", result.Columns[0].SourceColumnName);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.MissingHeaderRow);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsAmbiguousHeaderWarningWhenFirstRowLooksHeaderLikeButUnknown()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "ambiguous-header.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            Alpha,Beta,Gamma,Delta
            2026-01-07T11:00:00Z,Alice,Bob,Hello
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.HasHeaderRow);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.AmbiguousHeaderRow);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsWarningForInconsistentRowWidths()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "inconsistent.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            timestamp,sender,recipient
            2026-01-08T12:00:00Z,Alice,Bob
            2026-01-08T12:01:00Z,Bob,Alice,Unexpected
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.InconsistentRowWidth);
        Assert.Equal("Column4", result.Columns[3].SourceColumnName);
        Assert.Equal("Unexpected", result.PreviewRows[1].Values[3]);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsWarningForEmptyFiles()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "empty.csv");
        await File.WriteAllTextAsync(filePath, string.Empty, Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.IsSupported);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.EmptyFile);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsWarningForUnsupportedExtensions()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "export.json");
        await File.WriteAllTextAsync(filePath, "{\"rows\":[]}", Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.IsSupported);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.UnsupportedFileExtension);
    }

    [Fact]
    public async Task PreviewAsync_RespectsRequestedRowCountAndWarnsWhenTruncated()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "preview.csv");
        await File.WriteAllTextAsync(
            filePath,
            """
            timestamp,sender,recipient,message_body
            2026-01-09T08:00:00Z,Alice,Bob,One
            2026-01-09T08:01:00Z,Bob,Alice,Two
            2026-01-09T08:02:00Z,Alice,Bob,Three
            """,
            Utf8NoBom);

        var importer = CreateImporter();
        var result = await importer.PreviewAsync(new ImportPreviewRequest
        {
            FilePath = filePath,
            RowCount = 2
        });

        Assert.True(result.IsSupported);
        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.PreviewTruncated);
        Assert.Equal("One", result.Rows[0].Values[3]);
        Assert.Equal("Two", result.Rows[1].Values[3]);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsUnsupportedEncodingWarningForInvalidUtf8()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "invalid-encoding.csv");
        await File.WriteAllBytesAsync(filePath, [0x74, 0x69, 0x6D, 0x65, 0x73, 0x74, 0x61, 0x6D, 0x70, 0x2C, 0xFF, 0xFF]);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.IsSupported);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.UnsupportedEncoding);
    }

    private static ISourceImporter CreateImporter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assemblyPath = Path.Combine(repositoryRoot, "src", "DumpLens.Ingestion", "bin", "Debug", "net9.0", "DumpLens.Ingestion.dll");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var importerType = assembly.GetType("DumpLens.Ingestion.Csv.CsvSourceImporter", throwOnError: true)!;

        return (ISourceImporter)Activator.CreateInstance(importerType, [null, null])!;
    }

    private static string GetMappedColumn(ImportProbeResult result, string fieldName)
    {
        return Assert.Single(result.FieldMappingSuggestions, suggestion => suggestion.DumpLensFieldName == fieldName).SourceColumnName!;
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

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        private TemporaryDirectoryScope(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TemporaryDirectoryScope Create()
        {
            var directoryPath = Path.Combine(Path.GetTempPath(), "DumpLens.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);
            return new TemporaryDirectoryScope(directoryPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
