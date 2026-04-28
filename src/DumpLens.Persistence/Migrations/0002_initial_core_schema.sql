CREATE TABLE cases (
    id TEXT NOT NULL PRIMARY KEY,
    case_number TEXT,
    title TEXT NOT NULL,
    incident_type TEXT,
    incident_start_utc TEXT,
    incident_end_utc TEXT,
    incident_timezone TEXT,
    incident_location_text TEXT,
    lead_investigator TEXT,
    agency TEXT,
    summary TEXT,
    case_status TEXT NOT NULL DEFAULT 'open',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE INDEX idx_cases_case_number ON cases (case_number);
CREATE INDEX idx_cases_status ON cases (case_status);

CREATE TABLE app_users (
    id TEXT NOT NULL PRIMARY KEY,
    display_name TEXT NOT NULL,
    username TEXT,
    agency TEXT,
    role TEXT NOT NULL DEFAULT 'investigator',
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE case_users (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    case_role TEXT NOT NULL,
    permissions_json TEXT,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES app_users(id),
    UNIQUE (case_id, user_id)
);

CREATE TABLE source_imports (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_name TEXT NOT NULL,
    source_type TEXT NOT NULL,
    platform TEXT,
    owner_person_id TEXT,
    device_id TEXT,
    platform_account_id TEXT,
    extraction_type TEXT,
    provider_return_type TEXT,
    original_filename TEXT NOT NULL,
    original_file_path TEXT,
    stored_file_path TEXT,
    file_size_bytes INTEGER,
    file_sha256 TEXT NOT NULL,
    file_md5 TEXT,
    imported_by_user_id TEXT,
    imported_at_utc TEXT NOT NULL,
    import_status TEXT NOT NULL DEFAULT 'imported',
    record_count INTEGER DEFAULT 0,
    warning_count INTEGER DEFAULT 0,
    notes TEXT,
    source_metadata_json TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (imported_by_user_id) REFERENCES app_users(id)
);

CREATE INDEX idx_source_imports_case ON source_imports (case_id);
CREATE INDEX idx_source_imports_hash ON source_imports (file_sha256);
CREATE INDEX idx_source_imports_type ON source_imports (source_type);

CREATE TABLE source_artifacts (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_import_id TEXT NOT NULL,
    artifact_type TEXT NOT NULL,
    artifact_locator TEXT,
    row_number INTEGER,
    page_number INTEGER,
    object_path TEXT,
    provider_object_id TEXT,
    artifact_hash TEXT,
    raw_text TEXT,
    raw_metadata_json TEXT,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id) ON DELETE CASCADE
);

CREATE INDEX idx_source_artifacts_source ON source_artifacts (source_import_id);
CREATE INDEX idx_source_artifacts_case ON source_artifacts (case_id);
CREATE INDEX idx_source_artifacts_provider_id ON source_artifacts (provider_object_id);

CREATE TABLE import_mappings (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_import_id TEXT NOT NULL,
    mapping_name TEXT,
    source_format TEXT,
    timezone_assumption TEXT,
    field_mapping_json TEXT NOT NULL,
    parser_settings_json TEXT,
    created_by_user_id TEXT,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id) ON DELETE CASCADE,
    FOREIGN KEY (created_by_user_id) REFERENCES app_users(id)
);

CREATE TABLE import_warnings (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT NOT NULL,
    source_import_id TEXT NOT NULL,
    artifact_id TEXT,
    severity TEXT NOT NULL,
    warning_code TEXT NOT NULL,
    message TEXT NOT NULL,
    field_name TEXT,
    raw_value TEXT,
    resolved_status TEXT NOT NULL DEFAULT 'open',
    resolved_by_user_id TEXT,
    resolved_at_utc TEXT,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (source_import_id) REFERENCES source_imports(id) ON DELETE CASCADE,
    FOREIGN KEY (artifact_id) REFERENCES source_artifacts(id),
    FOREIGN KEY (resolved_by_user_id) REFERENCES app_users(id)
);

CREATE INDEX idx_import_warnings_source ON import_warnings (source_import_id);
CREATE INDEX idx_import_warnings_status ON import_warnings (resolved_status);

CREATE TABLE audit_events (
    id TEXT NOT NULL PRIMARY KEY,
    case_id TEXT,
    user_id TEXT,
    action_type TEXT NOT NULL,
    entity_type TEXT,
    entity_id TEXT,
    summary TEXT NOT NULL,
    old_value_json TEXT,
    new_value_json TEXT,
    reason TEXT,
    event_time_utc TEXT NOT NULL,
    workstation TEXT,
    app_version TEXT,
    hash_chain_previous TEXT,
    hash_chain_current TEXT,
    FOREIGN KEY (case_id) REFERENCES cases(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES app_users(id)
);

CREATE INDEX idx_audit_events_case_time ON audit_events (case_id, event_time_utc);
CREATE INDEX idx_audit_events_entity ON audit_events (entity_type, entity_id);
CREATE INDEX idx_audit_events_action ON audit_events (action_type);

CREATE TABLE app_settings (
    key TEXT NOT NULL PRIMARY KEY,
    value_json TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
