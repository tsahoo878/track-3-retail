import { test, describe } from "node:test";
import assert from "node:assert/strict";
import { validateSql, MAX_SQL_LENGTH } from "../src/guard.js";
import {
  refundsExceedingPaidQuery,
  restockBacklogQuery,
  returnRateQuery,
  serialReturnersQuery,
} from "../src/queries.js";

const rejects = (sql) => assert.throws(() => validateSql(sql), Error, `should have been rejected: ${sql}`);
const accepts = (sql) => assert.doesNotThrow(() => validateSql(sql), `should have been accepted: ${sql}`);

describe("guard: statements that are not a single SELECT/WITH", () => {
  for (const sql of [
    "INSERT INTO mcp_returns (id) VALUES (1)",
    "UPDATE mcp_returns SET status = 'APPROVED'",
    "DELETE FROM mcp_returns",
    "REPLACE INTO mcp_returns (id) VALUES (1)",
    "TRUNCATE TABLE mcp_returns",
    "DROP VIEW mcp_returns",
    "ALTER TABLE returns ADD x INT",
    "CREATE TABLE t (id INT)",
    "GRANT ALL ON *.* TO 'kestrel_mcp'",
    "CALL some_proc()",
    "SET @x = 1",
    "SHOW TABLES",
    "DESCRIBE mcp_returns",
    "EXPLAIN SELECT * FROM mcp_returns",
    "LOAD DATA INFILE '/etc/passwd' INTO TABLE mcp_returns",
    "",
    "   ",
  ]) {
    test(`rejects: ${sql || "(empty)"}`, () => rejects(sql));
  }

  test("rejects non-strings", () => {
    rejects(undefined);
    rejects(null);
    rejects(42);
  });

  test("rejects DML hidden inside a CTE", () => {
    rejects("WITH x AS (SELECT 1) DELETE FROM mcp_returns");
    rejects("WITH x AS (SELECT 1) UPDATE mcp_returns SET status = 'APPROVED'");
  });

  test("rejects over-long queries", () => {
    rejects(`SELECT ${"1,".repeat(MAX_SQL_LENGTH)} 1 FROM mcp_returns`);
  });
});

describe("guard: multiple statements", () => {
  test("rejects a second statement", () => {
    rejects("SELECT 1 FROM mcp_returns; DELETE FROM mcp_returns");
    rejects("SELECT 1 FROM mcp_returns; SELECT 2 FROM mcp_returns");
    rejects("SELECT 1 FROM mcp_returns;; ");
  });

  test("a single trailing semicolon is tolerated", () => {
    accepts("SELECT id FROM mcp_returns;");
  });

  test("a semicolon inside a string literal is still rejected (fail closed)", () => {
    rejects("SELECT id FROM mcp_returns WHERE rma_number = 'a;b'");
  });
});

describe("guard: comments", () => {
  for (const sql of [
    "SELECT id FROM mcp_returns -- hello",
    "SELECT id FROM mcp_returns # hello",
    "SELECT id /* hi */ FROM mcp_returns",
    "SELECT /*! SLEEP(5) */ id FROM mcp_returns",
    "SELECT id FROM mcp_returns /* unterminated",
    "SELECT id FROM mcp_returns WHERE 1=1 */",
  ]) {
    test(`rejects: ${sql}`, () => rejects(sql));
  }
});

