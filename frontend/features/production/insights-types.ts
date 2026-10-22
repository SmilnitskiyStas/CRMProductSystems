// TASK-764: read-model DTOs for production Overview / Plan / Shipments / Warehouse (TASK-763 backend).
// Everything is optional/tolerant on purpose — the backend contract is being built in parallel.

export interface ProductionOverviewDto {
  incomingRequests?: number;
  awaitingOrder?: number;
  ordersPlanned?: number;
  ordersInProgress?: number;
  readyToShip?: number;
  shortages?: number;
  expiringSoon?: number;
  newRequests?: { id: string; storeName?: string | null; neededBy?: string | null; totalQty?: number }[];
  inProgressOrders?: { id: string; recipeName?: string | null; plannedQty?: number; startedAt?: string | null }[];
  readyToShipList?: { requestId: string; storeName?: string | null; qty?: number }[];
  expiring?: {
    itemName?: string | null;
    quantity?: number;
    unit?: string | null;
    expiryDate?: string | null;
    locationName?: string | null;
    kind?: string | null;
  }[];
  shortageList?: {
    itemId: string;
    itemName?: string | null;
    unit?: string | null;
    required?: number;
    available?: number;
    missing?: number;
  }[];
}

export type PlanGranularity = "day" | "week" | "month";

export interface ProductionPlanDto {
  from?: string;
  to?: string;
  granularity?: PlanGranularity;
  buckets?: { key: string; label: string; from?: string; to?: string }[];
  products?: {
    itemId: string;
    itemName?: string | null;
    unit?: string | null;
    hasRecipe?: boolean;
    perBucket?: { bucketKey: string; requestedQty?: number; pendingQty?: number; forecastQty?: number }[];
    totalRequested?: number;
    totalPending?: number;
    totalForecast?: number;
    finishedOnHand?: number;
    openOrderQty?: number;
    toProduce?: number;
  }[];
  ingredients?: {
    itemId: string;
    itemName?: string | null;
    unit?: string | null;
    required?: number;
    onHand?: number;
    missing?: number;
    defaultSupplierId?: string | null;
    defaultSupplierName?: string | null;
  }[];
}

export interface ShipmentLineDto {
  itemId?: string;
  itemName?: string | null;
  unit?: string | null;
  qty?: number;
}

export interface ReadyShipmentDto {
  requestId: string;
  storeLocationId?: string;
  storeName?: string | null;
  neededBy?: string | null;
  productionStage?: string | null;
  lines?: ShipmentLineDto[];
  orderIds?: string[];
  canShip?: boolean;
}

export interface ShippedShipmentDto {
  requestId: string;
  storeLocationId?: string;
  storeName?: string | null;
  shippedAt?: string | null;
  transferId?: string | null;
  transferStatus?: string | null;
  lines?: ShipmentLineDto[];
}

export type ShipmentsStatus = "ready" | "shipped";

export type StockKind = "all" | "raw" | "finished";

export interface ProductionStockBatchDto {
  id: string;
  batchNumber?: string | null;
  quantity?: number;
  expiryDate?: string | null;
  daysToExpiry?: number | null;
  status?: string | null;
  sourceType?: string | null;
  sourceId?: string | null;
}

export interface ProductionStockItemDto {
  itemId: string;
  itemName?: string | null;
  unit?: string | null;
  kind?: "raw" | "finished" | "other" | string;
  totalQty?: number;
  nearestExpiry?: string | null;
  locationName?: string | null;
  batches?: ProductionStockBatchDto[];
}
