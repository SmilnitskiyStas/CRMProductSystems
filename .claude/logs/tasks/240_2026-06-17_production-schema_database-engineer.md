# TASK-240 — DB: Production schema
**Agent:** database-engineer
**Date:** 2026-06-17
**Status:** done

## What was done

Created the full DB schema for the Production module (Phase 5).

### Domain Entities (ShelfGuard.Domain/Entities/)
- `ProductionOrderStatus.cs` — enum: `Planned | InProgress | Done | Cancelled`
- `Recipe.cs` — recipe with navigation to `RecipeIngredient` list and `Item` (output)
- `RecipeIngredient.cs` — ingredient line; no `TenantId` (tenant scope via parent Recipe JOIN)
- `ProductionOrder.cs` — production order with `ProductionOrderStatus` enum, navigations to Recipe, Location, User (creator), Consumptions
- `ProductionOrderConsumption.cs` — FEFO batch consumption record; no `TenantId` (tenant scope via parent ProductionOrder JOIN)

### AppDbContext (ShelfGuard.Infrastructure/Data/AppDbContext.cs)
Added 4 DbSets and Fluent API configuration:
- `Recipes`, `RecipeIngredients`, `ProductionOrders`, `ProductionOrderConsumptions`
- `ProductionOrderStatus` mapped to TEXT via `.HasConversion<string>()`
- All FK relationships with correct ON DELETE behaviors:
  - `recipe_ingredients.recipe_id` → CASCADE (child of recipe)
  - `recipe_ingredients.item_id` → RESTRICT (ingredient items must not be deleted)
  - `production_orders.recipe_id` → RESTRICT
  - `production_orders.location_id` → RESTRICT
  - `production_orders.created_by` → SET NULL (nullable)
  - `production_order_consumptions.production_order_id` → CASCADE (child of order)
  - `production_order_consumptions.item_id` → RESTRICT
  - `production_order_consumptions.product_stock_id` → RESTRICT (FEFO batch reference)

### Migration: 20260617194105_V4ProductionSchema.cs
4 tables created:
- `recipes` — TenantId + RLS
- `recipe_ingredients` — no TenantId, no RLS (parent-scoped)
- `production_orders` — TenantId + RLS + CHECK constraint on Status
- `production_order_consumptions` — no TenantId, no RLS (parent-scoped)

RLS applied to `recipes` and `production_orders`:
- `tenant_isolation` policy (strict `current_setting('app.tenant_id', true)::uuid`)
- `provider_bypass` policy

CHECK constraint: `CK_production_orders_status` — restricts Status to `'Planned','InProgress','Done','Cancelled'`

## Acceptance criteria verification
1. `dotnet ef migrations add V4ProductionSchema` — completed without error
2. `dotnet build` — green, 0 errors, 0 warnings
3. `dotnet test` — 450/450 passed
4. Migration SQL: 4 tables, all FKs, indexes, CHECK on status, RLS on recipes + production_orders
5. Task log: this file
6. backlog.md: TASK-240 → done

## Files changed
- `backend/ShelfGuard.Domain/Entities/ProductionOrderStatus.cs` (new)
- `backend/ShelfGuard.Domain/Entities/Recipe.cs` (new)
- `backend/ShelfGuard.Domain/Entities/RecipeIngredient.cs` (new)
- `backend/ShelfGuard.Domain/Entities/ProductionOrder.cs` (new)
- `backend/ShelfGuard.Domain/Entities/ProductionOrderConsumption.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (updated)
- `backend/ShelfGuard.Infrastructure/Migrations/20260617194105_V4ProductionSchema.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260617194105_V4ProductionSchema.Designer.cs` (new)
