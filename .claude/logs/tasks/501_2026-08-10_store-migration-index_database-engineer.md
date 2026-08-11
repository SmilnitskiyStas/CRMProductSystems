# TASK-479 (logged as 501 — see ID-collision note below): `pos_transactions` customer-migration index

**Agent:** database-engineer
**Date:** 2026-08-10
**Status:** done

## ID-collision note (read first)

The brief for this work (and the plan doc `C:\Users\stass\.claude\plans\flickering-moseying-fountain.md`)
assigns this task **TASK-479**, with the RFM store-migration feature's follow-on work as
TASK-480..483. Those IDs are already taken — completed 2026-08-07 by an unrelated feature
batch (interactive analytics + margin initiative, plan `iterative-purring-sifakis.md`):

- `.claude/logs/tasks/479_2026-08-07_pos-product-index_database-engineer.md`
- `.claude/logs/tasks/480_2026-08-07_margin-authorization_backend-developer.md`
- `.claude/logs/tasks/481_2026-08-07_category-losses-drilldown_backend-developer.md`
- `.claude/logs/tasks/482_2026-08-07_product-sales-trend_backend-developer.md`

Current actual max task ID in the repo is **TASK-500** (`500_2026-08-09_loyalty-customer-code-format-web_frontend-developer.md`).
Writing this log to the requested `479_...` path would have overwritten a legitimate, already-done
task's history. I logged this one as **TASK-501** instead and did not touch the pre-existing 479-482
files. The orchestrator should renumber the remaining store-migration plan steps (backend-developer,
frontend-developer, qa-tester, documentation-writer) to 501+ before spawning them, to avoid the same
collision downstream.

## Context

Plan: `C:\Users\stass\.claude\plans\flickering-moseying-fountain.md` §Database. New RFM-dashboard
"store migration" feature needs, per customer, first/last `pos_transactions` row (by `CreatedAt`)
within a `TenantId` + date-range window, via
`DISTINCT ON (t."CustomerId") ... WHERE t."TenantId" = ? AND t."CreatedAt" BETWEEN ? AND ?
AND t."CustomerId" IS NOT NULL AND t."Status" <> 'fiscalization_failed'
ORDER BY t."CustomerId", t."CreatedAt" [ASC/DESC]`.

## Decision: new composite index added

Confirmed the only two pre-existing relevant indexes (`AppDbContext.cs`):
- `(TenantId, StoreId, CreatedAt)` and its partial DESC variant `idx_pos_transactions_excl_failed`
  (`WHERE "Status" <> 'fiscalization_failed'`) — both keyed by `StoreId`, not `CustomerId`.
- `idx_pos_tx_customer` — `CustomerId` alone, partial `WHERE "CustomerId" IS NOT NULL`.

