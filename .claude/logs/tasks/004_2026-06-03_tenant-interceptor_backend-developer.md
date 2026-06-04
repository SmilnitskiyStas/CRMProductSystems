# TASK-004: TenantInterceptor (RLS middleware)

**Date:** 2026-06-03
**Agent:** backend-developer
**Status:** done
**Duration:** 1 session

## What was done

Implemented `TenantConnectionInterceptor` — a `DbConnectionInterceptor` that fires on every
connection open (including pool checkout) and executes `SET app.tenant_id / SET app.role`
so PostgreSQL RLS policies activate automatically for authenticated requests.

## Files changed

**New — Infrastructure:**
- `ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs`
- `ShelfGuard.Infrastructure/AssemblyInfo.cs` — `[InternalsVisibleTo("ShelfGuard.Tests")]`

**New — Tests:**
- `ShelfGuard.Tests/Infrastructure/TenantConnectionInterceptorTests.cs` — 13 test cases

**Modified:**
- `ShelfGuard.Infrastructure/ShelfGuard.Infrastructure.csproj` — added `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for `IHttpContextAccessor`
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — `AddHttpContextAccessor()`, `AddSingleton<TenantConnectionInterceptor>()`, `AddDbContext` now uses `(sp, options) => options.AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>())`
- `ShelfGuard.Tests/ShelfGuard.Tests.csproj` — added `ProjectReference` to Infrastructure

## Decisions made

- `DbConnectionInterceptor` (not `DbCommandInterceptor`): fires on connection open/pool-checkout, which is the correct point to set session variables
- Role validated against a static whitelist of 7 known roles — rejects any value that isn't an exact match (injection protection)
- `tenantId` validated via `Guid.TryParse` — rejects non-UUID values
- Provider users (no `tenant_id` JWT claim) get `SET app.role = 'provider'` only — activates `provider_bypass` RLS policy
- Unauthenticated requests (`IsAuthenticated == false`) skip all SET commands — RLS policies use `current_setting(..., true)` missing-ok mode and return no rows
- `BuildSetSql` is `internal static` for testability; `InternalsVisibleTo` exposes it to the test project

## Tests

- Unit tests written: yes — 13 tests in `TenantConnectionInterceptorTests`
- Build passes: yes — 0 errors, 0 warnings
- `dotnet test`: 26/26 passed

## Notes for next agent

TASK-005 (backend-developer): `RoleGuard` — role-based authorization attribute/policy.
- JWT role claim is mapped by ASP.NET Core to `ClaimTypes.Role` automatically by `JwtBearer`
- Standard `[Authorize(Roles = "store_manager,enterprise_admin")]` attributes work out of the box
- Need to add policy-based authorization for more granular checks (e.g. "can manage users")
- See v1-spec.md section 3.2 for the permissions matrix
