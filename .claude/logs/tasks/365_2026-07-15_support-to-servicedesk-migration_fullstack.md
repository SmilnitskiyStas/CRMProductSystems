# TASK-365 — Retire Support feature, fill ServiceDesk provider-reply gap

**Status:** done · **Agent:** main session (frontend + backend, per explicit user instruction —
no sub-agent spawned) · **Depends:** TASK-363 finding ("Flagged, NOT fixed")

## Context

TASK-363 flagged that tenant support tickets submitted via Settings "Служба підтримки"
(`SupportTab.tsx` → `Features/Support`) had no provider-side inbox — tickets appeared to vanish.
User decided: retire `Features/Support` entirely, move tenants onto the already-existing
`ServiceDesk` feature.

## Research finding that changed the plan

Before touching anything, traced every render path. Two things TASK-363 missed because it
checked "does the code exist and call a working API" rather than "is it reachable from any UI
route":

1. **Settings never renders `SupportTab.tsx`.** Commit `9c40cd91` (2026-06-20, TASK-270) already
   removed the "Support" tab from `app/(dashboard)/settings/page.tsx` and rewired the TopBar
   support button + Sidebar to `/service-desk`. `frontend/features/settings/components/SupportTab.tsx`
   and all of `frontend/features/support/*` (api/hooks/types) have been dead, unreachable code
   since that commit — confirmed via full-tree grep, only referenced by each other.
2. **A full tenant-facing ServiceDesk UI already exists and is already live** at `/service-desk`
   (`app/(dashboard)/service-desk/page.tsx`): ticket list/create/detail/comments for tenant users
   (`MyTicketList`, `TicketList`, `TicketDetail`, `CreateTicketForm`), and a separate
   `ProviderSupportTab` for provider role on the same route. So "build client UI for ServiceDesk"
   from the brief was already done — no new tenant-facing page was needed.
3. **Real gap found during verification, not in the original brief:** the provider side of
   ServiceDesk (`AdminServiceDeskController` → `/api/admin/service-desk`) only had list + create.
   No `GET /{id}` and no comment/reply endpoint — `ProviderSupportTab`'s ticket detail panel showed
   metadata only, no way to reply. This reproduced the exact "ticket visible but unactionable"
   failure mode TASK-363 was trying to fix, just one layer further in. Fixed as part of this task
   (judgment call per CLAUDE.md — objective completion of the stated goal, not a new product
   decision).

## Changes

**Deleted (dead code, Support feature retirement):**
- `frontend/features/settings/components/SupportTab.tsx`
- `frontend/features/support/` (api/supportApi.ts, hooks/useSupport.ts, types.ts)
- `backend/ShelfGuard.Api/Controllers/SupportController.cs` (tenant `/api/support/*`)
- `backend/ShelfGuard.Api/Controllers/ProviderSupportController.cs` (provider `/api/provider/support/*`)
- `backend/ShelfGuard.Application/Features/Support/` (ISupportService, SupportService, Dtos)
- `backend/ShelfGuard.Domain/Interfaces/ISupportRepository.cs` (ISupportTicketRepository, ISupportMessageRepository)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/SupportTicketRepository.cs`, `SupportMessageRepository.cs`
- DI registrations for all of the above removed from `ShelfGuard.Application/DependencyInjection.cs`
  and `ShelfGuard.Infrastructure/DependencyInjection.cs`.

**Deliberately NOT touched:** `SupportTicket`/`SupportMessage` domain entities, their `AppDbContext`
DbSet mappings, and the `support_tickets`/`support_messages` tables/migrations/RLS policies.
`SupportTicket` turned out to be the *same* entity ServiceDesk's `ITicketRepository` /
`IProviderTicketRepository` already use (shared table, `TicketComment` instead of the old
`SupportMessage` — confirmed via `dotnet build` succeeding with zero breakage). Verified in dev DB:
`support_tickets` and `support_messages` both have 0 rows — no data at risk either way.

**Sidebar/TopBar/Settings navigation:** no changes needed — already pointed at `/service-desk`
since the June commit.

**Provider reply capability added (new, closes the real gap above):**
- `IProviderTicketRepository` / `ProviderTicketRepository`: added `GetByIdWithCommentsAsync`
  (eager-loads `Comments.Author`) and `AddCommentAsync`, mirroring the existing cross-tenant,
  RLS-bypass pattern already used by `GetAllAsync`/`CreateAsync` in the same class.
- `IProviderTicketService` / `ProviderTicketService`: added `GetByIdAsync` (returns new
  `ProviderTicketDetailDto` with comments) and `AddCommentAsync` (provider-authored comments are
  always `IsInternal = false` — provider has no "internal note to self" concept here, matches
  "replying to the client" semantics).
- `AdminServiceDeskController`: added `GET /api/admin/service-desk/{id}` and
  `POST /api/admin/service-desk/{id}/comments`, both gated by the existing `ProviderTeamMember`
  policy (same as the rest of the controller).
- Frontend: `providerTickets.ts` (`getTicket`, `addComment`), `useProviderTickets.ts`
  (`useProviderTicket`, `useAddProviderComment`), `types.ts` (`ProviderTicketDetailDto`).
  `ProviderSupportTab.tsx`'s `TicketDetailPanel` now fetches full detail via `useProviderTicket`
  and renders a comment thread + reply textarea, styled to match `TicketDetail.tsx`'s (tenant-side)
  existing comment UI for consistency.

## Verification (both sides, in-browser)

- Tenant (`ea@demo.local`, Свіжий Кут): Settings shows only Загальні/Сповіщення/Інтеграції — no
  Support tab, nothing broken by the removal. Created ticket #1 "TASK-364 verification ticket" via
  `/service-desk` → `POST /api/service-desk`. Appeared immediately in "Мої тікети"/"Всі тікети".
- Provider (`admin@shelfguard.local`): ticket #1 visible on `/service-desk` provider tickets view,
  tagged with tenant "Свіжий Кут" and author "Василь Мороз". Opened detail, typed a reply, sent —
  comment posted as "Admin User", comment count updated to 1.
- Logged back in as tenant: opened ticket #1, provider's reply ("Дякуємо за звернення!...") visible
  in the comment thread. Full round-trip confirmed working both directions.
- Note for whoever reviews this: dev `.next` build cache got corrupted mid-session after deleting
  files while `next dev` was running (`Cannot find module './1638.js'` / vendor-chunks errors) —
  unrelated to the code changes, fixed by stopping the server and `rm -rf .next` before restart.
  Not a regression, just a dev-server quirk worth knowing if it recurs.

## Build/tests

- `dotnet build`: 0 errors, 0 warnings.
- `dotnet test`: 879/879 passed (unchanged from TASK-363 baseline — no test coverage existed for
  either the old Support feature or ServiceDesk's provider side; not added here either, flagged
  below).
- `npx tsc --noEmit` (frontend): clean, 0 errors.

## Not done / flagged

- No automated test coverage added for the new provider comment endpoints
  (`GetByIdAsync`/`AddCommentAsync` on `ProviderTicketService`, or the controller actions). Existing
  `ProviderTicketService` had zero test coverage before this change too — pre-existing gap, not
  introduced here, but now slightly larger surface area with none. Reasonable follow-up if this
  feature gets real traffic.
- Provider reply is plain-text only, no status/assignment control from the provider side (matches
  the narrow scope of "can reply" from the task brief; `TicketDetail.tsx`'s manager controls
  — status dropdown, assignee — were intentionally not ported to the provider view to avoid scope
  creep beyond what was asked).
