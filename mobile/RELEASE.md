# ShelfGuard Mobile release configuration

TASK-440 configures Expo/EAS for the first phone release. Android and iOS are supported,
tablet layouts are deferred, and every screen (including POS) is portrait-only.

## Environments

| EAS profile | Distribution | Update channel | API |
| --- | --- | --- | --- |
| `development` | internal development client | `development` | production API until a dedicated development endpoint is approved |
| `preview` | internal APK / iOS internal build | `preview` | `https://api.agrusystems.pp.ua:10054/api` |
| `production` | Android App Bundle / iOS archive | `production` | `https://api.agrusystems.pp.ua:10054/api` |

The API URL is public configuration, not a secret. Preview and production intentionally use the
approved production endpoint. Secrets and signing credentials must be managed by EAS/Apple/Google
credential stores and must never be committed.

## Version and update policy

- User-visible version is `expo.version` in `app.json`.
- `runtimeVersion` follows `appVersion`, so native-incompatible releases receive a new runtime when
  the user-visible app version changes.
- EAS remotely owns build numbers and auto-increments every profile build.
- Update channels are isolated as `development`, `preview`, and `production`.
- Android package and iOS bundle ID are both `com.shelfguard.mobile`.

## Commands

Run from `mobile/`:

```bash
npx expo config --type public
npx expo-doctor
npm run type-check
npm run lint
npm run test:ci
npx expo export --platform android --output-dir .expo-export-check

npx eas-cli build --platform android --profile preview
npx eas-cli build --platform ios --profile preview
npx eas-cli build --platform android --profile production
npx eas-cli build --platform ios --profile production
```

The EAS build commands are documented only; TASK-440 does not authorize remote builds, credential
generation, store submission, deployment, or publishing.

## Permissions

Camera access is retained for barcode scanning. Android audio recording is explicitly blocked:
the mobile source has no audio recording or microphone feature. Notifications may add the Android
notification permission appropriate to the target OS through `expo-notifications`.

## Release blockers

No source artwork exists under `mobile/assets/`. The generated Android native tree contains only
previously generated launcher and splash resources, which are not suitable source assets for
cross-platform EAS prebuilds. Before store builds, product/design must provide and approve:

- 1024 x 1024 square app icon (PNG, no transparency for iOS);
- Android adaptive icon foreground plus approved background color/image;
- splash-screen logo and approved background color;
- monochrome Android notification icon.

Apple distribution credentials, App Store Connect access, Android upload signing credentials,
Google Play Console access, and final store metadata/privacy declarations are also required for
live release builds and submission.
