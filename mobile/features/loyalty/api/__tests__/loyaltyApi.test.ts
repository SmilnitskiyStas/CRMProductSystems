import MockAdapter from 'axios-mock-adapter';
import { personalApiClient } from '@/lib/api-client';
import {
  getAvailableNetworks,
  getLoyaltyCode,
  getPublicRetailer,
  joinRetailerBySlug,
  setPreferredStore,
} from '../loyaltyApi';

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
        slug: 'merezha-a',
        stores: [{ storeId: 'store-a', storeName: 'Центр', address: null }],
      },
    ]);

    await expect(getAvailableNetworks()).resolves.toEqual([
      {
        tenantId: 'tenant-a',
        tenantName: 'Мережа A',
        slug: 'merezha-a',
        stores: [{ storeId: 'store-a', storeName: 'Центр', address: null }],
      },
    ]);
  });
});

describe('slug-addressed retailer onboarding', () => {
  afterEach(() => mock.reset());

  it('resolves only public retailer information before join', async () => {
    const retailer = { name: 'Свіжий Кут', slug: 'svizhyi-kut', logoUrl: null, joinable: true };
    mock.onGet('/v1/retailers/svizhyi-kut/public').reply(200, retailer);
    await expect(getPublicRetailer('svizhyi-kut')).resolves.toEqual(retailer);
  });

  it('joins using the authenticated slug endpoint', async () => {
    const membership = { tenantId: 'tenant-a' };
    mock.onPost('/v1/retailers/svizhyi-kut/join').reply(200, membership);
    await expect(joinRetailerBySlug('svizhyi-kut')).resolves.toEqual(membership);
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
