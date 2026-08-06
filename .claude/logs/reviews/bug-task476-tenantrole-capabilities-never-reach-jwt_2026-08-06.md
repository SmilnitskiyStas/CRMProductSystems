# TASK-476 defect — TenantRole capabilities (ADR-020) never reach the JWT on login or refresh; the entire role-or-capability escape hatch is currently non-functional tenant-wide

**Date:** 2026-08-06
**Severity:** high (a whole, previously-shipped authorization feature — ADR-020/TASK-345/346 — is
silently inert for every tenant; not a tenant-isolation/security *leak*, the opposite: capability
grants a tenant admin believes they've made are silently never honored)
**Task:** found as a byproduct of TASK-476 (E2E acceptance of Фаза 4 post-campaign audience
analysis) while trying to construct item 13's test scenario — **not** a Фаза 4 regression, not
caused by TASK-471/472/473/474/477's own code. Pre-existing, unrelated platform bug.
**Status:** open — not fixed (QA reports, does not fix, per this task's brief). Blocked live
verification of TASK-476 item 13's exact scenario — see the main task log for how that was handled.

## Bug

Every user's JWT "capabilities" claim (and the "tabs" claim) is **always empty**, on **every**
password login and **every** token refresh, regardless of whether that user has a real
`TenantRoleId` assigned with a non-empty `TenantRole.Capabilities` list. The `RoleOrCapability`
authorization mechanism (`AppPolicies.cs`, 7+ policies including this codebase's own
`MarketingAnalyticsViewOrCapability`) can currently **never** be satisfied via the capability
branch through a real login — only the base-role branch ever actually grants access. Any tenant
that has configured a narrower "delegate this one capability to a sub-manager role" grant
(ADR-020's entire stated purpose) gets nothing from it.

## Root cause

`TenantConnectionInterceptor.GetSetSql()` (`backend/ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs:64-69`):

```csharp
if (user?.Identity?.IsAuthenticated != true)
    return "RESET app.tenant_id; RESET app.role; RESET app.user_id; RESET app.consumer_account_id;";
```

`/api/auth/login` (and `/api/auth/refresh`, and 2FA verify) are, by definition, unauthenticated
requests — no incoming JWT exists yet. This RESET is intentional and correct for the `users` table
lookup itself: `users`' own RLS `tenant_isolation` policy has an explicit carve-out —

```
(NULLIF(current_setting('app.tenant_id'), '') IS NULL) OR (TenantId = ...) OR (TenantId IS NULL)
```

— live-confirmed via `pg_policies`. But `AuthService.IssueTokensAsync` (called from `LoginAsync`,
`RefreshAsync`, and `VerifyTwoFactorAsync` — all unauthenticated-request code paths) also calls
`BuildEffectiveCapabilitiesAsync`/`BuildEffectiveTabsAsync`
(`backend/ShelfGuard.Application/Features/Auth/AuthService.cs:469-475,515-525`), which reads
`ITenantRoleRepository.GetByIdAsync` — a query against `tenant_roles`. That table's RLS policy has
**no** such carve-out — live-confirmed via `pg_policies`:

```
tenant_isolation: "TenantId" = (NULLIF(current_setting('app.tenant_id'), ''))::uuid
```

the standard fail-closed NULLIF guard, same shape as every other tenant table in this codebase. With
`app.tenant_id` RESET to NULL (unauthenticated connection), `NULLIF(...)` is NULL, and
`TenantId = NULL` is never true in SQL — `tenant_roles` is **completely invisible** during login,
so `_tenantRoles.GetByIdAsync(...)` always returns `null`, and
`BuildEffectiveCapabilitiesAsync`/`BuildEffectiveTabsAsync` always fall through to their `[]` empty
default, **regardless of what's actually in the table.**

## Live repro (2026-08-06, dev stack, tenant "Свіжий Кут")

`manager@demo.local` (`store_manager`) has `TenantRoleId` pointing at the real `TenantRole` named
"HR" with `Capabilities = {users.manage, schedules.manage}` (pre-existing seed data, confirmed via
direct DB query). A fresh `POST /api/auth/login` for this user returns:

```json
"capabilities": [],
"tabs": [],
```

in the response **body** (not just a missing JWT claim — the DTO itself, built by the same
`BuildEffectiveCapabilitiesAsync` call, is empty). Decoded the JWT directly (HS256, header.payload
only, no signature needed to just read claims): no `"capabilities"` claim present at all. Repeated
for a second real user (`merch1@demo.local`, `merchandiser`, `TenantRoleId` pointing at a
`marketing_analytics.view`-only capability template) — same result: empty capabilities, and
consequently a 403 on every `marketing_analytics.view`-gated endpoint despite the real grant sitting
in the DB.

Contrast: `GetCurrentUserAsync` (`/api/auth/me`) calls the exact same
`BuildEffectiveCapabilitiesAsync` but on an **authenticated** request (the caller already has a
valid Bearer token, so `app.tenant_id` **is** set on that connection) — this path would correctly
compute real capabilities. So the DTO/UI *can* show correct capabilities after the fact, but the
**JWT itself**, minted at login/refresh time, never carries them — meaning every actual
authorization check (which reads the JWT's `capabilities` claim, not a fresh `/auth/me` call) is
blind to them for the token's entire lifetime.

## Why the existing test suite didn't catch it

`AuthServiceCapabilitiesTests.cs` mocks `ITenantRoleRepository` entirely
(`Substitute.For<ITenantRoleRepository>()`, `_tenantRoles.GetByIdAsync(...).Returns(role)`) — the
mock always returns whatever `TenantRole` the test wants, so these tests correctly prove
`BuildEffectiveCapabilitiesAsync`'s **own logic** is right given a correct repository answer, but
never exercise the real EF query + real RLS policy interaction that only manifests when a login
request's DB connection has been RESET. No integration test issues a real login through the real
DB for a user with a real `TenantRoleId` and checks the resulting JWT/body — same "mocked repository
hides a real RLS-interaction bug" shape as the two sibling findings from this same QA pass
(phone-matching, unknown-tokens export) — a recurring blind spot, not a coincidence.

## Impact

Every controller using the `RoleOrCapabilityRequirement`/`RoleOrCapabilityHandler` pattern
(`AppPolicies.cs`'s own summary table lists 7+: Schedules, Analytics, Integrations ×2, Orders,
Suppliers, AiOrders ×2, Users, MarketingAnalytics) has its capability-widening half silently dead.
A tenant admin who creates a `TenantRole` template and assigns it to a lower-ranked user (exactly
the documented, intended ADR-020 workflow) gets a role that behaves as if it had **no** capabilities
at all, with no error anywhere — the grant looks like it worked (the template shows the right
capabilities in the admin UI, since `GetAllForTenantAsync`/`GetById` reads are on an *authenticated*
connection) but never actually unlocks anything for its assignee.

## Expected

A user's JWT, freshly issued at login/refresh, should carry the real capabilities/tabs from their
assigned `TenantRole`, matching what `/auth/me` would independently compute for the same user.

## Suggested fix directions (not applied — flagging for a scoped follow-up, likely backend-developer + a security-reviewer pass given the auth-boundary sensitivity)

1. Give `tenant_roles`' RLS policy the same NULL-`tenant_id`-passthrough carve-out `users` already
   has. Broadest fix, smallest code change, but widens `tenant_roles`' visibility during ANY
   unauthenticated connection state tenant-wide — needs the same scrutiny KI-027/KI-028's history
   in this codebase already applies to RLS carve-outs.
2. Narrower: have `IssueTokensAsync` explicitly run the `tenant_roles` lookup on a connection/scope
   that legitimately bypasses RLS for this one query (e.g. a short `SET LOCAL app.role = 'provider'`
   bracket around just that call, mirroring how `worker`/`provider_bypass` already work elsewhere in
   this codebase) rather than loosening the table's general policy.
3. Either way: add a real integration test that performs an actual `LoginAsync`/`IssueTokensAsync`
   call through a real (or Testcontainers-backed) Postgres connection with RLS enabled, for a user
   with a real `TenantRoleId`, and asserts the resulting capabilities/tabs are non-empty — the
   category of test currently missing that would have caught this.

Needs a decision on which fix shape (RLS carve-out vs. scoped bypass) before implementation.
