---
task_id: BUG-004
date: 2026-06-11
agent: backend-developer (via qa-tester)
status: done
---

# BUG-004 — Inconsistent 404 error format

## Problem
Body-less client error results (bare `NotFound()` — 25 call sites across 7 controllers)
were auto-transformed by `[ApiController]` into ASP.NET ProblemDetails
(`{type, title, status, traceId}`), violating the api-contracts.md error
contract `{ "error": "..." }`. Automatic 400s from model binding returned
ValidationProblemDetails with the same issue.

## Fix (central — zero controller edits)
| File | Change |
|---|---|
| `ShelfGuard.Api/Infrastructure/ErrorBodyClientErrorFactory.cs` | New `IClientErrorFactory`: converts body-less 4xx results to `{error}` with per-status message |
| `ShelfGuard.Api/Program.cs` | Registers the factory + `InvalidModelStateResponseFactory` returning `{error: firstValidationMessage}` for binding/validation 400s |

Controllers that already pass a body (`NotFound(new { error })`, `BadRequest(new { error })`)
are unaffected — their custom messages flow through unchanged.

## Production verification (2026-06-11)
| Case | Result |
|---|---|
| GET /api/products/{bad-id} → 404 | `{"error":"Not found."}` ✅ |
| GET /api/stores/{bad-id} → 404 | `{"error":"Not found."}` ✅ |
| Malformed JSON → 400 | `{"error":"'b' is an invalid start of a property name…"}` ✅ |
| Valid GET /api/products | 200, unaffected ✅ |
| Business 400 (discount > 100%) | `{"error":"DiscountPercent must be between 0.01 and 100."}` — custom message preserved ✅ |

## Notes
- Frontend `lib/api.ts` reads `body.error ?? "HTTP {status}"` — now always gets the message.
- 2 pre-existing `AuthServiceTests` failures confirmed unrelated (fail on clean tree too;
  mock token generator returns "" — separate issue, flagged for follow-up).
