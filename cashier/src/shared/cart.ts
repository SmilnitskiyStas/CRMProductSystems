import type { CatalogItem } from './types';
import { fromKop, toKop } from './money';

export interface CartLine {
  itemId: string;
  barcode: string;
  name: string;
  unit: string;
  price: number;
  quantity: number;
}

const MAX_QTY = 9999;

/** Piece-like units sell in whole numbers; everything else (kg, l, ...) may be fractional. */
export const isWholeUnit = (unit: string): boolean => {
  const u = unit.trim().toLowerCase();
  return u === '' || u.startsWith('pcs') || u.startsWith('pc') || u.startsWith('piece') || u.startsWith('шт');
};

export function normalizeQty(unit: string, qty: number): number {
  if (!Number.isFinite(qty)) return 0;
  const clamped = Math.min(Math.max(qty, 0), MAX_QTY);
  return isWholeUnit(unit) ? Math.round(clamped) : Math.round(clamped * 1000) / 1000;
}

/** Adds a scanned/selected item; merges with an existing line for the same item. Returns a new array. */
export function addItem(cart: CartLine[], item: CatalogItem, barcode: string, qty = 1): CartLine[] {
  if (item.price == null) return cart;
  const idx = cart.findIndex((l) => l.itemId === item.id);
  if (idx >= 0) {
    return cart.map((l, i) =>
      i === idx ? { ...l, quantity: normalizeQty(l.unit, l.quantity + qty) } : l,
    );
  }
  return [
    ...cart,
    {
      itemId: item.id,
      barcode,
      name: item.name,
      unit: item.unit,
      price: item.price,
      quantity: normalizeQty(item.unit, qty) || 1,
    },
  ];
}

/** Sets quantity; a quantity of 0 removes the line. */
export function setQuantity(cart: CartLine[], itemId: string, qty: number): CartLine[] {
  return cart.flatMap((l) => {
    if (l.itemId !== itemId) return [l];
    const q = normalizeQty(l.unit, qty);
    return q > 0 ? [{ ...l, quantity: q }] : [];
  });
}

export const removeLine = (cart: CartLine[], itemId: string): CartLine[] =>
  cart.filter((l) => l.itemId !== itemId);

export const lineTotal = (l: CartLine): number => fromKop(Math.round(toKop(l.price) * l.quantity));

export const cartTotal = (cart: CartLine[]): number =>
  fromKop(cart.reduce((sum, l) => sum + Math.round(toKop(l.price) * l.quantity), 0));

export interface ChangeResult {
  enough: boolean;
  change: number;
  shortfall: number;
}

export function computeChange(total: number, tendered: number): ChangeResult {
  const diff = toKop(tendered) - toKop(total);
  return diff >= 0
    ? { enough: true, change: fromKop(diff), shortfall: 0 }
    : { enough: false, change: 0, shortfall: fromKop(-diff) };
}

/** Quick cash buttons: the exact amount plus round banknote amounts above it. */
export function quickCashAmounts(total: number): number[] {
  const out = new Set<number>([total]);
  for (const step of [50, 100, 200, 500, 1000]) {
    const up = Math.ceil(toKop(total) / (step * 100)) * step;
    if (up >= total) out.add(up);
  }
  return [...out].sort((a, b) => a - b).slice(0, 5);
}
