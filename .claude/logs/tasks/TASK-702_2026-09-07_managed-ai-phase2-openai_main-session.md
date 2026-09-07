# TASK-702 — Керована AI-інтеграція, Фаза 2 (провайдер-агностичний клієнт + OpenAI/Codex)

**Status:** done · main session · DEPLOYED to prod 2026-09-07
**Commits:** `ffe0b1b1` (крок 1 — абстракція + рефактор 6 адвайзерів) · `f65e289c` (крок 2 — OpenAI як вибір)
**Plan:** `.claude/plans/curried-mapping-sparkle.md` · **Design:** `.claude/docs/managed-ai-integration-plan.md`

## Крок 1 — `IAiChatClient` (чистий рефактор, поведінка Claude без змін)
- Нові контракти `Application/Services/`: `IAiChatClient` (`CompleteAsync(AiChatRequest)→AiChatResult`),
  `IAiClientFactory` (`ResolveAsync`/`IsConfiguredAsync`/`Create(AiProviderConfig)`),
  `AiChatRequest(SystemPrompt, UserPrompt, MaxTokens, JsonSchema?)`.
- `Infrastructure/AI/`: `AnthropicChatClient` (лифт коду адвайзера, Anthropic SDK),
  `OpenAiChatClient` (тонкий HttpClient до `{baseUrl}/chat/completions`, Bearer,
  `response_format=json_schema`; 0 нових пакетів), `AiClientFactory` (читає tenant AI-рядок
  `service ∈ {openai,claude}` під RLS → env fallback `Claude:ApiKey`/`OpenAI:ApiKey` → null).
- **6 адвайзерів**: ctor → `(IAiClientFactory, IAiPromptResolver)` (BusinessAssistant лишає
  `AppDbContext` для агрегації контексту). Прибрано `_envApiKey`/`_defaultModel`/`ApiTimeout`/
  `ResolveAsync`/`new AnthropicClient` — борг TASK-367 закрито. Structured-output адвайзери
  (`ClaudeOrderAdvisor`/`SupplierAdvisor`): `ResponseSchema()` → `const ResponseSchemaJson`
  → `AiChatRequest.JsonSchema`. `ParseAdvice`/`ParseRecommendations`/`BuildUserPrompt` —
  лишились `internal static` (тести їх викликають без конструктора).
- `AiPromptResolver` читає `extra_instructions` з claude **або** openai рядка.
- DI + `AddHttpClient("openai")`. Тести: `OpenAiChatClientTests` (5), `AiClientFactoryTests` (6).

## Крок 2 — OpenAI/Codex як вибір провайдера
- `IntegrationService.KnownServices` += `openai`; `GenericIntegrationSecrets` += `openai→api_key`.
- `ProviderDtos`: `TenantAiAgentDto`/`UpdateAiAgentRequest` += `Provider`, `BaseUrl`.
- `TenantAiConfigService`: динамічний `service` (`claude`|`openai`), при `UpdateAsync` **видаляє
  протилежний рядок** (рівно один активний AI-провайдер на тенант), `GetAsync` віддає який
  провайдер, `TestAsync` тестує обраний.
- `IAiConnectivityTester.ProbeAsync(AiProviderConfig)`; `AnthropicConnectivityTester` →
  **`AiConnectivityTester`** (через `IAiClientFactory.Create`, 1-token completion, гуманізує
  credit-balance / quota / invalid-key).
- Env: `OpenAI__ApiKey`/`OpenAI__Model` у `docker-compose.{production,staging}.yml` +
  `.env.*.example` (+ додано відсутній `CLAUDE_API_KEY` у `.env.production.example`).
- Frontend: у картці клієнта — тумблер Claude / OpenAI, поле `base_url` (тільки OpenAI),
  дефолт моделі per-provider, бейдж «OpenAI · {model}» / «Claude · {model}». Тенантський
  статус-рядок перевіряє `claude` **або** `openai`. `aiModels.ts` per-provider, i18n uk+en.

## Верифікація (прод, після деплою)
- CI (`f65e289c`) — усі 4 job + Deploy → production зелені. Локально: `dotnet test` **2423**,
  Release build, **`docker build -f backend/Dockerfile backend` зелений** (нових пакетів нема).
- Тенант `PUT /api/integrations/openai` → **403**.
- Провайдер `GET .../ai-agent` → форма з `provider`/`baseUrl`.
- Провайдер `POST .../ai-agent/test` `{provider:"openai", apiKey:"sk-fake"}` → **справжній
  виклик `api.openai.com/chat/completions`** → 401 → `{ok:false,"Невірний API-ключ."}` (тонкий
  OpenAI-клієнт працює e2e).
- У браузері: у картці клієнта тумблер Claude/OpenAI; вибір «OpenAI» → модель авто-`gpt-4o-mini`,
  зʼявляється поле «Base URL (optional)» з підказкою про Azure/Codex.
- Claude-регрес: assistant без ключа → «AI-агент не налаштований…».

## Не перевірено / далі
- Реальний OpenAI-запит з валідним ключем (немає ключа для тесту) — покрито `OpenAiChatClientTests`
  (handler-mock) + e2e-probe вище показав живий виклик.
- Structured output через OpenAI `response_format` з реальним ключем — не e2e; схема сумісна
  (обидві вже `additionalProperties:false` + `required`), покрито unit-тестом.
- **Фаза 3** (пресети промптів по типах бізнесу) — не почато.
- Комічено через тимчасові git worktree (основна тека на гілці Codex-сесії).
