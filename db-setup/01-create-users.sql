-- Run once as a MySQL admin (for example: mysql -h 127.0.0.1 -P 3313 -u root -p < db-setup/01-create-users.sql)
CREATE DATABASE IF NOT EXISTS kestrel CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE USER IF NOT EXISTS 'kestrel_app'@'%' IDENTIFIED BY 'Kestrel!App1';
GRANT ALL PRIVILEGES ON kestrel.* TO 'kestrel_app'@'%';
FLUSH PRIVILEGES;
