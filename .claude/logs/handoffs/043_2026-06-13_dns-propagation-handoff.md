# Handoff: TASK-043 — SSL Activation (pending DNS propagation)

**From:** devops-engineer  
**To:** administrator / devops-engineer  
**Date:** 2026-06-13

---

## Situation

TASK-043 is fully implemented. All config files are in place, containers rebuilt with HTTPS URLs. The only remaining step is **running one sudo command on the server** once DNS propagates.

## DNS Status

`agrusystems.pp.ua` currently resolves to `135.181.41.169` (wrong — old registrar default).  
Must resolve to `93.127.143.98` (the production server).

**Check DNS propagation:**
```bash
nslookup agrusystems.pp.ua 8.8.8.8
# or
host agrusystems.pp.ua
```
Expected: `address 93.127.143.98`

**If DNS is still wrong:** Log into uadns.com and verify/recreate A records:
- `agrusystems.pp.ua` → `93.127.143.98`
- `www.agrusystems.pp.ua` → `93.127.143.98`  
- `api.agrusystems.pp.ua` → `93.127.143.98`

## Once DNS propagates — run this

```bash
ssh -i ~/.ssh/workmate-deploy -p 10048 administrator@93.127.143.98
sudo bash ~/shelfguard/infra/nginx/install-nginx-ssl.sh
```

This will:
1. Install certbot
2. Get Let's Encrypt cert for all 3 subdomains
3. Enable HTTPS nginx vhost
4. Set up auto-renewal cron

## Verification checklist

- [ ] `curl -I https://agrusystems.pp.ua` → 200, `Strict-Transport-Security` header present
- [ ] `curl -I http://agrusystems.pp.ua` → 301 redirect to HTTPS
- [ ] `curl -I https://www.agrusystems.pp.ua` → 301 redirect to `https://agrusystems.pp.ua`
- [ ] `curl -I https://api.agrusystems.pp.ua/health` → 200
- [ ] Mobile APK rebuild needed: `cd mobile && eas build --platform android --profile preview`

## Mobile rebuild note

The `eas.json` now points to `https://api.agrusystems.pp.ua/api`. A new APK build is required for mobile clients to use the HTTPS endpoint. Until then, existing APKs with the old IP will fail (as expected — cleartext is now blocked).
