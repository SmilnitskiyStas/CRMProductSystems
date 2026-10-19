import type { SaleReceipt, SaleSubmitRequest, SaleGatewayResult } from '@shared/types';
import { toKop, fromKop } from '@shared/money';
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
}
export interface SaleWire {
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
    // tendered amount and change stay as the cashier saw them (a replayed duplicate echoes the total)
    items: w.items.map((i) => ({
      name: i.productName,
      barcode: i.barcode,
      quantity: i.quantity,
      unitPrice: i.unitPrice,
      total: i.total,
      discount: i.discountAmount ?? 0,
    })),
  };
}

export interface ReceiptContext {
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
    private readonly onQueued: (row: OutboxRow) => void = () => undefined,
  ) {}

  async submit(request: SaleSubmitRequest): Promise<SaleGatewayResult> {
    const existing = this.outbox.getRow(request.clientTransactionId);
    if (existing) return { ok: true, sale: existing.receipt }; // renderer retry of the same sale

    const ctx = this.context();
    if (!ctx) return { ok: false, kind: 'unauthorized', message: 'Сесію завершено. Увійдіть за PIN-кодом знову.' };
    if (request.items.length === 0) return { ok: false, kind: 'failed', message: 'Кошик порожній.' };

    let totalKop = 0;
    const items: SaleReceipt['items'] = [];
    for (const line of request.items) {
      const item = this.catalog.findByBarcode(line.barcode);
      if (!item || item.price == null) {
        return { ok: false, kind: 'failed', message: `Товар зі штрихкодом ${line.barcode} відсутній у локальному каталозі.` };
      }
      const lineKop = Math.round(toKop(item.price) * line.quantity);
      totalKop += lineKop;
      items.push({ name: item.name, barcode: line.barcode, quantity: line.quantity, unitPrice: item.price, total: fromKop(lineKop) });
    }
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
    this.onQueued(row);
    return { ok: true, sale: receipt };
  }
}
