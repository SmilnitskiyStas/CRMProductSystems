# PHASE-1-12 — self-service MFA disable

Date: 24.10.2026. Status: IN PROGRESS; explicitly authorized for every role.

Owner separately confirmed: "Так, дозволити всім ролям" to allowing administrators/managers to disable their own configured MFA after password and unused TOTP/recovery-code proof. Remove the legacy role prohibition; keep proof verification, anti-replay and five-failure/15-minute lockout. No real account is changed by deployment; users perform the verified operation themselves.

A successful disable increments AuthVersion, invalidating old sessions. Return a replacement access/refresh session to the proven client; it has no mfa_at claim. Browser credential/cache updates use the returned user, with late-response guards against restoring credentials after logout. Sensitive-operation step-up remains enforced. Fresh-code guidance explains why a login OTP cannot be reused immediately.

No database migration is required. Validate privileged role matrix, wrong password/code, lockout, real HTTP/RLS disable/re-login/refresh and stale-session rejection before rollout.

Validation so far: 1,000 selected backend tests, five real HTTP/RLS cases, 58 frontend regressions and 20 production API-origin guard checks PASS. Linux API publish and production web build (105 routes) PASS. PostgreSQL/session matrix is running.
