# TASK-593 — Events: multi-store scope + global header selector wiring (frontend)

**Status:** done · **Agent:** frontend-developer
**Parallel:** backend-developer (TASK-592 database-engineer already landed the `storeIds`
migration side) built the matching API contract in `/backend` concurrently — not touched here.

## What changed

1. **New `"stores"` scope** (several specific stores, vs. existing `"network"`/single-`"store"`):
   - `frontend/features/events/types.ts` — `DemandEvent`/`UpsertEventPayload.scope` gains
     `"stores"`; both gain `storeIds: string[]` (always present, empty unless scope is `"stores"`).
   - `frontend/features/events/components/EventForm.tsx` — 3rd `<option>`; zod schema adds
     `storeIds: z.array(z.string()).optional()` + object-level `.refine(...)` (≥1 store required
     when `scope === "stores"`, `path: ["storeIds"]`); new `toggleStoreId` via RHF `setValue`/
     `watch("storeIds")`; renders `LocationsMultiSelectDropdown` (reused from
     `features/users/components/...`) when `scope === "stores"`, same wiring pattern as
     `BannerForm.tsx`'s `locationIds`/`toggleLocation`; submit payload gains
     `storeIds: v.scope === "stores" ? v.storeIds ?? [] : []`; edit-mode prefill from
     `event.storeIds`.
   - `frontend/features/events/components/EventDetailPanel.tsx` — `SCOPE_LABEL_KEYS` lookup
     object (all 3 keys reused from `eventForm`, matching the form's existing reuse pattern
     instead of a new `dayDetail` key); `storeName` now resolves+joins `event.storeIds` for
     `"stores"` scope (same `stores.find` lookup as the singular case, mapped over the array);
     `LinkedProductSalesCard`'s `storeId` prop now only passes a concrete id for `scope ===
     "store"` — `"network"`/`"stores"` omit it (aggregate comparison).

2. **Events page now reacts to the global header store selector**:
   - `frontend/features/events/api/events.ts` — `getAll(from, to, storeIds?)`, repeated
     `?storeIds=` query params (mirrors `usersApi.getAll`'s serialization).
   - `frontend/features/events/hooks/useEvents.ts` — `useEvents(from, to, storeIds?)`, query key
     extended with `storeIds`.
   - `frontend/app/(dashboard)/events/page.tsx` — reads full `selectedStoreIds` array via
     `useStoreContext` (multi-store list/report pattern, same as `useUsers()` — not
     `usePrimaryStoreId()`), passes it to `useEvents`. No local store picker added.

`EventCalendar.tsx` / `EventDayDetailDrawer.tsx` — confirmed untouched, zero store-filtering
logic of their own (verified via grep, no `scope`/`storeId` references).

## i18n (both `frontend/messages/en.json` and `uk.json`, `Dashboard.events.eventForm`)

`scopeStoresOption`, `validationStoresRequired`, `storesLabel`, `storesSelectedCount`,
`storesPlaceholder`, `storesDoneButton`. No new `dayDetail` key — the 3-way scope label in
`EventDetailPanel.tsx` reuses `eventForm.scopeStoresOption` via `tForm(...)`, same as the
existing `scopeNetworkOption`/`scopeStoreOption` reuse.

## Verification

- `npx tsc --noEmit` — clean.
- `npx eslint` on all 6 touched files — clean.
- Both `messages/en.json` / `messages/uk.json` parse as valid JSON (`JSON.parse` check).
- Live browser check: **skipped** — no dev server running and no authenticated browser session
  available in this environment; did not attempt login (hard-blocked action).

## Files changed

- `frontend/features/events/types.ts`
- `frontend/features/events/api/events.ts`
- `frontend/features/events/hooks/useEvents.ts`
- `frontend/app/(dashboard)/events/page.tsx`
- `frontend/features/events/components/EventForm.tsx`
- `frontend/features/events/components/EventDetailPanel.tsx`
- `frontend/messages/en.json`
- `frontend/messages/uk.json`
