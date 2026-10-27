# MCP server security

The Kestrel MCP server (`mcp/Kestrel.Mcp`, stdio) lets an AI client inspect sales and stock data. It is read-only and sees no personal data.

## Layers
1. **Database account (authoritative).** `kestrel_mcp` (`db-setup/03-create-mcp-user.sql`) has `SELECT` on the `mcp_*` views only: no base tables, no writes, no DDL, 5 connections, 3600 queries/hour.
2. **Views (`db-setup/02-mcp-views.sql`).** They omit customer names, email, phone, address and date of birth, card details, provider references, and the `users` table (including password hashes).
3. **Query guard (`QueryGuard.cs`).** `run_readonly_query` accepts one `SELECT`/`WITH` statement. It rejects `;`, comments, backslashes outside literals, `@` variables, `INTO`, `LOAD_FILE`, `SLEEP`, `BENCHMARK`, locking reads, DML/DDL keywords, `information_schema`/`mysql`/`sys`, and any relation that is not an `mcp_*` view or a CTE. This is a fast-fail and a second line of defence; if it has a gap, the grants still hold.
4. **Execution limits (`ReadOnlyDb.cs`).** Read-only session and transaction, 5 s command timeout plus `MAX_EXECUTION_TIME`, row cap of 200 by default and 1000 at most.
5. **Secrets.** The connection string comes only from `KESTREL_MCP_CONNECTION`; the server refuses to start without it. Do not reuse the `root` or `kestrel_app` credentials, and never commit the MCP password.

## Setup
```
mysql -u root -p < db-setup/02-mcp-views.sql
sed "s/CHANGE_ME/$MCP_DB_PASSWORD/" db-setup/03-create-mcp-user.sql | mysql -u root -p
export KESTREL_MCP_CONNECTION="Server=127.0.0.1;Port=3306;Database=kestrel;User ID=kestrel_mcp;Password=$MCP_DB_PASSWORD"
```
Run the views after the migrations, and rerun them when the schema changes. Rotate with `ALTER USER 'kestrel_mcp'@'%' IDENTIFIED BY '...'`.

## Tests
`dotnet test` in `mcp/` runs the negative tests for the guard (about 40 cases). CI job `mcp-tests` runs them. Database-level checks (the account cannot read base tables or write) are not automated yet.
