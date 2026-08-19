# TASK-549 — QR/deep-link onboarding (backend half)

**Status:** done (backend portion only — frontend web fallback page is a separate follow-up)
**Agent:** backend-developer
**Date:** 2026-08-18

## What was built

`GET /api/v1/retailers/{slug}/public` — a new, distinct, `[AllowAnonymous]` endpoint on the
existing `RetailersController.cs`, resolving a retailer slug to minimal public display info for
the QR/deep-link onboarding web fallback page (`https://app.domain/join/{slug}`, to be built next
by a frontend-developer). This is the contract that page needs to render "Join {name}" before the
visitor has the app or a consumer session.

**Response shape** (`RetailerPublicInfoDto`, `backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs`):

```json
{ "name": "Свіжий Кут", "slug": "svizhyi-kut", "logoUrl": "https://.../logo.png", "joinable": true }
```

- `logoUrl` is `null` when the tenant has never uploaded one (`Tenant.LogoUrl`, TASK-527).
- `joinable` is always `true` whenever this DTO is returned at all — see the 404 policy below.
- 404 body on failure: `{ "error": "Retailer not found." }` (same structured-error shape every
  other action on this controller already uses — never a 500, never HTML).

## Anonymous-access design decision

The brief asked me to check whether `ILoyaltyService.GetNetworkBySlugAsync` (TASK-548's method
backing the existing authenticated `GET /api/v1/retailers/{slug}`) actually uses the consumer id
for anything beyond auth-gating, before deciding between "relax that endpoint's auth" vs. "add a
new anonymous method." It does not — `GetNetworkBySlugAsync(string slug, CancellationToken ct)`
takes no consumer identity at all; the auth requirement lives entirely in the controller
(`ResolveConsumerAccountId() is null → Forbid()`).

**Decision: added a new anonymous method/DTO/route rather than relaxing the existing one.** The
reason is not the service logic — it's the response shape. `GetNetworkBySlugAsync`'s DTO
(`LoyaltyNetworkSummaryDto`) carries the tenant's full shoppable-store list (`Stores`: name +
address per location) and the internal `TenantId` guid. Making the existing action
`[AllowAnonymous]` as-is would have newly exposed that store list to anyone, unauthenticated —
data this task's brief explicitly said not to leak ("no store lists beyond what's already public
via `ConsumerContentController`"; that controller requires a `storeId` as *input*, it never lists
all of a tenant's stores). So:

- `ILoyaltyService.GetNetworkBySlugAsync` / `GetRetailer` (`GET /api/v1/retailers/{slug}`) —
  **unchanged**, still `[Authorize]`, still returns the full `LoyaltyNetworkSummaryDto`.
- New `ILoyaltyService.GetPublicRetailerInfoAsync(slug, ct)` — reuses `GetNetworkBySlugAsync`'s
  exact eligibility filter (tenant active, `loyalty` module enabled,
  `LoyaltyProgramSettings.IsEnabled` not explicitly `false`) by re-deriving it directly against
  `ITenantRepository`/`ILoyaltyRepository` (does **not** call `LoadNetworkDetailsAsync` — no
  reason to load/project the store list for a DTO that never uses it), but returns the new minimal
  `RetailerPublicInfoDto` instead.
- New `GetRetailerPublic` action on `RetailersController`, `[HttpGet("{slug}/public")]` +
  action-level `[AllowAnonymous]`, which correctly overrides the controller's class-level
  `[Authorize]` for that one action only (standard ASP.NET Core behavior — same technique
  `AuthController`'s `login`/`refresh`/`2fa/verify` actions already use in this codebase; verified
  this is the safe direction — action-level `[AllowAnonymous]` beats controller-level
  `[Authorize]` — by finding that precedent already in use rather than assuming).

## 404 policy (the DoD's "your call, document it")

