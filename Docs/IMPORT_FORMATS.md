# IMPORT_FORMATS.md

## Purpose

Defines planned import formats and mapping expectations.

## Supported First

Generic CSV/XLSX imports:

- Message exports.
- Call log exports.
- Manual social media message tables.

## Required Message Fields

Minimum useful fields:

```text
timestamp
sender
recipient
message_body
platform/source_app
```

Optional fields:

```text
direction
thread_id
message_id
attachment
deleted_status
read_status
timezone
metadata
```

## Required Call Fields

Minimum useful fields:

```text
timestamp
caller
callee
direction
```

Optional fields:

```text
duration
call_type
carrier/platform
cell_site
timezone
metadata
```

## Column Mapping Synonyms

| DumpLens Field | Possible Source Columns |
|---|---|
| Timestamp | date, time, datetime, sent_at, created_at |
| Sender | from, sender, author, source, account_from |
| Recipient | to, recipient, destination, account_to |
| Message Body | body, text, message, content |
| Platform | app, service, platform, source_app |
| Direction | incoming, outgoing, direction |
| Thread ID | conversation_id, chat_id, thread, room |
| Message ID | message_id, id, guid |
| Attachment | attachment, media, filename |

## Import Validation Warnings

Create warnings for:

- Missing timestamp.
- Unparseable timestamp.
- Missing sender/recipient.
- Empty body with attachment.
- Duplicate rows.
- Unknown timezone.
- Conflicting owner information.
- Inconsistent phone number formats.
- Missing platform.
- Missing thread ID when grouping may be less reliable.

Warnings should not always block import. The user should be allowed to import imperfect data while seeing what needs review.

## Advanced Future Importers

Planned:

- JSON provider returns.
- HTML social media exports.
- XML exports.
- Cellebrite-style exports.
- Magnet-style exports.
- GrayKey-style exports.
- Oxygen/XRY-style exports.
- PDF transcripts where structured parsing is feasible.
- Screenshot/manual entry.
- OCR screenshots only as carefully labeled, controlled workflow.

## Evidence Rules

- Preserve original import file.
- Hash original import file.
- Store source artifact row/object/page locators.
- Preserve raw metadata where appropriate.
- Never modify original evidence.
