// Pure threshold logic for the "promo item without sales" alert.
//
// PARITY — load-bearing: mirrors backend/ShelfGuard.Application/Features/Analytics/PromoNoSales/
// PromoNoSalesDetector.cs (PromoNoSalesThresholds + Evaluate). The API endpoint runs the C# path,
// this worker runs the TS path; change both together.

export const MIN_PROMO_DAYS = 3;
export const LOOKBACK_DAYS = 30;
export const MIN_STORE_STOCK = 1;
export const MAX_SALES_RATIO = 0.2;
export const MIN_EXPECTED_UNITS = 2;
export const NO_BASELINE_MIN_DAYS = 7;
/** Re-notify interval for a still-unresolved (store, product, promo). */
export const RENOTIFY_DAYS = 7;

export type PromoNoSalesReason = "no_sales" | "low_sales";

export type PromoNoSalesVerdict = {
  days: number;
  unitsSold: number;
  expectedUnits: number | null;
  reason: PromoNoSalesReason;
};

const DAY_MS = 86_400_000;

/** Whole-day difference between two UTC calendar dates given as `YYYY-MM-DD`. */
export function dayNumber(isoDate: string): number {
  return Math.floor(Date.parse(`${isoDate}T00:00:00Z`) / DAY_MS);
}

export function addDays(isoDate: string, days: number): string {
  return new Date((dayNumber(isoDate) + days) * DAY_MS).toISOString().slice(0, 10);
}

/**
 * @param today        UTC date `YYYY-MM-DD`
 * @param promoStart   `YYYY-MM-DD`
 * @param promoEnd     `YYYY-MM-DD` or null (open-ended)
 * @param salesByDate  DailySale quantities of this store + product keyed by `YYYY-MM-DD`
 */
export function evaluatePromoNoSales(
  today: string,
  promoStart: string,
  promoEnd: string | null,
  adu: number | null,
  storeStock: number,
  salesByDate: ReadonlyMap<string, number>
): PromoNoSalesVerdict | null {
  if (storeStock < MIN_STORE_STOCK) return null;

  const yesterday = addDays(today, -1);
  const end = promoEnd !== null && promoEnd < yesterday ? promoEnd : yesterday;
  const earliest = addDays(today, -LOOKBACK_DAYS);
  const start = promoStart < earliest ? earliest : promoStart;

  const days = dayNumber(end) - dayNumber(start) + 1;
  if (days < MIN_PROMO_DAYS) return null;

  let sold = 0;
  for (const [date, qty] of salesByDate) {
    if (date >= start && date <= end) sold += qty;
  }

  const expected = adu !== null && adu > 0 ? adu * days : null;
  let reason: PromoNoSalesReason;
  if (expected === null) {
    if (sold > 0 || days < NO_BASELINE_MIN_DAYS) return null;
    reason = "no_sales";
  } else {
    if (expected < MIN_EXPECTED_UNITS) return null;
    if (sold >= expected * MAX_SALES_RATIO) return null;
    reason = sold <= 0 ? "no_sales" : "low_sales";
  }

  return {
    days,
    unitsSold: sold,
    expectedUnits: expected === null ? null : Math.round(expected * 10) / 10,
    reason,
  };
}
