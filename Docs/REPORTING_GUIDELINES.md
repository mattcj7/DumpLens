# REPORTING_GUIDELINES.md

## Purpose

Defines reporting/export expectations for DumpLens.

## Report Principles

Reports must be:

- Clean.
- Readable.
- Defensible.
- Source-cited.
- Clear about fact vs inference.
- Clear about AI-assisted content.
- Clear about review status.

## Required Report Types

Planned:

- Case communication summary.
- Incident-window timeline.
- Missing-message/gap report.
- Warrant lead packet.
- Unknown identity/account report.
- Source import manifest.
- AI findings review log.
- Conversation reconstruction report.
- Platform-switching report.
- Entity/alias resolution report.

## Citation Requirements

Every exported artifact should cite:

- Source import name.
- Original filename.
- File hash.
- Artifact row/object/page.
- Message/call ID.
- Timestamp.
- Sender/recipient.
- Platform.
- Review status.

## AI Labeling

Reports must label:

- Source artifact.
- System-derived match.
- AI-assisted summary.
- Investigator-confirmed finding.
- Investigator note.
- Unreviewed suggestion.
- Rejected finding.

## Warrant Lead Packets

Warrant lead packets should say “suggested investigative lead,” not “probable cause established.”

Suggested sections:

1. Proposed target.
2. Reason for review.
3. Supporting artifacts.
4. Related timeline events.
5. Known identifiers.
6. Suggested record categories.
7. Limitations.
8. Investigator approval status.

## Export Hashing

Exported reports and ZIP packages must be hashed and audited when export functionality exists.

## Tests

Report/export tests should verify:

- Source citations are present.
- Hash display is correct.
- AI labeling is present.
- Review status filtering works.
- Exported file hash is stored.
- Timeline ordering is correct.
