-- SIMULATED mobile-app content + engagement for the pchilka-pos-sim tenant (local dev only; run after loyalty.sql).
-- The export contains nothing about the app, so banners, a catalog and their views/clicks/scans below are
-- generated purely so the "Застосунок → Аналітика" pages have something to show. Promotions are not seeded.
-- Idempotent: removes this tenant's "(симуляція)" content first.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';

DELETE FROM banners WHERE "TenantId" = (SELECT tid FROM t) AND "Title" LIKE '%(симуляція)';
DELETE FROM mobile_catalog_settings WHERE "TenantId" = (SELECT tid FROM t) AND "Title" LIKE '%(симуляція)';

CREATE TEMP TABLE stores AS
  SELECT "Id" AS id, row_number() OVER (ORDER BY "Name") AS n FROM locations WHERE "TenantId" = (SELECT tid FROM t);
CREATE TEMP TABLE accs AS
  SELECT "ConsumerAccountId" AS id, row_number() OVER (ORDER BY "JoinedAt", "Id") AS n
  FROM loyalty_memberships WHERE "TenantId" = (SELECT tid FROM t);

-- ── Banners ──────────────────────────────────────────────────────────────────────────────────
CREATE TEMP TABLE b AS
  SELECT gen_random_uuid() AS id, v.title, v.eyebrow, v.descr, v.bg, v.accent, v.views, v.clicks
  FROM (VALUES
    ('Свіжа випічка щодня (симуляція)', 'Новинка', 'Тепла випічка власного виробництва щоранку', '#7C2D12', '#FDBA74', 4200, 640),
    ('Тиждень молочних продуктів (симуляція)', 'Акція', 'Знижки на сир, сметану та йогурти', '#1E3A8A', '#93C5FD', 3100, 380)
  ) AS v(title, eyebrow, descr, bg, accent, views, clicks);

INSERT INTO banners ("Id","TenantId","Title","Eyebrow","Description","Body","Terms","Icon","BackgroundColor","AccentColor",
                     "DetailMode","ValidFrom","IsActive","SortOrder","PublishedAt")
  SELECT id, (SELECT tid FROM t), title, eyebrow, descr, descr, 'Діє в усіх магазинах мережі', 'sparkles', bg, accent,
         'internal', now() - interval '40 days', true, 0, now() - interval '40 days'
  FROM b;
INSERT INTO banner_locations ("TenantId","BannerId","LocationId")
  SELECT (SELECT tid FROM t), b.id, s.id FROM b CROSS JOIN stores s;

-- views / clicks spread over the last 30 days
INSERT INTO banner_events ("TenantId","BannerId","EventType","ConsumerAccountId","OccurredAt","StoreId")
  SELECT (SELECT tid FROM t), b.id, ev.kind,
         (SELECT a.id FROM accs a WHERE a.n = 1 + (abs(hashtext(b.id::text || ev.kind || ev.g)) % (SELECT count(*) FROM accs))),
         now() - (abs(hashtext(ev.kind || ev.g || b.id::text)) % 2592000) * interval '1 second',
         (SELECT s.id FROM stores s WHERE s.n = 1 + (abs(hashtext(ev.g || b.id::text)) % (SELECT count(*) FROM stores)))
  FROM b
  JOIN LATERAL (
    SELECT 'view' AS kind, g FROM generate_series(1, b.views) g
    UNION ALL
    SELECT 'click', g FROM generate_series(1, b.clicks) g
  ) ev ON true;

-- ── Catalog ──────────────────────────────────────────────────────────────────────────────────
CREATE TEMP TABLE cat AS SELECT gen_random_uuid() AS id;
INSERT INTO mobile_catalog_settings
  ("Id","TenantId","Title","Description","LayoutMode","IsEnabled","PublishAt","Status","PublishedAt")
  SELECT id, (SELECT tid FROM t), 'Каталог пропозицій (симуляція)', 'Добірка популярних товарів з вигідними цінами',
         'grid', true, now() - interval '40 days', 'published', now() - interval '40 days'
  FROM cat;
INSERT INTO mobile_catalog_locations ("Id","TenantId","SettingsId","LocationId")
  SELECT gen_random_uuid(), (SELECT tid FROM t), cat.id, s.id FROM cat CROSS JOIN stores s;

CREATE TEMP TABLE topp AS
  SELECT i."Id" AS pid, i."Name" AS name, i."PriceRetail" AS price,
         row_number() OVER (ORDER BY sum(d."QuantitySold") DESC) AS rn
  FROM daily_sales d JOIN items i ON i."Id" = d."ProductId"
  WHERE d."TenantId" = (SELECT tid FROM t) AND i."PriceRetail" IS NOT NULL
  GROUP BY i."Id", i."Name", i."PriceRetail"
  ORDER BY sum(d."QuantitySold") DESC
  LIMIT 12;
INSERT INTO mobile_catalog_items
  ("Id","TenantId","SettingsId","ProductId","SortOrder","IsFeatured","MobileDiscountPercent",
   "MobilePriceSnapshot","ProductNameSnapshot","RegularPriceSnapshot")
  SELECT gen_random_uuid(), (SELECT tid FROM t), cat.id, p.pid, p.rn::int, p.rn <= 3, 10,
         ROUND(p.price * 0.9, 2), p.name, p.price
  FROM topp p CROSS JOIN cat;

INSERT INTO mobile_catalog_events
  ("Id","TenantId","CatalogId","StoreId","ProductId","ConsumerAccountId","SessionId","EventType","OccurredAt")
  SELECT gen_random_uuid(), (SELECT tid FROM t), cat.id,
         (SELECT s.id FROM stores s WHERE s.n = 1 + (abs(hashtext('s' || ev.kind || ev.g)) % (SELECT count(*) FROM stores))),
         CASE WHEN ev.kind = 'catalog_view' THEN NULL
              ELSE (SELECT p.pid FROM topp p WHERE p.rn = 1 + (abs(hashtext('p' || ev.kind || ev.g)) % 12)) END,
         (SELECT a.id FROM accs a WHERE a.n = 1 + (abs(hashtext('a' || ev.kind || ev.g)) % (SELECT count(*) FROM accs))),
         'sim-' || substr(md5(ev.kind || ev.g), 1, 12), ev.kind,
         now() - (abs(hashtext('t' || ev.kind || ev.g)) % 2592000) * interval '1 second'
  FROM cat
  JOIN LATERAL (
    SELECT 'catalog_view' AS kind, g FROM generate_series(1, 2500) g
    UNION ALL SELECT 'product_view', g FROM generate_series(1, 3000) g
    UNION ALL SELECT 'product_scan', g FROM generate_series(1, 420) g
  ) ev ON true;
COMMIT;

SELECT (SELECT count(*) FROM banner_events be JOIN banners b ON b."Id" = be."BannerId"
        WHERE b."TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) banner_events,
       (SELECT count(*) FROM mobile_catalog_events WHERE "TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) catalog_events;
