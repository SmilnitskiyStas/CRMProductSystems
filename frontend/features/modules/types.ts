export type ModuleKey =
  | "inventory"
  | "procurement"
  | "pos"
  | "auto_service"
  | "production"
  | "marketplace"
  // TASK-409 (marketing-analytics/RFM plan Фаза 1): gates the RFM dashboard
  // (features/marketing-analytics/). Backend already accepts this key (Tenant.UpdateModules,
  // TASK-405).
  | "marketing_analytics"
  // TASK-674: provider-controllable modules wired end-to-end.
  //   loyalty     — POS QR bonus accrual/redemption (LoyaltyController)
  //   mobile_app  — the whole "Застосунок" section (bonus program, tiers, banners, promos,
  //                 catalog, App Builder) — Sidebar `consumer_app` group + its controllers
  //   analytics   — the "Аналітика" reports/dashboards section (AnalyticsController, per-action)
  | "loyalty"
  | "mobile_app"
  | "analytics"
  // Supplier-portal expansion: "supplier_inventory" gates the supplier's own warehouses +
  // batch stock + batch-consuming shipment; "supplier_workforce" gates the supplier's
  // employee work schedules. Both provider-granted, default-off (not in any business-type
  // preset).
  | "supplier_inventory"
  | "supplier_workforce";

/** GET /api/settings/modules response */
export interface ModulesSettings {
  businessType: string;
  modules: ModuleKey[];
}

// Labels/descriptions live in i18n (Dashboard.modules.catalog.*, `useTranslations`) — see
// ModulesTab.tsx. Kept here only as the canonical module key list/order (ADR-015).
export const ALL_MODULE_KEYS: ModuleKey[] = [
  "inventory",
  "procurement",
  "pos",
  "auto_service",
  "production",
  "marketplace",
  "marketing_analytics",
  "loyalty",
  "mobile_app",
  "analytics",
  "supplier_inventory",
  "supplier_workforce",
];

// Labels live in i18n (Dashboard.modules.businessTypes.*, `useTranslations`) — see
// ModulesTab.tsx.
export const BUSINESS_TYPE_KEYS: string[] = [
  "retail",
  "auto_service",
  "warehouse",
  "restaurant",
  "production",
  "distribution",
];
