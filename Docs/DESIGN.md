# DESIGN.md

## Purpose

High-level product design for DumpLens.

DumpLens is an offline-first investigative communications reconstruction tool. It helps investigators ingest, normalize, compare, review, and report on phone dumps, social media messages, call logs, provider returns, transcripts, screenshots/manual entries, and related communication artifacts.

## Core Product Goals

- Fast triage of large message sets.
- Cross-device conversation reconstruction.
- Missing-message and deletion-gap detection.
- Unified investigative timeline.
- Evidence-backed AI assistance.
- Lead and warrant-target suggestion as reviewable investigative leads.
- Human review and auditability.

## Non-Goals

DumpLens must not:

- Determine guilt.
- Automatically label gang membership.
- Generate unsupported probable cause statements.
- Present AI interpretation as fact.
- Modify original evidence files.
- Replace forensic tools.
- Replace human review.

## Core Screens

```text
Dashboard
Sources
Conversations
Timeline
Gaps & Deletions
Entities & Aliases
Leads
AI Findings
Reports
Settings
```

## Related Docs

```text
Docs/TECHNICAL_ARCHITECTURE.md
Docs/UI_Guidelines.md
Docs/SECURITY_GUARDRAILS.md
Docs/AI_GUARDRAILS.md
```
