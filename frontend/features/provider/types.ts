export type TenantPlan   = "basic" | "standard" | "enterprise" | "trial";
export type TenantModule = "shelf_manager" | "crm" | "notifications" | "auto_order" | "iot" | "cv_camera";

export interface TenantSummaryDto {
  id: string;
  name: string;
  slug: string;
  plan: TenantPlan;
  modules: TenantModule[];
  isActive: boolean;
  createdAt: string;
  userCount: number;
  storeCount: number;
  expiredBatchCount: number;
}

export interface TenantDetailDto extends TenantSummaryDto {
  lastActivityAt: string | null;
}

export interface ProviderHealthDto {
  totalTenants: number;
  activeTenants: number;
  totalUsers: number;
  totalExpiredBatches: number;
  timestamp: string;
}

export interface ProviderLogDto {
  id: string;
  action: string;
  entityType: string;
  entityId: string | null;
  meta: string | null;
  ipAddress: string | null;
  userId: string;
  tenantId: string | null;
  createdAt: string;
}

export interface ImpersonateResponse {
  accessToken: string;
  tenantName: string;
  tenantId: string;
}

// ── Display helpers ──────────────────────────────────────────────────────────

export const PLAN_LABELS: Record<TenantPlan, string> = {
  basic:      "Basic",
  standard:   "Standard",
  enterprise: "Enterprise",
  trial:      "Trial",
};

export const PLAN_COLORS: Record<TenantPlan, { bg: string; border: string; text: string }> = {
  basic:      { bg: "#1F2937", border: "#374151", text: "#9CA3AF" },
  standard:   { bg: "#1D3461", border: "#3B82F6", text: "#93C5FD" },
  enterprise: { bg: "#2D1B69", border: "#7C3AED", text: "#C4B5FD" },
  trial:      { bg: "#451A03", border: "#92400E", text: "#FCD34D" },
};

export const MODULE_LABELS: Record<TenantModule, string> = {
  shelf_manager: "Менеджер полиць",
  crm:           "CRM",
  notifications: "Сповіщення",
  auto_order:    "Авто-замовлення",
  iot:           "IoT-інтеграція",
  cv_camera:     "CV-камера",
};

export const ALL_MODULES: TenantModule[]   = ["shelf_manager", "crm", "notifications", "auto_order", "iot", "cv_camera"];
export const ALL_PLANS:   TenantPlan[]     = ["basic", "standard", "enterprise", "trial"];
