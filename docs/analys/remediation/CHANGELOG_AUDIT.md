# CHANGELOG_AUDIT

## 23.10.2026 — PHASE-0-SEC-001: локальне containment

**Baseline:** HEAD `3563ce6c79596ae010f4baebc920d0685f073876`, dirty checkout. Файли цього fix перед початком не мали локальних змін; попередні зміни в loyalty/mobile/config/tests збережено. Коміт/PR/deployment не створювалися.

**Дозвіл:** власник дозволив почати поетапне редагування. Поточний diff стосується лише фази 0.

### Підтверджена причина і зміни

До виправлення `MobileAuthController.Register/Login` знаходили staff User за consumer phone/email і викликали `IssueLinkedMobileSessionAsync(userId)`, який міг видати staff session або MFA challenge без staff password. SEC-001 = CONFIRMED на поточному checkout перед зміною; production exploit/інцидент не перевірялися.

- `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs:47`: registration повертає лише consumer session; staff lookup/mint/challenge прибрано.
- `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs:70`: consumer login повертає personal token з consumer-only permissions; staff не шукається і не автентифікується за consumer proof.
- `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs:93`: staff-only fallback збережений через перевірку staff password у LoginAsync і штатну MFA.
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs:75`: штатний staff password/lockout/temp-password/MFA flow збережено. Passwordless `IssueLinkedMobileSessionAsync` видалено з сервісу й IAuthService.
- `backend/ShelfGuard.Application/Features/MobileAuth/Dtos/MobileAuthDtos.cs:22`: wire shape збережено; коментарі уточнені; невикористана ForLinkedStaff factory видалена.
- `backend/ShelfGuard.Tests/Auth/MobileAuthControllerTests.cs:43`: негативна матриця email/phone × staff/admin/provider × MFA on/off для login і registration, зміна/видалення consumer email, помилкові login/register; збережені personal та staff/MFA regressions.
- `backend/ShelfGuard.Tests/Auth/MobileLoginResponseFactoryTests.cs:28`: перевірка відсутності consumer workspace token/tenant/store/permissions/capabilities/tabs.

Переглянуто пов'язані mint paths: staff password login, MFA verify, refresh, provider impersonation, POS operator PIN/device session, consumer register/login. Межі й обмеження — TASKS/PHASE-0-SEC-001.md. Це source review, не повний runtime security audit.

### Фактичні перевірки

| Перевірка | Команда / доказ | Результат |
|---|---|---|
| Build backend + test project | `dotnet build backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal /nodeReuse:false` | PASS; 0 errors, 8 warnings поза зміненими auth файлами |
| Auth regression suite | `dotnet test backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~ShelfGuard.Tests.Auth --logger console;verbosity=minimal --logger trx;LogFileName=phase-0-auth.trx --results-directory docs/analys/remediation/test-results` (logger arguments передані з shell quoting) | PASS: 438 executed, 438 passed, 0 failed, 0 skipped; серед них 33 mobile-controller/factory cases |
| Raw test evidence | `test-results/phase-0-auth.trx`, ResultSummary/Counters | total=438, executed=438, passed=438, failed=0, notExecuted=0 |
| Diff whitespace | `git diff --check --` шість змінених auth source/test файлів | PASS |
| Removed auth path references | Пошук `IssueLinkedMobileSession`, `FindLinkedUserAsync`, `ForLinkedStaff` у backend | 0 references |
| Registry/matrix completeness | Рядки F01–F24, SEC-001–SEC-030 у MASTER_FINDINGS/TEST_MATRIX | 54 у кожному; current revalidation інших 53 не заявляється |
| ST-01/ST-02 isolated HTTP/client/staging acceptance | Не виконувалися | NOT TESTED |
| Production/deploy/session revoke | Не виконувалися | NOT TESTED / OPEN |

Перші стандартні parallel MSBuild/VSTest спроби не завершили перевірки: build повернув failure без diagnostics; VSTest у sandbox не зміг з'єднатися з локальним testhost за 90 секунд. Потім single-node build пройшов; auth suite успішно виконано поза sandbox з дозволеним локальним IPC. Початковий aborted TRX замінено успішним фінальним результатом. Не прирівнювати інфраструктурний aborted run до test assertion failure або PASS.

Build warnings: CS8604 у ShelfServiceTests; CS8602 у MarketplaceServiceTests/RegisterFlowIntegrationTests; EF1002 у ItemImagesRlsIntegrationTests/ShelfRlsIntegrationTests. Ці файли не змінювалися в межах фази 0.

### Сумісність, міграція, rollback і залишкові ризики

Routes/request/response fields збережені. Consumer завжди отримує лише personal token. Автоматичний dual-session login більше не працює. За спільного identifier mobile login обирає consumer; штатний staff `/api/auth/login` з email/password/MFA залишається доступним. Окремий захищений linking UX ще не реалізований.

Міграція не потрібна. Production і реальні identities/secrets не читалися та не змінювалися.

Rollback має зберігати заборону auto-link: forward fix, безпечний artifact або обмеження відповідного mobile route за погодженим operational scope. Blind revert цього diff відновить P0 і не є прийнятним rollback.

Вже видані access/refresh/2FA credentials не анульовані. Їх перегляд/відкликання, повний staging/client acceptance та deployment залишаються OPEN; варіанти — DECISIONS_REQUIRED.md. Статус: **LOCAL FIX VERIFIED; NOT DEPLOYED; OPERATIONAL ACCEPTANCE OPEN**.

## 23.10.2026 — Фаза 1, пакет 1: private files, token logs, identity limiter, POS HTTPS

**Дозвіл:** власник попросив продовжувати реалізацію по плану. На уточнення про MFA/production/AI вибрав «Спершу підготуй матрицю доступу». Підготовлено ACCESS_MATRIX_PROPOSAL.md (DRAFT), нові правила ще не застосовані.

**Baseline:** той самий HEAD і dirty checkout з попередніми змінами власника та фазою 0. Зміни не закомічені, PR/deployment не створені. Audit ZIP і вихідне ТЗ не редагувалися.

### Фактичні зміни

| Task / IDs | Що змінилося | Current evidence |
|---|---|---|
| PHASE-1-01 / SEC-002, F06 | Нові contracts/signatures/stamps/scans/plans зберігаються поза wwwroot. Старі private URL блокуються до StaticFiles на всіх API roles. Authenticated image endpoint перевіряє tenant, policy, живе location assignment і DB reference. Web Blob та mobile Image використовують header auth; PDFs лишаються на agreement-party download API. Додано persistent private volume у source Compose для всіх API roles. | PrivateFilesController.cs:22,78; PrivateUploadGuard.cs; PrivateUploadPaths.cs; ShelfService.cs:28,39,283; SupplierAgreementService.cs:513,619,702 |
| PHASE-1-02 / SEC-003 | Query-free access logs на gateway та edge; raw error logging для hub routes вимкнено, sanitized status/latency лишилися. Збережено WebSocket proxy headers. | infra/gateway/nginx.conf:59,119; infra/nginx/shelfguard-ratelimit.conf:22; infra/nginx/shelfguard.conf:21,66,96 |
| PHASE-1-03 / SEC-012 | Authentication виконується перед subject-based limiter. Anonymous IP policies і authorization збережені. | backend/ShelfGuard.Api/Program.cs:416–419 |
| PHASE-1-04 / SEC-014 | HTTPS policy на normalizer і кожному transport call, включно saved config. Remote HTTP/unsafe URL/redirect заборонено; dev loopback HTTP тільки з explicit opt-in у unpackaged app. Unsafe transport не відправляє auth/body і не створює HTTP 4xx verdict для outbox. | cashier/electron/http.ts:33–74; main.ts:133; service.ts:249,269 |

Нові файли: private path resolver, guard/controller, negative authorization/path tests, web PrivateImage і safe routing, mobile safe Image source та тести. Детальний scope/migration/rollback — TASKS/PHASE-1-01…04. Попередні локальні зміни в інших файлах не скасовувалися.

### Реальні результати перевірок

| Перевірка | Команда / proof | Результат |
|---|---|---|
| Backend build | `dotnet build backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal /nodeReuse:false` | PASS; 0 errors, 8 попередніх warnings поза зміненими файлами |
| Backend regression | `dotnet test ... --no-build --no-restore --filter` з Auth, PrivateFilesControllerTests, ShelfServiceTests, ShelfInsightTests, SupplierAgreementServiceTests, RoleControllerFeatureProviderTests, AppPoliciesTests | PASS: 541 executed/passed, 0 failed/skipped; включає 26 private file/guard cases і 19 floor-plan/insight cases; `test-results/phase-1-security.trx` |
| POS transport | `node node_modules/vitest/vitest.mjs run electron/http.test.ts` з JUnit reporter | PASS: 16 tests; `test-results/phase-1-pos-http.xml` |
| Web API/private URL routing | `node node_modules/vitest/vitest.mjs run lib/api.test.ts lib/privateAssets.test.ts` з JUnit reporter | PASS: 28 tests; `test-results/phase-1-web-assets.xml` |
| Mobile URL/header auth + shelf-check | `node node_modules/jest/bin/jest.js --runInBand --no-cache lib/__tests__/private-assets.test.ts features/shelf-check` | PASS: 17 tests, 2 suites; captured console output |
| TypeScript | Cashier `npm run typecheck`; frontend і mobile `tsc --noEmit --incremental false` | PASS для всіх трьох |
| Frontend lint | `next lint --file ...` для api/privateAssets/PrivateImage/ShelfScanPanel/FloorPlanCanvas/ContractSettingsForm | PASS, 0 warnings/errors |
| Nginx syntax + hub error privacy | `docs/analys/remediation/tests/Test-NginxPrivacy.ps1` | PASS: gateway/edge nginx -t; synthetic bearer marker відсутній у hub failure logs, sanitized request record присутній. `test-results/phase-1-nginx.json` |
| Diff whitespace | `git diff --check --` для змінених файлів пакета | PASS |

Разом у вибраних regression suites: **602 успішні тести** (541 + 16 + 28 + 17). Це не повний test run репозиторію або закриття всіх findings фази 1.

Sandbox спочатку блокував local POS connection/Jest transform cache; цільові тести пройшли після дозволеного запуску поза sandbox. Залежності cashier/frontend встановлено рівно за lockfiles через npm ci --ignore-scripts; lockfiles не змінені, lifecycle scripts/installer builds не запускалися. Mobile dependencies вже були наявні. Початкові невдалі infrastructure runs не оголошувалися PASS; JUnit/TRX містять фінальні успішні прогони.

Наявні tooling warnings: Vite CJS API deprecation, jsdom navigation stub у тесті denied refresh, старий nginx listen-http2 syntax. Їх не виправляли як частину security пакета. Nginx tests використовували локальний image `nginx:alpine`, без image pull, мережі чи портів; реальні cert/key/config secrets не підключалися. Тимчасовий synthetic certificate видалено після тесту.

### Відкриті gates і наступні роботи

- **SEC-004, SEC-005:** повторно підтверджено; proof-based reset і consumer session lifecycle ще не реалізовані. Наявний auth regression PASS не означає, що ці проблеми виправлені.
- **SEC-006, SEC-009, SEC-010, F07, F09:** підтверджено; concrete MFA/production/AI rules підготовлено в ACCESS_MATRIX_PROPOSAL.md. Застосування чекає погодження власника.
- **SEC-012:** runtime ST-13 subject partition test не запускався; local change перевірено build/regressions/source order.
- **Private files/POS/logs:** повний staging, actual DB grants/RLS, client/hardware/packaged-terminal QA, certificate/device tests, WAF/APM/CDN evidence лишаються OPEN.
- DB schema/data не змінювалися, legacy files не копіювали/видаляли, чинні сесії не відкликали. Private storage source definitions потребують узгодженого API/web/mobile rollout, persistent volume/backups і перевірки старих cache/alias. Не змішувати уразливий old backend із новими private-file writers.
- Rollback fail-closed: не відновлювати public private assets, raw bearer logs або remote HTTP POS; зберігати private volume/outbox і використовувати forward fix/обмеження відповідного route за затвердженим operational scope.

Статус: **ПЕРШИЙ ПАКЕТ ФАЗИ 1 ЛОКАЛЬНО ПЕРЕВІРЕНИЙ; ФАЗА 1 ЗАГАЛОМ IN PROGRESS; NOT DEPLOYED.**

## 23.10.2026 — Фаза 1, пакет 2: proof-based password recovery (SEC-004)

Власник дозволив продовжувати налаштування сайту по плану. SEC-004 повторно підтверджено на поточному source: старий ForgotPasswordAsync до proof змінював пароль на тимчасовий і відкликав refresh. Реалізовано окремі PasswordRecoveryController/Service/Store, web confirmation page та worker email event. Деталі, файли, migration/rollback і межі: **TASKS/PHASE-1-05-PASSWORD-RECOVERY.md**.

Тепер request тільки резервує SHA-256 digest випадкового 256-bit proof і system outbox, зі строком 15 хвилин і атомарним account cooldown 60 секунд. PasswordHash/сесії не змінюються до підтвердження. Guarded confirmation атомарно споживає proof, змінює пароль, відкликає refresh і пише audit; replay та конкурентний другий consume відхиляються. MFA збережено, нових сесій reset не видає. Legacy temporary-password login збережено лише для раніше виданих credentials.

Web використовує fragment link, очищує URL і після успіху прибирає локальний auth state. Український/англійський UX оновлено. Worker надсилає новий event тільки на email, редагує history/error details і прибирає proof з terminal outbox. Використано наявний `Frontend:BaseUrl` без fallback host; recovery приймає лише довірений HTTPS URL із конфігурації. Реальних конфігурацій або email не читали/не змінювали.

### Реальні перевірки пакета 2

| Перевірка | Команда / proof | Результат |
|---|---|---|
| Backend build | `dotnet build backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal /nodeReuse:false` | PASS; 0 errors, 8 попередніх warnings у ShelfServiceTests, MarketplaceServiceTests, RegisterFlowIntegrationTests, ItemImagesRlsIntegrationTests/ShelfRlsIntegrationTests |
| Auth/controller/notification regression | `dotnet test ... --no-build --no-restore`; filter об'єднує Auth, RoleControllerFeatureProviderTests і Notifications (точна команда нижче) | **513 PASS**, 0 failed/skipped; `test-results/phase-1-password-recovery.trx` |
| PostgreSQL concurrency/atomicity/migration | `& ./docs/analys/remediation/tests/Test-PasswordRecoveryPostgres.ps1` | **4 PASS**, 0 failed/skipped; `test-results/phase-1-password-recovery-postgres.trx` |
| Web proof/API contract | Із frontend: `node node_modules/vitest/vitest.mjs run lib/api.test.ts features/auth/resetGrant.test.ts features/auth/api/passwordRecovery.test.ts --reporter=default --reporter=junit --outputFile=../docs/analys/remediation/test-results/phase-1-password-recovery-web.xml` | **30 PASS**, 3 suites; JUnit artifact |
| Worker safe email/expiry/redaction | Із worker: `node --import tsx --test src/services/password-recovery-notification.test.ts` | **8 PASS**; captured TAP output; зовнішнього email/Telegram/push не було |
| TypeScript | Frontend `tsc --noEmit --incremental false`; worker `tsc --noEmit` | PASS |
| Frontend lint | `next lint --file ...` для змінених production auth/API files | PASS; тестовий passwordRecovery.test.ts ігнорується наявною ESLint policy, перевірений Vitest/tsc |
| Diff whitespace / retired method references | `git diff --check -- ...`; пошук ForgotPasswordAsync/GenerateTempPassword/App:FrontendUrl | PASS; retired/new-unused config references відсутні |
| HTTP/RLS/browser/email acceptance | ST-05 staging на окремому synthetic account не запускався | NOT TESTED; не прирівнювати mocks/fixture до deployed acceptance |

Разом вибрані suites пакета 2: **555 PASS** (513 + 4 + 30 + 8). Це не повний test run репозиторію.

Точна backend regression команда (з кореня репозиторію):

```powershell
dotnet test backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ShelfGuard.Tests.Auth|FullyQualifiedName~RoleControllerFeatureProviderTests|FullyQualifiedName~ShelfGuard.Tests.Notifications' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=phase-1-password-recovery.trx' --results-directory docs/analys/remediation/test-results
```

Початкові прогони виправлялися, не оголошувалися PASS: controller mock повертав empty string замість null; перший Docker internal network не публікував loopback порт; наступна fixture DB не мала application-required pg_trgm. Mock уточнено, fixture використовує окремий bridge з outbound NAT disabled і створює pg_trgm лише у synthetic DB. Фінальні TRX/JUnit містять успішні результати. Випадкові container/network прибрані в finally, tmpfs DB знищена. Попередження Vite CJS і jsdom navigation stub залишаються tooling limitations.

Автоматична approval review відхилила запропоновану нову доставку reset proof через Telegram: credential-equivalent secret ішов би в непогоджений зовнішній канал. Застосовано безпечний варіант **email-only** для нового event; зміни Telegram delivery не виконувалися. Це не залишило блокування цього пакета й не потребувало нової згоди для локальної email recovery реалізації.

### Deployment і відкриті питання

Підготовлено additive migration `20261023001000_AddProofBasedPasswordRecovery` із трьома nullable users columns; тільки її Down/Up operations перевірено на disposable DB. Наявні робочі/staging/production DB не підключалися й не мігрувалися. Потрібні правильний HTTPS `Frontend__BaseUrl`, погоджений migration/web/worker/API rollout і synthetic ST-05. Наявний HTTP localhost default Compose recovery відхиляє; без конфігурації немає fallback production host.

SEC-004 має статус **LOCAL FIX VERIFIED; NOT DEPLOYED; OPERATIONAL ACCEPTANCE OPEN**. SEC-005 залишається наступним session-lifecycle task: чинні access JWT/вже видані MFA challenges цим пакетом негайно не відкликаються. SEC-019 OPEN: raw proof короткий час потрібний у pending system outbox; encryption/backup доступ і purge за непрацюючого worker не реалізовані. Нові MFA/production/AI правила залишаються DRAFT у ACCESS_MATRIX_PROPOSAL.md.

Фаза 1 загалом **IN PROGRESS**. Deployment, робочі credentials/session revoke, реальна email-доставка, git commit і PR не виконувалися. Інші незакомічені зміни власника збережено.

## 23.10.2026 — Фаза 1, пакет 3: revocable personal/workspace sessions (SEC-005)

**Дозвіл:** «так. продовжуй роботу над сесіями». **Baseline:** той самий dirty checkout із попередніми локальними пакетами та незакоміченими змінами власника; чужі зміни не скасовано. Root cause підтверджено: 30-day signature-only consumer JWT без refresh/revoke; staff cookie-only logout і tracked non-atomic refresh rotation. Scope/сумісність/SLA/rollback: **TASKS/PHASE-1-06-SESSIONS.md**.

Реалізовано consumer access ≤15 хвилин, окремий hashed refresh на 30 днів, sid/auth-version binding і live DB validation на кожному новому authenticated HTTP request. Staff refresh зберігає абсолютні 7 днів. Atomic rotation/owner locks/partial unique indexes дають одного winner; grace replay → 409 без повторної видачі proof і без cookie deletion. Self logout/current-device/all-device revoke працюють на server session family, а не лише на локальному storage. Password recovery/password/MFA/deactivate/base-role changes інвалідовують стару staff auth version та MFA challenges. Consumer tracked credential/active writes також інвалідовують версію.

MFA enrollment видає нову сесію тільки після перевірки enrollment code, атомарно з recovery codes; web приймає оновлені credentials, тому recovery-code screen не зникає через фоновий refresh старої версії. Mandatory MFA за ролями не ввімкнено. Impersonation прив'язано до parent provider session. POS operator validator/cache залишено окремим чинним механізмом.

Mobile отримав SecureStore personal refresh і незалежний single-flight client. Серіалізація credential writes/cleanup та epoch/proof guards захищають від late refresh після logout і нового account login; personal terminal failure не видаляє workspace. Web має аналогічні epoch guards, обробку rotation 409 та існуючий cross-tab lock. На сайті й обох mobile profile screens додано «Вийти на всіх пристроях» з підтвердженням. Remote failure не оголошується успішним global revoke; offline mobile cleanup позначений як локальний вихід.

### Реальні перевірки пакета 3

| Перевірка | Команда / artifact | Результат |
|---|---|---|
| Backend build | `dotnet build backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal /nodeReuse:false` | PASS, 0 errors; повний rebuild має 8 попередніх warnings поза session scope; incremental run — 0 warnings |
| Auth/authorization/provider/POS regression | Точна команда нижче; `test-results/phase-1-sessions.trx` | **531 passed**, 0 failed/skipped |
| PostgreSQL + справжній HTTP/JwtBearer | `& ./docs/analys/remediation/tests/Test-PasswordRecoveryPostgres.ps1 -Suite Sessions`; `test-results/phase-1-sessions-postgres.trx` | **11 passed**, 0 failed/skipped: 7 session і 4 recovery cases |
| Web API/session contracts | Із frontend: `node node_modules/vitest/vitest.mjs run lib/api.test.ts features/auth/api/passwordRecovery.test.ts --reporter=default --reporter=junit --outputFile=../docs/analys/remediation/test-results/phase-1-sessions-web.xml` | **29 passed**, 2 suites |
| Mobile auth/store/client/profile | Із mobile: `node node_modules/jest/bin/jest.js --runInBand --no-cache lib/__tests__/api-client.test.ts lib/__tests__/personal-sessions.test.ts features/auth/__tests__/store.test.ts features/auth/__tests__/profileScreen.test.tsx --json --outputFile=../docs/analys/remediation/test-results/phase-1-sessions-mobile.json` | **41 passed**, 4 suites |
| TypeScript | Frontend і mobile: `node node_modules/typescript/bin/tsc --noEmit --incremental false` | PASS |
| Web lint | `next lint --file lib/api.ts --file features/auth/api/auth.ts --file features/auth/hooks/useAuth.ts --file components/layout/UserMenu.tsx` | PASS, 0 warnings/errors |
| EF model/snapshot | `dotnet ef migrations has-pending-model-changes --project backend/ShelfGuard.Infrastructure --startup-project backend/ShelfGuard.Api --context AppDbContext --no-build` із synthetic design-only connection на port 1 | PASS: no pending model changes; DB connection/update не виконувався |
| Diff whitespace | `git diff --check --` для source/test files пакета | PASS; тільки repository CRLF conversion notices |

```powershell
dotnet test backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ShelfGuard.Tests.Auth|FullyQualifiedName~ShelfGuard.Tests.Authorization|FullyQualifiedName~RoleControllerFeatureProviderTests|FullyQualifiedName~OperatorTokenValidationTests|FullyQualifiedName~ProviderServiceTests' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=phase-1-sessions.trx' --results-directory docs/analys/remediation/test-results
```

Разом: **612 PASS** (531 + 11 + 29 + 41), не повний test run репозиторію. PostgreSQL/Kestrel tests довели copied-token 401 після commit logout/password/deactivate/revoke-all, незалежність іншого consumer device, foreign/missing sid reject, parent impersonation revoke, один concurrent rotation winner і один recovery-code commit. Migration Down/Up/backfill та попередній password-recovery rollback теж PASS. Runtime production RLS/roles/ingress не перевірені.

Перший mobile run не виконав жодного тесту через sandbox EPERM на rename Jest transform-cache; після дозволеного локального запуску лишилася одна стара assertion про кількість storage keys (4 замість нових 5). Assertion оновлено з перевіркою видалення personal_refresh_token; остаточний JSON — 41 PASS. Vite CJS/jsdom navigation warnings — попередні tooling limitations. Dependencies/lockfiles не змінювалися, зовнішні сервіси не викликалися.

### Міграція, приймання та межі

Згенеровано `20261023151626_AddRevocableIdentitySessions` із Designer/snapshot. Підготовлено schema і metadata backfill SessionId=Id для наявних staff refresh rows; PasswordHash не переписується. Design tooling використовувало synthetic connection, а Down/Up виконувалися лише в одноразовій tmpfs PostgreSQL. Working/staging/production DB не підключалися й не змінювалися; живі сесії/ключі не відкликалися.

Потрібні погоджене migration/backfill window та сумісні API/web/mobile revisions на всіх instances. Legacy sid-less consumer JWT потребує оновленого клієнта та повторного входу. Staff cookie може пройти refresh після backfill; old MFA challenge потребує повторного password step. Не залишати старі signature-only API nodes і не робити blind rollback до 30-day consumer access. Full browser/device/multi-instance/staging/load/RLS acceptance **NOT TESTED**.

Підтверджений локальний SLA — новий HTTP auth check після revoke commit, без positive cache. In-flight HTTP може завершитися; активний consumer websocket живе до JWT expiry (≤15 хвилин), не розривається негайно при revoke. POS sessions мають окремий чинний lifecycle. Retention/purge refresh rows і незалежна revocation кожного impersonation delegation лишаються OPEN. Consumer raw SQL поза EF має явно змінювати AuthVersion при credential/active changes. SEC-019 pending-outbox secrecy і mandatory MFA/production/AI матриця не закриті.

**Статус:** SEC-005 **LOCAL FIX VERIFIED; NOT DEPLOYED; ST-06 LIVE ACCEPTANCE OPEN**. Фаза 1 загалом **IN PROGRESS**. Commit, PR, deployment, production revoke й реальна email-доставка не виконувалися.


## 23.10.2026 — PHASE-1-07: застосування матриці доступу

**Дозвіл власника:** «продовжи над Застосувати матрицю доступу». Таблиці ACCESS_MATRIX_PROPOSAL.md прийнято як основу локальної реалізації; staging/production rollout цим не виконується. Source IDs: SEC-006, SEC-009/F07/F09, SEC-010.

### Зміни

- Централізовано mandatory MFA roles і privileged grants/templates; обмежений 5-хвилинний enrollment proof з окремою audience/purpose/AuthVersion. До OTP немає workspace/refresh. Wrong codes враховуються в lockout; required MFA не вимикається recovery/disable. Web/mobile мають QR/manual-key setup і одноразові recovery codes.
- `MfaVerifiedAt` у refresh session і підписаний `mfa_at`; refresh не оновлює freshness. Live validator відхиляє unverified legacy mandatory/TOTP sessions. Step-up 5 хвилин перед критичними commands; code consumption/audit/session timestamp — одна транзакція. Існуючі consumer/POS PIN flows збережено.
- `WorkAccessContext` та action policies читають tenant, активну роль/template, overrides/grants і user_locations у БД. Додано незалежні production.view/manage/complete/cancel та ai_assistant.use у capability catalog і bounded per-user grants. Direct provider не має tenant production/AI доступу; дозволена delegation залишається scoped.
- Generic order PUT більше не приймає status; start/complete/cancel окремі. Done immutable; repeated complete повертає наявний output; optimistic concurrency і один EF transaction відкидають competing output/stock/consumption. Recipe items перевіряються за tenant, quantities/duplicates. Клієнти використовують start endpoint, окремі action gates та нормалізують status casing. FEFO expiry/cost policy не змінюється.
- AI reads фільтруються до query за незалежними правами й tenant/location; use-only не читає домени. Supplier contacts/batch identifiers/cost не передаються, sales quantity агреговані. Stub payload test перевіряє hidden stores/foreign tenant/PII/cost без зовнішніх model calls.
- Виправлено stale user graph updates: tracked UserRepository.Update та MFA commit не перезаписують незмінені permissions/роль. PostgreSQL-тест паралельного admin revoke підтверджує, що login і MFA не відновлюють відкликане право.
- UI не повторює write після cancellation/identity change; пізній enrollment response після logout не зберігає credentials. Recovery codes залишаються видимими після оновлення профілю до підтвердження користувачем. Прострочена impersonation freshness вимагає повторного provider step-up/delegation.

### Фактичні перевірки

| Перевірка | Результат / звіт |
|---|---|
| Backend build (`--no-restore --disable-build-servers -m:1`) | PASS, 8 попередніх nullable/EF1002 warnings |
| Auth, Authorization, Production, AI Assistant, ProviderService, POS, Users, TenantRoles, OperatorTokenValidation, RoleControllerFeatureProvider | **786 PASS**, 2 DB cases SKIP без fixture; `test-results/phase-1-access-matrix.trx` |
| Disposable PostgreSQL + Kestrel HTTP + migrations + POS stock/loyalty races | **25 PASS**, 0 skips; `test-results/phase-1-access-matrix-postgres.trx` |
| Web Vitest (api/recovery/matrix) | **36 PASS**; `test-results/phase-1-access-matrix-web.xml` |
| Mobile Jest (API/session/store/auth/navigation/MFA dialog) | **64 PASS**, 8 suites; `test-results/phase-1-access-matrix-mobile.json` |
| Frontend/mobile TypeScript `--noEmit --incremental false` | PASS |
| Web lint для змінених auth/production/layout/AI files | PASS, 0 warnings/errors |
| EF pending model changes, synthetic design-only connection на port 1 | PASS: no model changes; DB update не виконувався |
| Scoped `git diff --check` | PASS, тільки CRLF notices |

Разом **911 PASS**, не повний test run. Остаточні звіти замінюють попередні невдалі запуски. Новий UI-тест спершу очікував unmount замість явного підтвердження codes і зависав; сценарій змінено на user acknowledgment, а slow native rendering має 30-секундний тестовий timeout. Веб лишає попередні Vite CJS/jsdom navigation notices.

Перший regression run випадково включив два старі POS integration fixtures з автоматичним fallback до `localhost:5435` dev DB. Вони впали на відсутній AuthVersion; fixture cleanup не повідомив помилок. Міграції dev DB не виконувалися. Обидва fixtures переведено на явний `SHELFGUARD_RECOVERY_TEST_DB` і skip за відсутності цього env; жодного default DB fallback надалі. Фінальний disposable suite створює required pg_trgm/ICU collation, використовує штатний JSON-enabled data source та перевіряє обидві гонки. Перші спроби disposable tests потребували саме цих schema/runtime prerequisites; після виправлення 25/25 PASS. Тимчасові Docker containers/networks очищені.

Відтворення DB suite:

```powershell
./docs/analys/remediation/tests/Test-PasswordRecoveryPostgres.ps1 -Suite Matrix
```

Скрипт створює random loopback-only tmpfs PostgreSQL fixture з випадковим паролем і окремою мережею без outbound masquerading; не читає application connection strings і не переписує вже налаштований fixture env. Повний backend filter і команди клієнтів відповідають suites вище; artifacts містять фактичні результати.

### Міграція та межі

`20261023165833_EnforceAccessMatrix` додає nullable MfaVerifiedAt, із Designer/snapshot; Down/Up перевірені тільки в одноразовій БД. Legacy verified time не backfill-иться з CreatedAt: mandatory/TOTP users підтверджують MFA повторним входом. Потрібні узгоджені API/web/mobile revisions; old production PUT-status clients слід оновити. Всі нові API nodes мають працювати з новою колонкою.

**Production/staging DB і deployments не виконувалися; реальні email/AI services не викликалися.** Staging ingress, реальні RLS roles, browser/device/OTP clock/recovery UX, multi-instance/load і rollout window лишаються OPEN. Чинні policy families поза production/AI не переписувалися; їхній наявний JWT capability propagation строк зберігається. Break-glass identity, linking/SSO, consumer sockets, POS manager override, SEC-019 та фінансові інваріанти фази 2 — окремий scope. Немає нового публічного MFA bypass.

**Статус:** SEC-006/009/010 і F07/F09 — **LOCAL FIX VERIFIED; NOT DEPLOYED; STAGING ACCEPTANCE OPEN**. Фаза 1 загалом IN PROGRESS. Commit/PR не створювалися.


## PHASE-1-08 — спільний HTTP auth pipeline та pre-auth tenant context (23.10.2026)

`TASKS/PHASE-1-08-HTTP-RLS-ACCEPTANCE.md`: винесено production JWT/limiter registration і middleware order у спільний код; виправлено tenant context для pre-auth audit/permissions/profile через transaction-local override з persisted user. Rotation/MFA confirmation не вкладені в override. RLS policies та схема не змінені.

Фінальний build PASS (8 попередніх warnings). Вибрана регресія **816 PASS, 5 SKIP, 0 FAIL**, `test-results/phase-1-http-regression.trx`; включає 2 нові actual Kestrel pipeline cases. Окремі initial 2 PASS у `phase-1-http-pipeline.trx` не додаються вдруге. Unit checks контролюють tenant scope на login/failure/refresh/replay і capability lookup; не заміняють PostgreSQL доказ.

Підготовлено 3 HTTP acceptance cases з усім migration chain та NOSUPERUSER/NOBYPASSRLS runtime role. Їх запуск **PENDING**, Docker engine не відповідає; вони явно пропущені без disposable env. Перша Docker-спроба перервана через зависання; власні CLI завершені, engine cleanup listing недоступний. Доданий 15-секундний readiness probe підтверджено фактичним timeout до fixture creation. Не оголошувати новий RLS flow або весь migration chain перевіреним. Новий report не перезаписує попередні 911 PASS PHASE-1-07. Web/mobile не змінювали й не перезапускали.

Production/staging deployment, робочі DB migrations/DML/session revoke, Docker/WSL reset та зовнішні services не виконувалися. Фаза 1 лишається IN PROGRESS.


## 24.10.2026 — PHASE-1-09 production release / PR #26

Owner authorized server deployment and a GitHub PR. Runtime code `5917a60043e1e1acddfa37722af7632b732007d6` deployed from an isolated release directory; original local/server edits preserved. Verified backup, exact payload hashes, web/API/worker canaries, three auth/session migrations, six healthy services, nginx syntax/reload and local/public HTTPS checks. Runtime DB role is restricted; no customer fiscalize/AI-generation verification calls were made.

Preflight found a working password-based network_manager worker account. Added a dedicated five-minute tenant-bound worker audience for exactly three existing job operations, live DB validation and raw-peer/private-port issuance, preserving human MFA and the existing worker's tenant scope. No global internal cron key or MFA secrets/passwords were provisioned. Rolling migration supports inserts from old binaries.

Final selected checks: **983 backend + 4 actual HTTP/RLS + 25 PostgreSQL/concurrency + 51 web + 15 worker = 1,078 PASS**. Full GitHub CI run 476 passed all jobs, including the new disposable security acceptance gate. Evidence, image/backup hashes, limits and rollout details: `TASKS/PHASE-1-09-SERVER-RELEASE.md`. PR: https://github.com/SmilnitskiyStas/CRMProductSystems/pull/26

Production technical health is verified; full user/device/business acceptance is not claimed. Legacy mobile refresh compatibility requires the updated client. Historical NOT DEPLOYED entries above describe their original dates and are superseded only for the deployed code by this release record. Phase 1 remains in progress for outstanding operational/privacy work.
