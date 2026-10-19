import type { OutboxStatus, QueueEntry, QueueSummary, SaleReceipt } from '@shared/types';

/** Wire shape of one queued sale = backend CreateSaleRequest. */
export interface OutboxPayload {
  shiftId: string;
  items: { barcode: string; quantity: number }[];
  paymentType: 'Cash' | 'Card';
  paymentAmount: number;
  clientTransactionId: string;
  /** Cashier who rang the sale (operator session at sale time). Absent on legacy rows: the server then uses the token's cashier. */
  cashierId?: string;
  /** When the sale was made (ISO UTC); lets the server verify the cashier had a session then. */
  occurredAt?: string;
}

export interface OutboxRow {
  clientTransactionId: string;
  payload: OutboxPayload;
  status: OutboxStatus;
  attempts: number;
  lastError: string | null;
  createdAt: number; // epoch ms
  nextAttemptAt: number; // epoch ms; 0 = due now
  localReceiptNo: string;
  serverReceiptNo: string | null;
  receipt: SaleReceipt; // snapshot shown/printed; refreshed from the server result on sync
}

/** Storage seam: SQLite in the app, in-memory in unit tests. All methods are synchronous. */
export interface OutboxRepo {
  insert(row: OutboxRow): void;
  get(id: string): OutboxRow | null;
  update(id: string, patch: Partial<Omit<OutboxRow, 'clientTransactionId'>>): void;
  /** Rows with the given statuses, oldest first. */
  listByStatus(statuses: OutboxStatus[], limit?: number): OutboxRow[];
  /** Latest rows (any status except dismissed), newest first. */
  listRecent(limit: number): OutboxRow[];
  count(statuses: OutboxStatus[]): number;
  /** Atomically increments and returns the local receipt sequence. */
  nextSeq(): number;
}

// ── backoff ──────────────────────────────────────────────────────────────────

export const BACKOFF_BASE_MS = 5_000;
export const BACKOFF_MAX_MS = 5 * 60_000;
export const BATCH_SIZE = 100;

/** Exponential backoff after the Nth failed attempt (1-based): 5s, 10s, 20s ... capped at 5 min. */
export function backoffDelayMs(attempts: number): number {
  if (attempts <= 0) return 0;
  return Math.min(BACKOFF_BASE_MS * 2 ** (attempts - 1), BACKOFF_MAX_MS);
}

// ── batch result mapping ─────────────────────────────────────────────────────

export interface BatchItemWire {
  clientTransactionId: string | null;
  status: string; // created | duplicate | failed
  sale: unknown | null;
  error: string | null;
  errorStatusCode: number | null;
}

export type ItemVerdict =
  | { action: 'synced'; sale: unknown | null }
  | { action: 'rejected'; reason: string }
  | { action: 'retry'; error: string };

/** Maps one per-item batch result to what the outbox should do (see spec: Offline rules). */
export function classifyBatchItem(item: BatchItemWire | undefined): ItemVerdict {
  if (!item) return { action: 'retry', error: 'Сервер не повернув результат для цього продажу.' };
  if (item.status === 'created' || item.status === 'duplicate') return { action: 'synced', sale: item.sale };
  const code = item.errorStatusCode ?? undefined;
  const msg = item.error ?? '';
  if (code === 409) return { action: 'rejected', reason: `Зміну закрито/не знайдено або конфлікт залишків. ${msg}`.trim() };
  if (code === 423) return { action: 'rejected', reason: `Товар із вичерпаним терміном придатності. ${msg}`.trim() };
  if (code !== undefined && code >= 400 && code < 500) return { action: 'rejected', reason: msg || `Відхилено сервером (${code}).` };
  return { action: 'retry', error: msg || (code ? `Помилка сервера (${code}).` : 'Тимчасова помилка.') };
}

export type BatchResponse =
  | { kind: 'ok'; results: BatchItemWire[] }
  | { kind: 'network' }
  | { kind: 'unauthorized' }
  | { kind: 'server'; status: number; message: string };

// ── service ──────────────────────────────────────────────────────────────────

export interface OutboxDeps {
  send(payloads: OutboxPayload[]): Promise<BatchResponse>;
  now(): number;
  /** Merge server data (fiscal number, final items) into the stored receipt. */
  mergeServerSale(row: OutboxRow, sale: unknown | null): SaleReceipt;
  onChange?(summary: QueueSummary): void;
}

export class OutboxService {
  private syncing: Promise<void> | null = null;
  private needsLogin = false;
  private lastSyncAt: number | null = null;
  private lastError: string | null = null;

  constructor(
    private readonly repo: OutboxRepo,
    private readonly deps: OutboxDeps,
  ) {}

  /** Rows left in 'sending' by a crash/kill go back to pending (the server is idempotent on the key). */
  recoverStuck(): void {
    for (const r of this.repo.listByStatus(['sending'])) this.repo.update(r.clientTransactionId, { status: 'pending' });
  }

  enqueue(row: OutboxRow): void {
    this.repo.insert(row);
    this.emit();
  }

  nextSeq(): number {
    return this.repo.nextSeq();
  }

  hasUnsynced(): boolean {
    return this.repo.count(['pending', 'sending']) > 0;
  }

  summary(): QueueSummary {
    return {
      pending: this.repo.count(['pending', 'sending']),
      rejected: this.repo.count(['rejected']),
      needsLogin: this.needsLogin,
      syncing: this.syncing !== null,
      lastSyncAt: this.lastSyncAt ? new Date(this.lastSyncAt).toISOString() : null,
      lastError: this.lastError,
    };
  }

