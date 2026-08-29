import { useQuery } from "@tanstack/react-query";
import { catalogApi } from "../api/catalog";

export function useCatalogProducts(params?: {
  category_id?: string;
  management_type?: string;
  search?: string;
  ids?: string[];
}, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["catalog", params],
    queryFn: () => catalogApi.getAll(params),
    enabled: options?.enabled ?? true,
  });
}

export function useCatalogProduct(id: string | null) {
  return useQuery({
    queryKey: ["catalog", id],
    queryFn: () => catalogApi.getById(id!),
    enabled: !!id,
  });
}

/**
 * TASK-574 (ADR-032 Catalog Curation): resolves an exact set of product ids regardless of where
 * they fall in `/api/items`'s default alphabetical page window — used by `ProductPickerField`'s
 * selected-chip display and `AppPreviewPanel.tsx`'s curated-selection preview resolution (TASK-575).
 * Sorted key so re-ordering the same selection doesn't cause a spurious refetch.
 */
export function useCatalogProductsByIds(ids: string[] = []) {
  return useQuery({
    queryKey: ["catalog", "by-ids", [...ids].sort()],
    queryFn: () => catalogApi.getAll({ ids }),
    enabled: ids.length > 0,
  });
}
