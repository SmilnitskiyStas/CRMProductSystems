# TASK-707 — Керована AI-інтеграція, Фаза 4a: кілька агентів на бізнес (slot-и)

**Дата:** 2026-09-08 · main session, поетапно · **Дизайн:** `.claude/docs/managed-ai-phase4-plan.md`
**Пов'язане:** TASK-701..705, ADR-015

## Що зроблено

Один AI-агент на тенант → **три slot-и профілів**, кожен зі своїм провайдером/моделлю/
ключем/пресетом:
- `analyst` — `ClaudeOrderAdvisor`, `SupplierAdvisor`, `BusinessAssistantAdvisor` (сильна модель)
- `assistant` — `MarketingAdvisor`, `PriceSegmentAdvisor`, `PostCampaignAdvisor` (дешева модель)
- `consumer` — плюмбінг для 4b (guardrail-варіант + фабрика + картка), споживчого адвайзера ще немає

**Без нової таблиці:** `integration_configs`, `Service` = slot-ключ (`ai_analyst`/`ai_assistant`/
`ai_consumer`), `provider` переїхав усередину jsonb `Config`.

### Крок 1 (`274a0dd0`) — resolution layer
- `AiSlot` enum + `AiSlots.ServiceKey()`/`TryFromService()` (`Application/Services`)
- `IAiClientFactory.ResolveAsync(AiSlot)` / `IsConfiguredAsync(AiSlot)`;
  `IAiPromptResolver.WrapSystemPromptAsync(base, AiSlot)`
- `AiClientFactory`: рядок slot-у (provider з jsonb) → keyless→env для того провайдера →
  slot без рядка → `analyst` → env
- `AiPromptResolver`: `extra_instructions` slot-у (fallback на analyst); `AiGuardrail`
  += посилений `AiSlot.Consumer` prefix (текст: «розмовляєш з КЛІЄНТОМ, лише публічне + власне»)
- 6 адвайзерів: `private const AiSlot Slot = …` (Analyst×3, Assistant×3)
- `KnownServices` / `GenericIntegrationSecrets` / `IntegrationsController` 403 += 3 slot-и
- **міграція** `20260908084725_MigrateAiConfigToSlots` — data-only, `claude`/`openai` рядок →
  `ai_analyst` + `provider` у jsonb; idempotent, reversible; нема змін схеми

### Крок 2 (`5c424975`) — provider API
- `ITenantAiConfigService`/`TenantAiConfigService` per-slot: `GetAllAsync` (3), `GetAsync(slot)`,
  `UpdateAsync(slot)`, `TestAsync(slot)`, `DeleteAsync(slot)`. Провайдер у jsonb; більше **нема**
  «видалити протилежний рядок» — кожен slot незалежний.
- `TenantAiAgentDto` += `Slot`
- `ProviderController`: `GET /ai-agents`, `GET/PUT/POST test/DELETE /ai-agents/{slot}` (slot
  парситься case-insensitive → 404); legacy `/ai-agent` (однина) → alias на `analyst`
- тенантська вкладка «Інтеграції»: connected = будь-який enabled `ai_*` (або legacy `claude`)

### Крок 3 (frontend)
- `provider/types.ts`: `AiSlot` + `AI_SLOTS`; `TenantAiAgentDto` += `slot`
- `api/provider.ts` + `hooks/useProvider.ts`: `getAiAgents` (масив), `updateAiAgent(slot, body)`,
  `testAiAgent(slot, body)`, `deleteAiAgent(slot)`; `useTenantAiAgents`
- `TenantDetailPanel`: секція «AI-агенти» — таб-стрічка (Аналітик/Асистент/Споживчий, ● =
  налаштовано) над одним редактором; state per-slot; save/test/preset несуть slot
- `aiPromptPresets.ts`: `aiPromptPreset(slot, bt)` = `SLOT_TONE[slot]` + `AI_PROMPT_PRESETS[bt]`
- i18n `tenantDetailPanel.aiSlot.*` / `aiSlotHint.*` (uk+en); `aiSectionTitle` → «AI-АГЕНТИ»

## Перевірка
- `dotnet build` (Api+Tests) чисто; **повний backend suite 2435 green** (крок 2).
- Frontend `tsc` / `next lint` / `next build` чисто.
- Cross-tenant RLS integration тест оновлено на `ai_analyst` (soft-skip у CI, без Postgres).
- Прод-верифікація — після деплою (provider login → картка клієнта → «AI-агенти» → таби).

## Не зроблено / далі
- **4b** — споживчий агент (новий адвайзер + `IConsumerContext` + rate-limit + consumer-ендпоінт
  + мобільний UI + аудит-лог). Guardrail/фабрика/картка вже готові.
- `backend/openapi.json` регенерувати (нові ендпоінти).
- Legacy `/ai-agent` alias прибрати за 1–2 релізи.
