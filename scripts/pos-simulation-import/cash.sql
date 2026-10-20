-- Simulated cash register for the pchilka-pos-sim tenant (local dev only; run after history.sql).
-- One closed shift per store per calendar day (Europe/Kyiv): opened at the first receipt, closed at the last,
-- TotalSales = sum of that day's receipts. Receipts get ShiftId, the manager as cashier and a fiscal number.
-- No real fiscal provider is called — this is DB-only. Idempotent.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';
CREATE TEMP TABLE cashier AS
  SELECT "Id" AS uid FROM users WHERE "Email"='manager@pchilka.local';

UPDATE pos_transactions SET "ShiftId"=NULL WHERE "TenantId"=(SELECT tid FROM t);
DELETE FROM pos_shifts WHERE "TenantId"=(SELECT tid FROM t);

INSERT INTO pos_shifts ("TenantId","LocationId","CashierId","ShiftNumber","FiscalShiftNumber",
                        "OpenedAt","ClosedAt","OpeningCash","ClosingCash","TotalSales")
SELECT (SELECT tid FROM t), x."LocationId", (SELECT uid FROM cashier),
       ROW_NUMBER() OVER (PARTITION BY x."LocationId" ORDER BY x.d),
       'SIM-' || to_char(x.d,'YYYYMMDD'),
       x.opened, x.closed, 500, 500 + x.total, x.total
FROM (
  SELECT "LocationId", ("CreatedAt" AT TIME ZONE 'Europe/Kyiv')::date AS d,
         MIN("CreatedAt") AS opened, MAX("CreatedAt") AS closed, SUM("TotalAmount") AS total
  FROM pos_transactions
  WHERE "TenantId"=(SELECT tid FROM t) AND "Status"='fiscalized'
  GROUP BY 1, 2
) x;

UPDATE pos_transactions tx
SET "ShiftId"=s."Id", "CashierId"=(SELECT uid FROM cashier), "FiscalNumber"=tx."ReceiptNumber"
FROM pos_shifts s
WHERE tx."TenantId"=(SELECT tid FROM t)
  AND s."LocationId"=tx."LocationId"
  AND s."FiscalShiftNumber"='SIM-' || to_char((tx."CreatedAt" AT TIME ZONE 'Europe/Kyiv')::date,'YYYYMMDD');

COMMIT;
SELECT (SELECT count(*) FROM pos_shifts WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) shifts,
       (SELECT count(*) FROM pos_transactions WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim') AND "ShiftId" IS NULL) tx_without_shift;
