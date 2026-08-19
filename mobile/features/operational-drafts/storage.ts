import AsyncStorage from '@react-native-async-storage/async-storage';

export const OPERATIONAL_DRAFT_VERSION = 1;
const PREFIX = 'operational_draft_v1';

export type OperationalDraftKind = 'receipt' | 'write-off' | 'transfer' | 'production';
export type DraftSubmissionStatus = 'idle' | 'failed' | 'uncertain' | 'conflict';

export interface DraftOwner {
  tenantId: string;
  userId: string;
}

export interface DraftItem {
  productId: string;
  productStockId?: string;
  itemId?: string;
  productName: string;
  batchNumber?: string | null;
  expiryDate?: string | null;
  quantity: number;
  availableQty?: number;
}

export type OperationalDraftPayload =
  | { kind: 'receipt'; receiptId: string; items: DraftItem[] }
  | { kind: 'write-off'; locationId: string; reason: string; notes: string; items: DraftItem[] }
  | { kind: 'transfer'; fromLocationId: string; toLocationId: string; notes: string; items: DraftItem[] }
  | { kind: 'production'; locationId: string; recipeId: string; plannedQty: string; notes: string };

export interface OperationalDraft {
  version: typeof OPERATIONAL_DRAFT_VERSION;
  owner: DraftOwner;
  scope: string;
  payload: OperationalDraftPayload;
  submission: { status: DraftSubmissionStatus; message?: string };
  updatedAt: string;
}

const queues = new Map<string, Promise<void>>();
const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const nonEmpty = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const optionalString = (value: unknown) => value === undefined || value === null || typeof value === 'string';
const hasOwner = (value: unknown, owner: DraftOwner) =>
  isRecord(value)
  && isRecord(value.owner)
  && value.owner.tenantId === owner.tenantId
  && value.owner.userId === owner.userId;

const keyPart = (value: string) => encodeURIComponent(value);

export function draftStorageKey(
  owner: DraftOwner,
  kind: OperationalDraftKind,
  scope = 'create',
): string {
  return `${PREFIX}:${keyPart(owner.tenantId)}:${keyPart(owner.userId)}:${kind}:${keyPart(scope)}`;
}

/** Shared v1 key used before owner identity became part of the namespace. */
export function legacyDraftStorageKey(kind: OperationalDraftKind, scope = 'create'): string {
  return `${PREFIX}:${kind}:${scope}`;
}

function validItem(value: unknown): value is DraftItem {
  return isRecord(value)
    && nonEmpty(value.productId)
    && nonEmpty(value.productName)
    && optionalString(value.productStockId)
    && optionalString(value.itemId)
    && optionalString(value.batchNumber)
    && optionalString(value.expiryDate)
    && typeof value.quantity === 'number'
    && Number.isFinite(value.quantity)
    && value.quantity > 0
    && (value.availableQty === undefined
      || (typeof value.availableQty === 'number' && Number.isFinite(value.availableQty) && value.availableQty >= 0));
}

function validPayload(value: unknown, kind: OperationalDraftKind): value is OperationalDraftPayload {
  if (!isRecord(value) || value.kind !== kind) return false;
  if (kind === 'production') {
    return typeof value.locationId === 'string' && typeof value.recipeId === 'string'
      && typeof value.plannedQty === 'string' && typeof value.notes === 'string';
  }
  if (!Array.isArray(value.items) || !value.items.every(validItem)) return false;
  if (kind === 'receipt') return nonEmpty(value.receiptId);
  if (kind === 'write-off') {
    return typeof value.locationId === 'string' && typeof value.reason === 'string'
      && typeof value.notes === 'string';
  }
  return typeof value.fromLocationId === 'string' && typeof value.toLocationId === 'string'
    && typeof value.notes === 'string';
}

export function isValidOperationalDraft(
  value: unknown,
  owner: DraftOwner,
  kind: OperationalDraftKind,
  scope: string,
): value is OperationalDraft {
  if (!isRecord(value) || value.version !== OPERATIONAL_DRAFT_VERSION || value.scope !== scope) return false;
  if (!isRecord(value.owner) || value.owner.tenantId !== owner.tenantId || value.owner.userId !== owner.userId) return false;
  if (!validPayload(value.payload, kind) || !isRecord(value.submission)) return false;
  return ['idle', 'failed', 'uncertain', 'conflict'].includes(String(value.submission.status))
    && optionalString(value.submission.message) && typeof value.updatedAt === 'string';
}

