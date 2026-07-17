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
}

export function LossesByReasonChart({ data }: Props) {
  const t = useTranslations("Dashboard.analytics.lossesByReasonChart");
  const tReason = useTranslations("Dashboard.analytics.reason");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  if (!data || data.length === 0) return null;

  const chartData = data.map((r) => ({
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
          <Bar dataKey="loss" radius={[0, 4, 4, 0]} maxBarSize={24}>
            {chartData.map((_, i) => (
              <Cell key={i} fill="#F87171" fillOpacity={0.8 - i * 0.1} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
