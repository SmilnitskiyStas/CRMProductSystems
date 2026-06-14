# TASK-044 — CI/CD (GitHub Actions) + PostgreSQL автоматичні бекапи

**Agent:** devops-engineer
**Date:** 2026-06-14
**Status:** review

---

## What was done

### Part 1 — GitHub Actions CI (`.github/workflows/ci.yml`)

Created a CI workflow that triggers on every PR to `main`.

Four independent parallel jobs:

| Job | Runner | Steps |
|---|---|---|
| `backend-ci` | ubuntu-latest | dotnet 8.x · restore → build → test (xunit via `ShelfGuard.Tests`) |
| `frontend-ci` | ubuntu-latest | Node 20.x · npm ci → tsc --noEmit → npm run lint |
| `worker-ci` | ubuntu-latest | Node 20.x · npm ci → tsc --noEmit |
| `mobile-ci` | ubuntu-latest | Node 20.x · npm ci → tsc --noEmit (no gradlew) |

- `concurrency` group `ci-${{ github.ref }}` with `cancel-in-progress: true` — old runs cancelled on new push to the same branch.
- npm caches keyed by each sub-project's `package-lock.json`.
- Frontend: no `npm run build` (requires env vars, too slow for CI).
- Mobile: no Android SDK / gradlew.

### Part 2 — PostgreSQL backups (`infra/scripts/`)

**`backup-db.sh`**
- `set -euo pipefail` — aborts on any error.
- Dumps via `docker exec shelfguard_postgres pg_dump -U shelfguard shelfguard | gzip`.
- Saves to `/home/administrator/shelfguard/backups/shelfguard_YYYYMMDD_HHMMSS.sql.gz`.
- Keeps only the 7 most recent files (old ones auto-deleted with `ls -t | tail -n +8 | xargs rm`).
- Prints timestamped confirmation with file size.

**`setup-backup-cron.sh`**
- Run once on the production server.
- Sets `backup-db.sh` executable.
- Installs cron entry: `0 3 * * *` (daily at 03:00 server time).
- Appends stdout/stderr to `/home/administrator/shelfguard/backups/backup.log`.
- Replaces any existing `backup-db` cron line (idempotent).

### Part 3 — deploy.sh

Reviewed `deploy.sh` in repo root — no changes needed. CI and deploy are independent (CI is PR-only, deploy is manual via `bash deploy.sh` on server).

---

## Files created / changed

| File | Action |
|---|---|
| `.github/workflows/ci.yml` | Created |
| `infra/scripts/backup-db.sh` | Created |
| `infra/scripts/setup-backup-cron.sh` | Created |

---

## Manual steps after merge

1. On the production server, run once:
   ```bash
   bash /home/administrator/shelfguard/infra/scripts/setup-backup-cron.sh
   ```
2. Verify with: `crontab -l | grep backup`
3. Test a manual backup run: `bash /home/administrator/shelfguard/infra/scripts/backup-db.sh`

---

## Notes

- Backups run on-server via cron, NOT via GitHub Actions (no secrets for DB needed in CI).
- GitHub Actions only needs the public repo — no secrets required for CI jobs.
- If GitHub Secrets are added later for SSH deploy automation (TASK-044+), add a `deploy` job gated on all CI jobs passing.
