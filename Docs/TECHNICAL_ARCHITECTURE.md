# TECHNICAL_ARCHITECTURE.md

## Purpose

Technical architecture summary for DumpLens.

## Deployment Shape

- Desktop-first local application.
- Offline-first.
- Optional future agency/team server mode.
- Local case package with database, imports, indexes, reports, logs, and backups.

## Recommended Stack

| Layer | Recommended Tech |
|---|---|
| Desktop UI | Avalonia UI or WPF |
| Core application | .NET 8/9+ |
| Local database | SQLite + optional SQLCipher/encrypted container |
| Analytical cache | DuckDB |
| Full-text search | SQLite FTS5 initially |
| AI provider | Pluggable local/cloud provider interface |
| Reports | PDF/DOCX/CSV export library |
| Background jobs | Local database-backed jobs |
| Testing | xUnit/NUnit + golden data tests |
| Logging | Structured local logs with correlation IDs |

## Main Layers

```text
Presentation
Application
Domain/Core
Ingestion
Normalization
Reconciliation
Analysis
AI
Search
Persistence
Reporting
Security
Audit
Integration
```

## Project Structure

Follow:

```text
Docs/PROJECT_STRUCTURE.md
Docs/PROJECT_REFERENCES.md
```

## Key Architecture Rules

- Original evidence is immutable.
- SQLite is authoritative local case database.
- DuckDB is optional rebuildable analytical cache.
- Normalized data must link back to source artifacts.
- AI is optional and review-only.
- Findings require source support.
- Logs must be useful and evidence-safe.

## Case Creation Workflow

Application-facing case creation is exposed through `DumpLens.Application.Cases.ICaseService`.
The SQLite-backed implementation in `DumpLens.Persistence.Cases` orchestrates:

1. validate the request before filesystem or database writes
2. create the case package through the case package service
3. create and migrate the package database at `case.dlensdb`
4. insert the row in `cases`
5. write the `case_created` audit event through the audit logger
6. return case/package/database/manifest/audit metadata

Case creation logs operational stages with correlation IDs and safe identifiers only.

## Source Registration Workflow

Application-facing source registration is exposed through `DumpLens.Application.Sources.ISourceRegistrationService`.
The SQLite/filesystem-backed implementation in `DumpLens.Persistence.Sources` orchestrates:

1. validate the source registration request and normalize safe identifiers/paths
2. verify the existing case package root, case database path, and selected source file
3. confirm the target `case_id` exists in the case database
4. create a unique source import folder under `imports/source_<id>/`
5. copy the selected source file into `original/<safe-filename>` without mutating the original
6. compute SHA-256 for the original file and copied file through the injected `IFileHashService`
7. verify the copied-file hash matches the original-file hash
8. write `sha256.txt` and `manifest.json` into the source folder
9. insert the `source_imports` row with safe metadata and zero record/warning counts
10. write the `source_registered` audit event through the audit logger
11. return source import, file, manifest, hash, and audit metadata

This workflow intentionally does not parse the source file, create `source_artifacts`, or persist normalized messages/calls yet.
Operational logs for source registration use correlation IDs, case/source import IDs, hash prefixes, extensions, counts, and stage names only.

## Message Import Workflow

Application-facing message import persistence is exposed through `DumpLens.Application.MessageImports.IMessageImportService`.
The SQLite-backed implementation in `DumpLens.Persistence.MessageImports` orchestrates:

1. validate the case database path, source import ID, requested source kind, field mappings, and safe correlation ID
2. load the existing `source_imports` row created during source registration and confirm it belongs to the target case
3. resolve the registered copied source file path, or use the caller-provided registered source file path when supplied
4. read full tabular rows from the CSV/XLSX importer through application-facing import contracts
5. create one `source_artifacts` row per imported source row with stable row or worksheet-row locator context and row metadata JSON
6. normalize sender and recipient identities through `IIdentityNormalizer`, then create or reuse `identities` records deterministically
7. normalize timestamps through `ITimestampNormalizer`, preserving the original timestamp string and the normalized UTC value separately
8. persist `messages` and `message_recipients` rows in batches while keeping `conversation_id` null for now
9. persist row-level and import-level `import_warnings` without logging raw evidence content
10. update `source_imports.record_count`, `source_imports.warning_count`, `source_imports.updated_at_utc`, and the import status
11. write a `messages_imported` audit event after persistence commits successfully
12. return safe import counts and audit metadata to the caller

