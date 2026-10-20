import { describe, expect, it } from 'vitest';
import { addItem, priceCart } from './cart';
import type { PromotionRule } from './promotions/engine';
import type { CatalogItem } from './types';

const item: CatalogItem = { id: 'a', name: 'Bread', barcodes: ['1'], price: 100, vatRate: 20, unit: 'pcs', categoryId: 'c1' };
const rule = (scope: Record<string, unknown>): PromotionRule => ({
  id: '00000000-0000-0000-0000-000000000101',
  versionId: '00000000-0000-0000-0000-000000000201',
  name: 'Happy hour',
  createdAt: '2026-01-01T00:00:00Z',
  definition: { type: 'percent_off', targets: { itemIds: ['a'] }, scope, params: { percent: 10 } },
});
const cart = addItem([], item, '1');
const happyHour = rule({ timeFrom: '18:00', timeTo: '20:00' });
const total = (at: string, zone: string | null, r = happyHour) =>
  priceCart(cart, [r], { storeId: 's1', at, storeTimeZone: zone }).total;

describe('store-local time scope (IANA zone, DST aware)', () => {
  it('summer: 15:30 UTC is 18:30 in Kyiv (UTC+3) -> inside an 18:00-20:00 rule; a UTC-naive evaluation is outside', () => {
    expect(total('2026-07-15T15:30:00Z', 'Europe/Kyiv')).toBe(90);
    expect(total('2026-07-15T15:30:00Z', null)).toBe(100); // fallback = UTC, 15:30 is outside
  });

  it('winter (Kyiv = UTC+2): 16:30 UTC = 18:30 local is inside, 15:30 UTC = 17:30 local is outside', () => {
    expect(total('2026-01-15T16:30:00Z', 'Europe/Kyiv')).toBe(90);
    expect(total('2026-01-15T15:30:00Z', 'Europe/Kyiv')).toBe(100);
  });

  it('weekday follows the local day, and a midnight-crossing window belongs to the day it starts on', () => {
    const monNight = rule({ weekdays: [1], timeFrom: '22:00', timeTo: '02:00' });
    // Mon 2026-10-19 23:00 Kyiv (UTC+3) = 20:00 UTC -> inside
    expect(total('2026-10-19T20:00:00Z', 'Europe/Kyiv', monNight)).toBe(90);
    // Tue 2026-10-20 01:00 Kyiv = Mon 22:00 UTC -> still Monday's window
    expect(total('2026-10-19T22:00:00Z', 'Europe/Kyiv', monNight)).toBe(90);
    // Tue 2026-10-20 23:00 Kyiv -> Tuesday's window, not Monday's
    expect(total('2026-10-20T20:00:00Z', 'Europe/Kyiv', monNight)).toBe(100);
  });

  it('an unknown zone falls back to UTC (+ the legacy offset), like the server', () => {
    expect(total('2026-07-15T18:30:00Z', 'Mars/Olympus')).toBe(90);
    expect(total('2026-07-15T15:30:00Z', 'Mars/Olympus')).toBe(100);
    expect(
      priceCart(cart, [happyHour], { storeId: 's1', at: '2026-07-15T15:30:00Z', storeTimeZone: 'Mars/Olympus', utcOffsetMinutes: 180 }).total,
    ).toBe(90);
  });
});
