# Handoff: TASK-613 database-engineer → backend-developer

Plan: `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md` (read in full — §2 is this
wave's brief). Schema-only task log:
`.claude/logs/tasks/613_2026-08-24_crm-loyalty-tier-schema_database-engineer.md`.

## What's ready

All 5 migrations applied to dev DB. New entities, all in
`backend/ShelfGuard.Domain/Entities/` unless noted:

- `ConsumerAccountProfileChange` (table `consumer_account_profile_changes`, **no RLS**) —
  `Id`, `ConsumerAccountId`, `FieldName` (see `ConsumerAccountProfileChangeField` consts
  in the same file), `OldValue`, `NewValue`, `ChangedAt`. All `init`-only.
- `LoyaltyTierDefinition` (table `loyalty_tier_definitions`) — `Id`, `TenantId`, `Name`,
  `SortOrder`, `MinCompositeScore`, `AccrualMultiplier` (default 1.0m), `DiscountPercent`
  (default 0), `CreatedAt`, `UpdatedAt`. Unique `(TenantId, SortOrder)`.
- `LoyaltyMembership` gained `CurrentTierId` (Guid?, nav `CurrentTier`), `CompositeScore`
  (decimal, default 0), `TierScoreUpdatedAt` (DateTimeOffset?). **Nothing writes these yet
  except the (not-yet-built) nightly recompute job** — don't let request-time code touch
  them, per the plan's explicit reasoning (avoids conflicting with the `xmin` concurrency
  token PosService/LoyaltyService use for `Balance`).
- `LoyaltyTierChangeHistory` (table `loyalty_tier_change_history`, append-only, all
  `init`) — `Id`, `TenantId`, `MembershipId`, `FromTierId`/`ToTierId` (both nullable),
  `FromScore`, `ToScore`, `ChangedAt`.
- `ConsumerSupportTicket` (table `consumer_support_tickets`) — `Id`, `TenantId`,
  `ConsumerAccountId`, `CustomerId` (nullable, auto-link target), `Subject`, `Status` (see
  `ShelfGuard.Domain.Constants.ConsumerSupportTicketStatus`), `CreatedAt`, `UpdatedAt`,
  nav `Messages`.
- `ConsumerSupportTicketMessage` (table `consumer_support_ticket_messages`) — `Id`,
  `TicketId`, `SenderConsumerAccountId` (Guid?), `SenderUserId` (Guid?) — **exactly one of
  these two is set per message**, `Body`, `IsRead`, `CreatedAt`.
- `PurchaseReview` (table `purchase_reviews`) — `Id`, `TenantId`, `ConsumerAccountId`,
  `PosTransactionId` (**unique** — one review per purchase, enforced at the DB level),
  `Rating` (short 1-5), `Comment`, `CreatedAt`, `ReplyText`, `RepliedAt`,
  `RepliedByUserId`.
- `PosTransaction.CashRegisterId` (Guid?, no FK) — reserved, do not wire up in this wave.

RLS is live and FORCE-enabled on all 6 tenant-scoped tables (everything except
`consumer_account_profile_changes`, which has none at all by design). `consumer_self_access`
policies key off session var `app.consumer_account_id` — same mechanism already used by
`loyalty_memberships`/`loyalty_ledger_entries`, nothing new to wire up on the consumer-auth
side.

## What's NOT done (this wave's job, per plan §2)

1. `Features/ConsumerProfile` — self-service name/email/phone edit + write to
   `ConsumerAccountProfileChange`. Plan says: gate phone change behind password
   re-entry (no SMS/OTP infra exists).
2. `Features/Loyalty` extension — admin CRUD for `LoyaltyTierDefinition` (mirror
   `GetSettingsAsync`/`UpsertSettingsAsync` shape), consumer-facing "my tier + progress +
   history" endpoints reading `LoyaltyMembership.CurrentTier`/`CompositeScore` +
   `LoyaltyTierChangeHistory`.
3. `PosService.cs` integration — the plan found the exact accrual line (`accrual =
   tx.TotalAmount * loyaltySettings.AccrualRatePercent / 100m`); apply
   `CurrentTier.AccrualMultiplier` there, and `CurrentTier.DiscountPercent` the same way
   redemption already discounts `tx.TotalAmount` before that calc. Requires
   `.Include(m => m.CurrentTier)` wherever membership is loaded for a sale. **Needs a
   product-decision confirm first** (plan's open question): does the tier discount apply
   per-line-item (fiscalization/Checkbox-friendly) or as one total reduction? Check with
   whoever owns ПРРО compliance before implementing.
4. `Features/CustomerSupport` — ticket create/list/message/status-change for both sides
   (consumer + staff).
5. `Features/Reviews` — review create (must verify the purchase belongs to the reviewing
   consumer via `PosTransaction → LoyaltyLedgerEntry/LoyaltyMembership → ConsumerAccountId`
   — no direct FK from `PosTransaction` to `ConsumerAccountId` exists), list, staff reply.
6. `Features/Customers` extension — `CustomerDetailDto` gains tier/progress/open-ticket
   count/recent reviews for the admin customer card.

Worker recompute job (`worker/src/jobs/loyalty-tier-recompute.job.ts`) and all frontend
work are later waves per plan §5 — not this handoff's concern yet, but the backend
endpoints from steps 2-6 above are their dependency.

## Dev DB / tooling notes

Connect with `--connection "Host=localhost;Port=5435;Database=crm;Username=shelfguard_app_dev;Password=307823f594357b97c27a046f33bc5549ad09"`
on any `dotnet ef` command if `appsettings.Development.json` isn't picked up by the
design-time factory. Container: `crmproductsystems-postgres-1` (must be running via
`docker ps`).
