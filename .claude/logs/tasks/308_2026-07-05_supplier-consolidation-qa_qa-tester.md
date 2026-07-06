# TASK-308 — Supplier consolidation + roles/tasks: end-to-end QA

**Agent:** qa-tester
**Date:** 2026-07-05
**Plan:** `calm-singing-marble.md` (TASK-305/306/307)
**Status:** done — 2 bugs found (1 critical, 1 medium/data-quality, both handed off)

## Environment

- Docker stack (postgres:5435, redis:6380, mosquitto:1884, worker) — already up, healthy.
- Backend started fresh for this session: `dotnet run --project ShelfGuard.Api` → `http://localhost:5000`.
  Migrations already applied ("No migrations were applied. The database is already up to date.").
  Left running at end of session.
- Frontend: pre-existing dev server on `http://localhost:3000` (via preview tooling), reused as-is.
- Login: `admin@shelfguard.local` / `password` (seeded provider). Demo password for all seeded users is `password`.

## What was tested

### 1. Single supplier-creation path — PASS
- `POST /api/admin/marketplace/suppliers` → 404 (route gone), confirmed removed.
- Created new supplier tenant via `POST /api/provider/tenants` (businessType=supplier) → appears
  immediately in `GET /api/provider/tenants`.
- Added admin user via `POST /api/provider/tenants/{id}/users` with `role=supplier_admin` (frontend
  `AddTenantUserModal` auto-suggests this — confirmed in source, single-option dropdown for supplier
  tenants). Logged in as the new user, `/api/supplier-cabinet/profile` and `/staff` both resolved
  correctly (cabinet available immediately, profile auto-created).

### 2. Migrated orphan suppliers (supplier-alpha, supplier-beta) — PASS
- Both visible as independent, active, `businessType=supplier` tenants in
  `GET /api/provider/tenants`. DB check confirms `IsOwnerManaged=true` on both `supplier_profiles`,
  and no tenant remains pointed at `platform-marketplace` (0 rows — that system tenant is fully gone,
  cleanup from Part 1 already done).
- Added a new admin user to `supplier-alpha` via the same endpoint, logged in, confirmed
  `/api/supplier-cabinet/profile` returned full historical data intact: region "Kyiv", categories
  `["dairy","bakery"]`, website, delivery regions, working hours, payment terms, rating metric — all
  preserved from before migration. `GET /items` returned 2 pre-existing items correctly.
- Reply-to-review regression checked on alpha's existing review — see item 5 below.

### 3. Supplier staff roles — PASS with 2 bugs (see Bugs)
- Created custom role via `POST /api/supplier-cabinet/roles` with `permissions:
  ["catalog_management","client_reviews"]` — succeeded.
- Invited staff via `POST /api/supplier-cabinet/staff` with `supplierRoleId` set — permissions
  resolved correctly to `{"catalog_management":true,"client_reviews":true}` on the new user.
- Logged into frontend as the restricted staff user (browser, via preview tooling, real login
  form — not token injection, see methodology note): Sidebar correctly shows ONLY "Мої товари" and
  "Відгуки" — no Профіль/Завдання/Команда. Verified via accessibility snapshot, not just visually.
- Delete-guard confirmed: `DELETE /roles/{id}` on a role assigned to staff →
  `400 {"error":"Cannot delete a role currently assigned to staff. Reassign them first."}` — matches
  spec exactly.
- Duplicate role name guard confirmed: `400 {"error":"Role '...' already exists."}`. Invalid
  permission string on create → `400 {"error":"Unknown permission(s): ..."}`. Invite with bogus
  `supplierRoleId` → `400 {"error":"Role not found."}`. All correct.
- Backend does not enforce granular permissions per-endpoint (confirmed intentionally, per handoff
  305/306 — UI-level gate only, base access still gated by `AppPolicies.SupplierCabinet`). Restricted
  staff could still hit `/tasks`, `/staff`, `/roles` (200) despite lacking `task_board`/
  `staff_management`. This matches documented convention for the API layer, not a bug by itself —
  **but see Bug #3**: the frontend pages for these areas have no equivalent guard either, so the
  combination is a real gap (sidebar hides the link, but the page underneath is wide open to anyone
  who navigates to the URL directly).

### 4. Task board — PASS with 1 critical bug (see Bugs)
- Created tasks with different clients, assignees, statuses via API — list, `assignedToMe=true`,
  and `clientTenantId` filters all work correctly (verified against 7 tasks across 2 client
  tenants and 2 assignees).
