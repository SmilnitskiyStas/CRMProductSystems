# TEST_MATRIX

Дата: 23.10.2026. Вихідні T/ST плани залишаються в audit ZIP. Нижче збережено ID усіх findings.
Для неперевірених findings actual/result/evidence = NOT TESTED; план не є виконаним тестом.
Локальні auth unit/controller тести використовують mocks і синтетичні дані, без мережі або БД.
Фаза 1 додатково використовує InMemory DB, тимчасові synthetic files, disposable nginx без зовнішньої мережі/портів, власний loopback HTTP test server та окрему disposable PostgreSQL з random loopback port. Це не runtime-доказ production/staging.
Результати фази 0 та точні команди — у CHANGELOG_AUDIT.md. Staging ST-01/ST-02 повністю не виконувалися.

| Finding | Source test / verification | Expected | Actual / result / evidence |
|---|---|---|---|
| F01 | T03, T04; test-plan.md; Offline продати по 100, змінити каталог на 120 та версію промо, синхронізувати; сума прийнятої оплати має зберегтися, відмінність має потрапити в контроль розбіжностей. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F02 | T05; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F03 | T06, T18; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F04 | T06; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F05 | T09; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F06 | T20; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | LOCAL PASS: 26 file/guard cases + shelf/contract/floor-plan regressions in backend 541; web 28; mobile 17. phase-1-security.trx / phase-1-web-assets.xml; full T20/ST-03 staging OPEN |
| F07 | T13; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | OPEN / NOT FIXED: matrix DRAFT; new state-command acceptance NOT TESTED |
| F08 | T14; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F09 | T13; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | OPEN / NOT FIXED: matrix DRAFT; new production rights acceptance NOT TESTED |
| F10 | T08; test-plan.md; Прямий staging API: один product з запасом 5, два barcode-рядки того самого product по 4. Очікується атомарна відмова без чека/рухів/бонусів. Статичний контрприклад, не виконаний. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F11 | T07; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F12 | T11; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F13 | T01; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F14 | T19; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F15 | T17; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F16 | T18; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F17 | T10; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F18 | T18, T23; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F19 | GAP: у source test-plan немає прямого F-ID mapping; додати при revalidation; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F20 | T22; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F21 | T23; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F22 | T07, T12; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F23 | T03, T21; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| F24 | T20; test-plan.md; Див. test-plan.md; запуск не погоджено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-001 | SECURITY_TEST_PLAN.md; ST-01/ST-02: тільки synthetic identities на isolated staging; consumer registration/profile/login не видають чужий workspace token; legitimate linking проходить лише за сильного підтвердження. | Consumer proof never grants staff token/challenge; staff password/MFA still work | LOCAL PASS: 438 auth tests, 33 mobile cases; test-results/phase-0-auth.trx; full ST-01/ST-02 staging NOT TESTED |
| SEC-002 | SECURITY_TEST_PLAN.md; ST-03: synthetic files tenant A не читаються anonymous/B; short URL expiry, store/party permissions і deletion працюють. | Деталі в початковому плані; уточнити після revalidation | LOCAL PASS: private files tests in backend 541; web 28; mobile 17. Legacy migration/cache/device acceptance ST-03 OPEN |
| SEC-003 | SECURITY_TEST_PLAN.md; ST-04: synthetic token marker не з’являється у жодному log sink; Hub auth та unauthorized group denial зберігаються. | Деталі в початковому плані; уточнити після revalidation | LOCAL PASS: isolated gateway+edge syntax and synthetic hub failure log privacy; phase-1-nginx.json. Real deployment sinks ST-04 OPEN |
| SEC-004 | SECURITY_TEST_PLAN.md; ST-05: anonymous reset request не змінює PasswordHash/чинні sessions; grant single-use/expiry, доставка fault не блокує старий login. | PHASE-1-05: backend auth/controller + disposable PostgreSQL concurrency/rollback/migration + web proof/session + worker redaction | LOCAL PASS: backend regression 513, PostgreSQL 4, web 30, worker 8; phase-1-password-recovery*.trx/xml. Реальний HTTP/client/email/ST-05 staging NOT TESTED; SEC-019 OPEN |
| SEC-005 | SECURITY_TEST_PLAN.md; ST-06: local logout/emergency revoke/deactivate блокують synthetic copied token в погодженому revocation SLA; rotate/replay tests. | PHASE-1-06: next HTTP request після revoke commit → 401 без cache delay; single-use rotation, explicit identity/device scope | LOCAL PASS: backend 531; disposable PostgreSQL/Kestrel 11 (7 session + 4 recovery); web 29; mobile 41; phase-1-sessions*.trx/xml/json. Live staging/SLA/load/active socket immediate revoke NOT TESTED; sockets expire ≤15 min |
| SEC-006 | SECURITY_TEST_PLAN.md; ST-07: всі token issuance paths для privileged user без enrolled MFA обмежені enrollment, а не повним workspace access. | Деталі в початковому плані; уточнити після revalidation | OPEN / NOT FIXED: matrix DRAFT; new mandatory MFA acceptance NOT TESTED |
| SEC-007 | SECURITY_TEST_PLAN.md; ST-08: паралельна rotation одного synthetic token створює один допустимий successor/family outcome; reuse поза grace відкликає family. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-008 | SECURITY_TEST_PLAN.md; ST-09: tokens не persist у JS-readable storage, CSP не ламає hydration, cookie/refresh і API authorization працюють. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-009 | SECURITY_TEST_PLAN.md; ST-10: cashier/merchandiser без grant не змінюють order; Done transitions лише через valid complete, повтор не створює випуск. | Деталі в початковому плані; уточнити після revalidation | OPEN / NOT FIXED: matrix DRAFT; new production command/rights acceptance NOT TESTED |
| SEC-010 | SECURITY_TEST_PLAN.md; ST-11: synthetic low-privileged callers не отримують і не передають моделі поля поза дозволеним scope; model stub дає перевірити payload без paid call. | Деталі в початковому плані; уточнити після revalidation | OPEN / NOT FIXED: matrix DRAFT; authorized model-payload acceptance NOT TESTED |
| SEC-011 | SECURITY_TEST_PLAN.md; ST-12: isolated low-rate synthetic budget checks; load тільки за окремим resource ceiling; DB/AI counters обмежені і інші tenant мають SLO. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-012 | SECURITY_TEST_PLAN.md; ST-13: два authenticated users за одним IP мають окремі subject budgets; один user із різних IP має спільний subject budget. | Деталі в початковому плані; уточнити після revalidation | LOCAL PATCH/build/regressions PASS; source Authentication precedes RateLimiter. Actual HTTP subject partition ST-13 NOT TESTED |
| SEC-013 | SECURITY_TEST_PLAN.md; ST-14: у контрольованому staging spoofed forwarding headers не змінюють trusted client key; actual edge config review окремо. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-014 | SECURITY_TEST_PLAN.md; ST-15: remote http URL відхиляється, валідний HTTPS працює, invalid certificate fail-closed; dev loopback exception протестовано окремо. | Деталі в початковому плані; уточнити після revalidation | LOCAL PASS: POS transport 16 cases, phase-1-pos-http.xml; TS PASS. Packaged device/certificate/ST-15 staging OPEN |
| SEC-015 | SECURITY_TEST_PLAN.md; ST-16: disposable terminal profile, file permissions/encryption review, tamper/restore detection і power/disk tests лише після окремого дозволу. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-016 | SECURITY_TEST_PLAN.md; ST-17: synthetic ledger fixtures, duplicates/returns/concurrency/bonus/fiscal failure з stub provider; canonical sums і allocation узгоджені. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-017 | SECURITY_TEST_PLAN.md; ST-18: використання простроченого/повторного/чужого approval відхиляється; legitimate assisted flow не блокується зайвими діями. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-018 | SECURITY_TEST_PLAN.md; ST-19: fake isolated egress sink не отримує shared key для непогодженого host; internal destinations/redirects блокуються без реальних metadata calls. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-019 | SECURITY_TEST_PLAN.md; ST-20: DB/backup metadata/fixtures не містять readable TOTP/integration/живих reset secrets без key access; mask і notification history лишаються коректними. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-020 | SECURITY_TEST_PLAN.md; ST-21: role metadata та synthetic operations доводять runtime no-DDL, default deny без tenant, least-privilege job scopes. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-021 | SECURITY_TEST_PLAN.md; ST-22: foreign tenant parent/child insert/update заборонений DB independent від UI; restore/import paths теж перевірено. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-022 | SECURITY_TEST_PLAN.md; ST-23: local fixture Redis TLS/ACL приймає лише належний username/cert; rediss не деградує до plaintext. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-023 | SECURITY_TEST_PLAN.md; ST-24: synthetic device A не publish/subscribe topic B; unknown device denied; replay/time/schema controls перевірені. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-024 | SECURITY_TEST_PLAN.md; ST-25: inspect effective UID/caps/mounts/network і SBOM у staging, write paths/health/recovery працюють без root. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-025 | SECURITY_TEST_PLAN.md; ST-26: workflow policy review + sandbox unsigned artifact reject, secret canary detection і required checks; жоден deployment не виконувати як audit. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-026 | SECURITY_TEST_PLAN.md; ST-27: synthetic security events доставляються в незалежний sink; app/worker не може змінити/видалити їх; alert і tabletop IR перевірені. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-027 | SECURITY_TEST_PLAN.md; ST-28: disposable restore target, masked/synthetic dataset, measured RPO/RTO, integrity і no-cross-tenant export; tabletop host/ransomware/provider loss. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-028 | SECURITY_TEST_PLAN.md; ST-29: stub provider captures synthetic payload лише дозволених полів; injection dataset не отримує нових прав/даних; excerpt redaction/retention і approval перевірені. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-029 | SECURITY_TEST_PLAN.md; ST-30: inert mismatched image fixtures відхиляються, великий pixel count bounded, штатні PNG/JPEG працюють; жодних malicious exploit files. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |
| SEC-030 | SECURITY_TEST_PLAN.md; ST-31: synthetic contact/secret markers не присутні в низькодоступних logs/API errors; потрібні event IDs і діагностика збережені. | Деталі в початковому плані; уточнити після revalidation | NOT TESTED |

