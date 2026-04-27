# LOGGING_GUIDELINES.md

## Purpose

Defines the operational logging standard for DumpLens.

DumpLens logs must help developers and investigators diagnose failures without leaking sensitive evidence or weakening chain-of-custody.

## Core Logging Position

DumpLens requires evidence-safe structured logging.

Logs must:

- help explain what happened
- help locate the affected case, import, artifact, job, or AI run
- help reproduce failures with synthetic data
- avoid exposing sensitive evidence content
- complement, not replace, audit logging

Operational logging and audit logging serve different purposes:

- operational logs explain runtime behavior and failures
- audit logs record durable security- and review-relevant actions

## Required Logging Principles

- Prefer structured fields over free-text-only messages.
- Include correlation IDs for major operations.
- Keep source artifact traceability visible through safe identifiers.
- Log state transitions, counts, warnings, durations, and outcome summaries.
- Preserve enough detail to debug importer, normalization, reconciliation, AI, export, and storage failures.
- Never log sensitive evidence content unless an explicitly designed redacted field allows it.

## Correlation IDs

Use correlation IDs for major operations including:

- app startup session
- case open session
- source registration
- import run
- background job
- reconciliation run
- AI run
- report or export run
- audit-sensitive operation
- error boundary

Recommended fields:

```text
correlation_id
operation
case_id
source_import_id
source_artifact_id
job_id
ai_run_id
report_id
conversation_id
```

Rules:

- A major operation must keep the same correlation ID across its child log events.
- Child steps may add more specific IDs, but should not lose the parent correlation ID.
- If a failure crosses layers, the correlation ID must remain visible through the emitted logs.

## Sensitive Data Rules

Never log by default:

- full message bodies
- full call transcripts
- full screenshots or OCR text
- raw evidence files
- full raw metadata JSON blobs
- passwords
- API keys
- provider tokens
- private keys
- complete credentials
- full cloud AI prompts or responses
- unredacted PII or case narrative text

Allowed with care:

- stable IDs
- row numbers
- thread IDs
- timestamp values
- file sizes
- platform/source type
- counts and summaries
- full SHA-256 hashes in authoritative stores
- SHA-256 prefixes in operational logs when enough
- redacted snippets intentionally designed for diagnostics

When in doubt, log the identifier, not the content.

## Evidence-Safe Structured Fields

Preferred fields where relevant:

- `operation`
- `correlation_id`
- `case_id`
- `source_import_id`
- `source_artifact_id`
- `message_id`
- `row_number`
- `warning_code`
- `error_code`
- `duration_ms`
- `records_processed`
- `records_skipped`
- `matched_groups`
- `ambiguous_count`
- `redaction_enabled`
- `provider_mode`

Avoid burying all context in a single interpolated string.

## Log Levels

### Trace

Use for deep local diagnostics that are disabled by default.

Acceptable examples:

- parser branch selection
- batch timing detail
- score component breakdown without message body content

### Debug

Use for development diagnostics and safe internal detail.

Acceptable examples:

- importer selected
- candidate match counts
- normalization branch chosen
- index rebuild stage

### Information

Use for major successful operations.

Acceptable examples:

- case created
- source registered
- import completed
- reconciliation completed
- AI run completed
- report exported

### Warning

Use for recoverable problems, guardrail triggers, or data-quality issues.

Acceptable examples:

- timestamp parse failure on a row
- ambiguous match rate above threshold
- source hash changed in reference mode
- AI output failed schema validation

### Error

Use for failed operations.

Acceptable examples:

- migration failed
- import failed
- report export failed
- case database could not open
- hash verification failed

### Critical

Use for severe integrity or security failures.

Acceptable examples:

- evidence hash mismatch
- audit chain verification failure
- case integrity failure
- future unauthorized access attempt

## Audit Logging Relationship

Audit-relevant operations should emit:

- operational logs for runtime diagnostics
- audit events for durable security or review history

Examples that require audit history in addition to logs:

- source registration
- review-state changes
- manual merge/split decisions
- AI approval or rejection
- export generation
- integrity warning acknowledgement

Do not rely on ephemeral app logs as the only record of these actions.

## Import Logging

Log:

- source type selected
- importer selected
- source registration created
- SHA-256 computation status
- preview row count
- mapping or probe result
- validation warning counts
- imported record counts
- skipped row counts
- duration
- failure category

Do not log:

- raw rows
- full message bodies
- full attachment contents

Good example:

```text
Import completed operation=import correlation_id=imp_456 case_id=case_1 source_import_id=src_123 source_artifact_id=art_008 rows_imported=84211 warnings=19 duration_ms=53220
```

## Reconciliation Logging

Log:

- run start and completion
- candidate counts
- score distribution summaries
- matched group counts
- source-only counts
- possible missing-counterpart counts
- possible gap-window counts
- ambiguous match counts
- failure category and safe IDs

Do not log message body content by default.

Good example:

```text
Reconciliation completed operation=reconciliation correlation_id=rec_789 case_id=case_1 matched_groups=482 missing_counterparts=12 ambiguous=9 duration_ms=11892
```

## AI Logging

Log:

- provider mode
- model name if available
- prompt template ID/version
- redaction enabled/disabled
- input scope summary
- output schema validation result
- token usage if available
- run status
- error category

Do not log:

- full prompts
- full responses
- unredacted rehydration manifests

Good example:

```text
AI run completed operation=ai_run correlation_id=ai_204 case_id=case_1 ai_run_id=run_17 provider_mode=cloud redaction_enabled=true schema_valid=true findings_returned=4
```

## Report and Export Logging

Log:

- report type
- report ID
- export format
- included item counts
- output SHA-256 status
- duration
- export failure category

Reports and exports must remain traceable to cited source artifacts.

## Background Job Logging

Every job should log:

- queued
- started
- meaningful progress checkpoints
- completed
- canceled or failed
- safe error details and IDs

## Debugging Support Expectations

A developer reading logs should be able to answer:

- What operation ran?
- Which case, import, artifact, job, or AI run was involved?
- What stage failed?
- How many records were processed?
- Was redaction enabled?
- What should be checked next?

If logs cannot answer these questions without exposing sensitive content, improve the logging design.

## Storage and Retention Direction

Local-first default:

```text
case_folder/logs/app.log
case_folder/logs/audit.jsonl
case_folder/logs/ai_usage.jsonl
```

Rules:

- keep logs local by default
- do not automatically upload logs
- keep debug exports user-initiated
- redact sensitive content by default in any debug package

## Bad Examples

Bad:

```text
Missing message: "where he at? bring the switch" from 803-555-1111
```

Bad:

```text
Prompt sent to provider: [full prompt with raw conversation]
```

Use instead:

```text
Possible missing counterpart created operation=reconciliation correlation_id=rec_789 present_message_id=msg_1 present_source=src_victim missing_source=src_suspect confidence=medium
```
