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
import type { PosRevenueTrendDto } from "../types";

interface Props {
  data: PosRevenueTrendDto;
}

function formatDate(dateStr: string, groupBy: "day" | "week"): string {
  const d = new Date(dateStr);
  if (groupBy === "week") {
    return d.toLocaleDateString("uk-UA", { day: "numeric", month: "short" });
  }
  return d.toLocaleDateString("uk-UA", { day: "numeric", month: "short" });
}

export function PosRevenueTrendChart({ data }: Props) {
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

  const chartData = data.points.map((p) => ({
    date: formatDate(p.date, data.groupBy),
    rawDate: p.date,
    revenue: p.revenue,
    transactions: p.transactions,
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
            dataKey="date"
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
            formatter={(val, name) => {
              const v = Number(val);
              if (name === "revenue") {
                return [`${v.toLocaleString("uk-UA")} ₴`, "Виручка"];
              }
              return [v.toLocaleString("uk-UA"), "Транзакцій"];
            }}
            cursor={{ stroke: "#374151", strokeWidth: 1 }}
          />
          <Area
            type="monotone"
            dataKey="revenue"
            stroke="#3B82F6"
            strokeWidth={2}
            fill="url(#revenueGradient)"
            dot={false}
            activeDot={{ r: 4, fill: "#3B82F6" }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
