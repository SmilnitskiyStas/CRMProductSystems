# TASK-704 / TASK-705 — AI-агент: фікс keyless-конфіг + тири моделей

**Дата:** 2026-09-08 · main session · **Пов'язане:** TASK-701..703, `.claude/docs/managed-ai-integration-plan.md`

Обидва — з фідбеку власника на Фазу 3.

## TASK-704 — keyless-конфіг більше не читається як «не підключено» (`03e6c9ed`, DEPLOYED)

**Баг:** провайдер відкриває секцію «AI-агент», обирає пресет типу бізнесу, зберігає **без
API-ключа** → рядок `integration_configs` пишеться (model + extra_instructions + enabled),
але `TenantAiConfigService.GetAsync` гейтив увесь DTO на наявність `api_key` → повертав
`IsConfigured=false` + усе `null` → пресет «зникав».

- `GetAsync` — `if (config?.Config is null) continue;` замість гейта по ключу; повертає
  provider/model/baseUrl/extraInstructions, `apiKeyLast4=null` коли ключа немає.
- `AiClientFactory.ResolveAsync` — keyless enabled-рядок тепер fallback на env-ключ **того
  провайдера** (зберігає провайдера + модель + base_url рядка), а не одразу на generic
  Claude env default.
- `TenantDetailPanel` read-view — бурштиновий напис `aiStatusNoKey` коли `isConfigured &&
  !apiKeyLast4`.
- Тести: `TenantAiConfigServiceTests.Get_RowWithoutKey_StillReturnsTheSavedConfig`,
  `AiClientFactoryTests` — 2 keyless-row кейси. Бекенд AI-suite 525 green.

## TASK-705 — курований список моделей із ціновими тирами (frontend)

- `frontend/features/provider/aiModels.ts`: `SUGGESTED_AI_MODELS` `string[]` → `AiModelOption[]`
  (`{ id, tier }`, tier ∈ `budget`/`balanced`/`premium`). Набір: Claude
  `claude-haiku-4-5`/`claude-sonnet-4-6`/`claude-opus-4-1`; OpenAI
  `gpt-4o-mini`/`gpt-4.1-mini`/`gpt-4o`/`gpt-4.1`.
- `TenantDetailPanel` — під полем «Модель» ряд клікабельних чіпів `{id · тир}`; клік →
  `setAiModel(id)`. `<datalist>` лишається. Поле — вільне введення (будь-який ID).
- +i18n `Dashboard.provider.tenantDetailPanel.aiModelTier.{budget,balanced,premium}` (uk+en).
- `tsc` / `next lint` / `next build` чисто.

## Фаза 4 (дизайн, не почато)

`.claude/docs/managed-ai-phase4-plan.md` — кілька агентів на бізнес (slot-и
`analyst`/`assistant`/`consumer`), без нової таблиці (service=slot у `integration_configs`,
provider у jsonb). 4a внутрішні профілі, 4b споживчий агент. 4 відкриті питання власнику.