- Status transition verified: `completed` sets `completedAt`; moving to `in_progress` afterward
  clears `completedAt` back to `null`. Invalid status value rejected with clear error message.
- Denormalized `clientTenantName`/`assignedToUserName` resolve correctly (including Cyrillic names).
- **Bug found:** creating/updating a task with a `dueDate` throws an unhandled 500 — see Bug #1
  (critical). Reproduced both via raw API and via the actual `TasksBoard.tsx` "Нове завдання" modal
  in-browser (network trace: `POST /api/supplier-cabinet/tasks → 500`, user sees generic "Помилка
  збереження" toast, task is silently not created — transaction rolled back cleanly, no orphan row).

### 5. Regression — PASS
- Items: create via `POST /api/supplier-cabinet/items` — works, returns full DTO.
- Profile: `PUT /api/supplier-cabinet/profile` — updates persist correctly.
- Reviews reply: `PUT /api/supplier-cabinet/reviews/{id}/reply` (note: PUT, not POST) — works,
  `replyText`/`repliedAt` persist and round-trip correctly on re-fetch.
- Cyrillic rendering: confirmed correct in-browser (screenshots show proper Ukrainian text
  everywhere) — terminal/curl showing `?????` for Cyrillic was purely a shell display encoding
  artifact, NOT a real bug, except for one specific case — see Bug #2.

## Bugs found

### Bug #1 (critical): Task due date crashes task creation/update with 500
Severity: critical
Task: TASK-306/307 (backend: `SupplierTaskService`, frontend: `TasksBoard.tsx`)

Steps:
1. As a supplier_admin, `POST /api/supplier-cabinet/tasks` with body including
   `"dueDate":"2026-07-10"` (a date-only ISO string, no time/offset — exactly what a native
   `<input type="date">` produces).
2. Observe `500 Internal Server Error`.

Expected: task is created with the given due date.

Actual: unhandled `DbUpdateException` →
`System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type
'timestamp with time zone', only UTC is supported.` The JSON-deserialized `DateTime` has
`Kind=Unspecified`; Npgsql refuses to write it to the `timestamptz` column.

Reproduced identically through the real UI: opening "Нове завдання" on `/supplier/tasks`, filling
the "Дедлайн" date field, and submitting shows a generic "Помилка збереження" toast — the task is
not created (confirmed no orphan row in `supplier_tasks`), and the user gets no actionable error
message.

Root cause location: `backend/ShelfGuard.Application/Features/Marketplace/SupplierTaskService.cs`,
`CreateAsync` line 63 (`DueDate = request.DueDate`) and `UpdateAsync` line 91
(`task.DueDate = request.DueDate`) — the incoming `DateTime?` is assigned directly with no
`DateTime.SpecifyKind(..., DateTimeKind.Utc)` normalization. `SupplierTask.DueDate` is a plain
`DateTime?` (`backend/ShelfGuard.Domain/Entities/SupplierTask.cs:18`) mapped to `timestamptz`.

Suggested fix: normalize in the service before assignment, e.g.
`request.DueDate.HasValue ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) :
(DateTime?)null`, in both `CreateAsync` and `UpdateAsync`. Alternatively store `DueDate` as a
`DateOnly`/`date` column since it's conceptually a calendar date, not a timestamp (would also sidestep
timezone-shift-by-one-day issues at the UI boundary) — worth a design call, but the immediate fix is
the `SpecifyKind` normalization to unblock the feature.

Also affects `PUT /tasks/{id}` (update) via the same code path.

