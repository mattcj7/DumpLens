using System.Text.Json;
using System.Text.Encodings.Web;
using ClosedXML.Excel;
using DumpLens.Application.Imports;
using DumpLens.Ingestion.Xlsx;

namespace DumpLens.Tests.GoldenData.Ingestion.Xlsx;

public sealed class XlsxSourceImporterGoldenDataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly XlsxSourceImporter _importer = new();

    [Fact]
    public async Task ProbeAsync_MatchesSyntheticMessageWorkbookSnapshot()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "messages.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Messages");
                worksheet.Cell("A1").Value = "timestamp";
                worksheet.Cell("B1").Value = "sender";
                worksheet.Cell("C1").Value = "recipient";
                worksheet.Cell("D1").Value = "message_body";
                worksheet.Cell("E1").Value = "platform";
                worksheet.Cell("F1").Value = "direction";
                worksheet.Cell("G1").Value = "thread_id";
                worksheet.Cell("H1").Value = "message_id";
                worksheet.Cell("I1").Value = "attachment";
                worksheet.Cell("A2").Value = "2026-03-01T10:00:00Z";
                worksheet.Cell("B2").Value = "Alpha";
                worksheet.Cell("C2").Value = "Bravo";
                worksheet.Cell("D2").Value = "Meet at lot C";
                worksheet.Cell("E2").Value = "sms";
                worksheet.Cell("F2").Value = "outgoing";
                worksheet.Cell("G2").Value = "thread-1";
                worksheet.Cell("H2").Value = "msg-1";
                worksheet.Cell("I2").Value = "photo1.jpg";
            });

        var result = await _importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath,
            PreviewRowCount = 5,
            CorrelationId = "golden-xlsx-message"
        });

        Assert.Equal(
            NormalizeLineEndings(
            """
            {
              "WorksheetNames": [
                "Messages"
              ],
              "SelectedWorksheetName": "Messages",
              "HasHeaderRow": true,
              "Columns": [
                "timestamp",
                "sender",
                "recipient",
                "message_body",
                "platform",
                "direction",
                "thread_id",
                "message_id",
                "attachment"
              ],
              "PreviewRows": [
                {
                  "RowNumber": 2,
                  "Values": [
                    "2026-03-01T10:00:00Z",
                    "Alpha",
                    "Bravo",
                    "Meet at lot C",
                    "sms",
                    "outgoing",
                    "thread-1",
                    "msg-1",
                    "photo1.jpg"
                  ]
                }
              ],
              "Mappings": [
                {
                  "Field": "attachment",
                  "SourceColumn": "attachment",
                  "Candidates": [
                    "attachment"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "direction",
                  "SourceColumn": "direction",
                  "Candidates": [
                    "direction"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "message_body",
                  "SourceColumn": "message_body",
                  "Candidates": [
                    "message_body"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "message_id",
                  "SourceColumn": "message_id",
                  "Candidates": [
                    "message_id"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "platform",
                  "SourceColumn": "platform",
                  "Candidates": [
                    "platform"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "recipient",
                  "SourceColumn": "recipient",
                  "Candidates": [
                    "recipient"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "sender",
                  "SourceColumn": "sender",
                  "Candidates": [
                    "sender"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "thread_id",
                  "SourceColumn": "thread_id",
                  "Candidates": [
                    "thread_id"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "timestamp",
                  "SourceColumn": "timestamp",
                  "Candidates": [
                    "timestamp"
                  ],
                  "IsAmbiguous": false
                }
              ],
              "WarningCodes": []
            }
            """),
            NormalizeLineEndings(CreateProbeSnapshot(result)));
    }

    [Fact]
    public async Task ProbeAsync_MatchesSyntheticCallWorkbookSnapshot()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "calls.xlsx",
            workbook =>
            {
                var worksheet = workbook.AddWorksheet("Calls");
                worksheet.Cell("A1").Value = "date";
                worksheet.Cell("B1").Value = "from_number";
                worksheet.Cell("C1").Value = "to_number";
                worksheet.Cell("D1").Value = "duration_seconds";
                worksheet.Cell("E1").Value = "call_type";
                worksheet.Cell("F1").Value = "direction";
                worksheet.Cell("A2").Value = "2026-03-02T09:00:00Z";
                worksheet.Cell("B2").Value = "+15550000001";
                worksheet.Cell("C2").Value = "+15550000002";
                worksheet.Cell("D2").Value = 180;
                worksheet.Cell("E2").Value = "voice";
                worksheet.Cell("F2").Value = "outgoing";
            });

        var result = await _importer.ProbeAsync(new ImportProbeRequest
        {
            FilePath = filePath,
            PreviewRowCount = 5,
            CorrelationId = "golden-xlsx-call"
        });

        Assert.Equal(
            NormalizeLineEndings(
            """
            {
              "WorksheetNames": [
                "Calls"
              ],
              "SelectedWorksheetName": "Calls",
              "HasHeaderRow": true,
              "Columns": [
                "date",
                "from_number",
                "to_number",
                "duration_seconds",
                "call_type",
                "direction"
              ],
              "PreviewRows": [
                {
                  "RowNumber": 2,
                  "Values": [
                    "2026-03-02T09:00:00Z",
                    "+15550000001",
                    "+15550000002",
                    "180",
                    "voice",
                    "outgoing"
                  ]
                }
              ],
              "Mappings": [
                {
                  "Field": "call_type",
                  "SourceColumn": "call_type",
                  "Candidates": [
                    "call_type"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "callee",
                  "SourceColumn": "to_number",
                  "Candidates": [
                    "to_number"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "caller",
                  "SourceColumn": "from_number",
                  "Candidates": [
                    "from_number"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "direction",
                  "SourceColumn": "direction",
                  "Candidates": [
                    "direction"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "duration",
                  "SourceColumn": "duration_seconds",
                  "Candidates": [
                    "duration_seconds"
                  ],
                  "IsAmbiguous": false
                },
                {
                  "Field": "timestamp",
                  "SourceColumn": "date",
                  "Candidates": [
                    "date"
                  ],
                  "IsAmbiguous": false
                }
              ],
              "WarningCodes": []
            }
            """),
            NormalizeLineEndings(CreateProbeSnapshot(result)));
    }

    [Fact]
    public async Task PreviewAsync_MatchesSyntheticSelectedWorksheetSnapshot()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var filePath = CreateWorkbook(
            tempDirectory.DirectoryPath,
            "preview.xlsx",
            workbook =>
            {
                var blank = workbook.AddWorksheet("Blank");
                blank.Cell("A10").Clear();

                var worksheet = workbook.AddWorksheet("Calls");
                worksheet.Cell("A1").Value = "timestamp";
                worksheet.Cell("B1").Value = "caller";
                worksheet.Cell("C1").Value = "callee";
                worksheet.Cell("D1").Value = "duration";
                worksheet.Cell("E1").Value = "type";
                worksheet.Cell("A2").Value = "2026-03-03T12:00:00Z";
                worksheet.Cell("B2").Value = "+15550000003";
                worksheet.Cell("C2").Value = "+15550000004";
                worksheet.Cell("D2").Value = 42;
                worksheet.Cell("E2").Value = "voice";
                worksheet.Cell("A3").Value = "2026-03-03T12:05:00Z";
                worksheet.Cell("B3").Value = "+15550000005";
                worksheet.Cell("C3").Value = "+15550000006";
                worksheet.Cell("D3").Value = 65;
                worksheet.Cell("E3").Value = "video";
            });

        var result = await _importer.PreviewAsync(new ImportPreviewRequest
        {
            FilePath = filePath,
            WorksheetName = "Calls",
            RowCount = 1,
            CorrelationId = "golden-xlsx-preview"
        });

        Assert.Equal(
            NormalizeLineEndings(
            """
            {
              "WorksheetNames": [
                "Blank",
                "Calls"
              ],
              "SelectedWorksheetName": "Calls",
              "HasHeaderRow": true,
              "Columns": [
                "timestamp",
                "caller",
                "callee",
                "duration",
                "type"
              ],
              "Rows": [
                {
                  "RowNumber": 2,
                  "Values": [
                    "2026-03-03T12:00:00Z",
                    "+15550000003",
                    "+15550000004",
                    "42",
                    "voice"
                  ]
                }
              ],
              "WarningCodes": [
                "preview_truncated"
              ]
            }
            """),
            NormalizeLineEndings(CreatePreviewSnapshot(result)));
    }

    private static string CreateProbeSnapshot(ImportProbeResult result)
    {
        return JsonSerializer.Serialize(
            new
            {
                result.WorksheetNames,
                result.SelectedWorksheetName,
                result.HasHeaderRow,
                Columns = result.Columns.Select(static column => column.SourceColumnName).ToArray(),
                PreviewRows = result.PreviewRows.Select(row => new
                {
                    row.RowNumber,
                    Values = row.Values.ToArray()
                }).ToArray(),
                Mappings = result.FieldMappingSuggestions
                    .Where(static suggestion => !string.IsNullOrWhiteSpace(suggestion.SourceColumnName))
                    .OrderBy(static suggestion => suggestion.DumpLensFieldName, StringComparer.Ordinal)
                    .Select(suggestion => new
                    {
                        Field = suggestion.DumpLensFieldName,
                        SourceColumn = suggestion.SourceColumnName,
                        Candidates = suggestion.CandidateSourceColumnNames.ToArray(),
                        suggestion.IsAmbiguous
                    })
                    .ToArray(),
                WarningCodes = result.Warnings.Select(static warning => warning.Code).ToArray()
            },
            JsonOptions);
    }

    private static string CreatePreviewSnapshot(ImportPreviewResult result)
    {
        return JsonSerializer.Serialize(
            new
            {
                result.WorksheetNames,
                result.SelectedWorksheetName,
                result.HasHeaderRow,
                Columns = result.Columns.Select(static column => column.SourceColumnName).ToArray(),
                Rows = result.Rows.Select(row => new
                {
                    row.RowNumber,
                    Values = row.Values.ToArray()
                }).ToArray(),
                WarningCodes = result.Warnings.Select(static warning => warning.Code).ToArray()
            },
            JsonOptions);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string CreateWorkbook(string directoryPath, string fileName, Action<XLWorkbook> configure)
    {
        var filePath = Path.Combine(directoryPath, fileName);

        using var workbook = new XLWorkbook();
        configure(workbook);
        workbook.SaveAs(filePath);

        return filePath;
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
