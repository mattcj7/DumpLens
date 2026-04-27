# AGENTS.md

## Purpose

This file gives instructions for AI coding agents and human contributors working on DumpLens.

DumpLens is an offline-first investigative communications reconstruction tool. It ingests and compares phone dumps, social media message exports, call logs, provider returns, screenshots/manual entries, transcripts, and related communications evidence. It reconstructs conversations across devices and platforms, detects possible missing-message gaps, builds timelines, and produces source-backed investigative leads and reports.

## Required Reading Before Any Work

Read these files before making changes:

```text
Docs/TICKETS.md
Docs/PROJECT_STRUCTURE.md
Docs/PROJECT_REFERENCES.md
Docs/TESTING.md
Docs/LOGGING_GUIDELINES.md
Docs/DECISIONS.md
Docs/SECURITY_GUARDRAILS.md
Docs/AI_GUARDRAILS.md
Docs/UI_Guidelines.md
Docs/DATA_SCHEMA.md
Docs/IMPORT_FORMATS.md
```

For architecture context, also read:

```text
Docs/TECHNICAL_ARCHITECTURE.md
Docs/DESIGN.md
```

## Ticket-First Workflow

Work from `Docs/TICKETS.md`.

Rules:

1. Work on one ticket at a time.
2. Do not implement features outside the active ticket.
3. Keep acceptance criteria visible while working.
4. Respect the ticket's `Out of Scope` section.
5. Update relevant docs when a ticket changes architecture, behavior, schema, tests, logging, or workflow.
6. Report changed files, tests run, and verification results when finished.

## Current Starting Ticket

The first coding ticket is:

```text
T0001 - Create Solution and Repository Structure
```

Do not start with UI screens, database schema, importers, reconciliation, AI, or reports.

## Architecture Boundaries

Keep these layers separate:

```text
DumpLens.Core
DumpLens.Application
DumpLens.Persistence
DumpLens.Ingestion
DumpLens.Normalization
DumpLens.Reconciliation
DumpLens.Analysis
DumpLens.AI
DumpLens.Search
DumpLens.Reporting
DumpLens.Security
DumpLens.Audit
DumpLens.App.ViewModels
DumpLens.App
DumpLens.Integration.CaseGraph
```

Follow:

```text
Docs/PROJECT_STRUCTURE.md
Docs/PROJECT_REFERENCES.md
```

Do not create circular project references. Do not place domain/business logic directly in UI views.

## Evidence Integrity Rules

- Original evidence files are immutable.
- Source imports must be hashed with SHA-256.
- Normalized records must link back to source artifacts.
- Derived data should be rebuildable when possible.
- Investigator review decisions, notes, manual links, audit events, and reports are not disposable cache.
- No feature may modify original evidence files.
- Temp files must be cleaned up safely.
- Exports must be hashed and audited when export functionality exists.

## AI Guardrails

- AI is a review assistant only.
- AI output must be structured and reviewable.
- AI findings must cite source artifacts.
- AI cannot establish probable cause.
- AI cannot silently approve findings.
- AI cannot label someone as guilty, a gang member, a co-conspirator, or a criminal associate without human-reviewed source support.
- Cloud AI must be optional, logged, and redaction-capable.
- No official report may include unsupported AI conclusions.

Follow:

```text
Docs/AI_GUARDRAILS.md
```

## UI Guardrails

- Use investigator-friendly, plain-language labels.
- Keep source references one click away.
- Use review-first workflows for findings, gaps, and leads.
- Do not overwhelm users with raw tables as the first screen.
- Use progressive disclosure for raw metadata, score breakdowns, hashes, and technical details.
- Use careful language for missing-message and deletion-gap analysis.
- Large lists must use virtualization.

Follow:

```text
Docs/UI_Guidelines.md
```

## Testing Requirements

Strong testing is mandatory throughout the build.

General rules:

- Add or update unit tests for domain logic, normalization, scoring, parsing, services, and guardrails.
- Add integration tests for database migrations, repositories, import pipelines, and report/export pipelines.
- Add golden-data tests for import/reconciliation behavior.
- Add performance tests for large message sets and long conversations where relevant.
- If a ticket changes behavior and no tests are added, explain why.

Minimum test expectations by area:

| Area | Required Test Type |
|---|---|
| Core domain rules | Unit tests |
| Normalization | Unit tests with edge cases |
| Timestamp parsing | Unit tests with timezone cases |
| Import probing/parsing | Golden-data tests |
| Persistence/migrations | Integration tests |
| Reconciliation scoring | Unit + golden-data tests |
| Missing counterpart/gap detection | Unit + golden-data tests |
| AI output validation | Unit tests with valid/invalid JSON |
| Redaction | Unit tests proving sensitive value replacement |
| Reporting/export | Integration tests verifying citations/hashes |
| Logging | Unit/integration checks for sensitive-data-safe logging paths where practical |

Follow:

```text
Docs/TESTING.md
Docs/QUALITY_GATES.md
```

## Logging and Debugging Requirements

DumpLens must produce useful logs for debugging failures that slip past tests.

Logging rules:

- Log meaningful operational events.
- Log job starts, progress, completion, cancellation, and failures.
- Log import warnings and parser errors.
- Log reconciliation run summaries and score/debug metadata without dumping full sensitive message bodies.
- Log AI provider mode, run status, redaction enabled/disabled, and schema-validation failures.
- Never log full message bodies, raw evidence files, unredacted PII, passwords, API keys, tokens, or secret values.
- Use correlation IDs for case operations, imports, jobs, AI runs, and exports.
- Prefer structured logging fields over string-only messages.

Follow:

```text
Docs/LOGGING_GUIDELINES.md
```

## Security Requirements

- Do not write secrets into source code or docs.
- Do not create telemetry that sends case data externally.
- Any future cloud service integration must be explicit, optional, and documented.
- Any future logs that may contain case-sensitive data must remain local unless the user explicitly exports them.

Follow:

```text
Docs/SECURITY_GUARDRAILS.md
```

## Coding Standards

- Use clear names and small focused classes.
- Prefer dependency injection for services.
- Keep interfaces in the appropriate layer.
- Use stable string values for persisted enums.
- Avoid broad catch-all error handling that hides failures.
- Avoid global mutable state.
- Prefer deterministic behavior for evidence processing.
- Add comments only when they clarify non-obvious intent or legal/evidentiary guardrails.

Follow:

```text
Docs/CODING_STANDARDS.md
```

## Completion Response Required

When done with a ticket, report:

```text
Ticket completed:
Changed files:
Tests run:
Build commands run:
Verification:
Known issues:
Assumptions:
Out-of-scope items intentionally not implemented:
```

If something fails, say exactly what failed and what remains.
