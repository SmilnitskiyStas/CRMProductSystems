"use client";

import type { DashboardStats } from "../types";

interface StatCard {
  label: string;
  key: keyof DashboardStats;
  color: string;
  bg: string;
  border: string;
  dot: string;
}

const CARDS: StatCard[] = [
  { label: "Safe", key: "safe", color: "#22c55e", bg: "#0d2818", border: "#166534", dot: "#22c55e" },
  { label: "Warning", key: "warning", color: "#f59e0b", bg: "#261c05", border: "#854d0e", dot: "#f59e0b" },
  { label: "Critical", key: "critical", color: "#ef4444", bg: "#2a0a0a", border: "#991b1b", dot: "#ef4444" },
  { label: "Expired", key: "expired", color: "#6b7280", bg: "#141414", border: "#374151", dot: "#6b7280" },
];

interface Props {
  stats: DashboardStats | undefined;
  isLoading: boolean;
}

export function StatsCards({ stats, isLoading }: Props) {
  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
      {CARDS.map((card) => (
        <div
          key={card.key}
          style={{
            background: card.bg,
            border: `1px solid ${card.border}`,
            borderRadius: 12,
            padding: "20px 24px",
          }}
        >
          <div className="flex items-center gap-2 mb-3">
            <span
              style={{
                width: 8,
                height: 8,
                borderRadius: "50%",
                background: card.dot,
                display: "inline-block",
                flexShrink: 0,
              }}
            />
            <span style={{ color: "#8A94A8", fontSize: 12, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.06em" }}>
              {card.label}
            </span>
          </div>
          <div style={{ color: card.color, fontSize: 40, fontWeight: 700, lineHeight: 1 }}>
            {isLoading ? (
              <span style={{ color: "#374151", fontSize: 24 }}>—</span>
            ) : (
              stats?.[card.key] ?? 0
            )}
          </div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 6 }}>items</div>
        </div>
      ))}
    </div>
  );
}
