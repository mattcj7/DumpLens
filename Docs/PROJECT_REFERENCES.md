# PROJECT_REFERENCES.md

## Purpose

Defines allowed project dependency direction for DumpLens.

Agents must follow this file when adding project references. Avoid circular dependencies. Keep the UI thin. Keep domain logic out of UI projects.

## Production Project References

```text
DumpLens.Core
  No project references

DumpLens.Application
  -> DumpLens.Core

DumpLens.Persistence
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Ingestion
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Normalization
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Reconciliation
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Analysis
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.AI
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Search
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Reporting
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Security
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Audit
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.Integration.CaseGraph
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.App.ViewModels
  -> DumpLens.Core
  -> DumpLens.Application

DumpLens.App
  -> DumpLens.App.ViewModels
  -> DumpLens.Application
```

## Test Project References

```text
DumpLens.Tests.Unit
  -> DumpLens.Core
  -> DumpLens.Application
  -> DumpLens.Normalization
  -> DumpLens.Reconciliation
  -> DumpLens.Analysis
  -> DumpLens.Security
  -> DumpLens.AI

DumpLens.Tests.Integration
  -> DumpLens.Core
  -> DumpLens.Application
  -> DumpLens.Persistence
  -> DumpLens.Ingestion
  -> DumpLens.Search
  -> DumpLens.Reporting
  -> DumpLens.Audit

DumpLens.Tests.GoldenData
  -> DumpLens.Core
  -> DumpLens.Ingestion
  -> DumpLens.Normalization
  -> DumpLens.Reconciliation
  -> DumpLens.Analysis

DumpLens.Tests.Performance
  -> DumpLens.Core
  -> DumpLens.Application
  -> DumpLens.Persistence
  -> DumpLens.Search
  -> DumpLens.Reconciliation
```

## Dependency Rules

- `DumpLens.Core` must remain dependency-free.
- UI projects must not reference persistence directly unless a ticket explicitly approves it.
- Importers should not own review workflow logic.
- AI providers should not directly approve findings.
- Persistence should implement storage details but not own UI behavior.
- Reconciliation should not depend on UI or reporting.
- Reporting should consume reviewed/source-backed models and should not invent findings.
- Integration modules must remain optional.

## Changing This File

Any change to project reference direction must be recorded in:

```text
Docs/DECISIONS.md
Docs/CHANGELOG.md
```
