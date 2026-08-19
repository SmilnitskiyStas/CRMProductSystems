# TASK-558 — Wire consumer feature flags onto ConsumerContentController

**Status:** done
**Agent:** backend-developer

## What changed

`backend/ShelfGuard.Api/Controllers/ConsumerContentController.cs`:
- `[RequireConsumerFeature("promotions")]` added to `GetPromotions`.
- `[RequireConsumerFeature("catalog")]` added to `GetCatalog`.
- Class doc comment updated to note the new gating and why banners stay ungated.
- `GetBanners`/`RecordView`/`RecordClick` — no attribute added.

No changes to `RequireConsumerFeatureAttribute.cs`, `IConsumerFeatureFlagService.cs`,
`ConsumerFeatureFlagService.cs`, or `ConsumerLoyaltyController.cs` — this task only consumes
already-built infrastructure (TASK-543) and DI registration (already present at
`ShelfGuard.Application/DependencyInjection.cs:170`, no change needed there either).

## Banners confirmation

Read `MobileConfigWhitelists.FeatureKeys` directly:
```
"loyalty", "promotions", "catalog", "coupons", "news", "receipts", "delivery", "personalOffers"
```
`"banners"` is not in this set — confirmed, `GetBanners`/`RecordView`/`RecordClick` left ungated as
scoped.

## Tests added

1. `backend/ShelfGuard.Tests/Authorization/ConsumerContentControllerFeatureGateTests.cs` —
   reflection-level pin: `GetPromotions`/`GetCatalog` carry `RequireConsumerFeatureAttribute` with
   exactly `"promotions"`/`"catalog"`; `GetBanners`/`RecordView`/`RecordClick` carry none.
2. `backend/ShelfGuard.Tests/Infrastructure/ConsumerContentFeatureGateRlsIntegrationTests.cs` — new
   RLS integration file (`[Collection("TENANT_ISOLATION_TESTS")]`, same pattern as
   `MobileConfigPublishedReadRlsIntegrationTests`), wiring the REAL `ConsumerFeatureFlagService` +
   `MobileConfigPublishedReadService` + repositories + a real anonymous-session Postgres connection,
   then invoking the REAL `RequireConsumerFeatureFilter` — not a mocked
   `IConsumerFeatureFlagService` (that's what the existing unit tests already cover). Two theories
   over `["promotions", "catalog"]`:
   - `PRODUCTION_SAFETY_tenant_with_zero_MobileConfiguration_activity_passes_the_gate` — seeds only a
     `Tenant` row, no `MobileConfiguration` row at all, asserts the filter calls `next()` and leaves
     `context.Result` null (the 200 path).
   - `Explicit_false_in_a_published_config_returns_403_through_the_real_stack` — seeds a published
     `MobileConfigurationVersion` with `features.{flagKey}=false`, asserts a 403
     `{"error":"Feature not enabled"}` `ObjectResult`.

## THE critical safety-default proof — result

Ran against a real local Postgres (`localhost:5435`, the dev DB — not soft-skipped):

```
Passed ConsumerContentFeatureGateRlsIntegrationTests.PRODUCTION_SAFETY_..._passes_the_gate(flagKey: "catalog")     [6 s]
Passed ConsumerContentFeatureGateRlsIntegrationTests.PRODUCTION_SAFETY_..._passes_the_gate(flagKey: "promotions")  [3 s]
Passed ConsumerContentFeatureGateRlsIntegrationTests.Explicit_false_in_a_published_config_returns_403...(flagKey: "promotions") [3 s]
Passed ConsumerContentFeatureGateRlsIntegrationTests.Explicit_false_in_a_published_config_returns_403...(flagKey: "catalog")    [2 s]
```

Both directions hold end to end (real filter → real service → real published-config repository →
real RLS anonymous session → real DB): an unconfigured tenant passes (200 path), an
explicitly-disabled tenant is rejected (403). This is the strongest available proof short of an
actual HTTP call — this repo has no `WebApplicationFactory` HTTP harness (see
`MobileConfigPreviewAuthorizationTests`/`RlsRoleGuardTests` remarks), so driving the real filter
against a real DB is the established pattern for this kind of end-to-end proof here.

## Open item — NOT resolved, flagging per instructions

`ConsumerLoyaltyController` is untouched (confirmed via `git status`, no entry for that file). Its
`GetNetworks` action returns a cross-tenant list with no single `tenantId` to gate at the attribute
level — gating it would require per-item filtering logic inside the service, not an attribute, which
is a different kind of change than this task's scope. Open question for a future task: should the
new `features.loyalty` consumer flag additionally gate `ConsumerLoyaltyController` (on top of its
existing `Tenant.HasModule("loyalty")` B2B module gate inside `LoyaltyService.JoinAsync`), and if so,
how does a cross-tenant list-returning action (`GetNetworks`) apply a per-tenant flag — per-item
filter, or something else? No answer implemented or guessed here.

## Verification

- `dotnet build ShelfGuard.sln` — succeeded, 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, not touched by this task).
- `dotnet test ShelfGuard.sln` (full suite) — **1694/1694 passed, 0 failed, 0 skipped** (real DB was
  reachable, so all RLS integration tests including the new ones ran for real, not soft-skipped).
- Targeted run of just the two new test files — 9/9 passed (listed above).
- `git status` confirms changes limited to `ConsumerContentController.cs` + the two new test files;
  no other backend file touched.

All claims above are exactly what these two commands produced — no live/authenticated HTTP request
against a running API was made or claimed.
