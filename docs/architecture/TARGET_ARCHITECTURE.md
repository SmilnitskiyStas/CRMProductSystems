# TARGET_ARCHITECTURE — Multi-Tenant Consumer Platform (Backend + Web Admin)

**Owner:** project-architect
**Date:** 2026-08-17
**Task:** TASK-526 (Stage 6, `.claude/tasks/mobile-roadmap.md`)
**Target per:** `docs/CLAUDE CODE SPEC — Web Admin, App Builder & Backend.md` ЕТАП 1-18
**Companion:** `docs/architecture/CURRENT_STATE.md` (cited throughout as "CURRENT_STATE §n")

This document is planning only — no code, migrations, or `.claude/tasks/mobile-roadmap.md` entries
were created by it. §2 is the gap-annotated ЕТАП list; §3 is a proposed (not registered) task
breakdown for a future implementation effort, for the user/orchestrator to review and register.

---

## 1. Tenant-mapping decision (recorded as ADR-029)

**Confirmed with evidence, not assumed: the spec's "Tenant" (a retailer in the shared consumer
app) is ShelfGuard's existing `tenants` table — the same tenant a business already uses for the
whole SaaS platform. It is not, and should not become, a second parallel entity.**

See `CURRENT_STATE.md` §1/§3 for the full evidence chain (field-shape match, RLS reuse, `Banner`
already using a plain `TenantId` FK with the canonical isolation triad). Recorded as ADR-029 in
`.claude/docs/decisions.md`.

One related concept is **not** resolved and is called out explicitly rather than decided silently:
the spec's `UserTenant` (a generic "consumer joined this retailer" row, MASTER SPEC §14) has no
shipped equivalent independent of `LoyaltyMembership` (CURRENT_STATE §3). Building ЕТАП 2/14 requires
a product decision first — see §3 Stage A below.

## 2. ЕТАП-by-ЕТАП target model and gap status

