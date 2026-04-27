# DEBUGGING.md

## Purpose

Defines how to debug DumpLens safely.

Debugging must preserve evidence integrity, avoid sensitive-data leakage, and produce fixes backed by tests and evidence-safe diagnostics.

## Core Debugging Position

When a defect appears, do not trade chain-of-custody for speed.

Debugging must:

- preserve original evidence immutability
- rely on correlation IDs, safe logs, audit history, and source traceability
- reproduce issues with synthetic fixtures whenever possible
- add tests for regressions before or alongside the fix

## Required Debugging Workflow

1. Identify the failing operation, ticket scope, and correlation ID.
2. Determine whether the issue involves evidence integrity, traceability, or review-state risk.
3. Check operational logs for stage, warning, counts, and failure details.
4. Check audit events when the issue involves imports, approvals, exports, AI review, or security-sensitive actions.
5. Trace the affected records back to their `source_artifact_id`, `source_import_id`, and source locator if applicable.
6. Reproduce the issue with synthetic fixtures, temporary files, or isolated integration setup.
7. Add a failing unit, integration, or golden-data test that demonstrates the bug.
8. Implement the fix.
9. Re-run the relevant tests and baseline verification commands.
10. Confirm logs remain evidence-safe and sufficient for future diagnosis.

## Evidence-Safe Debugging Rules

- Do not edit original evidence to make a bug easier to reproduce.
- Do not paste real evidence into bug reports, docs, commits, tests, or chat.
- Do not attach raw evidence files to debug bundles by default.
- Do not enable verbose logging that dumps sensitive content unless it is explicitly redacted and still policy-compliant.
- Prefer identifiers, hashes, counts, and synthetic examples over content dumps.

## What To Check First

For any major failure, answer:

- What operation ran?
- Which correlation ID is associated with it?
- Which case, import, artifact, job, AI run, or report was involved?
- Did the failure occur before or after SHA-256 hashing, source registration, normalization, review-state change, or export?
- Can the affected derived item still be traced to its source artifact?
- Is there an audit event that confirms the user or system action?

## Common Debug Areas

### Import Problems

Check:

- source selection and importer choice
- source registration
- SHA-256 creation or verification
- row or object locator preservation
- mapping/probe decisions
- timestamp parsing warnings
- normalization warnings
- batch insert or persistence failures

### Evidence Integrity Problems

Check:

- original file path and read-only handling
- stored artifact metadata
- SHA-256 mismatch details
- reference-mode path or size changes
- audit history for registration and verification actions

### Reconciliation Problems

Check:

- conversation assignment
- candidate generation window
- normalized message-body comparison
- participant identity matching
- timestamp tolerance
- short/generic message penalties
- score breakdown visibility
- alternative explanation handling

### AI Problems

Check:

- provider mode
- prompt template ID/version
- redaction enabled/disabled
- structured output validation
- citation presence
- prohibited-language rejection
- review-state transitions

### Reporting and Export Problems

Check:

- report configuration
- included item review status
- source citations
- output hash generation
- export audit event
- evidence-safe export logging

## Synthetic Reproduction Guidance

Preferred reproduction methods:

- small synthetic CSV/XLSX fixtures
- temporary case folders
- temporary databases
- explicit row/message locators
- synthetic timestamps and timezone cases

If a bug originates from real evidence:

- describe the pattern safely
- reconstruct the pattern synthetically
- confirm the synthetic case still reproduces the defect

## Debug Logging Expectations

Every major operation should log enough safe context to answer:

- what ran
- which IDs were involved
- what stage failed
- how many records were processed
- whether redaction was enabled when applicable
- what the next likely diagnostic step is

If the current logs cannot answer these questions, improve the logging rather than reaching for raw evidence dumps.

## Regression Expectations

A debugging-driven fix is not complete unless:

- the root cause is understood well enough to explain
- a test now covers the failure mode where practical
- logs remain useful and evidence-safe
- the fix does not weaken immutability, SHA-256 hashing, traceability, or review controls

## Safe Bug Report Template

Use a report shape like:

```text
Operation: import
Correlation ID: imp_456
Case ID: case_1
Affected Source Artifact: art_008
Symptom: Timestamp parsing failed for 19 rows after importer selection.
Observed Safe Evidence: warning_code=unparseable_timestamp, row_numbers=144;145;146
Synthetic Repro: yes
Test Added: ImportParser_Rejects_UnparseableTimestamp_WithWarning
```

Avoid reports shaped like:

```text
These are the actual messages that failed to import: [real content pasted here]
```
