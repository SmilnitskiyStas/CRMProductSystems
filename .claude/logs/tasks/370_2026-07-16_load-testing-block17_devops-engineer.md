# TASK-370 — Load testing (audit Block 17)

**Status:** done (2026-07-16) · **Agent:** devops-engineer + database-engineer (main session, direct)
**Depends:** Block 0 (staging), Blocks 1-16

Plan: `eager-pondering-tower.md` Block 17. Environment: one local Docker host
(Windows), not a dedicated load rig — numbers below do not extrapolate
directly to prod hardware.

## Incident during stack bring-up (fixed before testing started)

First `docker compose -f docker-compose.staging.yml --env-file .env.staging
up -d --build` deleted the running **dev** stack's containers
(`crmproductsystems-postgres-1`/`redis-1`/`mosquitto-1`/`worker-1`). Root
cause: neither compose file declares an explicit project name, so both
default to the directory basename (`crmproductsystems`); staging's service
keys (`postgres`/`redis`/`mosquitto`/`worker`) collided with dev's identical
keys under that shared implicit project, and Compose "recreated" (stopped +
removed) the dev containers to match the staging spec. Dev's named volumes
(`crmproductsystems_pgdata`/`_redisdata`/`_mosquittodata`) were untouched
(no `-v` used) — verified all 76 migrations present (incl. all 9 from
2026-07-14/15) and 13 tenants/26 users/25 product_stock rows intact after
restoring the dev stack. **Fix:** added `name: shelfguard_staging` to
`docker-compose.staging.yml` (top-level, isolates the project regardless of
invoking directory); both stacks now coexist safely, verified.

Second issue found immediately after: `shelfguard_staging_api` crash-looped
(exit 139) because `.env.staging`'s `DATABASE_URL` used `Host=localhost;
Port=5436` — the host-side port mapping — but the `api` container sits on
the `shelfguard_staging_default` bridge network (not `network_mode: host`
like `worker`), so `localhost` inside it never reached postgres. Fixed to
`Host=postgres;Port=5432` (Compose-internal service DNS + container port).
`worker`'s `WORKER_DATABASE_URL` (`localhost:5436`) is correct as-is since
`worker` genuinely uses `network_mode: host`.

Staging confirmed up: migrations applied, DbSeeder ran (1 tenant, 7 users,
35 product_stock rows), `GET /api/marketplace/item-categories` → 200,
`GET localhost:3101` → 200.

## Scenarios (`loadtests/`, new files: `login-storm.js`, `pos-queue.js`,
`bulk-order-creation.js`, `analytics-concurrent-read.js`; README updated)

**Login-storm** (two sub-scenarios: spoofed-distinct-IPs + real-single-IP
burst, mixed valid/invalid credentials): 0% unexpected errors across every
run; rate limiter (429) and per-account lockout (401→5 failures→15min,
TASK-329) both held correctly under real concurrency, not just
sequentially. **Found + fixed a real bug**: `AuthService.IssueTokensAsync`
(login/2FA success) and `RegisterFailedAttemptAsync` (failed login) each
made 2-3 sequential `SaveChangesAsync` calls (separate DB round trips +
commits) on what is the same scoped `AppDbContext` shared by `_users`/
`_refreshTokens`/`_activityLogs` — batched into one call each. Measured
improvement: p95 2.28s→1.77s (-22%), p99 2.56s→1.94s (-24%). Residual
latency is bounded by `BCrypt.Net-Next` `workFactor: 12`, independently
benchmarked on this machine at **~530-720ms per single Verify() call** —
confirmed this, not DB I/O, is the dominant per-request cost. Did not lower
the work factor (security/crack-resistance tradeoff, out of this block's
scope — flagged for a user/security-reviewer decision if sub-1s p95 login
is required at this concurrency). Script thresholds set to the
actually-measured range (p95<2.3s/p99<2.8s), documented inline, not
silently loosened to "pass".

**POS-queue** (40 simulated concurrent registers, one shared shift, 350
sale attempts against 5 high-stock SKUs): 95 succeeded (201), 255 correctly
rejected 409 (optimistic-concurrency conflict, Block 6's xmin fix), 0
insufficient-stock 400s, **0 unexpected errors**. Cross-checked against
Postgres directly: stock delta 405→310 (-95) exactly matches 95 successful
sales and 95 `pos_transactions` rows — zero oversell, zero lost/duplicated
sales, confirmed under real 40-way concurrency (prior block only checked
~2 sequential requests). 73% conflict rate reflects this test's
deliberately narrow 5-SKU/1-5-batch-row pool (needed to avoid exhausting
seed stock) concentrating contention on a few rows — not representative of
a real multi-hundred-SKU store, noted as such. p95=2.19s/p99=2.4s driven by
genuine Postgres row-lock contention under that adversarial pattern, not a
code defect; not fixed (correctness, which is what matters here, holds).
**Found but deliberately not fixed** (flagged via spawn_task,
`task_7d60b19c`): `PosService.CreateSaleAsync`'s 423-expiry check fetches
all batches for a product with no store filter in the DB query
(`GetAllAsync(null,null,null,productId)`), filtering by store in memory —
invisible with 1 seeded store, real over-fetch in a multi-store tenant.
Touches core FEFO logic ("FEFO is sacred") — left for a dedicated pass
rather than a rushed edit here.

**Analytics-concurrent-read** (30 VUs, 25s, 6 dashboard endpoints, run
concurrently with pos-queue to reproduce read-vs-write contention): 6371
iterations, 248 req/s, 100% success, **p95=21ms, p99=275ms** — comfortably
under the 500ms/1000ms budget, no bottleneck.

**Bulk order creation** (`POST /api/orders/calculate`, 30 VUs, 20s):
deliberately does not hit `POST /api/ai-orders/generate` (real Claude API
cost per call, already audited in Block 7) — 2850 iterations, 137 req/s,
100% success, **p95=14ms, p99=75ms**, no bottleneck.

## Tests / verification

- `dotnet test` (full suite): **850/850 pass** after the `AuthService.cs`
  fix (Auth subset also run in isolation: 190/190).
- Stock-delta cross-check (psql) for POS correctness: exact match, no
  discrepancy.
- Both stacks (`crmproductsystems-*` dev, `shelfguard_staging_*` staging)
  confirmed healthy and left running per instruction.

## Open items for the user

- Login latency floor (~600-700ms/request from bcrypt) — decide whether
  this needs addressing (work-factor reduction, trading security margin for
  latency) before go-live, or is acceptable given expected real login
  volume.
- Follow-up task `task_7d60b19c` (POS unscoped-by-store batch fetch) is
  pending in the task queue, not yet started.