## PHASE-1-07 — матриця доступу

- Обов’язкові MFA ролі / privileged templates / active-vs-expired/revoked grants.
- Password-only enrollment без full access/refresh; audience/purpose/version isolation; wrong OTP lockout, one-use codes, required-disable deny.
- Refresh не подовжує mfa_at; step-up має 5-хвилинну межу й відхиляє future claims; одночасне використання одного OTP має один commit.
- Kestrel HTTP: enrollment/missing verified claim/legacy session відхиляються; старий JWT capability не обходить live role removal; view не дає complete; нова privileged permission вимагає нового MFA login.
- Production: generic status update deny; explicit start; Done immutable; complete-only grant; tenant/live assignment isolation; replay повертає одну output batch; competing completion rollback.
- AI: use-only не робить жодного domain query; stub payload лише assigned store/tenant; supplier contacts і cost відсутні.
- POS stock/loyalty races тільки у disposable fixture; migration Down/Up зберігає credentials, legacy verified time nullable.
- Web/mobile: proof не є access token; step-up retry один раз; cancel/logout/ordinary permission deny не повторює write; навігація та capability незалежність.

Звіти: `test-results/phase-1-access-matrix.trx`, `phase-1-access-matrix-postgres.trx`, `phase-1-access-matrix-web.xml`, `phase-1-access-matrix-mobile.json`. Повний staging/browser/device/production-RLS acceptance лишається відкритим.


