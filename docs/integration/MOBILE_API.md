# Mobile / Consumer-Platform API Reference

**Owner:** documentation-writer
**Last updated:** 2026-08-18
**Task:** TASK-552 (Stage 6 / Stage F, `.claude/tasks/mobile-roadmap.md`)
**Source of truth for the raw contract:** `backend/openapi.json` (committed, regenerated per
`.claude/docs/backend-structure.md` "OpenAPI Contract (TASK-552)"). This document adds the
auth/tenant-resolution/error-shape narrative the generated OpenAPI file doesn't capture — read both
together, not this file alone, before writing a client against any endpoint below.
**Companion contract file:** `contracts/mobile-config.schema.json` (canonical JSON Schema for the
document `GET /api/v1/mobile/config` serves — Draft 07, schema version 1).

## Scope of this document

Every endpoint under the versioned **`/api/v1/`** prefix — the consumer-platform surface built in
Stages B–F of the mobile roadmap's Stage 6 (TASK-531 through TASK-552). Per decision 2 (TASK-551,
TASK-556), only new consumer-platform endpoints from Stage B onward are versioned; the pre-existing,
unversioned `/api/...` surface (staff API, `ConsumerAuthController`, `MobileAuthController`,
`ConsumerContentController`, `ConsumerLoyaltyController`, `BannersController`, etc.) is **not**
retroactively aliased and is out of scope here except where a `/api/v1/` endpoint is a direct,
documented replacement/superset of one (noted inline where relevant — see §"Retailer discovery"
below).

16 endpoints across 8 controllers, all confirmed against `backend/openapi.json`'s `/api/v1/*` paths
and the controller source directly:

| Controller | Route prefix |
|---|---|
| `MobileConfigController` | `GET /api/v1/mobile/config` |
| `MobileConfigDraftController` | `/api/v1/mobile/config/draft` |
| `MobileConfigPreviewController` | `GET /api/v1/mobile/config/preview` |
| `MobileConfigPublishController` | `POST /api/v1/mobile/config/publish` |
| `MobileConfigVersionsController` | `/api/v1/mobile/config/versions` |
| `MobileThemeController` | `/api/v1/mobile/theme` |
| `MobileBlocksController` | `/api/v1/mobile/blocks` |
| `RetailersController` | `/api/v1/retailers` |

## Conventions

- **Base prefix:** `/api/v1/`. No API-wide version negotiation exists — a version bump would be a
  new prefix (`/api/v2/`), not a header/query parameter.
