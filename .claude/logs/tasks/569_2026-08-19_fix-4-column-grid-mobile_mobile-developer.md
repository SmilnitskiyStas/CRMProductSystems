# TASK-569 — Fix 4-column grid preview clamped to 2 columns (mobile half)

**Status:** done · **Agent:** mobile-developer
**Companion:** frontend-developer fixed the same bug in `frontend/features/consumer-app/components/blockPreviews.tsx` in parallel (disjoint files, coordinated only on the shared `23%` width value). Log (web): `.claude/logs/tasks/569_2026-08-19_fix-4-column-grid-preview_frontend-developer.md`

## Bug

Retailer admin: setting a Product Grid / Promotion Grid block's `columns` to `4` in the web App
Builder only showed 2 cards per row in the app, not 4. Backend's `BlockRegistry.cs` already
correctly declares `columns` as `Int, Min: 2, Max: 4` — no backend change needed.

## Root cause

Four separate 2-or-3 hardcodes in `mobile/features/server-driven-ui`:

1. `blocks/validators.ts` `validColumns` — type guard was `value is 2 | 3 | undefined`, so a
   published block with `columns: 4` **failed prop validation entirely**. Per `BlockRenderer.tsx`'s
   existing behavior (warn + render `null`), the whole block silently disappeared on real devices —
   worse than wrong layout.
2. `blocks/types.ts` — `columns?: 2 | 3` on both `PromotionCollectionProps` and
   `ProductCollectionProps`.
3. `resolveBlocks.ts` `columns()` helper — `value === 3 ? 3 : 2`, silently clamped `4` to `2` before
   `CoreBlocks.tsx` ever saw it.
4. `blocks/CoreBlocks.tsx` — `PromotionGridBlock`/`ProductGridBlock` computed
   `block.props.columns === 3 ? '31%' : '48%'`, a binary ternary with no branch for `4` (the literal
   cause of "shows only 2 cards" once items 1–3 above were bypassed, e.g. via `resolveBlock`
   defaults).

## Fix

- `validators.ts`: `validColumns` widened to `value is 2 | 3 | 4 | undefined`, accepts `4`.
- `types.ts`: both `columns?: 2 | 3` widened to `columns?: 2 | 3 | 4`.
- `resolveBlocks.ts`: `columns()` widened to `value === 3 ? 3 : value === 4 ? 4 : 2`, return type
  `2 | 3 | 4`.
- `CoreBlocks.tsx`: both grid width ternaries changed to
  `columns === 4 ? '23%' : columns === 3 ? '31%' : '48%'`.
- `23%` matches the parallel web fix (ADR-031 requires web-preview/real-app parity for the same
  `columns` value). Existing widths unchanged: 2 → `48%`, 3 → `31%`.

## Tests

Added to `__tests__/coreBlocks.test.tsx`:
- validators accept `columns: 4` for both promotion and product collection props.
- `PromotionGridBlock`/`ProductGridBlock` render cards at `width: '23%'` when `columns: 4`.

Added to `__tests__/resolveBlocks.test.ts`:
- `resolveBlock` forwards `columns: 4` unchanged for both `promotionGrid` and `productGrid`.

No existing test asserted "4 is rejected" (checked before adding) — purely additive.

## Verification

- `npx jest server-driven-ui`: 3 suites, 35/35 passing (was 30 before this change).
- `npx tsc --noEmit` (mobile): clean.
- Read the full diff: `columns: 2`, `columns: 3`, and `columns: undefined` all still resolve to
  today's exact widths (`48%`/`31%`) through validators → resolveBlocks → CoreBlocks — zero
  regression.

## Files changed

- `mobile/features/server-driven-ui/blocks/validators.ts`
- `mobile/features/server-driven-ui/blocks/types.ts`
- `mobile/features/server-driven-ui/resolveBlocks.ts`
- `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx`
- `mobile/features/server-driven-ui/__tests__/coreBlocks.test.tsx`
- `mobile/features/server-driven-ui/__tests__/resolveBlocks.test.ts`
