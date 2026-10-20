import { describe, expect, it } from 'vitest';
import { addItem, cartTotal, priceCart } from './cart';
import type { PromotionRule } from './promotions/engine';
import type { CatalogItem } from './types';

describe('cart priced through promotions', () => {
  const rule = (over: Record<string, unknown> = {}): PromotionRule => ({
    id: '00000000-0000-0000-0000-000000000101',
    versionId: '00000000-0000-0000-0000-000000000201',
    name: '2+1',
    createdAt: '2026-01-01T00:00:00Z',
    definition: {
      type: 'buy_n_get_m',
      targets: { itemIds: ['a'] },
      params: { buyQty: 2, getQty: 1, getPrice: 0.01 },
      ...over,
    } as PromotionRule['definition'],
  });
  const ctx = { storeId: 's1', at: '2026-10-21T10:00:00Z' };
  const bread: CatalogItem = { id: 'a', name: 'Bread', barcodes: ['1'], price: 42.73, vatRate: 20, unit: 'pcs', categoryId: 'c1' };

  it('without rules the priced total equals the regular total', () => {
    const cart = addItem([], bread, '1', 3);
    const p = priceCart(cart, [], ctx);
    expect(p.total).toBe(cartTotal(cart));
    expect(p.savings).toBe(0);
    expect(p.lines[0].segments).toHaveLength(1);
  });

  it('2+1: the third unit flips the line to the split 2 x 42.73 + 1 x 0.01 instantly', () => {
    let cart = addItem([], bread, '1');
    cart = addItem(cart, bread, '1');
    expect(priceCart(cart, [rule()], ctx).savings).toBe(0);
    cart = addItem(cart, bread, '1'); // scan #3
    const p = priceCart(cart, [rule()], ctx);
    expect(p.lines[0].segments.map((s) => [s.quantity, s.unitPrice])).toEqual([
      [2, 42.73],
      [1, 0.01],
    ]);
    expect(p.lines[0].labels).toEqual(['2+1']);
    expect(p.total).toBe(85.47);
    expect(p.regularTotal).toBe(128.19);
    expect(p.savings).toBe(42.72);
  });

  it('category targets use the categoryId carried by the cart line', () => {
    const r = rule({ targets: { categoryIds: ['c1'] }, type: 'percent_off', params: { percent: 10 } });
    const p = priceCart(addItem([], bread, '1', 2), [r], ctx);
    expect(p.lines[0].segments).toEqual([{ quantity: 2, unitPrice: 38.46, ruleId: r.id, label: '2+1' }]);
    expect(p.total).toBe(76.92);
  });

  it('a rule outside its store scope does not apply', () => {
    const p = priceCart(addItem([], bread, '1', 3), [rule({ scope: { storeIds: ['other'] } })], ctx);
    expect(p.savings).toBe(0);
  });
});
