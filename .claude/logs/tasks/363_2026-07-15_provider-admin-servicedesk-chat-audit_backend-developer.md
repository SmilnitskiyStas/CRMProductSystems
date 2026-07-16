# TASK-363 — Backend: Block 12 pre-launch audit — Provider/Admin/ServiceDesk/Chat

**Status:** done · **Agent:** backend-developer (main session) · **Depends:** TASK-362

Block 12 of the pre-launch audit (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`).
Scope: `Features/Provider`, `Features/Admin`, `Features/ServiceDesk`, `Features/Chat` (+ the
adjacent `Features/Support` feature, pulled in because it shares tables with ServiceDesk/Chat).

## Found + fixed — P0: provider_admin could self-escalate to the owner role

`ProviderTeamService` (`InviteMemberAsync`/`UpdateMemberAsync`/`DeactivateMemberAsync`) is
gated by `AppPolicies.ProviderCanInvite = [provider, provider_admin]` — deliberately open to
provider_admin, per `ProviderPermissions.SystemRoleDefaults` where provider_admin already holds
every `ProviderPermissions.All` flag. But the role-change logic itself had no rank/owner check:
a provider_admin could `PUT /api/provider/team/{memberId}` with `role: "provider"` on *themselves*
or any teammate, or `POST /api/provider/team/invite` a brand-new `role: "provider"` account
outright. The only existing guard ("cannot change the role of the owner account") protected
against *demoting* the literal owner, not against *promoting* someone else to it. Since
`ProviderController` (tenant CRUD, impersonation, platform-wide activity logs) is gated by
`AppPolicies.ProviderOnly = [provider]` only — strictly excluding provider_admin — this let any
provider_admin account grant itself full owner-level access, defeating the v1-spec.md §3.2
single-owner boundary ("Всі підприємства" / "Impersonation" — provider only).
Also found the same gap on Deactivate: a provider_admin could deactivate the literal owner
account (denial-of-service against the platform owner).

Fix (`IProviderTeamService`/`ProviderTeamService`/`ProviderTeamController`): all four methods now
take the caller's own role (read from the validated JWT `ClaimTypes.Role` claim in the
controller, never trusted from the request body). Invite/Update reject `role: "provider"` unless
`actingRole == "provider"`; Deactivate rejects acting on a `role == "provider"` target unless
`actingRole == "provider"`. Reactivate deliberately left unrestricted (restores the *target's*
own access — not a privilege grant to the actor). 10 new tests in
`ShelfGuard.Tests/Provider/ProviderTeamServiceTests.cs` (this service had zero test coverage
before) covering both the escalation-blocked and legitimate-owner-can-still-do-it paths for
each of the four operations.

## Found + fixed — P1/hardening: chat_messages / support_messages had RLS fully disabled

Live query against the dev DB (`pg_class.relrowsecurity`/`relforcerowsecurity`) found
`chat_messages` and `support_messages` were the only two tables in the whole
Chat/ServiceDesk/Support family with **no RLS at all** — not even enabled, let alone FORCE.
Every sibling table (`chat_sessions`, `support_tickets`, `ticket_comments`, and the analogous
marketplace tables `supplier_chat_messages`/`supplier_support_ticket_messages` from
`20260706110628_AddSupplierChat`) correctly has RLS + FORCE RLS. Neither table has its own
`TenantId` column (only `SessionId`/`TicketId`), so no policy had ever been written for them —
undocumented on the entities (unlike Block 10's `recipe_ingredients`, which explicitly documents
"no own RLS, scope inherited via parent join" as a deliberate decision).

Code review of every access path (`ChatService.cs`, `SupportService.cs`) confirmed this was not
a *live* exploit today — every query already scopes correctly through the parent
(`chat_sessions.TenantId` / `support_tickets.TenantId`) before touching the message tables, and
both `ChatController`/`AdminChatController` resolve tenant scope from the JWT claim only, never
the request body. But it was a real defense-in-depth gap of exactly the kind this audit series
has repeatedly found causing live bugs elsewhere (Block 2's RLS fail-open, several worker jobs
missing filters) — one future missing-filter bug in either service would have had zero database
safety net. Fixed via `20260715153812_AddChatAndSupportMessagesRls` (additive): EXISTS-subquery
tenant_isolation policy via the parent table (mirrors `supplier_chat_messages`'s existing
pattern), `provider_bypass` matching each parent's own role set exactly
(`provider`/`provider_admin`/`provider_agent`), `worker_bypass` for consistency (confirmed no
worker code currently touches either table — grepped `worker/src`, zero matches, so Block 11's
"worker missing `app.role='worker'`" bug class does not apply here). Applied directly to the dev
DB (`dotnet ef database update` failed locally on a Npgsql auth quirk unrelated to the migration
itself — applied the same SQL via `docker exec ... psql` and recorded it in
`__EFMigrationsHistory` so `dotnet ef` stays in sync). Live-verified: 0 rows visible with
`app.tenant_id` unset (fail-closed), 0 rows visible with a different tenant's id set, 5/5 own
rows visible with the correct tenant id (real data on `chat_messages`; `support_messages` has 0
rows in dev today — verified via the same fail-closed test only). Production not touched.

## Flagged, NOT fixed — needs a product decision (P0, high confidence, out of this block's scope)

**The `Support` feature's provider side is completely orphaned in the frontend — tenant support
tickets submitted via Settings go unanswered.** Full trace:
- Tenant users see "Служба підтримки" in Settings (`SupportTab.tsx`) — fully wired, hits
  `POST /api/support/tickets` + `/messages` (`SupportService`, backed by `support_tickets` +
  `support_messages`). This works end-to-end on the client side.
- The matching provider-side backend is equally complete: `ProviderSupportController`
  (`/api/provider/support/tickets/*` — list, get, assign, status, mark-read, reply) and its
  frontend API/hooks (`features/support/api/supportApi.ts`, `features/support/hooks/useSupport.ts`
  — `useAllTickets`, `useProviderTicket`, `useAssignTicket`, `useAddProviderMessage`, etc.) all
  exist and are correctly implemented.
- **But nothing in the Provider Panel ever renders them.** Grepped the whole frontend tree for
  every caller of `features/support/*` — only `SupportTab.tsx` (client side) and the api/hook
  files themselves. The Provider Panel's `/service-desk` page ("tickets" tab) instead uses a
  *different* backend feature (`ProviderSupportTab.tsx` → `/api/admin/service-desk`,
  `ProviderTicketService` — list+create only, no reply endpoint at all), and its "chat" tab uses
  yet a *third* feature (`ChatSupportTab.tsx` → `Features/Chat`, fully bidirectional and working).
- Migration dates support a "superseded but never cleaned up" theory:
  `20260614194314_AddSupportTickets` (Support) predates `20260618204137_AddServiceDesk`
  (ServiceDesk) by 4 days — ServiceDesk looks like the intended replacement (adds
  Category/Priority/Comments/formal status lifecycle on the *same* `support_tickets` table via
  `TicketComment` instead of `SupportMessage`), but the old client-facing Settings tab and its
  backend were never removed or redirected, and a provider inbox for it was apparently never
  built at all — same root-cause shape as BUG-018 ("client chat messages never reach supplier —
  no UI inbox"), just never caught because dev/staging has zero real `support_messages` rows.
- **Decision needed:** (a) build the missing Provider inbox UI for the Support feature (real
  frontend feature work, not a quick fix), (b) point Settings' "Служба підтримки" at the
  already-working `/service-desk` page instead and retire `Features/Support`'s tenant UI +
  provider backend, or (c) something else. Not fixed here — flagged via `spawn_task` so it isn't
  lost, and reported directly in chat given the severity (real clients would submit tickets that
  vanish).

## Reviewed and confirmed correct, no changes

- **Tenant onboarding atomicity** (`ProviderService.CreateTenantAsync`,
  `TenantAdminService.CreateTenantAsync`): both build the Tenant (+ Supplier/SupplierProfile for
  supplier business types) entirely in-memory and call `SaveChangesAsync` exactly once at the
  end — genuinely one DB transaction, no half-created-tenant state possible on error mid-flow.
- **Impersonation mechanics**: `ProviderService.ImpersonateAsync` issues a stateless, short-lived
  (60 min) JWT scoped to the target tenant with `role=enterprise_admin` + `impersonated=true` +
  `sub=<the real provider's own user id>` (so any action taken during impersonation still audits
  back to the real actor). `TenantConnectionInterceptor` reads `tenant_id`/role straight from
  that JWT on every connection checkout, so RLS context switches correctly with no separate
  server-side state to get out of sync. Frontend (`TenantDetailPanel.tsx`/`ImpersonationBanner.tsx`)
  saves the original provider token to `sessionStorage` before swapping, and the exit banner
  restores it explicitly + refetches `/me` — no way to get permanently stuck in a tenant, no
  server-side session to leak either (`DELETE .../impersonate` is a correctly-designed no-op,
  matches the stateless design, not a bug).
- **Provider role isolation from tenant flow**: `UserService`'s tenant-facing
  Invite/Update `ValidRoles` set explicitly excludes `provider`/`provider_admin`/`provider_agent`
  — no tenant-side path can ever mint a provider-tier account. (The escalation that *was* found
  lives entirely within the provider team's own management surface, fixed above.)
- **ServiceDesk status lifecycle + access** (`TicketService`): regular users can only see/edit
  their own tickets (`CreatedBy == userId`), can only edit while `status == open`, and cannot see
  `IsInternal` comments; managers (`store_manager`+, matches `AtLeastStoreManager`) see/edit all
  tenant tickets and set status/priority/assignment freely. `TicketRepository` eager-loads
  `Comments`/`CreatedByUser`/`AssignedToUser`/`Location` on every list/detail query — no N+1.
- **Chat feature IDOR + RLS**: `ChatController` resolves `tenantId` exclusively from the JWT
  claim (never the request body); `ChatService`'s tenant-side methods all filter by
  `session.TenantId == tenantId` (or the message's `Session.TenantId` via LINQ join) before
  returning or mutating anything — a forged session id from another tenant returns "not found" /
  empty, not another tenant's data. `AdminChatController`/`GetAllSessionsForProviderAsync` is
  deliberately cross-tenant, gated by `ProviderTeamMember` policy only, matching the same
  by-design pattern as `ProviderController`. Real-time delivery is 3-second client polling
  (`refetchInterval: 3000` in `useChat.ts`), no SignalR/WebSocket anywhere in the backend —
  consistent with the rest of the codebase, not a bug, just an architectural note.
- **RLS on `support_tickets`/`ticket_comments`/`chat_sessions`**: live-queried `pg_policies`
  against the dev DB — all three still carry the canonical Block 2 fail-closed
  NULLIF-guarded `tenant_isolation` + `provider_bypass` (correctly including
  `provider_admin`/`provider_agent`) + `worker_bypass`, unmodified by any later block.
- **Worker/cron class-of-bug lead from Block 11**: grepped `worker/src` for every
  ServiceDesk/Chat/Support table name — zero matches. No worker job touches any of these tables,
  so the "missing `SET app.role='worker'`" bug class found in Blocks 9/11 does not apply here.
- **N+1**: none found in ServiceDesk (`TicketRepository`, confirmed above) or Chat
  (`ChatService.GetSessionsAsync`/`GetAllSessionsForProviderAsync` both single-query
  `.Include(s => s.Messages)`, no per-row follow-up queries).

## Build/tests

`dotnet build` 0 errors / 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs:534`,
predates this block). `dotnet test` 879/879 green (was 869, +10 new
`ProviderTeamServiceTests`). Migration `20260715153812_AddChatAndSupportMessagesRls` applied to
dev DB only (via `docker exec psql` + manual `__EFMigrationsHistory` insert — `dotnet ef database
update` hit an unrelated local Npgsql auth error against `localhost:5435`, worked fine via
`docker exec` against the same container). Production not touched.
