// Parameterised SQL for the domain tools. Every statement reads mcp_* views only, uses ?
// placeholders for all caller input, and returns no names or other personal data.
// Fragments interpolated into SQL come from fixed maps below, never from caller input.

// BR-08. Same definition as ReturnService.GetSerialStatsAsync: APPROVED RMAs created in the window,
// rate = refunds / value of non-cancelled orders placed in the window.
export function serialReturnersQuery({ days, minRmas, minOrders, minRatePct }) {
  const sql = `
    SELECT r.customer_id,
           c.loyalty_tier,
           COUNT(*) AS rma_count,
           COUNT(DISTINCT r.order_id) AS distinct_orders,
           SUM(r.refund_total) AS returned_value,
           p.purchased_value,
           ROUND(100 * SUM(r.refund_total) / p.purchased_value, 2) AS rate_pct
      FROM mcp_returns r
      JOIN mcp_customers c ON c.id = r.customer_id
      JOIN (SELECT customer_id, SUM(total_amount) AS purchased_value
              FROM mcp_orders
             WHERE status <> 'CANCELLED' AND ordered_utc >= UTC_TIMESTAMP() - INTERVAL ? DAY
             GROUP BY customer_id) p ON p.customer_id = r.customer_id
     WHERE r.status = 'APPROVED' AND r.created_utc >= UTC_TIMESTAMP() - INTERVAL ? DAY
     GROUP BY r.customer_id, c.loyalty_tier, p.purchased_value
    HAVING COUNT(*) >= ?
       AND COUNT(DISTINCT r.order_id) >= ?
       AND p.purchased_value > 0
       AND 100 * SUM(r.refund_total) / p.purchased_value >= ?
     ORDER BY rate_pct DESC, rma_count DESC`;
  return { sql, params: [days, days, minRmas, minOrders, minRatePct] };
}

// BR-04 / BR-05 audit. Paid per line = line_total minus its prorated share of the order discount, the last line
// (highest id) absorbing rounding. Audits lines and orders that have at least one non-rejected RMA created in
// [from, to] (both optional); refunds are summed over every non-rejected RMA. Should always return no rows.
export function refundsExceedingPaidQuery({ from, to }) {
  const sql = `
    WITH scoped AS (
      SELECT DISTINCT rl.order_line_id, r.order_id
        FROM mcp_return_lines rl
        JOIN mcp_returns r ON r.id = rl.return_id
       WHERE r.status <> 'REJECTED'
         AND (? IS NULL OR r.created_utc >= ?)
         AND (? IS NULL OR r.created_utc < DATE_ADD(?, INTERVAL 1 DAY))
    ),
    order_lines_x AS (
      SELECT l.id, l.order_id, l.line_total, o.discount_amount,
             SUM(l.line_total) OVER (PARTITION BY l.order_id) AS gross,
             ROW_NUMBER() OVER (PARTITION BY l.order_id ORDER BY l.id DESC) AS rn_desc,
             ROUND(o.discount_amount * l.line_total / SUM(l.line_total) OVER (PARTITION BY l.order_id), 2) AS rounded_share
        FROM mcp_order_lines l
        JOIN mcp_orders o ON o.id = l.order_id
       WHERE l.order_id IN (SELECT order_id FROM scoped)
    ),
    paid AS (
      SELECT id, order_id, line_total,
             line_total - CASE WHEN rn_desc = 1
               THEN discount_amount - COALESCE(SUM(rounded_share) OVER (PARTITION BY order_id ORDER BY id
                      ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
               ELSE rounded_share END AS paid_amount
        FROM order_lines_x
    ),
    refunded AS (
      SELECT rl.order_line_id, SUM(rl.refund_amount) AS refunded_amount
        FROM mcp_return_lines rl
        JOIN mcp_returns r ON r.id = rl.return_id
       WHERE r.status <> 'REJECTED'
       GROUP BY rl.order_line_id
    ),
    captured AS (
      SELECT order_id, SUM(amount) AS captured_amount
        FROM mcp_payments
       WHERE status = 'CAPTURED'
       GROUP BY order_id
    ),
    order_refunds AS (
      SELECT order_id, SUM(refund_total) AS refunded_amount
        FROM mcp_returns
       WHERE status <> 'REJECTED'
       GROUP BY order_id
    )
    SELECT 'LINE' AS finding, o.order_number, p.id AS order_line_id,
           p.paid_amount AS paid, f.refunded_amount AS refunded, f.refunded_amount - p.paid_amount AS excess
      FROM paid p
      JOIN refunded f ON f.order_line_id = p.id
      JOIN mcp_orders o ON o.id = p.order_id
     WHERE p.id IN (SELECT order_line_id FROM scoped) AND f.refunded_amount > p.paid_amount
    UNION ALL
    SELECT 'ORDER' AS finding, o.order_number, NULL AS order_line_id,
           COALESCE(c.captured_amount, 0) AS paid, orf.refunded_amount AS refunded,
           orf.refunded_amount - COALESCE(c.captured_amount, 0) AS excess
      FROM order_refunds orf
      JOIN mcp_orders o ON o.id = orf.order_id
      LEFT JOIN captured c ON c.order_id = orf.order_id
     WHERE orf.order_id IN (SELECT order_id FROM scoped) AND orf.refunded_amount > COALESCE(c.captured_amount, 0)
     ORDER BY order_number, finding`;
  return { sql, params: [from, from, to, to] };
}

