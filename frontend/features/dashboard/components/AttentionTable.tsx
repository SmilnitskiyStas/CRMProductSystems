"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { BarChart2 } from "lucide-react";
import type { AttentionItem, DashboardStats, ItemStatus } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { Table, type TableColumn } from "@/components/ui/Table";

const STATUS_CONFIG: Record<ItemStatus, { color: string; bg: string }> = {
  safe: { color: "#22c55e", bg: "#0d2818" },
  warning: { color: "#f59e0b", bg: "#261c05" },
  critical: { color: "#ef4444", bg: "#2a0a0a" },
  expired: { color: "#9ca3af", bg: "#1a1a1a" },
  sold_out: { color: "#6B7280", bg: "#111827" },
  needs_verification: { color: "#A78BFA", bg: "#1E1B2E" },
};

// Fallback for any status value the backend emits that isn't mapped above yet —
// the API boundary cast (`b.status as ItemStatus` in api/dashboard.ts) isn't
// actually guaranteed to match the type, so this keeps unmapped values from
// crashing the render (see AGREEMENT_STATUS_COLORS ?? GRAY pattern in
// features/marketplace/components/CooperationBadges.tsx).
const DEFAULT_STATUS_CONFIG = { color: "#6B7280", bg: "#111827" };

// Narrower than ItemStatus on purpose: `stats` (DashboardStats) only has counts for
// these 4 keys, and the filter bar intentionally doesn't expose sold_out /
// needs_verification as filter buttons (out of scope — see dashboard status widening).
const FILTER_VALUES: ("all" | "expired" | "critical" | "warning")[] = ["all", "expired", "critical", "warning"];

const VISIBLE_ROWS = 10;

interface Props {
  items: AttentionItem[] | undefined;
  isLoading: boolean;
  stats: DashboardStats | undefined;
}

export function AttentionTable({ items = [], isLoading, stats }: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.dashboard.attentionTable");
  const tStatus = useTranslations("Dashboard.dashboard.status");
  const tCommon = useTranslations("Common");
  const tProductAnalytics = useTranslations("Dashboard.ui.productAnalyticsLink");
  const [filter, setFilter] = useState<ItemStatus | "all">("all");

  const filtered = filter === "all" ? items : items.filter((i) => i.status === filter);
  const visible = filtered.slice(0, VISIBLE_ROWS);
  const viewAllHref = filter === "all" ? "/stock" : `/stock?status=${filter}`;

  const columns: TableColumn<AttentionItem>[] = [
    {
      key: "name",
      header: t("headers.name"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (item) => item.name,
    },
    {
      key: "sku",
      header: t("headers.sku"),
      cellStyle: { color: "#6B7280", fontFamily: "monospace", fontSize: 12 },
      render: (item) => item.sku,
    },
    {
      key: "category",
      header: t("headers.category"),
      cellStyle: { color: "#8A94A8" },
      render: (item) => item.category,
    },
    {
      key: "zone",
      header: t("headers.zone"),
      cellStyle: { color: "#8A94A8" },
      render: (item) => item.zone,
    },
    {
      key: "quantity",
      header: t("headers.quantity"),
      cellStyle: { fontFamily: "monospace", fontWeight: 600 },
      render: (item) => (
        <span style={{ color: item.quantity === 0 ? "#EF4444" : "#E8EDF5" }}>{item.quantity}</span>
      ),
    },
    {
      key: "reorderLevel",
      header: t("headers.reorderLevel"),
      cellStyle: { color: "#6B7280", fontFamily: "monospace" },
      render: (item) => item.reorderLevel,
    },
    {
      key: "status",
      header: t("headers.status"),
      render: (item) => {
        const cfg = STATUS_CONFIG[item.status] ?? DEFAULT_STATUS_CONFIG;
        return (
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
        );
      },
    },
    {
      key: "actions",
      header: t("headers.actions"),
      render: (item) => (
        <ActionMenu
          items={[
            {
              label: tProductAnalytics("title"),
              icon: <BarChart2 size={13} />,
              onClick: () => router.push(`/inventory/${item.productId}?tab=analytics`),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, overflow: "hidden", display: "flex", flexDirection: "column" }}>
      {/* Header */}
      <div
        style={{
          padding: "12px 20px",
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
            // Badge counts come from the accurate store-wide stats (same source as the
            // top stat cards), not from `items` — that list is capped at pageSize=200
            // by the backend and sorted by urgency, so counting within it undercounts
            // (or misses entirely) less-urgent statuses once expired+critical alone
            // exceed the cap.
            const count = stats
              ? value === "all"
                ? stats.warning + stats.critical + stats.expired
                : stats[value]
              : value === "all"
                ? items.length
                : items.filter((i) => i.status === value).length;
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
        <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", color: "#4B5563", fontSize: 13 }}>
          {tCommon("loading")}
        </div>
      ) : filtered.length === 0 ? (
        <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", color: "#4B5563", fontSize: 13 }}>
          {t("empty")}
        </div>
      ) : (
        <div style={{ overflowX: "auto", flex: 1, display: "flex", flexDirection: "column" }}>
          <Table columns={columns} rows={visible} rowKey={(item) => item.id} />

          {/* View all */}
          {filtered.length > VISIBLE_ROWS && (
            <div
              style={{
                marginTop: "auto",
                borderTop: "1px solid #1F2937",
                padding: "8px 16px",
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
