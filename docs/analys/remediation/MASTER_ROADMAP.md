# MASTER_ROADMAP

Дата: 23.10.2026. Джерело: `../AGRUSYSTEMS_Master_Remediation_TZ.md`.
Дозвіл власника: поетапне редагування та продовження по плану, включно роботою над сесіями. Фаза 0 локально перевірена; реалізовано пакети 1–3 фази 1: private files/logs/limiter/POS, password recovery та revocable sessions. Власник дозволив застосувати ACCESS_MATRIX_PROPOSAL.md; локально реалізовано PHASE-1-07 (mandatory MFA, production/AI permissions).

| Фаза | Scope | Залежності / gate | Статус |
|---|---|---|---|
| 0 | SEC-001: прибрати contact-based staff token/challenge issuance | Consumer login/register тільки personal; staff password/MFA regression; fail-closed rollback | Локальна реалізація; фактичні перевірки у CHANGELOG_AUDIT.md; deployment не виконано |
| 1 | Private assets; token logs; recovery; session revoke/MFA; production/AI permissions; limiter; POS HTTPS | Інвентар файлів/клієнтів; rights matrix; MFA/session policy; окремі малі fixes з тестами | IN PROGRESS: SEC-002/F06, SEC-003/004/005/012/014 локально виправлені; PHASE-1-07 локально реалізовано; staging/legacy-session transition і live SLA ще OPEN |
| 2 | Receipt envelope, time/price/tax/bonus, fiscal status, stock allocation, FEFO, durability, returns/retry | Рішення про bonus/tax/business-day/returns/FEFO; backward compatibility; synthetic reconciliation | PLANNED |
| 3 | Tenant FK/grants, worker identity, rate budgets/egress/secrets, Redis/MQTT/container/CI/audit/DR | Зовнішні operational metadata; staged migrations; RPO/RTO; restore target | PLANNED |
| 4 | Метрики, historical COGS, daily_sales reconciliation, SQL aggregates, CI gates | Фінансові інваріанти фази 2 і погоджена cost policy | PLANNED |
| 5 | Sales outbox/inbox, workload isolation, tenant routing, planogram offline lifecycle | Виміряні SLO/load і стабільний ledger; не розривати sale+stock+loyalty transaction | PLANNED |

Критичний шлях: SEC-001 containment → staging та рішення щодо сесій/зв'язування → доступ і приватність → фінансові правила → інваріанти → reconciliation → еволюція архітектури.
Незалежні operational роботи можуть плануватися поряд, але не вважаються виконаними без доказів.

Для кожного наступного fix: повторно підтвердити finding на поточному коді, визначити root cause/duplicates, підготувати мінімальний diff, сумісність, тести, migration/rollback і фактичний звіт. ID збережені в MASTER_FINDINGS.md.

Staging/canary: лише ізольовані synthetic identities; перед релізом перевірити фактичний ingress/API revision, consumer login/register, staff password і MFA, перехід personal/workspace. Власник визначає rollout window та спостереження за відмовами входу.
Розгортання, live probes, зміни/відкликання сесій і ключів потребують окремого operational scope. Не відновлювати contact auto-link при rollback; за проблем вимикати відповідний mobile route або використовувати попередній безпечний artifact.

PHASE-1-08: shared HTTP security pipeline та pre-auth tenant context реалізовано; 816 local checks PASS. PostgreSQL/full-migration acceptance підготовлено, але PENDING через Docker engine timeout. Наступний крок — `-Suite Http`, потім `-Suite Matrix` після готовності Docker; production/staging gate не пройдено. Деталі: `TASKS/PHASE-1-08-HTTP-RLS-ACCEPTANCE.md`.

24.10.2026: phase-0/phase-1 code deployed as runtime `5917a600`; 1,078 selected checks PASS, full CI acceptance gate PASS, production backup/migrations/health verified. PR #26 open for review. Phase 1 remains IN PROGRESS for device/client/business acceptance and outstanding operational/privacy scope. See `TASKS/PHASE-1-09-SERVER-RELEASE.md`.
