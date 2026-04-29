# TICKETS.md

## Purpose

Development ticket pack for DumpLens.

Build stance: full-product architecture delivered in controlled dependency order.

## Global Build Rules

### Evidence Integrity

- Original imports remain read-only.
- Imported files are hashed with SHA-256.
- Normalized records link back to source artifacts.
- Derived data should be rebuildable when possible.
- Investigator notes, review decisions, manual links, audit logs, and reports are not disposable cache.
- No feature may modify original evidence files.

### AI Guardrails

- AI output is review assistance only.
- AI findings must cite source artifacts.
- AI may suggest leads but may not establish probable cause.
- AI may not label someone as guilty, gang member, co-conspirator, or criminal associate without human-reviewed source support.
- AI outputs must be structured and reviewable.
- Cloud AI must be optional, logged, and redaction-capable.

### UI/UX

- Investigator-friendly, plain-language UI.
- Source references one click away.
- Review-first workflows.
- Progressive disclosure for technical details.
- Large lists must use virtualization.

### Architecture

Follow:

```text
Docs/PROJECT_STRUCTURE.md
Docs/PROJECT_REFERENCES.md
```

### Testing and Logging

Follow:

```text
Docs/TESTING.md
Docs/QUALITY_GATES.md
Docs/LOGGING_GUIDELINES.md
```

Every behavior-heavy ticket should include unit tests. Every operational feature should include evidence-safe diagnostic logging where useful.

## Ticket Template

```markdown
## T#### - Ticket Title

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** Foundation / Ingestion / UI / Reconciliation / AI / Reporting / Security  
**Dependencies:** T####, T####  

### Goal
Describe what this ticket accomplishes.

### Background
Explain why this exists and how it fits DumpLens.

### Requirements
- Requirement 1
- Requirement 2
- Requirement 3

### Acceptance Criteria
- [ ] Specific outcome
- [ ] Tests added/updated if applicable
- [ ] Evidence-safe logging added/updated if applicable
- [ ] Docs updated if applicable
- [ ] No out-of-scope work added

### Out of Scope
- Explicitly list what should not be implemented here.

### Notes for Codex / Agent
- Implementation guidance.
- Files likely involved.
- Guardrails.
```

## Build Milestones

1. Repo and architecture foundation.
2. Basic ingestion and normalization.
3. Conversation workspace.
4. Reconciliation core.
5. Timeline, gaps, and leads.
6. AI analysis layer.
7. Reports and exports.
8. Advanced importers and full product expansion.

---

# Milestone 1 — Repo and Architecture Foundation

## T0001 - Create Solution and Repository Structure

**Status:** Not Started  
**Priority:** Critical  
**Size:** Medium  
**Layer:** Foundation  
**Dependencies:** None  

### Goal

Create the initial DumpLens solution/repository structure using the agreed full-product modular architecture.

### Requirements

- Create `DumpLens.sln`.
- Create the source projects listed in `Docs/PROJECT_STRUCTURE.md`.
- Create the test projects listed in `Docs/PROJECT_STRUCTURE.md`.
- Create `tools/` folder with placeholder README.
- Add placeholder README files where useful.
- Add project references according to `Docs/PROJECT_REFERENCES.md`.
- Ensure solution builds.

### Acceptance Criteria

- [ ] Solution builds successfully.
- [ ] Project references follow `Docs/PROJECT_REFERENCES.md`.
- [ ] UI project does not contain business logic.
- [ ] Test projects compile.
- [ ] No product features are implemented beyond skeleton structure.

### Out of Scope

- Database schema.
- UI screens.
- Importers.
- AI.
- Reconciliation.
- Reports.

---

## T0002 - Add Documentation Scaffold Verification

**Status:** Not Started  
**Priority:** Critical  
**Size:** Small  
**Layer:** Documentation  
**Dependencies:** T0001  

### Goal

Verify all required documentation files exist under `Docs/`.

### Requirements

Required docs:

