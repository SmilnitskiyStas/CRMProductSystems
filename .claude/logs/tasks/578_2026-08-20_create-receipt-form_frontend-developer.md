# TASK-578 — Receipts: create-receipt form on the web frontend

**Status:** done · **Agent:** frontend-developer

`/receipts` was read-only (view + open detail page) — no way to create a draft receipt
(прийомка) from the web UI. Backend (`POST /api/receipts`) and the frontend data layer
(`receiptsApi.create`, `useCreateReceipt`) already existed unused. This task wired up the
missing "create draft receipt" form, mirroring TASK-577's `CreateTransferForm.tsx`. No backend
changes; the existing `/receipts/[id]` detail page (fill-in/confirm-receive flow) was not touched.

## What was built

- **New feature module** `frontend/features/suppliers/` (`types.ts`, `api/suppliers.ts`,
  `hooks/useSuppliers.ts`) — mirrors `features/locations/` structure. `SupplierDto` matches the
  backend record exactly (`backend/.../Suppliers/Dtos/SupplierDtos.cs`). `suppliersApi.getAll`
  requests `pageSize=200` (backend cap) and defaults `include_inactive=false` so the picker only
  shows active suppliers.
- **New** `frontend/features/receipts/components/CreateReceiptForm.tsx` — same structural
  convention as `CreateTransferForm.tsx` (local inline style consts, native `<select>`/`<input>`,
  single aggregated error box, `Btn` footer, `noValidate` on the form). Data hooks:
  `useLocations()` (destination store, required), `useSuppliers()` (optional, empty option = "no
  supplier"), `useCatalogProducts({ search: debouncedQuery })` (product picker — receipts create
  new stock, so the catalog is the correct source, not `useStock`), `useCreateReceipt()`. Search
  input is debounced ~300ms (`useState`+`useEffect`+`setTimeout`, same pattern as
  `ProductPickerField.tsx`). Rows keyed by `productId` (not `productStockId` — a receipt line has
  no existing batch identity yet), so the same product can only appear once; re-clicking an
  already-added product is a no-op with an "Already added" badge. Each selected row has 4 compact
  inputs (qty required, price/expiry/batch optional) laid out in a small grid within the row card.
  `pricePurchase` pre-fills from the catalog's `pricePurchase` when available, otherwise blank.
  Validation on submit (single error, stop at first failure): missing destination store → empty
  rows → per-row `NaN`/`<=0` quantity. Price/expiry/batch are genuinely optional at draft-creation
  time (filled in later on `[id]`), so no client validation on them.
- `frontend/app/(dashboard)/receipts/page.tsx` — header turned into a flex row with a new "New
  receipt" `Btn`; `showCreateModal` state; `Modal` (width 720 — wider than transfers' 680 since
  rows carry 4 inputs) wraps `CreateReceiptForm`, closing on success/cancel. List refresh on
  create is automatic — `useCreateReceipt` already invalidates `["receipts"]`.
- `frontend/messages/uk.json` / `en.json` — `Dashboard.receipts.page.newButton` and a new
  `Dashboard.receipts.createForm` object (28 keys), structure kept parallel between the two files.

## Deviations from the brief

None. Followed the spec as written, including proactively adding `noValidate` to the form (per
the brief's explicit instruction, referencing TASK-577's same fix).

## Verification

`npx tsc --noEmit` and `npm run lint` in `frontend/` — both clean, no new errors.

**Live-verified in-browser**, not just type-checked. Started `dotnet run` (API, `:5000`) and
`next dev` (`:3002` — `:3000` occupied by an unrelated project's Docker container, `:3001` is a
Windows-reserved port in this session) against the local dev Postgres
(`crmproductsystems-postgres-1`, port 5435). Temporarily added `http://localhost:3002` to
`Cors:Origins` in `appsettings.Development.json` for the session, reverted immediately after
(confirmed via `git diff` — no residual change), same as TASK-577. Already-authenticated session
reused (tenant "Свіжий Кут", role `network_manager`).

Note: the Browser pane did not composite frames in this session (`computer` click/screenshot
actions failed with "Browser pane is not displayed"), so interactions were driven via
`form_input` for selects/inputs and via `javascript_tool` (`.click()` DOM dispatch) for buttons
and picker rows, with every result independently confirmed via `read_page`/`get_page_text` and
the network request log — not just assumed from the dispatched click.

- Opened `/receipts` → "New receipt" → modal renders all copy correctly; supplier dropdown (3
  active suppliers) and destination store dropdown (4 locations) populated from real data.
- Typed "Молоко" in the product search → confirmed via network log that `GET
  /api/items?search=Молоко` fired only after the ~300ms debounce (not on every keystroke) → 5
  matching products rendered.
- Added 2 products → "Selected items (2)"; re-clicking an already-added product's row was a
  no-op, showed "Already added" badge, count stayed unchanged.
- Removed one row via its "×" button → count dropped to 1, correct row removed (product
  reappeared as pickable, badge cleared).
- Submitted with an empty quantity → `Enter a quantity for "Молоко 2,5% Галичина 1л"` error
  shown inline, no request fired.
- Filled qty=10, price=45.5, expiry=2026-12-01, batch=BATCH-001, submitted → `POST
  /api/receipts` → **201 Created**, modal closed, new "Draft" row appeared at the top of the
  list with **no manual refresh**; response payload confirmed all 4 fields round-tripped
  correctly.
- Opened the new receipt's `/receipts/{id}` detail page → confirmed the qty (shown as the
  "received" input's placeholder), expiry date, and batch number all round-trip correctly into
  the existing editable table — the draft is usable downstream without any changes to that page.
- **Test data cleaned up** afterward: deleted the test `stock_receipts`/`stock_receipt_items`
  rows via `psql` (as the `crm` superuser, since RLS blocks the app role from an ad-hoc session
  without tenant context) — no stock movements existed yet (receipt was never confirmed-received),
  so no other tables needed touching. Dev DB left exactly as found.

Dev servers stopped; CORS entry and `.claude/launch.json` port both reverted to their committed
values (`git diff` clean on both files) at the end of the session.

## Files touched

- `frontend/features/suppliers/types.ts` (new)
- `frontend/features/suppliers/api/suppliers.ts` (new)
- `frontend/features/suppliers/hooks/useSuppliers.ts` (new)
- `frontend/features/receipts/components/CreateReceiptForm.tsx` (new)
- `frontend/app/(dashboard)/receipts/page.tsx`
- `frontend/messages/uk.json`, `frontend/messages/en.json`
