# TASK-332 — Infrastructure security hardening

**Agent:** devops-engineer · **Date:** 2026-07-09 · **Status:** done (repo side; manual server steps below)
**Context:** security audit `.claude/logs/reviews/2026-07-09_security-audit_auth-infra.md` (issues #3, #9, #10, #11)

## Changed

- `docker-compose.production.yml` — all published ports bound to loopback:
  api `127.0.0.1:5100`, web `127.0.0.1:3100`, redis `127.0.0.1:6380`,
  mosquitto `127.0.0.1:1884`. Nothing bypasses nginx/TLS; worker
  (network_mode:host) and host nginx still reach everything via localhost.
  Redis optional auth: `command: redis-server ${REDIS_PASSWORD:+--requirepass ${REDIS_PASSWORD}}`
  — verified with `docker compose config`: unset → plain `redis-server`
  (deploy-safe before .env update), set → `--requirepass <value>`.
- `infra/nginx/shelfguard-ratelimit.conf` — NEW: `limit_req_zone shelfguard_auth`
  (10r/m per IP), http-level, intended for `/etc/nginx/conf.d/` (symlink step in header).
- `infra/nginx/shelfguard.conf` — api block: `location ~ ^/api/auth/(login|refresh|2fa)`
  with `limit_req zone=shelfguard_auth burst=10 nodelay; limit_req_status 429` and
  duplicated proxy settings (proxy_pass not inherited). Both server blocks:
  `Referrer-Policy no-referrer` + `Permissions-Policy` (always); CSP added as
  commented suggestion only (Next.js inline scripts risk).
- `infra/scripts/harden-server.sh` — NEW, idempotent, dry-run by default /
  `--apply` executes: fail2ban (sshd + nginx-limit-req jails), UFW (detects sshd
  port, allows it first, allows 8443/tcp, deny incoming, loud warning before
  enable), backup cron via existing `setup-backup-cron.sh` (daily 03:00, keeps 7).
  Prints (never executes) shelfguard_postgres loopback-rebind procedure.
  `bash -n` OK; exec bit set in git index.
- `.env.production.example` — REDIS_PASSWORD added; REDIS_URL corrected to
  `redis://:PASSWORD@localhost:6380` (was stale `localhost:6379`).
- `.claude/docs/integrations.md` — mosquitto entry notes loopback binding.

## Manual server steps (ordered, after git pull / deploy)

1. `sudo ln -sf ~/shelfguard/infra/nginx/shelfguard-ratelimit.conf /etc/nginx/conf.d/shelfguard-ratelimit.conf`
   then `sudo nginx -t && sudo systemctl reload nginx`
2. `.env`: add `REDIS_PASSWORD=<strong>` and set `REDIS_URL=redis://:<strong>@localhost:6380`,
   then `docker compose -f docker-compose.production.yml up -d` (recreates redis + worker together —
   never set one without the other or BullMQ auth fails)
3. `sudo ~/shelfguard/infra/scripts/harden-server.sh` (review dry-run) →
   `sudo ~/shelfguard/infra/scripts/harden-server.sh --apply` — keep the ssh session open,
   verify a second ssh connection after UFW enable
4. Postgres rebind to `127.0.0.1:5434` — manual maintenance-window procedure printed by
   the script (data safe in `shelfguard_pgdata` volume); verify externally with
   `nc -vz 93.127.143.98 5434` (must fail)

## Notes

- Loopback port bindings take effect on next `docker compose up -d` (containers recreated).
- `/api/auth/2fa` location prefix covers TASK-330 endpoints once backend ships them.
- CSP deliberately NOT enabled; suggested policy commented in shelfguard.conf web block.
