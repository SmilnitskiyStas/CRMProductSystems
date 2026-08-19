# TASK-507: Loyalty — richer network stores + consumer preferred-store preference

**Status:** done · **Agent:** backend-developer

Renumbered from the brief's suggested log filename — current.md's actual max was TASK-506
(TASK-501/503/505 already taken by concurrent store-migration/RFM work). Nothing
staged/committed — orchestrator reviews and commits.

## What changed

1. **`GET /api/consumer/loyalty/networks`** — `LoyaltyNetworkSummaryDto.StoreNames: string[]`
   replaced with `Stores: LoyaltyNetworkStoreDto[]` (`{ storeId, storeName, address }`). Same
   active/non-warehouse filter and alphabetical sort as before (`LoadNetworkDetailsAsync` in
   `LoyaltyService.cs`), now projecting the full `Location` instead of just `.Name`.

2. **New "preferred store" concept** — explicitly NOT a membership/join change (still exactly
   one `LoyaltyMembership` per (tenant, consumer)):
   - `LoyaltyMembership.PreferredStoreId` (`Guid?`, nullable/SetNull, same FK convention as
     `CustomerId`/`LinkedUserId` — no navigation property, since `LoyaltyService` resolves the
     `Location` itself via `ITenantSessionOverride`, not EF `Include`).
   - Migration `20260811054559_AddLoyaltyMembershipPreferredStore` — adds the column + FK +
     index on `loyalty_memberships`. Applied to the local dev Postgres (port 5435) so
     integration tests could run.
   - `PUT /api/consumer/loyalty/preferred-store` (`ConsumerLoyaltyController.SetPreferredStore`,
     body `{ tenantId, storeId }`) → `LoyaltyService.SetPreferredStoreAsync`. No membership at
     that tenant → 403 ("You are not a member of this network." — exact wording match with
     `GetConsumerCodeAsync`'s explicit-tenantId branch). Invalid store (wrong tenant, inactive,
     or non-shoppable type) → 400 ("Invalid store for this network."). Valid → 200 with the
     updated membership. Never auto-creates a membership. Runs the membership-check +
     store-validate + write as one `ITenantSessionOverride` block (same pattern as `JoinAsync`'s
     consumer-session path), since "locations" has no `consumer_self_access` RLS policy.

3. **`GET /consumer/loyalty/memberships`** — `LoyaltyMembershipSummaryDto` gained
   `PreferredStoreId`, `PreferredStoreName`, `PreferredStoreAddress` (all nullable). Resolved
   per-membership in `GetMembershipsForConsumerAsync` via a new `ResolvePreferredStoreAsync`
   helper (each membership's own `ITenantSessionOverride`, since memberships span tenants).
   Null/stale/inactive `PreferredStoreId` → both name fields null, never throws.

`ToSummaryDto` now takes an optional `Location? preferredStore = null`; call sites that don't
resolve a store (JoinAsync, ManualAdjustAsync, GetMyMembershipAsync, JoinAsStaffAsync — all
untouched otherwise) still return the raw `PreferredStoreId` but leave the two name fields null,
since none of those flows had a resolved `Location` on hand and doing so was out of this task's
scope.

## Files touched

- `backend/ShelfGuard.Domain/Entities/LoyaltyMembership.cs`
- `backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs`
- `backend/ShelfGuard.Application/Features/Loyalty/ILoyaltyService.cs`
- `backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`
- `backend/ShelfGuard.Api/Controllers/ConsumerLoyaltyController.cs`
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260811054559_AddLoyaltyMembershipPreferredStore.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260811054559_AddLoyaltyMembershipPreferredStore.Designer.cs` (new)
- `backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`

Not touched: MarketingAnalytics files, `20260810181059_AddPosTxCustomerMigrationIndex.*` — all
pre-existing concurrent work, left exactly as found.

## Tests

`backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`:
- Updated the 5 existing `GetAvailableNetworksAsync` store tests for the `Stores` shape
  (storeId/storeName/address, same sort/filter behavior).
- Added `ITenantSessionOverride` pass-through setups for the new `Location?` and
  `(LoyaltyMembership?, Location?, string?, int?)` closed generics.
- 4 new `GetMembershipsForConsumerAsync` tests: resolves name+address; no preferred store (no
  lookup); stale/removed store; inactive store.
- 6 new `SetPreferredStoreAsync` tests: no membership → 403; wrong-tenant store → 400;
  inactive store → 400; non-shoppable type → 400; valid → 200 persisted + resolved fields;
  re-setting to a different store overwrites (not additive).

## Verification

- `dotnet build` — 0 errors (1 pre-existing unrelated warning, `MarketplaceServiceTests.cs`).
- `dotnet test` (no filter) — 1397/1397 passed, 0 failures (1387 baseline + 10 new).
- `docker build -f backend/Dockerfile backend` — succeeded.

## Final response/request JSON shapes

`GET /api/consumer/loyalty/networks` → `200 OK`:
```json
[
  {
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "tenantName": "Свіжий кут",
    "stores": [
      { "storeId": "b1f2...", "storeName": "М3", "address": "вул. Шевченка, 10" },
      { "storeId": "a3c9...", "storeName": "Магазин №1 - Центральний", "address": null }
    ]
  }
]
```

`PUT /api/consumer/loyalty/preferred-store`
Request:
```json
{ "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "storeId": "b1f2..." }
```
Responses:
- `200 OK` — body is the updated `LoyaltyMembershipSummaryDto` (see below).
- `403 Forbidden` — `{ "error": "You are not a member of this network." }`
- `400 Bad Request` — `{ "error": "Invalid store for this network." }`

`GET /api/consumer/loyalty/memberships` → `200 OK`, array of:
```json
[
  {
    "membershipId": "...",
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "tenantName": "Свіжий кут",
    "balance": 42.50,
    "status": "active",
    "joinedAt": "2026-07-01T12:00:00+00:00",
    "preferredStoreId": "b1f2...",
    "preferredStoreName": "М3",
    "preferredStoreAddress": "вул. Шевченка, 10"
  }
]
```
`preferredStoreId`/`preferredStoreName`/`preferredStoreAddress` are all `null` together when no
preferred store was ever set, or the referenced store has since gone inactive/been removed —
never an error.
