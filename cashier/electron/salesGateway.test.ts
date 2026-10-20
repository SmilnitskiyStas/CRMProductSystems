import { describe, expect, it } from 'vitest';
import type { CatalogItem, SaleReceipt } from '@shared/types';
import type { CatalogStore } from './catalogStore';
import { OutboxService, toEntry, type BatchResponse, type OutboxPayload, type OutboxRow } from './outbox';
import { MemoryOutboxRepo } from './outboxMemory';
import { mergeServerSale, OutboxSalesGateway } from './salesGateway';

const milk: CatalogItem = { id: 'i1', name: 'Milk', barcodes: ['111'], price: 32.5, vatRate: 20, unit: 'pcs' };
const catalog = { findByBarcode: (b: string) => (b === '111' ? milk : null) } as unknown as CatalogStore;

function setup(cashierId = 'cashier-A', cashierName = 'Anna') {
  const repo = new MemoryOutboxRepo();
  const sent: OutboxPayload[][] = [];
  let respond: (p: OutboxPayload[]) => BatchResponse = () => ({ kind: 'network' });
  const outbox = new OutboxService(repo, {
    send: async (p) => {
      sent.push(p);
      return respond(p);
    },
    now: () => 1_000,
    mergeServerSale,
  });
  const gw = new OutboxSalesGateway(
    outbox,
    catalog,
    () => ({ storeId: 's1', cashierId, registerNumber: 3, registerName: 'R3', storeName: 'S', cashierName }),
    () => Date.parse('2026-10-18T10:00:00Z'),
  );
  return { repo, outbox, gw, sent, setRespond: (f: typeof respond) => (respond = f) };
}

const req = (id: string) => ({
  clientTransactionId: id,
  shiftId: 'sh',
  items: [{ barcode: '111', quantity: 2 }],
  paymentType: 'Cash' as const,
  paymentAmount: 100,
});

describe('OutboxSalesGateway', () => {
  it('stores cashierId and occurredAt in the payload and sends them in the batch', async () => {
    const { gw, outbox, sent } = setup();
    const r = await gw.submit(req('a'));
    expect(r.ok && r.sale.local).toBe(true);
    await outbox.syncNow(true);
    expect(sent[0][0]).toMatchObject({ clientTransactionId: 'a', cashierId: 'cashier-A', occurredAt: '2026-10-18T10:00:00.000Z' });
  });

  it('keeps the original cashier when another cashier flushes the queue (no blocking)', async () => {
    const a = setup('cashier-A', 'Anna');
    await a.gw.submit(req('a'));
    // a different operator signs in later: the queued row still carries Anna's id, and syncing is not blocked
    a.setRespond((p) => ({ kind: 'ok', results: p.map((x) => ({ clientTransactionId: x.clientTransactionId, status: 'created', sale: null, error: null, errorStatusCode: null })) }));
    await a.outbox.syncNow(true);
    expect(a.sent[0][0].cashierId).toBe('cashier-A');
    expect(a.repo.get('a')?.status).toBe('synced');
  });

  it('legacy rows without cashierId/occurredAt are sent unchanged (server uses the token cashier)', async () => {
    const { repo, outbox, sent } = setup();
    const legacy: OutboxRow = {
      clientTransactionId: 'old',
      payload: { shiftId: 'sh', items: [{ barcode: '111', quantity: 1 }], paymentType: 'Cash', paymentAmount: 50, clientTransactionId: 'old' },
      status: 'pending',
      attempts: 0,
      lastError: null,
      createdAt: 1,
      nextAttemptAt: 0,
      localReceiptNo: 'L-3-9',
      serverReceiptNo: null,
      receipt: { receiptNumber: 'L-3-9', subtotal: 32.5, paymentType: 'Cash', items: [] } as unknown as SaleReceipt,
    };
    repo.insert(legacy);
    await outbox.syncNow(true);
    expect(sent[0][0]).not.toHaveProperty('cashierId');
    expect(sent[0][0]).not.toHaveProperty('occurredAt');
    expect(toEntry(legacy).cashierName).toBeNull();
  });

  it('a server rejection of the cashier shows the reason in the queue entry', async () => {
    const { gw, outbox, setRespond, repo } = setup();
    await gw.submit(req('a'));
    setRespond((p) => ({
      kind: 'ok',
      results: p.map((x) => ({
        clientTransactionId: x.clientTransactionId,
        status: 'failed',
        sale: null,
        error: 'Cashier had no session on this register at that time.',
        errorStatusCode: 403,
      })),
    }));
    await outbox.syncNow(true);
    const row = repo.get('a')!;
    expect(row.status).toBe('rejected');
    expect(row.lastError).toContain('no session');
    expect(toEntry(row).cashierName).toBe('Anna');
  });
});
