import { describe, expect, it } from 'vitest';
import type { CatalogItem, SaleReceipt } from '@shared/types';
import type { PromotionRule } from '@shared/promotions/engine';
import type { CatalogStore } from './catalogStore';
import { OutboxService, toEntry, type BatchResponse, type OutboxPayload } from './outbox';
import { MemoryOutboxRepo } from './outboxMemory';
import { receiptLines } from './print/layout';
import { mergeServerSale, OutboxSalesGateway } from './salesGateway';

const bread: CatalogItem = { id: 'a', name: 'Bread Kyivkhlib', barcodes: ['111'], price: 42.73, vatRate: 20, unit: 'pcs', categoryId: 'c1' };
const catalog = { findByBarcode: (b: string) => (b === '111' ? bread : null) } as unknown as CatalogStore;
const rule: PromotionRule = {
  id: '00000000-0000-0000-0000-000000000101',
  versionId: '00000000-0000-0000-0000-000000000201',
  name: 'Акція 2+1',
  createdAt: '2026-01-01T00:00:00Z',
  definition: { type: 'buy_n_get_m', targets: { itemIds: ['a'] }, params: { buyQty: 2, getQty: 1, getPrice: 0.01 } },
};

const base = (over: Partial<SaleReceipt> = {}): SaleReceipt => ({
  transactionId: '',
  receiptNumber: 'L-2-1',
  localReceiptNo: 'L-2-1',
  local: true,
  subtotal: 85.47,
  paymentType: 'Cash',
  paymentAmount: 100,
  change: 14.53,
  fiscalStatus: 'offline',
  fiscalNumber: null,
  createdAt: '2026-10-21T10:00:00',
  registerNumber: 2,
  cashierName: 'Anna',
  items: [
    { name: 'Bread Kyivkhlib', barcode: '111', quantity: 2, unitPrice: 42.73, total: 85.46, regularUnitPrice: 42.73, promoLabel: null },
    { name: 'Bread Kyivkhlib', barcode: '111', quantity: 1, unitPrice: 0.01, total: 0.01, regularUnitPrice: 42.73, promoLabel: 'Акція 2+1' },
  ],
  ...over,
});

describe('receipt layout with promotion sub-lines', () => {
  it('prints the product once, a row per sub-line, the rule label and the promo savings (32 columns)', () => {
    const lines = receiptLines(base(), 32);
    expect(lines.every((l) => l.length <= 32)).toBe(true);
    expect(lines.filter((l) => l.includes('Bread Kyivkhlib'))).toHaveLength(1);
    expect(lines).toContain('  2 x 42,73                85,46');
    expect(lines).toContain('  1 x 0,01                  0,01');
    expect(lines).toContain('  Акція: Акція 2+1');
    expect(lines).toContain('Знижка за акціями         -42,72');
    const total = lines.find((l) => l.startsWith('РАЗОМ'))!;
    expect(total).toMatch(/^РАЗОМ +85,47$/);
    expect(total).toHaveLength(32);
  });

  it('fits 48 columns too and omits the savings row when nothing was discounted', () => {
    const plain = base({
      items: [{ name: 'Bread Kyivkhlib', barcode: '111', quantity: 3, unitPrice: 42.73, total: 128.19, regularUnitPrice: 42.73 }],
      subtotal: 128.19,
    });
    const lines = receiptLines(plain, 48);
    expect(lines.every((l) => l.length <= 48)).toBe(true);
    expect(lines.some((l) => l.startsWith('Знижка за акціями'))).toBe(false);
    expect(receiptLines(base(), 48).every((l) => l.length <= 48)).toBe(true);
  });
});

function setup(rules: PromotionRule[]) {
  const repo = new MemoryOutboxRepo();
  let respond: (p: OutboxPayload[]) => BatchResponse = () => ({ kind: 'network' });
  const outbox = new OutboxService(repo, { send: async (p) => respond(p), now: () => 1_000, mergeServerSale });
  const gw = new OutboxSalesGateway(
    outbox,
    catalog,
    () => ({ storeId: 's1', cashierId: 'c1', registerNumber: 2, registerName: 'R2', storeName: 'S', cashierName: 'Anna' }),
    () => Date.parse('2026-10-21T10:00:00Z'),
    () => rules,
  );
  return { repo, outbox, gw, setRespond: (f: typeof respond) => (respond = f) };
}

