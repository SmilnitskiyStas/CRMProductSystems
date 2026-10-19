-- POS receipts + simulated stock ledger for the pchilka-pos-sim tenant (local dev only; run after load.sql/supply.sql).
-- Expects transactions.csv / tx_items.csv / locations.csv / items.csv in /tmp/pos/.
-- Idempotent: wipes this tenant's POS + movement rows first.
--   * receipts -> pos_transactions (status 'fiscalized', payment 'cash' — the export carries no payment types)
--   * stock_movements: Mon/Wed/Fri 'receipt' sized to the actual sales of the coming cycle (x1.15 / x1.0 / x0.85
--     by product hash, so some products drift up and some down) + a few 'write_off'. QuantityAfter is the simulated
--     running balance (opening = 4 days of average, - sales + receipts, floored at 0).
--     product_stock is set to the final balance.
\set ON_ERROR_STOP on
\encoding UTF8
BEGIN;
CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';
DELETE FROM pos_transaction_items WHERE "TransactionId" IN (SELECT "Id" FROM pos_transactions WHERE "TenantId"=(SELECT tid FROM t));
DELETE FROM pos_transactions WHERE "TenantId"=(SELECT tid FROM t);
DELETE FROM stock_movements WHERE "TenantId"=(SELECT tid FROM t);

CREATE TEMP TABLE s_tx (receipt_id text, shop_code text, receipt_at text, total numeric, discount numeric);
CREATE TEMP TABLE s_txi (receipt_id text, product_code text, qty numeric, unit_price numeric, discount numeric);
CREATE TEMP TABLE s_loc (shop_code text, name text);
CREATE TEMP TABLE s_item (product_code text, name text, unit text, barcode text, price numeric);
\copy s_tx FROM '/tmp/pos/transactions.csv' CSV HEADER
\copy s_txi FROM '/tmp/pos/tx_items.csv' CSV HEADER
\copy s_loc FROM '/tmp/pos/locations.csv' CSV HEADER
\copy s_item FROM '/tmp/pos/items.csv' CSV HEADER

CREATE TEMP TABLE shop_loc AS
  SELECT sl.shop_code, l."Id" AS location_id
  FROM s_loc sl JOIN locations l ON l."Name"=sl.name AND l."TenantId"=(SELECT tid FROM t);
-- product_code -> item id (names are not unique in theory; take the lowest id to stay deterministic)
CREATE TEMP TABLE prod AS
  SELECT si.product_code, MIN(i."Id"::text)::uuid AS product_id
  FROM s_item si JOIN items i ON i."Name"=si.name AND i."TenantId"=(SELECT tid FROM t)
  GROUP BY si.product_code;

CREATE TEMP TABLE tx AS
  SELECT gen_random_uuid() AS id, x.*, sl.location_id,
         (x.receipt_at::timestamp AT TIME ZONE 'Europe/Kyiv') AS ts
  FROM s_tx x JOIN shop_loc sl USING (shop_code);
INSERT INTO pos_transactions ("Id","TenantId","LocationId","ReceiptNumber","PaymentType","TotalAmount","TaxAmount","Status","CreatedAt")
  SELECT id,(SELECT tid FROM t),location_id,receipt_id,'cash',total,ROUND(total*20/120,2),'fiscalized',ts FROM tx;
INSERT INTO pos_transaction_items ("TransactionId","ProductId","Quantity","PriceRetail","DiscountAmount","PriceFinal")
  SELECT tx.id, p.product_id, i.qty, i.unit_price, COALESCE(i.discount,0),
         GREATEST(0, i.unit_price - COALESCE(i.discount,0)/NULLIF(i.qty,0))
  FROM s_txi i JOIN tx USING (receipt_id) JOIN prod p USING (product_code);

-- Simulated stock ledger over daily_sales for products that have an opening stock row.
CREATE TEMP TABLE pairs AS
  SELECT ps."LocationId" AS loc, ps."ProductId" AS prd, ps."Id" AS stock_id,
         (hashtext(ps."ProductId"::text) % 3 + 3) % 3 AS drift_cls
  FROM product_stock ps WHERE ps."TenantId"=(SELECT tid FROM t) AND ps."BatchNumber"='SIM-OPEN';
