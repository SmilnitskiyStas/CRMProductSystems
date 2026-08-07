"use client";

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
import type { LossesTrendDto } from "../types";

interface Props {
  data: LossesTrendDto;
  /** Called with a point's `date` (yyyy-mm-dd) when the user clicks near it on the chart. */
  onDayClick?: (date: string) => void;
  /** Currently selected day, if any — hides the "click to see details" hint once set (mirrors
   * PosRevenueTrendChart's selectedDay prop/hint pattern). */
  selectedDay?: string | null;
}

function formatDate(dateStr: string, intlLocale: string): string {
  return new Date(`${dateStr}T00:00:00Z`).toLocaleDateString(intlLocale, { day: "numeric", month: "short" });
}

/**
 * Losses/write-offs trend over time (TASK-489 endpoint, TASK-492 UI). Single series
 * (totalLoss per day/week) — the endpoint has no compare-mode variant, so unlike
 * PosRevenueTrendChart there is no comparison Line/Legend here. AreaChart structure and the
 * click mechanism are copied from PosRevenueTrendChart.tsx verbatim (verified-working recharts
 * 3.8.1 shape: the click event has no `activePayload` — that was recharts@2's API — so
 * `activeTooltipIndex` is resolved against this component's own `chartData` array instead).
 * Tooltip/label tone matches the sibling LossesByReasonChart/LossesByStoreChart (loss amount +
 * doc-count suffix) rather than PosRevenueTrendChart's own revenue-period tooltip, since this is
 * a losses chart living in the same section as those two.
 */
export function LossesTrendChart({ data, onDayClick, selectedDay }: Props) {
  const t = useTranslations("Dashboard.analytics.lossesTrendChart");
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

  const chartData = data.points.map((p) => ({
    x: formatDate(p.date, intlLocale),
    date: p.date,
    totalLoss: p.totalLoss,
    count: p.count,
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
        {t("title")}
      </div>
      <ResponsiveContainer width="100%" height={220}>
        <AreaChart
          data={chartData}
          margin={{ left: 8, right: 16, top: 4, bottom: 4 }}
          style={onDayClick ? { cursor: "pointer" } : undefined}
          onClick={
            onDayClick
              ? (state) => {
                  // Recharts (3.x) no longer hands the click handler an `activePayload` (that was
                  // recharts@2's API) — it gives back `activeTooltipIndex`, the string index of
                  // whichever point is currently active, which we resolve against our own
                  // `chartData` array instead. Same mechanism as PosRevenueTrendChart.tsx.
                  const idx = state?.activeTooltipIndex;
                  const i = idx == null ? NaN : Number(idx);
                  const point = Number.isFinite(i) ? chartData[i] : undefined;
                  if (point?.date) onDayClick(point.date);
                }
              : undefined
          }
        >
          <defs>
            <linearGradient id="lossesTrendGradient" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#F87171" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#F87171" stopOpacity={0.02} />
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
            formatter={(val, _name, props) => [
              `${Number(val).toLocaleString(intlLocale)} ₴ (${(props.payload as { count: number }).count} ${t("tooltipDocsSuffix")})`,
              t("tooltipLabel"),
            ]}
            cursor={{ stroke: "#374151", strokeWidth: 1 }}
          />
          <Area
            type="monotone"
            dataKey="totalLoss"
            name="totalLoss"
            stroke="#F87171"
            strokeWidth={2}
            fill="url(#lossesTrendGradient)"
            dot={false}
            activeDot={{ r: 4, fill: "#F87171" }}
            connectNulls
          />
        </AreaChart>
      </ResponsiveContainer>
      {onDayClick && !selectedDay && (
        <p style={{ color: "#4B5563", fontSize: 12, marginTop: 14, marginBottom: 0 }}>{t("hint")}</p>
      )}
    </div>
  );
}
