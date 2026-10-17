#!/bin/bash
# ShelfGuard production deploy script
# Run on the server: bash deploy.sh

set -e

DEPLOY_DIR="/home/administrator/shelfguard"
REPO_URL="https://github.com/SmilnitskiyStas/CRMProductSystems"

echo "=== ShelfGuard Deploy ==="

# Phase 1: clone/pull, then RE-EXEC the freshly pulled script. bash reads a script lazily, so
# when `git pull` rewrites deploy.sh while it is running, the rest of the OLD run would continue
# from a shifted offset / old logic (this skipped the health gate on the first stage 3b deploy).
# The whole `if` block is parsed before it runs, so it is safe to replace this file inside it.
if [ "${1:-}" != "--after-pull" ]; then
  if [ -d "$DEPLOY_DIR/.git" ]; then
    echo ">>> Pulling latest code..."
    cd "$DEPLOY_DIR" && git pull origin main
  else
    echo ">>> Cloning repo..."
    git clone "$REPO_URL" "$DEPLOY_DIR"
    cd "$DEPLOY_DIR"
  fi
  exec bash "$DEPLOY_DIR/deploy.sh" --after-pull
fi
cd "$DEPLOY_DIR"

# Check .env exists
if [ ! -f "$DEPLOY_DIR/.env" ]; then
  echo "ERROR: .env file not found at $DEPLOY_DIR/.env"
  echo "Copy .env.production.example to .env and fill in values"
  exit 1
fi

# Do NOT `source .env` here: unquoted values with `;` (the .NET connection
# string) get truncated by bash, and the exported shell vars then OVERRIDE
# --env-file in compose interpolation. compose reads .env itself.

cd "$DEPLOY_DIR"
COMPOSE="docker compose -f docker-compose.production.yml --env-file .env"

# Build FIRST while the old containers keep serving traffic — downtime is then only the
# container swap (seconds), not the whole image build (minutes).
echo ">>> Building images (old containers keep running)..."
$COMPOSE build

# shelfguard_postgres is external (not in compose) — ensure it's in the network
echo ">>> Ensuring postgres is reachable..."
docker network connect shelfguard_default shelfguard_postgres 2>/dev/null || true

# Recreate only services whose image/config changed; untouched ones (redis, mosquitto) stay up.
echo ">>> Starting containers..."
if ! $COMPOSE up -d --remove-orphans; then
  # Fallback for ghost containers (Created/Exited/hash-named) holding names or ports.
  echo "!!! up failed — falling back to full cleanup + restart"
  $COMPOSE down --remove-orphans 2>/dev/null || true
  docker rm -f shelfguard_api shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider shelfguard_gateway shelfguard_web shelfguard_redis shelfguard_worker shelfguard_mosquitto 2>/dev/null || true
  for port in 5100 3100 6380 1884; do
    CONTAINER=$(docker ps -aq --filter "publish=$port" 2>/dev/null)
    if [ -n "$CONTAINER" ]; then
      echo "    Removing ghost container on port $port: $CONTAINER"
      docker rm -f "$CONTAINER" 2>/dev/null || true
    fi
  done
  docker network connect shelfguard_default shelfguard_postgres 2>/dev/null || true
  $COMPOSE up -d
fi

# Health gate: wait until each service reports healthy (HEALTHCHECK in its Dockerfile/compose).
# API start-period covers EF migrations that run at startup.
wait_healthy() {
  local name="$1" timeout="${2:-150}" waited=0 status
  while [ "$waited" -lt "$timeout" ]; do
    status=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}nohealth{{end}}' "$name" 2>/dev/null || echo "missing")
    case "$status" in
      healthy) echo "    $name: healthy (${waited}s)"; return 0 ;;
      nohealth) echo "    $name: no healthcheck defined, skipping"; return 0 ;;
    esac
    sleep 3; waited=$((waited + 3))
  done
  echo "!!! $name did not become healthy within ${timeout}s (last status: $status)"
  docker logs --tail 40 "$name" 2>&1 || true
  return 1
}

echo ">>> Waiting for services to become healthy..."
FAILED=0
# core first (runs migrations), then the other roles, then gateway
for svc in shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider shelfguard_gateway shelfguard_web shelfguard_worker shelfguard_redis; do
  wait_healthy "$svc" || FAILED=1
done
if [ "$FAILED" -ne 0 ]; then
  echo "=== Deploy FAILED: unhealthy service(s) — see logs above ==="
  docker ps | grep shelfguard || true
  exit 1
fi
echo ">>> Migrations run automatically on API startup (EF Core)"

echo "=== Deploy complete ==="
echo "Gateway: http://$(curl -s ifconfig.me):5100  (api_core/api_consumer/api_provider are internal-only; see infra/gateway/README.md)"
echo "Web:     http://$(curl -s ifconfig.me):3100"
docker ps | grep shelfguard