CREATE TEMP TABLE grid AS
  SELECT p.loc, p.prd, p.stock_id, p.drift_cls, g.d::date AS d, COALESCE(ds."QuantitySold",0) AS sold,
         EXTRACT(isodow FROM g.d) IN (1,3,5) AS is_delivery
  FROM pairs p
  CROSS JOIN generate_series('2026-09-01'::date,'2026-09-30'::date,'1 day') g(d)
  LEFT JOIN daily_sales ds ON ds."LocationId"=p.loc AND ds."ProductId"=p.prd AND ds."Date"=g.d::date;
CREATE INDEX ON grid (loc, prd, d);
-- receipt qty on a delivery day = sales from that day up to (not incl.) the next delivery day, x drift factor
CREATE TEMP TABLE plan AS
  SELECT g.*,
    CASE WHEN g.is_delivery THEN
      CEIL((SELECT COALESCE(SUM(x.sold),0) FROM grid x
            WHERE x.loc=g.loc AND x.prd=g.prd AND x.d>=g.d
              AND x.d < COALESCE((SELECT MIN(y.d) FROM grid y
                                  WHERE y.loc=g.loc AND y.prd=g.prd AND y.d>g.d AND y.is_delivery), '2026-10-01'))
           * CASE g.drift_cls WHEN 0 THEN 1.15 WHEN 1 THEN 1.0 ELSE 0.85 END)
    ELSE 0 END AS recv,
    (SELECT AVG(NULLIF(x.sold,0))*4 FROM grid x WHERE x.loc=g.loc AND x.prd=g.prd) AS opening
  FROM grid g;
CREATE TEMP TABLE bal AS
  SELECT *,
    GREATEST(0, ROUND(COALESCE(opening,0) + SUM(recv - sold) OVER w, 2)) AS bal_end,
    GREATEST(0, ROUND(COALESCE(opening,0) + COALESCE(SUM(recv - sold) OVER w0, 0), 2)) AS bal_before
  FROM plan
  WINDOW w  AS (PARTITION BY loc, prd ORDER BY d),
         w0 AS (PARTITION BY loc, prd ORDER BY d ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING);

INSERT INTO stock_movements ("TenantId","MovementType","ProductStockId","ProductId","ToLocationId","Quantity",
                             "QuantityBefore","QuantityAfter","ReferenceType","Notes","PerformedBy","CreatedAt")
  SELECT (SELECT tid FROM t),'receipt',stock_id,prd,loc,recv,bal_before,bal_end,'simulation','Поставка (симуляція)',
         (SELECT "Id" FROM users WHERE "Email"='manager@pchilka.local'),
         (d + time '09:00') AT TIME ZONE 'Europe/Kyiv'
  FROM bal WHERE recv > 0;
-- a few write-offs: every 7th product on day 10 and 20
INSERT INTO stock_movements ("TenantId","MovementType","ProductStockId","ProductId","FromLocationId","Quantity",
                             "QuantityBefore","QuantityAfter","ReferenceType","Notes","PerformedBy","CreatedAt")
  SELECT (SELECT tid FROM t),'write_off',stock_id,prd,loc,1,bal_end,bal_end-1,'simulation',
         'Списання: термін придатності (симуляція)',
         (SELECT "Id" FROM users WHERE "Email"='manager@pchilka.local'), (d + time '20:00') AT TIME ZONE 'Europe/Kyiv'
  FROM bal WHERE d IN ('2026-09-10','2026-09-20') AND ((hashtext(prd::text) % 7)+7)%7 = 0 AND bal_end >= 1;
UPDATE product_stock ps SET "Quantity"=GREATEST(1,b.bal_end)
  FROM bal b WHERE b.stock_id=ps."Id" AND b.d='2026-09-30';
COMMIT;

SELECT (SELECT count(*) FROM pos_transactions WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) tx,
       (SELECT count(*) FROM pos_transaction_items i JOIN pos_transactions t ON t."Id"=i."TransactionId"
         WHERE t."TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) tx_items,
       (SELECT count(*) FROM stock_movements WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) movements;
