---
task_id: TASK-045
date: 2026-06-12
agent: mobile-developer
status: done
---

# TASK-045 — Mobile polish: receipts contract + profile actions

## Root cause of "empty receipt screen"
Mobile types were written against an imagined contract, not the real ReceiptDto:
- statuses `receiving/completed` vs backend `in_transit/received`
- fields `number/orderedQty/receivedQty/unit/barcode` vs backend
  (no number) `quantityOrdered/quantityReceived/productBarcode`
- supplierName non-null vs backend nullable

## Fixes
| File | Change |
|---|---|
| `features/receipt/types.ts` | Rewritten 1:1 against ReceiptDto; `receiptNumber()` derives display № from id |
| `app/(app)/receipt/index.tsx` | Status labels/colors for draft/in_transit/received/cancelled; "supplier → store" line; safe fallbacks |
| `app/(app)/receipt/[id].tsx` | Confirm button only for `draft`; **tap-to-process**: tapping an unprocessed line quick-accepts it (received = ordered via PUT /receipts/{id}/items) with hint text |
| `features/receipt/{api,hooks}` | + `processItem` / `useProcessItem` |
| `app/(app)/profile/index.tsx` | Menu wired: Сповіщення → opens @shelfguard_bot, Підтримка → Telegram/mailto chooser, Про застосунок → version+API+user alert; logout with confirm dialog; role shown in Ukrainian |

## Verified
- Backend item keys match new types exactly (checked live: 4 receipts —
  2 received / 1 draft / 1 in_transit; draft has 4 items with isProcessed)
- `tsc --noEmit` clean

## How to see it on the phone
JS-only changes — `cd mobile; npx expo start` hot-reloads into the dev build;
for the standalone APK: `npx expo prebuild -p android && cd android && .\gradlew assembleRelease`.
