#!/bin/bash
# Run on production server to install daily backup cron
SCRIPT_PATH="/home/administrator/shelfguard/infra/scripts/backup-db.sh"
chmod +x "$SCRIPT_PATH"
# Daily at 03:00
(crontab -l 2>/dev/null | grep -v backup-db; echo "0 3 * * * $SCRIPT_PATH >> /home/administrator/shelfguard/backups/backup.log 2>&1") | crontab -
echo "Cron installed:"
crontab -l | grep backup
