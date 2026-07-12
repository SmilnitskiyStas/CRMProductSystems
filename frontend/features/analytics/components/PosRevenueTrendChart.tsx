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
}

const MS_PER_DAY = 86_400_000;

function daysBetween(fromStr: string, dateStr: string): number {
  return Math.round((new Date(`${dateStr}T00:00:00Z`).getTime() - new Date(`${fromStr}T00:00:00Z`).getTime()) / MS_PER_DAY);
}

function formatDate(dateStr: string): string {
  return new Date(`${dateStr}T00:00:00Z`).toLocaleDateString("uk-UA", { day: "numeric", month: "short" });
}

export function PosRevenueTrendChart({ data, from, comparison }: Props) {
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
        Немає даних за обраний період
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
      x: formatDate(p.date),
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
        x: `День ${offset + 1}`,
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
        Динаміка виручки
      </div>
      <ResponsiveContainer width="100%" height={260}>
        <AreaChart data={chartData} margin={{ left: 8, right: 16, top: 4, bottom: 4 }}>
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
            tickFormatter={(v: number) => `${(v / 1000).toFixed(0)}к`}
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
                return [`${v.toLocaleString("uk-UA")} ₴${d ? ` (${formatDate(d)})` : ""}`, "Попередній період"];
              }
              const d = (props.payload as { date?: string }).date;
              return [`${v.toLocaleString("uk-UA")} ₴${d ? ` (${formatDate(d)})` : ""}`, "Поточний період"];
            }}
            cursor={{ stroke: "#374151", strokeWidth: 1 }}
          />
          {hasComparison && <Legend wrapperStyle={{ fontSize: 12, color: "#9CA3AF" }} />}
          <Area
            type="monotone"
            dataKey="revenue"
            name={hasComparison ? "Поточний період" : "revenue"}
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
              name="Попередній період"
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
    </div>
  );
}
