# TASK-654 (T7) — Shared `frontend/features/geo/` components

**Status:** done · **Agent:** frontend-developer · **Model:** sonnet
**Branch:** main (main working tree) · **Depends on:** T1 (`GET /api/geo/regions`, backend, concurrent)

## Scope

New shared feature `frontend/features/geo/` — the single client-side home for the
Ukraine region taxonomy. No hard-coded region list anywhere: everything reads
`useRegions()` → `GET /api/geo/regions`. Only `frontend/` touched (backend T1 builds
the endpoint concurrently). No changes to `frontend/features/marketplace/*` or
`frontend/messages/*` — wiring + i18n are T8–T12.

## Files created

| File | Contents |
|---|---|
| `frontend/features/geo/types.ts` | `Region` (= backend `RegionDto`), `DeliveryCoverageEntry`, `DeliveryCoverage` |
| `frontend/features/geo/api/geo-api.ts` | `geoApi.getRegions(): Promise<Region[]>` via shared `@/lib/api` (marketplace-api idiom) + `getRegions` alias |
| `frontend/features/geo/hooks/useRegions.ts` | `useRegions()` (React Query, `queryKey: ["geo","regions"]`, `staleTime: Infinity`); `useRegionLabel(): (code) => string`; `GEO_KEYS` |
| `frontend/features/geo/lib/regionLabel.ts` | pure `regionLabel(code, regions)`; `groupRegions(regions): { oblast, cities }[]` (Intl.Collator "uk" sort, orphan cities dropped) |
| `frontend/features/geo/components/RegionSelect.tsx` | controlled single `<select>`, `<optgroup>` per oblast, oblast as first option + nested cities (NBSP-indented) |
| `frontend/features/geo/components/RegionMultiSelect.tsx` | controlled grouped checkbox checklist, scrollable (maxHeight 240), `disabledCodes`, "виберіть область/місто" hint when empty |
| `frontend/features/geo/components/DeliveryCoverageEditor.tsx` | served MultiSelect + per-region `terms` input; notServed MultiSelect (mutually exclusive via `disabledCodes`); `note` textarea; emits fully-formed `DeliveryCoverage` on every change |

## Component prop signatures

```ts
RegionSelect({ value: string | null; onChange: (code: string | null) => void;
               placeholder?: string; allowEmpty?: boolean })   // allowEmpty defaults true

RegionMultiSelect({ value: string[]; onChange: (codes: string[]) => void;
                    disabledCodes?: string[] })

DeliveryCoverageEditor({ value: DeliveryCoverage | null;
                         onChange: (v: DeliveryCoverage) => void })
```

## Styling

Inline `style={{...}}` objects, marketplace dark palette (`#0F1623` / `#1F2937` /
`#374151` / `#E8EDF5`), matching `CooperationRequestModal` `selectStyle` /
`SupplierFilters` `inputStyle`. No Tailwind classes.

## Notes / decisions

- `geo-api.ts` exports both `geoApi` (object, mirrors `marketplaceApi`) and a bare
  `getRegions` alias — the plan names the bare function, the codebase idiom is the object.
- `groupRegions` drops cities whose `parentCode` matches no oblast in the payload
  (defensive; shouldn't happen with a well-formed registry).
- `DeliveryCoverageEditor` normalises empty `terms` / `note` to `null` and always
  emits `{ served, notServed, note }` (never partial / never `null`).
- No browser verification: components aren't mounted on any page yet and the backend
  endpoint is still being built (T1). Wiring happens in T8–T10.

## Verification

`cd frontend && npx tsc --noEmit` — clean.
`npm run lint` — "No ESLint warnings or errors".
