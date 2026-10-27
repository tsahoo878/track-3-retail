# Spec: Returns and refunds (RMA)

Status: draft
Author: engineering
Date: 2026-10-27

Source: Tom Achebe's ticket, `docs/specs/returns-refunds.md` (unchanged). Where the ticket contradicts itself or cannot be built as written, this spec says what was built and lists the question under "Assumptions & Open Questions".

## Overview
Returns move from paper into the back-office app. A desk member checks whether an order can be returned, grades each returned item (A, B, C or SCRAP), and the system works out a refund that is never more than the customer paid. Items in grade A or B go back into stock at the receiving store when the return is approved. Customers who return a lot are flagged to the desk as a prompt only. A dashboard shows return rates by product, category and store, and one API call answers "can this order be returned, which items, and until when" for the chat team's pilot. Five MCP tools let analysts query the same data without seeing personal data.

The system records the refund amount and approves the return. It does not move money (see OQ-04).

Out of scope:
- Exchanges, store credit and courier labels (a later phase, per the ticket).
- Paying the refund back automatically to the original card, PayPal or gift card (no payment gateway integration; OQ-04).
- Capturing, keeping or sharing ID photos (driving licence or passport) (declined pending legal review; OQ-03).
- Login and per-user authentication (the app has none today; OQ-05).

## Users
There is no login. The caller sends a `userId` and the system reads that user's role from the `users` table. Roles that exist: STAFF, MANAGER, ANALYST, BUYER, ADMIN.

| Role | May | May not |
|---|---|---|
| STAFF (desk staff) | Check eligibility, create and approve RMAs up to and including 150.00, reject a draft, view an RMA and the dashboard. See the customer's name and the order only. | Approve an RMA above 150.00. See address, date of birth, phone or email. |
| MANAGER | Everything STAFF may do, and approve an RMA above 150.00. | |
| ADMIN | Same as MANAGER. | |
| BUYER | Change a category's return window (`PUT /api/categories/{id}/return-window`). | Approve RMAs above 150.00. |
| ANALYST | Use the MCP tools (PII-free `mcp_*` views only). | Approve RMAs; see names or any personal data through MCP. |

Notes:
- Role checks today are made in the approval rule (BR-06) only. The return-window endpoint accepts any caller; limiting it to BUYER and ADMIN is a production-readiness item (OQ-05).
- The desk DTOs (`EligibilityDto`, `ReturnDto`, flagged customers on the dashboard) carry the customer's name and never address, date of birth, phone or email. A unit test enforces this.
- MCP tools return customer ids and loyalty tier but no names.

## Business rules
Each rule is written so it can be tested. "Today" is the UTC date; all timestamps are UTC.

- BR-01 Return window. A line can be returned while today <= order date + `categories.return_window_days` (inclusive, counted in calendar days, not hours). The window is per category and held in the database. Default 30 days; boots (BOOT) and jackets (JKT-WP, JKT-INS) are 60 days. A buyer may change it (1 to 365 days) without a code change.
- BR-02 Order status. Only PAID or FULFILLED orders can be returned. CANCELLED and PLACED orders cannot.
- BR-03 Quantity. For each order line, the quantity in all non-rejected RMAs (DRAFT and APPROVED) must not exceed the quantity ordered.
- BR-04 Amount paid per line. Paid = `line_total` minus the line's share of the order discount, where the share is the discount prorated by `line_total`, rounded to 2 decimals. The last line (highest id) takes the rounding difference. When part of a line is returned, the paid share of the first N units is rounded cumulatively, so returning every unit in any number of RMAs adds up to exactly the paid amount.
- BR-05 Refund cap. The refunds on a line, summed over all non-rejected RMAs, never exceed the amount paid for that line. The refunds on an order never exceed its CAPTURED payments. The system caps a line's amount down to what is left; the RMA is rejected if the order would still exceed the captured payments. The customer is never charged a fee to return something.
- BR-06 Manager approval. An RMA with `refund_total` above 150.00 can be approved only by a MANAGER or ADMIN. Up to and including 150.00 any active user can approve. The threshold is configuration (`Returns:ManagerApprovalThreshold`).
- BR-07 Restock and supplier claim. When an RMA is approved, every grade A and B line is added to `stock_levels` at the RMA's receiving store in the same database transaction, and the line is marked `restocked`. Grade C and SCRAP lines are not restocked. SCRAP lines are marked `supplier_claim`. Approving twice is refused and stock is added once. A DRAFT can be rejected, which frees its quantity and refund cap.
- BR-08 Serial returner (advisory only). A customer is flagged when, in the last 180 days, they have at least 3 APPROVED RMAs, on at least 3 different orders, and the refund total of those RMAs is at least 40% of the value of their non-cancelled orders in the same period (refund_total / order total_amount). DRAFT RMAs are ignored; REJECTED RMAs are not counted. The thresholds are configuration (`Returns:SerialWindowDays`, `SerialMinRmas`, `SerialMinOrders`, `SerialMinRatePercent`). The flag is shown to the desk and must never block creating, approving or refunding a return. A customer who returns several items from one order is not flagged.

