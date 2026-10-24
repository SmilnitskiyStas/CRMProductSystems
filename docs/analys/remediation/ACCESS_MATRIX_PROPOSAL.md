# Пропозиція матриці доступу — фаза 1

Дата: 23.10.2026. **Власник дозволив застосування матриці командою «продовжи над Застосувати матрицю доступу».**
Початкові таблиці нижче є прийнятою основою локальної реалізації PHASE-1-07. Фактична реалізація та межі — [TASKS/PHASE-1-07-ACCESS-MATRIX.md](TASKS/PHASE-1-07-ACCESS-MATRIX.md). Deployment потребує окремого operational scope.

## Уточнення власника — 24.10.2026

Власник явно підтвердив: «Так, для всіх ролей без налаштованої MFA». Початковий enrollment gate нижче скасовано для звичайного входу, refresh та доступу до workspace, включно адміністраторами й менеджерами. Користувач отримує неблокувальне нагадування з переходом у налаштування. Якщо MFA вже підключена, OTP при вході та verified session залишаються обов’язковими. MFA для критичних операцій збережено. Наступним окремим дозволом PHASE-1-12 власник дозволив усім ролям вимикати власну вже підключену MFA після перевірки пароля й невикористаного другого фактора. Деталі й статус: [PHASE-1-10](TASKS/PHASE-1-10-OPTIONAL-MFA-LOGIN.md).

## Реалізація початкової матриці (до уточнення PHASE-1-10)

- Role/capability catalog: `TenantRoleCapabilities`; mandatory MFA та sensitive grants: `AccessMatrixPolicy`.
- `WorkAccessContext` читає live role/overrides/templates/grants/locations; production та AI policies не покладаються на старі JWT capabilities.
- Production використовує явні start/complete/cancel; generic PUT status заборонено. AI фільтрує домени до query та не передає supplier phone/cost.
- MFA enrollment proof окремий від bearer audience; повний workspace login тільки після OTP. Mandatory/TOTP legacy sessions без verified time потребують повторного входу. Step-up — 5 хвилин, refresh не оновлює цей час.
- Home `store_id` не замінює live `user_locations`. Network/store managers обмежені assigned locations; enterprise admin має tenant-wide scope.

## 1. Початкова вимога MFA (звичайний вхід змінено в PHASE-1-10)

Прийнятий початковий policy — захист усіх управлінських ролей без примусової MFA для звичайного consumer/POS PIN входу.

| Роль / повноваження | MFA для staff/workspace | Step-up перед критичною дією | Примітка |
|---|---|---|---|
| provider, provider_admin | Обов'язкова | Impersonation, права/ключі/експорти | Provider не отримує автоматичний доступ до чужого tenant |
| provider_agent | Обов'язкова | Лише дозволені support/admin actions | MFA не додає відсутні provider permissions |
| enterprise_admin | Обов'язкова | Права, приватні експорти, секрети, критичні фінансові операції | Власний tenant |
| network_manager, store_manager | Обов'язкова | Відповідальні фінансові дії та grants у своєму scope | Лише призначені магазини |
| supplier_admin | Обов'язкова | Підписи/печатки, договірні дії, staff/permission зміни | Лише власний supplier tenant |
| merchandiser, storekeeper, cashier, staff | Добровільна для звичайних staff-функцій | Обов'язкова при окремо наданих привілейованих grants | Список таких grants потрібно зберігати централізовано |
| consumer | Поточний personal-вхід | Окреме підтвердження bonus redemption погоджується у фазі 2 | Consumer proof ніколи не дає staff-сесію |
| POS device + cashier PIN | Окремий існуючий device/PIN flow | Manager override для критичних касових операцій — окрема policy | Не вимагати мобільний TOTP при кожному скануванні/чеку |

Поведінка без enrollment: після правильного staff password дозволити тільки коротку enrollment-сесію для setup/verify MFA; не видавати повний workspace/refresh token. Застосувати до login, refresh, linked/SSO grants і privileged session upgrade. Потрібен узгоджений rollout, щоб не заблокувати поточних адміністраторів.

Recovery: одноразові hashed recovery codes; відновлення не вимикає MFA автоматично. Break-glass — окрема контрольована identity, без постійного публічного bypass, з owner approval, audit і сповіщенням. Step-up 5 хвилин реалізовано; rollout чинних сесій погоджується перед deployment.

## 2. Виробництво

