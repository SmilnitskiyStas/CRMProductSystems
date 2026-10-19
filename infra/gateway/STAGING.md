# Staging run of stage 3b (API roles + gateway)

Goal: prove on staging, before production, that (1) all three roles start from one image,
(2) the gateway routes correctly, (3) a dead role does not take the others down, and
(4) the resource cost (RAM, Postgres connections) is acceptable. Production is not touched.

Staging is self-contained (`docker-compose.staging.yml`, project name `shelfguard_staging`):
gateway on `127.0.0.1:5101`, web on `3101`, own Postgres on `5436`, own Redis. Run everything
from the repo root, on the branch with stage 3b.

## 0. Prerequisites (once)
- `.env.staging` exists (copy from `.env.staging.example`, fill the `CHANGE_ME` values).
- `DATABASE_URL` is used **inside the API containers**, so its host must be the compose service
  `postgres` and the container port, e.g. `Host=postgres;Port=5432;Database=shelfguard_staging;Username=shelfguard_staging;Password=...;Maximum Pool Size=25`.
  (`localhost:5436` is only right for tools on the host such as `psql`.) `Maximum Pool Size=25`
  mirrors what production will use, so the connection count you measure is realistic.
- Never pass `-p` to compose: the file pins its project name to protect the dev containers.

Shortcut used below:

```bash
export SC="docker compose -f docker-compose.staging.yml --env-file .env.staging"
```

## 1. Build and start
```bash
$SC build            # backend image is built ONCE (api_core), consumer/provider reuse it
$SC up -d
$SC ps               # wait until api_core is healthy, then consumer/provider/gateway/web/worker
```
Expected: `shelfguard_staging_api_core` starts first (it runs EF migrations and seeds), consumer and
provider start after core is healthy. First start can take 1–2 minutes (migrations + seed).

Check the role each instance took and that only core migrated:
```bash
docker logs shelfguard_staging_api_core     2>&1 | grep -E "API role|Applying migration|Now listening"
docker logs shelfguard_staging_api_consumer 2>&1 | grep -E "API role|Applying migration"
docker logs shelfguard_staging_api_provider 2>&1 | grep -E "API role|Applying migration"
```
Pass: `API role: core|consumer|provider` respectively; migration lines only in core.

## 2. Routing through the gateway
Every response carries `X-Served-By`. Run:

```bash
G=http://127.0.0.1:5101
for p in /health/live /api/auth/login /api/v1/mobile/config /api/v1/mobile/blocks \
         /api/consumer-auth/login /api/consumer/profile /api/admin/chat /api/chat \
         /api/admin/service-desk /api/service-desk /api/marketplace/x /api/supplier-cabinet/x \
         /api/settings/modules /api/settings/supplier-profile /uploads/none.png; do
  printf '%-34s ' "$p"; curl -s -o /dev/null -D - "$G$p" | tr -d '\r' | awk '/^HTTP/{s=$2} /^X-Served-By/{r=$2} END{print s, r}'
done
```
Expected role (status is irrelevant here — 401/404 is fine, we only check who answered):

| Path | Served by |
|---|---|
| `/health/live`, `/api/auth/login`, `/api/v1/mobile/blocks`, `/api/chat`, `/api/service-desk`, `/api/settings/modules`, `/uploads/…` | core |
| `/api/v1/mobile/config`, `/api/consumer-auth/login`, `/api/consumer/profile` | consumer |
| `/api/admin/chat`, `/api/admin/service-desk`, `/api/marketplace/x`, `/api/supplier-cabinet/x`, `/api/settings/supplier-profile` | provider |

Pass = every row matches. A wrong row means a routing rule or `RoleControllerFeatureProvider`
classification is off — stop and report the path.

Wrong-role requests must 404, not work. Hit an instance directly from inside the network:
```bash
docker exec shelfguard_staging_gateway wget -qS -O- http://api_consumer:5100/api/chat 2>&1 | head -3   # expect 404
```

