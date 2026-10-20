# TASK-727 shelf planogram + AI scan (frontend)

Changed (frontend only): `features/locations/{types.ts,api/shelves.ts,hooks/useShelves.ts}`,
`components/ShelfProductModal.tsx` (multi-item + facings), new `components/ShelfScanPanel.tsx`
(upload w/ canvas downscale to 1600px JPEG, section selector, result, history),
shelves `page.tsx` (planogram state loaded once per zone from GET shelf-items with JSON itemId
fallback; Save = layout JSON with itemId=first item, then PUT shelf-items for changed/deleted
sections, then syncZoneTags; scan button; red/amber canvas highlight from active scan),
`messages/{uk,en}.json` (shelvesPage keys).

Decisions: highlight maps scan item statuses to sections via the planogram (scan items carry no
shelfId); scan with shelfId colours only that section. FloorPlanCanvas badge skipped (optional).
Verify: `tsc --noEmit` and eslint clean. Not exercised in browser against live backend/AI.
