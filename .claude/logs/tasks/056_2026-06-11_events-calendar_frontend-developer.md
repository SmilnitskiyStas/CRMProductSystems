---
task_id: TASK-056
date: 2026-06-11
agent: frontend-developer
status: done
---

# TASK-056 — Web: events calendar (/events)

## Files
```
app/(dashboard)/events/page.tsx        — month navigation, legend, seed button, modals
features/events/
  types.ts                             — DemandEvent, payloads, EVENT_TYPE_META (colors)
  api/events.ts                        — full CRUD + coefficients + seed-defaults
  hooks/useEvents.ts                   — 7 React Query hooks
  components/EventCalendar.tsx         — Monday-first month grid; recurring events
                                          projected by month/day incl. New Year wrap
  components/EventForm.tsx             — zod+rhf create/edit modal + CoefficientEditor
components/layout/Sidebar.tsx          — + "Події" (CalendarDays, AT_LEAST_STORE_MANAGER)
```

## UX
- Month grid: today highlighted, ≤3 event chips per day colored by type
  (свято/акція/місцева/сезон/інше) + "+N ще"; legend in the toolbar
- Click day → create form pre-filled with that date; click chip → edit
- Edit modal embeds coefficient editor: inline multiplier edit (blur-save),
  add coefficient (scope type + value); delete event button
- "Стандартні свята" button → seed-defaults (idempotent, toasts result)

## Follow-ups noted
- Coefficient scopeId shown as raw uuid prefix — needs category/segment/product
  pickers (no categories API hook in frontend yet)
- Week view (spec mentions week/month) — month only for now

## Verification
- `tsc --noEmit` clean; deployed; GET /events → 200

## Sprint v2.3 «Events & Weather» — COMPLETE
TASK-054 ✅ 055 ✅ 056 ✅ → next: v2.4 Cannibalization (TASK-057), v2.5 AI Agent (058-060)