This workflow intentionally does not register or copy source files again, persist call logs, build conversations, wire the WPF completion flow, create search indexes, or perform reconciliation.
Operational logs for message import use correlation IDs, case/source import IDs, batch kinds, row counts, warning counts, and stage names only. They do not log message bodies, phone numbers, emails, handles, raw rows, or free-form evidence text.

## Call Import Workflow

Application-facing call import persistence is exposed through `DumpLens.Application.CallImports.ICallImportService`.
The SQLite-backed implementation in `DumpLens.Persistence.CallImports` orchestrates:

1. validate the case database path, source import ID, requested source kind, field mappings, and safe correlation ID
2. load the existing `source_imports` row created during source registration and confirm it belongs to the target case
3. resolve the registered copied source file path, or use the caller-provided registered source file path when supplied
4. read full tabular rows from the CSV/XLSX importer through application-facing import contracts
5. create one `source_artifacts` row per imported source row with stable row or worksheet-row locator context and row metadata JSON
6. normalize caller and callee identities through `IIdentityNormalizer`, then create or reuse `identities` records deterministically
7. normalize timestamps through `ITimestampNormalizer`, preserving the original timestamp string and the normalized UTC value separately
8. parse supported duration shapes into `duration_seconds` while preserving the raw mapped values in metadata and warnings
9. persist `calls` rows in batches without creating conversations, messages, or reconciliation records
10. persist row-level and import-level `import_warnings` without logging raw evidence content
11. update `source_imports.record_count`, `source_imports.warning_count`, `source_imports.updated_at_utc`, and the import status
12. write a `calls_imported` audit event after persistence commits successfully
13. return safe import counts and audit metadata to the caller

This workflow intentionally does not register or copy source files again, persist messages, wire the WPF completion flow, build conversations, create search indexes, or perform reconciliation.
Operational logs for call import use correlation IDs, case/source import IDs, batch kinds, row counts, warning counts, and stage names only. They do not log phone numbers, names, raw rows, call notes, or source file contents.

## Conversation Build Workflow

Application-facing conversation building is exposed through `DumpLens.Application.Conversations.IConversationBuilderService`.
The SQLite-backed implementation in `DumpLens.Persistence.Conversations` orchestrates:

1. validate the case database path, target case ID, optional source import scope, rebuild flag, and safe correlation ID
2. confirm the target case exists and validate the optional `source_import_id` scope against that case
3. load existing conversation rows so stable thread-key and participant-key matches can be reused instead of duplicated
4. load candidate messages for the case or scoped source import, using only unassigned messages for refresh runs unless `rebuild_existing=true`
5. group messages deterministically by `platform + source_thread_id` when a thread ID exists, otherwise by `platform + normalized participant identity set`
6. create new conversation rows only when no stable existing match is available
7. update `messages.conversation_id` for changed assignments only
8. recompute conversation metadata including safe generic title, platform, normalized participant key, thread-key JSON, start/end timestamps, message count, and source count
9. sync `conversation_participants` from assigned sender/recipient identities without creating persons or merging identities
10. return safe build counts and conversation summaries for the caller

This workflow intentionally does not add conversation UI, search indexing, reconciliation, deletion-gap analysis, review controls, person creation, or identity merging.
Operational logs for conversation building use correlation IDs, case/source import IDs, counts, rebuild scope, and stage names only. They do not log message bodies, names, phone numbers, emails, handles, raw rows, or source file contents.

## Message Search Workflow

Application-facing message search indexing is exposed through `DumpLens.Application.Search.IMessageSearchIndexService`.
The SQLite-backed implementation in `DumpLens.Persistence.Search` orchestrates:

1. validate the case database path, target case ID, optional result limit, and safe correlation ID
2. rebuild the case-scoped FTS index from canonical `messages` rows when requested
3. clear prior search rows for the target case before re-inserting them so rebuild stays deterministic and idempotent
4. store searchable text from `message_body`, `platform`, `direction`, and `deleted_status`
5. preserve unindexed traceability fields including `message_id`, `conversation_id`, `source_import_id`, `source_artifact_id`, `provider_message_id`, `source_thread_id`, and `event_time_utc`
6. sanitize user query text into safe FTS phrase terms before execution
7. return case-scoped result rows with source-linked references, safe snippets, and rank metadata when available
8. return safe validation responses for empty or unsupported query shapes instead of raw SQLite syntax errors

This workflow intentionally does not add search UI, source-reference inspector UI, call search expansion, reconciliation analysis, AI summaries, or report/export behavior.
Operational logs for message search use correlation IDs, case IDs, counts, duration, and failure types only. They do not log search terms, message bodies, snippets, names, phone numbers, emails, handles, raw row contents, raw metadata JSON, or source file contents.
