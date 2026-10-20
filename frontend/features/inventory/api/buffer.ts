import { api, ApiError } from "@/lib/api";

/** CDA buffer for one product in one store (GET /api/buffer/{storeId}/{productId}). */
export interface ProductBufferDto {
  storeId: string;
  productId: string;
  bufferTotal: number;
  bufferGreen: number;
  bufferYellow: number;
  bufferRed: number;
  leadTimeDays: number;
  orderCycleDays: number;
  calculatedAt: string;
}

export const bufferApi = {
  /** null when no buffer has been calculated yet (404) — a normal state, not an error. */
  async getForProduct(storeId: string, productId: string): Promise<ProductBufferDto | null> {
    try {
      return await api.get<ProductBufferDto>(`/api/buffer/${storeId}/${productId}`);
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) return null;
      throw e;
    }
  },
};
