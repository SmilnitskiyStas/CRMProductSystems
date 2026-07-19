# Handoff 391b → frontend (per-role sidebar tab visibility, Feature 1)

Backend half of Feature 1 is done (TASK-391 schema + TASK-391b API/JWT). Exact names/shapes to
build `Sidebar.tsx` / `TenantRolesTab.tsx` / `useRequireTab.ts` against.

## New endpoint: `GET /api/tenant-roles/tabs`

Same gate as the existing `GET /api/tenant-roles/capabilities` you already integrate against
(`AtLeastEnterpriseAdmin`-only, not tenant-scoped — same catalog for every caller). Response:

```json
[
  { "key": "dashboard", "labelUa": "Дашборд" },
  { "key": "operations", "labelUa": "Операції" },
  { "key": "sales", "labelUa": "Продажі" },
  { "key": "procurement", "labelUa": "Постачання" },
  { "key": "marketplace", "labelUa": "Маркетплейс" },
  { "key": "auto_service", "labelUa": "Auto Service" },
  { "key": "production", "labelUa": "Виробництво" },
  { "key": "analytics", "labelUa": "Аналітика" },
  { "key": "workforce", "labelUa": "Персонал" },
  { "key": "support", "labelUa": "Підтримка" }
]
```

Order matches `Sidebar.tsx`'s visual top-to-bottom order (source: `TenantRoleTabs.Catalog`,
`backend/ShelfGuard.Domain/Constants/TenantRoleTabs.cs`). camelCase JSON (ASP.NET Core default,
no custom `JsonSerializerOptions` anywhere in the API).

## `TenantRoleDto` / Create/Update requests already carry `allowedTabs` (done in TASK-391, not new)

`GET/POST/PUT /api/tenant-roles*` responses/requests already have `allowedTabs: string[]`
alongside `capabilities: string[]` — same shape, same place. Nothing new here, just confirming
it's live so `TenantRolesTab.tsx`'s create/edit form can bind to it today.

## JWT access token: new `"tabs"` claim

Comma-joined string claim, same shape as the existing `"capabilities"` claim you already decode
client-side. **Absent entirely** when the user's effective tab list is empty (no `TenantRoleId`,
archived template, or template with an empty `AllowedTabs`) — same omit-when-empty rule as
`"capabilities"`. Split on `,` client-side, same as you already do for capabilities.

## `AuthUserDto` (i.e. `POST /api/auth/login` → `response.user`, `POST /api/auth/refresh` →
## `response.user`, `GET /api/auth/me`): new field

```ts
tabs: string[] | null   // mirrors the JWT "tabs" claim; null/[] = no tabs restricted → treat as "show default set" or "show nothing", your UX call
```

Positioned last in the record (after `preferredLocale`) — irrelevant for JSON consumers, only
matters if some code was doing positional deserialization (nothing in this repo does; DTOs are
always consumed by property name).

Same trust model as `capabilities`: this field is a **UI-only mirror** of what the server already
decided (client convenience so your sidebar logic doesn't have to decode the JWT itself if you'd
rather read the parsed user object) — it carries no enforcement weight, don't treat its absence/
presence as a security boundary. **Backend enforcement of tabs does not exist yet** — see below.

## What "tabs" do NOT do yet (Tier 2, explicitly deferred — do not assume otherwise)

Tabs are a **UI-visibility** signal only right now. No backend endpoint checks the `"tabs"` claim
or rejects a request based on it — every existing capability-gated endpoint (`users.manage`,
`schedules.manage`, `analytics.view`, etc.) is unaffected by this feature and was already
independently enforced server-side before TASK-391/391b (ADR-020). Don't build any frontend logic
that assumes hitting a hidden route/tab is itself blocked server-side — it isn't, by design, for
now. That's tracked as a separate, deliberately-deferred "Tier 2" follow-up (new capabilities for
sales/marketplace/auto_service/production tabs), not part of this handoff.

## Empty/no-role case

A user with no `TenantRoleId` (most users today — this is an opt-in per-role feature, most tenants
haven't created a template yet) gets `tabs: null` from every endpoint and no `"tabs"` JWT claim at
all. Decide the fallback UX explicitly (e.g. "no TenantRoleId at all → show every tab, tab
filtering only applies to users who HAVE a TenantRoleId with a non-empty AllowedTabs" is the
reading that matches how `AllowedTabs` defaults to `[]` at the DB layer for brand-new templates
too — an empty list is not distinguishable from "no template assigned" purely from the claim, if
that distinction matters to your UX you need `TenantRoleId`/the full user object, not just `tabs`).

## Reference docs

- Full tab catalog + the 3 deliberate exclusions (`admin`, `supplier_cabinet`, `settings`) and
  why: `backend/ShelfGuard.Domain/Constants/TenantRoleTabs.cs` doc comment.
- Schema-stage reasoning (text[] not jsonb, etc.):
  `.claude/logs/tasks/391_2026-07-19_tenant-role-allowed-tabs-schema_database-engineer.md`.
- This stage's full change list: `.claude/logs/tasks/391b_2026-07-19_tenant-role-tabs-api_backend-developer.md`.

Build clean, 866/866 tests green. Not pushed to remote yet (deploy paused today by product owner)
— pull from local `main` after it's committed, don't wait on a remote push to start.
