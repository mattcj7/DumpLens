# PROJECT_STRUCTURE.md

## Purpose

Defines the expected DumpLens repository structure. Coding agents must follow this layout unless `Docs/DECISIONS.md` records an approved change.

## Expected Root Layout

```text
O:\DumpLens
├── DumpLens.sln
├── README.md
├── AGENTS.md
├── Docs
├── src
├── tests
└── tools
```

## Source Project Layout

```text
src
├── DumpLens.App
├── DumpLens.App.ViewModels
├── DumpLens.Core
├── DumpLens.Application
├── DumpLens.Persistence
├── DumpLens.Ingestion
├── DumpLens.Normalization
├── DumpLens.Reconciliation
├── DumpLens.Analysis
├── DumpLens.AI
├── DumpLens.Search
├── DumpLens.Reporting
├── DumpLens.Security
├── DumpLens.Audit
└── DumpLens.Integration.CaseGraph
```

## Test Project Layout

```text
tests
├── DumpLens.Tests.Unit
├── DumpLens.Tests.Integration
├── DumpLens.Tests.GoldenData
└── DumpLens.Tests.Performance
```

## Tools Layout

```text
tools
├── DumpLens.SchemaMigrator
├── DumpLens.SampleDataGenerator
└── DumpLens.ImportFormatInspector
```

Tool projects may be added later by ticket. For `T0001`, creating the `tools` folder with a placeholder README is enough unless the ticket is updated.

## Documentation Layout

```text
Docs
├── AGENTS.md
├── DESIGN.md
├── TECHNICAL_ARCHITECTURE.md
├── TICKETS.md
├── DECISIONS.md
├── PROJECT_STRUCTURE.md
├── PROJECT_REFERENCES.md
├── UI_Guidelines.md
├── SECURITY_GUARDRAILS.md
├── AI_GUARDRAILS.md
├── DATA_SCHEMA.md
├── IMPORT_FORMATS.md
├── TESTING.md
├── QUALITY_GATES.md
├── LOGGING_GUIDELINES.md
├── CODING_STANDARDS.md
├── RECONCILIATION_GUIDELINES.md
├── REPORTING_GUIDELINES.md
├── DEBUGGING.md
└── CHANGELOG.md
```

## T0001 Expected Final Shape

After `T0001`, this should exist:

```text
O:\DumpLens
├── DumpLens.sln
├── README.md
├── AGENTS.md
├── Docs
├── src
│   ├── DumpLens.App
│   ├── DumpLens.App.ViewModels
│   ├── DumpLens.Core
│   ├── DumpLens.Application
│   ├── DumpLens.Persistence
│   ├── DumpLens.Ingestion
│   ├── DumpLens.Normalization
│   ├── DumpLens.Reconciliation
│   ├── DumpLens.Analysis
│   ├── DumpLens.AI
│   ├── DumpLens.Search
│   ├── DumpLens.Reporting
│   ├── DumpLens.Security
│   ├── DumpLens.Audit
│   └── DumpLens.Integration.CaseGraph
├── tests
│   ├── DumpLens.Tests.Unit
│   ├── DumpLens.Tests.Integration
│   ├── DumpLens.Tests.GoldenData
│   └── DumpLens.Tests.Performance
└── tools
```

## Rules

- Do not add random top-level folders without a ticket.
- Do not place source code under `Docs`.
- Do not place test fixtures under `src`.
- Do not place production evidence or real case data in the repository.
- Use synthetic fixtures only.
