# План: керована AI-інтеграція — Фаза 4 («кілька агентів на бізнес»)

**Статус:** **4a реалізовано** (TASK-707, коміти `274a0dd0` step 1 / `5c424975` step 2 /
frontend step 3), деплой через push→CI. **4b (споживчий агент) — не почато.**
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

## Consumer агент (Фаза 4b — окремий, більший спринт)

Залежить від 4a. Потребує узгодження UX споживчого застосунку.

- Новий `ConsumerAssistantAdvisor` (`Infrastructure/AI/`).
- **Контекст:** ідентичність споживача (loyalty-член) — `IConsumerContext` з consumer-app
  auth (`ConsumerAccount`), окремо від `ITenantContext`.
- **Бачить лише:** власний бонусний баланс / рівень / історію цього клієнта; власні
  замовлення; публічний каталог + наявність; правила програми лояльності; графік і адреси
  магазинів. **Нічого внутрішнього** (маржа, собівартість, постачальники, інші клієнти,
  продажі, персонал, прогнози).
- **Посилений guardrail:** «ти говориш з КЛІЄНТОМ бізнесу «{Name}», не з персоналом; лише
  його акаунт лояльності, його замовлення, публічний каталог; відмовляй на решту».
- **Rate-limiting:** per-consumer, per-day (недовірена аудиторія, ризик обсягу/вартості).
- **Ендпоінт:** consumer-app API surface, `[RequireModule("mobile_app")]` (або `loyalty`).
- **UI:** у споживчому застосунку (не staff-мобільний).
- **Аудит-лог** запитів (промпт + відповідь) — закриває залишкове питання №3 основного плану.

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