### PHASE-1-08 — фактичний shared HTTP pipeline та pre-auth scope

- **816 PASS / 5 SKIP / 0 FAIL**, `test-results/phase-1-http-regression.trx`. Вибрані suites PHASE-1-07 + TenantConnectionInterceptor + нові acceptance cases; два POS та три HTTP/PostgreSQL cases явно skip без disposable env.
- Shared Kestrel/JWT/limiter: per-user budget за однакової IP, enrollment audience deny, non-hub query bearer deny, hub query bearer allow. Session validator у цих двох pipeline tests підмінений.
- Unit guards: persisted-user tenant для login/failure audit, capability lookup, refresh profile/replay audit; token rotation не має вкладеної tenant transaction.
- **PENDING:** full migration chain, actual AuthController enrollment/OTP/cookie/refresh/logout, capability-only staff response під NOSUPERUSER/NOBYPASSRLS. Docker engine readiness timeout; це не PASS. Запуск: `tests/Test-PasswordRecoveryPostgres.ps1 -Suite Http`. Після нього повторити `-Suite Matrix`.
- TLS/ingress/browser/device/deployed sessions не перевірено.


### PHASE-1-09 — deployed release evidence (24.10.2026)

983 backend + 4 full-chain/controller/MFA/worker/RLS + 25 PostgreSQL/concurrency + 51 web + 15 worker = **1,078 PASS**, with no double counting of repeated runs. Two DB cases skip the ordinary regression and pass the disposable suite. GitHub CI run 476 passes all five check jobs, including dedicated Http/Matrix acceptance. Production Linux web/API/worker canaries, exact image labels, three migrations, backup parse, nginx syntax and local/public HTTPS negative/health checks PASS. No real fiscalize/paid-AI calls or device distribution were tested. The former PHASE-1-08 Docker/PENDING status is superseded by the successful server-isolated and CI runs. Full evidence: `TASKS/PHASE-1-09-SERVER-RELEASE.md`.
