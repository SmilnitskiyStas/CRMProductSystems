# TASK-588 — Remove event coefficient endpoint (backend)

**Status:** done · **Agent:** backend-developer · **Date:** 2026-08-21

## Context
Events calendar day-detail view (frontend, in progress in parallel) needs to unlink a
product/category/segment from a demand event. `POST`/`PUT /api/events/{id}/coefficients`
already existed; no delete.

## Changes
- `backend/ShelfGuard.Domain/Interfaces/IEventRepository.cs` — added
  `void RemoveCoefficient(DemandEventCoefficient coefficient);` (mirrors existing
  `void Remove(DemandEvent demandEvent)`, reuses existing `GetCoefficientAsync(Guid, ct)`).
- `backend/ShelfGuard.Infrastructure/Data/Repositories/EventRepository.cs` — implemented via
  `_db.DemandEventCoefficients.Remove(coefficient)`.
- `backend/ShelfGuard.Application/Features/Events/IEventService.cs` — added
  `Task<string?> RemoveCoefficientAsync(Guid eventId, Guid coefId, CancellationToken ct = default);`
- `backend/ShelfGuard.Application/Features/Events/EventService.cs` — implemented, mirrors
  `UpdateCoefficientAsync`'s lookup/ownership convention exactly: `GetCoefficientAsync(coefId, ct)`,
  returns `"Coefficient not found."` when `coef is null || coef.EventId != eventId`, otherwise
  `RemoveCoefficient` + `SaveChangesAsync` + `return null`.
- `backend/ShelfGuard.Api/Controllers/EventsController.cs` — added, right after `UpdateCoefficient`:
  ```csharp
  [HttpDelete("{id:guid}/coefficients/{coefId:guid}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> RemoveCoefficient(Guid id, Guid coefId, CancellationToken ct)
  {
      var error = await _events.RemoveCoefficientAsync(id, coefId, ct);
      return error is not null ? NotFound(new { error }) : NoContent();
  }
  ```
  Authorization: no per-action attribute, same as `AddCoefficient`/`UpdateCoefficient` — inherits
  controller-level `[Authorize(Policy = AppPolicies.AtLeastStoreManager)]`.
- `backend/ShelfGuard.Tests/Events/EventServiceTests.cs` — new file (Events test folder previously
  had only `EventCoefficientResolverTests.cs`). NSubstitute/xUnit, same constructor-DI style as
  `LocationServiceTests.cs`. 3 tests: not-found, belongs-to-different-event, happy-path
  (`RemoveCoefficient` called with exact entity + `SaveChangesAsync` called + returns null).

## Verification
- `dotnet build backend/ShelfGuard.sln` — clean, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test --filter "FullyQualifiedName~Events"` — 11 passed, 0 failed.
- Full `dotnet test backend/ShelfGuard.sln` — 1788 passed, 0 failed.

## Handoff
Frontend (day-detail drawer) can now call `DELETE /api/events/{id}/coefficients/{coefId}` →
204 on success, 404 `{ error: "Coefficient not found." }` if missing or mismatched event.
