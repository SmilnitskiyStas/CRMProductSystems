"use client";

import { useNotificationHistory } from "../hooks/useNotifications";
import {
  EVENT_TYPE_LABELS,
  CHANNEL_LABELS,
  CHANNEL_ICONS,
} from "../types";

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

const STATUS_COLOR: Record<string, { bg: string; text: string; label: string }> = {
  sent: { bg: "#052e16", text: "#4ADE80", label: "Надіслано" },
  failed: { bg: "#450a0a", text: "#F87171", label: "Помилка" },
  skipped: { bg: "#1f2937", text: "#9CA3AF", label: "Пропущено" },
  pending: { bg: "#1c1917", text: "#FACC15", label: "Очікує" },
};

export function NotificationHistoryList() {
  const { data: history, isLoading } = useNotificationHistory();

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
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      {history.map((item) => {
        const statusMeta = STATUS_COLOR[item.status] ?? STATUS_COLOR.pending;
        return (
          <div
            key={item.id}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "12px 16px",
              display: "flex",
              alignItems: "center",
              gap: 16,
            }}
          >
            {/* Channel icon */}
            <div
              style={{
                width: 36,
                height: 36,
                borderRadius: 8,
                background: "#1F2937",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                fontSize: 18,
                flexShrink: 0,
              }}
            >
              {CHANNEL_ICONS[item.channel]}
            </div>

            {/* Main info */}
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 2 }}>
                <span style={{ color: "#D1D5DB", fontSize: 13, fontWeight: 500 }}>
                  {EVENT_TYPE_LABELS[item.eventType]}
                </span>
                <span style={{ color: "#4B5563", fontSize: 12 }}>
                  via {CHANNEL_LABELS[item.channel]}
                </span>
              </div>
              <div
                style={{
                  color: "#6B7280",
                  fontSize: 12,
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                }}
              >
                {item.payload}
              </div>
            </div>

            {/* Status badge */}
            <div
              style={{
                padding: "3px 10px",
                borderRadius: 6,
                background: statusMeta.bg,
                color: statusMeta.text,
                fontSize: 11,
                fontWeight: 600,
                flexShrink: 0,
              }}
            >
              {statusMeta.label}
            </div>

            {/* Date */}
            <div
              style={{
                color: "#4B5563",
                fontSize: 11,
                flexShrink: 0,
                minWidth: 110,
                textAlign: "right",
              }}
            >
              {formatDate(item.createdAt)}
            </div>
          </div>
        );
      })}
    </div>
  );
}
