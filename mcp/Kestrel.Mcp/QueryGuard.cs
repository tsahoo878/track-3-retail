using System.Text;
using System.Text.RegularExpressions;

namespace Kestrel.Mcp;

public sealed class QueryRejectedException(string message) : Exception(message);

/// <summary>
/// Defence-in-depth validation for ad-hoc SQL. The authoritative control is the database
/// account (SELECT on mcp_* views only); this guard rejects anything else before it is sent.
/// </summary>
public static partial class QueryGuard
{
    public const int MaxSqlLength = 4000;

    private static readonly string[] ForbiddenWords =
    [
        "into", "outfile", "dumpfile", "load_file", "sleep", "benchmark", "get_lock",
        "release_lock", "release_all_locks", "master_pos_wait", "information_schema",
        "performance_schema", "mysql", "sys", "update", "delete", "insert", "replace",
        "drop", "alter", "create", "truncate", "grant", "call", "handler", "load",
    ];

    [GeneratedRegex(@"\b(?:from|join)\s+([A-Za-z0-9_$.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RelationRegex();

    // "FROM a x, b" - a comma straight after a relation (and optional alias) starts another relation.
    [GeneratedRegex(@"\b(?:from|join)\s+[A-Za-z0-9_$.]+(?:\s+(?:as\s+)?[A-Za-z0-9_$]+)?\s*,\s*([A-Za-z0-9_$.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CommaRelationRegex();

    [GeneratedRegex(@"(?:\bwith\s+(?:recursive\s+)?|\)\s*,\s*)([A-Za-z0-9_$]+)\s+as\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex CteRegex();

    [GeneratedRegex(@"\bfor\s+(?:update|share)\b|\block\s+in\s+share\s+mode\b", RegexOptions.IgnoreCase)]
    private static partial Regex LockingRegex();

    [GeneratedRegex(@"^(?:kestrel\.)?mcp_[a-z_]+$", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedRelationRegex();

    /// <summary>Returns the SQL to execute (trailing semicolon removed) or throws.</summary>
    public static string Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new QueryRejectedException("Query is empty.");
        if (sql.Length > MaxSqlLength)
            throw new QueryRejectedException($"Query is longer than {MaxSqlLength} characters.");

        var trimmed = sql.Trim();
        if (trimmed.EndsWith(';')) trimmed = trimmed[..^1].TrimEnd();

        var stripped = StripLiterals(trimmed);

        var first = stripped.TrimStart().Split([' ', '\t', '\r', '\n', '('], 2)[0];
        if (!first.Equals("select", StringComparison.OrdinalIgnoreCase) &&
            !first.Equals("with", StringComparison.OrdinalIgnoreCase))
            throw new QueryRejectedException("Only SELECT queries are allowed.");

        if (stripped.Contains('@'))
            throw new QueryRejectedException("User and system variables are not allowed.");

        foreach (var word in ForbiddenWords)
            if (Regex.IsMatch(stripped, $@"\b{word}\b", RegexOptions.IgnoreCase))
                throw new QueryRejectedException($"Keyword or function '{word}' is not allowed.");

        if (LockingRegex().IsMatch(stripped))
            throw new QueryRejectedException("Locking reads are not allowed.");

        var ctes = CteRegex().Matches(stripped).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relations = RelationRegex().Matches(stripped).Concat(CommaRelationRegex().Matches(stripped))
            .Select(m => m.Groups[1].Value);
        foreach (var relation in relations)
        {
            if (ctes.Contains(relation) || AllowedRelationRegex().IsMatch(relation)) continue;
            throw new QueryRejectedException($"Table '{relation}' is not allowed; query the mcp_* views (see list_tables).");
        }

        return trimmed;
    }

    /// <summary>
    /// Replaces string literals with a placeholder and unwraps backtick identifiers, rejecting
    /// statement separators, comments and backslashes that appear outside literals.
    /// </summary>
    private static string StripLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c is '\'' or '"')
            {
                i = SkipLiteral(sql, i, c);
                sb.Append("''");
            }
            else if (c == '`')
            {
                var end = sql.IndexOf('`', i + 1);
                if (end < 0) throw new QueryRejectedException("Unterminated identifier quote.");
                sb.Append(sql, i + 1, end - i - 1);
                i = end;
            }
            else if (c == ';')
                throw new QueryRejectedException("Multiple statements are not allowed.");
            else if (c == '#' || (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') ||
                     (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*'))
                throw new QueryRejectedException("SQL comments are not allowed.");
            else if (c == '\\')
                throw new QueryRejectedException("Backslashes are not allowed outside string literals.");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static int SkipLiteral(string sql, int start, char quote)
    {
        for (var i = start + 1; i < sql.Length; i++)
        {
            if (sql[i] == '\\') { i++; continue; }
            if (sql[i] != quote) continue;
            if (i + 1 < sql.Length && sql[i + 1] == quote) { i++; continue; }
            return i;
        }
        throw new QueryRejectedException("Unterminated string literal.");
    }
}
