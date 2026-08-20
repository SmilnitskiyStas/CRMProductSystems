# TASK-579 — Write-offs: create-write-off form on the web frontend

**Status:** done · **Agent:** frontend-developer

`/write-offs` was read-only (view + approve/reject) — no way to create a new write-off document
from the web UI. Backend (`POST /api/write-offs`), and the frontend data layer (`writeOffsApi
.create`, `useCreateWriteOff`) already existed unused. This is the third of three similar
create-document forms (after TASK-577 transfers, TASK-578 receipts); mirrors `CreateTransferForm
.tsx` closely since a write-off item references an existing stock batch (`productStockId`, via
`useStock`), not a new one. No backend changes.

## What was built

- **New** `frontend/features/write-offs/components/CreateWriteOffForm.tsx` — same structural
  convention as `CreateTransferForm.tsx` (local inline style consts, native `<select>`/`<input>`,
  single aggregated error box, `Btn` footer, `noValidate` on the form to avoid the native `max`
  attribute silently blocking submission). Data hooks: `useLocations()` (single store, required),
  `useStock({store_id}, !!storeId)` (FEFO-sorted batch picker, `WRITE_OFF_REASON_VALUES` for the
  optional reason `<select>`), `useCreateWriteOff()`. Rows keyed by `productStockId` (two batches
  of the same product with different expiry stay distinct rows). Batch list sorted by
  `expiryDate` ascending (soonest-to-expire first — most relevant for write-offs). Each selected
  row has 2 compact inputs (qty required with `max={availableQty}`, unit price optional) laid out
  side-by-side per the receipts-form's compact-multi-input pattern. No notes field — per brief,
  `CreateWriteOffRequest.notes` is silently discarded server-side (no `Notes` column on the
  `WriteOff` entity), so wiring a UI field to it would silently lose user input; flagged as a
  backend follow-up below, not fixed here. Validation on submit (single error, stop at first
  failure): missing store → empty rows → per-row NaN/`<=0` quantity → quantity exceeds
  `availableQty`. `reason` and `unitPrice` are optional, no validation on either.
- `frontend/app/(dashboard)/write-offs/page.tsx` — this page previously had **no access gate at
  all** (unlike transfers/receipts). Per the brief, did NOT add a page-wide gate (GET policy
  `CanViewStock` is broader than create's `CanReceiveStock` — merchandiser can view but not
  create) — only the new "New write-off" `Btn` is conditionally rendered via `canCreate = me ?
  hasRole(me.role, CAN_RECEIVE_STOCK) : false`. Header restructured into a flex row (existing
  status-tabs/filter-chip UI below is untouched). `Modal` (width 700) wraps `CreateWriteOffForm`
  near the existing `DetailDrawer` render block. List refresh on create is automatic —
  `useCreateWriteOff` already invalidates `["write-offs"]`.
- `frontend/messages/uk.json` / `en.json` — `Dashboard.writeOffs.page.newButton` and a new
  `Dashboard.writeOffs.createForm` object (24 keys), structure kept parallel between the two
  files. Reason labels reused from the existing `Dashboard.writeOffs.reason` namespace, not
  duplicated.

## Deviations from the brief

None. Followed the spec as written.

## Backend follow-up (not fixed, flagged only)

`CreateWriteOffRequest.Notes` (DTO) has no backing column on the `WriteOff` entity — the field is
silently accepted and discarded server-side. Not in scope for this frontend-only task; worth a
small backend ticket if a notes field is ever wanted on write-offs.

## Verification

`npx tsc --noEmit` and `npm run lint` in `frontend/` — both clean, no new errors.
`node -e "JSON.parse(...)"` confirmed both message files stay valid JSON.

**Live-verified in-browser end-to-end.** Started `dotnet run` (API, `:5000`) and `next dev`
(`:3002`) against the local dev Postgres (`crmproductsystems-postgres-1`, port 5435).
Temporarily added `http://localhost:3002` to `Cors:Origins` in `appsettings.Development.json`,
reverted immediately after (confirmed via `git diff` — no residual change). Reused an
already-authenticated browser session (role `network_manager`, tenant "Свіжий Кут"). The Browser
pane did not composite frames this session either (same as TASK-578) — interactions driven via
`javascript_tool` DOM dispatch, every result confirmed via `get_page_text`/direct `fetch` calls
against `/api/stock`, not assumed from the dispatched click.

- `/write-offs` → "New write-off" button visible for this role → modal opens, all copy renders
  correctly (English locale active this session).
- Selected store "Свіжий Кут Центральний" → batch picker populated, confirmed FEFO order
  (earliest expiry 6/4/2026 first, ascending through the list).
- Typed "Молоко" in the search box → filtered to exactly the 2 matching batches, both still
  FEFO-ordered.
- Added batch `MLK-2026-051` (expiry 6/25/2026, available 45) → row appeared, badge flipped to
  "Already added", re-clicking the same batch was confirmed a no-op (opacity 0.5/cursor default,
  "Selected items" count stayed at 1).
- Added a second batch, then removed it via "×" → count dropped back to 1, the correct row
  removed.
- Submitted with an empty quantity → `Enter a quantity for "Молоко 2,5% Галичина 1л"` shown
  inline, no request fired.
- Set qty=999 (`max=45` confirmed on the input) and submitted → `Quantity for "Молоко 2,5%
  Галичина 1л" exceeds the batch balance (available: 45)` shown — confirms `noValidate` correctly
  let the custom validator run past the native `max` block.
- Set qty=3, unitPrice=25.5, reason=Expired, submitted → `POST /api/write-offs` → modal closed,
  new row appeared at the top of the list with **no manual refresh** (loss amount 76.5 = 3 ×
  25.5 correct, status "Pending Approval", Pending-Approval tab count 1→2).
- Confirmed via direct `fetch('/api/stock?store_id=...')` that the batch's `quantity` was still
  45 immediately after creation — stock genuinely untouched until approval.
- Approved the write-off via the existing (unmodified) approve action → status flipped to
  "Approved" in the list, Pending-Approval count 2→1.
- Re-fetched `/api/stock` → batch `quantity` now 42 (45 − 3) — confirms the create flow feeds the
  existing FEFO-consuming approve flow correctly, exactly as intended by the backend design.

**Test data cleaned up** via `psql` against the dev container (`crmproductsystems-postgres-1`,
as the `crm` superuser — RLS blocks the app role from an ad-hoc session without tenant context):
restored `product_stock.Quantity` +3 back to 45 for the affected batch, then `DELETE FROM
write_offs` for the test row (cascades to `write_off_items`). Re-verified in-browser afterward —
list back to exactly the original 3 rows.

Dev servers stopped; CORS entry reverted (`git diff` clean on
`appsettings.Development.json`).

## Files touched

- `frontend/features/write-offs/components/CreateWriteOffForm.tsx` (new)
- `frontend/app/(dashboard)/write-offs/page.tsx`
- `frontend/messages/uk.json`, `frontend/messages/en.json`
