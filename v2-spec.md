# ShelfGuard v2.0 — Auto Order + AI Forecasting
> Читай CLAUDE.md перед цим файлом.
> Передумова: v1.0 стабільний і є 3+ місяці реальних даних продажів.

---

## 🎯 МЕТА v2.0

Розумне автозамовлення на основі:
- ADU (середньодобове споживання)
- Динамічного буфера (CDA алгоритм)
- Погодних умов (Open-Meteo API)
- Календаря подій та свят
- Акцій і канібалізації попиту
- AI агента (Claude API) з редагуванням менеджером

---

## 🗂️ ЗМІСТ

1. [ADU розрахунок](#1-adu)
2. [Динамічний буфер CDA](#2-cda-буфер)
3. [Формула замовлення](#3-формула-замовлення)
4. [Календар подій](#4-календар-подій)
5. [Акції та канібалізація](#5-акції-та-канібалізація)
6. [Погодні умови](#6-погодні-умови)
7. [AI агент замовлень](#7-ai-агент)
8. [База даних v2.0](#8-база-даних-v20)
9. [API ендпоінти v2.0](#9-api-v20)
10. [UI для менеджера](#10-ui)
11. [Фази розробки v2.0](#11-фази)

---

## 1. ADU — Average Daily Usage

ADU = середньодобове споживання за валідні дні.

### Валідний день:
```
День є валідним якщо ОДНОЧАСНО:
  ✓ Товар НЕ перебуває в активній акції
  ✓ Залишок АБО продаж товару > 0
  ✗ Виключаються дні з оптовими продажами (якщо ADU не 0)
  ✗ Виключаються дні з нестандартним попитом (аномалії)

Розраховується тільки для:
  - management_type = 'MTS'
  - store активний
  - є активний графік поставки
  - товар НЕ в активній акції
```

### Три групи товарів (залежить від налаштувань компанії):
```
Група 01 — Рідко продаються:
  Період розрахунку: 90 днів
  Мінімум валідних днів: 10

Група 02 — Продаються середньо:
  Період розрахунку: 60 днів
  Мінімум валідних днів: 15

Група 03 — Часто продаються і FRESH товари:
  Період розрахунку: 30 днів
  Мінімум валідних днів: 20
```

### Формула:
```
ADU = SUM(sales_valid_days) / COUNT(valid_days)

Де sales_valid_days — продажі тільки у валідних днях
```

---

## 2. CDA Буфер — Consumption Driven Algorithm

Буфер = цільовий рівень запасу. Ділиться на 3 зони.

### Три зони буфера:
```
┌─────────────────────────────┐
│  🟢 ЗЕЛЕНА (верхня ~50%)    │ — Забезпечення попиту на повний цикл
│  ADU × (LT + OC)            │   LT = lead time (час доставки)
│                             │   OC = order cycle (між замовленнями)
├─────────────────────────────┤
│  🟡 ЖОВТА (середня ~30%)    │ — Запас на нерівномірність попиту
│  ADU × OC × variability     │   variability = коефіцієнт варіабельності
│                             │
├─────────────────────────────┤
│  🔴 ЧЕРВОНА (нижня ~20%)    │ — Страховий запас (несподівані події)
│  ADU × LT × safety_factor   │   safety_factor = 0.5..1.5 (налаштовується)
└─────────────────────────────┘

Повний буфер = Зелена + Жовта + Червона зони
```

### Правила CDA:
```
1. Буфер розраховується в день замовлення (не щодня)
2. До наступного замовлення буфер не змінюється
3. LT і OC розраховуються динамічно в момент розрахунку
4. Замовлення округляється за USQ (математично)
5. Швидка реакція: ADU змінився → наступне замовлення вже враховує це
```

---

## 3. Формула замовлення

```
Замовлення = Буфер + ББ - Залишок - В_дорозі + ОЗ + РТО

де:
  Буфер     — цільовий рівень (CDA розрахунок)
  ББ        — буфер безпеки (safety_buffer з products)
  Залишок   — quantity на кінець попереднього дня
  В_дорозі  — замовлено але ще не прийшло (status=in_transit)
  ОЗ        — одноразове замовлення (разова потреба)
  РТО       — зарезервовано під конкретного покупця (MTO)

Округлення:
  IF результат < MOQ → замовити MOQ
  IF результат > MOQ → округлити до найближчого кратного USQ
```

### Коефіцієнти подій (множники):
```
Фінальне_замовлення = Базове_замовлення
  × weather_coefficient    (з погоди)
  × event_coefficient      (зі свята/події)
  × promo_coefficient      (з акції або канібалізації)

де всі коефіцієнти незалежні і перемножуються
```

---

## 4. Календар подій

### Типи подій:
```
holiday       — свято (Новий рік, Великдень, 8 березня, День Незалежності)
promo         — акція на конкретний товар або категорію
local_event   — місцева подія (фестиваль, ярмарок поруч з магазином)
season_start  — початок сезону (шкільний, літній, зимовий)
custom        — довільна подія менеджера
```

### Коефіцієнти за замовчуванням (редагуються):
```
Новий рік (25 груд — 2 січ):
  Алкоголь:          × 2.5
  Кондитерські:      × 3.0
  Молочні:           × 1.2
  М'ясо:             × 1.8

Великдень (-7..+1 дні):
  Борошно, яйця, масло: × 3.5
  М'ясо:             × 2.0
  Кондитерські:      × 2.5

Початок школи (25 серп — 10 вер):
  Канцелярія:        × 5.0
  Продукти (ланч):   × 1.3
```

---

## 5. Акції та канібалізація попиту

### Принцип:
```
Якщо товар А в сегменті "Молоко 2.5%" іде по акції -30%:
  Товар А: попит × 2.0..2.5
  Товар Б (той самий сегмент, без акції): попит × 0.6..0.7
  Товар В (той самий сегмент, без акції): попит × 0.6..0.7
```

### Алгоритм при створенні акції:
```
1. Менеджер створює discount для Товару А
2. Система знаходить всі товари з тим самим segment_id в тому ж магазині
3. Автоматично генерує promo_cannibalization записи:
   - Товар А: order_coefficient = 2.0 (замовляти більше)
   - Товар Б: order_coefficient = 0.7 (замовляти менше)
   - Товар В: order_coefficient = 0.7 (замовляти менше)
4. Менеджер бачить пропозицію → може змінити коефіцієнти → підтверджує
5. При наступному замовленні формула враховує ці коефіцієнти
```

---

## 6. Погодні умови

### Джерело даних:
```
Open-Meteo API (безкоштовний, без ліміту):
  URL: https://api.open-meteo.com/v1/forecast
  Параметри: temperature_2m_max, precipitation_sum, weathercode
  Прогноз: до 16 днів
  Координати: latitude/longitude з таблиці stores

Cron: щодня 06:00 → завантажити прогноз на 7 днів для кожного магазину
```

### Коефіцієнти за температурою:
```
temp > 25°C:
  Сегмент "Вода, соки":      × 1.8
  Сегмент "Морозиво":        × 2.5
  Сегмент "Пиво":            × 1.6
  Сегмент "Гарячі напої":    × 0.7
  Сегмент "Перші страви":    × 0.6

temp < 0°C:
  Сегмент "Гарячі напої":    × 1.5
  Сегмент "Консерви, крупи": × 1.3
  Сегмент "Морозиво":        × 0.4
  Сегмент "Вода негазована": × 0.8

Дощ (precipitation > 5мм):
  Трафік магазину:            × 0.85
  (загальний множник на всі категорії)
```

---

## 7. AI Агент замовлень

### Архітектура:
```
Щодня 05:00 → BullMQ job → AI агент для кожного магазину:

1. Збір контексту:
   ├── Залишки по кожному MTS товару
   ├── ADU за 30/60/90 днів
   ├── Активні акції + канібалізація
   ├── Прогноз погоди на 7 днів
   ├── Майбутні події з календаря (наступні 14 днів)
   ├── Що "в дорозі"
   └── Графік поставок (наступне замовлення коли?)

2. Розрахунок базового замовлення:
   Замовлення = Буфер + ББ - Залишок - В_дорозі
   (математична формула, без AI)

3. Передача в Claude API:
   Базове замовлення + контекст → AI аналізує → коригує + пояснює

4. Збереження пропозиції:
   ai_order_suggestions + ai_order_suggestion_items

5. Сповіщення менеджеру:
   "Замовлення готове, перегляньте і підтвердіть"
```

### Claude API промпт (шаблон):
```
Ти — AI асистент менеджера магазину в Україні.
Проаналізуй дані та скоригуй автоматично розраховане замовлення.

КОНТЕКСТ МАГАЗИНУ:
  Назва: {store_name}
  Дата замовлення: {order_date}
  Наступна поставка: {next_delivery_date}

ПОГОДА (прогноз на 7 днів):
  {weather_forecast_json}

ПОДІЇ КАЛЕНДАРЯ (наступні 14 днів):
  {events_json}

АКТИВНІ АКЦІЇ:
  {active_promos_json}

ДАНІ ПО ТОВАРАХ:
  {stock_data_json}
  (product, current_stock, adu_30d, buffer, safety_buffer,
   in_transit, moq, usq, base_order_qty)

ЗАВДАННЯ:
Для кожного товару де base_order_qty > 0:
1. Скоригуй кількість якщо є вагомі причини (погода, події, акції)
2. Напиши коротке обґрунтування (1 речення, українською)
3. Вкажи confidence: high/medium/low
4. Вкажи фактори: {"weather": 1.4, "event": 1.0, "promo": 0.8}

Відповідай ТІЛЬКИ JSON без коментарів:
{
  "items": [
    {
      "product_id": "uuid",
      "quantity_suggested": 144,
      "reasoning": "Спека +34°C наступного тижня збільшить попит на воду",
      "confidence": "high",
      "factors": {"weather": 1.8, "event": 1.0, "promo": 1.0}
    }
  ]
}
```

### UI для менеджера:
```
Замовлення на {дата}       Магазин: {назва}

⚡ AI пропозиція готова ({N} позицій)
────────────────────────────────────────────────────────────
Товар              Базове  AI пропонує  Ваша зміна  Причина
────────────────────────────────────────────────────────────
Вода Моршин 1.5л    80      144 (+80%)    [144]     🌡️ Спека +34°C
Морозиво Рудь       40       96 (+140%)   [ 80]*    🌡️ Спека, ✏️ змінено
Молоко Яготин 2.5%  36       72 (+100%)   [ 72]     🏷️ Акція
Молоко Галичина     30       21 (-30%)    [ 21]     🏷️ Канібалізація
────────────────────────────────────────────────────────────
* — змінено менеджером

[  Підтвердити замовлення ({N} позицій)  ]
[  Скасувати  ]
```

---

## 8. БАЗА ДАНИХ v2.0

```sql
-- Графік поставок
CREATE TABLE supply_schedules (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  supplier_id UUID NOT NULL REFERENCES suppliers(id),
  day_of_week INT[],       -- [1,3,5] = Пн, Ср, Пт
  order_lead_days INT,     -- за скільки днів робити замовлення
  is_active BOOLEAN DEFAULT true
);

-- Щоденні продажі для ADU (заповнюється з касових даних або вручну)
CREATE TABLE daily_sales (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  product_id UUID NOT NULL REFERENCES products(id),
  date DATE NOT NULL,
  quantity_sold DECIMAL(10,2) NOT NULL,
  quantity_end_of_day DECIMAL(10,2),  -- залишок на кінець дня
  is_promo_day BOOLEAN DEFAULT false,
  is_anomaly BOOLEAN DEFAULT false,   -- виключати з ADU
  source VARCHAR(20) DEFAULT 'manual', -- manual/pos/import
  UNIQUE(store_id, product_id, date)
);

-- ADU розрахунок (кешується, перераховується при замовленні)
CREATE TABLE product_adu (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  product_id UUID NOT NULL REFERENCES products(id),
  adu_30d DECIMAL(10,4),
  adu_60d DECIMAL(10,4),
  adu_90d DECIMAL(10,4),
  adu_effective DECIMAL(10,4), -- той що використовується
  product_group SMALLINT,      -- 1/2/3 (рідко/середньо/часто)
  valid_days_30d INT,
  valid_days_60d INT,
  calculated_at TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(store_id, product_id)
);

-- Буфер (перераховується при замовленні)
CREATE TABLE product_buffer (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  product_id UUID NOT NULL REFERENCES products(id),
  buffer_total DECIMAL(10,2),
  buffer_green DECIMAL(10,2),   -- попит на повний цикл
  buffer_yellow DECIMAL(10,2),  -- нерівномірність попиту
  buffer_red DECIMAL(10,2),     -- страховий запас
  lead_time_days DECIMAL(5,1),  -- час доставки (динамічний)
  order_cycle_days DECIMAL(5,1),-- між замовленнями (динамічний)
  calculated_at TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(store_id, product_id)
);

-- Календар подій
CREATE TABLE demand_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  name VARCHAR(255) NOT NULL,
  event_type VARCHAR(50),
  scope VARCHAR(50) DEFAULT 'network', -- network/store
  store_id UUID REFERENCES stores(id),
  starts_at DATE NOT NULL,
  ends_at DATE NOT NULL,
  is_recurring BOOLEAN DEFAULT false,
  recurrence_rule VARCHAR(100),        -- RRULE
  notes TEXT,
  created_by UUID REFERENCES users(id)
);

CREATE TABLE demand_event_coefficients (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  event_id UUID NOT NULL REFERENCES demand_events(id),
  scope_type VARCHAR(20),  -- category/segment/product
  scope_id UUID,
  coefficient DECIMAL(5,2) DEFAULT 1.00,
  source VARCHAR(20) DEFAULT 'manual'  -- manual/learned/ai_suggested
);

-- Погода
CREATE TABLE weather_data (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  store_id UUID NOT NULL REFERENCES stores(id),
  date DATE NOT NULL,
  temp_min DECIMAL(5,1),
  temp_max DECIMAL(5,1),
  temp_avg DECIMAL(5,1),
  precipitation DECIMAL(6,2),
  weather_code INT,
  is_forecast BOOLEAN DEFAULT false,
  fetched_at TIMESTAMPTZ DEFAULT NOW(),
  UNIQUE(store_id, date)
);

CREATE TABLE weather_coefficients (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  segment_id UUID REFERENCES product_segments(id),
  category_id UUID REFERENCES categories(id),
  temp_above DECIMAL(5,1),   -- якщо temp > X → застосувати
  temp_below DECIMAL(5,1),   -- якщо temp < X → застосувати
  weather_code INT,
  coefficient DECIMAL(5,2) NOT NULL,
  source VARCHAR(20) DEFAULT 'manual'
);

-- Канібалізація акцій
CREATE TABLE promo_cannibalization (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  discount_id UUID NOT NULL REFERENCES discounts(id),
  affected_product_id UUID NOT NULL REFERENCES products(id),
  order_coefficient DECIMAL(5,2) NOT NULL,
  source VARCHAR(20) DEFAULT 'ai_suggested'  -- manual/ai_suggested/learned
);

-- AI пропозиції замовлень
CREATE TABLE ai_order_suggestions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  generated_at TIMESTAMPTZ DEFAULT NOW(),
  order_date DATE NOT NULL,
  context_snapshot JSONB,  -- весь контекст що передавався AI
  status VARCHAR(30) DEFAULT 'pending',
  -- pending/partially_accepted/accepted/rejected
  accepted_by UUID REFERENCES users(id),
  accepted_at TIMESTAMPTZ,
  ai_model VARCHAR(50) DEFAULT 'claude-sonnet-4-20250514',
  tokens_used INT
);

CREATE TABLE ai_order_suggestion_items (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  suggestion_id UUID NOT NULL REFERENCES ai_order_suggestions(id),
  product_id UUID NOT NULL REFERENCES products(id),
  quantity_base DECIMAL(10,2),      -- математична формула
  quantity_suggested DECIMAL(10,2), -- після AI
  quantity_final DECIMAL(10,2),     -- після редагування менеджером
  reasoning TEXT,
  confidence VARCHAR(10),           -- high/medium/low
  factors JSONB,
  -- {"weather": 1.4, "event": 1.0, "promo": 0.8, "base": 80}
  was_edited BOOLEAN DEFAULT false,
  edit_reason TEXT
);
```

---

## 9. API v2.0

### ADU та буфер
```
GET    /adu/:storeId/:productId     — поточний ADU
POST   /adu/recalculate             — перерахувати ADU для магазину
GET    /buffer/:storeId/:productId  — поточний буфер
POST   /buffer/recalculate          — перерахувати буфер
```

### Щоденні продажі
```
GET    /daily-sales                 — (?store_id, ?product_id, ?from, ?to)
POST   /daily-sales                 — ручне внесення продажів
POST   /daily-sales/import          — імпорт з CSV
PUT    /daily-sales/:id/mark-anomaly
```

### Календар подій
```
GET    /events                      — (?from, ?to, ?store_id)
POST   /events
GET    /events/:id
PUT    /events/:id
DELETE /events/:id
GET    /events/:id/coefficients
POST   /events/:id/coefficients
PUT    /events/:id/coefficients/:coefId
```

### Погода
```
GET    /weather/:storeId            — прогноз на 7 днів
GET    /weather/:storeId/history    — (?from, ?to)
POST   /weather/fetch               — тригер завантаження (адмін)
GET    /weather/coefficients        — налаштування коефіцієнтів
PUT    /weather/coefficients/:id
```

### Канібалізація
```
GET    /cannibalization/:discountId  — пропозиції канібалізації
PUT    /cannibalization/:id          — змінити коефіцієнт
POST   /cannibalization/apply/:discountId — застосувати
```

### AI замовлення
```
GET    /ai-orders                    — список пропозицій
POST   /ai-orders/generate           — згенерувати для магазину
GET    /ai-orders/:id
PUT    /ai-orders/:id/items/:itemId  — редагувати позицію
POST   /ai-orders/:id/accept         — підтвердити (всі або вибрані)
POST   /ai-orders/:id/reject
```

### Supply schedules
```
GET    /supply-schedules             — (?store_id, ?supplier_id)
POST   /supply-schedules
PUT    /supply-schedules/:id
DELETE /supply-schedules/:id
```

---

## 10. UI

Детальний дизайн-промпт для v2.0 — в `design-prompt.md`.

Ключові нові екрани:
- Дашборд замовлень (AI пропозиція + редагування)
- Календар подій (week/month view)
- Налаштування буферів по товарах
- Аналітика ADU і прогнозів
- Налаштування погодних коефіцієнтів
- Налаштування канібалізації

---

## 11. ФАЗИ РОЗРОБКИ v2.0

### Фаза 1 — Data Foundation
```
[ ] daily_sales таблиця + ручне внесення
[ ] Імпорт продажів з CSV
[ ] ADU розрахунок (три групи товарів)
[ ] supply_schedules (графік поставок)
[ ] Web: сторінка внесення продажів
```

### Фаза 2 — Buffer & Formula
```
[ ] Динамічний буфер (CDA): green/yellow/red зони
[ ] Формула замовлення: Буфер + ББ - Залишок - В_дорозі
[ ] Округлення по USQ/MOQ
[ ] Web: буфер-індикатор для кожного товару (воронка)
[ ] Web: базова сторінка замовлень
```

### Фаза 3 — Events & Weather
```
[ ] demand_events + coefficients
[ ] Передзаповнені свята (Новий рік, Великдень, 8 березня...)
[ ] Open-Meteo інтеграція + daily cron
[ ] weather_coefficients (за замовчуванням + редагування)
[ ] Вплив подій і погоди на формулу замовлення
[ ] Web: календар подій
```

### Фаза 4 — Promotions & Cannibalization
```
[ ] promo_cannibalization таблиця
[ ] Авто-генерація канібалізації при створенні акції
[ ] Web: UI підтвердження/редагування канібалізації
[ ] Вплив на формулу замовлення
```

### Фаза 5 — AI Agent
```
[ ] Claude API інтеграція
[ ] ai_order_suggestions + items
[ ] BullMQ job: щоденна генерація о 05:00
[ ] Web: дашборд AI замовлення з редагуванням
[ ] Сповіщення: "Замовлення готове до підтвердження"
[ ] Tracking: was_edited, edit_reason (навчання)
```
