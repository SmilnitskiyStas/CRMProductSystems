"use client";

import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import { useTranslations } from "next-intl";

interface CategoryStat {
  categoryId: string | null;
  categoryName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
  totalQuantity: number;
}

interface Props {
  data: CategoryStat[];
}

export function CategoryStatusChart({ data }: Props) {
  const t = useTranslations("Dashboard.analytics.categoryStatusChart");
  const tStatus = useTranslations("Dashboard.analytics.status");
  if (!data || data.length === 0) return null;

  const chartData = data.map((c) => ({
    name: c.categoryName,
    safe: c.safe,
    warning: c.warning,
    critical: c.critical,
    expired: c.expired,
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
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={chartData} margin={{ left: 8, right: 16, top: 4, bottom: 40 }}>
          <XAxis
            dataKey="name"
            tick={{ fill: "#9CA3AF", fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            angle={-20}
            textAnchor="end"
            interval={0}
          />
          <YAxis
            tick={{ fill: "#4B5563", fontSize: 11 }}
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
            cursor={{ fill: "rgba(255,255,255,0.03)" }}
          />
          <Legend
            iconType="circle"
            iconSize={8}
            wrapperStyle={{ paddingTop: 8 }}
            formatter={(val) => (
              <span style={{ color: "#9CA3AF", fontSize: 12 }}>{val}</span>
            )}
          />
          <Bar dataKey="safe" name={tStatus("safe")} stackId="a" fill="#4ADE80" radius={[0,0,0,0]} maxBarSize={40} />
          <Bar dataKey="warning" name={tStatus("warning")} stackId="a" fill="#FBBF24" maxBarSize={40} />
          <Bar dataKey="critical" name={tStatus("critical")} stackId="a" fill="#F87171" maxBarSize={40} />
          <Bar dataKey="expired" name={tStatus("expired")} stackId="a" fill="#DC2626" radius={[4,4,0,0]} maxBarSize={40} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
