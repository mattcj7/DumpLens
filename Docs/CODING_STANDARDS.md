# CODING_STANDARDS.md

## Purpose

Defines coding standards for DumpLens.

## General Rules

- Keep classes focused.
- Prefer explicit names over clever names.
- Keep domain logic out of UI views.
- Avoid circular dependencies.
- Prefer deterministic behavior for evidence processing.
- Preserve raw input values separately from normalized values.
- Avoid hidden mutation.
- Make failure modes visible.
- Do not swallow exceptions without logging safe context.
- Do not use real case data in tests or examples.

## Layering Rules

- `Core` contains domain models, value objects, stable enums, and pure domain rules.
- `Application` contains use cases, service interfaces, DTOs, and workflow orchestration.
- `Persistence` implements database access and migrations.
- `Ingestion` handles import probing, preview, parsing, and source adapters.
- `Normalization` canonicalizes identities, timestamps, bodies, and metadata.
- `Reconciliation` handles conversation/message matching and gap detection.
- `Analysis` handles deterministic non-AI analysis.
- `AI` handles provider abstraction, prompts, redaction, output validation.
- `Search` handles indexing and retrieval.
- `Reporting` handles report models and export.
- `Security` handles hashing, encryption, redaction primitives, path safety.
- `Audit` handles audit event creation and verification.
- `App.ViewModels` contains presentation logic.
- `App` contains desktop shell/views.

## Persistence Rules

- Store persisted enums as stable strings.
- Use UTC for normalized event timestamps.
- Preserve original timestamp strings.
- Preserve original raw metadata where appropriate.
- Use migrations for schema changes.
- Write tests for migrations.
- Avoid schema changes without updating `Docs/DATA_SCHEMA.md`.

## Error Handling

- Use specific exceptions where useful.
- Include safe diagnostic context.
- Do not log raw evidence.
- Return user-friendly warnings for import problems.
- Fail closed for evidence integrity problems.

## Logging

Follow:

```text
Docs/LOGGING_GUIDELINES.md
```

## Tests

Follow:

```text
Docs/TESTING.md
Docs/QUALITY_GATES.md
```
