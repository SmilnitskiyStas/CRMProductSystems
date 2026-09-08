# TASK-711 — AI weekly weather-promo suggestions (backend)

**Date:** 2026-09-08 · **Agent:** backend-developer · **Status:** review · не комічено/запушено
**Plan:** `~/.claude/plans/toasty-knitting-hinton.md` · **Branch:** `feat/ai-weather-promo`

## Що зроблено

Нова AI-фіча «Analyst» агента: прогноз погоди + топ-продажі (45 днів) + погодні коефіцієнти
→ структуровані поради «тепла погода → морозиво/вода на тижневу знижку»; другий ендпоінт
матеріалізує обрану пораду в реальні `Discount` + календарний `DemandEvent` promo.

### Нові файли
- `backend/ShelfGuard.Application/Features/WeatherPromo/`
  - `Dtos/WeatherPromoDtos.cs` — `WeatherPromoContext`/`…ForecastLine`/`…TopSellerLine`/`…CoefficientLine`,
    `WeatherPromoAdviceResult`, `WeatherPromoSuggestion`, `WeatherPromoProduct`,
    `WeatherPromoSuggestionsResponse`, `ApplySuggestionRequest`, `ApplySuggestionResult`
  - `IWeatherPromoAdvisor.cs` — Application-side contract (ADR-015), реалізація в Infrastructure
  - `IWeatherPromoRepository.cs` (+ `WeatherPromoContextData`, `WeatherPromoForecastLocation`,
    `WeatherPromoItemRow`) — DB-читання (Application не бачить `AppDbContext`)
  - `IWeatherPromoService.cs` / `WeatherPromoService.cs` — оркестрація + 12h `IMemoryCache`
    (`weather-promo:{tenantId}`) + apply-fan-out
