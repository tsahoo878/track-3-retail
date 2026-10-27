# Command policy

Claude Code runs a PreToolUse hook (`.claude/hooks/block-destructive.js`, registered in `.claude/settings.json`) on every `Bash` call and every `mcp__kestrel__run_query` call. A matching command is blocked and the reason is returned to Claude. Ask for explicit approval before a blocked command is run by other means.

This is a safety net against accidents, not a security boundary. Obfuscated commands can get past regex rules.

## Blocked

| Area | Examples |
| --- | --- |
| Recursive delete | `rm -r`, `rm -rf`, `del /s`, `rmdir /s`, `Remove-Item -Recurse` |
| Disk / filesystem | `format X:`, `mkfs`, `dd ... of=`, `shred` |
| Git history and work | `git push --force` / `-f` / `--force-with-lease`, `git reset --hard`, `git clean -f`, `git checkout .` / `git restore .`, `git branch -D`, `git push` to `main` or `master`, `--no-verify` |
| SQL (inline in `sqlcmd` or in MCP `run_query`) | `DROP TABLE/DATABASE/SCHEMA/VIEW/PROCEDURE/INDEX`, `TRUNCATE`, `ALTER TABLE/DATABASE/...`, `DELETE` or `UPDATE` without `WHERE`, `xp_cmdshell`, `SHUTDOWN` |
| EF / migrations | `dotnet ef database drop`, `dotnet ef migrations remove`, deleting anything under `Migrations/` |
| System / network | `shutdown`, `reboot`, `taskkill /f`, `kill -9`, `curl ... \| sh`, `iwr ... \| iex`, `chmod -R 777`, `npm publish`, `npm uninstall -g` |
| Secrets | `cat` / `type` / `head` / `tail` on `.env`, `.pem`, or `credentials` files |

## Allowed

- **Read-only:** `ls`, `dir`, `cat` / `type` on non-secret files, `grep`, `rg`, `git status`, `git diff`, `git log`, `git show`, `git branch`
- **Build, test, run:** `dotnet build`, `dotnet test`, `dotnet run`, `dotnet restore`, `dotnet ef migrations add`, `dotnet ef migrations list`, `npm install`, `npm start`, `npm test`, `npm run build`
- **Git writes that do not rewrite history:** `git add`, `git commit`, `git switch`, `git checkout -b`, `git pull`, `git stash`, `git push` to a non-main branch without `--force`
- **Database:** `SELECT`, `DESCRIBE`, the `list_tables` and `describe_table` MCP tools, `sqlcmd -i scripts/schema.sql` or `scripts/seed.sql` (file contents are not inspected)
- **Single-file deletes:** `rm some/file` without `-r` is not blocked

## Known gaps

- `sqlcmd -i <file>` is allowed, and the rules do not read the file. Review the file yourself if it could contain destructive SQL.
- Only inline SQL in `sqlcmd` is checked. Other database clients are not covered.
- Malformed hook input is allowed rather than blocking all tool use.

## PostToolUse: CLAUDE.md refresh

`.claude/hooks/post-commit-claudemd.js` runs after each `Bash` call. When the call was a successful `git commit`, it tells Claude which files changed so Claude can update CLAUDE.md. Commits that only touch CLAUDE.md are skipped. The hook does not commit the edit; the user reviews it. It only sees commits made through Claude's Bash tool, not from a terminal or IDE.

## Keeping in sync

Rules live in `BASH_RULES` and `SQL_RULES` in `.claude/hooks/block-destructive.js`. Update this file when you change them. The `permissions.deny` entries in `.claude/settings.json` cover the most dangerous commands as a second layer.