**Decision: unknown slug, inactive tenant, missing `loyalty` module, and a tenant that has since
paused its program (`LoyaltyProgramSettings.IsEnabled = false`) are all identical 404s** —
`{ "error": "Retailer not found." }`, no distinguishing detail. This deliberately mirrors
`GetNetworkBySlugAsync`'s existing behavior/precedent (TASK-548 decision 1) rather than inventing
a different rule for the new anonymous surface: that method's own doc already establishes "an
unknown slug, an inactive tenant, a tenant without the loyalty module, or a tenant whose
settings row has `IsEnabled = false` are all indistinguishable 404s." Reusing it here keeps this
new anonymous, less-trusted endpoint at least as conservative as the existing authenticated one —
it cannot be used to distinguish "this slug never existed" from "this retailer existed and later
deactivated/paused its program" any more precisely than the authenticated endpoint already
permits. This also directly satisfies the brief's "not a leak of which slugs exist vs. don't in a
way that aids enumeration beyond what's already true of `SlugExistsAsync`" instruction — a uniform
404 is the strictest reading of that constraint.

One consequence documented on the DTO itself: because every non-joinable case above already 404s,
`Joinable` is always `true` on any `200` response today. It's still included explicitly (rather
than dropped) so the frontend's contract is self-describing from the JSON body alone rather than
inferring meaning purely from the HTTP status code, and so a future distinct "found but
temporarily paused" state could set it `false` later without a breaking response-shape change —
that would be a product/UX call for a future task, not made here.

## Files changed

- `backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs` — added
  `RetailerPublicInfoDto`.
- `backend/ShelfGuard.Application/Features/Loyalty/ILoyaltyService.cs` — added
  `GetPublicRetailerInfoAsync` with the full design rationale in its XML doc.
- `backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs` — implementation.
- `backend/ShelfGuard.Api/Controllers/RetailersController.cs` — new `GetRetailerPublic` action;
  updated class doc to note the mixed auth posture.
- `backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs` — 5 new tests (see below).

No other file touched. `ConsumerLoyaltyController.cs` and the existing
`GET /api/v1/retailers/{slug}` action are untouched.

## Tests

Mock-based (`NSubstitute`), same convention `GetNetworkBySlugAsync`'s tests already use in this
file — no HTTP/controller test harness exists in this repo (confirmed at TASK-548/547).

`backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`, new `GetPublicRetailerInfoAsync` section
(5 tests, directly mirroring the `GetNetworkBySlugAsync` section immediately above it):
- unknown slug → 404
- inactive tenant → 404
- tenant without `loyalty` module → 404
- tenant with `loyalty` module but `LoyaltyProgramSettings.IsEnabled = false` → 404
- eligible tenant → 200 with `Name`/`Slug`/`LogoUrl` matching the tenant and `Joinable = true`

## Verification actually performed this run

- `dotnet build ShelfGuard.sln` — succeeded, 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, not touched by this task).
- `dotnet test ShelfGuard.sln --no-build` — **1678 passed, 0 failed, 0 skipped** (1673 baseline
  from TASK-548 + 5 new).
- `git status --porcelain backend/` reviewed: every modified/untracked file matches TASK-548's
  already-uncommitted baseline plus this task's edits inside `LoyaltyDtos.cs`,
  `ILoyaltyService.cs`, `LoyaltyService.cs`, `RetailersController.cs`, `LoyaltyServiceTests.cs` —
  nothing outside that set.
- No browser/manual/live HTTP verification performed — build + test output only.

## Handoff to frontend-developer

Contract for the web fallback page:

- `GET /api/v1/retailers/{slug}/public` — no `Authorization` header needed/expected.
- `200` → `{ name, slug, logoUrl, joinable }` (see shape above). `joinable` is currently always
  `true` when present — safe to render the join CTA whenever the request succeeds.
- `404` → `{ error }` — render a clean "not found" state, not a crash. Do not attempt to
  distinguish *why* it 404'd (unknown vs. deactivated vs. paused) — the backend deliberately does
  not tell you, and no future field is planned to.
- After the user has the app/is logged in, joining still goes through the already-existing,
  auth-required `POST /api/v1/retailers/{slug}/join` (TASK-548, unchanged) — this new endpoint is
  strictly the pre-auth "who is this retailer" info page, not the join action itself.