- `backend/ShelfGuard.Infrastructure/AI/WeatherPromoAdvisor/WeatherPromoAdvisor.cs` — нова тека,
  копіює патерн `SupplierAdvisor` (slot `Analyst`, `IAiClientFactory`/`IAiPromptResolver`,
  structured-output JSON schema, `internal static ParseSuggestions` — пропускає не-Guid `item_id`
  та нерозбірні дати вікна). Наявні адвайзери не чіпав.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/WeatherPromoRepository.cs` — топ-30
  продажів (GroupBy/Sum, `!IsAnomaly`, ≥ today-45), `weather_coefficients` з іменами
  категорій/сегментів, активні promo-події + товари з активною знижкою; items + active-location-ids
  для apply
- `backend/ShelfGuard.Api/Controllers/WeatherPromoController.cs` — `[Route("api/ai/weather-promo")]`,
  `[Authorize(Policy = AtLeastStoreManager)]`, `[RequireModule("inventory")]`;
  `GET suggestions?refresh=`, `POST apply`
- Тести `backend/ShelfGuard.Tests/WeatherPromo/` — `WeatherPromoAdvisorParsingTests` (4),
  `WeatherPromoServiceTests` (13)

### Змінені файли
- `backend/ShelfGuard.Application/ShelfGuard.Application.csproj` — `+ Microsoft.Extensions.Caching.Abstractions 8.0.0`
- `backend/ShelfGuard.Application/DependencyInjection.cs` — `+ IWeatherPromoService`
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — `+ IWeatherPromoAdvisor`, `+ IWeatherPromoRepository`

## Відхилення від брифу

1. **Новий `IWeatherPromoRepository` замість `_db` у сервісі.** `ShelfGuard.Application` референсить
   лише `Domain` (не `Infrastructure`), тож `AppDbContext` у Application-сервісі не компілюється.
   Контекст-збірка винесена в репозиторій (Infrastructure) — стандартний патерн проєкту.
   Бриф це передбачає: тести на «mocked repo».
2. **Погодні коефіцієнти читає новий репозиторій** (з `Include`-іменами категорій/сегментів), а не
   `_weatherRepo.GetCoefficientsAsync()` — бо той не тягне назви. Прогноз погоди — все ще через
   `IWeatherRepository.GetRangeAsync` у сервісі, як у плані.
3. **`GET suggestions`**: помилку від AI-виклику контролер ловить і віддає `502 {error}` (як
   `AiAssistantService`), щоб `status` лишався чистим (`ok|not_configured|insufficient_data`).
4. Додано дрібні валідації: `Title` обов'язковий; порожні/`Guid.Empty` storeIds відсіюються.

## Верифікація

- `dotnet build ShelfGuard.sln` — clean (0 errors; єдиний warning — прееіснуючий у
  `MarketplaceServiceTests.cs:1029`)
- `dotnet test --filter "FullyQualifiedName~WeatherPromo"` — **17/17 pass**
- `dotnet test --filter "FullyQualifiedName~Event|FullyQualifiedName~Discount"` — **41/41 pass**
- **Міграції немає** — лише читання наявних таблиць + `IMemoryCache`

## API контракт (для фронт-задачі)

### `GET /api/ai/weather-promo/suggestions?refresh=false`
`200` →
```json
{
  "status": "ok",                       // ok | not_configured | insufficient_data
  "generatedAt": "2026-09-08T10:00:00Z", // null коли не ok
  "model": "claude-...",                 // null коли не ok
  "suggestions": [
    {
      "title": "Тепле сонце на вихідних",
      "startsAt": "2026-09-12",
      "endsAt": "2026-09-19",
      "weatherSummary": "+27°C, ясно",
      "rationale": "Спека підвищує попит на морозиво та напої.",
      "recommendedDiscountPct": 15,
      "confidence": "high",              // low | medium | high
      "products": [
        { "itemId": "guid", "name": "Морозиво пломбір", "currentPrice": 49.90, "reason": "Погодозалежний топ" }
      ]
    }
  ]
}
```
`403` — немає tenant у claims. `502 {error}` — AI-виклик впав.
Кеш 12 год/тенант; `refresh=true` форсує новий виклик і перезапис кешу.

### `POST /api/ai/weather-promo/apply`
Body:
```json
{
  "title": "Погодна акція",
  "startsAt": "2026-09-12",
  "endsAt": "2026-09-19",
  "discountPct": 15,                 // 1..90
  "storeIds": ["guid"],             // [] → мережева подія + знижки в усіх активних локаціях
  "productIds": ["guid"],           // непорожній
  "createCalendarEvent": true,
  "createDiscounts": true            // хоча б один із двох true
}
```
`200` →
```json
{ "eventId": "guid or null", "discountIds": ["guid"], "warnings": ["Товар … без роздрібної ціни …"] }
```
`400 {error}` — валідація (discountPct поза 1..90, startsAt > endsAt, порожній productIds,
обидва прапорці false, жоден productId не належить тенанту).
Знижки створюються і одразу `approve` → `active` (вебхук на касу). Подія — `EventType="promo"`,
scope `stores`/`network`, + `DemandEventCoefficient` `ScopeType="product"`,
`Coefficient = round(1 + discountPct/100, 2)` на кожен товар.

## Наступний крок
Frontend-агент — `frontend/features/weather-promo/` + `WeatherPromoPanel` (/events) +
`WeatherPromoCompactCard` (дашборд) + i18n (`toasty-knitting-hinton.md` розділ Frontend).

---

# TASK-711 — frontend

**Date:** 2026-09-08 · **Agent:** frontend-developer · **Status:** review · не комічено/запушено
**Branch:** `feat/ai-weather-promo` (продовження)

## Нові файли — `frontend/features/weather-promo/`
- `types.ts` — `WeatherPromoStatus`, `WeatherPromoConfidence`, `WeatherPromoProduct`,
  `WeatherPromoSuggestion`, `WeatherPromoSuggestionsResponse`, `ApplyWeatherPromoRequest`,
  `ApplyWeatherPromoResult` (дзеркало `WeatherPromoDtos.cs`, camelCase, дати як `yyyy-MM-dd`)
- `api/weatherPromo.ts` — `weatherPromoApi.getSuggestions(refresh?)` → `GET …/suggestions?refresh=`,
  `weatherPromoApi.apply(payload)` → `POST …/apply` (через `@/lib/api`)
- `hooks/useWeatherPromo.ts`:
  - `useWeatherPromoSuggestions({ enabled? })` — `useQuery`, key `["weather-promo","suggestions"]`,
    `staleTime: Infinity`, `retry: false`. `enabled:false` → чистий cache-reader (для дашборд-картки)
  - `useRefreshWeatherPromo()` — `useMutation` → `getSuggestions(true)`, `onSuccess`
    `qc.setQueryData(key, data)`
  - `useApplyWeatherPromo()` — `useMutation` → `apply`, `onSuccess` інвалідовує
    `["demand-events"]` + `["weather-promo","suggestions"]`
- `components/SuggestionCard.tsx` — темна картка: заголовок + чип впевненості (high=зел/med=бурш/low=сір),
  погодне вікно + summary, rationale, таблиця товарів (чекбокс включення + назва + `currentPrice` або «—»
  + reason), **одне** поле «Знижка %» (1..90, дефолт `recommendedDiscountPct`),
  `LocationsMultiSelectDropdown` (дефолт = глобальний вибір магазинів), 2 чекбокси
  (createDiscounts/createCalendarEvent, обидва on), кнопка «Створити акцію» → **confirm `Modal`**
  (точний перелік: N% × M товарів × K магазинів × період, «знижки одразу на касу») → `apply.mutate`
  → `toast.success` + `warnings` як `toast.warning` → інлайн-стан «Створено» + `<Link href="/events">`
- `components/WeatherPromoPanel.tsx` — повна панель для `/events` у `CollapsibleSection`
  (`title` = «AI-поради на тиждень», `defaultOpen`). `not_configured`/error/initial-load → `null`.
  `insufficient_data` → підказка. `ok` без порад → «AI не знайшов…». `ok` з порадами → список
  `SuggestionCard`. Хедер: `Sparkles` + «оновлено {relative}» (Intl.RelativeTimeFormat) +
  кнопка «Оновити» (ghost, коли вже є `generatedAt`) / «Згенерувати поради» (primary інакше) →
  `useRefreshWeatherPromo`
- `components/WeatherPromoCompactCard.tsx` — дашборд-тизер, `useWeatherPromoSuggestions({enabled:false})`
  (лише читає кеш, ніколи не ініціює виклик). Ховається, якщо не `ok` або 0 порад. Показує топ-пораду:
  title + weatherSummary + перші 3 товари + `<Link href="/events">` «Переглянути всі →»

## Змінені файли
- `frontend/app/(dashboard)/events/page.tsx` — `<WeatherPromoPanel />` між weather-хінтами і календарем
- `frontend/app/(dashboard)/dashboard/page.tsx` — `<WeatherPromoCompactCard />` після `<WeeklyKpiCards />`
- `frontend/messages/uk.json` + `en.json` — новий блок `Dashboard.weatherPromo.*` (parity)

## Відхилення / нотатки
1. **`useApplyWeatherPromo` інвалідовує `["demand-events"]`, не `["events"]`** — реальний ключ
   events-фічі це `["demand-events"]` (`useEvents.ts`), `["events"]` у коді не існує.
2. **Cache-reader для дашборд-картки (`enabled:false`).** Бекенд `GET …?refresh=false` на
   cache-miss **все одно робить AI-виклик** (це закладений «1×/12год» бюджет). Щоб дашборд
   лишався пасивним (план: «дашборд читає лише кеш»), компакт-картка не ініціює запит —
   тільки `/events`-панель (авто-фетч) або кнопка «Оновити/Згенерувати». ⚠️ Наслідок: перший
   візит на `/events` у вікні 12 год тригерить генерацію без явного кліку. Якщо потрібно
   «AI лише за кнопкою» — бекенд `GetSuggestionsAsync` має на `!refresh` + cache-miss
   повертати порожній `ok`/`insufficient_data`, а не викликати advisor.
3. `CollapsibleSection` не приймає іконку в хедері — `Sparkles` перенесено в рядок керування
   всередині секції.
4. Спінер: інлайн `<style>{@keyframes spin}</style>` — наявний патерн проєкту
   (`ProductAnalyticsTab.tsx`).

## Верифікація
- `npx tsc --noEmit` — clean
- `npx next lint` (features/weather-promo + обидві сторінки) — clean
- `npx vitest run` — 64/64 pass (без змін)
- `npx next build` — clean (exit 0; `/events`, `/dashboard` компілюються)
- Браузер: не перевірено (локальний бекенд + `ai_analyst` агент можуть бути не налаштовані)

---

## Orchestrator additions (main session, after both agents)

1. **`GET /suggestions` no longer spends an AI call on a plain read.** On a cache miss without
   `?refresh=true` it returns the new status `not_generated` + empty suggestions (no advisor
   call, no context assembly). The panel shows a "Згенерувати поради" button; after one run the
   result is cached 12h and served automatically. Tests updated; new
   `GetSuggestions_NoCache_NoRefresh_ReturnsNotGenerated_AndSkipsAdvisor`.

2. **Running-campaign management inside the panel.** `Discount` rows created by `apply` go to the
   cash register, but the existing discount-management screen is `[RequireModule("mobile_app")]` —
   a tenant without that module (e.g. «Свіжий Кут»: pos but no mobile_app) had no way to see or
   cancel them. Added:
   - `IWeatherPromoRepository.GetActivePromoDiscountsAsync` (joins product + store names)
   - `WeatherPromoService.GetActiveDiscountsAsync` / `CancelDiscountsAsync` (only cancels ids
     that are genuinely this tenant's active promo discounts — eligibility check)
   - `GET /api/ai/weather-promo/active-discounts`, `POST /api/ai/weather-promo/cancel-discounts`
   - `WeatherPromoPanel`: an "Активні знижки акцій (N)" list with a per-row cancel (X) button,
     visible even when the AI agent is not configured (otherwise the discounts would be
     unmanageable). New test `CancelDiscounts_OnlyCancelsIdsThatAreThisTenantsActivePromoDiscounts`.
   - i18n `weatherPromo.{activeDiscountsTitle,cancelDiscount,discountCancelled}` uk+en.

## End-to-end verification (local stack, tenant «Свіжий Кут», no `ai_analyst` configured)

- `GET /suggestions` → `not_configured`; `?refresh=true` → still `not_configured`. Panel + compact
  card correctly hidden.
- `POST /apply` (hand-crafted payload, 3 products incl. one null-price, 1 store, both flags) →
  `eventId` + 3 `discountIds` + the null-price warning. DB: 3 `Discount` rows `active`,
  `Reason=promo`, 2026-09-12→19, prices `143.48→121.96` / `35.00→29.75` / null; `DemandEvent`
  "promo" with 3 product coefficients — shows on the calendar.
- `GET /active-discounts` → the 3 rows with names/prices/dates. `POST /cancel-discounts` with a
  real + a bogus id → `cancelled:1` + a warning; list 3→2.
- Browser `/events`: "Active promo discounts (2)" list renders, X button cancels (2→1, toast),
  calendar shows the promo event. Dashboard compact card hidden (no suggestions).
- **Not verified:** the live AI generation (no local `ai_analyst` key). Covered by
  `WeatherPromoAdvisorParsingTests` + `WeatherPromoServiceTests` (mocked advisor); same
  `IAiChatClient` structured-output path as the production `SupplierAdvisor`.
