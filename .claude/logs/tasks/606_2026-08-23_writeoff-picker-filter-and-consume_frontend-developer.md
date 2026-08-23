# TASK-606 — Write-offs: batch picker critical-only filter + "consume as added" behavior

**Agent:** frontend-developer
**Date:** 2026-08-23
**Status:** done

## Scope
Web UI only, single file: `frontend/features/write-offs/components/CreateWriteOffForm.tsx` (+ i18n keys). Mobile and backend explicitly out of scope (concurrent mobile agent owns `mobile/`).

## Changes
- `frontend/features/write-offs/components/CreateWriteOffForm.tsx`:
  - Imported `BatchStatus` from `@/features/shelf/types`; added module-level `CRITICAL_STATUSES: BatchStatus[] = ["critical", "expired"]` (same convention as `frontend/features/shelf/components/StockTable.tsx:27`).
  - Added `criticalOnly` state (`useState(false)`, opt-in).
  - Added `addedIds` memo (`new Set(rows.map(r => r.productStockId))`, deps `[rows]`) so the "already added" check is an explicit, correctly-tracked dependency instead of a closure-captured function.
  - `availableBatches` memo now also filters out `addedIds.has(b.id)` and, when `criticalOnly` is true, requires `CRITICAL_STATUSES.includes(b.status)`; deps updated to `[stockBatches, addedIds, criticalOnly]`.
  - Added a checkbox control (new `checkboxLabelStyle`) right below the existing batch search input, bound to `criticalOnly`, labeled via `t("criticalOnlyLabel")`.
  - Simplified the batch-row render: removed the dead `added`/opacity/cursor/"alreadyAdded" branch — since added batches are now excluded from `filteredBatches` entirely, every rendered row is always addable. `isRowAdded` is kept (still used as the `addRow` guard).
- `frontend/messages/uk.json` / `en.json` — added `Dashboard.writeOffs.createForm.criticalOnlyLabel`: "Лише критичні/прострочені" / "Critical/expired only", sibling to `batchSearchPlaceholder`. Left `alreadyAdded` in place in both files (now unused, harmless — not worth the churn per brief).

## Verification
- `npx tsc --noEmit` — clean.
- `npm run lint` — clean, no warnings/errors.
- Manual browser flow (local dev: backend on :5000, frontend on :3001 via `.claude/launch.json`, already-authenticated session):
  - Store "Свіжий Кут Центральний": all 50 batches are `status: "expired"` in this seed data (confirmed via direct `/api/stock` fetch) — checkbox toggle correctly showed no visible change (50/50 already match the filter), so switched to the other seeded store for a real mixed-status case.
  - Store "Свіжий Кут Подільський" (4 `expired` + 1 `safe`, confirmed via API): checking "Critical/expired only" narrowed the picker from 5 rows to 4, hiding exactly the `safe`-status batch (`RYS-2026-012`); unchecking restored it. Filter confirmed correct.
  - Clicked a batch row (`MLK-2026-053`) — it moved into "Selected items (1)" and simultaneously disappeared from the picker list above (4 → 3 remaining, while checkbox was still checked).
  - Clicked the row's `×` remove button — "Selected items" went back to 0 and `MLK-2026-053` reappeared in the picker (3 → 4).
  - All three required behaviors verified end-to-end.

## Deviations from brief
- Computer-tool mouse clicks (and even `form_input` on the checkbox) were unreliable against this app's React event handling in this session — same issue noted in TASK-604's log. Checkbox toggles via plain `dispatchEvent('input'/'change')` silently updated the DOM `.checked` property without firing React's handler (React tracks checkboxes via `click`, not `change`/`input`); worked around by calling `.click()` directly via `javascript_tool` for the checkbox and by dispatching a full `pointerdown/mousedown/pointerup/mouseup/click` sequence for the batch-row divs and the remove button. Verification-tooling issue only, not a product bug — real user clicks/taps work normally. No screenshot captured (Browser pane would not composite frames this session, same as TASK-604); verified via `get_page_text` + DOM/network inspection instead.
- Did not reset `criticalOnly` on store change (`setStore`) — left as a sticky user preference across store switches, consistent with how `reason` also persists; not specified either way in the brief and this reads as the more useful default.

## Not touched (out of scope)
- `mobile/` — separate concurrent agent.
- `backend/` — no backend changes needed; `ProductStockDto.status` was already present on every batch from `useStock()`.
