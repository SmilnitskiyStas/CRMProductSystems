export type ProductionStatus = 'planned' | 'in_progress' | 'done' | 'cancelled';

export interface RecipeListItem {
  id: string;
  name: string;
  outputItemName: string;
  outputQty: number;
  unit: string;
  ingredientCount: number;
  isActive: boolean;
}

export interface RecipeIngredient {
  id: string;
  itemId: string;
  itemName: string;
  qty: number;
  unit: string;
}

export interface RecipeDetail {
  id: string;
  name: string;
  outputItemId: string;
  outputItemName: string;
  outputQty: number;
  unit: string;
  notes: string | null;
  isActive: boolean;
  ingredients: RecipeIngredient[];
}

export interface ProductionOrderListItem {
  id: string;
  recipeName: string;
  locationName: string;
  plannedQty: number;
  status: ProductionStatus;
  createdAt: string;
  completedAt: string | null;
}

export interface ProductionConsumption {
  itemId: string;
  itemName: string;
  qtyConsumed: number;
  batchNumber: string | null;
  consumedAt: string;
}

export interface ProductionOrderDetail {
  id: string;
  recipeId: string;
  recipeName: string;
  locationName: string;
  plannedQty: number;
  status: ProductionStatus;
  notes: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  consumptions: ProductionConsumption[];
}

export interface ProductionOrderCreate {
  recipeId: string;
  locationId: string;
  plannedQty: number;
  notes?: string;
}

export interface ProductionCompleteResult {
  productionOrderId: string;
  newStockBatchId: string;
  outputQty: number;
  outputItemName: string;
}
