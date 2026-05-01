CREATE VIRTUAL TABLE message_search_index USING fts5 (
    case_id UNINDEXED,
    message_id UNINDEXED,
    conversation_id UNINDEXED,
    source_import_id UNINDEXED,
    source_artifact_id UNINDEXED,
    provider_message_id UNINDEXED,
    source_thread_id UNINDEXED,
    event_time_utc UNINDEXED,
    direction,
    platform,
    deleted_status,
    message_body,
    tokenize = 'unicode61'
);
