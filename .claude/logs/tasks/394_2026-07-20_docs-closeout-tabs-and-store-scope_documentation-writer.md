# TASK-394: Documentation close-out — per-role tab visibility (Feature 1) + store-scoped assignment (Feature 2)

**Agent:** documentation-writer
**Date:** 2026-07-20
**Status:** done (docs only, no code changes)

## Зроблено

1. **`.claude/docs/decisions.md`** — two new ADRs, inserted above ADR-020 (descending order),
   header `Updated:` bumped to 2026-07-20:
   - **ADR-021** — `TenantRole.AllowedTabs`, per-role sidebar tab visibility. Documents the
     10-key fixed catalog, the `text[]` storage correction vs. the original brief's `jsonb`
     assumption, the explicit admin/supplier_cabinet/settings exclusions, and — the most
     load-bearing part — the **Tier 1 / Tier 2 enforcement split**: only `workforce`
     (users.manage/schedules.manage) and `analytics` (analytics.view) have a real ADR-020
     backend capability behind them today; the other 7 tab keys make a sidebar link appear
     with nothing server-side enforcing it yet (UX gap, not a security hole, since
     `AllowedTabs` never grants backend access by itself).
   - **ADR-022** — `user_locations` + store-scope RLS. Documents the dead `User.StoreId` field
     background, the `enterprise_admin`-bypass-vs-everyone-else-via-`user_locations` model, the
     3-stage rollout (Stage 1 deployed / Stage 2 manual backfill / Stage 3 written+tested+held
     on `stage3-rls-enforcement-hold`), the 9 RESTRICTIVE-policy tables, the `provider_admin`
     bypass addition (deviation from the original brief, justified — avoids regressing its
     existing `provider_bypass` access), and the fail-closed behavior explicitly confirmed by
     the product owner. Points to `.claude/docs/store-scope-rollout-checklist.md` for the
     activation procedure rather than duplicating it.
   - Both ADRs read source directly (`TenantRoleTabs.cs`, `Sidebar.tsx`, `useRequireTab.ts`,
     `UserService.cs`, `UsersController.cs`, `TenantRolesController.cs`) rather than only the
     task logs, to keep the record accurate against what's actually in the tree today.

2. **`.claude/docs/api-contracts.md`** — header bumped to 2026-07-20.
   - `AuthUserDto`/JWT Claims block corrected: added `tabs` (this session, ADR-021) and, since
     it was sitting right next to it in the same real DTO but had never been documented at all
     (ADR-020, prior session), `capabilities` — otherwise the JSON block would still have been
     visibly wrong immediately after my own edit.
   - New **Tenant Roles** section (`/api/tenant-roles/*` — none of this existed in the doc
     before): full CRUD + `GET .../capabilities` + new `GET .../tabs`, `TenantRoleDto`/
     `Create`/`UpdateTenantRoleRequest` (with `allowedTabs`), `TenantRoleCapabilityDto`/
     `TenantRoleTabDto`.
   - New **Users** section (also didn't exist at all — the file's own backlog table still
     listed `GET /api/users` as "future"): full route list, `UserDto`, `InviteUserRequest`/
     `UpdateUserRequest` with the `storeId` validation/consumption behavior spelled out
     (TASK-392b), and the two new `PUT`/`GET /api/users/{id}/locations` endpoints with full
     request/response shapes and the "nothing enforces this yet, that's Stage 3" caveat.
   - Removed the now-false `GET /api/users | future` row from the Pending Endpoints backlog
     table (directly contradicted by the new Users section a few hundred lines above it).
   - Did **not** attempt a full reconciliation of the rest of the Users/Auth surface (2FA,
     permission-grants DTOs, activity log) — flagged as stale by TASK-392b/392c but out of this
     task's scope (two specific features, not a holistic Users API audit).

3. **`.claude/docs/domain-model.md`** — header bumped to 2026-07-20. This file predates the v4
   Store→Location rename and has no `TenantRole` section at all (never backfilled after
   ADR-020) — per the brief's conditional ("якщо є розділ про User/TenantRole/Location"), the
   `User` section qualifies, so updated:
   - `User` — one-line note that `store_id` is a UI hint only, real enforcement is
     `UserLocation`.
   - New brief `TenantRole` entity block (capabilities + allowed_tabs fields) — didn't exist
     before; added only what's needed to give `AllowedTabs` a home, not a full ADR-020 backfill.
   - New brief `UserLocation` entity block (fields, unique constraint, fail-closed/Stage-3 note).

## Не в скоупі (свідомо, per brief)

- No code changes anywhere.
- Branch `stage3-rls-enforcement-hold` — not merged, not touched, not checked out (read via
  `git show <branch>:<path>` only, to pull TASK-393's log + the rollout checklist content).
- No full reconciliation of pre-existing stale doc drift outside the two features in scope
  (e.g., `AuthUserDto`'s `tenantId`/`tenantName`/`legalEntityId`/`telegramChatId` were already
  undocumented before this task and are now included in the corrected JSON block since I was
  already rewriting that exact block anyway; but 2FA/permission-grants/activity-log DTOs were
  left as route-signatures-only, not fully expanded — a larger, separate Users-API-doc pass).

## Git

Local commit only, no push (doc-only change; matches this session's established
commit-locally/no-push pattern while deploys are paused).
