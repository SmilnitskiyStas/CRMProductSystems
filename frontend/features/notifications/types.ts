export type NotificationChannel = "telegram" | "push" | "email" | "webhook";

export type NotificationEventType =
  | "stock.expiry_warning"
  | "stock.expiry_critical"
  | "stock.expired"
  | "stock.needs_verification"
  | "weekly_report"
  | "receipt.created"
  | "order.replenishment_suggested"
  | "supplier.message"
  | "supplier_agreement.signed"
  | "iot.temp_alert"
  | "iot.offline"
  | "access.temporary_expiring_soon"
  | "access.temporary_expired";

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
  payload: string | null;
  createdAt: string;
  isRead: boolean;
  readAt: string | null;
  /** NEW (TASK-339/340) — short human-readable line; null on rows created before TASK-338. */
  title: string | null;
  /** NEW (TASK-339/340) — guid; null on rows created before TASK-338 or events with no store context. */
  storeId: string | null;
  /** Client-side enrichment only (backend doesn't join it) — resolved from the locations list when available. */
  storeName?: string | null;
  /** NEW (TASK-339/340) — the notified employee's guid; null on rows created before TASK-338. */
  userId: string | null;
}

export interface NotificationHistoryFilters {
  search?: string;
  eventType?: NotificationEventType | "";
  userId?: string;
  storeId?: string;
  /** yyyy-MM-dd or ISO datetime — sent as-is to `dateFrom` */
  dateFrom?: string;
  /** yyyy-MM-dd or ISO datetime — sent as-is to `dateTo` */
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

export const EVENT_TYPE_SOURCE: Record<NotificationEventType, { service: string; actor: string }> = {
  "stock.expiry_warning":    { service: "Модуль інвентаризації", actor: "Моніторинг терміну придатності" },
  "stock.expiry_critical":   { service: "Модуль інвентаризації", actor: "Моніторинг терміну придатності" },
  "stock.expired":           { service: "Модуль інвентаризації", actor: "Контроль якості" },
  "stock.needs_verification":{ service: "Модуль інвентаризації", actor: "Контроль якості" },
  "weekly_report":           { service: "Планувальник завдань",  actor: "Автоматичний звіт" },
  "receipt.created":            { service: "Модуль надходжень", actor: "Підтвердження поставки" },
  "order.replenishment_suggested": { service: "AI замовлення", actor: "Автоматичний прогноз" },
  "supplier.message":           { service: "Маркетплейс", actor: "Чат з постачальником" },
  "supplier_agreement.signed":  { service: "Маркетплейс", actor: "Угода про співпрацю" },
  "iot.temp_alert":              { service: "IoT моніторинг", actor: "Датчик температури" },
  "iot.offline":                 { service: "IoT моніторинг", actor: "Стан пристрою" },
  "access.temporary_expiring_soon": { service: "Управління доступом", actor: "Тимчасові гранти (ADR-019)" },
  "access.temporary_expired":       { service: "Управління доступом", actor: "Тимчасові гранти (ADR-019)" },
};

export const EVENT_TYPE_LABELS: Record<NotificationEventType, string> = {
  "stock.expiry_warning": "Попередження про термін",
  "stock.expiry_critical": "Критичний термін",
  "stock.expired": "Прострочено",
  "stock.needs_verification": "Потребує перевірки",
  "weekly_report": "Тижневий звіт",
  "receipt.created": "Надходження товару",
  "order.replenishment_suggested": "AI-замовлення готове",
  "supplier.message": "Повідомлення постачальника",
  "supplier_agreement.signed": "Договір підписано",
  "iot.temp_alert": "Температурний алерт",
  "iot.offline": "Пристрій офлайн",
  "access.temporary_expiring_soon": "Тимчасовий доступ незабаром закінчується",
  "access.temporary_expired": "Тимчасовий доступ закінчився",
};

export const CHANNEL_LABELS: Record<NotificationChannel, string> = {
  telegram: "Telegram",
  push: "Push",
  email: "Email",
  webhook: "Webhook",
};

export const CHANNEL_ICONS: Record<NotificationChannel, string> = {
  telegram: "✈️",
  push: "📱",
  email: "📧",
  webhook: "🔗",
};
