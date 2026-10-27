-- Least-privilege account for the MCP server: SELECT on mcp_* views only.
-- Replace the password before running (do not commit the real one), e.g.:
--   sed "s/CHANGE_ME/$MCP_DB_PASSWORD/" db-setup/03-create-mcp-user.sql | mysql -u root -p
CREATE USER IF NOT EXISTS 'kestrel_mcp'@'%' IDENTIFIED BY 'CHANGE_ME'
  WITH MAX_USER_CONNECTIONS 5 MAX_QUERIES_PER_HOUR 3600;

GRANT SELECT ON kestrel.mcp_stores TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_categories TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_products TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_stock_levels TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_customers TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_orders TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_order_lines TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_payments TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_order_status_history TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_returns TO 'kestrel_mcp'@'%';
GRANT SELECT ON kestrel.mcp_return_lines TO 'kestrel_mcp'@'%';
FLUSH PRIVILEGES;
