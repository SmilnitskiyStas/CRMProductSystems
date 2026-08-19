# TASK-555 — SubscriptionPlan → Features architecture ADR

**Status:** done
**Agent:** project-architect
**Depends on:** TASK-543

## Scope

Documentation-only task: write an ADR formalizing the target `Tenant.Plan` → features
architecture (ЕТАП 18) that TASK-543 built a no-enforcement placeholder hook for, confirm whether
that hook already satisfies what real enforcement would need, and record the plan-tier naming
discrepancy as an explicit open item. This is the last registered task of the entire Stage 6
initiative (TASK-527–555).

## What was read

- `backend/ShelfGuard.Application/Features/MobileConfig/ISubscriptionPlanFeatureGate.cs` and
  `SubscriptionPlanFeatureGate.cs` — confirmed both are documented as a no-enforcement placeholder
  that reads and returns `Tenant.Plan` via `ITenantRepository`, called by nothing.
- `IConsumerFeatureFlagService.cs` / `ConsumerFeatureFlagService.cs` — confirmed the real
  TASK-543 flag-resolution mechanism (8 keys in `MobileConfigWhitelists.FeatureKeys`, fail-open
  default, sourced from the published `MobileConfigurationVersion.features` document) never calls
  `ISubscriptionPlanFeatureGate`.
- `ShelfGuard.Application/DependencyInjection.cs:170-171` — confirmed both
  `IConsumerFeatureFlagService`/`ConsumerFeatureFlagService` and
  `ISubscriptionPlanFeatureGate`/`SubscriptionPlanFeatureGate` are DI-registered (`AddScoped`)
  today, not merely present in source.
- `backend/ShelfGuard.Domain/Entities/Tenant.cs:41-49` (`UpdatePlan`) — confirmed the live valid
  set is exactly `basic`/`standard`/`enterprise`/`trial` (case-insensitive, lowercased on write).
- `docs/architecture/TARGET_ARCHITECTURE.md` §2 row 18 and `docs/architecture/CURRENT_STATE.md`
  §1 — confirmed the spec's ЕТАП 18 language (`START`/`BUSINESS`/`PRO`/`ENTERPRISE`, "no billing")
  and that `Tenant.Plan` is flagged there as "a ready, unused hook, not a built one."

## Decision recorded

Added **ADR-030** to `.claude/docs/decisions.md` (current max was ADR-029, confirmed unchanged
since TASK-526). Four points, matching the task brief:

1. Target architecture: `Tenant.Plan` → future plan→features mapping (not yet built) → constrains
   `MobileConfigWhitelists.FeatureKeys` → enforced inside `ConsumerFeatureFlagService.IsEnabledAsync`
   by ANDing today's config-driven result with a plan-driven check, no caller-facing contract
   change implied.
2. **Confirmed as a checked conclusion, not an assumption:** TASK-543's
   `ISubscriptionPlanFeatureGate`/`SubscriptionPlanFeatureGate` already satisfies the ЕТАП 18 hook
   as built. Both types exist, are DI-registered, and `GetTenantPlanAsync` is a live working read
   path through `ITenantRepository` to the real `Tenant.Plan` column today. A future implementer's
   work is additive only (define the mapping, inject the gate into
   `ConsumerFeatureFlagService`/a decorator, fold into `IsEnabledAsync`) — no interface, DI, or
   caller rework needed.
3. Open reconciliation item, deliberately left unresolved: `Tenant.Plan`'s
   `basic`/`standard`/`enterprise`/`trial` does not match the spec's
   `START`/`BUSINESS`/`PRO`/`ENTERPRISE`, and no mapping between them exists anywhere today.
   Recorded the two resolution options (remap `Tenant.Plan`'s values, or add an explicit
   translation layer) as a product/naming decision for whoever schedules real enforcement — not
   resolved in this ADR.
4. Explicitly stated no billing/payment implementation is in scope now or implied by this ADR;
   `Tenant.Plan` continues to be set the way it is today (provider/admin, via `Tenant.UpdatePlan`)
   until a follow-up task decides to enforce it.

## Verification

`git status` after the edit shows changes limited to `.claude/docs/decisions.md` (169 insertions,
1 deletion — the "Updated" date line). No code, migration, or other file touched, consistent with
the project-architect role's "no business code" guardrail.

## Result

ADR-030 added. TASK-543's hook confirmed sufficient as-built — no rework needed. Naming
discrepancy (`Tenant.Plan` vs. `START/BUSINESS/PRO/ENTERPRISE`) recorded as an explicit open item.
No billing/payment work implied.

**Log:** this file
**Handoff:** none — orchestrating session to mark TASK-555 done and close the Stage 6 registered
task list in `.claude/tasks/mobile-roadmap.md`.
**Next:** none (Stage 6 registered tasks exhausted; any follow-up — real plan-gating
implementation, naming reconciliation — is unregistered future work per ADR-030).
