# План: керована AI-інтеграція — Фаза 4 («кілька агентів на бізнес»)

**Статус:** **4a DEPLOYED prod 2026-09-08** (TASK-707, `274a0dd0`/`754a719b`/`260f6401`).
**4b-backend реалізовано** (TASK-709 — `ConsumerAssistantService` + `/api/consumer/assistant/
{tenantId}/ask` + rate-limit `consumer-ai` + `consumer_ai_requests` аудит-таблиця);
**4b-mobile (чат-екран) — не почато, mobile-власнику.**
Продовження `.claude/docs/managed-ai-integration-plan.md` (Фази 1–3 — DEPLOYED).

**Реалізація 4a (де відхилилась від дизайну нижче):**
- Frontend — не 3 підсекції, а **таб-стрічка** (Аналітик / Асистент / Споживчий) над одним
  редактором у секції «AI-агенти» картки клієнта (`TenantDetailPanel`).
- Пресети per-slot — **тон slot-у + базовий контекст типу бізнесу** (`aiPromptPreset(slot, bt)`
  у `aiPromptPresets.ts`), не 27 окремих текстів.
- `consumer` slot + `AiGuardrail` consumer-варіант **плюмбляться в 4a** (провайдер може
  зберегти конфіг), споживчий адвайзер/контекст/UI — 4b.
- Legacy `/api/provider/tenants/{id}/ai-agent` (однина) лишається як alias на `analyst`.

**Рішення власника по 4 відкритих питаннях (2026-09-08):**
1. Розбивка адвайзерів — **3+3 як нижче** (`analyst` = order/supplier/business-assistant;
   `assistant` = marketing/price-segment/post-campaign). Окремого `chat` slot немає.
2. Пресети — **окремо на кожен slot** (не спільний пресет типу бізнесу).
3. Глобальний дефолт slot-у «на всіх тенантів» — **ні**, кожен тенант налаштовується окремо.
4. `consumer` (4b) — **одразу після 4a** (не чекати на прод-фідбек від 4a).

## Мотивація

Один агент на бізнес (Фази 1–3: один рядок `integration_configs`, один провайдер/модель/
пресет, спільний для всіх 6 адвайзерів) не покриває різні сценарії:

- **Внутрішній глибокий аналіз** — автозамовлення, бізнес-асистент, постачальники. Рідкі
  запити, потрібна сильна модель, довгі структуровані відповіді.
- **Внутрішні легкі пояснення** — маркетинг, цінові сегменти, підсумки кампаній. Часті
  короткі запити (1024 токени), дешева модель — ок.
- **Споживчий консультант** у мобільному застосунку для кінцевих клієнтів бізнесу — зовсім
  інша аудиторія (клієнт, не персонал), найжорсткіша ізоляція, високий обсяг, rate-limiting,
  лише публічні дані + власні дані цього клієнта.

Рішення: **профілі агента (slot-и)** — тенант може мати кілька AI-конфігів, кожен зі своїм
провайдером/моделлю/пресетом, прив'язаних до призначення.

## Slot-и

| Slot | Хто споживає | Модель (типово) | Guardrail |
|---|---|---|---|
| `analyst` | `ClaudeOrderAdvisor`, `SupplierAdvisor`, `BusinessAssistantAdvisor` | сильна (sonnet / opus) | стандартний (лише бізнес, відмова на конкурентів/ринок) |
| `assistant` | `MarketingAdvisor`, `PriceSegmentAdvisor`, `PostCampaignAdvisor` | дешева (haiku / gpt-4o-mini) | стандартний |
| `consumer` | новий `ConsumerAssistantAdvisor` (споживчий / loyalty застосунок) | дешева + швидка | **посилений** — розмова з КЛІЄНТОМ бізнесу |

**Fallback:** slot без власного enabled-конфігу → `analyst` slot → env-ключ. Провайдер не
мусить налаштовувати всі три.

## Модель даних — без нової таблиці

Лишаємо `integration_configs`; slot стає значенням `Service`:
`ai_analyst` / `ai_assistant` / `ai_consumer` (замість `claude` / `openai`).
`provider` (`"claude"|"openai"`) переїжджає **всередину** jsonb `Config`:

```jsonc
{ "provider": "claude", "api_key": "…", "model": "…", "base_url": null, "extra_instructions": null }
```

- `UNIQUE (TenantId, Service)` природно дає рівно один рядок на slot.
- RLS-політики на `integration_configs` (`tenant_isolation` + `provider_bypass` +
  `worker_bypass`) **вже є** — нова таблиця їх дублювала б.
- `IntegrationService.KnownServices` += 3 slot-и; `GenericIntegrationSecrets.SecretFieldByService`
  += 3 slot-и → `api_key` (маскування без змін).
- `IntegrationsController` вже 403-ить `claude`/`openai` тенанту — додати 3 slot-и до того ж списку.

