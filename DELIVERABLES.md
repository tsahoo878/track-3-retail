# Deliverables: Track 3, Kestrel Outfitters (returns and refunds)

Tick each box when the file exists at exactly this path and is finished. Paths are relative to this folder.

## Documents (all tracks)
- [ ] `docs/onboarding-brief.md`
- [ ] `docs/specs/returns-refunds.md`
- [ ] `docs/prompt-journal.md` (at least 15 entries)
- [ ] `docs/mcp-security.md`
- [ ] `docs/agent-map.md`
- [ ] `docs/debug-report.md`
- [ ] `docs/refactor-note.md`
- [ ] `docs/perf-note.md` - Performance and concurrency note (before and after)
- [ ] `docs/review-report.md`
- [ ] `docs/ai-usage-reflection.md` (one page, written last)

## Claude Code setup (all tracks)
- [ ] `CLAUDE.md` (corrected so every command and statement is true)
- [ ] `.claude/rules/*` or a nested `CLAUDE.md`
- [ ] `.claude/skills/` (at least 2)
- [ ] `.claude/agents/` (at least 5: backend, frontend, test, reviewer, db-analyst)
- [ ] `.claude/settings.json` with hooks
- [ ] `.mcp.json`

## MCP server (all tracks)
- [ ] `mcp/` (the server and `mcp/tests/`)
- [ ] `db-setup/03-create-mcp-user.sql`
- [ ] `db-setup/02-mcp-views.sql`

## Pipeline and run files (all tracks)
- [ ] `.github/workflows/ci.yml` (fixed, plus a job that runs the MCP negative tests)
- [ ] `compose.yaml` (fixed)
- [ ] `README.md` (updated)

## Specific to this track: returns and refunds (RMA)

Feature
- [ ] A return window per category, held in the database and changeable without a code change
- [ ] Condition grading of each returned item
- [ ] Partial refunds and a restocking fee, where a refund never goes above the amount paid
- [ ] Restocking of returned items into the right store
- [ ] A flag for serial returners

Backend
- [ ] A new migration in `backend/db/migrations/` (number 0006 or higher; do not edit older migrations)
- [ ] `GET /api/orders/{id}/return-eligibility` (the single call: can it be returned, which items, until when)
- [ ] An endpoint to open a return against an order
- [ ] An endpoint to grade the returned items
- [ ] An endpoint to approve and refund a return
- [ ] Invalid moves return a clear 4xx error
- [ ] At least 25 tests named after acceptance criteria, for example `AC_04_RefundNeverExceedsAmountPaid`

Frontend
- [ ] `frontend/src/pages/RmaDeskPage.tsx` (the RMA desk)
- [ ] `frontend/src/pages/ReturnsDashboardPage.tsx` (returns rate dashboard)
- [ ] Loading, empty and error states on both, and validation that matches the backend

MCP business tools (in `mcp/`)
- [ ] `serial_returners`
- [ ] `refunds_exceeding_paid`
- [ ] `return_rate_by_product`
- [ ] `restock_backlog`
- [ ] `run_readonly_query` (guarded)

Ticket KO-341
- [ ] Regression test that fails on the original code and passes after the fix
- [ ] `docs/debug-report.md` with the evidence from the MCP server and the hypothesis you discarded

Review and ship (all tracks)
- [ ] Findings from the review checklist fixed or explained
- [ ] `ci.yml` and `compose.yaml` fixed so a clean checkout builds, tests and starts
- [ ] Every item in `docs/review-checklist.md` ticked or explained in `docs/review-report.md`
