# TASK-428: Item.Name trigram index (Фаза 3 AudienceBuilder prep)

**Agent:** database-engineer
**Date:** 2026-07-27
**Status:** done — index created, migrated, build/tests clean · **CRITICAL finding: the new
index cannot be used by the planner on the real (RLS-protected) connection — needs a decision
before backend-developer relies on it for AudienceBuilder performance.**

## Context

Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`. Design doc:
`phase3-audience-builder-design.md` §2.1/§11 (scratchpad) — Фаза 3 AudienceBuilder substring
search on `Item.Name`. Task #1 of Фаза 3's agent sequence (schema-only slice), same shape as
TASK-419/404: narrow index task, no new tables.

## Done

- `AppDbContext.cs` (Item entity block) — new GIN trigram index, same fluent pattern as
  `idx_notification_queue_title_trgm` (`ExtendNotificationQueueFiltering`):
  ```csharp
  e.HasIndex(p => p.Name)
   .HasDatabaseName("idx_items_name_trgm")
   .HasMethod("gin")
   .HasOperators("gin_trgm_ops");
  ```
  `pg_trgm` untouched (already enabled since that same migration) — brief's instruction followed
  exactly, no `CREATE EXTENSION` in the new migration.
- Migration `AddItemNameTrigramIndex` (`20260727175924`, next timestamp after
  `20260726211248_AddPriceSegmentSettings`) — single `CreateIndex` with
  `Npgsql:IndexMethod=gin` / `Npgsql:IndexOperators=[gin_trgm_ops]` annotations, `Down()` drops it.
  Generated via `dotnet ef migrations add` (not hand-written) so Designer.cs/ModelSnapshot.cs stay
  in sync — snapshot diff is exactly the expected 6 lines, mirroring the Title-trgm precedent
  byte-for-byte.
- Applied to dev DB via the app's own non-superuser connection (`shelfguard_app_dev`, same
  TASK-419 discipline — not the `crm` superuser escape hatch). `dotnet ef database update` needed
  `ConnectionStrings__DefaultConnection` set explicitly in the shell first —
  `AppDbContextFactory.cs` (EF design-time factory) falls back to a hardcoded
  `postgres/postgres@5432/shelfguard_dev` placeholder when that env var is absent, which doesn't
  match this repo's real dev DB (`crmproductsystems-postgres-1`, port 5435, db `crm`); this is
  pre-existing design-time-factory behavior, not something this task changed.
- Confirmed live: `\d items` / `pg_indexes` show `idx_items_name_trgm — gin ("Name" gin_trgm_ops)`,
  correctly built.

## CRITICAL finding — the index is provably unusable on the real query path

`items` has the canonical RLS triad + `FORCE ROW LEVEL SECURITY`. Dev's `items` table is empty
(0 rows), so verifying "does the planner actually pick this index" required seeding synthetic
data — did so inside a single transaction, always `ROLLBACK`ed at the end (dev DB confirmed back
to 0 rows in every table touched, no residue).

Seeded 500,000 items (1 tenant, ~100 rows containing a rare marker substring, rest random
`md5()` noise) inside `BEGIN; SET LOCAL app.tenant_id=...; ...; ANALYZE; EXPLAIN ANALYZE; ROLLBACK;`
via the real app role:

```
Seq Scan on items  (cost=... ) (actual time=202.827..1085.667 rows=100 loops=1)
  Filter: ((current_setting('app.role')='worker') OR (TenantId = ...) OR (current_setting('app.role') = ANY('{provider,provider_admin}'))
           AND (("Name")::text ~~* '%ZzqRareTrgm%'))
  Rows Removed by Filter: 499922
Execution Time: 1085–1523 ms  (also tried with enable_seqscan=off — planner still had no
  alternative plan at all, confirming no index path is considered, not just deprioritized)
