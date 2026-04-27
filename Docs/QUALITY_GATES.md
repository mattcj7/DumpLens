# QUALITY_GATES.md

## Purpose

Defines the enforceable acceptance gates for DumpLens tickets.

A ticket is not complete because code compiles. It is complete only when scope, evidence integrity, testing, logging, documentation, and reporting expectations are satisfied.

## Universal Acceptance Gate

Do not accept a ticket unless all applicable statements are true:

- Work stayed within the assigned ticket scope.
- Architecture boundaries were preserved.
- Original evidence immutability was preserved.
- Required SHA-256 hashing behavior was added or preserved.
- Required source artifact traceability was added or preserved.
- Audit logging expectations were met for security- or review-relevant operations.
- AI remained optional, structured, and review-only where applicable.
- Logs remained evidence-safe and did not leak sensitive content.
- Tests were added or updated where behavior changed.
- Related docs were updated when requirements, behavior, logging, workflow, or guardrails changed.
- Completion reporting clearly states what changed, what was tested, and what remains.

## Standard Verification Commands

Baseline verification commands:

```powershell
dotnet restore O:\DumpLens\DumpLens.sln
dotnet build O:\DumpLens\DumpLens.sln
dotnet test O:\DumpLens\DumpLens.sln
```

If a user or ticket explicitly requires these commands, run them even for docs-only work unless blocked. If they fail, report the exact failure.

If a docs-only ticket does not require product validation commands, the completion report may explain why full build/test execution was not necessary.

## Documentation Consistency Gate

Documentation-affecting tickets are not complete unless:

- the updated docs agree with `Docs/AGENTS.md`, `Docs/TICKETS.md`, and `Docs/DECISIONS.md`
- terminology is consistent across related docs
- examples reinforce the actual guardrails instead of weakening them
- docs do not silently contradict evidence immutability, SHA-256 hashing, traceability, audit logging, or AI review-only policy

## Testing Gate

For behavior-heavy changes:

- strong unit tests are required
- integration tests are required where workflows cross filesystem, database, or service boundaries
- golden-data tests are required where parsing, normalization, reconciliation, or stable outputs matter
- synthetic fixtures are required
- failure and guardrail cases must be covered, not only happy paths

It is not acceptable to claim a change is low risk without proving it through the right test type.

## Security and Evidence Gate

For security-, import-, storage-, reconciliation-, AI-, and export-adjacent work:

- [ ] Original evidence remains immutable.
- [ ] SHA-256 is used where authoritative hashing is required.
- [ ] Derived records remain traceable to source artifacts.
- [ ] Integrity warnings are surfaced rather than hidden.
- [ ] No unsupported shortcuts weaken chain-of-custody.
- [ ] No real evidence appears in tests, docs, or debug materials.

## Logging Gate

For operational or behavior-heavy changes:

- [ ] Logs include correlation IDs where major operations exist.
- [ ] Logs contain safe identifiers and stage/outcome detail.
- [ ] Logs avoid full message bodies, raw evidence, secrets, and unredacted sensitive values.
- [ ] Audit-relevant actions are captured in durable audit history where applicable.
- [ ] Logging changes are test-covered where practical.

## AI Gate

For any AI-related work:

- [ ] AI remains optional.
- [ ] AI output is structured and schema-validated.
- [ ] AI findings cite source artifacts.
- [ ] AI remains review-only and does not silently approve findings.
- [ ] Cloud mode is redaction-capable.
- [ ] Unsupported legal or evidentiary conclusions are not generated or accepted.
- [ ] Reports remain source-cited and do not present unsupported AI claims as fact.

## Import and Normalization Gate

For import or normalization work:

- [ ] Original file is copied or referenced read-only.
- [ ] SHA-256 hash is computed and stored.
- [ ] Source artifact references are created.
- [ ] Raw values are preserved where required.
- [ ] Normalized values remain separate from raw values.
- [ ] Import warnings preserve row/object locator context.
- [ ] Golden-data coverage exists where behavior stability matters.

## Reconciliation Gate

For reconciliation or gap-analysis work:

- [ ] False-match risk was considered.
- [ ] Short or generic messages are not over-trusted.
- [ ] Score breakdown or equivalent debug context is available.
- [ ] Missing-counterpart and gap language remains careful.
- [ ] Alternative explanations can be preserved.
- [ ] Golden-data tests cover changed matching behavior.

## Reporting Gate

For reporting or export work:

- [ ] Findings remain source-cited.
- [ ] Included items preserve review state and provenance.
- [ ] Output hash creation or verification is handled where required.
- [ ] Export actions are auditable.
- [ ] Logs remain evidence-safe.

## Ticket Completion Reporting Gate

Every completed ticket report must include:

```text
Ticket:
Status:
Changed files:
Summary:
Tests added/updated:
Tests run:
Build commands run:
Logging added/updated:
Docs updated:
Verification:
Known issues:
Assumptions:
Out-of-scope items intentionally not implemented:
```

The report must clearly distinguish:

- what changed
- what was intentionally not changed
- what verification actually ran
- what failed or remains uncertain

## Failure Gate

Do not hide incomplete verification.

If build, test, logging, auditability, traceability, or guardrail work remains incomplete:

- state it directly
- state what failed
- state the impact
- state what remains to finish the ticket
