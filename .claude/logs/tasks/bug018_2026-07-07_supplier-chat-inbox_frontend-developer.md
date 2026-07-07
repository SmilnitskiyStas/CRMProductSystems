# BUG-018 — Client chat messages never reach supplier (no UI inbox)

**Agent:** frontend-developer
**Status:** done
**Date:** 2026-07-07

## Root cause

`frontend/features/supplier-cabinet/components/ClientsTab.tsx` (`/supplier/clients`)
was the only entry point in the supplier cabinet that opened
`SupplierClientChatPanel`. Its client list comes from `useSupplierClients()` →
`SupplierCabinetService.GetClientsAsync`, which unions only clients who left a
review or have a task linked via `ClientTenantId` (TASK-313 design). A client who
only started a chat thread — no review, no task — never appeared in that list, so
the supplier had no UI path to see or reply, even though the message was saved
correctly server-side.

The backend endpoint `GET /api/supplier-cabinet/chat/sessions`
(`SupplierCabinetController.GetChatSessions` → `ISupplierChatService.GetSessionsAsync`)
already lists all chat threads regardless of review/task status, and the frontend
already had a working hook (`useSupplierChatSessions()` in `useSupplierCabinet.ts`)
and API call (`supplierCabinetApi.getChatSessions`) — both dead code, referenced
nowhere.

## Fix (frontend-only, no backend changes)

1. New component `frontend/features/supplier-cabinet/components/ChatInboxTab.tsx`:
   renders `useSupplierChatSessions()` as a list of threads (other tenant name,
   last message preview, last message time). Clicking a row opens the existing
   `SupplierClientChatPanel` unchanged. Empty state: "Повідомлень від клієнтів ще
   немає." Loading/error states match `ClientsTab` styling (dark theme
   #0D1117/#111827/#1F2937, accent #3B82F6).

2. Wired into `frontend/app/(dashboard)/supplier/clients/page.tsx` via a tab
   switcher ("Клієнти" / "Повідомлення") — chosen over a new route + Sidebar nav
   entry since it needed no new permission wiring and kept the existing
   `client_management` permission gate covering both tabs. Header renamed to
   "Клієнти та повідомлення".

3. `SupplierCabinetService.GetClientsAsync`, `SupplierClientChatPanel.tsx`, and all
   backend files left untouched, per instructions.

## Files changed

- `frontend/features/supplier-cabinet/components/ChatInboxTab.tsx` (new)
- `frontend/app/(dashboard)/supplier/clients/page.tsx` (tab switcher added)

## Verification

- `npx tsc --noEmit` (frontend): clean, no errors.
- `npm run build` (frontend): compiled successfully, all 48 routes generated,
  `/supplier/clients` built at 5.12 kB.
