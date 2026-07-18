export type ModuleKey =
  | "inventory"
  | "procurement"
  | "pos"
  | "auto_service"
  | "production"
  | "marketplace";

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
