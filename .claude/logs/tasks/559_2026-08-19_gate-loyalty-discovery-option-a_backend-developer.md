# TASK-559 — Gate ConsumerLoyaltyController's discovery/join surface (Option A)

**Status:** done
**Agent:** backend-developer

## What changed

`backend/ShelfGuard.Api/Controllers/ConsumerLoyaltyController.cs`:
- `[RequireConsumerFeature("loyalty")]` added to `Join` only. Class doc comment extended to record
  the Option-A scope (which actions are/aren't gated and why).

`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`:
- Constructor now takes `IConsumerFeatureFlagService featureFlags` (registered in DI already —
  `ShelfGuard.Application/DependencyInjection.cs:170`, no change needed there).
- `GetAvailableNetworksAsync` now excludes any tenant where
  `_featureFlags.IsEnabledAsync(tenant.Id, "loyalty", ct)` resolves `false`, checked **before** the
  existing `_tenantScope.ExecuteAsync(...)` settings/store load — a disabled tenant skips that
  second per-tenant round trip entirely instead of paying for both.

No changes to `RetailersController.cs`, `RequireConsumerFeatureAttribute.cs`, or
`IConsumerFeatureFlagService`/`ConsumerFeatureFlagService.cs` — confirmed via `git status`.
`GetMemberships`/`GetCode`/`SetPreferredStore`/`GetHistory` carry no new gate logic; verified by
reading each (not assumed) that all four already structurally require an existing
`LoyaltyMembership` row (`GetMembershipByTenantConsumerAsync` returning null → 403/404) before
returning any data, which is what makes "existing members keep access" hold without new code.

## N+1 note (per task instructions — flagged, not fixed)

`GetAvailableNetworksAsync` already paid one `ITenantSessionOverride` round trip per candidate
tenant before this task (pre-existing pattern, not introduced here). This change adds one more
`IsEnabledAsync` call per candidate tenant — but since it runs *first* and `continue`s on a
disabled tenant, it actually **saves** the tenant-scoped round trip for excluded tenants rather
than stacking on top of it. Not optimized into a bulk lookup; acceptable per TASK-559 scope, same
judgment call TASK-551 made for a similar case.

## Tests

1. `backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs` — constructor updated with a
   `IConsumerFeatureFlagService` substitute defaulting `IsEnabledAsync` to `true` (keeps every
   pre-existing `GetAvailableNetworksAsync` test passing unchanged, mirroring the real service's
   own default-enabled contract). Three new tests: excludes a flagged-disabled tenant, includes a
   tenant with no explicit stub (default-enabled path), and pins that a disabled flag skips the
   `GetSettingsAsync` tenant-scoped lookup (the N+1 mitigation).
2. `backend/ShelfGuard.Tests/Infrastructure/LoyaltyJoinRlsIntegrationTests.cs` — `BuildLoyaltyService`
   helper updated with a default-enabled `IConsumerFeatureFlagService` stub (JoinAsync itself never
   consults the flag — only the controller attribute does).
3. `backend/ShelfGuard.Tests/Authorization/ConsumerLoyaltyControllerFeatureGateTests.cs` (new) —
   reflection-level pin: `Join` carries `RequireConsumerFeatureAttribute("loyalty")`;
   `GetMemberships`/`GetNetworks`/`GetCode`/`SetPreferredStore`/`GetHistory` carry none.
4. `backend/ShelfGuard.Tests/Infrastructure/LoyaltyFeatureGateRlsIntegrationTests.cs` (new) — real
   Postgres, real `LoyaltyService` + real `ConsumerFeatureFlagService`/`MobileConfigPublishedReadService`
   + real repositories/`TenantSessionOverride`, `rls_audit_test_role` session, same rigor as
   TASK-558's `ConsumerContentFeatureGateRlsIntegrationTests`. Five tests:
   - `PRODUCTION_SAFETY_tenant_with_zero_MobileConfiguration_activity_passes_the_join_gate` — real
     `RequireConsumerFeatureFilter` against a tenant with no `MobileConfiguration` row → `next()`
     called, 200 path.
   - `Explicit_false_in_a_published_config_returns_403_through_the_real_join_gate` — published
     `features.loyalty:false` → real filter returns 403 `{"error":"Feature not enabled"}`.
   - `PRODUCTION_SAFETY_GetAvailableNetworksAsync_includes_tenant_with_zero_MobileConfiguration_activity`
     — zero-activity tenant still appears in the real `GetAvailableNetworksAsync` result.
   - `GetAvailableNetworksAsync_excludes_tenant_with_published_features_loyalty_false` — gated tenant
     excluded, a control tenant (also `loyalty` module, no config) still included.
   - **`OptionA_existing_member_keeps_full_access_after_tenant_later_disables_loyalty_discovery`** —
     THE Option-A-vs-B proof: a consumer joins via the real `JoinAsync` while the flag is still
     (implicitly) enabled → real membership row persisted; the tenant then publishes
     `features.loyalty:false`; asserts (a) the tenant now drops out of `GetAvailableNetworksAsync`,
     (b) a brand-new `Join` attempt is rejected 403 by the real filter, **and** (c) the *same*
     already-joined consumer's `GetMembershipsForConsumerAsync`/`GetConsumerCodeAsync`/
     `GetHistoryAsync`/`SetPreferredStoreAsync` (against a seeded shoppable `Location`) all still
     succeed with no error — proving discovery/join are cut off while existing access is not, which
     is exactly what distinguishes Option A from the rejected Option B.

## Verification

- `dotnet build ShelfGuard.sln` — succeeded, 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, not touched by this task).
- `dotnet test ShelfGuard.sln` (full suite, `--no-build`) — **1708/1708 passed, 0 failed, 0
  skipped** (real DB was reachable — TASK-558 baseline was 1694; +14 new tests here, all ran for
  real, none soft-skipped).
- Targeted run of the two new test files — **11/11 passed**, all 5 real-Postgres
  `LoyaltyFeatureGateRlsIntegrationTests` individually confirmed passing, including the
  Option-A-proof test above (5 s).
- `git status` confirms changes limited to `ConsumerLoyaltyController.cs`, `LoyaltyService.cs`,
  `LoyaltyServiceTests.cs`, `LoyaltyJoinRlsIntegrationTests.cs`, and the two new test files. No
  other backend file touched.

All claims above are exactly what these commands produced — no live/authenticated HTTP request
against a running API was made or claimed.
