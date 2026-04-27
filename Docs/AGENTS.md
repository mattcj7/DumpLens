# AGENTS.md

## Purpose

This file gives instructions for AI coding agents and human contributors working on DumpLens.

DumpLens is an offline-first investigative communications reconstruction tool. It ingests and compares phone dumps, social media message exports, call logs, provider returns, screenshots/manual entries, transcripts, and related communications evidence. It reconstructs conversations across devices and platforms, detects possible missing-message gaps, builds timelines, and produces source-backed investigative leads and reports.

## Required Reading Before Any Work

Read in this order before making changes:

```text
1. Docs/TICKETS.md
2. Docs/PROJECT_STRUCTURE.md
3. Docs/PROJECT_REFERENCES.md
4. Docs/TESTING.md
5. Docs/LOGGING_GUIDELINES.md
6. Docs/QUALITY_GATES.md
7. Docs/DECISIONS.md
8. Docs/SECURITY_GUARDRAILS.md
9. Docs/AI_GUARDRAILS.md
10. Docs/UI_Guidelines.md
```

Read these when the active ticket touches their area, or when the ticket explicitly requires them:

```text
Docs/CODING_STANDARDS.md
Docs/DATA_SCHEMA.md
Docs/IMPORT_FORMATS.md
Docs/TECHNICAL_ARCHITECTURE.md
Docs/DESIGN.md
```

## Ticket-First Workflow

`Docs/TICKETS.md` controls the work.

Rules:

1. Work on one ticket at a time.
2. Treat the user-assigned ticket as active. If no ticket is assigned explicitly, use the current active ticket from `Docs/TICKETS.md`, not historical setup tickets by default.
3. Keep the ticket goal, requirements, acceptance criteria, and out-of-scope section visible while working.
4. Do not implement features, cleanup, refactors, or docs changes outside the active ticket unless the ticket explicitly requires them.
5. If the ticket is documentation-only, do not implement app features.
6. Update relevant docs when the active ticket changes architecture, behavior, schema, tests, logging, workflow, or guardrails.
7. Finish with the required completion report format in this file.

## Historical Context

`T0001 - Create Solution and Repository Structure` remains useful as historical foundation context, but it is not the default active ticket. Do not treat `T0001` as current work unless the user explicitly assigns it.

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

Do not create circular project references. Do not place domain/business logic directly in UI views. Do not bypass documented layer boundaries because a shortcut seems convenient.

## Evidence Integrity Rules

- Original evidence files are immutable.
- Source imports must be hashed with SHA-256.
- Normalized records must link back to source artifacts.
- Derived data should be rebuildable when possible.
- Investigator review decisions, notes, manual links, audit events, and reports are not disposable cache.
- No feature may modify original evidence files.
- Temp files must be cleaned up safely.
- Exports must be hashed and audited when export functionality exists.
- Never mutate, overwrite, normalize in place, or silently discard original evidence.

## AI Guardrails

- AI is a review assistant only.
- AI output must be structured and reviewable.
- AI findings must cite source artifacts.
- AI cannot establish probable cause.
- AI cannot silently approve findings.
- AI cannot label someone as guilty, a gang member, a co-conspirator, or a criminal associate without human-reviewed source support.
- Cloud AI must be optional, logged, and redaction-capable.
- No official report may include unsupported AI conclusions.
- Do not add unsupported legal or evidentiary conclusions anywhere in the product or docs.

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

Strong testing is mandatory throughout the build for behavior-heavy changes.

General rules:

- Add or update unit tests for domain logic, normalization, scoring, parsing, services, and guardrails.
- Add integration tests for database migrations, repositories, import pipelines, and report/export pipelines.
- Add golden-data tests for import/reconciliation behavior.
- Add performance tests for large message sets and long conversations where relevant.
- If a ticket changes behavior and no tests are added, explain why in the completion report.
- If a ticket is docs-only or otherwise cannot justify tests, say that explicitly.

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
- Evidence-safe logging is required for operational or behavior-heavy changes where diagnostics matter.

Follow:

```text
Docs/LOGGING_GUIDELINES.md
```

## Security Requirements

- Do not write secrets into source code or docs.
- Do not create telemetry that sends case data externally.
- Any future cloud service integration must be explicit, optional, and documented.
- Any future logs that may contain case-sensitive data must remain local unless the user explicitly exports them.
- Do not weaken chain-of-custody, hashing, auditability, or local-first evidence protections.

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

## Scope Control

- No scope creep.
- Do not implement app features, schemas, UI screens, importers, AI, reconciliation, reports, or other product work unless the active ticket explicitly requires them.
- Do not change `Docs/PROJECT_REFERENCES.md` or architecture direction unless the active ticket requires it and `Docs/DECISIONS.md` is updated accordingly.
- Do not invent requirements that are not stated in the active ticket or governing docs.

## Completion Report Required

When done with a ticket, report:

```text
Ticket:
Status:
Changed files:
Summary:
Tests added/updated:
Tests run:
Build commands run:
Logging added/updated:
Docs updated:
Verification:
Known issues:
Assumptions:
Out-of-scope items intentionally not implemented:
```

If something fails, say exactly what failed and what remains.
