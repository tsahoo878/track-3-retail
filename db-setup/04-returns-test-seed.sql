-- TEST ONLY: sample RMAs for verifying the returns feature and the MCP returns tools.
-- Do NOT run against production. Run after migration 0006, as an admin user.
-- Re-runnable: removes its own RMA-TEST-* rows first, touches nothing else.
--
-- Amounts follow ReturnService / ReturnPricing: grades A, B and SCRAP refund 100% of the amount paid for the
-- units returned, grade C refunds 85% (a reduced refund, never a fee). Every order below has no discount except
-- order 87 and order 66 (5% loyalty discount), where paid = line_total - prorated discount share.
-- BR-08 counts APPROVED RMAs only; DRAFT RMAs are ignored and REJECTED ones never count.
--
-- Scenarios (ids refer to the seeded data):
--  * Customer 16, SERIAL RETURNER: 3 APPROVED RMAs on 3 orders (92, 130, 171) = 529.15 of 1035.00 purchased
--    in the last 180 days = 51.13% (defaults: >=3 RMAs, >=3 orders, >=40%).
--  * Customer 15, NOT flagged: one APPROVED RMA, 3 items from the single order 126.
--  * Customer 1: DRAFT RMA on order 87 awaiting approval for more than a day (restock_backlog, DRAFT row), and an
--    APPROVED A line on order 66 that was never restocked (restock_backlog, APPROVED_NOT_RESTOCKED row).
--    Customer 1 has one APPROVED RMA, so is not flagged.
--  * Correctly restocked A/B lines: RMA-TEST-0001 and RMA-TEST-0002 (restocked = 1).
--  * Refund over 150 approved by a MANAGER (user 4): RMA-TEST-0002 (299.00).
USE kestrel;

DELETE rl FROM return_lines rl JOIN returns r ON r.id = rl.return_id WHERE r.rma_number LIKE 'RMA-TEST-%';
DELETE FROM returns WHERE rma_number LIKE 'RMA-TEST-%';

INSERT INTO returns (rma_number, order_id, store_id, customer_id, status, refund_total, created_by_user_id, approved_by_user_id, created_utc, approved_utc) VALUES
('RMA-TEST-0001', 92,  4, 16, 'APPROVED',  95.00, 5, 5,    '2026-10-01 10:00:00', '2026-10-01 10:05:00'),
('RMA-TEST-0002', 130, 4, 16, 'APPROVED', 299.00, 5, 4,    '2026-10-08 11:00:00', '2026-10-08 11:05:00'),
('RMA-TEST-0003', 171, 3, 16, 'APPROVED', 135.15, 4, 4,    '2026-10-15 12:00:00', '2026-10-15 12:05:00'),
('RMA-TEST-0004', 126, 4, 15, 'APPROVED', 126.00, 5, 5,    '2026-10-12 09:30:00', '2026-10-12 09:35:00'),
('RMA-TEST-0005', 87,  2,  1, 'DRAFT',     75.05, 5, NULL, '2026-10-25 10:00:00', NULL),
('RMA-TEST-0006', 66,  2,  1, 'APPROVED',  56.05, 6, 6,    '2026-10-26 08:00:00', '2026-10-26 08:05:00');

INSERT INTO return_lines (return_id, order_line_id, quantity, grade, refund_amount, restocked, supplier_claim)
SELECT r.id, v.order_line_id, v.quantity, v.grade, v.refund_amount, v.restocked, v.supplier_claim
FROM (
  SELECT 'RMA-TEST-0001' AS rma, 168 AS order_line_id, 1 AS quantity, 'A' AS grade,  95.00 AS refund_amount, 1 AS restocked, 0 AS supplier_claim
  UNION ALL SELECT 'RMA-TEST-0002', 231, 1, 'B',     299.00, 1, 0
  UNION ALL SELECT 'RMA-TEST-0003', 299, 1, 'C',     135.15, 0, 0   -- 85% of 159.00
  UNION ALL SELECT 'RMA-TEST-0004', 223, 1, 'A',      22.00, 1, 0
  UNION ALL SELECT 'RMA-TEST-0004', 224, 1, 'B',      45.00, 1, 0
  UNION ALL SELECT 'RMA-TEST-0004', 225, 1, 'SCRAP',  59.00, 0, 1
  UNION ALL SELECT 'RMA-TEST-0005', 156, 1, 'B',      75.05, 0, 0   -- 79.00 - 3.95 discount share
  UNION ALL SELECT 'RMA-TEST-0006', 120, 1, 'A',      56.05, 0, 0   -- 59.00 - 2.95 discount; NOT restocked (backlog case)
) v JOIN returns r ON r.rma_number = v.rma;