| ЕТАП | Target (spec) | Status | Evidence / notes |
|---|---|---|---|
| 0 | Repository audit, `CURRENT_STATE.md`/`TARGET_ARCHITECTURE.md` | **done** | This pair of documents (TASK-526). |
| 1 | `Tenant` minimal model + centralized `TenantContext` | **partially shipped** | `Tenant` entity: shipped, minor field gap (`LogoUrl`, `UpdatedAt`). `TenantContext` as a centralized service: **not started** — tenant resolution is per-controller claim reads today (CURRENT_STATE §1). |
| — | Tenant isolation (`TenantId` on every tenant-owned entity, tested) | **shipped, load-bearing** | Canonical RLS triad on every existing table; `ConsumerAccount`/`Tenant` are the only documented no-RLS exceptions. No consolidated `TENANT_ISOLATION_TESTS` suite yet (CURRENT_STATE §2). |
| 2 | `UserTenant` (generic customer↔retailer relationship), staff membership kept separate | **partially shipped, coupled differently** | `ConsumerAccount` = spec's global `User`. `LoyaltyMembership` is the closest analog to `UserTenant` but is loyalty-specific, not generic (CURRENT_STATE §3) — **open product decision**, see §3 Stage A. Staff membership (`User`+`TenantRole`) is already correctly separate, per the spec's own instruction. |
| 3 | `MobileConfiguration`/`MobileConfigurationVersion`/`MobileTheme` domain | **not started** | `Banner.PublishedAt`'s draft/publish switch (CURRENT_STATE §6) is the one shipped precedent for "a single entity with a publish lifecycle," at a much smaller scope than a full versioned config document. |
| — | `/contracts/mobile-config.schema.json` | **not started** | No `contracts/` directory exists at the repo root. Mobile has AJV installed and ready to consume it (`MOBILE_CURRENT_STATE.md` §12). |
| 4 | `GET /api/v1/mobile/config` (+ ETag) | **not started** | No `/api/v1/` prefix exists anywhere yet (CURRENT_STATE §8). This is explicitly the mobile side's #1 blocking contract (`STAGE_0_REPORT.md`). |
| 5 | Retailer Admin area (Dashboard, Mobile App: Design/Pages/Navigation/Features/Versions), RBAC | **partially shipped** | `/consumer-app` exists with Dashboard/Banners/Promotions/Catalog, `AtLeastEnterpriseAdmin`-gated (CURRENT_STATE §7). Covers only a content-management slice; no Design/Pages/Navigation/Features/Versions sub-areas. |
| 6 | Theme Editor (whitelisted colors/radii/spacing, live preview) | **not started** | `LoyaltyProgramSettings.CustomerCodeFormat` (CURRENT_STATE §5) is the one shipped example of a tenant-level, admin-editable, mobile-rendering-affecting field — proves the pattern works end-to-end at tiny scope, nothing theme-shaped exists. |
| 7 | App Builder foundation (drag & drop block canvas) | **not started** | — |
| — | Block Registry (`displayName`/`icon`/`category`/`defaultProps`/`validationSchema`/`supportedDataSource`) | **not started** | — |
| — | Block Property Editor (schema-driven, no hardcoded if/else) | **not started** | — |
| 8 | Page Builder (Home fully block-driven; Profile/Auth/Security stay system-controlled) | **not started** | Existing consumer screens (banners/promotions/catalog/loyalty wallet) are exactly the kind of content Page Builder would eventually compose from blocks — currently server-rendered as fixed screens in `mobile/`, not block-driven (out of scope here, see `MOBILE_CURRENT_STATE.md` §10). |
| 9 | Navigation Builder (min 2/max 5, permitted icons, backend validation) | **not started** | — |
| 10 | Feature Flags (`loyalty`/`promotions`/`catalog`/`coupons`/`news`/`receipts`/`delivery`/`personalOffers`), subscription-plan-ready | **partially shipped, wrong shape for consumer sessions** | `Tenant.Modules` + `RequireModuleAttribute` is a working staff-side feature-flag precedent (ADR-015) but structurally cannot gate a consumer/cross-tenant session (CURRENT_STATE §5/§6 — it reads a `tenant_id` claim consumer JWTs never carry). A parallel, consumer-session-aware flag engine is needed, not a copy-paste of the existing one. |
| 11 | Draft → Preview → Validate → Publish, atomic, invalid schema rejected | **partially shipped at single-entity scale** | `Banner`'s `PublishedAt` (CURRENT_STATE §6) is a working, idempotent, one-field version of this for exactly one entity. No general-purpose Draft/Preview/Publish service exists for a whole config document. |
| 12 | Version History + Rollback (never deletes, clones forward) | **not started** | — |
| 13 | Preview API (`GET /api/v1/mobile/config/preview`, staff-only) | **not started** | — |
| 14 | Retailer discovery API (`GET /api/v1/retailers[/{slug}]`, join, leave) | **not started, narrower analog exists** | `GET /api/consumer/loyalty/networks` + `POST /api/consumer/loyalty/{tenantId}/join` (CURRENT_STATE §5) do similar work but are loyalty-module-gated, not a general retailer directory. Same open decision as ЕТАП 2. |
| 15 | QR/deep-link onboarding (`https://app.domain/join/{slug}`) | **not started** | — |
| 16 | Core retail domains tenant-aware incrementally (Stores/Loyalty/Promotions/Products/Categories/Coupons/News/Receipts) | **mostly already tenant-aware** | Stores/Loyalty/Products/Categories are already tenant-scoped, RLS-isolated, and reused by the consumer surfaces (CURRENT_STATE §5/§6). `Coupons`/dedicated `News`/`Receipts`-for-consumers are not modeled (Promotions reuses `Discount`; no consumer News/Coupon/Receipt entity exists). |
| 17 | Audit (mobile config changed/published/rolled back, feature changed, role changed, promotion edited) | **partially shipped, generic** | `ActivityLog` (`domain-model.md`) is an existing generic tenant-scoped audit table with `action`/`entity_type`/`entity_id`/`meta` — plausibly reusable rather than a new table; not yet wired to any of the new consumer-platform actions since most don't exist yet. |
| 18 | Subscription-ready feature architecture (`START/BUSINESS/PRO/ENTERPRISE` → Features hook, no billing) | **not started** | `Tenant.Plan` (`basic`/`standard`/`enterprise`/`trial`, CURRENT_STATE §1) already exists as a string field but nothing reads it to gate features today — a ready, unused hook, not a built one. |
| 27 | API rules: `/api/v1/`, `ProblemDetails`/structured errors, standardized pagination, UTC dates | **not started (versioning), partially shipped (errors/pagination)** | No versioning anywhere. Structured `{ error }` responses and paginated list endpoints are an existing, consistent convention across the whole API (not literally RFC 7807 `ProblemDetails`, but a stable, structured shape) — extending it to new endpoints is straightforward, not greenfield. |
| 28 | OpenAPI generation, kept current after every API change | **partially shipped** | Swashbuckle already wired (dev-only), never published as a committed `openapi.json` (CURRENT_STATE §8). |
| 29 | `docs/integration/MOBILE_API.md` | **not started** | Two one-off handoff docs exist in spirit (CURRENT_STATE §8) but no `docs/integration/` directory or living endpoint reference. |
| 30 | Testing: unit (validator/feature rules/versioning/theme) + integration (isolation/RBAC/publish/rollback/join/config) + dedicated `TENANT_ISOLATION_TESTS` gate | **not started (new surfaces), partial precedent (isolation)** | RLS integration test pattern already exists and is proven (`LoyaltyRlsIntegrationTests.cs` et al., CURRENT_STATE §2) — the pattern to extend, not invent. Nothing yet exists for config validation, versioning, or theme. |
| 31 | Migration safety (apply → test → rollback plan per DB stage) | **already the house style** | `database-engineer`'s existing migration discipline (documented in `.claude/docs/database-schema.md`) already follows this; no gap, just an ЕТАП to keep following. |

