# TASK-731 Mobile shelf check (AI planogram scan)
- New: mobile/features/shelf-check/{types,prepareImage}.ts, api/shelfCheckApi.ts, hooks/useShelfCheck.ts, components/ScanResultView.tsx; mobile/app/(app)/shelf-check/{index,capture}.tsx
- Wired: _layout hidden screens, navigation/policy.ts route rule (merchandiser + managers, module inventory), More menu entry "Перевірка полиць". Scan button hidden unless store_manager+/provider.
- New deps: expo-image-picker ~56.0.25, expo-image-manipulator ~56.0.26 (+app.json plugin) -> needs new APK build.
- Photo: CameraView quality 0.7 or gallery -> manipulator resize longest side 1600, JPEG 0.7 -> multipart POST.
- Build: tsc --noEmit and eslint clean. Not verified on device / against live backend.
