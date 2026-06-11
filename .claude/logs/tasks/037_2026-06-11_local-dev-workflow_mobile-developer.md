---
task_id: TASK-037
date: 2026-06-11
agent: mobile-developer
status: done
---

# TASK-037 — Local dev workflow (no more EAS rebuilds)

## Problem
Every JS fix required a cloud EAS build (~15-20 min, burns build limits).

## Solution: development build, compiled locally
Local toolchain verified on the dev machine: Android SDK
(`C:\Users\stass\AppData\Local\Android\Sdk`), adb, JDK 17 — EAS not needed at all.

## Changes
| File | Change |
|---|---|
| `mobile/package.json` | + `expo-dev-client ~56.0.20` |
| `mobile/.env` (local only, not committed) | `EXPO_PUBLIC_API_URL=http://93.127.143.98:10053/api` |
| `mobile/.gitignore` | + `.env`, `.env.local` |

## Workflow
**One-time (and after any native config change):**
```bash
cd mobile
npx expo run:android        # phone connected via USB, USB debugging on
```
Builds debug APK locally with Gradle, installs to phone, starts Metro.

**Daily development (zero rebuilds):**
```bash
npx expo start              # press 'a' or open the dev app on the phone
```
JS/TS changes hot-reload instantly over Wi-Fi (same network) or use `--tunnel`.

**Rebuild needed only when:** adding/removing native modules, changing
app.json plugins/permissions. JS, screens, styles, API — never.

EAS remains only for shareable/release APKs (`eas build --profile preview`).
