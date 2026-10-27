#!/usr/bin/env node
// PreToolUse hook: blocks destructive shell commands and SQL.
// Claude Code passes the tool call as JSON on stdin. Exit 2 blocks the call and
// sends the stderr message back to Claude. Rules are documented in docs/command-policy.md.

const BASH_RULES = [
  // Filesystem
  [/\brm\b(?:\s+\S+)*\s+-[a-zA-Z]*[rR]/, 'recursive rm'],
  [/\brm\b(?:\s+\S+)*\s+--recursive\b/, 'recursive rm'],
  [/\b(?:del|erase|rmdir|rd)\b.*\s\/s\b/i, 'recursive delete (/s)'],
  [/Remove-Item\b.*-Recurse/i, 'recursive Remove-Item'],
  [/\bformat\s+[a-z]:/i, 'disk format'],
  [/\bmkfs\b/, 'filesystem creation'],
  [/\bdd\b.*\bof=/, 'raw disk write (dd)'],
  [/\bshred\b/, 'shred'],
  [/\b(?:rm|del|erase|Remove-Item)\b.*Migrations/i, 'deleting EF Migrations (never edit them)'],

  // Git
  [/\bgit\s+push\b.*(?:\s--force(?:-with-lease)?\b|\s-f\b|\s\+\S)/, 'force push'],
  [/\bgit\s+push\b.*\s(?:origin\s+)?(?:main|master)\b/, 'push to main/master'],
  [/\bgit\s+reset\b.*--hard/, 'git reset --hard'],
  [/\bgit\s+clean\b.*-[a-zA-Z]*f/, 'git clean -f'],
  [/\bgit\s+(?:checkout|restore)\s+(?:--\s+)?\.(?:\s|$)/, 'discard all working-tree changes'],
  [/\bgit\s+branch\s+-D\b/, 'force branch delete'],
  [/--no-verify\b/, 'skipping git hooks'],

  // EF / dotnet
  [/\bdotnet\s+ef\s+database\s+drop\b/, 'dropping a database'],
  [/\bdotnet\s+ef\s+migrations\s+remove\b/, 'removing a migration'],

  // System / network
  [/\b(?:shutdown|reboot)\b/i, 'shutdown/reboot'],
  [/\btaskkill\b.*\s\/f\b/i, 'forced process kill'],
  [/\bkill\s+-9\b/, 'forced process kill'],
  [/\b(?:curl|wget|iwr|Invoke-WebRequest)\b[^|]*\|\s*(?:sh|bash|iex|Invoke-Expression)\b/i, 'piping download into a shell'],
  [/\bchmod\s+-R\s+777\b/, 'recursive chmod 777'],
  [/\bnpm\s+publish\b/, 'npm publish'],
  [/\bnpm\s+uninstall\b.*\s(?:-g|--global)\b/, 'global npm uninstall'],

  // Secrets
  [/\b(?:cat|type|more|head|tail|less)\b.*(?:\.env\b|\.pem\b|credentials)/i, 'reading a secrets file'],
];

const SQL_RULES = [
  [/\bDROP\s+(?:TABLE|DATABASE|SCHEMA|VIEW|PROCEDURE|INDEX|USER|LOGIN)\b/i, 'DROP'],
  [/\bTRUNCATE\b/i, 'TRUNCATE'],
  [/\bALTER\s+(?:TABLE|DATABASE|SCHEMA|USER|LOGIN)\b/i, 'ALTER'],
  [/\bDELETE\b(?![^;]*\bWHERE\b)/i, 'DELETE without WHERE'],
  [/\bUPDATE\b\s+[\w.[\]]+\s+SET\b(?![^;]*\bWHERE\b)/i, 'UPDATE without WHERE'],
  [/\bxp_cmdshell\b/i, 'xp_cmdshell'],
  [/\bSHUTDOWN\b/i, 'SHUTDOWN'],
];

function check(rules, text) {
  for (const [pattern, reason] of rules) {
    if (pattern.test(text)) return reason;
  }
  return null;
}

function decide(event) {
  const input = event.tool_input || {};

  if (event.tool_name === 'Bash') {
    const command = input.command || '';
    const reason = check(BASH_RULES, command);
    if (reason) return reason;
    // Inline SQL passed through sqlcmd (-Q / -q). Files passed with -i are not inspected.
    if (/\bsqlcmd\b/i.test(command)) return check(SQL_RULES, command);
    return null;
  }

  if (event.tool_name === 'mcp__kestrel__run_query') {
    return check(SQL_RULES, JSON.stringify(input));
  }

  return null;
}

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => (raw += chunk));
process.stdin.on('end', () => {
  let event;
  try {
    event = JSON.parse(raw);
  } catch {
    // Malformed hook input: allow the call rather than breaking all tool use.
    process.exit(0);
  }

  const reason = decide(event);
  if (reason) {
    process.stderr.write(`Blocked by destructive-command policy: ${reason}. Ask the user for explicit approval before retrying.\n`);
    process.exit(2);
  }
  process.exit(0);
});
