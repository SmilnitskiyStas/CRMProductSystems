# TASK-659 (T12): i18n sweep for the supplier-coverage feature

**Agent:** frontend-developer · **Date:** 2026-08-31 · **Status:** done (committed to main)
Plan: `eventual-whistling-rabbit.md` (Frontend § i18n bullet), T12 of T1–T16.
Depends on T7–T11 (TASK-654..658), all on main. Frontend only.

## Зроблено

Прибрано хардкод українських рядків зі спільних `features/geo` компонентів + мітку
регіону в `LocationFormDialog`. Ключі — через наявний `DashboardIntlProvider` (він
віддає лише `Common` + `Dashboard` слайси, тож **top-level `Geo` неможливий** —
використано `Dashboard.geo.*`, узгоджено з планом, де всі coverage-ключі під `Dashboard.*`).

### Файли компонентів (4)
- `features/geo/components/DeliveryCoverageEditor.tsx` — `useTranslations("Dashboard.geo.coverageEditor")`; 5 рядків (servedLabel, servedTermsPlaceholder, notServedLabel, noteLabel, notePlaceholder). Cyrillic-коментарі теж переведено на англ.
- `features/geo/components/RegionMultiSelect.tsx` — `Dashboard.geo.regionMultiSelect`; `loading` + `emptyHint` (використовується двічі — hint + placeholder).
- `features/geo/components/RegionSelect.tsx` — `Dashboard.geo.regionSelect`; `placeholder` prop-override збережено, fallback тепер `t("allPlaceholder")` / `t("choosePlaceholder")`.
- `features/locations/components/LocationFormDialog.tsx` — мітка `<RegionSelect>` `label={t("regionLabel")}`, `placeholder={t("regionPlaceholder")}` через наявний `Dashboard.locations.form`; прибрано stale TASK-659 коментарі.

### Ключі (обидві локалі, `frontend/messages/{uk,en}.json`) — 11 нових
`Dashboard.geo.coverageEditor.{servedLabel,servedTermsPlaceholder,notServedLabel,noteLabel,notePlaceholder}`
`Dashboard.geo.regionMultiSelect.{loading,emptyHint}`
`Dashboard.geo.regionSelect.{allPlaceholder,choosePlaceholder}`
`Dashboard.locations.form.{regionLabel,regionPlaceholder}`
uk = поточні літерали; en = переклади (напр. "Served regions", "All regions", "Region / city").

## Sweep

Grep по Cyrillic (JSX/props, не коментарі) у `features/geo`, `features/locations`,
`features/marketplace/components/{SupplierCoveragePanel,DeliveryByRegionPanel,CooperationCoveragePanel,SupplierMetrics}.tsx`,
`marketplace/[id]/page.tsx`, `SupplierProfileForm.tsx`, `CabinetProfileForm.tsx`, `SupplierFilters.tsx`,
`CooperationRequestModal.tsx`. **Понад список у брифі нічого не знайдено** — усе інше з
TASK-654..657 вже було keyed. Лишились тільки Cyrillic-коментарі в `features/geo/types.ts`
(UA-30/UA-32 пояснення) — не user-facing, не чіпав.

## Verification

- `npx tsc --noEmit` — clean
- `npm run lint` — clean
- `npx vitest run` — 50 passed / 7 files
- uk/en parity (node deep-key diff): 4611 == 4611, key sets identical
- Node-резолв усіх 11 нових ключів у обох локалях — OK
- Dev-server (frontend :3021, backend down): marketplace `SupplierFilters` `<RegionSelect>`
  рендерить fallback з ключа — "Усі регіони" (uk) / "All regions" (en, через `sg_locale=en`
  cookie); **0** next-intl missing-key / IntlError у консолі в обох локалях. Screenshot знято.
- **Не вдалось інтерактивно** (backend не піднятий → module-gate / geo API / supplier_admin роль):
  `DeliveryCoverageEditor`, `RegionMultiSelect` на екрані та форма локації. Покрито
  статичними перевірками (резолв ключів + tsc + спільний `RegionSelect` довів, що
  неймспейс вантажиться під `DashboardIntlProvider`).

## Commit

`feat(geo): i18n for shared region components + location form label (TASK-659)` — not pushed.
