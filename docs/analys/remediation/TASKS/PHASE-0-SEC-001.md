# PHASE-0-SEC-001

- **Source finding:** SEC-001, P0; суміжні SEC-006/007 залишаються окремими findings.
- **Baseline:** `3563ce6c79596ae010f4baebc920d0685f073876`, dirty checkout, 23.10.2026.
- **Owner approval:** «можеш починати редагувати по етапно» — дозволено локальне поетапне редагування. Operational session revoke/deployment не виконано.
- **Revalidation:** CONFIRMED перед змінами: MobileAuthController.Register/Login → FindLinkedUserAsync(phone/email) → AuthService.IssueLinkedMobileSessionAsync(userId) → staff JWT або MFA challenge без staff password.
- **Root cause:** user-declared consumer contact використано як staff authentication proof. ConsumerProfileService дозволяє зміну email; жоден contact не є linking grant.
- **Scope:** controller, staff auth interface/service, mobile response factory/docs, auth regression tests, audit tracking documents.
- **Proposed/final change:** прибрати contact lookup і staff mint/challenge з consumer register/login; видалити passwordless mint API та unused dual-token factory. Залишити consumer response та staff password/MFA path.
- **Compatibility:** HTTP routes, request bodies, personalAccessToken/workspaceAccessToken/user/access поля збережені. Consumer повертає personal token і consumer-only права. Staff-only mobile fallback працює через LoginAsync. Shared identifier входить як consumer; staff `/api/auth/login` лишається окремим credential flow. Новий linking UX не реалізовано.
- **Dependencies/business decisions:** explicit linking UX, раніше видані sessions/challenges та staging rollout — DECISIONS_REQUIRED.md.
- **Tests and actual results:** CHANGELOG_AUDIT.md і test-results/phase-0-auth.trx. ST-01/ST-02 staging acceptance не підміняти локальним PASS.
- **Migration:** не потрібна; schema/data не змінені.
- **Security/privacy:** consumer не може отримати staff token/challenge збігом телефону/email, зміною email, роллю staff/provider/admin або станом MFA. Реальні контакти, tokens і secrets не використовувалися в тестах.
- **Rollback:** не повертати видалений auto-link/mint. За несправності mobile auth обмежити відповідний route на ingress із затвердженим scope або випустити forward fix; для звичайного staff використовувати безпечний credential flow. Не робити blind revert на уразливий artifact.
- **Residual risk:** чинні access/refresh/MFA credentials з попереднього flow; невідомий production state; client/device QA та staging acceptance залишаються відкритими.
- **Status:** локально реалізовано; перевірки у CHANGELOG_AUDIT.md; deployment/operational acceptance OPEN.

## Перевірені mint boundaries

| Path | Proof / status |
|---|---|
| ConsumerAuthService.RegisterAsync/LoginAsync | Consumer JWT only; mobile controller не викликає staff auth у consumer гілці |
| AuthService.LoginAsync | Active user, lockout, staff password, temp-password expiry; optional MFA challenge після password proof |
| AuthService.VerifyTwoFactorAsync | Signed challenge, active/MFA-enabled user, valid TOTP або recovery code; старі challenges не анульовані цим fix |
| AuthService.RefreshAsync | Existing staff refresh credential; provenance старих linked refresh невідомий; atomic rotation окремо SEC-007 |
| ProviderService.ImpersonateAsync | ProviderOnly policy на ProviderController, активний tenant; окремий privileged flow, не змінено |
| RegisterService.StartSessionAsync | Device credential, register/tenant/POS gate, cashier PIN, session-bound operator token; не consumer contact linking |

Точні поточні file:line посилання — CHANGELOG_AUDIT.md. Це перевірка source boundaries, не повний runtime penetration test.
