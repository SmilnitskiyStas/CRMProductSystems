# TASK-589 — Events calendar: day-detail drawer (shell + basic info)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-21

First piece of a larger approved feature on the Events calendar page. Clicking a day used to
jump straight into the create-event form (`onDayClick={setCreating}`); now it opens a
detail drawer listing that day's existing event(s), from which the user can drill into an
event or add a new one. Product-linking + sales-comparison sections in the event-detail view
are a follow-up piece for a later agent — out of scope here.

**Files created:**
- `frontend/features/events/utils.ts` — `isEventActiveOnDate` (extracted verbatim from
  `EventCalendar.tsx`'s private `isActiveOn`) + new `resolveEventWindowForYear` (projects a
  recurring event's stored MM-dd window onto a reference year, incl. New-Year wraparound —
  for the later agent's sales-comparison window; not consumed by this task's UI).
- `frontend/features/events/components/EventDayDetailDrawer.tsx` — one `DetailDrawer`
  instance, view swapped via local `selectedEventId` state: day-event list (width 480,
  empty-state + "add event"/"add another" actions) vs. `EventDetailPanel` (width 860, "Back"
  in the drawer's `actions` slot).
- `frontend/features/events/components/EventDetailPanel.tsx` — basic info only (type badge,
  scope/store, date range, recurring flag, notes, Edit button calling `onEdit`). Left open
  for the follow-up agent to add Linked Products / Sales Comparison sections.

**Files changed:**
- `frontend/features/events/components/EventCalendar.tsx` — dropped its private `isActiveOn`,
  imports `isEventActiveOnDate` from the new `utils.ts` instead (behavior-preserving refactor).
- `frontend/app/(dashboard)/events/page.tsx` — added `selectedDay` state,
  `onDayClick={setSelectedDay}` (was `setCreating`), renders `<EventDayDetailDrawer>` wired to
  the existing `setCreating`/`setEditing` setters. `onEventClick={setEditing}` and the
  existing `creating`/`editing` `<EventForm>` blocks are untouched.
- `frontend/messages/en.json`, `frontend/messages/uk.json` — new
  `Dashboard.events.dayDetail.*` namespace (drawer title, empty state, add/back/edit buttons,
  field labels). Reuses `Dashboard.events.eventForm.scopeNetworkOption`/`scopeStoreOption` for
  the scope value text instead of duplicating.

**`resolveEventWindowForYear` — settled logic** (brief's sketch had the right shape but the
non-recurring/non-wrap branches needed the reasoning made explicit):
```ts
export function resolveEventWindowForYear(ev: DemandEvent, referenceDateIso: string): { from: string; to: string } {
  if (!ev.isRecurring) return { from: ev.startsAt, to: ev.endsAt };
  const year = Number(referenceDateIso.slice(0, 4));
  const startMd = ev.startsAt.slice(5);
  const endMd = ev.endsAt.slice(5);
  if (startMd <= endMd) return { from: `${year}-${startMd}`, to: `${year}-${endMd}` };
  const refMd = referenceDateIso.slice(5);
  if (refMd >= startMd) return { from: `${year}-${startMd}`, to: `${year + 1}-${endMd}` };
  return { from: `${year - 1}-${startMd}`, to: `${year}-${endMd}` };
}
```
Verified against Dec 20 – Jan 5 (wrapping): reference Dec 25 → `2026-12-20..2027-01-05`
(window started this year); reference Jan 2 → `2025-12-20..2026-01-05` (window started last
year). Non-wrapping windows always resolve within `referenceDateIso`'s own year.

**Verification:** `npx tsc --noEmit` clean, `npx eslint` clean on all touched/created files,
both `messages/{en,uk}.json` parse as valid JSON. No dev server was running and no
authenticated browser session existed — live click-through in the app was skipped per the
task boundary (did not attempt login).

**Not done (by design):** Linked Products / Sales Comparison sections in
`EventDetailPanel.tsx` — next agent's scope.
