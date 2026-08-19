# Review: Security Review — Consumer Platform Surface (TASK-554)

**Date:** 2026-08-18
**Reviewer:** security-reviewer
**Task:** Security review of the multi-tenant consumer app-builder surface built across Stage 6
(TASK-527–553): backend MobileConfig domain/API, Retailer Admin web UI, QR/join fallback page.
**Result:** approved — one real gap found and fixed directly; no critical/high findings; three
already-tracked backlog items reconfirmed accurate.

## Scope reviewed

Backend: `MobileConfigController`, `MobileConfigDraftController`, `MobileConfigPublishController`,
`MobileConfigVersionsController`, `MobileConfigPreviewController`, `MobileThemeController`,
`MobileBlocksController`, `RetailersController`; `ShelfGuard.Application/Features/MobileConfig/`;
`MobileConfiguration*`/`MobileTheme` entities + RLS migration.
Frontend: `frontend/features/consumer-app/`, `/consumer-app/{design,pages,navigation,features,
versions}`, `frontend/app/[locale]/join/[slug]/page.tsx`.

Every finding below was checked directly against current source (validators, controllers,
migrations, tests) — not inferred from prior task logs.

## 1. RBAC on Retailer Admin — PASS

All 5 admin routes (`design`/`pages`/`navigation`/`features`/`versions`) use the identical
`useMe()` + `hasRole(AT_LEAST_ENTERPRISE_ADMIN)` + `AccessDenied` + null-while-loading guard —
confirmed by reading all 5 page files, not just one.

Backend policy confirmed per controller:
- `MobileConfigDraftController`, `MobileConfigPublishController`, `MobileConfigVersionsController`,
  `MobileThemeController`, `MobileBlocksController` — all carry class-level
  `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`, no `[AllowAnonymous]` anywhere on them.
