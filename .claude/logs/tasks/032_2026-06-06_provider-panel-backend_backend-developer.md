# TASK-032 — Provider Panel: Backend API

**Date:** 2026-06-06
**Agent:** backend-developer
**Status:** done

## What was implemented

### Domain layer
- `Tenant.cs` — added domain methods: `UpdatePlan()`, `UpdateModules()`, `Deactivate()`, `Activate()`
- `ITenantRepository.cs` — new interface: `GetAllAsync`, `GetByIdAsync`, group count queries, health aggregates, `SaveChangesAsync`
- `IActivityLogRepository.cs` — extended with `GetByTenantAsync` and `GetAllTenantsAsync` for cross-tenant provider view

### Application layer
- `Features/Provider/Dtos/ProviderDtos.cs` — `TenantSummaryDto`, `TenantDetailDto`, `UpdatePlanRequest`, `UpdateModulesRequest`, `ImpersonateResponse`, `ProviderHealthDto`, `ProviderLogDto`
- `Features/Provider/IProviderService.cs` — interface: `GetTenantsAsync`, `GetTenantAsync`, `UpdatePlanAsync`, `UpdateModulesAsync`, `ImpersonateAsync`, `GetHealthAsync`, `GetLogsAsync`
- `Features/Provider/ProviderService.cs` — full implementation with audit logging on impersonation
- `Services/IJwtService.cs` — added `GenerateImpersonationToken(providerId, providerEmail, targetTenantId)`
- `DependencyInjection.cs` — registered `IProviderService/ProviderService`

### Infrastructure layer
- `Data/Repositories/TenantRepository.cs` — new: efficient GROUP BY queries for user/store/expired counts (no N+1)
- `Data/Repositories/ActivityLogRepository.cs` — implemented `GetByTenantAsync`, `GetAllTenantsAsync`
- `Services/JwtService.cs` — implemented `GenerateImpersonationToken` (60-min, role=enterprise_admin, impersonated=true claim)
- `DependencyInjection.cs` — registered `ITenantRepository/TenantRepository`

### Api layer
- `Controllers/ProviderController.cs` — replaced placeholder with full implementation:
  - `GET  /api/provider/tenants` — list all tenants with stats
  - `GET  /api/provider/tenants/:id` — single tenant detail + last activity
  - `PUT  /api/provider/tenants/:id/plan` — update billing plan
  - `PUT  /api/provider/tenants/:id/modules` — update enabled modules
  - `POST /api/provider/tenants/:id/impersonate` — 60-min scoped JWT + audit log
  - `DELETE /api/provider/tenants/:id/impersonate` — end impersonation (client signal)
  - `GET  /api/provider/health` — platform-wide metrics
  - `GET  /api/provider/logs?limit=100` — cross-tenant activity logs

## Build verification
`dotnet build ShelfGuard.Api --output C:\Temp\sg-provider-check`
→ **0 Errors, 0 Warnings**

## Key design decisions
- **RLS**: Provider role JWT sets `app.role = 'provider'` via TenantConnectionInterceptor → `provider_bypass` policy fires automatically. No manual tenant filtering needed.
- **Impersonation token**: Short-lived (60 min), role downgraded to `enterprise_admin`, `tenant_id` = target tenant, `impersonated=true` claim for audit detection.
- **Counts**: All per-tenant stats use single GROUP BY queries — no N+1 loops.

## Next
- Frontend: `/provider` page — TASK-033 for frontend-developer
