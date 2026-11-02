import type { ProductStockDto } from "../types";

export function canWriteOffBatch(batch: ProductStockDto): boolean {
  return batch.quantity > 0 && batch.status !== "archived";
}

/** A barcode identifies a product; only the stock row ID identifies its expiry batch. */
export function getWriteOffBatches(batches: ProductStockDto[], selectedIds: Set<string>): ProductStockDto[] {
  return batches.filter(batch => selectedIds.has(batch.id) && canWriteOffBatch(batch));
}

export function shareWriteOffStore(batches: ProductStockDto[]): boolean {
  return batches.length > 0 && batches.every(batch => batch.storeId === batches[0].storeId);
}
