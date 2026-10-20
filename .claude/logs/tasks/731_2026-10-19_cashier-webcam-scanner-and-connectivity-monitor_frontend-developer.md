# TASK-731 - Cashier: webcam barcode scanner + connectivity monitor

Status: review. Not committed. Backend untouched.

## Webcam scanner
- `CameraPanel` on CheckoutScreen (button "Камера (F3)", F3 hotkey, hints line): live preview, scan-frame overlay, camera selector (>1 camera), states requesting / denied (Windows privacy-toggle hint) / no camera / busy / scanning, retry. All tracks stopped on close/unmount. On/off + last camera stored in `userData/camera-prefs.json` (IPC `camera:get/savePrefs`).
- Decoder `@zxing/library` (EAN-13/EAN-8/UPC-A/Code128, TRY_HARDER only on every 3rd miss), ~9 fps from the video element downscaled to <=640 px. `ScanGate`: EAN/UPC check digits validated, 2 consecutive identical reads, same code ignored 1.5 s (sliding while it stays in view). Accepted code goes through the SAME `onScan` handler as the USB wedge (catalog lookup, not-found/no-price messages); UPC-A also tried as `0`+code. Feedback: WebAudio beep (ok/error), green/red ring, "Додано: <name>" toast.
- Main: `setPermissionRequestHandler/CheckHandler` allow only `media` video for our own window; CSP gets `media-src 'self' blob:`; mac `NSCameraUsageDescription` via electron-builder extendInfo.
- Demo material: `cashier/dev-assets/barcodes/*.png` (+README, `scripts/gen-barcodes.mjs`). API was down, so items come from the cached dev catalog: 01 bread is verified sellable; 02-04 unverified stock/expiry; 99 is an unknown code.

## Connectivity monitor (`electron/connectivity.ts`)
Single source of truth in main: probe `/health/live` (3 s timeout, ANY HTTP status = reachable), every 5 s growing to 15 s cap while offline, 30 s online, 2 failures to go offline (confirm probe after 2 s), 1 success to go online; extra probes on window focus, on any failed API call (`setNetworkErrorListener`, throttled 1/s) and via "Спробувати зараз" (`connection:retry`). Offline->online fires once: outbox flush, delta catalog sync (full when local catalog empty), shift refresh (cache replaced; renderer refetches too), session validated lazily (401 -> PIN + existing notice). Timers `unref`'d and stopped on quit. Existing `onOnlineChange` event now carries `{online, checking, nextProbeAt}`.
UI: `ConnectionBanner` (offline + countdown + button; green "Зв’язок відновлено · Синхронізація…/Синхронізовано"), Shift screen buttons enable and stale error clears on reconnect, Activation screen checks the typed URL in the background (`connection:check`, 4 s while down / 15 s up) with a readable reason.

## Verification
tsc (node+web), eslint, vitest 84 tests (decoder on generated EAN-13/EAN-8/Code128 PNGs and on every dev-asset PNG, check digits, gate, camera controller with fake MediaStream, monitor with fake timers, http reachability), build, dist OK.
Packaged smoke with Chromium fake camera fed from a .y4m containing the bread barcode + mock API: camera started through our permission handler, product added once to the cart ("Додано: ..."), panel close removed the video; app started with API down (activation hint "Сервер недоступний ... повторюється автоматично"), hint flipped to reachable ~3 s after the API appeared, banner shown after API stop and auto-recovered ~5 s after restart.

## Not verified
Real webcam / phone-screen reading (focus, glare, moiré), real Windows camera privacy prompt, real-backend start-order scenario, mac build.
