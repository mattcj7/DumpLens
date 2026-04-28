# DATA_SCHEMA.md

## Purpose

Tracks the DumpLens database/data model. The technical architecture is the source of truth until migrations are implemented.

## Schema Rules

- Use stable string values for persisted enums.
- Use UUID strings for IDs unless a future decision changes this.
- Use UTC for normalized event timestamps.
- Preserve original timestamp strings separately.
- Preserve raw metadata in JSON fields where appropriate.
- Link normalized records to source artifacts.
- Update this file when migrations are added or changed.

## Primary Schema Areas

```text
cases
app_users
case_users
source_imports
source_artifacts
import_mappings
import_warnings
persons
identities
identity_links
devices
platform_accounts
messages
message_recipients
conversations
conversation_participants
calls
attachments
reconciled_message_groups
reconciled_message_members
missing_counterparts
gap_windows
timeline_events
timeline_event_links
findings
finding_support
leads
lead_support
ai_runs
ai_outputs
ai_output_support
prompt_templates
message_fts
search_index_jobs
embeddings
reports
report_items
audit_events
schema_migrations
app_settings
jobs
```

## Current Migration Status

`T0006` introduces the SQLite migration runner and this bootstrap migration:

```text
src/DumpLens.Persistence/Migrations/0001_bootstrap_schema_migrations_support.sql
```

This migration creates only `schema_migrations` so later schema tickets can be tracked safely.

`T0007` adds the initial core schema migration:

```text
src/DumpLens.Persistence/Migrations/0002_initial_core_schema.sql
```

The migration runner enables SQLite foreign-key enforcement on its migration connection before applying ordered scripts.

Initial planned tickets:

```text
T0006 - Implement Case Database Migration System
T0007 - Implement Initial Core Schema Migration
T0014 - Implement Communication Schema Migration
T0031 - Implement Reconciliation Schema Migration
T0040 - Implement Timeline Schema Migration
T0046 - Implement Leads Schema Migration
T0049 - Implement AI Schema Migration
T0056 - Implement Reporting Schema Migration
```

## T0007 Core Schema

Migration file:

```text
src/DumpLens.Persistence/Migrations/0002_initial_core_schema.sql
```

Tables added:

- `cases`
- `app_users`
- `case_users`
- `source_imports`
- `source_artifacts`
- `import_mappings`
- `import_warnings`
- `audit_events`
- `app_settings`

Indexes added:

- `idx_cases_case_number`
- `idx_cases_status`
- `idx_source_imports_case`
- `idx_source_imports_hash`
- `idx_source_imports_type`
- `idx_source_artifacts_source`
- `idx_source_artifacts_case`
- `idx_source_artifacts_provider_id`
- `idx_import_warnings_source`
- `idx_import_warnings_status`
- `idx_audit_events_case_time`
- `idx_audit_events_entity`
- `idx_audit_events_action`

Important relationships:

- `case_users.case_id -> cases.id` with `ON DELETE CASCADE`
- `case_users.user_id -> app_users.id`
- `source_imports.case_id -> cases.id` with `ON DELETE CASCADE`
- `source_imports.imported_by_user_id -> app_users.id`
- `source_artifacts.case_id -> cases.id` with `ON DELETE CASCADE`
- `source_artifacts.source_import_id -> source_imports.id` with `ON DELETE CASCADE`
- `import_mappings.case_id -> cases.id` with `ON DELETE CASCADE`
- `import_mappings.source_import_id -> source_imports.id` with `ON DELETE CASCADE`
- `import_mappings.created_by_user_id -> app_users.id`
- `import_warnings.case_id -> cases.id` with `ON DELETE CASCADE`
- `import_warnings.source_import_id -> source_imports.id` with `ON DELETE CASCADE`
- `import_warnings.artifact_id -> source_artifacts.id`
- `import_warnings.resolved_by_user_id -> app_users.id`
- `audit_events.case_id -> cases.id` with `ON DELETE CASCADE`
- `audit_events.user_id -> app_users.id`

Deferred constraints:

- `source_imports.owner_person_id`
- `source_imports.device_id`
- `source_imports.platform_account_id`

These remain plain `TEXT` columns in `T0007`. Their foreign-key constraints are deferred until `T0014` adds the communication-schema tables they will reference.

Tests added:

- `tests/DumpLens.Tests.Integration/Persistence/InitialCoreSchemaMigrationTests.cs`

## T0008 Case Package Manifest

Local case storage now includes a package manifest at:

```text
manifest.json
```

The expected local case database path inside each package is:

```text
case.dlensdb
```

Current manifest fields:

- `package_version`
- `package_id`
- `case_id`
- `case_number` when supplied
- `title` when supplied
- `created_at_utc`
- `app_name`
- `database_relative_path`
- `preparation_mode`
- `folders`

Standard package folders:

- `imports`
- `indexes`
- `attachments`
- `attachments/thumbnails`
- `attachments/extracted_text`
- `attachments/media_cache`
- `reports`
- `exports`
- `logs`
- `backups`

Tests added:

- `tests/DumpLens.Tests.Unit/Core/Storage/SafePathNameTests.cs`
- `tests/DumpLens.Tests.Integration/CasePackages/CasePackageServiceTests.cs`

## Enum Families

```text
source_type
platform
review_status
confidence_label
deleted_status
reconciliation_status
finding_type
lead_type
job_status
audit_action_type
```

## Documentation Requirement

Every schema ticket must update this file with:

- Tables added.
- Indexes added.
- Important relationships.
- Enum changes.
- Migration file name.
- Tests added.
