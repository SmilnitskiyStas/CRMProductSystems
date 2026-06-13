# TASK-043 — Domain + HTTPS (Let's Encrypt) + drop cleartext from mobile

**Date:** 2026-06-13  
**Agent:** devops-engineer  
**Status:** done (pending DNS propagation for SSL activation)

---

## What was done

### 1. nginx config (`infra/nginx/shelfguard.conf`)
Created system-nginx (not containerized) virtual host config for `agrusystems.pp.ua`:
- HTTP server block for `agrusystems.pp.ua`, `www.agrusystems.pp.ua`, `api.agrusystems.pp.ua`: serves certbot ACME challenge from `/var/www/certbot`, redirects all other traffic to HTTPS.
- HTTPS server block for web (`agrusystems.pp.ua`, `www.agrusystems.pp.ua`) → `127.0.0.1:3100`; `www` → `apex` redirect included.
- HTTPS server block for API (`api.agrusystems.pp.ua`) → `127.0.0.1:5100`.
- TLS 1.2/1.3 only, HSTS 1 year, modern cipher defaults.

**Decision:** `api.agrusystems.pp.ua` subdomain (not `/api/*` path prefix) — avoids conflicts with Next.js App Router routing and is cleaner for CORS.

### 2. SSL setup script (`infra/nginx/install-nginx-ssl.sh`)
One-shot script that must be run with sudo once DNS propagates:
```bash
sudo bash ~/shelfguard/infra/nginx/install-nginx-ssl.sh
```
Steps it performs:
1. `apt-get install certbot`
2. Creates `/var/www/certbot` webroot
3. Deploys HTTP-only nginx config temporarily (for ACME challenge)
4. Runs `certbot certonly --webroot` for `agrusystems.pp.ua`, `www.agrusystems.pp.ua`, `api.agrusystems.pp.ua`
5. Swaps in full HTTPS config (`shelfguard.conf`) and reloads nginx
6. Adds cron: `30 3 * * * certbot renew --quiet --deploy-hook 'systemctl reload nginx'`

### 3. Server `.env` updated (live on server)
- `NEXT_PUBLIC_API_URL` → `https://api.agrusystems.pp.ua`
- `Cors__Origins` → `https://agrusystems.pp.ua,https://www.agrusystems.pp.ua,http://localhost:3000`

### 4. `.env.production.example` updated in repo
- Added HTTPS domain values with comments for the migration path.
- Added `Cors__Origins` line (was missing from template).

### 5. `deploy.sh` ran successfully
Rebuilt all containers with new `NEXT_PUBLIC_API_URL=https://api.agrusystems.pp.ua` baked into the Next.js bundle. All containers healthy.

### 6. Mobile — cleartext traffic removed
- `mobile/android/app/src/main/AndroidManifest.xml`: `usesCleartextTraffic="true"` → `"false"`
- `mobile/app.json`: `expo-build-properties` android `usesCleartextTraffic: true` → `false`
- `mobile/eas.json`: all three environments (`development`, `preview`, `production`) `EXPO_PUBLIC_API_URL` changed from `http://93.127.143.98:10053/api` → `https://api.agrusystems.pp.ua/api`

---

## Pending: DNS Propagation

**Current state:** DNS for `agrusystems.pp.ua` still resolves to `135.181.41.169` (old hosting/registrar default). Server IP is `93.127.143.98`.

**Required DNS records (to set at uadns.com):**
```
agrusystems.pp.ua.      A   93.127.143.98
www.agrusystems.pp.ua.  A   93.127.143.98
api.agrusystems.pp.ua.  A   93.127.143.98
```

**Once DNS propagates:**
1. SSH to server: `ssh -i ~/.ssh/workmate-deploy -p 10048 administrator@93.127.143.98`
2. Run: `sudo bash ~/shelfguard/infra/nginx/install-nginx-ssl.sh`
3. Verify: `https://agrusystems.pp.ua` loads, `https://api.agrusystems.pp.ua/health` returns 200, HTTP redirects to HTTPS.

---

## Ports remain accessible
- `3100` and `5100` remain bound on all interfaces (needed for deploy.sh health check and fallback access). They become effectively internal after nginx takes port 80/443, but are not firewalled.
- Other projects (`workmate` on port 3001, `trading` on port 10048/8080) — untouched.

---

## Files changed
- `infra/nginx/shelfguard.conf` — NEW
- `infra/nginx/setup-ssl.sh` — NEW (legacy helper, superseded by install-nginx-ssl.sh)
- `infra/nginx/install-nginx-ssl.sh` — NEW (on server only, not in repo — written directly)
- `.env.production.example` — updated NEXT_PUBLIC_API_URL + added Cors__Origins
- `mobile/android/app/src/main/AndroidManifest.xml` — usesCleartextTraffic false
- `mobile/app.json` — usesCleartextTraffic false
- `mobile/eas.json` — EXPO_PUBLIC_API_URL → HTTPS domain
