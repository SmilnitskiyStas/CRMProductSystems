# TASK-071 (frontend part) — ПРРО Settings UI
**Date:** 2026-06-13
**Agent:** frontend-developer
**Status:** done

## What was built

### New files

| File | Purpose |
|---|---|
| `frontend/features/integrations/api/prro.ts` | API functions: `fetchPrroSettings`, `updatePrroSettings`, `testPrroConnection`; types `PrroSettings`, `UpdatePrroSettingsRequest`, `PrroTestResult`, `PrroProvider`; `CHECKBOX_BASE_URLS` constants |
| `frontend/features/integrations/hooks/usePrroSettings.ts` | React Query hooks: `usePrroSettings()` (GET), `useUpdatePrroSettings()` (PUT mutation + cache invalidate), `useTestPrroConnection()` (POST mutation, result shown inline) |
| `frontend/features/integrations/components/PrroConfigModal.tsx` | Dedicated ПРРО config modal: provider select (Вимкнено / Checkbox, extensible via `<select>`), test/prod radio for base URL, license key password input, cashier auth mode toggle (PIN / Логін+Пароль), connection test button with inline result display |

### Modified files

| File | Change |
|---|---|
| `frontend/features/integrations/types.ts` | Replaced stale `prro` `fields[]` (api_url, api_key, cashier_id) with `fields: []` + comment explaining the dedicated modal |
| `frontend/features/settings/components/IntegrationsTab.tsx` | Added `usePrroSettings()` pre-fetch; renders dedicated `PrroCard` + `PrroConfigModal` for ПРРО; generic `IntegrationCard` + `IntegrationConfigModal` for all other services |

## Architecture decisions

- ПРРО settings use dedicated endpoints (`/api/settings/prro`) rather than the generic `/api/integrations/prro` — kept as a separate hook/API file to avoid polluting the generic integration abstraction.
- Masked-secret sentinel: when the user submits without touching a password/key field, the current masked value (e.g. `••••••••`) is sent as-is. The backend interprets this as "keep existing secret".
- Provider select is the extension point for future providers (e.g. Vchasno, Poster) — adding a new `<option>` and a new conditional block is all that is needed.
- `PrroCard` in `IntegrationsTab` shows a provider-aware badge: "Checkbox" (green) or "Вимкнено" (grey), derived from `usePrroSettings()` data.

## Acceptance criteria

- [x] `tsc --noEmit` — 0 errors
- [x] `next build` — compiled successfully, 23 static pages generated
- [x] Provider select with Вимкнено / Checkbox options
- [x] Checkbox environment radio (Тестовий / Виробничий) mapping to correct base URLs
- [x] License Key, PIN, Login, Password as password inputs with masked-value pre-fill
- [x] «Перевірити з'єднання» → POST /api/settings/prro/test → inline result with fiscal number + cashier status
- [x] Status badge in IntegrationsTab card
