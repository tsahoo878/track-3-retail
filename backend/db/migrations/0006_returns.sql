-- Returns and refunds (RMA): per-category return window, returns, return lines

ALTER TABLE categories ADD COLUMN return_window_days INT NOT NULL DEFAULT 30;

UPDATE categories SET return_window_days = 60 WHERE code IN ('BOOT', 'JKT-WP', 'JKT-INS');

CREATE TABLE returns (
  id INT NOT NULL AUTO_INCREMENT,
  rma_number VARCHAR(24) NOT NULL,
  order_id INT NOT NULL,
  store_id INT NOT NULL,
  customer_id INT NOT NULL,
  status VARCHAR(12) NOT NULL,
  refund_total DECIMAL(10,2) NOT NULL DEFAULT 0,
  created_by_user_id INT NOT NULL,
  approved_by_user_id INT NULL,
  created_utc DATETIME NOT NULL,
  approved_utc DATETIME NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_returns_rma (rma_number),
  KEY ix_returns_order (order_id),
  KEY ix_returns_customer (customer_id),
  KEY ix_returns_created (created_utc),
  KEY ix_returns_store (store_id),
  CONSTRAINT fk_returns_order FOREIGN KEY (order_id) REFERENCES orders (id),
  CONSTRAINT fk_returns_store FOREIGN KEY (store_id) REFERENCES stores (id),
  CONSTRAINT fk_returns_customer FOREIGN KEY (customer_id) REFERENCES customers (id),
  CONSTRAINT fk_returns_created_by FOREIGN KEY (created_by_user_id) REFERENCES users (id),
  CONSTRAINT fk_returns_approved_by FOREIGN KEY (approved_by_user_id) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE return_lines (
  id INT NOT NULL AUTO_INCREMENT,
  return_id INT NOT NULL,
  order_line_id INT NOT NULL,
  quantity INT NOT NULL,
  grade VARCHAR(5) NOT NULL,
  refund_amount DECIMAL(10,2) NOT NULL,
  restocked TINYINT(1) NOT NULL DEFAULT 0,
  supplier_claim TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  KEY ix_return_lines_return (return_id),
  KEY ix_return_lines_order_line (order_line_id),
  CONSTRAINT fk_return_lines_return FOREIGN KEY (return_id) REFERENCES returns (id),
  CONSTRAINT fk_return_lines_order_line FOREIGN KEY (order_line_id) REFERENCES order_lines (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
