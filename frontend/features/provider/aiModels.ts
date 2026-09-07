/**
 * Managed-AI Phase 2 — the provider's client-card AI section supports Claude and any
 * OpenAI-compatible endpoint. Model fields are free-text; these lists only pre-fill the
 * datalist. Keep the defaults in sync with the backend (`Claude:Model` / `OpenAI:Model`).
 */
export type AiProvider = "claude" | "openai";

export const AI_PROVIDERS: AiProvider[] = ["claude", "openai"];

export const DEFAULT_AI_MODELS: Record<AiProvider, string> = {
  claude: "claude-sonnet-4-6",
  openai: "gpt-4o-mini",
};

export const SUGGESTED_AI_MODELS: Record<AiProvider, string[]> = {
  claude: ["claude-sonnet-4-6", "claude-opus-4-1", "claude-haiku-4-5"],
  openai: ["gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1"],
};

/** Back-compat: TenantDetailPanel imported this before Phase 2. */
export const DEFAULT_AI_MODEL = DEFAULT_AI_MODELS.claude;
