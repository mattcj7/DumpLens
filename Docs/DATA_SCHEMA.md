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

This migration creates only `schema_migrations` so later schema tickets can be tracked safely. The full product schema remains out of scope until `T0007`.

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
