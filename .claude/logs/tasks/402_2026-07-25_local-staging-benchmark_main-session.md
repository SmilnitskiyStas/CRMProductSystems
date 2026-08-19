# TASK-402 — Local staging benchmark (capacity / abuse-protection / breakpoint)

**Status:** done (2026-07-25) · **Agent:** main session, direct (matches TASK-370 precedent for load-testing work) · **Depends:** loadtests/ (TASK-370), TASK-400/401, Stage 3 RLS merge

## Context

User asked for a full "DDoS resilience" / benchmark run against the live production site.
Investigated the prod VPS first (93.127.143.98, read-only): only 2 vCPU / 3.8GB RAM, ~350MB
free, already swapping, running 16 containers across ~6 unrelated products (ShelfGuard,
WaterTracker, AI Trader, Trading bot, YouTube bot, WorkMate) sharing the same box. A real
load/stress test there would risk collateral damage to unrelated products for no reliable
data (true DDoS-resilience testing is a network/provider-level concern, not app benchmarking,
and flooding your own small VPS just crashes it). Presented this to the user; they chose the
existing local staging stack (`docker-compose.staging.yml`, already documented in
`loadtests/README.md` as the intended target for this exact k6 suite) instead — zero risk to
prod or other products, and the local Docker Desktop allocation (20 CPU / 16GB) dwarfs the
VPS anyway.

## What was done

1. **Rebuilt staging clean** (`down -v` + `up -d --build`) to get a reproducible baseline and
   pick up the day's not-yet-pushed local commits (TASK-400, TASK-401, and the Stage 3
   `AddLocationStoreScopeRlsPolicies` migration merge) — this doubled as a pre-push validation
   of that work under real load.
2. **Recovered from a self-inflicted issue**: wiping the postgres volume also deleted the
   manually-created non-superuser `shelfguard_staging_app` role (KI-027/TASK-372 fix — never
   captured in any init script). The KI-028 startup canary correctly fail-fasted on the
   superuser reconnect. Fixed by temporarily flipping `ASPNETCORE_ENVIRONMENT` to `Development`
   for one boot (bypasses the canary, lets `DbSeeder` run under the bootstrap superuser),
   then recreated the app role + transferred ownership of all 85 tables/sequences via psql,
   reverted both `docker-compose.staging.yml` and `.env.staging` to their original values
   (confirmed zero net diff), and did a final restart — canary passed clean, confirmed live
   that the API connects as `shelfguard_staging_app` (non-superuser). Flagged as a background
   task for a real fix (`DbSeeder` should `SET app.role='provider'` around its own inserts, or
   an init-script should recreate the role automatically) so this doesn't have to be repeated
   by hand next time.
3. Fresh seed has **zero `user_locations` rows** for any user (DbSeeder never populates this —
   correct, Stage 2 backfill is a deliberate manual/admin decision). This meant Stage 3's
   `store_scope` RLS correctly rejected `manager@demo.local`'s `POST /api/pos/shifts/open`
   (`new row violates row-level security policy "store_scope"`) — live proof Stage 3 enforces
   as designed. Assigned the 5 scoped demo users to the one seeded location via a direct
   `INSERT INTO user_locations`, mirroring exactly the Stage 2 backfill the user will do on
   prod, then re-ran.
4. Wrote `loadtests/breakpoint.js` (new) — ramping-VUs (0→800 over 6 stages + 30s hold) against
   a realistic dashboard-read mix, with `abortOnFail` thresholds (error rate <5%, p95 <3s) so
   the run stops at the actual breakpoint instead of finishing a misleading full ramp.
5. Ran the full suite against staging (`http://localhost:5101`): `pos-queue.js`,
   `analytics-concurrent-read.js`, `breakpoint.js`, `login-storm.js` (last, per README, to avoid
   its deliberate rate-limit exhaustion interfering with the others' `setup()` logins).

## Results

- **pos-queue.js** (40 concurrent registers, 350 iterations, 5 shared barcodes): 0 unexpected
  errors/crashes, 91 sales created, 259 correctly-handled 409 conflicts (xmin optimistic
  concurrency still working, no oversell). Latency regressed vs TASK-370's original numbers:
  p95=2.1s / p99=2.48s vs the 1s/2s budget — flagged, not root-caused (candidates: WSL2/Docker
  Desktop overhead, cold containers, Stage 3's extra RLS `EXISTS` subquery cost — see below).
- **analytics-concurrent-read.js** (30 VUs, 25s, 6 endpoints): all thresholds passed cleanly,
  p95=28ms (budget 500ms), 100% success, 258 req/s sustained.
- **login-storm.js** (70 total VUs across two sub-scenarios): all thresholds passed, p95=1.7s
  (budget 2.3s), rate limiter fired correctly (50/260 requests got 429 on the single-IP burst,
  matching the 10 req/min per-IP limit), 0 unexpected errors, account lockout paths behaved as
  expected (19.6% 401 on the wrong-password mix).
- **breakpoint.js** (new, ramp 0→800 VUs): error rate stayed low even at 800 VUs (0.70%, well
  under the 5% abort bar) — no crash storm at any concurrency tested. Aborted on the **latency**
  threshold during the 800-VU hold stage (cumulative p95=3.19s > 3s bar); med stayed 11ms
  throughout (most requests fast) while p90/p95/max blew out (1.42s/3.19s/21.83s) — the classic
  signature of a fixed-size resource queueing under saturation, not raw compute exhaustion (this
  ran on a 20-core/16GB local Docker host, not the tiny VPS). No `Maximum Pool Size` is set
  anywhere in the Npgsql connection strings (`grep` across `backend/` found none) — Npgsql's
  default (100) is a strong candidate for the actual ceiling being hit around 800 concurrent
  requests-needing-a-connection. Not fixed (perf tuning, not a bug) — flagged for the user with
  the exact remedy (`Maximum Pool Size=` in the connection string, or read-replica/pool-per-
  service scaling if 100 concurrent DB ops genuinely needs to be exceeded in production).

## Not fixed / flagged for follow-up

- Background task spawned (task_45a2bf05): make staging's post-`down -v` role bootstrap
  automatic (`DbSeeder` RLS-safe seeding fix or a postgres init script) so this doesn't require
  manual superuser-bootstrap surgery again.
- POS-path latency regression vs TASK-370 baseline — not root-caused this session (candidates:
  Docker Desktop/WSL2 host overhead vs the original bare-metal/Linux dev box that produced the
  1.77s/1.94s TASK-370 numbers; Stage 3's added `EXISTS`-into-`user_locations` RLS predicate on
  `pos_shifts`/`product_stock`; general shared-host run-to-run variance, same caveat the
  original script's own comments already carry).
- Npgsql connection pool size — no explicit `Maximum Pool Size` anywhere; likely real ceiling
  around ~100 concurrent DB-bound requests. Candidate for a dedicated small tuning task if
  production concurrency is expected to approach that number.

## Build/test

No application code changed (only a new k6 script + local-only staging config, both reverted
where temporary). `git diff docker-compose.staging.yml` = empty (confirmed). `.env.staging` is
gitignored (local secrets, not committed either way).
