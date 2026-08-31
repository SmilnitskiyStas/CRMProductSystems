"use client";

import { useEffect } from "react";
import { useTranslations, useLocale } from "next-intl";
import { X, CheckCircle, Clock, Send, AlertTriangle, RotateCcw } from "lucide-react";
import type { NotificationHistoryItem } from "../types";
import {
  getEventTypeLabel,
  getChannelLabel,
  CHANNEL_ICONS,
  getEventTypeSource,
} from "../types";

interface Props {
  item: NotificationHistoryItem | null;
  onClose: () => void;
  onMarkUnread?: (id: string) => void;
}

function formatDateFull(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function parsePayload(raw: string | null): Record<string, unknown> | null {
  if (!raw) return null;
  try { return JSON.parse(raw) as Record<string, unknown>; } catch { return null; }
}

const STATUS_ICON: Record<string, { icon: React.ReactNode; color: string }> = {
  sent:    { icon: <CheckCircle size={14} />, color: "#4ADE80" },
  failed:  { icon: <AlertTriangle size={14} />, color: "#F87171" },
  skipped: { icon: <Clock size={14} />, color: "#9CA3AF" },
  pending: { icon: <Clock size={14} />, color: "#FACC15" },
  integration_pending: { icon: <Clock size={14} />, color: "#93C5FD" },
};

export function NotificationDetailDrawer({ item, onClose, onMarkUnread }: Props) {
  const t = useTranslations("Dashboard.notifications.detailDrawer");
  const tEventTypes = useTranslations("Dashboard.notifications.eventTypes");
  const tChannels = useTranslations("Dashboard.notifications.channels");
  const tStatus = useTranslations("Dashboard.notifications.status");
  const tEventSource = useTranslations("Dashboard.notifications.eventSource");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  // Close on Escape
  useEffect(() => {
    if (!item) return;
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [item, onClose]);

  if (!item) return null;

  const source = getEventTypeSource(tEventSource, item.eventType, {
    service: t("defaultServiceFallback"),
    actor: t("defaultActorFallback"),
  });
  const statusKey  = item.status in STATUS_ICON ? item.status : "pending";
  const statusIcon = STATUS_ICON[statusKey];
  const statusLabel = tStatus(statusKey);
  const parsed    = parsePayload(item.payload);

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0, background: "rgba(0,0,0,0.5)", zIndex: 40,
        }}
      />

      {/* Drawer panel */}
      <div
        style={{
          position: "fixed", top: 0, right: 0, bottom: 0,
          width: 420, maxWidth: "100vw",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 50,
          display: "flex", flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div style={{
          padding: "20px 24px 16px",
          borderBottom: "1px solid #1F2937",
          display: "flex", alignItems: "center", justifyContent: "space-between",
          flexShrink: 0,
        }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <span style={{ fontSize: 22 }}>{CHANNEL_ICONS[item.channel]}</span>
            <div>
              <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
                {getEventTypeLabel(tEventTypes, item.eventType)}
              </div>
              <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
                {t("via")} {getChannelLabel(tChannels, item.channel)}
              </div>
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "6px 8px", cursor: "pointer", color: "#6B7280",
              display: "flex", alignItems: "center",
            }}
          >
            <X size={16} />
          </button>
        </div>

        {/* Body */}
        <div style={{ padding: 24, display: "flex", flexDirection: "column", gap: 20 }}>

          {/* Read status */}
          <div style={{
            display: "flex", alignItems: "center", gap: 8,
            padding: "8px 12px", borderRadius: 8,
            background: item.isRead ? "#052e16" : "#1c1917",
            border: `1px solid ${item.isRead ? "#166534" : "#292524"}`,
          }}>
            {item.isRead
              ? <CheckCircle size={14} style={{ color: "#4ADE80" }} />
              : <div style={{ width: 8, height: 8, borderRadius: "50%", background: "#3B82F6", flexShrink: 0 }} />
            }
            <span style={{ fontSize: 12, color: item.isRead ? "#4ADE80" : "#93C5FD", flex: 1 }}>
              {item.isRead
                ? t("viewedPrefix", { date: item.readAt ? formatDateFull(item.readAt, intlLocale) : "" })
                : t("unreadStatus")}
            </span>
            {item.isRead && onMarkUnread && (
              <button
                onClick={() => onMarkUnread(item.id)}
                title={t("markUnreadTitle")}
                style={{
                  display: "flex", alignItems: "center", gap: 5,
                  background: "transparent", border: "1px solid #1F2937",
                  borderRadius: 6, padding: "3px 8px",
                  color: "#6B7280", fontSize: 11, cursor: "pointer",
                  transition: "border-color 0.1s, color 0.1s",
                  flexShrink: 0,
                }}
                onMouseEnter={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
                  (e.currentTarget as HTMLElement).style.color = "#93C5FD";
                }}
                onMouseLeave={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
                  (e.currentTarget as HTMLElement).style.color = "#6B7280";
                }}
              >
                <RotateCcw size={11} />
                {t("markUnreadLabel")}
              </button>
            )}
          </div>

          {/* From / Source */}
          <Section title={t("fromSectionTitle")}>
            <Row label={t("serviceLabel")} value={source.service} />
            <Row label={t("sourceLabel")} value={source.actor} icon={<Send size={12} />} />
          </Section>

          {/* Delivery */}
          <Section title={t("deliverySectionTitle")}>
            <Row label={t("channelLabel")} value={`${CHANNEL_ICONS[item.channel]} ${getChannelLabel(tChannels, item.channel)}`} />
            <Row
              label={t("statusLabel")}
              value={statusLabel}
              valueStyle={{ color: statusIcon.color, display: "flex", alignItems: "center", gap: 4 }}
              icon={statusIcon.icon}
            />
            <Row label={t("sentAtLabel")} value={formatDateFull(item.createdAt, intlLocale)} />
          </Section>

          {/* Message / Payload */}
          <Section title={t("messageSectionTitle")}>
            {parsed ? (
              <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                {Object.entries(parsed).map(([k, v]) => (
                  <Row key={k} label={k} value={String(v)} />
                ))}
              </div>
            ) : (
              <div style={{
                background: "#111827", borderRadius: 8, padding: "10px 12px",
                color: "#D1D5DB", fontSize: 13, lineHeight: 1.5,
              }}>
                {item.payload ?? t("noPayload")}
              </div>
            )}
          </Section>

          {/* ID (for debugging / support) */}
          <div style={{ borderTop: "1px solid #1F2937", paddingTop: 16 }}>
            <Row label={t("idLabel")} value={item.id} valueStyle={{ color: "#374151", fontSize: 11 }} />
          </div>
        </div>
      </div>
    </>
  );
}

// ── Internal helpers ──────────────────────────────────────────────────────────

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.06em", marginBottom: 8 }}>
        {title}
      </div>
      <div style={{ background: "#111827", borderRadius: 8, padding: "4px 0", border: "1px solid #1F2937" }}>
        {children}
      </div>
    </div>
  );
}

function Row({ label, value, icon, valueStyle }: {
  label: string;
  value: string;
  icon?: React.ReactNode;
  valueStyle?: React.CSSProperties;
}) {
  return (
    <div style={{
      display: "flex", alignItems: "center", justifyContent: "space-between",
      padding: "8px 12px", gap: 12,
      borderBottom: "1px solid #1F2937",
    }}>
      <span style={{ color: "#6B7280", fontSize: 12, flexShrink: 0 }}>{label}</span>
      <span style={{ color: "#D1D5DB", fontSize: 12, textAlign: "right", display: "flex", alignItems: "center", gap: 4, ...valueStyle }}>
        {icon}{value}
      </span>
    </div>
  );
}
