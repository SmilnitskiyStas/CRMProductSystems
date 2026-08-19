# Integration Changelog — Consumer-Platform API

**Owner:** documentation-writer
**Convention established:** 2026-08-18 (TASK-552). Entries are newest-first. Every entry that
touches the wire contract (`/api/v1/` endpoints or `contracts/mobile-config.schema.json`) records
what changed, the `schemaVersion` impact, and whether it's backward compatible — matching the
example format in `docs/CLAUDE CODE SPEC — Web Admin, App Builder & Backend.md` §33. Entries for
tasks that changed domain/infrastructure code without touching the wire contract are marked
**(no contract change)** and kept brief.

This file is backfilled from the actual TASK-527 through TASK-552 history (`.claude/logs/tasks/`),
not started blank on the day this convention was written. Going forward: **update this file in the
same task/PR as any `/api/v1/` endpoint or `contracts/mobile-config.schema.json` change** — do not
defer it to a later documentation pass.

---

## 2026-08-18 — Product decision: two mobile-workstream divergences resolved (no contract change)

TASK-552's documentation pass flagged two genuine, undecided divergences between what
`docs/integration/MOBILE_API_STAGE_11.md`/`_12.md` (mobile workstream notes) anticipated and what
Stages E/D actually shipped. The product owner decided both the same day — see
`docs/integration/MOBILE_API.md` §7 for full rationale:

- **QR/deep-link invite security** (`MOBILE_API_STAGE_11.md`): the shipped plain-slug
  `GET /api/v1/retailers/{slug}/public` design is kept. No signed/opaque/expiring invite-token
  contract will be built — the slug is already public elsewhere in this API, so a token wouldn't
  meaningfully reduce exposure, and the actual join still requires full consumer auth.
- **Staff preview mechanism** (`MOBILE_API_STAGE_12.md`): `GET /api/v1/mobile/config/preview`
  stays web-admin-only (`AtLeastEnterpriseAdmin` JWT). No `X-Mobile-Preview-Token`/mobile-renders-
  preview mechanism will be built — the mobile client only ever reaches the published,
  never-draft `GET /api/v1/mobile/config`.

Schema version: unaffected. No contract change — both endpoints' actual wire shape is exactly what
TASK-547/TASK-549 already shipped; this entry records that no further change is coming, so the
mobile workstream can close out `MOBILE_API_STAGE_11.md`/`_12.md`'s preview-token item against the
current implementation rather than waiting on a new endpoint.

## 2026-08-18 — TASK-552: OpenAPI publication

