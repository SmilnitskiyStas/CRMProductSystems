import { useQuery } from "@tanstack/react-query";
import { movementsApi } from "../api/movements";

export function useProductMovements(
  productId: string | null,
  params?: { from?: string; to?: string; page_size?: number; store_id?: string },
) {
  return useQuery({
    queryKey: ["product-movements", productId, params],
    queryFn: () => movementsApi.getByProduct(productId!, params),
    enabled: !!productId,
  });
}