```

Re-ran the **identical** query as the `crm` superuser (real Postgres superuser, `rolbypassrls=t`,
so RLS never applies):

```
Bitmap Heap Scan on items  (cost=701.50..891.62 rows=50 width=55) (actual time=1.642..1.910 rows=100)
  ->  Bitmap Index Scan on idx_items_name_trgm  (actual time=1.611..1.611 rows=100)
        Index Cond: (("Name")::text ~~* '%ZzqRareTrgm%')
Execution Time: 2.0 ms
```

Same index, same query, same data — **1085ms Seq Scan vs 2ms Bitmap Index Scan.** Root cause is
a documented, general PostgreSQL RLS rule, not a bug in this migration's DDL: under RLS with
`FORCE ROW LEVEL SECURITY`, any qual using a non-`LEAKPROOF` function/operator can only be applied
as a post-scan `Filter`, never pushed down into an index condition — this holds even for the table
owner once `FORCE` is set. Confirmed directly against `pg_proc`:
`texticlike` (backs `~~*`/ILIKE) → `proleakproof = f`. `enable_seqscan=off` still produced only
Seq Scan (cost artificially set to 1e10 and it was *still* chosen) — proof no index-based plan
exists at all for this predicate under RLS, not merely a cost-losing one.

**This is not new to this task — it already silently affects the shipped
`idx_notification_queue_title_trgm`.** Same live test against `notification_queue` (which the
design doc cited as the working precedent, `NotificationRepository.cs:69`) under the real
RLS-scoped connection:
```
Seq Scan on notification_queue  (cost=0.00..16.60 rows=1 width=16)
  Filter: (... OR "TenantId" = ... AND ("Title")::text ~~* '%test%')
