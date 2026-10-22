# TASK-757 — Edit draft orders + send to supplier (`ordered` status), backend

Endpoints (ReceiptsController, AtLeastStoreManager): PUT /api/receipts/{id}/header, POST /{id}/items,
DELETE /{id}/items/{itemId}, POST /{id}/send. Errors: 404 not found; 409 taken-by-other / not a draft;
400 validation (no supplier, last line, qty<=0, unknown supplier/product, no lines).
Taken guard: auto drafts only, draft status only (manual never locked; receiving an ordered receipt is not blocked).
Migration AddReceiptOrderedFields (OrderedAt, OrderedByUserId), applied to local dev DB. ReceiptDto += orderedAt, orderedByName.
'ordered' first-class: OrderCalcRepository.GetInTransitAsync + GetDelayedInTransitAsync (draft|ordered),
MovementRepository.GetIncomingAsync (draft|ordered), OrderTrackingRepository ordered -> confirmed (draft -> new, filterable),
receive/cancel accept draft|ordered|in_transit. AutoReplenishmentService untouched (it only loads untaken auto DRAFTs).
Tests: ReceiptSendEditTests (27), ReceiptOrderedStatusRepositoryTests (4, InMemory). Filtered dotnet test (Release): all pass.
No real-Postgres flow test added (skipped as not cheap). Not committed.
