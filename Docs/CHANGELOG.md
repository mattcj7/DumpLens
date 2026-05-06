# CHANGELOG.md

## Unreleased

### Added

- Repo documentation pack.
- Project structure guidance.
- Project reference guidance.
- Strong unit testing requirements.
- Evidence-safe logging guidelines.
- Quality gates.
- Debugging guide.
- Updated agent workflow requirements.
- Composition-root guidance allowing `DumpLens.App` to wire existing persistence-backed services without moving business logic into the UI layer.
- Composition-root guidance allowing `DumpLens.App` to wire ingestion-backed import preview services without moving preview logic into the UI layer.
- Composition-root guidance allowing `DumpLens.App` to wire existing normalization and hashing services needed by the approved import workflow.
- Import preview wizard shell flow for CSV/XLSX inspection, worksheet selection, mapping review, timezone confirmation, warning review, and preview-only summary.
- Real import wizard persistence flow for source registration, evidence copy and hashing, mapped message/call persistence, audit verification, and safe completion summaries.
- Deterministic conversation builder contracts, SQLite-backed conversation assignment service, and evidence-safe unit/integration coverage for thread-based and participant-set grouping.
- Message search indexing contracts, SQLite FTS5-backed case rebuild/search service, schema migration, and integration coverage for case-scoped traceable message search.
- Shared source-reference inspector contracts, SQLite-backed source-reference reader, and shell wiring for safe traceability from search results, conversation messages, and source selections.
