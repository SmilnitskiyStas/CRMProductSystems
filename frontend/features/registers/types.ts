export type RegisterStatus = "pending" | "active" | "locked" | "revoked";
export type RegisterType = "line" | "self_checkout";

export interface Register {
  id: string;
  storeId: string;
  storeName: string | null;
  number: number;
  type: RegisterType;
  name: string;
  status: RegisterStatus;
  lastSeenAt: string | null;
  appVersion: string | null;
  activatedAt: string | null;
  hasPendingActivationCode: boolean;
  activationCodeExpiresAt: string | null;
  createdAt: string;
}

export interface CreateRegisterPayload {
  storeId: string;
  name: string;
  number?: number | null;
  type?: RegisterType;
}

export interface UpdateRegisterPayload {
  name?: string;
  storeId?: string;
}

export interface ActivationCode {
  activationCode: string;
  expiresAt: string;
}

export interface RegisterCreated {
  register: Register;
  activationCode: string;
  activationCodeExpiresAt: string;
}

export interface RegisterOperator {
  registerId: string;
  cashierId: string;
  cashierName: string;
  startedAt: string;
}

export interface PosEmployee {
  id: string;
  fullName: string;
  role: string;
  hasPin: boolean;
  pinSetAt: string | null;
  pinLockedOut: boolean;
}

export interface SetPinResult {
  pin: string;
}

export interface SalesTotals {
  turnover: number;
  receipts: number;
  averageReceipt: number;
  cash: number;
  card: number;
  /** Receipts paid partly cash / partly card (not split further). */
  mixed: number;
}

export interface OperatorStats {
  cashierId: string | null;
  cashierName: string | null;
  totals: SalesTotals;
}

export interface RegisterStats {
  registerId: string;
  number: number;
  name: string;
  totals: SalesTotals;
  operators: OperatorStats[];
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface RegisterSession {
  id: string;
  cashierId: string;
  cashierName: string;
  startedAt: string;
  endedAt: string | null;
  endReason: "logout" | "idle" | "revoked" | "replaced" | null;
}

export interface RegisterShift {
  id: string;
  shiftNumber: number;
  cashierId: string | null;
  cashierName: string | null;
  openedAt: string;
  closedAt: string | null;
  openingCash: number | null;
  closingCash: number | null;
  totalSales: number;
  receipts: number;
}

export type StatsPeriod = "today" | "7d" | "30d";
