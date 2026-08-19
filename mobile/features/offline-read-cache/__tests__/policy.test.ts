import {
  getOfflineReadFamily,
  sanitizeOfflineReadData,
  OFFLINE_READ_EXPLICIT_EXCLUSIONS,
} from '../policy';

describe('offline read allowlist policy', () => {
  test('allows only the three explicitly approved summary families', () => {
    expect(getOfflineReadFamily(['schedules', 'loc', '2026-08-01'])).toBe('schedules');
    expect(getOfflineReadFamily(['marketplace-suppliers', { page: 1 }])).toBe('marketplace-suppliers');
    expect(getOfflineReadFamily(['production-recipes', false])).toBe('production-recipes');
    for (const family of OFFLINE_READ_EXPLICIT_EXCLUSIONS) {
      expect(getOfflineReadFamily([family])).toBeNull();
    }
    expect(getOfflineReadFamily(['schedules', () => undefined])).toBeNull();
    expect(getOfflineReadFamily(['schedules'])).toBeNull();
    expect(getOfflineReadFamily(['schedules', 'location', 'week', 'detail-id'])).toBeNull();
    expect(getOfflineReadFamily(['marketplace-suppliers', { page: 1, search: 'private query' }])).toBeNull();
    expect(getOfflineReadFamily(['marketplace-suppliers', 'supplier-detail-id'])).toBeNull();
    expect(getOfflineReadFamily(['production-recipes', false, 'recipe-detail-id'])).toBeNull();
  });

  test('serializers drop unexpected sensitive and detail fields', () => {
    expect(sanitizeOfflineReadData('schedules', [{
      id: 's1', locationId: 'l1', locationName: 'Store', name: 'Week', weekStart: '2026-08-01',
      status: 'published', shiftCount: 2, createdAt: 'now', shifts: [{ userName: 'Private' }],
      accessToken: 'secret',
    }])).toEqual([{ id: 's1', locationId: 'l1', locationName: 'Store', name: 'Week',
      weekStart: '2026-08-01', status: 'published', shiftCount: 2, createdAt: 'now' }]);

    const marketplace = sanitizeOfflineReadData('marketplace-suppliers', {
      items: [{ id: 'p1', name: 'Supplier', region: 'Kyiv', plan: 'free', categories: ['food'],
        rating: 5, avgDeliveryDays: 2, isPublic: true, phone: '+380', paymentToken: 'secret' }],
      total: 1, page: 1, pageSize: 20, auth: 'secret',
    });
    expect(JSON.stringify(marketplace)).not.toMatch(/phone|paymentToken|auth|secret/);
  });
});
