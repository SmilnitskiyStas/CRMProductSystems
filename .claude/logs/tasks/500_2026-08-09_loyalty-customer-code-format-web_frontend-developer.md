# TASK-500 — Frontend: standalone Consumer App page with loyalty settings (incl. `customerCodeFormat`)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-09

## Initial blocker (resolved)

Original brief assumed an existing tenant loyalty settings page consuming
`GET/PUT /api/settings/loyalty`. None existed — see this file's first version / `blocked.md`
history. Product owner resolved it: build a **dedicated standalone page** (not a Settings tab,
not a modal), scoped to grow into a general "consumer/mobile-app management" area over time
(loyalty now; news/promos/etc. later, NOT scaffolded now), covering **all 5 existing backend
fields** plus the new `customerCodeFormat`. Backend contract finalized by the parallel TASK-499
agent: `LoyaltyProgramSettingsDto`/`UpsertLoyaltyProgramSettingsRequest` now carry
`CustomerCodeFormat` (string, "qr"|"barcode", inserted after `CodeTtlSeconds`, before
`UpdatedAt`), validated server-side in `LoyaltyService.UpsertSettingsAsync`
(`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`).

## What was built

- **Route:** `frontend/app/(dashboard)/consumer-app/page.tsx` — new standalone page, gated to
  `AT_LEAST_ENTERPRISE_ADMIN` (mirrors `LoyaltySettingsController`'s
  `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]` exactly: provider, enterprise_admin
  only). No module-key gating added — the backend endpoint itself has no `[RequireModule]`, so
  none was invented client-side. Renders a header + `<BonusProgramSection />`; deliberately just
  a vertical stack of cards so a future sibling section is a one-line addition, with no
  placeholder "coming soon" cards scaffolded (per explicit out-of-scope instruction).
- **Feature dir:** `frontend/features/consumer-app/` (new) — `types.ts` (`LoyaltyProgramSettings`
  read type, `UpdateLoyaltyProgramSettingsRequest` write type, `CustomerCodeFormat = "qr" |
  "barcode"`), `api/loyaltySettings.ts` (`fetchLoyaltySettings`/`updateLoyaltySettings` via the
  shared `api` client), `hooks/useLoyaltySettings.ts` (React Query query+mutation, same shape as
  `features/integrations/hooks/usePrroSettings.ts`), `components/BonusProgramSection.tsx` (the
  form — all 5 pre-existing fields, which never had any UI before this task, plus
  `customerCodeFormat`).
- **Form conventions:** no existing loyalty-settings UI to match, so followed the closest real
  analog referenced by the backend's own doc comment ("same upsert shape as
  PrroSettingsController") — `features/integrations/components/PrroConfigModal.tsx`'s plain
  `useState` + manual-validation pattern, same color/style constants, `sonner` toast on
  save, `Btn`/`Switch` from `components/ui/`. Client-side numeric bounds mirror
  `LoyaltyService.UpsertSettingsAsync` exactly: accrual/redemption-cap 0-100, min balance ≥ 0,
  code TTL 5-300s integer.
- **`customerCodeFormat` field:** `<select>` with exactly the two options/copy specified — label
  "Формат картки покупця", options "QR-код" / "Штрихкод Code 128", helper text "Цей формат буде
  використовуватися в усіх магазинах мережі." (uk.json); English mirror in en.json.
- **Sidebar:** `frontend/components/layout/Sidebar.tsx` — new single-item `NavGroup`
  (`key: "consumer_app"`, `Smartphone` icon from lucide-react) placed between
  `marketing_analytics` and `workforce`, item `href: "/consumer-app"`, roles
  `AT_LEAST_ENTERPRISE_ADMIN`.
- **i18n:** `frontend/messages/uk.json` + `en.json` — `Dashboard.sidebar.groups.consumerApp`
  (label + item label) and a new `Dashboard.consumerApp.{page,bonusProgram}` namespace (page
  title/subtitle, all field labels/hints/errors, save/loading/error copy). Both files kept
  structurally parallel per existing convention; validated with `JSON.parse` after editing.

## Verification

- `npx tsc --noEmit` (frontend/): clean, 0 errors.
- `npm run lint` (frontend/): "No ESLint warnings or errors" — matches exactly what
  `frontend-ci` runs in `.github/workflows/ci.yml`.
- Payload trace: `BonusProgramSection.handleSubmit` builds an explicit object literal (not a
  spread) passed to `update.mutateAsync({ isEnabled, accrualRatePercent, redemptionCapPercent,
  minRedemptionBalance, codeTtlSeconds, customerCodeFormat })` → `useUpdateLoyaltySettings` →
  `updateLoyaltySettings(body)` → `api.put("/api/settings/loyalty", body)` — `customerCodeFormat`
  confirmed present in the outgoing PUT body by direct code read (no live backend available
  locally to round-trip against; TASK-499's backend isn't deployed yet from this task's vantage
  point, same limitation the brief itself anticipated).
- Round-trip read path: `useLoyaltySettings()` → GET response typed as `LoyaltyProgramSettings`
  → `useEffect` seeds all 6 form fields (including `customerCodeFormat`) from whatever the
  backend returned, including its "barcode"/3%/50%/0/30s proposed-default shape for a tenant
  that never saved a row.
- Live dev-server check (no backend running, so this only proves compile/render correctness, not
  data round-trip): started `frontend-dev` via `preview_start`, navigated to `/consumer-app` —
  Next.js log shows `Compiled /consumer-app in 24.9s (925 modules)` and `GET /consumer-app 200`,
  no errors from the new code (the one logged warning, `IntlError: ENVIRONMENT_FALLBACK`
  timeZone, is a pre-existing, unrelated warning from the shared `(dashboard)/layout.tsx`
  Loading component, not from this task's files). Client then redirected away because there was
  no authenticated session locally (expected — `useMe()` has nothing to resolve without a
  backend); this exercises the exact same pre-existing "no session" redirect path every other
  dashboard route hits, not a defect in the new page.
- Two `<option>` values (`"qr"`/`"barcode"`) are unconditional JSX, confirmed by direct file
  read — not gated behind any data condition beyond the form itself rendering.

## Deviations from the field name

None — `customerCodeFormat` used exactly as specified, matching the finalized backend DTO field
name (`CustomerCodeFormat` → camelCase on the wire).

## Files touched

- `frontend/app/(dashboard)/consumer-app/page.tsx` (new)
- `frontend/features/consumer-app/types.ts` (new)
- `frontend/features/consumer-app/api/loyaltySettings.ts` (new)
- `frontend/features/consumer-app/hooks/useLoyaltySettings.ts` (new)
- `frontend/features/consumer-app/components/BonusProgramSection.tsx` (new)
- `frontend/components/layout/Sidebar.tsx` (edited — new NavGroup + `Smartphone` import)
- `frontend/messages/uk.json`, `frontend/messages/en.json` (edited — new i18n keys)

Nothing staged or committed (repo convention — user reviews/commits).
