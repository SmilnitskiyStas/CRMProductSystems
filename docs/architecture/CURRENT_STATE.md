# CURRENT_STATE — Multi-Tenant Consumer Platform (Backend + Web Admin)

**Owner:** project-architect
**Date:** 2026-08-17
**Task:** TASK-526 (Stage 6, `.claude/tasks/mobile-roadmap.md`)
**Audited against:** `docs/CLAUDE CODE SPEC — Web Admin, App Builder & Backend.md` (ЕТАП 0)
**Scope:** `backend/` (Domain/Application/Infrastructure/Api) and `frontend/`. `mobile/` is out of
scope for this document — see `docs/mobile/MOBILE_CURRENT_STATE.md` and
`docs/mobile/STAGE_0_REPORT.md`, written the same day by the Codex mobile workstream, for the
client-side equivalent audit. Its "Blocking contracts for production integration" section is the
mobile side's own list of exactly what this document's target counterpart needs to unblock.

This is not a greenfield read. Six recent, undocumented-until-now commits already implement large
pieces of what the new spec formalizes: `eaacfa7d` (unified mobile auth, `ConsumerAccount`),
`29ec2fd4`/`4fa15f7d` (universal cross-tenant loyalty code), `075af2f9`/`9acf6ff5`/`db7c5d40`
(network catalogue, preferred store), `0dccb0d9`/`2cff57e5`/`c17a772c`/`72e33308`/`7208f89f`
(banners, promo products, catalog admin). This document inventories what exists, cites the actual
files, and flags where the shipped model diverges from the spec's.

---

## 1. Tenant model

`backend/ShelfGuard.Domain/Entities/Tenant.cs` — `Id`, `Name`, `Slug`, `Plan`, `Modules` (JSON
string array), `BusinessType`, `IsActive`, `CreatedAt`. `ICollection<User> Users`.

