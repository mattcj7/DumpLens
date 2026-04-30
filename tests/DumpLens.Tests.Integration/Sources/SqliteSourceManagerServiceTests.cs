using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DumpLens.Application.Cases;
using DumpLens.Application.FileHashing;
using DumpLens.Application.Sources;
using DumpLens.Persistence.Cases;
using DumpLens.Persistence.Sources;
using DumpLens.Tests.Integration.CasePackages;
using Microsoft.Data.Sqlite;

namespace DumpLens.Tests.Integration.Sources;

public sealed class SqliteSourceManagerServiceTests
{
    [Fact]
    public async Task GetSummariesAsync_Returns_Ordered_Source_Summaries_With_Safe_Metadata()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);

        var beta = await RegisterSourceAsync(caseResult, tempDirectory.DirectoryPath, "beta.csv", "Beta Source", "beta row");
        var alpha = await RegisterSourceAsync(caseResult, tempDirectory.DirectoryPath, "alpha.csv", "Alpha Source", "alpha row");
        var latest = await RegisterSourceAsync(caseResult, tempDirectory.DirectoryPath, "latest.csv", "Latest Source", "latest row");

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        await UpdateSourceImportAsync(connection, beta.SourceImportId, "Beta Source", "2026-04-01T10:00:00Z", 3, 1);
        await UpdateSourceImportAsync(connection, alpha.SourceImportId, "Alpha Source", "2026-04-01T10:00:00Z", 4, 0);
        await UpdateSourceImportAsync(connection, latest.SourceImportId, "Latest Source", "2026-04-02T10:00:00Z", 9, 2);

        var service = new SqliteSourceManagerService();
        var summaries = await service.GetSummariesAsync(new LoadSourceImportSummariesRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath
        });

        Assert.Equal(3, summaries.Count);
        Assert.Equal(["Latest Source", "Alpha Source", "Beta Source"], summaries.Select(static item => item.SourceName).ToArray());
        Assert.Equal(9, summaries[0].RecordCount);
        Assert.Equal(2, summaries[0].WarningCount);
        Assert.Equal("latest.csv", summaries[0].OriginalFilename);
        Assert.NotNull(summaries[0].FileSizeBytes);
        Assert.Equal(64, summaries[0].FileSha256.Length);
        Assert.Equal(latest.SourceImportId, summaries[0].SourceImportId);
    }

    [Fact]
    public async Task GetDetailAsync_Returns_Safe_Detail_And_Grouped_Warning_Codes()
    {
        using var tempDirectory = TemporaryDirectoryScope.Create();
        var caseResult = await CreateCaseAsync(tempDirectory.DirectoryPath);
        var registration = await RegisterSourceAsync(
            caseResult,
            tempDirectory.DirectoryPath,
            "warnings.csv",
            "Warnings Source",
            "warning row",
            importedByUserId: null,
            notes: "sensitive notes should not be exposed",
            sourceMetadataJson: "{\"sensitive\":\"metadata\"}");

        await using var connection = new SqliteConnection(BuildConnectionString(caseResult.DatabasePath));
        await connection.OpenAsync();

        await InsertAppUserAsync(connection, "user-src-001");
        await UpdateSourceImportDetailAsync(connection, registration.SourceImportId, "user-src-001", 12, 4);
        await InsertWarningAsync(connection, caseResult.CaseId, registration.SourceImportId, "missing_timestamp", "row contains TOP_SECRET");
        await InsertWarningAsync(connection, caseResult.CaseId, registration.SourceImportId, "missing_timestamp", "row contains TOP_SECRET");
        await InsertWarningAsync(connection, caseResult.CaseId, registration.SourceImportId, "duplicate_row", "row contains TOP_SECRET");
        await InsertWarningAsync(connection, caseResult.CaseId, registration.SourceImportId, "unknown_timezone", "row contains TOP_SECRET");

        var service = new SqliteSourceManagerService();
        var detail = await service.GetDetailAsync(new LoadSourceImportDetailRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SourceImportId = registration.SourceImportId
        });

        Assert.NotNull(detail);
        Assert.Equal(registration.SourceImportId, detail.SourceImportId);
        Assert.Equal("Warnings Source", detail.SourceName);
        Assert.Equal("warnings.csv", detail.OriginalFilename);
        Assert.Equal(registration.FileSha256, detail.FileSha256);
        Assert.Equal(12, detail.RecordCount);
        Assert.Equal(4, detail.WarningCount);
        Assert.Equal("user-src-001", detail.ImportedByUserId);
        Assert.True(detail.HasNotes);
        Assert.True(detail.HasSourceMetadata);
        Assert.False(Path.IsPathRooted(detail.StoredFilePath!));
        Assert.StartsWith("imports/source_", detail.StoredFilePath!, StringComparison.Ordinal);

        Assert.Equal(4, detail.WarningSummary.TotalWarnings);
        Assert.Equal(
            ["missing_timestamp", "duplicate_row", "unknown_timezone"],
            detail.WarningSummary.WarningCodeCounts.Select(static item => item.WarningCode).ToArray());
        Assert.Equal([2, 1, 1], detail.WarningSummary.WarningCodeCounts.Select(static item => item.Count).ToArray());
    }

    private static async Task<CreateCaseResult> CreateCaseAsync(string parentDirectoryPath)
    {
        var caseService = new SqliteCaseService();
        return await caseService.CreateAsync(new CreateCaseRequest
        {
            CaseNumber = "DL-SRC-MGR-001",
            Title = "Synthetic Source Manager Case",
            ParentDirectoryPath = parentDirectoryPath,
            CorrelationId = "source-manager-case-create"
        });
    }

    private static async Task<RegisterSourceResult> RegisterSourceAsync(
        CreateCaseResult caseResult,
        string tempDirectoryPath,
        string fileName,
        string sourceName,
        string sourceContent,
        string? importedByUserId = null,
        string? notes = null,
        string? sourceMetadataJson = null)
    {
        var sourceFilePath = Path.Combine(tempDirectoryPath, fileName);
        await File.WriteAllTextAsync(
            sourceFilePath,
            $"timestamp,message_body{Environment.NewLine}2026-04-01T12:00:00Z,{sourceContent}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var service = new SqliteSourceRegistrationService(new DeterministicSha256FileHashService(), new SqliteSourceImportRepository());
        return await service.RegisterAsync(new RegisterSourceRequest
        {
            CaseId = caseResult.CaseId,
            CaseDatabasePath = caseResult.DatabasePath,
            CasePackageRootPath = caseResult.PackageRootPath,
            SelectedSourceFilePath = sourceFilePath,
            SourceName = sourceName,
            SourceType = "csv_messages",
            Platform = "sms",
            ImportedByUserId = importedByUserId,
            Notes = notes,
            SourceMetadataJson = sourceMetadataJson,
            CorrelationId = $"register-{fileName}"
        });
    }

    private static string BuildConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    private static async Task UpdateSourceImportAsync(
        SqliteConnection connection,
        string sourceImportId,
        string sourceName,
        string importedAtUtc,
        int recordCount,
        int warningCount)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE source_imports
            SET source_name = $sourceName,
                imported_at_utc = $importedAtUtc,
                record_count = $recordCount,
                warning_count = $warningCount,
                import_status = 'imported',
                updated_at_utc = $importedAtUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sourceImportId);
        command.Parameters.AddWithValue("$sourceName", sourceName);
        command.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
        command.Parameters.AddWithValue("$recordCount", recordCount);
        command.Parameters.AddWithValue("$warningCount", warningCount);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateSourceImportDetailAsync(
        SqliteConnection connection,
        string sourceImportId,
        string importedByUserId,
        int recordCount,
        int warningCount)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE source_imports
            SET imported_by_user_id = $importedByUserId,
                record_count = $recordCount,
                warning_count = $warningCount,
                import_status = 'imported',
                updated_at_utc = '2026-04-03T10:00:00Z'
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sourceImportId);
        command.Parameters.AddWithValue("$importedByUserId", importedByUserId);
        command.Parameters.AddWithValue("$recordCount", recordCount);
        command.Parameters.AddWithValue("$warningCount", warningCount);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAppUserAsync(SqliteConnection connection, string userId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_users (
                id,
                display_name,
                username,
                role,
                is_active,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                'Synthetic User',
                'synthetic-user',
                'investigator',
                1,
                '2026-04-01T00:00:00Z',
                '2026-04-01T00:00:00Z'
            );
            """;
        command.Parameters.AddWithValue("$id", userId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertWarningAsync(
        SqliteConnection connection,
        string caseId,
        string sourceImportId,
        string warningCode,
        string message)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO import_warnings (
                id,
                case_id,
                source_import_id,
                severity,
                warning_code,
                message,
                raw_value,
                created_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $sourceImportId,
                'warning',
                $warningCode,
                $message,
                'TOP_SECRET_VALUE',
                '2026-04-03T12:00:00Z'
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$caseId", caseId);
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);
        command.Parameters.AddWithValue("$warningCode", warningCode);
        command.Parameters.AddWithValue("$message", message);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class DeterministicSha256FileHashService : IFileHashService
    {
        public async Task<FileHashResult> ComputeHashAsync(
            FileHashRequest request,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(request.FilePath);
            await using var stream = new FileStream(
                fullPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            var startedAtUtc = DateTimeOffset.UtcNow;
            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var completedAtUtc = DateTimeOffset.UtcNow;

            return new FileHashResult
            {
                CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                    ? Guid.NewGuid().ToString("N")
                    : request.CorrelationId.Trim(),
                FilePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                Algorithm = FileHashAlgorithm.Sha256,
                HexDigest = Convert.ToHexString(hashBytes).ToLowerInvariant(),
                FileSizeBytes = stream.Length,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                Duration = completedAtUtc - startedAtUtc
            };
        }

        public async Task<string> WriteSha256FileAsync(
            FileHashResult result,
            string targetFolderPath,
            string outputFileName = "sha256.txt",
            CancellationToken cancellationToken = default)
        {
            var outputPath = Path.Combine(targetFolderPath, outputFileName);
            var content = string.Join(
                "\n",
                [
                    "algorithm: SHA-256",
                    $"file_name: {result.FileName}",
                    $"file_size_bytes: {result.FileSizeBytes.ToString(CultureInfo.InvariantCulture)}",
                    $"sha256: {result.HexDigest}",
                    string.Empty
                ]);

            await File.WriteAllTextAsync(
                outputPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            return outputPath;
        }
    }
}
