# TASK-281 — Dashboard і /stock: консистентний фільтр магазину

**Agent:** frontend-developer · **Date:** 2026-07-02 · **Status:** done

## Проблема
Dashboard-віджети (`getStats`, `getAttentionItems`, `getStoreZones`) викликали
`/api/stock/summary`, `/api/stock?pageSize=200`, `/api/stock/zones-summary` без
`store_id` — агрегували дані всіх магазинів. Сторінка `/stock` фільтрує за
`selectedStoreId` з header StoreSelector (`useStoreContext`). Після кліку
«Переглянути всі» (TASK-280) користувач бачив порожній список, якщо вибраний
магазин не містив партій з дашборда.

## Зміни
- `frontend/features/dashboard/api/dashboard.ts`
  - helper `withStore(path, storeId)` — додає `store_id=` до URL, якщо storeId задано
  - `getDashboardStats`, `getAttentionItems`, `getStoreZones` приймають `storeId: string | null`
- `frontend/features/dashboard/hooks/useDashboard.ts`
  - всі три хуки читають `selectedStoreId` з `useStoreContext`
  - `selectedStoreId` включено в queryKey → зміна магазину рефетчить дашборд

## Перевірено
- Бекенд `StockController` приймає `Guid? store_id` на всіх трьох ендпоінтах (BUG-002 підтверджував summary)
- `/stock` fallback: `filters.store_id || selectedStoreId || undefined` — той самий магазин, що й дашборд
- StoreSelector «всі магазини» (`selectedStoreId = null`) → параметр не додається, обидві сторінки показують все
- Інші віджети дашборда (StatsCards, StoreMap, QuickActions) споживають дані лише через ці три хуки — інших mismatch немає
- `npx tsc --noEmit` — green; `npm run build` — green
