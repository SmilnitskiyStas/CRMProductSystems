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
  /** "yyyy-MM-dd" — when the output is needed (from store requests). */
  neededBy?: string | null;
  plannedDate?: string | null;
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
  neededBy?: string | null;
  plannedDate?: string | null;
  requests?: ProductionOrderRequestRefDto[] | null;
}

export interface ProductionOrderRequestRefDto {
  requestId: string;
  requestLineId: string;
  storeLocationName: string;
  qty: number;
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

// ─── Store → production requests ──────────────────────────────────────────────

export type ProductionRequestStatus =
  | "draft"
  | "submitted"
  | "accepted"
  | "partial"
  | "rejected"
  | "fulfilled"
  | "cancelled";

export interface ProductionRequestListItemDto {
  id: string;
  storeLocationId: string;
  storeLocationName: string;
  productionLocationId: string;
  productionLocationName: string;
  neededBy: string;
  status: ProductionRequestStatus;
  /** manual | auto_buffer | auto_promo */
  source: string;
  lineCount: number;
  totalRequestedQty: number;
  totalAcceptedQty: number | null;
  createdAt: string;
  submittedAt: string | null;
}

export interface ProductionRequestLineDto {
  id: string;
  itemId: string;
  itemName: string;
  unit: string;
  requestedQty: number;
  acceptedQty: number | null;
  reason: string | null;
  /** Human text for auto-generated draft lines, e.g. "Буфер: на складі 4 кг, потрібно 60". */
  autoReason?: string | null;
}

export interface ProductionRequestDetailDto
  extends Omit<ProductionRequestListItemDto, "lineCount" | "totalRequestedQty" | "totalAcceptedQty"> {
  notes: string | null;
  reviewNote: string | null;
  reviewedAt: string | null;
  shippedAt: string | null;
  shipmentTransferId: string | null;
  shipmentStatus: string | null;
  lines: ProductionRequestLineDto[];
}

export interface ProductionRequestFilters {
  storeLocationId?: string;
  productionLocationId?: string;
  /** Comma-separated statuses. */
  status?: string;
  neededFrom?: string;
  neededTo?: string;
}

export interface ProductionRequestCreateDto {
  storeLocationId: string;
  productionLocationId: string;
  neededBy: string;
  notes?: string;
  lines: { itemId: string; requestedQty: number }[];
  submit?: boolean;
}

export interface ProductionRequestUpdateDto {
  neededBy?: string;
  notes?: string;
  lines?: { itemId: string; requestedQty: number }[];
}

export interface ProductionRequestReviewDto {
  lines: { lineId: string; acceptedQty: number; reason?: string }[];
  note?: string;
}

export interface ProductionRequestSummaryItemDto {
  itemId: string;
  itemName: string;
  unit: string;
  requestCount: number;
  requestedQty: number;
  qtyToProduce: number;
  hasRecipe: boolean;
  earliestNeededBy: string | null;
}

export interface OrdersFromRequestsDto {
  requestIds: string[];
  plannedDate?: string;
  notes?: string;
}

export interface OrderFromRequestResultDto {
  orderId: string;
  itemId: string;
  itemName: string;
  recipeName: string;
  plannedQty: number;
  requestLineCount: number;
}
