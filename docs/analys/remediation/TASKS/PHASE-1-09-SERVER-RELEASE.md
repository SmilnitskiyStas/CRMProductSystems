# PHASE-1-09 — server release and GitHub PR

Date: 24.10.2026, Europe/Kyiv. Owner authorized server deployment and a GitHub PR.
PR: https://github.com/SmilnitskiyStas/CRMProductSystems/pull/26
Status: PREPARING; production cutover has not yet run.

The release branch starts at current main 4159e520 and preserves the seven purchase-history source/migration files already deployed on the server. Unrelated local UI, mobile content and configuration work remains in the original checkout. Production images are prepared from versioned Linux API output, the production Next build and worker sources; secrets, uploads and generated reports are excluded from the source commit.

Preflight found that the configured worker successfully logs in as a tenant network_manager every five minutes. Mandatory human MFA would break the existing fiscalization retry job. The release therefore uses a separate machine-purpose grant: configured identity only, existing password proof, tenant-bound :worker audience, five-minute expiry, live active-user/tenant/role/AuthVersion/lockout checks. Exactly three operations accept it: read pending fiscalization, retry an existing fiscalization and generate an AI-order suggestion. It has no staff refresh cookie, sid or MFA-verification claim and cannot access ordinary workspace endpoints. Human login still requires MFA. No new internal cron shared key is created or enabled.

The grant endpoint is under /api/internal (blocked by the public gateway). It accepts the captured TCP peer before ForwardedHeaders: loopback, or explicitly enabled Docker host-bridge access for the host-network worker. Client-provided forwarding headers do not establish this boundary. Worker credentials are sent only to the loopback internal URL. Long jobs renew grants without changing tenant; AI-order enumeration stays in the signed tenant.

Session migration gives old binaries a generated session UUID when they omit SessionId during rolling deployment, preserving the unique active-session index without login collisions. Existing rows retain the Id-based backfill. No new user MFA secrets/passwords are provisioned.

Validation includes local backend/worker tests, actual controller/MFA/worker token tests under NOSUPERUSER/NOBYPASSRLS, all migrations on a fresh disposable PostgreSQL, old-writer inserts, and the existing PostgreSQL/session/stock/loyalty races. CI now runs the disposable Http and Matrix suites and gates future production deploys on them. Deployment evidence and final counts will be recorded after cutover.

Operational sequence: exact payload hashes; old runtime image tags; isolated Linux web canary; private pg_dump backup fully parsed without applying it; API canary and three additive auth migrations; read-only worker credential/route checks; named-service rolling update; nginx syntax check/reload; local and public HTTPS health/revision checks. Original server checkout and unrelated local changes are preserved. Verification does not invoke fiscalize, AI generation, paid providers or customer notification endpoints.

Legacy consumer mobile clients require the updated refresh implementation; privileged users must sign in again and enroll MFA when needed. Mobile binaries are not distributed by this server release. Broader phase-3 worker identity isolation, external ingress/logging evidence and full device/production-user acceptance remain separate work.
