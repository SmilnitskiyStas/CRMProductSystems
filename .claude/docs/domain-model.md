# Domain Model

**Owner:** project-architect
**Updated:** 2026-08-06
**Source:** v1-spec.md

## Core Entities

### Tenant
Multi-tenant root. All data tables have TenantId FK + RLS.
Fields: id, name, slug, plan, modules (JSONB), is_active, created_at

### User
Belongs to tenant (or NULL for provider role).
Roles: provider / enterprise_admin / network_manager / store_manager / merchandiser / storekeeper / cashier
Fields: id, tenant_id, email, role, store_id, telegram_chat_id, push_token, is_active
> `store_id` is a UI "default home location" hint only (set at invite/edit time) — it is never
> read for access control. Real store-scoped data visibility goes through the `UserLocation`
> join entity below (ADR-022). Also carries an optional `tenant_role_id` (ADR-020) — see
> `TenantRole` below.

### TenantRole
Named, reusable custom-role template, additive on top of a user's base `Role` (ADR-020).
Assigned via `User.tenant_role_id` (nullable FK, `SET NULL` on delete — templates are archived
via `is_active = false`, never hard-deleted while users may still reference them).
Fields: id, tenant_id, name, capabilities (text[] — backend action keys, e.g. `users.manage`,
resolved into a JWT `capabilities` claim), allowed_tabs (text[] — sidebar/route visibility keys,
ADR-021, resolved into a JWT `tabs` claim; a separate axis from capabilities, never merged —
see `TenantRoleTabs.cs`), is_active, created_by_user_id, created_at, updated_at

### UserLocation
Many-to-many assignment of a `User` to a `Store`/`Location` — the single enforcement mechanism
for store-scoped data visibility (ADR-022). `enterprise_admin` needs no rows (unconditional
bypass); every other rank gets one row per assigned location, including single-location roles
(no shortcut through `User.store_id`).
Fields: id, tenant_id, user_id, location_id, assigned_by_user_id, created_at
Unique (tenant_id, user_id, location_id). No soft-delete — hard DELETE revokes the assignment.
As of this doc's snapshot, no RLS policy reads this table yet — that is ADR-022 Stage 3
(RESTRICTIVE `store_scope` policy on 9 tables, written and tested but held on branch
`stage3-rls-enforcement-hold` pending a manual backfill gate, see
`.claude/docs/store-scope-rollout-checklist.md`).

### Store
Physical location. Types: shop / central_warehouse / production / distribution
Fields: id, tenant_id, name, address, latitude, longitude, type, floor_plan (JSONB)

### StoreZone
Zone within a store (shelf, fridge, freezer, display, production, warehouse).
Fields: id, store_id, name, type, position (JSONB), shelves_count, temp_min, temp_max
Note: no direct tenant_id — RLS enforced via stores join.

### Category
Hierarchical product categories. Self-referencing parent_id.
Fields: id, tenant_id, name, parent_id, is_active

### ProductSegment
Demand segment (e.g. "Milk 2.5%"). Used for cannibalization analysis in v2.
Fields: id, tenant_id, name, category_id, description

### Supplier
Fields: id, tenant_id, name, edrpou, contact_person, delivery_days, has_supplier_portal, return_policy

### CatalogProduct (v1 tenant-aware product)
EF entity: `CatalogProduct` → table: `catalog_products`
Supports ABM management types (MTS/MTO/NA/NM), buffers, shelf life.
Fields: id, tenant_id, barcode, name, category_id, segment_id, unit, management_type, min_stock, max_stock, safety_buffer, shelf_life_days, default_supplier_id, vat_rate, price_purchase, price_retail

> ⚠️ Legacy POC `Product` entity → `Products` table (no tenant_id) still exists for the catalog API. Will be removed in TASK-003b.

### ProductSupplierSetting
ABM params per product-supplier pair: MOQ, USQ, price, delivery days.
Fields: id, tenant_id, product_id, supplier_id, moq, usq, price_purchase, delivery_days, is_primary