Published `backend/openapi.json` (committed, ~1.17 MB, 351 paths / 424 schemas) as a source-
controlled snapshot of the full Swashbuckle-generated API surface, regenerated via a pinned
`Swashbuckle.AspNetCore.Cli` local tool. Required `Program.cs`'s `AddSwaggerGen` to add
`c.CustomSchemaIds(type => type.FullName)` — a real schema-name collision across feature namespaces
(e.g. two different `CustomerDetailDto`s) forced this. **Schema names in the published document are
now fully namespace-qualified**, not bare class names — anyone reading `openapi.json` directly needs
to know this. Also established `docs/integration/MOBILE_API.md` (this changelog's sibling) and this
file itself.
Schema version: unaffected (no `mobile-config.schema.json` change). Backward compatible — pure
documentation/tooling addition, no runtime behavior changed.

## 2026-08-18 — TASK-551: API versioning audit

Audit-only pass confirming all 8 consumer-platform controllers built since TASK-534 are correctly
under `/api/v1/` (no stragglers, cross-checked against all 74 controllers in the API project), error
shape and UTC date handling are consistent, and the two endpoints that don't paginate genuinely don't
need to. **No code changed.** One real finding flagged as follow-up, not fixed inline:
`RetailersController.GetRetailers` N+1-queries every active tenant platform-wide — fixing it would
touch the pre-existing `ConsumerLoyaltyController.GetNetworks` sibling (out of this task's scope) and
would be a breaking wire-format change with no current consumer.
Schema version: unaffected. No contract change.

## 2026-08-18 — TASK-550: Audit log wiring **(no contract change)**

Wired `ActivityLog` entries into `MobileConfigDraftService.SaveDraftAsync` (`mobileconfig.draft_saved`,
plus `mobileconfig.feature_flags_changed` when the `features` object diffs), `MobileConfigPublishService.PublishAsync`/`RollbackAsync`
(`mobileconfig.published` / `mobileconfig.rolled_back`), and `MobileThemeService.UpdateThemeAsync`
(`mobileconfig.theme_updated`, which required adding an `actingUserId` parameter to that method —
internal signature change only, no HTTP contract change since `MobileThemeController` already
resolved and threaded the acting user's id). Reused the existing generic `ActivityLog` table per
decision 3 (no new table/migration). Role-change and promotion-edit audit coverage investigated and
found already-covered / genuinely out of scope respectively — no code added for either.

## 2026-08-18 — TASK-549: QR/deep-link onboarding, backend half

**New endpoint:** `GET /api/v1/retailers/{slug}/public` (`[AllowAnonymous]`) — minimal public
retailer info (`name`, `slug`, `logoUrl`, `joinable`) for the QR/deep-link onboarding web fallback
page, reached before a visitor has the app or a consumer session. Deliberately a distinct
route/DTO/service method from the existing authenticated `GET /api/v1/retailers/{slug}` — that
endpoint's DTO carries a full store list and internal tenant GUID, unsafe to expose anonymously.
Same indistinguishable-404 policy as the authenticated sibling endpoint (unknown slug / inactive
tenant / no loyalty module / paused program all → one generic 404).
**Divergence from the originally-requested contract** (`MOBILE_API_STAGE_11.md` asked for a signed,
expiring invite token) — see `MOBILE_API.md` §7A for the full flag. Frontend web-fallback page
(`frontend/app/[locale]/join/[slug]/page.tsx`) shipped the same day as a separate task; see
`docs/integration/deep-link-onboarding.md`.
Schema version: N/A (this endpoint doesn't serve the mobile-config document). Backward compatible —
purely additive new endpoint.

## 2026-08-18 — TASK-548: Retailer discovery API

**New endpoints** (`RetailersController`, new file): `GET /api/v1/retailers`,
`GET /api/v1/retailers/{slug}`, `POST /api/v1/retailers/{slug}/join`,
`DELETE /api/v1/retailers/{slug}/membership`. Generalizes `ConsumerLoyaltyController`'s
tenant-id-addressed network/join endpoints into a slug-addressed `/api/v1/` surface — the old
endpoints (`GET /api/consumer/loyalty/networks`, `POST /api/consumer/loyalty/{tenantId}/join`) are
kept as a **permanent, un-deprecated alias**, not touched, still calling the identical service
methods.
**New capability:** `DELETE .../membership` (leave a loyalty network) — no such endpoint existed
anywhere before this task. Required adding a third `LoyaltyMembershipStatus` value (`left`, soft-
deactivation, never hard-delete) and fixing a related idempotency gap so rejoining after a leave
reactivates the membership instead of handing back a permanently-`left` row.
**DTO change:** `LoyaltyNetworkSummaryDto` gained an additive `Slug` field — flows through to the
pre-existing `GET /api/consumer/loyalty/networks` response too (additive, non-breaking).
**Contract discrepancy vs. the original mobile request** (`MOBILE_API_STAGE_2.md` asked for
`{tenantId}` addressing on the delete endpoint; shipped as `{slug}`) — see `MOBILE_API.md` §6 for
the full flag.
Schema version: N/A. Backward compatible — additive endpoints + additive DTO field; nothing removed
or renamed.

## 2026-08-18 — TASK-547: Preview API

**New endpoint:** `GET /api/v1/mobile/config/preview` (`AtLeastEnterpriseAdmin`) — read-only preview
of the tenant's current draft, composed into the same document shape the published-config endpoint
serves (theme composed live), never mutates anything. Separate controller from
`MobileConfigController`/`MobileConfigDraftController` for a real ASP.NET Core authorization-
attribute-ordering reason: `MobileConfigController` carries a controller-level `[AllowAnonymous]`,
which unconditionally skips the authorize check for that controller's endpoints regardless of any
action-level `[Authorize]`.
**Divergence from the originally-requested contract** (`MOBILE_API_STAGE_12.md` anticipated a
`X-Mobile-Preview-Token`-header-based mechanism implying the mobile app itself renders the preview)
— see `MOBILE_API.md` §7B for the full flag.
Schema version: unaffected (reuses schema 1, same document shape as the published-config endpoint).
Backward compatible — new endpoint only.

## 2026-08-18 — TASK-545: Version History + Rollback

**New endpoints:** `GET /api/v1/mobile/config/versions` (full history, draft/published/archived, all
kept, never deleted) and `POST /api/v1/mobile/config/versions/{versionId}/rollback` (publishes a
**new** version cloned from a historical one, including restoring the historical theme snapshot onto
the tenant's live `MobileTheme` row so the next normal publish doesn't silently undo the rollback).
Publish now also archives the previously-published version on every publish (previously left
untouched/un-archived by design pending this task).
Schema version: unaffected. Backward compatible — new endpoints only; no existing endpoint's
response shape changed.

## 2026-08-18 — TASK-544: Generalized Draft → Validate → Publish

**New endpoint:** `POST /api/v1/mobile/config/publish` — validates the draft, composes the current
`MobileTheme` into the document, atomically marks it `Published`, and opens a fresh cloned `Draft`.
**Behavior change to an already-shipped endpoint:** `GET /api/v1/mobile/config` (TASK-534) now reads
`theme` from the **published snapshot** (`ConfigurationJson.theme`, composed at the moment of
publish) instead of live-joining the tenant's current `MobileTheme` row on every request. Direct
consequence: `PUT /api/v1/mobile/theme` (TASK-536) no longer takes effect for real consumers until
the tenant publishes again — closing the "immediate live effect" gap TASK-536 had explicitly flagged
as pending this task. Concurrency protection added (xmin tokens + unique-index fallback) — a
conflicting concurrent publish now returns `409` instead of a silent last-write-wins overwrite.
Schema version: unaffected (schema 1 throughout). **Not fully backward compatible for theme
consumers**: any client that assumed a theme `PUT` took effect immediately (true before this task)
must now account for the publish step.

## 2026-08-18 — TASK-543: Consumer-session-aware feature flags **(no contract change)**

Built `IConsumerFeatureFlagService`/`ConsumerFeatureFlagService` and a
`[RequireConsumerFeature("...")]` filter attribute, unit-tested, DI-registered — **deliberately not
attached to any controller/endpoint yet**. Wiring it onto `ConsumerContentController`/
`ConsumerLoyaltyController` before any tenant has ever published a config would 403 every real
consumer request in production today (every tenant's `PublishedVersionId` is still `null`); the
service defaults every flag to "enabled" specifically to make that future wiring safe once it
happens. No live endpoint calls this service as of this entry.

## 2026-08-18 — TASK-542: Navigation icon whitelist

**Schema/validator change, no new endpoint.** `MobileConfigWhitelists.NavigationIcons`
(`{home, tag, grid, qr, ticket, map, news, user}`) added and enforced by
`MobileConfigValidator.ValidateNavigation` — previously `navigation[].icon` accepted any non-empty
string. `contracts/mobile-config.schema.json`'s `navigation.items.properties.icon` changed from a
free string to a matching `enum`. The set matches `mobile/features/mobile-config/validation.ts`'s
AJV enum exactly — resolves the reconciliation `MOBILE_API_STAGE_12.md` flagged as needed.
Schema version: still 1 — this is a **tightening** of an already-versioned schema, not a version
bump (per `MobileConfigWhitelists.SupportedSchemaVersions`' documented policy: a schema bump is for
adding new accepted values, not for narrowing an existing free-string field into a whitelist).
**Compatibility note:** a draft/publish payload using a previously-accepted-but-now-disallowed icon
value would newly fail validation — no live tenant had used a non-whitelisted icon at the time of
this change (verified by grep across test fixtures), so no real document was invalidated in
practice, but this is a real tightening, not purely additive.

## 2026-08-18 (also 2026-08-17 for TASK-538) — TASK-538 / TASK-538b: Block Registry + Draft CRUD endpoints

**New endpoints:** `GET /api/v1/mobile/blocks` and `GET /api/v1/mobile/blocks/{type}` (TASK-538,
2026-08-17) — the server-owned catalog of 12 Core Blocks V1 types. `GET`/`PUT /api/v1/mobile/config/draft`
(TASK-538b, 2026-08-17) — HTTP wrapper around TASK-532's already-shipped draft service; `PUT`
returns `{ errors: [{ field, message }] }` on validation failure, matching `MobileThemeController`'s
existing shape. Block *props* validation against each block's own `validationSchema` was
investigated and deliberately **not** wired into `MobileConfigValidator` — would have broken two
already-shipped, already-tested free-form-props test cases with no real block-authoring UI yet to
confirm the right shape against (flagged as a deferred follow-up, see `MOBILE_API.md` §1).
Schema version: unaffected. Backward compatible — new endpoints only.

## 2026-08-17 — TASK-536: Theme domain validation + PUT endpoints

**New endpoints:** `GET`/`PUT /api/v1/mobile/theme`. `MobileThemeWhitelists` established as the
theme domain's whitelist source (hex-color pattern, button/card radius bounds 0–32/0–40, spacing
preset enum `{compact, comfortable}` — matched to the mobile client's already-shipped placeholder
guess rather than inventing a third value). `contracts/mobile-config.schema.json`'s `theme.spacing`
changed from a free non-empty string (TASK-533's flagged gap) to the matching enum.
**Explicitly documented, not fixed here:** a successful `PUT` took effect in
`GET /api/v1/mobile/config` **immediately** at this point in the timeline — no draft/publish
protection for theme existed yet (closed later by TASK-544, see above).
Schema version: still 1 (spacing field narrowed from free string to enum — same tightening pattern
as TASK-542's icon whitelist below). Backward compatible for existing consumers of
`GET /api/v1/mobile/config` (theme was already being served; this task only added the write side).

## 2026-08-17 — TASK-534: `GET /api/v1/mobile/config`

**New endpoint** — the first route under the `/api/v1/` prefix. Serves a tenant's currently published
mobile configuration document (`schemaVersion`, `configVersion`, `tenant`, `theme`, `features`,
`navigation`, `pages`), `[AllowAnonymous]`, `tenantId` via `?tenantId=` query parameter resolved
through `ITenantSessionOverride`, strong ETag for conditional `GET`/`304`. At this point in the
timeline, `theme` was composed **live** from the tenant's current `MobileTheme` row on every call
(changed later by TASK-544 to read from the published snapshot instead — see above).
Schema version: 1 (first version — `MobileConfigWhitelists.CurrentSchemaVersion` established at
TASK-532). Not a compatibility question yet — this is the endpoint's first release.

## 2026-08-17 — TASK-533: Canonical `/contracts/mobile-config.schema.json`

Authored the canonical JSON Schema (Draft 07, not 2020-12 — a deliberate choice: mobile's AJV
default-instance setup can only load Draft 07 without additional imports it doesn't have) for the
document `GET /api/v1/mobile/config` serves. `MobileConfigSchemaContractTests` added — asserts the
schema's whitelisted values stay set-equal to `MobileConfigWhitelists`'s constants (a lockstep test
pattern every subsequent whitelist change, e.g. TASK-536/542, extends). `theme.spacing` left as a
free string at this point (closed by TASK-536 above) — an honestly-flagged gap, not an oversight.
Schema version: 1 (this task is what makes "schema version 1" a checkable artifact, not just a
constant in code).

## 2026-08-17 — TASK-532: Config validation service + Draft CRUD (application layer) **(no contract change yet)**

`MobileConfigWhitelists`, `MobileConfigValidator`, `MobileConfigDraftService` built at the
Application layer — no controller/endpoint exists yet (added later by TASK-538b). Established the
foundational rule later endpoints depend on: a draft's `ConfigurationJson` never carries a `theme`
key; theme is composed in only at publish time.

## 2026-08-17 — TASK-531: `MobileConfiguration` / `MobileConfigurationVersion` / `MobileTheme` domain **(no contract change)**

New entities + migration + RLS (canonical tenant_isolation/provider_bypass/worker_bypass triad) —
pure schema/domain groundwork, no endpoint yet.

## 2026-08-17 — TASK-528: Centralized `ITenantContext` **(no contract change)**

Internal refactor — new `ITenantContext` service for staff-tenant resolution, migrated onto
`BannersController`/`LoyaltyController`/`LoyaltySettingsController` (pre-existing, unversioned
controllers). No request/response shape changed on any endpoint; this is the pattern every
`/api/v1/mobile/*` admin controller built afterward (draft/preview/publish/versions/theme) uses from
day one instead of a duplicated `ResolveTenantId()` helper.

## 2026-08-17 — TASK-527: `Tenant.LogoUrl` / `Tenant.UpdatedAt` **(no contract change yet)**

New nullable `LogoUrl`/`UpdatedAt` columns on `Tenant` + migration. No endpoint exposed either field
yet at this point — `logoUrl` is first surfaced publicly by `GET /api/v1/mobile/config`'s `tenant`
object (TASK-534, one day later) and `GET /api/v1/retailers/{slug}/public` (TASK-549).
