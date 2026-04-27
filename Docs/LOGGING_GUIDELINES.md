# LOGGING_GUIDELINES.md

## Purpose

DumpLens must generate high-quality logs for debugging issues that slip past unit tests while protecting sensitive evidence.

Logs should help developers and investigators diagnose import failures, parsing issues, reconciliation problems, AI validation failures, report/export errors, and background job problems.

## Logging Principles

- Prefer structured logs over unstructured text.
- Include correlation IDs.
- Include case operation context without exposing sensitive evidence content.
- Log state transitions and summaries.
- Log warnings with actionable messages.
- Never log full message bodies by default.
- Never log raw evidence files.
- Never log secrets.
- Never log API keys, tokens, passwords, or private keys.
- Never send logs externally without explicit user action.

## Correlation IDs

Use correlation IDs for:

- App startup session.
- Case open session.
- Import run.
- Source registration.
- Background job.
- Reconciliation run.
- AI run.
- Report/export run.
- Audit operation.
- Error boundary.

Recommended field names:

```text
correlation_id
case_id
source_import_id
job_id
ai_run_id
report_id
conversation_id
operation
```

## Log Levels

### Trace

Use for very detailed local debugging. Disabled by default.

Examples:

- Detailed scoring component names without message body.
- Internal parser branch selection.
- Timing details for indexing batches.

### Debug

Use for development diagnostics.

Examples:

- Importer selected.
- Number of rows previewed.
- Candidate match count.
- Score distribution summary.
- Search index rebuild stage.

### Information

Use for normal important operations.

Examples:

- Case created.
- Source import registered.
- Import completed.
- Reconciliation run completed.
- Report exported.
- AI run completed.

### Warning

Use for recoverable problems requiring user/developer attention.

Examples:

- Timestamp could not be parsed.
- Missing sender column.
- Unknown timezone assumption.
- Potential duplicate source import.
- AI output failed schema validation.
- Reconciliation produced many ambiguous matches.

### Error

Use for failed operations.

Examples:

- Import failed.
- Migration failed.
- Case database could not open.
- Report export failed.
- AI provider request failed.
- Hash mismatch detected.

### Critical

Use for severe corruption/security issues.

Examples:

- Evidence hash mismatch.
- Audit hash chain verification failure.
- Case database integrity failure.
- Unauthorized access attempt in future team mode.

## Sensitive Data Rules

Do not log:

- Full message bodies.
- Full call transcripts.
- Full raw metadata JSON.
- Full file contents.
- Passwords.
- API keys.
- Provider tokens.
- Private keys.
- Complete access credentials.
- Full unredacted cloud AI request/response bodies.

Allowed with care:

- Message ID.
- Source import ID.
- Row number.
- Hash prefix or full SHA-256 where appropriate.
- Timestamp.
- Platform.
- Sender/recipient identity IDs.
- Counts and summaries.
- Redacted snippets when explicitly designed for safe diagnostics.

## Evidence-Safe Log Examples

Good:

```text
Import completed: source_import_id=src_123 rows=84211 warnings=19 duration_ms=53220 correlation_id=imp_456
```

Good:

```text
Timestamp parse warning: source_import_id=src_123 row=144 field=sent_at warning_code=unparseable_timestamp correlation_id=imp_456
```

Good:

```text
Reconciliation completed: case_id=case_1 conversations=34 matched_groups=482 missing_counterparts=12 ambiguous=9 duration_ms=11892 correlation_id=rec_789
```

Avoid:

```text
Missing message: "where he at? bring the switch" from 803-555-1111
```

Use instead:

```text
Missing counterpart candidate created: present_message_id=msg_1 present_source=src_victim missing_source=src_suspect confidence=medium correlation_id=rec_789
```

## Import Logging

Log:

- Source selected.
- Importer chosen.
- File hash computed.
- Source registration created.
- Preview row count.
- Mapping chosen.
- Validation warning counts.
- Import counts.
- Skipped rows.
- Failure reason.

Do not log raw rows or full message bodies.

## Reconciliation Logging

Log:

- Reconciliation run start/end.
- Candidate counts.
- Match score distribution.
- Number of matched groups.
- Number of source-only messages.
- Number of missing counterpart candidates.
- Number of gap windows.
- Ambiguous match count.
- Error details tied to IDs.

Do not log message body content by default.

## AI Logging

Log:

- Provider mode.
- Model name if available.
- Prompt template ID/version.
- Redaction enabled/disabled.
- Input scope summary.
- Output schema validation result.
- Token usage if available.
- Run status and error category.

Do not log full prompts or responses by default. Store AI outputs in the database according to the AI architecture and guardrails, not in application logs.

## Report/Export Logging

Log:

- Report type.
- Report ID.
- Export format.
- Output hash.
- Included item counts.
- Export duration.
- Export errors.

## Background Job Logging

Every job should log:

- Job queued.
- Job started.
- Meaningful progress checkpoints.
- Job completed.
- Job failed/canceled.
- Error category and safe details.

## Log Storage

Local-first default:

```text
case_folder/logs/app.log
case_folder/logs/audit.jsonl
case_folder/logs/ai_usage.jsonl
```

Do not upload logs externally automatically.

## Debug Packages

Future debug export packages should:

- Be user-initiated.
- Redact sensitive evidence content by default.
- Include app version, environment info, recent safe logs, job summaries, and error IDs.
- Exclude raw evidence unless explicitly selected by the user.
