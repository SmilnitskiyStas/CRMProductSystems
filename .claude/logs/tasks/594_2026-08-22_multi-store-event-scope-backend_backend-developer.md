# TASK-594 — Events: multi-store scope backend (`Scope == "stores"`)

**Status:** done · **Agent:** backend-developer
**Depends on:** TASK-592 (database-engineer, `DemandEventStore` entity/table — already merged).
**Parallel:** TASK-593 (frontend-developer) built the matching UI concurrently against this
same contract — verified compatible after the fact (repeated `?storeIds=` query params,
`storeIds: string[]` field name).

## What changed

Wired the repository/service/controller layers to use the new `DemandEventStore` join table,
adding a third `Scope` value `"stores"` alongside `"network"`/`"store"`, and widened the events
list endpoint to accept multiple store ids (global header multi-select).

- `backend/ShelfGuard.Domain/Interfaces/IEventRepository.cs` — `GetAsync(DateOnly?, DateOnly?, Guid[]? storeIds, ct)`;
  new `Task ReplaceStoresForEventAsync(Guid eventId, IReadOnlyCollection<Guid> storeIds, ct)`
  (delete-existing/insert-new, no `SaveChangesAsync` call — same convention as
  `UserLocationRepository.ReplaceForUserAsync`).
- `backend/ShelfGuard.Infrastructure/Data/Repositories/EventRepository.cs`:
  - `GetAsync` — `storeIds` filter: no filter when null/empty; otherwise
    `Scope == "network" || StoreId in storeIds || Stores.Any(s => storeIds.Contains(s.StoreId))`.
    Added `.Include(e => e.Stores)`.
  - `GetByIdAsync` — added `.Include(e => e.Stores)` too (needed so `GET /api/events/{id}` and
    post-update `ToDto` both see the correct store list; the brief only mentioned `GetAsync`
    but `GetByIdAsync` feeds `ToDto` in the same way).
  - `GetCandidatesForDateAsync` — added the third OR clause (`Stores.Any(...)`), unchanged
    single-store signature.
  - `ReplaceStoresForEventAsync` — new, mirrors `UserLocationRepository.ReplaceForUserAsync`.
- `backend/ShelfGuard.Application/Features/Events/EventService.cs` / `IEventService.cs`:
  - `ValidScopes` gains `"stores"`; `Validate` requires non-empty `StoreIds` for `"stores"`.
  - `CreateAsync`/`UpdateAsync` call `ReplaceStoresForEventAsync(ev.Id, scope == "stores" ?
    StoreIds : [], ct)` — always called, so switching a `"stores"`-scoped event to another scope
    clears stale rows.
  - `ToDto` — `StoreIds = e.Stores.Select(s => s.StoreId).ToList()`, no `Scope` special-casing
    (reflects the entity's actual collection state).
  - `GetAsync(DateOnly?, DateOnly?, Guid[]? storeIds, ct)` — pass-through.
- `backend/ShelfGuard.Application/Features/Events/Dtos/EventDtos.cs` — `DemandEventDto` gains
  `StoreIds: List<Guid>` (never null); `UpsertEventRequest` gains `StoreIds: List<Guid>?`
  (nullable input, `?? []` guarded in the service).
- `backend/ShelfGuard.Api/Controllers/EventsController.cs` — `GET /api/events`:
  `[FromQuery] Guid? store_id` → `[FromQuery] Guid[]? storeIds` (camelCase, repeated-param
  convention already used by `PriceSegmentsController`/`UsersController`; no real callers broke,
  frontend never sent `store_id`).
- `backend/ShelfGuard.Application/Features/AiOrders/AiOrderService.cs` (~line 109) —
  `_events.GetAsync(today, today.AddDays(14), new[] { storeId }, ct)`, one-line call-site update,
  no behavior change.

## Verification

- `dotnet build ShelfGuard.sln` — clean (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test --filter "FullyQualifiedName~Events"` — 25/25 passed.
- `dotnet test --filter "FullyQualifiedName~AiOrders"` — 10/10 passed.
- `dotnet test --filter "FullyQualifiedName~Orders"` — 71/71 passed.
- Full `dotnet test` — **1807/1807 passed** (1793 baseline + 14 new).

New tests: `backend/ShelfGuard.Tests/Events/EventRepositoryStoresTests.cs` (8, EF InMemory
provider — any-match multi-store semantics, network-always-matches, single-store/null-storeIds
regression guards, `GetCandidatesForDateAsync` business-critical path, `ReplaceStoresForEventAsync`
insert/replace/clear incl. EF navigation-fixup check); 6 more added to existing
`EventServiceTests.cs` (Validate rejection for `"stores"` + empty/null `StoreIds`, `CreateAsync`/
`UpdateAsync` repo-call wiring including scope-switch-away clears stores).

## Contract for frontend (matches TASK-593 as built)

- `GET /api/events?from=...&to=...&storeIds=<guid>&storeIds=<guid>...` (repeated param, omit
  entirely for "all stores").
- `DemandEventDto`: `{ Id, Name, EventType, Scope, StoreId, StoreIds: Guid[], StartsAt, EndsAt,
  IsRecurring, Notes, Coefficients }` — JSON camelCase (`storeIds`), always an array (empty when
  not `"stores"` scope).
- `UpsertEventRequest` body: same shape plus `StoreIds` (send `[]` or omit unless
  `scope === "stores"`).

## Files changed

- `backend/ShelfGuard.Domain/Interfaces/IEventRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/EventRepository.cs`
- `backend/ShelfGuard.Application/Features/Events/IEventService.cs`
- `backend/ShelfGuard.Application/Features/Events/EventService.cs`
- `backend/ShelfGuard.Application/Features/Events/Dtos/EventDtos.cs`
- `backend/ShelfGuard.Api/Controllers/EventsController.cs`
- `backend/ShelfGuard.Application/Features/AiOrders/AiOrderService.cs`
- `backend/ShelfGuard.Tests/Events/EventServiceTests.cs` (extended)
- `backend/ShelfGuard.Tests/Events/EventRepositoryStoresTests.cs` (new)
- `backend/ShelfGuard.Tests/AiOrders/AiOrderServiceTests.cs` (mock signature fix for the
  `Guid[]?` param)