```text
Docs/DESIGN.md
Docs/TECHNICAL_ARCHITECTURE.md
Docs/TICKETS.md
Docs/DECISIONS.md
Docs/AGENTS.md
Docs/PROJECT_STRUCTURE.md
Docs/PROJECT_REFERENCES.md
Docs/UI_Guidelines.md
Docs/SECURITY_GUARDRAILS.md
Docs/AI_GUARDRAILS.md
Docs/DATA_SCHEMA.md
Docs/IMPORT_FORMATS.md
Docs/TESTING.md
Docs/QUALITY_GATES.md
Docs/LOGGING_GUIDELINES.md
Docs/CODING_STANDARDS.md
Docs/RECONCILIATION_GUIDELINES.md
Docs/REPORTING_GUIDELINES.md
Docs/DEBUGGING.md
Docs/CHANGELOG.md
```

### Acceptance Criteria

- [ ] Docs folder exists.
- [ ] Each required `.md` file exists.
- [ ] No conflicting guidance exists between docs.
- [ ] Root `AGENTS.md` points to `Docs/AGENTS.md`.

---

## T0003 - Write/Refine AGENTS.md Project Instructions

**Status:** Not Started  
**Priority:** Critical  
**Size:** Medium  
**Layer:** Documentation / Agent Workflow  
**Dependencies:** T0002  

### Goal

Ensure agent instructions are complete and strict enough for reliable Codex work.

### Acceptance Criteria

- [ ] Agent file requires ticket-first workflow.
- [ ] Agent file references project structure/reference docs.
- [ ] Agent file requires tests for behavior-heavy changes.
- [ ] Agent file requires evidence-safe logging.
- [ ] Agent file prevents unsupported AI/legal conclusions.
- [ ] Agent file prevents original evidence mutation.
- [ ] Agent file prevents scope creep.

---

## T0004 - Refine UI_Guidelines.md

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** Documentation / UI  
**Dependencies:** T0002  

### Acceptance Criteria

- [ ] Three-panel layout is defined.
- [ ] Plain-language labels are defined.
- [ ] Source reference behavior is defined.
- [ ] Gap/deletion warning language is defined.
- [ ] Accessibility and virtualization requirements are defined.

---

## T0005 - Refine Security, AI, Testing, and Logging Guardrails

**Status:** Not Started  
**Priority:** Critical  
**Size:** Medium  
**Layer:** Documentation / Security / AI / Testing / Logging  
**Dependencies:** T0002  

### Acceptance Criteria

- [ ] Security guardrails exist.
- [ ] AI guardrails exist.
- [ ] Testing requirements exist.
- [ ] Logging guidelines exist.
- [ ] Quality gates exist.

---

## T0006 - Implement Case Database Migration System

**Status:** Not Started  
**Priority:** Critical  
**Size:** Large  
**Layer:** Persistence  
**Dependencies:** T0001  

### Requirements

- Add migration runner in `DumpLens.Persistence`.
- Support ordered SQL migrations.
- Track applied migrations in `schema_migrations`.
- Store migration version and checksum.
- Fail safely on migration error.
- Add integration tests using a temporary database.
- Add evidence-safe logs for migration start/completion/failure.

### Acceptance Criteria

- [ ] New case database can be created.
- [ ] Migrations apply in order.
- [ ] Re-running migrations is safe.
- [ ] Migration checksum is stored.
- [ ] Integration test verifies migration application.
- [ ] Logs include safe migration diagnostics.

---

## T0007 - Implement Initial Core Schema Migration

**Status:** Not Started  
**Priority:** Critical  
**Size:** Large  
**Layer:** Persistence / Schema  
**Dependencies:** T0006  

### Requirements

Implement:

```text
cases
app_users
case_users
source_imports
source_artifacts
import_mappings
import_warnings
audit_events
schema_migrations
app_settings
```

### Acceptance Criteria

- [ ] Migration creates all listed tables.
- [ ] Required indexes exist.
- [ ] Foreign keys are enabled and enforced.
- [ ] Integration tests verify table creation.
- [ ] `Docs/DATA_SCHEMA.md` updated.

---

## T0008 - Implement Case Folder and Case Package Model

**Status:** Not Started  
**Priority:** Critical  
**Size:** Large  
**Layer:** Storage / Security  
**Dependencies:** T0006  

### Acceptance Criteria

- [ ] Creating a case creates standard folder/package structure.
- [ ] Case manifest is created.
- [ ] Invalid filenames are sanitized.
- [ ] Unit tests cover path sanitization.
- [ ] Integration test verifies folder creation.
- [ ] Evidence-safe logs identify operation and outcome.

