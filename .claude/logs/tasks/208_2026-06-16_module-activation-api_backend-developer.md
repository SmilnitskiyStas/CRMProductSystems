# TASK-208 — Backend: Tenant business_type + Module activation API

**Agent:** backend-developer · **Date:** 2026-06-16 · **Status:** done

## What was done

### Domain (`ShelfGuard.Domain/Entities/Tenant.cs`)
- `GetModules()` — parses the `Modules` JSONB string into `IReadOnlyList<string>` (replaces the duplicated private `ParseModules` that used to live in `TenantAdminService`).
- `HasModule(string key)` — case-insensitive membership check, used by the new module filter.
- `DefaultModulesForBusinessType(string businessType)` (static) — ADR-015's default module map:
  - `retail` → `{inventory, procurement, pos}`
  - `auto_service` → `{auto_service, procurement}`
  - `restaurant` → `{inventory, pos, production}`
  - `warehouse` → `{inventory, procurement}`
  - `production` → `{inventory, procurement, production}` (not in backlog spec, added for completeness since `UpdateBusinessType` already accepts this value)
  - `distribution` → `{inventory, procurement, marketplace}` (same reason)
  - unknown → `{inventory}` fallback

### `[RequireModule]` attribute + filter (`ShelfGuard.Infrastructure/Authorization/RequireModuleAttribute.cs`)
- `RequireModuleAttribute : Attribute, IFilterFactory` — `[RequireModule("auto_service")]` on a controller/action.
- `RequireModuleFilter : IAsyncActionFilter` (internal) — reads `tenant_id` + role claims, looks up the tenant via `ITenantRepository`, returns `403 { error: "Module not activated" }` if the module isn't in the tenant's list. Provider-role requests bypass the check entirely (providers manage tenants via `/api/admin`, not through module-gated tenant endpoints; impersonation issues a JWT with the impersonated user's own role/tenant_id, so it's never the `provider` role here and goes through the normal check).
- **Not yet attached to any live controller** — see Known Issue KI-012 below for why.

### `GET /api/settings/modules` (new)
- `ModulesSettingsController` (`api/settings/modules`, `AtLeastEnterpriseAdmin` policy) → `IModulesSettingsService` → `ITenantRepository.GetByIdAsync` (RLS-scoped to the caller's own tenant) → `ModulesSettingsDto(BusinessType, Modules)`.

### `GET/PATCH /api/admin/tenants/{id}/modules` (ProviderOnly)
Already existed from prior work — confirmed present in `AdminController`/`TenantAdminService`, no changes needed there beyond the DTO/mapping updates below.

### Default modules on tenant creation
- `CreateTenantRequest` gained an optional `BusinessType` (defaults to `"retail"` server-side when omitted).
- `TenantAdminService.CreateTenantAsync` now calls `tenant.UpdateBusinessType(businessType)` then `tenant.UpdateModules(Tenant.DefaultModulesForBusinessType(businessType))` right after the tenant is created — so brand-new tenants start with the correct module set for their industry. Invalid `BusinessType` values return an error before the tenant is persisted (same `(Result, Error)` pattern as the rest of the service).
- `TenantDto` gained a `BusinessType` field.

## Files changed
- `ShelfGuard.Domain/Entities/Tenant.cs`
- `ShelfGuard.Domain/Interfaces/ITenantRepository.cs` (doc comment only — clarified it's RLS-scoped per caller role, not provider-exclusive)
- `ShelfGuard.Application/Features/Admin/Dtos/AdminDtos.cs`
- `ShelfGuard.Application/Features/Admin/TenantAdminService.cs`
- `ShelfGuard.Application/Features/Settings/IModulesSettingsService.cs` (new)
- `ShelfGuard.Application/Features/Settings/ModulesSettingsService.cs` (new)
- `ShelfGuard.Application/Features/Settings/Dtos/ModulesSettingsDto.cs` (new)
- `ShelfGuard.Application/DependencyInjection.cs`
- `ShelfGuard.Api/Controllers/ModulesSettingsController.cs` (new)
- `ShelfGuard.Infrastructure/Authorization/RequireModuleAttribute.cs` (new — attribute + filter)
- Tests: `ShelfGuard.Tests/Domain/TenantTests.cs` (new), `ShelfGuard.Tests/Authorization/RequireModuleFilterTests.cs` (new), `ShelfGuard.Tests/Admin/TenantAdminServiceTests.cs` (3 new cases)

## Verification
- `dotnet build` → 0 errors
- `dotnet test` → 420/420 passed (402 existing + 18 new)
- No DB migration — `BusinessType`/`Modules` columns already existed; this task is pure C# (entity methods, one new GET endpoint, default-module wiring on create).

## Known issue flagged (KI-012 in `.claude/docs/known-issues.md`)
Existing tenants (including the production tenant) still carry **legacy** module keys (`shelf_manager`, `crm`, `notifications`) from before this feature, not the v4 keys (`inventory`, `procurement`, `pos`, etc.). `[RequireModule]` isn't attached to any live controller yet, so this is harmless today — but the first future task that gates a real endpoint with `[RequireModule(...)]` (Phase 4 Auto Service, Phase 5 Production) needs to backfill existing tenants' `Modules` first, or every current tenant gets locked out of that endpoint. Documented so it isn't a surprise later.

## Next
TASK-209 — Frontend: Module activation settings UI (depends on this; consumes `GET /api/settings/modules`).
