# DECISIONS_REQUIRED

Дата: 23.10.2026. Нижче відкриті рішення; жоден варіант не вважається затвердженим автоматично.

## Перед прийманням і розгортанням фази 0

1. **Зв'язування personal/workspace.** Поточне containment: consumer завжди personal-only. Якщо staff і consumer мають спільний identifier, mobile login обирає consumer і не переходить до staff навіть за помилки пароля. Звичайний `/api/auth/login` використовує staff email/password/MFA незалежно від consumer. Варіанти наступного UX: окремий явний staff login; link через staff reauthentication + MFA; одноразовий admin-approved grant, прив'язаний до обох identities. Самого підтвердження телефону/email недостатньо. Потрібен вибір власника перед реалізацією нового linking UX.
2. **Раніше видані сесії.** Виправлення припиняє нову небезпечну видачу; вже видані JWT/refresh/2FA challenges автоматично не відкликані. У старого flow немає надійного маркера походження linked session. Варіанти: погоджена примусова переавтентифікація всіх staff; scoped revoke після metadata/log review; тимчасові обмеження доступу до завершення TTL. Визначити охоплення refresh та outstanding MFA challenges, maintenance window і користувачів, яких зачепить рішення. Не виконувати revoke або rotation самостійно.
3. **Staging і rollout.** Хто надає ізольоване середовище, synthetic accounts і відповідального за приймання ST-01/ST-02? Визначити безпечний rollback artifact/route disable, критерії спостереження та момент розгортання.

## Перед наступними фазами

Для фази 1 власник спочатку попросив матрицю, а потім прямо дозволив її застосування. Mandatory MFA, production permissions та AI context restrictions реалізовано локально в PHASE-1-07; таблиці збережено в ACCESS_MATRIX_PROPOSAL.md.

- MFA: staging/device enrollment/recovery, operational session-revoke SLA, окрема break-glass identity.
- Права: staging приймання production.view/manage/complete/cancel, ai_assistant.use і обмеженого context, PII export.
- Private storage: класифікація public/private assets, клієнтська сумісність, access audit і retention.
- Бонуси: discount чи tender, line allocation, rounding, tax base, customer approval thresholds/override.
- Податки/ПРРО/повернення: ставки, фіскальні стани, partial returns і reversal — підтверджує відповідальний фахівець.
- Business date: timezone/cutoff/DST/offline clock skew; історичні записи inferred чи корекція.
- FEFO: expired/quarantine/no-expiry batches, actual yield/waste, cost allocation.
- DR/privacy/AI: RPO/RTO, offsite/PITR, retention/країна, дозволені провайдери/поля/egress та consent.

Ці рішення не блокують уже виконане локальне усунення автоматичної видачі staff-сесії.

## Приймання password recovery (PHASE-1-05)

Локальний SEC-004 fix уже реалізовано: одноразове email-посилання на 15 хвилин; до proof пароль і сесії не змінюються. Це не затверджує нової role/MFA policy. Перед rollout потрібні фактичний HTTPS `Frontend__BaseUrl` для відповідного середовища, узгоджені migration/web/API/worker artifacts і synthetic ST-05 із тестовою поштовою скринькою. Робочі DB та реальні email не використовувалися. PHASE-1-06 додав invalidation access JWT/старих MFA challenges через auth version; захист pending outbox SEC-019 ще OPEN.

## Приймання sessions (PHASE-1-06)

Механізм локально перевірено; production сесії не змінювалися. Потрібно погодити вікно міграції/backfill, обов'язкове оновлення personal mobile client та повторний вхід для legacy sid-less consumer JWT. Staff refresh-cookie migration path збережено. Всі API instances мають одночасно перейти на live session check. Локально підтверджений SLA стосується нового HTTP request після commit; активний consumer websocket може жити до expiry (≤15 хвилин), POS operator — окремий lifecycle. Потрібно визначити, чи достатньо цього перед production, або потрібен наступний пакет негайного відключення sockets. Власник дозволив застосування mandatory MFA/production/AI; PHASE-1-07 реалізовано локально. Потрібні rollout window, узгоджені клієнти та staging acceptance.

## Матриця доступу (PHASE-1-07)

Власник дав дозвіл «продовжи над Застосувати матрицю доступу». Policy таблиць ACCESS_MATRIX_PROPOSAL.md прийнята для реалізації; строк step-up — 5 хвилин, maker/checker не вимагається. Перед deployment лишаються operational рішення: window для міграції MfaVerifiedAt і повторного входу mandatory/TOTP sessions без verified time; сумісні API/web/mobile artifacts; MFA/recovery enrollment на реальних підтримуваних пристроях. Break-glass identity не створюється автоматично. Новий linking/SSO flow потребує окремого task scope.
