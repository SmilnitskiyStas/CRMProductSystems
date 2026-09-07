# План: керована провайдером AI-інтеграція («AI-агенти як послуга»)

**Статус:** чернетка на обговорення · автор: main session · 2026-09-07
**Пов'язане:** TASK-700 (рольова сторінка Налаштувань), `shelfguard-settings-page-role-aware` (memory)

## Контекст

Зараз вкладка **Налаштування → Інтеграції** має картку «Claude AI», де **тенант сам
вставляє свій Anthropic API-ключ** + модель. Ключ пише `PUT /api/integrations/claude`
(політика `IntegrationsManageOrCapability` — `store_manager+` тенанта), лягає в
`integration_configs` (service='claude', per-tenant RLS, jsonb `Config`), звідки його
читають 6 AI-адвайзерів у `ShelfGuard.Infrastructure/AI/`
(`ClaudeOrderAdvisor`, `BusinessAssistant`, `MarketingAdvisor`, `PostCampaignAdvisor`,
`PriceSegmentAdvisor`, `SupplierAdvisor`) — резолв: `integration_configs` → fallback
`Claude:ApiKey` env. Промпти захардкожені в кожному адвайзері (`BuildSystemPrompt()`).

**Нова модель (рішення власника):** AI-агентів для бізнесу налаштовує **провайдер** як
платну послугу — ми готуємо ключі, промпти, скіли, вибір моделі. Тенант ключі не вводить.
Підтримуємо **Claude і OpenAI/Codex**.

## Цілі

1. Прибрати self-serve введення AI-ключа з тенантської вкладки.
2. Дати провайдеру per-tenant екран керування AI-підключенням (провайдер, ключ, модель,
   пресет промптів/скілів, увімк/вимк).
3. Зробити AI-шар провайдер-агностичним: Claude **або** OpenAI за конфігом тенанта.
4. Тенант бачить лише статус («налаштовано / не підключено») + CTA «замовити».

## Не в скоупі (окремі рішення)

- Ціноутворення / білінг за AI-послугу (модуль? окремий тариф?) — впливає лише на тексти.
- Стрімінг відповідей, ліміти токенів per-tenant, облік вартості — фаза 2+.
- Прибирання решти карток Інтеграцій (ПРРО / Telegram / Webhook / IoT) — вони справді
  self-serve тенантські, лишаються.

---

## Модель даних

`integration_configs` **лишається** носієм, розширюємо семантику `Config` (jsonb) для
`service` ∈ {`claude`, `openai`}:

```jsonc
{
  "provider": "anthropic" | "openai",   // дублює service для явності
  "api_key": "…",                        // масковано на GET (last-4), як зараз
  "model": "claude-sonnet-4-6" | "gpt-…",
  "base_url": null,                      // для OpenAI-сумісних проксі, опц.
  "prompt_preset_id": "retail-default",  // → таблиця пресетів (нижче)
  "prompt_overrides": { "order_advisor": "…" },  // опц., per-advisor текст
  "configured_by": "<provider user id>",
  "configured_at": "2026-09-07T…Z"
}
```

**Нова таблиця `ai_prompt_presets`** (глобальна, провайдер-керована, без RLS — за
зразком `platform_categories`):

| колонка | тип | нотатки |
|---|---|---|
| Id | uuid pk | |
| Key | varchar unique | `retail-default`, `auto-service`, … |
| Name | varchar | UI-назва |
| Description | text | |
| Prompts | jsonb | `{ "<advisor-key>": "<system prompt template>" }` для 6 адвайзерів |
| Skills | jsonb | опц. — структуровані «скіли» (набори інструкцій/інструментів), фаза 2 |
| IsActive | bool | |

Тенант обирає **один пресет**; `prompt_overrides` у `integration_configs` перекриває
окремі адвайзери. Якщо пресет не заданий → поточні захардкожені `BuildSystemPrompt()`.

Міграція: `AddAiPromptPresets` (1 таблиця) + seed 1-2 базових пресетів (retail, auto).
`integration_configs` схему не чіпаємо — тільки вміст jsonb.

