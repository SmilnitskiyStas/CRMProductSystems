# TASK-369 — DB: Block 16 pre-launch audit (part 2) — RLS tenant-index sweep, EF/FK re-check, N+1 sweep

**Status:** done (2026-07-15) · **Agent:** database-engineer (main session, direct — no sub-agent
per explicit instruction) · **Continues:** an earlier same-day attempt that got only as far as
`AddActivityLogsIndexesAndDropSupersededStockIndexes` (activity_logs indexes + dropped 2
superseded product_stock indexes) before running out of session budget — that part was already
verified (build/tests green) and is not repeated here.

## 1. Systemic audit: FORCE RLS tables without a tenant-leading index

Queried `pg_class`/`pg_index`/`pg_policy` directly (not just `pg_indexes` text-parsing, to avoid
misreads) across all **76** `FORCE ROW LEVEL SECURITY` tables (up from 74 at Block 2 — two chat/
support-message RLS tables added 2026-07-15). For each: does it carry its own tenant column, does
its RLS policy reference that column directly vs. via an `EXISTS` join to a parent, and is there
an index leading on the column the policy actually filters by.

Cross-referenced against the real repository query methods (not just schema) before flagging
anything — a table can have zero tenant-leading index and still be perfectly fine if every live
query already filters on something else that's globally unique per tenant (a Guid FK to a
one-tenant-only parent, e.g. `StoreId`/`WorkOrderId`/`DiscountId`/`TicketId`/`OrderId` — Postgres
uses that index first, RLS's extra `TenantId` filter then only applies to an already-tiny
rowset). Full breakdown now lives in `.claude/docs/database-schema.md` under "Block 16".

**Real gaps found and fixed** (migration `AddChatSessionsAndSupplySchedulesTenantIndexes`,
applied to dev DB, EF-tracked via fluent `HasIndex` in `AppDbContext.cs` — not raw SQL):
- `chat_sessions` — zero index besides PK; `ChatService.GetSessionsAsync` (tenant chat inbox)
  queries `WHERE TenantId == tenantId ORDER BY UpdatedAt DESC` directly. Added
  `idx_chat_sessions_tenant_updated (TenantId, UpdatedAt DESC)`. Live chat is an actively growing
  feature — this was a present-day full scan on every inbox page load, not a future risk.
- `supply_schedules` — `SupplyScheduleRepository.GetAsync(storeId?, supplierId?)` has both filters
  optional; the Settings page's unfiltered list has nothing but RLS to narrow rows. Added
  `idx_supply_schedules_tenant (TenantId)`.

**Checked and confirmed NOT a practical gap** (traced the actual query methods, not just the
schema) — `product_adu`/`product_buffer` (always `StoreId`-scoped via existing
`(StoreId,ProductId)` unique index), `promo_cannibalization` (`DiscountId`/`Discount.StoreId`-
scoped), `product_supplier_settings` (`ProductId`-scoped), `as_work_order_lines` (always via
`.Include(w => w.Lines)` off `WorkOrderId`), `ticket_comments` (`.Include(t => t.Comments)` off
`TicketId`, or `AuthorId` — already indexed), `marketplace_order_items` (`.Include(o => o.Items)`
off `OrderId`), `supplier_chat_messages`/`supplier_support_ticket_messages` (RLS is an `EXISTS`
join on `SessionId`/`TicketId`, never touches the `SenderTenantId` column despite the name
looking like a gap). `stock_events` is write-only today (`AddStockEventAsync` is the only call
site anywhere in the codebase, zero read/list queries exist) — flagged as preventive-only, not
fixed now; would need `(TenantId, CreatedAt DESC)` if a read feature ships later. Deliberately did
**not** blindly add a `TenantId` index to all 10 originally-suspected tables — 8 of them would
have been pure write overhead with zero read benefit, which is the same over-indexing failure
mode Block 15 already flagged for `notification_queue`.

Every other FORCE RLS table with its own tenant column already had a leading index from earlier
per-block audits (TASK-352/353/354/356 etc.).

## 2. EF FK/index-tracking re-check

