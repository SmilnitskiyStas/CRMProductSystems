# MASTER SPEC — Multi-Tenant Retail & Loyalty Platform

## 1. Мета продукту

Необхідно побудувати SaaS-платформу для продуктових рітейлерів, магазинів та торгових мереж.

Платформа повинна дозволяти багатьом незалежним підприємствам використовувати одну інфраструктуру, але мати повністю ізольовані:

- дані;
- покупців;
- магазини;
- бонусні програми;
- акції;
- товари;
- новини;
- push-повідомлення;
- дизайн;
- мобільний інтерфейс;
- функціональні можливості.

Архітектурна модель:

```text
Platform
│
├── Tenant A — Retailer A
│   ├── Stores
│   ├── Customers
│   ├── Loyalty
│   ├── Promotions
│   └── Mobile configuration
│
├── Tenant B — Retailer B
│   └── ...
│
└── Tenant N
```

Ключове поняття системи:

```text
tenantId
```

Tenant — окреме підприємство / мережа / рітейлер.

Дані одного tenant ніколи не повинні бути доступні іншому tenant.

---

# 2. Основна концепція мобільного застосунку

Повинен існувати один загальний мобільний застосунок платформи.

Покупець встановлює його один раз.

Всередині він може підключити один або декілька рітейлерів.

Наприклад:

```text
Мої магазини

Пчілка
1250 бонусів

Food Market
420 бонусів

Fresh
810 бонусів
```

Користувач не повинен щоразу обирати магазин при запуску.

Застосунок повинен запам'ятовувати:

```text
activeTenantId
```

і при наступному запуску автоматично відкривати останнього активного рітейлера.

---

# 3. Ізоляція UX рітейлерів

Після переходу всередину конкретного retailer користувач повинен бачити тільки його середовище.

Наприклад:

```text
Пчілка
```

може мати:

- жовту тему;
- власну головну сторінку;
- власне меню;
- власну loyalty card;
- акції;
- каталог;
- новини.

Інший retailer може мати зовсім інший layout.

При цьому mobile engine залишається один.

---

# 4. Mobile App Builder / Retail CMS

Рітейлер через web admin повинен мати можливість самостійно налаштовувати свій мобільний інтерфейс.

Система повинна працювати аналогічно:

```text
WordPress + Elementor / Webflow
```

але для мобільного застосунку.

Retailer НЕ повинен отримувати можливість виконувати власний JavaScript/React-код.

Необхідно створити контрольований component-based builder.

---

# 5. Основний принцип UI Builder

Backend зберігає декларативний опис UI.

НЕ зберігати JSX, HTML або executable code.

Наприклад:

```json
{
  "page": "home",
  "blocks": [
    {
      "id": "block-1",
      "type": "heroBanner",
      "order": 1,
      "props": {
        "height": "large"
      }
    },
    {
      "id": "block-2",
      "type": "loyaltyCard",
      "order": 2,
      "props": {}
    },
    {
      "id": "block-3",
      "type": "promotionCarousel",
      "order": 3,
      "props": {
        "title": "Акції тижня",
        "limit": 10
      }
    }
  ]
}
```

Mobile application повинен мати Component Registry:

```typescript
const componentRegistry = {
  heroBanner: HeroBanner,
  loyaltyCard: LoyaltyCard,
  promotionCarousel: PromotionCarousel,
  productGrid: ProductGrid,
  categories: Categories,
  newsList: NewsList,
};
```

Mobile renderer будує сторінку відповідно до server configuration.

---

# 6. Початковий перелік UI Blocks

Перший набір:

```text
HeroBanner
BannerCarousel

LoyaltyCard
LoyaltyBalance
LoyaltyProgress

PromotionCarousel
PromotionGrid

ProductCarousel
ProductGrid
ProductCategories

CouponCarousel
CouponGrid

NewsList
NewsCarousel

StoreList
NearestStores

QuickActions

TextBlock
ImageBlock

SectionHeader

PersonalOffers

RecentReceipts
```

Архітектура повинна дозволяти додавати нові типи без переписування renderer.

---

# 7. Theme Engine

Tenant повинен мати власну тему.

Приклад:

```json
{
  "colors": {
    "primary": "#FFD600",
    "secondary": "#222222",
    "background": "#FFFFFF",
    "surface": "#F7F7F7",
    "textPrimary": "#111111",
    "textSecondary": "#777777"
  },
  "buttons": {
    "radius": 14
  },
  "cards": {
    "radius": 18
  },
  "spacing": "comfortable"
}
```

Дозволяється конфігурація тільки за whitelist параметрів.

---

# 8. Navigation Builder

Retailer повинен мати можливість керувати bottom navigation.

Наприклад:

```json
[
  {
    "type": "home",
    "label": "Головна",
    "icon": "home"
  },
  {
    "type": "promotions",
    "label": "Акції",
    "icon": "tag"
  },
  {
    "type": "loyalty",
    "label": "Картка",
    "icon": "qr"
  },
  {
    "type": "stores",
    "label": "Магазини",
    "icon": "map"
  },
  {
    "type": "profile",
    "label": "Профіль",
    "icon": "user"
  }
]
```

Повинні існувати обмеження:

