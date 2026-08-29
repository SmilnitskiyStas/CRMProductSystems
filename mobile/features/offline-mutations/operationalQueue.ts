import AsyncStorage from '@react-native-async-storage/async-storage';
import type { CreateTransferPayload } from '@/features/transfers/types';
import type { ProductionOrderCreate } from '@/features/production/types';
import { createTransfer } from '@/features/transfers/api/transferApi';
import { createProductionOrder } from '@/features/production/api';
import { confirmReceipt, processItem } from '@/features/receipt/api/receiptApi';
import type { UpdateReceiptItemRequest } from '@/features/marketplace-orders/types';
import { finalizeMarketplaceReceipt, updateMarketplaceReceiptItem } from '@/features/marketplace-orders/api/marketplaceOrdersApi';

type Owner = { tenantId: string; userId: string };
export type OperationalMutation =
  | { kind: 'transfer.create'; payload: CreateTransferPayload }
  | { kind: 'production.create'; payload: ProductionOrderCreate }
  | { kind: 'receipt.item'; payload: { receiptId: string; itemId: string; quantityReceived: number } }
  | { kind: 'receipt.confirm'; payload: { receiptId: string } }
  | { kind: 'marketplace-receipt.item'; payload: { orderId: string; itemId: string; body: UpdateReceiptItemRequest } }
  | { kind: 'marketplace-receipt.finalize'; payload: { orderId: string } };
export interface QueuedOperationalMutation {
  operationId: string;
  owner: Owner;
  mutation: OperationalMutation;
  status: 'queued' | 'syncing' | 'failed' | 'uncertain';
  attempts: number;
  createdAt: string;
  message?: string;
}

const PREFIX = 'offline_operational_queue_v1';
const locks = new Map<string, Promise<unknown>>();
const listeners = new Set<() => void>();
const keyFor = (owner: Owner) => `${PREFIX}:${encodeURIComponent(owner.tenantId)}:${encodeURIComponent(owner.userId)}`;
const operationId = (kind: string) => `${kind}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;

async function read(key: string): Promise<QueuedOperationalMutation[]> {
  try {
    const raw = await AsyncStorage.getItem(key);
    const value: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(value) ? value as QueuedOperationalMutation[] : [];
  } catch { return []; }
}
async function write(key: string, queue: QueuedOperationalMutation[]) {
  if (queue.length) await AsyncStorage.setItem(key, JSON.stringify(queue));
  else await AsyncStorage.removeItem(key);
  listeners.forEach((listener) => listener());
}
function serialized<T>(key: string, action: () => Promise<T>): Promise<T> {
  const previous = locks.get(key) ?? Promise.resolve();
  const next = previous.catch(() => undefined).then(action);
  locks.set(key, next);
  return next.finally(() => { if (locks.get(key) === next) locks.delete(key); });
}

export async function enqueueOperationalMutation(owner: Owner, mutation: OperationalMutation) {
  const key = keyFor(owner);
  return serialized(key, async () => {
    const queue = await read(key);
    const item: QueuedOperationalMutation = {
      operationId: operationId(mutation.kind), owner, mutation, status: 'queued', attempts: 0,
      createdAt: new Date().toISOString(),
    };
    queue.push(item);
    await write(key, queue);
    return item;
  });
}

export async function listOperationalMutations(owner: Owner) {
  const key = keyFor(owner);
  await locks.get(key)?.catch(() => undefined);
  return read(key);
}

async function send(item: QueuedOperationalMutation) {
  if (item.mutation.kind === 'transfer.create') {
    await createTransfer(item.mutation.payload, item.operationId);
  } else if (item.mutation.kind === 'production.create') {
    await createProductionOrder(item.mutation.payload, item.operationId);
  } else if (item.mutation.kind === 'receipt.item') {
    const p = item.mutation.payload;
    await processItem(p.receiptId, p.itemId, p.quantityReceived, item.operationId);
  } else if (item.mutation.kind === 'receipt.confirm') {
    await confirmReceipt(item.mutation.payload.receiptId, item.operationId);
  } else if (item.mutation.kind === 'marketplace-receipt.item') {
    const p = item.mutation.payload;
    await updateMarketplaceReceiptItem(p.orderId, p.itemId, p.body, item.operationId);
  } else {
    await finalizeMarketplaceReceipt(item.mutation.payload.orderId, item.operationId);
  }
}

export async function syncOperationalMutations(owner: Owner) {
  const key = keyFor(owner);
  return serialized(key, async () => {
    let queue = (await read(key)).map((item) => item.status === 'syncing'
      ? { ...item, status: 'uncertain' as const, message: 'Результат попередньої синхронізації не підтверджено.' }
      : item);
    let synced = 0;
    for (const candidate of queue.filter((item) => item.status === 'queued')) {
      const index = queue.findIndex((item) => item.operationId === candidate.operationId);
      queue[index] = { ...queue[index], status: 'syncing', attempts: queue[index].attempts + 1 };
      await write(key, queue);
      try {
        await send(candidate);
        queue = queue.filter((item) => item.operationId !== candidate.operationId);
        synced += 1;
      } catch (error) {
        const response = (error as { response?: { status?: number; data?: { error?: string } } }).response;
        const current = queue.find((item) => item.operationId === candidate.operationId);
        if (current) {
          current.status = !response || (response.status ?? 0) >= 500 ? 'uncertain' : 'failed';
          current.message = response?.data?.error ?? 'Операцію не підтверджено.';
          if (current.status === 'uncertain') { await write(key, queue); break; }
        }
      }
      await write(key, queue);
    }
    return { synced, remaining: queue.length };
  });
}

export function subscribeOperationalQueue(listener: () => void) { listeners.add(listener); return () => listeners.delete(listener); }
export function resetOperationalQueueForTests() { locks.clear(); listeners.clear(); }
