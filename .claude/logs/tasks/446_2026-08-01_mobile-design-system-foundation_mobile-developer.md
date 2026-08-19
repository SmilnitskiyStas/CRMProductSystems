# TASK-446 — Mobile design-system foundation

**Agent:** mobile-developer  
**Date:** 2026-08-01  
**Status:** partial_device_pass / accessibility-and-login-smoke pending

## Delivered

- Added canonical color, typography, spacing, radius, and touch-target tokens.
- Added documented shared primitives: Screen, Header, Button, IconButton, Card, ListRow,
  TextField, SelectField, StatusBadge, EmptyState, ErrorState, Skeleton, Modal, Sheet,
  ConfirmDialog, and OfflineBanner.
- Added accessibility roles/labels/state, disabled/loading/error presentation, 44–48 px targets,
  safe-area and keyboard handling, Android modal Back handling, and font-scaling-compatible Text.
- Used static NativeWind class maps; no dynamic shadow toggle or new dependency was introduced.
- Converted only staff login, dashboard, and customers. Customers was chosen because prior device
  QA covered its read-only route and it exercises search/loading/error/empty/list states safely.
- Preserved auth/session flow, route guards, role/module visibility, React Query hooks, refresh,
  navigation destinations, and create/detail behavior.

## Files

- `mobile/components/ui/tokens.ts`
- `mobile/components/ui/index.tsx`
- `mobile/components/ui/README.md`
- `mobile/components/ui/__tests__/ui.test.tsx`
- `mobile/app/(auth)/login.tsx`
- `mobile/app/(app)/index.tsx`
- `mobile/app/(app)/customers/index.tsx`
- `mobile/features/customers/components/CustomerCard.tsx`
- `mobile/features/dashboard/components/StatusCard.tsx`
- `.claude/tasks/mobile-roadmap.md`, `.claude/tasks/current.md`, `.claude/tasks/blocked.md`

## Verification

- `npx tsc --noEmit` — PASS.
- `npm run lint` — PASS, 0 errors / 12 pre-existing unrelated warnings.
- `npx jest --runInBand --watch=false` — PASS, 21/21 suites and 96/96 tests.
- Android Expo export — PASS; temporary export removed after verification.
- Focused UI test — PASS, 4/4 tests.

## Remaining acceptance

Device visual/accessibility smoke is intentionally not claimed. On Android verify staff login
keyboard/small screen, dashboard refresh/role/module variants, customers loading/error/empty/search/
list/create/detail navigation, Android Back, large font scaling, TalkBack order/labels, touch targets,
and absence of the former css-interop navigation-context crash. No APK install, credentials, or
server mutation was performed in this task.

## Device QA attempt — 2026-08-01

Metro manifest became healthy and the Android bundle was prewarmed successfully (HTTP 200,
12,539,147 bytes) before launch. The current bundle opened without the historical css-interop or
navigation-context crash, but authenticated bootstrap correctly remained on `Немає з’єднання`.
The device had Wi-Fi enabled and mobile data disabled, while direct API-host checks returned
`Destination Host Unreachable`. A controlled Wi-Fi off/on cycle and 31 bounded checks did not
restore routing. QA did not logout or destroy the retained session, so login/dashboard/customers
visual assertions, large-font, keyboard, and accessibility-tree acceptance remain pending.

No defect is attributed to TASK-446. Final status: `review_pending_device / blocked_device_network`.
Original/final font scale is `0.9`; Wi-Fi setting is ON and mobile data OFF.

Cleanup note: owned Metro was stopped and port 8082 has no listener. The font-scale restore command
was issued first, followed by reverse removal, but ADB then became unresponsive and final device /
reverse queries timed out. Last verified settings before cleanup were font scale `0.9`, Wi-Fi ON,
mobile data OFF. Do not kill the shared ADB daemon; reconnect/unlock and remove only `tcp:8082` if
it is still listed.

## Device QA continuation — 2026-08-01

Current-source dashboard and Customers reference screens pass on the realme RMX2063 (Android 11):
safe-area layout, accessibility labels, 44 px-or-larger touch bounds, Android Back, Customers empty
state, search, clear, and return to dashboard were verified. The historical css-interop/navigation-
context crash did not recur. No business mutation was made.

The final staff-login logout/login smoke could not be completed. Before launch, ADB confirmed the
retained app, font scale `0.9`, Wi-Fi ON, mobile data OFF, and no stale reverse. Metro/reverse startup
then repeatedly failed to yield a controllable UI and ADB timed out after launch. QA did not logout,
submit credentials, clear app data, or alter the retained manager session. Large-font testing is
also not claimed because realme blocks the required `WRITE_SETTINGS` change; TalkBack was not run.

Final acceptance is therefore partial: implementation and automated checks pass, dashboard and
Customers device smoke pass, while converted staff-login keyboard/validation/login smoke, large
font, and TalkBack remain pending. Status: `partial_device_pass / accessibility-and-login-smoke pending`.