- **Auth shapes in use** (three, not interchangeable):
  1. **Anonymous** — no token at all. Only `GET /api/v1/mobile/config` and
     `GET /api/v1/retailers/{slug}/public`. Both exist specifically for MASTER SPEC §12's
     "discover before joining/installing" flow.
  2. **Consumer session** — `Authorize` + a `consumer_account_id` JWT claim (no `tenant_id` claim;
     a consumer's session is cross-tenant by design). Every action on `RetailersController` except
     the one `[AllowAnonymous]` action above.
  3. **Staff session, `AtLeastEnterpriseAdmin` policy** — roles `provider` or `enterprise_admin`
     only (`AppPolicies.AtLeastEnterpriseAdminRoles`,
     `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`). Every Mobile
     Configuration/Theme/Blocks admin-write endpoint.
- **Tenant-resolution mechanisms** (three, each endpoint below states which one it uses — do not
  assume they're interchangeable, see `.claude/docs/backend-structure.md`):
  1. **`ITenantContext`** — reads the staff `tenant_id` JWT claim. Used by every
     `AtLeastEnterpriseAdmin`-gated endpoint (draft/preview/publish/versions/theme) to resolve "my
     own tenant." Never present on a consumer/anonymous session.
  2. **`ITenantSessionOverride`** — lets an anonymous/consumer request temporarily assume a
     specific, explicitly-named tenant's RLS context for one operation. Used by
     `GET /api/v1/mobile/config` (tenant travels as `?tenantId=`, resolved through this mechanism
     exactly like `ConsumerContentController`'s `{tenantId}` route segment already does).
  3. **Consumer-identity resolution** — `RetailersController` resolves the acting consumer from the
     `consumer_account_id` claim; the *retailer* being addressed travels as a `{slug}` route segment
     resolved by `ITenantRepository.GetBySlugAsync` (case-insensitive), not by claim or session
     override — a consumer's own identity and the tenant they're addressing are two separate pieces
     of information here.
- **Error shapes** — two conventions in use across these endpoints, not one universal
  `ProblemDetails`:
  - Field-level validation failure: `{ "errors": [{ "field": "navigation[0].icon", "message": "..." } ] }`
    (draft save, theme update, publish/rollback validation failure).
  - Everything else (not-found, forbidden, conflict, generic bad-request): `{ "error": "..." }`.
  - `403 Forbidden` (no body) is used when tenant/consumer resolution itself fails (e.g.
    `ITenantContext.TenantId is null`), distinct from `403` with an `{ "error" }` body used for a
    genuine business rule (e.g. loyalty module not active).
- **Dates:** UTC throughout (`DateTime`/`DateTimeOffset` fields on every DTO below), consistent with
  the rest of the API — see TASK-551's audit.
- **Pagination:** none of these 16 endpoints paginate. `GET /api/v1/mobile/blocks` is a small,
  compile-time-fixed catalog (12 block types); `GET /api/v1/mobile/config/versions` is a
  tenant-scoped history list, small by nature (TASK-551 audit conclusion — both genuinely don't need
  it, not an oversight).
- **Schema version:** every document below (`GET /api/v1/mobile/config` and its draft/preview
  counterparts) carries `schemaVersion: 1` — the only value `MobileConfigWhitelists.CurrentSchemaVersion`
  currently defines. A future schema bump adds a second accepted value to
  `MobileConfigWhitelists.SupportedSchemaVersions`, not a breaking replacement.

---

## 1. Mobile Configuration domain — consumer read

### `GET /api/v1/mobile/config`

- **Purpose:** returns a tenant's current **published** mobile app configuration document — the
  consumer client's single source of truth for theme/features/navigation/pages. Mobile's #1
  blocking contract for a production cold-bootstrap (per `docs/mobile/MOBILE_CURRENT_STATE.md`).
- **Auth:** `[AllowAnonymous]`. Identical response for every viewer of a given tenant; no benefit to
  requiring a consumer session.
- **Tenant behavior:** `tenantId` travels as an explicit **query parameter** (`?tenantId=<guid>`),
  not a route segment — deliberately, so the route itself stays byte-for-byte spec-compliant
  (`GET /api/v1/mobile/config`, no tenant path segment) while still resolving safely via
  `ITenantSessionOverride`. `tenantId` missing/empty → `400`.
- **Request:** `GET /api/v1/mobile/config?tenantId={guid}`. Supports conditional requests via
  `If-None-Match` against the response's own `ETag` (strong ETag = SHA-256 hex of the exact served
  JSON bytes, including the composed theme).
- **Response `200`:**
  ```json
  {
    "schemaVersion": 1,
    "configVersion": 3,
    "tenant": { "id": "guid", "slug": "svizhyi-kut", "name": "Свіжий Кут", "logoUrl": "https://.../logo.png" },
    "theme": { "primaryColor": "#FF5733", "...": "..." },
    "features": { "loyalty": true, "promotions": true, "...": "..." },
    "navigation": [ { "type": "home", "label": "Головна", "icon": "home" }, "..." ],
    "pages": { "home": { "blocks": ["..."] }, "...": "..." }
  }
  ```
  `theme` is read from the published version's own `ConfigurationJson.theme` — the exact snapshot
  composed by `POST /api/v1/mobile/config/publish` at publish time (TASK-544), **not** a live join
  against the tenant's current `MobileTheme` row. A `PUT /api/v1/mobile/theme` call therefore has
  **no effect on this endpoint until the tenant publishes again.**
- **Response `304`:** when `If-None-Match` already matches the current ETag — empty body.
- **Errors:** `400` missing/empty `tenantId`. `404 { "error": "Tenant not found." }` (unknown
  tenant id) or `404 { "error": "This tenant has no published mobile configuration yet." }`
  (tenant exists, never published, or draft-only) — the two cases are deliberately distinguished,
  matching `ConsumerContentController`'s existing disclosure level for the same public tenant space.
- **Known gap (not a bug, documented, not yet closed):** a block's `props` object is currently
  free-form JSON — `MobileConfigValidator` does not check a block's props against
  `MobileBlocksController`'s own registered `validationSchema`. See TASK-538's log for the full
  reasoning (retrofitting strict prop validation onto an already-shipped, free-form field with no
  real producer yet risked breaking existing coverage); flagged there as deliberate deferred
  follow-up, tracked in the mobile roadmap's Stage 6 header as one of two non-blocking backend
  hardening gaps.

---

## 2. Mobile Configuration domain — retailer-admin write surface

Five controllers, all `AtLeastEnterpriseAdmin`, all `ITenantContext`-resolved (never
`ITenantSessionOverride` — these are staff acting on their *own* tenant, not a cross-tenant
consumer read), sharing the same `403` (no body) when `ITenantContext.TenantId` is `null`.

### `GET /api/v1/mobile/config/draft`

- **Purpose:** returns the acting tenant's current draft `MobileConfigurationVersion` — the App
  Builder canvas's load call.
- **Auth / tenant behavior:** as above.
- **Request:** no body.
- **Response `200`:** always `200`, never `404` — a tenant with no draft yet gets
  `{ "hasDraft": false, "mobileConfigurationId": null, "versionId": null, "version": 0, "schemaVersion": 1, "status": "draft", "configurationJson": null, "createdBy": null, "createdAt": null }`
  (first-ever App Builder visit renders an empty canvas, not an error). A saved draft returns
  `hasDraft: true` plus the real `configurationJson` string (the theme-less document — see
  `GET /api/v1/mobile/config`'s remarks on theme composition timing).
- **Errors:** `403` (no body) when unresolved tenant.

### `PUT /api/v1/mobile/config/draft`

- **Purpose:** validates and saves the tenant's draft document.
- **Auth / tenant behavior:** as above. `actingUserId` resolved server-side from the JWT
  (`ClaimTypes.NameIdentifier`/`sub`), never trusted from the body.
- **Request:** `{ "configurationJson": "<stringified JSON document>" }`. The document is validated
  against `MobileConfigWhitelists`/`MobileConfigValidator` (top-level keys `schemaVersion`,
  `features`, `navigation`, `pages` only — **no `theme` key at draft time**, rejected as unknown if
  present; `navigation` 2–5 items, each `type`/`icon` whitelist-checked; `pages.*.blocks[].type`
  whitelist-checked against the 12 Core Blocks V1 types).
- **Response `200`:** the saved draft, same shape as the GET response with `hasDraft: true`.
- **Errors:** `400 { "errors": [{ "field": "navigation[2].icon", "message": "Unknown navigation icon 'flame'." }, ...] }` on
  any validation failure — nothing persisted. `403` (no body) unresolved tenant.
- **Side effect (TASK-550):** logs `ActivityLog` entries — `mobileconfig.draft_saved` on every save,
  plus a separate `mobileconfig.feature_flags_changed` entry (only when the `features` object
  actually differs from the previous draft, `Meta` = just the changed keys).

### `GET /api/v1/mobile/config/preview`

- **Purpose:** read-only "what would publishing right now produce" — the same document shape
  `GET /api/v1/mobile/config` returns for a published config, composed live from the current draft
  + current `MobileTheme` row, without publishing anything.
- **Auth / tenant behavior:** as above. **Deliberately its own controller**, not an action on
  `MobileConfigController` — that controller carries a controller-level `[AllowAnonymous]`, and
  ASP.NET Core's authorization middleware skips the authorize check for an endpoint whenever *any*
  `AllowAnonymousAttribute` is present in that endpoint's metadata (an action-level `[Authorize]`
  would not reliably override it). See §7B — decided 2026-08-18: this is, and stays, a
  **staff/web-admin-only** preview; the mobile app never renders draft/preview content.
- **Request:** no body.
- **Response `200`:** `{ "hasDraft": bool, "schemaVersion": 1, "configVersion": ..., "tenant": {...}, "theme": {...}, "features": {...}, "navigation": [...], "pages": {...} }`.
  No draft yet → `hasDraft: false` + empty/default body, still `200`, never a bare `404`.
- **Errors:** `403` (no body) unresolved tenant. Never mutates anything — no draft save, no publish,
  no version-status change.

### `POST /api/v1/mobile/config/publish`

- **Purpose:** publishes the tenant's current draft — validates, composes the current `MobileTheme`
  into the document, atomically archives the previous published version (if any), marks the draft
  `Published`, and opens a fresh `Draft` row cloned from the just-published content (theme-less).
- **Auth / tenant behavior:** as above.
- **Request:** no body — operates on whatever the tenant's current draft already is.
- **Response `200`:** `{ "mobileConfigurationId", "versionId", "version", "schemaVersion", "configurationJson", "createdBy", "createdAt", "publishedAt" }`
  — the newly published version, `configurationJson` now including the composed `theme` key.
- **Errors:** `400 { "error": "..." }` — no draft to publish. `400 { "errors": [...] }` — draft
  failed validation (same field-level shape as the draft-save endpoint), nothing persisted.
  `409 { "error": "..." }` — a concurrent publish for the same tenant committed first (xmin/unique-
  index conflict); safe to retry, nothing corrupted or partially applied. `403` (no body) unresolved
  tenant.
- **Side effect:** logs `mobileconfig.published`, `Meta = {"version": N}`.

### `GET /api/v1/mobile/config/versions`

- **Purpose:** full version history for the tenant — draft, published, and archived rows, newest
  `Version` first. Never deletes a row; this is the rollback picker's data source.
- **Auth / tenant behavior:** as above.
- **Request:** no body.
- **Response `200`:** array of
  `{ "id", "version", "status", "createdAt", "publishedAt", "createdBy" }`. Deliberately excludes
  `configurationJson` — a full document is fetched on demand, not embedded in the list.
- **Errors:** `403` (no body) unresolved tenant.

### `POST /api/v1/mobile/config/versions/{versionId}/rollback`

- **Purpose:** publishes a **new** version cloned from a historical version's content (never
  "reactivates" the old row in place) — includes restoring the historical `theme` snapshot onto the
  tenant's live `MobileTheme` row, so the very next normal publish doesn't silently overwrite the
  rollback with the pre-rollback theme.
- **Auth / tenant behavior:** as above.
- **Request:** `versionId` (route, GUID) — the historical version to roll back to.
- **Response `200`:** same `MobileConfigPublishedResponse` shape as `POST .../publish`.
- **Errors:** `404 { "error": "..." }` — unknown version id, or belongs to a different tenant.
  `400 { "error": "..." }` — target is already the tenant's current published or draft version
  (nothing to roll back to). `400 { "errors": [...] }` — defensive re-validation failure of the
  theme-less body (expected to never actually trigger — see TASK-545's log). `409` — concurrent
  publish conflict, same as above. `403` (no body) unresolved tenant.
- **Side effect:** logs `mobileconfig.rolled_back`, `Meta` includes `rolledBackToVersion`,
  `rolledBackToVersionId`, and `newVersion`.

### `GET /api/v1/mobile/theme`

- **Purpose:** the tenant's current `MobileTheme` row — the Theme Editor's load call.
- **Auth / tenant behavior:** as above.
- **Request:** no body.
- **Response `200`:** `{ "logoUrl", "primaryColor", "secondaryColor", "backgroundColor", "surfaceColor", "textPrimaryColor", "textSecondaryColor", "buttonRadius", "cardRadius", "spacingPreset", "updatedAt" }`.
  `updatedAt: null` means the tenant has never saved a theme — the response reflects
  `MobileTheme.CreateDefault`'s built-in defaults, not a fabricated "save."
- **Errors:** `403` (no body) unresolved tenant.

### `PUT /api/v1/mobile/theme`

- **Purpose:** create-or-update the tenant's one `MobileTheme` row (full replace, every field
  required on every call — no partial update).
- **Auth / tenant behavior:** as above. `actingUserId` resolved from the JWT, threaded into audit
  logging (TASK-550).
- **Request:** same shape as the GET response minus `updatedAt`. Every field whitelist-validated:
  6 hex-color fields (`^#[0-9A-Fa-f]{6}$`), `buttonRadius` 0–32, `cardRadius` 0–40, `spacingPreset`
  ∈ `{compact, comfortable}`, optional `logoUrl` (absolute `http`/`https` only, ≤2048 chars).
- **Response `200`:** the saved theme.
- **Errors:** `400 { "errors": [{ "field": "primaryColor", "message": "..." }, ...] }`. `403` (no
  body) unresolved tenant.
- **Live-effect note (important, not obvious from the endpoint alone):** as of TASK-544, a
  successful `PUT` here only edits the tenant's *pending* theme — it has **no effect** on real
  consumers (`GET /api/v1/mobile/config`) until the tenant's next `POST .../publish`. Prior to
  TASK-544 this endpoint took effect immediately in production; that gap is now closed, but any
  frontend copy predating TASK-544 that still says "changes take effect immediately" is stale (see
  TASK-544's log — flagged as a known, unfixed frontend follow-up, not addressed in this
  documentation pass).
- **Side effect:** logs `mobileconfig.theme_updated`.

### `GET /api/v1/mobile/blocks`

- **Purpose:** the server-owned Block Registry — every block type's `displayName`/`icon`/`category`/
  `defaultProps`/`validationSchema`/`supportedDataSource`, backing the App Builder's block palette
  and Property Editor.
- **Auth:** `AtLeastEnterpriseAdmin`. **Not tenant-scoped** — the catalog is identical, compile-time-
  fixed data for every tenant (no `ITenantContext` dependency at all).
- **Request:** no body.
- **Response `200`:** array of 12 `BlockDefinitionDto` — `heroBanner`, `bannerCarousel`,
  `loyaltyCard`, `loyaltyBalance`, `promotionCarousel`, `promotionGrid`, `productCarousel`,
  `productGrid`, `sectionHeader`, `quickActions`, `newsList`, `storeList`. `newsList`/`storeList`
  honestly document a real backend gap in `supportedDataSource` (no `News` entity, no
  list-of-stores endpoint exist yet) rather than pointing at a fabricated data source.
- **Errors:** none beyond standard `401`/`403` (no body).

### `GET /api/v1/mobile/blocks/{type}`

- **Purpose:** single block type's definition.
- **Auth:** as above.
- **Request:** `type` (route, string).
- **Response `200`:** one `BlockDefinitionDto`.
- **Errors:** `404 { "error": "Unknown block type '{type}'." }`.

---

## 3. Retailer discovery domain — consumer-facing

`RetailersController`, `/api/v1/retailers`. Generalizes `ConsumerLoyaltyController`'s
tenant-id-addressed network catalogue/join endpoints into a slug-addressed `/api/v1/` surface
(TASK-548). **The pre-existing `GET /api/consumer/loyalty/networks` and
`POST /api/consumer/loyalty/{tenantId}/join` are kept exactly as-is, as a permanent (not
time-boxed) alias** — not deprecated, both still live, both still reachable, both still calling
the exact same `ILoyaltyService` methods this controller calls. See §"Related non-versioned
endpoints" below.

Every action except the last requires a **consumer session** (`consumer_account_id` JWT claim, no
`tenant_id` claim). Deliberately **not** gated by `[RequireModule("loyalty")]` — that filter reads a
`tenant_id` claim a consumer session never carries; module activation is enforced inside
`ILoyaltyService` itself instead (a tenant without the `loyalty` module enabled is unjoinable and
absent from discovery — decision 1's accepted consequence, see `docs/architecture/TARGET_ARCHITECTURE.md`
§3 open decision #1).

### `GET /api/v1/retailers`

- **Purpose:** lists retailers available to the calling consumer.
- **Auth:** consumer session. `403` (no body) if the `consumer_account_id` claim is missing/invalid.
- **Tenant behavior:** none — lists across all tenants the consumer is eligible to see (same
  eligibility rule as `GetAvailableNetworksAsync`: tenant active, `loyalty` module enabled,
  `LoyaltyProgramSettings.IsEnabled` not explicitly `false`).
- **Response `200`:** array of `{ "tenantId", "tenantName", "slug", "stores": [{ "storeId", "storeName", "address" }] }`.
- **Errors:** `403` (no body) unauthenticated/malformed consumer claim.

### `GET /api/v1/retailers/{slug}`

- **Purpose:** single retailer lookup by slug (case-insensitive).
- **Auth:** consumer session.
- **Request:** `slug` (route, string).
- **Response `200`:** same shape as one entry of the list endpoint above.
- **Errors:** `404 { "error": "..." }` for an unknown slug, inactive tenant, missing `loyalty`
  module, or a paused program — **all four cases are indistinguishable 404s by deliberate design**
  (enumeration-safety; same rule the list endpoint enforces by simply omitting the tenant). `403`
  (no body) unresolved consumer.

### `POST /api/v1/retailers/{slug}/join`

- **Purpose:** joins (or idempotently returns the existing/reactivated membership for) the
  retailer's loyalty program. Slug-addressed counterpart of the pre-existing
  `POST /api/consumer/loyalty/{tenantId}/join` — same underlying `JoinAsync` logic once the slug
  resolves to a tenant id.
- **Auth:** consumer session.
- **Request:** `slug` (route). No body.
- **Response `200`:** `LoyaltyMembershipSummaryDto` — `{ "membershipId", "tenantId", "tenantName", "balance", "status", "joinedAt", "preferredStoreId", "preferredStoreName", "preferredStoreAddress" }`.
  Rejoining after a prior `Leave` **reactivates** the same membership (`status` back to `active`) —
  `balance`/`joinedAt`/ledger history are preserved across a leave→rejoin cycle, never reset.
- **Errors:** `404 { "error": "..." }` unknown slug. `403 { "error": "..." }` tenant doesn't have the
  `loyalty` module active (a genuine business-rule `403`, distinct from the no-body `403` used for
  an unresolved consumer identity). `403` (no body) unresolved consumer identity itself.

### `DELETE /api/v1/retailers/{slug}/membership`

- **Purpose:** leaves the retailer's loyalty program — **new capability added by TASK-548** (no
  prior "leave" endpoint existed anywhere in the API before this).
- **Auth:** consumer session.
- **Request:** `slug` (route). No body.
- **Tenant behavior / semantics:** soft-deactivates the membership (`Status = "left"`) — never
  hard-deletes. `Balance`/`JoinedAt`/ledger history/`TotpSecret` are preserved untouched, same
  never-hard-delete-financial-data precedent as the rest of the loyalty domain. **Not gated on the
  tenant's current `loyalty` module state** — a consumer can always leave a network they once
  joined, even if the retailer later disabled the program.
- **Response `204`:** No content. **Idempotent** — leaving an already-left membership also returns
  `204` (no redundant write).
- **Errors:** `404 { "error": "..." }` — unknown slug, or the consumer has no membership at this
  retailer to leave. `403` (no body) unresolved consumer.
- **⚠ Contract discrepancy from the mobile request — see reconciliation §"MOBILE_API_STAGE_2.md"
  below.** `MOBILE_API_STAGE_2.md` requested this route addressed by `{tenantId}`; the endpoint that
  actually shipped is addressed by `{slug}`, matching the rest of this controller's slug-based
  design (TASK-548). Any mobile client built against the originally-requested `{tenantId}` shape
  needs to switch to `{slug}`.

### `GET /api/v1/retailers/{slug}/public`

- **Purpose:** minimal, anonymous-safe retailer preview for the QR/deep-link onboarding web fallback
  page (`https://<domain>/join/{slug}`, see `docs/integration/deep-link-onboarding.md`) — reached by
  anyone who scans a retailer's QR code or opens a shared join link before installing the app or
  having a consumer session at all.
- **Auth:** `[AllowAnonymous]` — the one action-level override on this controller. **Deliberately a
  distinct route/DTO/service method from `GET /api/v1/retailers/{slug}` above, not a relaxation of
  it** — that endpoint's DTO carries a full shoppable-store list (name+address per location) and the
  internal tenant GUID, neither of which is safe to hand to an unauthenticated caller.
- **Tenant behavior:** anonymous — no consumer identity involved at all; resolves purely from the
  `slug` route segment.
- **Request:** `slug` (route).
- **Response `200`:** `{ "name": "Свіжий Кут", "slug": "svizhyi-kut", "logoUrl": "https://.../logo.png", "joinable": true }`.
  `logoUrl` is `null` when the tenant has never uploaded one. `joinable` is always `true` whenever
  this DTO is returned at all — see the 404 policy below; the field ships anyway so the response is
  self-describing rather than requiring the caller to infer meaning purely from the HTTP status.
- **Errors:** `404 { "error": "Retailer not found." }` for an unknown slug, inactive tenant, missing
  `loyalty` module, or a paused program — same indistinguishable-404 policy as
  `GET /api/v1/retailers/{slug}`, reused deliberately so this new, less-trusted anonymous surface
  cannot enumerate tenant state any more precisely than the existing authenticated endpoint already
  can. **The web fallback page shows one generic "this link isn't valid" state for every 404 and
  every network/server error — it never attempts to infer or display why**, and any future native
  mobile handler is expected to follow the same rule (see `docs/integration/deep-link-onboarding.md`).
- **Decided 2026-08-18 (§7A):** this slug-based design is kept as-is — no signed/opaque
  invite-token contract will be built. See §7A for the full rationale.

---

## 4. Related non-versioned endpoints (context only, not fully documented here)

Out of this document's `/api/v1/` scope by definition, but directly relevant because
`RetailersController` above is an additive generalization of them, not a replacement:

- `GET /api/consumer/loyalty/networks` / `POST /api/consumer/loyalty/{tenantId}/join`
  (`ConsumerLoyaltyController`) — the original tenant-id-addressed contracts. **Permanent alias, not
  deprecated.** Both call the exact same `ILoyaltyService.GetAvailableNetworksAsync`/`JoinAsync`
  methods `GET /api/v1/retailers` / `POST /api/v1/retailers/{slug}/join` call — genuinely identical
  behavior, not just parallel routes. `GetAvailableNetworksAsync`'s response gained an additive
  `slug` field (used by the v1 surface) with no change to any existing field.
- `GET /api/consumer/{tenantId}/banners|promotions|catalog` (`ConsumerContentController`,
  `[AllowAnonymous]`) — pre-existing, unversioned, route-segment tenant addressing via
  `ITenantSessionOverride`. `GET /api/v1/mobile/config`'s tenant-transport design explicitly follows
  this endpoint's precedent (see §1 above), but this endpoint itself was not touched by Stage 6 and
  is not versioned.

## 5. Not yet wired to any endpoint (built, dormant)

`IConsumerFeatureFlagService`/`ConsumerFeatureFlagService` (TASK-543) and the companion
`[RequireConsumerFeature("...")]` filter attribute exist, are unit-tested, and are registered in DI —
but are **not attached to any controller action yet**, deliberately. Wiring them onto
`ConsumerContentController`/`ConsumerLoyaltyController` before any tenant has ever published a
`MobileConfigurationVersion` would 403 every real consumer request in production (every tenant's
`PublishedVersionId` is still `null` today, and the service is designed to default every flag to
"enabled" specifically to avoid that outcome once it is wired in). No endpoint in this document
currently calls it. Whoever wires it in next should read TASK-543's task log's "production-safety
issue" section first.

---

## 6. Reconciliation against the mobile workstream's `MOBILE_API_STAGE_*.md` requests

The mobile client workstream (a separately-owned effort, not part of Stage 6) left six short
request/notes files in this directory while building against these APIs, each written before the
corresponding Stage 6 work landed. Status of every one, checked directly against the code as of
2026-08-18 — none silently dropped:

| File | Request | Status |
|---|---|---|
| `MOBILE_API_STAGE_2.md` | `DELETE /api/v1/retailers/{tenantId}/membership` | **Resolved, with a discrepancy** — see below |
| `MOBILE_API_STAGE_9.md` | Loyalty tier field on membership summary | **Open, out of scope** |
| `MOBILE_API_STAGE_10.md` | Category/product/promotion detail endpoints | **Open, out of scope** |
| `MOBILE_API_STAGE_11.md` | Signed, opaque, expiring invite-token resolve contract | **Decided 2026-08-18 — shipped slug-based design kept, token contract superseded** (§7A) |
| `MOBILE_API_STAGE_12.md` (icon whitelist) | Reconcile mobile's icon enum with the canonical contract | **Resolved** |
| `MOBILE_API_STAGE_12.md` (preview token) | Staff preview via `X-Mobile-Preview-Token` header | **Decided 2026-08-18 — web-admin-only preview, mobile never renders drafts** (§7B) |
| `MOBILE_API_STAGE_14.md` | Consumer analytics ingestion endpoint | **Open, out of scope** |

### `MOBILE_API_STAGE_2.md` — resolved, with a discrepancy (flag, don't gloss over)

Requested `DELETE /api/v1/retailers/{tenantId}/membership`. TASK-548 shipped
`DELETE /api/v1/retailers/{slug}/membership` — see §3 above for the full contract. **The path
parameter is `{slug}`, not `{tenantId}`**, matching every sibling action on `RetailersController`
(all slug-addressed) rather than the originally-requested tenant-id addressing. Everything else
requested — resolve identity only from the JWT, verify membership ownership, idempotent
already-absent success, retained ledger/history, `204` on success, standardized error responses,
tenant-isolation tests — was delivered as specified. **This needs an explicit call, not an assumed
resolution:** either the mobile client adapts to `{slug}` (it already has the slug from
`GET /api/v1/retailers`'s list response), or a follow-up task adds a `{tenantId}`-addressed alias.
Not decided in this documentation pass.

### `MOBILE_API_STAGE_9.md`, `MOBILE_API_STAGE_10.md`, `MOBILE_API_STAGE_14.md` — open, out of scope

None of these three were touched by anything built in Stage 6 (TASK-527–555). They request work on
different, unrelated features this initiative never touched:

- **Loyalty tier** (`STAGE_9`) — belongs to a future retailer-defined-tiers feature; no `tier`
  concept exists anywhere in the loyalty domain today (`LoyaltyMembershipSummaryDto` has no such
  field, verified directly).
- **Catalog/promotion detail endpoints** (`STAGE_10`) — belongs to the catalog/marketplace feature;
  the existing `GET /api/consumer/{tenantId}/catalog` (paginated list, no single-item detail route)
  is unchanged.
- **Analytics ingestion** (`STAGE_14`) — belongs to a future analytics feature; no ingestion endpoint
  of any kind exists in this codebase.

These remain **real, valid, unimplemented requests** — not considered and rejected, just genuinely
never in scope for this initiative. A future task picking one up should start from the linked file
directly, not from this reconciliation note.

### `MOBILE_API_STAGE_12.md`, icon whitelist — resolved

`MobileConfigWhitelists.NavigationIcons` (TASK-542,
`backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigWhitelists.cs`) —
`{home, tag, grid, qr, ticket, map, news, user}` — verified directly against
`mobile/features/mobile-config/validation.ts`'s AJV `icon: { enum: [...] }` for navigation items:
**the two sets are identical**, confirmed by reading both files, not by trusting the task log's
claim alone. `MobileConfigValidator.ValidateNavigation` now enforces this whitelist server-side
(previously "any non-empty string"), and `contracts/mobile-config.schema.json`'s
`navigation.items.properties.icon` carries the matching `enum`. No further action needed on this
item.

**Adjacent, separately-tracked gap (not part of the STAGE_12 request, noted for completeness):**
mobile's own AJV schema additionally bounds `navigation[].label` to `maxLength: 30`; the backend
validator currently only type-checks `label` as a non-empty string, with no length bound. This is
one of the two "non-blocking backend hardening gaps" the mobile roadmap's Stage 6 header records as
deliberately deferred to backlog (alongside the block-props validation gap noted in §1 above) — not
a STAGE file request, and not resolved by this documentation pass.

---

## 7. Divergences — decided 2026-08-18 (product owner)

Two genuine architecture/security divergences between what the mobile workstream's notes
anticipated and what Stage 6 actually shipped. Both were flagged during TASK-552's documentation
pass without being resolved by the writing agent, then decided explicitly by the product owner the
same day. Recorded here as the authoritative resolution — `MOBILE_API_STAGE_11.md` and
`MOBILE_API_STAGE_12.md` themselves are left unedited (they're the mobile workstream's own working
notes), so this section is the canonical place a future reader should check for the final answer.

### A. QR/deep-link invite security model (`MOBILE_API_STAGE_11.md` vs. `GET /api/v1/retailers/{slug}/public`, TASK-549)

**Requested** (`MOBILE_API_STAGE_11.md`): `POST /api/consumer/retailer-invites/resolve` taking an
**opaque, signed, expiring invite token**, explicitly warning "QR content must never contain
theme/config JSON or be treated as an arbitrary URL."

**Shipped** (TASK-549): `GET /api/v1/retailers/{slug}/public` — a **plain, unsigned,
human-readable tenant slug** used directly as a public URL path segment
(`https://<domain>/join/{slug}`). No signing, no expiry. The slug is guessable/enumerable by design
— it is also the tenant's public discovery identifier used elsewhere (`GET /api/v1/retailers`'s list
response, `GET /api/v1/retailers/{slug}`). The 404 policy deliberately makes "slug never existed"
indistinguishable from "slug existed, later deactivated" — a real mitigation, but not the same
security model as an unguessable, time-boxed token.

**Decision: keep the shipped design as-is.** No signed/opaque invite-token contract will be built.
Rationale: the slug is already the tenant's public discovery identifier everywhere else in this
API (`GET /api/v1/retailers`, `GET /api/v1/retailers/{slug}`) — a signed token for the QR path
specifically would not meaningfully reduce exposure, since the same tenant is trivially
discoverable through the already-public list/lookup endpoints regardless. The actual join
still requires a full authenticated `POST /api/v1/retailers/{slug}/join` — the public page/QR only
ever previews non-sensitive display info. `MOBILE_API_STAGE_11.md`'s request is superseded by this
decision, not implemented.

### B. Staff preview mechanism (`MOBILE_API_STAGE_12.md` vs. `GET /api/v1/mobile/config/preview`, TASK-547)

**Requested** (`MOBILE_API_STAGE_12.md`): a short-lived, scoped, single-purpose preview token sent
via a dedicated `X-Mobile-Preview-Token` header — implying the **mobile app itself** renders a live
preview of a draft configuration using that scoped token, separate from a full staff session.

**Shipped** (TASK-547): `GET /api/v1/mobile/config/preview`, gated by the same
`AtLeastEnterpriseAdmin`-policy staff JWT every other admin endpoint in this document uses — no
preview-token mechanism, no header. This implicitly assumes a **web admin UI** polls this endpoint to
show a preview, not that the mobile app itself renders one from a scoped token.

**Decision: preview stays web-admin-only.** The mobile consumer app will never render draft/
preview content — only `GET /api/v1/mobile/config` (published-only) reaches the mobile client, for
every tenant, always. `TASK-546`'s Version History screen (Retailer Admin, web) is the sole
preview/publish surface. No `X-Mobile-Preview-Token` mechanism will be built.
`MOBILE_API_STAGE_12.md`'s preview-token request is superseded by this decision, not implemented.
