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
