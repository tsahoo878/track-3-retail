# Prompt Journal

Entries: 2 / 15 minimum

---

## Entry 1: Request to create a prompt-journal skill

- **Prompt:** > i want to create a skill for below
  > Keep a prompt journal as you go. Write docs/prompt-journal.md while you work, not at the end. You need at least 15 entries. Each entry has: the prompt you gave Claude Code; the Claude Code mode you used: plan, normal, auto-accept, or agent; what came back; whether you accepted it, rejected it or rewrote it, and why.
- **Mode:** plan
- **What came back:** Explored the repo (`docs/`, `.claude/`) with a read-only search, then wrote a plan to `C:\Users\labuser\.claude\plans\i-want-to-create-polished-yao.md` for a project skill at `.claude/skills/prompt-journal/SKILL.md`. Plan covered frontmatter, entry template, rules, and a `check` subcommand.
- **Decision:** accepted
- **Why:** The plan matched the assignment's five required fields and the "write while working" requirement. Scope was small and nothing was edited until approval.

## Entry 2: Approve plan and implement

- **Prompt:** > (plan approval via ExitPlanMode; no text prompt)
- **Mode:** plan (exited to normal editing after approval)
- **What came back:** Created `.claude/skills/prompt-journal/SKILL.md` and this journal file. The skill enforces verbatim prompts, the real mode, user-owned decisions (marked `pending` until answered), and a `check` subcommand.
- **Decision:** pending
- **Why:** Waiting on the user to confirm the skill works in their workflow. Entry format and the `pending` rule may need adjustment after the first real use.
