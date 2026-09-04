# TASK-692 — Слайс 5: банер «акція наближається» на сторінці товару

**Date:** 2026-09-04 · **Agent:** main session · **Plan:** `.claude/plans/catalog-form-buffers-promo.md` (Слайс 5, останній)
**State:** done · not yet deployed at time of writing

## Що зроблено

Банер над табами на `/inventory/{id}` (видно на Info і Analytics обох): «🏷 Акція на товар діє /
з DD.MM.YYYY (−N%). Прогноз ×K — замовлення вже збільшено автоматично. / Замовлення буде
збільшено автоматично.» Зелений (active) / жовтий (upcoming) — конвенція `PromoBadge` (Слайс 3).

### Backend
- `ItemRepository.GetPromoDetailAsync(productId, upcomingWithinDays)` — новий, окремий метод.
  Той самий active/upcoming resolution, що й `GetPromoStatesAsync`, але scoped на один продукт +
  join на **застосовану** (`IsApplied=true`) `PromoCannibalization` для точно того (winning)
  discountId + цього продукту. Навмисно НЕ чіпає `GetPromoStatesAsync` (пейджед-каталог, hot path).
- `ItemDto` += `PromoOrderCoefficient`. `PromoState`/`PromoStartsAt`/`PromoDiscountPercent`
  тепер заповнюються і `GetByIdAsync` (раніше — лише пейджед-списком).
- **×K ніколи не вигадується.** Реальний `CannibalizationService.PromoProductCoefficient` (2.0)
  показується лише коли менеджер уже згенерував+застосував пропозицію канібалізації для цього
  промо (окремий, здебільшого невикористовуваний manual/AI-suggested воркфлоу) — інакше банер
  показує нейтральний текст без числа.
- 7 нових repository-level тестів (EF InMemory) `ItemRepositoryGetPromoDetailTests.cs`
  (no-promo→null, active, upcoming, active-beats-upcoming, applied-coef, unapplied-coef-null,
  coef-for-unrelated-discount-null) + 2 service-level у `ItemServiceTests.cs`.
- Обидва hand-fake `IItemRepository` (Pos/Fiscalization) отримали стаб нового методу.

### Frontend
- `PromoBanner` — локальний компонент у `app/(dashboard)/inventory/[id]/page.tsx` (як інші
  сторінко-специфічні `Field`/`Grid`/`StatCard`). `types.ts` Product += `promoOrderCoefficient`.
- i18n: `Dashboard.inventory.productPage.promoBannerActive/Upcoming/promoForecastWithCoef/Plain`.

## Верифікація
- `dotnet test` **2347/2347**; frontend `tsc`/eslint (нові файли чисто) / i18n parity **4974==4974**.
- E2E на dev-БД (seed → перевірка → прибрано):
  - active promo + applied coef 2.0 → зелений банер «Promo active … (−18%). Forecast ×2 — …»
  - upcoming promo, без coef → жовтий банер «Promo starts DD/MM/YYYY … Order volume will be…»
  - товар без промо → банера немає, лейаут не ламається.

## Схема
Без міграції.

## Підсумок плану `catalog-form-buffers-promo.md`
Усі 5 слайсів завершено (Слайс 1..3 задеплоєно раніше; Слайс 4 (`5c1de9dc`) задеплоєно
2026-09-04; Слайс 5 — цей коміт, ще не запушено).
