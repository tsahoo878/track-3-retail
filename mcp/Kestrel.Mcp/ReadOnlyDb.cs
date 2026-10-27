using System.Text.Json;
using MySqlConnector;

namespace Kestrel.Mcp;

public sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows, bool Truncated)
{
    public IEnumerable<Dictionary<string, object?>> AsObjects() =>
        Rows.Select(r => Columns.Select((c, i) => (c, v: r[i])).ToDictionary(x => x.c, x => x.v));

    public string ToJson() => JsonSerializer.Serialize(new
    {
        columns = Columns,
        rows = AsObjects(),
        rowCount = Rows.Count,
        truncated = Truncated,
    });
}

/// <summary>Runs queries inside a READ ONLY transaction with a timeout and a row cap.</summary>
public sealed class ReadOnlyDb(string connectionString)
{
    public const string ConnectionEnvVar = "KESTREL_MCP_CONNECTION";
    public const int DefaultMaxRows = 200;
    public const int HardMaxRows = 1000;
    private const int TimeoutSeconds = 5;

    public static ReadOnlyDb FromEnvironment()
    {
        var cs = Environment.GetEnvironmentVariable(ConnectionEnvVar);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException($"Environment variable {ConnectionEnvVar} is not set.");
        return new ReadOnlyDb(cs);
    }

    public async Task<QueryResult> QueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, int? maxRows = null,
        CancellationToken ct = default)
    {
        var cap = Math.Clamp(maxRows ?? DefaultMaxRows, 1, HardMaxRows);

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using (var setup = new MySqlCommand(
            "SET SESSION TRANSACTION READ ONLY; SET SESSION MAX_EXECUTION_TIME=5000", conn))
            await setup.ExecuteNonQueryAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);

        await using var cmd = new MySqlCommand(sql, conn, tx) { CommandTimeout = TimeoutSeconds };
        if (parameters is not null)
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<object?[]>();
        var truncated = false;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= cap) { truncated = true; break; }
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return new QueryResult(columns, rows, truncated);
    }
}
