# PHASE-1-06-SESSIONS

Дата: 23.10.2026. Source ID: **SEC-005 / P1**. Дозвіл власника: «так. продовжуй роботу над сесіями».
Status: **LOCAL FIX VERIFIED; NOT DEPLOYED; STAGING/OPERATIONAL ACCEPTANCE OPEN**. Вибрані suites: **612 PASS** (531 backend + 11 PostgreSQL/HTTP + 29 web + 41 mobile).

## Причина і реалізація

Consumer JWT раніше жив 30 днів без refresh або серверної перевірки session state; локальний вихід не відкликав скопійований токен. Staff logout відкликав тільки cookie refresh row, а чинний access JWT і старий MFA challenge залишалися придатними. Rotation через tracked read/write могла створити два replacement tokens при одночасних запитах.

- Consumer access тепер діє до 15 хвилин: `Jwt:ConsumerAccessTokenMinutes`, default 15, межі 1–15. Старий `ConsumerAccessTokenDays` більше не визначає строк access JWT. Окремий personal refresh має абсолютний строк 30 днів; staff refresh — 7 днів. Rotation зберігає початковий expiry, без нескінченного продовження сесії.
- Access JWT містить `sid` і `auth_ver`. На кожному новому authenticated HTTP request сервер читає session/account state з DB, без positive cache. Перевіряються власник, тип identity, активність, expiry, revocation, версія, staff role/tenant та активний tenant. Чужий/missing sid, consumer/staff mismatch, sid-less legacy JWT відхиляються. Збій перевірки закриває доступ.
- Consumer proofs зберігаються як SHA-256 hashes у новій `consumer_refresh_tokens`, staff використовує `refresh_tokens`. Refresh ніколи не передається в URL/logs. Окремі таблиці та endpoint не дозволяють оновити workspace за personal proof. Новий mobile response додає `personalRefreshToken`; native client зберігає його в SecureStore. Auth responses мають no-store.
- Атомарна rotation спочатку блокує owner row, перевіряє active/version, виконує guarded consume і вставляє наступний proof однією транзакцією. Один переможець; часткових replacement rows немає. Partial unique indexes забезпечують одну невідкликану refresh row на session і швидкий live lookup.
- Повтор rotated proof у 10-секундному вікні повертає 409, без повторної видачі секрету, без відкликання сімейства та без видалення staff cookie. Web робить одну повторну спробу з поточною cookie. За межами вікна consumer replay відкликає його session family; staff replay зберігає попередню сувору політику — відкликає всі staff sessions і змінює auth version. Невідомий/прострочений proof відхиляється.
- Logout відкликає все поточне сімейство, включно replacement після rotation. Staff використовує sid authenticated caller; за простроченого access може використати HttpOnly SameSite refresh-cookie proof. Cookie чужої сесії не підміняє sid authenticated caller. Consumer і impersonation credentials не можуть виконувати staff session-management actions.
- Self-service `POST /api/auth/logout-all` та `POST /api/consumer-auth/logout-all` змінюють owner auth version і відкликають усі його refresh rows транзакційно. Немає нового endpoint для довільного чужого account або нового admin grant.
- Зміна staff пароля, password recovery, деактивація, зміна базової ролі та MFA enable/disable змінюють auth version. Consumer password/active changes через tracked EF SaveChanges також змінюють версію; старі sessions не відновлюються після reactivation. Прямий SQL поза цими шляхами має явно змінювати AuthVersion/відкликати сесії — live DML не перевірявся.
- MFA challenge підписаний із auth version і повторно порівнюється з поточним User; reset/revoke роблять старий challenge недійсним. EF concurrency checks для AuthVersion, TOTP timestep і recovery-code list відхиляють stale password/login updates та подвійне споживання recovery code.
- Увімкнення добровільної MFA атомарно видає нову сесію тільки клієнту, який підтвердив enrollment code, разом зі збереженням MFA/recovery codes. Web приймає нові credentials до показу recovery codes; старі sessions не обходять новий захист. Обов'язкова MFA за ролями не застосовувалася.
- Impersonation JWT прив'язано до parent provider sid/version та активного target tenant; відкликання parent відключає його на наступному запиті. Завершення impersonation UI лишається відкиданням токена; окремої revocation identity для кожного delegation тут немає.

## Клієнти

Web зберігає наявні single-flight/cross-tab refresh locks. Session epoch забороняє запізнілому refresh або старому 401 очистити чи відновити credentials після logout/new login. У user menu додано «Вийти на всіх пристроях» з підтвердженням; невдале глобальне відкликання не оголошується успіхом.

