#!/bin/bash
# READ-ONLY preflight for the role-split API (resilience stage 3b) on the production host.
# Changes nothing: it only reads free/df/docker/psql and prints PASS/WARN/FAIL lines.
#
#   ssh -p 10048 administrator@93.127.143.98 'bash -s' < infra/scripts/prod-preflight-3b.sh
#   (or run on the server: bash infra/scripts/prod-preflight-3b.sh)
#
# Secrets are never printed: only the NON-secret pool parameter of DATABASE_URL is shown.
set -u

DEPLOY_DIR="${DEPLOY_DIR:-$HOME/shelfguard}"
PG_CONTAINER="${PG_CONTAINER:-shelfguard_postgres}"
PG_USER="${PG_USER:-shelfguard}"
PG_DB="${PG_DB:-shelfguard}"
POOL_RECOMMENDED=25      # per API instance (3 instances)
WORKER_POOL=10           # node-pg Pool default max (worker/src/db.ts sets none)
ADHOC=5                  # psql sessions, backups, migrations headroom
MIN_AVAIL_MB=1500
MIN_DISK_GB=5

fails=0; warns=0
pass(){ echo "PASS  $*"; }
warn(){ echo "WARN  $*"; warns=$((warns+1)); }
fail(){ echo "FAIL  $*"; fails=$((fails+1)); }

echo "=== ShelfGuard stage 3b preflight ($(date '+%F %T')) ==="

# --- RAM -------------------------------------------------------------------
avail_mb=$(free -m | awk '/^Mem:/{print $7}')
total_mb=$(free -m | awk '/^Mem:/{print $2}')
echo "      RAM: total ${total_mb} MB, available ${avail_mb} MB"
if [ "${avail_mb:-0}" -ge "$MIN_AVAIL_MB" ]; then pass "available RAM >= ${MIN_AVAIL_MB} MB"
else fail "available RAM ${avail_mb} MB < ${MIN_AVAIL_MB} MB (3 API instances idle ~0.8 GB, ~1.2 GB under load)"; fi

# --- Disk ------------------------------------------------------------------
disk_gb=$(df -BG --output=avail / | tail -1 | tr -dc '0-9')
if [ "${disk_gb:-0}" -ge "$MIN_DISK_GB" ]; then pass "free disk ${disk_gb} GB"
else warn "free disk ${disk_gb} GB < ${MIN_DISK_GB} GB (image builds) - run: docker image prune -f"; fi

# --- Containers ------------------------------------------------------------
echo "      containers:"
docker ps -a --filter name=shelfguard --format '        {{.Names}}  {{.Status}}' 2>/dev/null || warn "docker ps failed"
for c in shelfguard_gateway shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider shelfguard_web shelfguard_worker shelfguard_redis; do
  st=$(docker inspect -f '{{.State.Health.Status}} restarts={{.RestartCount}}' "$c" 2>/dev/null) || st="missing"
  st=$(printf '%s' "$st" | tr -d '\n')
  case "$st" in healthy*) pass "$c $st" ;; missing) warn "$c not found (stage 3b not deployed yet?)" ;; *) fail "$c $st" ;; esac
done
if docker inspect shelfguard_api >/dev/null 2>&1; then
  warn "legacy container shelfguard_api still exists (should be gone after the 3b deploy)"
fi

# --- Postgres connections --------------------------------------------------
psql_q(){ docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc "$1" 2>/dev/null; }
maxc=$(psql_q "show max_connections")
reserved=$(psql_q "show superuser_reserved_connections")
cur=$(psql_q "select count(*) from pg_stat_activity")
if [ -z "$maxc" ]; then
  fail "cannot query Postgres in container $PG_CONTAINER (check PG_CONTAINER/PG_USER/PG_DB)"
else
  usable=$((maxc - ${reserved:-3}))
  echo "      Postgres: max_connections=${maxc} (superuser reserved ${reserved:-3}), in use now=${cur}"
fi

# --- DATABASE_URL pool setting (never print the password) ------------------
envf="$DEPLOY_DIR/.env"
pool=""
if [ ! -f "$envf" ]; then
  fail ".env not found at $envf"
else
  line=$(grep -m1 '^DATABASE_URL=' "$envf" | tr -d '\r')
  if [ -z "$line" ]; then fail "DATABASE_URL missing in $envf"
  else
    pool=$(printf '%s' "$line" | grep -oiE '(Maximum Pool Size|MaxPoolSize)=[0-9]+' | head -1 | grep -oE '[0-9]+$')
    if [ -n "$pool" ]; then
      if [ "$pool" -le "$POOL_RECOMMENDED" ]; then pass "DATABASE_URL pool size = ${pool}"
      else warn "DATABASE_URL pool size = ${pool} (recommended <= ${POOL_RECOMMENDED})"; fi
    else
      fail "DATABASE_URL has NO 'Maximum Pool Size' -> each of 3 instances may open up to 100 connections. Fix: bash infra/scripts/prod-apply-pool-limit.sh"
    fi
  fi
  grep -q '^REDIS_PASSWORD=.\+' "$envf" && echo "      REDIS_PASSWORD: set (SignalR backplane uses it)" || echo "      REDIS_PASSWORD: empty (Redis without auth, as before)"
fi

# --- Connection budget -----------------------------------------------------
if [ -n "$maxc" ]; then
  eff_pool=${pool:-100}
  need=$((3 * eff_pool + WORKER_POOL + ADHOC))
  echo "      budget: 3 x ${eff_pool} (API pools) + ${WORKER_POOL} (worker) + ${ADHOC} (ad-hoc) = ${need}  vs usable ${usable}"
  if [ "$need" -le "$usable" ]; then pass "connection budget fits max_connections"
  elif [ -n "$pool" ]; then
    warn "worst-case budget ${need} > usable ${usable}: pools are lazy so normal load is far lower, but raise max_connections (e.g. 200) or lower the pool"
  else
    fail "worst-case budget ${need} > usable ${usable} (no pool limit set)"
  fi
fi

# --- Live usage snapshot ---------------------------------------------------
if command -v docker >/dev/null 2>&1; then
  echo "      docker stats (one shot):"
  docker stats --no-stream --format '        {{.Name}}  mem={{.MemUsage}}  cpu={{.CPUPerc}}' 2>/dev/null | grep -i shelfguard || true
fi

echo "=== result: ${fails} FAIL, ${warns} WARN ==="
[ "$fails" -eq 0 ]
