# TASK-668 — mobile coverage display for the new structured delivery fields

**Agent:** mobile-developer · **Status:** done (committed to main) · **Date:** 2026-09-01
**Scope:** Mobile only. Read-only display. Follows TASK-665 (backend, commit de8f1632).

## What changed

### `mobile/features/geo/types.ts`
`DeliveryCoverageEntry` served-entry shape updated to mirror the backend
`DeliveryCoverageEntryDto` from TASK-665:

```
terms: string | null
  →
deliveryDaysMin: number | null
deliveryDaysMax: number | null
minOrderAmount:  number | null
note:            string | null
```

`DeliveryCoverage.note` (global) unchanged. `DeliveryCoverageEntry` lives here, not in
`mobile/features/marketplace/types.ts` (that file only re-imports `DeliveryCoverage`), so the
type edit landed here despite the task naming `marketplace/types.ts`.

### `mobile/app/(app)/marketplace/[id].tsx`
- New local helper `formatDeliveryTerms(entry: DeliveryCoverageEntry): string` — flattens the
  structured fields into one Ukrainian line (project convention: hardcoded UA strings):
  - both min+max → `"1–3 дні"` (min === max → `"2 дн."`)
  - min only → `"від 2 днів"`
  - max only → `"до 3 днів"`
  - neither → skipped
  - `minOrderAmount` → `"від 5000 грн"` (insignificant decimals dropped)
  - present parts joined with `" · "`; nothing present → `"за домовленістю"`
  - defensive reversed-pair swap (backend already normalises, but the wire is not trusted)
- `DeliveryCoverageBlock` "Регіони доставки" served list now renders `formatDeliveryTerms(entry)`
  instead of `entry.terms`, and shows the per-region `entry.note` on its own muted italic line
  below the region row when present. Served-list gap bumped `gap-1` → `gap-2` for the now
  multi-line entries.
- Import updated: `DeliveryCoverageEntry` added alongside `DeliveryCoverage`.

No mobile `features/*/lib/` pattern exists, so the helper is local to the screen file as the
task specified.

## Verification
- `cd mobile && npm run type-check` (`tsc --noEmit`) — clean.
- `npx eslint` on both touched files — 0 errors (1 pre-existing unrelated warning: `FlatList`
  imported-but-unused in `[id].tsx`, not introduced here).

## Notes
- A concurrent session is actively working the web sibling of this task: uncommitted
  `frontend/features/geo/types.ts`, `frontend/features/geo/components/DeliveryCoverageEditor.tsx`,
  `frontend/features/marketplace/components/{CooperationCoveragePanel,SupplierCoveragePanel}.tsx`,
  `frontend/features/marketplace/types.ts`, `frontend/messages/{en,uk}.json`, plus new
  `frontend/features/geo/lib/formatDeliveryTerms.ts(.test.ts)`. None touched. The mobile helper
  mirrors that web helper's logic (minus i18n).
- No uncommitted changes found under `mobile/features/marketplace/` or
  `mobile/app/(app)/marketplace/`. Pre-existing unrelated `M mobile/features/pos/receiptPrinting.ts`
  left untouched and unstaged.

## Commit
`feat(mobile/marketplace): structured per-region delivery fields in coverage display (TASK-668)`
