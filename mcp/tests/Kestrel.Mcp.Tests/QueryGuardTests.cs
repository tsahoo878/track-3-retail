using Kestrel.Mcp;
using Xunit;

namespace Kestrel.Mcp.Tests;

public class QueryGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM mcp_orders")]
    [InlineData("select id from mcp_orders where status = 'a;b'")]
    [InlineData("SELECT * FROM `mcp_orders` o JOIN mcp_payments p ON p.order_id = o.id;")]
    [InlineData("WITH t AS (SELECT * FROM mcp_orders) SELECT COUNT(*) FROM t")]
    [InlineData("SELECT * FROM kestrel.mcp_stores")]
    [InlineData("SELECT * FROM mcp_orders WHERE status = 'it''s'")]
    public void Allows_safe_selects(string sql) => Assert.NotEmpty(QueryGuard.Validate(sql));

    [Theory]
    [InlineData("")]
    [InlineData("DROP TABLE orders")]
    [InlineData("DELETE FROM mcp_orders")]
    [InlineData("UPDATE mcp_orders SET status = 'x'")]
    [InlineData("INSERT INTO mcp_orders VALUES (1)")]
    [InlineData("SELECT 1; DROP TABLE orders")]
    [InlineData("SELECT * FROM mcp_orders; SELECT 1")]
    [InlineData("SELECT * FROM mcp_orders -- comment")]
    [InlineData("SELECT * FROM mcp_orders # comment")]
    [InlineData("SELECT /*! 1 */ * FROM mcp_orders")]
    [InlineData("SELECT * FROM mcp_orders INTO OUTFILE '/tmp/x'")]
    [InlineData("SELECT * FROM mcp_orders INTO DUMPFILE '/tmp/x'")]
    [InlineData("SELECT LOAD_FILE('/etc/passwd')")]
    [InlineData("SELECT SLEEP(10)")]
    [InlineData("SELECT BENCHMARK(1000000, MD5('a'))")]
    [InlineData("SELECT * FROM mcp_orders FOR UPDATE")]
    [InlineData("SELECT * FROM mcp_orders LOCK IN SHARE MODE")]
    [InlineData("SELECT @@version")]
    [InlineData("SELECT @x := 1")]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT * FROM customers")]
    [InlineData("SELECT * FROM orders")]
    [InlineData("SELECT * FROM `users`")]
    [InlineData("SELECT * FROM kestrel.users")]
    [InlineData("SELECT * FROM mcp_orders, users")]
    [InlineData("SELECT * FROM mcp_orders o, users u")]
    [InlineData("SELECT * FROM mcp_orders JOIN payments ON 1=1")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT * FROM mysql.user")]
    [InlineData("SELECT * FROM mcp_orders WHERE x = 'unterminated")]
    [InlineData("SELECT * FROM mcp_orders WHERE x = \' OR 1=1")]
    [InlineData("CALL some_proc()")]
    [InlineData("SHOW TABLES")]
    public void Rejects_unsafe_queries(string sql) => Assert.Throws<QueryRejectedException>(() => QueryGuard.Validate(sql));

    [Fact]
    public void Rejects_overlong_queries() =>
        Assert.Throws<QueryRejectedException>(() =>
            QueryGuard.Validate("SELECT * FROM mcp_orders WHERE id IN (" + new string('1', QueryGuard.MaxSqlLength) + ")"));

    [Fact]
    public void Allows_keywords_inside_string_literals() =>
        Assert.NotEmpty(QueryGuard.Validate("SELECT * FROM mcp_orders WHERE order_number = 'drop update sleep'"));
}
