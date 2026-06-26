"use client";

import { useState } from "react";
import { CheckCheck } from "lucide-react";
import { useNotificationHistory, useMarkAsRead, useMarkAllAsRead, useMarkAsUnread } from "../hooks/useNotifications";
import { NotificationDetailDrawer } from "./NotificationDetailDrawer";
import type { NotificationHistoryItem } from "../types";
import { EVENT_TYPE_LABELS, CHANNEL_LABELS, CHANNEL_ICONS } from "../types";

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

function formatPayloadPreview(eventType: string, payload: string | null): string {
  if (!payload) return "—";
  try {
    const data = JSON.parse(payload) as Record<string, unknown>;
    switch (eventType) {
      case "weekly_report": {
        const parts: string[] = [];
        if (data.safe    != null) parts.push(`Норма: ${data.safe}`);
        if (data.warning != null) parts.push(`Попередження: ${data.warning}`);
        if (data.critical != null) parts.push(`Критично: ${data.critical}`);
        if (data.expired  != null) parts.push(`Прострочено: ${data.expired}`);
        return parts.length ? parts.join(" · ") : "Тижневий звіт";
      }
      case "stock.expiry_warning":
      case "stock.expiry_critical":
      case "stock.expired": {
        const name = data.productName ?? data.name ?? data.product;
        const days = data.daysLeft ?? data.days_left;
        const qty  = data.quantity ?? data.qty;
        if (name) {
          const info: string[] = [String(name)];
          if (days != null) info.push(Number(days) <= 0 ? "прострочено" : `${days} дн.`);
          if (qty  != null) info.push(`к-сть: ${qty}`);
          return info.join(" · ");
        }
        break;
      }
      case "stock.needs_verification": {
        const name = data.productName ?? data.name ?? data.product;
        if (name) return `${name} — потребує перевірки`;
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
    return pairs || "—";
  } catch {
    return payload.length > 80 ? payload.slice(0, 80) + "…" : payload;
  }
}

const STATUS_COLOR: Record<string, { bg: string; text: string; label: string }> = {
  sent:    { bg: "#052e16", text: "#4ADE80", label: "Надіслано" },
  failed:  { bg: "#450a0a", text: "#F87171", label: "Помилка"   },
  skipped: { bg: "#1f2937", text: "#9CA3AF", label: "Пропущено" },
  pending: { bg: "#1c1917", text: "#FACC15", label: "Очікує"    },
};

export function NotificationHistoryList() {
  const { data: history, isLoading } = useNotificationHistory();
  const markAsRead    = useMarkAsRead();
  const markAllAsRead = useMarkAllAsRead();
  const markAsUnread  = useMarkAsUnread();

  // Store only the ID so the drawer always gets the fresh item from the cache
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = history?.find((n) => n.id === selectedId) ?? null;

  const unreadCount = history?.filter((n) => !n.isRead).length ?? 0;

  function handleClick(item: NotificationHistoryItem) {
    setSelectedId(item.id);
    if (!item.isRead) markAsRead.mutate(item.id);
  }

  if (isLoading) {
    return (
      <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 14 }}>
        Завантаження…
      </div>
    );
  }

  if (!history?.length) {
    return (
      <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 14 }}>
        Сповіщень ще не надсилалось
      </div>
    );
  }

  return (
    <>
      {/* Mark all read button */}
      {unreadCount > 0 && (
        <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 12 }}>
          <button
            onClick={() => markAllAsRead.mutate()}
            disabled={markAllAsRead.isPending}
            style={{
              display: "flex", alignItems: "center", gap: 6,
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "6px 12px",
              color: "#6B7280", fontSize: 12, cursor: "pointer",
              transition: "border-color 0.1s, color 0.1s",
            }}
            onMouseEnter={(e) => {
              (e.currentTarget as HTMLElement).style.borderColor = "#374151";
              (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
            }}
            onMouseLeave={(e) => {
              (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
              (e.currentTarget as HTMLElement).style.color = "#6B7280";
            }}
          >
            <CheckCheck size={13} />
            Позначити всі як прочитані ({unreadCount})
          </button>
        </div>
      )}

      {/* List */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        {history.map((item) => {
          const statusMeta = STATUS_COLOR[item.status] ?? STATUS_COLOR.pending;
          const isUnread   = !item.isRead;

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
                    {EVENT_TYPE_LABELS[item.eventType]}
                  </span>
                  <span style={{ color: "#4B5563", fontSize: 12 }}>
                    via {CHANNEL_LABELS[item.channel]}
                  </span>
                </div>
                <div style={{
                  color: "#6B7280", fontSize: 12,
                  whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
                }}>
                  {formatPayloadPreview(item.eventType, item.payload)}
                </div>
              </div>

              {/* Status badge */}
              <div style={{
                padding: "3px 10px", borderRadius: 6,
                background: statusMeta.bg, color: statusMeta.text,
                fontSize: 11, fontWeight: 600, flexShrink: 0,
              }}>
                {statusMeta.label}
              </div>

              {/* Date */}
              <div style={{
                color: "#4B5563", fontSize: 11, flexShrink: 0,
                minWidth: 110, textAlign: "right",
              }}>
                {formatDate(item.createdAt)}
              </div>
            </div>
          );
        })}
      </div>

      {/* Detail drawer */}
      <NotificationDetailDrawer
        item={selected}
        onClose={() => setSelectedId(null)}
        onMarkUnread={(id) => markAsUnread.mutate(id)}
      />
    </>
  );
}
