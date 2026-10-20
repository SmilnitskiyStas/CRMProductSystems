import { useQuery } from "@tanstack/react-query";
import { bufferApi } from "../api/buffer";

/**
 * Calculated CDA buffer of a product in a single store. `storeId` undefined (all stores / not
 * resolved yet) disables the query — a buffer only exists per store.
 */
export function useProductBuffer(productId: string | undefined, storeId: string | undefined) {
  return useQuery({
    queryKey: ["product-buffer", storeId, productId],
    queryFn: () => bufferApi.getForProduct(storeId!, productId!),
    enabled: !!productId && !!storeId,
  });
}

export interface EffectiveStockLevels {
  safetyBuffer: number;
  minStock: number;
  maxStock: number;
  /** True when the numbers come from the calculated buffer rather than the catalog fields. */
  fromBuffer: boolean;
}

/**
 * Item.MinStock/MaxStock/SafetyBuffer stay user-owned. When the user left them unset (all 0) and a
 * buffer is calculated for the selected store, show the CDA zones instead:
 *   safety = red zone, min = red + yellow, max = total (red + yellow + green).
 */
export function resolveStockLevels(
  product: { minStock: number; maxStock: number; safetyBuffer: number },
  buffer: { bufferRed: number; bufferYellow: number; bufferTotal: number } | null | undefined,
): EffectiveStockLevels {
  const unset = product.minStock === 0 && product.maxStock === 0 && product.safetyBuffer === 0;
  if (buffer && unset) {
    return {
      safetyBuffer: buffer.bufferRed,
      minStock: Math.round((buffer.bufferRed + buffer.bufferYellow) * 100) / 100,
      maxStock: buffer.bufferTotal,
      fromBuffer: true,
    };
  }
  return {
    safetyBuffer: product.safetyBuffer,
    minStock: product.minStock,
    maxStock: product.maxStock,
    fromBuffer: false,
  };
}
