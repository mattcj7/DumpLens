# SECURITY_GUARDRAILS.md

## Purpose

Defines evidence, security, privacy, and chain-of-custody guardrails for DumpLens.

## Original Evidence

- Original evidence files must be read-only.
- Original imports must be hashed using SHA-256.
- Original imports must never be modified by DumpLens.
- Normalized records must link back to source artifacts.
- Copy mode is preferred for evidentiary cleanliness.
- Reference mode must warn if source files move or hash changes.

## Hashing

Hash:

- Original import files.
- Extracted attachments.
- Exported reports.
- Export packages.
- Audit chain entries where applicable.

SHA-256 is the primary hash.

## Audit Logging

Audit logs must capture:

- Case creation.
- Source import registration.
- Import completion/failure.
- Review status changes.
- Manual identity merge/split.
- Reconciliation review decisions.
- AI runs and review decisions.
- Lead creation/completion.
- Report/export generation.
- Security/integrity warnings.

Audit logs should include hash chaining when implemented.

## Logs

Follow:

```text
Docs/LOGGING_GUIDELINES.md
```

Security logging rules:

- Never log secrets.
- Never log full message bodies by default.
- Never log full raw metadata JSON by default.
- Never send logs externally without explicit user action.
- Logs should remain local by default.

## Temp Files

- Use safe temp directories.
- Clean up temp files after use.
- Do not leave unencrypted evidence copies in temp folders.
- Avoid writing raw evidence to temp locations unless necessary.

## Permissions Model

Planned roles:

| Role | Capabilities |
|---|---|
| Admin | Manage case, settings, users, AI providers |
| Investigator | Import, review, approve findings, export reports |
| Analyst | Import, analyze, suggest findings, create leads |
| Reviewer | Review findings/reports, approve/reject |
| Read-only | View case and reports only |

## Cloud AI Controls

- Cloud AI must be optional.
- Cloud AI must be logged.
- Cloud AI should support redaction.
- Cloud AI should be disableable by case or agency setting.
- Cloud AI must clearly label outputs as cloud-assisted if used.

## Prohibited Behavior

- Do not mutate original evidence.
- Do not auto-upload case data.
- Do not hide evidence warnings.
- Do not export unsupported AI conclusions as fact.
- Do not bypass review workflow for findings.
