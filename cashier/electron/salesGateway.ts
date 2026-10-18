import { classifySaleFailure } from '@shared/saleSubmission';
import type { SaleGatewayResult, SaleReceipt, SaleSubmitRequest } from '@shared/types';
import { httpRequest } from './http';

/**
 * Where completed sales go. Phase 3: straight to the API (online only). Phase 4 replaces this with
 * an outbox-backed gateway (write to SQLite first, sync in the background) - same interface.
 */
export interface SalesGateway {
  submit(request: SaleSubmitRequest): Promise<SaleGatewayResult>;
}

interface SaleItemWire {
  productName: string;
  barcode: string;
  quantity: number;
  unitPrice: number;
  total: number;
}
interface SaleWire {
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

export function mapSale(w: SaleWire): SaleReceipt {
  return {
    transactionId: w.transactionId,
    receiptNumber: w.receiptNumber,
    subtotal: w.subtotal,
    paymentType: w.paymentType,
    paymentAmount: w.paymentAmount,
    change: w.change,
    fiscalStatus: w.fiscalStatus,
    fiscalNumber: w.fiscalNumber,
    createdAt: w.createdAt,
    items: w.items.map((i) => ({
      name: i.productName,
      barcode: i.barcode,
      quantity: i.quantity,
      unitPrice: i.unitPrice,
      total: i.total,
    })),
  };
}

export interface HttpContext {
  baseUrl: string;
  operatorToken: string | null;
}

export class HttpSalesGateway implements SalesGateway {
  constructor(
    private readonly ctx: () => HttpContext,
    private readonly onUnauthorized: () => void,
  ) {}

  async submit(request: SaleSubmitRequest): Promise<SaleGatewayResult> {
    const { baseUrl, operatorToken } = this.ctx();
    if (!operatorToken) {
      return { ok: false, kind: 'unauthorized', message: classifySaleFailure(401).message };
    }
    const res = await httpRequest<SaleWire | { error?: string }>(baseUrl, '/api/pos/sales', {
      method: 'POST',
      headers: { Authorization: `Bearer ${operatorToken}` },
      timeoutMs: 20_000,
      body: {
        shiftId: request.shiftId,
        items: request.items,
        paymentType: request.paymentType,
        paymentAmount: request.paymentAmount,
        clientTransactionId: request.clientTransactionId,
      },
    });

    // 200 (replayed idempotent key) and 201 both return the stored sale.
    if (!res.networkError && (res.status === 200 || res.status === 201) && res.body) {
      return { ok: true, sale: mapSale(res.body as SaleWire) };
    }
    const f = classifySaleFailure(res.networkError ? undefined : res.status, (res.body as { error?: string } | null)?.error);
    if (f.kind === 'unauthorized') this.onUnauthorized();
    return { ok: false, kind: f.kind, message: f.message };
  }
}
