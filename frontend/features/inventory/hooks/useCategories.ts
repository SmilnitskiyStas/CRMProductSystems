import { useQuery } from "@tanstack/react-query";
import { categoriesApi } from "../api/categories";

// Categories change rarely — 5 minute staleTime, matching useDashboard.ts's useStoreZones
// precedent for other slow-moving reference data.
export function useCategories() {
  return useQuery({
    queryKey: ["categories"],
    queryFn: () => categoriesApi.getAll(),
    staleTime: 5 * 60_000,
  });
}
