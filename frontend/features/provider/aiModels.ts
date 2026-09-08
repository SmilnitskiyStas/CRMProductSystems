/**
 * Managed-AI Phase 2 — the provider's client-card AI section supports Claude and any
 * OpenAI-compatible endpoint. Model fields are free-text; these lists only pre-fill the
 * datalist + the quick-pick chips. Keep the defaults in sync with the backend
 * (`Claude:Model` / `OpenAI:Model`).
 */
export type AiProvider = "claude" | "openai";

export const AI_PROVIDERS: AiProvider[] = ["claude", "openai"];

export const DEFAULT_AI_MODELS: Record<AiProvider, string> = {
  claude: "claude-sonnet-4-6",
  openai: "gpt-4o-mini",
};

/**
 * Cost tier of a suggested model — shown next to the ID so the provider can pick a cheap
 * model for testing / high-volume light work and a premium one for deep analysis. Labels
 * live in i18n (`Dashboard.provider.tenantDetailPanel.aiModelTier.*`). Not exhaustive and
 * not enforced — any model ID can still be typed by hand.
 */
export type AiModelTier = "budget" | "balanced" | "premium";

export interface AiModelOption {
  id: string;
  tier: AiModelTier;
}

export const SUGGESTED_AI_MODELS: Record<AiProvider, AiModelOption[]> = {
  claude: [
    { id: "claude-haiku-4-5", tier: "budget" },
    { id: "claude-sonnet-4-6", tier: "balanced" },
    { id: "claude-opus-4-1", tier: "premium" },
  ],
  openai: [
    { id: "gpt-4o-mini", tier: "budget" },
    { id: "gpt-4.1-mini", tier: "budget" },
    { id: "gpt-4o", tier: "balanced" },
    { id: "gpt-4.1", tier: "premium" },
  ],
};

/** Back-compat: TenantDetailPanel imported this before Phase 2. */
export const DEFAULT_AI_MODEL = DEFAULT_AI_MODELS.claude;
