# TASK-322 — Legal Entities: wire User side + JWT permission overrides

**Agent:** backend-developer
**Date:** 2026-07-08
**Status:** done

## Summary

Completes the two remaining pieces of the Legal Entities feature (TASK-321 laid the schema/CRUD groundwork):

1. `LegalEntityId` wired end-to-end through Users (create/update/response), with same-tenant FK validation.
2. Per-user permission overrides now reach authorization checks via a JWT claim, so `LegalEntitiesManage`
   can grant non-admin roles access to `LegalEntitiesController` without changing their role.

## Changes

- `backend/ShelfGuard.Domain/Entities/User.cs` — added `LegalEntityId` (Guid?), `SetLegalEntity(...)`,
  and a `legalEntityId` optional param on `User.Create(...)`.
- `backend/ShelfGuard.Application/Features/Users/Dtos/UserDtos.cs` — `LegalEntityId` added to `UserDto`,
  `InviteUserRequest`, `UpdateUserRequest`.
- `backend/ShelfGuard.Application/Features/Users/UserService.cs` — injects `ILegalEntityService`;
  `InviteAsync`/`UpdateAsync` validate `LegalEntityId` belongs to the tenant via
  `BelongsToTenantAsync(tenantId, id, ct)` (Ukrainian error message on mismatch), then persist via
  `User.Create(..., legalEntityId: ...)` / `user.SetLegalEntity(...)`; `ToDto` maps it through.
- `backend/ShelfGuard.Application/Features/Auth/Dtos/AuthDtos.cs` — `AuthUserDto` gained `LegalEntityId`
  for consistency with `UserDto` (used by `/auth/me` and login response).
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs` — `ToDto` passes `u.LegalEntityId`;
  both `LoginAsync` and `RefreshAsync` now call `_jwt.GenerateAccessToken(..., user.Permissions)`.
- `backend/ShelfGuard.Application/Services/IJwtService.cs` /
  `backend/ShelfGuard.Infrastructure/Services/JwtService.cs` — `GenerateAccessToken` gained an optional
  trailing `Dictionary<string, bool>? permissions = null` parameter. Truthy keys are serialized into a
  single comma-separated claim: `new Claim("permissions", string.Join(',', permissions.Where(p =>
  p.Value).Select(p => p.Key)))`, only added when non-empty. No JSON serializer introduced.
  `GenerateImpersonationToken` intentionally left without a permissions claim — impersonation already
  grants `enterprise_admin` role, which satisfies `LegalEntityAuthorization.CanManage` via the role check.
- `backend/ShelfGuard.Infrastructure/Authorization/LegalEntityAuthorization.cs` — `CanManage` now also
  returns true when the `permissions` claim (comma-split) contains
  `TenantUserPermissions.LegalEntitiesManage`, in addition to the existing
  `AtLeastEnterpriseAdminRoles` role check.

## Call sites for `GenerateAccessToken`

Two real call sites, both updated to pass `user.Permissions`: `AuthService.LoginAsync` and
`AuthService.RefreshAsync`. `GenerateImpersonationToken` is a separate method (own claim set) and was
left as-is — see note above.

## Verify

- `dotnet build` (backend/): succeeded, 0 errors, 1 pre-existing warning (unrelated, `MarketplaceServiceTests.cs`).
- `dotnet test` (backend/): 645/645 passed, including all Auth/User/Location suites (111 in that subset).

## Reviewer notes

- JWT claim format: `permissions` claim value is a flat comma-separated list of granted permission keys
  (e.g. `legal_entities.manage,some.other.perm`). No claim added at all if the user has no true-valued
  overrides — keeps token size minimal.
- Double-check `LegalEntityAuthorization.CanManage`'s claim split doesn't false-positive on substring
  matches — it splits on `,` and does an exact `Contains` on the resulting array, so no partial-match risk.
- Impersonation tokens do not carry a permissions claim by design; if per-user overrides are ever needed
  under impersonation, that's a separate follow-up.
