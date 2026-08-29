import MockAdapter from 'axios-mock-adapter';
import { apiClient } from '@/lib/api-client';
import { searchCatalogProducts } from '../api/marketplaceOrdersApi';

const mock = new MockAdapter(apiClient);

describe('marketplace order catalog search', () => {
  afterEach(() => mock.reset());

  test('unwraps the paged item response and maps the primary barcode', async () => {
    mock.onGet('/items').reply((config) => {
      expect(config.params).toEqual({ search: 'Pure Whey', page: 1, pageSize: 50 });
      return [200, {
        items: [{ id: 'item-1', name: '100% Pure Whey Strawberry 400 g', barcodes: ['5999076269549'] }],
        totalCount: 1,
        page: 1,
        pageSize: 50,
      }];
    });

    await expect(searchCatalogProducts('  Pure Whey  ')).resolves.toEqual([{
      id: 'item-1',
      name: '100% Pure Whey Strawberry 400 g',
      barcode: '5999076269549',
    }]);
  });
});