---

## Бекенд

### 1. Провайдер-агностичний AI-клієнт

Нова абстракція в `ShelfGuard.Infrastructure/AI/`:

```
IAiChatClient                          // Complete(system, messages, jsonSchema?) → text/json
├── AnthropicChatClient   (Anthropic SDK, наявний код з ClaudeOrderAdvisor.CallAsync)
└── OpenAiChatClient      (OpenAI .NET SDK або HttpClient до /v1/chat/completions;
                           structured outputs через response_format=json_schema)
IAiClientFactory.ResolveAsync(tenantId, ct) → IAiChatClient
   // читає integration_configs (claude|openai, IsEnabled) → створює відповідний клієнт
   // → fallback env (Claude:ApiKey / OpenAI:ApiKey)
```

6 адвайзерів рефакторяться: замість `new AnthropicClient{…}` беруть `IAiChatClient` з
фабрики. `BuildSystemPrompt()` → бере текст із пресету (`IAiPromptResolver.For(advisorKey,
tenantId)`), fallback на наявний захардкожений рядок. Structured-output контракт (JSON
schema) уже є — обидва провайдери його підтримують, формат виклику інкапсулюється в клієнті.

Обсяг: ~2 нових клієнти + фабрика + резолвер промптів + правки в 6 адвайзерах (механічні,
одна форма). OpenAI SDK: додати `OpenAI` NuGet (офіційний) — перевірити Docker-білд
(`shelfguard-cicd-and-deploy`: CI-green ≠ Docker-green).

### 2. Права: `claude`/`openai` config стає provider-only на запис

- `IntegrationsController` `PUT/DELETE /api/integrations/{service}` — для `service ∈
  {claude, openai}` повертає 403 для тенантських ролей (лишити `telegram/resend/webhook/iot`).
- **GET лишається** тенанту, але сервіс віддає лише `{ isConfigured, provider, model,
  updatedAt }` — **без** ключа (навіть маскованого) для не-провайдера.
- Новий **`AdminAiController`** (`api/admin/tenants/{id}/ai`, політика `ProviderOnly`):
  - `GET` → повна конфігурація (ключ масковано last-4) + список пресетів
  - `PUT` → `{ provider, apiKey?, model, promptPresetId?, promptOverrides?, isEnabled }`
  - `POST /test` → пінг обраного провайдера (як `PrroSettingsController` `/test`)
  - `DELETE` → від'єднати
- `ai_prompt_presets` CRUD: `ProviderAiPresetsController` (`ProviderOnly`).
- RLS: `integration_configs` уже має `tenant_isolation` + `provider_bypass` — провайдерський
  запис іде через `provider_bypass` primitive (`shelfguard-rls-override-primitives`).
  `ai_prompt_presets` — без RLS (глобальна).

### 3. Env

`OpenAI:ApiKey`, `OpenAI:Model` у `.env` (fallback, як `Claude:ApiKey`). `.env` на сервері
руками (`shelfguard-cicd-and-deploy` — деплой env не чіпає).

---

## Провайдерська консоль (frontend)

Новий екран у розділі провайдера (поряд з `/provider/team`, `/provider/categories`):
**`/provider/ai`** або таб на сторінці тенанта в адмін-панелі («Клієнти» → тенант → «AI»).