describe("guard: base tables and other schemas", () => {
  for (const sql of [
    "SELECT * FROM customers",
    "SELECT * FROM orders",
    "SELECT * FROM users",
    "SELECT * FROM returns",
    "SELECT * FROM return_lines",
    "SELECT * FROM stock_levels",
    "SELECT * FROM payments",
    "SELECT * FROM mcp_returns r JOIN customers c ON c.id = r.customer_id",
    "SELECT * FROM mcp_returns, customers",
    "SELECT * FROM mcp_returns r, mcp_orders o, users u",
    "SELECT * FROM mcp_returns WHERE customer_id IN (SELECT id FROM customers)",
    "SELECT (SELECT COUNT(*) FROM users) FROM mcp_returns",
    "SELECT * FROM mcp_returns UNION SELECT * FROM users",
    "SELECT * FROM mcp_returns UNION ALL SELECT email, 1, 1, 1, 1, 1, 1, 1, 1 FROM customers",
    "SELECT * FROM (SELECT * FROM customers) c",
    "WITH x AS (SELECT * FROM customers) SELECT * FROM x",
    "SELECT * FROM mcp_returns JOIN orders ON 1 = 1",
    "SELECT * FROM mcp_returns LEFT JOIN customers ON 1 = 1",
    "SELECT * FROM mcp_returns NATURAL JOIN customers",
    "SELECT * FROM kestrel_customers",
    "SELECT * FROM mcpx_returns",
    "SELECT * FROM xmcp_returns",
    "SELECT 1 FROM",
  ]) {
    test(`rejects: ${sql}`, () => rejects(sql));
  }

  test("rejects information_schema, mysql, sys and performance_schema", () => {
    rejects("SELECT * FROM information_schema.tables");
    rejects("SELECT * FROM INFORMATION_SCHEMA.COLUMNS");
    rejects("SELECT * FROM mysql.user");
    rejects("SELECT * FROM sys.version");
    rejects("SELECT * FROM performance_schema.threads");
    rejects("SELECT table_name FROM information_schema . tables");
    rejects("SELECT id FROM mcp_returns WHERE id IN (SELECT 1 FROM information_schema.tables)");
  });

  test("rejects dotted names, even for mcp_* views", () => {
    rejects("SELECT * FROM kestrel.customers");
    rejects("SELECT * FROM kestrel.mcp_returns");
    rejects("SELECT * FROM mcp_returns r JOIN kestrel.mcp_orders o ON o.id = r.order_id");
  });

  test("table names are not case sensitive tricks", () => {
    rejects("SELECT * FROM CUSTOMERS");
    rejects("select * from Users");
  });
});

// Relations reached through STRAIGHT_JOIN, index hints, PARTITION, UNION TABLE/VALUES and parenthesised table
// lists used to slip past the guard; they must all be refused now.
describe("guard: relation-position bypasses", () => {
  for (const sql of [
    "SELECT * FROM mcp_returns STRAIGHT_JOIN customers",
    "SELECT * FROM mcp_returns r STRAIGHT_JOIN users",
    "SELECT * FROM mcp_returns USE INDEX (x), customers",
    "SELECT * FROM mcp_returns FORCE INDEX (x), customers",
    "SELECT * FROM mcp_returns IGNORE KEY (x), customers",
    "SELECT * FROM mcp_returns PARTITION (p0), customers",
    "SELECT * FROM mcp_returns WHERE id = 1 UNION TABLE customers",
    "SELECT * FROM mcp_returns UNION VALUES ROW(1, 2)",
    "SELECT * FROM mcp_returns JOIN (customers) ON 1",
    "SELECT * FROM (mcp_returns, customers)",
    "SELECT * FROM ((mcp_returns), customers)",
  ]) {
    test(`rejects: ${sql}`, () => rejects(sql));
  }
});

describe("guard: dangerous functions, variables and clauses", () => {
  for (const sql of [
    "SELECT SLEEP(10) FROM mcp_returns",
    "SELECT sleep ( 10 ) FROM mcp_returns",
    "SELECT BENCHMARK(100000000, MD5('x')) FROM mcp_returns",
    "SELECT LOAD_FILE('/etc/passwd') FROM mcp_returns",
    "SELECT GET_LOCK('a', 10) FROM mcp_returns",
    "SELECT * FROM mcp_returns INTO OUTFILE '/tmp/x'",
    "SELECT * FROM mcp_returns INTO DUMPFILE '/tmp/x'",
    "SELECT id INTO @x FROM mcp_returns",
    "SELECT @@version",
    "SELECT @@global.read_only FROM mcp_returns",
    "SELECT @x := 1 FROM mcp_returns",
    "SELECT @x FROM mcp_returns",
    "SELECT * FROM mcp_returns FOR UPDATE",
    "SELECT * FROM mcp_returns FOR SHARE",
    "SELECT * FROM mcp_returns LOCK IN SHARE MODE",
    "SELECT * FROM mcp_returns for   update",
    "SELECT * FROM mcp_returns\nFOR\nUPDATE",
  ]) {
    test(`rejects: ${sql.replace(/\n/g, "\\n")}`, () => rejects(sql));
  }
});

