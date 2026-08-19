import MockAdapter from 'axios-mock-adapter';
import { personalApiClient } from '@/lib/api-client';
import { getAvailableNetworks, getLoyaltyCode, setPreferredStore } from '../loyaltyApi';

const mock = new MockAdapter(personalApiClient);

describe('getLoyaltyCode', () => {
  afterEach(() => mock.reset());

  it('requests the system default without a tenant for a new consumer', async () => {
    mock.onGet('/consumer/loyalty/code').reply((config) => {
      expect(config.params).toBeUndefined();
      return [200, { code: 'SGCUS1.consumer.123456', displayFormat: 'barcode', balance: 0, expiresInSeconds: 30 }];
    });

    await expect(getLoyaltyCode()).resolves.toMatchObject({ displayFormat: 'barcode' });
  });

  it('passes the selected network and preserves its display format', async () => {
    mock.onGet('/consumer/loyalty/code').reply((config) => {
      expect(config.params).toEqual({ tenantId: 'tenant-a' });
      return [200, { code: 'SGCUS1.consumer.654321', displayFormat: 'qr', balance: 0, expiresInSeconds: 30 }];
    });

    await expect(getLoyaltyCode('tenant-a')).resolves.toMatchObject({ displayFormat: 'qr' });
  });
});

describe('getAvailableNetworks', () => {
  afterEach(() => mock.reset());

  it('returns the consumer-safe network catalogue', async () => {
    mock.onGet('/consumer/loyalty/networks').reply(200, [
      {
        tenantId: 'tenant-a',
        tenantName: 'Мережа A',
        stores: [{ storeId: 'store-a', storeName: 'Центр', address: null }],
      },
    ]);

    await expect(getAvailableNetworks()).resolves.toEqual([
      {
        tenantId: 'tenant-a',
        tenantName: 'Мережа A',
        stores: [{ storeId: 'store-a', storeName: 'Центр', address: null }],
      },
    ]);
  });
});

describe('setPreferredStore', () => {
  afterEach(() => mock.reset());

  it('sends tenant and store identifiers to the preferred-store endpoint', async () => {
    const membership = {
      membershipId: 'membership-a',
      tenantId: 'tenant-a',
      tenantName: 'Мережа A',
      balance: 0,
      status: 'active',
      joinedAt: '2026-08-11T00:00:00Z',
      preferredStoreId: 'store-a',
      preferredStoreName: 'Центр',
      preferredStoreAddress: null,
    };
    mock.onPut('/consumer/loyalty/preferred-store', {
      tenantId: 'tenant-a',
      storeId: 'store-a',
    }).reply(200, membership);

    await expect(setPreferredStore('tenant-a', 'store-a')).resolves.toEqual(membership);
  });
});
