// Wire shapes mirror backend `Features/Loyalty/Dtos/LoyaltyDtos.cs` (TASK-405) — System.Text.Json
// default camelCase policy means PascalCase record properties serialize camelCase on the wire.

// ─── Consumer-facing (wallet) ────────────────────────────────────────────────

export interface LoyaltyMembershipSummary {
  membershipId: string;
  tenantId: string;
  tenantName: string;
  balance: number;
  tier?: string | null;
  status: 'active' | 'blocked' | string;
  joinedAt: string;
  preferredStoreId: string | null;
  preferredStoreName: string | null;
  preferredStoreAddress: string | null;
}

export interface LoyaltyNetworkStore {
  storeId: string;
  storeName: string;
  address: string | null;
}

export interface LoyaltyNetworkSummary {
  tenantId: string;
  tenantName: string;
  slug: string;
  stores: LoyaltyNetworkStore[];
}

export interface RetailerPublicInfo {
  name: string;
  slug: string;
  logoUrl: string | null;
  joinable: boolean;
}

/** The rotating QR/barcode payload. Never carries the TOTP secret itself. */
export interface LoyaltyCode {
  code: string;
  displayFormat: 'qr' | 'barcode';
  balance: number;
  expiresInSeconds: number;
}

export interface LoyaltyLedgerEntry {
  id: string;
  entryType: 'accrual' | 'redemption' | 'manual_adjustment' | 'expiry' | string;
  amount: number;
  balanceAfter: number;
  note: string | null;
  createdAt: string;
}

/** Matches the generic `PagedResult<T>` shape already used by mobile/features/customers. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ─── Staff-facing (POS / cabinet) ────────────────────────────────────────────

export interface ResolveLoyaltyCodeResult {
  membershipId: string;
  customerId: string | null;
  customerName: string | null;
  maskedPhone: string | null;
  balance: number;
}

export interface ManualLoyaltyAdjustRequest {
  membershipId: string;
  amount: number;
  note?: string | null;
}