### ProductStock (Batch) — CORE FEFO TABLE
One product can have multiple batches with different expiry dates.
**FEFO rule**: always consume the batch with the lowest `expiry_date` where `quantity > 0`.
Fields: id, tenant_id, product_id, store_id, zone_id, shelf_number, batch_number, quantity, quantity_initial, expiry_date (DATE NOT NULL), status, source_type, source_id, added_by, notified_warning_at, notified_critical_at

Status values: safe / warning / critical / expired / sold_out / archived / needs_verification
Status computed by `expiry-check.job` (BullMQ cron, every hour).

### StockMovement
Audit trail for every quantity change.
Types: receipt / transfer / production / discount / write_off / sale / adjustment / return
Fields: id, tenant_id, movement_type, product_stock_id, product_id, from_store_id, to_store_id, quantity, quantity_before, quantity_after, unit_price, reference_id, performed_by

### StockEvent
IoT/sensor event placeholder (v3). Stores confidence score for sensor readings.
Fields: id, tenant_id, event_type, product_stock_id, source_device_id, quantity_delta, confidence (0-100), meta (JSONB)

### StockReceipt
Goods receiving document. status: draft → ordered → in_transit → received → cancelled.
Fields: id, tenant_id, supplier_id, destination_store_id, via_central_store, status, expected_at, received_at, created_by, received_by

### StockReceiptItem
Line item in a receipt. expiry_date and batch_number entered at receiving time.
Fields: id, receipt_id, product_id, quantity_ordered, quantity_received, price_purchase, expiry_date, batch_number, discrepancy_notes

### StockTransfer
Stock movement between stores. status: draft → in_transit → received → cancelled.
Fields: id, tenant_id, from_store_id, to_store_id, transfer_type, status, initiated_by, confirmed_by
**Rule:** expiry_date and batch_number are COPIED from ProductStock — never change.

### StockTransferItem
Fields: id, transfer_id, product_stock_id, product_id, quantity, expiry_date (copied), batch_number (copied)

### WriteOff
Write-off document. status: draft → pending_approval → approved → rejected.
Reasons: expired / damaged / theft / production_loss / other
Fields: id, tenant_id, store_id, status, reason, total_loss_amount, pdf_url, created_by, approved_by, approved_at

### WriteOffItem
Fields: id, write_off_id, product_stock_id, product_id, quantity, unit_price, loss_amount

### Discount
Price reduction for near-expiry stock. status: pending → active → expired → cancelled.
Fields: id, tenant_id, product_stock_id, product_id, store_id, discount_percent, price_original, price_discounted, reason, valid_from, valid_until, auto_applied, webhook_sent_at

### NotificationSetting
Per-user per-event per-channel notification preferences.
Fields: id, user_id, event_type, channel (telegram/push/email/webhook), is_enabled
Unique constraint: (user_id, event_type, channel)

### NotificationQueue
BullMQ-backed delivery queue. status: pending → sent / failed.
Fields: id, tenant_id, user_id, channel, event_type, payload (JSONB), status, retry_count, sent_at, error

### ActivityLog
Immutable audit log. All impersonated actions flagged.
Fields: id, tenant_id, user_id, action, entity_type, entity_id, meta (JSONB), ip_address, is_impersonated, created_at

### ConsumerAccount
Global, cross-tenant identity for an end customer (Loyalty Фаза 0, TASK-404) — **no** `tenant_id`,
**no** RLS (`database-schema.md` — same precedent as `Tenant`, protected only by application code
that never exposes a generic lookup to a non-owner). One `ConsumerAccount` backs exactly one JWT
(`consumer_account_id` claim, no `tenant_id`) and can hold a `LoyaltyMembership` in many tenants at
once — a "wallet of cards," no re-login per network.
Fields: id, phone (globally unique, normalized `+380XXXXXXXXX`), password_hash, full_name, email?,
failed_login_attempts, lockout_until, is_active, created_at, last_login_at

