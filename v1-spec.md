# ShelfGuard v1.0 — Специфікація MVP
> Читай CLAUDE.md перед цим файлом.
> Ця версія: Shelf Manager + CRM ядро + HR + Сповіщення

---

## 🗂️ ЗМІСТ

1. [Модулі v1.0](#1-модулі-v10)
2. [Бізнес-логіка](#2-бізнес-логіка)
3. [Ролі та права](#3-ролі-та-права)
4. [База даних — повна схема](#4-база-даних)
5. [API ендпоінти](#5-api-ендпоінти)
6. [Функціонал Web](#6-функціонал-web)
7. [Функціонал Mobile](#7-функціонал-mobile)
8. [Сповіщення](#8-сповіщення)
9. [Фази розробки](#9-фази-розробки)

---

## 1. МОДУЛІ v1.0

### 1.1 Shelf Manager
Відстеження термінів придатності товарів у реальному часі.
FEFO логіка, кольорові статуси, система пропозицій дій.

### 1.2 CRM/ERP ядро
Товарна база, партії, склад, постачальники, переміщення між магазинами,
ланцюг поставок (ЦС → магазин або пряма доставка).

### 1.3 HR (Управління персоналом)
Ролі, права доступу, картки працівників, лог активності.

### 1.4 Notifications
Telegram Bot, Push (expo), Email. Черга через BullMQ.

### 1.5 Provider Panel
Super Admin для власника платформи: всі клієнти, health-check, impersonation.

---

## 2. БІЗНЕС-ЛОГІКА

### 2.1 FEFO — First Expired, First Out

Один товар (product) = кілька партій (product_stock) з різними термінами.

```
Товар: "Молоко Яготинське 2.5% 1л"
  ├── Партія A: 200 шт, до 03.2026  ← продавати ПЕРШОЮ
  ├── Партія B: 150 шт, до 09.2026
  └── Партія C: 180 шт, до 03.2027  ← продавати ОСТАННЬОЮ
```

При будь-якому списанні → завжди брати партію з найближчим expiry_date.

### 2.2 Статуси партії та cron логіка

```
Cron: щогодини перевіряє ВСІ партії де quantity > 0

days_left = expiry_date - CURRENT_DATE

> 14 днів     → status = 'safe'
7..14 днів    → status = 'warning'    → сповіщення (1 раз, запис notified_warning_at)
1..6 днів     → status = 'critical'   → сповіщення + пропозиція дій
0 і менше     → status = 'expired'    → термінове сповіщення всім
quantity = 0  → status = 'sold_out'   → cron ІГНОРУЄ
last_checked_at > 90 днів → status = 'needs_verification' → сповіщення без терміну
```

### 2.3 Система пропозицій при warning/critical

```
Аналіз при зміні статусу:

IF дефіцит цього товару в іншому магазині мережі:
  → "Перемістити в Магазин №X (дефіцит Y шт)"

IF є виробництво/кулінарія в цьому магазині:
  → "Передати в кулінарію"

IF залишок > min_stock * 1.5:
  → "Встановити знижку X%"

IF є договір повернення з постачальником:
  → "Повернути постачальнику"

ELSE:
  → "Списати"

Менеджер бачить пропозиції → обирає дію одним кліком.
Можна налаштувати auto_action (без підтвердження).
```

### 2.4 Ланцюг поставок

```
Ланцюг А (через ЦС):
  Постачальник → ЦС (прийомка: вносить партії) →
  Переміщення ЦС→Магазин (дані партій передаються) →
  Магазин підтверджує кількість (не вводить дані заново)

Ланцюг Б (пряма доставка):
  Б1: Постачальник вносить дані через Supplier Portal →
      Магазин тільки підтверджує кількість
  Б2: Без порталу → комірник вносить дані при прийомці

Pre-populated delivery = якщо дані є заздалегідь,
при прийомці потрібно тільки підтвердити ✓
```

### 2.5 Переміщення між локаціями

Типи рухів в `stock_movements`:
```
receipt        — прихід від постачальника
transfer       — переміщення між магазинами / ЦС→магазин
production     — передача у виробництво/кулінарію
discount       — встановлення знижки
write_off      — списання
sale           — продаж через касу (якщо є інтеграція)
adjustment     — ручне коригування після інвентаризації
return         — повернення постачальнику
```

**Правило:** при переміщенні expiry_date і batch_number НІКОЛИ не змінюються.

### 2.6 Статуси управління товаром (з ABM Inventory)

```
MTS (Make to Stock)  — завжди на полиці, регулярно замовляється → в авто-замовлення
MTO (Make to Order)  — під спеціальне замовлення, не підтримується постійно
NA  (Not Active)     — виведений з асортименту, залишки = 0
NM  (Not Managed)    — не керується, але враховується в загальних звітах
```

### 2.7 MOQ та USQ (з ABM Inventory)

```
MOQ — мінімальне замовлення постачальника (не можна замовити менше)
USQ — кратність (після MOQ кожен крок = USQ)

Приклад: MOQ=12, USQ=6 → можна: 12, 18, 24, 30...
Округлення замовлення завжди за математичними правилами до USQ
```

### 2.8 Буфер безпеки (з ABM Inventory)

```
safety_buffer — недоторканний мінімум ("краса полиці", фейсинг)
НЕ призначений для продажу.
Якщо хоч 1 одиниця з буфера безпеки продана — продаж вважається ВТРАЧЕНИМ.
Система не керує ним, але враховує при формуванні замовлення (v2.0).
```

---

## 3. РОЛІ ТА ПРАВА

### 3.1 Ієрархія

```
provider            — власник платформи (всі клієнти)
  └── enterprise_admin   — власник/директор підприємства
        └── network_manager    — керівник мережі
              └── store_manager      — менеджер магазину
                    ├── merchandiser       — мерчандайзер (mobile)
                    ├── storekeeper        — комірник (mobile + web)
                    └── cashier            — касир (v3.0)
```

### 3.2 Матриця прав

| Дія | provider | enterprise_admin | network_manager | store_manager | merchandiser | storekeeper |
|-----|:--------:|:----------------:|:---------------:|:-------------:|:------------:|:-----------:|
| Всі підприємства | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Impersonation | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Налаштування підприємства | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Додати магазин | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Управління персоналом | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Перегляд товарів/партій | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Додавання партій | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Прийомка товару | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Підтвердження списання | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Переміщення між магазинами | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Налаштування знижок | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Аналітика | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Білінг | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |

---

## 4. БАЗА ДАНИХ

### 4.1 RLS шаблон (застосувати до КОЖНОЇ таблиці з даними)

```sql
ALTER TABLE {table_name} ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON {table_name}
  USING (tenant_id = current_setting('app.tenant_id')::uuid);

CREATE POLICY provider_bypass ON {table_name}
  USING (current_setting('app.role') = 'provider');
```

### 4.2 Повна SQL схема

```sql
-- ==================== CORE ====================

CREATE TABLE tenants (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name VARCHAR(255) NOT NULL,
  slug VARCHAR(100) UNIQUE NOT NULL,
  plan VARCHAR(50) DEFAULT 'basic',
  modules JSONB DEFAULT '[]',
  -- ['shelf_manager','crm','notifications','auto_order']
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID REFERENCES tenants(id), -- NULL для provider
  email VARCHAR(255) UNIQUE NOT NULL,
  phone VARCHAR(20),
  full_name VARCHAR(255) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  role VARCHAR(50) NOT NULL,
  -- provider/enterprise_admin/network_manager/store_manager/merchandiser/storekeeper/cashier
  store_id UUID,
  telegram_chat_id VARCHAR(100),
  push_token TEXT,
  is_active BOOLEAN DEFAULT true,
  last_active_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ==================== STRUCTURE ====================

CREATE TABLE stores (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  name VARCHAR(255) NOT NULL,
  address TEXT,
  latitude DECIMAL(10,7),   -- для погоди (v2.0)
  longitude DECIMAL(10,7),  -- для погоди (v2.0)
  type VARCHAR(50) NOT NULL,
  -- shop / central_warehouse / production / distribution
  floor_plan JSONB,
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE store_zones (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  store_id UUID NOT NULL REFERENCES stores(id),
  name VARCHAR(255) NOT NULL,
  type VARCHAR(50) NOT NULL,
  -- shelf / fridge / freezer / display / production / warehouse
  position JSONB, -- {x, y, width, height} для drag&drop
  shelves_count INT DEFAULT 1,
  temp_min DECIMAL(5,1),
  temp_max DECIMAL(5,1),
  is_active BOOLEAN DEFAULT true
);

CREATE TABLE categories (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  name VARCHAR(255) NOT NULL,
  parent_id UUID REFERENCES categories(id),
  is_active BOOLEAN DEFAULT true
);

-- Сегменти товарів (для канібалізації акцій, v2.0)
-- Закладаємо таблицю вже в v1.0
CREATE TABLE product_segments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  name VARCHAR(255) NOT NULL,  -- "Молоко 2.5%", "Вода негазована 1.5л"
  category_id UUID REFERENCES categories(id),
  description TEXT,
  is_active BOOLEAN DEFAULT true
);

CREATE TABLE suppliers (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  name VARCHAR(255) NOT NULL,
  edrpou VARCHAR(20),
  contact_person VARCHAR(255),
  phone VARCHAR(20),
  email VARCHAR(255),
  delivery_days INT DEFAULT 3,
  has_supplier_portal BOOLEAN DEFAULT false,
  return_policy BOOLEAN DEFAULT false,
  payment_terms TEXT,
  notes TEXT,
  is_active BOOLEAN DEFAULT true
);

-- ==================== PRODUCTS ====================

CREATE TABLE products (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  barcode VARCHAR(100),
  name VARCHAR(255) NOT NULL,
  category_id UUID REFERENCES categories(id),
  segment_id UUID REFERENCES product_segments(id),
  unit VARCHAR(20) DEFAULT 'шт',

  -- Управління запасами (ABM статуси)
  management_type VARCHAR(10) DEFAULT 'MTS',
  -- MTS / MTO / NA / NM

  -- Буфери (закладаємо для v2.0 Auto Order)
  min_stock DECIMAL(10,2) DEFAULT 0,    -- мінімальний буфер
  max_stock DECIMAL(10,2) DEFAULT 0,    -- максимальний буфер
  safety_buffer DECIMAL(10,2) DEFAULT 0, -- буфер безпеки (ББ, фейсинг)

  -- Зберігання
  storage_temp_min DECIMAL(5,1),
  storage_temp_max DECIMAL(5,1),
  shelf_life_days INT, -- стандартний термін придатності

  -- Постачальник за замовчуванням
  default_supplier_id UUID REFERENCES suppliers(id),

  -- Ціни
  vat_rate DECIMAL(5,2) DEFAULT 20,
  price_purchase DECIMAL(12,2),
  price_retail DECIMAL(12,2),

  image_url TEXT,
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- MOQ/USQ по кожному постачальнику (ABM параметри)
CREATE TABLE product_supplier_settings (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  product_id UUID NOT NULL REFERENCES products(id),
  supplier_id UUID NOT NULL REFERENCES suppliers(id),
  moq DECIMAL(10,2) DEFAULT 1,
  usq DECIMAL(10,2) DEFAULT 1,
  price_purchase DECIMAL(12,2),
  delivery_days INT DEFAULT 3,
  is_primary BOOLEAN DEFAULT false,
  is_active BOOLEAN DEFAULT true,
  UNIQUE(product_id, supplier_id, tenant_id)
);

-- ==================== STOCK ====================

CREATE TABLE product_stock (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  product_id UUID NOT NULL REFERENCES products(id),
  store_id UUID NOT NULL REFERENCES stores(id),
  zone_id UUID REFERENCES store_zones(id),
  shelf_number INT,
  batch_number VARCHAR(100),

  quantity DECIMAL(10,2) NOT NULL,
  quantity_initial DECIMAL(10,2) NOT NULL,

  expiry_date DATE NOT NULL,
  status VARCHAR(30) DEFAULT 'safe',
  -- safe/warning/critical/expired/sold_out/archived/needs_verification

  source_type VARCHAR(50),  -- receipt/transfer/production_output
  source_id UUID,

  added_by UUID REFERENCES users(id),
  added_at TIMESTAMPTZ DEFAULT NOW(),
  last_checked_at TIMESTAMPTZ DEFAULT NOW(),

  -- Захист від дублювання сповіщень
  notified_warning_at TIMESTAMPTZ,
  notified_critical_at TIMESTAMPTZ
);

CREATE INDEX idx_stock_expiry_active
  ON product_stock(expiry_date)
  WHERE quantity > 0 AND status NOT IN ('sold_out', 'archived');

CREATE INDEX idx_stock_tenant_store
  ON product_stock(tenant_id, store_id);

-- Центральна таблиця рухів товару
CREATE TABLE stock_movements (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  movement_type VARCHAR(50) NOT NULL,
  -- receipt/transfer/production/discount/write_off/sale/adjustment/return
  product_stock_id UUID REFERENCES product_stock(id),
  product_id UUID NOT NULL REFERENCES products(id),
  from_store_id UUID REFERENCES stores(id),
  to_store_id UUID REFERENCES stores(id),
  from_zone_id UUID REFERENCES store_zones(id),
  to_zone_id UUID REFERENCES store_zones(id),
  quantity DECIMAL(10,2) NOT NULL,
  quantity_before DECIMAL(10,2),
  quantity_after DECIMAL(10,2),
  unit_price DECIMAL(12,2),
  total_amount DECIMAL(12,2),
  reference_id UUID,
  reference_type VARCHAR(50),
  performed_by UUID REFERENCES users(id),
  notes TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Заготовка для IoT (v2.0 підключить датчики сюди)
CREATE TABLE stock_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  event_type VARCHAR(50) NOT NULL,
  -- manual/pos_sale/sensor/camera/transfer
  product_stock_id UUID REFERENCES product_stock(id),
  product_id UUID REFERENCES products(id),
  store_id UUID REFERENCES stores(id),
  source_device_id VARCHAR(100), -- NULL для v1.0
  quantity_delta DECIMAL(10,2),
  confidence INT DEFAULT 100,    -- 0-100, для датчиків <100
  meta JSONB,
  performed_by UUID REFERENCES users(id),
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ==================== DOCUMENTS ====================

CREATE TABLE stock_receipts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  supplier_id UUID REFERENCES suppliers(id),
  destination_store_id UUID NOT NULL REFERENCES stores(id),
  via_central_store BOOLEAN DEFAULT false,
  status VARCHAR(30) DEFAULT 'draft',
  -- draft/ordered/in_transit/received/cancelled
  expected_at DATE,
  received_at TIMESTAMPTZ,
  created_by UUID REFERENCES users(id),
  received_by UUID REFERENCES users(id),
  notes TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE stock_receipt_items (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  receipt_id UUID NOT NULL REFERENCES stock_receipts(id),
  product_id UUID NOT NULL REFERENCES products(id),
  quantity_ordered DECIMAL(10,2) NOT NULL,
  quantity_received DECIMAL(10,2),
  price_purchase DECIMAL(12,2),
  expiry_date DATE,           -- вноситься при прийомці або постачальником
  batch_number VARCHAR(100),
  discrepancy_notes TEXT      -- причина розбіжності якщо є
);

CREATE TABLE stock_transfers (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  from_store_id UUID NOT NULL REFERENCES stores(id),
  to_store_id UUID NOT NULL REFERENCES stores(id),
  transfer_type VARCHAR(50),
  -- store_to_store/cs_to_store/store_to_production
  status VARCHAR(30) DEFAULT 'draft',
  -- draft/in_transit/received/cancelled
  initiated_by UUID REFERENCES users(id),
  confirmed_by UUID REFERENCES users(id),
  notes TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE stock_transfer_items (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  transfer_id UUID NOT NULL REFERENCES stock_transfers(id),
  product_stock_id UUID NOT NULL REFERENCES product_stock(id),
  product_id UUID NOT NULL REFERENCES products(id),
  quantity DECIMAL(10,2) NOT NULL,
  expiry_date DATE NOT NULL,    -- КОПІЯ з product_stock, не змінюється!
  batch_number VARCHAR(100)     -- КОПІЯ з product_stock
);

CREATE TABLE write_offs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  store_id UUID NOT NULL REFERENCES stores(id),
  status VARCHAR(30) DEFAULT 'draft',
  -- draft/pending_approval/approved/rejected
  reason VARCHAR(50),
  -- expired/damaged/theft/production_loss/other
  total_loss_amount DECIMAL(12,2),
  pdf_url TEXT,
  created_by UUID REFERENCES users(id),
  approved_by UUID REFERENCES users(id),
  created_at TIMESTAMPTZ DEFAULT NOW(),
  approved_at TIMESTAMPTZ
);

CREATE TABLE write_off_items (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  write_off_id UUID NOT NULL REFERENCES write_offs(id),
  product_stock_id UUID REFERENCES product_stock(id),
  product_id UUID NOT NULL REFERENCES products(id),
  quantity DECIMAL(10,2) NOT NULL,
  unit_price DECIMAL(12,2),
  loss_amount DECIMAL(12,2)
);

CREATE TABLE discounts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES tenants(id),
  product_stock_id UUID REFERENCES product_stock(id),
  product_id UUID NOT NULL REFERENCES products(id),
  store_id UUID NOT NULL REFERENCES stores(id),
  discount_percent DECIMAL(5,2) NOT NULL,
  price_original DECIMAL(12,2),
  price_discounted DECIMAL(12,2),
  reason VARCHAR(50) DEFAULT 'expiry',
  valid_from TIMESTAMPTZ DEFAULT NOW(),
  valid_until TIMESTAMPTZ,
  status VARCHAR(20) DEFAULT 'pending',
  -- pending/active/expired/cancelled
  auto_applied BOOLEAN DEFAULT false,
  created_by UUID REFERENCES users(id),
  approved_by UUID REFERENCES users(id),
  webhook_sent_at TIMESTAMPTZ
);

-- ==================== NOTIFICATIONS ====================

CREATE TABLE notification_settings (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id),
  event_type VARCHAR(100) NOT NULL,
  channel VARCHAR(50) NOT NULL,  -- telegram/push/email/webhook
  is_enabled BOOLEAN DEFAULT true,
  UNIQUE(user_id, event_type, channel)
);

CREATE TABLE notification_queue (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID REFERENCES tenants(id),
  user_id UUID REFERENCES users(id),
  channel VARCHAR(50) NOT NULL,
  event_type VARCHAR(100),
  payload JSONB,
  status VARCHAR(20) DEFAULT 'pending',
  -- pending/sent/failed
  retry_count INT DEFAULT 0,
  sent_at TIMESTAMPTZ,
  error TEXT,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ==================== LOGS ====================

CREATE TABLE activity_logs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID REFERENCES tenants(id),
  user_id UUID REFERENCES users(id),
  action VARCHAR(100) NOT NULL,
  -- 'stock.add', 'write_off.approve', 'transfer.create' etc
  entity_type VARCHAR(50),
  entity_id UUID,
  meta JSONB,
  ip_address VARCHAR(50),
  is_impersonated BOOLEAN DEFAULT false,
  created_at TIMESTAMPTZ DEFAULT NOW()
);
```

---

## 5. API ЕНДПОІНТИ

### Base
```
Production: https://api.shelfguard.com/v1
Dev:        http://localhost:3000/v1
Auth:       Authorization: Bearer <jwt_token>
Tenant:     береться з JWT payload
```

### Auth
```
POST   /auth/login
POST   /auth/refresh
POST   /auth/logout
GET    /auth/me
POST   /auth/telegram/link     — прив'язати Telegram chat_id
```

### Products
```
GET    /products                — список (?category_id, ?segment_id, ?management_type)
POST   /products
GET    /products/:id
PUT    /products/:id
DELETE /products/:id            — soft delete (is_active = false)
GET    /products/by-barcode/:code
GET    /products/:id/suppliers  — налаштування постачальників (MOQ/USQ)
POST   /products/:id/suppliers  — додати постачальника до товару
```

### Stock (Партії)
```
GET    /stock                   — (?store_id, ?status, ?zone_id, ?product_id)
POST   /stock                   — додати партію
GET    /stock/:id
PUT    /stock/:id
GET    /stock/expiring?days=7   — закінчується через N днів
GET    /stock/expired           — прострочені (quantity > 0)
GET    /stock/needs-check       — needs_verification
POST   /stock/:id/verify        — підтвердити наявність
POST   /stock/fefo-consume      — FEFO списання: { product_id, store_id, quantity }
GET    /stock/suggestions       — товари що потребують дії
```

### Movements
```
GET    /movements               — (?product_id, ?store_id, ?type, ?from, ?to)
```

### Receipts
```
GET    /receipts
POST   /receipts
GET    /receipts/:id
PUT    /receipts/:id/items      — оновити item (кількість, термін при прийомці)
PUT    /receipts/:id/receive    — підтвердити прийомку
PUT    /receipts/:id/cancel
```

### Transfers
```
GET    /transfers
POST   /transfers
GET    /transfers/:id
PUT    /transfers/:id/confirm   — підтвердити прийом на магазині
PUT    /transfers/:id/cancel
```

### Write-offs
```
GET    /write-offs
POST   /write-offs
GET    /write-offs/:id
PUT    /write-offs/:id/approve
PUT    /write-offs/:id/reject
GET    /write-offs/:id/pdf
```

### Discounts
```
GET    /discounts               — активні (?store_id, ?status)
POST   /discounts
PUT    /discounts/:id/approve
PUT    /discounts/:id/cancel
```

### Stores
```
GET    /stores
POST   /stores
GET    /stores/:id
PUT    /stores/:id
PUT    /stores/:id/floor-plan
GET    /stores/:id/zones
POST   /stores/:id/zones
PUT    /stores/:id/zones/:zoneId
DELETE /stores/:id/zones/:zoneId  — soft delete
```

### Users
```
GET    /users                   — (store_manager бачить тільки свій магазин)
POST   /users/invite
GET    /users/:id
PUT    /users/:id
DELETE /users/:id               — деактивувати (is_active = false)
GET    /users/:id/activity      — лог активності
```

### Analytics
```
GET    /analytics/expiry-summary    — зведення (?store_id, ?network=true)
GET    /analytics/write-offs        — (?from, ?to, ?store_id)
GET    /analytics/movements         — (?type, ?store_id, ?from, ?to)
GET    /analytics/by-zone
GET    /analytics/by-category
GET    /analytics/losses            — загальні збитки від списань
```

### Notifications
```
GET    /notifications/settings
PUT    /notifications/settings
GET    /notifications/history
POST   /notifications/test
```

### Provider
```
GET    /provider/tenants
GET    /provider/tenants/:id
POST   /provider/tenants/:id/impersonate
DELETE /provider/tenants/:id/impersonate
GET    /provider/health
GET    /provider/logs
PUT    /provider/tenants/:id/modules
PUT    /provider/tenants/:id/plan
```

---

## 6. ФУНКЦІОНАЛ WEB

### 6.1 Layout
- Sidebar 240px фіксований ліворуч
- Top bar: назва магазину + юзер + сповіщення
- Collapsed sidebar на tablet (тільки іконки)

### 6.2 Дашборд магазину
- 4 метрики: Safe / Warning / Critical / Expired (великі кольорові картки)
- Таблиця "Потребують уваги" з фільтром safe/warning/critical/expired
- Блок "Швидкі дії" (правий сайдбар): критичні товари + пропозиції
- Кольорова карта магазину (зони з кольоровим статусом)

### 6.3 Сторінка Залишки (/stock)
- Пошук по назві / штрихкоду
- Фільтри: магазин / зона / статус / категорія
- Dense таблиця: фото / назва / штрихкод / зона / партія / к-сть / термін / дні / статус / дії
- Мультивибір → масові дії
- Колонка "Дні" — моноширинний шрифт для вирівнювання

### 6.4 Конструктор магазину (/stores/:id/floor-plan)
- Canvas з темним фоном і сіткою
- Drag & drop зони (dnd-kit)
- Кожна зона: назва, тип, колір = гірший статус товарів
- Tooltip при hover: кількість safe/warning/critical
- Права панель: інструменти + легенда

### 6.5 Прийомка (/receipts/:id)
- Список позицій з прогресом сканування
- Кожна позиція: ordered / received (може відрізнятись)
- Pre-populated: якщо дані є — тільки підтвердити ✓
- Кнопка "Підтвердити прийомку" активна тільки якщо всі позиції опрацьовані

### 6.6 Super Admin (/provider)
- Таблиця/картки підприємств зі статусом (онлайн/офлайн)
- Health indicators: синхронізація, помилки, expired товари
- Кнопка "Увійти як клієнт" (impersonation з логуванням)
- Білінг: план, модулі, дата оплати

---

## 7. ФУНКЦІОНАЛ MOBILE

### 7.1 Стек
```
Expo SDK 51+
Expo Router (file-based navigation)
NativeWind v4
expo-camera (barcode scanning)
expo-notifications
expo-secure-store (JWT tokens)
```

### 7.2 Навігація (файлова структура Expo Router)
```
app/
├── (auth)/
│   ├── _layout.tsx
│   └── login.tsx
└── (app)/
    ├── _layout.tsx          — Bottom Tab Navigator
    ├── index.tsx            — Дашборд
    ├── scan.tsx             — Сканування (center tab)
    ├── stock/
    │   ├── index.tsx        — Мої партії
    │   └── [id].tsx         — Деталі партії
    ├── receipt/
    │   └── [id].tsx         — Прийомка
    ├── inventory/
    │   └── [zoneId].tsx     — Інвентаризація зони
    └── profile/
        └── index.tsx
```

### 7.3 Bottom Navigation
```
[🏠 Дашборд]  [📦 Залишки]  [📷 СКАН]  [📋 Задачі]  [👤 Профіль]
                              ↑ виступає вгору, акцентний колір
```

### 7.4 Ключові екрани

**Дашборд:**
- 4 статус-картки (2×2 grid)
- Велика CTA кнопка "Сканувати"
- Список завдань на сьогодні

**Сканування:**
- Повноекранна камера
- Рамка з кутами для наведення
- Bottom sheet після успішного сканування
- Підтримка: EAN-8, EAN-13, QR, Code128

**Форма партії:**
- barcode (авто з камери)
- expiry_date (DatePicker DD/MM/YYYY)
- quantity (stepper ─ / число / +)
- zone_id (picker зі списку)
- shelf_number, batch_number (необов'язково)

**Прийомка:**
- Список позицій з прогресом
- Сканування кожної позиції
- Якщо pre-populated — тільки ✓

**Інвентаризація зони:**
- Список що має бути
- Сканування що є
- Різниця в реальному часі

---

## 8. СПОВІЩЕННЯ

### 8.1 Telegram Bot
Команди:
```
/start     — реєстрація, генерує код для прив'язки в профілі
/status    — стан мого магазину (warning + critical count)
/critical  — список критичних товарів прямо зараз
/tasks     — мої завдання на сьогодні
/help      — довідка
```

Прив'язка: в профілі генерується посилання `t.me/BotName?start=CODE`

### 8.2 Події та одержувачі

| Подія | Merchandiser | Store Manager | Director | Канал |
|-------|:------------:|:-------------:|:--------:|-------|
| product.warning | ✅ (хто вніс) | ✅ | ❌ | Push + TG |
| product.critical | ✅ | ✅ | ✅ | Push + TG + Email |
| product.expired | ✅ | ✅ | ✅ | Push + TG + Email |
| write_off.needs_approval | ❌ | ✅ | ❌ | TG |
| transfer.arrived | ✅ | ✅ | ❌ | Push + TG |
| stock.needs_verification | ❌ | ✅ | ❌ | TG |
| weekly_report | ❌ | ✅ | ✅ | Email |

### 8.3 BullMQ Jobs
```
expiry-check.job      — cron: щогодини, оновлення статусів партій
notification.job      — воркер черги сповіщень (retry при помилці)
weekly-report.job     — cron: кожної неділі 08:00
cleanup.job           — cron: щодня, архівація sold_out партій > 30 днів
```

---

## 9. ФАЗИ РОЗРОБКИ

### ✅ Фаза 1 — Foundation
```
[ ] Turborepo монорепо: apps/api, apps/web, apps/mobile, packages/types, packages/utils
[ ] NestJS: структура, всі модулі (порожні контролери)
[ ] PostgreSQL: всі міграції з розділу 4
[ ] RLS для всіх таблиць
[ ] Auth: реєстрація, логін, JWT, refresh tokens, HttpOnly cookie
[ ] TenantInterceptor: встановлює app.tenant_id для RLS
[ ] RoleGuard: перевірка ролей
[ ] CRUD: tenants, users, stores, categories, suppliers, product_segments
[ ] packages/types: всі TypeScript інтерфейси
[ ] packages/utils: fefo.ts, expiry.ts
[ ] Docker Compose для локальної розробки
[ ] .env.example з усіма змінними
```

### 🔄 Фаза 2 — Shelf Manager Core
```
[ ] API: products + product_supplier_settings (MOQ/USQ)
[ ] API: product_stock CRUD + FEFO логіка
[ ] API: stock_movements логування
[ ] BullMQ: expiry-check.job (щогодинний cron)
[ ] API: discounts, write-offs
[ ] Web: авторизація, layout, sidebar навігація
[ ] Web: дашборд магазину (метрики + таблиця + карта)
[ ] Web: сторінка /stock з dense таблицею
[ ] Web: конструктор магазину (dnd-kit)
[ ] Web: картка партії з пропозиціями дій
```

### ⏳ Фаза 3 — Документи та ланцюг поставок
```
[ ] API: receipts (прийомка) + pre-populated logic
[ ] API: transfers (переміщення між магазинами)
[ ] API: write-offs + PDF генерація (puppeteer)
[ ] API: discounts + webhook до каси
[ ] Web: сторінки receipts, transfers, write-offs
[ ] Web: workflow підтвердження (draft → approved)
[ ] Web: Super Admin панель провайдера
```

### ⏳ Фаза 4 — Mobile
```
[ ] Expo: налаштування, авторизація, Expo Router
[ ] expo-camera: сканування штрихкоду
[ ] Форма внесення партії
[ ] Прийомка (pre-populated workflow)
[ ] Інвентаризація зони
[ ] Дашборд + завдання
[ ] expo-notifications: push-сповіщення
```

### ⏳ Фаза 5 — Notifications
```
[ ] telegraf.js: Telegram Bot + команди
[ ] Прив'язка акаунту (/start flow)
[ ] BullMQ: notification.job (воркер + retry)
[ ] Resend API: email шаблони (weekly report, critical)
[ ] Налаштування сповіщень у профілі
[ ] weekly-report.job
```

### ⏳ Фаза 6 — Analytics + Polish
```
[ ] Аналітичні SQL запити
[ ] Web: сторінка аналітики (Recharts графіки)
[ ] Impersonation логіка + логування
[ ] Лог активності (перегляд)
[ ] cleanup.job
```

### ⏳ Фаза 7 — Deploy
```
[ ] Docker для production
[ ] Nginx конфігурація + SSL (Let's Encrypt)
[ ] GitHub Actions CI/CD
[ ] Hetzner CPX31: налаштування
[ ] Grafana + Loki моніторинг
[ ] Щоденні бекапи (Hetzner Volume)
```
