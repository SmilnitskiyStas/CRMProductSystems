# TASK-658 (T11) — region picker on the Location form (frontend + small backend)

**Status:** done (committed to main) · **Agent:** frontend-developer
Plan: `eventual-whistling-rabbit.md`, T11 of T1–T16.
Depends on: T7 (`RegionSelect` / `useRegions`, TASK-654 on main) + TASK-649 (`Location.RegionCode`
entity + migration `20260831090731`, on main HEAD `32116c47`).

## Backend

- `Features/Locations/Dtos/LocationDtos.cs` — `RegionCode` (`string?`, nullable, trailing
  optional param) added to `LocationDto`, `CreateLocationRequest`, `UpdateLocationRequest`.
- `Features/Locations/LocationService.cs`
  - new `NormalizeRegionCode(string?)` helper: blank/null → `(null, null)`; trims and checks
    against `ShelfGuard.Domain.Constants.UkraineRegions.IsValid`; unknown → `(null, "Invalid
    region code '<code>'.")` (standard service 400 tuple, mirrors the `IsValidLocationType`
    message style right above it).
  - `CreateAsync` + `UpdateAsync`: validate after the location-type check, set
    `location.RegionCode` to the normalized value.
  - `ToDto`: maps `RegionCode` (named arg, so the pre-existing unmapped `LegalEntityId` was
    left untouched — out of scope).
- Controller / repository unchanged (JSON model-binding + EF handle the new column).

## Frontend

- `features/locations/types.ts` — `regionCode: string | null` on `LocationDto`.
- `features/locations/api/locations.ts` — `regionCode?: string | null` on `CreateLocationDto`
  + `UpdateLocationDto`.
- `features/locations/components/LocationFormDialog.tsx` — `<RegionSelect>` field, label
  hardcoded **"Область / місто"** (i18n deferred to TASK-659, `messages/*` untouched). Zod
  `regionCode: z.string().nullable().optional()`; bound via `watch`/`setValue` (RegionSelect
  is a controlled component, not `register`-compatible); added to `Props.onSubmit` shape,
  `defaultValues` (`null`), both `reset()` branches (`location.regionCode ?? null`), and the
  `onValid` → `onSubmit` payload.
- `app/(dashboard)/locations/page.tsx` — `regionCode` added to the `handleSubmit` values type
  and threaded into `create.mutate` (update branch already forwards the whole `values` object).

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings. Re-verified immediately before commit.
- `dotnet test` (filter Locations + UkraineRegions) — 90/90 pass.
- `npx tsc --noEmit` — clean. `npm run lint` — clean.
- Browser check skipped: full render needs backend + `GET /api/geo/regions` + auth/tenant
  context running; brief explicitly allows skipping. Field renders unconditionally
  (RegionSelect shows a disabled placeholder select until regions load).

## Follow-ups

- TASK-659 — swap the hardcoded "Область / місто" label for an i18n key.
- Pre-existing bug (not touched): `LocationService.ToDto` never maps `LegalEntityId`, so the
  edit form always resets the legal-entity field. Separate fix.