**Міграція (дані, не схема):** для кожного рядка `service ∈ {claude, openai}`:
`UPDATE service → 'ai_analyst'`, `Config = jsonb_set(Config, '{provider}', to_jsonb(старий service))`.
Ідемпотентна, зворотна. Один `ai_analyst` рядок на тенант після backfill (Фаза 2 вже
гарантувала максимум один claude/openai рядок).

## Бекенд

- `enum AiSlot { Analyst, Assistant, Consumer }` (+ `ServiceKey()` → `"ai_analyst"` …).
- `IAiClientFactory.ResolveAsync(AiSlot slot, ct)` — рядок за `Service == slot.ServiceKey()`
  → keyless → env для його `provider` (як фікс `03e6c9ed`) → `slot != Analyst` ? спроба
  `Analyst` : → env. `IsConfiguredAsync(AiSlot)`.
- `IAiPromptResolver.WrapSystemPromptAsync(basePrompt, AiSlot slot, ct)` — guardrail-варіант
  за slot (`consumer` → посилений текст), `extra_instructions` того ж slot-рядка.
- 6 наявних адвайзерів: кожен передає свій slot — ~1 рядок зміни на адвайзер
  (`_ai.ResolveAsync(AiSlot.Analyst, ct)` / `AiSlot.Assistant`).
- `TenantAiConfigService` → `TenantAiAgentsService`: CRUD по slot.
  `GET /api/provider/tenants/{id}/ai-agents` → масив (3 slot-и, кожен `{slot, isConfigured,
  isEnabled, provider, model, apiKeyLast4, baseUrl, extraInstructions, updatedAt}`);
  `PUT|POST test|DELETE .../ai-agents/{slot}`. Старі `.../ai-agent` (однина) → лишити як
  alias на `analyst` slot на 1–2 релізи, потім прибрати.
- `AiAdvisorRlsContainmentTests` — розширити на нові типи; cross-tenant RLS integration-тест
  на `ai_*` service-значеннях.

## Consumer агент (Фаза 4b — уточнений план після розвідки consumer-інфри)

**Розвідка (2026-09-08):** consumer-стек уже є — `ConsumerAccount` + `ConsumerAuthController`
(JWT-claim `consumer_account_id`, `[AllowAnonymous]` auth-роут, окремо від staff `/api/auth`),
`ConsumerLoyaltyController`/`ConsumerProfileController`/`ConsumerCatalogEventsController`/…
(`[Authorize]`, `ResolveConsumerAccountId()` per-controller, дані per `{tenantId}`),
rate-limiter policy-и в `Program.cs` (`AddPolicy` + `GetFixedWindowLimiter`,
`[EnableRateLimiting("…")]`), `[RequireConsumerFeature("loyalty")]` фільтр.
**Споживчий UI = `mobile/`** (React Native: `mobile/app/(auth)/consumer-login.tsx`,
`mobile/app/(personal)/`, `mobile/features/consumer-*`) — не веб. `frontend/.../consumer-app/`
— це App Builder для тенанта, не сам застосунок.

### ⚠️ Два блокери, які треба вирішити в дизайні 4b

1. **RLS: споживча сесія не може прочитати per-tenant `ai_consumer` конфіг.** `AiClientFactory`
   резолвить `integration_configs` під RLS `tenant_isolation` через `ITenantContext` (claim
   `tenant_id`) — у споживчому JWT його НЕМА (сесія крос-тенантна за дизайном). Треба або
   окремий `IConsumerAiConfigResolver` під scoped `ITenantSessionOverride`
   (SET LOCAL app.tenant_id = X лише для читання конфігу → передати явний `AiProviderConfig`
   у фабрику через `Create()`), або новий bypass-primitive. `AiAdvisorRlsContainmentTests`
   забороняє AI-типам брати RLS-override — тому резолвер конфігу має бути **не в неймспейсі
   `Infrastructure.AI`**, а окремим сервісом, що віддає креди фабриці. **Це auth/RLS-межа —
   потрібен обережний дизайн + security-review.**
2. **`mobile/` — територія Codex-сесії** (memory `shelfguard-mobile-owned-by-other-agent`;
   зараз активна робота: `feat(consumer-app): …`, гілка `codex-consumer-app-analytics-complete`).
   Мобільний чат-екран 4b **не робити** в цій сесії — координувати з mobile-власником або
   окрема задача після backend-частини.

### 4b-backend — ✅ РЕАЛІЗОВАНО (TASK-709)

**Рішення власника (2026-09-08):** дані = власне + публічне; аудит = метадані + обрізаний
текст; напрям = backend зараз, mobile пізніше.

