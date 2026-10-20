import { describe, expect, it, vi } from 'vitest';
import { classifySaleFailure, SaleSubmitter } from './saleSubmission';
import type { SaleGatewayResult, SaleReceipt, SaleSubmitRequest } from './types';

const draft = { shiftId: 's', items: [{ barcode: '1', quantity: 1 }], paymentType: 'Cash' as const, paymentAmount: 50 };
const receipt = { transactionId: 't1', receiptNumber: 'R1' } as SaleReceipt;

describe('classifySaleFailure', () => {
  it('maps statuses', () => {
    expect(classifySaleFailure(undefined).kind).toBe('uncertain');
    expect(classifySaleFailure(409, 'x')).toEqual({ kind: 'conflict', message: 'x' });
    expect(classifySaleFailure(423).kind).toBe('locked');
    expect(classifySaleFailure(401).kind).toBe('unauthorized');
    expect(classifySaleFailure(503).kind).toBe('uncertain');
    expect(classifySaleFailure(400, 'bad').kind).toBe('failed');
  });
});

describe('SaleSubmitter', () => {
  it('generates one id and completes', async () => {
    const gateway = vi.fn(async (_r: SaleSubmitRequest): Promise<SaleGatewayResult> => ({ ok: true, sale: receipt }));
    const s = new SaleSubmitter(gateway, () => 'id-1');
    const st = await s.submit(draft);
    expect(st.status).toBe('completed');
    expect(gateway).toHaveBeenCalledWith({ ...draft, clientTransactionId: 'id-1' });
  });

  it('is single-flight: concurrent submits send one request', async () => {
    let resolve!: (r: SaleGatewayResult) => void;
    const gateway = vi.fn(() => new Promise<SaleGatewayResult>((r) => (resolve = r)));
    const s = new SaleSubmitter(gateway, () => 'id-1');
    const a = s.submit(draft);
    const b = s.submit(draft);
    expect(s.state.status).toBe('pending');
    resolve({ ok: true, sale: receipt });
    await Promise.all([a, b]);
    expect(gateway).toHaveBeenCalledTimes(1);
  });

  it('reuses the same ClientTransactionId when retrying after uncertain', async () => {
    const results: SaleGatewayResult[] = [
      { ok: false, kind: 'uncertain', message: 'timeout' },
      { ok: true, sale: receipt },
    ];
    const gateway = vi.fn(async (_r: SaleSubmitRequest) => results.shift()!);
    let n = 0;
    const s = new SaleSubmitter(gateway, () => `id-${++n}`);
    expect((await s.submit(draft)).status).toBe('uncertain');
    expect((await s.submit(draft)).status).toBe('completed');
    expect(gateway.mock.calls.map((c) => c[0].clientTransactionId)).toEqual([
      'id-1',
      'id-1',
    ]);
  });

  it('gets a fresh id after reset', async () => {
    const gateway = vi.fn(async (_r: SaleSubmitRequest): Promise<SaleGatewayResult> => ({ ok: false, kind: 'conflict', message: 'c' }));
    let n = 0;
    const s = new SaleSubmitter(gateway, () => `id-${++n}`);
    await s.submit(draft);
    s.reset();
    await s.submit(draft);
    expect(gateway.mock.calls.map((c) => c[0].clientTransactionId)).toEqual([
      'id-1',
      'id-2',
    ]);
  });

  it('treats a thrown gateway error as uncertain', async () => {
    const s = new SaleSubmitter(async () => {
      throw new Error('ipc');
    }, () => 'id');
    expect((await s.submit(draft)).status).toBe('uncertain');
  });

  it('does not resubmit a completed sale', async () => {
    const gateway = vi.fn(async (_r: SaleSubmitRequest): Promise<SaleGatewayResult> => ({ ok: true, sale: receipt }));
    const s = new SaleSubmitter(gateway, () => 'id');
    await s.submit(draft);
    await s.submit(draft);
    expect(gateway).toHaveBeenCalledTimes(1);
  });
});
