# TASK-662 (T15) — docs for the supplier delivery-coverage / performance-metrics feature

**Agent:** documentation-writer · **Date:** 2026-08-31 · **Status:** done
**Plan:** `eventual-whistling-rabbit.md`, T15 of T1–T16. Depends on TASK-648..661 (all merged to `main`).
**Scope:** docs only — `.claude/docs/*` + this log + `current.md`. No code touched.

## What changed

### `.claude/docs/decisions.md` — new **ADR-036**
"Supplier delivery coverage + performance metrics" — 4 decisions in the file's Decision/Context/
Consequences shape:
1. Region taxonomy is the app-side `UkraineRegions` domain constant (mirrors `SupplierItemCategories`),
   served via `GET /api/geo/regions`, **not** a DB reference table. ISO 3166-2:UA codes; `UA-30`
   (м. Київ) ≠ `UA-32` (Київська обл.) called out; `UA-40`/`UA-43` included with neutral labels.
2. `MarketplaceOrder.DestinationRegionCode` is a point-in-time snapshot, not a live join through
   `DestinationStoreId` — same rationale as ADR-033. Consequence: historical orders have null
   snapshot → overall avg only, per-region starts at n=0.
3. Delivery coverage is **not** premium-gated — deliberate deviation from the premium `SupplierProfileDto`
   pattern; website/hours/payment stay premium.
4. `supplier-metrics-recompute` worker write-boundary — fixed disjoint column set, never
   `Rating`/`QualityScore`; `supplier_metrics` has no `xmin`, safety rests on disjoint columns +
   separate UPDATEs. Mirrors ADR-034 Decision 4.
Header `**Updated:**` bumped to 2026-08-31.

### `.claude/docs/domain-model.md`
New Core-Entities entries before "Key Business Rules": **Ukraine Region Registry** (domain constant,
non-DB, alongside the Block Registry precedent); **SupplierProfile — delivery coverage**
(`DeliveryCoverage` jsonb shape, supersedes `[Obsolete]` `DeliveryRegions`, not premium-gated);
**SupplierMetrics — now actually populated** (4 new columns, worker job, only `Rating` synchronous,
no `xmin`); **Location.RegionCode / MarketplaceOrder.DestinationRegionCode** (snapshot). Header date bumped.

### `.claude/docs/database-schema.md`
New **TASK-649 — Supplier performance data (`AddSupplierPerformanceData`)** section: 4 column groups
table, the 2 indexes (composite `supplier_chat_messages`, partial `ix_marketplace_orders_metrics`),
explicit "no new tables / no RLS change — columns inherit the triad" note, `varchar(20)` deviation,
worker write-boundary restatement. Header date bumped.

### `.claude/docs/api-contracts.md`
New section in the marketplace area: `GET /api/geo/regions` → `RegionDto[]`;
`GET /api/marketplace/suppliers/{id}/coverage` → `SupplierCoverageForBuyerDto` (full JSON,
`buyerRegionStatus` enum); `region`→`regionCode` rename + coverage-match semantics on
`GET /api/marketplace/suppliers` + `POST /api/marketplace/search`; `SupplierProfileDto.deliveryCoverage`
(not premium-gated); `SupplierMetricsDto` +4 fields; `SupplierProfileUpdateDto`/`CabinetProfileUpdateDto`
`deliveryCoverage` patch (`deliveryRegions` ignored); contract-PDF §5/§6 renumber. Header date bumped.

### `.claude/docs/known-issues.md`
- **KI-038** — supplier delivery/response metrics measurement limitations (per-region sparsity,
  receipt-finalization bias, answered-sessions-only response median, no response-rate, null
  `qualityScore`). Cross-references existing KI-037, does not duplicate it.
- **KI-039** — `DeliveryRegions` → `DeliveryCoverage` backfill low match rate; unmapped free text → `note`.
- **KI-040** — `backend/openapi.json` not regenerated for the new endpoints/DTOs (pending chore;
  could not regenerate — docs-only task, no live API).
Header date bumped.

## Notes

- `openapi.json` NOT regenerated — out of a docs-only task's scope (needs a live API + dev Postgres
  run per `backend-structure.md`), recorded as KI-040 instead.
- `git add` limited to `.claude/docs/*` + this log + `current.md`. `mobile/features/pos/receiptPrinting.ts`
  (another session's dirty file) not staged.
