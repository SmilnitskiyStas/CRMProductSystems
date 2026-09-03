export interface OrderLine {
  productId: string;
  productName: string;
  barcode: string | null;
  bufferTotal: number;
  bufferGreen: number;
  bufferYellow: number;
  bufferRed: number;
  safetyBuffer: number;
  stockOnHand: number;
  /** Combined "already on the way" qty the formula subtracts: draft supplier receipts + open
   *  B2B marketplace orders headed to this store (Phase 4, plan D5). */
  inTransit: number;
  /** The marketplace slice of {@link inTransit} (open marketplace orders only). Always ≤
   *  inTransit; 0 for tenants not receiving via the B2B marketplace. Drives the in-transit
   *  source-breakdown tooltip in OrderLinesTable. */
  inTransitFromMarketplace: number;
  quantityRaw: number;
  quantityToOrder: number;
  moq: number;
  usq: number;
  rounding: "none" | "moq_floor" | "usq_rounded";
}

export interface OrderCalcResult {
  storeId: string;
  calculatedAt: string;
  productsEvaluated: number;
  linesToOrder: number;
  lines: OrderLine[];
}

export interface RecalcAduResult {
  storeId: string;
  productsProcessed: number;
  withEffectiveAdu: number;
  insufficientData: number;
}

export interface RecalcBuffersResult {
  storeId: string;
  buffersCalculated: number;
  skippedNoSchedule: number;
}
