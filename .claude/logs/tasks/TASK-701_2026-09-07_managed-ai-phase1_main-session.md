# TASK-701 — Керована провайдером AI-інтеграція, Фаза 1

**Status:** done · main session · DEPLOYED to prod 2026-09-07
**Commits:** `7ffefeb7` (guardrail), `5a93f3d3` (provider config), `0b298daa` (error-msg fix)
**Plan:** `.claude/plans/curried-mapping-sparkle.md` · **Design:** `.claude/docs/managed-ai-integration-plan.md`

## Що зроблено

### 1. Ізоляція тенанта (security-critical)
- Новий `IAiPromptResolver` / `AiGuardrail.Compose` (чиста статика) / `AiPromptResolver`
  (`ITenantContext` + `AppDbContext`). Обовʼязковий guardrail-префікс у системний промпт
  **усіх 6 адвайзерів** (`System = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), ct)`):
  агент працює тільки для бізнесу «{Назва}», лише з його даними, відмовляє на питання про
  конкурентів / інші компанії / ринок. `extra_instructions` (з картки провайдера) —
  між guardrail і базовим промптом. Не вимикається конфігом.
- Дані вже scoped: адвайзери читають `integration_configs` під RLS `tenant_isolation`,
  жоден не бере RLS-bypass. Нові тести: `AiGuardrailTests` (7), `AiAdvisorRlsContainmentTests`
  (рефлексія — жоден тип у `Infrastructure.AI` не приймає override-примітив),
  `AiIntegrationConfigCrossTenantRlsIntegrationTests` (real Postgres — тенант A не бачить
  ключ B; пінить KI-036 «F5»).

### 2. Провайдер керує, тенант заблокований
- `IntegrationsController`: `service ∈ {claude, openai}` → 403 на GET-detail/PUT/DELETE
  (self-serve тенанта закрито). `GET /api/integrations` (list) лишається — статус-рядок.
- `ProviderController` +4: `GET/PUT/POST test/DELETE /api/provider/tenants/{id}/ai-agent`,
  бекенд `ITenantAiConfigService` (пише `integration_configs` claude чужого тенанта через
  session `provider_bypass` — без нового примітиву; ключ через `GenericIntegrationSecrets`,
  порожній → `••••` зберігає наявний; GET віддає лише last-4).
- `IAiConnectivityTester` / `AnthropicConnectivityTester` — 1-token probe, ніколи не кидає,
  завжди 200 з ok/error. Тест `TenantAiConfigServiceTests` (6).

### 3. Frontend
- Тенант `IntegrationsTab`: `claude` прибрано з `ALL_SERVICES`; read-only рядок
  «AI-агент: Підключено / Не підключено» (provider-managed).
- Провайдерська картка `TenantDetailPanel`: секція «AI-агент» (клон патерну «План») —
  тумблер, модель, write-only ключ (`••••last4` placeholder), extra-instructions,
  «Перевірити підключення». `aiModels.ts`, hooks/api/types.
- i18n uk+en.

### 4. Дрібне (`0b298daa`)
- `AiAssistantService` / `AiOrderService` «not configured» текст → «AI-агент не налаштований.
  Зверніться до вашого провайдера…» (було посилання на прибрану картку). Тести оновлено.

## Верифікація (прод, після деплою)
- CI #349 + #350 — всі джоби + Deploy → production зелені.
- Тенант `PUT/GET /api/integrations/claude` → **403**; `GET /api/integrations` → `[]`.
- Провайдер `GET .../ai-agent` → `{isConfigured:false}`; `POST .../ai-agent/test` (порожньо)
  → `{ok:false,"Не вказано API-ключ."}`; у браузері — секція «AI AGENT» рендериться,
  «Перевірити підключення» з фейковим ключем робить справжній виклик Anthropic → «API key is invalid».
- Тенант `/settings?tab=integrations` — рядок «AI agent / Not connected / configured by your
  provider», **картки Claude немає**.
- `dotnet test` весь — 2416 green (11 + 6 нових); frontend tsc/lint/build чисто.

## Не перевірено / далі
- Guardrail «модель реально відмовляє на питання про конкурентів» — не перевірено e2e, бо
  на проді в жодного тенанта немає робочого Claude-ключа. Покрито юніт-тестами; власник може
  перевірити, вписавши ключ у картку провайдера.
- Дрібна полірувка: `/ai-agent/test` error включає сирий Anthropic-JSON — трохи багатослівно.
- **Фаза 2** (OpenAI/Codex через `IAiChatClient` + тонкий HttpClient) і **Фаза 3** (пресети) — окремо.
- Комічено через тимчасові git worktree (основна тека на гілці Codex-сесії).