---

## T0009 - Implement Source File Hashing Service

**Status:** Not Started  
**Priority:** Critical  
**Size:** Medium  
**Layer:** Security / Evidence  
**Dependencies:** T0008  

### Acceptance Criteria

- [ ] SHA-256 hash is computed correctly.
- [ ] Large files are streamed.
- [ ] Hash output is saved in source folder.
- [ ] Unit tests verify known hash values.
- [ ] Logs include hash operation status without leaking file contents.

---

## T0010 - Implement Audit Event System with Hash Chain

**Status:** Not Started  
**Priority:** Critical  
**Size:** Large  
**Layer:** Audit / Security  
**Dependencies:** T0007  

### Acceptance Criteria

- [ ] Audit events can be recorded.
- [ ] Hash chain is computed.
- [ ] Tampering test can detect changed event JSON.
- [ ] Unit/integration tests cover continuity.
- [ ] Audit logger is available to application services.

---

## T0011 - Implement Initial App Shell and Navigation

**Status:** Not Started  
**Priority:** High  
**Size:** Large  
**Layer:** UI  
**Dependencies:** T0001, T0004  

### Acceptance Criteria

- [ ] App launches.
- [ ] Navigation works between placeholder pages.
- [ ] Layout follows three-panel design direction.
- [ ] No business logic is implemented in UI shell.
- [ ] Basic startup logs exist without sensitive data.

---

## T0012 - Implement Case Creation Service

**Status:** Not Started  
**Priority:** Critical  
**Size:** Medium  
**Layer:** Application / Persistence  
**Dependencies:** T0006, T0008, T0010  

### Acceptance Criteria

- [ ] Case service creates valid case package.
- [ ] Case record exists in database.
- [ ] Audit event is written.
- [ ] Integration test verifies case creation end to end.
- [ ] Logs include correlation ID and safe case creation diagnostics.

---

# Milestone 2 — Ingestion and Conversation Foundations

## T0018 - Implement Generic XLSX Import Probe and Preview

**Status:** Completed  
**Priority:** High  
**Size:** Medium  
**Layer:** Ingestion  
**Dependencies:** T0017  

### Goal

Add a generic XLSX workbook probe/preview path that mirrors the CSV preview flow while preserving evidence-safe behavior and careful user-facing warnings.

### Requirements

- Add an XLSX importer under `src/DumpLens.Ingestion`.
- Keep the application-facing preview/probe contracts in `src/DumpLens.Application/Imports`.
- Support `.xlsx` workbook inspection without writing database records.
- Detect worksheet names and select the first non-empty worksheet by default when no worksheet is requested.
- Return preview rows, column headers, header-detection status, mapping suggestions, and warnings.
- Return safe warnings for unsupported extensions, unreadable workbooks, empty workbooks, empty worksheets, missing headers, ambiguous headers, missing likely fields, truncated preview, and missing requested worksheets.
- Use synthetic unit/golden data coverage only.

### Acceptance Criteria

- [x] `.xlsx` files can be probed through the shared import contract.
- [x] Worksheet names are returned when available.
- [x] Preview rows and column headers are returned for the selected worksheet.
- [x] Suggested field mappings are returned for common message/call headers.
- [x] Safe warnings are returned for unsupported, empty, unreadable, or ambiguous workbook states.
- [x] Unit and golden-data coverage exist for the accepted behavior.
- [x] No source registration, evidence copy, hashing, or database persistence is performed.

### Out of Scope

- Source registration.
- Evidence copy or hashing.
- Database writes.
- Message or call persistence.
- Conversation building.

### Notes for Codex / Agent

- Reuse the `ISourceImporter` contract family in `src/DumpLens.Application/Imports`.
- Keep XLSX-specific implementation in `src/DumpLens.Ingestion/Xlsx`.
- Prefer safe workbook metadata reads before deeper preview extraction.
- Do not add UI flow here; T0019 owns the wizard shell.

---

## T0019 - Build Import Wizard UI Skeleton

**Status:** Completed  
**Priority:** High  
**Size:** Large  
**Layer:** UI / App.ViewModels  
**Dependencies:** T0011, T0017, T0018  

### Goal

