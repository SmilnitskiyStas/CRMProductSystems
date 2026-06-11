# QA Review — Mobile EAS Build Failure (Gradle)
**Agent:** qa-tester
**Date:** 2026-06-11
**Symptom:** `Build failed: Gradle build failed with unknown error. See logs for the "Run gradlew" phase`

## Root cause
`mobile/android/` (output of a local `expo run:android` prebuild) was committed to git.
A committed `android/` directory switches EAS Build from CNG (managed) to **bare workflow**:
`expo prebuild` is skipped and the committed native project is built as-is.

That committed project carried a manually pinned **Gradle 8.13**
(downgraded on 2026-06-10 in an attempt to fix the build), while the
React Native 0.85.3 gradle plugin targets **Gradle 9.x** — the version
prebuild originally generated (9.3.1). The mismatch fails the
`Run gradlew` phase. The "Deprecated Gradle features… incompatible with
Gradle 10" line in the log is a standard warning, not the cause.

## Fix (commit 491e3e10)
1. Deleted `mobile/android/` from git and disk — it contained zero custom
   native code (template `MainActivity.kt` / `MainApplication.kt`; all config
   lives in `app.json`).
2. Added `/android` and `/ios` to `mobile/.gitignore` so prebuild output can
   never be committed again.
3. Verified `npx expo config --type prebuild` resolves all plugins cleanly —
   EAS prebuild will succeed.

EAS now regenerates the native project on every build, version-matched to
the installed `expo`/`react-native` packages.

## How to build
```bash
cd mobile
eas build -p android --profile preview
```

## Rule for the team
Never commit `mobile/android/` or `mobile/ios/`. If `expo run:android` was used
locally, the generated folders stay local. Native configuration belongs in
`app.json` (plugins, permissions, package name).
