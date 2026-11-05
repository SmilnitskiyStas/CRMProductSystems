# TASK-827/829/832: Beauty CRM control plane, backend ShelfGuard

**Date:** 2026-11-05
**Agent:** backend-developer
**Status:** done (review pending; not committed)

## What was done
- Outbound HMAC v1 client `IBeautyControlClient` (Infrastructure/Integrations/BeautyControlClient.cs): fresh nonce per request, 10 s timeout, no retries, typed `BeautyResult<T>`, Beauty error bodies never forwarded.
- Provider endpoints `/api/provider/beauty/tenants...` (ProviderOnly) + `BeautyProvisioningService`; every mutation writes ActivityLog (no invite URL/tokens).
- Inbound `POST /api/integrations/beauty/feedback` (HMAC, separate key, +-300 s, nonce replay, per-kid rate limit 60/min, idempotent by feedbackId) -> LandingLead source=beauty, RequestType, bug => critical, Telegram notifier.
- `GET /api/provider/leads?requestType=` filter; lead DTO fields requestType, beautyTenantName, beautyTenantSlug (+ beautyTenantRef, reporterRole, contact).
- Migration `AddBeautyControlPlane` (table beauty_tenant_links; landing_leads new columns, Message 1000 -> 2000, unique filtered index on ExternalId).
- Config only from env: BeautyControl__*, BeautyFeedbackInbound__Keys__{kid}__Secret|Status (compose + .env.*.example placeholders).

## Decisions
- Nonce store is in-memory (single provider API instance); restart reopens only the 300 s window, duplicates still blocked by feedbackId.
- Inbound body accepts flat fields and Beauty's nested tenant/user shape (contracts section 27.9).
- Suspend reason is optional on ShelfGuard side; default "suspended by provider operator".
- locationTimezone is accepted but not forwarded (not in Beauty create contract).

## Tests
- 2868 backend tests pass (dotnet test ShelfGuard.Tests), incl. new BeautyControl tests against the ADR-018 vector. DB-backed migration not applied/executed (no Docker run).

## Notes for next agent
- Beauty worker (TASK-821) must POST to /api/integrations/beauty/feedback signed with the inbound key.
- Apply migration before deploy; set BEAUTY_* secrets on the server.
