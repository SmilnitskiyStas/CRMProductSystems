# TASK-560 — App Builder Live Preview: Architecture + Task Breakdown

**Status:** done
**Agent:** project-architect

ADR: `.claude/docs/decisions.md` ADR-031. Doc updates: `.claude/docs/domain-model.md` (Block
Registry section — new size props). `architecture.md` deliberately not touched — no layer/module/
endpoint change, see ADR-031 Consequences. Task registrations: `.claude/tasks/current.md`.
Handoff: `.claude/logs/handoffs/560-to-backend-mobile-frontend_project-architect.md`.

## Decision summary (full reasoning in ADR-031)

Both user-supplied architecture calls are **confirmed as scoped**:
1. Web-native mirror components in `frontend/features/consumer-app/`, not react-native-web reuse.
2. Resize limited to 4 block types with a real fixed dimension and no prop for it today:
   `heroBanner` (height), `bannerCarousel`/`promotionCarousel`/`productCarousel` (card width).
   `promotionGrid`/`productGrid`'s existing `columns` prop is untouched, out of scope.

New `BlockPropDefinition` entries (bounds bracket today's hardcoded value so the default renders
byte-identical to every already-saved config):

| Block type | Prop | Default | Min | Max |
|---|---|---|---|---|
| `heroBanner` | `heightPx` | 190 | 120 | 260 |
| `bannerCarousel` | `cardWidthPx` | 280 | 200 | 360 |
| `promotionCarousel` | `cardWidthPx` | 210 | 150 | 270 |
| `productCarousel` | `cardWidthPx` | 170 | 120 | 220 |

Card image height inside carousel cards (130px banners, 120px promotion/product default) is
**not** tied to the new width — width-only resize, no aspect-ratio cascade.

**Zero new backend endpoints.** Preview is 100% client-side: `AppBuilderCanvas.tsx` already holds
the full draft document in memory pre-save; the preview reads that plus existing
`AtLeastEnterpriseAdmin`-gated GETs (`useBanners`, `usePromoProducts`, `useCatalogProducts`,
`useLocations`, `useMobileTheme`, `useBlockRegistry`) the admin already has access to.
`MobileConfigPreviewController`/`Service` (TASK-547, reads the last *saved* DB draft) plays no role
here — it is strictly staler than the in-memory `configDoc`.

**Correctness finding that gates the mobile task:** `mobile/features/server-driven-ui/
resolveBlocks.ts`'s `resolveBlock()` rebuilds the `props` object for `bannerCarousel`,
`promotionCarousel`/`promotionGrid`, `productCarousel`/`productGrid` — any static authored prop not
explicitly copied into the new literal is silently dropped. `heroBanner` has no `case` (falls to
`default: return block`, unchanged) so `heightPx` passes through for free; the 3 carousel types do
**NOT** get `cardWidthPx` for free and `resolveBlocks.ts` must be edited to forward it, or the prop
will visibly work in the web preview and silently no-op on real phones.

## Task breakdown

Dependency graph:
```
TASK-561 (backend)  ──┬──► TASK-562 (mobile)                    [independent branch]
                       └──► TASK-565 (needs registry bounds)
TASK-563 (frontend) ──► TASK-564 (frontend) ──► TASK-565 (frontend) ──► TASK-566 (qa, needs all)
```
TASK-561/562/563 can start immediately and in parallel. No git worktree isolation needed —
backend (`backend/`), mobile (`mobile/`), frontend (`frontend/`) are disjoint trees with zero file
overlap; TASK-563→564→565 is a sequential chain within one agent's session (frontend files
overlap, so hand these three to a single frontend-developer spawn working them in order, not three
concurrent spawns).

---

### TASK-561 — Block Registry: 4 new resizable size props
**Agent:** backend-developer

**File:** `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockRegistry.cs`

Add one `BlockPropDefinition` to each of 4 `Props` lists (append at the end of each list, matching
the existing `new("limit", BlockPropTypes.Int, Required: false, Default: 5, Min: 1, Max: 10)` call
shape already used throughout the file):