Neither can serve the migration query's access pattern. B-tree leftmost-prefix rule: in both
existing composite indexes, `StoreId` sits between `TenantId` and `CreatedAt` with no equality
constraint on it (the migration query intentionally does *not* filter by store in the per-customer
subqueries — the store filter is applied post-hoc to the from/to result per the plan's "either
store" semantics), so `CreatedAt` can't be used as an index condition — it degrades to a filter
applied after a heap fetch for every one of the tenant's historical rows, not just the ones in the
selected date window. `idx_pos_tx_customer` has no `TenantId` prefix at all, so it can't narrow to
one tenant either. Net effect without a new index: a tenant with a long transaction history would
force a scan of its *entire* POS history (not date-bounded) plus an in-memory sort, every time this
dashboard section loads — a structural gap, not a marginal one, regardless of exact row counts.

Added:
```csharp
e.HasIndex(t => new { t.TenantId, t.CustomerId, t.CreatedAt })
 .HasDatabaseName("idx_pos_tx_customer_migration")
 .HasFilter("\"CustomerId\" IS NOT NULL AND \"Status\" <> 'fiscalization_failed'")
 .IncludeProperties(t => new { t.StoreId });
```
(`AppDbContext.cs`, in the existing `PosTransaction` "customer FK index" block, right after
`idx_pos_tx_customer`.)

Reasoning for this exact shape:
- `(TenantId, CustomerId, CreatedAt)` ascending: for a fixed `TenantId`, index order already
  matches `ORDER BY CustomerId, CreatedAt` — the "first transaction" subquery gets a pure ordered
  Index Scan with **no sort node**. The "last transaction" (`... CreatedAt DESC`) subquery can't
  use the same ascending index for a full order match (mixed-direction requirement), but still
  benefits: Postgres's Incremental Sort (PG13+) exploits the `CustomerId` pre-ordering the index
  already provides, sorting only within each customer's small `CreatedAt` group instead of the
  whole tenant-period result set. Considered a second, DESC-on-CreatedAt index (mirroring how
  `idx_pos_transactions_excl_failed` uses `.IsDescending(false, false, true)`) to make *both*
  subqueries fully sort-free, but rejected it — doubles write-amplification on a table written on
  every POS sale, for a gain (skipping Incremental Sort on an already-tiny per-customer group) that
  doesn't justify the cost on what's an occasional analytics-dashboard query, not a hot path.
- Partial predicate `"CustomerId" IS NOT NULL AND "Status" <> 'fiscalization_failed'` — both
  conditions are already individually precedented on this table (`idx_pos_tx_customer`'s
  `CustomerId IS NOT NULL`, `idx_pos_transactions_excl_failed`'s `Status` filter); combining them
  means the query's exact `WHERE` conditions are satisfied by the index predicate itself, no heap
  fetch needed to evaluate either. `CreatedAt BETWEEN` stays a real (non-partial) key column, not
  baked into the filter, since partial-index predicates must be static and can't depend on query
  parameters.
- `INCLUDE ("StoreId")` (column `LocationId`) — the query needs the from/to store per customer;
  covering it avoids a heap fetch purely to read that one column once the other predicates are
  already satisfied by the index/partial-filter.

**Kept the old `idx_pos_tx_customer` — did not drop it.** This differs from TASK-479's
(2026-08-07) precedent, which dropped a redundant single-column FK index because the new composite
index kept that same column as its *leading* key. Here `CustomerId` is the **2nd** key (TenantId
leads), so it can't serve an efficient `CustomerId`-only lookup. That lookup still matters:
`PosTransaction.CustomerId → Customer` is `OnDelete(SetNull)` (`AppDbContext.cs` ~line 1556); a
customer delete issues an update filtered by `CustomerId` alone (no tenant scoping in that FK
action). Dropping the plain index would regress that from an index lookup to a full table scan on
a high-write table. Both indexes stay.

## Build/test

- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, same one noted in TASK-479's 2026-08-07 log).
- Migration `AddPosTxCustomerMigrationIndex` (`20260810181059`) generated via
  `dotnet ef migrations add --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api`
  (not hand-written). `Up()` creates `idx_pos_tx_customer_migration` on
  `("TenantId", "CustomerId", "CreatedAt")` with the partial filter and
  `Npgsql:IndexInclude = ["LocationId"]`; `Down()` drops it. Model snapshot diff confirmed scoped
  to exactly this one addition, no unrelated entity changes.
- Not live-applied to a local dev DB this round (no local Postgres instance touched in this
  session) — plain `dotnet ef database update` will apply it whenever the next agent/dev runs
  migrations; no manual step needed beyond the normal flow.
- No test suite run in full (`dotnet test`) — out of scope for an index-only change with no
  query/service code yet to exercise it (that lands in TASK-480/501+). Build is the correctness
  gate here.

## Not in scope (per brief)

No DTOs/repository/service/controller code touched — that's the next task (backend-developer).

## Files

- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260810181059_AddPosTxCustomerMigrationIndex.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260810181059_AddPosTxCustomerMigrationIndex.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated, `PosTransaction` index metadata only)
