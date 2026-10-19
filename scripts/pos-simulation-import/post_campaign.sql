-- Demo "post-campaign" segment for the pchilka-pos-sim tenant (local dev only; run after clients.sql).
-- The Post-campaign page analyses a customer list (normally imported from CSV) before/after a campaign date.
-- This builds one such list from the simulation: 800 customers who bought in the first half of September,
-- as if they had been targeted by a campaign that started on 2026-09-16. Idempotent.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';

DELETE FROM post_campaign_segments
 WHERE "TenantId" = (SELECT tid FROM t) AND "Name" = 'Кампанія «Повернення» (симуляція)';

CREATE TEMP TABLE picked AS
  SELECT DISTINCT tx."CustomerId" AS cid
  FROM pos_transactions tx
  WHERE tx."TenantId" = (SELECT tid FROM t)
    AND tx."CustomerId" IS NOT NULL
    AND tx."CreatedAt" >= '2026-09-01' AND tx."CreatedAt" < '2026-09-16'
  ORDER BY 1
  LIMIT 800;

INSERT INTO post_campaign_segments
  ("TenantId","CreatedByUserId","Name","UploadedCount","MatchedCount","AfterStart","AfterEnd","SegmentHash","AnalyzedAt")
SELECT (SELECT tid FROM t),
       (SELECT "Id" FROM users WHERE "Email"='manager@pchilka.local'),
       'Кампанія «Повернення» (симуляція)',
       (SELECT count(*) FROM picked), (SELECT count(*) FROM picked),
       DATE '2026-09-16', DATE '2026-09-30',
       md5('pchilka-sim-return-campaign'), now();

INSERT INTO post_campaign_segment_members ("TenantId","SegmentId","CustomerId")
SELECT (SELECT tid FROM t),
       (SELECT "Id" FROM post_campaign_segments
         WHERE "TenantId" = (SELECT tid FROM t) AND "Name" = 'Кампанія «Повернення» (симуляція)'),
       cid
FROM picked;
COMMIT;

SELECT "Name","MatchedCount","AfterStart","AfterEnd" FROM post_campaign_segments
WHERE "TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim');
