import AsyncStorage from '@react-native-async-storage/async-storage';
import type { CreateWriteOffPayload } from '@/features/write-offs/types';
import { createWriteOff } from '@/features/write-offs/api/writeOffApi';

export type QueuedWriteOffStatus = 'queued' | 'syncing' | 'failed' | 'uncertain';

export interface QueuedWriteOff {
  operationId: string;
  owner: { tenantId: string; userId: string };
  payload: CreateWriteOffPayload;
  status: QueuedWriteOffStatus;
  attempts: number;
  createdAt: string;
  updatedAt: string;
  message?: string;
}

const PREFIX = 'offline_write_off_queue_v1';
const locks = new Map<string, Promise<unknown>>();
const listeners = new Set<() => void>();

const keyFor = (owner: QueuedWriteOff['owner']) =>
  `${PREFIX}:${encodeURIComponent(owner.tenantId)}:${encodeURIComponent(owner.userId)}`;

function serialize<T>(key: string, work: () => Promise<T>): Promise<T> {
  const previous = locks.get(key) ?? Promise.resolve();
  const next = previous.catch(() => undefined).then(work);
  locks.set(key, next);
  return next.finally(() => { if (locks.get(key) === next) locks.delete(key); });
}

async function read(key: string): Promise<QueuedWriteOff[]> {
  const raw = await AsyncStorage.getItem(key);
  if (!raw) return [];
  try {
    const value = JSON.parse(raw) as unknown;
    return Array.isArray(value) ? value.filter(isQueuedWriteOff) : [];
  } catch {
    return [];
  }
}

function isQueuedWriteOff(value: unknown): value is QueuedWriteOff {
  if (!value || typeof value !== 'object') return false;
  const item = value as Partial<QueuedWriteOff>;
  return typeof item.operationId === 'string'
    && typeof item.owner?.tenantId === 'string'
    && typeof item.owner?.userId === 'string'
    && typeof item.payload?.locationId === 'string'
    && Array.isArray(item.payload.items)
    && ['queued', 'syncing', 'failed', 'uncertain'].includes(String(item.status));
}

async function write(key: string, items: QueuedWriteOff[]): Promise<void> {
  if (items.length === 0) await AsyncStorage.removeItem(key);
  else await AsyncStorage.setItem(key, JSON.stringify(items));
  listeners.forEach((listener) => listener());
}

export function subscribeWriteOffQueue(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function newOperationId(): string {
  return `wo-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

export async function enqueueWriteOff(
  owner: QueuedWriteOff['owner'],
  payload: CreateWriteOffPayload,
): Promise<QueuedWriteOff> {
  const key = keyFor(owner);
  return serialize(key, async () => {
    const items = await read(key);
    const now = new Date().toISOString();
    const queued: QueuedWriteOff = {
      operationId: newOperationId(), owner, payload, status: 'queued', attempts: 0,
      createdAt: now, updatedAt: now,
    };
    items.push(queued);
    await write(key, items);
    return queued;
  });
}

export async function listQueuedWriteOffs(owner: QueuedWriteOff['owner']): Promise<QueuedWriteOff[]> {
  const key = keyFor(owner);
  await locks.get(key)?.catch(() => undefined);
  return read(key);
}

type SyncOutcome = 'synced' | 'failed' | 'uncertain';

export async function syncQueuedWriteOffs(owner: QueuedWriteOff['owner']): Promise<{
  synced: number;
  failed: number;
  uncertain: number;
}> {
  const key = keyFor(owner);
  return serialize(key, async () => {
    let queue = await read(key);
    let synced = 0;
    let failed = 0;
    let uncertain = 0;

    // An interrupted `syncing` record is uncertain: the server may have committed it.
    queue = queue.map((item) => item.status === 'syncing'
      ? { ...item, status: 'uncertain', message: 'Попередню синхронізацію не було підтверджено.' }
      : item);

    for (const candidate of queue.filter((item) => item.status === 'queued')) {
      const index = queue.findIndex((item) => item.operationId === candidate.operationId);
      if (index < 0) continue;
      queue[index] = { ...queue[index], status: 'syncing', attempts: queue[index].attempts + 1, updatedAt: new Date().toISOString() };
      await write(key, queue);

      let outcome: SyncOutcome;
      try {
        await createWriteOff(candidate.payload, candidate.operationId);
        outcome = 'synced';
      } catch (error) {
        const response = (error as { response?: { status?: number; data?: { error?: string } }; code?: string }).response;
        if (!response || (response.status != null && response.status >= 500)) outcome = 'uncertain';
        else outcome = 'failed';
        const current = queue.find((item) => item.operationId === candidate.operationId);
        if (current) {
          current.status = outcome;
          current.message = response?.data?.error ?? (outcome === 'uncertain'
            ? 'Сервер не підтвердив операцію; автоматичний повтор зупинено для захисту від дубля.'
            : 'Синхронізація не вдалася.');
          current.updatedAt = new Date().toISOString();
        }
      }

      if (outcome === 'synced') {
        queue = queue.filter((item) => item.operationId !== candidate.operationId);
        synced += 1;
      } else if (outcome === 'uncertain') uncertain += 1;
      else failed += 1;
      await write(key, queue);

      // A transport failure usually means connectivity disappeared; avoid hammering it.
      if (outcome === 'uncertain') break;
    }
    return { synced, failed, uncertain };
  });
}

export function resetWriteOffQueueLocksForTests(): void {
  locks.clear();
  listeners.clear();
}
