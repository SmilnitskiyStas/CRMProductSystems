# TASK-339: Notification triggers + history filtering (backend) — DONE

**Agent:** backend-developer · **Date:** 2026-07-12

## Scope
Per ADR-018 and handoff `338-to-339_backend-developer.md` (database-engineer added `StoreId`/`Title` + indexes to `NotificationQueue`): new EventTypes, Postgres outbox writes from 3 backend services, worker dispatch job, `ai-order.job.ts` rewire, and filtered/paginated `GET /api/notifications/history`.

## What was built

1. **EventType validation** — `NotificationService.ValidEventTypes` gained `iot.temp_alert`, `iot.offline` (already in prod, previously unvalidated) plus 4 new: `receipt.created`, `order.replenishment_suggested`, `supplier.message`, `supplier_agreement.signed`.

2. **Postgres outbox writes** (ADR-018 §2) — each service now takes `INotificationRepository` and calls the existing `EnqueueAsync` with `UserId = null`, `Channel = "system"`, `Status = "pending"`:
   - `ReceiptService.ReceiveAsync` (after `Status = "received"`) → `receipt.created`, `StoreId = DestinationStoreId`, Title `"Надходження товару: {supplierName}"`.
   - `SupplierChatService.SendMessageAsync` — only when `session.SupplierTenantId == senderTenantId` (supplier → client direction) → `supplier.message`, `TenantId = ClientTenantId`, Title = sender name + 80-char excerpt.
   - `SupplierAgreementService.MarkSignedAsync` → `supplier_agreement.signed`, `TenantId = ClientTenantId`, Title `"Договір підписано: {supplierName}"`.
   - All 3 test files (`ReceiptServiceTests`, `SupplierChatServiceTests`, `SupplierAgreementServiceTests`) updated with an `INotificationRepository` substitute in the constructor.

3. **Worker dispatch job** — new `worker/src/jobs/notification-dispatch.job.ts`, cron `* * * * *` (every minute), registered in `worker/src/index.ts`. Polls `notification_queue WHERE Status='pending' AND Channel='system'` (batch 50), resolves recipients via a `DISPATCH_EVENT_ROLES` role matrix (judgment call, no explicit product spec — mirrors `EXPIRY_EVENT_ROLES` shape), checks `notification_settings` (explicit settings win, role defaults as fallback), delivers via `deliver()`, logs real per-user rows via `logNotifications()`, marks the intent row `Status='dispatched'`.

4. **`ai-order.job.ts` rewire** — `notifyManagers` no longer loops `sendTelegramMessage` directly (previously also missing tenant scoping — queried all managers regardless of tenant). Now: role query scoped to `tenantId` (stores query extended to select `TenantId`), `notification_settings` check with `["telegram"]` default fallback, `deliver()` + `logNotifications()`, `EventType = "order.replenishment_suggested"`. No outbox hop — runs in-process in the worker already.

5. **History filtering/pagination** — `GET /api/notifications/history` now takes `[FromQuery] NotificationHistoryQuery` (`search`, `eventType`, `userId`, `storeId`, `dateFrom`, `dateTo`, `page`, `pageSize`) and returns `PagedResult<NotificationHistoryDto>`. `search` uses `EF.Functions.ILike(q.Title, "%term%")` to hit the pg_trgm GIN index. `GetHistoryAsync` unconditionally excludes `Channel = 'system'` rows. `NotificationHistoryDto` gained `Title`, `StoreId`, `UserId`.

## Build / test status
- `dotnet build` (full solution) — 0 errors, 0 warnings.
- `dotnet test` (full suite) — 701/701 passed, no regressions.
- `worker`: `npx tsc --noEmit` — clean, no errors.

## Files changed
- `backend/ShelfGuard.Application/Features/Notifications/NotificationService.cs`, `INotificationService.cs`, `Dtos/NotificationDtos.cs`
- `backend/ShelfGuard.Domain/Interfaces/INotificationRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/NotificationRepository.cs`
- `backend/ShelfGuard.Api/Controllers/NotificationsController.cs`
- `backend/ShelfGuard.Application/Features/Receipts/ReceiptService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/SupplierChatService.cs`, `SupplierAgreementService.cs`
- `backend/ShelfGuard.Tests/Receipts/ReceiptServiceTests.cs`, `Marketplace/SupplierChatServiceTests.cs`, `Marketplace/SupplierAgreementServiceTests.cs` (constructor updates)
- `worker/src/jobs/notification-dispatch.job.ts` (new)
- `worker/src/jobs/ai-order.job.ts`, `worker/src/index.ts`

## Next
Handoff to frontend-developer at `.claude/logs/handoffs/339-to-340_frontend-developer.md` with exact endpoint signature, DTO shape, and recommended `EVENT_TYPE_LABELS` for the 6 new/newly-validated event types.
