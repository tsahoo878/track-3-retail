---
name: prompt-journal
description: Use when a task requires a prompt journal, or the user says "log this", "journal", or "/prompt-journal". Appends an entry to docs/prompt-journal.md after each prompt/response cycle, while working, not at the end.
---

# Prompt Journal

Keep `docs/prompt-journal.md` up to date as the work happens. Append one entry per meaningful prompt/response cycle, immediately after that cycle finishes. Never batch entries at the end of the session, and never backfill entries from memory without saying so.

## File setup

If `docs/prompt-journal.md` does not exist, create it with:

```
# Prompt Journal

Entries: 0 / 15 minimum

---
```

## Entry template

Append entries in order. Number them sequentially and never rewrite earlier entries except to fill in a `pending` decision the user has answered.

```
## Entry N: <short title>

- **Prompt:** > verbatim prompt text, as a blockquote
- **Mode:** plan | normal | auto-accept | agent
- **What came back:** 2-4 sentences: files touched, key output, errors
- **Decision:** accepted | rejected | rewrote | pending
- **Why:** concrete reasoning: what was right, what was wrong, what changed
```

Update the `Entries: N / 15 minimum` counter line each time you append.

## Rules

- **Prompt verbatim.** Copy the user's prompt exactly. Do not paraphrase or merge prompts.
- **Mode is the real mode.** Use the mode that was actually active for that prompt (plan, normal, auto-accept, or agent). If it is unclear, ask the user rather than guessing.
- **Decision belongs to the user.** Accepted, rejected, or rewrote describes what the user did with the output. If the user has not said, write `pending` and ask at a natural break. Do not mark a decision the user has not made.
- **Be honest.** Record rejections, rewrites, and errors, including failures in your own output. Do not soften them.
- **Warn near the end.** If the session is ending and the count is below 15, say so.

## `/prompt-journal check`

Read `docs/prompt-journal.md` and report:

1. The entry count against the 15 minimum.
2. Any entry missing one of the five fields (prompt, mode, what came back, decision, why).
3. Any entry still marked `pending`.

Do not add entries during a check.
