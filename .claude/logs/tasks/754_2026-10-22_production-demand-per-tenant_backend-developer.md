# TASK-754 — Production auto-demand: isolated run per tenant

**Status:** review · **Agent:** backend-developer (+security review of own design) · not committed

## Problem
TASK-752 cron logged in as one service account (WORKER_API_EMAIL) → only that account's tenant was analysed.

## Design
- Worker (`worker/src/jobs/production-demand.job.ts`): enumerates candidate tenants via direct pool
  (`BEGIN READ ONLY; SET LOCAL app.role='worker'`; active tenant, `production` in `Modules` jsonb,
  ≥1 active `Type='production'` location). Then, sequentially, one call per tenant:
  `POST {INTERNAL_API_BASE_URL}/api/internal/production-demand/run {tenantId}` with header
  `X-Internal-Job-Key`. 200 completed / 404,422 skipped / other = failed; each tenant try/catch'ed,
  120 s timeout, per-tenant + total log lines. Old single-account path removed.
- API: `InternalJobsController` (`api/internal`, `[AllowAnonymous]`, `InternalJobKeyFilter`, hidden from
  ApiExplorer, CORE role only). Filter: key from `InternalJobs:Key` (env `InternalJobs__Key`); unset/<32
  chars → 404 (disabled); request with `Authorization` header → 400; missing/wrong key → 401
  (SHA-256 + `FixedTimeEquals`).
- `IProductionDemandTenantRunner` (Application) → `ProductionDemandTenantRunner` (Infrastructure):
  one tx, `SET LOCAL app.tenant_id=<id>; app.role='enterprise_admin'; app.user_id/consumer_account_id=null-uuid`;
  verifies tenant exists (404) / active + `production` module (422 skipped); calls the unchanged
  `ProductionDemandService.GenerateAsync(tenantId, null, null)`; error → rollback, 422.
  `enterprise_admin` only appears in RESTRICTIVE store_scope policies → tenant-wide within the tenant,
  no cross-tenant bypass (worker/provider deliberately not used). tenant_isolation = WITH CHECK.
- `TenantContext`: internal `BindJobTenant(id)` (throws if request has a tenant claim / already bound),
  so `OrderCalcService` (reads `ITenantContext`) behaves identically to the manual run (marketplace
  in-transit term). DI: concrete `TenantContext` scoped, `ITenantContext` → same instance.
- Network: gateway `location ~* ^/api/internal(/|$)` → 404 (case-insensitive, verified incl. `//`, `%69`);
  api_core publishes loopback-only `127.0.0.1:5110` (staging `5111`) for the host-network worker.
- Manual `POST /api/production-requests/auto-generate` unchanged.

## Files
backend: Api/Controllers/InternalJobsController.cs (new), Api/Infrastructure/InternalJobKeyFilter.cs (new),
Application/Features/Production/IProductionDemandTenantRunner.cs (new),
Infrastructure/Services/ProductionDemandTenantRunner.cs (new), Infrastructure/Services/TenantContext.cs,
Infrastructure/DependencyInjection.cs; tests: Production/InternalProductionDemandTests.cs (new),
Infrastructure/ProductionDemandTenantRunnerRlsIntegrationTests.cs (new).
infra: infra/gateway/nginx.conf, infra/gateway/README.md, docker-compose.production.yml,
docker-compose.staging.yml, .env.staging.example. worker: src/jobs/production-demand.job.ts.

## Verification
- `dotnet build -c Release` clean; `dotnet test --filter Production` 90/90; full suite 2827/2827.
- Live-RLS test (SET ROLE rls_audit_test_role): run(A) → draft only in A, B empty; run(B) → only B, A
  untouched (same UpdatedAt); rerun idempotent; no-module tenant skipped, no writes; unknown → NotFound;
  app.tenant_id/app.role empty on the connection after the run.
- worker `npx tsc --noEmit` clean. Gateway `nginx -t` OK + smoke (all /api/internal variants → 404).

## Ops (prod, not deployed)
Set `INTERNAL_JOB_KEY` (`openssl rand -hex 32`) in server `.env` (shared by api_core + worker);
port 5110 must be free on host. Without the key the job logs "skipping" and the endpoint is 404.
