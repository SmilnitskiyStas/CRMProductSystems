# TASK-444 handoff — receipt creation and mutation reconciliation

**From:** mobile-developer  
**Date:** 2026-07-29

## Contract gaps

1. Mobile has no receipt-create form, create payload, or `POST /receipts` API function. Its only
   receipt operation is processing and confirming a server-created receipt. A durable
   **receipt-creation** draft cannot be wired without an approved payload and UX.
2. Create write-off, transfer, and production-order endpoints expose no client idempotency key
   and no lookup by client operation id. A timeout is therefore ambiguous: the document may
   already exist. Mobile retains an `uncertain` draft and blocks blind retry.

## Requested decisions/contracts

- Confirm whether mobile must create receipts, and document the exact create DTO plus reference
  lookup APIs (supplier, destination, products/batches).
- Add a client-generated idempotency key, or a reconciliation endpoint, to all non-idempotent
  operational creates.
- Keep FEFO and stock allocation server-authoritative. Mobile sends selected reference IDs and
  quantities only; it does not calculate FEFO.

No backend or frontend implementation was read or changed for TASK-444.
