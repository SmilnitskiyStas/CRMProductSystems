# BUG-005 — pos_transactions.RetryCount missing on production

**Status:** done · **Agent:** database-engineer · **Updated:** 2026-06-16

## Problem
Flagged in TASK-204 log (2026-06-16): production logs showed
`PosService.GetPendingFiscalizationAsync` throwing
`column p.RetryCount does not exist` on `pos_transactions`. The column is part of the
domain model (`PosTransaction.RetryCount`, added for TASK-069 fiscalization retry job,
committed 2026-06-13 as migration `20260613000000_AddPosTransactionRetryCount`), and is
used throughout `PosService`/`PosRepository`/`PosDtos` — but the migration never actually
landed on the production schema, even though it was committed to git on 2026-06-13.

## Root cause
The original `20260613000000_AddPosTransactionRetryCount` migration predates all the v4
entity-rename migrations (`V4LocationsRename`, `V4ItemsRename`, `V4ItemEntityRename`).
It's a small, isolated `AddColumn` with no risk of conflicting with those — most likely
it was simply never deployed (no prod deploy ran between TASK-069 landing and the v4 work
starting), so the column genuinely doesn't exist on the live DB despite being in
`AppDbContextModelSnapshot.cs` and used by app code.

## Fix
Deleted the stale `20260613000000_AddPosTransactionRetryCount` migration files and
regenerated it with a current timestamp (`20260616151654_AddPosTransactionRetryCount`)
so it sits correctly after the latest applied v4 migrations and will run cleanly on the
next deploy via the existing `deploy.sh` auto-migrate-on-start flow. Migration content
unchanged (single `AddColumn<int>("RetryCount", "pos_transactions", default 0)`).

## Verification
- `dotnet build ShelfGuard.Infrastructure` → green
- `dotnet test --filter "FullyQualifiedName~Pos"` → 76/76 green
- Not yet applied to production — will apply automatically on next deploy (per existing
  pattern, confirmed working in TASK-204 deploy log).

## Next
Next deploy should be checked: confirm migration log shows
`Applying migration '20260616151654_AddPosTransactionRetryCount'` and that
`GetPendingFiscalizationAsync` / fiscalization retry worker stop throwing on prod.
