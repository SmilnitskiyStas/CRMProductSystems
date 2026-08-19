# CLAUDE CODE SPEC — Web Admin, App Builder & Backend

## ROLE

Ти відповідаєш за:

```text
Backend
Web Admin
Retailer Dashboard
Mobile App Builder
API
Database
Security
Multi-Tenancy
```

Ти НЕ реалізовуєш React Native mobile application.

Mobile application розробляється окремим Codex agent.

Backend є source of truth.

---

# 1. Technology

Backend:

```text
ASP.NET Core API
C#
EF Core / existing persistence architecture
PostgreSQL або existing DB
```

Web:

```text
Next.js
React
TypeScript
TailwindCSS
```

Якщо repository вже має обрані libraries/patterns — продовжуй існуючу architecture.

Не проводь великі migrations без необхідності.

---

# 2. ЕТАП 0 — Repository Audit

Перед coding:

проаналізувати:

- solution structure;
- projects;
- database;
- authentication;
- authorization;
- existing entities;
- stores;
- users;
- loyalty;
- promotions;
- existing frontend;
- Docker;
- migrations;
- tests.

Створити:

```text
docs/architecture/CURRENT_STATE.md
```

та:

```text
docs/architecture/TARGET_ARCHITECTURE.md
```

Не починати великий refactor до завершення audit.

---

# 3. ЕТАП 1 — Multi-Tenant Foundation

Створити:

```text
Tenant
```

мінімальна модель:

```text
Id
Name
Slug
LogoUrl
Status
CreatedAt
UpdatedAt
```

Створити:

```text
TenantContext
```

TenantContext повинен централізовано визначати current tenant.

Не дозволяти business code самостійно діставати tenantId з request body.

---

# 4. Tenant Isolation

Усі tenant-owned сутності повинні мати:

```text
TenantId
```

Наприклад:

```text
Store
Promotion
Product
Coupon
News
LoyaltyAccount
MobileConfiguration
```

Необхідно створити централізований механізм filtering.

Найважливіший security requirement:

```text
Tenant A
cannot
read/write Tenant B data
```

Обов'язково integration tests.

---

# 5. ЕТАП 2 — User ↔ Tenant architecture

Один global user може мати relationships із декількома retailer.

Створити:

```text
UserTenant
```

Приклад:

```text
UserId
TenantId
Status
JoinedAt
```

Для retailer staff потрібна окрема модель membership/roles, якщо її ще немає.

Не змішувати:

```text
Customer membership
```

і:

```text
Retailer employee permissions
```

---

# 6. ЕТАП 3 — Mobile Configuration domain

Створити domain:

```text
MobileConfiguration
MobileConfigurationVersion
MobileTheme
```

Recommended model:

```text
MobileConfiguration

Id
TenantId
PublishedVersionId
DraftVersionId
CreatedAt
UpdatedAt
```

```text
MobileConfigurationVersion

Id
MobileConfigurationId
Version
SchemaVersion
Status
ConfigurationJson
CreatedBy
CreatedAt
PublishedAt
```

Status:

```text
Draft
Published
Archived
```

---

# 7. Configuration Schema

Створити canonical:

```text
/contracts/mobile-config.schema.json
```

Він є єдиним контрактом між:

```text
Backend
Web Builder
Mobile
```

Приклад structure:

```json
{
  "schemaVersion": 1,
  "theme": {},
  "features": {},
  "navigation": [],
  "pages": {}
}
```

Backend повинен validate configuration перед Publish.

---

# 8. ЕТАП 4 — Mobile Configuration API

Реалізувати:

```http
GET /api/v1/mobile/config
```

Current tenant визначається server-side.

Response:

```json
{
  "schemaVersion": 1,
  "configVersion": 12,

  "tenant": {},

  "theme": {},

  "features": {},

  "navigation": [],

  "pages": {}
}
```

Підтримати:

```text
ETag
```

або equivalent config caching strategy, якщо доцільно.

---

# 9. ЕТАП 5 — Retailer Admin

Створити web area:

```text
Retailer Admin
```

Початкові сторінки:

```text
Dashboard

Mobile App
  ├── Design
  ├── Pages
  ├── Navigation
  ├── Features
  └── Versions
```

RBAC.

Не кожен employee retailer повинен мати право змінювати mobile application.