- **`Application/Features/ConsumerAssistant/`** (НЕ `Infrastructure.AI` — щоб не тригерити
  `AiAdvisorRlsContainmentTests`): `IConsumerAssistantService.AskAsync(consumerAccountId,
  tenantId, message)`.
  1. `ITenantSessionOverride.ExecuteAsync(tenantId, …)` — читає `integration_configs`
     `Service == "ai_consumer" && IsEnabled` (споживча сесія не має `app.tenant_id` — це
     санкціонований примітив, як `ConsumerContentService`). Нема рядка / нема ключа й env →
     404 «недоступний».
  2. Поза override, споживчо-безпечні прямі виклики `ILoyaltyService`:
     `GetMembershipsForConsumerAsync` (баланс, preferred store), `GetTierProgressAsync`,
     `GetTierLadderForConsumerAsync`, `GetHistoryAsync` (10), `GetNetworksForConsumerAsync`
     (магазини+адреси). Каталог — `IConsumerContentService.GetCatalogAsync(tenantId,
     preferredStoreId, search: message, …, 15)` (керує власною tx — не викликати з override).
  3. Промпт: `IAiPromptResolver.Compose(tenantName, extra_instructions, base, AiSlot.Consumer)`
     — нова **синхронна pure** перевантаження (звичайний `WrapSystemPromptAsync` мовчки
     скинув би guardrail для null-`ITenantContext`).
  4. `IAiClientFactory.CreateOrEnv(provider, apiKey|null, model, baseUrl)` — нова: явні креди
     з fallback на env-ключ провайдера. `CompleteAsync(MaxTokens 1024)`. Помилка моделі → 502.
  5. Аудит: 2-й `ITenantSessionOverride.ExecuteAsync` пише `consumer_ai_requests`
     (метадані + перші 200 симв. prompt/response).
- **`ConsumerAssistantController`** `POST /api/consumer/assistant/{tenantId}/ask`,
  `[Authorize]` (споживчий JWT), `[EnableRateLimiting("consumer-ai")]`. **Gate = сам факт
  налаштованого `ai_consumer` slot** — нового feature-flag не додавали (щоб не чіпати
  App Builder / mobile-config, які веде Codex).
- **`consumer_ai_requests`** — нова таблиця + міграція `20260908122806` з канонічною RLS-тріадою
  + `consumer_self_access`. Ретеншн (прунити >90 днів) — follow-up для worker.
- **Rate-limit** `consumer-ai` у `Program.cs`: fixed-window **6 / 3 хв** партиціонований по
  `consumer_account_id`.
- Тести: `ConsumerAssistantServiceTests` (7) — gate, guardrail-wiring, аудит, 502.

### 4b-mobile (окремо, mobile-власник) — НЕ ПОЧАТО

Чат-екран у споживчому застосунку (`mobile/app/(personal)/…`), викликає
`POST /api/consumer/assistant/{tenantId}/ask` з тілом `{ "message": "…" }`, відповідь
`{ text, model, tokensUsed }`. 404 = провайдер не налаштував консультанта для цього магазину.
429 = rate-limit. **Не в цій сесії** (`mobile/` — територія Codex).

## Провайдерська картка (frontend)

Секція «AI-агент» → «AI-агенти»: 3 підсекції (Аналітик / Асистент / Споживчий), однаковий
набір полів (провайдер / модель+тири / ключ / base_url / пресет / тест / увімкнено).
Порожній slot → «використовує Аналітик / системний ключ».

## Фазування

| Під-фаза | Обсяг | Ризик / хто |
|---|---|---|
| **4a — внутрішні профілі** (`analyst` + `assistant`) | data-міграція service→slot + `provider` в jsonb; `AiSlot`; резолвер+промпт-резолвер за slot; мапінг 6 адвайзерів; картка з 2 підсекціями; alias старого ендпоінта | середній — database-engineer (міграція) → backend-developer → frontend-developer |
| **4b — споживчий агент** (`consumer`) | новий адвайзер + `IConsumerContext` + посилений guardrail + rate-limit + consumer-ендпоінт + мобільний UI + аудит-лог | великий, окремий спринт, залежить від 4a + UX споживчого застосунку |

## Наслідки затверджених рішень

- **Пресети per-slot (рішення 2):** `aiPromptPresets.ts` → від `Record<BusinessType, string>`
  до `Record<AiSlot, Record<BusinessType, string>>` (або base-текст типу бізнесу + slot-тон).
  `analyst` — акцент на глибині аналізу; `assistant` — короткі практичні пояснення;
  `consumer` — тон звернення до клієнта, без внутрішньої термінології. Картка: дропдаун
  пресету в кожній з 3 підсекцій, за замовч. підсвічує тип бізнесу тенанта.
- **Без глобального дефолту (рішення 3):** не робимо таблицю/екран глобальних конфігів.
  Порожній slot → fallback `analyst` → env-ключ (як у плані). Провайдер налаштовує кожен
  тенант у його картці.
- **4b одразу за 4a (рішення 4):** тримати `AiSlot.Consumer` + `AiGuardrail` consumer-варіант
  у коді вже з 4a (плюмбінг), сам `ConsumerAssistantAdvisor` + споживчий контекст + UI — 4b.
