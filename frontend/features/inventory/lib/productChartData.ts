import type { MovementDto, StockHistoryDto } from "../api/movements";

export interface ChartPoint {
  date: string;
  stock: number | null;
  receipt: number;
  transfer: number;
  write_off: number;
  adjustment: number;
  /** Sales and optional financial series are merged separately by the chart component. */
  revenue?: number;
  quantitySold?: number;
  /** Units sold that day (POS sales trend) — drawn as the "sold" line on the quantity axis. */
  sold?: number;
  /** null = a real sale happened that day but margin is unknown (no Item.PricePurchase on file,
   * or the caller can't view margin) — distinct from 0 (a sale day with exactly zero margin) and
   * from "no key at all" (not a showRevenueSeries render). Rendered as a connectNulls gap, not
   * coerced to 0 — unlike revenue/quantitySold, where "no sales that day" legitimately is 0. */
  margin?: number | null;
}

export function buildProductChartData(
  movements: MovementDto[],
  from: string,
  until: string,
  history: StockHistoryDto["points"],
): ChartPoint[] {
  const balances = new Map(history.map((point) => [point.date, point.stock]));
  const points = new Map<string, ChartPoint>();
  const end = new Date(`${until}T00:00:00Z`);
  for (const day = new Date(`${from}T00:00:00Z`); day <= end; day.setUTCDate(day.getUTCDate() + 1)) {
    const date = day.toISOString().slice(0, 10);
    points.set(date, { date, stock: balances.get(date) ?? null, receipt: 0, transfer: 0, write_off: 0, adjustment: 0 });
  }
  for (const movement of movements) {
    const point = points.get(movement.createdAt.slice(0, 10));
    if (point) point[movement.movementType] += Math.abs(movement.quantity);
  }
  // Missing history (including future delivery dates) stays unknown, rather than displaying
  // a flat balance copied from a past receipt. POS quantities are already included by the API.
  return Array.from(points.values());
}
