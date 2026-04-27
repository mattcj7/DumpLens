# UI_Guidelines.md

## Purpose

Defines DumpLens UI/UX standards.

DumpLens should feel like a practical investigator tool: fast, clear, evidence-first, and defensible.

## Core Layout

Use a consistent three-panel layout:

```text
Top bar: Case / Search / Date Filter / Source Filter / Import / Export
Left rail: Navigation and filters
Center: Main work area
Right inspector: Details, source references, actions, review controls
```

## Main Navigation

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

## Top Bar

The top bar should include:

- Case title.
- Case number.
- Global search.
- Date range filter.
- Source filter.
- Import button.
- Export/report button.
- User/profile/settings menu.

## Investigator-Friendly Design

Prefer:

- Clear labels.
- Plain-language warnings.
- Guided next actions.
- Source references.
- Review status.
- Practical filters.

Avoid:

- Data-science jargon.
- Raw tables as the first user experience.
- Flashy/gimmicky design.
- Color-only status meaning.
- Hidden source support.

## Plain-Language Label Examples

Use:

```text
Possible missing message
Source comparison
Needs review
Possible deletion gap
Source-only message
Provider-only message
Screenshot-only message
```

Avoid:

```text
Asymmetric artifact absence
Multi-source reconciliation matrix
Unvalidated inference state
Confirmed deletion
Evidence tampering
Probable cause established
```

## Source References

Every derived item should have source support one click away:

- Message.
- Call.
- Attachment.
- Timeline event.
- Finding.
- Lead.
- Report item.

The right inspector should show:

- Source import name.
- Original filename.
- File hash.
- Row/object/page locator.
- Timestamp.
- Platform.
- Sender/recipient.
- Review status.
- Raw metadata in advanced disclosure.

## Review-First Workflow

Important items must support review:

- Matches.
- Missing counterpart candidates.
- Gap windows.
- AI findings.
- Leads.
- Report items.

Common actions:

```text
Confirm
Reject
Needs Review
Mark Extraction Limitation
Create Lead
Pin to Timeline
Add Note
Open Source
```

## Gap/Deletion Warning Language

Default gap explanation:

```text
This message appears in one source but was not located in another comparable source. This may indicate deletion, incomplete extraction, platform sync differences, retention differences, provider/export limitations, timezone issues, or import mapping problems. Investigator review is required.
```

Do not call something confirmed deletion unless the investigator explicitly documents that conclusion separately.

## Timeline Visual Cues

Use visual cues plus text labels:

| Item Type | Suggested Cue |
|---|---|
| Source artifact | Solid item |
| AI-suggested event | Dashed/AI label |
| Investigator-confirmed | Check label |
| Needs review | Question label |
| Possible gap | Warning label |

## Empty States

Empty states should guide the user:

```text
No sources imported yet. Import a phone dump, message export, call log, provider return, or transcript to begin.
```

```text
No gaps detected yet. Import at least two comparable sources and run reconciliation.
```

## Error and Warning Language

Errors should explain what happened and what the user can do.

Bad:

```text
Import failed.
```

Good:

```text
Import failed because the timestamp column could not be parsed. Check the selected timestamp field or set the source timezone, then try again.
```

## Accessibility

- Support keyboard navigation.
- Support high contrast.
- Support resizable text.
- Use icons plus labels.
- Avoid relying on color only.
- Keep controls large enough for desktop use.

## Large Lists

Use virtualization for:

- Message threads.
- Source tables.
- Search results.
- Timeline rows.
- Findings queue.
- Leads queue.
