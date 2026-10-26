-- Kestrel Outfitters: base schema

CREATE TABLE categories (
  id INT NOT NULL AUTO_INCREMENT,
  code VARCHAR(20) NOT NULL,
  name VARCHAR(80) NOT NULL,
  department VARCHAR(40) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY uq_categories_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE stores (
  id INT NOT NULL AUTO_INCREMENT,
  code VARCHAR(10) NOT NULL,
  name VARCHAR(100) NOT NULL,
  region VARCHAR(30) NOT NULL,
  city VARCHAR(60) NOT NULL,
  timezone VARCHAR(40) NOT NULL DEFAULT 'Europe/London',
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_stores_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE users (
  id INT NOT NULL AUTO_INCREMENT,
  email VARCHAR(200) NOT NULL,
  display_name VARCHAR(100) NOT NULL,
  password_hash VARCHAR(100) NOT NULL,
  role VARCHAR(20) NOT NULL,
  store_id INT NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_utc DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_users_email (email),
  CONSTRAINT fk_users_store FOREIGN KEY (store_id) REFERENCES stores (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE products (
  id INT NOT NULL AUTO_INCREMENT,
  sku VARCHAR(32) NOT NULL,
  name VARCHAR(100) NOT NULL,
  category_id INT NOT NULL,
  brand VARCHAR(60) NOT NULL,
  unit_price DECIMAL(10,2) NOT NULL,
  cost_price DECIMAL(10,2) NOT NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_utc DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_products_sku (sku),
  KEY ix_products_category (category_id),
  CONSTRAINT fk_products_category FOREIGN KEY (category_id) REFERENCES categories (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE stock_levels (
  id INT NOT NULL AUTO_INCREMENT,
  store_id INT NOT NULL,
  product_id INT NOT NULL,
  quantity INT NOT NULL,
  reorder_level INT NOT NULL DEFAULT 2,
  updated_utc DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_stock_store_product (store_id, product_id),
  KEY ix_stock_product (product_id),
  CONSTRAINT fk_stock_store FOREIGN KEY (store_id) REFERENCES stores (id),
  CONSTRAINT fk_stock_product FOREIGN KEY (product_id) REFERENCES products (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE customers (
  id INT NOT NULL AUTO_INCREMENT,
  email VARCHAR(200) NOT NULL,
  first_name VARCHAR(60) NOT NULL,
  last_name VARCHAR(60) NOT NULL,
  phone VARCHAR(30) NULL,
  address_line1 VARCHAR(120) NULL,
  city VARCHAR(60) NULL,
  postcode VARCHAR(12) NULL,
  date_of_birth DATE NULL,
  loyalty_tier VARCHAR(10) NOT NULL DEFAULT 'NONE',
  region VARCHAR(30) NOT NULL,
  marketing_opt_in TINYINT(1) NOT NULL DEFAULT 0,
  created_utc DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_customers_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE orders (
  id INT NOT NULL AUTO_INCREMENT,
  order_number VARCHAR(20) NOT NULL,
  customer_id INT NOT NULL,
  store_id INT NOT NULL,
  channel VARCHAR(10) NOT NULL,
  status VARCHAR(12) NOT NULL,
  ordered_utc DATETIME NOT NULL,
  subtotal DECIMAL(10,2) NOT NULL,
  discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0,
  total_amount DECIMAL(10,2) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_orders_number (order_number),
  KEY ix_orders_customer (customer_id),
  KEY ix_orders_ordered (ordered_utc),
  KEY ix_orders_store (store_id),
  CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customers (id),
  CONSTRAINT fk_orders_store FOREIGN KEY (store_id) REFERENCES stores (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE order_lines (
  id INT NOT NULL AUTO_INCREMENT,
  order_id INT NOT NULL,
  product_id INT NOT NULL,
  quantity INT NOT NULL,
  unit_price DECIMAL(10,2) NOT NULL,
  line_total DECIMAL(10,2) NOT NULL,
  PRIMARY KEY (id),
  KEY ix_lines_order (order_id),
  KEY ix_lines_product (product_id),
  CONSTRAINT fk_lines_order FOREIGN KEY (order_id) REFERENCES orders (id),
  CONSTRAINT fk_lines_product FOREIGN KEY (product_id) REFERENCES products (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE payments (
  id INT NOT NULL AUTO_INCREMENT,
  order_id INT NOT NULL,
  method VARCHAR(12) NOT NULL,
  amount DECIMAL(10,2) NOT NULL,
  card_last4 CHAR(4) NULL,
  provider_ref VARCHAR(40) NULL,
  status VARCHAR(12) NOT NULL,
  paid_utc DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_payments_order (order_id),
  CONSTRAINT fk_payments_order FOREIGN KEY (order_id) REFERENCES orders (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE order_status_history (
  id INT NOT NULL AUTO_INCREMENT,
  order_id INT NOT NULL,
  from_status VARCHAR(12) NULL,
  to_status VARCHAR(12) NOT NULL,
  changed_utc DATETIME NOT NULL,
  changed_by_user_id INT NULL,
  PRIMARY KEY (id),
  KEY ix_history_order (order_id),
  CONSTRAINT fk_history_order FOREIGN KEY (order_id) REFERENCES orders (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE order_notes (
  id INT NOT NULL AUTO_INCREMENT,
  order_id INT NOT NULL,
  author_user_id INT NOT NULL,
  note VARCHAR(500) NOT NULL,
  created_utc DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_notes_order (order_id),
  CONSTRAINT fk_notes_order FOREIGN KEY (order_id) REFERENCES orders (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE import_batches (
  id INT NOT NULL AUTO_INCREMENT,
  file_name VARCHAR(200) NOT NULL,
  started_utc DATETIME NOT NULL,
  finished_utc DATETIME NULL,
  total_lines INT NOT NULL DEFAULT 0,
  applied_lines INT NOT NULL DEFAULT 0,
  rejected_lines INT NOT NULL DEFAULT 0,
  status VARCHAR(12) NOT NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE import_errors (
  id INT NOT NULL AUTO_INCREMENT,
  batch_id INT NOT NULL,
  line_number INT NOT NULL,
  message VARCHAR(300) NOT NULL,
  PRIMARY KEY (id),
  KEY ix_import_errors_batch (batch_id),
  CONSTRAINT fk_import_errors_batch FOREIGN KEY (batch_id) REFERENCES import_batches (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
