import {
  resetSubmissionLockForTests,
  submitSaleSingleFlight,
  type SubmissionTransition,
} from '../saleSubmission';
import type { SaleRequest, SaleResponse } from '../types';

const request: SaleRequest = {
  shiftId: 'shift',
  items: [{ barcode: '123', quantity: 1 }],
  paymentType: 'Card',
  paymentAmount: 10,
};
const response: SaleResponse = {
  transactionId: 'tx',
  items: [],
  subtotal: 10,
  paymentType: 'Card',
  paymentAmount: 10,
  change: 0,
  fiscalStatus: 'fiscalized',
};

describe('safe POS sale submission', () => {
  beforeEach(resetSubmissionLockForTests);

  it('single-flights concurrent double taps and completes once', async () => {
    let resolve!: (value: SaleResponse) => void;
    const submit = jest.fn(
      () => new Promise<SaleResponse>((done) => (resolve = done))
    );
    const transitions: SubmissionTransition[] = [];
    const transition = async (state: SubmissionTransition) => {
      transitions.push(state);
    };

    const first = submitSaleSingleFlight(request, submit, transition);
    const second = submitSaleSingleFlight(request, submit, transition);
    await Promise.resolve();
    resolve(response);

    await expect(Promise.all([first, second])).resolves.toEqual([response, response]);
    expect(submit).toHaveBeenCalledTimes(1);
    expect(transitions).toEqual([
      { status: 'pending' },
      { status: 'completed', transactionId: 'tx' },
    ]);
  });

  it.each([
    [
      '409',
      { response: { status: 409, data: { error: 'Stock changed' } } },
      'conflict',
    ],
    ['timeout', { code: 'ECONNABORTED' }, 'uncertain'],
    ['network loss', {}, 'uncertain'],
    ['deterministic 400', { response: { status: 400 } }, 'failed'],
  ])('preserves the draft as %s', async (_label, error, expected) => {
    const transitions: SubmissionTransition[] = [];
    const result = await submitSaleSingleFlight(
      request,
      jest.fn().mockRejectedValue(error),
      async (state) => {
        transitions.push(state);
      }
    );

    expect(result).toBeNull();
    expect(transitions.at(-1)?.status).toBe(expected);
  });
});
