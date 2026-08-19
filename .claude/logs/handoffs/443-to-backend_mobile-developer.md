# TASK-443 → Backend: POS retry idempotency gap

**Date:** 2026-07-29  
**From:** mobile-developer (Codex)  
**Priority:** high before enabling automatic POS retry

## Confirmed contract gap

`POST /api/pos/sales` accepts no client-generated idempotency key/request ID. The documented API
also has no endpoint that can reconcile a timed-out request by client request ID. A timeout can
therefore mean either:

1. the request never reached the server; or
2. the transaction committed but the response was lost.

Automatically sending the same request again can create a second sale. A stock `409` is not proof
that the first request committed and cannot safely be treated as idempotent success.

## Mobile behavior until backend support exists

- double taps within the running process share one in-flight promise;
- a timeout/network loss after submit is persisted as `uncertain`;
- an interrupted persisted `pending` submission restores as `uncertain`;
- automatic/manual resubmit is blocked in that state;
- cashier is instructed to reconcile the current shift's sales;
- cart is retained; only a confirmed `201 SaleDto` clears it.

## Requested backend contract

Add a client-generated `idempotencyKey` (UUID) to sale creation, enforce uniqueness per tenant,
persist the key atomically with `PosTransaction`, and return the original `SaleDto` for an exact
replay. Reject reuse with a different canonical request payload. Alternatively add an authenticated
lookup endpoint by client request ID with the same tenant scope. Document retention duration and
concurrent-request behavior.

Do not enable automatic mobile retry until this contract is implemented and tested.
