# TASK-350 — Staging environment + KI-006 fix + audit tooling base (Block 0)

**Agent:** devops-engineer (executed directly in main session per user instruction) · **Date:** 2026-07-14 · **Status:** done
**Context:** Block 0 of pre-launch audit plan (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`)

## Part 1 — Staging Docker Compose environment

- `docker-compose.staging.yml` (NEW, repo root) — full stack in Docker (api,
  web, postgres, redis, mosquitto, worker), unlike dev (postgres/redis/mosquitto
  in Docker, api/web on host). Unlike prod, postgres is its own container in
  this compose file (not `external_links`).
  Ports (no collision with dev 5435/6380/1884/5000/3000 or prod
  5100/3100/loopback-6380/loopback-1884): postgres `127.0.0.1:5436`, redis
  `127.0.0.1:6381`, mosquitto `127.0.0.1:1885`, api `127.0.0.1:5101`, web
  `127.0.0.1:3101`. `api`/`web` containers still listen on 5100/3100
  internally (hardcoded in `backend/Dockerfile` / `frontend/Dockerfile` —
  same image as prod), only the host-side published port differs.
  `SEED_ON_START: true` set on the `api` service (see Part 2).
- `.env.staging.example` (NEW, repo root) — placeholder env vars mirroring
  `.env.production.example`'s shape, staging ports/URLs, `SEED_ON_START=true`.
  No real secrets.
- `.gitignore` — added `.env.staging` and `.env.production` (real files);
  `.env.staging.example` stays tracked.
- `docs/staging.md` (NEW) — setup/start/stop/migrate instructions, dev vs
  staging vs prod port comparison table. `README.md` — added a pointer section
  linking to it.
- Verified: `docker compose -f docker-compose.staging.yml --env-file .env.staging.example config`
  parses cleanly, all 6 services resolve with correct ports/env. Did not bring
  the stack up (no Docker images built) — structural validation only, per brief.

## Part 2 — KI-006 fix (auto-seed in every environment)

- `backend/ShelfGuard.Api/Program.cs` (~line 159-172): `MigrateAsync()` stays
  unconditional. `DbSeeder.SeedAsync()` now gated:
  `app.Environment.IsDevelopment() || Environment.GetEnvironmentVariable("SEED_ON_START") == "true"`.
  Dev always seeds; staging seeds via `SEED_ON_START=true` (set in
  `docker-compose.staging.yml`); `docker-compose.production.yml` has no such
  var — confirmed via grep, production never auto-seeds.
- `dotnet build ShelfGuard.Api` — succeeded, 0 errors/warnings.
- `.claude/docs/known-issues.md` KI-006: `Status: open` → `resolved (2026-07-14)`,
  resolution line updated to describe the actual fix.

## Part 3 — Audit tooling base

- **k6:** `loadtests/README.md` (install instructions, no `/health` endpoint
  exists so smoke test targets `GET /api/marketplace/item-categories`
  — `[AllowAnonymous]`, in-memory fixed registry, no DB call, closest thing
  to a liveness probe). `loadtests/smoke.js` — 1 VU, 5 iterations,
  `BASE_URL` env var (default staging `:5101`), threshold `p(95)<1000ms`,
  status/JSON-array checks. Not run against a live server (no stack up) but
  script is syntactically self-contained k6 JS; full scenarios are Block 17.
- **Vulnerability scans:**
  - `dotnet list ShelfGuard.Api package --vulnerable --include-transitive` —
    ran cleanly, no config error. Found: `Microsoft.Extensions.Caching.Memory`
    8.0.0 (High) and `Npgsql` 8.0.0 (High), both transitive. Not remediated
    (Block 18).
  - `npm audit` in `frontend`, `worker`, `mobile` — all three ran cleanly
    (exit 0, no config/lockfile errors, nothing needed fixing). Results:
    frontend 12 vulns (3 moderate, 7 high, 2 critical, mostly via `next`);
    worker 1 low (`esbuild` dev-server file-read, Windows-only); mobile 11
    (10 moderate, 1 high, Expo toolchain packages). Not remediated (Block 18).
- **Vitest:** `frontend/vitest.config.ts` (NEW) — `@vitejs/plugin-react`
  (had to pin `^4` — vitest 1.6.1 resolves `vite@5`, and `@vitejs/plugin-react@6`
  requires `vite@8`, so `^4` was the compatible line; installed alongside
  `jsdom` as new devDependencies), `jsdom` environment, `@/*` alias mirroring
  `tsconfig.json`. `frontend/lib/utils.test.ts` (NEW) — 2 assertions on `cn()`
  (falsy-class filtering + tailwind-merge conflict resolution).
  `npm test -- --run` → **2/2 tests passed.**

## Files created

- `docker-compose.staging.yml`
- `.env.staging.example`
- `docs/staging.md`
- `loadtests/README.md`
- `loadtests/smoke.js`
- `frontend/vitest.config.ts`
- `frontend/lib/utils.test.ts`

## Files modified

- `backend/ShelfGuard.Api/Program.cs` (KI-006 seed guard)
- `.claude/docs/known-issues.md` (KI-006 → resolved)
- `.gitignore` (`.env.staging`, `.env.production`)
- `README.md` (staging pointer)
- `frontend/package.json`, `frontend/package-lock.json` (`@vitejs/plugin-react@^4`, `jsdom` devDeps)

## Needs user decision / follow-up

- User must fill real secrets into their own local `.env.staging` (copied from
  `.env.staging.example`) before `docker compose -f docker-compose.staging.yml up`
  will actually work — not created automatically, per "secrets never in code".
- Full `docker compose ... up -d --build` was not run (would require building
  4 Docker images) — only `config` validation was done. Recommend a first
  real boot + smoke test before relying on staging for Block 1+ audit work.
- Backend/frontend/mobile vulnerable-package counts above are informational
  only (Block 18 will triage/fix).
