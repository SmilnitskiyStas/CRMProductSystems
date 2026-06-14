export type TenantPlan = "basic" | "standard" | "enterprise" | "trial";

export interface TenantUsage {
  usersCount: number;
  storesCount: number;
  productsCount: number;
  salesLast30Days: number;
}

export interface TenantDto {
  id: string;
  name: string;
  slug: string;
  plan: TenantPlan;
  modules: string[];
  isActive: boolean;
  createdAt: string;
  usage: TenantUsage;
}

export interface CreateTenantRequest {
  name: string;
  slug: string;
  plan: string;
  adminEmail: string;
  adminFullName: string;
  adminPassword: string;
}

// ── Display helpers ──────────────────────────────────────────────────────────

export const PLAN_LABELS: Record<TenantPlan, string> = {
  trial:      "Trial",
  basic:      "Basic",
  standard:   "Standard",
  enterprise: "Enterprise",
};

export const PLAN_COLORS: Record<TenantPlan, { bg: string; border: string; text: string }> = {
  trial:      { bg: "#451A03", border: "#92400E", text: "#FCD34D" },
  basic:      { bg: "#1D3461", border: "#3B82F6", text: "#93C5FD" },
  standard:   { bg: "#2D1B69", border: "#7C3AED", text: "#C4B5FD" },
  enterprise: { bg: "#052e16", border: "#166534", text: "#4ADE80" },
};

export const MODULE_LABELS: Record<string, string> = {
  shelf_manager: "Менеджер полиць",
  crm:           "CRM",
  notifications: "Сповіщення",
  auto_order:    "Авто-замовлення",
  iot:           "IoT-інтеграція",
  cv_camera:     "CV-камера",
};

export const ALL_MODULES = ["shelf_manager", "crm", "notifications", "auto_order", "iot", "cv_camera"] as const;
export const ALL_PLANS: TenantPlan[] = ["trial", "basic", "standard", "enterprise"];
