# TESTING.md

## Purpose

DumpLens must have strong testing throughout the build. The app handles sensitive investigative evidence, so correctness, traceability, and careful language matter.

Testing must cover both expected behavior and failure/edge cases.

## Test Projects

```text
tests/DumpLens.Tests.Unit
tests/DumpLens.Tests.Integration
tests/DumpLens.Tests.GoldenData
tests/DumpLens.Tests.Performance
```

## Project References

Use the reference layout in:

```text
Docs/PROJECT_REFERENCES.md
```

## Unit Testing Requirements

Unit tests are required for:

- Domain/value-object behavior.
- Enum/string mapping helpers.
- Identity normalization.
- Phone number normalization.
- Username/handle normalization.
- Email normalization.
- Timestamp parsing and timezone conversion.
- Message body normalization and hashing helpers.
- Reconciliation scoring.
- Missing counterpart detection rules.
- Gap-window clustering rules.
- Slang/dictionary matching.
- AI structured output validation.
- AI redaction and rehydration.
- Guardrail language checks.
- File/path sanitization helpers.
- Logging redaction helpers.

## Unit Test Quality Rules

- Tests must be deterministic.
- Tests must not require network access.
- Tests must not use real case data.
- Use synthetic fixtures only.
- Test names should describe the expected behavior.
- Include edge cases and failure cases.
- Avoid asserting vague behavior.
- Avoid tests that pass only because implementation details are mocked too heavily.

## Integration Testing Requirements

Integration tests are required for:

- Database migration runner.
- Schema creation and foreign key enforcement.
- Repository persistence/readback.
- Source registration and case folder creation.
- File hashing service with real temporary files.
- Import pipeline with temporary case databases.
- Search indexing and rebuild behavior.
- Report/export pipeline.
- Audit event hash chain.

## Golden-Data Testing Requirements

Golden-data tests are required for:

- CSV message imports.
- XLSX message imports.
- Call log imports.
- Conversation building.
- Same-message reconciliation across two sources.
- Missing middle-segment detection.
- Provider-only messages.
- Screenshot-only/manual-entry messages.
- Group chat participant behavior.
- Short/generic message false-match prevention.

Golden fixtures must be synthetic and stored under a test fixtures folder. Never use real evidence.

## Performance Testing Requirements

Performance tests should cover:

- Importing large CSV files.
- Indexing large message sets.
- Opening long conversations with virtualization assumptions.
- Reconciliation candidate generation.
- Reconciliation scoring.
- Search query performance.
- Report generation on large timelines.

Initial performance targets:

| Operation | Target |
|---|---:|
| Open case dashboard after initial load | < 3 seconds typical |
| Search indexed data | < 2 seconds typical |
| Open conversation with 10k messages | < 3 seconds with virtualization |
| Import 100k CSV rows | Progress shown; no UI freeze |
| Reconcile 100k messages | Background job; resumable |
| Export report | Progress shown; output hash recorded |

## AI Testing Requirements

AI-related tests must verify:

- Output validates against schema.
- Output includes source references.
- Invalid output is rejected.
- Unsupported conclusions are not accepted.
- Confidence language is careful.
- Warrant target suggestions are framed as investigative leads.
- Redaction removes sensitive values before cloud-mode requests.
- AI-assisted labels are preserved after approval.

## Reconciliation Testing Requirements

Required tests:

1. Exact same SMS appears on two devices.
2. Same SMS differs by 30 seconds.
3. Same message body appears twice in same thread; avoid false match.
4. Short generic messages require stronger supporting signals.
5. Group chat message has multiple recipients.
6. One device is missing a middle segment.
7. Provider return has messages not in device dump.
8. Screenshot-only message cannot be over-trusted.
9. Manual match overrides scoring.
10. Investigator exclusion prevents future auto-match.
11. Deleted artifact is flagged but not over-labeled.
12. Timezone mismatch creates warning or corrected normalized time.

## Logging Test Expectations

Where practical, tests should verify:

- Sensitive values are redacted from diagnostic log helper output.
- Correlation IDs are included in operation logs.
- Failed jobs log enough diagnostic context to debug.
- Import warnings are recorded without dumping full raw evidence.
- AI provider failures are logged without request body leakage.

## Build Commands

Preferred baseline commands after projects exist:

```powershell
dotnet restore
dotnet build
dotnet test
```

For performance tests, use a separate command or category so they do not run on every quick loop unless requested.

## Ticket Completion Test Report

Every completed ticket response must include:

```text
Tests added/updated:
Tests run:
Build commands run:
Known test gaps:
```

If tests cannot be added for a ticket, explain why.