  /** Call after a successful PIN login: the queue resumes. */
  clearNeedsLogin(): void {
    this.needsLogin = false;
    this.emit();
  }

  list(): QueueEntry[] {
    return this.repo.listRecent(200).map(toEntry);
  }

  getRow(id: string): OutboxRow | null {
    return this.repo.get(id);
  }

  /** Manual retry of a rejected sale (e.g. after the manager fixed the shift). */
  retryRejected(id: string): void {
    const r = this.repo.get(id);
    if (r?.status !== 'rejected') return;
    this.repo.update(id, { status: 'pending', nextAttemptAt: 0, lastError: null });
    this.emit();
  }

  /** The cashier acknowledged a rejected sale; it is hidden from the queue but kept in the DB. */
  dismissRejected(id: string): void {
    const r = this.repo.get(id);
    if (r?.status !== 'rejected') return;
    this.repo.update(id, { status: 'dismissed' });
    this.emit();
  }

  /** Single-flight sync. `force` ignores backoff timers (manual / connectivity regained). */
  syncNow(force = false): Promise<void> {
    if (this.syncing) return this.syncing;
    this.syncing = this.run(force).finally(() => {
      this.syncing = null;
      this.emit();
    });
    this.emit();
    return this.syncing;
  }

  private due(force: boolean): OutboxRow[] {
    const now = this.deps.now();
    return this.repo
      .listByStatus(['pending'])
      .filter((r) => force || r.nextAttemptAt <= now)
      .slice(0, BATCH_SIZE);
  }

  private async run(force: boolean): Promise<void> {
    for (;;) {
      const batch = this.due(force);
      if (batch.length === 0) return;
      for (const r of batch) this.repo.update(r.clientTransactionId, { status: 'sending' });
      this.emit();

      let res: BatchResponse;
      try {
        res = await this.deps.send(batch.map((r) => r.payload));
      } catch (e) {
        res = { kind: 'server', status: 0, message: (e as Error).message };
      }

      if (res.kind === 'unauthorized') {
        // keep everything; wait for a PIN re-login (no attempts burned)
        for (const r of batch) this.repo.update(r.clientTransactionId, { status: 'pending' });
        this.needsLogin = true;
        this.lastError = 'Потрібен повторний вхід за PIN-кодом для відправки черги.';
        return;
      }
      if (res.kind !== 'ok') {
        const msg = res.kind === 'network' ? 'Немає зв’язку з сервером.' : `Помилка сервера${res.status ? ` (${res.status})` : ''}: ${res.message}`;
        this.lastError = msg;
        for (const r of batch) this.scheduleRetry(r, msg);
        return;
      }

      this.needsLogin = false;
      const byId = new Map<string, BatchItemWire>();
      for (const it of res.results) if (it.clientTransactionId) byId.set(it.clientTransactionId.toLowerCase(), it);

      let anyRetry = false;
      for (const r of batch) {
        const verdict = classifyBatchItem(byId.get(r.clientTransactionId.toLowerCase()));
        if (verdict.action === 'synced') {
          const receipt = this.deps.mergeServerSale(r, verdict.sale);
          this.repo.update(r.clientTransactionId, {
            status: 'synced',
            lastError: null,
            serverReceiptNo: receipt.receiptNumber === r.localReceiptNo ? null : receipt.receiptNumber,
            receipt,
          });
        } else if (verdict.action === 'rejected') {
          this.repo.update(r.clientTransactionId, { status: 'rejected', lastError: verdict.reason });
        } else {
          anyRetry = true;
          this.scheduleRetry(r, verdict.error);
        }
      }
      this.lastSyncAt = this.deps.now();
      this.lastError = anyRetry ? 'Частину продажів буде повторно відправлено пізніше.' : null;
      this.emit();
      if (anyRetry) return; // retried items are in backoff: do not spin
      if (batch.length < BATCH_SIZE) return;
    }
  }

  private scheduleRetry(r: OutboxRow, error: string): void {
    const attempts = r.attempts + 1;
    this.repo.update(r.clientTransactionId, {
      status: 'pending',
      attempts,
      lastError: error,
      nextAttemptAt: this.deps.now() + backoffDelayMs(attempts),
    });
  }

  private emit(): void {
    this.deps.onChange?.(this.summary());
  }
}

export function toEntry(r: OutboxRow): QueueEntry {
  return {
    clientTransactionId: r.clientTransactionId,
    status: r.status,
    attempts: r.attempts,
    lastError: r.lastError,
    createdAt: new Date(r.createdAt).toISOString(),
    nextAttemptAt: r.nextAttemptAt > 0 ? new Date(r.nextAttemptAt).toISOString() : null,
    localReceiptNo: r.localReceiptNo,
    serverReceiptNo: r.serverReceiptNo,
    total: r.receipt.subtotal,
    paymentType: r.payload.paymentType,
    itemCount: r.payload.items.length,
    cashierName: r.receipt.cashierName ?? null,
  };
}

/** Provisional receipt number shown until the server assigns the real one. */
export const localReceiptNo = (registerNumber: number, seq: number): string => `L-${registerNumber}-${seq}`;

/** Closing a shift needs the server to know every sale of it: block while anything is unsent. */
export function closeShiftBlockReason(unsyncedCount: number): string | null {
  return unsyncedCount > 0
    ? `Є невідправлені продажі (${unsyncedCount}). Дочекайтесь синхронізації черги (екран «Черга») і повторіть закриття зміни.`
    : null;
}