Capability keys додані до каталогу ролей і bounded per-user grants. Для кожного запиту потрібні authentication, активний production module, tenant і live location scope.

| Дія | Default roles у своєму tenant/scope | Окремий grant для інших staff | Серверне обмеження |
|---|---|---|---|
| Читати рецепти/замовлення | enterprise_admin, network_manager, store_manager | production.view | Рецепти tenant-owned; orders тільки дозволених location |
| Створювати/редагувати рецепти | Ті самі управлінські ролі | production.manage | Інгредієнти/output item належать тому самому tenant |
| Створювати/редагувати planned order | Ті самі управлінські ролі | production.manage | Generic PUT редагує лише дозволені поля, не status |
| Почати order | Ті самі управлінські ролі | production.complete | Явна команда start, тільки Planned → InProgress |
| Завершити order / провести випуск | Ті самі управлінські ролі | production.complete | Явна complete, atomic/idempotent stock consumption + output |
| Скасувати невиконаний order | Ті самі управлінські ролі | production.cancel | Явна cancel; Done не повертається в Planned/Cancelled |
| Виправити вже проведений order | Окреме погодження відповідального | Не дозволяти generic update | Компенсуюча операція з reason/approver; не стирання історії |

Cashier/merchandiser/storekeeper/staff без явного production grant не отримують write доступ. Provider — лише через санкціонований tenant-scoped impersonation. Наявність production.complete не відкриває production.manage; frontend visibility не замінює серверні перевірки.

Початкове правило не вимагає двох різних працівників для створення та підтвердження випуску. Maker/checker можна додати окремим рішенням.

## 3. AI-помічник: право виклику і право на дані

Пропозиція: `ai_assistant.use` як окремий новий grant; менеджери отримують його за default policy, інші staff — явно. Consumer AI лишається окремим personal endpoint. AI не виконує автоматичні записи/фінансові операції.

| Частина контексту | Необхідне право на дані ДО побудови prompt | Дозволені поля | Заборонено за замовчуванням |
|---|---|---|---|
| Залишки / expiry | CanViewStock і live location scope | Назва товару, кількість, статус, expiry | Приховані склади/магазини, закупівельна ціна без окремого права |
| Продажі | AnalyticsViewOrCapability і той самий location scope | Агреговані quantity/revenue за погоджений період | Персональні чеки, покупці, телефони, loyalty history |
| Pending AI orders | AiOrdersViewOrCapability | Дозволені suggestions, store, date, item count | Suggestions інших location/tenant |
| Постачальники | SuppliersViewOrCapability | Назва та delivery days | Телефон/email/банківські/договірні реквізити, підписи/печатки |
| Маржа / cost | AnalyticsViewMargin або еквівалентне погоджене право | Лише погоджені агрегати | Автоматичне включення закупівельних цін для всіх менеджерів |
| Staff/customer/secret data | Не включати в початкову версію | — | Паролі, TOTP, ключі, JWT, ПІБ/контакти, платіжні реквізити |

Правило перетину: `ai_assistant.use` дозволяє виклик, але не відкриває жодного додаткового домену. Якщо caller не має analytics.view, sales context не читається з БД і не передається моделі; prompt guardrail не є механізмом авторизації. Supplier phone прибрати з payload незалежно від формулювання запиту.

Однакові tenant/location/permission filters мають діяти для direct API і AI. Відсутні права не обходяться через інший prompt, history, cache або worker identity. Потрібен model stub test, який перевіряє payload перед external call, без платних викликів.

## Подальше приймання

Власник дозволив реалізацію mandatory MFA, незалежних production permissions та обмежень AI-контексту. У межах цього пакета не створюється новий consumer/staff linking або SSO grant, break-glass identity чи production deployment. Перед розгортанням потрібні узгоджені API/web/mobile artifacts, rollout window і staging/device перевірки. Фактичні результати див. CHANGELOG_AUDIT.md.

24.10.2026 — PHASE-1-12: власник окремо дозволив усім ролям, включно адміністраторами й менеджерами, вимикати власну вже підключену MFA після перевірки пароля та невикористаного TOTP/коду відновлення. Це уточнення скасовує попередню рольову заборону вимкнення. Anti-replay, lockout, інвалідація старих сесій та MFA для критичних дій збережено. Статус і перевірки: TASKS/PHASE-1-12-SELF-SERVICE-MFA-DISABLE.md.
