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

interface StoreStat {
  storeId: string;
  storeName: string;
  writeOffCount: number;
  totalLoss: number;
}

interface Props {
  data: StoreStat[];
  onStoreClick?: (storeId: string) => void;
  /** undefined = no selection (original per-store color palette stays at its base opacity). */
  selectedStoreId?: string;
}

const STORE_COLORS = ["#F87171", "#FB923C", "#FBBF24", "#A78BFA", "#60A5FA"];

export function LossesByStoreChart({ data, onStoreClick, selectedStoreId }: Props) {
  const t = useTranslations("Dashboard.analytics.lossesByStoreChart");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  if (!data || data.length === 0) return null;

  const chartData = data
    .slice()
    .sort((a, b) => b.totalLoss - a.totalLoss)
    .map((s) => ({
      storeId: s.storeId,
      name: s.storeName,
      loss: s.totalLoss,
      count: s.writeOffCount,
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
        <BarChart data={chartData} margin={{ left: 8, right: 24, top: 4, bottom: 40 }}>
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
            tickFormatter={(v) => v.toLocaleString(intlLocale)}
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
              `${Number(val).toLocaleString(intlLocale)} ₴ (${(props.payload as { count: number }).count} ${t("tooltipWriteOffsSuffix")})`,
              t("tooltipLabel"),
            ]}
            cursor={{ fill: "rgba(255,255,255,0.03)" }}
          />
          <Bar
            dataKey="loss"
            fill="#E8EDF5"
            radius={[4, 4, 0, 0]}
            maxBarSize={48}
            onClick={onStoreClick ? (entry) => onStoreClick(entry.payload.storeId as string) : undefined}
            style={onStoreClick ? { cursor: "pointer" } : undefined}
          >
            {chartData.map((d, i) => (
              <Cell
                key={i}
                fill={STORE_COLORS[i % STORE_COLORS.length]}
                fillOpacity={selectedStoreId === undefined ? 0.85 : selectedStoreId === d.storeId ? 1 : 0.25}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
