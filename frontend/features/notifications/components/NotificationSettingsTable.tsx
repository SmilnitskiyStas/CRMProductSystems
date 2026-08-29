"use client";

import { useTranslations } from "next-intl";
import type { NotificationChannel, NotificationEventType } from "../types";
import {
  getEventTypeLabel,
  getChannelLabel,
  CHANNEL_ICONS,
} from "../types";
import { useNotificationSettings, useToggleNotification } from "../hooks/useNotifications";
import { Table, type TableColumn } from "@/components/ui/Table";

const ALL_EVENTS: NotificationEventType[] = [
  "stock.expiry_warning",
  "stock.expiry_critical",
  "stock.expired",
  "stock.needs_verification",
  "weekly_report",
];

const ALL_CHANNELS: NotificationChannel[] = ["telegram", "push", "email"];

function Toggle({
  checked,
  onChange,
  disabled,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      style={{
        width: 36,
        height: 20,
        borderRadius: 10,
        background: checked ? "#3B82F6" : "#374151",
        border: "none",
        cursor: disabled ? "default" : "pointer",
        position: "relative",
        transition: "background 0.2s",
        opacity: disabled ? 0.4 : 1,
        flexShrink: 0,
      }}
    >
      <span
        style={{
          position: "absolute",
          top: 2,
          left: checked ? 18 : 2,
          width: 16,
          height: 16,
          borderRadius: "50%",
          background: "#fff",
          transition: "left 0.2s",
        }}
      />
    </button>
  );
}

export function NotificationSettingsTable() {
  const t = useTranslations("Dashboard.notifications.settingsTable");
  const tEventTypes = useTranslations("Dashboard.notifications.eventTypes");
  const tChannels = useTranslations("Dashboard.notifications.channels");
  const { data: settings, isLoading } = useNotificationSettings();
  const toggle = useToggleNotification();

  if (isLoading) {
    return (
      <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 14 }}>
        {t("loading")}
      </div>
    );
  }

  const columns: TableColumn<NotificationEventType>[] = [
    {
      key: "event",
      width: "45%",
      header: t("eventColumnHeader"),
      render: (eventType) => (
        <>
          <span style={{ color: "#D1D5DB", fontSize: 13 }}>
            {getEventTypeLabel(tEventTypes, eventType)}
          </span>
          <span
            style={{
              display: "block",
              color: "#4B5563",
              fontSize: 11,
              marginTop: 2,
            }}
          >
            {eventType}
          </span>
        </>
      ),
    },
    ...ALL_CHANNELS.map((channel) => ({
      key: channel,
      header: (
        <>
          {CHANNEL_ICONS[channel]} {getChannelLabel(tChannels, channel)}
        </>
      ),
      render: (eventType: NotificationEventType) => {
        const eventSettings = settings?.[eventType] ?? {};
        const isEnabled = eventSettings[channel]?.isEnabled ?? false;
        return (
          <div style={{ display: "flex", justifyContent: "center" }}>
            <Toggle
              checked={isEnabled}
              disabled={toggle.isPending}
              onChange={(value) => toggle.mutate({ eventType, channel, isEnabled: value })}
            />
          </div>
        );
      },
    })),
  ];

  return (
    <div>
      <p style={{ color: "#6B7280", fontSize: 13, marginBottom: 20 }}>
        {t("description")}
      </p>
      <Table columns={columns} rows={ALL_EVENTS} rowKey={(eventType) => eventType} minWidth={500} />
      <p style={{ color: "#374151", fontSize: 12, marginTop: 16 }}>
        {t("footerHint")}
      </p>
    </div>
  );
}
