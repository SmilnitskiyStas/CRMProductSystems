export type ModuleKey =
  | "inventory"
  | "procurement"
  | "pos"
  | "auto_service"
  | "production"
  | "marketplace"
  // TASK-409 (marketing-analytics/RFM plan Фаза 1): gates the RFM dashboard
  // (features/marketing-analytics/). Backend already accepts this key (Tenant.UpdateModules,
  // TASK-405) — registered here so it type-checks as a Sidebar NavGroup.moduleKey and shows up
  // in the tenant-facing Settings "Modules" tab. NOT added to admin/types.ts or
  // provider/types.ts's own separate ALL_MODULES lists (the provider-panel tenant module
  // activation checkboxes) — that's a pre-existing gap shared with "loyalty" (TASK-405),
  // flagged as a follow-up rather than fixed here (out of scope for this frontend task).
  | "marketing_analytics";

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
