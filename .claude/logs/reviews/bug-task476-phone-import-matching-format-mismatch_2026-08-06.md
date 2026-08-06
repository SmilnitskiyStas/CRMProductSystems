# TASK-476 defect — post-campaign import: phone-based customer matching silently fails unless the customer's stored phone is already in canonical +380 form

**Date:** 2026-08-06
**Severity:** high (silently corrupts the feature's core "existence validation" promise — real, known customers get bucketed as "unknown")
**Task:** TASK-476 (found during E2E acceptance of Фаза 4 post-campaign audience analysis, TASK-471/472/473)
**Status:** open — not fixed (QA reports, does not fix, per this task's brief)

## Bug

`POST /api/marketing-analytics/post-campaign/segments/import` resolves a pasted/uploaded phone
number to a real `Customer` only when that customer's `Phone` column happens to already be stored
in the exact canonical `+380XXXXXXXXX` form. Any other equally-valid, equally-accepted-at-creation
phone format (e.g. `0501234567`, `050 123 45 67`, `380501234567` with no leading `+`) causes the
import to silently misclassify a real, existing customer as **unknown** — the exact failure mode
this whole feature (source doc `docs/uployal/AUDIENCE_ANALYSIS.md` §8/§35.1/§38) exists to prevent.

## Root cause

`PostCampaignRepository.FindCustomersByIdsOrPhonesAsync`
(`backend/ShelfGuard.Infrastructure/Data/Repositories/PostCampaignRepository.cs:56-60`):

```csharp
return await _db.Customers
    .Where(c => c.TenantId == tenantId &&
        (candidateIds.Contains(c.Id) || (c.Phone != null && candidatePhones.Contains(c.Phone))))
```

`candidatePhones` always comes from `PhoneNormalizer.Normalize(...)` in `SegmentImportParser.Classify`
— always the canonical `+380XXXXXXXXX` form, regardless of how the marketer typed/pasted the number.
This is compared with a **raw string equality** against `c.Phone` **as stored**, with no
normalization applied to the stored side.

`Customer.Phone` is never normalized at write time. Confirmed both write paths:
- `CustomerService.cs:54,86` — `Phone = dto.Phone?.Trim()` (trim only), validated only by the
  intentionally permissive `PhoneRegex` (`CustomerService.cs:123-124`):
  `^\+?[\d\s\-()]{7,20}$` — accepts `0501234567`, `050 123 45 67`, `(050) 123-45-67`, etc., all
  without a leading `+`/`380`.
- `AutoServiceService.cs:43,63` — `Phone = dto.Phone` (not even trimmed).

Only `LoyaltyService.cs` (customers created/linked via the consumer loyalty self-service flow)
normalizes via `PhoneNormalizer.Normalize` before writing — so a customer's phone format at rest
depends entirely on *which* internal code path created that specific `Customer` row, invisible to
the marketer doing the import.

## Live repro (2026-08-06, dev stack, tenant "Свіжий Кут")

13 of this tenant's 14 real seeded customers have `Phone` stored **without** a leading `+`
(e.g. `"380501110001"`). Importing their real phone numbers, correctly formatted
(`+380501110008`, `+380 50 111 00 09`, `0501110010`, `380-50-111-00-11` — all four are DIFFERENT,
individually valid textual formats of four different real customers), returned:

```json
{"uploadedCount":15,"matchedCount":3,"duplicateCount":2,"unknownCount":5,"invalidCount":5,
 "unknownTokensSample":["+380501110008","+380 50 111 00 09","0501110010","380-50-111-00-11", ...]}
```

All 4 phone-based tokens for real customers came back **unknown**; only the 3 GUID-based tokens in
the same submission matched. Control test: the one customer in this tenant whose `Phone` happens to
already be stored **with** a leading `+` (`"+380991110410"`) matched correctly when pasted — proving
the matching mechanism itself works, and the gap is specifically the stored-format mismatch.

GUID-based matching is unaffected (`Customer.Id` has no formatting variance).

## Why the existing test suite didn't catch it

`PostCampaignServiceTests.cs` mocks `IPostCampaignRepository.FindCustomersByIdsOrPhonesAsync`
entirely (`_repo.FindCustomersByIdsOrPhonesAsync(...).Returns([...])`) — the mock always returns
whatever `MatchedCustomerRow` the test wants, so the test proves `PostCampaignService`'s own logic
is correct given a correct repository answer, but never exercises the real EF/SQL translation of
`candidatePhones.Contains(c.Phone)` against a realistically-varied stored `Phone` value. No
repository-level integration test (hitting a real Postgres DB) exists for this feature at all
(confirmed: no `PostCampaignRepositoryTests.cs`, unlike Фаза 3's `AudienceBuilderRepositoryIntegrationTests.cs`).

## Expected

A phone number typed/pasted in any of the formats `CustomerService`'s own `PhoneRegex` already
accepts as valid customer data should resolve to the same real customer, regardless of how that
customer's phone happens to be stored at rest.

## Suggested fix directions (not applied — flagging for a scoped follow-up)

1. Normalize on the **read side**: compare a normalized form of `c.Phone` in the query. This
   repository is otherwise 100% LINQ (own class doc comment) — normalizing inside a LINQ `Where`
   isn't directly translatable to SQL for arbitrary format variance, so this likely needs either a
   raw-SQL regex-strip predicate (a first for this feature) or client-eval fallback (defeats
   indexing/scale for large tenants).
2. Normalize `Customer.Phone` at write time going forward (`CustomerService.CreateAsync`/`Update`,
   `AutoServiceService`) **plus** a one-time backfill migration for existing rows — the more
   durable fix, but a real product/schema decision (does `Customer.Phone` become
   `+380XXXXXXXXX`-only going forward? does the existing permissive regex change?).
3. Partial/faster: add a separate, always-normalized `NormalizedPhone` column populated at write
   time (trigger or app-level), matched against that instead of `Phone` directly — avoids touching
   the existing loosely-formatted `Phone` column's display semantics.

Needs a product/architecture decision before implementation, not a one-line patch.
