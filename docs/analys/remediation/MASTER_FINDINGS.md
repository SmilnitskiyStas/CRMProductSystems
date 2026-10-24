# MASTER_FINDINGS

Дата: 23.10.2026, Europe/Kyiv. Baseline HEAD: `3563ce6c79596ae010f4baebc920d0685f073876`, dirty checkout.
Дозвіл власника: поетапна реалізація плану та «продовжуй роботу над сесіями». Фаза 0 і пакети 1–3 фази 1 локально перевірені. Повний статус — CHANGELOG_AUDIT.md.

Вихідний статус — оцінка статичного аудиту 22.10.2026, не повторна перевірка актуального коду.
`NOT REVALIDATED` означає, що поточний статус ще не встановлено; не прирівнювати до NOT REPRODUCED.
SEC-001 підтверджено перед зміною; локальне виправлення не означає deployment або приймання production.
Власники/PR ще не призначені. Коміти, PR і deployment не створювалися. Recovery/session migrations підготовлені й перевірені лише на disposable DB; робочі DB не змінювалися.

| ID | Severity | Finding | Audit status | Current status | Phase | Root-cause group / dependencies |
|---|---|---|---|---|---|---|
| F01 | P1 | Offline-чек перераховується за поточним каталогом без незмінного знімка ціни | VERIFIED | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| F02 | P1 | Час здійснення offline-продажу не зберігається у центральному факті | VERIFIED | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| F03 | P1 | Погашення бонусів зменшує заголовок чека, але не суми його позицій | VERIFIED | NOT REVALIDATED | 2 | SEC-016 |
| F04 | P1 | TaxAmount жорстко обчислюється як ПДВ 20% для всього чека | VERIFIED | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| F05 | P1 | Помилка фіскалізації виключає вже проведений продаж з аналітики | VERIFIED | NOT REVALIDATED | 2 | SEC-016 |
| F06 | P1 | Фото полиць і фонові плани зберігаються у публічному статичному шляху | VERIFIED | CONFIRMED; LOCAL FIX VERIFIED; private files; NOT DEPLOYED | 1 | SEC-002 |
| F07 | P1 | Загальний PUT виробничого замовлення обходить процес завершення | VERIFIED | LOCAL FIX VERIFIED; PHASE-1-07; NOT DEPLOYED; staging OPEN | 1 | SEC-009 |
| F08 | P1 | Виробництво може споживати прострочені партії сировини | VERIFIED | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| F09 | P1 | Запис у виробництво захищений лише входом та активацією модуля | VERIFIED | LOCAL FIX VERIFIED; PHASE-1-07; NOT DEPLOYED; staging OPEN | 1 | SEC-009 |
| F10 | P1 | Повтор одного SKU у різних рядках може розійтися зі списанням залишку | VERIFIED | NOT REVALIDATED | 2 | SEC-016 |
| F11 | P2 | Лічильники зміни та клієнта мають ризик втрати паралельних оновлень | PARTIAL | NOT REVALIDATED | 4 | Окрема причина; деталізувати при revalidation |
| F12 | P1 | SQLite outbox не гарантує збереження останнього чека при втраті живлення | VERIFIED | NOT REVALIDATED | 2 | SEC-015 |
| F13 | P2 | Переглянуті FK не примушують рівність tenant дочірнього та батьківського рядка | VERIFIED | NOT REVALIDATED | 3 | SEC-021 |
| F14 | P1 | Щоденний AI scheduler обходить усі tenant, але викликає API одним staff JWT | PARTIAL | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| F15 | P2 | Нічна агрегація daily_sales не прибирає зниклі групи та зберігає ручну перевагу без reconciliation | VERIFIED | NOT REVALIDATED | 4 | Окрема причина; деталізувати при revalidation |
| F16 | P2 | Маржа використовує поточну закупівельну ціну, а не історичну собівартість продажу | VERIFIED | NOT REVALIDATED | 4 | Окрема причина; деталізувати при revalidation |
| F17 | P1 | У переглянутому POS-контурі не знайдений повний продаж-повернення-сторно бонусів | NOT FOUND | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| F18 | P2 | Масштабування аналітики обмежене матеріалізацією детальних фактів у API | PARTIAL | NOT REVALIDATED | 4 | SEC-011 |
| F19 | P2 | CI не запускає наявні POS/UI/mobile тести як повний deployment gate | VERIFIED | NOT REVALIDATED | 4 | SEC-025 |
| F20 | P1 | Перевірений backup script дає лише локальний dump без доказу відновлюваності | PARTIAL | NOT REVALIDATED | 3 | SEC-027 |
| F21 | P2 | Незалежне масштабування є за API-ролями, але tenant DB routing не знайдено | PARTIAL | NOT REVALIDATED | 5 | Окрема причина; деталізувати при revalidation |
| F22 | P2 | POS retry не має jitter і переводить конкурентний 409 у rejected | VERIFIED | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| F23 | P2 | Загальний транзакційний sales outbox/inbox та версійний event contract не знайдені | NOT FOUND | NOT REVALIDATED | 5 | Окрема причина; деталізувати при revalidation |
| F24 | P2 | Контур планограм і AI-фото поки не підтверджений як версійний offline workflow | PARTIAL | NOT REVALIDATED | 5 | Окрема причина; деталізувати при revalidation |
| SEC-001 | P0 | Неперевірені consumer контакти можуть перетворюватися на staff/workspace identity | VERIFIED | CONFIRMED before fix; LOCAL FIX VERIFIED (438 auth tests); NOT DEPLOYED | 0 | Consumer contact used as staff proof; session/linking decisions OPEN |
| SEC-002 | P1 | Внутрішні договори, підписи, печатки та фото лежать у static wwwroot | VERIFIED | CONFIRMED; LOCAL FIX VERIFIED; private files; NOT DEPLOYED | 1 | F06 |
| SEC-003 | P1 | Gateway access log може записувати SignalR bearer token у query string | VERIFIED | CONFIRMED; LOCAL FIX VERIFIED; isolated nginx privacy PASS; NOT DEPLOYED | 1 | Окрема причина; деталізувати при revalidation |
| SEC-004 | P1 | Публічний forgot-password змінює пароль і відкликає refresh до підтвердження користувачем | VERIFIED | CONFIRMED; LOCAL FIX VERIFIED; NOT DEPLOYED; ST-05 staging OPEN | 1 | PHASE-1-05: proof-based reset; pending outbox secrecy окремо SEC-019 |
| SEC-005 | P1 | Consumer access token має довгий строк без серверного session revocation | VERIFIED | CONFIRMED; LOCAL FIX VERIFIED; NOT DEPLOYED; ST-06 staging/SLA OPEN | 1 | PHASE-1-06: short access, independent refresh, live HTTP revoke; active sockets bounded by expiry |
| SEC-006 | P1 | MFA залишається добровільним навіть для привілейованих ролей | VERIFIED | LOCAL FIX VERIFIED; PHASE-1-07; NOT DEPLOYED; staging/device acceptance OPEN | 1 | Окрема причина; деталізувати при revalidation |
| SEC-007 | P2 | Refresh rotation не має доведеного атомарного single-use захисту при паралельних запитах | PARTIAL | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-008 | P2 | Web bearer token зберігається у localStorage, а CSP web не ввімкнено в source nginx | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-009 | P1 | Production write API має лише authentication/module gate і дозволяє довільну зміну статусу | VERIFIED | LOCAL FIX VERIFIED; PHASE-1-07; NOT DEPLOYED; staging/device acceptance OPEN | 1 | F07, F09 |
| SEC-010 | P1 | Staff AI assistant не перевіряє право на дані, які збирає в контекст | VERIFIED | LOCAL FIX VERIFIED; PHASE-1-07; NOT DEPLOYED; staging/device acceptance OPEN | 1 | Окрема причина; деталізувати при revalidation |
| SEC-011 | P2 | Paid staff AI та важкі analytics endpoints не мають повного tenant quota/concurrency budget | PARTIAL | NOT REVALIDATED | 3 | F18 |
| SEC-012 | P2 | Identity-based rate-limit policies виконуються раніше за authentication middleware | VERIFIED | CONFIRMED; LOCAL PATCH/build verified; ST-13 OPEN; NOT DEPLOYED | 1 | Окрема причина; деталізувати при revalidation |
| SEC-013 | P2 | Forwarded headers довіряються без списку proxies, gateway limiter бере перший XFF | PARTIAL | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-014 | P1 | POS дозволяє HTTP для віддаленого server URL | VERIFIED | CONFIRMED; LOCAL FIX VERIFIED; POS transport; NOT DEPLOYED | 1 | Окрема причина; деталізувати при revalidation |
| SEC-015 | P2 | SQLite receipts/outbox не мають app encryption/integrity protection, NORMAL не є power-loss durability | VERIFIED | NOT REVALIDATED | 2 | F12 |
| SEC-016 | P1 | POS business invariants допускають розрив receipt, stock та bonus accounting | VERIFIED | NOT REVALIDATED | 2 | F03, F05, F10 |
| SEC-017 | P2 | Немає доведеного customer authorization, прив’язаного до конкретного bonus redemption | PARTIAL | NOT REVALIDATED | 2 | Окрема причина; деталізувати при revalidation |
| SEC-018 | P2 | Custom AI endpoint може отримати server-held shared API key та внутрішній payload | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-019 | P2 | TOTP та integration secrets зберігаються як звичайні entity/text/JSON поля; reset credential живе в outbox | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-020 | P2 | Runtime API запускає міграції, worker використовує широкі GUC bypass policies | PARTIAL | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-021 | P2 | У sampled FK відсутній DB інваріант tenant equality | VERIFIED | NOT REVALIDATED | 3 | F13 |
| SEC-022 | P2 | Redis URL parser ігнорує TLS scheme та ACL username | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-023 | P2 | MQTT anonymous і host-network worker створюють широке локальне trust boundary | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-024 | P2 | Контейнери не оголошують non-root user; worker запускає dev runtime та npm install | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-025 | P2 | CI не містить доведених security supply-chain gates і POS test gate | PARTIAL | NOT REVALIDATED | 3 | F19 |
| SEC-026 | P2 | Audit events видаляються тим самим широким worker, tamper resistance не доведено | PARTIAL | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-027 | P1 | Backup/DR у source обмежені локальним dump; restore та offsite security не підтверджені | PARTIAL | NOT REVALIDATED | 3 | F20 |
| SEC-028 | P2 | AI передає business/customer context зовнішній моделі та зберігає user excerpts без доведеної minimization policy | PARTIAL | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-029 | P2 | Contract signature/stamp upload перевіряє розширення, але не magic bytes/безпечний decode | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
| SEC-030 | P3 | Контактні дані потрапляють у auth logs; export/log sink privacy не має доведеного retention/access contract | VERIFIED | NOT REVALIDATED | 3 | Окрема причина; деталізувати при revalidation |