## 3. Functional smoke (the part automated checks can't prove)
Use the staging web (`http://localhost:3101`) with the seeded users:
1. Log in as a store user → dashboard loads, open stock, create a POS sale (core).
2. Log in as the provider → provider panel, marketplace, supplier cabinet pages load (provider).
3. Consumer app / `GET /api/v1/mobile/config` returns config (consumer).
4. Upload a banner or item image as staff, then open its `/uploads/...` URL (shared volume).
5. Worker: `docker logs shelfguard_staging_worker --tail 50` shows no API connection errors
   (it calls the API through the gateway).

## 4. Failure isolation (the point of the stage)
Run each, check, then restore with `$SC start <service>`.

| Kill | Must still work | Expected failure |
|---|---|---|
| `$SC stop api_consumer` | login, POS, stock, provider panel, marketplace | `/api/consumer/*`, `/api/v1/mobile/config` → JSON 503 `service_unavailable` (`X-Served-By: gateway`) |
| `$SC stop api_provider` | login, POS, stock, consumer endpoints | `/api/provider/*`, `/api/marketplace/*`, `/api/supplier-cabinet/*` → JSON 503 |
| `$SC stop api_core` | consumer and provider endpoints with already-issued tokens (JWT is stateless) | login/refresh/POS → JSON 503 |
| `$SC stop redis` | **every endpoint above** (API must not depend on Redis) | only realtime (SignalR backplane) and the worker queues |

For each row, also confirm the UI shows the soft "temporarily unavailable" notices rather than
crashing, and that `docker ps` shows the other containers still `healthy`.

Redis check specifically (never verified so far): after `$SC stop redis`,
`curl -s -o /dev/null -w '%{http_code}\n' $G/health/live` must stay `200` and
`docker logs shelfguard_staging_api_core --tail 30` may show Redis reconnect warnings but no crash/restart
(`docker inspect -f '{{.RestartCount}}' shelfguard_staging_api_core` stays `0`).

Restart order test: `$SC restart api_core` while consumer/provider run → they must keep serving
(`depends_on` only applies at `up`).

## 5. SignalR (realtime support chat)
Needs a consumer-app session (real client) — the only end-to-end proof that the backplane works:
1. Consumer opens a support ticket in the mobile app (connects to `/api/hubs/consumer-support`).
2. Staff replies from the web (`/api/customer-support`, served by **core**).
3. The reply must appear in the consumer app within seconds without refresh.

If it only appears after a manual refresh, the backplane is not delivering core → consumer:
check `docker logs shelfguard_staging_api_core | grep -i redis` and that
`SignalR__RedisConnection` is set on all three (`docker exec shelfguard_staging_api_core printenv SignalR__RedisConnection`).
Quick negotiate check (expect 401, answered by consumer): `curl -s -o /dev/null -D - -X POST $G/api/hubs/consumer-support/negotiate | grep -i -E "HTTP|x-served-by"`.

## 6. Rate limit
```bash
for i in $(seq 1 14); do curl -s -o /dev/null -w '%{http_code} ' -X POST $G/api/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"x@x.x","password":"x"}'; done; echo
```
Pass: the first ~10–20 (rate + burst) return 401, then `429`. Repeat for `/api/consumer-auth/login`.

## 7. Resource cost — decide go/no-go for production
```bash
docker stats --no-stream                      # RAM per container vs mem_limit
docker exec shelfguard_staging_postgres psql -U shelfguard_staging -d shelfguard_staging -c \
  "select application_name, count(*) from pg_stat_activity where datname='shelfguard_staging' group by 1;"
docker exec shelfguard_staging_postgres psql -U shelfguard_staging -c "show max_connections;"
```
Record: idle RAM of core/consumer/provider, total connections (must stay well below
`max_connections`, expect ≤ 3×25). Optionally run `loadtests/smoke.js` against `$G` (see
`loadtests/README.md`) and re-check `docker stats` — a role must not hit its `mem_limit`.

**Production go/no-go:** all of §2, §4 (incl. Redis stop), §5 and §6 pass, RAM fits the production
host with headroom (compare against `free -m` there), and `max_connections` there exceeds
3×25 + worker pool (10).

## 8. Cleanup / rollback
```bash
$SC down            # keeps volumes (data); add -v only if you want to wipe staging data
```
Rollback of the code is `git revert` of the stage 3b commit and redeploy; staging data is disposable.

## Report back
Send: the §2 table output, which §4 rows failed (if any), the §5 result, and the §7 numbers.
