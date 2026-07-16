# Load testing (k6)

`smoke.js` proves the tool works end-to-end. The four scenario scripts below
are audit Block 17's real load scenarios, each targeting the staging stack
(`docker-compose.staging.yml`) at `http://localhost:5101` by default.

All four assume the Block 0 DbSeeder data is present (`SEED_ON_START=true` in
staging): seeded users `manager@demo.local` / `keeper@demo.local` /
`netmgr@demo.local` / `ea@demo.local` / `merch1@demo.local` /
`merch2@demo.local`, all password `password` (DbSeeder.cs DefaultSeedPassword),
and one seeded store (`STAGING_STORE_ID` env var, defaults to the demo
location `56cb968e-3ce2-4f13-a19b-83df2bee3c95`).

**Rate-limiter note:** `login-storm.js`'s `single_ip_burst` sub-scenario
deliberately exhausts the 10 req/min per-IP login rate limit from this
machine's real IP. If you run `pos-queue.js` / `bulk-order-creation.js` /
`analytics-concurrent-read.js` (whose `setup()` each do one real login) right
after `login-storm.js`, their setup may get a 429 — wait ~60s for the fixed
window to clear, or just run login-storm.js last.

## Scenario scripts

- **`login-storm.js`** — concurrent login attempts (valid + invalid
  passwords), split into a many-distinct-IPs sub-scenario (spoofed
  X-Forwarded-For, exercises the real auth path + per-account lockout under
  concurrency) and a single-real-IP burst sub-scenario (stresses the
  per-IP rate limiter's own concurrency correctness). See the file's header
  comment for a documented BCrypt-cost latency floor found on 2026-07-16.
- **`pos-queue.js`** — N simulated concurrent cash registers selling against
  one shared shift, the exact scenario that exercises the Block 6
  optimistic-concurrency (xmin) fix on `product_stock`. Correctness (no
  oversell/lost sales) is verified out-of-band via `psql` stock-delta checks,
  not by the script itself (k6 has no Postgres driver here).
- **`bulk-order-creation.js`** — concurrent `POST /api/orders/calculate`
  (the non-AI order-formula computation). Deliberately does NOT hit
  `POST /api/ai-orders/generate` — that calls the real Claude API per
  request and Block 7 already audited its error handling/call hygiene
  separately; hammering it here would just burn Anthropic budget.
- **`analytics-concurrent-read.js`** — concurrent dashboard/analytics GETs.
  Run it at the same time as `pos-queue.js` (two terminals) to reproduce the
  audit brief's "reads while another scenario writes" case; standalone it's
  still a useful pure-read latency check.

```bash
k6 run loadtests/login-storm.js
k6 run loadtests/pos-queue.js
k6 run loadtests/bulk-order-creation.js
k6 run loadtests/analytics-concurrent-read.js

# reproduce concurrent read+write (two terminals, started together):
k6 run loadtests/pos-queue.js
k6 run loadtests/analytics-concurrent-read.js
```

## Install k6

k6 is a standalone CLI, not an npm package — install it once per machine.

- **Windows:** `choco install k6` (Chocolatey), or download the binary from
  https://github.com/grafana/k6/releases and put it on `PATH`.
- **macOS:** `brew install k6`
- **Linux:** see https://k6.io/docs/get-started/installation/ for your distro.

Full docs: https://k6.io/docs/get-started/installation/

## Run the smoke test

Point it at any running ShelfGuard API instance via `BASE_URL` (defaults to
the staging api — see `docs/staging.md`):

```bash
# against staging (default)
k6 run loadtests/smoke.js

# against local dev (dotnet run on :5000)
BASE_URL=http://localhost:5000 k6 run loadtests/smoke.js
```

A passing run means k6 itself is correctly installed and can reach the API —
this is a tooling smoke test, not a real load test.

## Why `/api/marketplace/item-categories`

The API has no dedicated `/health` endpoint. `GET /api/marketplace/item-categories`
(`MarketplaceController`, `[AllowAnonymous]`) was chosen instead: it requires
no auth, no module activation, and no tenant context, and it serves a small
fixed in-memory registry (no DB round-trip) — the closest equivalent to a
liveness probe currently exposed by the API.
