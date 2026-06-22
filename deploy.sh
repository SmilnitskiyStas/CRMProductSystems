#!/bin/bash
# ShelfGuard production deploy script
# Run on the server: bash deploy.sh

set -e

DEPLOY_DIR="/home/administrator/shelfguard"
REPO_URL="https://github.com/SmilnitskiyStas/CRMProductSystems"

echo "=== ShelfGuard Deploy ==="

# Clone or pull
if [ -d "$DEPLOY_DIR/.git" ]; then
  echo ">>> Pulling latest code..."
  cd "$DEPLOY_DIR" && git pull origin main
else
  echo ">>> Cloning repo..."
  git clone "$REPO_URL" "$DEPLOY_DIR"
  cd "$DEPLOY_DIR"
fi

# Check .env exists
if [ ! -f "$DEPLOY_DIR/.env" ]; then
  echo "ERROR: .env file not found at $DEPLOY_DIR/.env"
  echo "Copy .env.production.example to .env and fill in values"
  exit 1
fi

# Do NOT `source .env` here: unquoted values with `;` (the .NET connection
# string) get truncated by bash, and the exported shell vars then OVERRIDE
# --env-file in compose interpolation. compose reads .env itself.

echo ">>> Stopping existing containers..."
cd "$DEPLOY_DIR"

# Remove all known ShelfGuard containers — running, stopped, or created.
# docker rm -f is idempotent: if the container doesn't exist it exits non-zero,
# which we suppress. Using -a to also catch Exited/Created containers.
echo "    Removing ghost containers by name..."
docker rm -f shelfguard_api shelfguard_web shelfguard_redis shelfguard_worker shelfguard_mosquitto 2>/dev/null || true

# Also sweep by port in case any hash-named containers hold the ports.
for port in 5100 3100 6380 1884; do
  CONTAINER=$(docker ps -aq --filter "publish=$port" 2>/dev/null)
  if [ -n "$CONTAINER" ]; then
    echo "    Removing ghost container on port $port: $CONTAINER"
    docker rm -f "$CONTAINER" 2>/dev/null || true
  fi
done

docker compose -f docker-compose.production.yml --env-file .env down --remove-orphans 2>/dev/null || true

# shelfguard_postgres is external (not in compose) — ensure it's in the network after compose recreates it
echo ">>> Ensuring postgres is reachable..."
docker network connect shelfguard_default shelfguard_postgres 2>/dev/null || true

echo ">>> Building and starting containers..."
docker compose -f docker-compose.production.yml --env-file .env up -d --build

echo ">>> Waiting for API to start..."
sleep 15
echo ">>> Migrations run automatically on container startup (EF Core)"

echo "=== Deploy complete ==="
echo "API:     http://$(curl -s ifconfig.me):5100"
echo "Web:     http://$(curl -s ifconfig.me):3100"
docker ps | grep shelfguard
