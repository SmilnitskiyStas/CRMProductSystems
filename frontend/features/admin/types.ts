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
  /**
   * Single `SupplierItemCategories` key (food / auto_parts / medical / construction).
   * Sent only when `businessType === "supplier"` — validated server-side for supplier
   * tenants (TASK-665/667), ignored otherwise.
   */
  supplierCategory?: string;
}

// ── Display helpers ──────────────────────────────────────────────────────────

// Labels live in i18n (Dashboard.admin.plans.*, `useTranslations`) — see
// TenantTable.tsx, TenantDetailDrawer.tsx, CreateTenantModal.tsx.
export const PLAN_COLORS: Record<TenantPlan, { bg: string; border: string; text: string }> = {
  trial:      { bg: "#451A03", border: "#92400E", text: "#FCD34D" },
  basic:      { bg: "#1D3461", border: "#3B82F6", text: "#93C5FD" },
  standard:   { bg: "#2D1B69", border: "#7C3AED", text: "#C4B5FD" },
  enterprise: { bg: "#052e16", border: "#166534", text: "#4ADE80" },
};

// Labels live in i18n (Dashboard.admin.modules.*, `useTranslations`) — see
// TenantDetailDrawer.tsx.
// TASK-413: added "loyalty"/"marketing_analytics" (backend Tenant.UpdateModules already
// accepted both since TASK-405/406) so the admin panel can actually enable them for a tenant.
export const ALL_MODULES = ["inventory", "procurement", "pos", "auto_service", "production", "marketplace", "loyalty", "marketing_analytics"] as const;
export const ALL_PLANS: TenantPlan[] = ["trial", "basic", "standard", "enterprise"];

// Mirrors backend Tenant.UpdateBusinessType valid values (ADR-014/016).
// Labels live in i18n (Dashboard.admin.businessTypes.*, `useTranslations`) — see
// CreateTenantModal.tsx.
export const ALL_BUSINESS_TYPES = [
  "retail",
  "auto_service",
  "warehouse",
  "restaurant",
  "production",
  "distribution",
  "pharmacy",
  "floristry",
  "supplier",
];
