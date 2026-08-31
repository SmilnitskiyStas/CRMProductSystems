# TASK-657 (T10) — Delivery-coverage panel in `CooperationRequestModal`

**Status:** done · **Agent:** frontend-developer · **Model:** sonnet
**Branch:** main (main working tree) · **Depends on:** T4 (TASK-651, `GET .../coverage`, on main),
T7 (TASK-654, `features/geo`, on main)

## Scope

Frontend only. Show the buyer, at the top of the cooperation-request modal, whether
this supplier delivers to their region — before they send the request. Advisory:
the panel never disables or blocks the submit button.

## Files created

| File | Contents |
|---|---|
| `frontend/features/marketplace/components/CooperationCoveragePanel.tsx` | `{ supplierId }`. `useSupplierCoverageForBuyer(supplierId, regionOverride)`. Loading → muted "Перевірка покриття доставки…". `served` → green line + `termsLabel` + `measuredDeliveryLabel` (only when the worker value is non-null). `not_served` → amber line. `unknown` → neutral line + `<RegionSelect>` whose value is local `useState` fed back as the `buyerRegionCode` override so the panel re-resolves. Below: compact full-coverage summary (served + terms / "за домовленістю", "Не доставляє: …", note) — inlined, not `SupplierCoveragePanel` (that one carries a page-scale `<h2>` + 28px margin that reads wrong in a 480px modal). Dark-theme inline styles matching the modal. |

## Files changed

- `frontend/features/marketplace/types.ts` — new `BuyerRegionStatus` union + `SupplierCoverageForBuyer`
  interface (matches backend `SupplierCoverageForBuyerDto`; `coverage` reuses the geo `DeliveryCoverage`
  type already imported at the top of the file). Appended before `SupplierReviewStats`.
- `frontend/features/marketplace/api/marketplace-api.ts` — `getSupplierCoverageForBuyer(supplierId,
  buyerRegionCode?)` → `GET /api/marketplace/suppliers/{id}/coverage` (+ `?buyerRegionCode=` when
  passed, `encodeURIComponent`). Type import added.
- `frontend/features/marketplace/hooks/useMarketplace.ts` — `MARKETPLACE_KEYS.supplierCoverage(id,
  code)` = `["marketplace","supplier-coverage", id, code]`; `useSupplierCoverageForBuyer(supplierId:
  string | null, buyerRegionCode?: string | null)` — React Query, `enabled: !!supplierId`,
  `staleTime: 30_000`.
- `frontend/features/marketplace/components/CooperationRequestModal.tsx` — renders
  `<CooperationCoveragePanel supplierId={supplierId} />` right after the supplier-name line, above
  the message textarea (message `<label>` top margin folded into a `margin` shorthand).
- `frontend/messages/uk.json` + `frontend/messages/en.json` — new `coverage` sub-object under
  `Dashboard.marketplace.cooperationRequestModal`, both locales, full parity.

## New i18n keys — `Dashboard.marketplace.cooperationRequestModal.coverage` (both locales)

| key | uk | en | params |
|---|---|---|---|
| `loading` | Перевірка покриття доставки… | Checking delivery coverage… | — |
| `servesYourRegion` | Постачальник доставляє у ваш регіон ({region}) | The supplier delivers to your region ({region}) | `region` |
| `doesNotServeYourRegion` | Постачальник НЕ доставляє у ваш регіон ({region}) | The supplier does NOT deliver to your region ({region}) | `region` |
| `regionUnknown` | Не вдалося визначити ваш регіон | Could not determine your region | — |
| `yourRegionLabel` | Вкажіть ваш регіон доставки | Specify your delivery region | — |
| `regionSelectPlaceholder` | Оберіть регіон | Select a region | — |
| `termsLabel` | Умови: {terms} | Terms: {terms} | `terms` |
| `measuredDeliveryLabel` | Середній термін доставки у ваш регіон: {days} дн. (на основі {count}) | Average delivery time to your region: {days} d. (based on {count}) | `days`, `count` |
| `termsByAgreement` | за домовленістю | by agreement | — |
| `notServed` | Не доставляє: {regions} | Does not deliver to: {regions} | `regions` |

## Buyer-region "unknown" override flow

Backend resolves the buyer region server-side (oldest active `Location` with a `RegionCode`).
When it can't (`buyerRegionStatus === "unknown"`, `buyerRegionCode === null`), the panel shows a
`<RegionSelect>`. Its value is `regionOverride` (`useState<string | null>` local to
`CooperationCoveragePanel`). `useSupplierCoverageForBuyer(supplierId, regionOverride)` puts that
into the query key and sends it as `?buyerRegionCode=`, so picking a region re-fetches and the
panel re-renders as `served` / `not_served` (the select then disappears — it only renders in the
`unknown` branch). State is component-local, so it resets when the modal closes.

## Verification

- `cd frontend && npx tsc --noEmit` — clean.
- `npm run lint` — "No ESLint warnings or errors".
- `npx vitest run` — 7 files / 50 tests pass (no marketplace component tests exist).
- i18n parity (node deep-key diff) — uk 4600 == en 4600, 0 drift.
- **Browser (frontend dev :3007 + backend dev :5000 + dev DB :5435, logged in as `ea@demo.local` /
  tenant «Свіжий Кут»):** supplier `b4e21658…` (seeded coverage: served UA-30 "1-2 дні, від 3000 грн"
  + UA-32 "2-3 дні"; notServed UA-43; note). Marketplace → supplier profile → "Подати заявку на
  співпрацю":
  - **unknown** (tenant has no located region): "Не вдалося визначити ваш регіон" + `<RegionSelect>`;
    full summary + note below; submit enabled.
  - Pick **м. Київ (UA-30)** in the select → panel re-resolves to green "Постачальник доставляє у ваш
    регіон (м. Київ)" + "Умови: 1-2 дні, від 3000 грн"; select gone.
  - Reopen, pick **Крим (UA-43)** → amber "Постачальник НЕ доставляє у ваш регіон (Автономна
    Республіка Крим)"; submit still enabled.
  - `measuredDeliveryLabel` line not exercised — worker job hasn't run, `measuredAvgDeliveryDaysToBuyerRegion`
    is null; code guards on non-null and correctly omits the line.
  - `sg_locale=en` → all strings render in English, no next-intl missing-key throw, no console/React
    errors (region NAMES stay Ukrainian — they come from `GET /api/geo/regions`, per plan).

## Notes / decisions

- Full-coverage summary inlined rather than reusing `SupplierCoveragePanel` — its `<h2>` (15px, ==
  modal title) + `marginBottom: 28` are profile-page scale; a compact block matches the modal's
  11–13px type. `SupplierCoveragePanel` stays as-is on the profile page.
- `buyerRegionStatus === "not_served"` only fires when the region is explicitly in the supplier's
  `notServed` list; a region that's in neither `served` nor `notServed` comes back `unknown` (backend
  behavior, confirmed by curl) — the panel then shows the region-select prompt, which is the sensible
  fallback.
- Backend dev CORS (`Cors:Origins`) only allows :3000/:3001; ran the dev server on :3007 with
  `Cors__Origins` extended for the browser check. No repo change — dev-only.