- мінімум 2 пункти;
- максимум 5 основних пунктів;
- системні критичні сторінки не можна видаляти повністю;
- invalid configuration не може бути опублікована.

---

# 9. Feature Flags

Кожен tenant має набір можливостей.

Приклад:

```json
{
  "loyalty": true,
  "promotions": true,
  "catalog": true,
  "coupons": true,
  "news": true,
  "receipts": false,
  "delivery": false,
  "personalOffers": true
}
```

Mobile application не повинен відображати функціонал, який вимкнений.

Backend також повинен забороняти API операції для disabled features.

---

# 10. Draft / Preview / Publish

Конфігурація mobile application не повинна змінювати production одразу.

Workflow:

```text
Draft
↓
Preview
↓
Validation
↓
Publish
↓
Production
```

Потрібно зберігати версії:

```text
Version 1
Version 2
Version 3
...
```

Має бути:

```text
Rollback
```

на попередню published version.

---

# 11. Mobile Configuration API

Основний endpoint:

```http
GET /api/v1/mobile/config
```

Конфігурація визначається authenticated user + active tenant.

Можливий response:

```json
{
  "schemaVersion": 1,
  "configVersion": 12,

  "tenant": {
    "id": "uuid",
    "slug": "pchilka",
    "name": "Пчілка",
    "logoUrl": "..."
  },

  "theme": {},

  "features": {},

  "navigation": [],

  "pages": {
    "home": {
      "blocks": []
    }
  }
}
```

Обов'язково:

```text
schemaVersion
configVersion
```

для backward compatibility.

---

# 12. Tenant Discovery

Покупець повинен мати можливість:

- знайти магазин;
- відсканувати QR;
- відкрити deep link;
- підключити retailer;
- видалити retailer зі своїх;
- перемикатися між підключеними retailer.

---

# 13. QR / Deep Link onboarding

Приклад:

```text
https://app.platform.com/join/pchilka
```

Якщо app встановлений:

```text
Open app
↓
Find tenant pchilka
↓
Add retailer
↓
Set active tenant
```

Якщо app не встановлений:

```text
Store
↓
Install
↓
Onboarding
↓
Retailer connection
```

Deferred linking бажано підтримати окремим етапом.

---

# 14. User ↔ Tenant relationship

Один global user може належати декільком tenant.

Не робити окремий account для кожного retailer.

Приклад:

```text
User
id = U1

UserTenant
U1 → Pchilka
U1 → FoodMarket
U1 → Fresh
```

Але loyalty account повинен бути tenant-specific:

```text
LoyaltyAccount

userId
tenantId
balance
cardNumber
tier
```

---

# 15. Безпека multi-tenancy

Це абсолютна вимога.

Кожна tenant-specific entity повинна мати:

```text
tenantId
```

Backend не повинен довіряти tenantId з request body.

Поточний tenant визначається через authorized tenant context.

Необхідно реалізувати централізований:

```text
TenantContext
```

і repository/query filtering.

Обов'язкові automated tests:

```text
Tenant A cannot read Tenant B data
Tenant A cannot modify Tenant B data
Tenant A cannot access Tenant B files
Tenant A cannot publish Tenant B configuration
```

---

# 16. Основні домени системи

Архітектуру необхідно підготувати під:

```text
Identity

Tenants
Retailers
Stores

Users
Customers

Loyalty
LoyaltyTransactions

Promotions
Coupons

Products
Categories
Catalog

Receipts

News

PushNotifications

MobileConfiguration

MobileThemes

AppBuilder

FeatureFlags

Analytics

Audit
```

Не обов'язково реалізовувати все одразу.

---

# 17. API Contract

Backend є source of truth.

Backend команда повинна вести:

```text
openapi.json
```

або інший OpenAPI contract.

Mobile НЕ повинен самостійно вигадувати API.

Mobile API client повинен генеруватись або строго відповідати OpenAPI.

При breaking API change:

```text
/api/v2/
```

або контрольована versioning strategy.

---

# 18. Shared contracts

Необхідно мати документ:

```text
/contracts/mobile-config.schema.json
```

Це JSON Schema для Mobile Configuration.

Він є контрактом між:

```text
Backend
Web Builder
Mobile Renderer
```

Зміни schema повинні бути backward compatible або збільшувати:

```text
schemaVersion
```

---

# 19. Development principle

ЗАБОРОНЕНО реалізовувати весь продукт одним великим етапом.

Розробка повинна бути incremental.

Кожен етап:

```text
Plan
↓
Implement
↓
Test
↓
Document
↓
Verify
↓
Only then next stage
```

Після кожного етапу система повинна залишатися runnable.

---

# 20. Definition of Done

Завдання вважається завершеним тільки якщо:

- implementation виконано;
- project builds;
- lint проходить;
- tests проходять;
- migrations працюють;
- API documented;
- error states handled;
- loading states handled;
- security перевірена;
- немає tenant leakage;
- документація оновлена.

---

# 21. Головний принцип

Не створювати:

```text
окрему логіку Pchilka
окрему логіку FoodMarket
окрему логіку Fresh
```

Створювати:

```text
один reusable retail engine
+
tenant configuration
+
feature flags
+
server-driven UI
```

Це фундаментальна архітектурна вимога продукту.