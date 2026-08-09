// Consumer App feature (TASK-500) — a tenant-admin-only settings area for the consumer-facing
// mobile app. Starts with just the bonus/loyalty program settings; the page this feature backs
// (`app/(dashboard)/consumer-app/page.tsx`) is deliberately structured to grow additional cards
// (news, promos, etc.) later without a route change — see that page's own comment. Only the
// loyalty section is implemented today; no other sections are scaffolded.

/** Exactly "qr" or "barcode" — how a tenant's consumers render their universal bonus-card code. */
export type CustomerCodeFormat = "qr" | "barcode";

/** GET /api/settings/loyalty response (LoyaltySettingsController, enterprise_admin+ only). */
export interface LoyaltyProgramSettings {
  isEnabled: boolean;
  accrualRatePercent: number;
  redemptionCapPercent: number;
  minRedemptionBalance: number;
  codeTtlSeconds: number;
  /** TASK-499/500: "qr" or "barcode". Defaults to "barcode" server-side when never saved. */
  customerCodeFormat: CustomerCodeFormat;
  updatedAt: string | null;
}

/**
 * PUT /api/settings/loyalty request body. Full replace — no partial-update semantics, every
 * field must always be sent (mirrors UpsertLoyaltyProgramSettingsRequest on the backend).
 */
export interface UpdateLoyaltyProgramSettingsRequest {
  isEnabled: boolean;
  accrualRatePercent: number;
  redemptionCapPercent: number;
  minRedemptionBalance: number;
  codeTtlSeconds: number;
  customerCodeFormat: CustomerCodeFormat;
}
