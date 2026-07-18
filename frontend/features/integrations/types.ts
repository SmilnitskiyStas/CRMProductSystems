export type IntegrationService = "telegram" | "resend" | "webhook" | "prro" | "iot" | "claude";

export interface IntegrationSummary {
  service: IntegrationService;
  isEnabled: boolean;
  updatedAt: string;
}

export interface IntegrationConfig {
  id: string;
  service: IntegrationService;
  config: Record<string, unknown> | null;
  isEnabled: boolean;
  updatedAt: string;
}

export interface UpsertIntegrationRequest {
  config: Record<string, unknown>;
  isEnabled: boolean;
}

// ── Per-service config field descriptors ───────────────────────────────────
// Labels/placeholders/hints live in i18n (Dashboard.integrations.services.<service>.*,
// `useTranslations`) — see IntegrationCard.tsx / IntegrationConfigModal.tsx. Kept here
// only as the canonical field list/order + structural behavior (input type, required).

export interface ConfigField {
  key: string;
  type: "text" | "password" | "url";
  required: boolean;
}

export interface ServiceMeta {
  service: IntegrationService;
  icon: string;
  fields: ConfigField[];
}

export const SERVICE_META: Record<IntegrationService, ServiceMeta> = {
  claude: {
    service: "claude",
    icon: "🤖",
    fields: [
      { key: "api_key", type: "password", required: true },
      { key: "model",   type: "text",     required: false },
    ],
  },
  telegram: {
    service: "telegram",
    icon: "✈️",
    fields: [
      { key: "bot_token",    type: "password", required: true },
      { key: "bot_username", type: "text",     required: false },
    ],
  },
  resend: {
    service: "resend",
    icon: "📧",
    fields: [
      { key: "api_key",    type: "password", required: true },
      { key: "from_email", type: "text",     required: true },
      { key: "from_name",  type: "text",     required: false },
    ],
  },
  webhook: {
    service: "webhook",
    icon: "🔗",
    fields: [
      { key: "url",    type: "url",      required: true },
      { key: "secret", type: "password", required: false },
    ],
  },
  // NOTE: ПРРО uses a dedicated PrroConfigModal (not the generic IntegrationConfigModal).
  // The fields array here is intentionally empty; the real UI lives in
  // frontend/features/integrations/components/PrroConfigModal.tsx which talks directly to
  // GET/PUT /api/settings/prro instead of the generic /api/integrations/:service endpoint.
  prro: {
    service: "prro",
    icon: "🖨️",
    fields: [],
  },
  iot: {
    service: "iot",
    icon: "📡",
    fields: [
      { key: "endpoint_url", type: "url",      required: true },
      { key: "api_key",      type: "password", required: true },
      { key: "device_ids",   type: "text",     required: false },
    ],
  },
};

export const ALL_SERVICES: IntegrationService[] = ["claude", "telegram", "resend", "webhook", "prro", "iot"];
