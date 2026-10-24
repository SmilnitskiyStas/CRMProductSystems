# PHASE-1-12 — self-service MFA disable

Date: 24.10.2026. Status: DEPLOYED AND VERIFIED; explicitly authorized for every role.

Owner separately confirmed: "Так, дозволити всім ролям" to allowing administrators/managers to disable their own configured MFA after password and unused TOTP/recovery-code proof. Remove the legacy role prohibition; keep proof verification, anti-replay and five-failure/15-minute lockout. No real account is changed by deployment; users perform the verified operation themselves.

A successful disable increments AuthVersion, invalidating old sessions. Return a replacement access/refresh session to the proven client; it has no mfa_at claim. Browser credential/cache updates use the returned user, with late-response guards against restoring credentials after logout. Sensitive-operation step-up remains enforced. Fresh-code guidance explains why a login OTP cannot be reused immediately.

No database migration is required. Validate privileged role matrix, wrong password/code, lockout, real HTTP/RLS disable/re-login/refresh and stale-session rejection before rollout.

Validation so far: 1,000 selected backend tests, five real HTTP/RLS cases, 58 frontend regressions and 20 production API-origin guard checks PASS. Linux API publish and production web build (105 routes) PASS. All 25 PostgreSQL/session/concurrency cases PASS. Total selected checks: 1,108 PASS. GitHub CI run 483 passed all five validation jobs.

## Production release — 24.10.2026, approximately 12:32–12:34 Europe/Kyiv

Application revision `cf73dcb0bec9703bf9048e4433e452d5a8a2b9ed` deployed with API-first rolling update, followed by gateway, frontend and worker. All six application services are healthy; application revision labels match the checked code. Both active release pointers now identify this full release, superseding the earlier frontend-only origin correction. A verified pre-deployment backup and previous runtime images are retained. No new database migrations were applied; existing ingress configuration was unchanged.

Linux web/API canaries, live read-only worker access, public HTTPS health/pages, anonymous workspace/private-file denial and public login API-origin chunk checks passed. The public settings-page bundle matches the tested build and contains fresh-code guidance. Unauthenticated POST to MFA disable returns 401. Positive disable/password/recovery/session behavior was verified only with synthetic accounts under HTTP/RLS; no real user's MFA was changed by verification or deployment.

The browser receives a replacement session immediately after successful disable; existing sessions and old security proofs fail version checks. New sessions carry no MFA timestamp, and critical actions still require MFA. Mobile binaries were not distributed. PR #26 remains open and unmerged; subsequent documentation-only commits do not change runtime code.
