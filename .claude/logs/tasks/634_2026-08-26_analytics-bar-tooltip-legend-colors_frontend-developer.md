# TASK-634 — Analytics: fix black tooltip text / black legend dots on Bar charts (frontend)

**Status:** done · **Agent:** frontend-developer

## Bug

On `/analytics`: (1) hovering chart bars showed illegible dark tooltip text on the dark tooltip
background; (2) the "По категоріях" panel's 4 status legend dots all rendered black instead of
green/amber/red/dark-red.

## Root cause (given, not re-diagnosed)

Recharts (`^3.8.1`) computes `entry.color` for both the Legend icon and the Tooltip row text from
a `<Bar>` element's own `fill` prop — never from per-point `<Cell fill>` children. All three
affected charts colored bars only via `<Cell>`, leaving the `<Bar>`'s own `fill` unset, so
`entry.color` was `undefined` and both Legend and Tooltip fell back to black.

## What changed

- `frontend/features/analytics/components/CategoryStatusChart.tsx` — added a `fill` prop
  directly on each of the 4 `<Bar>` elements (`safe` #4ADE80, `warning` #FBBF24, `critical`
  #F87171, `expired` #DC2626), matching each Bar's existing `<Cell fill>` exactly. These are 4
  real distinct series, so this gives each its own correct Legend/Tooltip color. Left the
  existing `<Tooltip contentStyle>` untouched — no `itemStyle` added (would have flattened all
  rows to one color).
- `frontend/features/analytics/components/LossesByStoreChart.tsx` — single `<Bar dataKey="loss">`
  with per-store `<Cell fill={STORE_COLORS[i]}>`, i.e. no single "correct" per-series color exists
  for this shape. Added `fill="#E8EDF5"` on the `<Bar>` itself, matching this file's own
  `<Tooltip contentStyle.color>` — fixes illegibility without faking a per-store color.
- `frontend/features/analytics/components/LossesByReasonChart.tsx` — same shape/fix: added
  `fill="#E8EDF5"` on the single `<Bar dataKey="loss">`, matching its own tooltip `contentStyle.color`.

Bar-level `fill` only feeds Recharts' internal Legend/Tooltip color metadata here — every `<Cell>`
still has its own explicit `fill`, so actual bar rendering/colors are visually unchanged.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean.
- Live E2E on `/analytics` (localhost:3001 + localhost:5000, logged-in session already present).
  Browser-pane screenshot compositing was unavailable in this session (`document.hidden: true`,
  tab backgrounded — an environment limitation, not an app issue), so verification was done via
  direct DOM/React-prop inspection instead of pixel screenshots:
  - **CategoryStatusChart ("Status by category")**: legend SVG swatches now read
    `fill="#F87171"` (Critical), `"#DC2626"` (Expired), `"#4ADE80"` (Safe), `"#FBBF24"` (Warning)
    — 4 distinct colors, confirming bug 2 fixed. Simulating a real hover (dispatched on the
    correct SVG target so `offsetX/Y` compute natively, then invoked React's `onMouseMove` prop)
    produced a tooltip on dark background `rgb(17,24,39)` with each row individually colored:
    `Critical: rgb(248,113,113)`, `Expired: rgb(220,38,38)`, `Safe: rgb(74,222,128)`,
    `Warning: rgb(251,191,36)` — all legible, matching the Bar fills exactly. Bug 1 confirmed fixed.
  - **LossesByStoreChart / LossesByReasonChart**: same hover technique produced tooltips with row
    color `rgb(232,237,245)` (`#E8EDF5`) on the dark background — legible, matching the intended
    fallback color, versus black before the fix.

## Files touched

`frontend/features/analytics/components/CategoryStatusChart.tsx`,
`frontend/features/analytics/components/LossesByStoreChart.tsx`,
`frontend/features/analytics/components/LossesByReasonChart.tsx`.
