# QUALITY_GATES.md

## Purpose

Defines required checks before accepting DumpLens tickets.

## Universal Acceptance Gate

A ticket is not accepted unless:

- It stays within scope.
- It follows architecture boundaries.
- It updates or adds tests where practical.
- It updates documentation if behavior, schema, logging, or workflow changes.
- It builds successfully.
- It does not introduce unsupported AI/legal conclusions.
- It does not mutate original evidence.
- It does not create sensitive logging leaks.

## Standard Verification Commands

After project setup exists:

```powershell
dotnet restore
dotnet build
dotnet test
```

If a ticket only changes docs, the completion report should say build/test was not necessary and explain why.

## Required Ticket Completion Report

```text
Ticket:
Status:
Changed files:
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

## Code Review Checklist

- [ ] Clear separation between layers.
- [ ] No circular project references.
- [ ] No UI business logic.
- [ ] No raw evidence mutation.
- [ ] No sensitive data in logs.
- [ ] Tests cover new behavior.
- [ ] Error paths handled.
- [ ] User-facing warnings are plain-language.
- [ ] AI output, if any, is structured and cited.
- [ ] Reports, if any, are source-cited.

## Reconciliation Gate

For reconciliation logic:

- [ ] False-match risk considered.
- [ ] Short/generic messages penalized.
- [ ] Score breakdown available.
- [ ] Missing counterpart language remains careful.
- [ ] Alternative explanations are stored.
- [ ] Golden-data tests updated.

## Import Gate

For import logic:

- [ ] Original file is copied/referenced read-only.
- [ ] SHA-256 hash is computed.
- [ ] Source artifact references are created.
- [ ] Warnings are stored for ambiguous data.
- [ ] Raw values are preserved.
- [ ] Normalized values are separate from raw values.
- [ ] No real evidence used in tests.

## AI Gate

For AI logic:

- [ ] AI output validates against schema.
- [ ] AI findings cite source artifacts.
- [ ] AI output is labeled AI-assisted.
- [ ] Human review state exists.
- [ ] Cloud mode is optional.
- [ ] Redaction path exists where cloud mode is used.
- [ ] Prohibited conclusions are not generated or accepted.

## Logging Gate

For logging additions:

- [ ] Logs include correlation IDs where useful.
- [ ] Logs help debug likely failures.
- [ ] Logs avoid full message bodies and raw evidence.
- [ ] Errors include safe diagnostic context.
- [ ] Logs do not include secrets.
