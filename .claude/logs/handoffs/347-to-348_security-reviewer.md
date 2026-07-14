# Handoff 347 → 348 (security-reviewer)

TASK-347 closed the privilege-escalation gap flagged in `346-to-347_security-reviewer.md`
(a `users.manage` TenantRole capability holder could Invite/Update/Deactivate a user of ANY
rank via `UsersController`, including above their own, because the coarse
`[Authorize(Policy=...)]` gate has no notion of RoleRank). This session finished a
previous agent's in-progress work: fixed the 2 test files left with compile errors, found
and fixed a live regression the fix itself introduced, added the missing exploit-chain
tests, and re-verified the three methods' logic. Please audit before this ships — it's a
security-critical diff that also touches a previously-untested area (supplier cabinet
staff management, `IntegrationService`).

## (а) Exact production changes, method by method

All in `backend/ShelfGuard.Application/Features/Users/UserService.cs`
(+`IUserService.cs` signatures) unless noted:

- **`InviteAsync(tenantId, actingUserId, request, inviterName, ct)`** — new
  `actingUserId` param. Loads the acting user (404 "Acting user not found." if missing/
  cross-tenant — this is a 400/generic-error path in the controller, not a distinct HTTP
  status; see UsersController mapping below). Rejects if
  `RoleRank[request.Role] > RoleRank[actingUser.Role]` (`GetValueOrDefault(role, 0)` for
  unranked roles). Runs *after* role-validity and password-policy checks, *before* the
  email-uniqueness check.
