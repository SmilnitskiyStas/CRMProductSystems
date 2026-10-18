# Production deploy checklist — resilience PR (stages 1, 2, 3b)

PR #10 bundles: health endpoints + health-gated `deploy.sh` (stage 1), Polly isolation (stage 2),
and the API split into `core` / `consumer` / `provider` behind an nginx gateway (stage 3b).
No EF migrations and no schema changes are included.

> **Merging to `main` deploys to production automatically** (`.github/workflows/ci.yml`: push to
> `main` → all 4 CI jobs green → SSH → `bash deploy.sh`). So everything in section A must be done
> BEFORE the merge button, not after.

Staging evidence (2026-10-17, see `.claude/logs/tasks/725_*`): routing, role isolation, Redis-down,
rate limit, 300-request load — all passed. **Not** proven on staging: SignalR end-to-end, memory
limits, file uploads through the gateway, the production host's RAM/connection headroom.

## 0. Catch-up after the 2026-10-17 merge (PR #10 was merged BEFORE section A was done)

State found after that deploy (run 39037241607): the new topology came up healthy, but
1. **The old `deploy.sh` ran** (it was replaced by `git pull` while bash was executing it), so the
   health gate did not run; `shelfguard_web` was still `health: starting` when it finished.
2. **`shelfguard_web` will report `unhealthy`** (stage-1 bug: Next.js standalone listened on the container
   IP, the HEALTHCHECK probed 127.0.0.1). Traffic is unaffected (the port mapping works), but the next
   health-gated deploy would fail on it. Fixed in `frontend/Dockerfile` (`ENV HOSTNAME=0.0.0.0`) and
   `deploy.sh` now re-execs itself after `git pull` — both ship with the follow-up PR.
3. Pool limit / RAM / `max_connections` were not verified.

Do on the server, in this order (the scripts are in the follow-up PR; run them straight from the
branch without merging, so nothing deploys):
```bash
ssh -p 10048 administrator@93.127.143.98
cd ~/shelfguard && git fetch origin chore/prod-prep-3b
# 1. read-only check - changes nothing
git show origin/chore/prod-prep-3b:infra/scripts/prod-preflight-3b.sh | bash
# 2. see exactly what would change (no writes)
git show origin/chore/prod-prep-3b:infra/scripts/prod-apply-pool-limit.sh > /tmp/apply-pool.sh
bash /tmp/apply-pool.sh --dry-run
# 3. apply: backs up .env, adds ;Maximum Pool Size=25, rolling-recreates provider -> consumer -> core
bash /tmp/apply-pool.sh
# 4. re-run step 1; every line should be PASS (web may still WARN/FAIL until the follow-up PR is deployed)
```
If `max_connections` is 100, the budget (3x25 + worker 10 + ad-hoc 5 = 90 vs 97 usable) fits but is tight;
raising it to 200 needs a Postgres restart (`ALTER SYSTEM SET max_connections = 200;` then restart
`shelfguard_postgres`) — do that in a quiet window, it is a separate decision.
Then merge the follow-up PR (it deploys the web healthcheck fix and the self-re-exec `deploy.sh`).

## A. Before merging (on the production server, ssh -p 10048 administrator@93.127.143.98)

- [ ] **CI is green on PR #10** (backend, frontend, worker, mobile). A red job blocks the deploy step.
- [ ] **Backup the database** (cheap insurance even without migrations):
  `bash ~/shelfguard/infra/scripts/backup-db.sh` → note the file name and size.
- [ ] **RAM headroom.** `free -m`. The three API instances idle at ~230–290 MB each (core ~420 MB
  under load), plus gateway ~45 MB. Their `mem_limit` total is 1536+768+768 MB for API alone.
  Need roughly **+1 GB free** beyond what the single old API used. If `available` is under ~1.5 GB, stop
  and lower the limits or add RAM first.
- [ ] **Disk.** `df -h /` and `docker system df` — two new image builds (backend, frontend) need a few GB.
  `docker image prune -f` if tight.
- [ ] **Postgres connections.** `docker exec shelfguard_postgres psql -U shelfguard -c "show max_connections;"`
  must exceed `3 × 25 + worker pool (10) + ad-hoc (5) = 90`, i.e. fits the default 100 (97 usable) only with little slack — prefer raising it to 200.
  Current usage: `... -c "select count(*) from pg_stat_activity;"`.
- [ ] **Add the pool limit to the server `.env`** (`~/shelfguard/.env`): append
  `;Maximum Pool Size=25` to the end of `DATABASE_URL` (see `.env.production.example`). Without it each
  instance may open up to 100 connections (300 total). Keep the value on one line; do not `source` the file.
- [ ] `.env` has `REDIS_PASSWORD` set the same way as before (the API now also uses it for the SignalR
  backplane). If `REDIS_PASSWORD` is set, the worker's `REDIS_URL` must already include it (existing rule).
- [ ] `.env` still has `API_BASE_URL` unset or `http://localhost:5100` (worker → gateway on 5100 — correct as is).
- [ ] **Pick the window.** Low-traffic time. Expected user-visible impact: the API swap takes seconds, but
  the first deploy also replaces the single `shelfguard_api` that currently owns port 5100 — plan for
  **up to ~1–2 minutes of API errors** while core migrates/starts its health check.
