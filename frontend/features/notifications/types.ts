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
  | "access.temporary_expired"
  | "auth.password_reset_requested";

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

/**
 * Event-type display strings moved to i18n as of i18n Block 9 (TASK-388):
 * `Dashboard.notifications.eventTypes.*` (labels), `Dashboard.notifications.eventSource.*`
 * (service/actor), `Dashboard.notifications.channels.*` (channel labels). Event-type keys
 * contain dots ("stock.expiry_warning"), which next-intl would otherwise parse as nested
 * namespace path segments, so {@link EVENT_TYPE_I18N_KEY} maps each to a flat camelCase
 * leaf key instead (same fix pattern as `ProviderLogsPanel.tsx`'s `actionLabel`). Channel
 * keys ("telegram"/"push"/"email"/"webhook") have no dots and are used as leaf keys as-is.
 */
export const EVENT_TYPE_I18N_KEY: Record<NotificationEventType, string> = {
  "stock.expiry_warning": "stockExpiryWarning",
  "stock.expiry_critical": "stockExpiryCritical",
  "stock.expired": "stockExpired",
  "stock.needs_verification": "stockNeedsVerification",
  "weekly_report": "weeklyReport",
  "receipt.created": "receiptCreated",
  "order.replenishment_suggested": "orderReplenishmentSuggested",
  "supplier.message": "supplierMessage",
  "supplier_agreement.signed": "supplierAgreementSigned",
  "iot.temp_alert": "iotTempAlert",
  "iot.offline": "iotOffline",
  "access.temporary_expiring_soon": "accessTemporaryExpiringSoon",
  "access.temporary_expired": "accessTemporaryExpired",
  "auth.password_reset_requested": "authPasswordResetRequested",
};

/** Translated event-type label. `t` must be scoped to `Dashboard.notifications.eventTypes`. */
export function getEventTypeLabel(t: (key: string) => string, eventType: NotificationEventType): string {
  const key = EVENT_TYPE_I18N_KEY[eventType];
  return t(key ?? eventType);
}

/** Translated channel label. `t` must be scoped to `Dashboard.notifications.channels`. */
export function getChannelLabel(t: (key: string) => string, channel: NotificationChannel): string {
  return t(channel);
}

/**
 * Translated {service, actor} pair describing where a notification came from. `t` must be
 * scoped to `Dashboard.notifications.eventSource`. `fallback` covers event types outside
 * {@link EVENT_TYPE_I18N_KEY} (defensive only — the `NotificationEventType` union is
 * currently exhaustive here); callers pass their own translated fallback strings.
 */
export function getEventTypeSource(
  t: (key: string) => string,
  eventType: NotificationEventType,
  fallback: { service: string; actor: string },
): { service: string; actor: string } {
  const key = EVENT_TYPE_I18N_KEY[eventType];
  if (!key) return fallback;
  return { service: t(`${key}.service`), actor: t(`${key}.actor`) };
}

export const CHANNEL_ICONS: Record<NotificationChannel, string> = {
  telegram: "✈️",
  push: "📱",
  email: "📧",
  webhook: "🔗",
};