Create a WPF import wizard skeleton that guides users through source selection, file selection, preview, mapping, timezone confirmation, warning review, and a preview-only final summary without persisting any import data yet.

### Requirements

- Add an `Import` action to the app shell top bar.
- Add a modal or overlay wizard consistent with the existing shell modal pattern.
- Keep business workflow in view models and application-facing contracts, not in WPF views.
- Support these steps:
  - Choose source type
  - Select file
  - Assign source owner/device/account placeholder text
  - Preview data
  - Map columns
  - Confirm timestamp/timezone
  - Review validation warnings
  - Import summary placeholder
- Support at least CSV and XLSX preview paths.
- Reuse the existing CSV/XLSX probe-preview services where practical.
- Show worksheet selection when an XLSX exposes multiple worksheets.
- Show suggested mappings and warnings returned by the importer.
- Allow the user to adjust mappings in the UI skeleton.
- Allow timezone text entry/confirmation.
- Make the final step state clearly that persistence is not implemented yet.
- Add evidence-safe structured logs for wizard opened, file selected, preview requested, preview succeeded, preview failed, and wizard closed/canceled.
- Do not log raw preview values, names, phone numbers, handles, message bodies, emails, or file contents.
- Add unit tests for the import wizard view-model behavior.

### Acceptance Criteria

- [x] User can open the import wizard from the app shell.
- [x] User can choose CSV or XLSX.
- [x] User can enter a file path and inspect support details safely.
- [x] CSV preview displays rows through the CSV importer path.
- [x] XLSX preview displays rows through the XLSX importer path.
- [x] XLSX worksheet names and selection are shown when available.
- [x] Suggested field mappings are shown and editable.
- [x] Import warnings are shown with progressive disclosure.
- [x] Timezone confirmation field exists.
- [x] Final summary clearly states that persistence will be added later.
- [x] Cancel/close returns to the shell.
- [x] Evidence-safe lifecycle logging exists.
- [x] Unit tests cover key wizard view-model behavior.

### Out of Scope

- Source registration.
- Evidence copy or hashing.
- Database writes.
- Message/call/identity persistence.
- Case package import completion workflow.
- Source manager, conversation builder, search, reconciliation, AI, reports, or exports.

### Notes for Codex / Agent

- WPF views stay in `src/DumpLens.App`.
- View models stay in `src/DumpLens.App.ViewModels`.
- Import contracts stay in `src/DumpLens.Application/Imports`.
- Composition-root reference changes must be minimal and documented in `Docs/PROJECT_REFERENCES.md`, `Docs/DECISIONS.md`, and `Docs/CHANGELOG.md`.
- Do not imply import completion or persistence success anywhere in the UI copy.

---

## T0020 - Implement Source Registration and Evidence Copy

**Status:** Not Started  
**Priority:** Critical  
**Size:** Large  
**Layer:** Application / Persistence / Security  
**Dependencies:** T0007, T0008, T0009, T0010, T0019  

### Goal

Register an imported source artifact safely in the case package, compute and store SHA-256 evidence metadata, and create the initial `source_imports` / `source_artifacts` records without yet persisting normalized messages or calls.

### Requirements

- Add application-facing source registration workflow contracts.
- Copy or reference original evidence according to the approved storage model while preserving immutability.
- Compute SHA-256 for the imported artifact.
- Store safe source metadata in `source_imports` and `source_artifacts`.
- Preserve original filename, stored/referenced path, byte length, source type, and import timestamp.
- Add audit and operational logging for source registration start/success/failure.
- Add integration coverage across filesystem, hashing, persistence, and audit boundaries.

### Acceptance Criteria

- [ ] Source registration creates the expected case-package artifact placement or reference metadata.
- [ ] SHA-256 is computed and stored for the original artifact.
- [ ] `source_imports` and `source_artifacts` records are created with safe metadata.
- [ ] Audit and operational logs exist with safe identifiers only.
- [ ] Integration tests verify immutability, hashing, persistence, and traceability.
- [ ] No normalized messages, calls, identities, or conversation records are created.

### Out of Scope

- Message persistence.
- Call persistence.
- Conversation building.
- Search indexing.
- Review controls.

### Notes for Codex / Agent

