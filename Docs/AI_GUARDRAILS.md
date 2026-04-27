# AI_GUARDRAILS.md

## Purpose

Defines how AI may and may not be used in DumpLens.

DumpLens handles sensitive investigative evidence. AI can assist review, but it cannot replace source evidence, human judgment, chain-of-custody, or cautious investigative language.

## Core AI Position

AI is optional review assistance only.

AI may help:

- summarize long conversations
- cluster related evidence
- suggest investigative leads
- identify items for human review
- explain why records may deserve attention

AI may not:

- establish probable cause
- determine guilt, intent, or credibility
- assert evidence tampering as fact
- label someone a gang member, co-conspirator, criminal associate, or offender without human-reviewed source support
- silently approve a finding
- bypass review workflow
- replace source-cited reporting

If a workflow depends on accepting AI output without human review, it violates DumpLens policy.

## AI Must Stay Optional

Required rules:

- DumpLens must support a non-AI workflow.
- Cases must remain usable if AI is disabled.
- Cloud AI must remain optional.
- AI-related settings must be explicit when implemented.
- Turning AI off must not corrupt case state or source traceability.

## Source-Cited Findings Requirement

AI outputs that affect review, leads, timelines, or reports must cite supporting source material.

Every reviewable AI finding must include:

- stable finding ID
- finding type
- short plain-language title
- concise summary
- confidence label
- limitations
- recommended human review action
- one or more citations to supporting source artifacts

Each citation should identify, where applicable:

- `source_artifact_id`
- `source_import_id`
- source locator such as row number, thread ID, message ID, call record ID, or transcript segment
- quoted or paraphrased support kept inside the case store, not in logs

AI output without source citations:

- may be stored only as a draft suggestion or discarded
- must not be promoted to an evidentiary finding
- must not appear in official reports

## Structured Output Requirement

AI output must be structured, schema-validated, and reviewable.

Free-form prose alone is not acceptable for system-ingested findings.

Minimum structured fields:

```json
{
  "finding_id": "ai_001",
  "finding_type": "suggested_lead",
  "title": "Possible coordination around meeting time",
  "summary": "Two source-backed messages suggest coordination around a meeting window.",
  "confidence": "medium",
  "limitations": [
    "One source is screenshot-only."
  ],
  "recommended_review_action": "Verify with device dump or provider return.",
  "citations": [
    {
      "source_artifact_id": "art_0012",
      "source_import_id": "imp_0007",
      "locator": "thread=abc message=193"
    }
  ]
}
```

Validation rules:

- Reject missing required fields.
- Reject invalid confidence labels.
- Reject unsupported conclusion types.
- Reject outputs with empty citation lists for reportable findings.
- Preserve validation errors for debugging and review.

## Allowed Confidence Labels

Allowed labels:

```text
high
medium
low
unknown
investigator_confirmed
investigator_rejected
```

Interpretation rules:

- `high` never means proven.
- `unknown` is preferred over forced confidence.
- investigator statuses are review outcomes, not model confidence.

## Language Guardrails

Preferred language:

- possible missing counterpart
- possible deletion gap
- source-only message
- provider-only message
- screenshot-only item
- possible coordination
- suggested investigative lead
- needs investigator review
- AI-assisted summary

Disallowed as unreviewed AI conclusions:

- guilty
- proven conspiracy
- confirmed deletion
- evidence tampering occurred
- gang member
- criminal associate
- probable cause established
- definitive intent

If stronger language is needed, it must come from a human-authored review artifact with cited source support.

## Human Review Workflow

AI-assisted items must support explicit review state.

Minimum workflow states:

- draft
- pending_review
- approved
- rejected
- superseded

Workflow rules:

- approval must preserve AI provenance
- edits by investigators must not erase the original AI suggestion
- report inclusion must require explicit review state
- rejected items must remain traceable for audit purposes
- AI suggestions converted into leads must retain their citations and provenance

## Cloud AI and Redaction

Cloud AI is allowed only as an explicit, optional, redaction-capable path.

Required controls:

- redact sensitive values before transmission when cloud mode is used
- preserve a local redaction manifest so investigators can rehydrate results inside the app
- log whether redaction was enabled
- allow case-level or global disablement when those settings exist
- keep the provider mode visible in logs and audit events

Example redaction:

```text
Michael Johnson -> Person 1
803-555-1212 -> Phone 1
@mike_170 -> Account 1
Elm Street Apartments -> Location 1
```

## AI Logging Requirements

Operational logs for AI should capture:

- provider mode
- model name or family if available
- prompt template ID and version
- redaction enabled or disabled
- input scope summary
- schema validation status
- run status
- error category
- correlation ID
- case ID and AI run ID where applicable

Do not log by default:

- full prompts
- full responses
- raw evidence bodies
- rehydration manifests containing real sensitive values

Follow:

```text
Docs/LOGGING_GUIDELINES.md
Docs/SECURITY_GUARDRAILS.md
```

## AI in Reports

Report rules:

- Official reports must remain source-cited.
- AI-assisted content must remain labeled as AI-assisted unless fully rewritten and affirmed by a human reviewer.
- Unsupported AI conclusions must never appear as fact.
- A report may include an AI-assisted summary only if the cited supporting evidence is available for review.

## Testing Expectations For AI Features

AI-related implementation tickets must include tests for:

- schema validation
- citation presence
- prohibited language rejection
- confidence label validation
- redaction and rehydration behavior
- review-state transitions
- provenance preservation after investigator edits or approval

Use only synthetic test data.

## Good vs Bad Examples

Good:

```text
Suggested investigative lead: Two cited source messages may indicate meeting coordination. Needs investigator review.
```

Good:

```text
AI-assisted summary includes citations to art_0012/message-193 and art_0015/row-44.
```

Bad:

```text
AI determined the suspect deleted the messages and coordinated the crime.
```

Bad:

```text
Cloud AI automatically processed all case messages without redaction because review was faster that way.
```
