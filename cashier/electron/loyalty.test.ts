import { describe, expect, it } from 'vitest';
import type { CatalogItem } from '@shared/types';
import type { PromotionRule } from '@shared/promotions/engine';
import type { CatalogStore } from './catalogStore';
import { LOYALTY_MESSAGES, mapLoyaltyError, mapLoyaltyMember, toLoyaltyResult } from './loyalty';
import { OutboxService, type OutboxPayload } from './outbox';
import { MemoryOutboxRepo } from './outboxMemory';
import { receiptLines } from './print/layout';
import { mergeServerSale, OutboxSalesGateway } from './salesGateway';

const res = (status: number, networkError = false, body: unknown = null) => ({ status, networkError, body });

describe('loyalty resolve error mapping', () => {
  it('maps statuses to readable Ukrainian messages', () => {
    expect(mapLoyaltyError(res(409), 'code').message).toBe(LOYALTY_MESSAGES.codeInvalid);
    expect(mapLoyaltyError(res(404), 'code').message).toBe(LOYALTY_MESSAGES.codeInvalid);
    expect(mapLoyaltyError(res(404), 'identifier').message).toBe(LOYALTY_MESSAGES.notFound);
    expect(mapLoyaltyError(res(429), 'code').message).toBe('Забагато спроб, зачекайте.');
    expect(mapLoyaltyError(res(401), 'code').kind).toBe('unauthorized');
    expect(mapLoyaltyError(res(403), 'code').message).toBe(LOYALTY_MESSAGES.forbidden);
    expect(mapLoyaltyError(res(500), 'code').kind).toBe('server');
    const off = mapLoyaltyError(res(0, true), 'identifier');
    expect(off.kind).toBe('network');
    expect(off.message).toContain('лише онлайн');
  });

  it('never echoes server text (which could carry the code) into the message', () => {
    const e = mapLoyaltyError(res(409, false, { error: 'code SECRET-123 already used' }), 'code');
    expect(JSON.stringify(e)).not.toContain('SECRET');
  });

  it('maps a successful result including the nullable tier fields', () => {
    const ok = toLoyaltyResult(
      res(200, false, { membershipId: 'm1', customerId: 'c1', customerName: 'Іван', maskedPhone: '+380***1234', balance: 12.5, tierId: 't1', tierName: 'Gold', tierDiscountPercent: 5 }) as never,
      'code',
    );
    expect(ok).toEqual({
      ok: true,
      data: { membershipId: 'm1', customerId: 'c1', customerName: 'Іван', maskedPhone: '+380***1234', balance: 12.5, tierId: 't1', tierName: 'Gold', tierDiscountPercent: 5 },
    });
    expect(mapLoyaltyMember({ membershipId: 'm', customerId: null, customerName: null, maskedPhone: null, balance: 0 }).tierId).toBeNull();
  });
});

const bread: CatalogItem = { id: 'a', name: 'Bread', barcodes: ['111'], price: 100, vatRate: 20, unit: 'pcs', categoryId: 'c1' };
const catalog = { findByBarcode: (b: string) => (b === '111' ? bread : null) } as unknown as CatalogStore;
const memberRule: PromotionRule = {
  id: '00000000-0000-0000-0000-000000000101',
  versionId: '00000000-0000-0000-0000-000000000201',
  name: 'Для членів',
  createdAt: '2026-01-01T00:00:00Z',
  definition: { type: 'percent_off', targets: { itemIds: ['a'] }, audience: { kind: 'loyalty_member' }, params: { percent: 20 } },
};
const member = { membershipId: 'm1', customerId: 'cu1', customerName: 'Іван Петренко', maskedPhone: '+380***1234', balance: 10, tierId: 't1', tierName: 'Gold', tierDiscountPercent: 5 };

function setup() {
  const repo = new MemoryOutboxRepo();
  const sent: OutboxPayload[][] = [];
  const outbox = new OutboxService(repo, {
    send: async (p) => {
      sent.push(p);
      return { kind: 'network' };
    },
    now: () => 1,
    mergeServerSale,
  });
  const gw = new OutboxSalesGateway(
    outbox,
    catalog,
    () => ({ storeId: 's1', cashierId: 'c1', registerNumber: 2, registerName: 'R2', storeName: 'S', cashierName: 'Anna' }),
    () => Date.parse('2026-10-21T10:00:00Z'),
    () => [memberRule],
  );
  return { repo, outbox, gw, sent };
}
const req = (loyaltyMember: typeof member | null) => ({
  clientTransactionId: 'a',
  shiftId: 'sh',
  items: [{ barcode: '111', quantity: 1 }],
  paymentType: 'Cash' as const,
  paymentAmount: 100,
  loyaltyMember,
});

describe('sale with a loyalty member', () => {
  it('sends loyaltyMembershipId + customerId in the outbox/batch payload and prices with the member (rule 20% beats tier 5%)', async () => {
    const { gw, outbox, sent, repo } = setup();
    const r = await gw.submit(req(member));
    expect(r.ok && r.sale.subtotal).toBe(80);
    expect(r.ok && r.sale.customerName).toBe('Іван Петренко');
    await outbox.syncNow(true);
    expect(sent[0][0]).toMatchObject({ loyaltyMembershipId: 'm1', customerId: 'cu1' });
    expect(repo.get('a')!.payload).not.toHaveProperty('redeemAmount');
  });

  it('without a member the loyalty-only rule does not fire and no loyalty fields are sent', async () => {
    const { gw, outbox, sent } = setup();
    const r = await gw.submit(req(null));
    expect(r.ok && r.sale.subtotal).toBe(100);
    await outbox.syncNow(true);
    expect(sent[0][0]).not.toHaveProperty('loyaltyMembershipId');
    expect(sent[0][0]).not.toHaveProperty('customerId');
  });

  it('offline receipt promises bonuses; the server receipt carries accrued/balance after sync', async () => {
    const { gw, repo, outbox } = setup();
    const r = await gw.submit(req(member));
    if (!r.ok) throw new Error('x');
    expect(receiptLines(r.sale, 32).join(' ')).toContain('Бонуси будуть нараховані після синхронізації');
    expect(receiptLines(r.sale, 32).join('\n')).toContain('Клієнт: Іван Петренко');

    const merged = mergeServerSale(repo.get('a')!, {
      transactionId: 't',
      receiptNumber: 'R-1',
      subtotal: 80,
      paymentType: 'cash',
      paymentAmount: 80,
      change: 0,
      fiscalStatus: 'pending_fiscalization',
      fiscalNumber: null,
      createdAt: '2026-10-21T10:00:01Z',
      items: [{ productName: 'Bread', barcode: '111', quantity: 1, unitPrice: 100, discountAmount: 20, total: 80 }],
      loyaltyAccrued: 4,
      loyaltyRedeemed: 0,
      loyaltyBalance: 14,
      customerName: 'Іван Петренко',
    });
    for (const cols of [32, 48]) {
      const lines = receiptLines(merged, cols);
      expect(lines.every((l) => l.length <= cols)).toBe(true);
      expect(lines.join('\n')).toContain('Нараховано бонусів');
      expect(lines.join('\n')).toContain('Баланс бонусів');
      expect(lines.join(' ')).not.toContain('після синхронізації');
    }
    void outbox;
  });
});
