using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using ClosedXML.Excel;
using DumpLens.Application.Imports;

namespace DumpLens.Tests.Unit.Ingestion.Xlsx;

public sealed class XlsxSourceImporterTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsWorksheetNamesAndSelectsFirstNonEmptyWorksheet()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "multi-sheet.xlsx",
            workbook =>
            {
                workbook.AddWorksheet("Empty Sheet");

                var worksheet = workbook.AddWorksheet("Messages");
                worksheet.Cell("A1").Value = "timestamp";
                worksheet.Cell("B1").Value = "sender";
                worksheet.Cell("C1").Value = "recipient";
                worksheet.Cell("D1").Value = "message_body";
                worksheet.Cell("A2").Value = "2026-02-01T10:00:00Z";
                worksheet.Cell("B2").Value = "Alpha";
                worksheet.Cell("C2").Value = "Bravo";
                worksheet.Cell("D2").Value = "Synthetic workbook row";
            });

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath,
            PreviewRowCount = 5,
            CorrelationId = "xlsx-default-sheet"
        });

        Assert.True(result.IsSupported);
        Assert.Equal(["Empty Sheet", "Messages"], result.WorksheetNames);
        Assert.Equal("Messages", result.SelectedWorksheetName);
        Assert.True(result.HasHeaderRow);
        Assert.Equal("timestamp", result.Columns[0].SourceColumnName);
        Assert.Equal("Synthetic workbook row", result.PreviewRows[0].Values[3]);
    }

    [Fact]
    public async Task PreviewAsync_UsesRequestedWorksheetAndRespectsRowLimit()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "preview.xlsx",
            workbook =>
            {
                var messages = workbook.AddWorksheet("Messages");
                messages.Cell("A1").Value = "timestamp";
                messages.Cell("B1").Value = "sender";
                messages.Cell("C1").Value = "recipient";
                messages.Cell("D1").Value = "message_body";
                messages.Cell("A2").Value = "2026-02-02T09:00:00Z";
                messages.Cell("B2").Value = "Alpha";
                messages.Cell("C2").Value = "Bravo";
                messages.Cell("D2").Value = "Keep this sheet secondary";

                var calls = workbook.AddWorksheet("Calls");
                calls.Cell("A1").Value = "date";
                calls.Cell("B1").Value = "from_number";
                calls.Cell("C1").Value = "to_number";
                calls.Cell("D1").Value = "duration_seconds";
                calls.Cell("E1").Value = "call_type";
                calls.Cell("A2").Value = "2026-02-02T11:00:00Z";
                calls.Cell("B2").Value = "+15550000001";
                calls.Cell("C2").Value = "+15550000002";
                calls.Cell("D2").Value = 180;
                calls.Cell("E2").Value = "voice";
                calls.Cell("A3").Value = "2026-02-02T11:05:00Z";
                calls.Cell("B3").Value = "+15550000003";
                calls.Cell("C3").Value = "+15550000004";
                calls.Cell("D3").Value = 42;
                calls.Cell("E3").Value = "video";
            });

        var importer = CreateImporter();
        var result = await importer.PreviewAsync(new ImportPreviewRequest
        {
            FilePath = filePath,
            WorksheetName = "Calls",
            RowCount = 1,
            CorrelationId = "xlsx-preview-sheet"
        });

        Assert.True(result.IsSupported);
        Assert.Equal("Calls", result.SelectedWorksheetName);
        Assert.Single(result.Rows);
        Assert.Equal("180", result.Rows[0].Values[3]);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.PreviewTruncated);
    }

    [Fact]
    public async Task ProbeAsync_SuggestsMessageMappingsForCommonHeaders()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "message-mappings.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Messages");
                worksheet.Cell("A1").Value = "date";
                worksheet.Cell("B1").Value = "from";
                worksheet.Cell("C1").Value = "to";
                worksheet.Cell("D1").Value = "message";
                worksheet.Cell("E1").Value = "app";
                worksheet.Cell("F1").Value = "direction";
                worksheet.Cell("G1").Value = "conversation_id";
                worksheet.Cell("H1").Value = "id";
                worksheet.Cell("I1").Value = "media";
                worksheet.Cell("A2").Value = "2026-02-03T08:00:00Z";
            });

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.Equal("date", GetMappedColumn(result, ImportFieldNames.Timestamp));
        Assert.Equal("from", GetMappedColumn(result, ImportFieldNames.Sender));
        Assert.Equal("to", GetMappedColumn(result, ImportFieldNames.Recipient));
        Assert.Equal("message", GetMappedColumn(result, ImportFieldNames.MessageBody));
        Assert.Equal("app", GetMappedColumn(result, ImportFieldNames.Platform));
        Assert.Equal("direction", GetMappedColumn(result, ImportFieldNames.Direction));
        Assert.Equal("conversation_id", GetMappedColumn(result, ImportFieldNames.ThreadId));
        Assert.Equal("id", GetMappedColumn(result, ImportFieldNames.MessageId));
        Assert.Equal("media", GetMappedColumn(result, ImportFieldNames.Attachment));
    }

    [Fact]
    public async Task ProbeAsync_SuggestsCallMappingsForCommonHeaders()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "call-mappings.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Calls");
                worksheet.Cell("A1").Value = "timestamp";
                worksheet.Cell("B1").Value = "from_number";
                worksheet.Cell("C1").Value = "to_number";
                worksheet.Cell("D1").Value = "duration_seconds";
                worksheet.Cell("E1").Value = "call_type";
                worksheet.Cell("F1").Value = "direction";
                worksheet.Cell("A2").Value = "2026-02-03T09:00:00Z";
            });

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.Equal("timestamp", GetMappedColumn(result, ImportFieldNames.Timestamp));
        Assert.Equal("from_number", GetMappedColumn(result, ImportFieldNames.Caller));
        Assert.Equal("to_number", GetMappedColumn(result, ImportFieldNames.Callee));
        Assert.Equal("duration_seconds", GetMappedColumn(result, ImportFieldNames.Duration));
        Assert.Equal("call_type", GetMappedColumn(result, ImportFieldNames.CallType));
        Assert.Equal("direction", GetMappedColumn(result, ImportFieldNames.Direction));
    }

    [Fact]
    public async Task ProbeAsync_GeneratesGenericColumnsWhenHeaderRowIsMissing()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "missing-header.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Messages");
                worksheet.Cell("A1").Value = "2026-02-04T06:00:00Z";
                worksheet.Cell("B1").Value = "Alpha";
                worksheet.Cell("C1").Value = "Bravo";
                worksheet.Cell("D1").Value = "Hello";
                worksheet.Cell("A2").Value = "2026-02-04T06:01:00Z";
                worksheet.Cell("B2").Value = "Bravo";
                worksheet.Cell("C2").Value = "Alpha";
                worksheet.Cell("D2").Value = "Copy";
            });

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.HasHeaderRow);
        Assert.Equal("Column1", result.Columns[0].SourceColumnName);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.MissingHeaderRow);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsAmbiguousHeaderWarningWhenFirstRowLooksHeaderLikeButUnknown()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "ambiguous-header.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Messages");
                worksheet.Cell("A1").Value = "Alpha";
                worksheet.Cell("B1").Value = "Beta";
                worksheet.Cell("C1").Value = "Gamma";
                worksheet.Cell("D1").Value = "Delta";
                worksheet.Cell("A2").Value = "2026-02-05T11:00:00Z";
                worksheet.Cell("B2").Value = "Alpha";
                worksheet.Cell("C2").Value = "Bravo";
                worksheet.Cell("D2").Value = "Synthetic row";
            });

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.HasHeaderRow);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.AmbiguousHeaderRow);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsWarningWhenSelectedWorksheetIsNotFound()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "worksheet-not-found.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Messages");
                worksheet.Cell("A1").Value = "timestamp";
                worksheet.Cell("A2").Value = "2026-02-06T12:00:00Z";
            });

        var importer = CreateImporter();
        var result = await importer.PreviewAsync(new ImportPreviewRequest
        {
            FilePath = filePath,
            WorksheetName = "Missing Sheet"
        });

        Assert.True(result.IsSupported);
        Assert.Null(result.SelectedWorksheetName);
        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.SelectedWorksheetNotFound);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsWarningsForEmptyWorkbookAndEmptyWorksheet()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "empty-workbook.xlsx",
            workbook =>
            {
                workbook.AddWorksheet("Sheet1");
                workbook.AddWorksheet("Sheet2");
            });

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.True(result.IsSupported);
        Assert.Equal(["Sheet1", "Sheet2"], result.WorksheetNames);
        Assert.Equal("Sheet1", result.SelectedWorksheetName);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.EmptyWorkbook);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.EmptyWorksheet);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsWarningsForWorkbookWithoutWorksheets()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbookWithoutWorksheets(tempDirectory.DirectoryPath, "no-worksheets.xlsx");

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.True(result.IsSupported);
        Assert.Empty(result.WorksheetNames);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.NoWorksheets);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.EmptyWorkbook);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsWarningForUnsupportedXlsFiles()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = Path.Combine(tempDirectory.DirectoryPath, "legacy.xls");
        await File.WriteAllTextAsync(filePath, "not-a-real-xls", Encoding.UTF8);

        var importer = CreateImporter();
        var result = await importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath
        });

        Assert.False(result.IsSupported);
        Assert.Contains(result.Warnings, warning => warning.Code == ImportWarningCodes.UnsupportedFileExtension);
    }

    private static ISourceImporter CreateImporter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assemblyPath = Path.Combine(repositoryRoot, "src", "DumpLens.Ingestion", "bin", "Debug", "net9.0", "DumpLens.Ingestion.dll");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var importerType = assembly.GetType("DumpLens.Ingestion.Xlsx.XlsxSourceImporter", throwOnError: true)!;

        return (ISourceImporter)Activator.CreateInstance(importerType)!;
    }

    private static string GetMappedColumn(ImportProbeResult result, string fieldName)
    {
        return Assert.Single(result.FieldMappingSuggestions, suggestion => suggestion.DumpLensFieldName == fieldName).SourceColumnName!;
    }

    private static string CreateWorkbook(string directoryPath, string fileName, Action<XLWorkbook> configure)
    {
        var filePath = Path.Combine(directoryPath, fileName);

        using var workbook = new XLWorkbook();
        configure(workbook);
        workbook.SaveAs(filePath);

        return filePath;
    }

    private static string CreateWorkbookWithoutWorksheets(string directoryPath, string fileName)
    {
        var filePath = Path.Combine(directoryPath, fileName);

        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);

        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            </Types>
            """);
        WriteEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteEntry(
            archive,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets />
            </workbook>
            """);

        return filePath;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
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
