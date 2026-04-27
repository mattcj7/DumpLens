# UI_Guidelines.md

## Purpose

Defines DumpLens UI/UX standards for the initial desktop shell and future UI work.

DumpLens should feel like a practical investigator tool: fast, clear, evidence-first, review-first, and careful about what it claims.

This document defines interaction and wording expectations. It does not authorize UI implementation, schema work, AI behavior, or workflow logic beyond high-level guidance.

## Product Alignment

UI work must align with:

- `Docs/DESIGN.md`
- `Docs/DECISIONS.md`
- `Docs/AI_GUARDRAILS.md`
- `Docs/SECURITY_GUARDRAILS.md`

The UI must reinforce these product truths:

- DumpLens is desktop-first and offline-first.
- Original evidence is immutable.
- AI is optional and review-only.
- Missing-message and deletion-gap analysis must avoid overclaiming.
- Source support and review status must stay visible.

## Initial Desktop Shell

Per `D0012 - WPF Selected for Initial Desktop Shell` in `Docs/DECISIONS.md`, WPF is the initial desktop shell choice.

High-level WPF guidance:

- Keep views focused on presentation, layout, accessibility, and interaction flow.
- Keep business rules, evidence logic, reconciliation logic, and AI logic out of WPF views.
- Prefer binding-friendly layouts and state-driven UI over code-behind-heavy behavior.
- Design for large datasets, long-running operations, and desktop keyboard use.
- Plan for progressive disclosure so advanced metadata does not crowd primary review tasks.

This document defines expected behavior and structure, not specific XAML, styles, or control implementations.

## Core UI Principles

- Use plain-language labels that investigators can understand quickly.
- Put source references one click away from every derived item.
- Make review status visible without opening deep dialogs.
- Start with guided workspaces, not raw tables.
- Use progressive disclosure for hashes, raw metadata, scoring details, and technical internals.
- Treat deletion and AI conclusions carefully and explicitly.
- Support large lists without freezing the desktop UI.

## App Shell Layout

DumpLens should use a consistent three-panel layout with a top bar.

```text
Top bar: global case context, search, high-value global actions
Left panel: main navigation and workspace-level filters
Center workspace: primary task surface
Right inspector: selected item details, source references, review state, and actions
```

This layout should stay stable across the main investigative screens so users do not have to relearn the shell.

## Main Navigation

Main navigation should follow the product design screens:

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

Navigation expectations:

- Keep screen names stable and plain-language.
- Use the same names in navigation, page titles, review actions, and help text.
- Show the current screen clearly.
- Preserve filters and selection state when reasonable during navigation.
- Do not hide major workflows behind ambiguous icons.

## Top Bar Expectations

The top bar provides persistent case context and the smallest set of cross-cutting controls needed throughout the app.

Expected top bar content:

- Case title.
- Case number or case identifier when present.
- Current screen title or breadcrumb context.
- Global search entry point.
- Global date range filter entry point.
- Global source filter entry point.
- Import entry point.
- Export or report entry point when applicable.
- Settings or profile entry point.

Top bar behavior:

- Keep the top bar compact, stable, and available on all main screens.
- Do not overload the top bar with screen-specific controls that belong in the workspace.
- Make active global filters visible.
- Show long-running global operations with clear progress and status.
- Keep destructive or high-risk actions out of default top-bar placement unless the workflow clearly justifies it.

## Left Panel: Navigation and Filters

The left panel serves two roles: primary navigation and workspace-level filtering.

Expected behavior:

- Keep main navigation at the top or in a clearly dominant position.
- Keep screen-specific filters below navigation or in a clearly separated section.
- Allow filters to be scanned quickly and reset easily.
- Show active filter counts or summaries when useful.
- Support collapsing filter groups when the screen has many filter options.
- Preserve filter state while the user reviews items in the center and right panels.
- Do not let filters replace the main workspace as the primary focus.

Examples of left-panel filters:

- Date range
- Source type
- Platform
- Participant
- Review status
- Gap status
- AI-assisted only

The left panel should not become a dumping ground for advanced metadata. Technical details belong in progressive disclosure or the right inspector.

## Center Workspace Behavior

The center workspace is the primary task surface. It should support focused review, comparison, and investigation work.

Expected behavior:

