"use client";

import type { NotificationChannel, NotificationEventType } from "../types";
import {
  EVENT_TYPE_LABELS,
  CHANNEL_LABELS,
  CHANNEL_ICONS,
} from "../types";
import { useNotificationSettings, useToggleNotification } from "../hooks/useNotifications";

const ALL_EVENTS: NotificationEventType[] = [
  "stock.expiry_warning",
  "stock.expiry_critical",
  "stock.expired",
  "stock.needs_verification",
  "weekly_report",
];

const ALL_CHANNELS: NotificationChannel[] = ["telegram", "push", "email"];

const thStyle: React.CSSProperties = {
  padding: "10px 16px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textAlign: "left",
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  borderBottom: "1px solid #1F2937",
};

const tdStyle: React.CSSProperties = {
  padding: "12px 16px",
  borderBottom: "1px solid #111827",
  verticalAlign: "middle",
};

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
  const { data: settings, isLoading } = useNotificationSettings();
  const toggle = useToggleNotification();

  if (isLoading) {
    return (
      <div style={{ padding: 32, textAlign: "center", color: "#4B5563", fontSize: 14 }}>
        Завантаження…
      </div>
    );
  }

  return (
    <div>
      <p style={{ color: "#6B7280", fontSize: 13, marginBottom: 20 }}>
        Оберіть, які події та через які канали ви хочете отримувати сповіщення.
      </p>
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 500 }}>
          <thead>
            <tr>
              <th style={{ ...thStyle, width: "45%" }}>Подія</th>
              {ALL_CHANNELS.map((ch) => (
                <th key={ch} style={{ ...thStyle, textAlign: "center" }}>
                  {CHANNEL_ICONS[ch]} {CHANNEL_LABELS[ch]}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {ALL_EVENTS.map((eventType) => {
              const eventSettings = settings?.[eventType] ?? {};
              return (
                <tr key={eventType}>
                  <td style={tdStyle}>
                    <span style={{ color: "#D1D5DB", fontSize: 13 }}>
                      {EVENT_TYPE_LABELS[eventType]}
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
                  </td>
                  {ALL_CHANNELS.map((channel) => {
                    const isEnabled = eventSettings[channel]?.isEnabled ?? false;
                    return (
                      <td key={channel} style={{ ...tdStyle, textAlign: "center" }}>
                        <div style={{ display: "flex", justifyContent: "center" }}>
                          <Toggle
                            checked={isEnabled}
                            disabled={toggle.isPending}
                            onChange={(value) =>
                              toggle.mutate({ eventType, channel, isEnabled: value })
                            }
                          />
                        </div>
                      </td>
                    );
                  })}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <p style={{ color: "#374151", fontSize: 12, marginTop: 16 }}>
        Налаштування зберігаються автоматично при зміні перемикача.
      </p>
    </div>
  );
}
