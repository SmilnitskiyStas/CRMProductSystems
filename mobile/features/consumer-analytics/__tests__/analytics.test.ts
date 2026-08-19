import { buildConsumerAnalyticsEvent, setConsumerAnalyticsTransport, trackConsumerEvent } from '../analytics';

describe('consumer analytics privacy boundary', () => {
  afterEach(() => setConsumerAnalyticsTransport(null));

  test('requires tenantId and keeps only event-specific allowlisted fields', () => {
    const event = buildConsumerAnalyticsEvent('product_opened', 'tenant-a', {
      productId: 'product-1',
      source: 'catalog',
      customerPhone: '+380000000000',
      balance: '999',
      qrCode: 'secret',
    } as never);
    expect(event).toEqual({
      name: 'product_opened', tenantId: 'tenant-a',
      properties: { productId: 'product-1', source: 'catalog' },
    });
    expect(buildConsumerAnalyticsEvent('loyalty_card_opened', '', {})).toBeNull();
  });

  test('rejects malformed identifiers instead of forwarding them', () => {
    expect(buildConsumerAnalyticsEvent('promotion_opened', 'tenant-a', { promotionId: 'https://evil' })).toBeNull();
  });

  test('uses an injectable transport while default remains no-op', async () => {
    const capture = jest.fn();
    setConsumerAnalyticsTransport({ capture });
    await trackConsumerEvent('retailer_joined', 'tenant-a', { source: 'qr' });
    expect(capture).toHaveBeenCalledWith({
      name: 'retailer_joined', tenantId: 'tenant-a', properties: { source: 'qr' },
    });
  });
});
