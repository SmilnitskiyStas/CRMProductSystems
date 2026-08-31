import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { marketplaceApi } from "../api/marketplace-api";
import type {
  MarketplaceFilters,
  MarketplaceSearchRequest,
  CreateReviewRequest,
  SupplierProfileUpdateRequest,
  AddSupplierItemRequest,
  SendSupplierChatMessageRequest,
} from "../types";

// ─── Query keys ───────────────────────────────────────────────────────────────

export const MARKETPLACE_KEYS = {
  suppliers: (filters: MarketplaceFilters, page: number) =>
    ["marketplace", "suppliers", filters, page] as const,
  supplier: (id: string) => ["marketplace", "supplier", id] as const,
  supplierItems: (id: string) => ["marketplace", "supplier-items", id] as const,
  supplierReviews: (id: string, page: number) =>
    ["marketplace", "supplier-reviews", id, page] as const,
  /** Prefix for invalidating all pages of a supplier's reviews. */
  supplierReviewsPrefix: (id: string) =>
    ["marketplace", "supplier-reviews", id] as const,
  myProfile: ["marketplace", "my-profile"] as const,
  supplierCoverage: (
    supplierId: string | null,
    buyerRegionCode: string | null
  ) =>
    ["marketplace", "supplier-coverage", supplierId, buyerRegionCode] as const,
  itemCategories: ["marketplace", "item-categories"] as const,
  supplierChatMessages: (supplierId: string) =>
    ["marketplace", "supplier-chat-messages", supplierId] as const,
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
        regionCode: filters.regionCode || undefined,
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

/**
 * GET /api/marketplace/suppliers/{id}/coverage — the supplier's delivery coverage
 * resolved against the buyer's region (TASK-657). Pass `buyerRegionCode` to override
 * the server-resolved region; a new value re-resolves the panel. Disabled until a
 * supplier id is known.
 */
export function useSupplierCoverageForBuyer(
  supplierId: string | null,
  buyerRegionCode?: string | null
) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.supplierCoverage(supplierId, buyerRegionCode ?? null),
    queryFn: () =>
      marketplaceApi.getSupplierCoverageForBuyer(
        supplierId!,
        buyerRegionCode ?? undefined
      ),
    enabled: !!supplierId,
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

export function useSupplierReviews(supplierId: string, page = 1, pageSize = 20) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.supplierReviews(supplierId, page),
    queryFn: () => marketplaceApi.getSupplierReviews(supplierId, page, pageSize),
    enabled: !!supplierId,
  });
}

/**
 * Review count only — fetches the lightest possible page (pageSize=1) and reads
 * `total`. Used on listing cards (TASK-287).
 */
export function useSupplierReviewCount(supplierId: string) {
  return useQuery({
    queryKey: ["marketplace", "supplier-review-count", supplierId] as const,
    queryFn: () => marketplaceApi.getSupplierReviews(supplierId, 1, 1),
    enabled: !!supplierId,
    staleTime: 60_000,
    select: (data) => data.total,
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
      // Reviews (all pages) + count badge
      queryClient.invalidateQueries({
        queryKey: MARKETPLACE_KEYS.supplierReviewsPrefix(supplierId),
      });
      queryClient.invalidateQueries({
        queryKey: ["marketplace", "supplier-review-count", supplierId],
      });
      // Profile + listing — rating is recalculated synchronously on the backend
      queryClient.invalidateQueries({
        queryKey: MARKETPLACE_KEYS.supplier(supplierId),
      });
      queryClient.invalidateQueries({ queryKey: ["marketplace", "suppliers"] });
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

/** GET /api/marketplace/item-categories — static registry, cached indefinitely. */
export function useItemCategories() {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.itemCategories,
    queryFn: () => marketplaceApi.getItemCategories(),
    staleTime: Infinity,
  });
}

// ─── Supplier ↔ client chat, client side (TASK-314) ────────────────────────────

export function useSupplierChatMessages(supplierId: string | null) {
  return useQuery({
    queryKey: MARKETPLACE_KEYS.supplierChatMessages(supplierId ?? ""),
    queryFn: () => marketplaceApi.getSupplierChatMessages(supplierId!),
    enabled: Boolean(supplierId),
    refetchInterval: 3000,
  });
}

export function useSendSupplierChatMessage(supplierId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SendSupplierChatMessageRequest) =>
      marketplaceApi.sendSupplierChatMessage(supplierId!, body),
    onSuccess: () => {
      if (supplierId) {
        queryClient.invalidateQueries({ queryKey: MARKETPLACE_KEYS.supplierChatMessages(supplierId) });
      }
    },
  });
}
