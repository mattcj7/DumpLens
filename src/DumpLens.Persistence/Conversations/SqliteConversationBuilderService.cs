using System.Globalization;
using System.Text.Json;
using DumpLens.Application.Conversations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpLens.Persistence.Conversations;

public sealed class SqliteConversationBuilderService : IConversationBuilderService
{
    private const string ConversationParticipantRole = "participant";
    private const string DefaultReconciliationStatus = "not_started";
    private const string DefaultReviewStatus = "unreviewed";
    private const string GroupKindParticipantSet = ConversationGroupingRules.ParticipantGroupKind;
    private const string GroupKindSourceThread = ConversationGroupingRules.ThreadGroupKind;
    private const string OperationName = "conversation_build";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly ILogger<SqliteConversationBuilderService> _logger;

    public SqliteConversationBuilderService(ILogger<SqliteConversationBuilderService>? logger = null)
    {
        _logger = logger ?? NullLogger<SqliteConversationBuilderService>.Instance;
    }

    public async Task<BuildConversationsResult> BuildAsync(
        BuildConversationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = ValidateAndNormalize(request);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var failureStage = "validation";

        _logger.LogInformation(
            "Conversation build started. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} source_scope_present={SourceScopePresent} rebuild_existing={RebuildExisting}",
            OperationName,
            normalizedRequest.CorrelationId,
            normalizedRequest.CaseId,
            normalizedRequest.SourceImportId,
            normalizedRequest.SourceImportId is not null,
            normalizedRequest.RebuildExisting);

        try
        {
            var connectionString = BuildConnectionString(normalizedRequest.CaseDatabasePath);
            var conversationCountCreated = 0;
            var conversationCountUpdated = 0;
            var participantCountCreated = 0;
            var messageCountAssigned = 0;
            var unassignedMessageCount = 0;
            var participantCountRemoved = 0;
            var conversationSummaries = new List<ConversationBuildSummary>();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            failureStage = "case_validation";
            await EnsureCaseExistsAsync(connection, transaction, normalizedRequest.CaseId, cancellationToken).ConfigureAwait(false);

            if (normalizedRequest.SourceImportId is not null)
            {
                failureStage = "source_scope_validation";
                await EnsureSourceImportExistsAsync(
                        connection,
                        transaction,
                        normalizedRequest.CaseId,
                        normalizedRequest.SourceImportId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            failureStage = "existing_conversations_load";
            var existingConversations = await LoadExistingConversationsAsync(
                    connection,
                    transaction,
                    normalizedRequest.CaseId,
                    cancellationToken)
                .ConfigureAwait(false);
            var threadConversationMap = BuildThreadConversationMap(existingConversations);
            var participantConversationMap = BuildParticipantConversationMap(existingConversations);

            failureStage = "candidate_messages_load";
            var candidateMessages = await LoadCandidateMessagesAsync(
                    connection,
                    transaction,
                    normalizedRequest,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Candidate messages loaded. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} candidate_message_count={CandidateMessageCount} rebuild_existing={RebuildExisting}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                candidateMessages.Count,
                normalizedRequest.RebuildExisting);

            failureStage = "group_build";
            var groupingResult = BuildGroups(candidateMessages);

            _logger.LogInformation(
                "Conversation groups created. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} group_count={GroupCount} source_thread_group_count={SourceThreadGroupCount} participant_set_group_count={ParticipantSetGroupCount} ungroupable_candidate_count={UngroupableCandidateCount}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                groupingResult.Groups.Count,
                groupingResult.SourceThreadGroupCount,
                groupingResult.ParticipantSetGroupCount,
                groupingResult.UngroupableCandidateCount);

            failureStage = "conversation_upsert";
            var impactedConversationIds = new HashSet<string>(StringComparer.Ordinal);
            var newConversationIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in groupingResult.Groups.OrderBy(static candidate => candidate.GroupKey, StringComparer.Ordinal))
            {
                var existingConversation = ResolveExistingConversation(group, threadConversationMap, participantConversationMap);
                if (existingConversation is null)
                {
                    var metadata = CreateConversationMetadataFromGroup(group);
                    var conversationId = Guid.NewGuid().ToString("N");
                    var createdAtUtc = DateTimeOffset.UtcNow;

                    var createdConversation = new ExistingConversation(
                        conversationId,
                        normalizedRequest.CaseId,
                        metadata.Title,
                        metadata.Platform,
                        metadata.NormalizedParticipantKey,
                        metadata.SourceThreadKeys,
                        metadata.StartTimeUtc,
                        metadata.EndTimeUtc,
                        metadata.MessageCount,
                        metadata.SourceCount,
                        GapCount: 0,
                        PriorityScore: 0,
                        DefaultReconciliationStatus,
                        DefaultReviewStatus,
                        createdAtUtc,
                        createdAtUtc);

                    await InsertConversationAsync(connection, transaction, createdConversation, cancellationToken).ConfigureAwait(false);

                    existingConversations[conversationId] = createdConversation;
                    RegisterConversationMappings(createdConversation, threadConversationMap, participantConversationMap);
                    newConversationIds.Add(conversationId);
                    impactedConversationIds.Add(conversationId);
                    conversationCountCreated++;
                    existingConversation = createdConversation;
                }

                foreach (var message in group.Messages.OrderBy(static candidate => candidate.Id, StringComparer.Ordinal))
                {
                    if (string.Equals(message.ConversationId, existingConversation.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var updated = await AssignMessageToConversationAsync(
                            connection,
                            transaction,
                            message.Id,
                            existingConversation.Id,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!updated)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(message.ConversationId))
                    {
                        impactedConversationIds.Add(message.ConversationId!);
                    }

                    impactedConversationIds.Add(existingConversation.Id);
                    messageCountAssigned++;
                }
            }

            failureStage = "conversation_recompute";
            foreach (var conversationId in impactedConversationIds.OrderBy(static id => id, StringComparer.Ordinal))
            {
                var existingConversation = existingConversations[conversationId];
                var aggregate = await LoadConversationAggregateAsync(
                        connection,
                        transaction,
                        normalizedRequest.CaseId,
                        conversationId,
                        existingConversation.Platform,
                        cancellationToken)
                    .ConfigureAwait(false);

                var participantSync = await SyncConversationParticipantsAsync(
                        connection,
                        transaction,
                        normalizedRequest.CaseId,
                        conversationId,
                        aggregate.Participants,
                        cancellationToken)
                    .ConfigureAwait(false);
                participantCountCreated += participantSync.CreatedCount;
                participantCountRemoved += participantSync.RemovedCount;

                var metadata = aggregate.ToMetadata();
                if (UpsertedConversationChanged(existingConversation, metadata))
                {
                    await UpdateConversationAsync(
                            connection,
                            transaction,
                            existingConversation,
                            metadata,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!newConversationIds.Contains(conversationId))
                    {
                        conversationCountUpdated++;
                    }

                    existingConversation = existingConversation with
                    {
                        Title = metadata.Title,
                        Platform = metadata.Platform,
                        NormalizedParticipantKey = metadata.NormalizedParticipantKey,
                        SourceThreadKeys = metadata.SourceThreadKeys,
                        StartTimeUtc = metadata.StartTimeUtc,
                        EndTimeUtc = metadata.EndTimeUtc,
                        MessageCount = metadata.MessageCount,
                        SourceCount = metadata.SourceCount,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };
                    existingConversations[conversationId] = existingConversation;
                }

                conversationSummaries.Add(new ConversationBuildSummary
                {
                    ConversationId = conversationId,
                    Title = metadata.Title,
                    Platform = metadata.Platform,
                    NormalizedParticipantKey = metadata.NormalizedParticipantKey,
                    SourceThreadKeysJson = SerializeThreadKeys(metadata.SourceThreadKeys),
                    StartTimeUtc = metadata.StartTimeUtc,
                    EndTimeUtc = metadata.EndTimeUtc,
                    MessageCount = metadata.MessageCount,
                    SourceCount = metadata.SourceCount,
                    GapCount = existingConversation.GapCount,
                    PriorityScore = existingConversation.PriorityScore,
                    ReconciliationStatus = existingConversation.ReconciliationStatus,
                    ReviewStatus = existingConversation.ReviewStatus
                });
            }

            _logger.LogInformation(
                "Conversation records upserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} conversation_count_created={ConversationCountCreated} conversation_count_updated={ConversationCountUpdated}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                conversationCountCreated,
                conversationCountUpdated);

            _logger.LogInformation(
                "Participants upserted. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} participant_count_created={ParticipantCountCreated} participant_count_removed={ParticipantCountRemoved}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                participantCountCreated,
                participantCountRemoved);

            _logger.LogInformation(
                "Messages assigned. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} message_count_assigned={MessageCountAssigned} impacted_conversation_count={ImpactedConversationCount}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                messageCountAssigned,
                impactedConversationIds.Count);

            failureStage = "unassigned_count";
            unassignedMessageCount = await CountUnassignedMessagesAsync(
                    connection,
                    transaction,
                    normalizedRequest,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var completedAtUtc = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Conversation build completed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} conversation_count_created={ConversationCountCreated} conversation_count_updated={ConversationCountUpdated} participant_count_created={ParticipantCountCreated} message_count_assigned={MessageCountAssigned} unassigned_message_count={UnassignedMessageCount}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                conversationCountCreated,
                conversationCountUpdated,
                participantCountCreated,
                messageCountAssigned,
                unassignedMessageCount);

            return new BuildConversationsResult
            {
                CaseId = normalizedRequest.CaseId,
                ConversationCountCreated = conversationCountCreated,
                ConversationCountUpdated = conversationCountUpdated,
                ParticipantCountCreated = participantCountCreated,
                MessageCountAssigned = messageCountAssigned,
                UnassignedMessageCount = unassignedMessageCount,
                ConversationSummaries = conversationSummaries
                    .OrderBy(summary => summary.StartTimeUtc ?? DateTimeOffset.MaxValue)
                    .ThenBy(summary => summary.ConversationId, StringComparer.Ordinal)
                    .ToArray(),
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Conversation build failed. operation={Operation} correlation_id={CorrelationId} case_id={CaseId} source_import_id={SourceImportId} failure_stage={FailureStage} failure_type={FailureType}",
                OperationName,
                normalizedRequest.CorrelationId,
                normalizedRequest.CaseId,
                normalizedRequest.SourceImportId,
                failureStage,
                exception.GetType().Name);
            throw;
        }
    }