- Preserve evidence immutability first.
- Stream hashes; do not read large artifacts fully into memory.
- Keep authoritative hashing and source registration out of the WPF layer.
- Do not let preview-only wizard state leak into persistence logic.

---

## T0021 - Implement Message Import Persistence

**Status:** Not Started  
**Priority:** Critical  
**Size:** Large  
**Layer:** Application / Persistence / Ingestion / Normalization  
**Dependencies:** T0014, T0020  

### Goal

Persist normalized message records and import warnings for supported tabular imports after source registration exists.

### Requirements

- Accept resolved file, worksheet, and mapping choices from the application workflow.
- Parse supported message-oriented rows into normalized message records.
- Preserve source artifact locator context such as worksheet and row number.
- Preserve original timestamp text separately from normalized UTC timestamps.
- Persist import warnings with row/object locator context.
- Keep person/device/platform-account creation out of scope unless already explicitly required by the ticket sequence.
- Add strong unit tests for row mapping and timestamp behavior plus integration coverage for database writes.

### Acceptance Criteria

- [ ] Supported message rows are persisted to `messages` and related tables required by the accepted schema.
- [ ] Source artifact traceability is preserved for each created message.
- [ ] Timestamp normalization preserves original strings and UTC values separately.
- [ ] Import warnings are persisted with locator context.
- [ ] Tests cover row mapping, timestamp handling, and end-to-end persistence.
- [ ] No conversation-building or search indexing is performed yet.

### Out of Scope

- Call persistence.
- Conversation grouping.
- Source manager UI.
- Search indexing.

### Notes for Codex / Agent

- Keep normalization deterministic and traceable.
- Use synthetic fixtures only.
- Avoid silently discarding rows that should become warnings instead.

---

## T0022 - Implement Call Log Import Persistence

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** Application / Persistence / Ingestion / Normalization  
**Dependencies:** T0014, T0020  

### Goal

Persist normalized call records and related import warnings for supported call-log style tabular sources.

### Requirements

- Add application workflow support for call-oriented mappings.
- Persist normalized calls with source artifact traceability.
- Preserve original timestamp strings and normalized UTC timestamps.
- Persist warnings for missing/ambiguous call fields.
- Add unit tests for mapping behavior and integration tests for call persistence.

### Acceptance Criteria

- [ ] Supported call rows are persisted safely.
- [ ] Call records link back to the supporting source artifact and locator.
- [ ] Import warnings are persisted with row/object context.
- [ ] Unit and integration tests cover the accepted call persistence path.
- [ ] No message conversation-building, search indexing, or source manager UI work is added.

### Out of Scope

- Message persistence changes beyond shared workflow plumbing.
- Conversation grouping.
- Search indexing.
- Source manager UI.

### Notes for Codex / Agent

- Reuse shared tabular workflow pieces where possible without collapsing message and call semantics together.
- Keep persisted enum/text values stable.

---

## T0023 - Complete Import Wizard Persistence Flow

**Status:** Not Started  
**Priority:** High  
**Size:** Large  
**Layer:** UI / App.ViewModels / Application  
**Dependencies:** T0020, T0021, T0022  

### Goal

Connect the import wizard from preview-only mode to the actual source registration and message/call persistence workflow.

### Requirements

- Replace the preview-only final step with a real import execution summary.
- Orchestrate source registration, hashing, warning capture, and message/call persistence through application-facing services.
- Keep the WPF layer responsible only for state, commands, and user messaging.
- Show progress, safe success/failure summaries, and counts.
- Add evidence-safe operational logs and audit events for the import execution lifecycle.
- Add integration coverage across the end-to-end wizard-driven import path.

### Acceptance Criteria

- [ ] Wizard can execute a real import workflow for supported CSV/XLSX sources.
- [ ] Source registration runs before normalized persistence.
- [ ] Final summary reports safe import counts and warnings.
- [ ] Failures show safe user-facing messages without raw exception dumps.
- [ ] Logs and audit events capture the import lifecycle safely.
- [ ] Integration tests cover the wizard-driven persistence flow.

### Out of Scope

- Source manager screen.
- Conversation builder.
- Search indexing.
- Reconciliation.

### Notes for Codex / Agent

- Keep command orchestration in view models thin.
- Use application services for durable work.
- Preserve the careful distinction between preview, registration, and persistence stages.

---