```
Also a Filter, not an Index Cond — that index has (as far as this session can tell) never
actually accelerated a real tenant-scoped keyword search in production either. **Flagged as a
separate background task** (not fixed here, out of this task's scope) — see below.

### Why this matters for the very next task (backend-developer, AudienceBuilder)

Every real AudienceBuilder query runs through the tenant-scoped app connection — the exact path
just shown to never use this index. Whatever `ILIKE '%term%'` query the next task writes against
`items.Name` will silently full-scan `items` (all tenants combined, since the scan can't stop at
tenant boundaries) regardless of this index's existence, exactly like the notification_queue case.

**This is a genuine cross-cutting security/architecture tradeoff, not an isolated indexing
decision — not resolving it unilaterally here**, per CLAUDE.md's clarify-before-implementing gate
(marking a core Postgres function LEAKPROOF is a schema-wide security posture change, arguable
either way on the timing-side-channel question). Options for whoever picks this up next
(backend-developer/project-architect/security-reviewer):
1. Mark `texticlike`/related pattern-matching support functions `LEAKPROOF` after a dedicated
   security review of the side-channel tradeoff (global change, affects every RLS table using
   LIKE/ILIKE, not just this one).
2. A `SECURITY DEFINER` search function (owned by a privileged role) that bypasses RLS internally
   but re-applies a hardcoded, provably-safe `TenantId = current_setting(...)` guard — narrower
   blast radius than (1), same spirit as the existing `provider_bypass`/`worker_bypass` policies.
3. Accept the Seq Scan at realistic single-tenant catalog sizes (thousands of SKUs, not the
   500k-row/all-tenants synthetic worst case tested here) and treat this index as inert today,
   useful only if (1) or (2) is ever adopted later.
No code changed toward any of these — flagging only, per the brief's own instruction to keep this
task narrow (index only).

## Composite `(TenantId, CustomerId, CreatedAt)` on `pos_transactions` — assessed, not added

Seeded a realistic scenario (rolled back afterward, dev DB confirmed clean): 3,000 customers,
5 items, 100,000 `pos_transactions` over a 180-day spread (~30% with a real `CustomerId`,
matching the plan's post-loyalty-launch reality), ~100,000 `pos_transaction_items`.

1. **The design doc's actual §3/§5/§8 CTE shape** (`pos_transaction_items` → matched anchor
   items → join to `pos_transactions` **by primary key**) — `EXPLAIN ANALYZE` confirms
   `pos_transactions` is reached exclusively via `Index Scan using "PK_pos_transactions" ... Index
   Cond: ("Id" = ti."TransactionId")`, with Tenant/Customer/Status/Date applied as a cheap
   post-lookup `Filter` on an already-tiny per-line-item candidate set. A composite index on
   `(TenantId, CustomerId, CreatedAt)` cannot help this access pattern — `pos_transactions` is
   never independently scanned by those columns in any of the plan's own query shapes.
2. **A hypothetical alternate "bare filter" shape** (`SELECT ... FROM pos_transactions WHERE
   TenantId=X AND CustomerId IS NOT NULL AND CreatedAt BETWEEN ...`, in case a future endpoint
   needs it) — measured **before and after** creating the composite index in the same
   transaction: cost/time essentially unchanged (68.3ms → 66.2ms, noise-level), and the planner
   kept using the existing `idx_pos_tx_customer` bitmap scan in both cases — did not even switch
   to the new composite. At ~30% customer-attach selectivity, the existing partial index already
   does the job.

**Conclusion: no new index added.** Both realistic access patterns show zero measurable benefit
from `(TenantId, CustomerId, CreatedAt)` — exactly the "premature optimization without data"
outcome the design doc itself warns against. `idx_pos_tx_customer` +
`idx_pos_transactions_excl_failed` (both pre-existing) remain sufficient.

**Side note for backend-developer, not independently re-verified live (confounded my own attempt
at reproducing it, see below), but grounded in a direct `pg_proc` check:** cross-type
`timestamptz`-vs-`date` comparison functions (`timestamptz_eq_date`, `timestamptz_lt_date`, etc. —
what Npgsql would invoke if a C# `DateOnly` parameter is compared against `CreatedAt` without an
explicit cast) are **also non-leakproof** (`proleakproof=f`), unlike the plain `timestamptz`-vs-
`timestamptz` comparison functions (`timestamptz_eq`/`_lt`/etc., all leakproof). Recommend the
`AudienceBuilderRepository` bind `From`/`To` date-range parameters as `timestamptz` explicitly
(e.g. `{n}::timestamptz`) rather than leaving a bare `DateOnly` parameter to compare against
`CreatedAt`, to avoid quietly re-triggering the same RLS/leakproof index-blocking class of issue
this task already found once. (My own attempt to reproduce this live was confounded — the seeded
test data was single-tenant, making `TenantId` ~100% selective regardless of the date cast, so a
Seq Scan was correctly chosen either way; didn't re-run with a second decoy tenant to properly
isolate it given this was the optional half of an already-thorough side task.)

## Build/test

- `dotnet build ShelfGuard.sln` — 0 err (1 pre-existing unrelated warning,
  `MarketplaceServiceTests.cs`, same one every recent task log reports).
- `dotnet test ShelfGuard.sln` — **1186/1186 green**, no regressions. No new test file added
  (index-only change, same precedent as TASK-419's `price_segment_settings` — no new RLS shape
  here since `items`' RLS triad is unchanged, only a new secondary index).

## Follow-up flagged (spawned as a background task, not fixed here)

`idx_notification_queue_title_trgm` (shipped, `ExtendNotificationQueueFiltering`) has the identical
RLS+non-leakproof-ILIKE problem confirmed live above — the notifications keyword-search feature
has likely never actually benefited from its GIN index in production. Flagged via `spawn_task` for
a dedicated look (same fix menu as this task's finding above).

## Not in scope (per brief)

- No new tables.
- `.claude/docs/database-schema.md` not updated (documentation-writer's pass, same precedent as
  TASK-419/404).
- Did not implement any of the three RLS/leakproof remediation options — flagged for a decision,
  per CLAUDE.md's clarify-before-implementing gate.

## Git

Not committed (repo convention — main session/user commits).

## Files

- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (Item entity — new index config)
- `backend/ShelfGuard.Infrastructure/Migrations/20260727175924_AddItemNameTrigramIndex.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260727175924_AddItemNameTrigramIndex.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated,
  `Item.Name` index metadata only)
