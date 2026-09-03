export type NotificationChannel = "telegram" | "push" | "email" | "webhook";
export type NotificationHistoryChannel = NotificationChannel | "messenger" | "sms";

export type CustomerMessageChannel = "push" | "messenger" | "sms";
import type { MarketingAnalyticsPeriodPreset, RfmSegmentKey } from "@/features/marketing-analytics/types";

export type CustomerMessageAudience = "all_customers" | "loyalty_members" | "rfm_segment" | "purchase_history";
export type MessengerProvider = "telegram" | "viber" | "whatsapp";
export type CustomerMessageContentType = "promotion" | "banner" | "catalog";
export type CustomerMessageDeliveryMode = "draft" | "send_now" | "scheduled";

export interface CreateCustomerMessageRequest {
  title: string;
  message: string;
  audience: CustomerMessageAudience;
  channels: CustomerMessageChannel[];
  messengerProvider?: MessengerProvider;
  content?: { type: CustomerMessageContentType; id: string };
  rfmAudience?: {
    segment: RfmSegmentKey;
    period: MarketingAnalyticsPeriodPreset;
    from?: string;
    to?: string;
    storeIds: string[];
    estimatedRecipients: number;
  };
  purchaseAudience?: {
    from: string;
    to: string;
    storeIds: string[];
    terms: Array<{ kind: "Text" | "Category"; text: string | null; categoryId: string | null }>;
    mode: "Any" | "All";
    minQuantity: number | null;
    minAmount: number | null;
    estimatedRecipients: number;
  };
  deliveryMode: CustomerMessageDeliveryMode;
  scheduledAt?: string;
}

export interface CreateCustomerMessageResult {
  campaignId: string;
  queuedChannels: number;
  status: "draft" | "scheduled" | "integration_pending";
}

export interface CustomerMessageCampaignItem {
  id: string;
  title: string;
  message: string;
  audienceSource: CustomerMessageAudience;
  audienceDefinition: string;
  channels: CustomerMessageChannel[];
  messengerProvider: MessengerProvider | null;
  contentType: CustomerMessageContentType | null;
  contentId: string | null;
  contentTitle: string | null;
  contentImageUrl: string | null;
  deliveryMode: CustomerMessageDeliveryMode;
  scheduledAt: string | null;
  submittedAt: string | null;
  estimatedRecipients: number;
  resolvedRecipients: number;
  status: "integration_pending" | "draft" | "scheduled" | "sending" | "completed" | "failed";
  createdAt: string;
}

export interface CustomerMessageChannelSummary {
  channel: CustomerMessageChannel;
  status: string;
  recipientCount: number;
  sentCount: number;
  failedCount: number;
  pendingCount: number;
}

export interface CustomerMessageCampaignDetail {
  campaign: CustomerMessageCampaignItem;
  channels: CustomerMessageChannelSummary[];
  totalDeliveries: number;
  sentCount: number;
  failedCount: number;
  pendingCount: number;
  providersConnected: boolean;
}

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
  | "auth.password_reset_requested"
  | "customer_message.created"
  | "marketplace_order.created"
  | "marketplace_order.shipped"
  | "marketplace_order.delay_reason_added"
  | "marketplace_order.delivery_rescheduled";

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
  channel: NotificationHistoryChannel;
  status: "sent" | "failed" | "skipped" | "pending" | "integration_pending";
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
  "customer_message.created": "customerMessageCreated",
  "marketplace_order.created": "marketplaceOrderCreated",
  "marketplace_order.shipped": "marketplaceOrderShipped",
  "marketplace_order.delay_reason_added": "marketplaceOrderDelayReasonAdded",
  "marketplace_order.delivery_rescheduled": "marketplaceOrderDeliveryRescheduled",
};

/** Translated event-type label. `t` must be scoped to `Dashboard.notifications.eventTypes`. */
export function getEventTypeLabel(t: (key: string) => string, eventType: NotificationEventType): string {
  const key = EVENT_TYPE_I18N_KEY[eventType];
  return t(key ?? eventType);
}

/** Translated channel label. `t` must be scoped to `Dashboard.notifications.channels`. */
export function getChannelLabel(t: (key: string) => string, channel: NotificationHistoryChannel): string {
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

export const CHANNEL_ICONS: Record<NotificationHistoryChannel, string> = {
  telegram: "✈️",
  push: "📱",
  email: "📧",
  webhook: "🔗",
  messenger: "💬",
  sms: "✉️",
};
