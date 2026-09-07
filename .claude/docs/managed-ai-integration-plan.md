# План: керована провайдером AI-інтеграція («AI-агент для бізнесу»)

**Статус:** **Фаза 1 — DEPLOYED prod 2026-09-07** (TASK-701, `7ffefeb7`/`5a93f3d3`/`0b298daa`,
log `.claude/logs/tasks/TASK-701_2026-09-07_managed-ai-phase1_main-session.md`).
Фаза 2 (OpenAI/Codex через `IAiChatClient` + тонкий HttpClient) і Фаза 3 (пресети по типах
бізнесу) — не почато. Розділи нижче описують повний дизайн; де реалізація відхилилась —
дивись task log.
**Пов'язане:** TASK-700, memory `shelfguard-settings-page-role-aware`, ADR-015 (AI isolation),
`shelfguard-rls-override-primitives`

## Контекст

Вкладка **Налаштування → Інтеграції** має картку «Claude AI», де **тенант сам вставляє
Anthropic API-ключ**. Ключ пише `PUT /api/integrations/claude` (`IntegrationsManageOrCapability`),
лягає в `integration_configs` (service='claude', per-tenant RLS, jsonb `Config`). Його читають
6 AI-адвайзерів у `ShelfGuard.Infrastructure/AI/`: `ClaudeOrderAdvisor`, `BusinessAssistant`,
`MarketingAdvisor`, `PostCampaignAdvisor`, `PriceSegmentAdvisor`, `SupplierAdvisor`. Резолв:
`integration_configs` → fallback env `Claude:ApiKey`. Промпти захардкожені (`BuildSystemPrompt()`).

**Нова модель (рішення власника):**
1. AI-агента налаштовує **провайдер** — у картці клієнта на `/provider` (вкладка «Клієнти»),
   там де вже редагуються план і модулі. Конфіг привʼязаний до `tenant.Id`.
2. Тенант у себе бачить **лише статус**: підключено / не підключено. Жодних полів, жодного ключа.
3. Підтримка **Claude і OpenAI/Codex**.
4. **Найважливіше — ізоляція:** агент відповідає **тільки** на питання про власний бізнес
   клієнта і бачить **тільки** його дані. На питання типу «розкажи про моїх конкурентів, де я
   просідаю» агент відповідає, що такі відповіді не надаються — тільки по бізнесу «{Назва}».

---

## Принцип №1 — ізоляція тенанта (найважливіше)

### Рівень даних — вже майже готово, треба верифікувати

- Усі 6 адвайзерів будують контекст запитами `WHERE TenantId == {tenantId}` (tenantId з JWT
  `tenant_id`), поверх RLS. Тобто модель фізично не отримує чужих даних.
- **Треба перевірити й закріпити тестом:**
  - жоден адвайзер не використовує `analytics_bypass` / інші RLS-примітиви
    (`shelfguard-rls-override-primitives`) — лише звичайний tenant-scope;
  - `SupplierAdvisor` та marketplace-контекст не підтягують дані інших тенантів про
    спільного постачальника (рейтинги/обсяги інших магазинів);
  - контекст-білдери не мають галузевих бенчмарків/агрегатів по всіх тенантах.
- Додати інтеграційний тест: два тенанти з даними, запит асистента від тенанта A ніколи не
  повертає сутностей тенанта B (аналогічно `RlsAudit` тестам).

### Рівень поведінки — нове: обовʼязковий guardrail у промпті

Спільний **префікс системного промпту**, який `IAiPromptResolver` **завжди** додає до
`BuildSystemPrompt()` кожного адвайзера, параметризований назвою бізнесу:

> Ти — AI-асистент виключно для бізнесу «{TenantName}». Ти маєш доступ лише до власних даних
> цього бізнесу. Якщо запит стосується інших компаній, конкурентів, порівнянь з ринком чи
> галуззю, бенчмарків, або будь-чого, що вимагає даних поза «{TenantName}» — відповідай, що
> надаєш інформацію тільки по бізнесу «{TenantName}» і не даєш даних про конкурентів чи інші
> компанії. Не вигадуй зовнішні цифри.

- Адвайзери отримують `TenantName` (зараз мають лише `tenantId`) — один запит
  `_db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name)` або прокидання з сервісу.
- `BuildSystemPrompt()` → `BuildSystemPrompt(AiPromptContext ctx)` де `ctx` несе назву +
  розвʼязаний текст guardrail.
- Guardrail **не вимикається** конфігом — це інваріант, не опція.

