# TASK-563/564/565 — App Builder live preview panel (frontend)

**Status:** done · **Agent:** frontend-developer
Brief: `.claude/logs/tasks/560_2026-08-19_app-builder-live-preview-architecture_project-architect.md`
(TASK-563/564/565 sections). ADR: `.claude/docs/decisions.md` ADR-031.

## TASK-563 — shared `PhoneFrame.tsx`
New `frontend/features/consumer-app/components/PhoneFrame.tsx` — pure extraction of the phone
chrome (320px max-width, 8px black border, 28px radius, drop shadow) from `ThemeEditorSection.tsx`'s
`ThemePreview`. Takes `background`/`padding`/`children`; deliberately does not impose its own
`display:flex`/`gap` (that stayed with `ThemeEditorSection`'s own spacing-preset-driven inner
wrapper div) so the shell is reusable for any content. `ThemeEditorSection.tsx` now wraps its
header/card/pill/bottom-nav mock in `<PhoneFrame>` unchanged. Verified in-browser via computed
styles (`maxWidth: 320px`, `boxShadow` byte-identical, `padding: 24px` = `metrics.padding+8`) —
zero visual change.

## TASK-564 — block preview mirrors + `AppPreviewPanel`
New `frontend/features/consumer-app/components/blockPreviews.tsx` — one mirror component per the
12 `MobileConfigBlockType`s, matching `CoreBlocks.tsx`'s exact proportions (280/210/170px carousel
widths, 190px hero minHeight, 48%/31% grid widths), plus `renderBlockPreview` dispatch. Theme
tokens (`PreviewTokens`) are derived from the tenant's real `MobileThemeDto` using the same
`readableTextOn`/`mixWithBackground` formulas as `mobile/features/theme/tokens.ts`, not a fixed
palette. `loyaltyCard`/`loyaltyBalance` render illustrative sample data with a visible "приклад
даних" badge. `newsList` reuses banner data, mirroring `resolveBlocks.ts`'s own interim behavior.

New `frontend/features/consumer-app/components/AppPreviewPanel.tsx` — fetches `useMobileTheme`,
`useBanners`, `usePromoProducts(storeId)`, `useCatalogProducts`, `useLocations`; maps each DTO into
the preview item shapes the same way `resolveBlocks.ts` maps its own consumer DTOs (e.g.
`DiscountDto` joined to `CatalogProductDto` by `productId`, mirroring `PromoProductsSection.tsx`'s
existing join pattern). `storeId = locations?.[0]?.id ?? null` (ADR-031's preview-only convenience);
zero-location tenants render promotion blocks with an empty-state hint instead of crashing.
Page-agnostic: takes `blocks`/`registryByType` as props.

`AppBuilderCanvas.tsx` gained a third sticky column (`flex: "0 1 340px"`, same pattern as the
palette column) rendering `<AppPreviewPanel>`.

## TASK-565 — live unsaved-edit reflection + resize handles
`BlockPropertyEditor.tsx`: new optional `onLiveChange?: (props) => void`, fired from a `useEffect`
on `watch()`'s `values` — "Apply" still the only thing writing `configDoc`.

`AppBuilderCanvas.tsx`: `liveProps` state (reset on `selectedBlockId` change via effect), a
`previewBlocks` memo substituting the selected block's props with `liveProps` when present (passed
to `AppPreviewPanel` instead of raw `blocks`), and `updateBlockSizeProp` (single-key merge, same
shape as `updateBlockProps`) wired as `onResizeCommit`.

New `frontend/features/consumer-app/hooks/useResizeDrag.ts` — native Pointer Events +
`setPointerCapture` (wrapped in try/catch — some pointer sources reject capture), no drag library.
Shows the live in-drag value via local state; commits exactly once on `pointerup`/`pointercancel`,
clamped to `[min, max]` read from `registryByType`'s `validationSchema`.

`blockPreviews.tsx`: grab handles added to the 4 resizable previews (hero banner bottom edge for
`heightPx`, the 3 carousel variants' right edge for `cardWidthPx`) — grid variants untouched, per
scope.

## Verification
- `npx tsc --noEmit` clean throughout (checked after each task and at the end).
- `eslint` on all touched files: 0 errors, 2 pre-existing-style warnings (one `no-img-element`
  matching this feature's established opt-out convention, one `exhaustive-deps` on a `?? []`
  fallback array — not introduced by the new logic's correctness).
- Live browser verification (`ea@demo.local` on the seeded dev tenant, `/consumer-app/design` and
  `/consumer-app/pages`):
  - Design page: `PhoneFrame`'s computed styles are byte-identical to pre-refactor (320px/28px/8px
    border/exact `boxShadow`/24px padding).
  - Pages screen: preview column renders real data (Hero Banner title, Loyalty Card sample-data
    badge); add/remove of a Banner Carousel block updated the preview panel in the same render,
    including its empty-state hint when the tenant has no active banners.
  - Typing a new title in the drawer updated the preview instantly (before Apply); clicking Cancel
    reverted the preview to the last-applied value and left `dirty`/"No unsaved changes" untouched.
  - Hero Banner's height handle: dragged +40px → committed to 230px; dragged far past each bound →
    clamped to exactly 120px (min) and 260px (max); `dirty`/Save-button state changed exactly once
    per gesture (unchanged during every intermediate `pointermove`, flipped only on `pointerup`).
  - Banner Carousel's width handle rendered at the registry default (280px) as soon as the block
    was added.

No backend/mobile changes in this session (TASK-561/562 were already `done` going in). No new
tests added — this feature area has no frontend unit test suite yet (pre-existing state, not
introduced here).
