# TASK-554 — Security review of the consumer-platform surface

**Status:** done
**Agent:** security-reviewer
**Depends on:** Stages A–E substantially complete (TASK-527–553)

## Scope

Reviewed the entire Stage 6 consumer app-builder surface against the 4 checklist items plus 3
already-flagged backlog items in the roadmap's TASK-554 brief: RBAC on Retailer Admin, upload
hardening, rate limiting on join/publish/QR-public endpoints, stored-XSS on admin-authored free
text. Read every controller/validator/page file directly rather than trusting prior task logs'
claims — see the review log for the full evidence trail.

## Findings

1. **RBAC — PASS.** All 5 admin routes have matching frontend guard + backend
   `[Authorize(Policy = AtLeastEnterpriseAdmin)]`. Confirmed `MobileConfigPreviewController`'s
   separate-controller design genuinely avoids the `[AllowAnonymous]`-inheritance trap (verified
   the ASP.NET Core middleware behavior this claim rests on), and checked every other controller
   in the surface for the same risk — none found. No IDOR: tenant always resolved server-side from
   `ITenantContext`, never client input; rollback correctly 404s on a foreign-tenant version id.
2. **Upload hardening — confirmed none needed.** No `IFormFile` anywhere in this surface (verified
   by grep, not assumed). `logoUrl` is validated server-side as http(s)-only, never fetched/proxied
   server-side — no SSRF path.
3. **Rate limiting — real gap found, fixed directly.** `GET /api/v1/retailers/{slug}/public`
   (anonymous, no per-account accountability, uniform-404 enumeration surface) had no rate limit.
   Added `retailer-public-lookup` policy (20 req/min/IP, same `FixedWindowRateLimiter` pattern as
   the 4 existing anonymous-endpoint policies) in `Program.cs` +
   `[EnableRateLimiting("retailer-public-lookup")]` on `RetailersController.GetRetailerPublic`.
   `POST /{slug}/join` and `POST /mobile/config/publish` deliberately left alone — both are
   authenticated, and no authenticated business endpoint anywhere in the ~74-controller API carries
   a per-endpoint rate limit (confirmed, including the pre-existing `ConsumerLoyaltyController.Join`
   this endpoint aliases) — adding one only here would invent a new convention, not apply the
   existing one.
4. **Stored-XSS — PASS.** Zero `dangerouslySetInnerHTML`/`innerHTML`/`srcDoc`/`iframe`/`eval`
   anywhere in `frontend/features/consumer-app/` or the join page. All admin text renders via JSX
   (auto-escaped) or safe attributes. Backend serves raw `application/json`, never pre-rendered
   HTML — verified directly in `MobileConfigController`/`MobileConfigPreviewController` source.
5. **Backlog reconfirmation** (TASK-540 block-props validation, TASK-542 label maxLength,
   TASK-551 `GetRetailers` N+1) — all three re-verified against current source, still accurate,
   still low/medium severity, no new task needed. Added one observation to TASK-551's existing scope
   (N+1 + no rate limit on that endpoint together mean its cost scales with total platform-wide
   tenant count on every uncapped call) rather than opening a new task, since the eventual fix is
   the same one.
6. **Additional, unprompted checks:** independently re-verified the RLS triad
   (`tenant_isolation`+`NULLIF` guard, `provider_bypass`, `worker_bypass`, `FORCE ROW LEVEL
   SECURITY`) directly from the migration SQL for all 3 new tables, and confirmed
   `RlsCrossTenantIntegrationTests`'s "every FORCE-RLS table" assertion is genuinely dynamic
   (queries `pg_catalog` at runtime) rather than a hardcoded list.

## Fix applied

- `backend/ShelfGuard.Api/Program.cs` — new `retailer-public-lookup` rate-limit policy.
- `backend/ShelfGuard.Api/Controllers/RetailersController.cs` — `[EnableRateLimiting(
  "retailer-public-lookup")]` on `GetRetailerPublic`, plus a `using
  Microsoft.AspNetCore.RateLimiting;` and a doc-comment note.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, untouched by this task).
- `dotnet test` (full suite, real Postgres, `--no-build`) — **1685/1685 passed, 0 skipped** —
  identical to the pre-fix baseline, confirming no regression from the rate-limit change.

## Follow-up tasks registered

None. No finding rose to critical/high severity — the one real (medium) gap was fixed directly per
the task brief's guidance to prefer a small, well-understood fix over spinning off a follow-up.
TASK-540/542/551 remain the correct homes for the three reconfirmed low-severity items.

## Log

`.claude/logs/reviews/2026-08-18_consumer-platform-security-review.md` — full findings, evidence,
and reasoning per checklist item.
