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
  scope: "network" | "store";
  storeId: string | null;
  startsAt: string; // yyyy-MM-dd
  endsAt: string;
  isRecurring: boolean;
  notes: string | null;
  coefficients: EventCoefficient[];
}

export interface UpsertEventPayload {
  name: string;
  eventType: EventType;
  scope: "network" | "store";
  storeId: string | null;
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

export const EVENT_TYPE_META: Record<EventType, { label: string; color: string; bg: string }> = {
  holiday: { label: "Свято", color: "#FCA5A5", bg: "#7F1D1D" },
  promo: { label: "Акція", color: "#FDBA74", bg: "#7C2D12" },
  local_event: { label: "Місцева подія", color: "#93C5FD", bg: "#1E3A8A" },
  season_start: { label: "Сезон", color: "#86EFAC", bg: "#14532D" },
  custom: { label: "Інше", color: "#D1D5DB", bg: "#374151" },
};
