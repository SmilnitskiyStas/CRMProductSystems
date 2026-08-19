# TASK-562 — Mobile: consume heightPx/cardWidthPx + fix resolveBlocks prop-forwarding gap

**Status:** done
**Agent:** mobile-developer

## What changed

- `mobile/features/server-driven-ui/blocks/types.ts` — added optional `heightPx?: number` to
  `HeroBannerProps`, optional `cardWidthPx?: number` to `BannerCarouselProps`,
  `PromotionCollectionProps`, `ProductCollectionProps`.
- `mobile/features/server-driven-ui/blocks/validators.ts` — added `finiteNumber(value.heightPx)` /
  `finiteNumber(value.cardWidthPx)` checks (optional) to the 4 corresponding type guards.
- `mobile/features/server-driven-ui/resolveBlocks.ts` — **load-bearing fix**: `bannerCarousel`,
  `promotionCarousel`/`promotionGrid`, `productCarousel`/`productGrid` cases now explicitly forward
  `cardWidthPx` from raw authored `props` into the rebuilt props literal (was silently dropped
  before — confirmed `heroBanner` needed no change, it already passes through via
  `default: return block`).
- `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx` — `HeroBannerBlock` now uses
  `heightPx ?? 190`; `BannerCarouselBlock`/`PromotionCarouselBlock`/`ProductCarouselBlock` now use
  `cardWidthPx ?? {280,210,170}` respectively. `PromotionGridBlock`/`ProductGridBlock` untouched
  (percent-based width, out of scope). Added `testID`s to the carousel card `View`s
  (`banner-card-*`, `promotion-card-*`, `product-card-*`) so tests can assert on actual rendered
  pixel width — none existed before beyond the hero banner's own `block-${id}`.
- Tests: `__tests__/resolveBlocks.test.ts` — new cases confirm `cardWidthPx` survives
  `resolveBlock()` for all 3 carousel types (custom value and undefined-when-absent), plus
  `heightPx` passthrough on `heroBanner`. `__tests__/coreBlocks.test.tsx` — new render-based cases
  assert custom `heightPx`/`cardWidthPx` render at the authored value, and blocks without them
  render at exactly today's hardcoded defaults (190/280/210/170) — the zero-regression guard.
  Also extended the existing validator test with accept/reject cases for the two new optional
  numeric props.

## Verification

- `npx tsc --noEmit` — clean.
- `npx jest` (full mobile suite) — 54 suites / 255 tests passed, including all new cases.
- Manually confirmed via the new regression-guard test: a block with no `heightPx`/`cardWidthPx`
  in `props` renders at exactly today's pixel values (190/280/210/170) — no visual change to
  existing saved configs.

No blockers, no open questions.