function cleanItem(item: DraftItem): DraftItem {
  return {
    productId: item.productId,
    productName: item.productName,
    quantity: item.quantity,
    ...(item.productStockId ? { productStockId: item.productStockId } : {}),
    ...(item.itemId ? { itemId: item.itemId } : {}),
    ...(item.batchNumber !== undefined ? { batchNumber: item.batchNumber } : {}),
    ...(item.expiryDate !== undefined ? { expiryDate: item.expiryDate } : {}),
    ...(item.availableQty !== undefined ? { availableQty: item.availableQty } : {}),
  };
}

/** Explicit field whitelist: tokens, QR values and arbitrary caller fields are dropped. */
export function sanitizeOperationalDraft(draft: OperationalDraft): OperationalDraft {
  const p = draft.payload;
  let payload: OperationalDraftPayload;
  if (p.kind === 'production') {
    payload = { kind: p.kind, locationId: p.locationId, recipeId: p.recipeId, plannedQty: p.plannedQty, notes: p.notes };
  } else if (p.kind === 'receipt') {
    payload = { kind: p.kind, receiptId: p.receiptId, items: p.items.map(cleanItem) };
  } else if (p.kind === 'write-off') {
    payload = { kind: p.kind, locationId: p.locationId, reason: p.reason, notes: p.notes, items: p.items.map(cleanItem) };
  } else {
    payload = { kind: p.kind, fromLocationId: p.fromLocationId, toLocationId: p.toLocationId, notes: p.notes, items: p.items.map(cleanItem) };
  }
  return {
    version: OPERATIONAL_DRAFT_VERSION,
    owner: { tenantId: draft.owner.tenantId, userId: draft.owner.userId },
    scope: draft.scope,
    payload,
    submission: {
      status: draft.submission.status,
      ...(draft.submission.message ? { message: draft.submission.message } : {}),
    },
    updatedAt: draft.updatedAt,
  };
}

export function saveOperationalDraft(draft: OperationalDraft): Promise<void> {
  const key = draftStorageKey(draft.owner, draft.payload.kind, draft.scope);
  const previous = queues.get(key) ?? Promise.resolve();
  const next = previous.catch(() => undefined).then(() =>
    AsyncStorage.setItem(key, JSON.stringify(sanitizeOperationalDraft(draft))));
  queues.set(key, next);
  return next.finally(() => {
    if (queues.get(key) === next) queues.delete(key);
  });
}

export async function loadOperationalDraft(
  owner: DraftOwner,
  kind: OperationalDraftKind,
  scope = 'create',
): Promise<OperationalDraft | null> {
  const key = draftStorageKey(owner, kind, scope);
  await queues.get(key)?.catch(() => undefined);
  const raw = await AsyncStorage.getItem(key);
  if (raw) {
    try {
      const value: unknown = JSON.parse(raw);
      if (isValidOperationalDraft(value, owner, kind, scope)) return sanitizeOperationalDraft(value);
    } catch {
      // Corrupt data in an owner-specific namespace is safe to remove.
    }
    await AsyncStorage.removeItem(key);
    return null;
  }

  // One-time migration from the former shared operation key. A foreign owner's
  // valid record is deliberately left untouched so only that owner can migrate it.
  const legacyKey = legacyDraftStorageKey(kind, scope);
  await queues.get(legacyKey)?.catch(() => undefined);
  const legacyRaw = await AsyncStorage.getItem(legacyKey);
  if (!legacyRaw) return null;
  try {
    const value: unknown = JSON.parse(legacyRaw);
    if (!isValidOperationalDraft(value, owner, kind, scope)) {
      // Remove an invalid legacy record only when it claims the current namespace.
      // A valid/invalid foreign owner's record is never ours to destroy.
      if (hasOwner(value, owner)) await AsyncStorage.removeItem(legacyKey);
      return null;
    }
    const draft = sanitizeOperationalDraft(value);
    await saveOperationalDraft(draft);
    await AsyncStorage.removeItem(legacyKey);
    return draft;
  } catch {
    await AsyncStorage.removeItem(legacyKey);
    return null;
  }
}

export async function clearOperationalDraft(
  owner: DraftOwner,
  kind: OperationalDraftKind,
  scope = 'create',
): Promise<void> {
  const key = draftStorageKey(owner, kind, scope);
  await queues.get(key)?.catch(() => undefined);
  await AsyncStorage.removeItem(key);

  const legacyKey = legacyDraftStorageKey(kind, scope);
  const legacyRaw = await AsyncStorage.getItem(legacyKey);
  if (!legacyRaw) return;
  try {
    const value: unknown = JSON.parse(legacyRaw);
    if (isValidOperationalDraft(value, owner, kind, scope)) {
      await AsyncStorage.removeItem(legacyKey);
    }
  } catch {
    await AsyncStorage.removeItem(legacyKey);
  }
}

export function resetDraftQueuesForTests(): void {
  queues.clear();
}