### Bug #2 (medium, pre-existing/data-quality — informational, may be out of scope for TASK-305/306/307)
Severity: medium (data quality, not a defect in this feature's code)
Task: pre-dates TASK-305 (orphan supplier seed data)

The `Name` column for both migrated orphan tenants (`supplier-alpha`, `supplier-beta`) — and the
underlying `suppliers.Name` they were copied from — contains literal `?` (0x3F) bytes instead of the
original Cyrillic company names. Confirmed via `encode(name::bytea,'hex')` in psql — this is real
stored corruption, not a terminal/psql display artifact (other Cyrillic data in the same DB, e.g.
`Свіжий Кут`, review comments, renders correctly everywhere, including in-browser and via `/tasks`
API responses).

The `20260705171004_MigrateOrphanSuppliersToTenants` migration copies `suppliers."Name"` verbatim —
it does not introduce the corruption, it just carries forward pre-existing bad data from whatever
originally seeded `supplier-alpha`/`supplier-beta`.

Visible impact: provider's Suppliers list and the task board's client-tenant display both show
`????????????` for these two suppliers' names instead of real names.

Not blocking TASK-305/306/307 acceptance (the migration mechanism itself is correct), but worth a
follow-up ticket to fix the source data (re-enter the correct names for these two tenants) since it's
now user-visible in the provider panel.

### Bug #3 (medium, frontend): no page-level permission guard on `/supplier/team` and `/supplier/tasks`
Severity: medium
Task: TASK-307 (frontend)

Steps:
1. Log in as a supplier staff user whose role permissions are `["catalog_management",
   "client_reviews"]` only (no `staff_management`, no `task_board`).
2. Confirm the sidebar correctly hides "Команда" and "Завдання" (it does — verified via
   accessibility snapshot).
3. Manually navigate the browser to `/supplier/team` (or `/supplier/tasks`) directly (address bar,
   bookmark, shared link, etc.).

Expected: page redirects away or shows an access-denied state, since the user's permission set
excludes this area.

Actual: the page renders fully and is fully functional — `/supplier/team` shows the staff list,
"Запросити співробітника" invite button, and the roles panel with "Нова роль"; `/supplier/tasks`
shows the complete task board with "Нове завдання", status dropdowns per task, and both filters.
The user can invite staff, create/edit/delete roles, and create/edit tasks despite lacking the
permissions that are supposed to gate this.

Since the backend intentionally does not enforce these permissions per-endpoint (by design, mirrors
`ProviderRole` convention — see item 3 above), the frontend page component was the only remaining
place this could be gated, and it isn't. The sidebar-only filtering is not sufficient on its own.

Root cause confirmed by reading source: both pages only guard on base role, not on granular
permissions —
`frontend/app/(dashboard)/supplier/team/page.tsx:11` and
`frontend/app/(dashboard)/supplier/tasks/page.tsx:10`:
```tsx
if (me && !hasRole(me.role, SUPPLIER_ONLY)) { ... "Доступ лише для адміністраторів постачальника." }
```
This only checks `me.role === "supplier_admin"` (true for every supplier-tenant user regardless of
their `permissions` dict) — it never checks `me.permissions` for `staff_management` (team page) or
`task_board` (tasks page). Since permissions, not roles, are what's supposed to gate these areas
(per TASK-307), this check is the wrong guard for this purpose.

Suggested fix: add a permission check alongside the existing role check in both pages, using the
same `supplierEffectivePermissions`/`resolveSupplierPermissions` helper from
`frontend/lib/supplierPermissions.ts` that `Sidebar.tsx` already uses to decide visibility — e.g.
render the same "Доступ лише..." message (or a more specific one) when
`!effectivePermissions.staff_management` / `!effectivePermissions.task_board`, remembering that
`permissions === null` means full/owner access (existing convention, don't break that case).

## Verdict

Feature TASK-305/306/307 is functionally complete and ready **except** Bug #1 (blocking) and Bug #3
(should fix before shipping — real authorization gap, low complexity to fix). Everything else —
single supplier creation path, orphan migration, custom roles/permissions backend validation, task
filters, status/completedAt lifecycle, sidebar nav filtering, and regression on items/reviews/profile
— passed cleanly.

Recommend: fix Bug #1 (backend) and Bug #3 (frontend) before shipping — both small, well-localized
fixes. File Bug #2 as a separate low-priority data-cleanup ticket.

## Note on session continuity

This QA pass picked up mid-stream from an earlier attempt at this same task earlier in the day
(same DB, same scratchpad — found `QA Test Supplier` tenant, `qa-supplier-admin@test.local`,
`qa-content-mgr@test.local`, and several test roles/tasks already in place). Reused those fixtures
rather than creating duplicates; reset both accounts' passwords to the seed default hash (dev DB
only, `UPDATE users SET "PasswordHash" = '<seed hash for "password">' WHERE "Email" IN (...)`) since
the original passwords set by the earlier pass weren't recoverable. Also reset
`alpha@supplier.local` the same way to test the migrated supplier-alpha tenant end to end.

Browser auth is cookie (`sg_session`) + `localStorage.sg_token` hybrid — directly injecting a token
into `localStorage` gets silently overridden by a refresh flow tied to the session cookie. Had to
log out via the actual UI "Вийти" button and log back in through the real form to reliably switch
test users in the browser for permission-based UI checks.