---

# 10. ЕТАП 6 — Theme Editor

Реалізувати web editor:

```text
Logo

Primary color
Secondary color
Background
Surface
Text

Button radius
Card radius

Spacing preset
```

Не дозволяти arbitrary CSS.

Всі values проходять validation.

Показувати live preview.

---

# 11. ЕТАП 7 — App Builder foundation

Створити block-based editor.

UI приблизно:

```text
┌───────────────┬────────────────┬──────────────┐
│ BLOCKS        │ BUILDER        │ PREVIEW      │
│               │                │              │
│ Banner        │ Hero           │ 📱           │
│ Loyalty       │ Loyalty Card   │              │
│ Promotions    │ Promotions     │              │
│ Products      │ Products       │              │
│ News          │                │              │
└───────────────┴────────────────┴──────────────┘
```

Потрібен drag & drop.

---

# 12. Block Registry

Backend/Web повинні мати registry доступних block types.

Наприклад:

```text
heroBanner
bannerCarousel

loyaltyCard
loyaltyBalance

promotionCarousel
promotionGrid

productCarousel
productGrid

sectionHeader
quickActions

newsList
storeList
```

Для кожного типу:

```text
displayName
icon
category
defaultProps
validationSchema
supportedDataSource
```

---

# 13. Block Property Editor

При виборі block:

наприклад:

```text
Promotion Carousel
```

показати:

```text
Title
Limit
Show "View all"
Card style
```

UI генерується на основі block definition.

Не створювати величезний hardcoded editor if/else.

---

# 14. ЕТАП 8 — Page Builder

Початкові configurable pages:

```text
Home
Promotions
Catalog
News
```

Home — повністю block-driven.

Системні сторінки типу:

```text
Profile
Authentication
Security
```

можуть залишатися system-controlled.

Не все потрібно дозволяти перебудовувати retailer.

---

# 15. ЕТАП 9 — Navigation Builder

Retailer може:

- міняти order;
- міняти labels;
- вибирати permitted icon;
- enable/disable allowed navigation items.

Правила:

```text
min 2
max 5
```

Backend validation required.

---

# 16. ЕТАП 10 — Feature Flags

Створити tenant features:

```text
loyalty
promotions
catalog
coupons
news
receipts
delivery
personalOffers
```

Feature може залежати від subscription plan.

Архітектура повинна дозволяти:

```text
Plan → Features
Tenant override
```

але V1 можна почати з tenant features.

---

# 17. ЕТАП 11 — Draft / Preview / Publish

Workflow:

```text
Edit Draft
↓
Save
↓
Preview
↓
Validate
↓
Publish
```

Publish transaction має бути atomic.

Не дозволяти publish invalid schema.

---

# 18. ЕТАП 12 — Version History

Створити:

```text
Mobile App → Version History
```

Показувати:

```text
Version
Status
Published by
Published date
```

Операція:

```text
Rollback
```

Rollback не повинен фізично видаляти нові version.

Створити новий published state на основі старої.

---

# 19. ЕТАП 13 — Preview API

Необхідно дати web builder можливість показувати точний mobile representation.

Підготувати:

```http
GET /api/v1/mobile/config/preview
```

тільки для authorized retailer staff.

Або інший secure preview mechanism.

Draft не повинен бути доступний звичайним customer.

---

# 20. ЕТАП 14 — Retailer discovery API

Реалізувати:

```http
GET /api/v1/retailers
GET /api/v1/retailers/{slug}
POST /api/v1/retailers/{id}/join
DELETE /api/v1/retailers/{id}/membership
```

Підготувати:

```text
search
pagination
status
```

Пізніше можна додати геолокацію.

---

# 21. ЕТАП 15 — QR onboarding

Створити permanent retailer links:

```text
https://app.domain/join/{tenantSlug}
```

Web endpoint повинен:

- знайти tenant;
- перевірити active;
- показати fallback web page;
- підтримати mobile deep linking.

---

# 22. ЕТАП 16 — Core Retail Domains

Поступово інтегрувати existing/new:

```text
Stores
Loyalty
Promotions
Products
Categories
Coupons
News
Receipts
```

Кожен API повинен бути tenant-aware.

---

# 23. ЕТАП 17 — Audit

Особливо важливі операції:

