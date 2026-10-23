# PHASE-1-07 — застосування матриці доступу

Дата: 23.10.2026. Дозвіл власника: «продовжи над Застосувати матрицю доступу».
Source IDs: **SEC-006, SEC-009 / F07 / F09, SEC-010**. Policy: `../ACCESS_MATRIX_PROPOSAL.md`.
Стан: **LOCAL FIX VERIFIED; NOT DEPLOYED**. Вибрані перевірки: **911 PASS** (786 backend + 25 PostgreSQL/HTTP + 36 web + 64 mobile). TypeScript обох клієнтів, web lint і EF model/snapshot — PASS. Staging/device/operational acceptance OPEN.

## Реалізовані правила

- Mandatory MFA: provider/provider_admin/provider_agent/enterprise_admin/network_manager/store_manager/supplier_admin. Для інших staff — за live privileged grants/templates з централізованого `AccessMatrixPolicy.PrivilegedPermissions`. Читання production/виклик AI самі по собі не роблять MFA обов'язковою.
- Після staff password без обов'язкової MFA — тільки proof з окремою audience `:mfa-enrollment`, строком 5 хвилин і поточною `AuthVersion`. Немає workspace access/refresh. Setup/enable приймають proof у POST body. Повний вхід — тільки після OTP; коди відновлення одноразові, зберігаються хешованими. Wrong OTP зараховується в lockout. Старий proof після зміни credential/MFA відхиляється.
- Refresh зберігає початковий `MfaVerifiedAt`; не подовжує step-up. Live bearer check перевіряє відповідність часу в підписаному JWT та сесії і поточну MFA policy. Старі mandatory/TOTP sessions без verified time мають увійти знову. Mandatory MFA не вимикається звичайним disable/recovery.
- Tracked user updates записують тільки змінені поля: login/MFA не відновлюють permissions, відкликані паралельним admin request.
- Step-up: 5 хвилин, TOTP або одноразовий recovery code. Code consumption, audit і оновлення поточної сесії — одна транзакція з concurrency guards. Перевірка перед impersonation, змінами staff/roles/grants/locations, integrations/keys, legal entities, приватними експортами, договірними/фінансовими командами та production start/complete/cancel. Не застосовується до звичайних GET, підтримки/чатів, reviews, previews, mark-seen, POS device/PIN.
- Web/mobile мають enrollment з QR/ручним ключем і збереженням recovery codes, запит step-up та один retry захищеної команди. Cancellation або зміна identity не повторює write. Staff без TOTP може налаштувати його перед критичною дією; після enrollment слід повторити дію. Enrollment proof не зберігається як access token. Прострочена impersonation freshness вимагає повернутися в provider і повторно почати delegation.
- Нові незалежні `production.view/manage/complete/cancel` та `ai_assistant.use` є в каталозі capability templates і bounded user-grants. Default: enterprise_admin/network_manager/store_manager. Direct provider/consumer відхиляються; санкціонований impersonation має target-tenant scope. UI підказки не заміняють серверну перевірку.
- Live tenant role, явні overrides, не прострочені/не відкликані grants і `user_locations` читаються з БД. Активний тимчасовий grant має чинний пріоритет над permanent false; `store_id` не визначає scope. Enterprise admin — tenant-wide, решта — assigned locations.
- Generic order PUT відхиляє `status`; planned-only notes. Явний start Planned → InProgress; complete InProgress → Done; cancel тільки непроведений order. Done immutable. Повтор complete повертає існуючу output batch; конкурентний програш повертає 409, транзакція відкидає всі stock/consumption/output writes. Рецепт не приймає дубльовані/непозитивні інгредієнти або foreign items; заміна ingredients за активних orders заборонена. FEFO expiry policy — окремий F08 фази 2.
- AI: use permission не відкриває інші домени. До query застосовуються наявні stock/sales/AI-orders/suppliers права та live tenant/location scope. Sales — агрегати quantity; PII, supplier contacts, batch identifiers, cost/margin, credentials не додаються. Provider errors узагальнено. Модель не отримує інструментів запису; тести використовують заглушку без зовнішніх AI calls.

## Міграція й приймання

`20261023165833_EnforceAccessMatrix` додає nullable `refresh_tokens.MfaVerifiedAt`; Designer/snapshot враховує optimistic concurrency для статусу order та stock quantity. Паролі та TOTP secrets не переписуються. Legacy verified time не вигадується з CreatedAt: користувач підтверджує MFA повторно.

Усі API instances, web і mobile мають перейти узгоджено. Перед rollout: HTTPS cookies, OTP enrollment/QR/recovery, низькопривілейований staff, role/grant removal, assigned locations, impersonation та нові production commands у staging. Old clients із PUT status треба оновити. Retention, негайне закриття consumer sockets, POS manager override, break-glass identity, SEC-019 та фінансові правила фази 2 залишаються окремими задачами.

Blind rollback на signature-only auth або довільний status PUT неприпустимий; при проблемах обмежити відповідні маршрути/модулі й зберегти чинні permission/MFA перевірки. Down migration тільки в контрольованому вікні після зупинки нового API; новий API без колонки не працює.

## Докази

Результати запусків і фактичні межі перевірки записуються в `../CHANGELOG_AUDIT.md`; звіти — `../test-results/phase-1-access-matrix*`. Тимчасовий PostgreSQL fixture — `../tests/Test-PasswordRecoveryPostgres.ps1 -Suite Matrix`.

### Межі локальної перевірки

Це вибрані suites, не весь репозиторій. Два POS database-тести пропущені у звичайному запуску та успішно виконані в 25-тестовому disposable suite. PostgreSQL/Kestrel перевірки використовують isolated schema, а не фактичні production RLS roles/ingress. Повний browser/device/multi-instance/load rollout не перевірявся. Нові live policies стосуються production/AI та MFA; інші наявні role-or-JWT-capability policies не переписані цим пакетом і зберігають свій існуючий строк поширення JWT claims. Production/staging DB, deployment і зовнішні AI/email services не використовувалися. Перший прогін старих POS fixtures підключався до локальної dev DB; це виправлено явним disposable-only guard, докладніше в CHANGELOG_AUDIT.md.