- Make the current task obvious within a few seconds.
- Default to investigator-readable views before raw technical detail.
- Support quick scanning of status, context, and next actions.
- Keep primary lists and timelines responsive at large scale.
- Show selection clearly so the user knows which item drives the right inspector.
- Allow side-by-side or grouped comparison where the workflow depends on comparing sources.
- Use progressive disclosure for raw fields, score internals, hashes, and parser details.

Examples of center workspace modes:

- Conversation review
- Timeline review
- Gap review queue
- Source comparison
- Lead review
- AI finding review

The center workspace should not present unsupported conclusions as if they are final findings.

## Right Inspector and Source Reference Panel

The right panel is the inspector and source reference panel. It exists to keep evidence support and review state close to the active item without replacing the primary workspace.

Expected behavior:

- Update based on the current selection in the center workspace.
- Show a concise summary first, then source support, then advanced metadata.
- Keep review state, review actions, and source references visible without excessive clicking.
- Support fast switching between source-backed items during review.
- Use progressive disclosure for raw metadata and low-frequency details.

The right inspector should be the primary place for:

- Review status
- Review notes
- Source references
- Original filename
- Source import name
- Artifact type
- Timestamp and timezone context
- Sender and recipient context
- Hash display
- Row, page, object, or record locator
- Platform or provider context
- AI-assisted provenance when applicable

## Source Reference Standards

Every derived or reviewable item should have source support one click away.

This includes:

- Messages
- Calls
- Attachments
- Timeline events
- Gap candidates
- AI findings
- Leads
- Report items

Source reference expectations:

- Show which source or sources support the item.
- Show enough locator detail to find the original record again.
- Show whether the item is directly observed, source-derived, or AI-assisted.
- Keep hash information accessible but not visually dominant in the default view.
- Make it obvious when source support is limited, partial, or unavailable.

Preferred source reference fields:

- Source import name
- Original filename
- SHA-256 hash or useful hash display
- Row, page, object, or event locator
- Timestamp
- Timezone context if relevant
- Platform or provider
- Sender and recipient
- Review status
- Advanced raw metadata disclosure

## Plain-Language Label Standards

Labels should be understandable to an investigator without requiring technical translation.

Use labels that are:

- Short
- Concrete
- Actionable
- Cautious when certainty is limited

Preferred examples:

```text
Possible missing message
Possible deletion gap
Source comparison
Needs review
Reviewed
Source-only message
Provider-only message
Screenshot-only message
Open source reference
Mark extraction limitation
AI-assisted summary
Suggested investigative lead
```

Prohibited or discouraged examples:

```text
Asymmetric artifact absence
Multi-source reconciliation matrix
Unvalidated inference state
Confirmed deletion
Evidence tampering
Probable cause established
Criminal associate
Gang member
AI conclusion
```

If technical terminology is necessary, pair it with a plain-language label or explanatory text.

## Missing-Message and Deletion-Gap Wording

DumpLens must be careful when describing items that appear in one source and not another.

Preferred wording:

```text
Possible missing message
Possible missing counterpart
Possible deletion gap
Present in one reviewed source but not located in another comparable source
Needs investigator review
Possible extraction limitation
Possible provider or retention difference
```

Prohibited wording unless independently established and documented by an investigator outside the system's automated claim:

```text
Confirmed deletion
Deleted by suspect
Evidence tampering
Intentional concealment
Proof of deletion
Probable cause established
```

Default explanation example:

```text
This item appears in one source but was not located in another comparable source. This may reflect deletion, incomplete extraction, sync differences, provider retention differences, timezone issues, or import limitations. Investigator review is required.
```

The UI must consistently communicate that absence in one source does not automatically prove deletion.

## Review-First Workflow Controls

DumpLens is review-first. Derived items and suggested findings must support explicit human review actions.

Common review states should include:

- Needs review
- Reviewed
- Confirmed by investigator
- Rejected by investigator
- Extraction limitation noted

Common review controls should include:

```text
Confirm
Reject
Needs Review
Mark Extraction Limitation
Open Source
Add Note
Create Lead
Pin to Timeline
```

Review workflow expectations:

- Keep review status visible in lists, the center workspace, and the right inspector.
- Do not silently upgrade system suggestions into confirmed findings.
- Preserve provenance when an AI-assisted or system-derived item is reviewed.
- Make it clear who or what created the item and what a human reviewer has decided.

