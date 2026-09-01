"use client";

import { useId } from "react";
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
} from "recharts";
import { useTranslations, useLocale } from "next-intl";

/** Units the buyer-facing metric charts render. `percent` values are already
 *  scaled to 0..100 by the caller (the API sends 0..1 fractions). */
export type MetricTrendUnit = "day" | "hour" | "percent" | "star" | "score";

export interface MetricTrendPoint {
  /** yyyy-mm-dd */
  date: string;
  /** null → a gap in the line for that day. */
  value: number | null;
}

interface Props {
  points: MetricTrendPoint[];
  unit: MetricTrendUnit;
  /** Series name shown in the tooltip. */
  label: string;
  /** Line / area colour. Defaults to the blue used across the analytics charts. */
  color?: string;
}

function round(v: number, digits: number): number {
  return Number(v.toFixed(digits));
}

function formatDate(dateStr: string, intlLocale: string): string {
  return new Date(`${dateStr}T00:00:00Z`).toLocaleDateString(intlLocale, {
    day: "numeric",
    month: "short",
  });
}

/**
 * Shared trend chart for one supplier performance metric on the metrics detail
 * page (TASK-672). Mirrors `analytics/components/LossesTrendChart.tsx`'s recharts
 * 3.8.1 `AreaChart` setup / dark-theme axis + tooltip styling. Single series, no
 * click drill-down. `connectNulls` is deliberately OFF so a missing day shows as
 * a gap rather than a straight interpolated line. When fewer than two real data
 * points exist a muted empty state is shown instead of a broken chart.
 */
export function SupplierMetricTrendChart({ points, unit, label, color = "#3B82F6" }: Props) {
  const t = useTranslations("Dashboard.marketplace.metricsPage");
  const tMetrics = useTranslations("Dashboard.marketplace.metrics");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const gradientId = useId();

  const drawable = points.filter((p) => p.value != null);

  if (drawable.length < 2) {
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
        {t("chartEmpty")}
      </div>
    );
  }

  const fmtValue = (v: number): string => {
    switch (unit) {
      case "day":
        return `${round(v, 1)}${tMetrics("daySuffix")}`;
      case "hour":
        return `${round(v, 1)}${tMetrics("hourSuffix")}`;
      case "percent":
        return `${Math.round(v)}%`;
      case "star":
        return round(v, 1).toString();
      case "score":
        return round(v, 2).toString();
    }
  };

  const yTickFormatter = (v: number): string =>
    unit === "percent" ? `${round(v, 1)}%` : round(v, 1).toString();

  // `star` sits on a fixed 0..5 scale; everything else (incl. percent) auto-fits
  // so a metric that only varies by a point or two still reads as a real trend.
  const yDomain: [number | string, number | string] =
    unit === "star" ? [0, 5] : ["auto", "auto"];

  const chartData = points.map((p) => ({
    x: formatDate(p.date, intlLocale),
    date: p.date,
    value: p.value,
  }));

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 16px",
      }}
    >
      <ResponsiveContainer width="100%" height={200}>
        <AreaChart data={chartData} margin={{ left: 8, right: 16, top: 4, bottom: 4 }}>
          <defs>
            <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={color} stopOpacity={0.3} />
              <stop offset="95%" stopColor={color} stopOpacity={0.02} />
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
            domain={yDomain}
            tickFormatter={yTickFormatter}
            width={44}
          />
          <Tooltip
            contentStyle={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
            }}
            formatter={(val) => [fmtValue(Number(val)), label]}
            cursor={{ stroke: "#374151", strokeWidth: 1 }}
          />
          <Area
            type="monotone"
            dataKey="value"
            name={label}
            stroke={color}
            strokeWidth={2}
            fill={`url(#${gradientId})`}
            dot={false}
            activeDot={{ r: 4, fill: color }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
