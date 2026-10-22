# TASK-750 - Planogram backend: floor-plan upload, zone types, shelf insights, scan enrichment

Plan: `~/.claude/plans/rustling-wiggling-pie.md` sections 1-4. No migrations.

Changed (backend/):
- `ShelfService`: `SaveFloorPlanBackgroundAsync` (png/jpg/webp <=8MB, magic bytes, guid filename, `wwwroot/uploads/floor-plans/{tenant}`); scan enrichment via new `ShelfInsightService` + `ShelfRecommendationRules` (deterministic); `ShelfRestockAdvisor` (Infrastructure/AI, Analyst slot, text only) for the summary - failures never fail the scan; vision failure now persists a `failed` scan (+ErrorMessage) and still returns 502.
- `ShelfInsightService`: stock/sales/ADU via 3 bulk queries added to `IShelfRepository` (`GetStockAggregatesAsync`, `GetSalesAggregatesAsync`, `GetAduAsync`).
- `LocationService`: zone types += checkout, entrance_exit, department, storage, other.
- `LocationsController`: `POST {id}/floor-plan/background`, `GET .../shelves/{shelfId}/insights`; `POST /scans` does an explicit role check (403 with message).
- Tests: `ShelfInsightTests` (bulk insights, rules, scan enrichment, restock/insight failure, upload validation); `ShelfServiceTests` updated (502 now persists failed scan).

Decisions: warehouse = `LocationType` `warehouse` or `central_warehouse`; today's POS lines not included (daily_sales only); DaysOfCover = store qty / ADU (ADU falls back to sold30d/30); present items get `recommendation.action = none`.
Build: `dotnet build` OK; `dotnet test` 2762 passed. Not committed.
