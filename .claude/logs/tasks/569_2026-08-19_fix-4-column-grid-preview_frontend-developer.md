# TASK-569 — Fix 4-column grid preview clamped to 2 columns (web half)

**Status:** done · **Agent:** frontend-developer
**Companion:** mobile-developer fixed the same bug in `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx` in parallel (disjoint files, no coordination needed beyond the shared `23%` width value).

## Bug

Setting a Product Grid / Promotion Grid block's `columns` property to `4` in the web App Builder
(`/consumer-app/pages`) only showed 2 cards per row in the live preview, not 4. Every other value
worked. Backend's `BlockRegistry.cs` already correctly declared `columns` as `Int, Min: 2, Max: 4`
— no backend change needed.

## Root cause

`frontend/features/consumer-app/components/blockPreviews.tsx`:
1. `columns()` helper (line 112) only recognized `3`, clamping any other value (including `4`)
   down to `2`.
2. `PromotionGridPreview` (line 403) and `ProductGridPreview` (line 474) computed width with a
   binary ternary — `columns(...) === 3 ? "31%" : "48%"` — so there was no branch for `4`.

## Fix

- `columns()` widened to return `2 | 3 | 4`: `value === 4 ? 4 : value === 3 ? 3 : 2`.
- Both call sites changed to a 3-way check: `c === 4 ? "23%" : c === 3 ? "31%" : "48%"`.
- `23%` chosen to match the parallel mobile-developer fix to `CoreBlocks.tsx` (ADR-031 requires
  web preview / real app parity for the same `columns` value). Existing widths unchanged (2→48%,
  3→31%).

## Verification

- `npx tsc --noEmit` — clean.
- In-browser (frontend dev server, logged in as enterprise_admin, `/consumer-app/pages`): added a
  Product Grid block, opened its property editor.
  - `columns=4` → Apply → DOM geometry check: 12 catalog cards, all `width: 23%`, laid out in 3
    rows of 4 (`rowCounts: [4,4,4]`).
  - `columns=3` → 4 rows of 3 at `width: 31%` (`rowCounts: [3,3,3,3]`).
  - `columns=2` → 6 rows of 2 at `width: 48%` (`rowCounts: [2,2,2,2,2,2]`) — matches pre-fix
    behavior, zero regression.
  - Screenshot tool unavailable this session (Browser pane not compositing); verified instead via
    `getBoundingClientRect()` row-grouping through `javascript_tool`, which is unambiguous for a
    flex-wrap layout.
- Test block removed after verification; draft was never saved so no persisted state changed.

## Files changed

- `frontend/features/consumer-app/components/blockPreviews.tsx`
