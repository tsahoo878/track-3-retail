# KO-341: Daily sales report puts some orders on the wrong day

Reported by: Tom Achebe (Customer Operations), with Priya Raman (Finance)
Area: Reports
Priority: High

## What we see

Finance reconciles the daily sales report against the till system and the web store every morning. For the last
week of September the totals did not agree on two days, and both days had orders taken around midnight.

Our shops and our customers work on UK time. A sale made at 00:30 on Wednesday 30 September in the UK is a
Wednesday sale. The report puts some of these on the day before.

## Numbers (seed data, report called with the date shown)

`GET /api/reports/daily-sales?date=2026-09-30` (Wednesday 30 September)

| | Orders | Net sales |
|---|---|---|
| What the report shows | 6 | 688.75 |
| What finance says it should be | 7 | 911.90 |

The two days either side are also wrong:

| Date | Report shows | Finance says |
|---|---|---|
| Tue 29 Sep | 6 orders, 1038.90 | 2 orders, 550.00 |
| Thu 1 Oct | 0 orders, 0.00 | 3 orders, 265.75 |

Orders finance says belong to a different day than the report puts them on:

| Order | Placed (UK time) | Net | Report puts it on | It belongs on |
|---|---|---|---|---|
| KO-20260930-0001 | Wed 30 Sep 00:12 | 242.10 | 29 Sep | 30 Sep |
| KO-20260930-0002 | Wed 30 Sep 00:31 | 59.00 | 29 Sep | 30 Sep |
| KO-20260930-0003 | Wed 30 Sep 00:48 | 70.00 | 29 Sep | 30 Sep |
| KO-20260930-0004 | Wed 30 Sep 00:55 | 117.80 | 29 Sep | 30 Sep |
| KO-20261001-0001 | Thu 1 Oct 00:05 | 137.75 | 30 Sep | 1 Oct |
| KO-20261001-0002 | Thu 1 Oct 00:40 | 69.00 | 30 Sep | 1 Oct |
| KO-20261001-0003 | Thu 1 Oct 00:50 | 59.00 | 30 Sep | 1 Oct |

Across the whole of August and September, 12 non-cancelled orders land on a different day from the one the shop or
customer would say. Cancelled orders are not part of the report, and that is correct.

## Steps to reproduce

1. Start the API against the seeded database.
2. Call `GET /api/reports/daily-sales?date=2026-09-30`.
3. Compare the order count and net sales with the table above.
4. Call the same report for 2026-09-29 and 2026-10-01.

## Expected

A report for a date counts every order placed from 00:00 to 23:59 on that date in the store's own time (UK time),
cancelled orders excluded. The same must be true of the date-range report, `GET /api/reports/daily-sales/range`.

## Actual

Orders placed in the first hour after midnight UK time are counted on the previous day.

## Notes

- Everyone looks at this report first thing in the morning, so please keep the response time as it is.
- Do not change the order times that are already saved.
