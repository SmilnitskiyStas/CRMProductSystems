import { describe, expect, it, vi } from 'vitest';
import { addItem, priceCart } from './cart';
import type { PromotionRule } from './promotions/engine';
import { routeScan } from './scanRouting';
import { ScanDetector } from './scanBuffer';
import { isAcceptableCustomerCode } from './cameraScan';
import type { CatalogItem } from './types';

const item: CatalogItem = { id: 'a', name: 'Milk', barcodes: ['1'], price: 100, vatRate: 20, unit: 'pcs', categoryId: 'c1' };
const memberRule = (kind: string, extra: Record<string, unknown> = {}): PromotionRule => ({
  id: '00000000-0000-0000-0000-000000000101',
  versionId: '00000000-0000-0000-0000-000000000201',
  name: 'Для своїх',
  createdAt: '2026-01-01T00:00:00Z',
  definition: { type: 'percent_off', targets: { itemIds: ['a'] }, audience: { kind, ...extra }, params: { percent: 20 } },
});
const ctx = { storeId: 's1', at: '2026-10-21T10:00:00Z' };

describe('cart priced with a loyalty member', () => {
  const cart = addItem([], item, '1', 2);

  it('a loyalty_member rule fires only when a member is attached', () => {
    expect(priceCart(cart, [memberRule('loyalty_member')], ctx).total).toBe(200);
    const withMember = priceCart(cart, [memberRule('loyalty_member')], { ...ctx, membership: { id: 'm', tierId: null, tierPercent: 0 } });
    expect(withMember.total).toBe(160);
    expect(withMember.lines[0].labels).toEqual(['Для своїх']);
    expect(withMember.savings).toBe(40);
  });

  it('loyalty_tier rules need the listed tier', () => {
    const r = memberRule('loyalty_tier', { tierIds: ['tier-gold'] });
    expect(priceCart(cart, [r], { ...ctx, membership: { id: 'm', tierId: 'tier-silver', tierPercent: 0 } }).total).toBe(200);
    expect(priceCart(cart, [r], { ...ctx, membership: { id: 'm', tierId: 'tier-gold', tierPercent: 0 } }).total).toBe(160);
  });

  it('the tier discount competes with promotions: the better price wins, a tier price carries no rule label', () => {
    const rule = memberRule('all'); // 20% for everyone
    const tier25 = priceCart(cart, [rule], { ...ctx, membership: { id: 'm', tierId: 't', tierPercent: 25 } });
    expect(tier25.total).toBe(150);
    expect(tier25.lines[0].labels).toEqual([]);
    const tier5 = priceCart(cart, [rule], { ...ctx, membership: { id: 'm', tierId: 't', tierPercent: 5 } });
    expect(tier5.total).toBe(160);
    expect(tier5.lines[0].labels).toEqual(['Для своїх']);
  });

  it('tier only (no rules): discount off the regular price, shown through the same savings figure', () => {
    const p = priceCart(cart, [], { ...ctx, membership: { id: 'm', tierId: 't', tierPercent: 10 } });
    expect(p.total).toBe(180);
    expect(p.savings).toBe(20);
  });
});

describe('scan routing: customer-armed scans never hit the catalog', () => {
  it('routes to the customer handler when armed and to the catalog otherwise', async () => {
    const product = vi.fn(async (c: string) => `p:${c}`);
    const customer = vi.fn(async (c: string) => `c:${c}`);
    expect(await routeScan(true, 'LOYALTY-QR', { product, customer })).toBe('c:LOYALTY-QR');
    expect(product).not.toHaveBeenCalled();
    expect(await routeScan(false, '4823117506458', { product, customer })).toBe('p:4823117506458');
    expect(customer).toHaveBeenCalledTimes(1);
  });
});

describe('long QR payloads', () => {
  it('the wedge detector reports a long (150 char) payload typed fast ending with Enter as one scan', () => {
    const payload = 'loyalty:v1:' + 'AbC123-_.:/'.repeat(14);
    const d = new ScanDetector();
    let t = 1000;
    for (const ch of payload) d.feed(ch, (t += 4));
    expect(d.feed('Enter', t + 4)).toEqual({ type: 'scan', code: payload });
  });

  it('customer mode accepts free-form payloads, rejects control characters', () => {
    expect(isAcceptableCustomerCode('https://x.y/c?code=abc.def')).toBe(true);
    expect(isAcceptableCustomerCode('ab')).toBe(false);
    expect(isAcceptableCustomerCode('abcd\u0007efg')).toBe(false);
  });
});