const req = (id: string, qty: number, pay: number) => ({
  clientTransactionId: id,
  shiftId: 'sh',
  items: [{ barcode: '111', quantity: qty }],
  paymentType: 'Cash' as const,
  paymentAmount: pay,
});

describe('offline sale priced with promotions + server re-pricing report', () => {
  it('the provisional receipt uses the promo total and promo sub-lines; the payload still sends only barcode+quantity', async () => {
    const { gw, repo } = setup([rule]);
    const r = await gw.submit(req('a', 3, 100));
    expect(r.ok && r.sale.subtotal).toBe(85.47);
    expect(r.ok && r.sale.change).toBe(14.53);
    expect(r.ok && r.sale.items.map((i) => [i.quantity, i.unitPrice, i.promoLabel])).toEqual([
      [2, 42.73, null],
      [1, 0.01, 'Акція 2+1'],
    ]);
    expect(repo.get('a')!.payload.items).toEqual([{ barcode: '111', quantity: 3 }]);
  });

  it('a different server total is stored on the queue entry (difference) and the receipt carries localTotal', async () => {
    const { gw, outbox, repo, setRespond } = setup([rule]);
    await gw.submit(req('a', 3, 100));
    setRespond((p) => ({
      kind: 'ok',
      results: p.map((x) => ({
        clientTransactionId: x.clientTransactionId,
        status: 'created',
        error: null,
        errorStatusCode: null,
        sale: {
          transactionId: 't1',
          receiptNumber: 'R-9',
          subtotal: 128.19, // server had no promo at that moment
          paymentType: 'cash',
          paymentAmount: 128.19,
          change: 0,
          fiscalStatus: 'pending_fiscalization',
          fiscalNumber: null,
          createdAt: '2026-10-21T10:00:01Z',
          items: [{ productName: 'Bread Kyivkhlib', barcode: '111', quantity: 3, unitPrice: 42.73, discountAmount: 0, total: 128.19 }],
        },
      })),
    }));
    await outbox.syncNow(true);
    const row = repo.get('a')!;
    expect(row.status).toBe('synced');
    expect(row.receipt.subtotal).toBe(128.19);
    expect(row.receipt.localTotal).toBe(85.47);
    expect(row.receipt.change).toBe(0); // tendered 100 < server total: no change due
    const e = toEntry(row);
    expect(e.totalDifference).toBe(42.72);
    expect(e.serverTotal).toBe(128.19);
  });

  it('identical totals leave no re-pricing marker; server promo sub-lines and labels are kept', async () => {
    const { gw, outbox, repo, setRespond } = setup([rule]);
    await gw.submit(req('a', 3, 100));
    setRespond((p) => ({
      kind: 'ok',
      results: p.map((x) => ({
        clientTransactionId: x.clientTransactionId,
        status: 'created',
        error: null,
        errorStatusCode: null,
        sale: {
          transactionId: 't1',
          receiptNumber: 'R-9',
          subtotal: 85.47,
          paymentType: 'cash',
          paymentAmount: 85.47,
          change: 0,
          fiscalStatus: 'pending_fiscalization',
          fiscalNumber: null,
          createdAt: '2026-10-21T10:00:01Z',
          items: [
            { productName: 'Bread Kyivkhlib', barcode: '111', quantity: 2, unitPrice: 42.73, discountAmount: 0, total: 85.46 },
            { productName: 'Bread Kyivkhlib', barcode: '111', quantity: 1, unitPrice: 0.01, discountAmount: 0, total: 0.01, promotionRuleId: rule.id, promotionLabel: 'Акція 2+1' },
          ],
        },
      })),
    }));
    await outbox.syncNow(true);
    const row = repo.get('a')!;
    expect(row.receipt.localTotal).toBeUndefined();
    expect(row.receipt.items[1].promoLabel).toBe('Акція 2+1');
    expect(row.receipt.items[1].regularUnitPrice).toBe(42.73);
    expect(row.receipt.change).toBe(14.53);
    expect(toEntry(row).totalDifference).toBeNull();
  });
});
