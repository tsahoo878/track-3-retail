// SQL guard for run_readonly_query. It is a fast-fail and a second line of defence:
// the authoritative controls are the kestrel_mcp grants (SELECT on mcp_* views only)
// and the READ ONLY transaction in index.js.

export const MAX_SQL_LENGTH = 4000;

const FORBIDDEN_KEYWORDS =
  /\b(insert|update|delete|replace|drop|alter|create|truncate|grant|revoke|call|load|rename|lock|set|into|handler|do|prepare|execute|deallocate|outfile|dumpfile)\b/i;
const FORBIDDEN_FUNCTIONS = /\b(load_file|sleep|benchmark|get_lock|release_lock|release_all_locks|is_free_lock|is_used_lock|master_pos_wait|source_pos_wait|sys_exec|sys_eval)\b/i;
const FORBIDDEN_SCHEMAS = /\b(information_schema|mysql|sys|performance_schema)\b/i;
const LOCKING_READ = /\bfor\s+(update|share)\b|\block\s+in\s+share\s+mode\b/i;
// Constructs this guard cannot parse reliably: reject them outright (fail closed).
const FORBIDDEN_CONSTRUCTS = [
  [/\bstraight_join\b/i, "STRAIGHT_JOIN is not allowed."],
  [/\b(use|force|ignore)\s+(index|key)\b/i, "Index hints are not allowed."],
  [/\bpartition\s*\(/i, "Table PARTITION selection is not allowed (window PARTITION BY is fine)."],
  [/\bunion\s+(all\s+|distinct\s+)?(table|values)\b/i, "TABLE and VALUES statements are not allowed."],
  [/(^|[^a-z0-9_$.])(table|values)\s+[a-z_(]/i, "TABLE and VALUES statements are not allowed."],
];
const VIEW_NAME = /^mcp_[a-z0-9_]+$/i;

// Replace every quoted literal with '' so keyword checks do not trip on data, and
// so identifiers hidden inside literals cannot be mistaken for SQL. Returns null
// when a quote is left open.
function stripLiterals(sql) {
  const stripped = sql.replace(/'(?:[^']|'')*'|"(?:[^"]|"")*"/g, "''");
  return /['"]/.test(stripped.replace(/''/g, "")) ? null : stripped;
}

// Names declared by WITH [RECURSIVE] a [(cols)] AS ( ... ), b AS ( ... )
function cteNames(sql) {
  const names = new Set();
  if (!/^with\b/i.test(sql)) return names;
  const re = /(?:\bwith(?:\s+recursive)?|,)\s*([a-z_][a-z0-9_]*)\s*(?:\([^()]*\))?\s*as\s*\(/gi;
  for (const m of sql.matchAll(re)) names.add(m[1].toLowerCase());
  return names;
}

function tokenize(sql) {
  return sql.match(/[a-z_][a-z0-9_$]*(?:\.[a-z_][a-z0-9_$]*)*|\d+(?:\.\d+)?|[(),]|[^\s\w(),]+/gi) ?? [];
}

// Every relation named after FROM / JOIN (including comma lists) must be an mcp_* view or a CTE.
function checkRelations(sql, ctes) {
  const tokens = tokenize(sql);
  const lower = tokens.map((t) => t.toLowerCase());
  const reserved = new Set([
    "where", "group", "order", "having", "limit", "union", "join", "inner", "left", "right", "cross",
    "natural", "on", "using", "as", "select", "from", "straight_join", "outer", "full", "window",
  ]);

  for (let i = 0; i < lower.length; i++) {
    if (lower[i] !== "from" && lower[i] !== "join") continue;
    let j = i + 1;
    for (;;) {
      const tok = tokens[j];
      if (tok === undefined) throw new Error("Expected a table name after FROM or JOIN.");
      if (tok === "(") {
        // Only a subquery (SELECT/WITH) may follow; any other parenthesised table list could hide relations.
        if (lower[j + 1] !== "select" && lower[j + 1] !== "with") {
          throw new Error("Parenthesised table references are not allowed; use a plain mcp_* view or a SELECT subquery.");
        }
        break; // subquery: its own FROM is checked when the scan reaches it
      }
      const name = tok.toLowerCase();
      if (name.includes(".")) throw new Error(`Only unqualified mcp_* views may be queried (found '${tok}').`);
      if (reserved.has(name) || !/^[a-z_][a-z0-9_$]*$/i.test(tok)) throw new Error("Expected a table name after FROM or JOIN.");
      if (!VIEW_NAME.test(tok) && !ctes.has(name)) {
        throw new Error(`Only mcp_* views may be queried ('${tok}' is not allowed).`);
      }
      j++;
      // optional alias
      if (lower[j] === "as") j++;
      if (tokens[j] !== undefined && /^[a-z_][a-z0-9_$]*$/i.test(tokens[j]) && !reserved.has(lower[j])) j++;
      if (tokens[j] === ",") {
        j++;
        continue; // another relation in the same FROM list
      }
      break;
    }
  }
}

// Reject anything that is not a single read-only SELECT / WITH statement over mcp_* views.
// Returns the trimmed statement, or throws Error with a user-readable reason.
export function validateSql(sql) {
  if (typeof sql !== "string") throw new Error("Query must be a string.");
  const trimmed = sql.trim().replace(/;\s*$/, "");
  if (!trimmed) throw new Error("Query is empty.");
  if (trimmed.length > MAX_SQL_LENGTH) throw new Error("Query is too long.");
  if (trimmed.includes(";")) throw new Error("Only one statement is allowed.");
  if (/--|#|\/\*|\*\//.test(trimmed)) throw new Error("SQL comments are not allowed.");
  if (/[\\`\0]/.test(trimmed)) throw new Error("Backslashes, backticks and NUL characters are not allowed.");
  if (!/^(select|with)\b/i.test(trimmed)) throw new Error("Only SELECT and WITH statements are allowed.");

  const code = stripLiterals(trimmed);
  if (code === null) throw new Error("Unterminated string literal.");

  if (code.includes("@")) throw new Error("User and system variables (@) are not allowed.");
  if (FORBIDDEN_KEYWORDS.test(code)) throw new Error("Statement contains a keyword that is not allowed in read-only mode.");
  if (FORBIDDEN_FUNCTIONS.test(code)) throw new Error("Statement uses a function that is not allowed (LOAD_FILE, SLEEP, BENCHMARK, locks).");
  if (LOCKING_READ.test(code)) throw new Error("Locking reads (FOR UPDATE, FOR SHARE, LOCK IN SHARE MODE) are not allowed.");
  if (FORBIDDEN_SCHEMAS.test(code)) throw new Error("information_schema, mysql, sys and performance_schema are not allowed.");

  for (const [re, msg] of FORBIDDEN_CONSTRUCTS) if (re.test(code)) throw new Error(msg);

  checkRelations(code, cteNames(code));
  return trimmed;
}
