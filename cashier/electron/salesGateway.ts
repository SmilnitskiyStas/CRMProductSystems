import type { SaleReceipt, SaleSubmitRequest, SaleGatewayResult } from '@shared/types';
import { toKop, fromKop } from '@shared/money';
import { applyPromotions, type PromotionLine, type PromotionRule } from '@shared/promotions/engine';
import type { CatalogStore } from './catalogStore';
import { localReceiptNo, type OutboxRow, type OutboxService } from './outbox';

/**
 * Where completed sales go. The sale is always written to the local outbox first (the UI succeeds
 * immediately with a provisional local receipt number); a background worker delivers it to
 * POST /api/pos/sales/batch. See outbox.ts.
 */
export interface SalesGateway {
  submit(request: SaleSubmitRequest): Promise<SaleGatewayResult>;
}

export interface SaleItemWire {
  productName: string;
  barcode: string;
  quantity: number;
  unitPrice: number;
  discountAmount?: number;
  total: number;
  promotionRuleId?: string | null;
  promotionLabel?: string | null;
}
export interface SaleWire {
  loyaltyAccrued?: number | null;
  loyaltyRedeemed?: number | null;
  loyaltyBalance?: number | null;
  customerName?: string | null;
  transactionId: string;
  receiptNumber: string;
  subtotal: number;
  paymentType: string;
  paymentAmount: number;
  change: number;
  fiscalStatus: string;
  fiscalNumber: string | null;
  createdAt: string;
  items: SaleItemWire[];
}

/** Folds the server's view of the sale (final receipt no., fiscal data, discounts) into the local snapshot. */
export function mergeServerSale(row: OutboxRow, sale: unknown | null): SaleReceipt {
  const w = sale as SaleWire | null;
  if (!w?.receiptNumber) return row.receipt;
  return {
    ...row.receipt,
    transactionId: w.transactionId,
    receiptNumber: w.receiptNumber,
    subtotal: w.subtotal,
    fiscalStatus: w.fiscalStatus,
    fiscalNumber: w.fiscalNumber,
    createdAt: w.createdAt,
    local: false,
    customerName: w.customerName ?? row.receipt.customerName,
    loyaltyAccrued: w.loyaltyAccrued ?? undefined,
    loyaltyRedeemed: w.loyaltyRedeemed ?? undefined,
    loyaltyBalance: w.loyaltyBalance ?? undefined,
    // the server's lines are authoritative (promotions split a line into sub-lines)
    items: w.items.map((i) => ({
      name: i.productName,
      barcode: i.barcode,
      quantity: i.quantity,
      // SaleItemDto.UnitPrice is the REGULAR price; the net price per unit is Total / Quantity
      unitPrice: i.quantity > 0 ? Math.round((i.total / i.quantity) * 100) / 100 : i.unitPrice,
      total: i.total,
      promoLabel: i.promotionLabel ?? null,
      regularUnitPrice: i.unitPrice,
    })),
    // tendered amount stays as the cashier saw it (a replayed duplicate echoes the total); change follows the real total
    change: row.receipt.paymentType.toLowerCase() === 'card' ? 0 : Math.max(0, fromKop(toKop(row.receipt.paymentAmount) - toKop(w.subtotal))),
    // the server re-priced the sale: remember what the cashier saw first
    localTotal:
      Math.abs(w.subtotal - row.receipt.subtotal) >= 0.005 ? (row.receipt.localTotal ?? row.receipt.subtotal) : row.receipt.localTotal,
  };
}

export interface ReceiptContext {
  storeId: string;
  cashierId: string;
  registerNumber: number;
  registerName: string;
  storeName: string | null;
  cashierName: string;
}

export class OutboxSalesGateway implements SalesGateway {
  constructor(
    private readonly outbox: OutboxService,
    private readonly catalog: CatalogStore,
    private readonly context: () => ReceiptContext | null,
    private readonly now: () => number = Date.now,
    private readonly rules: () => readonly PromotionRule[] = () => [],
    private readonly storeTimeZone: () => string | null = () => null,
  ) {}