Re-verified the `StockMovement`/`Discount` finding from TASK-352 is still accurate — and found it
had drifted slightly since: `StockMovement`'s fluent config in `AppDbContext.cs` still has zero
`HasOne`/`HasForeignKey` calls (all 3 of its DB-level FKs — `FromLocationId`/`ToLocationId`/
`ProductId` — remain raw-SQL-only, invisible to EF), but `Discount` now has proper fluent FK
config for `TenantId`/`CreatedBy`/`ApprovedBy` (added since TASK-352's pass) — only its
`ProductId`/`StoreId`/`ProductStockId` FKs are still raw-SQL-only. Updated the stale blanket
claim in `database-schema.md`. Risk assessment unchanged from TASK-352: **low today** (EF only
diffs what it's told, won't touch or duplicate these on a future `migrations add`); the real
exposure is only if a future dev adds a fluent nav property for one of these columns without
checking the DB first (redundant, differently-named FK — not fatal).

Grepped all 47 migrations using `migrationBuilder.Sql(...)` for FK/index statements specifically
(as opposed to RLS policy SQL, which is expected/documented separately — RLS has no EF fluent
equivalent at all). Found: `FullSchema` (2026-06-04, the two now-superseded `product_stock`
indexes cleaned up in this morning's first part of Block 16), `FixRlsAndForeignKeys` (2026-06-04,
the `StockMovement`/`Discount`/`write_offs` raw FKs + `stock_movements` indexes above),
`AddProviderRoles` (2026-06-21, a defensive idempotent raw FK for `users.ProviderRoleId` — this
one is now **also** fluent-tracked in `AppDbContext.cs`, so no drift risk there, just a
historical artifact of how it was first added), `AddNotificationIsRead` (2026-06-22, one raw
`CREATE INDEX IF NOT EXISTS` on `notification_queue` — benign, EF has no opinion on it either
way). No new/previously-undocumented cases found.

## 3. N+1 sweep — Analytics / Catalog / Events / Notifications

Grepped for `foreach`/`for` loops containing repository or DbContext calls in
`Features/Analytics`, `Features/Catalog`, `Features/Events`, `Features/Notifications` (services)
plus their backing repositories (`AnalyticsRepository`, `EventRepository`, `NotificationRepository`,
`ItemRepository`). **Clean — no N+1 found:**
- `AnalyticsService.cs`/`AnalyticsRepository.cs` (531 lines): zero loops, pure LINQ aggregate
  queries.
- `NotificationService.cs`: zero loops, every method a thin single-repo-call passthrough.
  `NotificationRepository.MarkAllAsReadAsync`'s `foreach (var item in items) item.MarkRead()` is
  an in-memory mutation on an already-`ToListAsync()`-loaded set, followed by one
  `SaveChangesAsync()` — EF batches the updates in one round trip, not N+1.
- `ItemService.cs`'s only loop (line 254) enumerates an in-memory `JsonElement` array (country
  lookup from a JSON blob) — no DB call inside it.
- `EventService.SeedDefaultsAsync`'s nested `foreach` (seeding default holidays + per-category
  coefficients) calls `_repo.AddAsync`/`AddCoefficientAsync` per iteration — verified these are
  plain `DbSet.AddAsync(entity)` (change-tracker staging only, no round trip); the single
  `SaveChangesAsync()` at the end of the method batches everything into one transaction. Also a
  one-time admin seed action, not a hot path even if it weren't batched.

## Build / test status

`dotnet build ShelfGuard.sln`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`
possible-null-deref). `dotnet ef migrations add AddChatSessionsAndSupplySchedulesTenantIndexes`
generated exactly the 2 intended `CreateIndex` calls, nothing else — confirms the fluent
`HasIndex` additions didn't accidentally diff any unrelated model drift.
`dotnet ef database update` failed to connect (`password authentication failed for user
"postgres"` — same environment quirk noted in TASK-352, EF design-time tooling resolves a
different connection string than `appsettings.Development.json`'s `crm`/`crm_dev_password`;
not investigated further, out of scope). Applied the migration's SQL directly to the dev DB via
`docker exec ... psql` and inserted the matching `__EFMigrationsHistory` row by hand (hand-verified
both indexes exist afterward). `dotnet test ShelfGuard.Tests`: **850/850 green**.

## Docs updated

`.claude/docs/database-schema.md`: corrected the `StockMovement`/`Discount` FK-tracking note
(Discount is now partially fluent-tracked), added a new "Block 16" section documenting the full
RLS tenant-index audit method, what was fixed, and what was checked-and-fine with rationale.

## Needs a decision

Nothing blocking. One low-priority, non-blocking item for a future session if/when it becomes
relevant: if a "stock event history" read UI ever ships, add a `(TenantId, CreatedAt DESC)` index
to `stock_events` at that time (not needed today — zero read call sites exist).
