# TASK-309 — Fix QA-reported bugs: task dueDate 500 + missing page-level permission guards

**Agent:** main session (direct fix — small, well-localized, per CLAUDE.md exception for
quick isolated fixes; several backend-developer sub-agent spawns for this task got stuck in a
self-delegation loop, claiming to wait on "the backend-developer agent" instead of doing the
work, and repeatedly hit session limits, so the fix was applied and verified directly instead of
re-spawning again)
**Date:** 2026-07-06
**Plan:** `calm-singing-marble.md` (TASK-305/306/307/308)
**Status:** done — both QA-blocking bugs fixed and verified live

## Bug #1 (critical, backend) — task dueDate 500

Fix was already present in `backend/ShelfGuard.Application/Features/Marketplace/SupplierTaskService.cs`
(`CreateAsync`/`UpdateAsync` both normalize `request.DueDate` via
`DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc)`) by the time this session picked
the task back up — verified by reading the file directly rather than trusting prior agent
self-reports (several of those reports were suspect: repeated "I'll wait for the agent to
finish" replies instead of actual work, and a mid-session "safety classifier unavailable, verify
manually" warning).

Verified for real:
- `dotnet build` — 0 warnings/errors.
- `dotnet test` — 575/575 passing.
- Live API test against the dev stack (docker compose, port 5435, backend on :5000): created a
  fresh supplier tenant + supplier_admin user, then `POST /api/supplier-cabinet/tasks` with
  `"dueDate":"2026-07-10"` → `201 Created`, `dueDate` round-trips as
  `"2026-07-10T00:00:00Z"`. Previously this was a `500`.

## Bug #3 (medium, frontend) — no page-level permission guard on /supplier/team and /supplier/tasks

QA (TASK-308) found that both pages only checked base role (`supplier_admin`), never the
granular `permissions` dict — so a restricted staff member without `staff_management`/
`task_board` couldn't see the links in the sidebar but could still fully use both pages via
direct URL navigation.

Fix: added a permission check in both page components, mirroring the exact convention already
used in `Sidebar.tsx` (`me.permissions` is `null`/`undefined` for full/owner access; a non-null
dict without the required key means restricted):

- `frontend/app/(dashboard)/supplier/team/page.tsx` — added check on `staff_management`.
- `frontend/app/(dashboard)/supplier/tasks/page.tsx` — added check on `task_board`.

Verified for real:
- `npx tsc --noEmit` — 0 errors.
- Live browser test (Claude Preview tooling) using a real restricted staff session ("QA Content
  Manager", role `supplier_admin`, permissions `{"catalog_management":true,"client_reviews":true}`
  — no `staff_management`/`task_board`): navigating directly to `/supplier/team` now renders
  "Немає доступу до управління командою." instead of the full staff/roles panel; `/supplier/tasks`
  renders "Немає доступу до дошки завдань." instead of the full task board. No console errors.

## Not in scope

Bug #2 from TASK-308 (pre-existing data corruption — `supplier-alpha`/`supplier-beta` names
stored as literal `?` bytes) is a data-cleanup item, not a code defect in this feature, and
remains open as a follow-up.

## Note on agent reliability this session

Multiple `backend-developer` sub-agent spawns for this specific bug fix responded with
meta-commentary ("The backend-developer agent is now running in the background... I'll wait
for it to complete") instead of performing the fix, apparently misinterpreting the
"Read .claude/agents/backend-developer.md first, then implement" instruction as a request to
delegate rather than to adopt the persona and act. Multiple resumes didn't reliably fix this
and several hit session/rate limits. Given the fix was small, single-purpose, and precisely
located (matches the CLAUDE.md exception for quick isolated fixes in well-known files), it was
completed and verified directly in the main session instead of continuing to re-spawn.
