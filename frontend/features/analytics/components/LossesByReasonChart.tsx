"use client";

import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  Cell,
} from "recharts";
import { useTranslations, useLocale } from "next-intl";

interface ReasonStat {
  reason: string;
  count: number;
  totalLoss: number;
}

interface Props {
  data: ReasonStat[];
  onReasonClick?: (reason: string) => void;
  /** undefined = no selection (original per-rank opacity gradient stays as-is). */
  selectedReason?: string;
}

export function LossesByReasonChart({ data, onReasonClick, selectedReason }: Props) {
  const t = useTranslations("Dashboard.analytics.lossesByReasonChart");
  const tReason = useTranslations("Dashboard.analytics.reason");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  if (!data || data.length === 0) return null;

  // `name` is already the localized display label — keep the raw `reason` key alongside it so
  // the click handler always reports a stable, backend-matching value, never translated text.
  const chartData = data.map((r) => ({
    reasonKey: r.reason,
    name: tReason.has(r.reason) ? tReason(r.reason) : r.reason,
    loss: r.totalLoss,
    count: r.count,
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
        <BarChart data={chartData} layout="vertical" margin={{ left: 16, right: 24, top: 4, bottom: 4 }}>
          <XAxis
            type="number"
            tick={{ fill: "#4B5563", fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={(v) => v.toLocaleString(intlLocale)}
          />
          <YAxis
            type="category"
            dataKey="name"
            width={120}
            tick={{ fill: "#9CA3AF", fontSize: 12 }}
            axisLine={false}
            tickLine={false}
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
            cursor={{ fill: "rgba(255,255,255,0.03)" }}
          />
          <Bar
            dataKey="loss"
            radius={[0, 4, 4, 0]}
            maxBarSize={24}
            onClick={onReasonClick ? (entry) => onReasonClick(entry.payload.reasonKey as string) : undefined}
            style={onReasonClick ? { cursor: "pointer" } : undefined}
          >
            {chartData.map((d, i) => (
              <Cell
                key={i}
                fill="#F87171"
                fillOpacity={selectedReason === undefined ? 0.8 - i * 0.1 : selectedReason === d.reasonKey ? 1 : 0.2}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
