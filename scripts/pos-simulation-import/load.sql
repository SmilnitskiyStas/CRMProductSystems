-- Loads aggregate.py output into a NEW local tenant (run as the bootstrap superuser, local dev DB only).
-- Expects CSVs in /tmp/pos/ inside the postgres container. Aborts if the tenant already exists.
\set ON_ERROR_STOP on
\encoding UTF8
BEGIN;

CREATE TEMP TABLE s_loc (shop_code text, name text);
CREATE TEMP TABLE s_item (product_code text, name text, unit text, barcode text, price numeric);
CREATE TEMP TABLE s_sale (shop_code text, product_code text, date date, qty numeric);
\copy s_loc FROM '/tmp/pos/locations.csv' CSV HEADER
\copy s_item FROM '/tmp/pos/items.csv' CSV HEADER
\copy s_sale FROM '/tmp/pos/daily_sales.csv' CSV HEADER

DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM tenants WHERE "Slug" = 'pchilka-pos-sim') THEN
    RAISE EXCEPTION 'tenant pchilka-pos-sim already exists';
  END IF;
END $$;

INSERT INTO tenants ("Name","Slug","Plan","Modules","BusinessType")
VALUES ('Пчілка (POS симуляція 2026-09)','pchilka-pos-sim','basic',
        '["inventory","pos","procurement","marketing_analytics","marketplace"]'::jsonb,'retail');

CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';

CREATE TEMP TABLE m_loc AS
  SELECT l.shop_code, gen_random_uuid() AS id, l.name FROM s_loc l;
INSERT INTO locations ("Id","TenantId","Name","Type","LocationType")
  SELECT id,(SELECT tid FROM t),name,'shop','retail_store' FROM m_loc;

CREATE TEMP TABLE m_item AS
  SELECT i.*, gen_random_uuid() AS id FROM s_item i
  WHERE i.product_code IN (SELECT DISTINCT product_code FROM s_sale);
INSERT INTO items ("Id","TenantId","Name","Unit","ManagementType","MinStock","MaxStock","SafetyBuffer",
                   "PriceRetail","Barcodes","ItemType")
  SELECT id,(SELECT tid FROM t),name,unit,'MTS',0,0,0,price,
         CASE WHEN barcode ~ '^\d{8,14}$' THEN jsonb_build_array(barcode) ELSE '[]'::jsonb END,'product'
  FROM m_item;

INSERT INTO daily_sales ("TenantId","LocationId","ProductId","Date","QuantitySold","Source")
  SELECT (SELECT tid FROM t), l.id, i.id, s.date, s.qty, 'import'
  FROM s_sale s JOIN m_loc l USING (shop_code) JOIN m_item i USING (product_code);

-- Opening stock (not in the export): ~3 days of the last-14-day average, one batch per product/shop,
-- only for products sold on >=10 days. Deliberately thin so the buffer engine has orders to propose.
INSERT INTO product_stock ("TenantId","ProductId","LocationId","BatchNumber","Quantity","QuantityInitial","ExpiryDate","SourceType")
  SELECT (SELECT tid FROM t), i.id, l.id, 'SIM-OPEN',
         GREATEST(1, ROUND(SUM(s.qty) FILTER (WHERE s.date >= '2026-09-17') / 14.0 * 3, 0)),
         GREATEST(1, ROUND(SUM(s.qty) FILTER (WHERE s.date >= '2026-09-17') / 14.0 * 3, 0)),
         DATE '2026-11-30', 'simulation'
  FROM s_sale s JOIN m_loc l USING (shop_code) JOIN m_item i USING (product_code)
  GROUP BY i.id, l.id
  HAVING COUNT(DISTINCT s.date) >= 10 AND SUM(s.qty) FILTER (WHERE s.date >= '2026-09-17') > 0;

-- Login for the web app: reuses the dev password of manager@demo.local.
INSERT INTO users ("TenantId","Email","FullName","PasswordHash","Role")
  SELECT (SELECT tid FROM t),'manager@pchilka.local','Менеджер Пчілка',"PasswordHash",'store_manager'
  FROM users WHERE "Email"='manager@demo.local';

COMMIT;
SELECT (SELECT count(*) FROM locations WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) locs,
       (SELECT count(*) FROM items WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) items,
       (SELECT count(*) FROM daily_sales WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) sales,
       (SELECT count(*) FROM product_stock WHERE "TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) stock;
