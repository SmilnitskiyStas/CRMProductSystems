# Current Sprint

## TASK-002 — Full DB Schema ✅ done (2026-06-04)
Log: `.claude/logs/tasks/002_2026-06-04_full-db-schema_database-engineer.md`

## TASK-010 — Web dashboard ✅ done (2026-06-03)
Log: `.claude/logs/tasks/010_2026-06-03_web-dashboard_frontend-developer.md`

---

## Next candidates

- **TASK-011** — `/api/stock` backend endpoint + `/stock` frontend page
  - Requires: product_stock table ✅, catalog_products ✅
  - Blocks: real dashboard stats (Safe/Warning/Critical/Expired from actual batches)

- **TASK-012** — Extend DbSeeder with store, zones, catalog_products, stock batches
  - Makes dashboard show real FEFO data instead of POC products proxy

- **TASK-003b** — Migrate catalog API from POC `Products` → `catalog_products`
  - Low priority until stock API is built
