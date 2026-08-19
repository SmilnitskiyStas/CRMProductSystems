# TASK-506: Loyalty network picker — store names per tenant

**Status:** done · **Agent:** backend-developer

Renumbered from the brief's TASK-501 — that ID was already taken by a concurrent
database-engineer task (store-migration index) that landed after the brief was written;
current.md's actual max was 505 (TASK-503/505 reserved for that workstream's still-pending
frontend/docs follow-ups). Nothing staged/committed per the brief — orchestrator reviews and
commits.

## What changed

`GET /api/consumer/loyalty/networks` now includes each tenant's active, shoppable store names.

- `LoyaltyNetworkSummaryDto` (`backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs`)
  gained `IReadOnlyList<string> StoreNames`.
- `LoyaltyService.GetAvailableNetworksAsync` (`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`)
  now injects `ILocationRepository` (already DI-registered in
  `ShelfGuard.Infrastructure/DependencyInjection.cs:53`, no new registration needed) and, inside
  the same per-tenant `ITenantSessionOverride.ExecuteAsync` block that already read
  `LoyaltyProgramSettings`, also calls `ILocationRepository.GetAllAsync` and combines both reads
  into one override (new private `LoadNetworkDetailsAsync` helper) instead of opening two.
- Store filter: `IsActive == true` AND `Type` not in a new `NonShoppableLocationTypes` exclude-set
  (`warehouse`, `central_warehouse`, `distribution`, `office`, `production`). Investigated first
  (grep across `backend/ShelfGuard.Application` for `LocationType ==`/`.Type ==`): despite its
  name, `Location.LocationType` (default `"retail_store"`) is dead code — nothing in Application
  reads or writes it. The DTO field also confusingly named `LocationType`
  (`CreateLocationRequest.LocationType`/`UpdateLocationRequest.LocationType`) actually maps onto
  entity `Type` in `LocationService.CreateAsync`/`UpdateAsync`. `Type` is the real, populated
  field; `LocationService.IsValidLocationType` is its full valid-value set. Used an exclude-list
  against that set (not an include-list) so a new customer-facing type added there later shows up
  in the picker automatically.
- Names sorted alphabetically (`StringComparer.OrdinalIgnoreCase`) for stable ordering across
  requests.
- Tenant with zero qualifying stores still appears, with `StoreNames: []` (not omitted, not null).
- `JoinAsync`/membership semantics untouched — informational only, per product owner: one
  membership per tenant, no per-store selection.

## Tests

`backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`: added `ILocationRepository` mock (default
`GetAllAsync` → empty list) and a third `ITenantSessionOverride.ExecuteAsync` pass-through setup
for the new combined-read tuple's closed generic. Extended the existing
`GetAvailableNetworksAsync_returns_only_active_enabled_loyalty_networks` test to assert
`StoreNames` is empty (not null) for a tenant with no locations stubbed. Added 4 new tests:
active-store sort order, inactive-store exclusion, non-shoppable-type exclusion (warehouse/office),
zero-stores-still-included.

`backend/ShelfGuard.Tests/Infrastructure/LoyaltyJoinRlsIntegrationTests.cs`'s
`BuildLoyaltyService` helper also constructs `LoyaltyService` directly — added the new
`ILocationRepository` constructor arg using the real `LocationRepository(db)` (consistent with
its other db-backed repos in that RLS test), not a substitute.

## Verification

- `dotnet build` — 0 errors (1 pre-existing unrelated warning in `MarketplaceServiceTests.cs`).
- `dotnet test` (no filter) — 1387/1387 passed, 0 failures.
- `docker build -f backend/Dockerfile backend` — succeeded.

## Final response JSON shape

`GET /api/consumer/loyalty/networks` → `200 OK`, array of:

```json
[
  {
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "tenantName": "Свіжий кут",
    "storeNames": ["М3", "Магазин №1 - Центральний"]
  },
  {
    "tenantId": "9c858901-8a57-4791-81fe-4c455b099bc9",
    "tenantName": "Нова мережа",
    "storeNames": []
  }
]
```

`storeNames` is always present and always an array (never `null`, never omitted) — empty when the
tenant currently has zero active/shoppable stores. Order is alphabetical and stable across
requests. No change to any other field or endpoint.
