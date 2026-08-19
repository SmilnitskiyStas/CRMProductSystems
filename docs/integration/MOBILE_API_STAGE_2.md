# Mobile Stage 2 — Backend Integration Notes

Date: 2026-08-17

## Contracts used by mobile

The Stage 2 discovery/add/switch flow uses existing consumer JWT endpoints:

```http
GET  /api/consumer/loyalty/memberships
GET  /api/consumer/loyalty/networks
POST /api/consumer/loyalty/{tenantId}/join
```

The memberships response is the source of truth. A locally restored `activeTenantId` is
accepted only when a matching membership has `status: "active"`; otherwise mobile falls back
to the first active membership or clears the selection.

## Missing contract: remove retailer

The product specification requires customers to remove a retailer from "My Retailers", but no
consumer endpoint currently exists. Mobile deliberately does not fake this operation locally.

Requested contract:

```http
DELETE /api/v1/retailers/{tenantId}/membership
Authorization: Bearer <consumer JWT>
```

Required behavior:

- resolve `consumer_account_id` only from the authenticated token;
- verify that the membership belongs to that consumer and tenant;
- idempotent success for an already absent membership is preferred;
- define whether ledger/history is retained, anonymized or deleted;
- return 204 on success;
- return standardized 401/403/404 ProblemDetails errors;
- add tenant-isolation and cross-consumer authorization tests;
- update OpenAPI and `docs/integration/MOBILE_API.md`.

After this contract exists, mobile can add a destructive confirmation action, remove the tenant
from membership cache, reconcile `activeTenantId`, and clear all removed-tenant query data.
