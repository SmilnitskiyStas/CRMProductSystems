# TASK-435 — physical-device QA resume

**Date:** 2026-08-01  
**Agent:** qa-tester (Codex)  
**Scope:** `mobile/` and `.claude/` only  
**Device:** realme RMX2063, Android 11/API 30, serial `13cb6660`  
**Build:** installed ShelfGuard SDK 56 development client, current-source Metro on port 8082  

## Results

### TASK-437 offline cold bootstrap — PASS

An online authenticated store-manager dashboard was confirmed. Original connectivity was Wi-Fi ON
and mobile data OFF. Wi-Fi was disabled, then the app was force-stopped and reconnected to the
current Metro bundle with both phone transports off. Bootstrap showed only the offline retry state,
explicitly said the session was saved, withheld private UI, and did not return to auth choice.

Wi-Fi was restored; API-host reachability was proven from the device. Tapping Retry restored the
same manager dashboard without login. Final connectivity matched the original Wi-Fi ON/mobile data
OFF state. Focused logcat contained no ShelfGuard fatal, React Native JS error, SecureStore error,
or navigation-context crash.

### TASK-438 live TOTP — NOT TESTED

No current six-digit authenticator value or TOTP seed was supplied. No value was guessed/submitted
and no recovery code was used.

### TASK-443 POS durability — BLOCKED

Manager POS still displays `Зміна не відкрита`. Open shift was not tapped. No shift,
cart, sale, customer selection, payment, or other server mutation occurred. Acceptance requires an
already-open safe test shift.

### TASK-444 operation drafts — PARTIAL / BLOCKED BY FIXTURES

- Write-off create opens but requires scanning a real product before a draft line exists. No item
  was scanned and no draft was created.
- Production is absent from normal manager navigation and its guarded direct link renders the
  standard disabled-module state. No production draft or mutation was created.
- Transfer draft acceptance remains the previously recorded pass.

One write-off detail error state was observed once, but the exact identifier/path was not proven
and reproduction was interrupted; it is not classified as a defect yet.

## Safety

No credentials, tokens, OTPs, recovery codes, or secrets were recorded. No business mutation was
performed. No local draft marker was introduced. Connectivity was restored before the final
prerequisite checks.

At the end, the device/ADB transport stopped responding during a second write-off navigation
reproduction attempt; even `adb devices` timed out. The attempt therefore remains inconclusive and
no defect is filed from it. The owned Metro process reached its bounded timeout and port 8082 has
no listener. Because ADB was unresponsive, the task-owned `tcp:8082` reverse could not be queried or
removed in this run. Connectivity had already been restored and verified before the transport
issue. Do not kill the shared ADB daemon; reconnect/unlock the phone, recheck `adb devices`, then
remove only `tcp:8082` if it is still listed.

TASK-435 cannot be marked done yet. Exact remaining acceptance blockers are: current live TOTP;
an already-open safe POS shift; a safe write-off product/barcode; activated production plus safe
recipe/components; the receipt-create contract; and the broader still-unexecuted baseline matrix
flows recorded in the main report.

## Read-only baseline continuation

The current manager session was restored after one transient development-client stream failure;
the immediate warm retry passed. No server mutation was performed.

| Flow | Result | State / evidence |
|---|---|---|
| Dashboard | pass | Existing manager identity and stock summary rendered. |
| Stock list | pass / empty | Filters rendered; `Партій не знайдено`; no detail available. |
| Receipts list | pass / empty | `Прийомок не знайдено`; no detail available. |
| Customers list | pass / empty | `Клієнтів ще немає`; create action not used. |
| Service Desk list | pass / empty | My/All tabs and `Немає тікетів`; create not used. |
| Schedules | pass / empty | Weekly view and `Немає змін на цей тиждень`. |
| AI assistant | pass / idle | Screen and suggestions rendered; no prompt sent. |
| Marketplace | pass | Visible list plus an existing supplier detail/catalog rendered; Back passed. |
| Auto-service | not available | Not offered in current manager navigation/module context. |
| Notifications | inconclusive | Attempt opened the development-client menu instead of the list; no notification was mutated. |

Android Back returned safely across the exercised list/assistant screens. Existing datasets were
empty, so stock/receipt/customer/ticket detail flows could not be tested without guessing IDs.

Focused remainder: Marketplace list and the existing BioTech USA supplier detail/catalog pass
read-only. Notifications remains inconclusive: two attempts at the top-right control opened SDK 56
development-client tools rather than the app list; no notification state was changed.

### Notifications static-route closure

After closing the development-client tools overlay, the authorized static deep link
`shelfguard://notifications` reached the authenticated notification screen. The list rendered with
`50 непрочитаних` and existing `weekly_report` / `stock.expiry_critical` entries and dates.
The `Всі прочитані` mutation was not tapped, and no item was opened. Route authentication,
list rendering, unread count/badge data, and non-empty state therefore pass. Pagination/end-of-list
and pull-to-refresh were not exercised. Android Back was sent, but the follow-up UI dump timed out,
so Back confirmation for this exact route is inconclusive rather than claimed pass.

Final cleanup: owned Metro PID `40052` was stopped and port 8082 has no listener. ADB became
unresponsive while removing the task-owned reverse, so removal and final connectivity could not be
queried. The last verified state remains Wi-Fi ON/mobile data OFF. The shared ADB daemon was not
killed; the app and its data remain installed.
