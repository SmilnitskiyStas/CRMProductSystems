# TASK-607: Fix dashboard crash on sold_out / needs_verification status

**Agent:** frontend-developer
**Date:** 2026-08-24
**Status:** done

## Bug

`/dashboard` crashed with `TypeError: Cannot read properties of undefined (reading 'bg')`.
`ItemStatus` in `frontend/features/dashboard/types.ts` only declared 4 values, but the
backend's `StockStatus.Compute` (`backend/ShelfGuard.Application/Features/Stock/StockStatus.cs`)
can also emit `sold_out` and `needs_verification`. `getAttentionItems()` casts the raw
string unchecked, so those two values indexed a 4-key `Record<ItemStatus, {...}>` color
map and returned `undefined`, crashing on `.bg`.

## Changes

- `frontend/features/dashboard/types.ts` — widened `ItemStatus` to 6 values (added
  `sold_out`, `needs_verification`).
- `frontend/features/dashboard/components/AttentionTable.tsx` — added the 2 missing
  `STATUS_CONFIG` entries (colors reused from `features/shelf/types.ts` `STATUS_COLOR`),
  added `DEFAULT_STATUS_CONFIG` fallback, applied `?? DEFAULT_STATUS_CONFIG` at the lookup
  site (pattern from `CooperationBadges.tsx`). Also narrowed `FILTER_VALUES`'s type from
  `(ItemStatus | "all")[]` to the literal 4-value union it actually holds — the widened
  `ItemStatus` broke `stats[value]` indexing against `DashboardStats` (which only has 4
  keys) otherwise. Filter UI itself unchanged (still only All/Expired/Critical/Warning).
- `frontend/features/dashboard/components/StoreMap.tsx` — same pattern: 2 new
  `STATUS_CONFIG` entries + `DEFAULT_STATUS_CONFIG`, fallback applied at both lookup sites
  (legend loop + zone card loop).
- `frontend/messages/uk.json` and `en.json` — added `sold_out` / `needs_verification` keys
  to `Dashboard.dashboard.status` (copy matches the existing `shelf.status` block).

## Verification

- `npx tsc --noEmit` — clean (confirms both `Record<ItemStatus, {...}>` maps are
  exhaustive over all 6 values, and no other consumer broke).
- JSON validity checked for both locale files.
- Started `frontend-dev` (3001) + `backend-dev` (5000), logged into the real dashboard
  with live seed data (443 attention items: 409 expired/31 critical/3 warning) — renders
  correctly, no console crash.
- Confirmed via `/api/stock?status=sold_out|needs_verification` that current seed data has
  zero rows in either status, so the exact crash path couldn't be exercised against live
  data. Attempted to simulate it by mocking the `/api/stock` fetch response client-side;
  this repo's Next.js 14 dev server falls back to a hard page reload on client
  navigation ("Failed to fetch RSC payload... Falling back to browser navigation" —
  pre-existing dev-mode issue, unrelated to this fix) which wipes the patched `fetch`
  before a remount could pick it up, so the live-injection test wasn't achievable in the
  time available. Confidence instead rests on the `tsc` exhaustiveness check (compiler
  proves both maps cover all 6 `ItemStatus` values) plus the `?? DEFAULT_STATUS_CONFIG`
  runtime fallback as defense-in-depth for any future/unmapped value.
- `/stock` page (shelf feature) already has a working "Verification" filter for
  `needs_verification`, confirming the status is a real, reachable value in this domain.

## Out of scope (per brief, untouched)

- `FILTER_VALUES` UI buttons — still don't expose sold_out/needs_verification filters.
- `DashboardStats` type / stat-card counts.
- Auth/session code (confirmed not an auth bug).
- `frontend/features/shelf/*` (reference only).
