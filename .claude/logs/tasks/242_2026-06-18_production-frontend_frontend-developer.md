# TASK-242 — Production Module Frontend

**Agent:** frontend-developer
**Date:** 2026-06-18
**Status:** done

## Summary

Implemented the full Production Module frontend for ShelfGuard.

## Files Created

### Feature folder: `frontend/features/production/`

- `types.ts` — TypeScript interfaces matching backend DTOs:
  `RecipeListItemDto`, `RecipeDetailDto`, `RecipeCreateDto`, `RecipeUpdateDto`,
  `ProductionOrderListItemDto`, `ProductionOrderDetailDto`, `ProductionOrderCreateDto`,
  `ProductionCompleteResultDto`, `ItemSlimDto`

- `api/production-api.ts` — API client using `@/lib/api` (recipes CRUD + orders CRUD + complete/cancel + items/locations selectors)

- `hooks/useProduction.ts` — React Query hooks:
  `useRecipes`, `useRecipe`, `useCreateRecipe`, `useUpdateRecipe`, `useDeactivateRecipe`,
  `useProductionOrders`, `useProductionOrder`, `useCreateProductionOrder`,
  `useUpdateProductionOrder`, `useCompleteProductionOrder`, `useCancelProductionOrder`,
  `useProductionItems`, `useProductionLocations`

- `components/RecipeTable.tsx` — Table with name, output item, qty+unit, ingredient count, status badge; Edit and Deactivate row actions
- `components/RecipeForm.tsx` — Create/Edit modal with dynamic ingredient rows (add/remove, min 1 enforced)
- `components/ProductionOrderTable.tsx` — Table with recipe, location, planned qty, status badge, dates; status filter dropdown; click-to-detail navigation
- `components/ProductionOrderForm.tsx` — Create modal (recipe, location, planned qty, notes)
- `components/ProductionOrderDetail.tsx` — Detail page with status action buttons, complete/cancel flow, 422 insufficient-stock error parsing, consumptions table

### Pages: `frontend/app/(dashboard)/production/`

- `recipes/page.tsx` — Recipes list with show-inactive toggle; module gate
- `orders/page.tsx` — Orders list; module gate
- `orders/[id]/page.tsx` — Order detail; module gate

### Sidebar update

Added "Виробництво" nav group to `frontend/components/layout/Sidebar.tsx`:
- `moduleKey: "production"` — group hidden when module inactive
- Рецепти → `/production/recipes`
- Ордери → `/production/orders`
- Icons: `FlaskConical`, `ListOrdered` (from lucide-react)

## Acceptance Criteria

- [x] `tsc --noEmit` — green (no output)
- [x] `next build` — green (33 routes compiled successfully)
- [x] Recipes CRUD (create with dynamic ingredients, edit name/notes, deactivate)
- [x] Orders list with status filter + create modal
- [x] Order detail with planned → in_progress → done flow + cancel
- [x] 422 insufficient stock error parsed and displayed
- [x] Sidebar "Виробництво" group with moduleKey="production" gate
- [x] Module gate on all 3 pages

## Notes

- Edit recipe only supports name/notes/isActive fields via `RecipeUpdateDto` — ingredient editing is not in the backend PUT contract
- `ProductionCompleteResultDto` returns `newStockBatchId` (UUID); batch number display uses first 8 chars of the UUID as a human-readable identifier since a dedicated `batchNumber` field is not in the complete response
- `useModules` called with `!!userRole && userRole !== "provider"` guard pattern (same as Sidebar) — pages use simpler `!modulesData || modules.includes("production")` pattern (optimistic until loaded)
