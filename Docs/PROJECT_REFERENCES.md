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
  -> DumpLens.Ingestion (composition root only for preview/probe wiring)
  -> DumpLens.Normalization (composition root only for existing import normalization service wiring)
  -> DumpLens.Persistence (composition root only for startup/service wiring)
  -> DumpLens.Security (composition root only for existing hashing service wiring)
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
- UI projects must not reference ingestion directly unless a ticket explicitly approves it.
- `DumpLens.App` may reference `DumpLens.Persistence` only as a composition root for startup wiring to existing application services. Keep persistence types out of views and view models.
- `DumpLens.App` may reference `DumpLens.Ingestion` only as a composition root for startup wiring to existing application-facing import preview/probe services. Keep ingestion types out of views and view models.
- `DumpLens.App` may reference `DumpLens.Normalization` only as a composition root for startup wiring to existing application-facing import normalization services. Keep normalization types out of views and view models.
- `DumpLens.App` may reference `DumpLens.Security` only as a composition root for startup wiring to existing application-facing hashing or evidence-safety services. Keep security types out of views and view models.
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
