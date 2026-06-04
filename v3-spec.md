# ShelfGuard v3.0 — IoT + Smart Shelf + Власна Каса
> Читай CLAUDE.md перед цим файлом.
> Передумова: v2.0 стабільний, є клієнти Enterprise рівня.

---

## 🎯 МЕТА v3.0

Фізична інтеграція з магазином:
- Датчики ваги на полицях (автоматичне оновлення залишків)
- CV камери (розпізнавання товарів і термінів)
- Власна ПРРО каса (повний контроль даних)
- Температурний моніторинг холодильників

---

## 🗂️ ЗМІСТ

1. [Датчики ваги](#1-датчики-ваги)
2. [Computer Vision камери](#2-computer-vision)
3. [Власна ПРРО каса](#3-власна-каса)
4. [Температурний моніторинг](#4-температура)
5. [База даних v3.0](#5-база-даних-v30)
6. [Фази розробки v3.0](#6-фази)

---

## 1. ДАТЧИКИ ВАГИ

### Принцип роботи:
```
Тензодатчик під секцією полиці → фіксує зміну ваги → MQTT повідомлення →
ShelfGuard API → оновлення stock_events (type: sensor) →
FEFO списання з найстарішої партії
```

### Інтеграція:
```
Протокол: MQTT (порт 1883) або HTTP webhook
Topic: shelfguard/{tenant_id}/{store_id}/{zone_id}/{shelf_id}
Payload:
  {
    "device_id": "shelf-A1-3",
    "weight_before": 2450,  // грам
    "weight_after": 2205,   // грам
    "delta": -245,          // грам
    "timestamp": "2026-06-10T14:23:11Z"
  }

Розрахунок кількості:
  delta_weight / product.weight_per_unit = кількість знятих одиниць
  Округлення математично
```

### Рівень впевненості (confidence):
```
Зняли рівно 1 unit_weight         → confidence = 95
Зняли кратно unit_weight          → confidence = 85
Некратне значення (поклали назад?) → confidence = 60 → не оновлювати, логувати

Якщо confidence < 70 → НЕ оновлювати автоматично,
логувати в stock_events для аналізу
```

---

## 2. COMPUTER VISION

### Що вміють CV камери:
```
Рівень 1 (базовий): Підрахунок кількості товарів на полиці
  - Камера → frame кожні 30 сек → YOLO → count objects
  - Порівняння з expected → alert якщо -20% від норми

Рівень 2 (розширений): Розпізнавання конкретного товару
  - Навчена модель на упаковках українських товарів
  - Визначає: який товар де стоїть (planogram compliance)

Рівень 3 (AI): Читання терміну придатності
  - OCR (Tesseract або Google Vision API)
  - Читання дати з упаковки
  - Формати: DD.MM.YYYY, MM/YY, "До:", "EXP:", тиснення
```

### Логіка при продажу:
```
Покупець сканує штрихкод на касі →
Камера робить фото товару →

IF OCR розпізнав термін:
  confidence = 90..100
  Шукаємо партію з цим терміном в stock
  IF знайдено → списуємо саме її
  IF не знайдено → попередження касиру

IF OCR не розпізнав (погане фото, тиснення):
  confidence = 0
  → FEFO автоматично (найстаріша партія)

Все логується в stock_events
```

### Технічний стек CV:
```
Камера: IP камера з RTSP стрімом ($50-150/шт)
Обробка: Python service (окремий мікросервіс)
  ├── OpenCV для захоплення кадрів
  ├── YOLOv8 для детекції об'єктів
  ├── Tesseract OCR для читання дат
  └── REST API → ShelfGuard backend
GPU: опціонально (NVIDIA Jetson для edge computing)
```

---

## 3. ВЛАСНА ПРРО КАСА

### Юридичні вимоги (Україна):
```
1. Реєстрація в ДПС як виробник програмного РРО
2. Сертифікація ПЗ (тест в акредитованій лабораторії)
3. Інтеграція з фіскальним сервером ДПС (API відкрите)
4. КЕП для підписання кожного чека
5. Резервний фіскальний пристрій (якщо немає інтернету)
```

### Технічний стек каси:
```
Додаток: React Native (планшет Android або iOS)
  ├── Сканер: expo-camera (або зовнішній USB/Bluetooth сканер)
  ├── Принтер чеків: Bluetooth/WiFi (EPSON TM-T20, Star TSP)
  ├── Термінал оплати: PAX або Ingenico SDK
  └── Фіскальний модуль: ПРРО API ДПС

Синхронізація:
  ├── Товарна база: автооновлення з ShelfGuard (WebSocket)
  ├── Ціни і акції: real-time з discounts таблиці
  └── Кожен чек → stock_events (type: pos_sale) → FEFO списання
```

### Робочий день касира:
```
Відкриття зміни:
  ├── Авторизація (PIN або відбиток)
  ├── Відкриття фіскальної зміни (ДПС API)
  └── Синхронізація товарів і цін

Продаж:
  ├── Сканування штрихкоду
  ├── Система показує актуальну ціну (з урахуванням акцій)
  ├── Якщо товар CRITICAL → автоматично ціна зі знижкою
  ├── Якщо EXPIRED → попередження! Блок продажу
  ├── Оплата: готівка / термінал / розділена
  ├── Фіскальний чек → принтер + SMS/email покупцю
  └── → stock_events → FEFO списання

Закриття зміни:
  ├── Z-звіт (автоматично до ДПС)
  ├── Інкасація
  └── Звіт касира за зміну
```

---

## 4. ТЕМПЕРАТУРНИЙ МОНІТОРИНГ

### Датчики температури:
```
Протокол: MQTT або HTTP polling
Встановлюються в: холодильники, морозильні камери, склад
Дані: temperature (°C), humidity (%), battery (%)
Частота: кожні 5 хвилин

Alert правила:
  Холодильник (+2..+6°C): якщо > +8°C → critical alert
  Морозильна камера (-18..-15°C): якщо > -12°C → critical alert
  Відключення датчика > 30 хв → alert
```

### Вплив на товари:
```
IF температура холодильника > норми на > 2 години:
  Всі партії в цій зоні → status = 'temp_violation'
  Сповіщення менеджеру + директору
  Менеджер вирішує: OK / списати / передати

Логується в stock_events (type: temp_violation)
```

---

## 5. БАЗА ДАНИХ v3.0

```sql
-- IoT пристрої
CREATE TABLE iot_devices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  zone_id UUID REFERENCES store_zones(id),
  device_type VARCHAR(50),
  -- weight_sensor/camera/temp_sensor/barcode_reader
  device_id VARCHAR(100) UNIQUE NOT NULL,  -- фізичний ID пристрою
  name VARCHAR(255),
  mqtt_topic VARCHAR(255),
  config JSONB,            -- специфічні налаштування типу пристрою
  is_active BOOLEAN DEFAULT true,
  last_seen_at TIMESTAMPTZ,
  battery_level INT,       -- % (для бездротових)
  firmware_version VARCHAR(50)
);

-- Показники температури
CREATE TABLE temperature_readings (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  device_id UUID NOT NULL REFERENCES iot_devices(id),
  store_id UUID NOT NULL REFERENCES stores(id),
  zone_id UUID REFERENCES store_zones(id),
  temperature DECIMAL(5,1) NOT NULL,
  humidity DECIMAL(5,1),
  is_alert BOOLEAN DEFAULT false,
  recorded_at TIMESTAMPTZ NOT NULL,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_temp_readings_device_time
  ON temperature_readings(device_id, recorded_at DESC);

-- Дані сканера ваги
CREATE TABLE weight_readings (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  device_id UUID NOT NULL REFERENCES iot_devices(id),
  zone_id UUID REFERENCES store_zones(id),
  weight_before DECIMAL(10,2),
  weight_after DECIMAL(10,2),
  delta_weight DECIMAL(10,2),
  processed BOOLEAN DEFAULT false,  -- чи вже оброблено в stock_events
  confidence INT,
  recorded_at TIMESTAMPTZ NOT NULL
);

-- Касові транзакції
CREATE TABLE pos_transactions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  cashier_id UUID REFERENCES users(id),
  shift_id UUID,           -- зміна касира
  receipt_number VARCHAR(50),
  fiscal_number VARCHAR(100),  -- фіскальний номер ДПС
  payment_type VARCHAR(20),    -- cash/card/mixed
  total_amount DECIMAL(12,2),
  tax_amount DECIMAL(12,2),
  status VARCHAR(20) DEFAULT 'completed',
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE pos_transaction_items (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  transaction_id UUID NOT NULL REFERENCES pos_transactions(id),
  product_id UUID NOT NULL REFERENCES products(id),
  product_stock_id UUID REFERENCES product_stock(id), -- яка партія
  quantity DECIMAL(10,2) NOT NULL,
  price_retail DECIMAL(12,2),
  discount_amount DECIMAL(12,2) DEFAULT 0,
  price_final DECIMAL(12,2),
  expiry_date DATE,        -- термін з чека (якщо CV розпізнав)
  cv_confidence INT        -- рівень впевненості CV
);

-- Касові зміни
CREATE TABLE pos_shifts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL,
  store_id UUID NOT NULL REFERENCES stores(id),
  cashier_id UUID REFERENCES users(id),
  shift_number INT,
  fiscal_shift_number VARCHAR(50),  -- номер зміни від ДПС
  opened_at TIMESTAMPTZ,
  closed_at TIMESTAMPTZ,
  opening_cash DECIMAL(12,2),
  closing_cash DECIMAL(12,2),
  total_sales DECIMAL(12,2),
  z_report_url TEXT
);
```

---

## 6. ФАЗИ РОЗРОБКИ v3.0

### Фаза 1 — IoT Infrastructure
```
[ ] MQTT broker (Mosquitto в Docker)
[ ] iot_devices CRUD + реєстрація
[ ] Обробник MQTT повідомлень → stock_events
[ ] Température monitoring + alerts
[ ] Web: дашборд IoT пристроїв
```

### Фаза 2 — Weight Sensors
```
[ ] weight_readings обробка
[ ] Конвертація delta_weight → кількість одиниць
[ ] Confidence логіка
[ ] Автоматичне FEFO списання від датчиків
[ ] Web: live view залишків від датчиків
```

### Фаза 3 — CV Cameras
```
[ ] Python CV мікросервіс (OpenCV + YOLOv8)
[ ] RTSP стрім з IP камер
[ ] Підрахунок товарів на полиці
[ ] OCR для читання термінів (Tesseract)
[ ] Інтеграція: CV результат → stock_events
```

### Фаза 4 — ПРРО Каса
```
[ ] Реєстрація в ДПС як виробник ПРРО
[ ] React Native каса (планшет)
[ ] Інтеграція з фіскальним сервером ДПС
[ ] Bluetooth принтер чеків
[ ] Термінал оплати SDK
[ ] Синхронізація з ShelfGuard real-time
[ ] Кожен продаж → stock_events → FEFO
[ ] Z-звіти, зміни
```
