export type NotificationChannel = "telegram" | "push" | "email";

export type NotificationEventType =
  | "stock.expiry_warning"
  | "stock.expiry_critical"
  | "stock.expired"
  | "stock.needs_verification"
  | "weekly_report";

export interface NotificationSetting {
  id: string;
  eventType: NotificationEventType;
  channel: NotificationChannel;
  isEnabled: boolean;
}

export interface NotificationSettingsMap {
  [eventType: string]: {
    [channel in NotificationChannel]?: { id: string; isEnabled: boolean };
  };
}

export interface NotificationHistoryItem {
  id: string;
  eventType: NotificationEventType;
  channel: NotificationChannel;
  status: "sent" | "failed" | "skipped" | "pending";
  payload: string;
  createdAt: string;
}

export const EVENT_TYPE_LABELS: Record<NotificationEventType, string> = {
  "stock.expiry_warning": "Попередження про термін",
  "stock.expiry_critical": "Критичний термін",
  "stock.expired": "Прострочено",
  "stock.needs_verification": "Потребує перевірки",
  "weekly_report": "Тижневий звіт",
};

export const CHANNEL_LABELS: Record<NotificationChannel, string> = {
  telegram: "Telegram",
  push: "Push",
  email: "Email",
};

export const CHANNEL_ICONS: Record<NotificationChannel, string> = {
  telegram: "✈️",
  push: "📱",
  email: "📧",
};
