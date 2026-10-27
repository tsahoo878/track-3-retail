-- Read-only views exposed to the MCP server. No PII (names, email, phone, address, DOB),
-- no card data, no users/password hashes. Run after the migrations, as an admin user.
USE kestrel;

CREATE OR REPLACE VIEW mcp_stores AS
  SELECT id, code, name, region, city, is_active FROM stores;

CREATE OR REPLACE VIEW mcp_categories AS
  SELECT id, code, name, department, return_window_days FROM categories;

CREATE OR REPLACE VIEW mcp_products AS
  SELECT id, sku, name, category_id, brand, unit_price, cost_price, is_active FROM products;

CREATE OR REPLACE VIEW mcp_stock_levels AS
  SELECT id, store_id, product_id, quantity, reorder_level, updated_utc FROM stock_levels;

CREATE OR REPLACE VIEW mcp_customers AS
  SELECT id, loyalty_tier, region, created_utc FROM customers;

CREATE OR REPLACE VIEW mcp_orders AS
  SELECT id, order_number, customer_id, store_id, channel, status, ordered_utc,
         subtotal, discount_amount, total_amount
  FROM orders;

CREATE OR REPLACE VIEW mcp_order_lines AS
  SELECT id, order_id, product_id, quantity, unit_price, line_total FROM order_lines;

CREATE OR REPLACE VIEW mcp_payments AS
  SELECT id, order_id, method, amount, status, paid_utc FROM payments;

CREATE OR REPLACE VIEW mcp_order_status_history AS
  SELECT id, order_id, from_status, to_status, changed_utc FROM order_status_history;

CREATE OR REPLACE VIEW mcp_returns AS
  SELECT id, rma_number, order_id, store_id, customer_id, status, refund_total,
         created_utc, approved_utc
  FROM returns;

CREATE OR REPLACE VIEW mcp_return_lines AS
  SELECT id, return_id, order_line_id, quantity, grade, refund_amount, restocked, supplier_claim
  FROM return_lines;
