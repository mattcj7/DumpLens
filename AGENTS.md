# DumpLens Root Agent Instructions

This root `AGENTS.md` is the entry point for coding agents. The authoritative workflow and guardrails live in `Docs/AGENTS.md`.

Before making any change:

```text
1. Read Docs/AGENTS.md first.
2. Read Docs/TICKETS.md and identify the current active ticket.
3. Read the required project, testing, logging, decision, security, AI, and UI guidance listed in Docs/AGENTS.md.
```

Then work only on the active ticket and its acceptance criteria.

Do not implement out-of-scope features. Do not mutate original evidence. Do not add unsupported AI/legal conclusions. Add or update tests when the ticket changes behavior. Add evidence-safe logging when the ticket changes operational behavior.
