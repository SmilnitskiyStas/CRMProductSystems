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
  inTransit: number;
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
