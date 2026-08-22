export type EventType = "holiday" | "promo" | "local_event" | "season_start" | "custom";

export interface EventCoefficient {
  id: string;
  scopeType: "category" | "segment" | "product";
  scopeId: string | null;
  coefficient: number;
  source: string;
}

export interface DemandEvent {
  id: string;
  name: string;
  eventType: EventType;
  scope: "network" | "store" | "stores";
  storeId: string | null;
  /** Populated (non-empty) only when `scope === "stores"`; empty array otherwise. */
  storeIds: string[];
  startsAt: string; // yyyy-MM-dd
  endsAt: string;
  isRecurring: boolean;
  notes: string | null;
  coefficients: EventCoefficient[];
}

export interface UpsertEventPayload {
  name: string;
  eventType: EventType;
  scope: "network" | "store" | "stores";
  storeId: string | null;
  /** Populated (non-empty) only when `scope === "stores"`; empty array otherwise. */
  storeIds: string[];
  startsAt: string;
  endsAt: string;
  isRecurring: boolean;
  notes: string | null;
}

export interface CreateCoefficientPayload {
  scopeType: string;
  scopeId: string | null;
  coefficient: number;
}

/**
 * Event-type labels moved to i18n as of i18n Block 10 (TASK-389): `Dashboard.events.types.*`.
 * Style-only lookup (color/bg) stays a plain constant since it's not user-facing text; the
 * key-order array and label helper mirror `getEventTypeLabel` (features/notifications/types.ts).
 */
export const EVENT_TYPES: EventType[] = ["holiday", "promo", "local_event", "season_start", "custom"];

export const EVENT_TYPE_STYLES: Record<EventType, { color: string; bg: string }> = {
  holiday: { color: "#FCA5A5", bg: "#7F1D1D" },
  promo: { color: "#FDBA74", bg: "#7C2D12" },
  local_event: { color: "#93C5FD", bg: "#1E3A8A" },
  season_start: { color: "#86EFAC", bg: "#14532D" },
  custom: { color: "#D1D5DB", bg: "#374151" },
};

const EVENT_TYPE_I18N_KEY: Record<EventType, string> = {
  holiday: "holiday",
  promo: "promo",
  local_event: "localEvent",
  season_start: "seasonStart",
  custom: "custom",
};

/** Translated event-type label. `t` must be scoped to `Dashboard.events.types`. */
export function getEventTypeLabel(t: (key: string) => string, eventType: EventType): string {
  return t(EVENT_TYPE_I18N_KEY[eventType] ?? eventType);
}
