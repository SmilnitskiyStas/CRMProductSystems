import { useQuery } from "@tanstack/react-query";
import { ApiError } from "@/lib/api";
import { aduApi } from "../api/adu";

/**
 * Single-product ADU lookup (TASK-494: days-of-stock-remaining on ProductTrendPanel). No prior
 * frontend consumption of GET /api/adu/{storeId}/{productId} existed anywhere in the app (only
 * the bulk POST /api/adu/recalculate action, in features/orders) -- this is a new, minimal
 * hook/api pair, following the same {queryKey, queryFn, enabled} shape as every other hook in
 * this feature (see useAnalytics.ts / usePosAnalytics.ts).
 *
 * A 404 means no ProductAdu row has been calculated yet for this (store, product) pair -- an
 * expected, common state (a brand-new product, or a store that's never run the nightly ADU
 * recalculation), not a transient failure worth retrying -- same "don't retry an expected 404"
 * precedent as features/pos/hooks/usePos.ts's useCurrentShift. Any other settled error is
 * treated the same way by this hook's caller (ProductTrendPanel): both collapse to "no usage
 * signal yet" for what is an optional, non-critical summary card, never surfaced as a scary
 * error state.
 */
export function useAdu(storeId: string | undefined, productId: string, enabled = true) {
  return useQuery({
    queryKey: ["adu", storeId, productId],
    queryFn: () => aduApi.get(storeId!, productId),
    enabled: enabled && !!storeId && !!productId,
    retry: (failureCount, error) => {
      if (error instanceof ApiError && error.status === 404) return false;
      return failureCount < 1;
    },
  });
}
