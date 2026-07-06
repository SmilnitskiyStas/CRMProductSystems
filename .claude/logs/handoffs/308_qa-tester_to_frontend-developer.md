# Handoff: Missing page-level permission guard on /supplier/team and /supplier/tasks → Frontend

**From:** qa-tester (TASK-308)
**To:** frontend-developer
**Date:** 2026-07-05
**Plan:** `calm-singing-marble.md` (TASK-307)

## Bug to fix (medium, should fix before shipping)

Full repro and analysis in
`.claude/logs/tasks/308_2026-07-05_supplier-consolidation-qa_qa-tester.md` (Bug #3) — summary:

A supplier-tenant staff user whose custom role's permissions are e.g.
`["catalog_management","client_reviews"]` only (no `staff_management`, no `task_board`) correctly
does NOT see "Команда"/"Завдання" in the sidebar (`Sidebar.tsx` filtering works as designed). But
navigating directly to `/supplier/team` or `/supplier/tasks` (address bar, bookmark, shared link)
fully renders both pages — full staff list + invite button + role CRUD on `/supplier/team`, full
task board + create/status-change on `/supplier/tasks` — with no access-denied state.

Backend intentionally does not enforce these permissions per-endpoint (mirrors the `ProviderRole`
convention — confirmed in the TASK-305/306 handoffs, this is by design, not something to change).
That makes the frontend page component the *only* place this was supposed to be gated, and
currently it isn't.

## Root cause

`frontend/app/(dashboard)/supplier/team/page.tsx:11` and
`frontend/app/(dashboard)/supplier/tasks/page.tsx:10` both guard only on base role:

```tsx
if (me && !hasRole(me.role, SUPPLIER_ONLY)) {
  return <div ...>Доступ лише для адміністраторів постачальника.</div>;
}
```

`hasRole(me.role, SUPPLIER_ONLY)` checks `me.role === "supplier_admin"`, which is true for every
supplier-tenant user regardless of their `permissions` dict — a restricted staff member still has
`role: "supplier_admin"`, just with a narrower `permissions` object. The check never looks at
`me.permissions` at all, so it can't distinguish "full access supplier_admin" from "restricted
supplier_admin with only catalog_management + client_reviews".

## Suggested fix

Add a permission check alongside the existing role check in both pages, reusing whatever helper
`Sidebar.tsx` already uses to compute `supplierEffectivePermissions` (per
`.claude/logs/tasks/307_2026-07-05_supplier-roles-tasks-frontend_frontend-developer.md`, built on
`frontend/lib/supplierPermissions.ts` / `resolveSupplierPermissions`). Remember the existing
convention: `permissions === null` on the user means full/owner access (not "no access") — don't
invert that.

Rough shape:
```tsx
const effective = resolveSupplierPermissions(me?.permissions);
if (me && (!hasRole(me.role, SUPPLIER_ONLY) || !effective.staff_management)) {
  return <div ...>Доступ лише для адміністраторів постачальника.</div>; // or a more specific message
}
```
(analogous check with `task_board` on the tasks page)

Check first whether a shared route-guard hook/HOC already exists elsewhere in the app for
permission-gated pages (e.g. provider-side pages) — if so, reuse that pattern instead of writing a
one-off check in each page.

## Verify after fix

- Log in as a restricted staff user (permissions minus `staff_management`/`task_board`), navigate
  directly to `/supplier/team` and `/supplier/tasks` by URL — should see an access-denied state, not
  the full page content.
- Confirm full-access supplier_admin (`permissions: null`) and a staff user who DOES have
  `staff_management`/`task_board` in their custom role still see the pages normally — don't
  regress the working case.
- `tsc --noEmit` should stay at 0 errors.
