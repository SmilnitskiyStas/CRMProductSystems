import MockAdapter from 'axios-mock-adapter';
import { personalApiClient } from '@/lib/api-client';
import { getConsumerCatalog, getConsumerPromotions } from '../api';

const mock = new MockAdapter(personalApiClient);
const context = { tenantId: 'tenant-a', storeId: 'store-a' };

describe('tenant-scoped consumer content API', () => {
  afterEach(() => mock.reset());

  test('scopes promotions to the selected tenant and store', async () => {
    mock.onGet('/consumer/tenant-a/promotions').reply((config) => {
      expect(config.params).toEqual({ storeId: 'store-a' });
      return [200, []];
    });
    await expect(getConsumerPromotions(context)).resolves.toEqual([]);
  });

  test('scopes catalog category and pagination to the same context', async () => {
    mock.onGet('/consumer/tenant-a/catalog').reply((config) => {
      expect(config.params).toEqual({
        storeId: 'store-a',
        categoryId: 'category-a',
        page: 2,
        pageSize: 20,
      });
      return [200, { items: [], totalCount: 0, page: 2, pageSize: 20, totalPages: 0 }];
    });
    await expect(
      getConsumerCatalog(context, { categoryId: 'category-a', page: 2, pageSize: 20 })
    ).resolves.toMatchObject({ page: 2 });
  });
});
