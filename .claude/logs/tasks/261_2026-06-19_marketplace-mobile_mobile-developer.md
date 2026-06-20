# TASK-261 — Mobile: Marketplace screens

**Agent:** mobile-developer  
**Date:** 2026-06-19  
**Status:** done

## Що зроблено

Реалізовано мобільний Marketplace модуль.

### Нові файли

**Feature layer:**
- `mobile/features/marketplace/types.ts` — TS interfaces (SupplierListItem, SupplierProfile, SupplierMetrics, SupplierItem, SupplierReview, PagedResult<T>)
- `mobile/features/marketplace/api.ts` — getSuppliers, searchSuppliers, getSupplierById, getSupplierItems, createReview
- `mobile/features/marketplace/hooks/useMarketplace.ts` — React Query hooks: useSuppliers, useSearchSuppliers, useSupplierById, useSupplierItems, useCreateReview

**Screens:**
- `mobile/app/(app)/marketplace/index.tsx` — список постачальників + пошук
- `mobile/app/(app)/marketplace/[id].tsx` — деталі: профіль, метрики, каталог, відгуки + modal

### Оновлені файли
- `mobile/app/(app)/_layout.tsx` — реєстрація `marketplace/index` + `marketplace/[id]` як hidden routes
- `mobile/app/(app)/more/index.tsx` — додано "Маркетплейс" першим у список модулів

## Функціонал

### Listing screen (marketplace/index.tsx)
- FlatList постачальників (GET /api/marketplace/suppliers)
- SearchBar → POST /api/marketplace/search при натисканні "Знайти"
- Скидання пошуку ("Скинути") повертає до paginated listing
- SupplierCard: назва, регіон, категорії (до 3+N), рейтинг зірками, час доставки, plan badge
- Pull-to-refresh, empty state

### Detail screen (marketplace/[id].tsx)
- Профіль: назва, регіон, категорії, робочий час, умови оплати
- Метрики: рейтинг + StarDisplay, плитки (доставка/точність/якість)
- Tabs: Каталог / Відгуки
  - Каталог: рядки товарів (назва, ціна, мін.кількість, badge наявності)
  - Відгуки: кнопка "Залишити відгук" (тільки авторизованим) + placeholder list
  - ReviewModal: StarPicker (1-5) + textarea + POST /reviews; 409 → українське повідомлення
- Back navigation

## Прийнято
- `npx tsc --noEmit` — 0 помилок
- Публічні ендпоінти: apiClient прикріплює Bearer token якщо є, але не вимагає (anonymous доступ)
- BUG-006 вже виправлено (MarketplaceController) — anonymous GET /suppliers повертає 200
