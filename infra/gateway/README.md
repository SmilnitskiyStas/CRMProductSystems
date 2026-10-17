# API gateway runbook (resilience stage 3b)

One backend image runs as three role instances behind an nginx gateway, so a crash or
overload of one role does not take down the others.

| Container | Role (`API_ROLE`) | Serves |
|---|---|---|
| `shelfguard_gateway` | nginx, publishes host `5100:80` | path routing, auth rate limit, JSON 503 |
| `shelfguard_api_core` | core | everything else (auth, stock, POS, AI, `/uploads/`, `/health/*`), runs EF migrations |
| `shelfguard_api_consumer` | consumer | consumer mobile app, `/api/hubs/` (SignalR via Redis backplane) |
| `shelfguard_api_provider` | provider | provider, supplier cabinet, marketplace, admin, leads |

Instances listen on `:5100` inside the compose network only (no host ports). Host nginx still
proxies to `127.0.0.1:5100` and the provider edge still hits host port 5100 - both now reach the
gateway; no host nginx change. Routing table: `infra/gateway/nginx.conf` (anchored prefixes - do
not loosen, e.g. `/api/admin/service-desk` is provider but `/api/service-desk` is core).

## Which role served a request
Every response has `X-Served-By: core|consumer|provider` (`gateway` for gateway-made 503).
`curl -sI https://api.agrusystems.pp.ua/api/chat | grep -i x-served-by`. Gateway logs show
`up=<ip> role=<role>`: `docker logs shelfguard_gateway`.

## What degrades when a container dies
- core: login/refresh, stock, POS, AI, uploads, health for everything on core. Consumer/provider keep
  serving already-issued JWTs. Migrations only run on core start.
- consumer: mobile consumer app + realtime hub. Web dashboard and provider unaffected.
- provider: provider panel, supplier cabinet, marketplace, admin. Retail dashboard unaffected.
- gateway: all API traffic (instances stay up; web SSR unaffected until it calls the API).
A dead/unresolvable upstream returns `503 {"error":"Service temporarily unavailable.","code":"service_unavailable"}`
(frontend handles it). The gateway starts even with no upstream running. `depends_on` is only enforced
at `up` (core healthy before consumer/provider on deploy); later core restarts never block the others.

## Rate limit
Gateway `limit_req` 10 req/min/IP (burst 10) on `/api/auth/(login|2fa)`, `/api/consumer-auth/`,
`/api/mobile-auth/login*` -> 429. Key is the first `X-Forwarded-For` entry.

## Postgres connections
Each instance has its own Npgsql pool. No pool size is set in code, so the default Maximum Pool Size
is 100 per instance (300 total). Set `;Maximum Pool Size=30` in `DATABASE_URL` (shared by all three)
and verify on the server: `docker exec shelfguard_postgres psql -U shelfguard -c "SHOW max_connections;"`
- it must exceed 3x30 + worker pool + headroom (default 100 is enough with 30).

## Rollback
`git revert <stage-3b commit(s)>` and redeploy (`bash deploy.sh`). Redeploy removes orphaned
containers (`--remove-orphans`); the old single `shelfguard_api` returns on port 5100.
