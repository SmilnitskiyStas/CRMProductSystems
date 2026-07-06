# Handoff: Task due-date crash → Backend

**From:** qa-tester (TASK-308)
**To:** backend-developer
**Date:** 2026-07-05
**Plan:** `calm-singing-marble.md` (TASK-305/306/307)

## Bug to fix (critical, blocking)

Creating or updating a supplier task with a `dueDate` throws an unhandled 500. Full repro,
stack trace, and root cause analysis are in
`.claude/logs/tasks/308_2026-07-05_supplier-consolidation-qa_qa-tester.md` (Bug #1) — summary:

- `POST /api/supplier-cabinet/tasks` with `"dueDate":"2026-07-10"` → 500.
- Npgsql exception: `Cannot write DateTime with Kind=Unspecified to PostgreSQL type
  'timestamp with time zone', only UTC is supported.`
- Reproduced through the real UI too: `/supplier/tasks` → "Нове завдання" → fill "Дедлайн" → submit
  → generic "Помилка збереження" toast, task silently not created (transaction rolls back cleanly,
  no orphan row, but the feature is unusable with a due date).

## Where to fix

`backend/ShelfGuard.Application/Features/Marketplace/SupplierTaskService.cs`:
- `CreateAsync`, line ~63: `DueDate = request.DueDate` — assigns the raw incoming `DateTime?`
  (JSON-deserialized from a date-only string like `"2026-07-10"`, which has `Kind=Unspecified`)
  directly to an entity property mapped to a `timestamptz` column.
- `UpdateAsync`, line ~91: `task.DueDate = request.DueDate` — same issue.

Suggested minimal fix: normalize to UTC before assignment in both places, e.g.

```csharp
DueDate = request.DueDate.HasValue
    ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc)
    : (DateTime?)null,
```

Alternative (bigger, optional): since `DueDate` is conceptually a calendar date (not a point in
time), consider switching `SupplierTask.DueDate` to `DateOnly?` / a `date` Postgres column instead
of `timestamptz` — this also avoids off-by-one-day issues when a UTC-shifted timestamp crosses
midnight relative to the user's local timezone. That's a schema change (new migration) though, so
treat the `SpecifyKind` fix as the immediate unblock and the `DateOnly` migration as an optional
follow-up.

Please also check other recently-added `DateTime?` request fields across the codebase for the same
pattern (this is a known project pitfall per the Npgsql provider — `EnableDynamicJson`-adjacent class
of issues, worth a quick grep for `.DueDate =`/`= request.` assignments feeding `timestamptz` columns
elsewhere) — not required for this fix, just a heads-up.

## Verify after fix

- `dotnet test` — should stay green (575+ baseline).
- Manual: `POST /api/supplier-cabinet/tasks` with a `dueDate` — should return 201, not 500.
- Manual: `PUT /api/supplier-cabinet/tasks/{id}` with a `dueDate` — should return 200.
- UI: `/supplier/tasks` → create task with a deadline — should succeed and show the date on the
  card.

## Not blocking, but worth a follow-up ticket (not this handoff's responsibility)

Bug #2 in the same task log: `supplier-alpha`/`supplier-beta` tenant/supplier `Name` columns contain
literal `?` bytes (real DB data corruption, pre-dates TASK-305's migration — the migration just
copies it forward). Visible in provider's Suppliers list and task board client names. Separate
data-cleanup task, not a code defect in TASK-305/306/307.
