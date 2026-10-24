# PHASE-1-05-PASSWORD-RECOVERY

Дата: 23.10.2026. Source ID: **SEC-004 / P1**. Дозвіл: продовжувати налаштування сайту по плану. Status: **LOCAL FIX VERIFIED; NOT DEPLOYED; ST-05 STAGING OPEN**.

## Причина і поведінка

Старий anonymous forgot-password одразу замінював PasswordHash тимчасовим паролем і відкликав refresh до підтвердження власником. Помилка доставки залишала власника без його попереднього пароля. Цей метод видалено з AuthService/IAuthService і замінено окремими recovery controller/service/store.

- `POST /api/auth/forgot-password`: валідний запит повертає 204 для відсутнього, неактивного або активного облікового запису й queue fault. PasswordHash, lockout, MFA та refresh не змінюються. Email нормалізується; account cooldown 60 секунд захищений атомарним UPDATE. Існуючий IP limiter 5/хв збережено.
- 32 криптографічно випадкові байти формують URL-safe proof на 15 хвилин. На users зберігається тільки SHA-256 digest; grant і system outbox записуються однією транзакцією. Повторний дозволений запит замінює старий grant. Клієнт не задає host/return URL.
- Посилання має `/reset-password#email=...&token=...`. Proof не входить до URL query, access logs чи Referer; web прибирає fragment із поточного history entry після завантаження і тримає proof лише в пам'яті компонента.
- `POST /api/auth/reset-password`: proof та пароль передаються в JSON body через HTTPS. Shared password policy лишається чинною. Digest/expiry/active state/expected password перевіряються повторно в guarded SQL UPDATE. Один переможець серед конкурентних підтверджень; replay та expiry відхиляються.
- Одна транзакція змінює пароль, споживає grant, прибирає legacy temp marker, скидає login failure counters, відкликає refresh і пише generic audit. Помилка останнього запису відкочує всі зміни. Скидання не видає JWT/MFA challenge і не вимикає MFA; наступний вхід проходить звичайний password/MFA flow.
- Web очищає локальний auth state лише після успішного підтвердження; API видаляє refresh cookie. Помилковий proof або запит листа не розлогінює поточний клієнт. Звичайна зміна пароля теж прибирає outstanding recovery grant.
- Новий worker event `auth.password_reset_link_requested` допускає тільки email існуючого облікового запису. Прострочені payload не надсилаються. History не містить resetUrl/tempPassword, provider errors для цього event мають generic текст, terminal system intent очищається від proof. Новий flow не доставляє секрет через Telegram/push.

## Файли

Backend: PasswordRecoveryController, PasswordRecoveryService/IPasswordRecoveryService, PasswordRecoveryStore/IPasswordRecoveryStore, User/AppDbContext, DI та migration/snapshot. Shared role-controller registry включає новий controller на всіх API roles, як і AuthController. Legacy temp-password login лишено для раніше виданих credentials, нові temporary passwords більше не генеруються.

Web: `/reset-password`, ResetPasswordForm, resetGrant parser, shared auth card/API/types, anonymous API retry rules, en/uk texts. Сторінка має noindex і no-referrer metadata.

Worker: notification-dispatch і password-recovery-notification helper; старий event лишено тільки для сумісності наявної черги. Notification history вже виключає `Channel=system`; новий proof не потрапляє до per-channel history.

## Перевірки

- Backend build: PASS, 0 errors, 8 попередніх warnings. Вибрана Auth + RoleControllerFeatureProvider + Notifications regression: **513 PASS**, 0 failed/skipped, `test-results/phase-1-password-recovery.trx`.
- Реальна disposable PostgreSQL 16: **4 PASS**, 0 failed/skipped. Concurrent issue → один grant/outbox без зміни credential; concurrent confirm → один winner, replay reject, refresh revoke, MFA збережено; expiry і final audit failure → консистентний rollback; migration Down/Up → account збережений, grant columns nullable. Proof: `test-results/phase-1-password-recovery-postgres.trx`.
- Fixture: `tests/Test-PasswordRecoveryPostgres.ps1`; тільки новий випадковий container/network/password, loopback random port, tmpfs data, outbound NAT disabled, локальний image без pull. Не читає application connection strings; відмовляється перезаписувати наявний recovery-test env. Видаляє тільки власні згенеровані ресурси. Application-required pg_trgm встановлюється лише в fixture DB.
- Web: **30 PASS**, `test-results/phase-1-password-recovery-web.xml`: proof parsing/duplicates, auth API local-state effects, anonymous reset failures без session refresh; frontend TypeScript і targeted lint PASS.
- Worker: **8 PASS** (`node --import tsx --test src/services/password-recovery-notification.test.ts`), TypeScript PASS. Synthetic HTTPS email formatting/escaping, unsafe URL reject, expiry, history redaction; зовнішні провайдери не викликалися.
- Візуальне browser/client QA, actual HTTP middleware/RLS і реальний email round trip у staging: **NOT TESTED**. Unit/controller tests не замінюють цей gate.

## Конфігурація, міграція і rollout

Використано наявний ключ `Frontend:BaseUrl` / `Frontend__BaseUrl` у shared API Compose environment. Для кожного середовища він має містити його довірений HTTPS URL із потрібним портом; наприклад `https://web.synthetic.example`. Не допускаються credentials, query або fragment. Відсутній/HTTP/небезпечний URL відхиляється до user lookup; hardcoded production fallback і Request.Host не використовуються. Наявний localhost HTTP default Compose непридатний для recovery: перед rollout потрібно явно задати HTTPS адресу. Реальне значення не змінювалося й не перевірялося.

Migration `20261023001000_AddProofBasedPasswordRecovery` додає три nullable users columns: PasswordResetTokenHash varchar(64), PasswordResetExpiresAt timestamptz, PasswordResetRequestedAt timestamptz. Наявні PasswordHash/інші дані не переписує. Migration підготовлена; Down/Up operations перевірені на synthetic fixture, application migration chain/реальні DB не запускалися.

Порядок приймання: перевірити цільову HTTPS адресу й тестову email-доставку; у погодженому rollout застосувати additive migration; розгорнути сумісні web і worker перед новим API; виконати ST-05 на synthetic account, перевірити старий login до підтвердження, single-use/expiry, MFA після reset і redacted history/logs. Не вмикати новий API зі старим worker: той не знає нового event. Не повертати старий public password-mutation flow під час rollback; forward fix або тимчасовий fail-closed recovery route. Не видаляти columns, доки їх використовує будь-який новий API artifact.

## Межі

**SEC-019 OPEN:** pending system outbox містить raw короткоживуче посилання для доставки. Worker очищає його після terminal processing, але encryption/backup access і cleanup за вимкненого worker тут не вирішені. Users зберігає digest; proof незалежно перестає працювати через 15 хвилин.

**SEC-005 OPEN:** відкликаються refresh, але вже видані access JWT і outstanding MFA challenges не отримали нового механізму негайної інвалідації. Повний session lifecycle і його SLA залишаються наступною роботою. Це також не доводить відсутність усіх race cases між reset та іншими auth flows.

Нові mandatory MFA/production/AI правила з ACCESS_MATRIX_PROPOSAL.md залишаються DRAFT. Реальні email, sessions, staging/production DB і deployment не змінювалися.

Оновлення наступного пакета: **PHASE-1-06-SESSIONS.md** додав live access-session checks та auth-version invalidation після reset для staff JWT і MFA challenges. Наведений вище SEC-005 OPEN описує стан пакета 2; актуальні межі й rollout gates — у пакеті 3 та OPEN_GAPS.md.