- `MobileConfigController` — intentionally `[AllowAnonymous]` (published-only consumer read,
  matches `ConsumerContentController`'s "discover before joining" precedent). Draft/archived
  content is structurally unreachable through it (verified in `MobileConfigPublishedReadService`
  and confirmed by the passing "never leaks draft" test suite).
- `MobileConfigPreviewController` — verified the TASK-547 claim is technically correct: ASP.NET
  Core's authorization middleware skips the `[Authorize]` check for an endpoint entirely if *any*
  `IAllowAnonymous` metadata is present on it, so an action-level `[Authorize]` would NOT override
  a controller-level `[AllowAnonymous]`. Making Preview its own controller with its own
  `[Authorize(Policy = AtLeastEnterpriseAdmin)]` avoids that trap correctly. Checked every other
  controller in the surface for the same risk — none of the other 7 has a controller-level
  `[AllowAnonymous]`, so this was the only place the trap could have applied, and it didn't.
- `RetailersController` — class-level `[Authorize]` (ConsumerAccount session), with one
  action-level `[AllowAnonymous]` override (`GetRetailerPublic`, TASK-549's QR fallback) — the
  standard, correct ASP.NET Core pattern (action-level `[AllowAnonymous]` legitimately overrides
  class-level `[Authorize]`, the reverse of the trap above).

No IDOR: every admin controller resolves the tenant exclusively from `ITenantContext` (JWT claim),
never from client-supplied body/query (`SaveMobileConfigDraftRequest` carries no `tenantId`).
`MobileConfigVersionsController.Rollback` scopes `GetVersionByIdAsync(tenantId, versionId)` and
returns 404 for a foreign-tenant version id (read directly in `MobileConfigPublishService.
RollbackAsync`) — a tenant admin cannot roll back or read another tenant's version by guessing its
GUID.

## 2. Upload hardening — confirmed, nothing to harden

Grepped the entire MobileConfig/Retailers surface for `IFormFile`/upload code — zero matches,
independently reconfirming the brief's own finding. `logoUrl` is the only URL-shaped field
(`MobileTheme.LogoUrl`); `MobileThemeValidator.ValidateLogoUrl` requires it be a well-formed
absolute `http`/`https` URL and rejects anything else (including `javascript:`/`data:` schemes).
It is never fetched or proxied server-side anywhere — only stored as a string and returned as JSON;
the browser fetches it directly via `<img src>` on the client. No SSRF path exists. Verdict:
genuinely no upload path in this surface, correctly documented rather than skipped.

## 3. Rate limiting — real gap found and fixed

`GET /api/v1/retailers/{slug}/public` (`RetailersController.GetRetailerPublic`) is the one action
on that controller with no `ConsumerAccount` JWT and no per-account accountability — every
failure mode (unknown/inactive/loyalty-less/paused slug) collapses to the same 404, making it the
single most attractive target on the surface for slug enumeration/scraping. It had no rate limit,
unlike the codebase's existing anonymous-endpoint convention (`auth-login`, `auth-refresh`,
`public-leads`, `auth-forgot-password`, all in `Program.cs`).

**Fixed directly** (small, well-understood, uses the existing pattern):
- `backend/ShelfGuard.Api/Program.cs` — new `retailer-public-lookup` policy, same per-IP
  `FixedWindowRateLimiter` shape as the existing four, 20 req/min/IP (looser than `public-leads`'
  5/min since this is a read-only lookup and legitimate bursts from a shared IP — in-store wifi,
  CGNAT — are plausible for a QR-scan flow).
- `backend/ShelfGuard.Api/Controllers/RetailersController.cs` — `[EnableRateLimiting(
  "retailer-public-lookup")]` on `GetRetailerPublic`.

**Deliberately left unchanged** (judgment call, documented not silently skipped):
`POST /{slug}/join` (consumer-authenticated) and `POST /api/v1/mobile/config/publish`
(staff-authenticated). Checked the rest of the API: rate limiting in this codebase is reserved for
*anonymous* endpoints with no other accountability — every authenticated business endpoint across
all ~74 controllers, including the pre-existing `ConsumerLoyaltyController.Join` that
`RetailersController.Join` aliases, follows the same no-per-endpoint-limit convention. Adding a
limit to only these two authenticated endpoints would invent a new, inconsistent convention rather
than apply the existing one. The absence of any platform-wide authenticated-endpoint throttle is a
real, pre-existing characteristic of the whole API (confirmed: no global limiter is configured in
`Program.cs`), not something Stage 6 introduced — out of this review's scope to fix wholesale.

**Verification of the fix:** `dotnet build` 0 errors/0 warnings (new); `dotnet test` 1685/1685
passed, 0 skipped (matches the pre-fix baseline exactly — no regressions), including the
live-Postgres RLS suite.

## 4. Stored-XSS / output encoding — PASS, verified for real

Grepped `frontend/features/consumer-app/` and `frontend/app/[locale]/join/` for
`dangerouslySetInnerHTML`, `innerHTML`, `document.write`, `srcDoc`, `iframe`, `eval` — zero matches
anywhere. All admin-authored text (theme fields, navigation labels, block props) renders exclusively
through JSX text interpolation (React-escaped) or as attribute values (`<img src>` — not an
HTML-execution context; `javascript:`/malformed `src` values are inert on `<img>` in every modern
browser). `AppBuilderCanvas.tsx` doesn't even render live block-instance content today — it only
shows block type/category chrome, so no admin-authored text reaches that screen's live preview at
all yet. `BlockPropertyEditor.tsx`'s one `backgroundImage` reference is a static, hardcoded SVG
data-URI (select-arrow icon), not user data.

Backend side confirmed directly in source: `MobileConfigController.GetConfig` and
`MobileConfigPreviewController.Get` both return `Content(documentJson, "application/json")` — raw
JSON, `application/json` content type, never pre-rendered as HTML. No stored-XSS path exists in the
code that exists today. (Consumer mobile-app rendering is out of scope, per the brief — different,
separately-owned codebase.)

## 5. Known backlog items — reconfirmed against current code, not just the log

- **TASK-540** (block `props` bounds/required not enforced server-side): still true —
  `MobileConfigValidator.ValidateBlocks` only checks `props` is a JSON object; `BlockRegistry`'s
  per-block-type typed/bounded schemas (e.g. `imageUrl: Url, maxLength`) are not cross-checked.
  Severity: low. Only an `AtLeastEnterpriseAdmin` of the *same* tenant can write this, and it stays
  JSON (never becomes HTML/script) — worst case is a malformed value breaking that tenant's own
  consumer-app rendering, a data-integrity gap, not an injection vector.
- **TASK-542** (`navigation[].label` no server-side `maxLength`): still true —
  `MobileConfigValidator`'s `RequireString` for `label` only checks non-empty-string, despite
  `contracts/mobile-config.schema.json` declaring `maxLength: 30`. Same severity/reasoning as
  TASK-540 — same-tenant-admin-only, UI-robustness concern, not a security hole.
  `NavigationBuilderSection.tsx` already enforces 1–30 client-side as a stricter, safe-direction
  guard.
- **TASK-551** (`RetailersController.GetRetailers` N+1): still true — read
  `LoyaltyService.GetAvailableNetworksAsync` directly: loads every active tenant platform-wide via
  `_tenants.GetAllAsync`, then opens a separate `ITenantSessionOverride` and 2 more queries
  (loyalty settings + locations) per tenant in a `foreach`. **New observation folded into this
  finding:** this endpoint also has no rate limiting (consistent with §3's finding — it's an
  authenticated, not anonymous, endpoint) and no caching, so its cost scales linearly with the
  total platform-wide count of active+loyalty-enabled tenants on *every* call from *every* consumer
  session, with no ceiling. Not urgent today at current tenant counts, but the amplification
  characteristic is worth carrying forward explicitly in TASK-551 rather than treated as pure
  latency debt — the eventual fix (batch-load tenants/settings/locations instead of the per-tenant
  round trips) is the same fix that caps the request's cost, so no separate task is needed; noting
  it here so it isn't rediscovered as a fresh "DoS-adjacent" finding later.

No new critical/high-severity issue was found in this area — all three remain correctly categorized
as deferred hardening, not live bugs.

## 6. Additional checks performed (beyond the brief's checklist)

- **RLS independently re-verified from the migration SQL itself**, not just the task log: all three
  new tables (`mobile_configurations`, `mobile_configuration_versions`, `mobile_themes`) carry
  `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + the canonical `tenant_isolation`
  (`NULLIF(current_setting('app.tenant_id', true), '')::uuid` guard) + `provider_bypass` +
  `worker_bypass` triad, read directly in
  `20260817090727_AddMobileConfigurationDomain.cs`. Confirmed `mobile_themes.TenantId` is a real
  denormalized column (not assumed) — the policy is valid.
- Confirmed `RlsCrossTenantIntegrationTests.AllForceRlsTables_...` is genuinely dynamic (queries
  `pg_catalog`/`pg_policies` at runtime, no hardcoded table list) — it automatically covers the 3
  new tables with no code change required, and it passed in this review's own fresh full-suite run
  against real Postgres.
- No other controller in the reviewed surface carries a stray `[AllowAnonymous]` (checked all 8).

## Approved for

- Production deploy: yes, for the surface as reviewed. The one real gap found (rate limiting on
  the anonymous retailer-lookup endpoint) is fixed and verified in this same task.

## Follow-up tasks registered

None. No finding in this review rose to critical/high severity. The one medium-severity gap
(§3) was small, well-understood, and fixed directly per the task brief's guidance. TASK-540,
TASK-542, and TASK-551 remain the correct, already-registered homes for the three low-severity
hardening items reconfirmed in §5 — no new TASK-554-N entries were needed.
