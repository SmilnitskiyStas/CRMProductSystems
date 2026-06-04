# TASK-005: RoleGuard

**Date:** 2026-06-03
**Agent:** backend-developer
**Status:** done
**Duration:** 1 session

## What was done

Implemented named authorization policies matching v1-spec.md section 3.2 permissions matrix.

## Files changed

**New — Domain:**
- `ShelfGuard.Domain/Constants/AppRoles.cs` — role name string constants + `All` list

**New — Infrastructure:**
- `ShelfGuard.Infrastructure/Authorization/AppPolicies.cs` — policy name constants, internal role arrays, `Configure(AuthorizationOptions)` static method

**New — Api:**
- `ShelfGuard.Api/Controllers/ProviderController.cs` — stub for provider endpoints with `[Authorize(Policy = AppPolicies.ProviderOnly)]`

**New — Tests:**
- `ShelfGuard.Tests/Authorization/AppPoliciesTests.cs` — 42 tests covering every policy's allowed/denied roles

**Modified:**
- `ShelfGuard.Api/Program.cs` — `AddAuthorization(AppPolicies.Configure)` + added `using ShelfGuard.Infrastructure.Authorization`
- `ShelfGuard.Tests/ShelfGuard.Tests.csproj` — added `<FrameworkReference Include="Microsoft.AspNetCore.App">` for `AuthorizationOptions` + `RolesAuthorizationRequirement`

## Policies registered

| Policy | Allowed roles |
|---|---|
| `ProviderOnly` | provider |
| `AtLeastEnterpriseAdmin` | provider, enterprise_admin |
| `AtLeastNetworkManager` | + network_manager |
| `AtLeastStoreManager` | + store_manager |
| `CanReceiveStock` | + storekeeper (NOT merchandiser) |
| `CanViewStock` | all 6 staff roles |

## Decisions made

- Role arrays are `internal static` in `AppPolicies` — same arrays used for both policy registration and tests (no duplication)
- `AppRoles` placed in Domain (pure string constants, zero framework dependency)
- `AppPolicies` placed in Infrastructure (needs `Microsoft.AspNetCore.Authorization`, which is already available via `FrameworkReference`)
- `ProviderController.GetHealth()` is the first real-use of a named policy on a spec endpoint

## Tests

- Unit tests written: yes — 42 in `AppPoliciesTests` (Theory + Fact)
- Build passes: yes — 0 errors, 0 warnings
- `dotnet test`: 68/68 passed

## Notes for next agent

Critical chain is now complete:
- TASK-001 ✅ Rename
- TASK-002 ⏳ Full v1 DB schema (blocked — awaits permission to delete POC Product entity)
- TASK-003 ✅ JWT auth
- TASK-004 ✅ TenantInterceptor (RLS)
- TASK-005 ✅ RoleGuard

Next task: TASK-006 — Products API (real v1 schema).
Requires TASK-002 first (tenants/products/categories tables must exist).
Alternatively, start TASK-009 (web auth pages) which only needs TASK-003.