Mobile personal client отримав незалежний single-flight refresh; staff cookie endpoint не використовується як fallback. Terminal personal failure очищує лише personal identity/proof, зберігаючи workspace. 409/429/5xx/transport errors зберігають credentials для повторної спроби. Login/refresh credential writes і logout cleanup серіалізовані; late refresh перевіряє epoch та поточний proof, щоб не перезаписати новий account. Зміна personal account очищує старий query cache. Обидва profile screens викликають server logout для наявних identities та підтримують logout-all; локальна очистка прибирає й refresh proof.

Без зв'язку серверне відкликання неможливо підтвердити: mobile показує повідомлення про локальний вихід і очищує пристрій. Це не гарантує відкликання раніше скопійованої сесії на сервері. Після отриманого online logout ACK новий HTTP request із копією JWT відхиляється.

## Перевірки

Фактичні команди й лічильники — у CHANGELOG_AUDIT.md та `test-results/phase-1-sessions*`.

- Auth/MFA/consumer/controller/authorization/provider/POS regressions; реальні JWT signature/claims/TTL tests.
- Disposable PostgreSQL + loopback Kestrel + справжній JwtBearer event: copied JWT після logout → 401, інший personal device → 200; logout-all → 401; consumer не проходить staff role policy; password/deactivate/re-activate/stale write/foreign sid/legacy JWT; parent impersonation revoke; concurrent rotation один winner; recovery code один commit; migration Down/Up/backfill; попередні recovery transaction tests.
- Web: rotation conflict, late refresh після logout/new login, MFA credential upgrade, failed global logout збереження state, попередній recovery contract.
- Mobile: окремий personal refresh, parallel 401 single-flight, identity isolation, transient/terminal failures, network/persistence logout races, новий login під час refresh, SecureStore cleanup і profile regression. TypeScript та web targeted lint.

Fixture: `& ./docs/analys/remediation/tests/Test-PasswordRecoveryPostgres.ps1 -Suite Sessions`. Він не використовує working DB connection, створює tmpfs PostgreSQL на випадковому loopback port з локального image і прибирає тільки власні випадкові container/network names. Ніяких production token probes або реальних identities.

## Міграція та приймання

Підготовлено `20261023151626_AddRevocableIdentitySessions` із Designer/model snapshot. Додає AuthVersion до users/consumer_accounts/refresh_tokens, SessionId до refresh_tokens і нову consumer_refresh_tokens з FK/unique indexes. Існуючим staff refresh rows присвоюється SessionId = Id; PasswordHash не змінюється. Backfill/index creation потребують оцінки обсягу й вікна rollout. Migration згенеровано через design factory із synthetic connection на незадіяний port; migration update робочої БД не запускався. Down/Up operations перевіряються лише у fixture, не весь історичний migration chain.

Перед deployment потрібні міграція, сумісні API/web/mobile artifacts і погоджена legacy-session transition. Sid-less consumer JWT більше не приймається: старим personal clients потрібні оновлення та повторний вхід. Чинна staff cookie може пройти refresh на новому API після backfill; старі MFA challenges потребують повторного password step. Не залишати старі API instances, які приймають JWT без live session check. Раніше видані production credentials зараз не відкликалися, бо deployment/DML не виконувалися.

Rollback fail-closed: forward fix або тимчасове обмеження відповідних auth routes; не повертати signature-only validation і 30-day access JWT. Не виконувати Down при працюючому новому API: він видаляє session metadata, а стара версія не гарантує revocation. Перевірити runtime grants/RLS/tenant visibility і всі API_ROLE за актуальним ingress у staging.

## SLA і залишкові межі

Локально підтверджена межа: **новий HTTP auth check після commit revoke відхиляє JWT без cache delay**. Вже виконуваний запит може завершитися; live SLA/load/performance ще не виміряні. Consumer SignalR закривається за JWT expiry (`CloseOnAuthenticationExpiration`), але наявне websocket-з'єднання не розривається миттєво при revoke: можливе вікно до 15 хвилин, у тому числі server push. Це не заявляється як негайна revocation активних sockets.

POS operator sessions лишаються в окремому existing validator/cache lifecycle; staff password reset не є новою командою закриття всіх POS register sessions. Offline POS workflow не змінено. Retention/purge старих refresh rows, live DB grants/RLS, staging multi-instance/network/device acceptance, provider delegation cleanup і operational acceptance лишаються відкритими.

SEC-004 отримав додаткову session invalidation в цьому пакеті; його попередній опис обмежень у PHASE-1-05 є історичним. SEC-006/009/010 і ACCESS_MATRIX_PROPOSAL.md лишаються DRAFT/OPEN щодо нових MFA/production/AI правил. **LOCAL IMPLEMENTATION; NOT DEPLOYED.**
