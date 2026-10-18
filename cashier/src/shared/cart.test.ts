import { describe, expect, it } from 'vitest';
import { addItem, cartTotal, computeChange, lineTotal, quickCashAmounts, removeLine, setQuantity } from './cart';
import type { CatalogItem } from './types';

const milk: CatalogItem = { id: 'a', name: 'Milk', barcodes: ['1'], price: 32.5, vatRate: 20, unit: 'pcs' };
const cheese: CatalogItem = { id: 'b', name: 'Cheese', barcodes: ['2'], price: 199.99, vatRate: 20, unit: 'kg' };

describe('cart', () => {
  it('merges the same item and totals without float drift', () => {
    let cart = addItem([], milk, '1');
    cart = addItem(cart, milk, '1');
    cart = addItem(cart, milk, '1');
    expect(cart).toHaveLength(1);
    expect(cart[0].quantity).toBe(3);
    expect(cartTotal(cart)).toBe(97.5);
  });

  it('handles fractional quantity for weight units and whole for pieces', () => {
    let cart = addItem([], cheese, '2', 0.35);
    expect(lineTotal(cart[0])).toBe(70);
    cart = setQuantity(cart, 'b', 0.3333);
    expect(cart[0].quantity).toBe(0.333);
    cart = addItem(cart, milk, '1');
    cart = setQuantity(cart, 'a', 2.6);
    expect(cart[1].quantity).toBe(3);
  });

  it('removes a line when quantity becomes 0 and via removeLine', () => {
    let cart = addItem(addItem([], milk, '1'), cheese, '2');
    cart = setQuantity(cart, 'a', 0);
    expect(cart.map((l) => l.itemId)).toEqual(['b']);
    expect(removeLine(cart, 'b')).toEqual([]);
  });

  it('does not add items without a price', () => {
    expect(addItem([], { ...milk, price: null }, '1')).toEqual([]);
  });

  it('computes change and shortfall', () => {
    expect(computeChange(97.5, 100)).toEqual({ enough: true, change: 2.5, shortfall: 0 });
    expect(computeChange(97.5, 50)).toEqual({ enough: false, change: 0, shortfall: 47.5 });
    expect(computeChange(0.3, 0.1 + 0.2).enough).toBe(true);
  });

  it('suggests exact and rounded-up cash amounts', () => {
    const q = quickCashAmounts(97.5);
    expect(q[0]).toBe(97.5);
    expect(q).toContain(100);
    expect(q.every((a) => a >= 97.5)).toBe(true);
  });
});
