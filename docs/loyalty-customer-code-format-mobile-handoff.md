# Handoff: формат картки покупця (QR / Code 128) — на рівні мережі

## Мета

Мережа магазинів (`Tenant`) обирає **один** формат відображення універсального
коду покупця для **всіх** своїх магазинів — QR або Code 128. Немає
налаштувань per-store. Мобільний застосунок ніколи не показує обидва
формати одночасно — тільки той, який визначив backend.

## Статус

Backend (TASK-499) і адмін-панель налаштувань лояльності на вебі (TASK-500)
реалізовані, повністю протестовані і задеплоєні на прод:

- `dotnet build` — 0 помилок
- Повний backend test suite — **1375/1375**, без регресій
- Docker-образи backend і frontend зібрані локально перед пушем (deploy.sh
  не має health-gate/rollback, тож це обов'язкова перевірка в цьому проєкті)
- Коміти: `4fa15f7d` (backend), `72bd9438` (web), запушені в `main`,
  автодеплой через CI/CD

Перевірити наживо: `GET https://api.agrusystems.pp.ua:10054/api/consumer/loyalty/code`

## Що вже готово на backend

### Налаштування формату — на рівні tenant

`LoyaltyProgramSettings.CustomerCodeFormat` — рядок, `"qr"` або `"barcode"`,
дефолт `"barcode"`. Редагується власником мережі на новій сторінці вебкабінету
`/consumer-app` (enterprise_admin), через існуючий
`GET/PUT /api/settings/loyalty` — до цього поля мобільний застосунок жодного
відношення не має, воно тут лише для контексту.

### Універсальний код — розширена відповідь

```http
GET /api/consumer/loyalty/code
GET /api/consumer/loyalty/code?tenantId={tenantId}
```

Успішна відповідь (200):

```json
{
  "code": "SGCUS1.{consumerAccountId}.{totp}",
  "displayFormat": "qr",
  "balance": 0,
  "expiresInSeconds": 30
}
```

`displayFormat` — це єдине нове поле. `code`/`balance`/`expiresInSeconds` не
змінились: **payload коду не залежить від формату** — це той самий рядок,
просто різна візуалізація (QR-компонент або `Code128Barcode`).

`balance` як і раніше завжди `0` в цій відповіді (це існуюче спрощення,
не пов'язане з цією задачею — не намагайтесь інтерпретувати його як реальний
баланс).

### Логіка визначення формату

**Без `tenantId`:**

| Кількість memberships | Результат |
|---|---|
| 0 | `displayFormat: "barcode"` (системний дефолт — мережа ще невідома) |
| 1 | формат саме цієї мережі |
| 2+ | **409**, `{ "error": "network_selection_required" }` |

**З `tenantId`:** backend перевіряє, що consumer дійсно має membership у цій
мережі.
- Є membership → повертає формат цієї конкретної мережі (навіть якщо у
  consumer є інші memberships — явний `tenantId` знімає неоднозначність).
- Немає membership → **403**, `{ "error": "You are not a member of this network." }`

Інші помилки без змін: **404** `{ "error": "Consumer account not found." }`
якщо акаунт неактивний/не знайдений.

### Що НЕ змінилось

- Старі `SGLOY1.`-коди (membership-scoped, видані до попереднього релізу)
  досі приймаються касою без змін — це окремий, незалежний шлях у
  `ResolveCodeAsync`, ця задача його не торкалась.
- Авто-створення membership при першому скані на POS (TASK-498) — без змін.
- `POST /api/loyalty/resolve-or-create-by-phone` (TASK-498) — без змін.

## Що потрібно зробити в mobile

### 1. Тип `LoyaltyCode`

```ts
interface LoyaltyCode {
  code: string;
  displayFormat: 'qr' | 'barcode';
  balance: number;
  expiresInSeconds: number;
}
```

### 2. `wallet.tsx` — рендерити лише один компонент

```
displayFormat === 'qr'      → react-native-qrcode-svg
displayFormat === 'barcode' → Code128Barcode (mobile/features/loyalty/components/Code128Barcode.tsx)
```

Без заголовків/блоків обох форматів одночасно — тільки той, що прийшов у
відповіді.

### 3. Вибір мережі (лише коли реально потрібен)

- **0 memberships** — не показувати жоден пікер мереж. Запит без `tenantId`,
  backend сам поверне системний `barcode`.
- **1 membership** — автоматичний вибір, запит без `tenantId` (backend і так
  поверне формат єдиної мережі; явно передавати `tenantId` не обов'язково,
  але можна для консистентності з `useLoyaltyUiStore.selectedTenantId`, якщо
  так простіше по коду).
- **2+ memberships**:
  - Перший запит — без `tenantId`.
  - Якщо відповідь **409 `network_selection_required`** — показати вибір
    мережі (дані про мережі вже є через `GET /consumer/loyalty/memberships`,
    новий виклик для цього не потрібен).
  - Після вибору — повторити запит із `?tenantId={обраний}`.
  - Обрану мережу зберігати в `useLoyaltyUiStore.selectedTenantId` (він уже
    існує й використовується для балансу/історії — синхронізувати вибір коду
    з тим самим стором, не заводити окремий).
  - При зміні мережі — негайно перезапитувати код (той самий debounce/retry
    патерн, що вже є).

### 4. Поведінка кнопки повторного запиту — без змін

Індикатор завантаження під час запиту, блокування повторних натискань,
зрозуміле повідомлення про помилку — включно з новими кейсами (403 "ви не є
учасником цієї мережі" не мало б взагалі траплятись у нормальному флоу,
оскільки мобільний клієнт сам вибирає `tenantId` тільки зі списку своїх
memberships — якщо все ж прийде, показати як звичайну помилку завантаження).

## Тестові сценарії (mobile)

- Рендериться лише QR (мережа з `customerCodeFormat: "qr"`).
- Рендериться лише Code 128 (мережа з `"barcode"` або 0 memberships).
- Автоматичний вибір при рівно одній мережі — без пікера.
- 2+ мережі → 409 → показ пікера → вибір → новий код із правильним форматом.
- Зміна мережі в пікері одразу оновлює і формат, і сам код.
- Немає падіння на Android Fabric (вже перевірено на попередній версії
  `Code128Barcode`, лишається актуальним).
- `npx tsc --noEmit` чистий.

## Файли (орієнтовно)

- `mobile/features/loyalty/types.ts` — `LoyaltyCode.displayFormat`
- `mobile/features/loyalty/api/loyaltyApi.ts` — `getLoyaltyCode(tenantId?: string)`,
  обробка 409/403
- `mobile/features/loyalty/hooks/useLoyalty.ts` — логіка вибору мережі /
  реакція на `network_selection_required`
- `mobile/features/loyalty/store.ts` — переконатись, що `selectedTenantId`
  використовується консистентно для коду, балансу й історії
- `mobile/app/(personal)/wallet.tsx` — умовний рендер QR/Code128, UI пікера
  мережі
- `mobile/features/loyalty/components/MembershipSelector.tsx` — можливо
  перевикористати як пікер мережі для 409-кейсу (він уже вміє показувати
  список memberships і викликати `onSelect(tenantId)`)

Після реалізації й перевірки — потрібна нова mobile-збірка, оскільки
попередня версія розрахована на відповідь без `displayFormat`.
