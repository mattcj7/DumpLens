# SECURITY_GUARDRAILS.md

## Purpose

Defines DumpLens-specific security, privacy, evidence-integrity, and chain-of-custody requirements for handling investigative communications evidence.

These guardrails apply to imports, normalization, storage, search, AI review, reporting, logging, debugging, and exports.

## Core Security Position

DumpLens is a desktop-first, local-first evidence application.

Security decisions must preserve:

- Original evidence immutability.
- SHA-256-based artifact verification.
- Source artifact traceability.
- Auditability of investigator and system actions.
- Least-necessary exposure of sensitive data.
- Explicit human review for investigative conclusions.

If a proposed implementation improves convenience while weakening any of the above, reject it.

## Original Evidence Immutability

Required rules:

- Original evidence files are immutable after intake.
- DumpLens must never modify, normalize in place, rename in place, redact in place, or overwrite original evidence.
- Any working copy, derived export, normalized record, cache, preview, thumbnail, transcript segment, or AI input package must be created separately from the original artifact.
- If reference mode is used instead of copy mode, DumpLens must retain enough metadata to detect path changes, size changes, and SHA-256 hash changes.
- If evidence cannot be safely handled without mutation, the feature is not acceptable.

Minimum metadata to retain for each imported artifact:

- `source_import_id`
- `source_artifact_id`
- original filename
- stored or referenced path
- byte length
- SHA-256 hash
- acquisition/import timestamp
- source platform or source type

## SHA-256 Hashing Requirements

SHA-256 is the required content hash for DumpLens evidence integrity work.

DumpLens must compute SHA-256 for:

- original imported files
- extracted attachments stored as separate artifacts
- generated export files
- debug/export bundles when implemented
- audit chain payloads when hash chaining is implemented

Implementation rules:

- Stream large files; do not require full file loads into memory.
- Store the full SHA-256 hash, not only a prefix.
- A hash prefix may appear in operational logs for readability, but the full SHA-256 must remain available in authoritative storage.
- Do not use MD5 or SHA-1 as the authoritative integrity check.
- Re-hash when validating evidence integrity after copy, restore, export, or suspected tampering.

Example:

```text
source_artifact_id=art_sms_001 sha256=8b7f4f5f...<full 64 hex chars in storage>
```

## Source Artifact Traceability

Every normalized, reconciled, AI-assisted, timeline, lead, and reportable item must be traceable back to its source artifacts.

Required traceability behaviors:

- Normalized records must reference the source artifact that produced them.
- Import warnings must preserve artifact and row/object locator context.
- Reconciliation outputs must reference all contributing source artifacts.
- Timeline items and leads must preserve the artifact or artifacts supporting them.
- Reports must cite source artifacts, not only derived record IDs.
- AI findings may not be treated as evidentiary findings unless they cite supporting source artifacts or source record locators.

Examples of acceptable locators:

- CSV row number
- XLSX sheet name and row number
- message export thread ID and message ID
- call log row/object index
- screenshot artifact ID plus investigator-selected region reference when implemented
- transcript segment or line locator

## Audit Logging Requirements

Security-relevant actions must be auditable even when operational logs rotate.

Audit events are required for:

- case creation and case settings changes
- source registration
- evidence hash creation or verification
- import completion, failure, and cancellation
- manual review decisions
- manual identity merge/split and exclusion actions
- reconciliation review outcomes
- AI run creation, review, approval, rejection, and report inclusion
- report and export generation
- integrity warning acknowledgement
- security-sensitive configuration changes

Audit event expectations:

- record who or what initiated the action
- record when it happened
- record the target case and relevant artifact/import IDs
- record the event type and safe summary
- record before/after values for review-state or configuration changes where practical
- support hash chaining when the audit subsystem is implemented

Operational logs do not replace audit logs.

## Local-Only and Cloud Boundary

Default posture:

- Keep evidence, logs, and derived artifacts local.
- Do not auto-upload case data, logs, prompts, artifacts, or exports.
- Any future external transmission must be explicit, user-visible, and documented.

Cloud AI controls:

- Cloud AI must be optional.
- Cloud AI must be disableable globally and per case when those settings exist.
- Cloud AI must support a redaction-capable path before sensitive evidence leaves the machine.
- Cloud AI usage must be logged and auditable.
- The product must preserve whether a result was local-AI-assisted, cloud-AI-assisted, or human-only.

## Sensitive Data Handling

Treat as sensitive by default:

- message bodies
- attachments
- screenshots
- transcripts
- phone numbers
- email addresses
- social handles
- contact names
- addresses and locations
- device identifiers
- provider account numbers
- credentials, tokens, and keys

Rules:

- Do not expose sensitive evidence in logs except through explicitly designed redacted fields.
- Do not place real evidence in test fixtures, bug reports, docs, or screenshots.
- Do not store secrets in source control, sample configs, or documentation.
- Use synthetic fixture data for tests and examples.

## Temporary Files and Derived Artifacts

Required rules:

- Use controlled temp locations.
- Minimize raw evidence writes to temp storage.
- Clean up temporary files after successful completion or controlled failure.
- Do not leave stray evidence copies in user temp folders after normal operation.
- If temp files are needed for parsing or export, they must still preserve traceability and respect logging redaction rules.
- Derived artifacts must be distinguishable from originals.

## Access and Permissions Direction

Planned role direction:

| Role | Expected Security Boundary |
|---|---|
| Admin | Manage app settings, users, AI provider settings, and export controls |
| Investigator | Import evidence, review findings, approve conclusions, export reports |
| Analyst | Analyze evidence and create draft leads without bypassing review controls |
| Reviewer | Approve or reject findings and reports without changing source evidence |
| Read-only | View case data without mutating evidence or approval state |

Until team mode exists, do not design features that assume invisible privilege escalation or hidden background approval behavior.

## Security Review Questions For New Features

Before accepting a feature, confirm:

- Does it preserve original evidence immutability?
- Does it compute or preserve the required SHA-256 hashes?
- Can every derived item be traced to source artifacts?
- Are security-relevant actions auditable?
- Does it avoid leaking sensitive evidence in logs or debug output?
- Does it keep external transmission explicit and optional?
- If cloud AI is involved, is redaction-capable mode available?

If any answer is no or unknown, the feature is not ready.

## Good vs Bad Examples

Good:

```text
Import stored original file as read-only, computed SHA-256, and linked 842 normalized messages to source_artifact_id=art_0021.
```

Good:

```text
Report cites source_artifact_id=art_0044 and message_locator=thread-7/msg-193 for each exported finding.
```

Bad:

```text
Importer fixed malformed timestamps by editing the original CSV in place.
```

Bad:

```text
Cloud AI received unredacted message bodies automatically because redaction support was not ready.
```

## Prohibited Behavior

- Mutating original evidence.
- Using a weaker hash as the authoritative integrity check.
- Creating derived records that cannot be traced to source artifacts.
- Writing unsupported AI conclusions into official findings or reports.
- Sending case data externally without explicit user action.
- Logging secrets or raw evidence content by default.
- Treating operational logs as a substitute for audit history.
