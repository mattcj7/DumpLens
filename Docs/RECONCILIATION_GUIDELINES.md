# RECONCILIATION_GUIDELINES.md

## Purpose

Defines rules for message matching, missing counterpart detection, and gap-window review.

## Matching Signals

Use weighted signals:

| Signal | Weight |
|---|---:|
| Provider message ID exact match | 100 |
| Body hash exact match | 35 |
| Normalized body exact match | 25 |
| Sender/recipient inverse match | 25 |
| Timestamp within 5 seconds | 25 |
| Timestamp within 60 seconds | 15 |
| Same platform | 10 |
| Same source-native thread ID | 20 |
| Attachment hash match | 35 |
| Participant overlap | 15 |
| Directionally consistent across devices | 15 |
| Manual investigator link | Override |

## Score Thresholds

| Score | Label | Action |
|---:|---|---|
| 85+ | High | Auto-suggest matched |
| 65-84 | Medium | Suggest match; review optional |
| 45-64 | Low | Ambiguous; review required |
| <45 | Weak | Do not match automatically |

## Short/Generic Message Rule

Short or generic messages require stronger supporting signals.

Examples:

```text
ok
yes
no
call me
where
bet
```

Do not match generic messages on body alone.

## Missing Counterpart Logic

A missing counterpart candidate may be created when:

1. Source A and Source B both contain the same conversation.
2. There are matched messages before and after the window.
3. A message appears in Source A.
4. No credible match appears in Source B.
5. Source B would reasonably be expected to contain the message.

## Required Alternative Explanations

Gap/missing counterpart findings must store alternatives:

- Incomplete extraction.
- App sync differences.
- Different retention behavior.
- Timezone conversion issue.
- Duplicate account/device confusion.
- Provider export limitation.
- Import mapping error.

## Language Rules

Use:

```text
possible missing counterpart
possible missing-message window
possible deletion gap
needs investigator review
```

Avoid:

```text
confirmed deletion
evidence tampering
proof of consciousness of guilt
```

## Tests

Follow `Docs/TESTING.md`.