Refund by condition grade (applied to the amount paid for the units returned, then capped by BR-05):

| Grade | Meaning (ticket) | Refund | Restocked |
|---|---|---|---|
| A | As new | 100% | Yes |
| B | Opened or lightly tried on | 100% | Yes, at the normal price |
| C | Worn or damaged by the customer | 85% (`Returns:GradeCRefundPercent`) | No |
| SCRAP | Faulty or unusable | 100% (needs Tom's confirmation, OQ-09) | No; supplier claim |

Grade C is a reduced refund for the item's condition. It is not a restocking fee and nothing is deducted from the customer on top of it (OQ-01).

## API
Base path `/api`. No authentication; see OQ-05. Money is a decimal with 2 places. Dates are ISO 8601, UTC.

Errors for every endpoint use `application/problem+json`:
- 400 validation (`errors` map of field to messages).
- 404 "Not found" (unknown order, RMA, category, user, store).
- 422 "Business rule violated" (`detail` has the plain-language reason).
- 500 "Unexpected error". (The shared handler also defines 409 "Conflict", which these endpoints do not raise.)

### GET /api/returns/eligibility/{orderNumber}
The single "can this be returned?" call, used by the desk screen and the chat team.
- Who: any caller.
- Response 200 `EligibilityDto`: `orderId`, `orderNumber`, `orderStatus`, `customerName`, `orderedUtc`, `orderStoreId`, `eligible` (true if at least one line can be returned), `reason` (why not, when `eligible` is false), `returnableUntil` (latest date across lines), `capturedPayments`, `refundedTotal`, `orderRefundableRemaining`, `serialReturner`, and `lines[]`. Each line has `orderLineId`, `sku`, `productName`, `categoryCode`, `windowDays`, `quantityOrdered`, `quantityReturned`, `returnableQuantity`, `paidAmount`, `refundedAmount`, `returnableAmount`, `returnableUntil`, `eligible`, `reason`.
- Errors: 404 order not found.

### POST /api/returns
Creates a DRAFT RMA and calculates the refund (nothing is refunded and stock is untouched). Any earlier DRAFT on the same order is superseded (set to REJECTED), so repeated "Calculate refund" clicks do not hold quantity.
- Who: any active user id in `createdByUserId`.
- Request `CreateReturnDto`: `orderNumber` (max 20), `storeId` (receiving store, must be open), `createdByUserId`, `lines[]` of `{ orderLineId, quantity (1..1000), grade (A, B, C or SCRAP, any case) }`. A line may appear once.
- Response 201 `ReturnDto` with a `Location` header: `id`, `rmaNumber` (`RMA-yyyyMMdd-nnnn`), `orderId`, `orderNumber`, `customerName`, `storeId`, `storeName`, `status` (DRAFT), `refundTotal`, `requiresManagerApproval`, `createdByUserId`, `approvedByUserId`, `createdUtc`, `approvedUtc`, `serialReturner`, `lines[]` (`id`, `orderLineId`, `sku`, `productName`, `quantity`, `grade`, `refundAmount`, `restocked`, `supplierClaim`).
- Errors: 400 invalid grade, empty lines, duplicate line, bad quantity. 404 order, store or user not found. 422 order not PAID or FULFILLED (BR-02); window ended (BR-01); more than the remaining quantity (BR-03); line from another order; store closed; refund would exceed what the customer paid for the order (BR-05).

### GET /api/returns/{id}
- Response 200 `ReturnDto` (as above). Errors: 404.

### POST /api/returns/{id}/approve
- Request `ApproveReturnDto`: `{ userId }`.
- Behaviour: in one transaction, locks the RMA, checks BR-06, restocks A and B at the RMA's store, sets SCRAP `supplierClaim`, sets status APPROVED with `approvedByUserId` and `approvedUtc`.
- Response 200 `ReturnDto`.
- Errors: 400 invalid user id. 404 RMA or user not found. 422 RMA is not DRAFT (already approved, rejected or superseded); user inactive; refund above 150.00 and user is not MANAGER or ADMIN.

### POST /api/returns/{id}/reject
- Request `RejectReturnDto`: `{ userId }`. DRAFT only. Sets REJECTED, which frees the quantity and refund cap.
- Response 200 `ReturnDto`. Errors: 400, 404, 422 (not DRAFT, or inactive user).

### GET /api/returns/dashboard?from=yyyy-MM-dd&to=yyyy-MM-dd
- Defaults: `to` = today UTC, `from` = `to` minus 90 days.
- Response 200 `ReturnsDashboardDto`: `from`, `to`, `byProduct[]`, `byCategory[]`, `byStore[]` (each row `key`, `label`, `soldQuantity`, `returnedQuantity`, `returnRatePct`, `refundTotal`), and `flaggedCustomers[]` (`customerId`, `customerName`, `rmaCount`, `distinctOrders`, `returnedValue`, `purchasedValue`, `ratePct`). Only groups with at least one APPROVED return in the range are listed, highest rate first.
- Errors: 422 `to` before `from`.

### PUT /api/categories/{id}/return-window
- Request `UpdateReturnWindowDto`: `{ days }` (1 to 365).
- Response 200 `CategoryDto` (includes `returnWindowDays`). Errors: 400 days out of range, 404 category not found.
- Takes effect immediately for new eligibility checks and new RMAs.

## UI
Routes `/returns/desk` and `/returns/dashboard`.

RMA desk (`RmaDeskPage.tsx`)
- Search by order number ("Check order"). Shows the order, customer name, status, paid, already refunded and refundable remaining.
- A warning banner "Serial returner" when `serialReturner` is true. It says the return can still be processed.
- A table per line: item, ordered, returnable, refundable up to, return until, return quantity (limited to the returnable quantity), condition (A, B, C, SCRAP). Lines that cannot be returned are disabled and show the reason. A "Not returnable" banner shows the order-level reason.
- Receiving store and acting user selectors, then "Calculate refund" creates a DRAFT and shows the RMA number, per-line refunds (C marked "reduced refund for condition", SCRAP marked "supplier claim, not restocked") and the total.
- "Approve" and "Discard draft" buttons. A note appears when the refund is above 150.00 and needs a manager.
- States: loading while searching; empty "Enter an order number to see what can be returned."; errors show the API's reason (not found, rule violations) in the search area or an alert banner; a separate message if stores or users fail to load; a hint when no quantity has been entered; buttons disabled while a request runs.

Returns dashboard (`ReturnsDashboardPage.tsx`)
- Date range (default last 90 days). Tables by product, category and store with sold, returned, rate and refund total; a flagged customers table (name, returns, orders, returned value, purchased value, rate).
- States: loading; error from the API; "No data in this period." for an empty table; "No customers are currently flagged."

## MCP tools
The Node server in `kestrel-mysql-mcp/` reads PII-free views (`mcp_returns`, `mcp_return_lines` and the existing `mcp_*` views). All domain tools use fixed parameterised SQL.

| Tool | Input | Returns |
|---|---|---|
| `serial_returners` | `days` (180), `minRmas` (3), `minOrders` (3), `minRatePct` (40) | customer_id, loyalty_tier, rma_count, distinct_orders, returned_value, purchased_value, rate_pct. Same definition as BR-08. No names. |
| `refunds_exceeding_paid` | optional `from`, `to` | Lines whose refunds exceed the prorated paid amount, and orders whose refunds exceed captured payments (BR-04, BR-05). Should always be empty. |
| `return_rate_by_product` | `from`, `to`, `groupBy` (product, category or store), `limit` | sold_qty, returned_qty, return_rate_pct, refund_total. Matches the dashboard for the same range. |
| `restock_backlog` | optional `storeId`, `olderThanHours` | APPROVED lines of grade A or B with `restocked = 0`, and DRAFT RMAs awaiting approval, with age in hours (BR-07). |
| `run_readonly_query` | `sql`, `maxRows` | One SELECT or WITH statement over `mcp_*` views only. The guard rejects DML, `;`, comments, backticks, `@` variables, `INTO`, locking reads, `LOAD_FILE`, `SLEEP`, `BENCHMARK`, `information_schema`/`mysql`/`sys`, dotted names and any table that is not an `mcp_*` view or a CTE. |

The server is meant to connect as the `kestrel_mcp` account, which can only SELECT the `mcp_*` views (see OQ-07).

## Acceptance Criteria

- AC-01 Window by category
  - Given a FULFILLED order whose item is in a 30-day category, ordered 30 days ago
  - When the desk creates an RMA for it today
  - Then the RMA is created; and it is refused with 422 "the 30-day return window ended on ..." when the order is 31 days old. For a 60-day category (boots, jackets) the same applies at 60 and 61 days.
- AC-02 Window is configurable
  - Given a buyer sets a category's return window to 45 days with `PUT /api/categories/{id}/return-window`
  - When eligibility is checked for an order in that category
  - Then `windowDays` is 45 and `returnableUntil` is the order date plus 45 days, without a code change; a value of 0 or 400 is refused with 400.
- AC-03 Order status
  - Given an order that is CANCELLED or PLACED
  - When the desk creates an RMA or checks eligibility
  - Then creating is refused with 422 and eligibility returns `eligible: false` with a reason; PAID and FULFILLED orders are allowed.
- AC-04 Quantity across RMAs
  - Given a line of quantity 2 with one unit already in an earlier DRAFT or APPROVED RMA
  - When the desk tries to return 2 units
  - Then it is refused with 422 "only 1 of 2 can still be returned"; and when the earlier RMA was REJECTED, the 2 units are accepted.
- AC-05 Refund by grade
  - Given an item paid 100.00 (after discount)
  - When it is returned as A, B, SCRAP, or C
  - Then the refund is 100.00 for A, B and SCRAP and 85.00 for C, and no fee is taken.
- AC-06 Discounted orders
  - Given an order with a loyalty discount
  - When a line is returned
  - Then the refund is based on `line_total` minus that line's share of the discount, and returning all units over several RMAs adds up to exactly the amount paid.
- AC-07 Never more than paid
  - Given earlier RMAs have already refunded part of a line or an order
  - When a new RMA would take the total above the amount paid for the line or the captured payments for the order
  - Then the line refund is capped to what is left, and the RMA is refused with 422 if the order would still be over the captured payments. The MCP tool `refunds_exceeding_paid` returns no rows.
- AC-08 Manager approval
  - Given a DRAFT RMA with a refund above 150.00
  - When a STAFF user approves it
  - Then it is refused with 422 and stays DRAFT; a MANAGER or ADMIN can approve it. An RMA of exactly 150.00 can be approved by STAFF.
- AC-09 Restock
  - Given a DRAFT RMA with one A, one B, one C and one SCRAP line
  - When it is approved
  - Then stock rises by the returned quantity for the A and B lines at the RMA's receiving store, the C and SCRAP lines add no stock, the SCRAP line has `supplierClaim: true`, and the A and B lines have `restocked: true`.
- AC-10 Approve once
  - Given an APPROVED, REJECTED or superseded RMA
  - When someone approves it again
  - Then the call is refused with 422 and stock is not added a second time.
- AC-11 Draft does not move stock
  - Given the desk calculates a refund
  - When the DRAFT is created
  - Then `stock_levels` is unchanged until approval; a new DRAFT on the same order supersedes the earlier one; and "Discard draft" (reject) frees the quantity and the cap.
- AC-12 Serial returner flagged
  - Given a customer with 3 APPROVED RMAs on 3 different orders in the last 180 days, refunding 40% or more of what they bought in that period
  - When the desk checks any of their orders
  - Then `serialReturner` is true, the desk screen shows the warning, the dashboard lists them, and the MCP `serial_returners` tool lists their id.
- AC-13 Serial returner not flagged
  - Given a customer who returns 3 items from one order, or has only 2 orders, or under 40% by value, or whose extra RMAs are DRAFT or REJECTED
  - When flags are calculated
  - Then they are not flagged.
- AC-14 Flag never blocks
  - Given a flagged customer
  - When the desk creates and approves an RMA for them
  - Then both succeed as normal.
- AC-15 One call to answer "can this be returned?"
  - Given an order number
  - When `GET /api/returns/eligibility/{orderNumber}` is called
  - Then one response states whether the order can be returned, which lines (with returnable quantity, amount and `returnableUntil`), and the reason when it cannot; an unknown order returns 404.
- AC-16 Dashboard
  - Given approved returns in a date range
  - When the dashboard is requested
  - Then it shows return rate by product, category and store and the flagged customers; the MCP `return_rate_by_product` tool returns the same rows for the same range and grouping; a `to` before `from` returns 422.
- AC-17 Restock backlog
  - Given an APPROVED A or B line with `restocked = 0`, or a DRAFT RMA older than a day
  - When `restock_backlog` runs
  - Then each is listed with its age in hours; correctly restocked lines and approved C and SCRAP lines are not listed.
- AC-18 Desk sees name and order only
  - Given a desk user opens an order or an RMA
  - When the API responds
  - Then it contains the customer's name and the order, and no address, date of birth, phone or email.
- AC-19 MCP guard
  - Given a client calls `run_readonly_query`
  - When the statement is DML, has several statements, comments, `@` variables, `SLEEP`, `INTO OUTFILE`, `information_schema`, or reads a base table such as `customers`
  - Then it is rejected before it reaches the database; a plain SELECT or CTE over `mcp_*` views is accepted.
- AC-20 Desk screen states
  - Given the RMA desk or dashboard is opened
  - When data is loading, empty, or the API fails
  - Then a loading state, an empty message, or the API's error text is shown instead of a blank page.

## Assumptions & Open Questions
Items marked OPEN need an answer before the pilot.

- OQ-01 (OPEN, Tom and Finance) The ticket says "we charge a 15% restocking fee on anything graded C" and also "we never charge a customer to return something... the desk must not take a fee off anyone, ever". Both cannot hold. Assumption built: grade C is a reduced refund of 85%, not a fee, and the "restocking fee" in the ticket's feature list is read the same way. If Finance means a fee on top of grade C, or a fee on other grades, that is a different rule and conflicts with the brand promise. Confirm with Tom and Finance.
- OQ-02 (OPEN, Tom) The ticket says 30 days "everywhere" and "on our website", and also 60 days for boots and jackets. Assumption built: 30 days is the default, 60 for BOOT, JKT-WP and JKT-INS, per category and configurable. The website wording ("30 days everywhere") needs updating, or the 60-day categories need to be called out there.
- OQ-03 (DECLINED, DPO and legal) Keeping ID photos on the return record indefinitely and sharing them with other retailers in the fraud group is not built. It is personal data (a government ID) under UK GDPR: no lawful basis, retention period or data-sharing agreement has been shown, and indefinite retention and sharing with third parties are hard to justify. It needs a DPIA and sign-off from the DPO or legal first. A narrower approach (record only that ID was checked, with a short retention period) could be considered then.
- OQ-04 (OPEN, Finance) "Refund goes straight back to the original card, PayPal or gift card" is not built. There is no payment gateway integration. Approval records the refund; Finance pays it manually. Tom's "straight away" is therefore not met. It needs a gateway decision and a payment provider integration, and cannot be done before the pilot without one. This is the answer to "before pilot": manual refunds by Finance.
- OQ-05 (OPEN, engineering) The app has no authentication. Role checks use the `userId` the caller sends, so anyone can claim to be a manager. JWT (or similar) login is needed before production use, and `PUT /api/categories/{id}/return-window` should then be limited to BUYER and ADMIN. The ticket's "do not let the whole customer list go to everybody" is met for the returns screens (name and order only), but not for the app as a whole until login exists.
- OQ-06 (OPEN, engineering) `GET /api/users` returns `PasswordHash` (existing behaviour, not part of this feature). It should be removed from `UserDto`. The RMA desk uses that list to populate the acting-user selector.
- OQ-07 (OPEN, ops, urgent) The `kestrel_mcp` database account is not provisioned on the local database (only `kestrel_app` exists), so the MCP server cannot yet be used with its intended least-privilege account. The root database password was committed to `.mcp.json` in git history (commit `f0518c6`). Rotate it, run `db-setup/03-create-mcp-user.sql` with a new password, and keep the password in the `MCP_DB_PASSWORD` environment variable.
- OQ-08 (assumption, Tom to confirm) Rate definitions. The serial returner rate is by value: the sum of `refund_total` of APPROVED RMAs divided by the `total_amount` of non-cancelled orders in the same 180 days. The dashboard return rate is by quantity: returned quantity divided by sold quantity. The dashboard cohorts differ: items sold are counted by order date in the range, items returned by the RMA's creation date in the range. A return of an item sold before the range counts as returned but not sold (a rate can show 0.00% alongside returns, or exceed 100%). Read dashboard rates as an indicator, not an exact cohort rate.
- OQ-09 (OPEN, Tom) The ticket does not say how much to refund for SCRAP (faulty or unusable). Assumption: 100% of the amount paid (a faulty item is not the customer's doing), with the supplier claim pursued by the buyers. Confirm.
- OQ-10 (known limitation, engineering) Concurrency. Creating an RMA locks the order row, and approving locks the RMA and the stock rows, so double approval and concurrent RMAs on one order are safe. One residual race remains: if two approvals restock the same product at the same store and that store has no stock row yet, both may try to create it. The unique key `uq_stock_store_product` makes one fail with a database error (500) that can be retried; stock is not corrupted.
- OQ-11 (assumption) A return refunds the amount paid for the item, including its share of the discount. Delivery charges are not refunded.
- OQ-12 (assumption) Returns are attributed to the receiving store (the RMA's store), not the store that sold the item. The dashboard and MCP agree on this.
- OQ-13 (assumption) `db-setup/04-returns-test-seed.sql` is test data only (RMA-TEST-* rows) and must not be run in production.

### What we can do before the pilot, and what we cannot
For Tom.

Can do (built and tested):
- Return window per category in the database, changeable without code (30 days, 60 for boots and jackets).
- Grading A, B, C and SCRAP; partial refunds; refunds never above what was paid (per item and per order, discount included).
- Manager approval above 150.00 (with the caveat that, without login, the role comes from the user id sent).
- Same-day restock of A and B in the right store, and a supplier claim flag for SCRAP.
- Serial returner flag on the RMA desk and dashboard; it does not block refunds.
- RMA desk screen, returns dashboard, and the single "can this be returned?" call for the chat team (`GET /api/returns/eligibility/{orderNumber}`).
- MCP tools for analysts, without personal data.

Can do once you confirm (a decision, not engineering): OQ-01 (C is a reduced refund, not a fee), OQ-02 (website wording), OQ-09 (SCRAP refund percent).

Cannot do before the pilot, or not as asked:
- Instant refund to the original card, PayPal or gift card: no payment gateway. Finance pays manually after approval (OQ-04).
- ID photos kept forever and shared with other retailers: declined pending DPO and legal review (OQ-03).
- Real access control: the app has no login. Until JWT login is added, manager approval and "name and order only" cannot be enforced against a determined user (OQ-05, OQ-06).
- The least-privilege MCP account and a rotated database password (OQ-07) need ops action.
- Not asked for and not built: exchanges, store credit, courier labels.

## Traceability
Tests are in `backend/Kestrel.Tests/ReturnServiceTests.cs` (RS), `backend/Kestrel.Tests/ReturnPricingTests.cs` (RP) and `kestrel-mysql-mcp/test/guard.test.js` (G, by describe title). Test names are the C# method names or the Node describe titles.

| Business rule | Acceptance criteria | Endpoint / screen | Test name |
|---|---|---|---|
| BR-01 | AC-01, AC-02, AC-15 | POST /api/returns; GET /api/returns/eligibility/{orderNumber}; PUT /api/categories/{id}/return-window; RMA desk | RS: `The_return_window_is_inclusive_and_depends_on_the_category`, `The_window_counts_calendar_days_not_hours`, `Only_the_expired_line_is_refused_when_categories_differ`, `Eligibility_reports_returnableUntil_per_line`, `Eligibility_marks_only_the_expired_line_as_not_returnable`, `Eligibility_of_an_unknown_order_is_not_found` |
| BR-02 | AC-03, AC-15 | POST /api/returns; GET /api/returns/eligibility/{orderNumber}; RMA desk "Not returnable" banner | RS: `Cancelled_and_placed_orders_cannot_be_returned`, `Paid_and_fulfilled_orders_can_be_returned` |
| BR-03 | AC-04, AC-11 | POST /api/returns; POST /api/returns/{id}/reject; RMA desk quantity inputs | RS: `Cannot_return_more_than_was_ordered_in_one_rma`, `Quantity_is_counted_across_every_rma_on_the_order`, `A_rejected_rma_does_not_use_up_the_quantity`, `A_line_from_another_order_is_refused`, `A_new_draft_supersedes_the_earlier_draft_on_the_same_order`, `Rejecting_a_draft_frees_the_quantity_and_the_refund_cap` |
| BR-04 | AC-05, AC-06 | POST /api/returns; RMA desk refund preview | RP: `Without_a_discount_each_line_is_paid_in_full`, `An_order_with_no_lines_has_nothing_paid`, `The_discount_is_shared_between_lines_in_proportion_to_their_value`, `The_last_line_absorbs_the_rounding_difference`, `The_last_line_is_the_one_with_the_highest_id_whatever_the_list_order`, `Paid_amounts_always_add_up_to_subtotal_minus_discount`, `A_discount_larger_than_the_order_never_makes_a_line_negative`, `Returning_one_unit_at_a_time_adds_up_to_exactly_the_paid_amount`, `Returning_in_uneven_chunks_adds_up_to_exactly_the_paid_amount`, `A_discounted_order_refunds_exactly_what_was_paid_in_total`; RS: `The_refund_uses_the_discounted_price` |
| Grade refund % (BR-04/BR-05, OQ-01, OQ-09) | AC-05 | POST /api/returns; RMA desk | RP: `Grades_A_B_and_scrap_refund_the_full_paid_amount`, `Grade_C_refunds_the_configured_percentage`, `Grade_C_rounds_to_two_decimals`; RS: `A_grade_c_item_refunds_85_percent`, `Grades_are_accepted_in_any_case`, `An_unknown_grade_is_a_validation_error` |
| BR-05 | AC-07 | POST /api/returns; GET eligibility (`returnableAmount`); MCP `refunds_exceeding_paid` | RP: `The_refund_is_capped_to_what_is_left_of_the_paid_amount`, `RefundForLine_is_capped_by_what_was_already_refunded`; RS: `Refunds_on_a_discounted_order_never_exceed_what_was_paid_even_over_repeated_rmas`, `Eligibility_shows_what_can_still_be_refunded_after_earlier_rmas`, `The_order_total_refunded_never_exceeds_the_captured_payments`, `Only_captured_payments_count_towards_the_order_cap`, `Eligibility_never_offers_more_than_the_order_has_left`, `A_rule_violation_in_create_propagates_and_persists_nothing`; G: `domain query builders` |
| BR-06 | AC-08 | POST /api/returns/{id}/approve; RMA desk Approve button and manager note | RS: `Staff_can_approve_up_to_and_including_150`, `Staff_cannot_approve_above_150`, `Managers_and_admins_can_approve_above_150`, `An_unknown_or_inactive_approver_is_refused` |
| BR-07 | AC-09, AC-10, AC-11, AC-17 | POST /api/returns/{id}/approve; POST /api/returns/{id}/reject; RMA desk; MCP `restock_backlog` | RS: `Grades_a_and_b_are_restocked_at_the_rma_store_and_c_and_scrap_are_not`, `Restocking_creates_the_stock_row_when_the_store_has_none`, `Scrap_sets_the_supplier_claim_and_no_other_grade_does`, `Creating_a_draft_does_not_touch_stock`, `Approving_twice_is_rejected_and_stock_is_only_added_once`, `A_second_approval_after_the_first_is_refused_and_stock_is_added_once`, `A_rejected_rma_cannot_be_approved`, `A_superseded_rma_cannot_be_approved`, `Approving_an_unknown_rma_is_not_found`, `Only_a_draft_can_be_rejected`, `Rejecting_an_unknown_rma_is_not_found`, `An_unknown_or_inactive_user_cannot_reject`, `Superseding_leaves_approved_rmas_and_other_orders_alone`; G: `domain query builders` |
| BR-08 | AC-12, AC-13, AC-14, AC-16 | Eligibility `serialReturner`; GET /api/returns/dashboard; RMA desk banner; MCP `serial_returners` | RS: `One_order_with_several_returned_items_is_not_a_serial_returner`, `Three_rmas_on_three_orders_at_40_percent_by_value_is_flagged`, `Under_40_percent_by_value_is_not_flagged`, `Two_orders_are_not_enough_even_with_three_rmas_and_a_high_rate`, `Two_rmas_are_not_enough`, `Rejected_and_old_rmas_do_not_count`, `Draft_rmas_do_not_count_towards_the_serial_returner_flag`, `The_thresholds_come_from_configuration`, `The_serial_flag_never_blocks_creating_or_approving_a_return`, `Approving_the_third_rma_is_what_raises_the_flag_and_it_blocks_nothing`, `The_dashboard_lists_flagged_customers`; G: `domain query builders` |
| Dashboard (supports BR-05, BR-08) | AC-16 | GET /api/returns/dashboard; Returns dashboard page; MCP `return_rate_by_product` | RS: `The_dashboard_lists_flagged_customers`, `The_dashboard_rejects_an_inverted_date_range`; G: `domain query builders` |
| Desk sees name and order only (ticket "Who sees what") | AC-18 | Desk DTOs; RMA desk | RS: `Desk_dtos_expose_no_address_date_of_birth_phone_or_email`, `Desk_data_carries_only_the_customer_name` |
| MCP read-only guard | AC-19 | MCP `run_readonly_query` | G: `guard: statements that are not a single SELECT/WITH`, `guard: multiple statements`, `guard: comments`, `guard: base tables and other schemas`, `guard: relation-position bypasses`, `guard: dangerous functions, variables and clauses`, `guard: escaping tricks`, `guard: allowed queries` |
| UI states | AC-20 | RMA desk; Returns dashboard | No automated test; shared `Status` component, checked by hand |
