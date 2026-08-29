import { api } from "@/lib/api";

export type MobileCatalogLayout = "grid" | "list" | "featured";
export interface MobileCatalogItemSetting { productId: string; productName: string; imageUrl: string | null; sortOrder: number; isFeatured: boolean; mobileDiscountPercent: number | null }
export type MobileCatalogStatus = "draft" | "scheduled" | "published" | "archived";
export interface MobileCatalogSettings { id: string; title: string; description: string; bannerUrl: string | null; layoutMode: MobileCatalogLayout; isEnabled: boolean; status: MobileCatalogStatus; publishAt: string; unpublishAt: string | null; createdAt: string; updatedAt: string; publishedAt: string | null; archivedAt: string | null; locationIds: string[]; items: MobileCatalogItemSetting[] }
export type SaveMobileCatalogSettings = Pick<MobileCatalogSettings, "title" | "description" | "layoutMode" | "publishAt" | "unpublishAt" | "locationIds"> & { items: Array<Pick<MobileCatalogItemSetting, "productId" | "isFeatured" | "mobileDiscountPercent">> };
export interface CatalogProductAnalytics { productId: string; productName: string; views: number; scans: number; purchases: number; revenue: number; viewToPurchasePercent: number }
export interface CatalogDailyAnalytics { date: string; catalogViews: number; productViews: number; scans: number; purchases: number; revenue: number }
export interface CatalogStoreAnalytics { storeId: string; storeName: string; catalogViews: number; scans: number; purchases: number; revenue: number }
export interface CatalogAudienceAnalytics { key: string; label: string; tierId: string | null; reach: number; interactions: number; purchases: number; revenue: number }
export interface AttributionPolicy { modelVersion: string; confidence: string; name: string; rules: string[]; limitation: string }
export interface CatalogAnalytics { catalogId: string; catalogViews: number; uniqueUsers: number; productViews: number; productScans: number; purchases: number; revenue: number; conversionPercent: number; products: CatalogProductAnalytics[]; daily: CatalogDailyAnalytics[]; stores: CatalogStoreAnalytics[]; audience: CatalogAudienceAnalytics[]; attributionPolicy: AttributionPolicy }

export const mobileCatalogSettingsApi = {
  list: () => api.get<MobileCatalogSettings[]>("/api/mobile-catalog-settings"),
  get: (id: string) => api.get<MobileCatalogSettings>(`/api/mobile-catalog-settings/${id}`),
  create: (body: SaveMobileCatalogSettings) => api.post<MobileCatalogSettings>("/api/mobile-catalog-settings", body),
  update: (id: string, body: SaveMobileCatalogSettings) => api.put<MobileCatalogSettings>(`/api/mobile-catalog-settings/${id}`, body),
  publish: (id: string) => api.post<MobileCatalogSettings>(`/api/mobile-catalog-settings/${id}/publish`),
  archive: (id: string) => api.post<MobileCatalogSettings>(`/api/mobile-catalog-settings/${id}/archive`),
  duplicate: (id: string) => api.post<MobileCatalogSettings>(`/api/mobile-catalog-settings/${id}/duplicate`),
  analytics: (id: string, params?: { from?: string; to?: string; storeIds?: string[] }) => {
    const qs = new URLSearchParams();
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    params?.storeIds?.forEach((storeId) => qs.append("storeIds", storeId));
    return api.get<CatalogAnalytics>(`/api/mobile-catalog-settings/${id}/analytics${qs.size ? `?${qs}` : ""}`);
  },
  uploadBanner: (id: string, file: File) => { const form = new FormData(); form.append("file", file); return api.postForm<{ bannerUrl: string }>(`/api/mobile-catalog-settings/${id}/banner`, form); },
};
