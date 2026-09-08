# TASK-709 — Керована AI-інтеграція, Фаза 4b-backend: споживчий AI-консультант

**Дата:** 2026-09-08 · main session · **Дизайн:** `.claude/docs/managed-ai-phase4-plan.md` §4b
**Пов'язане:** TASK-707 (4a — slot `ai_consumer` + guardrail-варіант уже там), ADR-015

## Рішення власника (AskUserQuestion, 2026-09-08)
- Обсяг: **4b-backend зараз, mobile-чат пізніше** (`mobile/` — територія Codex-сесії).
- Дані агента: **власне + публічне** (loyalty цього споживача в цьому тенанті + історія покупок
  + публічний каталог/наявність + правила програми + магазини).
- Аудит: **метадані + обрізаний текст** (перші ~200 симв. prompt/response), ретеншн 90 днів.

## Що зроблено (backend-only, `mobile/` не чіпається)

**`Application/Features/ConsumerAssistant/`** (навмисно НЕ `Infrastructure.AI` — щоб
`AiAdvisorRlsContainmentTests` не тригерився на `ITenantSessionOverride`):
- `IConsumerAssistantService.AskAsync(consumerAccountId, tenantId, message)` →
  `(ConsumerAssistantAnswer? {Text,Model,TokensUsed}, error, statusCode)`.
- `ConsumerAssistantService`:
  1. `ITenantSessionOverride.ExecuteAsync(tenantId, …)` читає `integration_configs`
     `Service=="ai_consumer" && IsEnabled` — санкціонований примітив для споживчої сесії
     (нема `app.tenant_id`), як `ConsumerContentService`. Нема рядка / нема ключа й env → **404**.
  2. Споживчо-безпечні прямі виклики `ILoyaltyService` (поза override):
     `GetMembershipsForConsumerAsync` / `GetTierProgressAsync` / `GetTierLadderForConsumerAsync` /
     `GetHistoryAsync(1,10)` / `GetNetworksForConsumerAsync`. Каталог —
     `IConsumerContentService.GetCatalogAsync(tenantId, preferredStoreId, search=message, …, 15)`
     (керує власною tx → не з-під override).
  3. `IAiPromptResolver.Compose(tenantName, extra_instructions, base, AiSlot.Consumer)` —
     **нова синхронна pure перевантаження** (`WrapSystemPromptAsync` мовчки скинув би guardrail
     для null `ITenantContext`).
  4. `IAiClientFactory.CreateOrEnv(provider, apiKey|null, model, baseUrl)` — **нова**: явні
     креди з fallback на env-ключ провайдера. `CompleteAsync(MaxTokens 1024)`; помилка → **502**.
  5. Аудит: 2-й `ITenantSessionOverride.ExecuteAsync` → `IConsumerAiRequestRepository.AddAsync`
     (метадані + `Excerpt` 200 симв.).

**`ConsumerAssistantController`** `POST /api/consumer/assistant/{tenantId}/ask` — `[Authorize]`
(споживчий JWT, `ResolveConsumerAccountId()`), `[EnableRateLimiting("consumer-ai")]`.
**Gate = сам факт налаштованого/enabled `ai_consumer` slot** (нового feature-flag не додавали —
App Builder / mobile-config веде Codex).

**`consumer_ai_requests`** — нова таблиця, ентіті `ConsumerAiRequest`, репо, AppDbContext-мапінг,
міграція `20260908122806_AddConsumerAiRequests` з канонічною RLS-тріадою + `consumer_self_access`
(INSERT іде з-під `ITenantSessionOverride` → `tenant_isolation` WITH CHECK проходить).

**`Program.cs`** — policy `consumer-ai`: fixed-window **6 запитів / 3 хв** партиціонований по
`consumer_account_id` (fallback IP).

## Перевірка
- `dotnet build` (Api+Tests) чисто; **повний backend suite 2442 green** (+7 нових).
- `ConsumerAssistantServiceTests` — gate (empty/404 tenant/no-row/disabled/no-key), guardrail-wiring
  (`Compose(..., AiSlot.Consumer)` received), model call, аудит-write, 502-on-throw.
- `docker build -f backend/Dockerfile backend` — (перевіряється).
- Прод: після деплою — smoke роутів (401/404), authenticated flow не роблю (потрібен consumer JWT).

## Не зроблено / далі
- **4b-mobile** — чат-екран у споживчому застосунку. Контракт: `POST .../ask` `{message}` →
  `{text, model, tokensUsed}`; 404 = не налаштовано, 429 = rate-limit. Mobile-власнику.
- Ретеншн-джоб (`consumer_ai_requests` >90 днів) — follow-up для `/worker`.
- `backend/openapi.json` регенерувати (нові ендпоінти 4a+4b).
