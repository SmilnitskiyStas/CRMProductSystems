import { useQuery } from "@tanstack/react-query";
import { movementsApi } from "../api/movements";

/** Open (ordered / shipped, not yet received) supply of a product — optionally for one store. */
export function useProductIncoming(productId: string | null, storeId?: string) {
  return useQuery({
    queryKey: ["product-incoming", productId, storeId],
    queryFn: () => movementsApi.getIncoming(productId!, storeId),
    enabled: !!productId,
  });
}