```text
Mobile config changed
Published
Rolled back

Feature changed
Role changed
Promotion edited
```

Audit:

```text
TenantId
UserId
Action
Entity
EntityId
Timestamp
Metadata
```

---

# 24. ЕТАП 18 — Subscription-ready architecture

Не потрібно одразу будувати billing.

Але feature architecture повинна дозволяти:

```text
START
BUSINESS
PRO
ENTERPRISE
```

майбутню прив'язку:

```text
SubscriptionPlan → Features
```

Не hardcode тарифну логіку всередині UI.

---

# 25. Web UX

Builder повинен бути зрозумілим не програмісту.

Мета:

> manager retailer може сам змінити mobile app без звернення до developer.

Потрібні:

```text
autosave draft
unsaved changes indication
preview
validation messages
undo where reasonable
publish confirmation
```

---

# 26. Security

Обов'язково:

```text
RBAC
Tenant isolation
Input validation
Output encoding
Rate limiting where required
Secure file upload
Audit
```

Не дозволяти upload executable content.

Images проходять type/size validation.

---

# 27. API Rules

API version:

```text
/api/v1/
```

Use consistent:

```text
ProblemDetails
```

або existing structured errors.

Pagination standardized.

Dates:

```text
UTC
```

except explicitly localized display logic.

---

# 28. OpenAPI

Backend повинен генерувати:

```text
openapi.json
```

Mobile agent використовує його як contract.

Після API change:

```text
update OpenAPI
update integration docs
```

---

# 29. Integration document

Підтримувати:

```text
docs/integration/MOBILE_API.md
```

Для кожного endpoint:

```text
purpose
auth
tenant behavior
request
response
errors
```

---

# 30. Testing

## Unit

```text
config validator
feature rules
versioning
theme validation
```

## Integration

```text
Tenant isolation
RBAC
Publish flow
Rollback
Join retailer
Mobile config
```

Особливий test suite:

```text
TENANT_ISOLATION_TESTS
```

Його падіння має блокувати release.

---

# 31. Migration safety

Кожен DB stage:

```text
migration
↓
apply locally
↓
test
↓
rollback strategy
```

Не робити destructive migration без плану.

---

# 32. Development workflow Claude Code

Перед кожним етапом:

```text
1. Inspect current code
2. Write implementation plan
3. Identify affected modules
4. Implement
5. Migrations
6. Tests
7. Build
8. Documentation
9. Stage report
```

Не намагайся реалізувати всі етапи за один запуск.

---

# 33. Communication із Mobile Agent

Mobile розробляє Codex.

Не створюй mobile implementation.

При зміні API/schema:

оновити:

```text
openapi.json
mobile-config.schema.json
MOBILE_API.md
```

і створити:

```text
docs/integration/CHANGELOG.md
```

Приклад:

```text
2026-08-15
Added promotionGrid block
Schema remains version 1
Backward compatible
```

---

# 34. Заборонені рішення

НЕ:

```text
if tenant.Name == "Pchilka"
```

НЕ створювати database tables:

```text
PchilkaPromotions
FoodMarketPromotions
```

НЕ робити окремі backend deployments для кожного tenant.

НЕ зберігати JSX / JavaScript / executable code в mobile config.

НЕ дозволяти retailer arbitrary HTML/CSS/JS.

---

# 35. Target architecture

```text
                         PLATFORM
                            │
              ┌─────────────┴─────────────┐
              │                           │
          Web Admin                   Mobile App
              │                           │
        App Builder                  Config Renderer
              │                           │
              └─────────────┬─────────────┘
                            │
                     ASP.NET Core API
                            │
              ┌─────────────┼─────────────┐
              │             │             │
           Tenant A      Tenant B      Tenant C
              │             │             │
            Data A        Data B        Data C
```

---

# 36. Головний результат

Після завершення основних етапів retailer повинен мати можливість:

```text
Login
↓
Mobile Application
↓
Choose Theme
↓
Configure Colors
↓
Configure Navigation
↓
Drag & Drop Blocks
↓
Configure Content
↓
Preview
↓
Publish
```

а покупець:

```text
Open one shared application
↓
Select/Add retailer
↓
Application loads retailer configuration
↓
See completely retailer-specific experience
```

без нового App Store / Google Play release при кожній зміні дизайну.