"use client";

import { useState } from "react";
import type { AttentionItem, ItemStatus } from "../types";

const STATUS_CONFIG: Record<ItemStatus, { label: string; color: string; bg: string }> = {
  safe: { label: "Safe", color: "#22c55e", bg: "#0d2818" },
  warning: { label: "Warning", color: "#f59e0b", bg: "#261c05" },
  critical: { label: "Critical", color: "#ef4444", bg: "#2a0a0a" },
  expired: { label: "Expired", color: "#9ca3af", bg: "#1a1a1a" },
};

const FILTERS: { label: string; value: ItemStatus | "all" }[] = [
  { label: "All", value: "all" },
  { label: "Expired", value: "expired" },
  { label: "Critical", value: "critical" },
  { label: "Warning", value: "warning" },
];

interface Props {
  items: AttentionItem[] | undefined;
  isLoading: boolean;
}

export function AttentionTable({ items = [], isLoading }: Props) {
  const [filter, setFilter] = useState<ItemStatus | "all">("all");

  const filtered = filter === "all" ? items : items.filter((i) => i.status === filter);

  return (
    <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, overflow: "hidden" }}>
      {/* Header */}
      <div
        style={{
          padding: "16px 20px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: 12,
        }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: 0 }}>
          Потребують уваги
        </h2>
        <div style={{ display: "flex", gap: 6 }}>
          {FILTERS.map((f) => {
            const count =
              f.value === "all" ? items.length : items.filter((i) => i.status === f.value).length;
            const active = filter === f.value;
            return (
              <button
                key={f.value}
                onClick={() => setFilter(f.value)}
                style={{
                  padding: "4px 12px",
                  borderRadius: 6,
                  border: active ? "1px solid #3B82F6" : "1px solid #1F2937",
                  background: active ? "#1D3461" : "transparent",
                  color: active ? "#93C5FD" : "#6B7280",
                  fontSize: 12,
                  cursor: "pointer",
                  fontWeight: active ? 600 : 400,
                }}
              >
                {f.label}
                {count > 0 && (
                  <span
                    style={{
                      marginLeft: 6,
                      background: active ? "#3B82F6" : "#1F2937",
                      color: active ? "#fff" : "#9CA3AF",
                      borderRadius: 10,
                      padding: "1px 6px",
                      fontSize: 11,
                    }}
                  >
                    {count}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>

      {/* Table */}
      {isLoading ? (
        <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
          Завантаження…
        </div>
      ) : filtered.length === 0 ? (
        <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
          Немає товарів з цим статусом
        </div>
      ) : (
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr style={{ borderBottom: "1px solid #1F2937" }}>
                {["Назва", "SKU", "Категорія", "Зона", "Кількість", "Мін. залишок", "Статус"].map(
                  (h) => (
                    <th
                      key={h}
                      style={{
                        padding: "10px 16px",
                        textAlign: "left",
                        color: "#4B5563",
                        fontSize: 11,
                        fontWeight: 500,
                        textTransform: "uppercase",
                        letterSpacing: "0.06em",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {h}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {filtered.map((item, idx) => {
                const cfg = STATUS_CONFIG[item.status];
                return (
                  <tr
                    key={item.id}
                    style={{
                      borderBottom: idx < filtered.length - 1 ? "1px solid #111827" : "none",
                      transition: "background 0.1s",
                    }}
                    onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.background = "#1a1f2e")}
                    onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.background = "transparent")}
                  >
                    <td style={{ padding: "12px 16px", color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                      {item.name}
                    </td>
                    <td style={{ padding: "12px 16px", color: "#6B7280", fontSize: 12, fontFamily: "monospace" }}>
                      {item.sku}
                    </td>
                    <td style={{ padding: "12px 16px", color: "#8A94A8", fontSize: 13 }}>
                      {item.category}
                    </td>
                    <td style={{ padding: "12px 16px", color: "#8A94A8", fontSize: 13 }}>
                      {item.zone}
                    </td>
                    <td
                      style={{
                        padding: "12px 16px",
                        color: item.quantity === 0 ? "#EF4444" : "#E8EDF5",
                        fontSize: 13,
                        fontFamily: "monospace",
                        fontWeight: 600,
                      }}
                    >
                      {item.quantity}
                    </td>
                    <td style={{ padding: "12px 16px", color: "#6B7280", fontSize: 13, fontFamily: "monospace" }}>
                      {item.reorderLevel}
                    </td>
                    <td style={{ padding: "12px 16px" }}>
                      <span
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 5,
                          padding: "3px 10px",
                          borderRadius: 6,
                          background: cfg.bg,
                          border: `1px solid ${cfg.color}30`,
                          color: cfg.color,
                          fontSize: 12,
                          fontWeight: 500,
                        }}
                      >
                        <span
                          style={{
                            width: 5,
                            height: 5,
                            borderRadius: "50%",
                            background: cfg.color,
                            display: "inline-block",
                          }}
                        />
                        {cfg.label}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
