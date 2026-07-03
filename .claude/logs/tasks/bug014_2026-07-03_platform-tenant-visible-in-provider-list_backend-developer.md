# BUG-014 — Platform Marketplace tenant visible in provider list

**Agent:** backend-developer · **Date:** 2026-07-03 · **Status:** done

## Root cause
`MarketplaceRepository.GetOrCreatePlatformTenantIdAsync` (BUG-012) lazily creates an
internal system tenant (`slug=platform-marketplace`, inactive, no users). It wasn't
filtered out of `ProviderService.GetTenantsAsync`, so it appeared in the provider panel;
provider created a `supplier_admin` user there → 403 on `/api/supplier-cabinet/*`
(tenant inactive, no `marketplace_supplier` module).

## Fix
- `TenantRepository.GetAllAsync` (`backend/ShelfGuard.Infrastructure/Data/Repositories/TenantRepository.cs`):
  added `.Where(t => t.Slug != MarketplaceRepository.PlatformTenantSlug)` — filtered at
  repository level, avoids pulling the constant into `Application`.
- `ProviderService.CreateTenantUserAsync` (`backend/ShelfGuard.Application/Features/Provider/ProviderService.cs`):
  added `!tenant.IsActive` guard → returns `"Tenant is not active."` error, no user created.
  General safety net, not specific to the platform tenant.

## Tests added
- `backend/ShelfGuard.Tests/Provider/TenantRepositoryPlatformTenantTests.cs` —
  `GetAllAsync_ExcludesPlatformMarketplaceTenant` (EF InMemory).
- `backend/ShelfGuard.Tests/Provider/ProviderServiceTests.cs` —
  `CreateTenantUser_InactiveTenant_IsRejected`.

## Verification
- `dotnet build`: 0 errors, 0 warnings.
- `dotnet test`: 515/515 passing (was 513; +2 new tests).

## Out of scope
Prod data cleanup (stray user `stassmilnitskiy2@gmail.com` under tenant
`89d95a15-abcb-459a-b943-6e9a8a3f07ac`) — handled separately by main session/user.
Not committed/pushed per instruction.
