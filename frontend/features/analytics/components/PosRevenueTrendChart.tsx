"use client";

import {
  AreaChart,
  Area,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
  ResponsiveContainer,
  CartesianGrid,
} from "recharts";
import { useTranslations, useLocale } from "next-intl";
import type { PosRevenueTrendDto, PosRevenueTrendPoint } from "../types";

interface ComparisonSeries {
  points: PosRevenueTrendPoint[];
  /** Start date (yyyy-mm-dd) of the comparison range — used to align points with the current series by offset, not array index. */
  from: string;
}

interface Props {
  data: PosRevenueTrendDto;
  /** Start date (yyyy-mm-dd) of the current range — required to align with `comparison` by offset. */
  from?: string;
  comparison?: ComparisonSeries;
  /** Called with a point's `date` (yyyy-mm-dd) when the user clicks near it on the chart. Only
   * fires for current-period points — an offset that only has comparison-period data (sparse
   * series) has no `date` and is silently ignored. */
  onDayClick?: (date: string) => void;
  /** Currently selected day, if any — hides the "click to see details" hint once set (mirrors
   * SegmentGrid's `selectedKey` prop/hint pattern). */
  selectedDay?: string | null;
}

const MS_PER_DAY = 86_400_000;

function daysBetween(fromStr: string, dateStr: string): number {
  return Math.round((new Date(`${dateStr}T00:00:00Z`).getTime() - new Date(`${fromStr}T00:00:00Z`).getTime()) / MS_PER_DAY);
}

function formatDate(dateStr: string, intlLocale: string): string {
  return new Date(`${dateStr}T00:00:00Z`).toLocaleDateString(intlLocale, { day: "numeric", month: "short" });
}

export function PosRevenueTrendChart({ data, from, comparison, onDayClick, selectedDay }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.revenueTrend");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (!data || data.points.length === 0) {
    return (
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "20px 16px",
          color: "#4B5563",
          fontSize: 13,
          textAlign: "center",
        }}
      >
        {t("empty")}
      </div>
    );
  }

  const hasComparison = !!(comparison && comparison.points.length > 0 && from);

  let chartData: Array<{
    x: string;
    date: string;
    revenue: number | undefined;
    transactions: number | undefined;
    comparisonDate?: string;
    comparisonRevenue?: number;
    comparisonTransactions?: number;
  }>;

  if (!hasComparison) {
    chartData = data.points.map((p) => ({
      x: formatDate(p.date, intlLocale),
      date: p.date,
      revenue: p.revenue,
      transactions: p.transactions,
    }));
  } else {
    // Points are sparse (no entry for zero-activity days) and may not share array
    // length/order — align by day offset from each series' own start date.
    const byOffset = new Map<
      number,
      { date?: string; revenue?: number; transactions?: number; comparisonDate?: string; comparisonRevenue?: number; comparisonTransactions?: number }
    >();
    for (const p of data.points) {
      const offset = daysBetween(from!, p.date);
      byOffset.set(offset, { ...byOffset.get(offset), date: p.date, revenue: p.revenue, transactions: p.transactions });
    }
    for (const p of comparison!.points) {
      const offset = daysBetween(comparison!.from, p.date);
      byOffset.set(offset, {
        ...byOffset.get(offset),
        comparisonDate: p.date,
        comparisonRevenue: p.revenue,
        comparisonTransactions: p.transactions,
      });
    }
    chartData = Array.from(byOffset.entries())
      .sort(([a], [b]) => a - b)
      .map(([offset, v]) => ({
        x: t("dayLabel", { n: offset + 1 }),
        date: v.date ?? "",
        revenue: v.revenue,
        transactions: v.transactions,
        comparisonDate: v.comparisonDate,
        comparisonRevenue: v.comparisonRevenue,
        comparisonTransactions: v.comparisonTransactions,
      }));
  }

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 16px",
      }}
    >
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 16 }}>
        {t("title")}
      </div>
      <ResponsiveContainer width="100%" height={260}>
        <AreaChart
          data={chartData}
          margin={{ left: 8, right: 16, top: 4, bottom: 4 }}
          style={onDayClick ? { cursor: "pointer" } : undefined}
          onClick={
            onDayClick
              ? (state) => {
                  // Recharts (3.x) no longer hands the click handler an `activePayload` (that
                  // was recharts@2's API) — it gives back `activeTooltipIndex`, the string index
                  // of whichever point is currently active, which we resolve against our own
                  // `chartData` array instead.
                  const idx = state?.activeTooltipIndex;
                  const i = idx == null ? NaN : Number(idx);
                  const point = Number.isFinite(i) ? chartData[i] : undefined;
                  if (point?.date) onDayClick(point.date);
                }
              : undefined
          }
        >
          <defs>
            <linearGradient id="revenueGradient" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#3B82F6" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#3B82F6" stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="#1F2937" vertical={false} />
          <XAxis
            dataKey="x"
            tick={{ fill: "#6B7280", fontSize: 11 }}
            axisLine={false}
            tickLine={false}
          />
          <YAxis
            tick={{ fill: "#4B5563", fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={(v: number) => `${(v / 1000).toFixed(0)}${locale === "en" ? "k" : "к"}`}
            width={40}
          />
          <Tooltip
            contentStyle={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
            }}
            formatter={(val, name, props) => {
              const v = Number(val);
              if (name === "comparisonRevenue") {
                const d = (props.payload as { comparisonDate?: string }).comparisonDate;
                return [`${v.toLocaleString(intlLocale)} ₴${d ? ` (${formatDate(d, intlLocale)})` : ""}`, t("previousPeriod")];
              }
              const d = (props.payload as { date?: string }).date;
              return [`${v.toLocaleString(intlLocale)} ₴${d ? ` (${formatDate(d, intlLocale)})` : ""}`, t("currentPeriod")];
            }}
            cursor={{ stroke: "#374151", strokeWidth: 1 }}
          />
          {hasComparison && <Legend wrapperStyle={{ fontSize: 12, color: "#9CA3AF" }} />}
          <Area
            type="monotone"
            dataKey="revenue"
            name={hasComparison ? t("currentPeriod") : "revenue"}
            stroke="#3B82F6"
            strokeWidth={2}
            fill="url(#revenueGradient)"
            dot={false}
            activeDot={{ r: 4, fill: "#3B82F6" }}
            connectNulls
          />
          {hasComparison && (
            <Line
              type="monotone"
              dataKey="comparisonRevenue"
              name={t("previousPeriod")}
              stroke="#A78BFA"
              strokeWidth={2}
              strokeDasharray="5 4"
              dot={false}
              activeDot={{ r: 4, fill: "#A78BFA" }}
              connectNulls
            />
          )}
        </AreaChart>
      </ResponsiveContainer>
      {onDayClick && !selectedDay && (
        <p style={{ color: "#4B5563", fontSize: 12, marginTop: 14, marginBottom: 0 }}>{t("hint")}</p>
      )}
    </div>
  );
}
