# TASK-262 — Mobile: Production Module screens

**Agent:** mobile-developer
**Date:** 2026-06-20
**Status:** done

## Summary

Реалізовано мобільний Production Module для ShelfGuard.
Backend (TASK-241) і Web (TASK-242) вже були готові; залишалась тільки мобільна частина.

## Нові файли

### Feature layer
- `mobile/features/production/types.ts` — TS interfaces: RecipeListItem, RecipeDetail, RecipeIngredient, ProductionOrderListItem, ProductionOrderDetail, ProductionConsumption, ProductionOrderCreate, ProductionCompleteResult, ProductionStatus
- `mobile/features/production/api.ts` — getRecipes, getRecipeById, getProductionOrders, getProductionOrderById, createProductionOrder, completeProductionOrder, cancelProductionOrder
- `mobile/features/production/hooks/useProduction.ts` — React Query hooks: useRecipes, useRecipeById, useProductionOrders, useProductionOrderById, useCreateProductionOrder, useCompleteProductionOrder, useCancelProductionOrder

### Screens
- `mobile/app/(app)/production/index.tsx` — список ордерів:
  - FlatList з OrderCard (recipe, location, status badge, plannedQty, дата)
  - Horizontal scroll фільтр по статусу (all / planned / in_progress / done / cancelled)
  - FAB "+" → CreateOrderModal (recipe picker + plannedQty + notes)
  - Pull-to-refresh
  - Кнопка "Рецепти" в header → `/production/recipes`
- `mobile/app/(app)/production/[id].tsx` — деталь ордера:
  - Інфо-картка (recipe, location, qty, created/started/completed at, notes)
  - Action buttons (Завершити + Скасувати) для planned/in_progress статусів
  - Alert-confirmation перед complete (з описом FEFO-списання)
  - Alert-confirmation перед cancel (destructive)
  - Success alert показує outputQty + outputItemName + перші 8 символів newStockBatchId
  - 422 → "Недостатньо запасів" повідомлення
  - 409 → "Неможливо завершити" повідомлення
  - Consumptions list (itemName, qtyConsumed, batchNumber, consumedAt)
  - Status banners для done/cancelled стану
- `mobile/app/(app)/production/recipes/index.tsx` — список рецептів:
  - FlatList з RecipeCard (name, outputQty+unit+outputItemName, ingredientCount)
  - Badge "Неактивний" для isActive=false
  - Pull-to-refresh, empty state

## Оновлені файли
- `mobile/app/(app)/_layout.tsx` — registered hidden routes: production/index, production/[id], production/recipes/index
- `mobile/app/(app)/more/index.tsx` — "Виробництво" (flask-outline, #7c3aed) після Маркетплейс

## Технічні деталі
- `user.locationId` з useAuthStore як locationId при створенні ордера (same pattern as write-offs)
- CreateOrderModal показує тільки isActive=true рецепти в picker
- completeProductionOrder повертає ProductionCompleteResult → відображається в Alert
- Ніяких StyleSheet.create — тільки NativeWind className
- FlatList скрізь, не ScrollView+map

## Acceptance criteria
- [x] npx tsc --noEmit — 0 помилок
- [x] SafeAreaView на всіх 3 кореневих екранах
- [x] FlatList для всіх списків
- [x] React Query для server state
- [x] hidden routes зареєстровані в _layout.tsx
- [x] "Виробництво" в more/index.tsx MODULES
- [x] FEFO complete flow з error handling (422 + 409)
- [x] Consumptions list на detail screen
