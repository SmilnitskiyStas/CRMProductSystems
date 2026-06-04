# Handoff: TASK-004 → TASK-005

**Date:** 2026-06-03
**From:** backend-developer
**To:** backend-developer
**Task:** TASK-005 — RoleGuard

## What was completed

TASK-004 done. `TenantConnectionInterceptor` wires PostgreSQL RLS automatically:
- `ConnectionOpenedAsync` / `ConnectionOpened` fire on every pool checkout
- Executes `SET app.tenant_id = '{guid}'; SET app.role = '{role}';` for authenticated requests
- Role whitelist + UUID parsing prevents SQL injection via JWT claims
- Unauthenticated requests pass through cleanly (no SET, RLS missing-ok)
- 26/26 tests pass, 0 build warnings

## What to do next

Implement `RoleGuard` — role-based authorization:

1. Create `AuthorizationPolicies` static class in `ShelfGuard.Api` with named policies that match the v1-spec.md section 3.2 permissions matrix
2. Register all policies in `Program.cs` via `AddAuthorization(options => ...)`
3. Add `[Authorize(Policy = "...")]` attributes to controllers/endpoints as needed
4. Consider a custom `[RequireRole(...)]` attribute for convenience

## Permissions matrix (from v1-spec.md 3.2)

| Capability | Min role required |
|---|---|
| All tenants / impersonation | provider |
| Tenant settings / add store | enterprise_admin |
| User management | network_manager (or above) |
| View stock / add batch | merchandiser (or above) |
| Receipts, transfers | storekeeper (or above) |
| Approve write-offs, discounts | store_manager (or above) |
| Analytics | store_manager (or above) |
| Billing | enterprise_admin (or above) |

## Role hierarchy (for policy checks)

provider > enterprise_admin > network_manager > store_manager > merchandiser / storekeeper

## Important context

- JWT bearer maps the role claim to `ClaimTypes.Role` automatically
- `[Authorize(Roles = "provider,enterprise_admin")]` syntax works out of the box
- For hierarchical "minimum role" checks, either list all allowed roles explicitly OR use a custom `IAuthorizationRequirement`
- The `store_id` JWT claim is set for users assigned to a specific store — store_managers should only see their store's data (enforced by RLS + application layer)

## Files to review

- `v1-spec.md` section 3.2 — permissions matrix
- `ShelfGuard.Api/Controllers/AuthController.cs` — example of `[Authorize]` usage
- `ShelfGuard.Api/Program.cs` — where to add `AddAuthorization(options => ...)`

## Definition of done

- Named policies registered matching v1-spec.md permissions matrix
- At least one controller uses a named policy attribute
- `dotnet build` exits 0
- Existing 26 tests pass
