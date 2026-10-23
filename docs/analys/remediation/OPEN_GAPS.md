# OPEN_GAPS

Дата: 23.10.2026.

- Audit/current HEAD однаковий: `3563ce6c79596ae010f4baebc920d0685f073876`; checkout містить багато попередніх незакомічених змін. Їх не скасовано, не включено в окремий коміт і не оголошено результатом remediation.
- SEC-001 і 12 IDs фази 1 повторно підтверджені: SEC-002/003/004/005/006/009/010/012/014, F06/F07/F09. Решта 41 findings ще не пройшли current-code revalidation.
- Локальний fix не розгорнуто. Production/staging revision, мережеву доступність та реальні accounts не перевірено.
- Локальні controller/service tests не доводять HTTP middleware/RLS/ingress/client rollout. Повний ST-01/ST-02 staging сценарій NOT TESTED.
- Раніше видані linked access/refresh tokens і MFA challenges не відкликані. Session origin metadata недостатні для надійного автоматичного відбору. Рішення та виконання revoke залишаються відкритими.
- Захищеного explicit linking grant у поточному scope не додано. Mobile dual-session auto-link вимкнено; сумісність персонального входу збережено, обмеження shared identifiers описано в DECISIONS_REQUIRED.md.
- Live DB roles/owners/grants/FORCE RLS, tenant FK completeness, pooling isolation: UNKNOWN.
- Deployed gateway/log sinks, secret/key storage metadata, CI settings, provider/backup/offsite/PITR/restore evidence: UNKNOWN.
- Негативні тести live-сервісів, production fault injection/scanning/load, migrations/DML робочих DB, fiscal/payment operations, credential rotation і deployment не виконувалися. Локальні isolated synthetic tests фази 1, включно disposable PostgreSQL, описані нижче та в CHANGELOG_AUDIT.md.
- Наявні build warnings поза зміненими auth файлами записуються у CHANGELOG_AUDIT.md; їх усунення не входить до фази 0.

Оновлення фази 1:

- SEC-002/F06: local private storage/static deny/authorized web+mobile delivery перевірені синтетичними тестами. Реальні legacy assets не переносилися; CDN/proxy caches/aliases і client-version rollout UNKNOWN.
- SEC-003: source gateway/edge safe access logs та hub failure privacy перевірені в isolated nginx. Deployed WAF/ingress/APM/log sinks і реальні log retention/access UNKNOWN. Raw hub error logging вимкнено; sanitized access metrics збережено.
- SEC-012: actual source middleware order виправлено; PHASE-1-08 shared Kestrel pipeline перевірив subject partition, enrollment audience і hub query-token boundary. ST-13 deployed ingress/multi-instance limits ще NOT TESTED.
- SEC-014: transport tests PASS; packaged terminal/device lifecycle і реальний certificate acceptance NOT TESTED. Існуючі HTTP-конфігурації потребують controlled HTTPS transition без втрати outbox.
- ACCESS_MATRIX_PROPOSAL.md — власник дозволив застосування; локальна реалізація PHASE-1-07. Operational rollout і staging/device/RLS acceptance SEC-006/009/010 та F07/F09 лишаються OPEN до фактичного приймання.
- SEC-004: PHASE-1-05 локально виправлено й перевірено. Proof 15 хвилин, одноразове атомарне підтвердження, email-only delivery, MFA збережено. Prepared migration `20261023001000_AddProofBasedPasswordRecovery` застосовано лише як Down/Up operations у disposable synthetic DB; робочі DB не змінювалися. Повний HTTP/client/email/ST-05 staging OPEN.
- SEC-019 залишається OPEN: raw reset link тимчасово живе в pending system outbox; після terminal dispatch/expiry worker прибирає link, history/error details редагуються. Шифрування outbox, DB/backup доступ і cleanup за вимкненого worker ще не реалізовані; queued proof стає непридатним через 15 хвилин незалежно від доставки.
- SEC-005: PHASE-1-06 локально виправлено й перевірено. Consumer access ≤15 хвилин, окремий hashed refresh на 30 днів, live DB check без positive cache, current/all-session revoke, атомарна rotation, mobile/web race guards. Staff reset/password/MFA/deactivate/role changes invalidates old auth version і MFA challenges. Новий HTTP request після revoke commit → 401 підтверджено через Kestrel/PostgreSQL; live staging/SLA/load ще OPEN.
- Session migration `20261023151626_AddRevocableIdentitySessions` підготовлена; робочі DB не змінювалися. Legacy sid-less consumer JWT вимагають оновленого mobile client і повторного входу; staff cookie може перейти через refresh після backfill. Перехід чинних production сесій і узгоджений rollout усіх API_ROLE потребують окремого operational scope.
- Existing ConsumerSupportHub connection не закривається миттєво при revoke; CloseOnAuthenticationExpiration обмежує життя consumer socket до expiry JWT (до 15 хвилин). In-flight HTTP може завершитися. Негайна distributed socket revocation тут не реалізована; POS operator lifecycle/cache залишено окремим чинним механізмом.
- Offline mobile logout очищує локальні credentials, але без server ACK не доводить відкликання скопійованого токена; UI це повідомляє. Refresh-row retention/purge і незалежне відкликання кожного impersonation delegation ще не реалізовані. Tracked consumer EF writes змінюють AuthVersion; прямі operational SQL updates мають робити це явно.
- Recovery rollout потребує правильного HTTPS `Frontend__BaseUrl` (наявний ключ Compose), міграції та сумісного web/API/worker; реальна доставка та deployed config UNKNOWN. За відсутності/небезпечного URL recovery fail-closed, fallback на інший host немає.
- Фаза 1 загалом не завершена й не прийнята для production. Жодні production session revoke/deploy/DB migrations/DML не виконувалися. Source Compose definitions змінені для нового persistent private volume; recovery використовує наявний `Frontend__BaseUrl`.

PHASE-1-08: 816 PASS / 5 SKIP у вибраній регресії. Pre-auth tenant context для audit/profile виправлено за статичними RLS policies та unit guards. Нові 3 full-migration/real-controller/NOBYPASSRLS cases підготовлені, але НЕ ВИКОНАНІ через Docker timeout. Повний current migration chain і виправлений pre-auth flow під реальною PostgreSQL RLS ще PENDING. Відсутність ресурсів першого перерваного Docker fixture потребує перевірки після готовності engine; cleanup не підтверджено через недоступний listing. Подробиці: `TASKS/PHASE-1-08-HTTP-RLS-ACCEPTANCE.md`.
