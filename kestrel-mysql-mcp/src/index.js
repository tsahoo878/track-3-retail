#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import mysql from "mysql2/promise";
import { z } from "zod";

// stdout carries the MCP protocol, so diagnostics go to stderr.
const log = (...args) => console.error("[kestrel-mysql-mcp]", ...args);

const config = {
  host: process.env.MYSQL_HOST ?? "127.0.0.1",
  port: Number(process.env.MYSQL_PORT ?? 3306),
  database: process.env.MYSQL_DATABASE ?? "kestrel",
  user: process.env.MYSQL_USER ?? "root",
  password: process.env.MYSQL_PASSWORD ?? "",
};

const pool = mysql.createPool({
  ...config,
  connectionLimit: 4,
  waitForConnections: true,
  multipleStatements: false,
  timezone: "Z",
});

const DEFAULT_MAX_ROWS = 200;
const HARD_MAX_ROWS = 1000;
const QUERY_TIMEOUT_MS = 5000;
const ALLOWED_LEAD = /^(select|with|show|describe|desc|explain)\b/i;
const FORBIDDEN = /\b(insert|update|delete|replace|drop|alter|create|truncate|grant|revoke|call|load|rename|lock|set|into)\b/i;

// Reject anything that is not a single read-only statement. The READ ONLY
// transaction below is the real guard; this is an early, readable check.
function validateSql(sql) {
  const trimmed = sql.trim().replace(/;\s*$/, "");
  if (!trimmed) throw new Error("Query is empty.");
  if (trimmed.includes(";")) throw new Error("Only one statement is allowed.");
  if (/--|#|\/\*/.test(trimmed)) throw new Error("SQL comments are not allowed.");
  if (!ALLOWED_LEAD.test(trimmed)) {
    throw new Error("Only SELECT, WITH, SHOW, DESCRIBE and EXPLAIN statements are allowed.");
  }
  if (FORBIDDEN.test(trimmed)) {
    throw new Error("Statement contains a keyword that is not allowed in read-only mode.");
  }
  return trimmed;
}

// Runs one statement inside a READ ONLY transaction with a row cap and timeout.
async function runReadOnly(sql, params = [], maxRows = DEFAULT_MAX_ROWS) {
  const cap = Math.min(Math.max(1, maxRows), HARD_MAX_ROWS);
  const conn = await pool.getConnection();
  try {
    await conn.query("SET SESSION TRANSACTION READ ONLY");
    await conn.query(`SET SESSION MAX_EXECUTION_TIME=${QUERY_TIMEOUT_MS}`);
    await conn.beginTransaction();
    const [rows, fields] = await conn.query({ sql, timeout: QUERY_TIMEOUT_MS }, params);
    await conn.rollback();
    const list = Array.isArray(rows) ? rows : [];
    return {
      columns: (fields ?? []).map((f) => f.name),
      rows: list.slice(0, cap),
      rowCount: Math.min(list.length, cap),
      truncated: list.length > cap,
    };
  } catch (err) {
    await conn.rollback().catch(() => {});
    throw err;
  } finally {
    conn.release();
  }
}

const asText = (value) => ({
  content: [{ type: "text", text: JSON.stringify(value, (_, v) => (typeof v === "bigint" ? v.toString() : v), 2) }],
});

const asError = (message) => ({ content: [{ type: "text", text: message }], isError: true });

const server = new McpServer({ name: "kestrel-mysql", version: "0.1.0" });

server.tool(
  "list_tables",
  "List tables and views in the Kestrel database.",
  {},
  async () => {
    const result = await runReadOnly(
      `SELECT table_name AS name, table_type AS type
         FROM information_schema.tables
        WHERE table_schema = DATABASE()
        ORDER BY table_name`,
      [],
      HARD_MAX_ROWS,
    );
    return asText(result.rows);
  },
);

server.tool(
  "describe_table",
  "Describe the columns of one table or view.",
  { name: z.string().regex(/^[A-Za-z0-9_]+$/).describe("Table name, e.g. orders") },
  async ({ name }) => {
    const result = await runReadOnly(
      `SELECT column_name AS name, column_type AS type, is_nullable AS nullable, column_key AS key_type
         FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = ?
        ORDER BY ordinal_position`,
      [name],
      HARD_MAX_ROWS,
    );
    if (result.rowCount === 0) return asError(`Unknown table '${name}'.`);
    return asText(result.rows);
  },
);

server.tool(
  "run_query",
  "Run one read-only SQL statement (SELECT, WITH, SHOW, DESCRIBE, EXPLAIN). Results are capped and time-limited to 5s.",
  {
    sql: z.string().max(4000).describe("A single read-only SQL statement"),
    maxRows: z.number().int().min(1).max(HARD_MAX_ROWS).optional().describe("Max rows to return (default 200, max 1000)"),
  },
  async ({ sql, maxRows }) => {
    let safe;
    try {
      safe = validateSql(sql);
    } catch (err) {
      return asError(`Query rejected: ${err.message}`);
    }
    try {
      const result = await runReadOnly(safe, [], maxRows ?? DEFAULT_MAX_ROWS);
      return asText(result);
    } catch (err) {
      return asError(`Query failed: ${err.message}`);
    }
  },
);

async function main() {
  if (!config.password) log("warning: MYSQL_PASSWORD is empty");
  await server.connect(new StdioServerTransport());
  log(`connected to ${config.user}@${config.host}:${config.port}/${config.database}`);
}

main().catch((err) => {
  log("fatal:", err);
  process.exit(1);
});
