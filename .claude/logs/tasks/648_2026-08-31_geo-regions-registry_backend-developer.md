# TASK-648 — UkraineRegions registry + GET /api/geo/regions

**Agent:** backend-developer · **Model:** sonnet · **Status:** done (2026-08-31) · **Depends:** —
Part of plan `eventual-whistling-rabbit.md` (supplier delivery-coverage feature), split T1.

## What / where

- `backend/ShelfGuard.Domain/Constants/UkraineRegions.cs` (NEW) — source-of-truth registry,
  mirrors `SupplierItemCategories.cs`. `RegionDefinition(Code, NameUa, Kind, ParentCode)`.
  `All` = 27 ISO 3166-2:UA oblast-level units (`Kind="oblast"`, `ParentCode=null`) + 24 major
  cities (`Kind="city"`, `Code="{oblast}-{TRANSLIT}"`, `ParentCode`). Class XML-doc flags
  `UA-30` = м. Київ vs `UA-32` = Київська обл.; `UA-40`/`UA-43` included with neutral labels.
  Helpers: `Find(code)` (dict), `IsValid(code)`, `Validate(codes)` → error list,
  `TryMatchFreeText(raw)` (lowercase/trim, strip область/обл./обл/місто/м., alias map +
  oblast/city name match; used later by the T14 backfill).
- `backend/ShelfGuard.Application/Features/Geo/` (NEW) — `Dtos/GeoDtos.cs`
  `RegionDto(Code, NameUa, Kind, ParentCode)`; `IGeoService.GetRegions()`; `GeoService`
  maps `UkraineRegions.All` → `RegionDto` (static list, no DB, still injectable).
- `backend/ShelfGuard.Api/Controllers/GeoController.cs` (NEW) — `[ApiController]`,
  `[Route("api/geo")]`, `GET regions` `[AllowAnonymous]` (matches marketplace item-categories
  precedent), thin: `Ok(_geoService.GetRegions())`.
- `backend/ShelfGuard.Application/DependencyInjection.cs` — `AddScoped<IGeoService, GeoService>()`.
- Tests (NEW): `Domain/UkraineRegionsTests.cs` (unique codes, city→oblast parent validity,
  Find/IsValid round-trip, Validate, TryMatchFreeText incl. "Київська область"→UA-32,
  "м. Київ"→UA-30, "Дніпро"→UA-12, "АР Крим"→UA-43), `Geo/GeoServiceTests.cs` (1:1 mapping),
  `Geo/GeoControllerTests.cs` (thin endpoint returns service list).

## Status

- `dotnet build ShelfGuard.sln` — 0 errors (built to scratch `--artifacts-path`; a dev API
  process was holding the normal `bin/` DLLs).
- `dotnet test --filter "FullyQualifiedName~UkraineRegionsTests|~GeoServiceTests|~GeoControllerTests"`
  — 41/41 green.
- No migration, no RLS, no touched marketplace files. No new NuGet packages.

## Final contract for downstream (T7 frontend, T13 mobile)

- Endpoint: `GET /api/geo/regions` (anonymous) → `RegionDto[]`
- `RegionDto { code: string; nameUa: string; kind: "oblast" | "city"; parentCode: string | null }`
- Oblasts first, then cities. Oblast code = ISO 3166-2:UA (`UA-30` m. Kyiv ≠ `UA-32` Kyiv oblast).
  City code = `{oblastCode}-{TRANSLIT}` (e.g. `UA-18-ZHYTOMYR`), `parentCode` = oblast code.
