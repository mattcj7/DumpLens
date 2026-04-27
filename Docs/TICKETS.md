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

# Remaining Ticket Groups

Continue the full build using the ticket sequence from the DumpLens Development Ticket Pack:

```text
T0013-T0023 - Ingestion and normalization
T0024-T0030 - Source manager, conversations, and search
T0031-T0039 - Reconciliation and gap detection
T0040-T0048 - Timeline, entities, and leads
T0049-T0055 - AI and analysis
T0056-T0060 - Reports and exports
T0061-T0070 - Advanced sources and expansion
```

As each later ticket is expanded, add explicit test and logging acceptance criteria.
