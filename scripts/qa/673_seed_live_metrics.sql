-- TASK-673 QA seed: rich live supplier_metrics row for public supplier
-- b4e21658-13b7-44d2-8924-4fd1aa5105d3 so the metrics detail page renders a
-- current value in every section (QualityScore left NULL on purpose — exercises
-- the "—" / chart-empty state). DeliveryByRegion carries one declared region
-- (UA-18, also in DeliveryCoverage.served) + two undeclared measured regions
-- (UA-30, UA-32) so DeliveryRegionComparison shows both "declared" and "—".
-- Run as crm superuser (RLS bypassed — seed only):
--   docker exec -i crmproductsystems-postgres-1 psql -U crm -d crm -f - < this file

UPDATE supplier_metrics SET
  "AvgDeliveryDays"      = 2.60,
  "DeliverySampleSize"   = 48,
  "DeliveryByRegion"     = '[{"regionCode":"UA-18","avgDeliveryDays":1.80,"sampleSize":25},
                             {"regionCode":"UA-30","avgDeliveryDays":2.40,"sampleSize":15},
                             {"regionCode":"UA-32","avgDeliveryDays":3.10,"sampleSize":8}]'::jsonb,
  "ResponseTimeHours"    = 5.80,
  "ResponseSampleSize"   = 12,
  "CancellationRate"     = 0.0440,
  "OrderAccuracy"        = 0.9720,
  "QualityScore"         = NULL,
  "Rating"               = 4.00,
  "AggregatesComputedAt" = NOW()
WHERE "SupplierId" = 'b4e21658-13b7-44d2-8924-4fd1aa5105d3';

SELECT "AvgDeliveryDays","DeliverySampleSize","ResponseTimeHours","ResponseSampleSize",
       "CancellationRate","OrderAccuracy","QualityScore","Rating"
FROM supplier_metrics WHERE "SupplierId" = 'b4e21658-13b7-44d2-8924-4fd1aa5105d3';