- Таблиця тенантів: статус AI (не підключено / Claude / OpenAI), модель, пресет, останнє оновл.
- Форма на тенанта: вибір провайдера (Claude / OpenAI), поле ключа (write-only, маскується),
  модель (dropdown відомих + вільний ввід), вибір пресету промптів, per-advisor overrides
  (collapsible, необов'язково), тумблер «увімкнено», кнопка «Перевірити підключення».
- Окремий екран **`/provider/ai/presets`** — CRUD пресетів промптів/скілів.

Feature-модуль: `frontend/features/provider/` (наявний, 14 компонентів) + `api/ai.ts`,
`hooks/useAdminAi.ts`. Гейт: `PROVIDER_TEAM` (+ `providerPermissions` якщо треба гранульованість).

---

## Тенантська вкладка «Інтеграції» (frontend)

- **Прибрати** `claude` з `ALL_SERVICES` / `SERVICE_META` (`features/integrations/types.ts`)
  → картка введення ключа зникає.
- **Додати** окремий read-only блок «AI-помічник» вгорі вкладки (над ПРРО), за зразком
  `PrroCard`:
  - бейдж статусу: «Налаштовано (Claude)» / «Налаштовано (OpenAI)» / «Не підключено»
  - короткий опис що вміє AI (автозамовлення, бізнес-асистент, маркетинг-поради…)
  - якщо не підключено → кнопка **«Замовити налаштування»** → відкриває support-chat або
    створює service-desk тикет категорії `feature_request` (обидва механізми вже є)
  - якщо підключено → лише «Керується вашим провайдером», без дій
- Джерело даних: `GET /api/integrations` (той самий, віддає урізаний summary для тенанта).
- i18n: новий `Dashboard.settings.integrationsTab.aiManaged.*` (uk+en).

Гейтинг вкладки «Інтеграції» (з TASK-700) не змінюється — `store_manager+` або
capability `integrations.view`.

---

## Рефактор наявних згадок

- `ClaudeOrderAdvisor` кидає текст помилки «Add it in Налаштування → Інтеграції → Claude
  AI» → замінити на «AI-помічник не налаштований. Зверніться до провайдера.» (6 місць /
  grep `Налаштування → Інтеграції`).
- `frontend` — усі підказки/лінки на self-config Claude (`grep -ri "claude" features/integrations`).
- `openapi.json` регенерувати (нові admin-ендпоінти) — `shelfguard-mobile-app-analytics-modules`
  вже має pending-regen нотатку.

---

## Рол-аут / міграція наявних тенантів

1. Тенанти, що вже поклали свій ключ у `integration_configs` (service='claude') —
   **нічого не ламається**: адвайзери й далі його читають. Просто тенант більше не може
   його змінити з UI; провайдер бачить і керує ним зі своєї консолі.
2. Деплой — звичайний push→CI→deploy. Міграція `AddAiPromptPresets` застосується авто на
   старті API (`shelfguard-cicd-and-deploy`).
3. Після деплою — провайдер проходить по активних тенантах з AI й привʼязує пресет.

---

## Фази

| Фаза | Обсяг | Ризик |
|---|---|---|
| **1. Замок + статус** | Тенантський `PUT claude` → provider-only; тенантська картка → read-only статус + CTA; `AdminAiController` (Claude лише); provider-консоль форма (без пресетів) | низький, 1 агент |
| **2. OpenAI** | `IAiChatClient` абстракція + `OpenAiChatClient` + фабрика; рефактор 6 адвайзерів; OpenAI env/SDK; Docker-білд перевірка | середній, backend-агент |
| **3. Пресети промптів/скілів** | `ai_prompt_presets` таблиця + CRUD + `IAiPromptResolver`; provider `/ai/presets` екран; per-advisor overrides | середній |

Фази незалежні: 1 можна зробити зараз без 2/3 (Claude-only, промпти лишаються захардкожені).

## Відкриті питання

1. AI-послуга — це **новий модуль** (`ai_assistant` у `tenants.modules`, гейт
   `[RequireModule]`) чи просто вкл/викл у `integration_configs`? Модуль дає чистий
   per-module білінг (узгоджується з логікою «за кожен модуль окрема ціна»).
2. Пресети «скілів» — що це технічно? Наперед заготовлені набори інструкцій у промпті,
   чи справжні tool-/ function-набори (тоді потрібен tool-execution шар)?
3. Provider-консоль — окремий розділ `/provider/ai` чи таб у картці тенанта в `/admin`?
4. OpenAI — офіційний `OpenAI` NuGet чи тонкий `HttpClient` (менше залежностей, легший
   Docker-білд)?
