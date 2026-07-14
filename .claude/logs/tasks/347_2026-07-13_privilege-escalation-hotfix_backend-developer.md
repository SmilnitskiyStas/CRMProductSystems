# TASK-347 — Privilege-escalation hotfix (security review of ADR-020/TASK-346)

**Agent:** backend-developer (continuation — previous session hit its limit mid-task)
**Status:** done
**Build:** 0 errors, 0 warnings (project code)
**Tests:** 800/800 green (was 779 after TASK-346; +21 new tests this task)

## Context

`.claude/logs/handoffs/346-to-347_security-reviewer.md` flagged that TASK-346's
`RoleOrCapabilityRequirement`/`AppPolicies` OR-policies let a low-rank user holding a
`users.manage` TenantRole capability clear the coarse `[Authorize(Policy=...)]` gate on
`UsersController.Invite/Update/Deactivate` with **no server-side rank check at all** —
e.g. a `staff`-rank (rank 0) capability holder could invite a brand-new `enterprise_admin`,
or `store_manager` could already self-promote via `Update` even pre-ADR-020.

## What the previous agent had already done (verified, not re-done)

`UserService.InviteAsync/UpdateAsync/DeactivateAsync` signatures gained `Guid actingUserId`
and each does a `RoleRank` check: actor must strictly outrank the target's current role, a
newly-requested role can never exceed the actor's own rank, and self-`Update` blocks any
role change outright (even a demotion). Same pattern threaded through
`SupplierCabinetService.InviteStaffAsync/DeactivateStaffAsync` (now also take
`actingUserId`) and all call sites (`UsersController`, `SupplierCabinetController`).
`GenericIntegrationSecrets` extends secret-masking from prro/vchasno-only to every known
integration service. Domain/Application/Infrastructure/Api all compiled clean; only
`ShelfGuard.Tests` had 33 compile errors from two files still calling the old (shorter)
signatures.

## What this session did

1. **Fixed the 33 compile errors** — `UserServicePasswordTests.cs` (1 call site) and
   `SupplierCabinetServiceTests.cs` (6 test methods, 13 call sites) updated to the new
   signatures. Where a test's actingUser rank didn't matter to what it was actually
   testing (e.g. password-policy rejection short-circuits before the rank check even
   loads an acting user), no extra mock setup was needed — verified case by case rather
   than mechanically padding every call.

2. **Found and fixed a live regression the new RoleRank gate introduced**: `RoleRank` has
   no entry for `supplier_admin` (`GetValueOrDefault(role, 0)` → rank 0, same default as
   `staff`). Supplier cabinet (ADR-016) is a **flat, single-role domain** — every supplier
   tenant user, owner or invited teammate alike, is `role="supplier_admin"`
   (`AppRoles.cs`, `TenantAdminService`, `ProviderService` all confirm this — no other role
   is ever assigned there). `DeactivateAsync`'s new `actingRank <= targetRank` gate (and
   `UpdateAsync`'s equivalent) therefore always evaluated `0 <= 0 → true` for any
   supplier_admin-on-supplier_admin action, i.e. **always denied**. This isn't
   theoretical: `SupplierCabinetController.DeactivateStaff` →
   `SupplierCabinetService.DeactivateStaffAsync` → `UserService.DeactivateAsync` is a live,
   wired endpoint — every "deactivate teammate" call in every supplier tenant would have
   started failing with "You do not have permission…" after this hotfix shipped.
   `InviteAsync` happened to still work by coincidence (`0 > 0` is false, so the
   "requested role above actor's own rank" check never trips for two rank-0 peers) — only
   the `<=` ("must be strictly higher") gates were broken.
   Fix: added `UserService.IsExemptFromOutrankGate(actingRole, otherRole)` — true only when
   both sides are `supplier_admin` — and gated the `<=` checks in `DeactivateAsync` and
   `UpdateAsync` behind `!IsExemptFromOutrankGate(...)`. Confirmed this reopens no
   escalation path: `supplier_admin` is absent from every `AppPolicies` role array
   (including all the `*OrCapability` ones), so it can never reach `UsersController`'s
   Invite/Update/Deactivate in the first place — `SupplierCabinetService` is the only
   caller, already tenant-scoped.

3. **Added the missing exploit-chain tests** (none existed — grepped for
   "escalat"/"outrank"/"TASK-347", zero hits before this session):
   - `backend/ShelfGuard.Tests/Users/UserServiceEscalationTests.cs` (12 tests) — Invite
     above own rank rejected / at-or-below succeeds; self-Update role-change always
     blocked (even demotion) while non-role fields still apply; Update assigning a role
     above own rank rejected; Update/Deactivate on an equal-or-higher-rank target
     rejected, lower-rank succeeds; self-Deactivate rejected; both supplier_admin-peer
     regression tests locking in fix #2 above.
   - `backend/ShelfGuard.Tests/Integrations/IntegrationServiceTests.cs` (9 tests, new
     file — `IntegrationService`/`GenericIntegrationSecrets` had zero test coverage
     before this task) — GET masks the secret field for all 5 generic services
     (claude/telegram/resend/webhook/iot, last-4-chars preserved), not-configured-yet and
     unknown-service paths, and PUT round-trip semantics (masked placeholder keeps the
     stored secret; a real new value overwrites it).

4. **Re-reviewed the three methods' logic against the intended design** (task point 5):
   confirmed (a) actor needs strictly-higher rank than the target's *current* role, (b) a
   newly-requested role can never exceed the actor's own rank, (c) self-Update fully blocks
   role changes — all correct, plus the supplier_admin fix above.

## Verification

- `dotnet build backend/ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, not touched this task).
- `dotnet test backend/ShelfGuard.sln` — 800/800 green, run twice for confirmation.

## Not done / out of scope

- `.claude/tasks/current.md` not updated — not part of this task's explicit deliverables
  list; leaving for whoever closes out the v4.5 sprint entry.
- No further ADR-020 points remain open per the 346-to-347 handoff besides this review.

Handoff: `.claude/logs/handoffs/347-to-348_security-reviewer.md`.
