#!/bin/bash
# Caps the Npgsql pool per API instance on the production host (resilience stage 3b) and
# applies it with a ROLLING recreate of the three API containers (one at a time, health-gated).
#
#   bash infra/scripts/prod-apply-pool-limit.sh --dry-run   # show what would change, touch nothing
#   bash infra/scripts/prod-apply-pool-limit.sh             # apply
#   POOL=20 bash infra/scripts/prod-apply-pool-limit.sh     # custom size (default 25)
#
# What it does:
#   1. backs up ~/shelfguard/.env  ->  .env.bak.<timestamp> (chmod 600)
#   2. appends ";Maximum Pool Size=<POOL>" to the DATABASE_URL line if no pool setting exists
#      (an existing setting is left alone unless FORCE=1)
#   3. recreates api_provider -> api_consumer -> api_core, waiting for each to be healthy.
#      Other containers (gateway, web, worker, redis, mosquitto) are NOT touched; the gateway returns
#      JSON 503 for a few seconds for the one role being swapped, the other roles keep serving.
# Secrets are never printed. Rollback: cp the printed .env.bak.* back to .env and re-run step 3
#   (docker compose -f docker-compose.production.yml --env-file .env up -d --force-recreate --no-deps api_core ...).
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-$HOME/shelfguard}"
POOL="${POOL:-25}"
FORCE="${FORCE:-0}"
DRY=0; [ "${1:-}" = "--dry-run" ] && DRY=1

cd "$DEPLOY_DIR"
[ -f .env ] || { echo "ERROR: $DEPLOY_DIR/.env not found"; exit 1; }
case "$POOL" in ''|*[!0-9]*) echo "ERROR: POOL must be a number"; exit 1 ;; esac
[ "$POOL" -ge 5 ] && [ "$POOL" -le 100 ] || { echo "ERROR: POOL must be 5..100"; exit 1; }

line=$(grep -m1 '^DATABASE_URL=' .env | tr -d '\r' || true)
[ -n "$line" ] || { echo "ERROR: DATABASE_URL not found in .env"; exit 1; }
if [ "$(tr -cd '\r' < .env | wc -c)" -gt 0 ]; then
  echo "ERROR: .env has CRLF line endings - convert first: sed -i 's/\\r\$//' .env  (then re-run)"; exit 1
fi

existing=$(printf '%s' "$line" | grep -oiE '(Maximum Pool Size|MaxPoolSize)=[0-9]+' | head -1 || true)

if [ -n "$existing" ] && [ "$FORCE" != "1" ]; then
  echo "DATABASE_URL already has '${existing}' - leaving .env unchanged (FORCE=1 to overwrite)."
  if [ "${RECREATE:-0}" != "1" ]; then
    echo "Nothing to do (RECREATE=1 to still roll the API containers, e.g. if they started before .env was edited)."
    exit 0
  fi
  CHANGE_ENV=0
else
  CHANGE_ENV=1
fi

if [ "$CHANGE_ENV" = "1" ]; then
  if [ -n "$existing" ]; then
    sed_expr="s/(Maximum Pool Size|MaxPoolSize)=[0-9]+/Maximum Pool Size=${POOL}/I"
    echo "Plan: replace '${existing}' with 'Maximum Pool Size=${POOL}' in DATABASE_URL"
  else
    # append before an optional closing quote; tolerate one trailing ';'
    sed_expr="/^DATABASE_URL=/ s/^(DATABASE_URL=.*[^;\"'])(;?)([\"']?)\$/\\1;Maximum Pool Size=${POOL}\\3/"
    echo "Plan: append ';Maximum Pool Size=${POOL}' to DATABASE_URL"
  fi
  # Dry-run the edit on a copy and verify, before touching the real file.
  tmp=$(mktemp); trap 'rm -f "$tmp"' EXIT
  sed -E "$sed_expr" .env > "$tmp"
  new=$(grep -m1 '^DATABASE_URL=' "$tmp" | tr -d '\r')
  newpool=$(printf '%s' "$new" | grep -oiE '(Maximum Pool Size|MaxPoolSize)=[0-9]+' | head -1 || true)
  [ "$newpool" = "Maximum Pool Size=${POOL}" ] || { echo "ERROR: edit did not produce the expected pool setting (got '${newpool}') - aborting, .env untouched"; exit 1; }
  # nothing else in the file may change
  changed=$(diff <(grep -v '^DATABASE_URL=' .env) <(grep -v '^DATABASE_URL=' "$tmp") | wc -l)
  [ "$changed" -eq 0 ] || { echo "ERROR: edit touched other lines - aborting"; exit 1; }
  echo "Result (secret-free): DATABASE_URL ... ;${newpool}"
fi

COMPOSE="docker compose -f docker-compose.production.yml --env-file .env"

if [ "$DRY" = "1" ]; then
  echo "DRY RUN: would back up .env, apply the edit above and recreate: api_provider api_consumer api_core (rolling)."
  exit 0
fi

if [ "$CHANGE_ENV" = "1" ]; then
  bak=".env.bak.$(date +%Y%m%d-%H%M%S)"
  cp -p .env "$bak"; chmod 600 "$bak"
  # write through the original inode/permissions
  cat "$tmp" > .env
  echo "Backed up to $bak; .env updated."
fi

wait_healthy() {
  local name="$1" waited=0 status
  while [ "$waited" -lt 150 ]; do
    status=$(docker inspect -f '{{.State.Health.Status}}' "$name" 2>/dev/null || echo missing)
    [ "$status" = "healthy" ] && { echo "  $name healthy (${waited}s)"; return 0; }
    sleep 3; waited=$((waited + 3))
  done
  echo "!!! $name not healthy after 150s (last: $status)"; docker logs --tail 30 "$name" 2>&1 || true
  return 1
}

echo "Rolling recreate (compose re-reads .env):"
for svc in api_provider api_consumer api_core; do
  echo ">>> $svc"
  $COMPOSE up -d --force-recreate --no-deps "$svc"
  if ! wait_healthy "shelfguard_${svc}"; then
    echo "ABORTED at $svc. Remaining services were not touched."
    [ -n "${bak:-}" ] && echo "To roll back: cp -p $bak .env && $COMPOSE up -d --force-recreate --no-deps api_provider api_consumer api_core"
    exit 1
  fi
done

echo "Done. Connections now:"
docker exec shelfguard_postgres psql -U shelfguard -d shelfguard -tAc "select count(*) from pg_stat_activity" 2>/dev/null || true
echo "Verify: bash infra/scripts/prod-preflight-3b.sh"
