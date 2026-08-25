# TASK-613 — Customer/loyalty domain expansion: schema

**Agent:** database-engineer · **Status:** done · **Date:** 2026-08-24
**Plan:** `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md` §1 (a–e)

## Scope

Database/domain layer only, per plan: entities, `AppDbContext` registration, RLS,
migrations. No service/controller logic, no `mobile/` changes (owned by a separate
concurrent agent).

## What was built

**New entities** (`backend/ShelfGuard.Domain/Entities/`):
- `ConsumerAccountProfileChange` — append-only profile-edit audit trail. **No RLS, no
  `TenantId`** — same precedent as `ConsumerAccount` itself. New constants class
  `ConsumerAccountProfileChangeField` (phone/email/full_name) in the same file, mirroring
  `LoyaltyMembershipStatus`'s in-file pattern.
- `LoyaltyTierDefinition` — per-tenant tier ladder rung (Name, SortOrder,
  MinCompositeScore, AccrualMultiplier, DiscountPercent). Unique index
  `(TenantId, SortOrder)`.
- `LoyaltyTierChangeHistory` — append-only tier-progression audit (mirrors
  `LoyaltyLedgerEntry`'s all-`init` discipline).
- `ConsumerSupportTicket` + `ConsumerSupportTicketMessage` — mirrors
  `SupplierSupportTicket`/`...Message` but consumer↔tenant. New
  `backend/ShelfGuard.Domain/Constants/ConsumerSupportTicketStatus.cs` (mirrors
  `SupplierSupportTicketStatus`'s exact string values).
- `PurchaseReview` — mirrors `SupplierReview`, keyed to `PosTransactionId` (Restrict).
  **Unique index on `PosTransactionId`** — one review per purchase.

**Extended entities:**
- `LoyaltyMembership` — added `CurrentTierId` (Guid?, FK→`LoyaltyTierDefinition`,
  SetNull), `CompositeScore` (decimal, default 0), `TierScoreUpdatedAt` (DateTimeOffset?),
  nav `CurrentTier`. Written only by the future nightly tier-recompute worker job.
- `PosTransaction` — added `CashRegisterId` (Guid?, no FK, intentionally unwired —
  register hardware doesn't exist yet).

**`AppDbContext.cs`:** 6 new `DbSet<T>` properties + `OnModelCreating` config blocks for
all 6 pieces above (entity config + LoyaltyMembership extension), following existing
table/index/FK conventions.

## RLS

| Table | Policies |
|---|---|
| `consumer_account_profile_changes` | **none** (RLS disabled) |
| `loyalty_tier_definitions` | tenant_isolation / provider_bypass / worker_bypass |
| `loyalty_tier_change_history` | + `consumer_self_access` (EXISTS via membership) |
| `consumer_support_tickets` | + `consumer_self_access` (direct column) |
| `consumer_support_ticket_messages` | tenant_isolation via EXISTS-through-ticket + `consumer_self_access` via EXISTS-through-ticket |
| `purchase_reviews` | + `consumer_self_access` (direct column) |

All `provider_bypass` policies use `IN ('provider', 'provider_admin')`; all tables got
`worker_bypass` from creation (past-incident lesson: missing `worker_bypass` silently
breaks worker-job writes).

## Migrations

Generated via `dotnet ef migrations add` (not hand-written) as 5 separate migrations,
matching the task's suggested granularity. Achieved by staging the C# changes behind
temporary `#if TASK613_STAGEn` guards, generating each migration, then removing that
stage's guards before generating the next — final code has zero preprocessor directives:

1. `20260824140303_AddConsumerAccountProfileChanges`
2. `20260824140506_AddLoyaltyTierLadder`
3. `20260824140655_AddConsumerSupportTickets`
4. `20260824140834_AddPurchaseReviews`
5. `20260824140950_AddPosTransactionCashRegisterId`

RLS SQL was hand-added to each generated migration file (EF doesn't generate RLS), copied
verbatim in structure from `AddLoyaltyProgram`/`AddDemandEventStores`. `Down()` methods
explicitly `DROP POLICY IF EXISTS` before disabling RLS and dropping tables.

## Bug caught and fixed during generation

`e.HasOne<ConsumerAccount>().WithMany().HasForeignKey(x => x.ConsumerAccountId)` on an
entity that **also** has a `ConsumerAccount` navigation property makes EF create a second,
phantom relationship (shadow FK `ConsumerAccountId1`) alongside the intended one — first
`migrations add` run surfaced this as a build-time warning + a bogus extra column/FK in
the generated migration. Fixed both occurrences (`ConsumerAccountProfileChange`,
`PurchaseReview`) by using `e.HasOne(x => x.ConsumerAccount).WithMany()` instead, which
binds the FK to the actual nav property. Regenerated; no shadow properties in either
final migration.

## Verification

- `dotnet build`: clean, 0 warnings/errors (whole solution).
- `dotnet test`: **1837/1837 passing** (full suite; 298/298 on Pos-filtered subset alone).
- All 5 migrations applied to dev DB (`crmproductsystems-postgres-1`, port 5435,
  `dotnet ef database update`); `dotnet ef migrations list` confirms none pending.
- RLS verified structurally: `pg_class.relrowsecurity/relforcerowsecurity` — `false/false`
  on `consumer_account_profile_changes`, `true/true` on the other 5; `pg_policies` lists
  exactly the expected policy set per table (see table above).
- RLS verified functionally (as non-superuser `shelfguard_app_dev`, inside rolled-back
  transactions, no residual test data): `loyalty_tier_definitions` tenant isolation (wrong
  tenant → 0 rows, owning tenant → 1 row) and `worker_bypass` (worker role sees the row
  with no `app.tenant_id` set); `purchase_reviews` `consumer_self_access` (wrong
  `app.consumer_account_id` → 0 rows, owning consumer → 1 row).

## Not implemented (out of scope, per plan §5 sequencing)

`Features/ConsumerProfile`, `Features/Loyalty` tier-ladder CRUD/consumer endpoints,
`PosService.cs` accrual-multiplier/discount integration, `Features/CustomerSupport`,
`Features/Reviews`, the nightly tier-recompute worker job, frontend pages. Handoff written
to `.claude/logs/handoffs/613-to-backend_database-engineer.md`.
