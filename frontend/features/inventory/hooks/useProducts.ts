import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { productsApi } from "../api/products";
import type { CreateProductPayload, ProductSortBy, UpdateProductPayload } from "../types";

const PRODUCTS_KEY = ["products"] as const;

interface ProductsListParams {
  search?: string;
  category_id?: string;
  uncategorized?: boolean;
  min_price?: number;
  max_price?: number;
  page?: number;
  pageSize?: number;
  sortBy?: ProductSortBy | string;
  sortDescending?: boolean;
}

// Shared query definition for the paginated product list — used by both `useProducts` (flat
// array, for existing simple callers) and `useProductsPaged` (full envelope, for the Inventory
// page's pagination footer). Same queryKey/queryFn means React Query dedupes the network
// request when both are used against the same params.
function productsListQuery(params?: ProductsListParams) {
  const page = params?.page ?? 1;
  const pageSize = params?.pageSize ?? 50;
  return {
    queryKey: [...PRODUCTS_KEY, { ...params, page, pageSize }] as const,
    queryFn: () => productsApi.getAll({ ...params, page, pageSize }),
  };
}

// Flat `Product[]` list — unchanged external shape for existing callers (e.g. sales/page.tsx)
// that just want a simple product list, not pagination metadata.
export function useProducts(params?: ProductsListParams) {
  return useQuery({
    ...productsListQuery(params),
    placeholderData: (prev) => prev,
    select: (r) => r.items,
  });
}

// Full `PagedResult<Product>` — for pages (Inventory) that need `totalCount`/`page`/`pageSize`
// to drive a pagination footer.
export function useProductsPaged(params?: ProductsListParams) {
  return useQuery({
    ...productsListQuery(params),
    placeholderData: (prev) => prev,
  });
}

export function useProduct(id: string) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, id],
    queryFn: () => productsApi.getById(id),
    enabled: Boolean(id),
  });
}

export function useProductsByIds(ids: string[]) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, "by-ids", [...ids].sort()],
    queryFn: () => productsApi.getAll({ ids }),
    enabled: ids.length > 0,
    select: (r) => r.items,
  });
}

export function useProductSearch(search: string, enabled = true) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, "search", search],
    queryFn: () => productsApi.getAll({ search, pageSize: 20 }),
    enabled: enabled && search.trim().length > 0,
    select: (r) => r.items,
  });
}

export function useCreateProduct() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateProductPayload) => productsApi.create(payload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRODUCTS_KEY }),
  });
}

export function useUpdateProduct() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateProductPayload }) =>
      productsApi.update(id, payload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRODUCTS_KEY }),
  });
}

// Mirrors useUploadBannerImage (features/consumer-app/hooks/useBanners.ts) — ProductForm has
// always supported picking + previewing an image via its `onImageUpload` prop, but no page ever
// wired a mutation to it (TASK-718 is the first caller, from the new edit page).
export function useUploadProductImage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => productsApi.uploadImage(id, file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRODUCTS_KEY }),
  });
}

export function useDeleteProduct() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => productsApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PRODUCTS_KEY }),
  });
}

// Product↔zone tagging (TASK-715, backend from TASK-714).
function itemZonesKey(productId: string) {
  return [...PRODUCTS_KEY, productId, "zones"] as const;
}

export function useItemZones(productId: string) {
  return useQuery({
    queryKey: itemZonesKey(productId),
    queryFn: () => productsApi.getZones(productId),
    enabled: Boolean(productId),
  });
}

export function useAssignItemZone(productId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (zoneId: string) => productsApi.assignZone(productId, zoneId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: itemZonesKey(productId) }),
  });
}

export function useUnassignItemZone(productId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (zoneId: string) => productsApi.unassignZone(productId, zoneId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: itemZonesKey(productId) }),
  });
}
