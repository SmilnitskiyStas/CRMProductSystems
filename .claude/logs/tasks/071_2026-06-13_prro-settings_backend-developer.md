# TASK-071 Backend — ПРРО Settings API
**Agent:** backend-developer
**Date:** 2026-06-13
**Status:** done (backend part)

## What was done

### New files created

1. `backend/ShelfGuard.Application/Features/Integrations/Dtos/PrroSettingsDtos.cs`
   — Already existed (untracked). DTOs: `PrroSettingsDto`, `UpsertPrroSettingsRequest`, `PrroTestResult`.

2. `backend/ShelfGuard.Application/Features/Integrations/PrroSecrets.cs`
   — Already existed (untracked). Write-only secret masking: `Mask()`, `IsMasked()`, `Merge()`, `MaskInPlace()`, `MergeMaskedFromStored()`.

3. `backend/ShelfGuard.Application/Features/Integrations/PrroSettingsService.cs`
   — Already existed (untracked). `IPrroSettingsService` + `PrroSettingsService` implementing GET (masked), Upsert (keep-on-masked), TestAsync (ping + cashier signin).

4. `backend/ShelfGuard.Application/Features/Pos/Fiscal/IFiscalServiceFactory.cs`
   — Already existed (untracked). `PrroConnectionConfig` record + `IFiscalServiceFactory` interface. Updated interface: renamed `GetForCurrentTenantAsync` → `GetForTenantAsync(Guid tenantId, ...)` for explicit tenant context.

5. **NEW** `backend/ShelfGuard.Infrastructure/Integrations/Prro/CheckboxTokenStoreRegistry.cs`
   — Already existed (untracked). Singleton dictionary keyed by `{tenantId}|{baseUrl}|{licenseKey}`.

6. **NEW** `backend/ShelfGuard.Infrastructure/Integrations/Prro/FiscalServiceFactory.cs`
   — Implements `IFiscalServiceFactory`. Uses `IServiceScopeFactory` (proper singleton→scoped pattern). Resolution: DB row → env `PRRO:*` → `NoopFiscalService`. Creates `CheckboxFiscalClient` via named `"checkbox"` HttpClient pool + per-tenant `CheckboxTokenStore` from registry.

7. **NEW** `backend/ShelfGuard.Api/Controllers/PrroSettingsController.cs`
   — `GET /api/settings/prro`, `PUT /api/settings/prro`, `POST /api/settings/prro/test`. Policy: `AtLeastStoreManager`.

8. **NEW** `backend/ShelfGuard.Tests/Prro/PrroSecretsTests.cs` — 14 tests covering Mask/IsMasked/Merge.
9. **NEW** `backend/ShelfGuard.Tests/Prro/PrroSettingsServiceTests.cs` — 13 tests: masking, keep-on-masked PUT, validation, TestAsync with candidate.
10. **NEW** `backend/ShelfGuard.Tests/Prro/FiscalServiceFactoryTests.cs` — 11 tests: resolution order DB→env→noop, env override check, per-tenant token store isolation.

### Modified files

- `backend/ShelfGuard.Infrastructure/Integrations/Prro/CheckboxFiscalClient.cs`
  — Added `CheckCashierAsync()` (GET cashier/profile with bearer token). Added `CashierProfile` wire DTO.
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`
  — Replaced startup-time `PRRO:PROVIDER` DI switch with: `CheckboxTokenStoreRegistry` (singleton), named `"checkbox"` HttpClient, `IFiscalServiceFactory → FiscalServiceFactory` (singleton).
- `backend/ShelfGuard.Application/DependencyInjection.cs`
  — `IPrroSettingsService` already registered (was in untracked file).
- `backend/ShelfGuard.Application/Features/Integrations/IntegrationService.cs`
  — Already had masking via `PrroSecrets.MaskInPlace` for generic GET.
- `backend/ShelfGuard.Application/Features/Pos/Fiscal/IFiscalService.cs`
  — `CheckCashierAsync` was already added.
- `backend/ShelfGuard.Application/Features/Pos/Fiscal/NoopFiscalService.cs`
  — `CheckCashierAsync` was already in Noop.
- `backend/ShelfGuard.Application/Features/Pos/Fiscal/FiscalModels.cs`
  — `FiscalCashierResult` already added.

### Tests
- **Before:** 292/292
- **After:** 334/334 (+42 new tests, 0 failures)

## Endpoint shapes

### GET /api/settings/prro
Response 200:
```json
{
  "provider": "checkbox",
  "isEnabled": true,
  "baseUrl": "https://api.checkbox.in.ua/api/v1",
  "licenseKey": "••••6789",
  "cashierLogin": "kasir",
  "cashierPassword": "••••",
  "cashierPinCode": "••••",
  "source": "tenant",
  "updatedAt": "2026-06-13T10:00:00Z"
}
```
`source`: "tenant" | "env" | "none"
`licenseKey`: null when not set; `"••••" + last4` when set
`cashierPassword` / `cashierPinCode`: null when not set; `"••••"` when set

### PUT /api/settings/prro
Request body:
```json
{
  "provider": "checkbox",
  "isEnabled": true,
  "baseUrl": "https://api.checkbox.in.ua/api/v1",
  "licenseKey": "••••6789",
  "cashierLogin": "kasir",
  "cashierPassword": "••••",
  "cashierPinCode": null
}
```
Masked/null secret → keeps stored value. Empty string `""` → clears secret.
Response 200: same shape as GET (masked). Response 400: `{"error": "..."}`.

### POST /api/settings/prro/test
Request body: same as PUT (optional — omit to test stored/env config).
Response 200 (always):
```json
{
  "ok": true,
  "provider": "checkbox",
  "fiscalNumber": "TEST582378",
  "isTest": true,
  "hasOpenShift": false,
  "cashierOk": true,
  "error": null
}
```
`ok: false` example:
```json
{
  "ok": false,
  "provider": "checkbox",
  "fiscalNumber": "TEST582378",
  "isTest": true,
  "hasOpenShift": false,
  "cashierOk": false,
  "error": "Невірний пінкод"
}
```

## Architecture notes

- `IFiscalServiceFactory` replaces the startup-time `PRRO:PROVIDER` DI switch. TASK-068 / TASK-069 must resolve via `factory.GetForTenantAsync(tenantId)` instead of injecting `IFiscalService` directly.
- `FiscalServiceFactory` is a singleton that uses `IServiceScopeFactory` to create a transient scope for each `GetForTenantAsync` call (proper pattern for singleton→scoped resolution).
- Per-tenant token caching: `CheckboxTokenStoreRegistry` keys by `{tenantId}|{baseUrl}|{licenseKey}`. Env-fallback deployment uses `Guid.Empty` as the tenant key.
