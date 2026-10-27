# MCP server security

The Kestrel MCP server (`kestrel-mysql-mcp/`, Node, stdio) lets an AI client inspect sales, stock and returns data. It is read-only and sees no personal data.

## Tools
| Tool | Purpose |
|---|---|
| `list_tables`, `describe_table` | Discover the `mcp_*` views and their columns |
| `serial_returners` | Customers flagged under BR-08 (customer id and loyalty tier only, no names) |
| `refunds_exceeding_paid` | Audit of BR-04/BR-05: refunds above the prorated paid amount per line, or above captured payments per order. Expected to be empty |
| `return_rate_by_product` | Returned vs sold quantity by product, category or store (same numbers as the returns dashboard) |
| `restock_backlog` | Grade A/B lines on approved RMAs not restocked, and RMAs still awaiting approval, with age in hours |
| `run_readonly_query` | One guarded `SELECT`/`WITH` statement over the `mcp_*` views |

The domain tools use fixed, parameterised SQL (`?` placeholders, zod-validated input) in `src/queries.js` and read `mcp_*` views only.

## Layers
1. **Database account (authoritative).** `kestrel_mcp` (`db-setup/03-create-mcp-user.sql`) has `SELECT` on the `mcp_*` views only: no base tables, no writes, no DDL, 5 connections, 3600 queries/hour.
2. **Views (`db-setup/02-mcp-views.sql`).** They omit customer names, email, phone, address and date of birth, card details, provider references, and the `users` table (including password hashes).
3. **Query guard (`src/guard.js`).** `run_readonly_query` accepts one `SELECT`/`WITH` statement. It rejects `;`, comments, backslashes and backticks, `@` variables, `INTO`, `LOAD_FILE`, `SLEEP`, `BENCHMARK`, advisory locks, locking reads (`FOR UPDATE`, `FOR SHARE`, `LOCK IN SHARE MODE`), DML/DDL keywords, `information_schema`/`mysql`/`sys`/`performance_schema`, and any relation after `FROM`/`JOIN` that is not an unqualified `mcp_*` view or a CTE name. It is a fast-fail and a second line of defence; if it has a gap, the grants still hold. It can reject valid SQL it cannot parse (for example `EXTRACT(DAY FROM col)`).
4. **Execution limits (`src/index.js`).** Read-only session and transaction, 5 s timeout plus `MAX_EXECUTION_TIME`, row cap of 200 by default and 1000 at most.
5. **Secrets.** The password comes only from the environment (`MCP_DB_PASSWORD`, passed to the server as `MYSQL_PASSWORD` by `.mcp.json`); the server refuses to start without it. The server connects as `kestrel_mcp`. Do not use the `root` or `kestrel_app` credentials, and never commit the MCP password.

## Setup
```
mysql -u root -p < db-setup/02-mcp-views.sql
sed "s/CHANGE_ME/$MCP_DB_PASSWORD/" db-setup/03-create-mcp-user.sql | mysql -u root -p
export MCP_DB_PASSWORD='...'   # PowerShell: $env:MCP_DB_PASSWORD = '...'
cd kestrel-mysql-mcp && npm install
```
Run the views after the migrations, and rerun them when the schema changes. Rotate with `ALTER USER 'kestrel_mcp'@'%' IDENTIFIED BY '...'`.

## Tests
`npm test` in `kestrel-mysql-mcp/` runs `node --test` (guard tests). Database-level checks (the account cannot read base tables or write) are not automated yet.
