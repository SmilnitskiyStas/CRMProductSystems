# TASK-074 — SaaS Admin Panel: Tenant Management API
**Date:** 2026-06-14
**Agent:** backend-developer
**Status:** done

## Summary
Implemented the SaaS Admin Panel Tenant Management API: DTOs, service interface, service implementation, controller, infrastructure repository, DI wiring, and unit tests.

## Files Created
- `backend/ShelfGuard.Application/Features/Admin/Dtos/AdminDtos.cs` — TenantDto, TenantUsageDto, CreateTenantRequest, UpdatePlanRequest, UpdateModulesRequest
- `backend/ShelfGuard.Application/Features/Admin/ITenantAdminService.cs` — service interface
- `backend/ShelfGuard.Application/Features/Admin/TenantAdminService.cs` — business logic implementation
- `backend/ShelfGuard.Domain/Interfaces/ITenantAdminRepository.cs` — repository interface (cross-tenant, bypasses RLS)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/TenantAdminRepository.cs` — EF Core implementation
- `backend/ShelfGuard.Api/Controllers/AdminController.cs` — 7 endpoints at /api/admin/tenants
- `backend/ShelfGuard.Tests/Admin/TenantAdminServiceTests.cs` — 4 unit tests

## Files Modified
- `backend/ShelfGuard.Application/DependencyInjection.cs` — registered ITenantAdminService
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — registered ITenantAdminRepository

## Endpoints
```
GET    /api/admin/tenants                  — 200 IReadOnlyList<TenantDto>
POST   /api/admin/tenants                  — 201 TenantDto | 409 slug taken | 400 invalid plan
GET    /api/admin/tenants/{id}             — 200 TenantDto | 404
PATCH  /api/admin/tenants/{id}/plan        — 200 TenantDto | 400 invalid plan | 404
PATCH  /api/admin/tenants/{id}/modules     — 200 TenantDto | 400 unknown modules | 404
POST   /api/admin/tenants/{id}/activate    — 200 TenantDto | 404
POST   /api/admin/tenants/{id}/deactivate  — 200 TenantDto | 404
```

## Architecture Decisions
- Followed existing ProviderService pattern: domain interface (ITenantAdminRepository) → Application service → Infrastructure EF implementation
- Application layer has NO direct dependency on Infrastructure (no circular ref)
- Usage stats fetched via EF Count queries per-tenant (4 separate async queries)
- No DeletedAt in entities; UsersCount = all users for tenant, StoresCount = all stores

## Tests (4/4 passing)
- `GetAllTenants_ReturnsAll` — verifies all tenants returned
- `CreateTenant_DuplicateSlug_ReturnsConflictError` — 409 error + no DB write
- `UpdatePlan_InvalidPlan_ReturnsError` — domain validation error, no SaveChanges
- `Deactivate_SetsIsActiveFalse` — entity mutation + SaveChanges called

## Build & Tests
- `dotnet build` → 0 errors, 0 warnings
- `dotnet test --filter TenantAdminServiceTests` → 4/4 passed
- Full suite: 374 passed, 2 pre-existing failures (CheckboxFiscalClientTests — unrelated to this task)