## T0024 - Build Source Manager Screen

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** UI / App.ViewModels  
**Dependencies:** T0020, T0023  

### Goal

Add the `Sources` screen so investigators can review registered source imports, artifact metadata, hashes, warnings, and import status in a plain-language, evidence-first view.

### Requirements

- Add a `Sources` workspace implementation to the shell.
- Show source import summary cards or rows with safe metadata.
- Show artifact filename, source type, hash display, import status, and warning counts.
- Keep source reference and locator details one click away.
- Use progressive disclosure for technical metadata and hash details.
- Add unit tests for source-manager view-model behavior and integration coverage where query services are introduced.

### Acceptance Criteria

- [ ] Sources screen replaces the placeholder workspace.
- [ ] Registered sources are listed with safe summary metadata.
- [ ] Hash/source-reference details are accessible without overwhelming the default view.
- [ ] Warning/import status is visible.
- [ ] Tests cover the accepted view-model behavior.

### Out of Scope

- Conversation building.
- Search.
- Reconciliation review.
- Import execution changes beyond displaying existing state.

### Notes for Codex / Agent

- Follow `Docs/UI_Guidelines.md` for plain-language labels and progressive disclosure.
- Keep large-list scalability in mind from the start.

---

## T0025 - Implement Conversation Builder Service

**Status:** Not Started  
**Priority:** High  
**Size:** Large  
**Layer:** Application / Normalization / Persistence  
**Dependencies:** T0021  

### Goal

Group persisted messages into preliminary conversations/threads using deterministic rules and source-backed traceability.

### Requirements

- Add a conversation builder service behind application-facing contracts.
- Group messages using thread identifiers when available and careful fallback heuristics when they are not.
- Preserve the relationship between conversations and their source-supported message members.
- Add unit and golden-data coverage for thread grouping behavior, including edge cases.
- Add safe operational logging for conversation-build runs and counts.

### Acceptance Criteria

- [ ] Persisted messages can be grouped into conversations.
- [ ] Conversation membership remains traceable to source-backed messages.
- [ ] Thread/grouping heuristics are test-covered with synthetic data.
- [ ] Logs report safe conversation-build counts and outcomes.
- [ ] No UI thread review screen is implemented here.

### Out of Scope

- Conversation UI.
- Reconciliation.
- Search indexing.
- Manual review controls.

### Notes for Codex / Agent

- Avoid over-assertive grouping when source data is ambiguous.
- Prefer deterministic, explainable rules.

---

## T0026 - Build Conversation List and Thread View

**Status:** Not Started  
**Priority:** High  
**Size:** Large  
**Layer:** UI / App.ViewModels  
**Dependencies:** T0025  

### Goal

Add the `Conversations` workspace so investigators can browse conversations and inspect their source-backed threads.

### Requirements

- Replace the placeholder conversations workspace with a list-and-thread review surface.
- Show conversation list summaries with counts, participants when available, and review-friendly timestamps.
- Show a thread view with source-backed message rows.
- Keep source references and review context one click away.
- Use virtualization or equivalent scaling for long threads/lists.
- Add unit tests for view-model behavior.

### Acceptance Criteria

- [ ] Conversation list is available in the shell.
- [ ] Selecting a conversation loads a source-backed thread view.
- [ ] Large-list responsiveness is preserved through virtualization-aware design.
- [ ] Source references remain readily accessible.
- [ ] Tests cover key view-model behavior.

### Out of Scope

- Search.
- Reconciliation.
- Timeline.
- AI findings.

### Notes for Codex / Agent

- Follow the three-panel shell pattern.
- Default to investigator-readable thread views before raw technical metadata.

---

## T0027 - Implement Message Full-Text Search Indexing

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** Search / Persistence / Application  
**Dependencies:** T0021, T0025  

### Goal

Create the first full-text search indexing path for persisted messages so global search can query message content safely and efficiently.

### Requirements

- Add message indexing workflow using the approved local search approach.
- Keep index entries traceable back to case/message/source identifiers.
- Add index build or refresh logic for imported messages.
- Add unit/integration coverage for indexing and query behavior.
- Add evidence-safe logging for indexing start/completion/failure.

### Acceptance Criteria

