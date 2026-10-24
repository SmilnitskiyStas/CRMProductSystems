# PHASE-1-10 — MFA settings reminder and optional enrollment at login

Date: 24.10.2026, Europe/Kyiv.
Status: DEPLOYED AND VERIFIED — optional enrollment for all roles, including administrators and managers, explicitly confirmed by the owner.

Owner requested that accounts without configured MFA enter the app normally and receive a recommendation to enable two-factor authentication in account settings. Accounts that already enabled MFA must still verify the factor at login. Critical-operation step-up remains in place.

Prepared:
- Non-blocking Ukrainian/English web reminder linking directly to /settings-user#security.
- Mobile workspace reminder linking to profile settings, plus a voluntary MFA setup action in the profile.
- Reminders disappear when the authoritative account profile reports MFA enabled; consumer identities without a supported MFA settings flow are excluded.
- Regression checks for reminder visibility, localization, navigation, and manual setup.

Owner explicitly confirmed: "Так, для всіх ролей без налаштованої MFA". Password login, refresh and bearer validation now admit unenrolled sessions for every role. They do not mint an MFA verification claim. Once MFA is enabled, both the configured-factor login challenge and verified-session requirement remain mandatory. Sensitive-operation recent-MFA checks remain unchanged. Existing restrictions on disabling MFA for privileged accounts remain unchanged.

Validation: 991 selected backend tests PASS; four real-controller/full-migration/RLS HTTP checks PASS; 25 isolated PostgreSQL/session/concurrency checks PASS. 21 web and 12 mobile UI tests PASS; frontend/mobile TypeScript and Linux API publish PASS. Total selected checks: 1,053 PASS, without double-counting repeated runs. Production web build PASS (105 routes). GitHub CI run 478 passed all five validation jobs; production-deploy job is skipped for PR events. No database migration or credential change is required for this follow-up.

Local build preparation recovered a stale generated standalone dependency link by moving that artifact aside, installing dependencies independently with the CI-required npm 11, and copying the identical-lockfile install back to the original checkout without deleting source files. Original dependencies are restored; the worktree now builds with its own regular node_modules directory. Web build retains the existing next-intl environment fallback notices and completes successfully.

## Deployment — 24.10.2026, approximately 11:16–11:18 Europe/Kyiv

Application revision: `85d3191bad51e2c399d97a1e27567a50abee9768`, branch `codex/security-access-matrix`, PR #26. Later documentation-only commits do not change the deployed application code.

Production Linux web/API canaries, verified backup, per-service health gates and final HTTPS checks passed. All six application services are healthy; their application revision labels match the checked code. Public pages return 200, anonymous workspace requests return 401, and the private-contract URL probe returns 404. The public dashboard layout bundle exactly matches the local tested build and contains the non-blocking reminder/settings link. The live worker can read its queue and is denied staff workspace access. No fiscalization, AI generation or customer notification was invoked by verification.

No new database migrations were applied. Existing ingress configuration was identical, so it required no replacement or reload. Previous release images and the verified pre-deployment backup remain available. Disposable test/canary containers were removed. User/device end-to-end sign-in was exercised with synthetic identities in isolated HTTP/RLS tests; no real customer login was performed. Mobile source/UI checks pass, but mobile binaries were not distributed.

PR stays open for review and is not merged into main.

Follow-up: [PHASE-1-11](PHASE-1-11-PUBLIC-API-ORIGIN.md) corrected the frontend build API origin and supersedes this report for the current web image. API/worker and the approved optional-MFA policy remain unchanged.
