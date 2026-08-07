"use client";

import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
  Cell,
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
  /** Fired on click of any of the four stacked status segments for a category — all four
   * resolve to the same category, so they share one handler. `categoryId` is the raw domain
   * value (null = "uncategorized" bucket), never a UI sentinel. */
  onCategoryClick?: (categoryId: string | null) => void;
  /** `undefined` = no selection (every category renders at full opacity). `null` specifically
   * means the uncategorized bucket is selected — distinct from "nothing selected" because a
   * category id is itself nullable, so `string | null` alone can't tell the two apart. */
  selectedCategoryId?: string | null;
}

export function CategoryStatusChart({ data, onCategoryClick, selectedCategoryId }: Props) {
  const t = useTranslations("Dashboard.analytics.categoryStatusChart");
  const tStatus = useTranslations("Dashboard.analytics.status");
  if (!data || data.length === 0) return null;

  const chartData = data.map((c) => ({
    categoryId: c.categoryId,
    name: c.categoryName,
    safe: c.safe,
    warning: c.warning,
    critical: c.critical,
    expired: c.expired,
  }));

  // Stacked chart: each status segment keeps its own semantic color (green/yellow/red/dark-red)
  // regardless of selection — an active/inactive COLOR swap (as the two-tone precedent in
  // SegmentDistributionChart.tsx does for a single-series chart) would destroy that coding here.
  // Selection is instead expressed as opacity: full for the selected category's four segments,
  // dimmed for every other category's, full for everyone when nothing is selected.
  function opacityFor(categoryId: string | null): number {
    if (selectedCategoryId === undefined) return 1;
    return selectedCategoryId === categoryId ? 1 : 0.3;
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
          <Bar
            dataKey="safe"
            name={tStatus("safe")}
            stackId="a"
            radius={[0, 0, 0, 0]}
            maxBarSize={40}
            onClick={onCategoryClick ? (entry) => onCategoryClick(entry.payload.categoryId as string | null) : undefined}
            style={onCategoryClick ? { cursor: "pointer" } : undefined}
          >
            {chartData.map((d, i) => (
              <Cell key={i} fill="#4ADE80" fillOpacity={opacityFor(d.categoryId)} />
            ))}
          </Bar>
          <Bar
            dataKey="warning"
            name={tStatus("warning")}
            stackId="a"
            maxBarSize={40}
            onClick={onCategoryClick ? (entry) => onCategoryClick(entry.payload.categoryId as string | null) : undefined}
            style={onCategoryClick ? { cursor: "pointer" } : undefined}
          >
            {chartData.map((d, i) => (
              <Cell key={i} fill="#FBBF24" fillOpacity={opacityFor(d.categoryId)} />
            ))}
          </Bar>
          <Bar
            dataKey="critical"
            name={tStatus("critical")}
            stackId="a"
            maxBarSize={40}
            onClick={onCategoryClick ? (entry) => onCategoryClick(entry.payload.categoryId as string | null) : undefined}
            style={onCategoryClick ? { cursor: "pointer" } : undefined}
          >
            {chartData.map((d, i) => (
              <Cell key={i} fill="#F87171" fillOpacity={opacityFor(d.categoryId)} />
            ))}
          </Bar>
          <Bar
            dataKey="expired"
            name={tStatus("expired")}
            stackId="a"
            radius={[4, 4, 0, 0]}
            maxBarSize={40}
            onClick={onCategoryClick ? (entry) => onCategoryClick(entry.payload.categoryId as string | null) : undefined}
            style={onCategoryClick ? { cursor: "pointer" } : undefined}
          >
            {chartData.map((d, i) => (
              <Cell key={i} fill="#DC2626" fillOpacity={opacityFor(d.categoryId)} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
