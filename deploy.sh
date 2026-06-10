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

# Load env
set -a && source "$DEPLOY_DIR/.env" && set +a

echo ">>> Building and starting containers..."
cd "$DEPLOY_DIR"
docker compose -f docker-compose.production.yml --env-file .env up -d --build

echo ">>> Waiting for API to start..."
sleep 10

echo ">>> Running DB migrations..."
docker exec shelfguard_api dotnet ShelfGuard.Api.dll --migrate 2>/dev/null || true

echo "=== Deploy complete ==="
echo "API:     http://$(curl -s ifconfig.me):5100"
echo "Web:     http://$(curl -s ifconfig.me):3100"
docker ps | grep shelfguard
