# TASK-552 — OpenAPI publication + MOBILE_API.md (documentation-writer half)

**Status:** done
**Agent:** documentation-writer
**Date:** 2026-08-18
**Depends:** backend half (done, `.claude/logs/tasks/552_2026-08-18_openapi-publication_backend-developer.md`)

## What was built

- `docs/integration/MOBILE_API.md` (new) — per-endpoint reference for all 16 `/api/v1/` endpoints
  across 8 controllers (`MobileConfigController`, `MobileConfigDraftController`,
  `MobileConfigPreviewController`, `MobileConfigPublishController`, `MobileConfigVersionsController`,
  `MobileThemeController`, `MobileBlocksController`, `RetailersController`). Each endpoint documents
  purpose/auth/tenant-resolution mechanism/request/response/errors, following the CLAUDE CODE SPEC
  §29 template. Also documents: the three tenant-resolution mechanisms in use
  (`ITenantContext`/`ITenantSessionOverride`/consumer-claim+slug), the two error-shape conventions,
  the permanent alias relationship to `ConsumerLoyaltyController`'s pre-existing endpoints, and the
  built-but-not-wired `IConsumerFeatureFlagService` (TASK-543).
- `docs/integration/CHANGELOG.md` (new) — convention established per CLAUDE CODE SPEC §33's example
  format (date / what changed / schema-version impact / compat note), backfilled with 17
  newest-first entries covering TASK-527 through TASK-552's real history, not just a single "today"
  entry.
- Reconciliation section (in `MOBILE_API.md` §6-7): explicit status for every one of the six
  `docs/integration/MOBILE_API_STAGE_*.md` mobile-workstream request files — all read in full and
  verified against current code before writing a status, not assumed from the orchestrator's summary
  alone.

## Reconciliation results (all six files, no silent drops)

| File | Status |
|---|---|
| `MOBILE_API_STAGE_2.md` | **Resolved, with a discrepancy.** Requested `DELETE /api/v1/retailers/{tenantId}/membership`; TASK-548 shipped `DELETE /api/v1/retailers/{slug}/membership`. Everything else in the request (identity from JWT only, ownership check, idempotent-absent success, retained history, `204`, structured errors, isolation tests) matches. Flagged in `MOBILE_API.md` — mobile needs to adapt to `{slug}`, or a follow-up task adds a `{tenantId}` alias; not decided here. |
| `MOBILE_API_STAGE_9.md` | **Open, out of scope.** Loyalty tier field — verified `LoyaltyMembershipSummaryDto` has no `tier` field; unrelated feature, never touched by Stage 6. |
| `MOBILE_API_STAGE_10.md` | **Open, out of scope.** Category/product/promotion detail endpoints — verified `GET /api/consumer/{tenantId}/catalog` is still the existing paginated-list-only endpoint; unrelated feature. |
| `MOBILE_API_STAGE_11.md` | **Open, in-scope, unresolved — genuine divergence.** See below. |
| `MOBILE_API_STAGE_12.md` (icon whitelist) | **Resolved.** `MobileConfigWhitelists.NavigationIcons` verified byte-for-byte identical to `mobile/features/mobile-config/validation.ts`'s AJV enum by reading both files directly. |
| `MOBILE_API_STAGE_12.md` (preview token) | **Open, in-scope, unresolved — genuine divergence.** See below. |
| `MOBILE_API_STAGE_14.md` | **Open, out of scope.** Analytics ingestion — verified no ingestion endpoint of any kind exists in the codebase; unrelated feature. |

## The two open divergences (restate verbatim for the user — do not bury)

**A. QR/deep-link invite security model.** `MOBILE_API_STAGE_11.md` requested an opaque, signed,
expiring invite token (`POST /api/consumer/retailer-invites/resolve`), explicitly warning against
treating QR content as an arbitrary URL. TASK-549 shipped `GET /api/v1/retailers/{slug}/public` — a
plain, unsigned, human-readable tenant slug used directly in a public URL
(`https://<domain>/join/{slug}`), with no expiry and enumerable-by-design semantics (the same slug
used for public discovery elsewhere). This is a materially different security model, not an
implementation variant of the same design. Documented in `MOBILE_API.md` §7A; requires a product/
security decision this documentation pass does not make.

**B. Staff preview mechanism.** `MOBILE_API_STAGE_12.md` anticipated a short-lived, scoped,
single-purpose preview token sent via `X-Mobile-Preview-Token`, implying the mobile app itself
renders a live preview. TASK-547 shipped `GET /api/v1/mobile/config/preview` gated by a normal
`AtLeastEnterpriseAdmin` staff JWT — no token mechanism, implicitly assuming a web admin UI polls it
instead. Documented in `MOBILE_API.md` §7B; requires an architecture decision this documentation
pass does not make.

## Verification

- `git status --porcelain -uall docs/integration/` — only `MOBILE_API.md` and `CHANGELOG.md` are new
  from this task; the six `MOBILE_API_STAGE_*.md` files and `deep-link-onboarding.md` are pre-
  existing untracked files from the mobile workstream, confirmed unmodified.
- No code, migrations, or `.claude/tasks/mobile-roadmap.md` changes made (orchestrator's
  responsibility per the brief).
- Every controller (`RetailersController.cs`, `MobileConfigController.cs`,
  `MobileConfigDraftController.cs`, `MobileConfigPreviewController.cs`,
  `MobileConfigPublishController.cs`, `MobileConfigVersionsController.cs`,
  `MobileThemeController.cs`, `MobileBlocksController.cs`) and their DTOs
  (`Features/MobileConfig/Dtos/*`, `Features/Loyalty/Dtos/LoyaltyDtos.cs`) were read directly, not
  inferred from the OpenAPI doc alone. `backend/openapi.json`'s `/api/v1/*` paths were grepped and
  cross-checked against the controller-derived list — exact match, 16 endpoints / 13 route
  templates.
- Icon-whitelist resolution independently re-verified by reading
  `mobile/features/mobile-config/validation.ts` directly, not just trusting TASK-542's log claim.
- `AppPolicies.AtLeastEnterpriseAdminRoles` (`provider`, `enterprise_admin`) read directly from
  `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs` to state the auth requirement
  precisely rather than paraphrasing.

## Files changed

- `docs/integration/MOBILE_API.md` (new)
- `docs/integration/CHANGELOG.md` (new)
- `.claude/logs/tasks/552_2026-08-18_mobile-api-docs_documentation-writer.md` (this file)

## Next

Orchestrator marks TASK-552 `done` in `.claude/tasks/mobile-roadmap.md` and surfaces the two open
divergences (A: QR invite security model, B: preview mechanism) to the user as explicit decisions —
not made in this pass.
