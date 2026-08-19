# TASK-567 — App Builder: fix preview-panel wrap bug + phone-model picker

**Status:** done · **Agent:** frontend-developer
**Type:** frontend · **Follow-up to:** TASK-560..566 (App Builder live preview, ADR-031)

## Bug 1 — preview column wrapping below the canvas

Root cause was NOT the row's own `flexWrap` alone: `frontend/app/(dashboard)/consumer-app/pages/page.tsx`
hard-caps the whole page at `maxWidth: 1100` (global box-sizing:border-box reset → 1036px usable
inside the 28/32px padding). The row's old combined flex-basis (300+420+340+2×20=1100) already
exceeded that 1036px ceiling on *every* window size, not just narrow ones — explains the user's
"room on the right, but preview shows up at the bottom" report exactly (the room they saw was real
screen space sitting outside the too-narrow cap).

Fix:
- `page.tsx`: `maxWidth` 1100 → 1360 (fits the new combined basis 300+420+500+40=1260 with headroom).
- `AppBuilderCanvas.tsx`: new `useMediaQuery("(min-width: 1360px)")` hook
  (`frontend/features/consumer-app/hooks/useMediaQuery.ts`, new file) drives
  `flexWrap: canFitThreeColumns ? "nowrap" : "wrap"` on the row — CSS media-query based, not a
  `resize` listener, so it only re-renders on an actual breakpoint crossing.

Verified in-browser via DOM geometry probes (screenshot unavailable in this session — Browser pane
wouldn't composite frames, used `getBoundingClientRect`/computed-style instead):
- 1600px / 1440px window: all 3 columns same line, 0 body horizontal overflow.
- 1359px / 1300px / 900px window: row wraps, preview drops below canvas, 0 overflow from the row
  (900px does show a pre-existing, unrelated 62px overflow from the dashboard TopBar's user-menu —
  confirmed via element-level offender scan, not touched, out of scope).

## Feature 2 — device-model picker

- New `frontend/features/consumer-app/components/devicePresets.ts`: 5 presets (Pixel 8 Pro default,
  Pixel 8, iPhone 15 Pro Max, iPhone 15/14, Galaxy S23 Ultra), CSS-px viewport sizes per blisk.io.
- `PhoneFrame.tsx`: additive optional `width`/`height` props. When given, renders border-box at that
  exact size with `overflow: hidden`; omitted → byte-identical to before (verified: `/consumer-app/design`
  ThemeEditorSection's `ThemePreview` still renders 320px-wide, `overflow: visible`, unchanged).
  Exported `PHONE_FRAME_BORDER_PX = 8` so callers can compute true inner content height.
- `AppPreviewPanel.tsx`: local `deviceId` state (default `pixel8pro`, not persisted — pure display
  setting), device `<select>` next to the panel title (styled like `BlockPropertyEditor.tsx`'s
  `EnumField`), passes preset `width`/`height` into `PhoneFrame`, and the block-list scroll wrapper's
  `maxHeight` is now `device.height - PHONE_FRAME_BORDER_PX*2 - framePadding*2` instead of the old
  hardcoded `600`.
- `AppBuilderCanvas.tsx`: preview column `flex: "0 1 340px"` → `"0 1 500px"` (fits the widest preset,
  448px + 16px border + breathing room); `PhoneFrame`'s own `margin: 0 auto` keeps narrower presets
  centered without the column jumping width on switch.
- New translation key `Dashboard.consumerApp.appBuilder.preview.deviceLabel` (en + uk).

Verified in-browser: switching Pixel 8 Pro → iPhone 15/14 → Galaxy S23 Ultra changes the frame to
390×844 / 384×824 exactly; scroll-wrapper `maxHeight` computed correctly per device (e.g. 824 → 776);
Hero Banner's resize-drag handle (TASK-565) still works inside the frame — simulated pointer
down/move/up raised height 225→255px, committed once, `dirty`/Save-button flipped correctly.

## Build/tests

`npx tsc --noEmit` clean (before and after all edits).

## Files touched

- `frontend/app/(dashboard)/consumer-app/pages/page.tsx`
- `frontend/features/consumer-app/components/AppBuilderCanvas.tsx`
- `frontend/features/consumer-app/components/AppPreviewPanel.tsx`
- `frontend/features/consumer-app/components/PhoneFrame.tsx`
- `frontend/features/consumer-app/components/devicePresets.ts` (new)
- `frontend/features/consumer-app/hooks/useMediaQuery.ts` (new)
- `frontend/messages/en.json`, `frontend/messages/uk.json`
