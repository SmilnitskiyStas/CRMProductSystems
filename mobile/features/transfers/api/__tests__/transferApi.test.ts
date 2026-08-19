import MockAdapter from 'axios-mock-adapter';
import { apiClient } from '@/lib/api-client';
import { createTransfer } from '../transferApi';

it('sends selected stock ids and quantities without client-side FEFO allocation', async () => {
  const mock = new MockAdapter(apiClient);
  mock.onPost('/transfers').reply((config) => {
    const body = JSON.parse(config.data as string);
    expect(body).toEqual({
      fromLocationId: 'from',
      toLocationId: 'to',
      transferType: 'store_to_store',
      notes: 'careful',
      items: [{ productStockId: 'server-stock-id', quantity: 3 }],
    });
    expect(body).not.toHaveProperty('fefo');
    expect(body.items[0]).not.toHaveProperty('expiryDate');
    return [200, { id: 'transfer-1' }];
  });

  await createTransfer({
    fromLocationId: 'from',
    toLocationId: 'to',
    transferType: 'store_to_store',
    notes: 'careful',
    items: [{ productStockId: 'server-stock-id', quantity: 3 }],
  });
  expect(mock.history.post).toHaveLength(1);
});