- [ ] Imported messages can be indexed for full-text search.
- [ ] Search index records remain traceable to source-backed messages.
- [ ] Index builds/logs remain evidence-safe.
- [ ] Tests cover indexing and query basics.
- [ ] No global search UI is added here.

### Out of Scope

- Search UI.
- Call search expansion.
- Reconciliation or AI indexing.

### Notes for Codex / Agent

- Keep the indexing path rebuildable.
- Avoid storing unsupported derived claims in search documents.

---

## T0028 - Build Global Search UI

**Status:** Not Started  
**Priority:** Medium  
**Size:** Medium  
**Layer:** UI / App.ViewModels  
**Dependencies:** T0027  

### Goal

Add the first global search UI entry point in the shell and show investigator-friendly message search results.

### Requirements

- Activate the shell search entry point.
- Add query box, results list, and safe result summary view.
- Show plain-language snippets and source-backed metadata without dumping raw technical detail first.
- Keep source references one click away from each result.
- Add unit tests for search view-model behavior.

### Acceptance Criteria

- [ ] Global search entry point is wired in the shell.
- [ ] Message search results can be listed and selected.
- [ ] Source-backed result detail is available.
- [ ] Tests cover key search view-model behavior.
- [ ] No reconciliation, AI, or reporting UI is added here.

### Out of Scope

- Advanced search syntax.
- Entity search.
- Timeline search.
- Search export.

### Notes for Codex / Agent

- Follow `Docs/UI_Guidelines.md` for plain-language labels and empty-state guidance.
- Keep the initial search surface simple and local-first.

---

## T0029 - Build Source Reference Inspector

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** UI / App.ViewModels / Application  
**Dependencies:** T0024, T0026  

### Goal

Add a reusable source reference inspector that can show source import, artifact, locator, hash, and raw metadata disclosure for selected items.

### Requirements

- Add application/view-model support for a reusable source reference detail payload.
- Show source import name, artifact filename, locator context, timestamps, and safe hash display.
- Keep advanced raw metadata in progressive disclosure.
- Make the inspector usable from source and conversation workflows.
- Add unit tests for the source-reference view-model behavior.

### Acceptance Criteria

- [ ] Source reference inspector can render source-backed detail for selected items.
- [ ] Locator context is visible enough to find the original record again.
- [ ] Hash and raw metadata stay accessible without dominating the default view.
- [ ] Tests cover the accepted inspector behavior.

### Out of Scope

- Reconciliation evidence panels.
- Report export integration.
- AI citation review UI.

### Notes for Codex / Agent

- This ticket should improve reuse across the right-hand inspector panel, not scatter source-reference logic across views.

---

## T0030 - Implement Review Status Controls

**Status:** Not Started  
**Priority:** High  
**Size:** Medium  
**Layer:** Application / Persistence / UI  
**Dependencies:** T0026, T0029  

### Goal

Introduce the first review-state workflow controls for source-backed items so investigators can explicitly move items between review states.

### Requirements

- Define the initial review-state transition rules for the accepted workflows.
- Persist review status and safe review notes where the schema already supports them.
- Add review controls in the relevant UI surfaces using plain-language labels.
- Add audit and operational logging for review-state changes.
- Add unit/integration tests for review-state transitions and persistence.

### Acceptance Criteria

- [ ] Investigators can move supported items between initial review states.
- [ ] Review-state changes are persisted and auditable.
- [ ] UI labels remain plain-language and review-first.
- [ ] Tests cover transition rules and persistence behavior.
- [ ] No unsupported AI/legal conclusions are introduced.

### Out of Scope

- Reconciliation confirmation workflow.
- Report approval workflow.
- Team permissions model expansion.

### Notes for Codex / Agent

- Use careful wording such as `Needs review`, `Reviewed`, `Confirmed by investigator`, `Rejected by investigator`, and `Extraction limitation noted`.
- Keep auditability and source support visible.

---

# Remaining Ticket Groups

Continue the full build beyond this milestone using the ticket sequence from the DumpLens Development Ticket Pack:

```text
T0031-T0039 - Reconciliation and gap detection
T0040-T0048 - Timeline, entities, and leads
T0049-T0055 - AI and analysis
T0056-T0060 - Reports and exports
T0061-T0070 - Advanced sources and expansion
```

As each later ticket is expanded, add explicit test and logging acceptance criteria.