---

## Модель даних

`integration_configs` лишається носієм; для `service ∈ {claude, openai}` вміст `Config` (jsonb):

```jsonc
{
  "provider": "anthropic" | "openai",
  "api_key": "…",                         // масковано на будь-якому GET (last-4)
  "model": "claude-sonnet-4-6" | "gpt-…",
  "base_url": null,                        // для OpenAI-сумісних проксі (опц.)
  "extra_instructions": null,              // вільний текст: бізнес-контекст від провайдера
                                           // ("це аптека, акцент на термінах придатності")
  "configured_by": "<provider user id>",
  "configured_at": "2026-09-07T…Z"
}
```

**Без нової таблиці.** Глобальні пресети промптів (`ai_prompt_presets`) з v1 **прибрано** —
замість них вільне поле `extra_instructions` у картці клієнта. Пресети по типах бізнесу —
можлива пізніша ітерація, якщо провайдер втомиться писати те саме руками.

Міграції немає — тільки семантика вмісту jsonb + guardrail у коді.

---

## Бекенд

### 1. Провайдер-агностичний клієнт

```
IAiChatClient : Complete(system, userMessage, jsonSchema?) → text|json
├── AnthropicChatClient   — наявний код (Anthropic SDK), винести з адвайзерів
└── OpenAiChatClient      — ТОНКИЙ HttpClient до {base_url|https://api.openai.com}/v1/chat/completions
                            (Bearer, response_format=json_schema для structured output)
IAiClientFactory.ResolveAsync(tenantId, ct) → IAiChatClient
   // integration_configs (claude|openai, IsEnabled) → потрібний клієнт; fallback env
IAiPromptResolver.Build(advisorKey, tenantId, ct) → system prompt
   // guardrail(tenantName) + extra_instructions + захардкожений текст адвайзера
```

**Q4 — рекомендація: тонкий HttpClient для OpenAI, не офіційний SDK.**
Причини: менше залежностей → безпечніший Docker-білд (`shelfguard-cicd-and-deploy`: був
інцидент, коли CI-green ≠ Docker-green через транзитивну залежність); нам потрібен лише
`/v1/chat/completions` + structured outputs — маленька стабільна поверхня; той самий клієнт
працює з Azure OpenAI / Codex / локальними OpenAI-сумісними бекендами через `base_url`.
Anthropic-бік лишаємо на наявному `Anthropic` NuGet — він уже працює, `IAiChatClient` ховає різницю.

6 адвайзерів рефакторяться механічно: `new AnthropicClient{…}` → `_aiClientFactory` +
`BuildSystemPrompt()` → `_promptResolver.Build(...)`. Одна форма, ~10 рядків на адвайзер.

### 2. Права: `claude`/`openai` — provider-only

- `IntegrationsController` `PUT/DELETE /api/integrations/{service}` для `service ∈ {claude,
  openai}` → 403 тенантським ролям (лишити `telegram/resend/webhook/iot` як self-serve).
- `GET /api/integrations` тенанту віддає для цих сервісів лише `{ service, isEnabled }` —
  **без** `provider`/`model`/ключа.
- **Розширити `AdminController`** (`api/admin`, `ProviderOnly`) — нова секція, поряд з plan/modules:
  - `GET  /api/admin/tenants/{id}/ai` → `{ provider, model, baseUrl, extraInstructions, apiKeyLast4, isEnabled, configuredAt }`
  - `PUT  /api/admin/tenants/{id}/ai` → `{ provider, apiKey?, model, baseUrl?, extraInstructions?, isEnabled }`
    (порожній `apiKey` = не міняти)
  - `POST /api/admin/tenants/{id}/ai/test` → пінг обраного провайдера (як `PrroSettingsController` `/test`)
  - `DELETE /api/admin/tenants/{id}/ai` → відʼєднати
- Запис `integration_configs` для чужого тенанта — через `provider_bypass` primitive
  (`shelfguard-rls-override-primitives`), як решта `ITenantAdminRepository`.
- `TenantAdminService` (або новий `TenantAiConfigService`) — валідація моделі/провайдера, маскування.

### 3. Env

`OpenAI:ApiKey`, `OpenAI:Model` у `.env` на сервері (fallback, руками — деплой env не чіпає).

---

## Провайдерська консоль (frontend) — у картці клієнта

**Без окремого екрана.** Нова секція **«AI-агент»** у
`frontend/features/provider/components/TenantDetailPanel.tsx` (462 рядки — вже має секції
«План», «Модулі», «Адміни»; додаємо 4-ту, той самий патерн collapse/edit/save):

