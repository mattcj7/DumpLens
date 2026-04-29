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

`T0014` adds the communication schema migration:

```text
src/DumpLens.Persistence/Migrations/0003_communication_schema.sql
```

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

## T0014 Communication Schema

Migration file:

```text
src/DumpLens.Persistence/Migrations/0003_communication_schema.sql
```

Tables added:

- `persons`
- `identities`
- `identity_links`
- `devices`
- `platform_accounts`
- `conversations`
- `messages`
- `message_recipients`
- `conversation_participants`
- `calls`
- `attachments`

Indexes added:

- `idx_persons_case`
- `idx_persons_display_name`
- `idx_persons_role`
- `idx_identities_case`
- `idx_identities_type`
- `idx_identities_norm`
- `idx_identities_person`
- `idx_identity_links_case`
- `idx_identity_links_source`
- `idx_identity_links_status`
- `idx_devices_case`
- `idx_devices_owner`
- `idx_platform_accounts_case`
- `idx_platform_accounts_platform`
- `idx_platform_accounts_username`
- `idx_conversations_case`
- `idx_conversations_time`
- `idx_conversations_priority`
- `idx_messages_case_time`
- `idx_messages_source`
- `idx_messages_sender`
- `idx_messages_conversation`
- `idx_messages_body_hash`
- `idx_messages_deleted_status`
- `idx_messages_reconciliation_status`
- `idx_message_recipients_message`
- `idx_message_recipients_identity`
- `idx_conversation_participants_conv`
- `idx_conversation_participants_identity`
- `idx_calls_case_time`
- `idx_calls_caller`
- `idx_calls_callee`
- `idx_calls_source`
- `idx_attachments_case`
- `idx_attachments_message`
- `idx_attachments_hash`

Important relationships:

- `persons.case_id -> cases.id` with `ON DELETE CASCADE`
- `identities.case_id -> cases.id` with `ON DELETE CASCADE`
- `identities.linked_person_id -> persons.id`
- `identities.source_import_id -> source_imports.id`
- `identities.source_artifact_id -> source_artifacts.id`
- `identity_links.case_id -> cases.id` with `ON DELETE CASCADE`
- `identity_links.source_identity_id -> identities.id` with `ON DELETE CASCADE`
- `identity_links.target_identity_id -> identities.id` with `ON DELETE CASCADE`
- `identity_links.target_person_id -> persons.id` with `ON DELETE CASCADE`
- `identity_links.reviewed_by_user_id -> app_users.id`
- `devices.case_id -> cases.id` with `ON DELETE CASCADE`
- `devices.owner_person_id -> persons.id`
- `devices.phone_number_identity_id -> identities.id`
- `platform_accounts.case_id -> cases.id` with `ON DELETE CASCADE`
- `platform_accounts.linked_person_id -> persons.id`
- `platform_accounts.linked_phone_identity_id -> identities.id`
- `platform_accounts.linked_email_identity_id -> identities.id`
- `platform_accounts.source_import_id -> source_imports.id`
- `conversations.case_id -> cases.id` with `ON DELETE CASCADE`
- `messages.case_id -> cases.id` with `ON DELETE CASCADE`
- `messages.source_import_id -> source_imports.id` with `ON DELETE CASCADE`
- `messages.source_artifact_id -> source_artifacts.id`
- `messages.conversation_id -> conversations.id`
- `messages.sender_identity_id -> identities.id`
- `message_recipients.case_id -> cases.id` with `ON DELETE CASCADE`
- `message_recipients.message_id -> messages.id` with `ON DELETE CASCADE`
- `message_recipients.recipient_identity_id -> identities.id`
- `conversation_participants.case_id -> cases.id` with `ON DELETE CASCADE`
- `conversation_participants.conversation_id -> conversations.id` with `ON DELETE CASCADE`
- `conversation_participants.identity_id -> identities.id`
- `conversation_participants.person_id -> persons.id`
- `conversation_participants.source_import_id -> source_imports.id`
- `calls.case_id -> cases.id` with `ON DELETE CASCADE`
- `calls.source_import_id -> source_imports.id` with `ON DELETE CASCADE`
- `calls.source_artifact_id -> source_artifacts.id`
- `calls.caller_identity_id -> identities.id`
- `calls.callee_identity_id -> identities.id`
- `attachments.case_id -> cases.id` with `ON DELETE CASCADE`
- `attachments.source_import_id -> source_imports.id` with `ON DELETE CASCADE`
- `attachments.source_artifact_id -> source_artifacts.id`
- `attachments.linked_message_id -> messages.id` with `ON DELETE SET NULL`

Constraint notes:

- `identity_links` enforces `CHECK (target_identity_id IS NOT NULL OR target_person_id IS NOT NULL)`.
- `message_recipients` enforces `UNIQUE (message_id, recipient_identity_id, recipient_role)`.
- `conversation_participants` enforces `UNIQUE (conversation_id, identity_id)`.

Deferred constraints:

- `source_imports.owner_person_id`
- `source_imports.device_id`
- `source_imports.platform_account_id`

These three `source_imports` columns remain plain `TEXT` in `T0014`. SQLite cannot add normal foreign-key constraints to existing columns without rebuilding the table, and this ticket intentionally avoids a brittle `source_imports` table rebuild so `T0007` behavior remains stable. A later schema-hardening migration can add those constraints once that rebuild is intentionally designed and fully tested.

Tests added:

- `tests/DumpLens.Tests.Integration/Persistence/CommunicationSchemaMigrationTests.cs`

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
