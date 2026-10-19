import type { CatalogItem } from './types';
import { fromKop, toKop } from './money';
import { applyPromotions, type PromotionContext, type PromotionRule, type PromotionSegment } from './promotions/engine';

export interface CartLine {
  itemId: string;
  barcode: string;
  name: string;
  unit: string;
  price: number;
  quantity: number;
  /** For category-wide promotions. */
  categoryId?: string | null;
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
      categoryId: item.categoryId ?? null,
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

// ── promotions ───────────────────────────────────────────────────────────────

export interface PricedLine {
  itemId: string;
  /** Price segments after promotions, e.g. 2 x 42.73 + 1 x 0.01. */
  segments: PromotionSegment[];
  total: number;
  regularTotal: number;
  /** Distinct rule labels applied to this line. */
  labels: string[];
}

export interface PricedCart {
  lines: PricedLine[];
  total: number;
  regularTotal: number;
  /** regularTotal - total (>= 0). */
  savings: number;
}

const segKop = (s: PromotionSegment): number => Math.round(toKop(s.unitPrice) * s.quantity);

/** Prices the cart through the same promotion engine the server uses (server stays authoritative). */
export function priceCart(cart: CartLine[], rules: readonly PromotionRule[], ctx: PromotionContext): PricedCart {
  const segs = applyPromotions(
    cart.map((l) => ({ itemId: l.itemId, categoryId: l.categoryId ?? null, unitPrice: l.price, quantity: l.quantity })),
    rules,
    ctx,
  );
  let totalKop = 0;
  let regularKop = 0;
  const lines = cart.map((l, i) => {
    const lineKop = segs[i].reduce((a, s) => a + segKop(s), 0);
    const regKop = Math.round(toKop(l.price) * l.quantity);
    totalKop += lineKop;
    regularKop += regKop;
    return {
      itemId: l.itemId,
      segments: segs[i],
      total: fromKop(lineKop),
      regularTotal: fromKop(regKop),
      labels: [...new Set(segs[i].flatMap((s) => (s.label ? [s.label] : [])))],
    };
  });
  return {
    lines,
    total: fromKop(totalKop),
    regularTotal: fromKop(regularKop),
    savings: fromKop(Math.max(0, regularKop - totalKop)),
  };
}