- **`UpdateAsync(tenantId, actingUserId, userId, request, ct)`** — new `actingUserId`
  param. Two branches:
  - `actingUserId == userId` (self-update): if `request.Role != target.Role`
    (`roleChanging`), unconditionally rejected — "You do not have permission to change
    your own role." — **regardless of direction**, promotion or demotion. Non-role fields
    (name, phone, store, legal entity) still apply.
  - Otherwise: loads acting user, requires `RoleRank[actingUser.Role] >
    RoleRank[target.Role]` (target's *current* role, strictly greater) — **see the
    `IsExemptFromOutrankGate` exception below**. If `roleChanging`, additionally requires
    `RoleRank[request.Role] <= RoleRank[actingUser.Role]`.
- **`DeactivateAsync(tenantId, actingUserId, userId, ct)`** — new `actingUserId` param.
  `actingUserId == userId` → unconditional reject (friendlier message than the generic
  rank path, though equal rank would also reject it). Otherwise requires
  `RoleRank[actingUser.Role] > RoleRank[target.Role]` — **see the exception below**.

**New this session — `IsExemptFromOutrankGate(actingRole, otherRole)`** (private static,
`UserService.cs`, next to the `RoleRank` dict): returns true only when **both** roles are
`"supplier_admin"`. Gates the `<=`/outrank checks in `UpdateAsync`'s else-branch and
`DeactivateAsync` (NOT `InviteAsync`'s `requestedRank > actingRank` check — see why below).
**Please specifically re-verify this is safe** — it is the one new judgment call this
session made beyond finishing the previous agent's work:

- **Why it was needed**: `RoleRank` has no entry for `supplier_admin` →
  `GetValueOrDefault(role, 0)` silently gives it rank 0, identical to `staff`. Every
  supplier-tenant user (owner and every invited teammate) is `role="supplier_admin"` —
  confirmed via `AppRoles.cs`'s doc comment, `TenantAdminService.cs:90`, and
  `ProviderService.cs:309/319` — there is no hierarchy inside a supplier tenant at all
  (finer-grained access is a completely separate system, `SupplierRoleId`/
  `SupplierPermissions`, not `Role`/`RoleRank`). Two supplier_admin peers therefore always
  compared as equal rank under the un-exempted gate, and `DeactivateAsync`'s
  `actingRank <= targetRank` (0 <= 0 → true) **always rejected**.
- **Why this is a real, not theoretical, bug**: `SupplierCabinetController.DeactivateStaff`
  (`backend/ShelfGuard.Api/Controllers/SupplierCabinetController.cs:261-271`) →
  `SupplierCabinetService.DeactivateStaffAsync` → `UserService.DeactivateAsync` is a live,
  wired production endpoint. Without the exemption, every "deactivate teammate" call in
  every supplier tenant would have started returning "You do not have permission to
  deactivate a 'supplier_admin' user." — 100% failure rate, no workaround. Not caught by
  the previous agent's `SupplierCabinetServiceTests.cs` because those tests mock
  `IUserService` entirely (never exercise real `UserService.DeactivateAsync` logic).
  `InviteAsync` happened to keep working by coincidence (`0 > 0` is false for two rank-0
  peers) — only the `<=`/"strictly higher" gates were affected, which is why the exemption
  only needed to touch `UpdateAsync`/`DeactivateAsync`, not `InviteAsync`.
- **Why it does not reopen the escalation path TASK-347 exists to close**: `supplier_admin`
  is absent from every `AppPolicies` role array, including all the ADR-020
  `*OrCapability` ones (`AppPolicies.cs` — `EnterpriseAdminOrUsersManage`/
  `StoreManagerOrUsersManage`'s base role arrays are `AtLeastEnterpriseAdminRoles`/
  `AtLeastStoreManagerRoles`, neither contains it). A supplier_admin base role therefore
  cannot reach `UsersController.Invite/Update/Deactivate` at all. The only way in would be
  holding a `users.manage` TenantRole capability — which requires `AssignTenantRoleAsync`
  to have been called on that user, which is itself `AtLeastEnterpriseAdmin`-only with no
  capability bypass (see 346-to-347 handoff §в) — no supplier_admin user can reach that
  endpoint either. `SupplierCabinetService` (already tenant-scoped via
  `target.TenantId != tenantId` checks) is the only real caller of
  Invite/DeactivateAsync for supplier_admin actors. **Residual risk**, flagging rather
  than silently dismissing: if a Provider ever manually assigned a `users.manage`-capable
  TenantRole to a supplier tenant's own `TenantRole` row and then to a supplier_admin user
  (nothing at the data layer prevents this, only the controller-level `AtLeastEnterpriseAdmin`
  gate on the assignment endpoint, which no supplier_admin can pass), that user could then
  reach `UsersController.Invite` with `request.Role = "enterprise_admin"` and the
  `IsExemptFromOutrankGate` exemption would NOT apply (request.Role isn't supplier_admin),
  so the pre-existing `requestedRank > actingRank` check (4 > 0) would still correctly
  reject it. I.e. even in this far-fetched scenario the exemption's narrow "both sides
  supplier_admin" condition means it never weakens the check when the requested/target role
  is anything other than supplier_admin.

## (б) Test-file fixes (compile errors only, no logic questions)

- `backend/ShelfGuard.Tests/Users/UserServicePasswordTests.cs` — 1 call site
  (`Invite_rejects_password_that_violates_policy`) updated to pass an arbitrary
  `actingUserId`; no mock needed since password-policy rejection short-circuits before the
  acting-user lookup.
- `backend/ShelfGuard.Tests/Marketplace/SupplierCabinetServiceTests.cs` — 6 test methods,
  13 call sites, updated to the new `InviteStaffAsync`/`DeactivateStaffAsync`/
  `IUserService.InviteAsync`/`DeactivateAsync` signatures. All mock `IUserService`, so none
  of these exercise the real `UserService` RoleRank logic — they only assert
  `SupplierCabinetService` forwards `actingUserId` correctly.

## (в) New tests this session

- `backend/ShelfGuard.Tests/Users/UserServiceEscalationTests.cs` (12 tests, new file) —
  direct exploit-chain coverage: Invite above own rank rejected / at-or-below succeeds;
  self-Update role-change blocked in both directions (promotion/demotion) while non-role
  fields still apply; Update assigning a role above actor's own rank rejected; Update/
  Deactivate against an equal-or-higher-rank target rejected (Update also asserts
  `_users.DidNotReceive().Update(...)` — no partial mutation on rejection),
  lower-rank target succeeds; self-Deactivate rejected; two supplier_admin-peer regression
  tests (`DeactivateAsync_SupplierAdminPeer_Allowed`,
  `UpdateAsync_SupplierAdminPeer_NonRoleFields_Allowed`) locking in the fix from §а.
- `backend/ShelfGuard.Tests/Integrations/IntegrationServiceTests.cs` (9 tests, new file —
  `IntegrationService`/`GenericIntegrationSecrets` had **zero** prior test coverage, not
  just missing TASK-347 cases) — `GetByServiceAsync` masks the secret field for all 5
  generic services (`claude`/`telegram`/`resend`/`webhook`/`iot`) with last-4-chars
  preserved (same convention as `PrroSecrets`/`VchasnoSecrets`), not-configured-yet →
  null/no-error, unknown-service → error; `UpsertAsync` round-trip semantics — a masked
  placeholder round-tripped back on PUT keeps the stored secret (doesn't corrupt it), a
  genuine new value overwrites it.

## (г) Logic re-verification (task's explicit ask, not just compile)

Re-read all three methods against the intended design and confirmed: (a) actor needs
strictly-higher rank than the target's *current* role — yes, `<=` gates in both
`UpdateAsync`'s else-branch and `DeactivateAsync`; (b) a newly-requested role can never
exceed the actor's own rank — yes, `InviteAsync`'s `requestedRank > actingRank` and
`UpdateAsync`'s equivalent inside `if (roleChanging)`; (c) self-`Update` fully blocks role
changes, both directions — yes, unconditional reject on `roleChanging` in the
`actingUserId == userId` branch, no rank comparison involved (correctly, since
`RoleRank[x] > RoleRank[x]` is never true anyway — the explicit block is a deliberate
belt-and-suspenders against relying on that fact). No other logic issues found beyond the
supplier_admin gap in §а.

## Verification

- `dotnet build backend/ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning.
- `dotnet test backend/ShelfGuard.sln` — 800/800 green (was 779 before this task), run
  twice.

## Suggested focus for your review

1. Confirm the `IsExemptFromOutrankGate` reasoning in §а — it's the one new judgment call.
2. `IntegrationService`/`GenericIntegrationSecrets` had zero tests before this task landed
   a security fix in that exact file — worth a closer read of `GenericIntegrationSecrets.cs`
   itself (field list correctness, `HasSecretField`) alongside the new tests.
3. Everything else in this handoff is verification of the previous agent's already-reviewed
   design (per 346-to-347), not new design surface.
