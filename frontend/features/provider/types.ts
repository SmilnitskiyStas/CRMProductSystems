export type TenantPlan   = "basic" | "standard" | "enterprise" | "trial";
export type TenantModule =
  | "inventory" | "procurement" | "pos"
  | "auto_service" | "production" | "marketplace"
  | "marketplace_supplier";

export type BusinessType =
  | "retail" | "auto_service" | "restaurant"
  | "warehouse" | "production" | "distribution"
  | "pharmacy" | "floristry" | "supplier";

export interface TenantSummaryDto {
  id: string;
  name: string;
  slug: string;
  plan: TenantPlan;
  businessType: BusinessType;
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

export interface CreateTenantRequest {
  name: string;
  slug: string;
  businessType: BusinessType;
  plan: TenantPlan;
  modules?: TenantModule[];
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
  isImpersonated: boolean;
  createdAt: string;
}

export interface ProviderLogsPageDto {
  items: ProviderLogDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ProviderLogsFilter {
  tenantId?: string;
  action?: string;
  dateFrom?: string;
  dateTo?: string;
  page: number;
  pageSize: number;
}

export interface ImpersonateResponse {
  accessToken: string;
  tenantName: string;
  tenantId: string;
}

export interface TenantUserDto {
  id: string;
  fullName: string;
  email: string;
  role: string;
  createdAt: string;
}

export interface CreateTenantUserRequest {
  fullName: string;
  email: string;
  password: string;
  role: string;
}

// ── Business types ───────────────────────────────────────────────────────────

export const BUSINESS_TYPE_LABELS: Record<BusinessType, string> = {
  retail:       "Роздрібна торгівля",
  auto_service: "Автосервіс",
  restaurant:   "Ресторан / HoReCa",
  warehouse:    "Склад / Логістика",
  production:   "Виробництво",
  distribution: "Дистрибуція",
  pharmacy:     "Аптека / Медтовари",
  floristry:    "Флористика",
  supplier:     "Постачальник",
};

export const BUSINESS_TYPE_ICONS: Record<BusinessType, string> = {
  retail:       "🛒",
  auto_service: "🔧",
  restaurant:   "🍽️",
  warehouse:    "📦",
  production:   "🏭",
  distribution: "🚚",
  pharmacy:     "💊",
  floristry:    "🌸",
  supplier:     "🚚",
};

export const ALL_BUSINESS_TYPES: BusinessType[] = [
  "retail", "auto_service", "restaurant", "warehouse",
  "production", "distribution", "pharmacy", "floristry", "supplier",
];

// Default module presets per business type (mirrors backend Tenant.DefaultModulesForBusinessType)
export const BUSINESS_TYPE_PRESETS: Record<BusinessType, TenantModule[]> = {
  retail:       ["inventory", "procurement", "pos"],
  auto_service: ["auto_service", "procurement"],
  restaurant:   ["inventory", "pos", "production"],
  warehouse:    ["inventory", "procurement"],
  production:   ["inventory", "procurement", "production"],
  distribution: ["inventory", "procurement", "marketplace"],
  pharmacy:     ["inventory", "procurement", "pos"],
  floristry:    ["inventory", "procurement", "pos"],
  supplier:     ["marketplace_supplier"],
};

// ── Modules ──────────────────────────────────────────────────────────────────

export const MODULE_LABELS: Record<TenantModule, string> = {
  inventory:    "Інвентаризація",
  procurement:  "Постачання",
  pos:          "Каса (POS)",
  auto_service: "Автосервіс",
  production:   "Виробництво",
  marketplace:  "Маркетплейс постачальників",
  marketplace_supplier: "Кабінет постачальника",
};

export const MODULE_DESCRIPTIONS: Record<TenantModule, string> = {
  inventory:    "Каталог товарів, залишки, прийомка, переміщення, списання",
  procurement:  "Постачальники, замовлення постачання, AI-прогнозування закупівель",
  pos:          "Робота з касовим апаратом, продажі, фіскалізація чеків",
  auto_service: "Клієнти, автомобілі, наряд-замовлення, облік запчастин",
  production:   "Рецепти, виробничі замовлення, списання сировини",
  marketplace:  "Пошук і порівняння постачальників, відгуки, рейтинги",
  marketplace_supplier: "Self-service постачальника: каталог, замовлення, чат з клієнтами",
};

export const ALL_MODULES: TenantModule[] = [
  "inventory", "procurement", "pos", "auto_service", "production", "marketplace",
  "marketplace_supplier",
];

// ── Plans ────────────────────────────────────────────────────────────────────

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

export const ALL_PLANS: TenantPlan[] = ["trial", "basic", "standard", "enterprise"];
