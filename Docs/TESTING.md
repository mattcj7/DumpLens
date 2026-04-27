# TESTING.md

## Purpose

Defines the testing standard for DumpLens.

DumpLens handles evidentiary data, review workflows, and careful investigative language. Tests must prove correctness, traceability, and evidence-safe behavior, not only basic happy paths.

## Core Test Position

Testing is mandatory for behavior-heavy changes.

Required principles:

- Prefer strong unit tests for logic-heavy behavior.
- Add integration tests when behavior crosses storage, filesystem, migration, or workflow boundaries.
- Add golden-data tests when parsing, normalization, reconciliation, or output stability matters.
- Use performance tests when scale or responsiveness is an acceptance concern.
- Use synthetic fixtures only. Never use real evidence.

If a ticket changes behavior and no tests are added or updated, the completion report must explain why.

## Test Projects

```text
tests/DumpLens.Tests.Unit
tests/DumpLens.Tests.Integration
tests/DumpLens.Tests.GoldenData
tests/DumpLens.Tests.Performance
```

## Synthetic Test Data Rules

Required rules:

- Never place real case data, screenshots, transcripts, phone numbers, or account identifiers in fixtures.
- Use synthetic message bodies, names, phone numbers, handles, and timestamps.
- Fixture data should be readable enough to explain the behavior under test without resembling real evidence.
- If a bug was discovered from real evidence, reproduce it with a synthetic fixture before committing a test.

## Unit Testing Requirements

Unit tests are required for behavior-heavy logic including:

- domain rules and value objects
- enum or persisted-string mapping helpers
- evidence hashing helpers
- path and filename sanitization
- source artifact traceability helpers
- identity normalization
- phone number, email, and handle normalization
- timestamp parsing and timezone conversion
- message-body normalization
- reconciliation scoring and penalties
- missing-counterpart and gap detection logic
- AI structured output validation
- AI prohibited-language checks
- AI redaction and rehydration helpers
- logging redaction helpers
- correlation ID creation and propagation helpers

Unit test expectations:

- cover happy path, edge cases, and failure cases
- remain deterministic
- avoid real network access
- avoid dependence on machine-local secrets or mutable global state
- assert outcomes that matter to the user or integrity model

## Strong Unit Test Expectations

Behavior-heavy changes require more than one nominal-path test.

Strong unit test sets usually include:

- valid input cases
- malformed input cases
- boundary conditions
- duplicate or ambiguous data cases
- careful-language or guardrail enforcement cases
- traceability preservation cases
- redaction or evidence-safety cases where applicable

Examples:

- A timestamp parser change should include valid, ambiguous, invalid, and timezone-shift cases.
- A reconciliation scorer change should include exact matches, near matches, false-match prevention, and short/generic-message penalties.
- A logging helper change should prove sensitive content is removed while identifiers and correlation IDs remain useful.

## Integration Testing Requirements

Integration tests are required when behavior crosses component boundaries, especially for:

- database migration runner behavior
- schema creation and foreign key enforcement
- repository write/read workflows
- case folder creation
- source registration and artifact metadata persistence
- SHA-256 hashing service against real temporary files
- audit event persistence and hash-chain continuity
- import pipeline execution against temporary case storage
- export/report generation with citation and hash verification

Integration test expectations:

- use temporary directories and temporary databases
- verify behavior end to end, not only mocked calls
- confirm evidence immutability where relevant
- confirm traceability links are created where relevant
- confirm audit logging occurs for security-sensitive workflows where practical

## Golden-Data Testing Requirements

Golden-data tests are required where stable parsing or matching behavior matters, including:

- CSV message imports
- XLSX message imports
- call log imports
- conversation building
- normalization outputs that must remain stable
- same-message reconciliation across sources
- provider-only, device-only, and screenshot-only scenarios
- missing middle-segment and gap detection
- short/generic message false-match prevention
- group chat participant handling

Golden-data expectations:

- fixtures must be synthetic
- expected outputs must be reviewed and intentionally updated
- changes to expected outputs must be explained in the ticket or test update
- unstable or noisy fields should be normalized before snapshot comparison

## Logging and Audit Test Expectations

Where practical, tests should verify:

- sensitive evidence is not emitted to logs
- correlation IDs are present on major operations
- operational logs retain useful safe context
- audit events record the correct actor, operation, and target IDs
- traceability identifiers survive failure paths
- AI logging does not leak full prompts or responses

## Security and Evidence Integrity Test Expectations

Where relevant, tests should verify:

- original evidence files remain unchanged
- SHA-256 hashes match known values
- source artifact metadata is preserved
- derived records link back to source artifacts
- tampering or mismatch paths raise safe warnings or failures

## AI Test Expectations

AI-related tickets must include tests for:

- schema-valid structured output acceptance
- schema-invalid output rejection
- required citation presence
- prohibited conclusion rejection
- confidence label validation
- provenance preservation after review actions
- redaction-capable cloud mode behavior

AI tests must not call external providers.

## Performance Testing Requirements

Performance tests are required when the ticket changes scale-sensitive behavior.

Areas to cover where relevant:

- large imports
- indexing large message sets
- long conversation loading assumptions
- reconciliation candidate generation
- reconciliation scoring
- search query performance
- export generation

Initial targets:

| Operation | Target |
|---|---:|
| Open case dashboard after initial load | < 3 seconds typical |
| Search indexed data | < 2 seconds typical |
| Open conversation with 10k messages | < 3 seconds with virtualization |
| Import 100k CSV rows | Progress shown; no UI freeze |
| Reconcile 100k messages | Background job; resumable |
| Export report | Progress shown; output hash recorded |

## Test Review Questions

Before closing a ticket, confirm:

- Are behavior-heavy changes covered by strong unit tests?
- Are storage/filesystem/workflow boundaries covered by integration tests where needed?
- Are import/reconciliation/output stability concerns covered by golden-data tests where needed?
- Do tests prove evidence immutability, SHA-256 hashing, and traceability where relevant?
- Do tests use synthetic data only?
- Do tests cover failure and guardrail cases, not only success paths?

## Build and Test Commands

Preferred baseline commands:

```powershell
dotnet restore O:\DumpLens\DumpLens.sln
dotnet build O:\DumpLens\DumpLens.sln
dotnet test O:\DumpLens\DumpLens.sln
```

Performance tests may run separately when they should not block normal iteration.

## Ticket Completion Test Reporting

Every completion report must include:

```text
Tests added/updated:
Tests run:
Build commands run:
Known test gaps:
```

When a ticket is docs-only, say that no product-behavior tests were added and why.
