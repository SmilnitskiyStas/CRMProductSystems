"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { CheckCheck } from "lucide-react";
import { useNotificationHistory, useMarkAsRead, useMarkAllAsRead, useMarkAsUnread } from "../hooks/useNotifications";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { NotificationDetailDrawer } from "./NotificationDetailDrawer";
import type { NotificationHistoryItem, NotificationHistoryFilters } from "../types";
import { getEventTypeLabel, getChannelLabel, CHANNEL_ICONS } from "../types";
import { Btn } from "@/components/ui/Btn";
import { Pagination } from "@/components/ui/Pagination";
import { StatusPill } from "@/components/ui/StatusPill";

function formatDate(iso: string, locale: string): string {
  const d = new Date(iso);
  return d.toLocaleString(locale, {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

function formatPayloadPreview(t: (key: string, values?: Record<string, string | number | Date>) => string, eventType: string, payload: string | null): string {
  if (!payload) return t("empty");
  try {
    const data = JSON.parse(payload) as Record<string, unknown>;
    switch (eventType) {
      case "weekly_report": {
        const parts: string[] = [];
        if (data.safe    != null) parts.push(t("safe", { value: String(data.safe) }));
        if (data.warning != null) parts.push(t("warning", { value: String(data.warning) }));
        if (data.critical != null) parts.push(t("critical", { value: String(data.critical) }));
        if (data.expired  != null) parts.push(t("expired", { value: String(data.expired) }));
        return parts.length ? parts.join(" · ") : t("weeklyReportFallback");
      }
      case "stock.expiry_warning":
      case "stock.expiry_critical":
      case "stock.expired": {
        const name = data.productName ?? data.name ?? data.product;
        const days = data.daysLeft ?? data.days_left;
        const qty  = data.quantity ?? data.qty;
        if (name) {
          const info: string[] = [String(name)];
          if (days != null) info.push(Number(days) <= 0 ? t("expiredNow") : t("daysLeft", { days: Number(days) }));
          if (qty  != null) info.push(t("quantity", { value: String(qty) }));
          return info.join(" · ");
        }
        break;
      }
      case "stock.needs_verification": {
        const name = data.productName ?? data.name ?? data.product;
        if (name) return t("needsVerification", { name: String(name) });
        break;
      }
    }
    // Fallback: show key=value pairs, skip tenantId/storeId noise
    const SKIP = new Set(["tenantId", "storeId", "id", "tenantid", "storeid"]);
    const pairs = Object.entries(data)
      .filter(([k]) => !SKIP.has(k))
      .slice(0, 4)
      .map(([k, v]) => `${k}: ${v}`)
      .join(", ");
    return pairs || t("empty");
  } catch {
    return payload.length > 80 ? payload.slice(0, 80) + "…" : payload;
  }
}

const STATUS_COLOR: Record<string, { bg: string; text: string }> = {
  sent:    { bg: "#052e16", text: "#4ADE80" },
  failed:  { bg: "#450a0a", text: "#F87171" },
  skipped: { bg: "#1f2937", text: "#9CA3AF" },
  pending: { bg: "#1c1917", text: "#FACC15" },
  integration_pending: { bg: "#172554", text: "#93C5FD" },
};

interface Props {
  filters: NotificationHistoryFilters;
  onFiltersChange: (updater: (prev: NotificationHistoryFilters) => NotificationHistoryFilters) => void;
}

export function NotificationHistoryList({ filters, onFiltersChange }: Props) {
  const t = useTranslations("Dashboard.notifications.history");
  const tPayload = useTranslations("Dashboard.notifications.history.payload");
  const tEventTypes = useTranslations("Dashboard.notifications.eventTypes");
  const tChannels = useTranslations("Dashboard.notifications.channels");
  const tStatus = useTranslations("Dashboard.notifications.status");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: result, isLoading, isFetching } = useNotificationHistory(filters);
  const { data: locations } = useLocations();
  const markAsRead    = useMarkAsRead();
  const markAllAsRead = useMarkAllAsRead();
  const markAsUnread  = useMarkAsUnread();

  const history = result?.items ?? [];
  const page = result?.page ?? filters.page ?? 1;
  const pageSize = result?.pageSize ?? filters.pageSize ?? 20;
  const totalCount = result?.totalCount ?? 0;
  const totalPages = result?.totalPages ?? Math.max(1, Math.ceil(totalCount / pageSize));

  const storeNameById = new Map((locations ?? []).map((l) => [l.id, l.name]));

  // Store only the ID so the drawer always gets the fresh item from the cache
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = history.find((n) => n.id === selectedId) ?? null;

  const unreadCount = history.filter((n) => !n.isRead).length;

  function handleClick(item: NotificationHistoryItem) {
    setSelectedId(item.id);
    if (!item.isRead) markAsRead.mutate(item.id);
  }

  function goToPage(next: number) {
    onFiltersChange((prev) => ({ ...prev, page: next }));
  }

  if (isLoading) {
    return (
      <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 14 }}>
        {t("loading")}
      </div>
    );
  }

  if (!history.length) {
    return (
      <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 14 }}>
        {t("empty")}
      </div>
    );
  }

  return (
    <>
      {/* Mark all read button */}
      {unreadCount > 0 && (
        <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 12 }}>
          <Btn
            size="sm"
            variant="ghost"
            icon={<CheckCheck size={13} />}
            onClick={() => markAllAsRead.mutate()}
            disabled={markAllAsRead.isPending}
          >
            {t("markAllReadButton", { count: unreadCount })}
          </Btn>
        </div>
      )}

      {/* List */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        {history.map((item) => {
          const statusMeta = STATUS_COLOR[item.status] ?? STATUS_COLOR.pending;
          const statusLabel = tStatus(item.status in STATUS_COLOR ? item.status : "pending");
          const isUnread   = !item.isRead;
          const storeName  = item.storeId ? storeNameById.get(item.storeId) : undefined;
          const description = item.title || formatPayloadPreview(tPayload, item.eventType, item.payload);

          return (
            <div
              key={item.id}
              onClick={() => handleClick(item)}
              style={{
                background: isUnread ? "#0f172a" : "#111827",
                border: `1px solid ${isUnread ? "#1e3a5f" : "#1F2937"}`,
                borderRadius: 10,
                padding: "12px 16px",
                display: "flex", alignItems: "center", gap: 14,
                cursor: "pointer",
                transition: "border-color 0.1s, background 0.1s",
                position: "relative",
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.borderColor = isUnread ? "#2563EB" : "#374151";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.borderColor = isUnread ? "#1e3a5f" : "#1F2937";
              }}
            >
              {/* Unread dot */}
              {isUnread && (
                <div style={{
                  position: "absolute", top: 10, left: 8,
                  width: 6, height: 6, borderRadius: "50%",
                  background: "#3B82F6", flexShrink: 0,
                }} />
              )}

              {/* Channel icon */}
              <div style={{
                width: 36, height: 36, borderRadius: 8,
                background: "#1F2937",
                display: "flex", alignItems: "center", justifyContent: "center",
                fontSize: 18, flexShrink: 0, marginLeft: 6,
              }}>
                {CHANNEL_ICONS[item.channel]}
              </div>

              {/* Main info */}
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 3 }}>
                  <span style={{
                    color: isUnread ? "#E8EDF5" : "#D1D5DB",
                    fontSize: 13,
                    fontWeight: isUnread ? 600 : 500,
                  }}>
                    {getEventTypeLabel(tEventTypes, item.eventType)}
                  </span>
                  <span style={{ color: "#4B5563", fontSize: 12 }}>
                    {t("via")} {getChannelLabel(tChannels, item.channel)}
                  </span>
                  {storeName && (
                    <span style={{ color: "#4B5563", fontSize: 12 }}>
                      · {storeName}
                    </span>
                  )}
                </div>
                <div style={{
                  color: "#6B7280", fontSize: 12,
                  whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
                }}>
                  {description}
                </div>
              </div>

              {/* Status badge */}
              <StatusPill bg={statusMeta.bg} text={statusMeta.text} style={{ flexShrink: 0 }}>
                {statusLabel}
              </StatusPill>

              {/* Date */}
              <div style={{
                color: "#4B5563", fontSize: 11, flexShrink: 0,
                minWidth: 110, textAlign: "right",
              }}>
                {formatDate(item.createdAt, intlLocale)}
              </div>
            </div>
          );
        })}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPageChange={goToPage} />
      )}

      {/* Detail drawer */}
      <NotificationDetailDrawer
        item={selected}
        onClose={() => setSelectedId(null)}
        onMarkUnread={(id) => markAsUnread.mutate(id)}
      />
    </>
  );
}
