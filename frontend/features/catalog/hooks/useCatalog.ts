import { useQuery } from "@tanstack/react-query";
import { catalogApi } from "../api/catalog";

export function useCatalogProducts(params?: { category_id?: string; management_type?: string }) {
  return useQuery({
    queryKey: ["catalog", params],
    queryFn: () => catalogApi.getAll(params),
  });
}

export function useCatalogProduct(id: string | null) {
  return useQuery({
    queryKey: ["catalog", id],
    queryFn: () => catalogApi.getById(id!),
    enabled: !!id,
  });
}
