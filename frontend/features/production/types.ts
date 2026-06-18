// ─── Production Feature Types ──────────────────────────────────────────────────
// Matches API DTOs from TASK-241 (Production Module API)

// ─── Recipes ──────────────────────────────────────────────────────────────────

export interface RecipeListItemDto {
  id: string;
  name: string;
  outputItemName: string;
  outputQty: number;
  unit: string;
  ingredientCount: number;
  isActive: boolean;
}

export interface RecipeIngredientDto {
  id: string;
  itemId: string;
  itemName: string;
  qty: number;
  unit: string;
}

export interface RecipeDetailDto {
  id: string;
  name: string;
  outputItemId: string;
  outputItemName: string;
  outputQty: number;
  unit: string;
  notes: string | null;
  isActive: boolean;
  ingredients: RecipeIngredientDto[];
}

export interface RecipeCreateDto {
  name: string;
  outputItemId: string;
  outputQty: number;
  unit: string;
  notes?: string;
  ingredients: RecipeIngredientCreateDto[];
}

export interface RecipeIngredientCreateDto {
  itemId: string;
  qty: number;
  unit: string;
}

export interface RecipeUpdateDto {
  name?: string;
  notes?: string;
  isActive?: boolean;
}

// ─── Production Orders ─────────────────────────────────────────────────────────

export type ProductionOrderStatus = "planned" | "in_progress" | "done" | "cancelled";

export interface ProductionOrderListItemDto {
  id: string;
  recipeName: string;
  locationName: string;
  plannedQty: number;
  status: ProductionOrderStatus;
  createdAt: string;
  completedAt: string | null;
}

export interface ProductionConsumptionDto {
  itemId: string;
  itemName: string;
  qtyConsumed: number;
  batchNumber: string | null;
  consumedAt: string;
}

export interface ProductionOrderDetailDto {
  id: string;
  recipeId: string;
  recipeName: string;
  locationName: string;
  plannedQty: number;
  status: ProductionOrderStatus;
  notes: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  consumptions: ProductionConsumptionDto[];
}

export interface ProductionOrderCreateDto {
  recipeId: string;
  locationId: string;
  plannedQty: number;
  notes?: string;
}

export interface ProductionOrderUpdateDto {
  status?: string;
  notes?: string;
}

export interface ProductionCompleteResultDto {
  productionOrderId: string;
  newStockBatchId: string;
  outputQty: number;
  outputItemName: string;
}

// ─── Inventory item slim (for selectors) ──────────────────────────────────────

export interface ItemSlimDto {
  id: string;
  name: string;
  unit: string;
}
