import MockAdapter from 'axios-mock-adapter';
import { apiClient } from '@/lib/api-client';
import { createWriteOff, getWriteOff } from '../api/writeOffApi';

const mock = new MockAdapter(apiClient);
const wire = {
  id: 'write-off-1', storeId: 'store-1', storeName: 'Центр', status: 'pending_approval',
  reason: 'damaged', totalLossAmount: 100, totalLossAmountPurchase: 70,
  totalReimbursementAmount: 20, netLossAmount: 50, pdfUrl: null,
  createdAt: '2026-08-23T10:00:00Z', approvedAt: null, items: [],
};

describe('writeOffApi', () => {
  afterEach(() => mock.reset());

  test('maps backend store fields to the mobile location model', async () => {
    mock.onGet('/write-offs/write-off-1').reply(200, wire);
    await expect(getWriteOff('write-off-1')).resolves.toEqual(expect.objectContaining({
      locationId: 'store-1', locationName: 'Центр', netLossAmount: 50,
    }));
  });

  test('sends reimbursement data and renames locationId to storeId', async () => {
    mock.onPost('/write-offs').reply((config) => {
      const body = JSON.parse(config.data as string);
      expect(body.locationId).toBeUndefined();
      expect(body.storeId).toBe('store-1');
      expect(body.items[0]).toEqual(expect.objectContaining({
        unitPrice: 30,
        isReturnedToSupplier: true,
        reimbursementType: 'percent',
        reimbursementValue: 25,
      }));
      expect(config.headers?.['Idempotency-Key']).toBe('operation-1');
      return [201, wire];
    });

    await createWriteOff({
      locationId: 'store-1', reason: 'damaged', items: [{
        productId: 'product-1', quantity: 2, unitPrice: 30,
        isReturnedToSupplier: true, reimbursementType: 'percent', reimbursementValue: 25,
      }],
    }, 'operation-1');
  });
});
