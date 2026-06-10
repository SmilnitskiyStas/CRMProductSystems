export type IntegrationService = "telegram" | "resend" | "webhook" | "prro" | "iot";

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

export interface ConfigField {
  key: string;
  label: string;
  placeholder: string;
  type: "text" | "password" | "url";
  required: boolean;
  hint?: string;
}

export interface ServiceMeta {
  service: IntegrationService;
  label: string;
  description: string;
  icon: string;
  fields: ConfigField[];
}

export const SERVICE_META: Record<IntegrationService, ServiceMeta> = {
  telegram: {
    service: "telegram",
    label: "Telegram Bot",
    description: "Сповіщення про критичні терміни та тижневі звіти через Telegram.",
    icon: "✈️",
    fields: [
      { key: "bot_token",   label: "Bot Token",      placeholder: "1234567890:AAF...", type: "password", required: true,  hint: "Отримайте у @BotFather" },
      { key: "bot_username",label: "Username бота",  placeholder: "@ShelfGuardBot",   type: "text",     required: false },
    ],
  },
  resend: {
    service: "resend",
    label: "Email (Resend)",
    description: "Email-сповіщення та тижневі звіти для менеджерів.",
    icon: "📧",
    fields: [
      { key: "api_key",    label: "API Key",       placeholder: "re_...",              type: "password", required: true },
      { key: "from_email", label: "Від кого",      placeholder: "noreply@myshop.com",  type: "text",     required: true },
      { key: "from_name",  label: "Ім'я відправника", placeholder: "ShelfGuard",       type: "text",     required: false },
    ],
  },
  webhook: {
    service: "webhook",
    label: "Webhook",
    description: "HTTP POST-виклик до вашого сервісу при виникненні подій.",
    icon: "🔗",
    fields: [
      { key: "url",    label: "URL ендпоінту", placeholder: "https://my-app.com/webhook", type: "url",      required: true },
      { key: "secret", label: "Secret",        placeholder: "my-webhook-secret",           type: "password", required: false, hint: "Підписується заголовок X-ShelfGuard-Signature" },
    ],
  },
  prro: {
    service: "prro",
    label: "ПРРО Каса",
    description: "Інтеграція з програмним РРО для реєстрації продажів (v3).",
    icon: "🖨️",
    fields: [
      { key: "api_url",    label: "URL API каси",  placeholder: "https://prro.example.com/api", type: "url",      required: true },
      { key: "api_key",    label: "API Key",        placeholder: "prro-api-key",                 type: "password", required: true },
      { key: "cashier_id", label: "ID касира",      placeholder: "cashier-001",                  type: "text",     required: false },
    ],
  },
  iot: {
    service: "iot",
    label: "IoT Сенсори",
    description: "Підключення датчиків температури та вологості (v3).",
    icon: "📡",
    fields: [
      { key: "endpoint_url", label: "Endpoint URL", placeholder: "https://iot.example.com",  type: "url",      required: true },
      { key: "api_key",      label: "API Key",       placeholder: "iot-device-key",           type: "password", required: true },
      { key: "device_ids",   label: "Device IDs",    placeholder: "dev-001, dev-002",         type: "text",     required: false, hint: "Через кому" },
    ],
  },
};

export const ALL_SERVICES: IntegrationService[] = ["telegram", "resend", "webhook", "prro", "iot"];
