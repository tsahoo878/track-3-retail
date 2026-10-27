# kestrel-mysql-mcp

A read-only MCP server (stdio) for querying the Kestrel MySQL database.

## Tools

- `list_tables`: tables and views in the database.
- `describe_table(name)`: columns, types, nullability and key info.
- `serial_returners(days?, minRmas?, minOrders?, minRatePct?)`: customers flagged as serial returners (BR-08), no names.
- `refunds_exceeding_paid(from?, to?)`: audit of refunds above the amount paid; should be empty.
- `return_rate_by_product(from, to, groupBy?, limit?)`: return rate by product, category or store.
- `restock_backlog(storeId?, olderThanHours?)`: approved A/B lines not restocked and RMAs awaiting approval.
- `run_readonly_query(sql, maxRows?)`: one guarded SELECT/WITH over the `mcp_*` views (see `docs/mcp-security.md`). Results are capped (default 200, max 1000) and limited to 5 seconds.

## Configuration

Set these environment variables (defaults in parentheses):

- `MYSQL_HOST` (127.0.0.1)
- `MYSQL_PORT` (3306)
- `MYSQL_DATABASE` (kestrel)
- `MYSQL_USER` (kestrel_mcp)
- `MYSQL_PASSWORD` (required; `.mcp.json` passes `${MCP_DB_PASSWORD}`)

Never commit the password. `.mcp.json` reads it from your shell environment.

## Setup

```sh
cd kestrel-mysql-mcp
npm install
export MCP_DB_PASSWORD='...'   # PowerShell: $env:MCP_DB_PASSWORD = '...'
```

Claude Code picks the server up from the project `.mcp.json` (run `/mcp` to check it is connected).

## Safety

`run_readonly_query` passes `src/guard.js` and then runs in a `SET SESSION TRANSACTION READ ONLY` transaction. The `kestrel_mcp` grants (SELECT on `mcp_*` views only) are the real guard. Run `npm test` for the guard tests.
