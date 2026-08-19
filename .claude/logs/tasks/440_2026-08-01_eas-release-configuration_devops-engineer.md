# TASK-440 — EAS environments and release configuration

**Date:** 2026-08-01  
**Agent:** devops-engineer  
**Status:** review / blocked_credentials_assets_builds

## Delivered

- `mobile/eas.json`: development, preview, and production EAS environments with isolated matching
  update channels, remote build-number ownership/auto-increment, internal preview APK, Android AAB
  production default, and the approved production API for preview/production.
- `mobile/app.json`: Android+iOS identity `com.shelfguard.mobile`, phone-only iOS policy,
  portrait-only orientation, app-version runtime policy, Expo Updates project URL, explicit native
  version seeds, and HTTPS-only Android traffic.
- Removed Android `RECORD_AUDIO` and disabled camera-plugin microphone declarations after a scoped
  source/dependency search found no audio/microphone feature. Barcode camera permission remains.
- Aligned Expo patches to `expo ~56.0.18` and `expo-router ~56.2.17`; installed compatible
  `expo-system-ui` required by `userInterfaceStyle`.
- Added `mobile/RELEASE.md` with environment matrix, commands, version/update policy, permissions,
  security rules, and exact release gates.
- Deleted tracked machine-local `mobile/.expo/README.md`; `mobile/.gitignore` already ignores
  `.expo/` and generated native directories.

## Verification

- `npx expo config --type public`: PASS; Android+iOS, portrait, IDs, runtime/update URL confirmed.
- `npx expo config --type introspect`: PASS; Android microphone is represented as a removal rule,
  Expo Updates enabled with runtime `1.0.0`, Android activity portrait.
- `npm run type-check`: PASS.
- `npm run lint`: PASS, 0 errors / 12 pre-existing warnings.
- `npm run test:ci`: PASS, 21 suites / 96 tests.
- Android Expo export: PASS (2,157 modules, Hermes bundle).
- Expo Doctor: 20/21. Package alignment and system-ui findings are fixed. The remaining warning is
  generated `.expo` state being tracked in the current Git index; the tracked README is deleted and
  `.expo/` ignored, so this is commit-dependent rather than a runtime/config failure.

No remote EAS build, signing-credential generation, update publish, store submit, or deployment was
performed. No secret was added.

## Open gates

No `mobile/assets/` source artwork exists. Generated Android launcher/splash resources are not a
cross-platform design source. Product/design must approve app/adaptive/splash/notification assets.
The release owner must provision Apple and Google signing/store access and authorize remote builds.
TASK-440 cannot be `done` until release APK/AAB and IPA builds complete and install smoke evidence
is recorded.