- бейдж статусу: «Не підключено» / «Claude · {model}» / «OpenAI · {model}»
- в режимі редагування: вибір провайдера (Claude / OpenAI), поле ключа (write-only,
  показує `••••1234`), модель (список відомих + вільний ввід), `base_url` (опц., під OpenAI),
  `extra_instructions` (textarea), тумблер «увімкнено», кнопка «Перевірити підключення»
- хук `useTenantAi(tenantId)` + `updateTenantAi` у `frontend/features/provider/api/provider.ts`
  / `hooks/useProvider.ts` (там уже `useUpdatePlan` / `useUpdateModules` — та сама форма)

Опційно — крок «AI-агент» у `CreateTenantWizard.tsx` (444 рядки). Радше **ні** для v1:
провайдер створює бізнес, потім відкриває картку й налаштовує AI окремо (менше полів у візарді).

i18n: `Dashboard.provider.tenantDetail.aiSection.*` (uk+en).

---

## Тенантська вкладка «Інтеграції» (frontend)

- **Прибрати** `claude` з `ALL_SERVICES` / `SERVICE_META` (`features/integrations/types.ts`)
  → картка введення ключа зникає.
- **Додати** read-only рядок «AI-агент» вгорі вкладки (над ПРРО), мінімально:
  - «AI-агент: **Підключено**» (зелений бейдж) або «AI-агент: **Не підключено**» (сірий)
  - один рядок опису: «Налаштування AI-агента виконує ваш провайдер.»
  - якщо не підключено — маленьке посилання «Дізнатися більше» → support-chat (без форми замовлення в v1)
- Джерело: `GET /api/integrations` (урізаний summary: лише `isEnabled`).
- i18n: `Dashboard.settings.integrationsTab.aiAgent.*` (uk+en).
- Гейтинг вкладки (TASK-700) не змінюється.

---

## Рефактор наявних згадок

- Текст помилки в адвайзерах «Add it in Налаштування → Інтеграції → Claude AI» (grep
  `Налаштування → Інтеграції`, ~2-3 місця) → «AI-агент не налаштований. Зверніться до провайдера.»
- `frontend` self-config згадки Claude (`grep -ri claude frontend/features/integrations`).
- `backend/openapi.json` регенерувати (нові admin-ендпоінти).

---

## Рол-аут наявних тенантів

1. Тенанти з уже заданим ключем у `integration_configs` (service='claude') — **нічого не
   ламається**: адвайзери й далі його читають. Тенант більше не редагує його з UI; провайдер
   бачить/керує з картки клієнта.
2. Guardrail починає діяти одразу для всіх (він у коді, не в конфігу).
3. Деплой — звичайний push→CI→deploy. Міграцій немає.

---

## Фази

| Фаза | Обсяг | Ризик |
|---|---|---|
| **1. Ізоляція + provider-config (Claude)** | guardrail-префікс у 6 адвайзерах + `TenantName` в контекст + RLS-верифікація + cross-tenant тест; `claude` write → provider-only; `AdminController` AI-секція; секція «AI-агент» у `TenantDetailPanel`; тенантський read-only статус; прибрати self-serve картку | середній, 1 backend + 1 frontend агент |
| **2. OpenAI/Codex** | `IAiChatClient` + `OpenAiChatClient` (тонкий HttpClient) + `IAiClientFactory`; рефактор 6 адвайзерів на фабрику; вибір провайдера в картці; OpenAI env; Docker-білд перевірка | середній, backend-агент |
| **3. (опц.) Пресети по типах бізнесу** | якщо `extra_instructions` руками набридне — маленька таблиця шаблонів + дропдаун у картці | низький |

Фаза 1 самодостатня: закриває головну вимогу (ізоляція + провайдер керує) на Claude-only,
без абстракції провайдерів.

## Залишкові дрібні питання

1. Модель списку для `model` dropdown — тримати перелік відомих Claude/OpenAI моделей у
   `frontend` конфізі чи віддавати з бекенду (`GET /api/admin/ai/models`)? → FE-константа, простіше.
2. `POST /ai/test` — що саме шле (мінімальний «ping» промпт «відповідай OK») — 1 короткий call,
   рахувати як витрату токенів провайдера, не тенанта.
3. Чи логувати запити асистента (промпт+відповідь) для аудиту зловживань? — окреме рішення
   (приватність vs. можливість довести, що агент відмовив на competitor-запит).
