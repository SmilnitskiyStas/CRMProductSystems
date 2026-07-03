# TASK-286 + TASK-287 — Supplier cabinet UI + marketplace enrichment (v4.1, ADR-016)

**Date:** 2026-07-03 · **Agent:** frontend-developer · **Status:** done
**Depends on:** TASK-284, TASK-285 (backend done, log 283-285_2026-07-02).

## TASK-286 — supplier cabinet

- `lib/roles.ts`: `AppRoles.SupplierAdmin = "supplier_admin"` + set `SUPPLIER_ONLY`;
  роль НЕ додана до жодного tenant-staff set (TENANT_ROLES тощо).
- Нова feature `features/supplier-cabinet/` (`types.ts`, `api/supplier-cabinet-api.ts`,
  `hooks/useSupplierCabinet.ts`, `components/`): CabinetProfileForm (редагування +
  publish/hide toggle зі статус-бейджем), CabinetItemsTable (+ inline confirm delete),
  CabinetItemModal (create/edit, patch-семантика PUT), CabinetReviews
  (read-only, рейтинг-summary + пагінація). Типи відповідають backend DTO
  (SupplierProfileDto/SupplierItemDto/PublicSupplierReviewDto/PagedResult).
- Сторінки `(dashboard)/supplier/profile|items|reviews` (роль-guard на сторінці).
- `Sidebar.tsx`: для supplier_admin рендериться ТІЛЬКИ група «Кабінет постачальника»
  (Профіль / Мої товари / Відгуки) + Налаштування; NAV_GROUPS і module-fetch пропущені.
- `TopBar.tsx`: StoreSelector схований для supplier_admin (нема stores → 403).
- `useAuth.ts`: після логіну supplier_admin → `/supplier/profile` (не /dashboard).
- Admin onboarding: `CreateTenantModal` — select «Тип бізнесу» (всі valid backend
  business types, вкл. `supplier` з підказкою про supplier_admin); `businessType`
  доданий у `CreateTenantRequest`.

## TASK-287 — marketplace enrichment

- `features/marketplace/types.ts` приведено до фактичних backend DTO: новий
  `SupplierListItemDto` (листинг: id/name/rating/categories/…), `SupplierProfileDto`
  тепер `supplierId/supplierName/…/metrics` (раніше фронт читав неіснуючі
  `companyName`/`rating` flat — назва й рейтинг на детальній сторінці були undefined),
  `SupplierItemDto` nullable-поля, `PublicSupplierReviewDto`, `SupplierMetricsDto`.
- `/marketplace` картки (`SupplierCard`): зірки + число рейтингу, кількість відгуків
  (легкий запит `reviews?pageSize=1` → `total`, hook `useSupplierReviewCount`), категорії.
- `/marketplace/[id]`: рейтинг з `metrics`, кількість відгуків у хедері,
  `SupplierMetrics` приймає `metrics`; вкладка «Відгуки» — пагінований список з
  публічного `GET /suppliers/{id}/reviews` (зірки, дата, `reviewerName`), лічильник.
- Кнопка «Залишити відгук» — тільки для клієнтських tenant-ролей (`TENANT_ROLES`;
  provider team і supplier_admin не бачать). `ReviewModal`: 409 (дубль) і 400-guards
  (self-review / supplier-tenant) мапляться на українські повідомлення.
- `useCreateReview` інвалідовує reviews (всі сторінки), review-count, профіль і
  листинг — рейтинг оновлюється одразу після відгуку.
- `utils.ts`: `reviewWord` (укр. плюралізація «відгук/відгуки/відгуків»).

## Verify
`npx tsc --noEmit` — clean. `npm run build` — green (нові роути
/supplier/profile, /supplier/items, /supplier/reviews у виводі).

## Next
TASK-288 (QA regression). Деплой backend+frontend разом (типи фронта тепер
відповідають новим DTO).
