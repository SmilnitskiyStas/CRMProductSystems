# TASK-577 — Transfers: create-transfer form on the web frontend

**Status:** done · **Agent:** frontend-developer
**Type:** frontend · **Plan:** `C:\Users\stass\.claude\plans\swift-conjuring-mccarthy.md` (followed exactly, one deviation noted below)

`/transfers` was read-only (view/confirm/cancel) — no way to create a transfer from the web UI.
Backend (`POST /api/transfers`, `TransferService.CreateAsync`) and the frontend data layer
(`transfersApi.create`, `useCreateTransfer`) were already done and unused; this task wired up the
missing form. No backend changes.

## What was built

- **New** `frontend/features/transfers/components/CreateTransferForm.tsx` — self-contained form
  (mounts only while the modal is open), styled to match `AddBatchForm.tsx` (local
  `inputStyle`/`labelStyle`, native `<select>`/`<input>`, single error box, `Btn` footer).
  `useLocations()` for stores, `useStock({ store_id: fromStoreId }, !!fromStoreId)` for source
  batches, `useCreateTransfer()` to submit. Rows keyed by `productStockId` (not `productId`), so
  two batches of the same product with different expiry dates become two independent rows.
  Destination options structurally exclude the source store (`locations.filter(l => l.id !==
  fromStoreId)`) rather than duplicating the backend's from≠to check. Batch picker sorted FEFO
  (ascending `expiryDate`), text-filterable by product name/barcode/batch number, already-added
  batches shown disabled with an "Already added" badge instead of an alert. `transferType` is
  hardcoded `"store_to_store"` (no UI switch, matches the mobile reference and the page's existing
  subtitle copy). Validation on submit, one error at a time in the plan's specified order: missing
  destination → empty rows → per-row `NaN`/`<=0` quantity → per-row quantity exceeding
  `availableQty`; backend 400s surface through the same error box (no pre-submit re-fetch to catch
  staleness, per plan).
- `frontend/app/(dashboard)/transfers/page.tsx` — header turned into a flex row with a new
  "New Transfer" `Btn`; `showCreateModal` state; `Modal` (width 680) wraps
  `CreateTransferForm`, closing on success/cancel. List refresh on create is automatic —
  `useCreateTransfer` already invalidates the `["transfers"]` query key.
- `frontend/features/shelf/hooks/useStock.ts` — added an `enabled = true` second parameter
  (mirrors the pattern already used by `useTransfers` in the same feature area), so the form
  doesn't fetch tenant-wide stock before a source store is picked. Backward compatible; the one
  other call site (`stock/page.tsx`) is unaffected.
- `frontend/messages/uk.json` / `en.json` — `Dashboard.transfers.page.newButton` and a new
  `Dashboard.transfers.createForm` object (21 keys: labels, placeholders, empty states, per-field
  error messages, submit/saving copy). Structure kept parallel between the two files.

## Deviation from the plan

The plan's literal input spec (`input[type=number step=any min=0 max={availableQty}]`) causes the
browser's native HTML5 constraint validation (`rangeOverflow`) to silently block form submission
*before* the custom `handleSubmit` validation ever runs — discovered live in-browser, not by
inspection. A real click on "Create transfer" with an over-limit quantity did nothing visible: no
custom error box, no request, no native tooltip either (address bar focus quirk in the automated
click), just a dead button. Fixed by adding `noValidate` to the `<form>` so the browser defers
entirely to the plan's own validation/error-message logic, which is what actually renders the
specified error copy. `min`/`max`/`step` attributes are left in place for the native spinner
UX/keyboard-arrow clamping; they no longer gate submission.

## Verification

`npx tsc --noEmit` and `npm run lint` in `frontend/` — both clean, no new errors.

**Live-verified in-browser**, not just type-checked. Started `dotnet run` (API, `:5000`, dev
`appsettings.Development.json` → local `crmproductsystems-postgres-1` container, port 5435) and
`next dev` (`:3002` — `:3000`/`:3001` were both occupied by unrelated processes on this machine;
temporarily added `http://localhost:3002` to `Cors:Origins` in
`appsettings.Development.json` for the session and reverted it immediately after, confirmed via
`git diff` showing no residual change). Logged in as an already-authenticated session
(`ea@demo.local`, tenant "Свіжий Кут", role `enterprise_admin`) against real dev data — 4 stores,
"Свіжий Кут Центральний" carrying 645 live stock batches.

- Opened `/transfers` → New Transfer → modal renders all copy correctly.
- Selected "Свіжий Кут Центральний" as source → batch picker populated, FEFO-sorted (verified
  ascending expiry dates), text search field present.
- Destination dropdown confirmed to exclude "Свіжий Кут Центральний" (structural filter working).
- Added 3 rows including two different batches of the same product ("Куряче філе", KUR-2026-051 +
  KUR-2026-052) → confirmed as 2 independent rows ("Selected items (3)"), re-clicking an
  already-added batch showed "Already added" and was a no-op.
- Removed one row via its "×" button → count dropped to 2, correct row removed.
- Submitted with an empty quantity → "Enter a quantity for "Масло вершкове 82,5% 200г"" error shown,
  no request fired.
- Submitted with quantity 100 against an 8-available batch → (after the `noValidate` fix)
  "Quantity for "Масло вершкове 82,5% 200г" exceeds available stock (available: 8)" shown, no
  request fired.
- Set valid quantities (3 and 5.5 — fractional quantity accepted) + a note, submitted →
  `POST /api/transfers` → **201 Created**, modal closed, new row appeared at the top of the list
  ("Свіжий Кут Центральний → Свіжий Кут Подільський", 2 items, status "In Transit") with **no
  manual page refresh**.
- Cross-checked directly in Postgres: `stock_transfers`/`stock_transfer_items` rows correct
  (3.00 and 5.50 quantities against the right `ProductStockId`s), source `product_stock.Quantity`
  correctly decremented (8→5, 20→14.5), `stock_movements` rows written.
- **Test data cleaned up** afterward: deleted the test `stock_transfers`/`stock_transfer_items`/
  `stock_movements` rows and restored the two `product_stock` quantities to their original values
  via `psql` — dev DB left exactly as found.

Dev servers and the temporary CORS entry were stopped/reverted at the end of the session; no
lingering processes on `:5000`/`:3002`.

## Files touched

- `frontend/features/transfers/components/CreateTransferForm.tsx` (new)
- `frontend/app/(dashboard)/transfers/page.tsx`
- `frontend/features/shelf/hooks/useStock.ts`
- `frontend/messages/uk.json`, `frontend/messages/en.json`
