# TASK-543 — Consumer-session-aware Feature Flags domain

**Agent:** backend-developer
**Date:** 2026-08-18
**Status:** done (scope-bounded — service + attribute built and unit-tested; not wired onto any
live endpoint, per the brief's explicit instruction)

## Context loaded

`.claude/agents/backend-developer.md`, `CLAUDE.md`, `.claude/tasks/mobile-roadmap.md` TASK-543
entry (Stage 6, Stage D), `RequireModuleAttribute.cs`, `Tenant.cs` (`Modules`/`HasModule`),
`MobileConfigWhitelists.FeatureKeys`, `MobileConfigPublishedReadService.cs` +
`IMobileConfigPublishedReadService.cs` (TASK-534), `MobileConfigController.cs` (query-param
tenant transport), `ConsumerContentController.cs`/`ConsumerLoyaltyController.cs` (route-param
tenant transport, `ITenantSessionOverride` precedent), `MobileConfiguration.cs`/
`MobileConfigurationVersion.cs`, `RequireModuleFilterTests.cs` (test-harness pattern for
`ActionExecutingContext`).

## The production-safety issue this task centers on

`ConsumerContentController`/`ConsumerLoyaltyController` are already live in production, entirely
independent of the `MobileConfiguration` domain. No tenant has ever published a
`MobileConfigurationVersion` (Publish/TASK-544 doesn't exist yet), so
`MobileConfiguration.PublishedVersionId` is `null` for every tenant in existence. Defaulting a
flag check to "disabled" on that state would 403 every real consumer request in production the
instant this service is wired onto those endpoints — so every flag resolves to **enabled** unless
a tenant has actually published a config with that key explicitly `false`.

## What was built

- **`backend/ShelfGuard.Application/Features/MobileConfig/IConsumerFeatureFlagService.cs`** /
  **`ConsumerFeatureFlagService.cs`** — `IsEnabledAsync(tenantId, flagKey, ct)`. Delegates to the
  existing `IMobileConfigPublishedReadService.GetPublishedConfigAsync` (TASK-534) rather than
  duplicating its tenant-lookup/RLS-scoped-read/draft-never-leaks logic. Defaults to `true` when:
  the published-read call returns no document (tenant not found, no `MobileConfiguration` row, no
  published version yet — all covered by `documentJson is null`), the parsed document has no
  `features` object, or `features` doesn't mention the requested key. Only an explicit
  `features[flagKey] == false` in a real published document returns `false`. Throws
  `ArgumentException` for a key outside `MobileConfigWhitelists.FeatureKeys` (programming error,
  not user input).
- **`backend/ShelfGuard.Application/Features/MobileConfig/ISubscriptionPlanFeatureGate.cs`** /
  **`SubscriptionPlanFeatureGate.cs`** — ЕТАП 18 stub. `GetTenantPlanAsync(tenantId, ct)` reads and
  returns `Tenant.Plan` via `ITenantRepository`, nothing else. Documented explicitly as
  enforcing nothing; `IConsumerFeatureFlagService` never calls it.
- **`backend/ShelfGuard.Infrastructure/Authorization/RequireConsumerFeatureAttribute.cs`** —
  `[RequireConsumerFeature("loyalty")]`-style filter, deliberately named apart from
  `RequireModuleAttribute` (untouched) so the two gate types can't be confused: one is B2B module
  licensing off a `tenant_id` JWT claim, the other is per-tenant consumer-app section visibility
  with no such claim available. Resolves `tenantId` from a `{tenantId}` route segment first
  (`ConsumerContentController`/`ConsumerLoyaltyController` pattern), then a `?tenantId=` query
  parameter (`MobileConfigController` pattern); 400 if neither is present, 403 if the flag
  resolves to disabled.

### Registered in `ShelfGuard.Application/DependencyInjection.cs`

`IConsumerFeatureFlagService` → `ConsumerFeatureFlagService`,
`ISubscriptionPlanFeatureGate` → `SubscriptionPlanFeatureGate` (both scoped), under a new
`TASK-543` comment block.

### Tests added

- **`backend/ShelfGuard.Tests/MobileConfig/ConsumerFeatureFlagServiceTests.cs`** —
  `PRODUCTION_SAFETY_unconfigured_tenant_defaults_every_flag_to_enabled` (`[Theory]`, all 8 spec
  flags individually — the specific proof the brief requires), plus unknown-tenant-also-defaults,
  explicit-false-disables, unmentioned-key-still-enabled, missing-`features`-object-still-enabled,
  and unknown-flag-key-throws.
- **`backend/ShelfGuard.Tests/Authorization/RequireConsumerFeatureFilterTests.cs`** — disabled→403,
  enabled→next(), tenantId resolved from query when no route value, route takes precedence over
  query when both present, missing-both→400 with no flag lookup performed.
- **`backend/ShelfGuard.Tests/MobileConfig/SubscriptionPlanFeatureGateTests.cs`** — returns
  `Tenant.Plan`, returns `null` for an unknown tenant.

20 new tests total (13 + 5 + 2).

## Deliberate scope boundary (not an oversight)

Per the brief: `ConsumerContentController`, `ConsumerLoyaltyController`, and
`RequireModuleAttribute.cs` were **not touched**. `RequireConsumerFeatureAttribute` is built and
unit-tested in isolation only. Wiring it onto those live endpoints is explicit follow-up work for
whichever task first makes Publish (TASK-544/546) real — attaching it now, before any tenant can
publish, would 403 every real consumer request today.

## Verification

- `dotnet build ShelfGuard.sln` — **0 errors** (a locally running `ShelfGuard.Api` dev-server
  process from an earlier session was locking its own output DLLs on the first attempt; stopped it
  and rebuilt cleanly).
- `dotnet test ShelfGuard.sln --no-build` (full suite, re-run after all edits) —
  **1594/1594 passed**, 0 failed, 0 skipped (1574 baseline + 20 new).
- `git status` after all edits: only `IConsumerFeatureFlagService.cs`,
  `ConsumerFeatureFlagService.cs`, `ISubscriptionPlanFeatureGate.cs`,
  `SubscriptionPlanFeatureGate.cs` (new, inside the already-untracked `MobileConfig/` feature
  tree), `RequireConsumerFeatureAttribute.cs` (new), the 3 new test files above, and
  `DependencyInjection.cs` (already modified pre-existing for prior MobileConfig registrations —
  this task only appended 3 lines to it) changed. `ConsumerContentController.cs`,
  `ConsumerLoyaltyController.cs`, and `RequireModuleAttribute.cs` do not appear in `git status` at
  all — confirmed untouched.
