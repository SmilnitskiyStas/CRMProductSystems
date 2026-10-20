import { describe, expect, it } from 'vitest';
import type { SaleReceipt } from '@shared/types';
import {
  backoffDelayMs,
  BACKOFF_MAX_MS,
  BATCH_SIZE,
  classifyBatchItem,
  closeShiftBlockReason,
  OutboxService,
  type BatchItemWire,
  type BatchResponse,
  type OutboxPayload,
  type OutboxRow,
} from './outbox';
import { MemoryOutboxRepo } from './outboxMemory';

const receipt = (no: string): SaleReceipt => ({
  transactionId: '',
  receiptNumber: no,
  subtotal: 10,
  paymentType: 'Cash',
  paymentAmount: 10,
  change: 0,
  fiscalStatus: 'offline',
  fiscalNumber: null,
  createdAt: '2026-10-18T10:00:00Z',
  items: [],
  local: true,
});

const row = (id: string, createdAt = 1): OutboxRow => ({
  clientTransactionId: id,
  payload: { shiftId: 's', items: [{ barcode: '1', quantity: 1 }], paymentType: 'Cash', paymentAmount: 10, clientTransactionId: id },
  status: 'pending',
  attempts: 0,
  lastError: null,
  createdAt,
  nextAttemptAt: 0,
  localReceiptNo: `L-1-${createdAt}`,
  serverReceiptNo: null,
  receipt: receipt(`L-1-${createdAt}`),
});

const ok = (id: string, status = 'created', over: Partial<BatchItemWire> = {}): BatchItemWire => ({
  clientTransactionId: id,
  status,
  sale: { receiptNumber: `R-${id}` },
  error: null,
  errorStatusCode: null,
  ...over,
});

function setup(responder: (p: OutboxPayload[]) => BatchResponse | Promise<BatchResponse>) {
  const repo = new MemoryOutboxRepo();
  let now = 1_000_000;
  const sent: OutboxPayload[][] = [];
  const svc = new OutboxService(repo, {
    send: async (p) => {
      sent.push(p);
      return responder(p);
    },
    now: () => now,
    mergeServerSale: (r, sale) => ({ ...r.receipt, receiptNumber: (sale as { receiptNumber: string } | null)?.receiptNumber ?? r.receipt.receiptNumber, local: false }),
  });
  return { repo, svc, sent, advance: (ms: number) => (now += ms) };
}

describe('backoffDelayMs', () => {
  it('grows exponentially and is capped', () => {
    expect([0, 1, 2, 3, 4].map(backoffDelayMs)).toEqual([0, 5000, 10000, 20000, 40000]);
    expect(backoffDelayMs(50)).toBe(BACKOFF_MAX_MS);
  });
});

describe('classifyBatchItem', () => {
  it('created and duplicate are synced', () => {
    expect(classifyBatchItem(ok('a')).action).toBe('synced');
    expect(classifyBatchItem(ok('a', 'duplicate')).action).toBe('synced');
  });
  it('409 / 423 / other 4xx are rejected, 5xx and unknown retry', () => {
    const failed = (code: number | null) => classifyBatchItem(ok('a', 'failed', { sale: null, error: 'x', errorStatusCode: code }));
    expect(failed(409).action).toBe('rejected');
    expect(failed(423).action).toBe('rejected');
    expect(failed(400).action).toBe('rejected');
    expect(failed(500).action).toBe('retry');
    expect(failed(null).action).toBe('retry');
    expect(classifyBatchItem(undefined).action).toBe('retry');
  });
});

describe('closeShiftBlockReason', () => {
  it('blocks closing only while unsent sales exist', () => {
    expect(closeShiftBlockReason(0)).toBeNull();
    expect(closeShiftBlockReason(3)).toContain('(3)');
  });
});

