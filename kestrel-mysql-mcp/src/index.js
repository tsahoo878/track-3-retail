#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import mysql from "mysql2/promise";
import { z } from "zod";
import { validateSql } from "./guard.js";
import { refundsExceedingPaidQuery, restockBacklogQuery, returnRateQuery, serialReturnersQuery } from "./queries.js";

// stdout carries the MCP protocol, so diagnostics go to stderr.
const log = (...args) => console.error("[kestrel-mysql-mcp]", ...args);

const config = {
  host: process.env.MYSQL_HOST ?? "127.0.0.1",
  port: Number(process.env.MYSQL_PORT ?? 3306),
  database: process.env.MYSQL_DATABASE ?? "kestrel",
  user: process.env.MYSQL_USER ?? "kestrel_mcp",
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

const dateString = z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "Use YYYY-MM-DD");

// Shared wrapper for the domain tools: run parameterised SQL and shape the result.
async function runTool({ sql, params }, maxRows = HARD_MAX_ROWS) {
  try {
    return asText(await runReadOnly(sql, params, maxRows));
  } catch (err) {
    return asError(`Query failed: ${err.message}`);
  }
}

server.tool(
  "serial_returners",
  "Customers flagged as serial returners (BR-08): at least minRmas RMAs across minOrders orders and at least minRatePct percent of purchased value returned, within the last N days. Returns customer_id and loyalty tier only, no names.",
  {
    days: z.number().int().min(1).max(730).default(180),
    minRmas: z.number().int().min(1).max(100).default(3),
    minOrders: z.number().int().min(1).max(100).default(3),
    minRatePct: z.number().min(0).max(100).default(40),
  },
  async (args) => runTool(serialReturnersQuery(args)),
);

server.tool(
  "refunds_exceeding_paid",
  "Audit (BR-04/BR-05): order lines whose refunds, summed over every RMA, exceed the prorated amount paid, and orders whose refunds exceed captured payments. Should always be empty. Optional from/to (YYYY-MM-DD) limit the audit to orders with an RMA created in that range.",
  { from: dateString.optional(), to: dateString.optional() },
  async ({ from, to }) => runTool(refundsExceedingPaidQuery({ from: from ?? null, to: to ?? null })),
);

server.tool(
  "return_rate_by_product",
  "Return rate (returned qty / sold qty) between from and to (YYYY-MM-DD, inclusive), grouped by product (default), category or store, highest rate first. Matches the returns dashboard.",
  {
    from: dateString,
    to: dateString,
    groupBy: z.enum(["product", "category", "store"]).default("product"),
    limit: z.number().int().min(1).max(200).default(20),
  },
  async ({ from, to, groupBy, limit }) => {
    if (to < from) return asError("'to' must not be before 'from'.");
    return runTool(returnRateQuery({ from, to, groupBy, limit }));
  },
);

server.tool(
  "restock_backlog",
  "Grade A/B lines on APPROVED RMAs that have not been restocked, plus DRAFT RMAs still awaiting approval, with age in hours (BR-07 same-day restock). Optional storeId and olderThanHours (default 0).",
  {
    storeId: z.number().int().positive().optional(),
    olderThanHours: z.number().int().min(0).max(24 * 365).default(0),
  },
  async ({ storeId, olderThanHours }) => runTool(restockBacklogQuery({ storeId: storeId ?? null, olderThanHours })),
);

server.tool(
  "run_readonly_query",
  "Run one read-only SELECT or WITH statement over the mcp_* views. Rejects DML, comments, variables, locking reads, non-mcp relations and information_schema. Results are capped and time-limited to 5s.",
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
  if (!config.password) {
    log("fatal: MYSQL_PASSWORD is not set. Use the kestrel_mcp account and never commit its password.");
    process.exit(1);
  }
  await server.connect(new StdioServerTransport());
  log(`connected to ${config.user}@${config.host}:${config.port}/${config.database}`);
}

main().catch((err) => {
  log("fatal:", err);
  process.exit(1);
});