// Fixed fragments per grouping; selected by an enum, never built from caller strings.
const GROUPINGS = {
  product: {
    soldKey: "p.id", soldLabel: "CONCAT(p.sku, ' ', p.name)",
    retKey: "p.id", retLabel: "CONCAT(p.sku, ' ', p.name)",
  },
  category: {
    soldKey: "c.id", soldLabel: "c.name",
    retKey: "c.id", retLabel: "c.name",
  },
  store: {
    soldKey: "s.id", soldLabel: "s.name",
    // Returns are attributed to the store that received them (same as the dashboard).
    retKey: "s.id", retLabel: "s.name",
  },
};

export function returnRateQuery({ from, to, groupBy, limit }) {
  const g = GROUPINGS[groupBy];
  if (!g) throw new Error(`Unknown groupBy '${groupBy}'.`);
  const sql = `
    WITH sold AS (
      SELECT ${g.soldKey} AS k, MAX(${g.soldLabel}) AS label, CAST(SUM(l.quantity) AS SIGNED) AS qty
        FROM mcp_order_lines l
        JOIN mcp_orders o ON o.id = l.order_id
        JOIN mcp_products p ON p.id = l.product_id
        JOIN mcp_categories c ON c.id = p.category_id
        JOIN mcp_stores s ON s.id = o.store_id
       WHERE o.status <> 'CANCELLED' AND o.ordered_utc >= ? AND o.ordered_utc < DATE_ADD(?, INTERVAL 1 DAY)
       GROUP BY ${g.soldKey}
    ),
    ret AS (
      SELECT ${g.retKey} AS k, MAX(${g.retLabel}) AS label,
             CAST(SUM(rl.quantity) AS SIGNED) AS qty, SUM(rl.refund_amount) AS refund
        FROM mcp_return_lines rl
        JOIN mcp_returns r ON r.id = rl.return_id
        JOIN mcp_order_lines l ON l.id = rl.order_line_id
        JOIN mcp_products p ON p.id = l.product_id
        JOIN mcp_categories c ON c.id = p.category_id
        JOIN mcp_stores s ON s.id = r.store_id
       WHERE r.status = 'APPROVED' AND r.created_utc >= ? AND r.created_utc < DATE_ADD(?, INTERVAL 1 DAY)
       GROUP BY ${g.retKey}
    )
    SELECT ret.k AS group_key, ret.label,
           COALESCE(sold.qty, 0) AS sold_qty,
           ret.qty AS returned_qty,
           CASE WHEN COALESCE(sold.qty, 0) > 0 THEN ROUND(100 * ret.qty / sold.qty, 2) ELSE 0 END AS return_rate_pct,
           ret.refund AS refund_total
      FROM ret
      LEFT JOIN sold ON sold.k = ret.k
     ORDER BY return_rate_pct DESC, returned_qty DESC
     LIMIT ?`;
  return { sql, params: [from, to, from, to, limit] };
}

// BR-07: A/B lines on APPROVED RMAs that were not restocked, plus DRAFT RMAs still waiting for approval.
export function restockBacklogQuery({ storeId, olderThanHours }) {
  const sql = `
    SELECT * FROM (
      SELECT 'APPROVED_NOT_RESTOCKED' AS issue, r.id AS return_id, r.rma_number, r.store_id, r.status,
             r.refund_total, rl.id AS return_line_id, rl.order_line_id, rl.grade, rl.quantity,
             TIMESTAMPDIFF(HOUR, COALESCE(r.approved_utc, r.created_utc), UTC_TIMESTAMP()) AS age_hours
        FROM mcp_returns r
        JOIN mcp_return_lines rl ON rl.return_id = r.id
       WHERE r.status = 'APPROVED' AND rl.grade IN ('A', 'B') AND rl.restocked = 0
         AND (? IS NULL OR r.store_id = ?)
      UNION ALL
      SELECT 'DRAFT_AWAITING_APPROVAL' AS issue, r.id, r.rma_number, r.store_id, r.status,
             r.refund_total, NULL, NULL, NULL, NULL,
             TIMESTAMPDIFF(HOUR, r.created_utc, UTC_TIMESTAMP()) AS age_hours
        FROM mcp_returns r
       WHERE r.status = 'DRAFT'
         AND (? IS NULL OR r.store_id = ?)
    ) backlog
    WHERE age_hours >= ?
    ORDER BY age_hours DESC, return_id`;
  return { sql, params: [storeId, storeId, storeId, storeId, olderThanHours] };
}
