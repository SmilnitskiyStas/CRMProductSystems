// Review-count pluralization used to live here as a hand-rolled Ukrainian-only
// helper (`reviewWord`). It now lives in messages/{uk,en}.json as the ICU
// `Dashboard.marketplace.reviewCount` plural message (one/few/many/other),
// resolved via `useTranslations("Dashboard.marketplace")` + `t("reviewCount",
// { count })` at each call site (i18n rollout Block 6, TASK-384).

// ─── Shipping ETA (TASK-584) ────────────────────────────────────────────────
// `MarketplaceOrderDto.shippedAt` / `estimatedDeliveryDays` are the only
// stored fields — the estimated delivery date and "days in transit" are
// derived here, client-side, so they can never drift out of sync with the
// two source fields (per the approved plan). Shared by the client-facing
// orders page (app/(dashboard)/marketplace/orders/page.tsx) and the supplier
// cabinet's own order view (features/supplier-cabinet/components/
// CabinetOrdersTab.tsx) so the date math only lives in one place.

const MS_PER_DAY = 24 * 60 * 60 * 1000;

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

export interface ShippingEta {
  /** Calendar days elapsed since shippedAt, clamped to >= 0 (clock-skew guard). */
  daysElapsed: number;
  /** shippedAt + estimatedDeliveryDays. */
  estimatedDeliveryDate: Date;
  /** True once daysElapsed has passed estimatedDeliveryDays. */
  isOverdue: boolean;
}

/** Null when the order hasn't shipped yet (either input missing/null). */
export function getShippingEta(
  shippedAt: string | null,
  estimatedDeliveryDays: number | null,
  now: Date = new Date()
): ShippingEta | null {
  if (!shippedAt || estimatedDeliveryDays == null) return null;

  const shippedDate = new Date(shippedAt);
  const daysElapsed = Math.max(
    0,
    Math.round((startOfDay(now).getTime() - startOfDay(shippedDate).getTime()) / MS_PER_DAY)
  );
  const estimatedDeliveryDate = new Date(
    shippedDate.getTime() + estimatedDeliveryDays * MS_PER_DAY
  );

  return {
    daysElapsed,
    estimatedDeliveryDate,
    isOverdue: daysElapsed > estimatedDeliveryDays,
  };
}
