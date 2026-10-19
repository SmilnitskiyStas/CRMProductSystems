-- Customers for the pchilka-pos-sim tenant (local dev only; run after history.sql / cash.sql).
-- The export carries only an anonymous loyalty client code per receipt, so each distinct code becomes
-- a customer named "Клієнт <code>" (no phone/e-mail), and its receipts get CustomerId. This is what the
-- marketing-analytics pages (RFM segments, audience builder, store migration) are built on.
-- Expects /tmp/pos/tx_clients.csv. Idempotent: rebuilds this tenant's customers.
\set ON_ERROR_STOP on
\encoding UTF8
BEGIN;
CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';
CREATE TEMP TABLE s_cl (receipt_id text, client_code text);
\copy s_cl FROM '/tmp/pos/tx_clients.csv' CSV HEADER

UPDATE pos_transactions SET "CustomerId" = NULL WHERE "TenantId" = (SELECT tid FROM t);
DELETE FROM customers WHERE "TenantId" = (SELECT tid FROM t) AND "Name" LIKE 'Клієнт %';

CREATE TEMP TABLE m_cust AS
  SELECT client_code, gen_random_uuid() AS id FROM (SELECT DISTINCT client_code FROM s_cl) d;

INSERT INTO customers ("Id","TenantId","Name","Tags")
  SELECT id, (SELECT tid FROM t), 'Клієнт ' || client_code, '{}' FROM m_cust;

UPDATE pos_transactions tx SET "CustomerId" = m.id
FROM s_cl c JOIN m_cust m USING (client_code)
WHERE tx."TenantId" = (SELECT tid FROM t) AND tx."ReceiptNumber" = c.receipt_id;

UPDATE customers cu SET "TotalOrders" = a.n, "TotalSpent" = a.s
FROM (SELECT "CustomerId" AS cid, count(*) AS n, sum("TotalAmount") AS s
      FROM pos_transactions WHERE "TenantId" = (SELECT tid FROM t) AND "CustomerId" IS NOT NULL
      GROUP BY 1) a
WHERE cu."Id" = a.cid;
COMMIT;

SELECT (SELECT count(*) FROM customers WHERE "TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) customers,
       (SELECT count(*) FROM pos_transactions WHERE "TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')
                                              AND "CustomerId" IS NOT NULL) linked_receipts;