describe("guard: escaping tricks", () => {
  test("rejects backslashes, backticks and NUL", () => {
    rejects("SELECT * FROM `customers`");
    rejects("SELECT * FROM `mcp_returns`");
    rejects("SELECT 'a\\'b' FROM mcp_returns");
    rejects("SELECT * FROM mcp_returns WHERE rma_number = 'x\\' OR 1=1 -- '");
    rejects("SELECT 1 FROM mcp_returns\0");
  });

  test("rejects unterminated string literals", () => {
    rejects("SELECT * FROM mcp_returns WHERE rma_number = 'abc");
    rejects('SELECT * FROM mcp_returns WHERE rma_number = "abc');
  });

  test("a forbidden keyword outside literals is rejected even when a literal also follows", () => {
    rejects("SELECT 'x' FROM mcp_returns WHERE 1 = 1 FOR UPDATE");
  });

  test("a table name inside a string literal does not fool the table check", () => {
    rejects("SELECT * FROM 'mcp_returns'");
    rejects("SELECT * FROM \"mcp_returns\"");
  });
});

describe("guard: allowed queries", () => {
  test("a plain SELECT on an mcp view", () => {
    accepts("SELECT * FROM mcp_returns");
    accepts("select id, status from mcp_returns where status = 'DRAFT' order by id desc limit 10");
    accepts("SELECT COUNT(*) AS n FROM mcp_return_lines WHERE grade IN ('A', 'B') AND restocked = 0");
    accepts("SELECT id FROM MCP_RETURNS");
  });

  test("returns the trimmed statement without the trailing semicolon", () => {
    assert.equal(validateSql("  SELECT id FROM mcp_returns ;  ").trim(), "SELECT id FROM mcp_returns");
  });

  test("joins of mcp views, with aliases", () => {
    accepts(`SELECT r.rma_number, rl.grade, rl.refund_amount
               FROM mcp_returns r
               JOIN mcp_return_lines rl ON rl.return_id = r.id
               LEFT JOIN mcp_orders o ON o.id = r.order_id
              WHERE r.status = 'APPROVED'`);
    accepts("SELECT * FROM mcp_returns AS r INNER JOIN mcp_return_lines AS rl ON rl.return_id = r.id");
    accepts("SELECT * FROM mcp_returns r, mcp_return_lines rl WHERE rl.return_id = r.id");
  });

  test("subqueries and unions over mcp views", () => {
    accepts("SELECT * FROM mcp_returns WHERE order_id IN (SELECT id FROM mcp_orders WHERE status = 'PAID')");
    accepts("SELECT * FROM (SELECT customer_id, COUNT(*) n FROM mcp_returns GROUP BY customer_id) t WHERE n > 2");
    accepts("SELECT id FROM mcp_returns UNION ALL SELECT id FROM mcp_orders");
  });

  test("a CTE that references mcp views", () => {
    accepts(`WITH approved AS (SELECT * FROM mcp_returns WHERE status = 'APPROVED')
             SELECT a.id, COUNT(*) FROM approved a JOIN mcp_return_lines rl ON rl.return_id = a.id GROUP BY a.id`);
    accepts(`WITH a AS (SELECT id FROM mcp_returns), b AS (SELECT id FROM mcp_orders)
             SELECT * FROM a JOIN b ON a.id = b.id`);
    accepts("WITH RECURSIVE n AS (SELECT 1 AS i UNION ALL SELECT i + 1 FROM n WHERE i < 5) SELECT * FROM n");
    accepts("WITH x (id, st) AS (SELECT id, status FROM mcp_returns) SELECT * FROM x");
  });

  test("window functions with PARTITION BY are not mistaken for table partitions", () => {
    accepts("SELECT id, ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY id) AS rn FROM mcp_returns");
    accepts("SELECT customer_id, SUM(refund_total) OVER (PARTITION BY customer_id) AS s FROM mcp_returns");
  });

  test("IN (SELECT ...) subqueries and comma joins over mcp views", () => {
    accepts("SELECT * FROM mcp_returns WHERE customer_id IN (SELECT customer_id FROM mcp_orders)");
    accepts("SELECT * FROM mcp_returns r, mcp_orders o, mcp_return_lines rl WHERE o.id = r.order_id AND rl.return_id = r.id");
  });

  test("CTE joins", () => {
    accepts(`WITH r AS (SELECT * FROM mcp_returns), o AS (SELECT * FROM mcp_orders)
             SELECT * FROM r JOIN o ON o.id = r.order_id`);
  });

  test("keywords inside string literals are data, not SQL", () => {
    accepts("SELECT * FROM mcp_returns WHERE rma_number = 'DELETE FROM customers'");
    accepts("SELECT * FROM mcp_returns WHERE rma_number LIKE '%sleep%' OR rma_number = 'information_schema'");
    accepts("SELECT 'it''s fine' AS s FROM mcp_returns");
  });

  test("a CTE name does not open a door to a base table of the same name", () => {
    rejects("WITH x AS (SELECT 1) SELECT * FROM customers");
    rejects("SELECT * FROM x");
  });
});