Compared to the spec's ЕТАП 1 minimal model (`Id, Name, Slug, LogoUrl, Status, CreatedAt,
UpdatedAt`): `Id`/`Name`/`Slug`/`CreatedAt` already match. `LogoUrl` and `UpdatedAt` do not exist
yet (small, additive gap). `Status` is a plain `bool IsActive`, not an enum — functionally
equivalent for the spec's stated need (active/inactive), no evidence a richer status enum is
required yet. `Plan`, `Modules`, `BusinessType` are ShelfGuard-specific fields the spec doesn't
ask for and that already do useful work — see §7 (feature flags) and §8 (subscription-readiness).

`.claude/docs/database-schema.md`: `tenants | — | Root entity, no RLS needed` — `tenants` is the
one canonical table with no RLS, same status the spec implicitly assumes for the tenant root
itself (everything else hangs off `TenantId`).

**No formal `ITenantContext`/`ICurrentTenantService` abstraction exists.** Tenant context is
resolved in two independent, uncoordinated ways today:

1. **Per-controller claim reads.** Every staff-facing controller re-implements its own
   `ResolveTenantId()` helper reading `User.FindFirst("tenant_id")` — see
   `BannersController.ResolveTenantId()`, `LoyaltyController.GetTenantId()`, and the same pattern
   repeated across essentially every other controller in `backend/ShelfGuard.Api/Controllers/`.
   There is no single injectable service business/application-layer code calls instead of touching
   `ClaimsPrincipal` directly — the spec's "не дозволяти business code самостійно діставати
   tenantId з request body" principle is honored (nothing reads tenant from the body), but the
   "централізовано визначати current tenant" (TenantContext) part is not: the same three lines are
   copy-pasted per controller rather than centralized.
2. **DB-level enforcement via `TenantConnectionInterceptor`**
   (`backend/ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs`) — on every
   connection open, sets Postgres session variables (`app.tenant_id`, `app.role`, `app.user_id`,
   `app.consumer_account_id`) from the current JWT's claims, which the RLS policies below key off.
   This is arguably a *stronger* isolation guarantee than an app-level `TenantContext` object (the
   database itself refuses cross-tenant rows even if application code has a bug), but it doesn't
   give application code a typed "current tenant" object to depend on the way the spec's ЕТАП 1
   implies.

`ITenantSessionOverride` (`backend/ShelfGuard.Application/Services/ITenantSessionOverride.cs`,
impl `TenantSessionOverride.cs`) is the one existing mechanism for a session that structurally
carries no `tenant_id` claim (the consumer/`ConsumerAccount` case, see §3) to temporarily and
safely operate under a specific tenant's RLS context — `SET LOCAL app.tenant_id` inside a
transaction that reverts on commit/rollback. `LoyaltyService.JoinAsync`, `GetAvailableNetworksAsync`,
and `ConsumerContentService` (all of §4-5) already use it. Its own doc comment states the security
contract explicitly: the `tenantId` passed in must already be a value the caller trusts
unconditionally for that specific operation — not a general RLS bypass.

## 2. Tenant isolation / RLS

Canonical pattern (`.claude/docs/database-schema.md` §"RLS Template"): every tenant-owned table
gets `FORCE ROW LEVEL SECURITY` plus a `tenant_isolation` policy (`TenantId =
NULLIF(current_setting('app.tenant_id', true), '')::uuid`), a `provider_bypass` policy
(`app.role = 'provider'`), and a `worker_bypass` policy (background jobs). This is the spec's
ЕТАП 4 "tenant A cannot read/write tenant B data" requirement, already built and load-bearing for
every existing tenant-scoped table (`Store`, `Product`/`CatalogProduct`, `PosTransaction`, etc.,
and now `Banner`/`LoyaltyMembership`/`LoyaltyLedgerEntry`/`LoyaltyProgramSettings`).

`ConsumerAccount` (see §3) is a deliberate, documented exception to this rule — no `TenantId`
column, no RLS at all, same precedent as `tenants` itself (ADR-023 point 1, confirmed by
security-reviewer TASK-412 item #1: no generic non-owner lookup exists anywhere that could exploit
this). `LoyaltyMembership`/`LoyaltyLedgerEntry` additionally carry a first-of-its-kind
**identity-based** RLS policy, `consumer_self_access`, keyed on `app.consumer_account_id` rather
than `app.tenant_id` — necessary because a consumer session is cross-tenant by design and never
sets `app.tenant_id` to a real value.

Integration test coverage exists (`backend/ShelfGuard.Tests/Infrastructure/`):
`LoyaltyRlsIntegrationTests.cs`, `LoyaltyJoinRlsIntegrationTests.cs`,
`StoreScopeRlsIntegrationTests.cs`, `TenantConnectionInterceptorTests.cs`. **No suite is currently
named or organized as a single `TENANT_ISOLATION_TESTS` gate** the way the spec's §30 requires
("Його падіння має блокувати release") — isolation coverage exists but is scattered per-feature,
not consolidated into one release-blocking suite.

## 3. Identity model: `User` (spec) ↔ Tenant

**Confirmed: the spec's "Tenant" concept maps directly onto ShelfGuard's existing `tenants` table
— it is not a new, parallel entity.** Evidence: `Tenant.cs`'s existing `Id/Name/Slug/CreatedAt`
already satisfy the spec's minimal model; `TenantConnectionInterceptor` sets `app.tenant_id` from
the same JWT claim every existing tenant-scoped feature already reads; `Banner`, the newest
tenant-scoped entity in the codebase (added for this exact initiative before the spec existed),
has a plain `TenantId` FK to `tenants` with the canonical RLS triad — the same shape as every
pre-existing tenant table, not a new isolation model. This is now formally recorded as ADR-029
(§ below, `.claude/docs/decisions.md`).

The spec's other identity concept — "one global user, many-tenant relationships via `UserTenant`,
tenant-specific `LoyaltyAccount`" (MASTER SPEC §14) — is **partially shipped, under different
names, and coupled differently than the spec proposes:**

- **`ConsumerAccount`** (`backend/ShelfGuard.Domain/Entities/ConsumerAccount.cs`) is the spec's
  global "User": no `TenantId`, no RLS (same exception class as `tenants`), one JWT
  (`consumer_account_id` claim, no `tenant_id`) backs access across every tenant it holds a
  membership in. ADR-023 records the full rationale.
- **`LoyaltyMembership`** (`backend/ShelfGuard.Domain/Entities/LoyaltyMembership.cs`) is the
  closest existing analog to the spec's `UserTenant` join row — but it is **not** a generic
  "customer joined this retailer" relationship. It is specifically a bonus-program enrollment:
  `TenantId`, `ConsumerAccountId`, `CustomerId?`, `TotpSecret`, `Balance`, `Status`. Joining a
  network today (`POST /api/consumer/loyalty/{tenantId}/join`,
  `ConsumerLoyaltyController.Join`) *is* joining its loyalty program — there is no lighter-weight
  "browse this retailer's catalog/banners without a bonus card" membership distinct from it.
  **This is the one real structural divergence from the spec's model** — MASTER SPEC §14 explicitly
  separates a generic `UserTenant` from a tenant-specific `LoyaltyAccount` hanging off it; ShelfGuard
  currently conflates the two into one entity. Practical consequence: `GetAvailableNetworksAsync`
  (`LoyaltyService.cs:186`) only lists tenants where `HasModule("loyalty")` is true and
  `LoyaltyProgramSettings.IsEnabled` — a tenant that wanted a consumer-app presence (banners,
  catalog, theme) without running a bonus program could not appear in retailer discovery at all
  under the current model.
- **Staff-side membership is already correctly separated from the consumer side**, per the spec's
  own explicit warning (ЕТАП 2: "Не змішувати Customer membership і Retailer employee
  permissions"). `User` stays tenant-scoped 1:1 (a staff row belongs to exactly one tenant);
  `TenantRole` (ADR-020/021) is the existing named-role/capability/tab-visibility template system.
  No `UserTenant`-style many-to-many is needed or implied for staff — the spec doesn't ask for one
  either ("для retailer staff потрібна окрема модель membership/roles, **якщо її ще немає**" — it
  already exists).
- **Auto-linking between `ConsumerAccount` and `User`** (a person who is both a customer and an
  employee of the same tenant) is implemented by phone/email match, not yet a persisted FK — see
  `docs/mobile-unified-auth-backend-handoff.md` §"Завдання для backend/database-агента", item 1:
  `consumer_accounts.linked_user_id` is recommended but not yet added as an explicit column;
  current matching is a live lookup at login time (`MobileAuthController.FindLinkedUserAsync`).

## 4. Authentication surfaces (backend)

Three controllers coexist under `backend/ShelfGuard.Api/Controllers/`:

- **`AuthController`** — staff-only, pre-existing, unrelated to this initiative.
- **`ConsumerAuthController`** (`/api/consumer-auth/register|login`) — the original Фаза 0
  consumer-only auth flow (`IConsumerAuthService`), still live and still the underlying primitive
  the newer controller below calls into. Not deprecated, just superseded as the mobile client's
  entry point.
- **`MobileAuthController`** (`/api/mobile-auth/register|login`) — the unified entry point
  (`eaacfa7d`, `docs/mobile-unified-auth-backend-handoff.md`). The client never picks a role: it
  resolves the identifier against `ConsumerAccount` first, then auto-detects a linked, active
  `User` and — if found — additionally issues a **workspace** JWT alongside the **personal** JWT
  (dual-token model, respecting staff 2FA via `IAuthService.IssueLinkedMobileSessionAsync`).
  `canAccessWorkspace`, `role`, `permissions`, `capabilities`, `tabs` are all server-derived, never
  client-supplied.

Open item explicitly flagged in the handoff doc (not yet resolved): the personal token has no
refresh/expiry lifecycle decision recorded (three options listed, none chosen) — the mobile-side
audit (`MOBILE_CURRENT_STATE.md` §5, §15 High priority #4) independently flags this as its own
top blocker for a production cold-bootstrap.

## 5. Loyalty domain (tenant-scoped bonus program + cross-tenant wallet)

`LoyaltyMembership` / `LoyaltyLedgerEntry` / `LoyaltyProgramSettings` — see `domain-model.md` for
full field lists; ADR-023 for the identity/security rationale. Highlights relevant to this audit:

- **Universal cross-tenant customer code** (`29ec2fd4`): `GET /api/consumer/loyalty/code`
  (`ConsumerLoyaltyController.GetCode`) returns one rotating TOTP-backed code usable at any tenant
  the consumer belongs to; auto-membership is created on first POS scan
  (`LoyaltyController.ResolveOrCreateByPhone`, TASK-498).
- **Per-tenant display format** (`4fa15f7d`, TASK-499): `LoyaltyProgramSettings.CustomerCodeFormat`
  (`"qr"`/`"barcode"`) — one format for the whole network, editable via `GET/PUT
  /api/settings/loyalty` on `/consumer-app` in the web admin. This is a working, narrow example of
  exactly the kind of tenant-level "config affecting mobile rendering" the spec's Theme/Config
  domain (ЕТАП 3, 6) generalizes — currently a single hardcoded field, not part of any declarative
  config document.
- **Network catalogue + preferred store** (`075af2f9`/`9acf6ff5`/`db7c5d40`):
  `GET /api/consumer/loyalty/networks`, `PUT /api/consumer/loyalty/preferred-store`. As noted in
  §3, this list is gated on the `loyalty` module, not a general "retailer is live on the consumer
  app" flag — no `GET /api/v1/retailers` (spec ЕТАП 14) exists.

`[RequireModule("loyalty")]` gates the staff-facing `LoyaltyController`, but **cannot** gate
`ConsumerLoyaltyController` (its own doc comment states why: `RequireModuleAttribute` reads the
`tenant_id` claim, which a consumer JWT never carries) — module enforcement for consumer-loyalty
actions is done manually inside `LoyaltyService.JoinAsync` instead. This is the same structural
problem the spec's ЕТАП 10 Feature Flags will have to solve generally: today's only feature-gating
primitive (`RequireModuleAttribute`) assumes a staff, tenant-scoped session and doesn't work for
consumer/cross-tenant sessions at all.

## 6. Consumer-facing marketing content: Banners, Promotions, Catalog

- **`Banner`** (`backend/ShelfGuard.Domain/Entities/Banner.cs`) — tenant-scoped, standard RLS.
  Fields include `Title/Body/Terms` (plain text, `\n`-joined — not JSON blocks), `ImageUrl` with an
  `Icon`/`BackgroundColor`/`AccentColor` fallback, `DetailMode` (`internal`/`external`),
  `ValidFrom`/`ValidUntil`, `IsActive` (manual pause, never hard-deleted), and — the piece most
  relevant to the spec's Draft/Preview/Publish model — **`PublishedAt`** (`0dccb0d9`→`2cff57e5`→
  `c17a772c`): `null` = draft, never shown to consumers; set once via `Banner.Publish()`
  (idempotent) and never touched by `Update()`. `IsCurrentlyActive()` combines `IsActive` +
  `ValidFrom`/`ValidUntil` for "currently showing," independent of publish state.
- **`BannersController`** (`/api/banners`, `AtLeastEnterpriseAdmin`) — full admin CRUD, publish,
  soft-deactivate, image upload (5 MB cap, same allowlist discipline as `ItemsController`), and a
  view/click analytics read (`banner_events` via `BannerEvent`).
- **`ConsumerContentController`** (`/api/consumer/{tenantId}/banners|promotions|catalog`,
  `[AllowAnonymous]`) — the public read side. Anonymous browsing is explicitly supported (view/click
  events attribute to a consumer JWT when present, else recorded anonymously) — this already
  matches the spec's "discover before joining" intent (MASTER SPEC §12) for content, even though no
  generic retailer-discovery endpoint exists yet (§3/§5). Runs entirely through
  `ITenantSessionOverride` since `tenantId` here is a route parameter, never a JWT claim.
- **Promotions** reuse the existing `Discount` entity as a read projection (`GetActivePromotionsAsync`)
  — no new `Promotion`/`Coupon` domain was introduced; `frontend/features/consumer-app/api/discounts.ts`
  and `PromoProductsSection.tsx` are thin admin views over the existing discounts feature.
- **Catalog** for the consumer app is the existing `CatalogProduct`/`Items` catalog, unchanged —
  `ConsumerContentController.GetCatalog` just paginates/filters it and annotates per-store
  availability. The web admin's `/consumer-app/catalog` page
  (`frontend/features/consumer-app/components/CatalogSection.tsx`) is explicitly a read-only status
  card by design ("the catalog already has full CRUD at `/inventory`... this section deliberately
  does not duplicate it") — there is no consumer-app-specific catalog curation (e.g., "featured for
  mobile" flag, mobile-only sort order) distinct from the operational inventory catalog.

## 7. Web admin (`frontend/`)

`/consumer-app` area exists (`frontend/app/(dashboard)/consumer-app/{,banners,promotions,catalog}`),
gated `AT_LEAST_ENTERPRISE_ADMIN` in `frontend/components/layout/Sidebar.tsx`:

- `/consumer-app` — `BonusProgramSection.tsx` (loyalty program settings, incl. `CustomerCodeFormat`).
- `/consumer-app/banners` — `BannersSection.tsx` + `BannerForm.tsx` + `LifecycleTabs.tsx`
  (draft/running/past tabs, `7208f89f`) — full banner CRUD/publish UI.
- `/consumer-app/promotions` — `PromoProductsSection.tsx` over the existing discounts feature.
- `/consumer-app/catalog` — read-only status card (§6).

This is a real, working start on the spec's ЕТАП 5 "Retailer Admin" area, but covers only the
content-management slice (`Banners`≈content, no `Design`/`Pages`/`Navigation`/`Features`/`Versions`
sub-areas exist at all). **No Theme Editor, no App Builder/Block Registry/drag-and-drop, no
Navigation Builder, no Feature Flags UI, no Draft/Preview/Publish workflow beyond the one banner
entity, and no Version History/rollback UI exist anywhere in `frontend/`.**

## 8. API contract, versioning, documentation conventions

- **No API versioning** — every endpoint in this audit is unversioned `/api/...`; no `/api/v1/`
  prefix exists anywhere in the codebase.
- **Swagger/Swashbuckle is already wired** (`backend/ShelfGuard.Api/Program.cs`:
  `AddSwaggerGen`/`UseSwagger`/`UseSwaggerUI`, dev-environment gated) — an OpenAPI document can
  already be generated, but it is not published as a committed contract file, and nothing consumes
  it as a source of truth yet.
- **No `contracts/` directory** at the repo root — `/contracts/mobile-config.schema.json` does not
  exist. `mobile/` already has AJV installed in anticipation (`MOBILE_CURRENT_STATE.md` §12) but
  nothing to validate against yet.
- **No `docs/integration/MOBILE_API.md` or `docs/integration/CHANGELOG.md`** — the two existing
  handoff docs (`docs/mobile-unified-auth-backend-handoff.md`,
  `docs/loyalty-customer-code-format-mobile-handoff.md`) are close in spirit (per-endpoint
  purpose/auth/request/response/errors) but are one-off task handoffs, not the spec's proposed
  living, endpoint-indexed integration reference.

## 9. Cross-reference: the mobile side's own audit

`docs/mobile/MOBILE_CURRENT_STATE.md` and `docs/mobile/STAGE_0_REPORT.md` (Codex mobile
workstream, same date) independently confirm the shape of this backend audit from the client side:
dual personal/workspace tokens already exist and are structurally isolated; a `selectedTenantId`
already exists inside the loyalty feature but is loyalty-specific UI state, not an
application-wide active-tenant concept; and — most directly relevant to sequencing future
work — its own "Blocking contracts for production integration" list is: **`contracts/mobile-config.schema.json`**,
**documented active-tenant transport for `GET /api/v1/mobile/config`**, **OpenAPI +
standardized config/tenant errors**, and **final personal-session refresh/validation behavior**.
All four are backend/web-admin deliverables from this document's target side (see
`TARGET_ARCHITECTURE.md`), not something the mobile side can resolve on its own.

## 10. Summary — what already exists vs. what is greenfield

**Already shipped, directly reusable:**
`Tenant` root entity + RLS isolation triad · `ConsumerAccount` global identity · dual-token unified
mobile auth · TOTP-based rotating cross-tenant loyalty code · `LoyaltyMembership` as a (loyalty-
coupled) join mechanism · `Banner` Draft→Publish lifecycle (single-entity precedent for the
spec's general versioning model) · anonymous public consumer-content browsing · `ITenantSessionOverride`
as the pattern for any future cross-tenant consumer operation · `/consumer-app` admin area as the
Retailer Admin shell · `RequireModuleAttribute`/`Tenant.Modules` as the staff-side feature-flag
precedent · Swagger already wired.

**Not started (greenfield for ЕТАП 1, 3-18):**
`ITenantContext` centralized service · `UserTenant`-equivalent generic (non-loyalty) membership ·
`MobileConfiguration`/`MobileConfigurationVersion`/`MobileTheme` domain · `/contracts/mobile-config.schema.json` ·
`GET /api/v1/mobile/config` · Theme Editor · App Builder/Block Registry/Block Property Editor ·
Page Builder · Navigation Builder · consumer-facing Feature Flags engine (+ subscription-plan hook) ·
generalized Draft/Preview/Publish/Version History/rollback beyond `Banner` · Preview API ·
`GET /api/v1/retailers` discovery + QR/deep-link onboarding · Audit log for config changes ·
API versioning · committed OpenAPI contract · `docs/integration/MOBILE_API.md`/`CHANGELOG.md` ·
consolidated `TENANT_ISOLATION_TESTS` suite.
