CREATE TABLE persons (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    legal_name TEXT,
    person_type TEXT NOT NULL DEFAULT 'unknown',
    role_tags_json TEXT,
    date_of_birth TEXT,
    notes TEXT,
    confidence TEXT NOT NULL DEFAULT 'unknown',
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    created_by TEXT NOT NULL DEFAULT 'user',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE
);

CREATE INDEX idx_persons_case ON persons (case_id);
CREATE INDEX idx_persons_display_name ON persons (display_name);
CREATE INDEX idx_persons_role ON persons (person_type);

CREATE TABLE identities (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    identity_type TEXT NOT NULL,
    raw_value TEXT NOT NULL,
    normalized_value TEXT,
    display_value TEXT,
    linked_person_id TEXT,
    source_import_id TEXT,
    source_artifact_id TEXT,
    platform TEXT,
    confidence TEXT NOT NULL DEFAULT 'unknown',
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    created_by TEXT NOT NULL DEFAULT 'system',
    notes TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (linked_person_id) REFERENCES persons(id),
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id),
    FOREIGN KEY (source_artifact_id) REFERENCES source_artifacts(id)
);

CREATE INDEX idx_identities_case ON identities (case_id);
CREATE INDEX idx_identities_type ON identities (identity_type);
CREATE INDEX idx_identities_norm ON identities (normalized_value);
CREATE INDEX idx_identities_person ON identities (linked_person_id);

CREATE TABLE identity_links (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_identity_id TEXT NOT NULL,
    target_identity_id TEXT,
    target_person_id TEXT,
    link_type TEXT NOT NULL,
    confidence TEXT NOT NULL DEFAULT 'unknown',
    link_status TEXT NOT NULL DEFAULT 'suggested',
    supporting_artifacts_json TEXT,
    created_by TEXT NOT NULL DEFAULT 'system',
    reviewed_by_user_id TEXT,
    reviewed_at_utc TEXT,
    notes TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_identity_id) REFERENCES identities(id) ON DELETE CASCADE,
    FOREIGN KEY (target_identity_id) REFERENCES identities(id) ON DELETE CASCADE,
    FOREIGN KEY (target_person_id) REFERENCES persons(id) ON DELETE CASCADE,
    FOREIGN KEY (reviewed_by_user_id) REFERENCES app_users(id),
    CHECK (target_identity_id IS NOT NULL OR target_person_id IS NOT NULL)
);

CREATE INDEX idx_identity_links_case ON identity_links (case_id);
CREATE INDEX idx_identity_links_source ON identity_links (source_identity_id);
CREATE INDEX idx_identity_links_status ON identity_links (link_status);

CREATE TABLE devices (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    owner_person_id TEXT,
    device_label TEXT NOT NULL,
    make TEXT,
    model TEXT,
    os_name TEXT,
    os_version TEXT,
    phone_number_identity_id TEXT,
    imei TEXT,
    meid TEXT,
    serial_number TEXT,
    extraction_type TEXT,
    notes TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (owner_person_id) REFERENCES persons(id),
    FOREIGN KEY (phone_number_identity_id) REFERENCES identities(id)
);

CREATE INDEX idx_devices_case ON devices (case_id);
CREATE INDEX idx_devices_owner ON devices (owner_person_id);

CREATE TABLE platform_accounts (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    linked_person_id TEXT,
    platform TEXT NOT NULL,
    username TEXT,
    normalized_username TEXT,
    account_numeric_id TEXT,
    linked_phone_identity_id TEXT,
    linked_email_identity_id TEXT,
    confidence TEXT NOT NULL DEFAULT 'unknown',
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    source_import_id TEXT,
    notes TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (linked_person_id) REFERENCES persons(id),
    FOREIGN KEY (linked_phone_identity_id) REFERENCES identities(id),
    FOREIGN KEY (linked_email_identity_id) REFERENCES identities(id),
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id)
);

CREATE INDEX idx_platform_accounts_case ON platform_accounts (case_id);
CREATE INDEX idx_platform_accounts_platform ON platform_accounts (platform);
CREATE INDEX idx_platform_accounts_username ON platform_accounts (normalized_username);

CREATE TABLE conversations (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    title TEXT NOT NULL,
    platform TEXT,
    normalized_participant_key TEXT,
    source_thread_keys_json TEXT,
    start_time_utc TEXT,
    end_time_utc TEXT,
    message_count INTEGER NOT NULL DEFAULT 0,
    source_count INTEGER NOT NULL DEFAULT 0,
    gap_count INTEGER NOT NULL DEFAULT 0,
    priority_score REAL NOT NULL DEFAULT 0,
    reconciliation_status TEXT NOT NULL DEFAULT 'not_started',
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    summary TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE
);

CREATE INDEX idx_conversations_case ON conversations (case_id);
CREATE INDEX idx_conversations_time ON conversations (case_id, start_time_utc);
CREATE INDEX idx_conversations_priority ON conversations (priority_score);