describe("domain query builders", () => {
  const placeholders = (sql) => (sql.match(/\?/g) ?? []).length;
  const built = {
    serialReturners: serialReturnersQuery({ days: 180, minRmas: 3, minOrders: 3, minRatePct: 40 }),
    refundsExceedingPaid: refundsExceedingPaidQuery({ from: null, to: null }),
    refundsExceedingPaidRange: refundsExceedingPaidQuery({ from: "2026-01-01", to: "2026-12-31" }),
    restockBacklog: restockBacklogQuery({ storeId: null, olderThanHours: 0 }),
    restockBacklogStore: restockBacklogQuery({ storeId: 2, olderThanHours: 24 }),
    returnRateProduct: returnRateQuery({ from: "2026-01-01", to: "2026-12-31", groupBy: "product", limit: 20 }),
    returnRateCategory: returnRateQuery({ from: "2026-01-01", to: "2026-12-31", groupBy: "category", limit: 20 }),
    returnRateStore: returnRateQuery({ from: "2026-01-01", to: "2026-12-31", groupBy: "store", limit: 20 }),
  };

  for (const [name, q] of Object.entries(built)) {
    test(`${name}: passes the guard (reads only mcp_* views)`, () => {
      assert.doesNotThrow(() => validateSql(q.sql));
    });

    test(`${name}: every ? has a parameter`, () => {
      assert.equal(placeholders(q.sql), q.params.length);
    });
  }

  test("caller input only ever travels as parameters", () => {
    const evil = "2026-01-01'; DROP TABLE returns; --";
    const q = refundsExceedingPaidQuery({ from: evil, to: evil });
    assert.ok(!q.sql.includes("DROP"));
    assert.ok(q.params.includes(evil));
  });

  test("an unknown groupBy is refused rather than interpolated", () => {
    assert.throws(() => returnRateQuery({ from: "a", to: "b", groupBy: "p.id; DROP TABLE x", limit: 5 }), /Unknown groupBy/);
  });

  test("BR-08 and return-rate queries count APPROVED RMAs only, the audit counts everything not rejected", () => {
    const serial = serialReturnersQuery({ days: 180, minRmas: 3, minOrders: 3, minRatePct: 40 }).sql;
    assert.match(serial, /r\.status = 'APPROVED'/);
    assert.doesNotMatch(serial, /DRAFT/);
    for (const groupBy of ["product", "category", "store"]) {
      const rate = returnRateQuery({ from: "2026-01-01", to: "2026-12-31", groupBy, limit: 20 }).sql;
      assert.match(rate, /r\.status = 'APPROVED'/, groupBy);
    }
    const audit = refundsExceedingPaidQuery({ from: null, to: null }).sql;
    assert.match(audit, /r\.status <> 'REJECTED'/);
    assert.doesNotMatch(audit, /status = 'APPROVED'/);
  });

  test("serial returners uses the thresholds it is given and selects no names", () => {
    const q = serialReturnersQuery({ days: 90, minRmas: 5, minOrders: 4, minRatePct: 55 });
    assert.deepEqual(q.params, [90, 90, 5, 4, 55]);
    assert.doesNotMatch(q.sql, /first_name|last_name|email|phone|address|date_of_birth|postcode/i);
  });
});
