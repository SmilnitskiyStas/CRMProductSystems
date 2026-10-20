import { api } from "@/lib/api";
import type { OrderTrackingFilters, OrderTrackingPage, OrderTrackingSummary } from "../types";

export const orderTrackingApi = {
  getPage(f: OrderTrackingFilters) {
    const qs = new URLSearchParams();
    if (f.source) qs.set("source", f.source);
    f.statuses.forEach((s) => qs.append("status", s));
    if (f.storeId) qs.set("store_id", f.storeId);
    if (f.search) qs.set("search", f.search);
    if (f.overdue) qs.set("overdue", "true");
    qs.set("page", String(f.page));
    qs.set("pageSize", String(f.pageSize));
    return api.get<OrderTrackingPage>(`/api/orders/tracking?${qs.toString()}`);
  },

  /** Moves the expected delivery of an open internal order; it stays "in transit" for the order engine. */
  postpone(receiptId: string, newExpectedAt: string, reason?: string) {
    return api.put<unknown>(`/api/receipts/${receiptId}/postpone`, { newExpectedAt, reason: reason || null });
  },

  getSummary(storeId?: string) {
    const qs = storeId ? `?store_id=${storeId}` : "";
    return api.get<OrderTrackingSummary>(`/api/orders/tracking/summary${qs}`);
  },
};
