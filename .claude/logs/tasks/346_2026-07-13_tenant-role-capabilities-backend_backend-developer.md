# TASK-346 — TenantRole capability enforcement (ADR-020 points 3-8)

**Agent:** backend-developer
**Status:** done
**Build:** 0 errors, 0 warnings
**Tests:** 779/779 green (was 702 after TASK-345; +77 new tests this task)

## What was built

1. `TenantRoleCapabilities` (`ShelfGuard.Domain.Constants`) — 11 new capability constants
   (`users.manage`, `schedules.manage`, `analytics.view`, `integrations.view/manage`,
   `orders.manage`, `suppliers.view/manage`, `receipts.view`, `ai_orders.view/manage`) +
   `All` HashSet (validation) + `Groups` (specialty-grouped, backend source of truth for the
   future frontend picker). `legal_entities.manage` reuses `TenantUserPermissions.LegalEntitiesManage`
   as-is, not duplicated.
2. `RoleOrCapabilityRequirement`/`RoleOrCapabilityHandler` + `TenantRoleAuthorization.HasCapability`
   (`ShelfGuard.Infrastructure/Authorization/`) — custom `IAuthorizationHandler`, registered
   singleton in `Program.cs`. Succeeds on role match (same `IsInRole` check as `RequireRole`)
   OR JWT `capabilities` claim contains the capability.
3. 12 new named policies in `AppPolicies.cs`, each OR-ing one capability onto the EXACT
   pre-existing role array of the action(s) it replaces — zero behavior change for existing
   roles. Full list and per-controller mapping in the handoff.
4. 8 controllers migrated to per-action policies (`UsersController`, `SchedulesController`,
   `AnalyticsController`, `IntegrationsController`, `OrdersController`, `SuppliersController`,
   `ReceiptsController`, `AiOrdersController`) — class-level `[Authorize]` removed wherever any
   action needed to admit a role below the old class-level floor (7 of 8; `AnalyticsController`
   kept a class-level attribute since every action shares one identical policy).
   `LegalEntitiesController` takes the ADR-mandated other path: only
   `LegalEntityAuthorization.CanManage` was extended with a third OR branch, no policy/attribute
   changes — see handoff for the practical limitation this leaves.
5. `TenantRolesController` (`/api/tenant-roles`) — CRUD + archive + capability catalog,
   `TenantRoleService`/`ITenantRoleService` (`ShelfGuard.Application/Features/TenantRoles/`),
   all `AtLeastEnterpriseAdmin`-only, zero capability bypass.
6. `POST /api/users/{id}/tenant-role` (`UsersController.AssignTenantRole` +
   `UserService.AssignTenantRoleAsync`) — `AtLeastEnterpriseAdmin`-only. Cross-tenant
   `tenantRoleId` and archived-role assignment both rejected, both as 404/400 (never 403).
   `UserDto` gained `TenantRoleId` for the future frontend list view.
7. JWT merge: `AuthService.BuildEffectiveCapabilitiesAsync` (parallel to
   `BuildEffectivePermissionsAsync`, ADR-019), wired into both mint sites (`IssueTokensAsync` —
   covers login + 2FA verify — and `RefreshAsync`) and `GetCurrentUserAsync`.
   `JwtService.GenerateAccessToken` gained an optional `capabilities` param → comma-joined
   `capabilities` claim, same shape as `permissions`. `AuthUserDto` gained `Capabilities`.

## Judgment calls (logged for the security-reviewer to double check)

- UsersController's `users.manage` needed TWO OR-policies (`EnterpriseAdminOrUsersManage` for
  Invite/Deactivate, `StoreManagerOrUsersManage` for Update) instead of the usual 1:1
  capability-to-policy mapping, because those actions had two different pre-existing role
  floors and neither could be loosened without a regression.
- `ReceiptsController.UpdateItems` was folded into the "leave role-gated, no capability" group
  alongside Create/Receive/Cancel even though the brief's prose only named the latter three —
  it is the same write-heavy stock-mutation shape, treated as an enumeration gap in the brief
  rather than a deliberate exclusion. Conservative direction (less capability reach, not more).
- `LegalEntitiesController`: per the brief's explicit routing, only the imperative check grew a
  third OR branch. In practice a "staff"-rank (rank 0) capability holder is still 403'd by the
  controller's untouched class-level `AtLeastStoreManager` policy before that check ever runs —
  same limitation the pre-existing `legal_entities.manage`/`User.Permissions` override already
  had. Flagged, not fixed — fixing it would mean removing that class-level attribute, which the
  brief explicitly said not to do for this controller.

## Verification

- `dotnet build` clean after every substantial step (12 checkpoints total).
- `dotnet test` full suite green after every step; final run 779/779.
- `dotnet test --filter "FullyQualifiedName~Auth|FullyQualifiedName~Users|FullyQualifiedName~TenantRole"` → 213/213.
- New tests: `RoleOrCapabilityHandlerTests`, `TenantRoleAuthorizationTests` (in
  `RoleOrCapabilityHandlerTests.cs`), `LegalEntityAuthorizationTests`, extended
  `AppPoliciesTests` (per-policy role-array + capability assertions), `TenantRoleServiceTests`,
  `UserServiceTenantRoleTests`, `AuthServiceCapabilitiesTests`.
- Updated existing test constructors for the new `ITenantRoleRepository` dependency
  (`UserService`, `AuthService`) and the new `GenerateAccessToken` overload:
  `UserServicePasswordTests.cs`, `AuthServiceTests.cs`, `TwoFactorAuthTests.cs`.

## Not done (explicitly out of scope)

- ADR-020 point 9 (frontend UI) — separate agent per CLAUDE.md.
- `frontend/features/users/types.ts` `ROLE_RANK.staff = 0` — noted in TASK-345's handoff as
  frontend scope, still not done, belongs to whichever agent picks up point 9.

Handoff: `.claude/logs/handoffs/346-to-347_security-reviewer.md`.
