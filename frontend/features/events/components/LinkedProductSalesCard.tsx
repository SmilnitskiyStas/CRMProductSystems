"use client";

import { useMemo } from "react";
import { AreaChart, Area, Line, XAxis, Tooltip, ResponsiveContainer } from "recharts";
import { useLocale, useTranslations } from "next-intl";
import { TrendIndicator } from "@/components/ui/TrendIndicator";
import { useProductSalesTrendCompare } from "@/features/analytics/hooks/usePosAnalytics";
import type { DemandEvent } from "../types";
import { resolveEventWindowForYear } from "../utils";

interface Props {
  productId: string;
  productName: string;
  referenceDateIso: string;
  event: DemandEvent;
  storeId?: string;
}

const MS_PER_DAY = 86_400_000;

function daysBetween(fromStr: string, dateStr: string): number {
  return Math.round(
    (new Date(`${dateStr}T00:00:00Z`).getTime() - new Date(`${fromStr}T00:00:00Z`).getTime()) / MS_PER_DAY,
  );
}

/**
 * One compact card per event-linked product inside EventDetailPanel's "Sales Comparison"
 * section — its own component instance (not inlined in a `.map()`) specifically so it can
 * call useProductSalesTrendCompare per-product without violating the rules of hooks.
 *
 * Window comes from resolveEventWindowForYear(event, referenceDateIso) — critical for a
 * recurring holiday, whose stored startsAt/endsAt carry a stale year. compareFrom/compareTo
 * are intentionally omitted from the query so the server applies its own auto-baseline (an
 * equal-length period immediately preceding from/to).
 */
export function LinkedProductSalesCard({ productId, productName, referenceDateIso, event, storeId }: Props) {
  const t = useTranslations("Dashboard.events.dayDetail");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { from, to } = useMemo(
    () => resolveEventWindowForYear(event, referenceDateIso),
    [event, referenceDateIso],
  );

  const { data, isLoading, isError } = useProductSalesTrendCompare(productId, { from, to, store_id: storeId });

  const chartData = useMemo(() => {
    if (!data) return [];
    // Points are sparse (no entry for zero-activity days) and the two series don't share
    // array length/order — align by day offset from each series' own start date, same
    // technique as PosRevenueTrendChart.tsx's daysBetween/byOffset.
    const byOffset = new Map<number, { revenue?: number; comparisonRevenue?: number }>();
    for (const p of data.current) {
      const offset = daysBetween(data.from, p.date);
      byOffset.set(offset, { ...byOffset.get(offset), revenue: p.revenue });
    }
    for (const p of data.comparison) {
      const offset = daysBetween(data.compareFrom, p.date);
      byOffset.set(offset, { ...byOffset.get(offset), comparisonRevenue: p.revenue });
    }
    return Array.from(byOffset.entries())
      .sort(([a], [b]) => a - b)
      .map(([offset, v]) => ({ x: offset + 1, revenue: v.revenue, comparisonRevenue: v.comparisonRevenue }));
  }, [data]);

  const hasAnySales = !!data && (data.currentTotalRevenue > 0 || data.comparisonTotalRevenue > 0);
  // SVG gradient ids must be unique per document — several cards can render at once.
  const gradientId = `eventProductRevenue-${productId}`;

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: "10px 12px" }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8, marginBottom: 6 }}>
        <span
          style={{
            color: "#E8EDF5",
            fontSize: 12,
            fontWeight: 600,
            overflow: "hidden",
            textOverflow: "ellipsis",
            whiteSpace: "nowrap",
          }}
        >
          {productName}
        </span>
        {data && (
          <TrendIndicator
            current={data.currentTotalRevenue}
            previous={data.comparisonTotalRevenue}
            format="currency"
            size="sm"
          />
        )}
      </div>

      {isLoading && <p style={{ color: "#4B5563", fontSize: 11, margin: 0 }}>{t("salesComparisonLoading")}</p>}
      {isError && <p style={{ color: "#F87171", fontSize: 11, margin: 0 }}>{t("salesComparisonError")}</p>}
      {!isLoading && !isError && data && !hasAnySales && (
        <p style={{ color: "#4B5563", fontSize: 11, margin: 0 }}>{t("salesComparisonNoSales")}</p>
      )}

      {!isLoading && !isError && data && hasAnySales && (
        <ResponsiveContainer width="100%" height={90}>
          <AreaChart data={chartData} margin={{ left: 0, right: 4, top: 4, bottom: 0 }}>
            <defs>
              <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor="#3B82F6" stopOpacity={0.3} />
                <stop offset="95%" stopColor="#3B82F6" stopOpacity={0.02} />
              </linearGradient>
            </defs>
            <XAxis dataKey="x" hide />
            <Tooltip
              contentStyle={{
                background: "#111827",
                border: "1px solid #1F2937",
                borderRadius: 8,
                color: "#E8EDF5",
                fontSize: 11,
              }}
              formatter={(val, name) => {
                const v = Number(val);
                const label = name === "comparisonRevenue" ? t("comparisonSeriesLabel") : t("currentSeriesLabel");
                return [`${v.toLocaleString(intlLocale)} ₴`, label];
              }}
            />
            <Area
              type="monotone"
              dataKey="revenue"
              stroke="#3B82F6"
              strokeWidth={1.5}
              fill={`url(#${gradientId})`}
              dot={false}
              connectNulls
            />
            <Line
              type="monotone"
              dataKey="comparisonRevenue"
              stroke="#A78BFA"
              strokeWidth={1.5}
              strokeDasharray="4 3"
              dot={false}
              connectNulls
            />
          </AreaChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
