# Handoff: TASK-339 backend → TASK-340 frontend-developer (notification filtering — UI)

**From:** backend-developer · **Date:** 2026-07-12
**Backend state:** merged-ready on `main` working tree; `dotnet build` 0 errors, `dotnet test`
701/701 passed, `worker` `npx tsc --noEmit` clean. No DB migration needed here — schema
already landed in TASK-338.

## Endpoint — exact final signature

```
GET /api/notifications/history
  ?search=<string>          // optional, matches Title via ILIKE (case-insensitive substring)
  &eventType=<string>       // optional, exact match against EventType
  &userId=<guid>            // optional, exact match against UserId
  &storeId=<guid>           // optional, exact match against StoreId
  &dateFrom=<datetime>      // optional, CreatedAt >= dateFrom
  &dateTo=<datetime>        // optional, CreatedAt <= dateTo
  &page=<int>               // optional, default 1, clamped to >= 1
  &pageSize=<int>           // optional, default 50, clamped to [1, 200]
```

All query params are optional and combine with AND. Auth unchanged (`[Authorize]`,
tenant resolved from JWT `tenant_id` claim — same as before).

## Response shape (breaking change from the old unwrapped array)

```ts
// PagedResult<NotificationHistoryDto>
{
  items: NotificationHistoryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;   // computed server-side, Ceiling(totalCount / pageSize)
}

// NotificationHistoryDto — 3 new nullable fields appended at the end
{
  id: string;            // guid
  eventType: string;
  channel: string;       // "telegram" | "push" | "email" | "webhook"
  status: string;         // "sent" | "failed" | "skipped" | "pending"
  payload: string | null; // JSON string, opaque — same as before
  createdAt: string;      // ISO datetime
  isRead: boolean;
  readAt: string | null;
  title: string | null;   // NEW — short human-readable line, backs the search filter
  storeId: string | null; // NEW — guid, backs the "by store" filter
  userId: string | null;  // NEW — guid, the notified employee (null on old rows pre-TASK-338)
}
```

**Old rows** (created before TASK-338/339) will have `title = null` and `storeId = null` —
render a sensible fallback (e.g. derive a label from `eventType` + `payload`, same as the
existing `NotificationDetailDrawer.tsx` payload-parsing pattern) rather than showing blank.
`Channel = 'system'` rows never appear in this endpoint's results (server-side filter,
not client-visible) — no UI handling needed for that state.

The old unfiltered call (`GET /api/notifications/history` with no query params) still
works and returns page 1 of up to 50 items — but the response envelope changed shape
(`{items, totalCount, ...}` instead of a bare array), so `frontend/features/notifications/api/notifications.ts`
and `hooks/useNotifications.ts` need updating regardless of whether filters are used yet.

## New / newly-validated EventType values + recommended labels

Add to `EVENT_TYPE_LABELS` / `NotificationEventType` in `frontend/features/notifications/types.ts`:

| EventType | Recommended label (uk) | Notes |
|---|---|---|
| `iot.temp_alert` | Температурний алерт | Already live in prod (worker `handleTempAlert`), was missing a frontend label — display bug fix |
| `iot.offline` | Пристрій офлайн | Already live in prod (worker `handleIotOffline`), same display bug |
| `receipt.created` | Надходження товару | New — fires on `ReceiptService.ReceiveAsync`, has `storeId` populated |
| `order.replenishment_suggested` | AI-замовлення готове | New — fires from `ai-order.job.ts`, has `storeId` populated |
| `supplier.message` | Повідомлення постачальника | New — fires only supplier→client direction (client tenant is notified) |
| `supplier_agreement.signed` | Договір підписано | New — fires on `SupplierAgreementService.MarkSignedAsync`, client tenant is notified |

Recommend also adding an `EVENT_TYPE_SOURCE` entry for each (matches the existing
`{ service, actor }` shape) — e.g. `receipt.created` → `{ service: "Модуль надходжень",
actor: "Підтвердження поставки" }`, `order.replenishment_suggested` → `{ service: "AI
замовлення", actor: "Автоматичний прогноз" }`, `supplier.message` → `{ service:
"Маркетплейс", actor: "Чат з постачальником" }`, `supplier_agreement.signed` →
`{ service: "Маркетплейс", actor: "Угода про співпрацю" }`.

## Filter drawer (ADR-018 §4/§5 — already decided, no open question)

- Hand-rolled overlay panel following `NotificationDetailDrawer.tsx`'s existing
  fixed-panel + backdrop pattern — no new shadcn `Sheet` primitive.
- Filter state lives in component state + the React Query key (`["notifications",
  "history", filters]` or similar) — not synced to the URL (no precedent elsewhere in
  this repo).
- `storeId` filter needs a store picker — reuse whatever store-select component
  `frontend/features/stores/` or the sidebar `StoreSelector` already exposes.
- `userId` filter ("employee") needs a user picker scoped to the tenant — check
  `frontend/features/users/` for an existing list/select hook before building a new one.

## Not in scope here
- No changes to `notification_settings` UI (`NotificationSettingsTable.tsx`) — the 4 new
  EventTypes are outbox-driven (no per-user opt-in UI exists yet for `system`-originated
  events beyond the worker's own channel/settings check); if product wants users to be
  able to toggle e.g. `supplier.message` off, that's a separate settings-UI task.
