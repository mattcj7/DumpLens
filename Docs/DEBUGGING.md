# DEBUGGING.md

## Purpose

This file defines how to debug DumpLens safely and effectively.

## Debugging Priorities

When a bug appears:

1. Preserve evidence integrity.
2. Identify the operation and correlation ID.
3. Check audit events for user/system actions.
4. Check app logs for job/import/reconciliation/report errors.
5. Reproduce with synthetic fixtures if possible.
6. Add a failing unit/integration/golden-data test.
7. Fix the issue.
8. Confirm the test passes.
9. Check logging is sufficient for future diagnosis.

## Common Debug Areas

### Import Problems

Check:

- Source hash creation.
- Importer selected.
- Field mapping.
- Timestamp parser warnings.
- Identity normalization warnings.
- Row/object locator preservation.
- Batch insert failure.

### Reconciliation Problems

Check:

- Conversation assignment.
- Candidate generation window.
- Body hash normalization.
- Participant identity matching.
- Timestamp tolerance.
- Short/generic message penalty.
- Score breakdown.
- Missing counterpart alternatives.

### Timeline Problems

Check:

- UTC conversion.
- Timezone assumption.
- Source event time vs normalized event time.
- Duplicate events.
- Manual pinned events.
- Event links.

### AI Problems

Check:

- Prompt template version.
- Provider mode.
- Redaction manifest.
- Output schema validation.
- Source references.
- Review status.
- Prohibited language checks.

### Report Problems

Check:

- Report configuration.
- Included items.
- Review status filters.
- Source citations.
- Output hash.
- Export audit event.

## Sensitive Data Safety

Do not paste real evidence into bug reports or test fixtures. Use synthetic examples.

## Debug Logging Expectations

Every major operation should provide enough logs to answer:

- What operation ran?
- Who/what started it?
- Which case/source/job/report/AI run was involved?
- How many records were processed?
- What failed?
- Is there a safe error code or warning code?
- What should the developer check next?
