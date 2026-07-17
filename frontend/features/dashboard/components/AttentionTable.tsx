"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { BarChart2 } from "lucide-react";
import type { AttentionItem, ItemStatus } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";

const STATUS_CONFIG: Record<ItemStatus, { color: string; bg: string }> = {
  safe: { color: "#22c55e", bg: "#0d2818" },
  warning: { color: "#f59e0b", bg: "#261c05" },
  critical: { color: "#ef4444", bg: "#2a0a0a" },
  expired: { color: "#9ca3af", bg: "#1a1a1a" },
};

const FILTER_VALUES: (ItemStatus | "all")[] = ["all", "expired", "critical", "warning"];

const VISIBLE_ROWS = 5;

interface Props {
  items: AttentionItem[] | undefined;
  isLoading: boolean;
}

export function AttentionTable({ items = [], isLoading }: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.dashboard.attentionTable");
  const tStatus = useTranslations("Dashboard.dashboard.status");
  const tCommon = useTranslations("Common");
  const tProductAnalytics = useTranslations("Dashboard.ui.productAnalyticsLink");
  const [filter, setFilter] = useState<ItemStatus | "all">("all");

  const filtered = filter === "all" ? items : items.filter((i) => i.status === filter);
  const visible = filtered.slice(0, VISIBLE_ROWS);
  const viewAllHref = filter === "all" ? "/stock" : `/stock?status=${filter}`;
  const headers = [
    t("headers.name"),
    t("headers.sku"),
    t("headers.category"),
    t("headers.zone"),
    t("headers.quantity"),
    t("headers.reorderLevel"),
    t("headers.status"),
    t("headers.actions"),
  ];

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
          {t("title")}
        </h2>
        <div style={{ display: "flex", gap: 6 }}>
          {FILTER_VALUES.map((value) => {
            const label = value === "all" ? t("filterAll") : tStatus(value);
            const count =
              value === "all" ? items.length : items.filter((i) => i.status === value).length;
            const active = filter === value;
            return (
              <button
                key={value}
                onClick={() => setFilter(value)}
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
                {label}
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
          {tCommon("loading")}
        </div>
      ) : filtered.length === 0 ? (
        <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
          {t("empty")}
        </div>
      ) : (
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr style={{ borderBottom: "1px solid #1F2937" }}>
                {headers.map(
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
              {visible.map((item, idx) => {
                const cfg = STATUS_CONFIG[item.status];
                return (
                  <tr
                    key={item.id}
                    style={{
                      borderBottom: idx < visible.length - 1 ? "1px solid #111827" : "none",
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
                        {tStatus(item.status)}
                      </span>
                    </td>
                    <td style={{ padding: "8px 16px" }}>
                      <ActionMenu
                        items={[
                          {
                            label: tProductAnalytics("title"),
                            icon: <BarChart2 size={13} />,
                            onClick: () => router.push(`/inventory/${item.productId}?tab=analytics`),
                          },
                        ]}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          {/* View all */}
          {filtered.length > VISIBLE_ROWS && (
            <div
              style={{
                borderTop: "1px solid #1F2937",
                padding: "10px 16px",
                display: "flex",
                justifyContent: "center",
              }}
            >
              <button
                onClick={() => router.push(viewAllHref)}
                style={{
                  padding: "6px 16px",
                  borderRadius: 6,
                  border: "1px solid #1F2937",
                  background: "transparent",
                  color: "#93C5FD",
                  fontSize: 12,
                  fontWeight: 500,
                  cursor: "pointer",
                }}
                onMouseEnter={(e) => {
                  (e.currentTarget as HTMLElement).style.background = "#1D3461";
                  (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
                }}
                onMouseLeave={(e) => {
                  (e.currentTarget as HTMLElement).style.background = "transparent";
                  (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
                }}
              >
                {t("viewAll", { count: filtered.length })}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