describe('OutboxService', () => {
  it('syncs a batch and stores the server receipt number', async () => {
    const { repo, svc } = setup((p) => ({ kind: 'ok', results: p.map((x) => ok(x.clientTransactionId)) }));
    svc.enqueue(row('a', 1));
    svc.enqueue(row('b', 2));
    await svc.syncNow();
    expect(repo.get('a')).toMatchObject({ status: 'synced', serverReceiptNo: 'R-a' });
    expect(svc.summary().pending).toBe(0);
  });

  it('treats duplicate as synced and rejects 409/423 with a reason, keeping others', async () => {
    const { repo, svc } = setup((p) => ({
      kind: 'ok',
      results: [
        ok(p[0].clientTransactionId, 'duplicate'),
        ok(p[1].clientTransactionId, 'failed', { sale: null, error: 'closed', errorStatusCode: 409 }),
        ok(p[2].clientTransactionId, 'failed', { sale: null, error: 'expired', errorStatusCode: 423 }),
        ok(p[3].clientTransactionId, 'failed', { sale: null, error: 'boom', errorStatusCode: 500 }),
      ],
    }));
    for (const [i, id] of ['a', 'b', 'c', 'd'].entries()) svc.enqueue(row(id, i + 1));
    await svc.syncNow();
    expect(repo.get('a')?.status).toBe('synced');
    expect(repo.get('b')).toMatchObject({ status: 'rejected' });
    expect(repo.get('b')?.lastError).toContain('closed');
    expect(repo.get('c')?.status).toBe('rejected');
    expect(repo.get('d')).toMatchObject({ status: 'pending', attempts: 1 });
    expect(svc.summary()).toMatchObject({ pending: 1, rejected: 2 });
  });

  it('network failure keeps the sale pending with exponential backoff; due again after the delay', async () => {
    let online = false;
    const { repo, svc, sent, advance } = setup((p) =>
      online ? { kind: 'ok', results: p.map((x) => ok(x.clientTransactionId)) } : { kind: 'network' },
    );
    svc.enqueue(row('a'));
    await svc.syncNow();
    expect(repo.get('a')).toMatchObject({ status: 'pending', attempts: 1, nextAttemptAt: 1_000_000 + 5000 });
    await svc.syncNow(); // still in backoff
    expect(sent).toHaveLength(1);
    advance(5000);
    await svc.syncNow();
    expect(repo.get('a')?.attempts).toBe(2);
    expect(repo.get('a')?.nextAttemptAt).toBe(1_000_000 + 5000 + 10_000);
    online = true;
    await svc.syncNow(true); // manual retry ignores the timer
    expect(repo.get('a')?.status).toBe('synced');
  });

  it('401 keeps the queue untouched and flags re-login; resumes afterwards', async () => {
    let auth = false;
    const { repo, svc } = setup((p) =>
      auth ? { kind: 'ok', results: p.map((x) => ok(x.clientTransactionId)) } : { kind: 'unauthorized' },
    );
    svc.enqueue(row('a'));
    await svc.syncNow();
    expect(repo.get('a')).toMatchObject({ status: 'pending', attempts: 0 });
    expect(svc.summary().needsLogin).toBe(true);
    auth = true;
    svc.clearNeedsLogin();
    await svc.syncNow();
    expect(repo.get('a')?.status).toBe('synced');
    expect(svc.summary().needsLogin).toBe(false);
  });

  it('sends at most 100 per request and continues with the next batch', async () => {
    const { svc, sent } = setup((p) => ({ kind: 'ok', results: p.map((x) => ok(x.clientTransactionId)) }));
    for (let i = 0; i < BATCH_SIZE + 5; i++) svc.enqueue(row(`id-${i}`, i + 1));
    await svc.syncNow();
    expect(sent.map((b) => b.length)).toEqual([BATCH_SIZE, 5]);
  });

  it('is single-flight', async () => {
    const { svc, sent } = setup((p) => ({ kind: 'ok', results: p.map((x) => ok(x.clientTransactionId)) }));
    svc.enqueue(row('a'));
    await Promise.all([svc.syncNow(), svc.syncNow()]);
    expect(sent).toHaveLength(1);
  });

  it('recoverStuck returns sending rows to pending', () => {
    const { repo, svc } = setup(() => ({ kind: 'network' }));
    svc.enqueue({ ...row('a'), status: 'sending' });
    svc.recoverStuck();
    expect(repo.get('a')?.status).toBe('pending');
  });

  it('retry and dismiss only apply to rejected sales; closing is blocked while unsynced exist', async () => {
    const { repo, svc } = setup((p) => ({
      kind: 'ok',
      results: p.map((x) => ok(x.clientTransactionId, 'failed', { sale: null, error: 'closed', errorStatusCode: 409 })),
    }));
    svc.enqueue(row('a'));
    expect(svc.hasUnsynced()).toBe(true); // shift close must be refused now
    await svc.syncNow();
    expect(svc.hasUnsynced()).toBe(false); // rejected does not block closing
    svc.retryRejected('a');
    expect(repo.get('a')).toMatchObject({ status: 'pending', lastError: null });
    expect(svc.hasUnsynced()).toBe(true);
    await svc.syncNow(true);
    svc.dismissRejected('a');
    expect(repo.get('a')?.status).toBe('dismissed');
    expect(svc.list()).toHaveLength(0);
    svc.dismissRejected('a'); // no-op
  });
});