CREATE TABLE messages (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_import_id TEXT NOT NULL,
    source_artifact_id TEXT,
    platform TEXT,
    source_thread_id TEXT,
    provider_message_id TEXT,
    conversation_id TEXT,
    event_time_original TEXT,
    event_time_utc TEXT,
    timezone TEXT,
    sender_identity_id TEXT,
    direction TEXT,
    message_body TEXT,
    message_body_normalized TEXT,
    message_body_sha256 TEXT,
    has_attachments INTEGER NOT NULL DEFAULT 0,
    deleted_status TEXT NOT NULL DEFAULT 'present',
    read_status TEXT,
    import_confidence TEXT NOT NULL DEFAULT 'medium',
    reconciliation_status TEXT NOT NULL DEFAULT 'unmatched',
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    original_metadata_json TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id) ON DELETE CASCADE,
    FOREIGN KEY (source_artifact_id) REFERENCES source_artifacts(id),
    FOREIGN KEY (conversation_id) REFERENCES conversations(id),
    FOREIGN KEY (sender_identity_id) REFERENCES identities(id)
);

CREATE INDEX idx_messages_case_time ON messages (case_id, event_time_utc);
CREATE INDEX idx_messages_source ON messages (source_import_id);
CREATE INDEX idx_messages_sender ON messages (sender_identity_id);
CREATE INDEX idx_messages_conversation ON messages (conversation_id);
CREATE INDEX idx_messages_body_hash ON messages (message_body_sha256);
CREATE INDEX idx_messages_deleted_status ON messages (deleted_status);
CREATE INDEX idx_messages_reconciliation_status ON messages (reconciliation_status);

CREATE TABLE message_recipients (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    message_id TEXT NOT NULL,
    recipient_identity_id TEXT NOT NULL,
    recipient_role TEXT NOT NULL DEFAULT 'recipient',
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (message_id) REFERENCES messages(id) ON DELETE CASCADE,
    FOREIGN KEY (recipient_identity_id) REFERENCES identities(id),
    UNIQUE (message_id, recipient_identity_id, recipient_role)
);

CREATE INDEX idx_message_recipients_message ON message_recipients (message_id);
CREATE INDEX idx_message_recipients_identity ON message_recipients (recipient_identity_id);

CREATE TABLE conversation_participants (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    conversation_id TEXT NOT NULL,
    identity_id TEXT NOT NULL,
    person_id TEXT,
    participant_role TEXT,
    source_import_id TEXT,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE,
    FOREIGN KEY (identity_id) REFERENCES identities(id),
    FOREIGN KEY (person_id) REFERENCES persons(id),
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id),
    UNIQUE (conversation_id, identity_id)
);

CREATE INDEX idx_conversation_participants_conv ON conversation_participants (conversation_id);
CREATE INDEX idx_conversation_participants_identity ON conversation_participants (identity_id);

CREATE TABLE calls (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_import_id TEXT NOT NULL,
    source_artifact_id TEXT,
    event_time_original TEXT,
    event_time_utc TEXT,
    timezone TEXT,
    caller_identity_id TEXT,
    callee_identity_id TEXT,
    direction TEXT,
    call_type TEXT,
    duration_seconds INTEGER,
    platform_or_carrier TEXT,
    cell_site_json TEXT,
    import_confidence TEXT NOT NULL DEFAULT 'medium',
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    original_metadata_json TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id) ON DELETE CASCADE,
    FOREIGN KEY (source_artifact_id) REFERENCES source_artifacts(id),
    FOREIGN KEY (caller_identity_id) REFERENCES identities(id),
    FOREIGN KEY (callee_identity_id) REFERENCES identities(id)
);

CREATE INDEX idx_calls_case_time ON calls (case_id, event_time_utc);
CREATE INDEX idx_calls_caller ON calls (caller_identity_id);
CREATE INDEX idx_calls_callee ON calls (callee_identity_id);
CREATE INDEX idx_calls_source ON calls (source_import_id);

CREATE TABLE attachments (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_import_id TEXT NOT NULL,
    source_artifact_id TEXT,
    linked_message_id TEXT,
    filename TEXT,
    stored_path TEXT,
    file_sha256 TEXT,
    file_md5 TEXT,
    mime_type TEXT,
    size_bytes INTEGER,
    width INTEGER,
    height INTEGER,
    duration_seconds REAL,
    extracted_text TEXT,
    thumbnail_path TEXT,
    metadata_json TEXT,
    review_status TEXT NOT NULL DEFAULT 'unreviewed',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id) ON DELETE CASCADE,
    FOREIGN KEY (source_artifact_id) REFERENCES source_artifacts(id),
    FOREIGN KEY (linked_message_id) REFERENCES messages(id) ON DELETE SET NULL
);

CREATE INDEX idx_attachments_case ON attachments (case_id);
CREATE INDEX idx_attachments_message ON attachments (linked_message_id);
CREATE INDEX idx_attachments_hash ON attachments (file_sha256);
