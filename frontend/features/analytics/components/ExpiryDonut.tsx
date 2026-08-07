"use client";

import {
  PieChart,
  Pie,
  Cell,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import { useTranslations } from "next-intl";

interface Props {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  /**
   * Called with a status key ready for `/stock?status=...` when a slice is clicked — same
   * values (including snake_case `needs_verification`) the MetricCards/table rows on the
   * /analytics page already navigate with. Component stays presentational: the page owns the
   * actual `router.push`, this just reports which slice was clicked.
   */
  onSliceClick?: (status: string) => void;
}

export function ExpiryDonut({ safe, warning, critical, expired, needsVerification, onSliceClick }: Props) {
  const t = useTranslations("Dashboard.analytics.expiryDonut");
  const tStatus = useTranslations("Dashboard.analytics.status");
  const raw: Record<string, number> = { safe, warning, critical, expired, needsVerification };

  const SLICES = [
    { key: "safe",              status: "safe",               label: tStatus("safe"),             color: "#4ADE80" },
    { key: "warning",           status: "warning",            label: tStatus("warning"),          color: "#FBBF24" },
    { key: "critical",          status: "critical",           label: tStatus("critical"),         color: "#F87171" },
    { key: "expired",           status: "expired",            label: tStatus("expired"),          color: "#DC2626" },
    { key: "needsVerification", status: "needs_verification", label: tStatus("needsVerification"), color: "#A78BFA" },
  ] as const;

  const data = SLICES
    .map((s) => ({ name: s.label, value: raw[s.key], color: s.color, status: s.status }))
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
        {t("title")}
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
            // Recharts (3.x) dispatches per-sector clicks through the Pie's own onClick, not
            // through <Cell onClick>: it receives back the sector's data entry (our `data` item,
            // `status` included) — see Pie's PieSectorDataItem type, which is intentionally loose
            // ("we spread the data object in, so we can't know what's inside") hence the cast.
            onClick={
              onSliceClick
                ? (entry: unknown) => {
                    const status = (entry as { status?: string } | null)?.status;
                    if (status) onSliceClick(status);
                  }
                : undefined
            }
          >
            {data.map((entry, i) => (
              <Cell
                key={i}
                fill={entry.color}
                stroke="transparent"
                style={onSliceClick ? { cursor: "pointer" } : undefined}
              />
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
