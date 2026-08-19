# CODEX SPEC — Mobile Application

## ROLE

Ти відповідаєш ВИКЛЮЧНО за mobile application.

Не змінюй backend або web admin application, якщо це не було явно дозволено.

Якщо для mobile потрібна зміна API:

1. зафіксуй необхідний contract;
2. опиши його в integration notes;
3. не вигадуй backend implementation;
4. використовуй mock data до появи endpoint.

---

# 1. Stack

Основний стек:

```text
React Native
TypeScript
```

Якщо проект уже використовує Expo — продовжуй Expo architecture.

Якщо існуючий проект не використовує Expo — не мігруй його без необхідності.

Використовувати:

- strict TypeScript;
- component architecture;
- typed API;
- centralized error handling;
- secure token storage;
- query caching;
- schema validation на API boundaries.

Не використовувати `any`, якщо цього можна уникнути.

---

# 2. Основна задача Mobile

Побудувати:

> Multi-Tenant Server-Driven Retail Mobile Application.

Mobile application має бути одним reusable engine.

Retailer-specific UI формується із server configuration.

---

# 3. ЕТАП 0 — Existing project audit

Перед будь-яким coding:

1. досліди repository;
2. знайди architecture;
3. знайди navigation;
4. знайди authentication;
5. знайди API layer;
6. знайди state management;
7. знайди існуючі UI components;
8. знайди test setup.

Створи:

```text
docs/mobile/MOBILE_CURRENT_STATE.md
```

Опиши:

- що вже існує;
- що можна повторно використовувати;
- що потрібно refactor;
- потенційні конфлікти з новою architecture.

Не переписуй працюючі частини без необхідності.

---

# 4. ЕТАП 1 — Mobile architecture foundation

Створити базову структуру:

```text
src/
├── app/
├── navigation/
├── api/
├── auth/
├── tenant/
├── config/
├── theme/
├── features/
├── components/
├── blocks/
├── screens/
├── hooks/
├── store/
├── schemas/
├── types/
└── utils/
```

Реалізувати:

```text
ActiveTenantProvider
MobileConfigProvider
ThemeProvider
FeatureFlagProvider
```

Поки використати mock tenant.

Definition of Done:

- app запускається;
- tenant context працює;
- theme context працює;
- navigation працює;
- tests проходять.

---

# 5. ЕТАП 2 — Multi-Tenant user experience

Реалізувати:

```text
My Retailers
Retailer Search
Add Retailer
Switch Retailer
Remove Retailer
Active Retailer persistence
```

Користувач може мати N retailers.

Зберігати локально:

```text
activeTenantId
```

але backend залишається source of truth щодо доступних tenant.

Після перезапуску:

```text
open application
↓
restore authenticated session
↓
restore active tenant
↓
load mobile config
↓
open retailer environment
```

---

# 6. ЕТАП 3 — Mobile Configuration Contract

Реалізувати typed contract згідно:

```text
mobile-config.schema.json
```

Очікуваний root:

```typescript
interface MobileConfig {
  schemaVersion: number;
  configVersion: number;

  tenant: TenantConfig;
  theme: ThemeConfig;
  features: FeatureConfig;
  navigation: NavigationItem[];
  pages: Record<string, PageConfig>;
}
```

Обов'язково runtime validation.

Invalid config не повинен crash application.

Fallback:

```text
Last Valid Configuration
```

або safe default configuration.

---

# 7. ЕТАП 4 — Theme Engine

Реалізувати dynamic theme.

Підтримати:

```text
Primary color
Secondary color
Background
Surface
Primary text
Secondary text
Button radius
Card radius
Spacing preset
```

Не дозволяти server configuration контролювати arbitrary styling.

Створити design tokens.

Компоненти повинні використовувати tokens, а не hardcoded colors.

---

# 8. ЕТАП 5 — Server-Driven UI Renderer

Це ключовий етап.

Створити:

```text
BlockRenderer
ComponentRegistry
PageRenderer
```

Приклад:

```typescript
const componentRegistry = {
  heroBanner: HeroBannerBlock,
  bannerCarousel: BannerCarouselBlock,
  loyaltyCard: LoyaltyCardBlock,
  promotionCarousel: PromotionCarouselBlock,
};
```

Unknown component:

```text
ignore safely
+
log warning
```

Ніколи не crash app через unknown block.

