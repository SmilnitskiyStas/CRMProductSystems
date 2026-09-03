"use client";

// Thin single-series revenue trend chart for the supplier analytics page (Phase 6b).
// Recharts AreaChart setup / dark-theme axis + tooltip styling mirrors
// analytics/components/PosRevenueTrendChart.tsx — kept local (own i18n namespace,
// own data shape) rather than importing the retail component.

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
import type { SupplierAnalyticsTrendPoint } from "../types";

interface Props {
  points: SupplierAnalyticsTrendPoint[];
}

function formatDate(dateStr: string, intlLocale: string): string {
  return new Date(`${dateStr}T00:00:00Z`).toLocaleDateString(intlLocale, {
    day: "numeric",
    month: "short",
  });
}

export function SupplierRevenueTrendChart({ points }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.analytics");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const gradientId = useId();

  if (points.length === 0) {
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
        {t("trendEmpty")}
      </div>
    );
  }

  const chartData = points.map((p) => ({
    x: formatDate(p.date, intlLocale),
    date: p.date,
    revenue: p.revenue,
    orderCount: p.orderCount,
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
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 16 }}>
        {t("trendTitle")}
      </div>
      <ResponsiveContainer width="100%" height={240}>
        <AreaChart data={chartData} margin={{ left: 8, right: 16, top: 4, bottom: 4 }}>
          <defs>
            <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
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
            formatter={(val, name) => {
              const v = Number(val);
              if (name === "orderCount") return [v.toLocaleString(intlLocale), t("kpiOrders")];
              return [`${v.toLocaleString(intlLocale)} ₴`, t("kpiRevenue")];
            }}
            labelFormatter={(_label, payload) => {
              const d = (payload?.[0]?.payload as { date?: string })?.date;
              return d ? formatDate(d, intlLocale) : "";
            }}
            cursor={{ stroke: "#374151", strokeWidth: 1 }}
          />
          <Area
            type="monotone"
            dataKey="revenue"
            name="revenue"
            stroke="#3B82F6"
            strokeWidth={2}
            fill={`url(#${gradientId})`}
            dot={false}
            activeDot={{ r: 4, fill: "#3B82F6" }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
