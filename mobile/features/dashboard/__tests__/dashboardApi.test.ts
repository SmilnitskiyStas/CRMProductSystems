import MockAdapter from 'axios-mock-adapter';
import { apiClient } from '@/lib/api-client';
import { getRecentMovements } from '../api/dashboardApi';

const mock = new MockAdapter(apiClient);

describe('dashboard movement API', () => {
  afterEach(() => mock.reset());

  test('filters by the active store and maps backend store fields to mobile locations', async () => {
    mock.onGet('/movements').reply((config) => {
      expect(config.params).toEqual({ page: 1, page_size: 20, store_id: 'store-1' });
      return [200, {
        items: [{
          id: 'movement-1', movementType: 'transfer', productId: 'product-1', productName: 'Молоко',
          fromStoreId: 'store-1', fromStoreName: 'Центр', toStoreId: 'store-2', toStoreName: 'Північ',
          quantity: 4, quantityBefore: 10, quantityAfter: 6, unitPrice: 25, totalAmount: 100,
          referenceId: 'transfer-1', referenceType: 'transfer', notes: 'Терміново', createdAt: '2026-08-22T10:00:00Z',
        }],
        total: 1, page: 1, pageSize: 20,
      }];
    });

    await expect(getRecentMovements(20, 'store-1')).resolves.toEqual(expect.objectContaining({
      items: [expect.objectContaining({
        fromLocationId: 'store-1',
        fromLocationName: 'Центр',
        toLocationId: 'store-2',
        toLocationName: 'Північ',
        quantityBefore: 10,
        quantityAfter: 6,
      })],
    }));
  });
});