---

# 9. ЕТАП 6 — Core Blocks V1

Реалізувати:

```text
HeroBanner
BannerCarousel

LoyaltyCard
LoyaltyBalance

PromotionCarousel
PromotionGrid

ProductCarousel
ProductGrid

SectionHeader
QuickActions

NewsList
StoreList
```

Кожен block:

- reusable;
- typed;
- isolated;
- testable;
- не знає tenant напряму;
- отримує data через props/data provider.

---

# 10. ЕТАП 7 — Dynamic Navigation

Navigation повинна будуватися з config.

Підтримати:

```text
home
promotions
catalog
loyalty
coupons
stores
news
profile
```

Не дозволяти arbitrary component route із backend.

Backend може посилатися лише на whitelist route identifiers.

---

# 11. ЕТАП 8 — Feature Flags

Створити:

```typescript
useFeature("catalog")
```

або equivalent abstraction.

Якщо feature disabled:

- navigation item hidden;
- widgets hidden;
- routes protected.

Не дублювати feature checks по всьому application хаотично.

---

# 12. ЕТАП 9 — Loyalty

Реалізувати retailer-specific:

```text
Loyalty Card
Barcode / QR
Balance
Tier
Transactions
```

Loyalty data повинна змінюватися при переключенні tenant.

Ніколи не показувати balance іншого retailer.

---

# 13. ЕТАП 10 — Promotions / Catalog

Реалізувати:

```text
Promotions
Promotion Details

Categories
Products
Product Details
```

API запити повинні бути tenant-scoped через centralized API context.

---

# 14. ЕТАП 11 — QR retailer onboarding

Реалізувати:

```text
Scan QR
↓
resolve tenant
↓
show retailer
↓
join retailer
↓
set active
```

Підготувати architecture для Universal Links / App Links.

Deferred deep linking можна винести в наступний release.

---

# 15. ЕТАП 12 — Preview Mode

Web Builder повинен мати можливість переглядати draft configuration.

Mobile application повинна підтримати internal/dev preview mechanism.

Наприклад:

```text
previewToken
```

або preview config API.

Production user ніколи не повинен випадково отримати Draft.

---

# 16. ЕТАП 13 — Caching & Offline behavior

Кешувати:

```text
last valid mobile config
theme
navigation
basic tenant information
```

Якщо backend тимчасово недоступний:

```text
open last valid config
```

Показати non-blocking offline state.

---

# 17. ЕТАП 14 — Analytics Events

Підготувати abstraction:

```text
tenant_selected
promotion_opened
coupon_opened
loyalty_card_opened
product_opened
retailer_joined
```

Event повинен містити:

```text
tenantId
```

але не передавати sensitive data без необхідності.

---

# 18. ЕТАП 15 — Hardening

Перевірити:

- invalid configuration;
- missing images;
- broken URL;
- API timeout;
- unauthorized;
- tenant removed;
- feature disabled;
- configuration changed;
- stale cache;
- unknown block;
- unknown navigation item.

---

# 19. Tests

Обов'язково:

## Unit

```text
config validation
theme generation
feature flag logic
component registry
```

## Integration

```text
Tenant A → config A

switch →

Tenant B → config B
```

Особливо:

```text
Tenant A loyalty data
NEVER
appears in Tenant B context
```

---

# 20. Заборонені рішення

НЕ:

```text
if tenant === "pchilka"
```

НЕ:

```text
switch retailerName
```

НЕ hardcode retailer layouts.

НЕ створювати окремі screens:

```text
PchilkaHome
FoodMarketHome
FreshHome
```

Замість цього:

```text
PageRenderer(config)
```

---

# 21. Робочий процес Codex

Для кожного етапу:

1. проаналізуй задачу;
2. покажи короткий implementation plan;
3. реалізуй тільки цей етап;
4. запусти build;
5. запусти lint;
6. запусти tests;
7. виправ errors;
8. онови documentation;
9. створи короткий `STAGE_REPORT.md`.

Не переходь автоматично через багато великих етапів за одну ітерацію.

---

# 22. Головний результат

Mobile має бути:

```text
ONE APPLICATION
ONE CODEBASE
ONE MOBILE ENGINE

+

N TENANTS
N THEMES
N LAYOUTS
N FEATURE SETS
```

без окремого mobile source code для кожного retailer.