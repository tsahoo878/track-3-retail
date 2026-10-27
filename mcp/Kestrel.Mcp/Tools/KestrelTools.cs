using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kestrel.Mcp.Tools;

[McpServerToolType]
public static partial class KestrelTools
{
    [GeneratedRegex(@"^mcp_[a-z_]+$")]
    private static partial Regex ViewNameRegex();

    [McpServerTool(Name = "list_tables", ReadOnly = true),
     Description("List the read-only mcp_* views available for querying, with their columns.")]
    public static async Task<string> ListTables(ReadOnlyDb db, CancellationToken ct)
    {
        var result = await db.QueryAsync(
            @"SELECT table_name, column_name, column_type FROM information_schema.columns
              WHERE table_schema = DATABASE() AND table_name LIKE 'mcp\_%'
              ORDER BY table_name, ordinal_position", maxRows: ReadOnlyDb.HardMaxRows, ct: ct);
        return result.ToJson();
    }

    [McpServerTool(Name = "describe_table", ReadOnly = true),
     Description("Describe the columns of one mcp_* view.")]
    public static async Task<string> DescribeTable(
        ReadOnlyDb db, [Description("View name, e.g. mcp_orders")] string name, CancellationToken ct)
    {
        if (!ViewNameRegex().IsMatch(name ?? ""))
            throw new McpException("Name must be one of the mcp_* views (see list_tables).");
        var result = await db.QueryAsync(
            @"SELECT column_name, column_type, is_nullable FROM information_schema.columns
              WHERE table_schema = DATABASE() AND table_name = @name ORDER BY ordinal_position",
            new Dictionary<string, object?> { ["@name"] = name }, ct: ct);
        if (result.Rows.Count == 0) throw new McpException($"Unknown view '{name}'.");
        return result.ToJson();
    }

    [McpServerTool(Name = "run_readonly_query", ReadOnly = true),
     Description("Run a single SELECT over the mcp_* views. Results are capped (default 200 rows, max 1000) and time-limited to 5s.")]
    public static async Task<string> RunReadonlyQuery(
        ReadOnlyDb db,
        [Description("One SELECT (or WITH ... SELECT) statement against mcp_* views")] string sql,
        [Description("Maximum rows to return (1-1000, default 200)")] int? maxRows,
        CancellationToken ct)
    {
        string safe;
        try { safe = QueryGuard.Validate(sql); }
        catch (QueryRejectedException e) { throw new McpException($"Query rejected: {e.Message}"); }
        return (await db.QueryAsync(safe, maxRows: maxRows, ct: ct)).ToJson();
    }

    [McpServerTool(Name = "sales_summary", ReadOnly = true),
     Description("Orders and revenue per day and status between two dates (inclusive, UTC), optionally for one store. Range limited to 366 days.")]
    public static async Task<string> SalesSummary(
        ReadOnlyDb db,
        [Description("Start date, yyyy-MM-dd")] string from,
        [Description("End date, yyyy-MM-dd")] string to,
        [Description("Optional store id")] int? storeId,
        CancellationToken ct)
    {
        var start = ParseDate(from, nameof(from));
        var end = ParseDate(to, nameof(to));
        if (end < start) throw new McpException("'to' must not be before 'from'.");
        if (end.DayNumber - start.DayNumber > 365) throw new McpException("Date range is limited to 366 days.");

        var result = await db.QueryAsync(
            @"SELECT DATE(ordered_utc) AS day, status, COUNT(*) AS orders, SUM(total_amount) AS revenue
              FROM mcp_orders
              WHERE ordered_utc >= @from AND ordered_utc < @to AND (@store IS NULL OR store_id = @store)
              GROUP BY DATE(ordered_utc), status ORDER BY day, status",
            new Dictionary<string, object?>
            {
                ["@from"] = start.ToDateTime(TimeOnly.MinValue),
                ["@to"] = end.AddDays(1).ToDateTime(TimeOnly.MinValue),
                ["@store"] = storeId,
            }, ReadOnlyDb.HardMaxRows, ct);
        return result.ToJson();
    }

    [McpServerTool(Name = "low_stock", ReadOnly = true),
     Description("Products at or below a stock threshold (default: each row's own reorder level), optionally for one store.")]
    public static async Task<string> LowStock(
        ReadOnlyDb db,
        [Description("Quantity threshold; defaults to the reorder level")] int? threshold,
        [Description("Optional store id")] int? storeId,
        CancellationToken ct)
    {
        var result = await db.QueryAsync(
            @"SELECT s.code AS store, p.sku, p.name AS product, l.quantity, l.reorder_level
              FROM mcp_stock_levels l
              JOIN mcp_products p ON p.id = l.product_id
              JOIN mcp_stores s ON s.id = l.store_id
              WHERE l.quantity <= COALESCE(@threshold, l.reorder_level) AND (@store IS NULL OR l.store_id = @store)
              ORDER BY l.quantity, s.code, p.sku",
            new Dictionary<string, object?> { ["@threshold"] = threshold, ["@store"] = storeId }, ct: ct);
        return result.ToJson();
    }

    [McpServerTool(Name = "order_lookup", ReadOnly = true),
     Description("Order header, lines, payments and status history for one order id (no customer personal data).")]
    public static async Task<string> OrderLookup(
        ReadOnlyDb db, [Description("Order id")] int orderId, CancellationToken ct)
    {
        var p = new Dictionary<string, object?> { ["@id"] = orderId };
        var order = await db.QueryAsync("SELECT * FROM mcp_orders WHERE id = @id", p, ct: ct);
        if (order.Rows.Count == 0) throw new McpException($"Order {orderId} not found.");
        var lines = await db.QueryAsync(
            @"SELECT l.id, p.sku, p.name AS product, l.quantity, l.unit_price, l.line_total
              FROM mcp_order_lines l JOIN mcp_products p ON p.id = l.product_id WHERE l.order_id = @id", p, ct: ct);
        var payments = await db.QueryAsync("SELECT * FROM mcp_payments WHERE order_id = @id ORDER BY paid_utc", p, ct: ct);
        var history = await db.QueryAsync(
            "SELECT * FROM mcp_order_status_history WHERE order_id = @id ORDER BY changed_utc", p, ct: ct);

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            order = order.AsObjects().First(),
            lines = lines.AsObjects(),
            payments = payments.AsObjects(),
            statusHistory = history.AsObjects(),
        });
    }

    private static DateOnly ParseDate(string value, string name) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new McpException($"'{name}' must be a date formatted yyyy-MM-dd.");
}