  async submit(request: SaleSubmitRequest): Promise<SaleGatewayResult> {
    const existing = this.outbox.getRow(request.clientTransactionId);
    if (existing) return { ok: true, sale: existing.receipt }; // renderer retry of the same sale

    const ctx = this.context();
    if (!ctx) return { ok: false, kind: 'unauthorized', message: 'Сесію завершено. Увійдіть за PIN-кодом знову.' };
    if (request.items.length === 0) return { ok: false, kind: 'failed', message: 'Кошик порожній.' };

    const member = request.loyaltyMember ?? null;
    // price through the promotion engine (server re-prices authoritatively at sync time)
    const resolved: { name: string; barcode: string; regular: number; line: PromotionLine }[] = [];
    for (const line of request.items) {
      const item = this.catalog.findByBarcode(line.barcode);
      if (!item || item.price == null) {
        return { ok: false, kind: 'failed', message: `Товар зі штрихкодом ${line.barcode} відсутній у локальному каталозі.` };
      }
      resolved.push({
        name: item.name,
        barcode: line.barcode,
        regular: item.price,
        line: { itemId: item.id, categoryId: item.categoryId ?? null, unitPrice: item.price, quantity: line.quantity },
      });
    }
    const segments = applyPromotions(resolved.map((r) => r.line), this.rules(), {
      storeId: ctx.storeId,
      at: this.now(),
      storeTimeZone: this.storeTimeZone(),
      membership: member
        ? { id: member.membershipId, tierId: member.tierId, tierPercent: member.tierDiscountPercent ?? 0 }
        : null,
    });
    let totalKop = 0;
    const items: SaleReceipt['items'] = [];
    resolved.forEach((r, i) => {
      for (const s of segments[i]) {
        const lineKop = Math.round(toKop(s.unitPrice) * s.quantity);
        totalKop += lineKop;
        items.push({
          name: r.name,
          barcode: r.barcode,
          quantity: s.quantity,
          unitPrice: s.unitPrice,
          total: fromKop(lineKop),
          promoLabel: s.label,
          regularUnitPrice: r.regular,
        });
      }
    });
    const total = fromKop(totalKop);
    const isCard = request.paymentType === 'Card';
    if (!isCard && toKop(request.paymentAmount) < totalKop) {
      return { ok: false, kind: 'failed', message: 'Отримана сума менша за суму чека.' };
    }

    const seq = this.outbox.nextSeq();
    const local = localReceiptNo(ctx.registerNumber, seq);
    const nowMs = this.now();
    const receipt: SaleReceipt = {
      transactionId: '',
      receiptNumber: local,
      localReceiptNo: local,
      local: true,
      clientTransactionId: request.clientTransactionId,
      subtotal: total,
      paymentType: request.paymentType,
      paymentAmount: isCard ? total : request.paymentAmount,
      change: isCard ? 0 : fromKop(toKop(request.paymentAmount) - totalKop),
      fiscalStatus: 'offline',
      fiscalNumber: null,
      createdAt: new Date(nowMs).toISOString(),
      items,
      storeName: ctx.storeName,
      registerNumber: ctx.registerNumber,
      registerName: ctx.registerName,
      cashierName: ctx.cashierName,
      customerName: member?.customerName ?? null,
      tierName: member?.tierName ?? null,
    };
    const row: OutboxRow = {
      clientTransactionId: request.clientTransactionId,
      payload: {
        shiftId: request.shiftId,
        items: request.items,
        paymentType: request.paymentType,
        paymentAmount: receipt.paymentAmount,
        clientTransactionId: request.clientTransactionId,
        cashierId: ctx.cashierId,
        occurredAt: receipt.createdAt,
        ...(member ? { loyaltyMembershipId: member.membershipId, ...(member.customerId ? { customerId: member.customerId } : {}) } : {}),
      },
      status: 'pending',
      attempts: 0,
      lastError: null,
      createdAt: nowMs,
      nextAttemptAt: 0,
      localReceiptNo: local,
      serverReceiptNo: null,
      receipt,
    };
    this.outbox.enqueue(row);
    return { ok: true, sale: receipt };
  }
}
