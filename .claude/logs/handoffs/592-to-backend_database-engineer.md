# Handoff: TASK-592 database-engineer → backend-developer / frontend-developer

## What's ready

New entity `DemandEventStore` (`backend/ShelfGuard.Domain/Entities/DemandEventStore.cs`):
```csharp
public sealed class DemandEventStore
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; init; }
    public Guid StoreId { get; init; }
    public DemandEvent? Event { get; init; }
}
```
`DemandEvent.Stores` (`ICollection<DemandEventStore>`) added alongside `Coefficients`.
`AppDbContext.DemandEventStores` DbSet added.

Migration `20260822081221_AddDemandEventStores` applied to local dev DB. Table
`demand_event_stores`: `Id` (uuid PK), `EventId` (uuid, FK → `demand_events` CASCADE),
`StoreId` (uuid, FK → `locations` CASCADE), unique composite index on
`(EventId, StoreId)`, plus single-column indexes on each. RLS: `tenant_isolation` /
`provider_bypass` / `worker_bypass` triad, tenant derived via `EXISTS` into
`demand_events` (no own `TenantId` column), `FORCE ROW LEVEL SECURITY` on.

## What's NOT done (next wave's job)

1. `DemandEvent.Scope` validation (`ValidScopes` or wherever it's enforced in
   `EventService.cs`/`EventsController.cs`/`EventDtos.cs`) still only accepts
   `"network"`/`"store"` — add `"stores"`.
2. Repository/service query logic: "does event X apply to store Y" needs a new branch for
   `Scope == "stores"` that checks `DemandEventStore` membership, alongside the existing
   `network` (always) / `store` (`StoreId` match) branches. This logic currently lives
   wherever `EventRepository.cs`/`EventService.cs` filters/resolves events for a store —
   not touched by this task.
3. CRUD for managing an event's `Stores` collection (add/remove target stores) — no
   endpoint exists yet, same shape as the existing `DemandEventCoefficient`
   add/remove endpoints on `EventsController.cs` is a reasonable precedent to follow.
4. DTOs (`EventDtos.cs`): no `DemandEventStore`/`Stores`/store-list shape exists yet on
   any request/response DTO.
5. Frontend: nothing wired — `EventForm.tsx`/`EventDetailPanel.tsx` etc. only know about
   `network`/`store` scope today (per TASK-589/591's day-detail-drawer work logged in
   `current.md`).

## Note on running `dotnet ef` / touching the dev DB

`AppDbContextFactory.cs` (design-time factory) ignores `appsettings.Development.json`; it
only reads env var `ConnectionStrings__DefaultConnection`, falling back to a non-matching
hardcoded string. Export before any `dotnet ef` DB command:
```
ConnectionStrings__DefaultConnection="Host=localhost;Port=5435;Database=crm;Username=shelfguard_app_dev;Password=307823f594357b97c27a046f33bc5549ad09"
```
Also: local dev's `crmproductsystems-postgres-1` docker container may need `docker start`
if Docker Desktop was recently restarted (it doesn't always auto-resume).
