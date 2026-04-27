# AI_GUARDRAILS.md

## Purpose

Defines AI/LLM guardrails for DumpLens.

## AI Role

AI is a review assistant. It helps investigators find, summarize, organize, and prioritize material.

AI does not:

- Establish probable cause.
- Determine guilt.
- Automatically label gang membership.
- Automatically label criminal association.
- Replace human review.
- Replace source evidence.
- Replace forensic tools.

## Source Support Requirement

Every AI finding must include:

- Finding title.
- Plain-language summary.
- Confidence label.
- Source artifact references.
- Exact message/call/artifact IDs or locators where applicable.
- Limitations.
- Alternative explanations when relevant.
- Recommended human review action.

AI output without source references may only be stored as a draft note and should not be exportable as an evidentiary finding.

## Structured Output Requirement

AI outputs must be structured and schema-validated.

Free text can exist inside JSON fields, but free text alone is not acceptable for system-ingested AI findings.

## Confidence Labels

Allowed confidence labels:

```text
high
medium
low
unknown
investigator_confirmed
investigator_rejected
```

## Careful Language

Use:

- Possible missing counterpart.
- Possible deletion gap.
- Needs investigator review.
- Source-only message.
- Provider-only message.
- Screenshot-only message.
- AI-assisted summary.
- Suggested investigative lead.

Avoid:

- Guilty.
- Confirmed deletion.
- Evidence tampering.
- Criminal associate.
- Gang member.
- Proven conspiracy.
- Probable cause established.

Stronger language may only appear if entered by an investigator in a clearly labeled investigator note/conclusion.

## Cloud AI Redaction

When cloud AI is used, support redaction:

```text
Michael Johnson -> Person 1
803-555-1212 -> Phone 1
@mike_170 -> Account 1
Elm Street Apartments -> Location 1
```

Maintain a local redaction manifest so results can be rehydrated inside the app.

## AI Logging

Log:

- Provider mode.
- Model name if available.
- Prompt template ID/version.
- Redaction enabled/disabled.
- Input scope summary.
- Output validation status.
- Run status.
- Error category.

Do not log full prompts or responses by default.

Follow:

```text
Docs/LOGGING_GUIDELINES.md
```

## AI Review Workflow

AI findings must support:

- Approve.
- Reject.
- Edit summary.
- Convert to lead.
- Pin to timeline.
- Add to report only with label/review state.

Approval must not remove the AI-assisted provenance.
