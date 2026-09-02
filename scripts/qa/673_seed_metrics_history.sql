-- TASK-673 QA seed: ~10 historical supplier_metrics_snapshots for public supplier
-- b4e21658-13b7-44d2-8924-4fd1aa5105d3 (tenant f1bbc48c-ded8-4c02-988f-93875ce2dcee)
-- spread across the last ~40 days, so the buyer metrics-history endpoint + trend charts
-- have real data. Run as the crm superuser (RLS bypassed — seed only):
--   docker exec -i crmproductsystems-postgres-1 psql -U crm -d crm -f - < this file
-- Idempotent: clears prior historical rows for this supplier first.

DELETE FROM supplier_metrics_snapshots
 WHERE "SupplierId" = 'b4e21658-13b7-44d2-8924-4fd1aa5105d3'
   AND "SnapshotDate" < CURRENT_DATE;

INSERT INTO supplier_metrics_snapshots
  ("Id","SupplierId","TenantId","SnapshotDate","AvgDeliveryDays","OrderAccuracy","QualityScore",
   "Rating","CancellationRate","ResponseTimeHours","DeliverySampleSize","ResponseSampleSize","CreatedAt")
SELECT gen_random_uuid(),
       'b4e21658-13b7-44d2-8924-4fd1aa5105d3',
       'f1bbc48c-ded8-4c02-988f-93875ce2dcee',
       CURRENT_DATE - g,
       (3.5 - g * 0.03)::numeric(5,2),        -- AvgDeliveryDays: trends down
       (0.90 + (40 - g) * 0.002)::numeric(5,4),-- OrderAccuracy: trends up
       NULL,                                   -- QualityScore: always null
       (3.6 + (40 - g) * 0.01)::numeric(3,2),  -- Rating: trends up toward 4.0
       (0.08 - (40 - g) * 0.001)::numeric(5,4),-- CancellationRate: trends down
       (6.0 - g * 0.05)::numeric(6,2),         -- ResponseTimeHours
       10 + g,                                 -- DeliverySampleSize
       4,                                      -- ResponseSampleSize
       NOW()
FROM generate_series(4, 40, 4) AS g;

SELECT "SnapshotDate","AvgDeliveryDays","OrderAccuracy","Rating","CancellationRate","ResponseTimeHours"
FROM supplier_metrics_snapshots
WHERE "SupplierId" = 'b4e21658-13b7-44d2-8924-4fd1aa5105d3'
ORDER BY "SnapshotDate";