- [ ] Note the **current production commit** (`cd ~/shelfguard && git rev-parse --short HEAD`) — that is the
  rollback target.
- [ ] Nobody else is deploying/pushing to `main` right now (concurrent sessions share this repo).

## B. Deploy

- [ ] Merge PR #10 into `main` (squash or merge — either; keep the commit sha).
- [ ] Watch the Actions run → "Deploy → production" step output. `deploy.sh` builds images first (old
  containers keep serving), starts the new set, then waits for `shelfguard_api_core`, `_consumer`,
  `_provider`, `shelfguard_gateway`, `shelfguard_web`, `shelfguard_worker`, `shelfguard_redis` to be healthy
  (up to 150 s each). It exits non-zero on an unhealthy service — treat a red deploy as a failed deploy
  and go to section D.
- [ ] If the log shows `up failed — falling back to full cleanup + restart`, that is the designed
  ghost-container fallback (old `shelfguard_api` on 5100); acceptable once, confirm it ended healthy.

## C. Verify after deploy (≈10 minutes)

On the server:
- [ ] `docker ps --format '{{.Names}} {{.Status}}' | grep shelfguard` — expect `shelfguard_gateway`,
  `shelfguard_api_core`, `shelfguard_api_consumer`, `shelfguard_api_provider`, `shelfguard_web`,
  `shelfguard_worker`, `shelfguard_redis`, `shelfguard_mosquitto` all `healthy`/`Up`;
  the old `shelfguard_api` is **gone**.
- [ ] Roles and migrations: `docker logs shelfguard_api_core | grep -E "API role|Applying migration"` →
  `API role: core`; consumer/provider logs show their role and **no** migration lines.
- [ ] RAM/connections: `docker stats --no-stream` (each API under its limit) and
  `docker exec shelfguard_postgres psql -U shelfguard -c "select count(*) from pg_stat_activity;"`.

From outside (or the server) through the public URL:
- [ ] `curl -sI https://api.agrusystems.pp.ua/health/live | grep -i -E "HTTP|x-served-by"` → `200`, `core`.
- [ ] Routing spot-check, expect the role in parentheses (status code irrelevant):
  `/api/auth/login` (core), `/api/consumer-auth/login` (consumer), `/api/marketplace/suppliers` (provider),
  `/api/service-desk` (core), `/api/admin/service-desk` (provider), `/uploads/none.png` (core).
- [ ] `https://agrusystems.pp.ua` loads; log in as a store user, open stock and a POS sale; log in as the
  provider and open the provider panel + marketplace.
- [ ] **Mobile (consumer) app** on a phone (still `https://api.agrusystems.pp.ua:10054/api`): loads config,
  logs in, opens a support ticket.
- [ ] **SignalR** (never proven on staging): consumer opens a support ticket in the app; staff replies from the
  web — the reply must appear in the app within seconds without a manual refresh. If not, check
  `docker exec shelfguard_api_core printenv SignalR__RedisConnection` (set on all three) and Redis logs.
- [ ] **Uploads**: upload a banner/item image as staff, open its `/uploads/...` URL.
- [ ] **Worker**: `docker logs shelfguard_worker --tail 50` — no API connection errors; the next
  `fiscalization-retry` / `ai-order` run reaches the API (they call `/api/auth/login` through the gateway).
- [ ] **POS fiscalization**: one test sale with ПРРО (if a test register exists) — receipt fiscalizes;
  Checkbox calls now go through the stage-2 breaker (no retry on POST).
- [ ] Watch `docker logs -f shelfguard_gateway` for ~5 min: `role=` in access lines, no wave of `502/503`.

## D. Rollback (decide fast — criteria below)

Roll back if: core not healthy after ~3 minutes, login/POS broken, repeated 5xx on `/api/stock`,
RAM exhaustion (OOM kills: `dmesg | grep -i oom`, or containers restarting), or SignalR + mobile broken
and not quickly fixable.

1. On a workstation: `git revert -m 1 <merge-commit-sha>` (or revert the squash commit) → push to `main`
   (CI redeploys the old topology; `--remove-orphans` removes gateway + the three role containers and
   restores the single `shelfguard_api` on 5100). Or on the server:
   `cd ~/shelfguard && git checkout <previous-sha> && bash deploy.sh` (then fix `main` afterwards).
2. Verify `docker ps` shows `shelfguard_api` healthy and `https://api.agrusystems.pp.ua/health/live` = 200.
3. No data rollback needed (no migrations). DB backup from step A is only for the unexpected.
4. Record what failed in `.claude/docs/known-issues.md` before retrying.

## E. After a successful deploy

- [ ] Tune `mem_limit` per role from a day of `docker stats`; remember the in-memory resolve-code lockout
  counter requires **one** core instance — do not scale core to 2 replicas without moving it to Redis.
- [ ] Optional hardening backlog: stage-2 breaker thresholds from real traffic; `client_max_body_size` on
  host nginx (default 1 MB blocks large uploads on the host-nginx path — pre-existing); object storage for
  `uploads` before any multi-host setup.
- [ ] Update `.claude/docs/` (architecture: roles + gateway) and close TASK-722..725.
