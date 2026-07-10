#!/bin/bash
# ShelfGuard — production server hardening (TASK-332)
# Target: Ubuntu host 93.127.143.98 (~/shelfguard checkout).
#
# Usage:
#   ./harden-server.sh            # DRY RUN (default) — prints what it would do
#   sudo ./harden-server.sh --apply   # actually applies changes (needs root)
#
# What it does (idempotent — safe to re-run):
#   1. fail2ban: install + enable, sshd jail (defaults) + nginx-limit-req jail
#      (bans IPs that keep tripping the shelfguard_auth limit_req zone)
#   2. UFW firewall: allow current sshd port FIRST (never lock out), allow
#      8443/tcp (nginx TLS), default deny incoming, enable
#   3. Daily DB backup cron via infra/scripts/setup-backup-cron.sh (03:00)
#   4. PRINTS (never executes) guidance for rebinding the external
#      shelfguard_postgres container to 127.0.0.1:5434
set -euo pipefail

SHELFGUARD_DIR="${SHELFGUARD_DIR:-/home/administrator/shelfguard}"
APPLY=0
[ "${1:-}" = "--apply" ] && APPLY=1

if [ "$APPLY" -eq 1 ] && [ "$(id -u)" -ne 0 ]; then
    echo "ERROR: --apply requires root (sudo $0 --apply)" >&2
    exit 1
fi

run() {
    if [ "$APPLY" -eq 1 ]; then
        echo "+ $*"
        "$@"
    else
        echo "[dry-run] $*"
    fi
}

# Writes stdin to $1 (apply) or prints it (dry-run)
write_file() {
    local dest="$1"
    if [ "$APPLY" -eq 1 ]; then
        echo "+ write $dest"
        cat > "$dest"
    else
        echo "[dry-run] would write $dest:"
        sed 's/^/    | /'
    fi
}

echo "=== ShelfGuard server hardening ($( [ "$APPLY" -eq 1 ] && echo APPLY || echo DRY-RUN ) mode) ==="
echo

# ---------------------------------------------------------------------------
# 1. fail2ban
# ---------------------------------------------------------------------------
echo "--- 1. fail2ban (sshd + nginx-limit-req jails) ---"
if dpkg -s fail2ban >/dev/null 2>&1; then
    echo "fail2ban already installed"
else
    run apt-get update -qq
    run apt-get install -y -qq fail2ban
fi

# jail.local: sshd on (defaults), nginx-limit-req watches nginx error.log for
# 'limiting requests' lines produced by the shelfguard_auth zone.
write_file /etc/fail2ban/jail.d/shelfguard.local <<'EOF'
# ShelfGuard hardening (TASK-332) — managed by infra/scripts/harden-server.sh
[sshd]
enabled = true

[nginx-limit-req]
enabled  = true
port     = 8443
filter   = nginx-limit-req
logpath  = /var/log/nginx/error.log
# ban after 30 rate-limited requests within 10 min → 1 h ban
maxretry = 30
findtime = 600
bantime  = 3600
EOF

run systemctl enable --now fail2ban
if [ "$APPLY" -eq 1 ]; then
    run systemctl restart fail2ban
    fail2ban-client status || true
fi
echo

# ---------------------------------------------------------------------------
# 2. UFW
# ---------------------------------------------------------------------------
echo "--- 2. UFW firewall ---"

# Detect sshd port (first Port directive in sshd_config or sshd_config.d/*);
# default 22 when none is set explicitly.
SSH_PORT=$(grep -rhsE '^[[:space:]]*Port[[:space:]]+[0-9]+' \
    /etc/ssh/sshd_config /etc/ssh/sshd_config.d/ 2>/dev/null \
    | awk '{print $2; exit}')
SSH_PORT="${SSH_PORT:-22}"
echo "Detected sshd port: $SSH_PORT"

if ! command -v ufw >/dev/null 2>&1; then
    run apt-get install -y -qq ufw
fi

