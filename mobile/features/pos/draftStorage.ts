import AsyncStorage from '@react-native-async-storage/async-storage';
import type { PaymentType, SaleItem } from './types';

export const POS_DRAFT_STORAGE_KEY = 'pos_draft_v1';
export const POS_DRAFT_VERSION = 1;

export type SaleSubmissionStatus =
  | 'idle'
  | 'pending'
  | 'failed'
  | 'uncertain'
  | 'conflict'
  | 'completed';

export interface PosDraftOwner {
  tenantId: string;
  userId: string;
}

export interface PosDraftCustomer {
  customerId?: string;
  customerName?: string;
  maskedPhone?: string;
  membershipId?: string;
  redeemAmount?: number;
}

export interface PosDraftSnapshot {
  version: typeof POS_DRAFT_VERSION;
  owner: PosDraftOwner;
  shiftId: string;
  cart: (Required<Pick<SaleItem, 'barcode' | 'quantity'>> & {
    productName: string;
    unitPrice: number;
    isCritical?: boolean;
  })[];
  customer: PosDraftCustomer | null;
  paymentType: PaymentType;
  cashReceived: string;
  printReceipt: boolean;
  submission: {
    status: SaleSubmissionStatus;
    message?: string;
    transactionId?: string;
  };
  updatedAt: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isFiniteNonNegative(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}

export function isValidPosDraft(
  value: unknown,
  owner: PosDraftOwner
): value is PosDraftSnapshot {
  if (!isRecord(value) || value.version !== POS_DRAFT_VERSION) return false;
  if (!isRecord(value.owner)) return false;
  if (value.owner.tenantId !== owner.tenantId || value.owner.userId !== owner.userId) return false;
  if (typeof value.shiftId !== 'string' || !value.shiftId) return false;
  if (!Array.isArray(value.cart)) return false;
  if (
    !value.cart.every(
      (item) =>
        isRecord(item) &&
        typeof item.barcode === 'string' &&
        item.barcode.length > 0 &&
        Number.isInteger(item.quantity) &&
        (item.quantity as number) > 0 &&
        typeof item.productName === 'string' &&
        isFiniteNonNegative(item.unitPrice)
    )
  ) {
    return false;
  }
  if (value.customer !== null && !isRecord(value.customer)) return false;
  if (value.paymentType !== 'Cash' && value.paymentType !== 'Card') return false;
  if (typeof value.cashReceived !== 'string') return false;
  if (value.printReceipt !== undefined && typeof value.printReceipt !== 'boolean') return false;
  if (!isRecord(value.submission)) return false;
  if (
    !['idle', 'pending', 'failed', 'uncertain', 'conflict', 'completed'].includes(
      String(value.submission.status)
    )
  ) {
    return false;
  }
  return typeof value.updatedAt === 'string';
}

/**
 * Whitelists operational fields. Raw QR/code/TOTP/recovery/auth values can never
 * reach AsyncStorage even if a caller accidentally adds them to an in-memory object.
 */
export function sanitizePosDraft(snapshot: PosDraftSnapshot): PosDraftSnapshot {
  const customer = snapshot.customer
    ? {
        ...(snapshot.customer.customerId ? { customerId: snapshot.customer.customerId } : {}),
        ...(snapshot.customer.customerName ? { customerName: snapshot.customer.customerName } : {}),
        ...(snapshot.customer.maskedPhone ? { maskedPhone: snapshot.customer.maskedPhone } : {}),
        ...(snapshot.customer.membershipId ? { membershipId: snapshot.customer.membershipId } : {}),
        ...(isFiniteNonNegative(snapshot.customer.redeemAmount)
          ? { redeemAmount: snapshot.customer.redeemAmount }
          : {}),
      }
    : null;

  return {
    version: POS_DRAFT_VERSION,
    owner: { tenantId: snapshot.owner.tenantId, userId: snapshot.owner.userId },
    shiftId: snapshot.shiftId,
    cart: snapshot.cart.map((item) => ({
      barcode: item.barcode,
      quantity: item.quantity,
      productName: item.productName,
      unitPrice: item.unitPrice,
      ...(item.isCritical === true ? { isCritical: true } : {}),
    })),
    customer,
    paymentType: snapshot.paymentType,
    cashReceived: snapshot.cashReceived,
    printReceipt: snapshot.printReceipt !== false,
    submission: {
      status: snapshot.submission.status,
      ...(snapshot.submission.message ? { message: snapshot.submission.message } : {}),
      ...(snapshot.submission.transactionId
        ? { transactionId: snapshot.submission.transactionId }
        : {}),
    },
    updatedAt: snapshot.updatedAt,
  };
}

export async function savePosDraft(snapshot: PosDraftSnapshot): Promise<void> {
  await AsyncStorage.setItem(POS_DRAFT_STORAGE_KEY, JSON.stringify(sanitizePosDraft(snapshot)));
}

export async function loadPosDraft(owner: PosDraftOwner): Promise<PosDraftSnapshot | null> {
  const raw = await AsyncStorage.getItem(POS_DRAFT_STORAGE_KEY);
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (isValidPosDraft(parsed, owner)) return sanitizePosDraft(parsed);
  } catch {
    // Corrupt snapshots are removed below.
  }
  await AsyncStorage.removeItem(POS_DRAFT_STORAGE_KEY);
  return null;
}

export async function clearPosDraft(): Promise<void> {
  await AsyncStorage.removeItem(POS_DRAFT_STORAGE_KEY);
}
