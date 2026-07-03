# BUG-013 — provider wizard: supplier business type + Cyrillic slug

**Agent:** frontend-developer · **Date:** 2026-07-03 · **Status:** done

## Problem
1. Майстер «Новий клієнт» (`features/provider/components/CreateTenantWizard.tsx`) не мав
   типу бізнесу «Постачальник» — supplier було додано лише в admin (TASK-286).
2. Кирилична назва компанії → локальний `slugify` викидав усі не-ASCII → slug порожній →
   `canGoStep2 = false` → «Далі» заблоковано. Та сама вада в `admin/CreateTenantModal.tsx`
   (`autoSlug`).

## Changes
- `frontend/features/provider/types.ts`:
  - `BusinessType` + `"supplier"`; label «Постачальник», icon 🚚, ALL_BUSINESS_TYPES,
    preset `["marketplace_supplier"]`.
  - `TenantModule` + `"marketplace_supplier"`; MODULE_LABELS («Кабінет постачальника»),
    MODULE_DESCRIPTIONS, ALL_MODULES. Звірено з backend `Tenant.cs` (TASK-282): обидва
    ключі валідні, preset збігається з `DefaultModulesForBusinessType("supplier")`.
- `frontend/lib/slug.ts` (new): спільний `slugify` з транслітерацією укр→лат
  (а→a … щ→shch, ю→yu, я→ya, ї→yi, є→ye, й→i, х→kh, ц→ts, ч→ch, ж→zh, г→h, ґ→g, и→y,
  і→i + ru extras) → sanitize → max 32 символи. Назва зберігається як введена.
- `CreateTenantWizard.tsx`: локальний slugify видалено, імпорт з `@/lib/slug`.
- `admin/components/CreateTenantModal.tsx`: `autoSlug` видалено, імпорт `slugify` з
  `@/lib/slug`.
- Крок «Модулі» рендериться з `ALL_MODULES` — `marketplace_supplier` показується і
  авто-обирається пресетом для supplier без змін у компоненті.

## Verify
- `npx tsc --noEmit` — clean.
- `npm run build` — green (40/40 pages).
