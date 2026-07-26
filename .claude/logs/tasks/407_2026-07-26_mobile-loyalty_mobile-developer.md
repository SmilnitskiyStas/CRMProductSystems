# TASK-407: Mobile loyalty wallet + POS loyalty scan (Фаза 0)

**Agent:** mobile-developer
**Date:** 2026-07-26
**Status:** done
**Depends:** TASK-405 (backend Application+Api layer) · **Parallel with:** frontend-developer
(TASK-406, `SaleDetailDrawer`/`SalesTable` loyalty section + `marketing-analytics`)

## Контекст

Task #3 (mobile half) з послідовності агентів плану `C:\Users\stass\.claude\plans\
deep-cooking-nygaard.md` §"Фаза 0". Backend (TASK-405) вже готовий — читав контролери
напряму (`ConsumerAuthController`, `ConsumerLoyaltyController`, `LoyaltyController`,
`LoyaltySettingsController`, DTOs, `PosService.CreateSaleAsync`), не покладався на переказ
з плану. Одне суттєве розходження план↔факт, зафіксоване нижче.

## Зроблено

**Consumer auth** (`mobile/features/auth/`) — `api/consumerAuthApi.ts` (register/login проти
`/api/consumer-auth/*`), `hooks/useConsumerAuth.ts`. `types.ts` отримав `ConsumerUser`
(`id/fullName/phone/role:'consumer'`) поруч із наявним `AuthUser`, без зміни останнього.
`store.ts` — адитивно: новий `sessionKind: 'staff'|'consumer'|null` + `consumerUser` +
`setConsumerAuth`/`setConsumerUser`, existing `setAuth`/`setUser`/`clearAuth`/`loadToken`
зберегли сигнатури один-в-один (усі наявні call-сайти компілюються без змін).
`loadToken`/`clearAuth` тепер читають/пишуть 2 нових SecureStore-ключі
(`session_kind`, `consumer_user` — JSON-знімок профілю, бо `ConsumerAuthController` не має
"me"-ендпоінта). Сесії, збережені до цього тавска, мають `session_kind=null` →
трактуються як `'staff'` (міграційна сумісність).

**Auth-флоу вибору ролі** (`mobile/app/(auth)/`) — новий `select-role.tsx` ("Я покупець" /
"Я співробітник"), `consumer-login.tsx`, `consumer-register.tsx` (react-hook-form + zod,
пароль-політика дзеркалить `PasswordValidator` — 12 симв./літера/цифра клієнтською
перевіркою, common-password-list лишається server-only, показується raw 400 body).
Автоматичні guard-редіректи (`(app)/_layout.tsx`, `(consumer)/_layout.tsx`) тепер ведуть на
`/(auth)/select-role` замість `/(auth)/login`; явний logout-кнопки лишили свій old
fast-path (staff → `/(auth)/login`, consumer → `/(auth)/consumer-login`) — по
дизайну, не по забудькуватості.

**Кореневе розгалуження** (`mobile/app/_layout.tsx`) — новий `Stack.Screen name="(consumer)"`
поруч з `(auth)`/`(app)`. Bootstrap-ефект після `loadToken()`: staff-гілка викликає `getMe()`
як і раніше; consumer-гілка НЕ викликає нічого мережевого (немає еквівалента) — форс-логаут
лише якщо знімок `consumerUser` порожній/пошкоджений.

**Consumer-навігація** (`mobile/app/(consumer)/`, новий route group, БЕЗ власного
`index.tsx` — уникнув колізії з `(app)/index.tsx`, обидві групи не мають префіксу шляху):
`_layout.tsx` (Tabs: `wallet`/`history`/`account`, guard дзеркалить `(app)/_layout.tsx`),
`wallet.tsx` (мультитенантний селектор — `features/loyalty/components/MembershipSelector.tsx`
— живий QR через `react-native-qrcode-svg`, код текстом під QR за вимогою бекенд-логу
"manual code entry має бачити повний рядок", баланс), `history.tsx` (paginated ledger),
`account.tsx` (профіль/logout). Polling `GET /{tenantId}/code` кожні 22с **тільки** коли
`useFocusEffect` (навігаційний фокус табу) І `AppState==='active'` одночасно true —
свідомо строгіше за наявний `useCurrentShift`-патерн (той лише робить `refetch()` при фокусі,
не зупиняє сам interval) — тут secutity-чутливий ротаційний секрет, тому
`refetchInterval: enabled ? 22000 : false` реально зупиняється.

**POS — сканування лояльності** (`mobile/app/(app)/pos/loyalty.tsx`, новий екран, вставлений
МІЖ `scanner.tsx` і `payment.tsx` — `scanner.tsx`'s "Перейти до оплати" тепер веде сюди,
1-рядкова зміна pathname, решта файлу не чіпав) — 3 режими (QR-скан / код вручну / пошук
клієнта), використовує новий `features/pos/components/BarcodeCameraView.tsx` (мінімальний
camera+permission шматок, а НЕ рефактор `scanner.tsx` — той навмисно залишений незайманим,
бо вже пройшов аудит TASK-366; копіювання ~60 рядків дешевше за ризик регресії робочого
файлу). Ручний пошук перевикористовує `features/customers/hooks/useCustomers` +
`components/CustomerCard` один-в-один. Результат (membership+redeem або голий customerId)
летить у `payment.tsx` через router params.

**payment.tsx / receipt.tsx** — прийняли нові опційні params
(`customerId/membershipId/redeemAmount/customerName/maskedPhone`), додано в `SaleRequest`.
**Знайдена й виправлена реальна проблема, не описана в брифі буквально**: backend
(`PosService.CreateSaleAsync`) віднімає `redeemAmount` від `TotalAmount` ДО податку/решти —
тобто сума, яку клієнт реально винен, менша за сирий subtotal кошика. `payment.tsx` тепер
рахує `netTotal = subtotal - redeemAmount` і саме його використовує для перевірки
достатності готівки/решти/суми на card-оплаті — без цього касир вимагав би з клієнта більше
готівки, ніж треба після списання бонусів. При `redeemAmount=0` (звичайний продаж) `netTotal
=== subtotal` — нуль видимих змін у стандартному флоу. Додано рядок "Списано бонусів" +
"До сплати" в чек-картці, і невеликий зелений блок "клієнт приєднаний". `receipt.tsx` показує
`loyaltyAccrued/loyaltyRedeemed/loyaltyBalance`, якщо backend їх повернув (нові опційні поля
в `SaleResponse`/`SaleDto`).

**Персонал у власній програмі** (`mobile/app/(app)/profile/index.tsx`) — новий блок
"Бонусна програма" (`LoyaltySection`, локальний компонент за прецедентом наявного `MenuItem`):
`GET /api/loyalty/my-membership` (404→показує кнопку "Приєднатися", 403→ховає секцію
повністю — модуль вимкнено в тенанта), кнопка викликає `POST /api/loyalty/join-as-staff`.

**Ролі** — `mobile/lib/roles.ts` вже мав голий `AppRoles.Consumer='consumer'` від
backend-агента (TASK-405), не чіпав. Розгалуження staff/consumer у `_layout.tsx`/
`(app)/(consumer)` guard'ах керується `sessionKind` (не role-set) — `ConsumerUser.role`
все одно несе літерал `'consumer'` для коду, якому потрібна явна `role==='consumer'`
перевірка, а не похідна від sessionKind.

**Нова залежність:** `react-native-qrcode-svg@6.3.21` + peer `react-native-svg@15.15.4`
(SDK-56-сумісні версії, встановлено через `npx expo install`, без нативного лінкування/
app.json plugin — `react-native-svg` не потребує expo config plugin). Сканування
(`expo-camera`, `barcodeTypes` вже включає `'qr'`) — без нових залежностей, підтверджено.

## Розходження план↔факт (задокументовано, як просив бриф)

- Ручний ввід коду: план писав "6-значний код вручну", але
  `POST /api/loyalty/resolve-code` (перевірено в `LoyaltyService`/backend-логу TASK-405)
  очікує ПОВНИЙ рядок `SGLOY1.{membershipId}.{code}` і зі сканера, і з ручного вводу.
  UI (`loyalty.tsx`) відповідно просить повний рядок, не 6 цифр.
- `ResolveLoyaltyCodeResult` не повертає ліміт списання (лише `Balance`) — ліміт (`%` від
  чека) живе в `LoyaltyProgramSettingsDto`, а `GET/PUT /api/settings/loyalty` гейтовано
  `AtLeastEnterpriseAdmin` — каса (звичайний cashier) не може його прочитати. Тому
  `loyalty.tsx` дозволяє вводити суму аж до `min(balance, subtotal)` як клієнтську
  підказку, а реальний ліміт (`RedemptionCapPercent`/`MinRedemptionBalance`) перевіряється
  атомарно на сервері при `POST /pos/sales` — перевищення повертає 400 з готовим текстом
  "Redeem amount exceeds the redemption cap (X% of the sale)", який вже коректно спливає
  через наявний `onError` у `payment.tsx` (той показує `errData.error` як є) — без змін.
- Немає backend-ендпоінта "список тенантів з увімкненою лояльністю" — консюмер не може
  "browse" програми. `wallet.tsx` дає мінімальний ручний "Приєднатися за кодом магазину"
  (текстове поле з Tenant ID → `POST /{tenantId}/join`) як прохідний, але не гарний UX —
  без цього фіча була б непроходимою end-to-end (реєстрація → порожній гаманець назавжди).
  Позначено нижче як follow-up.

## Верифікація

- `npx tsc --noEmit` — чисто (0 помилок) на всьому проєкті (весь `mobile/`, не лише нові
  файли) — прогнано ПІСЛЯ всіх правок, включно з payment/receipt/profile/loyalty.tsx.
- `npm run lint` — падає (відсутній `eslint.config.js`) — **не мій регрес**, той самий
  пре-екзистуючий стан, задокументований TASK-366 ("npm run lint fails on missing
  eslint.config.js, pre-existing, not fixed").
- Тестового раннера в mobile немає (`package.json` без `"test"` скрипта, нуль
  `*.test.ts(x)` файлів поза `node_modules`) — нічого запускати.
- Живої верифікації на emulator/device не було (той самий пре-екзистуючий брак середовища,
  що і в TASK-366) — верифікація на рівні контракту: маршрути/поля запиту-відповіді звірені
  прямим читанням контролерів/DTO (не з переказу плану), наскрізний потік параметрів
  (scanner→loyalty→payment→receipt) прослідкований вручну поле-в-поле, включно з
  netTotal-математикою.
- `npx expo install react-native-svg react-native-qrcode-svg` пройшов чисто (мережа
  доступна в цьому середовищі), 34 нових пакети, версії SDK-56-сумісні.

## Свідомі рішення (без user sign-off, за судженням)

- `(auth)` group отримав новий entry-screen `select-role.tsx` замість вибору просто в
  наявному `login.tsx` — окремі форми, як явно вимагав бриф ("не змішуй форми").
- `(consumer)` group без власного `index.tsx` (навмисно) — обидва route groups не несуть
  префіксу шляху, тож `index.tsx` в обох одночасно колізіонував би на `/`; усі 3 екрани
  групи отримали явні імена (`wallet`/`history`/`account`), той самий патерн, що вже
  використовує `(auth)` (лише `login.tsx`, без `index.tsx`) — не новий прецедент, а
  продовження наявного.
- `scanner.tsx` навмисно НЕ рефакторено на спільний компонент — нова `BarcodeCameraView`
  використовується лише в новому `loyalty.tsx`. Ризик регресії аудитованого файлу
  переважив DRY.
- `useManualAdjustLoyalty` (обгортка над `POST /api/loyalty/manual-adjust`) написана в
  `features/loyalty/hooks/useLoyalty.ts` для повноти API-шару, але БЕЗ UI-екрана — не було
  в 5 пунктах завдання. Кандидат на майбутній екран (напр. кнопка в майбутньому
  customer-detail/admin view).
- Ручний "приєднатися за кодом магазину" в `wallet.tsx` — мінімальний stopgap (raw Tenant ID
  текстом), не гарний UX. Потребує продуктового рішення: QR на кассі для консюмерів,
  чи staff-ініційоване запрошення.

## Не в скоупі / follow-up для наступних агентів

- **security-reviewer:** consumer JWT lifetime/revocation (вже флаговано TASK-405), тепер
  додатково — мобільний `clearAuth()` не інвалідує токен на сервері (немає такого
  ендпоінта), тільки локальний drop; `wallet.tsx`'s manual tenant-join приймає будь-який
  синтаксично валідний GUID без rate-limit на мобільній стороні (backend-side
  rate-limit/auth на `/join` не перевіряв — поза скоупом цього тавска).
- **Продуктове рішення:** гарний UX для "consumer приєднується до нової програми" (замість
  ручного Tenant ID) — див. розходження план↔факт вище.
- **qa-tester:** end-to-end сценарій "реєстрація consumer → join → живий QR → скан на POS
  → продаж з нарахуванням/списанням → баланс на чеку" не пройдено живцем (немає
  emulator/device в цьому середовищі) — контрактна перевірка лише.
- `frontend-developer` (TASK-406, паралельно) — не чіпав нічого у `frontend/`.
