"use client";

import {
  PieChart,
  Pie,
  Cell,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";

interface Props {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
}

const SLICES = [
  { key: "safe",             label: "Норма",      color: "#4ADE80" },
  { key: "warning",          label: "Попередження", color: "#FBBF24" },
  { key: "critical",         label: "Критично",   color: "#F87171" },
  { key: "expired",          label: "Прострочено", color: "#DC2626" },
  { key: "needsVerification",label: "Перевірка",  color: "#A78BFA" },
] as const;

export function ExpiryDonut({ safe, warning, critical, expired, needsVerification }: Props) {
  const raw: Record<string, number> = { safe, warning, critical, expired, needsVerification };

  const data = SLICES
    .map((s) => ({ name: s.label, value: raw[s.key], color: s.color }))
    .filter((d) => d.value > 0);

  if (data.length === 0) return null;

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
        Розподіл партій за статусом
      </div>
      <ResponsiveContainer width="100%" height={260}>
        <PieChart>
          <Pie
            data={data}
            cx="50%"
            cy="50%"
            innerRadius={60}
            outerRadius={100}
            paddingAngle={3}
            dataKey="value"
          >
            {data.map((entry, i) => (
              <Cell key={i} fill={entry.color} stroke="transparent" />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
            }}
            formatter={(val) => [val, ""]}
          />
          <Legend
            iconType="circle"
            iconSize={8}
            formatter={(val) => (
              <span style={{ color: "#9CA3AF", fontSize: 12 }}>{val}</span>
            )}
          />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
