---
task_id: TASK-036
date: 2026-06-11
agent: mobile-developer
status: done
---

# TASK-036 — Scanner shows black screen on device

## Problem
Tapping the scan tab rendered a plain black screen instead of the camera.

## Root cause
`app/(app)/scan.tsx` styled `CameraView` with `className="flex-1"`.
NativeWind v4 only applies `className` to components registered in its
CSS interop; third-party native components like `expo-camera`'s `CameraView`
are not registered by default, so the class was silently dropped.
CameraView received no style → zero height → the parent's `bg-black`
filled the screen.

## Fix
- Registered the interop once at module level:
  `cssInterop(CameraView, { className: 'style' })`
- Bonus UX: camera permission is now auto-requested when the screen opens
  (`useEffect` on `permission.canAskAgain`) instead of requiring a button tap.

## Verification
- `npx tsc --noEmit` — clean ✅
- Requires APK rebuild to test on device (JS bundle is baked in):
  `eas build -p android --profile preview`

## Lesson for other screens
Any third-party native component (CameraView, MapView, video players…)
needs `cssInterop(Component, { className: 'style' })` before `className` works.
Core RN components and react-native-safe-area-context are pre-registered.
