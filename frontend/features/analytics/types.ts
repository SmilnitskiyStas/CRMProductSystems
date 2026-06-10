export interface ExpirySummaryStoreDto {
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface ExpirySummaryDto {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  total: number;
  stores: ExpirySummaryStoreDto[];
}

export interface WriteOffByReasonDto {
  reason: string;
  count: number;
  totalLoss: number;
}

export interface WriteOffByDateDto {
  date: string;
  count: number;
  totalLoss: number;
}

export interface WriteOffAnalyticsDto {
  totalDocuments: number;
  totalLoss: number;
  byReason: WriteOffByReasonDto[];
  byDate: WriteOffByDateDto[];
}

export interface MovementByTypeDto {
  movementType: string;
  count: number;
  totalQuantity: number;
}

export interface MovementAnalyticsDto {
  totalMovements: number;
  totalQuantity: number;
  byType: MovementByTypeDto[];
}

export interface ZoneAnalyticsDto {
  zoneId: string;
  zoneName: string;
  zoneType: string;
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
}

export interface CategoryAnalyticsDto {
  categoryId: string | null;
  categoryName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
  totalQuantity: number;
}

export interface LossByStoreDto {
  storeId: string;
  storeName: string;
  totalLoss: number;
  writeOffCount: number;
}

export interface LossesDto {
  totalLoss: number;
  totalWriteOffs: number;
  averageLossPerWriteOff: number;
  byStore: LossByStoreDto[];
}
