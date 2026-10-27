# kestrel-mysql-mcp

A read-only MCP server (stdio) for querying the Kestrel MySQL database.

## Tools

- `list_tables`: tables and views in the database.
- `describe_table(name)`: columns, types, nullability and key info.
- `run_query(sql, maxRows?)`: one read-only statement (SELECT, WITH, SHOW, DESCRIBE, EXPLAIN). Results are capped (default 200, max 1000) and limited to 5 seconds.

## Configuration

Set these environment variables (defaults in parentheses):

- `MYSQL_HOST` (127.0.0.1)
- `MYSQL_PORT` (3306)
- `MYSQL_DATABASE` (kestrel)
- `MYSQL_USER` (root)
- `MYSQL_PASSWORD` (empty)

Never commit the password. `.mcp.json` reads it from your shell environment.

## Setup

```sh
cd kestrel-mysql-mcp
npm install
export MYSQL_PASSWORD='...'   # PowerShell: $env:MYSQL_PASSWORD = '...'
```

Claude Code picks the server up from the project `.mcp.json` (run `/mcp` to check it is connected).

## Safety

Queries go through two checks: a statement allow-list and a `SET SESSION TRANSACTION READ ONLY` transaction. The transaction is the real guard. The connecting account can still read anything it is granted, so for anything beyond local testing use a SELECT-only database user.
