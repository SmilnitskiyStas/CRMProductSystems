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
  /**
   * Determines the default module set (ADR-015). "supplier" (ADR-016) creates a
   * marketplace supplier tenant: Supplier + SupplierProfile pair, first user
   * gets the supplier_admin role. Backend defaults to "retail" when omitted.
   */
  businessType?: string;
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
  inventory:    "Інвентаризація",
  procurement:  "Постачання",
  pos:          "Каса (POS)",
  auto_service: "Автосервіс",
  production:   "Виробництво",
  marketplace:  "Маркетплейс постачальників",
};

export const ALL_MODULES = ["inventory", "procurement", "pos", "auto_service", "production", "marketplace"] as const;
export const ALL_PLANS: TenantPlan[] = ["trial", "basic", "standard", "enterprise"];

// Mirrors backend Tenant.UpdateBusinessType valid values (ADR-014/016).
export const BUSINESS_TYPE_LABELS: Record<string, string> = {
  retail:       "Рітейл (магазин)",
  auto_service: "Автосервіс",
  warehouse:    "Склад",
  restaurant:   "Ресторан",
  production:   "Виробництво",
  distribution: "Дистрибуція",
  pharmacy:     "Аптека",
  floristry:    "Флористика",
  supplier:     "Постачальник (маркетплейс)",
};

export const ALL_BUSINESS_TYPES = Object.keys(BUSINESS_TYPE_LABELS);