```csharp
// heroBanner's Props list — after "ctaLink":
new("heightPx", BlockPropTypes.Int, Required: false, Default: 190, Min: 120, Max: 260),

// bannerCarousel's Props list — after "autoPlay":
new("cardWidthPx", BlockPropTypes.Int, Required: false, Default: 280, Min: 200, Max: 360),

// promotionCarousel's Props list — after "cardStyle":
new("cardWidthPx", BlockPropTypes.Int, Required: false, Default: 210, Min: 150, Max: 270),

// productCarousel's Props list — after "showViewAll":
new("cardWidthPx", BlockPropTypes.Int, Required: false, Default: 170, Min: 120, Max: 220),
```

Do **not** touch `promotionGrid`/`productGrid` (they keep only `columns`), and do not touch
`MobileConfigWhitelists.cs` or `MobileConfigValidator.cs` — block `props` stays free-form JSON at
save-time by design (see that class's own doc comment on the deferred props-validation decision);
adding a registry entry only affects what `GET /api/v1/mobile/blocks` advertises to the admin UI.

**File:** `backend/ShelfGuard.Tests/MobileConfig/BlockRegistryTests.cs`

Existing generic theory tests (`Every_prop_has_a_known_type_and_non_empty_name`,
`DefaultProps_is_derived_from_Props_defaults_for_every_definition`) already cover any new prop with
no changes needed. Add one new focused test protecting the exact bounds table above, e.g.:

```csharp
[Theory]
[InlineData("heroBanner", "heightPx", 190, 120, 260)]
[InlineData("bannerCarousel", "cardWidthPx", 280, 200, 360)]
[InlineData("promotionCarousel", "cardWidthPx", 210, 150, 270)]
[InlineData("productCarousel", "cardWidthPx", 170, 120, 220)]
public void Resizable_block_types_declare_their_new_size_prop_with_expected_bounds(
    string blockType, string propName, int expectedDefault, int expectedMin, int expectedMax)
{
    var def = BlockRegistry.Definitions.Single(d => d.Type == blockType);
    var prop = def.Props.Single(p => p.Name == propName);
    Assert.Equal(BlockPropTypes.Int, prop.Type);
    Assert.Equal(expectedDefault, prop.Default);
    Assert.Equal(expectedMin, prop.Min);
    Assert.Equal(expectedMax, prop.Max);
    Assert.False(prop.Required);
}
```

**Acceptance criteria:**
- `dotnet build` clean, `dotnet test --filter MobileConfig` full pass including the new test.
- `GET /api/v1/mobile/blocks` response (manually verified or via existing controller test) shows
  the 4 new entries in `validationSchema` for their respective block types.
- No changes outside `BlockRegistry.cs` + its test file (confirmed no validator/whitelist edits
  needed).
- Task log per convention; no handoff doc needed (mobile/frontend consume via this log + TASK-560,
  not a live API dependency at build time).

---

### TASK-562 — Mobile: consume heightPx/cardWidthPx + fix resolveBlocks prop-forwarding gap
**Agent:** mobile-developer
**Depends on:** TASK-561 (mirrors its exact prop names/bounds; not a runtime dependency — mobile
never calls `GET /api/v1/mobile/blocks`, it only reads `props.heightPx`/`cardWidthPx` off the
published config JSON, so this can start once TASK-561's table above is confirmed, without
waiting for a merged PR).

**File:** `mobile/features/server-driven-ui/blocks/types.ts`
Add optional fields:
```ts
export interface HeroBannerProps {
  // ...existing fields...
  heightPx?: number;
}
export interface BannerCarouselProps {
  items: BannerItem[];
  cardWidthPx?: number;
}
export interface PromotionCollectionProps {
  // ...existing fields...
  cardWidthPx?: number; // only meaningful for promotionCarousel; promotionGrid ignores it
}
export interface ProductCollectionProps {
  // ...existing fields...
  cardWidthPx?: number; // only meaningful for productCarousel; productGrid ignores it
}
```

**File:** `mobile/features/server-driven-ui/blocks/validators.ts`
Add `finiteNumber(value.heightPx)` / `finiteNumber(value.cardWidthPx)` checks (optional, so pass
`required=false`, the default) to `isHeroBannerProps`, `isBannerCarouselProps`,
`isPromotionCollectionProps`, `isProductCollectionProps` — same pattern already used for
`finiteNumber(value.balance, true)` etc. in this file.

**File:** `mobile/features/server-driven-ui/resolveBlocks.ts` — the load-bearing fix (see ADR-031's
correctness finding above). In each `case` that rebuilds `props`, explicitly forward the new field
from the raw authored `props` through to the resolved object:
```ts
case 'bannerCarousel': {
  const limit = positiveInt(props.limit, 5, 10);
  return { ...block, props: {
    items: data.banners.slice(0, limit).map(...),
    cardWidthPx: typeof props.cardWidthPx === 'number' ? props.cardWidthPx : undefined,
  } };
}
case 'promotionCarousel':
case 'promotionGrid': {
  // ...existing...
  return { ...block, props: { title: props.title, showViewAll: props.showViewAll,
    columns: columns(props.columns),
    cardWidthPx: typeof props.cardWidthPx === 'number' ? props.cardWidthPx : undefined,
    items: ... } };
}
case 'productCarousel':
case 'productGrid': {
  // same cardWidthPx forwarding added to the shared literal
}
```
(`heroBanner` needs no change — already passes through via `default: return block`.)

**File:** `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx`
- `HeroBannerBlock`: destructure `heightPx` from `block.props`; `minHeight: 190` →
  `minHeight: heightPx ?? 190`.
- `BannerCarouselBlock`: card `View` currently hardcodes `width: 280` — change to
  `width: block.props.cardWidthPx ?? 280`.
- `PromotionCarouselBlock`: currently calls `<PromotionCard key={item.id} item={item} width={210} />`
  — change the literal `210` to `block.props.cardWidthPx ?? 210`. Leave
  `PromotionGridBlock`'s percent-based `width` call untouched.
- `ProductCarouselBlock`: same pattern, literal `170` → `block.props.cardWidthPx ?? 170`. Leave
  `ProductGridBlock` untouched.

**Files:** `mobile/features/server-driven-ui/__tests__/coreBlocks.test.tsx` and
`__tests__/resolveBlocks.test.ts` — add cases: (a) a block with the new prop set renders/resolves
the custom value, (b) a block without it falls back to today's exact default (regression guard for
old saved configs), (c) `resolveBlocks.test.ts` specifically covers that `cardWidthPx` survives
`resolveBlock()` for all 3 carousel types (this is the test that would have caught the
prop-forwarding gap).