# ORDER MATTERS: allow ssh BEFORE 'default deny' + 'enable' — never lock out.
run ufw allow "${SSH_PORT}/tcp" comment 'sshd'
run ufw allow 8443/tcp comment 'shelfguard nginx TLS'
run ufw default deny incoming
run ufw default allow outgoing

echo
echo "!!! WARNING: enabling UFW will drop all incoming traffic except"
echo "!!! ${SSH_PORT}/tcp (ssh) and 8443/tcp (nginx). Docker's published ports"
echo "!!! bypass UFW via iptables, but after TASK-332 all compose ports are"
echo "!!! bound to 127.0.0.1 anyway. Keep this ssh session open and verify a"
echo "!!! second ssh connection works AFTER enabling."
echo
if [ "$APPLY" -eq 1 ]; then
    ufw --force enable
    ufw status verbose
else
    echo "[dry-run] ufw --force enable"
fi
echo

# ---------------------------------------------------------------------------
# 3. Daily DB backup cron
# ---------------------------------------------------------------------------
echo "--- 3. Daily DB backup cron (03:00, keeps last 7) ---"
SETUP_CRON="$SHELFGUARD_DIR/infra/scripts/setup-backup-cron.sh"
if [ -f "$SETUP_CRON" ]; then
    # setup-backup-cron.sh is idempotent (grep -v backup-db before re-adding).
    # It must run as the user owning ~/shelfguard so cron + docker exec work.
    if [ "$APPLY" -eq 1 ]; then
        run bash "$SETUP_CRON"
    else
        echo "[dry-run] bash $SETUP_CRON"
        echo "          (installs: 0 3 * * * $SHELFGUARD_DIR/infra/scripts/backup-db.sh)"
    fi
else
    echo "WARN: $SETUP_CRON not found — adjust SHELFGUARD_DIR" >&2
fi
echo

# ---------------------------------------------------------------------------
# 4. Postgres rebind guidance (PRINT ONLY — manual judgement required)
# ---------------------------------------------------------------------------
cat <<'EOF'
--- 4. MANUAL STEP (not executed): rebind shelfguard_postgres to loopback ---

The postgres container is EXTERNAL to docker-compose and currently publishes
0.0.0.0:5434 → internet-reachable. Data lives in the named volume
shelfguard_pgdata, so removing the container is safe, but do this manually
during a maintenance window:

  # 1. Inspect current container to confirm image/volume/network before removal:
  docker inspect shelfguard_postgres \
    --format 'image={{.Config.Image}} binds={{.HostConfig.Binds}} mounts={{json .Mounts}} env={{json .Config.Env}}'

  # 2. Stop API/worker first so nothing writes mid-switch:
  cd ~/shelfguard && docker compose -f docker-compose.production.yml stop api worker

  # 3. Recreate postgres bound to loopback (SAME volume => no data loss):
  docker stop shelfguard_postgres
  docker rm shelfguard_postgres
  docker run -d --name shelfguard_postgres --restart unless-stopped \
    -p 127.0.0.1:5434:5432 \
    -v shelfguard_pgdata:/var/lib/postgresql/data \
    -e POSTGRES_USER=shelfguard -e POSTGRES_PASSWORD=<from .env> -e POSTGRES_DB=shelfguard \
    postgres:16-alpine
    # ^ verify image tag, env and any extra networks against step 1 output
    #   (if the old container was attached to a compose network, re-attach:
    #    docker network connect <network> shelfguard_postgres)

  # 4. Restart the stack and verify:
  docker compose -f docker-compose.production.yml start api worker
  docker exec shelfguard_postgres pg_isready -U shelfguard
  curl -sk https://api.agrusystems.pp.ua:8443/health || curl -s http://127.0.0.1:5100/health

  # 5. From an EXTERNAL machine confirm the port is closed:
  nc -vz 93.127.143.98 5434   # must now fail/timeout

EOF

echo "=== Done ($( [ "$APPLY" -eq 1 ] && echo APPLIED || echo 'dry-run — re-run with --apply' )) ==="
