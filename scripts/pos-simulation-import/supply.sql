-- The POS export has no supplier data. This adds ONE simulated supplier + delivery schedule so the
-- ADU / buffer / order engines treat the imported items as orderable. Run after load.sql (local dev only).
\set ON_ERROR_STOP on
BEGIN;

INSERT INTO suppliers ("TenantId","Name","DeliveryDays","HasSupplierPortal","ReturnPolicy")
SELECT "Id",'Постачальник (симуляція)',2,false,false FROM tenants WHERE "Slug"='pchilka-pos-sim'
  AND NOT EXISTS (SELECT 1 FROM suppliers s WHERE s."TenantId"=tenants."Id" AND s."Name"='Постачальник (симуляція)');

-- Deliveries Mon/Wed/Fri, order 2 days ahead (day-of-week: 1=Mon .. 7=Sun).
INSERT INTO supply_schedules ("TenantId","LocationId","SupplierId","DayOfWeek","OrderLeadDays")
SELECT l."TenantId", l."Id", s."Id", ARRAY[1,3,5], 2
FROM locations l JOIN suppliers s ON s."TenantId"=l."TenantId" AND s."Name"='Постачальник (симуляція)'
WHERE l."TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')
  AND NOT EXISTS (SELECT 1 FROM supply_schedules x WHERE x."LocationId"=l."Id" AND x."SupplierId"=s."Id");

UPDATE items i SET "DefaultSupplierId"=s."Id"
FROM suppliers s
WHERE s."TenantId"=i."TenantId" AND s."Name"='Постачальник (симуляція)'
  AND i."TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim');

INSERT INTO product_supplier_settings ("TenantId","ProductId","SupplierId","Moq","Usq","PricePurchase","DeliveryDays","IsPrimary")
SELECT i."TenantId", i."Id", s."Id", 1, 1, ROUND(i."PriceRetail"*0.75,2), 2, true
FROM items i JOIN suppliers s ON s."TenantId"=i."TenantId" AND s."Name"='Постачальник (симуляція)'
WHERE i."TenantId"=(SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')
ON CONFLICT DO NOTHING;

COMMIT;
