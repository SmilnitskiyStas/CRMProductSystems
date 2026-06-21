import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { marketplaceApi } from "../api/marketplace-api";
import type {
  MarketplaceFilters,
  MarketplaceSearchRequest,
  CreateReviewRequest,
  SupplierProfileUpdateRequest,
  CreateSupplierRequest,
  AddSupplierItemRequest,
} from "../types";

// ─── Query keys ───────────────────────────────────────────────────────────────

export const MARKETPLACE_KEYS = {
  suppliers: (filters: MarketplaceFilters, page: number) =>
    ["marketplace", "suppliers", filters, page] as const,
  supplier: (id: string) => ["marketplace", "supplier", id] as const,
  supplierItems: (id: string) => ["marketplace", "supplier-items", id] as const,
  supplierReviews: (id: string) => ["marketplace", "supplier-reviews", id] as const,
  myProfile: ["marketplace", "my-profile"] as const,
};

// ─── Hooks ────────────────────────────────────────────────────────────────────

export function useSuppliers(
  filters: MarketplaceFilters,
  page: number = 1,
  pageSize: number = 20
) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.suppliers(filters, page),
    queryFn: () =>
      marketplaceApi.getSuppliers({
        page,
        pageSize,
        region: filters.region || undefined,
        category: filters.category || undefined,
        plan: filters.plan,
      }),
    staleTime: 30_000,
  });
}

export function useSupplier(id: string) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.supplier(id),
    queryFn: () => marketplaceApi.getSupplier(id),
    enabled: !!id,
    staleTime: 30_000,
  });
}

export function useSupplierItems(supplierId: string) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.supplierItems(supplierId),
    queryFn: () => marketplaceApi.getSupplierItems(supplierId),
    enabled: !!supplierId,
    staleTime: 60_000,
  });
}

export function useSupplierReviews(supplierId: string) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.supplierReviews(supplierId),
    queryFn: () => marketplaceApi.getSupplierReviews(supplierId),
    enabled: !!supplierId,
  });
}

export function useMarketplaceSearch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: MarketplaceSearchRequest) => marketplaceApi.search(body),
    onSuccess: () => {
      // invalidation not needed — search result is mutation-driven
    },
  });
}

export function useCreateReview(supplierId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateReviewRequest) =>
      marketplaceApi.createReview(supplierId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: MARKETPLACE_KEYS.supplierReviews(supplierId),
      });
      queryClient.invalidateQueries({
        queryKey: MARKETPLACE_KEYS.supplier(supplierId),
      });
    },
  });
}

export function useMySupplierProfile() {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.myProfile,
    queryFn: marketplaceApi.getMyProfile,
    retry: false,
  });
}

export function useUpdateMySupplierProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SupplierProfileUpdateRequest) =>
      marketplaceApi.updateMyProfile(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: MARKETPLACE_KEYS.myProfile });
    },
  });
}

// ─── Admin / platform hooks (TASK-275) ───────────────────────────────────────

export function useCreateSupplier() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSupplierRequest) =>
      marketplaceApi.adminCreateSupplier(body),
    onSuccess: () => {
      // Invalidate all supplier list queries so the new supplier appears
      queryClient.invalidateQueries({ queryKey: ["marketplace", "suppliers"] });
    },
  });
}

export function useAddSupplierItem(supplierId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AddSupplierItemRequest) =>
      marketplaceApi.adminAddSupplierItem(supplierId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: MARKETPLACE_KEYS.supplierItems(supplierId),
      });
    },
  });
}

export function useDeleteSupplierItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ supplierId, itemId }: { supplierId: string; itemId: string }) =>
      marketplaceApi.adminDeleteSupplierItem(supplierId, itemId),
    onSuccess: (_data, { supplierId }) => {
      queryClient.invalidateQueries({
        queryKey: MARKETPLACE_KEYS.supplierItems(supplierId),
      });
    },
  });
}
