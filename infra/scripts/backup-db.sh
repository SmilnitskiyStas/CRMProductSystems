#!/bin/bash
# Daily PostgreSQL backup — runs via cron on production server
# Keeps last 7 daily backups
set -euo pipefail

BACKUP_DIR="/home/administrator/shelfguard/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/shelfguard_$TIMESTAMP.sql.gz"

mkdir -p "$BACKUP_DIR"

# pg_dump via docker exec
docker exec shelfguard_postgres pg_dump -U shelfguard shelfguard | gzip > "$BACKUP_FILE"

# Keep only last 7 backups
ls -t "$BACKUP_DIR"/shelfguard_*.sql.gz | tail -n +8 | xargs rm -f 2>/dev/null || true

echo "[$(date)] Backup OK: $BACKUP_FILE ($(du -sh "$BACKUP_FILE" | cut -f1))"
