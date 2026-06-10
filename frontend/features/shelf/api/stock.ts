import { api } from "@/lib/api";
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
  }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    if (params?.zone_id) qs.set("zone_id", params.zone_id);
    if (params?.product_id) qs.set("product_id", params.product_id);
    const query = qs.toString();
    return api.get<ProductStockDto[]>(`/api/stock${query ? `?${query}` : ""}`);
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
