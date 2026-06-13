# TASK-069 — Worker: fiscalization retry job

**Date:** 2026-06-13
**Agent:** backend-developer (worker)
**Status:** review

## Summary

Implemented the fiscalization retry system end-to-end: EF entity change, new repository
method, two new backend endpoints, and the BullMQ cron worker job.

## Files Created

- `worker/src/jobs/fiscalization-retry.job.ts` — BullMQ Worker, cron `*/5 * * * *`
- `backend/ShelfGuard.Infrastructure/Migrations/20260613000000_AddPosTransactionRetryCount.cs` — EF migration
- `backend/ShelfGuard.Tests/Pos/FiscalizationRetryTests.cs` — 9 unit tests

## Files Modified

- `backend/ShelfGuard.Domain/Entities/PosTransaction.cs` — added `RetryCount` (int, default 0)
- `backend/ShelfGuard.Domain/Interfaces/IPosRepository.cs` — added `GetPendingFiscalizationAsync`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PosRepository.cs` — implemented `GetPendingFiscalizationAsync`
- `backend/ShelfGuard.Application/Features/Pos/Dtos/PosDtos.cs` — added `PendingFiscalizationDto`, `FiscalizeResultDto`
- `backend/ShelfGuard.Application/Features/Pos/IPosService.cs` — added two method signatures
- `backend/ShelfGuard.Application/Features/Pos/PosService.cs` — implemented `GetPendingFiscalizationAsync` + `FiscalizeTransactionAsync`
- `backend/ShelfGuard.Api/Controllers/PosController.cs` — added two endpoints (GET pending-fiscalization, POST fiscalize)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` — added `RetryCount` property
- `backend/ShelfGuard.Tests/Pos/PosServiceTests.cs` — added `GetPendingFiscalizationAsync` stub to `FakePosRepo`
- `worker/src/index.ts` — registered `fiscalization-retry` queue + worker

## Architecture Decisions

### Worker auth
The worker uses the existing `WORKER_API_EMAIL` / `WORKER_API_PASSWORD` service account
pattern (same as `ai-order.job.ts`): `POST /api/auth/login` → bearer token → `Authorization: Bearer`.
The two new endpoints are protected with `AtLeastStoreManager` policy, which the service
account role satisfies. No separate service token mechanism was needed.

### Endpoint visibility
`GET /api/pos/sales/pending-fiscalization` and `POST /api/pos/sales/{id}/fiscalize`
use `AtLeastStoreManager` (not `CanReceiveStock`) because the retry worker should not
need a storekeeper-level account — store managers and above are the natural service-account role.

### RetryCount semantics
- Always incremented on every attempt regardless of outcome.
- When RetryCount reaches 5: Status set to `fiscalization_failed` — no further retries.
- `fiscalized` transactions are returned immediately (idempotent, no retry).
- Transactions newer than 30 s are excluded from the retry list (avoids racing with
  the immediate post-sale async attempt in `PosService.CreateSaleAsync`).

### No explicit exponential backoff in the job
The cron fires every 5 min and queries `RetryCount < 5`. Natural backoff is implicit:
a transaction retried N times has already waited N * 5 min. The backend increments the
count so the next cron run's filter skips it if it just failed.

## Quality Gates

- `dotnet build` — green (0 warnings, 0 errors)
- `dotnet test` — **362/362 passed** (9 new tests in FiscalizationRetryTests.cs)
- `tsc --noEmit` — green (0 errors)
