import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type {
  ProductStockDto,
  SuggestionDto,
  CreateStockRequest,
  FefoConsumeRequest,
  FefoConsumeResult,
} from "../types";

export const stockApi = {
  getAll: (params?: {
    store_id?: string;
    status?: string;
    zone_id?: string;
    product_id?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const qs = new URLSearchParams();
    // Wire param renamed store_id -> storeIds (backend now takes Guid[]? storeIds on GET
    // /api/stock); this call site still only ever sends zero-or-one id, unchanged behavior.
    if (params?.store_id) qs.set("storeIds", params.store_id);
    if (params?.status) qs.set("status", params.status);
    if (params?.zone_id) qs.set("zone_id", params.zone_id);
    if (params?.product_id) qs.set("product_id", params.product_id);
    if (params?.page) qs.set("page", String(params.page));
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    const query = qs.toString();
    return api.get<PagedResult<ProductStockDto>>(`/api/stock${query ? `?${query}` : ""}`);
  },

  getById: (id: string) => api.get<ProductStockDto>(`/api/stock/${id}`),

  getExpiring: (days = 7, store_id?: string) => {
    const qs = new URLSearchParams({ days: String(days) });
    if (store_id) qs.set("store_id", store_id);
    return api.get<ProductStockDto[]>(`/api/stock/expiring?${qs}`);
  },

  getExpired: (store_id?: string) => {
    const qs = store_id ? `?store_id=${store_id}` : "";
    return api.get<ProductStockDto[]>(`/api/stock/expired${qs}`);
  },

  getNeedsCheck: (store_id?: string) => {
    const qs = store_id ? `?store_id=${store_id}` : "";
    return api.get<ProductStockDto[]>(`/api/stock/needs-check${qs}`);
  },

  getSuggestions: (store_id?: string) => {
    const qs = store_id ? `?store_id=${store_id}` : "";
    return api.get<SuggestionDto[]>(`/api/stock/suggestions${qs}`);
  },

  create: (data: CreateStockRequest) => api.post<ProductStockDto>("/api/stock", data),

  verify: (id: string) => api.post<ProductStockDto>(`/api/stock/${id}/verify`),

  fefoConsume: (data: FefoConsumeRequest) =>
    api.post<FefoConsumeResult>("/api/stock/fefo-consume", data),
};
