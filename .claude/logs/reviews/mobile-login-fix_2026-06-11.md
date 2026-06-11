# QA Review — Mobile APK: login fails on device
**Agent:** qa-tester
**Date:** 2026-06-11
**Symptom:** APK installs and opens, but authorization does not work.

## Root causes (two, both build-time)

### 1. API URL baked as localhost
`mobile/lib/api-client.ts` falls back to `http://localhost:5000/api` when
`EXPO_PUBLIC_API_URL` is unset. Only `.env.example` exists in the repo and
`eas.json` had no `env` block — so the EAS build baked the localhost fallback.
On a phone, localhost is the phone itself → every request fails with a network error.

### 2. Cleartext HTTP blocked by Android
The production API is plain `http://`. Android 9+ release builds block cleartext
traffic by default, so even with the correct URL all requests would be rejected
(`CLEARTEXT communication not permitted`).

## Verification of the correct external URL
- `http://93.127.143.98:10053/api/auth/login` → 401 on bad creds = reachable API ✅
  (10053 → api:5100; 10052 → web:3100 — confirmed via server `.env`:
  `NEXT_PUBLIC_API_URL=http://93.127.143.98:10053`)
- Mobile `LoginResponse` type (`{accessToken, user}`) matches backend contract ✅
- Token storage via expo-secure-store + Bearer interceptor — correct ✅

## Fix (commit pushed)
| File | Change |
|---|---|
| `mobile/eas.json` | `env.EXPO_PUBLIC_API_URL = "http://93.127.143.98:10053/api"` in all 3 build profiles |
| `mobile/app.json` | added `expo-build-properties` plugin with `android.usesCleartextTraffic: true` |
| `mobile/package.json` | + `expo-build-properties ~56.0.18` (SDK 56-matched via `expo install`) |

`npx expo config --type prebuild` resolves cleanly with `usesCleartextTraffic: true`.

## Required action
Re-run the build (config is baked at build time — reinstalling the old APK won't help):
```bash
cd mobile
eas build -p android --profile preview
```

## Follow-up (v1.x)
Move the API behind HTTPS (domain + Let's Encrypt on the VPS), then remove
`usesCleartextTraffic` and switch `EXPO_PUBLIC_API_URL` to `https://`.
Cleartext HTTP is acceptable only for demo/testing.
