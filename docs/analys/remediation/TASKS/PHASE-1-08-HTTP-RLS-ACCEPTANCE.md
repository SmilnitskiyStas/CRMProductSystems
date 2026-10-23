# PHASE-1-08 — HTTP-перевірки та контекст підприємства під час входу

Дата: 23.10.2026. Продовження PHASE-1-07 за дозволом власника «продовжи роботу».
Source IDs: SEC-005/006/009/010/012, F07/F09.
Стан: **LOCAL UNIT/HTTP CHECKS PASS; POSTGRESQL ACCEPTANCE PENDING; NOT DEPLOYED**.

## Зміни

- Спільна `ApiSecurityRegistration` реєструє JWT, перевірку живої сесії, policies та rate limits і встановлює порядок authentication → rate limiting → authorization. `Program` і нові HTTP-тести використовують один код. Hub query-token fallback зберігає наявну межу маршруту.
- Статично підтверджений RLS gap: anonymous login/enrollment/refresh ще не має tenant claim, але записує tenant-owned `activity_logs` та читає `tenant_roles` / `user_permission_grants`. Чинні SQL policies вимагають tenant context. `AuthService` тепер використовує `ITenantSessionOverride` лише з tenant ID збереженого користувача: видача сесії й audit commit, лічильники невдалих спроб, читання профілю, replay audit. `SET LOCAL` діє тільки до завершення транзакції; tenant не береться з login request. Rotation та MFA confirmation залишають власні транзакції, вкладення немає. Provider без tenant не отримує довільного override. Політики RLS і схема БД не змінені.
- Unit-перевірки контролюють tenant під час успішного/невдалого входу, читання capability template, refresh profile та replay audit; rotation виконується поза override.
- Два Kestrel HTTP-тести використовують справжню JWT registration та limiter: 11-й запит першого користувача отримує 429, інший користувач із тієї самої IP-адреси — 200; enrollment audience відхиляється, query bearer приймається лише на hub route. Live-session validator тут підмінений; ці два тести не доводять RLS або DB revocation.
- Підготовлено три окремі `SecurityHttpAcceptanceTests`: весь migration chain на порожній disposable DB, роль `NOSUPERUSER/NOBYPASSRLS`, anonymous isolation; реальні AuthController enrollment/OTP/cookie/refresh/logout; повернення capabilities/tabs/grants staff після MFA. Вони використовують справжні application/infrastructure services та shared auth pipeline. Немає реальних accounts, email/AI calls, startup seed або application secrets. Це локальний HTTP loopback: cookie flags перевіряються в response; TLS/browser acceptance окреме.
- Fixture `Test-PasswordRecoveryPostgres.ps1 -Suite Http` обирає лише ці три тести. Read-only Docker preflight має timeout 15 секунд і завершує лише власний probe process; за помилки ресурси fixture не створюються. Existing database fallback відсутній.

## Фактичні результати

- Backend build: PASS, 0 errors, 8 попередніх nullable/EF1002 warnings.
- Вибрані Auth/Authorization/Production/AI/Provider/POS/Users/TenantRoles/Operator/ControllerFeature/TenantConnectionInterceptor та нові HTTP suites: **816 PASS, 5 SKIP, 0 FAIL**. Звіт `../test-results/phase-1-http-regression.trx`. У pass входять два нові HTTP pipeline cases; їх початковий окремий запуск також 2 PASS (`phase-1-http-pipeline.trx`), вдруге їх не додаємо до суми.
- П’ять skip: два disposable POS races та три нові HTTP/PostgreSQL cases, бо `SHELFGUARD_RECOVERY_TEST_DB` не заданий. Пропуск не є успішним DB acceptance.
- Повний PostgreSQL/HTTP запуск **НЕ ВИКОНАНО**: Docker engine не відповідає. Docker Desktop запущено у фоні; `docker version` та перша спроба network create зависли й були перервані. Наступна спроба з preflight завершилась timeout до створення fixture. Активних власних `docker.exe` після скасування не залишилось; перелік engine resources недоступний, тому cleanup першої спроби незалежно не підтверджено. Docker/WSL не перезапускали, дані/volumes не видаляли.
- Первісна збірка після додавання constructor dependency виявила один пропущений target-typed test constructor; fixture оновлено, фінальна збірка успішна.
- Web/mobile цього пакета не змінювалися й повторно не запускалися. Попередні результати PHASE-1-07 збережені як історичний доказ, не як новий запуск.

## Наступний gate

Після готовності Docker виконати:

```powershell
dotnet build backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal /nodeReuse:false
./docs/analys/remediation/tests/Test-PasswordRecoveryPostgres.ps1 -Suite Http
./docs/analys/remediation/tests/Test-PasswordRecoveryPostgres.ps1 -Suite Matrix
```

Команди запускати послідовно; переходити далі лише після успішного попереднього кроку. Http застосовує весь поточний migration chain тільки до random loopback tmpfs fixture, Matrix перевіряє попередні 25 DB/HTTP/concurrency cases. Не використовувати інші legacy DB suites з default dev connection. За engine availability перевірити відсутність ресурсів перерваного fixture, видаляти лише підтверджені ресурси цього запуску. Не видаляти довільні ресурси за загальним prefix.

Після цього лишаються реальні staging ingress/TLS/browser/device checks, rollout web/mobile/API/worker, перехід legacy sessions і operational рішення. Фазу 1 та production acceptance не закрито. Робочі DB/міграції/сесії, deployment, ключі та зовнішні провайдери не змінювалися.

Rollback source: повернути попередній безпечний artifact лише в рамках узгодженого rollout. Повернення auth override відновить описаний RLS gap, тому не є прийнятим production fallback. Нової schema migration цей пакет не додає.