## Timeline Visual Cue Standards

Timeline views should use visual cues plus text labels. Do not rely on color alone.

Suggested cue standards:

| Item Type | Suggested Cue |
|---|---|
| Source-backed event | Solid marker with source label |
| AI-assisted event | Dashed marker plus `AI-assisted` label |
| Investigator-confirmed item | Check or confirmed label |
| Needs review item | Question or review label |
| Possible gap | Warning marker plus `Possible gap` label |
| Extraction limitation | Limitation label or muted caution marker |

Timeline expectations:

- Show whether an item is source-backed, AI-assisted, or investigator-confirmed.
- Make missing intervals visually distinct from observed events.
- Keep the label text readable at common desktop zoom levels.
- Support keyboard focus and screen-reader-friendly text equivalents.

## AI-Assisted Content Labeling

AI output must remain clearly labeled before and after review.

Required labeling expectations:

- Use `AI-assisted` in visible item labels or badges.
- Distinguish AI-assisted summaries from source-observed events.
- Preserve AI provenance after approval, rejection, editing, or report inclusion.
- Do not present AI output as a confirmed human conclusion by default.
- Keep source references and limitations visible alongside AI-assisted items.

Preferred examples:

```text
AI-assisted summary
AI-assisted finding
Suggested investigative lead
Needs investigator review
```

Prohibited examples:

```text
AI confirmed
AI proved
Automated conclusion
Verified by model
```

## Accessibility Requirements

Accessibility is required from the start of UI work.

Minimum requirements:

- Full keyboard navigation for major workflows.
- Visible focus states.
- High-contrast support.
- Text scaling without breaking core workflows.
- Icons paired with text labels where meaning matters.
- Status not conveyed by color alone.
- Adequate hit targets for desktop pointer use.
- Readable text hierarchy and spacing.
- Screen-reader-friendly names for key controls and review status indicators.

Accessibility should be considered part of layout and wording decisions, not a late visual pass.

## Large-List Virtualization

Large lists must use virtualization or equivalent scaling strategies.

This applies to:

- Message threads
- Conversation lists
- Search results
- Source tables
- Timeline rows
- Findings queues
- Leads queues

Requirements:

- Scrolling must remain responsive for large datasets.
- Loading more items must not freeze the shell.
- Selection, keyboard movement, and inspector updates must remain stable under virtualization.
- Review badges and source-reference cues must remain visible in virtualized rows.

## Empty State Guidance

Empty states should orient the user and suggest the next meaningful action.

Good empty states:

```text
No sources imported yet. Import a phone dump, message export, call log, provider return, screenshot entry, or transcript to begin.
```

```text
No possible gaps are ready for review yet. Import at least two comparable sources and run reconciliation.
```

```text
No AI findings yet. Run AI review only after relevant sources are imported and source references are available.
```

Empty states should:

- Explain why the screen is empty.
- Suggest the next action.
- Avoid implying failure when nothing is wrong.
- Avoid technical jargon unless it is explained.

## Error and Warning Language

Errors and warnings must explain what happened, what it affects, and what the user can do next.

Bad:

```text
Import failed.
```

Better:

```text
Import failed because the timestamp column could not be parsed. Check the selected timestamp field or set the source timezone, then try again.
```

Bad:

```text
Gap confirmed.
```

Better:

```text
Possible gap created for review. One source contains messages that were not located in another comparable source.
```

Language guidance:

- Be direct.
- Be specific.
- Be plain-language.
- Avoid blame or unsupported intent.
- Distinguish system status from investigator conclusion.
- Explain recovery steps when available.

## Prohibited UI Patterns

Avoid these patterns:

- Raw tables as the default first impression for major workflows.
- Hidden or hard-to-find source support.
- Color-only review status indicators.
- AI-generated content presented as fact.
- Deletion claims presented without review context.
- Overly technical labels when plain-language labels are possible.
- Busy layouts that hide the current task.
- UI flows that let users skip review context by accident.

## Summary Standard

Future DumpLens WPF UI work should consistently answer these questions on screen:

1. What am I looking at?
2. What is the review status?
3. What source supports this?
4. What should I do next?
5. How certain is the system, and what alternatives remain?

If the UI does not answer those questions clearly, it is not aligned with DumpLens design goals.