### LoyaltyMembership
A `ConsumerAccount`'s enrollment in one tenant's bonus program. Tenant-scoped, standard RLS triad
plus the identity-based `consumer_self_access` policy (`database-schema.md`). `Balance` is a
denormalized running total (same pattern as `Customer.total_spent`) protected by an `xmin`
optimistic-concurrency token (TASK-414) — the authoritative audit trail is `LoyaltyLedgerEntry`.
The "live" rotating QR/barcode is backed by `totp_secret` (Base32) — reuses the same TOTP
infrastructure as `User` 2FA (`ITotpService`); the secret never leaves the server, the client only
ever receives the current rotating code.
Fields: id, tenant_id, consumer_account_id, customer_id?, linked_user_id?, totp_secret,
last_redeemed_timestep? (anti-replay high-water mark), balance, status (active/blocked), joined_at
Unique (tenant_id, consumer_account_id).

### LoyaltyLedgerEntry
Append-only audit trail behind `LoyaltyMembership.balance` — rows are never updated or deleted;
balance only ever moves by inserting a new row and updating the parent membership's balance in the
same transaction (mirrors `StockMovement`'s discipline against `ProductStock`).
Fields: id, tenant_id, membership_id, entry_type (accrual/redemption/manual_adjustment/expiry),
amount (signed — positive accrual, negative redemption/expiry), balance_after, pos_transaction_id?,
created_by_user_id?, note?, created_at

### LoyaltyProgramSettings
One row per tenant — bonus program configuration. Defaults (3% accrual / 50% redemption cap) are
plan-proposed starting values, not competitor-sourced figures (the competitor analysis this plan
followed covers analytics only, never bonus mechanics).
Fields: id, tenant_id (unique), is_enabled, accrual_rate_percent (default 3.0),
redemption_cap_percent (default 50.0), min_redemption_balance, code_ttl_seconds (default 30),
updated_at

#### Relationships to existing entities
- **`Customer`** — `LoyaltyMembership.customer_id` (nullable, `SetNull`) links to the tenant's own
  CRM record, auto-found by phone or auto-created at membership-join time. This is what finally
  gives `PosTransaction.customer_id` (existed since v1, never previously written by any code path)
  a real writer: every sale carrying a `CustomerId` now updates `Customer.TotalOrders`/`TotalSpent`.
  `Customer` itself does **not** become a membership entity — it stays tenant-scoped CRM data;
  `LoyaltyMembership` sits alongside it.
- **`PosTransaction`** — `LoyaltyLedgerEntry.pos_transaction_id` (nullable, `SetNull`) links an
  accrual/redemption entry to the sale that produced it; `manual_adjustment`/`expiry` entries have
  none. Redemption reduces `PosTransaction.TotalAmount` *before* tax; accrual is computed on that
  same net amount — both happen inside `PosService.CreateSaleAsync`'s one existing
  `SaveChangesAsync()`, no separate commit.
- **`User`** — two distinct, deliberately different relationships:
  1. `LoyaltyLedgerEntry.created_by_user_id` (nullable, `SetNull`) — which staff member performed a
     `manual_adjustment`.
  2. `LoyaltyMembership.linked_user_id` (nullable, `SetNull`) — see "Two staff-join cases" below.
- **`Tenant`** — `LoyaltyMembership`/`LoyaltyLedgerEntry`/`LoyaltyProgramSettings` are all
  tenant-scoped (`FK ... Restrict`, RLS-isolated); `ConsumerAccount` is not tenant-scoped at all.
  `Tenant.modules` gained two independent keys: `"loyalty"` (this cluster) and
  `"marketing_analytics"` (RFM dashboard, Фаза 1 — no new tables of its own; see ADR-023).

#### Two ways staff end up with a LoyaltyMembership — deliberately different mechanisms
1. **Pure consumer** (never a `User` row) — registers through a wholly separate auth flow
   (`POST /api/consumer-auth/register`/`login`, `ConsumerAccount` + its own JWT, no `tenant_id`
   claim at all). Joins a tenant's program via `POST /api/consumer/loyalty/{tenantId}/join`.
2. **Staff member joining their own employer's program** — no new auth flow, no cross-tenant
   identity needed. From their existing staff session: `POST /api/loyalty/join-as-staff` finds-or-
   creates a `ConsumerAccount` by the caller's own `User.Phone`, then creates (or backfills)
   `LoyaltyMembership.linked_user_id = callerUserId` **in the caller's own tenant only**. This is
   an ordinary tenant-scoped staff endpoint (`[Authorize]`, reads `tenant_id`/`sub` claims from the
   normal staff JWT) — deliberately not routed through the consumer auth/identity machinery at all,
   since "which tenant" is already answered by the staff session itself. A given `ConsumerAccount`
   can simultaneously be case 1 in tenants it registered into directly and case 2 in its own
   employer's tenant — `linked_user_id` is simply null in the former, set in the latter.

### PostCampaignSegment
A marketer-uploaded list of customer identifiers (`Customer.Id` GUIDs or phone numbers), sourced
from *outside* this system (Post-Campaign Analysis, Фаза 4, TASK-471), compared across equal
before/after date windows around a campaign. The first **persisted** entity in the whole
marketing-analytics initiative — Фаза 1-3 (RFM, Price Segments, Audience Builder) compute
everything live on every request instead; see `decisions.md` ADR-023 addendum (Фаза 4) for why this
phase had to break that precedent. The nullability of `after_start`/`after_end`/`before_start`/
`before_end` together IS the draft-vs-analyzed state (see `database-schema.md` TASK-471 and
`glossary.md` "Draft vs. analyzed segment") — no separate boolean/enum column.
Fields: id, tenant_id, created_by_user_id (FK→users, Restrict, non-nullable — a segment always has
an owner), name?, uploaded_count, matched_count, duplicate_count, unknown_count, invalid_count,
unknown_tokens_sample (jsonb string[], capped ~20), invalid_tokens_sample (jsonb string[], capped
~20), after_start? / after_end? / before_start? / before_end? (date), segment_hash, created_at,
analyzed_at?

### PostCampaignSegmentMember
One matched customer within a `PostCampaignSegment` — created only for tokens that resolved to a
real `Customer.Id` in this tenant at import time. Unknown/invalid tokens are never materialized as
rows here; they exist only as counts plus a capped sample on the parent segment.
Fields: id, tenant_id (plain denormalized column, no separate FK to `tenants` — same treatment
`loyalty_ledger_entries.tenant_id` already gets), segment_id (FK→post_campaign_segments, Cascade),
customer_id (FK→customers, Cascade)
Unique (segment_id, customer_id) — a customer appears at most once per segment.

#### Relationships to existing entities (PostCampaignSegment/PostCampaignSegmentMember)
- **`Customer`** — `PostCampaignSegmentMember.customer_id` (Cascade) links to the tenant's existing
  CRM record; import matches an uploaded token against `Customer.Id` (GUID) **or** the customer's
  normalized phone, reusing Фаза 0's existing `PhoneNormalizer` verbatim — not a new identity
  concept (`decisions.md` ADR-023 addendum, Фаза 4, point b).
- **`User`** — `PostCampaignSegment.created_by_user_id` (Restrict, non-nullable) is the staff
  member who uploaded the segment. Unlike most `CreatedBy`/`CreatedByUserId` columns in this
  codebase (nullable, `SetNull`), this one is required — mirrors
  `UserPermissionGrant.granted_by_user_id`'s existing "staff-authored row always has an author"
  precedent, since a segment always has an owner.
- **`PosTransaction`** — read-only, never a stored FK: every report tab
  (summary/daily-turnover/rfm-activity/customers/migration) reads `PosTransaction` rows live for
  each matched member's before/after window, the same way Фаза 0-3 already do; nothing about a
  transaction is ever written back.
- **`Tenant`** — both entities are tenant-scoped (`PostCampaignSegment` via a real FK, `Restrict`;
  `PostCampaignSegmentMember` via a denormalized column, no FK — see `database-schema.md`), RLS
  isolated, and ride under the existing `"marketing_analytics"` module key — no new module key
  introduced for this phase.

---

## Key Business Rules
1. **FEFO** — always pick the batch with the nearest `expiry_date` where `quantity > 0`
2. **Expiry dates never change** — `expiry_date` and `batch_number` are copied as-is on transfer
3. **Batch status** — computed by `expiry-check.job` cron (hourly); never computed on-the-fly in queries
4. **Safety buffer** — reserved quantity for shelf presentation; not available for sale
5. **Soft delete** — all business entities use `is_active = false`, never hard DELETE
6. **Tenant isolation** — enforced at DB level via RLS; application layer never filters by tenant_id manually
