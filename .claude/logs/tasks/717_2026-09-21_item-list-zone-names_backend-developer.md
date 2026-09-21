# TASK-717 — Zone names on the paginated item list (backend)

**Status:** review · **Agent:** backend-developer · not pushed

## What changed

Third instance of the Slice 3/4c batch-load pattern (promo state, buffer suggestion), now for
zone tags — feeds a new "Zones" column on the frontend Catalog table (separate frontend task).

- `ItemDto.cs` — new trailing optional field `IReadOnlyList<string>? ZoneNames = null`.
- `IItemRepository`/`ItemRepository` += `GetZoneNamesAsync(IReadOnlyList<Guid> itemIds, ct)` →
  `Dictionary<Guid, List<string>>`. One query over `item_zone_assignments` (`.Include(a => a.Zone)`,
  `.Where(ItemId in ids)`), grouped in memory into item → zone-name list. Empty-ids guard mirrors
  `GetPromoStatesAsync`/`GetBufferSuggestionsAsync`.
- `ItemService.cs` — new `LoadZoneNamesAsync` loader (same empty-dict guard + null-fallback shape
  as `LoadPromoStatesAsync`/`LoadBufferSuggestionsAsync`), wired into both `GetAllAsync` and
  `GetPagedAsync` alongside `promo`/`suggestions`. `ToDto` mapper += 3rd dictionary param,
  populates `ZoneNames` via `zoneNames?.GetValueOrDefault(p.Id)`. `GetByIdAsync`/`CreateAsync`/
  `UpdateAsync` untouched (list-only field, same convention as `SuggestedMinStock`).

## Test fixes (compile-only, no behavior change)

New `IItemRepository` member required stubbing in the two hand-written fakes: `PosServiceTests.cs`
(`FakeCatalogRepo`) and `FiscalizationRetryTests.cs` (`RetryFakeCatalogRepo`) — both return an
empty dict, unused by those suites.

## New test

`ItemServiceTests.GetPagedAsync_MapsZoneNamesIntoDto` — mirrors the existing
`GetPagedAsync_MapsBufferSuggestionIntoDto`: tagged item gets its zone names, untagged item's
`ZoneNames` stays null.

## Build/test status

- `dotnet build` (full solution): 0 errors, 0 warnings (1 pre-existing warning in
  `MarketplaceServiceTests.cs`, unrelated file, not touched here).
- `dotnet test` filtered (`ItemServiceTests|ItemRepositoryGetPagedTests|ItemsControllerTests`): 44/44.
- `dotnet test` full suite: **2454/2454 green**.

## Notes

Frontend "Zones" column, `ItemsController.cs` routes, and TASK-714/715/716 files not touched —
out of scope per brief.
