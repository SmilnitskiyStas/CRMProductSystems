# TASK-568 — App Builder preview: viewport-fit scaling, show/hide toggle, interactive bottom nav

**Status:** done · **Agent:** frontend-developer
**Type:** frontend · **Follow-up to:** TASK-560..567 (App Builder live preview, ADR-031)

Three user-requested changes to the live preview panel on `/consumer-app/pages`.

## 1 — Scale the phone frame to fit the viewport

`PhoneFrame.tsx` gained an opt-in `fitToViewport?: boolean` prop (default omitted, unchanged
behavior). When set (and `width`/`height` are given), a new local `usePhoneFrameFitScale` hook
measures the outer box's `getBoundingClientRect().top` and computes
`scale = min(1, (window.innerHeight - top - 24) / deviceHeight)` on mount and on every `resize`.
Render technique: outer `<div>` reserves the scaled footprint (`width/height * scale`), inner
`<div>` renders the frame at its true 1:1 size with `transform: scale(...)`/`transformOrigin:
"top left"` — same technique DevTools/Figma device previews use, aspect ratio never distorts.
`ThemeEditorSection.tsx` passes neither `fitToViewport` nor `width`/`height` — verified in-browser
its preview is still exactly 320px wide, `transform: none`, `overflow: visible`.

`AppPreviewPanel.tsx` passes `fitToViewport` unconditionally now.

## 2 — Show/hide toggle for the preview panel

`AppBuilderCanvas.tsx` gained `previewVisible` (`useState(true)`) and a `Btn variant="ghost"
size="sm"` next to the canvas title (`hidePreviewButton`/`showPreviewButton`, en+uk). The preview
column (`flex: "0 1 500px"`) only renders while `previewVisible` — the canvas column
(`flex: "1 1 420px"`) naturally reclaims the freed width when it's hidden. No persistence, pure
local UI state.

## 3 — Interactive bottom nav inside the mockup

`AppPreviewPanel.tsx` now takes `pages`/`navigation`/`activePage` instead of a single page's
`blocks`, and renders its own bottom tab bar (one entry per `navigation` item) below the
scrollable block area, inside `PhoneFrame`. Icons reused from `NavigationBuilderSection.tsx`'s
`NAVIGATION_ICON_COMPONENTS` (now `export`ed — no logic change, avoids a third copy of the
icon-key→lucide mapping the type comment already flagged as living in exactly one file).

Type→page mapping verified directly against `mobile/features/retail-navigation/policy.ts`'s
`retailRoutePolicies` (not guessed): only `home`/`promotions`/`catalog`/`news` map to an
App-Builder page; `loyalty`→wallet, `coupons`→coupons, `stores`→retailers, `profile`→account are
fixed native screens with zero App Builder involvement. Clicking one of those 4 sets a
`nonEditableNavType` state that swaps the content area to a "not editable here" placeholder
(styled like the existing empty-page hint) instead of ever showing fabricated content — ADR-031's
core truthfulness requirement.

New internal state: `previewPage` (init from `activePage`, re-synced via `useEffect` whenever
`activePage` changes — clears `nonEditableNavType` too) and `nonEditableNavType`. Clicking an
editable nav item sets `previewPage` and clears the placeholder; clicking a non-editable one only
sets `nonEditableNavType`, leaving `previewPage` untouched (nothing to switch to).

`AppBuilderCanvas.tsx` now builds `previewPages = { ...configDoc.pages, [activePage]: { blocks:
previewBlocks } }` (the pre-existing TASK-565 live-edit-merged array) so before-Apply live typing
still reflects instantly whenever the preview's currently-shown page happens to be the canvas's
active page — the common case. Diverged state (admin browsing a different mockup tab than they're
editing) is expected/documented behavior, not a bug.

`scrollAreaMaxHeight` now subtracts a fixed `NAV_BAR_HEIGHT_PX` (54) so the nav bar always has
room and can't be pushed outside the (`overflow: hidden`) frame by a long block list; the nav bar
itself uses `marginTop: "auto"` to stay pinned to the bottom even when content is short, and
negative side/bottom margins to sit flush against the frame's edges.

## Verification

`npx tsc --noEmit` clean throughout.

In-browser (dev servers on :3001/:5000, logged in as `ea@demo.local`), 1500×800 viewport
(~800px vertical room, matching a laptop window):
- Frame (Pixel 8 Pro, 448×998 native) scaled to 222×495, bottom edge at y=776 — within the 800px
  viewport, confirming no outer-page scroll needed to see the whole mockup including the nav bar.
  (First attempt showed the row still wrapped because the CDP viewport resize didn't fire a
  `matchMedia` `change` event for the pre-existing `useMediaQuery` breakpoint hook — a reload
  after resizing fixed it; not a regression, a test-tooling quirk.)
- Toggle: clicked "Hide preview" → column and "LIVE PREVIEW" text gone entirely; clicked "Show
  preview" → reappeared showing the correct (Home) page content.
- Interactive nav: temporarily added `promotions`/`loyalty`-type nav items via
  `/consumer-app/navigation` for testing (removed + re-saved afterward). Clicking the promotions
  item switched the mockup's content to the Promotions page while the canvas above stayed on
  "HOME SCREEN" — confirmed independence. Clicking the loyalty item showed "This tab isn't
  editable in the App Builder." Clicking back to Home restored real block content.
- Re-sync: with the mockup diverged onto the placeholder, clicking the canvas's own "Catalog"
  `PageTabs` tab correctly snapped the mockup back to real Catalog content (both `previewPage` and
  `nonEditableNavType` cleared).
- Live-edit-before-Apply: opened the Product Carousel's Property Editor on the Catalog page
  (`activePage === previewPage`), typed into Title — preview updated instantly before Apply;
  Cancel left no residue.
- Resize-drag handle: simulated pointer down/move(+30)/up on the Hero Banner height handle —
  225px → 255px, Save button flipped to enabled (dirty), confirming the handle still works through
  the new nested/scaled frame DOM. Not saved (discarded on reload) to avoid polluting demo data.
- `/consumer-app/design` (`ThemeEditorSection`): confirmed its `PhoneFrame` still renders at
  320×304 unscaled, `transform: none`, `overflow: visible` — byte-identical.

Known, accepted minor tradeoff (out of this task's explicit scope, not fixed): the 4 resize
handles' pointer-delta math (`useResizeDrag.ts`) isn't scale-aware — under `fitToViewport`
scaling, a drag needs more physical mouse travel than before to produce the same value change
(value tracks raw `clientX`/`clientY` deltas 1:1, but the visible handle only moves by
`delta * scale` on screen). Functionally correct throughout (clamps and commits correctly,
verified above) — a UX-polish item, not a correctness bug.

## Files touched

- `frontend/features/consumer-app/components/PhoneFrame.tsx`
- `frontend/features/consumer-app/components/AppPreviewPanel.tsx`
- `frontend/features/consumer-app/components/AppBuilderCanvas.tsx`
- `frontend/features/consumer-app/components/NavigationBuilderSection.tsx` (exported one constant)
- `frontend/messages/en.json`, `frontend/messages/uk.json`
