# TASK-592 — DemandEventStore: event↔specific-stores join table (schema layer)

**Agent:** database-engineer
**Status:** done
**Scope:** entity + EF config + migration only — repository/service/controller/UI is a
follow-up wave (backend-developer, frontend-developer)

## What changed

New entity `backend/ShelfGuard.Domain/Entities/DemandEventStore.cs` — mirrors
`DemandEventCoefficient`'s actual style exactly (anemic, `init` properties, `Id { get; init;
} = Guid.NewGuid()`, no private setters, no static `Create()` — that entity has neither, so
`DemandEventStore` doesn't either, despite the illustrative sketch in the brief):
```csharp
public sealed class DemandEventStore
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; init; }
    public Guid StoreId { get; init; }
    public DemandEvent? Event { get; init; }
}
```

`DemandEvent.cs`: added `ICollection<DemandEventStore> Stores { get; init; } = new
List<DemandEventStore>();` next to `Coefficients`; updated `Scope`'s doc comment to mention
the third value (`stores` — several specific stores via `Stores`). `IsActiveOn` and all
existing logic untouched — no Scope/StoreId/Stores branching added, per brief.

`AppDbContext.cs`: `DbSet<DemandEventStore> DemandEventStores`; new config block
immediately after `DemandEventCoefficient`'s, same style (`ToTable`, `HasDefaultValueSql
("gen_random_uuid()")`, `HasOne(...).WithMany(...).OnDelete(Cascade)`).

Migration `20260822081221_AddDemandEventStores`:
- Table `demand_event_stores`: `Id uuid PK default gen_random_uuid()`, `EventId uuid NOT
  NULL`, `StoreId uuid NOT NULL`.
- FK `EventId → demand_events(Id)` CASCADE, FK `StoreId → locations(Id)` CASCADE.
  **Note:** physical stores table is `locations`, not `stores` — the old
  `V2EventsWeather` migration's `principalTable: "stores"` predates the v4 Store→Location
  rename; verified against `Location`'s current `ToTable("locations")` config and copied
  from `AddMarketplaceOrderReceiving`'s live FK before writing this one.
- Indexes: `IX_demand_event_stores_EventId`, `IX_demand_event_stores_StoreId` (from FK),
  and unique composite `IX_demand_event_stores_EventId_StoreId` — deliberately unlike
  `demand_event_coefficients`, which has no such constraint (that table permits multiple
  coefficient rows per scope; this one exists purely to answer "is store X targeted by
  event Y", so duplicates are meaningless).
- RLS (hand-added, EF doesn't scaffold this): no own `TenantId` — same shape as
  `demand_event_coefficients`, tenant derived via `EXISTS` into `demand_events`. Used the
  **current** canonical triad from `database-schema.md`'s RLS Template
  (`FORCE ROW LEVEL SECURITY`, NULLIF guard, `provider_bypass` as
  `IN ('provider','provider_admin')`, `worker_bypass`) rather than literally copying
  `demand_event_coefficients`'s 2026-06-11 policy, which predates that triad and was never
  backfilled — out of scope here. Added `WITH CHECK` mirroring `USING` since this is a
  writable join table (not read-only like the older coefficients policy).

## Verification

- `dotnet build backend/ShelfGuard.sln` — clean, 0 errors (1 pre-existing unrelated
  warning, `MarketplaceServiceTests.cs:534`).
- `dotnet ef database update` applied to local dev DB (docker `crmproductsystems-postgres-1`,
  port 5435 — container had exited when Docker Desktop was down at session start,
  restarted it). Required exporting `ConnectionStrings__DefaultConnection` (the
  `AppDbContextFactory` design-time factory ignores `appsettings.Development.json`,
  falls back to a non-matching hardcoded connection string — same gotcha TASK-584 logged).
- `\d demand_event_stores` in psql confirms: table, all 3 indexes, both FKs, "forced row
  security enabled", and all 3 policies (`tenant_isolation` EXISTS-into-`demand_events`
  with matching `WITH CHECK`, `provider_bypass` IN-list, `worker_bypass`) present exactly
  as written.
- `dotnet test backend/ShelfGuard.sln` — **1793 passed, 0 failed** (includes the dynamic
  `RlsCrossTenantIntegrationTests` suite that enumerates every `FORCE ROW LEVEL SECURITY`
  table — passed with the new table included).

## For the next wave (backend-developer, frontend-developer)

Contract to build against:
- Entity: `ShelfGuard.Domain.Entities.DemandEventStore` (`Id`, `EventId`, `StoreId`,
  nav `Event`), reachable via `DemandEvent.Stores` or `AppDbContext.DemandEventStores`.
- Table `demand_event_stores`, columns `Id`/`EventId`/`StoreId` (PascalCase, EF default).
- Not touched (deliberately, per brief): `EventRepository.cs`, `EventService.cs`,
  `EventsController.cs`, `EventDtos.cs`, `ValidScopes` validation. `DemandEvent.Scope`
  still only has `"network"`/`"store"` accepted anywhere in validation — `"stores"` isn't
  wired into `ValidScopes` yet, that's the next agent's job, along with the actual
  scope-filtering query logic (which events apply to a given store) and CRUD for managing
  the `Stores` collection.

See handoff: `.claude/logs/handoffs/592-to-backend_database-engineer.md`.
