#!/usr/bin/env node
// PostToolUse hook: after a successful `git commit`, tells Claude which files changed
// so it can check CLAUDE.md for staleness. Output on stdout is added to Claude's context.

const { execFileSync } = require('child_process');

const COMMIT_PATTERN = /\bgit\s+commit\b/;
const DRY_RUN_PATTERN = /--dry-run\b/;
const NOTHING_TO_COMMIT_PATTERN = /nothing (added )?to commit|no changes added/i;

function git(cwd, args) {
  return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
}

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => (raw += chunk));
process.stdin.on('end', () => {
  let event;
  try {
    event = JSON.parse(raw);
  } catch {
    process.exit(0);
  }

  const command = (event.tool_input && event.tool_input.command) || '';
  if (event.tool_name !== 'Bash' || !COMMIT_PATTERN.test(command) || DRY_RUN_PATTERN.test(command)) {
    process.exit(0);
  }

  const response = JSON.stringify(event.tool_response || '');
  if (NOTHING_TO_COMMIT_PATTERN.test(response)) {
    process.exit(0);
  }

  const cwd = event.cwd || process.cwd();
  let subject;
  let changes;
  try {
    subject = git(cwd, ['log', '-1', '--format=%h %s']);
    // -m --first-parent makes merge commits diff against their first parent instead of showing nothing.
    changes = git(cwd, ['diff-tree', '-m', '--first-parent', '--no-commit-id', '--name-status', '-r', 'HEAD'])
      .split('\n')
      .filter(Boolean);
  } catch {
    process.exit(0);
  }

  // Loop guard: a commit that only touched CLAUDE.md must not trigger another CLAUDE.md update.
  const onlyClaudeMd = changes.every((line) => line.split('\t').pop() === 'CLAUDE.md');
  if (changes.length === 0 || onlyClaudeMd) {
    process.exit(0);
  }

  const context = [
    `A commit was just made: ${subject}`,
    'Files changed in this commit:',
    ...changes.map((line) => `  ${line}`),
    '',
    'Check CLAUDE.md against this change. Update only the sections (Stack, Commands, Conventions)',
    'that are now wrong or missing, keeping the edit minimal. If the commit contradicts an existing',
    'CLAUDE.md rule, flag the contradiction to the user instead of picking a side.',
    'Do not commit the CLAUDE.md change; leave it for the user to review.',
  ].join('\n');

  process.stdout.write(
    JSON.stringify({ hookSpecificOutput: { hookEventName: 'PostToolUse', additionalContext: context } })
  );
  process.exit(0);
});
