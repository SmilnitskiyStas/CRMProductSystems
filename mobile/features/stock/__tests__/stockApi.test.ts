import MockAdapter from 'axios-mock-adapter';
import { apiClient } from '@/lib/api-client';
import { createStockBatch, getProductByBarcode, getStock, getStockBatch } from '../api/stockApi';

const mock = new MockAdapter(apiClient);

const wireBatch = {
  id: 'stock-1',
  productId: 'product-1',
  productName: 'Молоко',
  productBarcode: '4820000000001',
  storeId: 'store-1',
  storeName: 'Центр',
  zoneId: null,
  zoneName: null,
  shelfNumber: 4,
  batchNumber: 'B-1',
  quantity: 12.5,
  quantityInitial: 20,
  expiryDate: '2026-09-01',
  daysLeft: 10,
  status: 'safe',
  sourceType: 'receipt',
  addedAt: '2026-08-20T10:00:00Z',
  lastCheckedAt: '2026-08-21T10:00:00Z',
};

describe('stockApi', () => {
  afterEach(() => mock.reset());

  test('unwraps the paged backend response and maps backend field names', async () => {
    mock.onGet('/stock').reply((config) => {
      expect(config.params).toEqual({
        status: 'safe',
        store_id: 'store-1',
        zone_id: undefined,
      });
      return [200, {
        items: [wireBatch], totalCount: 1, page: 1, pageSize: 50, totalPages: 1,
      }];
    });

    await expect(getStock({ status: 'safe', locationId: 'store-1' })).resolves.toEqual([
      expect.objectContaining({
        id: 'stock-1',
        barcode: '4820000000001',
        locationId: 'store-1',
        shelfNumber: 4,
      }),
    ]);
  });

  test('maps a stock detail response through the same boundary', async () => {
    mock.onGet('/stock/stock-1').reply(200, wireBatch);
    await expect(getStockBatch('stock-1')).resolves.toEqual(
      expect.objectContaining({ productName: 'Молоко', barcode: '4820000000001' })
    );
  });

  test('renames locationId to the backend storeId when creating stock', async () => {
    mock.onPost('/stock').reply((config) => {
      const body = JSON.parse(config.data as string);
      expect(body.locationId).toBeUndefined();
      expect(body.storeId).toBe('store-1');
      return [201, wireBatch];
    });

    await expect(createStockBatch({
      productId: 'product-1',
      quantity: 12.5,
      expiryDate: '2026-09-01',
      locationId: 'store-1',
    })).resolves.toEqual(expect.objectContaining({ id: 'stock-1' }));
  });

  test('normalizes a formatted scanned barcode before exact lookup', async () => {
    mock.onGet('/items/by-barcode/5999076269549').reply(200, {
      id: 'product-1', name: '100% Pure Whey Strawberry 400 g', barcodes: ['5999076269549'],
    });

    await expect(getProductByBarcode(' 5999-0762-69549\n')).resolves.toEqual(
      expect.objectContaining({ id: 'product-1' })
    );
  });
});