**Acceptance criteria:**
- `npm test` (mobile) green, including new cases.
- Manually confirm (read the diff) that a block instance with no `heightPx`/`cardWidthPx` in
  `props` renders with exactly today's pixel values — zero visual regression on existing configs.
- Task log per convention.

---

### TASK-563 — Frontend: extract shared `PhoneFrame.tsx`
**Agent:** frontend-developer
**Depends on:** nothing — start immediately.

**New file:** `frontend/features/consumer-app/components/PhoneFrame.tsx`
Extract the phone-chrome markup currently inlined in `ThemeEditorSection.tsx`'s `ThemePreview`
(lines ~521–538: `width: "100%", maxWidth: 320, margin: "0 auto", borderRadius: 28,
border: "8px solid #000", boxShadow: "0 12px 30px rgba(0,0,0,0.35)"`) into a reusable component
taking `background` (the phone's screen background color) and `children`, e.g.:
```ts
interface PhoneFrameProps {
  background: string;
  padding?: number;
  children: React.ReactNode;
}
export function PhoneFrame({ background, padding = 16, children }: PhoneFrameProps) { ... }
```
Keep the exact same visual constants (320 max-width, 28px radius, 8px black border, same
box-shadow) — this is a pure extraction, not a redesign.

**Edit:** `frontend/features/consumer-app/components/ThemeEditorSection.tsx` — replace
`ThemePreview`'s inline chrome `div` with `<PhoneFrame background={background} padding={metrics.padding + 8}>...</PhoneFrame>`,
keeping every child element (header/card/pill/bottom-nav mock) exactly as-is.

**Acceptance criteria:**
- `npx tsc --noEmit` clean.
- Visual diff of `/consumer-app/design`'s preview panel: zero change (same pixels) before/after —
  spot-check in the browser preview tool.
- Task log per convention.

---

### TASK-564 — Frontend: block preview mirror components + AppPreviewPanel (read-only column)
**Agent:** frontend-developer
**Depends on:** TASK-563 (uses `PhoneFrame`).

**New file:** `frontend/features/consumer-app/components/blockPreviews.tsx` — one plain
React/inline-style mirror component per the 12 `MobileConfigBlockType`s (matches this feature's
established single-file-per-concern convention, e.g. `CoreBlocks.tsx`'s own flat structure), plus
a dispatch function `renderBlockPreview(block: MobileConfigBlockInstance, ctx: PreviewContext)`.
Match `CoreBlocks.tsx`'s exact current proportions (this is the "not a lie" requirement):
- `HeroBannerPreview` — `minHeight: props.heightPx ?? 190`, background image or primary-color
  fallback, title/subtitle/CTA from static props directly (no data hook).
- `BannerCarouselPreview` — horizontal-scroll row, card `width: props.cardWidthPx ?? 280`, image
  height 130, from `ctx.banners` (see data wiring below), sliced by `props.limit`.
- `LoyaltyCardPreview` / `LoyaltyBalancePreview` — **sample data**, clearly marked (small caption,
  e.g. "приклад даних" / "sample data" — an admin has no consumer session, so this must not look
  like a real balance). Use illustrative values (e.g. balance 1250, card `•••• 4821`, tier
  "Срібний").
- `PromotionCarouselPreview` — card width `props.cardWidthPx ?? 210`, from `ctx.promotions`.
- `PromotionGridPreview` — percent widths `columns === 3 ? '31%' : '48%'` (mirror
  `CoreBlocks.tsx`'s `PromotionGridBlock` exactly), from `ctx.promotions`.
- `ProductCarouselPreview` — card width `props.cardWidthPx ?? 170`, from `ctx.catalog`.
- `ProductGridPreview` — percent widths, same columns rule, from `ctx.catalog`.
- `SectionHeaderPreview` — static props only (title/subtitle/alignment).
- `QuickActionsPreview` — static `actions: string[]`, label lookup via a small local
  `actionLabels` map mirroring `resolveBlocks.ts`'s own (home/promotions/catalog/loyalty/coupons/
  stores/news/profile → Ukrainian labels) — do not invent different labels.
- `NewsListPreview` — reuses `ctx.banners` (mirrors mobile's own current interim behavior in
  `resolveBlocks.ts`'s `newsList` case — see ADR-031, this keeps the preview truthful to what
  mobile actually renders today, not a nicer-looking invented placeholder).
- `StoreListPreview` — from `ctx.locations`.

**New file:** `frontend/features/consumer-app/components/AppPreviewPanel.tsx`
- Fetches `useMobileTheme()` (chrome background/primary color for `PhoneFrame` + block styling),
  `useBanners()`, `usePromoProducts(storeId)`, `useCatalogProducts()`, `useLocations()`.
- **`storeId` for `usePromoProducts`:** App Builder has no store selector. Use the tenant's first
  `useLocations()` result (`locations?.[0]?.id ?? null`) — preview-only convenience per ADR-031;
  if the tenant has zero locations yet, render the promotion blocks' empty-state gracefully
  (`usePromoProducts` already handles `storeId: null` via its `enabled: !!storeId` guard — just
  pass an empty items array to the preview components in that case, don't crash).
- Maps each hook's DTOs into the small item shapes `blockPreviews.tsx` expects (id/title/
  imageUrl/etc. — mirror `resolveBlocks.ts`'s own field mapping for the equivalent block type,
  e.g. `BannerDto.description` → preview `subtitle`, `DiscountDto.discountPercent` → preview
  `badge`, matching exactly what mobile shows for the same data so admin and consumer never
  diverge in *content*, only in visual chrome).
- Renders `<PhoneFrame background={theme.backgroundColor}>` wrapping a scrollable list of
  `renderBlockPreview(block, ctx)` for every block in the page currently being edited.
- Props: accepts `blocks: MobileConfigBlockInstance[]` (NOT the whole `configDoc` — keep this
  component page-agnostic, `AppBuilderCanvas` decides which page's blocks to pass) and
  `registryByType` (for later use by TASK-565's resize handles).

**Edit:** `frontend/features/consumer-app/components/AppBuilderCanvas.tsx`
Add a third sticky column after the existing canvas column, inside the same
`display: "flex", gap: 20` row (palette `flex: "0 1 300px"`, canvas `flex: "1 1 420px"` — add
preview `flex: "0 1 340px", position: "sticky", top: 20`, matching the palette's existing sticky
pattern):
```tsx
<div style={{ flex: "0 1 340px", position: "sticky", top: 20 }}>
  <AppPreviewPanel blocks={blocks} registryByType={registryByType} />
</div>
```
`blocks` and `registryByType` already exist as local variables in this component — no new state
needed for this task (add/remove/reorder already re-render `blocks` instantly; TASK-540's existing
`updateBlockProps` on Apply already re-renders it too — this task's column already reflects both
"live" simply by being mounted, since it reads the same `blocks` array).

Known, accepted tradeoff (do not attempt to fix): `BlockPropertyEditor`'s `DetailDrawer` is a
fixed right-edge overlay up to 520px wide and will visually overlap this new preview column on
narrower viewports while the drawer is open. Not in scope.

**Acceptance criteria:**
- `npx tsc --noEmit` clean.
- Manually verified in the browser preview tool: adding a block from the palette, removing one,
  and dragging to reorder all update the preview column within the same render (no visible lag,
  no separate "refresh" action).
- Applying a property edit (drawer's "Apply" button) updates the preview.
- `loyaltyCard`/`loyaltyBalance` preview content is visibly labeled as sample data.
- Task log per convention.

---

### TASK-565 — Frontend: live unsaved-edit reflection + resize drag handles
**Agent:** frontend-developer
**Depends on:** TASK-561 (registry bounds via `useBlockRegistry()`), TASK-564.

**Edit:** `frontend/features/consumer-app/components/BlockPropertyEditor.tsx`
Add an optional prop `onLiveChange?: (props: Record<string, unknown>) => void` to
`BlockPropertyEditorProps`. Add, right after the existing `const values = watch();` line:
```ts
useEffect(() => {
  onLiveChange?.(values);
}, [values, onLiveChange]);
```
This fires on every keystroke/toggle before "Apply" — matches the existing `watch()`-based live
pattern `ThemeEditorSection.tsx` already uses for its own preview panel. Do not otherwise change
this component's persistence behavior — "Apply" still writes into `configDoc` exactly as today.

**Edit:** `frontend/features/consumer-app/components/AppBuilderCanvas.tsx`
- New state: `const [liveProps, setLiveProps] = useState<Record<string, unknown> | null>(null)`.
  Reset to `null` whenever `selectedBlockId` changes (block selected/deselected) — e.g. in
  `setSelectedBlockId`'s existing call sites, or a small `useEffect` keyed on `selectedBlockId`.
- Compute `previewBlocks` (pass this to `AppPreviewPanel` instead of raw `blocks`):
  ```ts
  const previewBlocks = useMemo(
    () => blocks.map((b) => (b.id === selectedBlockId && liveProps ? { ...b, props: liveProps } : b)),
    [blocks, selectedBlockId, liveProps],
  );
  ```
- Wire `<BlockPropertyEditor ... onLiveChange={setLiveProps} />`.
- New handler for resize-commit (called by the drag handles below), merging one prop key without
  disturbing the rest — same shape as `updateBlockProps` but single-field:
  ```ts
  function updateBlockSizeProp(id: string, propName: string, value: number) {
    setConfigDoc((doc) => doc ? withPageBlocks(doc, activePage, (current) =>
      current.map((b) => b.id === id ? { ...b, props: { ...b.props, [propName]: value } } : b)) : doc);
    setDirty(true);
    setSaveError(null);
  }
  ```
  Pass this down to `AppPreviewPanel` as `onResizeCommit`.

**New file:** `frontend/features/consumer-app/hooks/useResizeDrag.ts` — small reusable
pointer-event hook, no new drag library (per user's explicit decision — `@dnd-kit` is for the
canvas reorder only, not appropriate for single-axis numeric drag):
```ts
// Tracks pointerdown/pointermove/pointerup on a handle element. Reports live delta during drag
// (for immediate visual feedback via local component state in the preview) and calls onCommit
// once, clamped to [min, max], on pointerup. Does not touch configDoc itself — that's the
// caller's job via onCommit.
```
Exact signature left to the implementer; must expose enough to (a) show the dragged size live in
the preview card without touching `configDoc`, (b) clamp to registry `min`/`max`, (c) call a
commit callback exactly once per drag gesture on pointerup (not on every pointermove).

**Edit:** `frontend/features/consumer-app/components/blockPreviews.tsx`
For the 4 resizable preview components (`HeroBannerPreview`, `BannerCarouselPreview`,
`PromotionCarouselPreview`, `ProductCarouselPreview`), add a visible grab handle (bottom edge for
`heightPx`, right edge for `cardWidthPx`) using `useResizeDrag`. Look up `min`/`max` from
`ctx.registryByType.get(block.type)?.validationSchema.find((d) => d.name === 'heightPx' | 'cardWidthPx')`
(already fetched by the existing `useBlockRegistry()` call in `AppBuilderCanvas.tsx`, threaded
through via `AppPreviewPanel`'s existing `registryByType` prop from TASK-564). Local component
state holds the in-drag value for immediate visual feedback; `onResizeCommit(block.id, propName,
clampedValue)` fires once on pointerup only — per the user's explicit requirement, `dirty`/save
state must not churn on every pixel of movement.

**Acceptance criteria:**
- `npx tsc --noEmit` clean.
- Typing in the property drawer (before clicking Apply) visibly updates the preview panel in the
  same render pass.
- Closing the drawer without Apply reverts the preview to the last-applied value (live edits are
  provisional, matching "Apply" still being the actual commit point for that drawer's session).
- Dragging a resize handle on each of the 4 resizable block types updates the preview live during
  the drag and is clamped to the registry's min/max (verify by dragging past each end).
- `configDoc`/`dirty` only change once per drag gesture (on release), not per pointermove —
  verify by checking the Save button's enabled state doesn't flicker mid-drag.
- Task log per convention.

---

### TASK-566 — QA: App Builder live preview regression pass
**Agent:** qa-tester
**Depends on:** TASK-561, 562, 564, 565 all done.

Scope: `/consumer-app/pages` end-to-end, plus a real device/emulator check that what the preview
showed matches the published mobile app.
- Add/remove/reorder blocks on each of the 4 whitelisted pages (Home/Promotions/Catalog/News) —
  preview updates instantly, no stale state after switching page tabs.
- Property edits: live-reflects before Apply, reverts on drawer close-without-apply, persists
  after Apply + Save.
- Resize drag on all 4 resizable block types: clamps correctly at both ends, commits once per
  drag (not per pixel), survives Save + reload (re-hydrates from `configDoc` with the dragged
  value).
- Old saved config (pre-TASK-561, no `heightPx`/`cardWidthPx` in `props`) still renders at exactly
  today's default pixel values in both the web preview and the real mobile app — zero regression.
- Save draft → Publish (`/consumer-app/versions`) → open the real mobile app (or its dev preview)
  and confirm the published block sizes match what the web preview showed before publishing —
  the core "not a lie" claim of this whole feature.
- `loyaltyCard`/`loyaltyBalance` preview content is clearly distinguishable from real data.
- Regression: existing App Builder flows (TASK-539/540/541) unaffected — save/dirty-guard/
  unsaved-changes-warning still behave as before.

Report format: bug list (if any) with repro steps, or a clean pass. Per this repo's short-report
convention.