    private static NormalizedBuildRequest ValidateAndNormalize(BuildConversationsRequest request)
    {
        var caseId = NormalizeRequired(request.CaseId, nameof(request.CaseId));
        var caseDatabasePath = NormalizeAbsoluteFilePath(request.CaseDatabasePath, nameof(request.CaseDatabasePath));
        if (!File.Exists(caseDatabasePath) || Directory.Exists(caseDatabasePath))
        {
            throw new FileNotFoundException("The case database path must exist and point to a file.", caseDatabasePath);
        }

        return new NormalizedBuildRequest(
            CaseId: caseId,
            CaseDatabasePath: caseDatabasePath,
            SourceImportId: NormalizeOptional(request.SourceImportId),
            RebuildExisting: request.RebuildExisting,
            CorrelationId: NormalizeCorrelationId(request.CorrelationId));
    }

    private static async Task EnsureCaseExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM cases WHERE id = $caseId LIMIT 1;";
        command.Parameters.AddWithValue("$caseId", caseId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            throw new InvalidOperationException("The requested case_id was not found in the case database.");
        }
    }

    private static async Task EnsureSourceImportExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        string sourceImportId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT 1
            FROM source_imports
            WHERE id = $sourceImportId
              AND case_id = $caseId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sourceImportId", sourceImportId);
        command.Parameters.AddWithValue("$caseId", caseId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            throw new InvalidOperationException("The requested source_import_id was not found for the target case.");
        }
    }

    private static async Task<Dictionary<string, ExistingConversation>> LoadExistingConversationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ExistingConversation>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                id,
                case_id,
                title,
                platform,
                normalized_participant_key,
                source_thread_keys_json,
                start_time_utc,
                end_time_utc,
                message_count,
                source_count,
                gap_count,
                priority_score,
                reconciliation_status,
                review_status,
                created_at_utc,
                updated_at_utc
            FROM conversations
            WHERE case_id = $caseId
            ORDER BY created_at_utc ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            results[id] = new ExistingConversation(
                id,
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ParseThreadKeys(reader.IsDBNull(5) ? null : reader.GetString(5)),
                ParseNullableDateTimeOffset(reader.IsDBNull(6) ? null : reader.GetString(6)),
                ParseNullableDateTimeOffset(reader.IsDBNull(7) ? null : reader.GetString(7)),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetDouble(11),
                reader.GetString(12),
                reader.GetString(13),
                DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        return results;
    }

    private static Dictionary<string, ExistingConversation> BuildThreadConversationMap(
        IReadOnlyDictionary<string, ExistingConversation> existingConversations)
    {
        var results = new Dictionary<string, ExistingConversation>(StringComparer.Ordinal);

        foreach (var conversation in existingConversations.Values)
        {
            foreach (var threadKey in conversation.SourceThreadKeys)
            {
                var mapKey = ConversationGroupingRules.BuildThreadGroupKey(conversation.Platform, threadKey);
                results.TryAdd(mapKey, conversation);
            }
        }

        return results;
    }

    private static Dictionary<string, ExistingConversation> BuildParticipantConversationMap(
        IReadOnlyDictionary<string, ExistingConversation> existingConversations)
    {
        var results = new Dictionary<string, ExistingConversation>(StringComparer.Ordinal);

        foreach (var conversation in existingConversations.Values)
        {
            if (conversation.SourceThreadKeys.Count > 0 || string.IsNullOrWhiteSpace(conversation.NormalizedParticipantKey))
            {
                continue;
            }

            var mapKey = ConversationGroupingRules.BuildParticipantGroupKey(
                conversation.Platform,
                conversation.NormalizedParticipantKey);
            results.TryAdd(mapKey, conversation);
        }

        return results;
    }

    private static ExistingConversation? ResolveExistingConversation(
        ConversationGroup group,
        IReadOnlyDictionary<string, ExistingConversation> threadConversationMap,
        IReadOnlyDictionary<string, ExistingConversation> participantConversationMap)
    {
        return group.GroupKind switch
        {
            GroupKindSourceThread => threadConversationMap.TryGetValue(group.GroupKey, out var threadConversation)
                ? threadConversation
                : null,
            GroupKindParticipantSet => participantConversationMap.TryGetValue(group.GroupKey, out var participantConversation)
                ? participantConversation
                : null,
            _ => null
        };
    }

    private static void RegisterConversationMappings(
        ExistingConversation conversation,
        IDictionary<string, ExistingConversation> threadConversationMap,
        IDictionary<string, ExistingConversation> participantConversationMap)
    {
        foreach (var threadKey in conversation.SourceThreadKeys)
        {
            threadConversationMap.TryAdd(
                ConversationGroupingRules.BuildThreadGroupKey(conversation.Platform, threadKey),
                conversation);
        }

        if (conversation.SourceThreadKeys.Count == 0 && !string.IsNullOrWhiteSpace(conversation.NormalizedParticipantKey))
        {
            participantConversationMap.TryAdd(
                ConversationGroupingRules.BuildParticipantGroupKey(
                    conversation.Platform,
                    conversation.NormalizedParticipantKey),
                conversation);
        }
    }

    private static async Task<List<CandidateMessage>> LoadCandidateMessagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedBuildRequest request,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, CandidateMessageBuilder>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                m.id,
                m.source_import_id,
                m.platform,
                m.source_thread_id,
                m.conversation_id,
                m.event_time_utc,
                m.sender_identity_id,
                mr.recipient_identity_id
            FROM messages AS m
            LEFT JOIN message_recipients AS mr
                ON mr.message_id = m.id
            WHERE m.case_id = $caseId
              AND ($sourceImportId IS NULL OR m.source_import_id = $sourceImportId)
              AND ($rebuildExisting = 1 OR m.conversation_id IS NULL)
            ORDER BY m.id ASC, mr.recipient_identity_id ASC;
            """;
        command.Parameters.AddWithValue("$caseId", request.CaseId);
        command.Parameters.AddWithValue("$sourceImportId", ToSqlValue(request.SourceImportId));
        command.Parameters.AddWithValue("$rebuildExisting", request.RebuildExisting ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var messageId = reader.GetString(0);
            if (!results.TryGetValue(messageId, out var builder))
            {
                builder = new CandidateMessageBuilder(
                    messageId,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    ParseNullableDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6));
                results.Add(messageId, builder);
            }

            if (!reader.IsDBNull(7))
            {
                builder.RecipientIdentityIds.Add(reader.GetString(7));
            }
        }

        return results.Values
            .Select(static builder => builder.Build())
            .OrderBy(static message => message.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static GroupingResult BuildGroups(IReadOnlyList<CandidateMessage> candidateMessages)
    {
        var groups = new SortedDictionary<string, ConversationGroupBuilder>(StringComparer.Ordinal);
        var sourceThreadGroupCount = 0;
        var participantSetGroupCount = 0;
        var ungroupableCandidateCount = 0;

        foreach (var message in candidateMessages)
        {
            var participantIds = GetParticipantIdentityIds(message);
            var normalizedParticipantKey = ConversationGroupingRules.NormalizeParticipantKey(participantIds);
            var normalizedPlatform = ConversationGroupingRules.NormalizePlatform(message.Platform);
            var sourceThreadId = NormalizeOptional(message.SourceThreadId);

            string? groupKind = null;
            string? groupKey = null;

            if (sourceThreadId is not null)
            {
                groupKind = GroupKindSourceThread;
                groupKey = ConversationGroupingRules.BuildThreadGroupKey(normalizedPlatform, sourceThreadId);
            }
            else if (normalizedParticipantKey is not null)
            {
                groupKind = GroupKindParticipantSet;
                groupKey = ConversationGroupingRules.BuildParticipantGroupKey(normalizedPlatform, normalizedParticipantKey);
            }
            else
            {
                ungroupableCandidateCount++;
            }

            if (groupKind is null || groupKey is null)
            {
                continue;
            }

            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = new ConversationGroupBuilder(groupKind, groupKey, normalizedPlatform, sourceThreadId, normalizedParticipantKey);
                groups.Add(groupKey, group);

                if (string.Equals(groupKind, GroupKindSourceThread, StringComparison.Ordinal))
                {
                    sourceThreadGroupCount++;
                }
                else
                {
                    participantSetGroupCount++;
                }
            }

            group.Messages.Add(message);
            foreach (var participantId in participantIds)
            {
                group.ParticipantIdentityIds.Add(participantId);
            }
        }

        return new GroupingResult(
            groups.Values.Select(static builder => builder.Build()).ToArray(),
            sourceThreadGroupCount,
            participantSetGroupCount,
            ungroupableCandidateCount);
    }

    private static ConversationMetadata CreateConversationMetadataFromGroup(ConversationGroup group)
    {
        var threadKeys = group.Messages
            .Select(static message => NormalizeOptional(message.SourceThreadId))
            .Where(static threadKey => threadKey is not null)
            .Select(static threadKey => threadKey!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static threadKey => threadKey, StringComparer.Ordinal)
            .ToArray();
        var participantIds = group.ParticipantIdentityIds.OrderBy(static participantId => participantId, StringComparer.Ordinal).ToArray();
        var startTimeUtc = group.Messages
            .Where(static message => message.EventTimeUtc.HasValue)
            .Select(static message => message.EventTimeUtc!.Value)
            .DefaultIfEmpty()
            .Min();
        var endTimeUtc = group.Messages
            .Where(static message => message.EventTimeUtc.HasValue)
            .Select(static message => message.EventTimeUtc!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConversationMetadata(
            Title: ConversationGroupingRules.BuildSafeTitle(group.Platform, participantIds.Length),
            Platform: group.Platform,
            NormalizedParticipantKey: ConversationGroupingRules.NormalizeParticipantKey(participantIds),
            SourceThreadKeys: threadKeys,
            StartTimeUtc: startTimeUtc == default ? null : startTimeUtc,
            EndTimeUtc: endTimeUtc == default ? null : endTimeUtc,
            MessageCount: group.Messages.Count,
            SourceCount: group.Messages.Select(static message => message.SourceImportId).Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task InsertConversationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExistingConversation conversation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO conversations (
                id,
                case_id,
                title,
                platform,
                normalized_participant_key,
                source_thread_keys_json,
                start_time_utc,
                end_time_utc,
                message_count,
                source_count,
                gap_count,
                priority_score,
                reconciliation_status,
                review_status,
                summary,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $caseId,
                $title,
                $platform,
                $normalizedParticipantKey,
                $sourceThreadKeysJson,
                $startTimeUtc,
                $endTimeUtc,
                $messageCount,
                $sourceCount,
                $gapCount,
                $priorityScore,
                $reconciliationStatus,
                $reviewStatus,
                NULL,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", conversation.Id);
        command.Parameters.AddWithValue("$caseId", conversation.CaseId);
        command.Parameters.AddWithValue("$title", conversation.Title);
        command.Parameters.AddWithValue("$platform", ToSqlValue(conversation.Platform));
        command.Parameters.AddWithValue("$normalizedParticipantKey", ToSqlValue(conversation.NormalizedParticipantKey));
        command.Parameters.AddWithValue("$sourceThreadKeysJson", SerializeThreadKeys(conversation.SourceThreadKeys));
        command.Parameters.AddWithValue("$startTimeUtc", ToSqlValue(FormatUtc(conversation.StartTimeUtc)));
        command.Parameters.AddWithValue("$endTimeUtc", ToSqlValue(FormatUtc(conversation.EndTimeUtc)));
        command.Parameters.AddWithValue("$messageCount", conversation.MessageCount);
        command.Parameters.AddWithValue("$sourceCount", conversation.SourceCount);
        command.Parameters.AddWithValue("$gapCount", conversation.GapCount);
        command.Parameters.AddWithValue("$priorityScore", conversation.PriorityScore);
        command.Parameters.AddWithValue("$reconciliationStatus", conversation.ReconciliationStatus);
        command.Parameters.AddWithValue("$reviewStatus", conversation.ReviewStatus);
        command.Parameters.AddWithValue("$createdAtUtc", FormatUtc(conversation.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(conversation.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> AssignMessageToConversationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string messageId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE messages
            SET conversation_id = $conversationId,
                updated_at_utc = $updatedAtUtc
            WHERE id = $messageId
              AND (conversation_id IS NULL OR conversation_id <> $conversationId);
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$messageId", messageId);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static async Task<ConversationAggregate> LoadConversationAggregateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        string conversationId,
        string? fallbackPlatform,
        CancellationToken cancellationToken)
    {
        var messages = new List<ConversationMessageRow>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    id,
                    source_import_id,
                    platform,
                    source_thread_id,
                    event_time_utc
                FROM messages
                WHERE case_id = $caseId
                  AND conversation_id = $conversationId
                ORDER BY event_time_utc ASC, id ASC;
                """;
            command.Parameters.AddWithValue("$caseId", caseId);
            command.Parameters.AddWithValue("$conversationId", conversationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new ConversationMessageRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    ParseNullableDateTimeOffset(reader.IsDBNull(4) ? null : reader.GetString(4))));
            }
        }

        var participants = new List<ConversationParticipantTarget>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                WITH participant_candidates AS (
                    SELECT
                        m.sender_identity_id AS identity_id,
                        i.linked_person_id AS person_id,
                        m.source_import_id AS source_import_id
                    FROM messages AS m
                    LEFT JOIN identities AS i
                        ON i.id = m.sender_identity_id
                    WHERE m.case_id = $caseId
                      AND m.conversation_id = $conversationId
                      AND m.sender_identity_id IS NOT NULL

                    UNION ALL

                    SELECT
                        mr.recipient_identity_id AS identity_id,
                        i.linked_person_id AS person_id,
                        m.source_import_id AS source_import_id
                    FROM message_recipients AS mr
                    INNER JOIN messages AS m
                        ON m.id = mr.message_id
                    LEFT JOIN identities AS i
                        ON i.id = mr.recipient_identity_id
                    WHERE m.case_id = $caseId
                      AND m.conversation_id = $conversationId
                      AND mr.recipient_identity_id IS NOT NULL
                )
                SELECT
                    identity_id,
                    MAX(person_id),
                    MIN(source_import_id)
                FROM participant_candidates
                GROUP BY identity_id
                ORDER BY identity_id ASC;
                """;
            command.Parameters.AddWithValue("$caseId", caseId);
            command.Parameters.AddWithValue("$conversationId", conversationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                participants.Add(new ConversationParticipantTarget(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var platform = messages
            .Select(static message => ConversationGroupingRules.NormalizePlatform(message.Platform))
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static candidate => candidate, StringComparer.Ordinal)
            .FirstOrDefault() ?? ConversationGroupingRules.NormalizePlatform(fallbackPlatform);

        var sourceThreadKeys = messages
            .Select(static message => NormalizeOptional(message.SourceThreadId))
            .Where(static threadKey => threadKey is not null)
            .Select(static threadKey => threadKey!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static threadKey => threadKey, StringComparer.Ordinal)
            .ToArray();
        var participantIds = participants.Select(static participant => participant.IdentityId).ToArray();
        var normalizedParticipantKey = ConversationGroupingRules.NormalizeParticipantKey(participantIds);
        var startTimeUtc = messages
            .Where(static message => message.EventTimeUtc.HasValue)
            .Select(static message => message.EventTimeUtc!.Value)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
        var endTimeUtc = messages
            .Where(static message => message.EventTimeUtc.HasValue)
            .Select(static message => message.EventTimeUtc!.Value)
            .Cast<DateTimeOffset?>()
            .LastOrDefault();

        return new ConversationAggregate(
            Title: ConversationGroupingRules.BuildSafeTitle(platform, participantIds.Length),
            Platform: platform,
            NormalizedParticipantKey: normalizedParticipantKey,
            SourceThreadKeys: sourceThreadKeys,
            StartTimeUtc: startTimeUtc,
            EndTimeUtc: endTimeUtc,
            MessageCount: messages.Count,
            SourceCount: messages.Select(static message => message.SourceImportId).Distinct(StringComparer.Ordinal).Count(),
            Participants: participants);
    }

    private static bool UpsertedConversationChanged(ExistingConversation existingConversation, ConversationMetadata metadata)
    {
        return !string.Equals(existingConversation.Title, metadata.Title, StringComparison.Ordinal)
               || !string.Equals(existingConversation.Platform, metadata.Platform, StringComparison.Ordinal)
               || !string.Equals(existingConversation.NormalizedParticipantKey, metadata.NormalizedParticipantKey, StringComparison.Ordinal)
               || !existingConversation.SourceThreadKeys.SequenceEqual(metadata.SourceThreadKeys, StringComparer.Ordinal)
               || existingConversation.StartTimeUtc != metadata.StartTimeUtc
               || existingConversation.EndTimeUtc != metadata.EndTimeUtc
               || existingConversation.MessageCount != metadata.MessageCount
               || existingConversation.SourceCount != metadata.SourceCount;
    }

    private static async Task UpdateConversationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExistingConversation existingConversation,
        ConversationMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE conversations
            SET title = $title,
                platform = $platform,
                normalized_participant_key = $normalizedParticipantKey,
                source_thread_keys_json = $sourceThreadKeysJson,
                start_time_utc = $startTimeUtc,
                end_time_utc = $endTimeUtc,
                message_count = $messageCount,
                source_count = $sourceCount,
                updated_at_utc = $updatedAtUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", existingConversation.Id);
        command.Parameters.AddWithValue("$title", metadata.Title);
        command.Parameters.AddWithValue("$platform", ToSqlValue(metadata.Platform));
        command.Parameters.AddWithValue("$normalizedParticipantKey", ToSqlValue(metadata.NormalizedParticipantKey));
        command.Parameters.AddWithValue("$sourceThreadKeysJson", SerializeThreadKeys(metadata.SourceThreadKeys));
        command.Parameters.AddWithValue("$startTimeUtc", ToSqlValue(FormatUtc(metadata.StartTimeUtc)));
        command.Parameters.AddWithValue("$endTimeUtc", ToSqlValue(FormatUtc(metadata.EndTimeUtc)));
        command.Parameters.AddWithValue("$messageCount", metadata.MessageCount);
        command.Parameters.AddWithValue("$sourceCount", metadata.SourceCount);
        command.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ParticipantSyncResult> SyncConversationParticipantsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        string conversationId,
        IReadOnlyList<ConversationParticipantTarget> targetParticipants,
        CancellationToken cancellationToken)
    {
        var existingParticipants = new Dictionary<string, ExistingConversationParticipant>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT id, identity_id, person_id, source_import_id
                FROM conversation_participants
                WHERE conversation_id = $conversationId
                ORDER BY identity_id ASC;
                """;
            command.Parameters.AddWithValue("$conversationId", conversationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var participant = new ExistingConversationParticipant(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3));
                existingParticipants[participant.IdentityId] = participant;
            }
        }

        var targetByIdentityId = targetParticipants.ToDictionary(
            static participant => participant.IdentityId,
            StringComparer.Ordinal);

        var removedCount = 0;
        foreach (var existingParticipant in existingParticipants.Values)
        {
            if (targetByIdentityId.ContainsKey(existingParticipant.IdentityId))
            {
                continue;
            }

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM conversation_participants WHERE id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", existingParticipant.Id);
            removedCount += await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var createdCount = 0;
        foreach (var target in targetParticipants.OrderBy(static participant => participant.IdentityId, StringComparer.Ordinal))
        {
            if (existingParticipants.TryGetValue(target.IdentityId, out var existingParticipant))
            {
                var requiresUpdate =
                    !string.Equals(existingParticipant.PersonId, target.PersonId, StringComparison.Ordinal)
                    || !string.Equals(existingParticipant.SourceImportId, target.SourceImportId, StringComparison.Ordinal);

                if (!requiresUpdate)
                {
                    continue;
                }

                await using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText =
                    """
                    UPDATE conversation_participants
                    SET person_id = $personId,
                        source_import_id = $sourceImportId,
                        participant_role = $participantRole
                    WHERE id = $id;
                    """;
                updateCommand.Parameters.AddWithValue("$id", existingParticipant.Id);
                updateCommand.Parameters.AddWithValue("$personId", ToSqlValue(target.PersonId));
                updateCommand.Parameters.AddWithValue("$sourceImportId", ToSqlValue(target.SourceImportId));
                updateCommand.Parameters.AddWithValue("$participantRole", ConversationParticipantRole);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO conversation_participants (
                    id,
                    case_id,
                    conversation_id,
                    identity_id,
                    person_id,
                    participant_role,
                    source_import_id,
                    created_at_utc
                )
                VALUES (
                    $id,
                    $caseId,
                    $conversationId,
                    $identityId,
                    $personId,
                    $participantRole,
                    $sourceImportId,
                    $createdAtUtc
                );
                """;
            insertCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insertCommand.Parameters.AddWithValue("$caseId", caseId);
            insertCommand.Parameters.AddWithValue("$conversationId", conversationId);
            insertCommand.Parameters.AddWithValue("$identityId", target.IdentityId);
            insertCommand.Parameters.AddWithValue("$personId", ToSqlValue(target.PersonId));
            insertCommand.Parameters.AddWithValue("$participantRole", ConversationParticipantRole);
            insertCommand.Parameters.AddWithValue("$sourceImportId", ToSqlValue(target.SourceImportId));
            insertCommand.Parameters.AddWithValue("$createdAtUtc", FormatUtc(DateTimeOffset.UtcNow));
            createdCount += await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ParticipantSyncResult(createdCount, removedCount);
    }

    private static async Task<int> CountUnassignedMessagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NormalizedBuildRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM messages
            WHERE case_id = $caseId
              AND ($sourceImportId IS NULL OR source_import_id = $sourceImportId)
              AND conversation_id IS NULL;
            """;
        command.Parameters.AddWithValue("$caseId", request.CaseId);
        command.Parameters.AddWithValue("$sourceImportId", ToSqlValue(request.SourceImportId));

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> GetParticipantIdentityIds(CandidateMessage message)
    {
        var participantIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.SenderIdentityId))
        {
            participantIds.Add(message.SenderIdentityId!);
        }

        participantIds.AddRange(message.RecipientIdentityIds.Where(static identityId => !string.IsNullOrWhiteSpace(identityId)));
        return participantIds;
    }

    private static string SerializeThreadKeys(IReadOnlyList<string> threadKeys)
    {
        return JsonSerializer.Serialize(threadKeys, JsonOptions);
    }

    private static IReadOnlyList<string> ParseThreadKeys(string? sourceThreadKeysJson)
    {
        var normalizedJson = NormalizeOptional(sourceThreadKeysJson);
        if (normalizedJson is null)
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<string[]>(normalizedJson, JsonOptions) ?? [])
                .Where(static threadKey => !string.IsNullOrWhiteSpace(threadKey))
                .Select(static threadKey => threadKey!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static threadKey => threadKey, StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildConnectionString(string caseDatabasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = caseDatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? FormatUtc(DateTimeOffset? value)
    {
        return value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value)
    {
        var normalizedValue = NormalizeOptional(value);
        return normalizedValue is null
            ? null
            : DateTimeOffset.Parse(normalizedValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        return NormalizeOptional(correlationId) ?? Guid.NewGuid().ToString("N");
    }

    private static string NormalizeAbsoluteFilePath(string path, string parameterName)
    {
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        return Path.GetFullPath(path.Trim());
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static object ToSqlValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value;
    }

    private sealed record NormalizedBuildRequest(
        string CaseId,
        string CaseDatabasePath,
        string? SourceImportId,
        bool RebuildExisting,
        string CorrelationId);

    private sealed record ExistingConversation(
        string Id,
        string CaseId,
        string Title,
        string? Platform,
        string? NormalizedParticipantKey,
        IReadOnlyList<string> SourceThreadKeys,
        DateTimeOffset? StartTimeUtc,
        DateTimeOffset? EndTimeUtc,
        int MessageCount,
        int SourceCount,
        int GapCount,
        double PriorityScore,
        string ReconciliationStatus,
        string ReviewStatus,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record CandidateMessage(
        string Id,
        string SourceImportId,
        string? Platform,
        string? SourceThreadId,
        string? ConversationId,
        DateTimeOffset? EventTimeUtc,
        string? SenderIdentityId,
        IReadOnlyList<string> RecipientIdentityIds);

    private sealed class CandidateMessageBuilder
    {
        public CandidateMessageBuilder(
            string id,
            string sourceImportId,
            string? platform,
            string? sourceThreadId,
            string? conversationId,
            DateTimeOffset? eventTimeUtc,
            string? senderIdentityId)
        {
            Id = id;
            SourceImportId = sourceImportId;
            Platform = platform;
            SourceThreadId = sourceThreadId;
            ConversationId = conversationId;
            EventTimeUtc = eventTimeUtc;
            SenderIdentityId = senderIdentityId;
        }

        public string Id { get; }

        public string SourceImportId { get; }

        public string? Platform { get; }

        public string? SourceThreadId { get; }

        public string? ConversationId { get; }

        public DateTimeOffset? EventTimeUtc { get; }

        public string? SenderIdentityId { get; }

        public HashSet<string> RecipientIdentityIds { get; } = new(StringComparer.Ordinal);

        public CandidateMessage Build()
        {
            return new CandidateMessage(
                Id,
                SourceImportId,
                Platform,
                SourceThreadId,
                ConversationId,
                EventTimeUtc,
                SenderIdentityId,
                RecipientIdentityIds.OrderBy(static identityId => identityId, StringComparer.Ordinal).ToArray());
        }
    }

    private sealed class ConversationGroupBuilder
    {
        public ConversationGroupBuilder(
            string groupKind,
            string groupKey,
            string? platform,
            string? sourceThreadId,
            string? normalizedParticipantKey)
        {
            GroupKind = groupKind;
            GroupKey = groupKey;
            Platform = platform;
            SourceThreadId = sourceThreadId;
            NormalizedParticipantKey = normalizedParticipantKey;
        }

        public string GroupKind { get; }

        public string GroupKey { get; }

        public string? Platform { get; }

        public string? SourceThreadId { get; }

        public string? NormalizedParticipantKey { get; }

        public List<CandidateMessage> Messages { get; } = [];

        public HashSet<string> ParticipantIdentityIds { get; } = new(StringComparer.Ordinal);

        public ConversationGroup Build()
        {
            return new ConversationGroup(
                GroupKind,
                GroupKey,
                Platform,
                SourceThreadId,
                NormalizedParticipantKey,
                Messages.OrderBy(static message => message.Id, StringComparer.Ordinal).ToArray(),
                ParticipantIdentityIds.OrderBy(static identityId => identityId, StringComparer.Ordinal).ToArray());
        }
    }

    private sealed record ConversationGroup(
        string GroupKind,
        string GroupKey,
        string? Platform,
        string? SourceThreadId,
        string? NormalizedParticipantKey,
        IReadOnlyList<CandidateMessage> Messages,
        IReadOnlyList<string> ParticipantIdentityIds);

    private sealed record GroupingResult(
        IReadOnlyList<ConversationGroup> Groups,
        int SourceThreadGroupCount,
        int ParticipantSetGroupCount,
        int UngroupableCandidateCount);

    private sealed record ConversationMetadata(
        string Title,
        string? Platform,
        string? NormalizedParticipantKey,
        IReadOnlyList<string> SourceThreadKeys,
        DateTimeOffset? StartTimeUtc,
        DateTimeOffset? EndTimeUtc,
        int MessageCount,
        int SourceCount);

    private sealed record ConversationMessageRow(
        string Id,
        string SourceImportId,
        string? Platform,
        string? SourceThreadId,
        DateTimeOffset? EventTimeUtc);

    private sealed record ConversationParticipantTarget(
        string IdentityId,
        string? PersonId,
        string? SourceImportId);

    private sealed record ExistingConversationParticipant(
        string Id,
        string IdentityId,
        string? PersonId,
        string? SourceImportId);

    private sealed record ParticipantSyncResult(
        int CreatedCount,
        int RemovedCount);

    private sealed record ConversationAggregate(
        string Title,
        string? Platform,
        string? NormalizedParticipantKey,
        IReadOnlyList<string> SourceThreadKeys,
        DateTimeOffset? StartTimeUtc,
        DateTimeOffset? EndTimeUtc,
        int MessageCount,
        int SourceCount,
        IReadOnlyList<ConversationParticipantTarget> Participants)
    {
        public ConversationMetadata ToMetadata()
        {
            return new ConversationMetadata(
                Title,
                Platform,
                NormalizedParticipantKey,
                SourceThreadKeys,
                StartTimeUtc,
                EndTimeUtc,
                MessageCount,
                SourceCount);
        }
    }
}
