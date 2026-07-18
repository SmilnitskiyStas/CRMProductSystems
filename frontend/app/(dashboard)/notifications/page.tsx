"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Clock, Filter } from "lucide-react";
import { NotificationHistoryList } from "@/features/notifications/components/NotificationHistoryList";
import { NotificationFilterDrawer } from "@/features/notifications/components/NotificationFilterDrawer";
import type { NotificationHistoryFilters } from "@/features/notifications/types";

const DEFAULT_PAGE_SIZE = 20;

function countActiveFilters(f: NotificationHistoryFilters): number {
  return [f.search, f.eventType, f.userId, f.storeId, f.dateFrom, f.dateTo].filter(Boolean).length;
}

export default function NotificationsPage() {
  const t = useTranslations("Dashboard.notifications.page");
  const [filters, setFilters] = useState<NotificationHistoryFilters>({
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
  });
  const [filtersOpen, setFiltersOpen] = useState(false);
  const activeCount = countActiveFilters(filters);

  return (
    <div style={{ padding: "28px 32px", maxWidth: 860 }}>
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 28, gap: 16 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
            {t("subtitlePrefix")}{" "}
            <a href="/settings?tab=notifications" style={{ color: "#3B82F6" }}>
              {t("settingsLinkText")}
            </a>
            .
          </p>
        </div>

        <button
          onClick={() => setFiltersOpen(true)}
          style={{
            position: "relative",
            display: "flex", alignItems: "center", gap: 6,
            background: activeCount > 0 ? "#111827" : "transparent",
            border: `1px solid ${activeCount > 0 ? "#2563EB" : "#1F2937"}`,
            borderRadius: 8, padding: "8px 14px",
            color: activeCount > 0 ? "#93C5FD" : "#9CA3AF",
            fontSize: 13, cursor: "pointer", flexShrink: 0,
          }}
        >
          <Filter size={14} />
          {t("filtersButton")}
          {activeCount > 0 && (
            <span style={{
              display: "flex", alignItems: "center", justifyContent: "center",
              minWidth: 18, height: 18, padding: "0 4px",
              background: "#3B82F6", color: "#fff",
              borderRadius: 9, fontSize: 11, fontWeight: 700,
            }}>
              {activeCount}
            </span>
          )}
        </button>
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
        <Clock size={15} style={{ color: "#4B5563" }} />
        <span style={{ color: "#4B5563", fontSize: 13 }}>{t("recentActivityLabel")}</span>
      </div>

      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
        }}
      >
        <NotificationHistoryList filters={filters} onFiltersChange={setFilters} />
      </div>

      <NotificationFilterDrawer
        open={filtersOpen}
        onClose={() => setFiltersOpen(false)}
        filters={filters}
        onChange={setFilters}
      />
    </div>
  );
}