## 3. Proposed staged task breakdown (NOT registered — proposal only)

Candidate `TASK-527` onward (current max used anywhere in `.claude/logs/` is `TASK-525`; `TASK-526`
is this audit). Grouped into stages that mirror the ЕТАП list above; dependency order is
stage-by-stage, not strictly task-by-task within a stage. Agent column follows CLAUDE.md's
Agent→Task mapping table. **The orchestrating session registers these in
`.claude/tasks/mobile-roadmap.md` after user review — this document does not create them.**

### Open decisions to resolve before implementation starts (flag to the user first)

1. **`UserTenant` shape (blocks Stage A/E below).** Keep "joining a retailer" coupled to
   `LoyaltyMembership` as-is (cheaper, matches what's shipped, means a tenant without loyalty
   enabled can't have consumer-app members at all), or introduce a separate, generic
   `ConsumerTenantMembership`/`UserTenant` that `LoyaltyMembership` optionally extends (matches the
   spec literally, more schema/migration work, touches every existing network-discovery/join call
   site). This is a product/architecture tradeoff, not an implementation detail.
2. **API versioning scope (blocks Stage F).** Version only the new consumer-platform endpoints
   under `/api/v1/`, or retroactively alias/version the entire existing API surface. The spec asks
   for `/api/v1/` on new endpoints only; broadening it is a bigger, separate decision.
3. **Audit log reuse (Stage E).** Extend the existing generic `ActivityLog` table for the new
   config/publish/rollback/feature-flag events, or give the consumer-platform its own audit table.
   Leans toward reuse per evidence in §2 ЕТАП 17, but worth a one-line confirmation before
   `database-engineer` starts.

### Stage A — Multi-tenant & identity foundation (ЕТАП 1-2)

| Task | Scope | Agent | Depends on |
|---|---|---|---|
| TASK-527 | Add `Tenant.LogoUrl`/`UpdatedAt` columns + migration | `database-engineer` | none |
| TASK-528 | Introduce a centralized `ITenantContext`/`ICurrentTenantService` (Application layer), migrate controllers off duplicated `ResolveTenantId()` helpers | `backend-developer` | none |
| TASK-529 | Implement the chosen `UserTenant` shape from open decision #1 above (schema + migration) | `database-engineer` | open decision #1 |
| TASK-530 | Wire `LoyaltyMembership`/network-join/network-discovery call sites onto the new membership shape | `backend-developer` | TASK-529 |

### Stage B — Mobile Configuration domain & API (ЕТАП 3-4)

| Task | Scope | Agent | Depends on |
|---|---|---|---|
| TASK-531 | `MobileConfiguration`/`MobileConfigurationVersion`/`MobileTheme` entities + migration + RLS | `database-engineer` | TASK-528 |
| TASK-532 | Config JSON validation service (whitelist-based) + Draft CRUD application service | `backend-developer` | TASK-531 |
| TASK-533 | Author canonical `/contracts/mobile-config.schema.json`, keep in lockstep with TASK-532's validator | `backend-developer` | TASK-532 |
| TASK-534 | `GET /api/v1/mobile/config` (published-only, ETag/cache) — the mobile side's top blocking contract | `backend-developer` | TASK-532, TASK-533 |

### Stage C — Retailer Admin surface: Theme, App Builder, Pages, Navigation (ЕТАП 5-9)

| Task | Scope | Agent | Depends on |
|---|---|---|---|
| TASK-535 | Expand `/consumer-app` into full Retailer Admin shell (Design/Pages/Navigation/Features/Versions nav, no builder logic yet) | `frontend-developer` | TASK-534 |
| TASK-536 | Theme domain validation + `PUT` endpoints (whitelist palette/radius/spacing) | `backend-developer` | TASK-531 |
| TASK-537 | Theme Editor UI with live preview | `frontend-developer` | TASK-536, TASK-535 |
| TASK-538 | Block Registry (server-owned catalog of block types + definitions) | `backend-developer` | TASK-532 |
| TASK-539 | App Builder foundation — drag & drop canvas writing to Draft version | `frontend-developer` | TASK-538, TASK-535 |
| TASK-540 | Block Property Editor generated from block definitions | `frontend-developer` | TASK-538, TASK-539 |
| TASK-541 | Page Builder (Home fully block-driven; Promotions/Catalog/News scaffolds) | `frontend-developer` + `backend-developer` | TASK-539, TASK-540 |
| TASK-542 | Navigation Builder (min 2/max 5, permitted-icon whitelist, backend validation) | `backend-developer` + `frontend-developer` | TASK-536 |

### Stage D — Feature flags, Draft/Preview/Publish, Versioning (ЕТАП 10-13)

| Task | Scope | Agent | Depends on |
|---|---|---|---|
| TASK-543 | Consumer-session-aware Feature Flags domain (works for cross-tenant `ConsumerAccount` sessions, unlike `RequireModuleAttribute`); stub the subscription-plan hook per ЕТАП 18 | `backend-developer` | TASK-531 |
| TASK-544 | Generalize Draft→Preview→Validate→Publish beyond `Banner` (atomic transaction, schema gate) | `backend-developer` | TASK-532 |
| TASK-545 | Version History + Rollback (clone-forward, never delete) | `backend-developer` | TASK-544 |
| TASK-546 | Version History UI + rollback action + autosave/unsaved-changes/publish-confirmation UX | `frontend-developer` | TASK-545, TASK-535 |
| TASK-547 | Preview API (`GET /api/v1/mobile/config/preview`, staff-only, draft never reaches consumers) | `backend-developer` | TASK-544 |

### Stage E — Retailer discovery, QR onboarding, audit (ЕТАП 14-17)

| Task | Scope | Agent | Depends on |
|---|---|---|---|
| TASK-548 | `GET /api/v1/retailers[/{slug}]`, `POST .../join`, `DELETE .../membership` | `backend-developer` | TASK-529/530 (open decision #1) |
| TASK-549 | QR/deep-link onboarding (`/join/{slug}` web fallback + mobile deep-link contract) | `backend-developer` + `frontend-developer` | TASK-548 |
| TASK-550 | Audit log wiring for config/publish/rollback/feature/role events (reuse vs. new table per open decision #3) | `database-engineer` + `backend-developer` | TASK-544, TASK-545, open decision #3 |

### Stage F — Cross-cutting: API contract, docs, testing, subscription-readiness (ЕТАП 16, 18, 27-31)

| Task | Scope | Agent | Depends on |
|---|---|---|---|
| TASK-551 | API versioning rollout for new endpoints per open decision #2 | `backend-developer` | open decision #2 |
| TASK-552 | Publish committed `openapi.json` generation + `docs/integration/MOBILE_API.md` + `docs/integration/CHANGELOG.md` conventions | `backend-developer` + `documentation-writer` | TASK-534 onward, ongoing |
| TASK-553 | Consolidate RLS/isolation coverage into one explicit, CI-gating `TENANT_ISOLATION_TESTS` suite, extended to the new tables | `backend-developer` | TASK-531, TASK-538, TASK-548 |
| TASK-554 | Security review of the full consumer-platform surface (RBAC on Retailer Admin, upload hardening reusing the Фаза 4 two-layer guard pattern, rate limiting on join/publish, output encoding for admin-authored theme/block/banner text) | `security-reviewer` | Stages A-E substantially complete |
| TASK-555 | `SubscriptionPlan → Features` architecture ADR (no billing implementation); confirm TASK-543's flag engine already satisfies the hook | `project-architect` | TASK-543 |

**Total proposed: 29 candidate tasks (TASK-527 through TASK-555) across 6 stages**, plus 3 open
decisions to resolve first. Stage A should start first regardless of decision timing (TASK-527/528
have no dependency on the open `UserTenant` question); Stage B can start in parallel once TASK-528
lands, since the config domain doesn't depend on how retailer membership ends up shaped. Stages C/D
depend on B. Stage E depends on Stage A's open decision. Stage F is continuous/ongoing rather than
a single gate at the end, per the spec's own §19/§32 "incremental, runnable after every stage"
principle — TASK-552/553 in particular should be revisited after every stage lands, not deferred
to the very end.
