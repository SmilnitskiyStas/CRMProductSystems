# Architecture Decisions (ADR Log)

**Owner:** project-architect
**Updated:** 2026-07-30

## ADR-024: Forgot/reset-password flow — outbox reuse, third fail-open RLS exception, env-var frontend URL, 400 not 401
Date: 2026-07-30
Status: accepted

Context: ShelfGuard had no way for a user locked out of `/login` to recover a forgotten
password — a repo-wide grep (`backend`/`frontend`/`mobile`/`worker`/`.claude`) for
forgot/reset-password confirmed zero existing code, and `v1-spec.md` never specified the
flow either. Two possible delivery channels exist for reaching that user: email
(`worker/src/services/email.ts` — complete, working code, but blocked today: `RESEND_API_KEY`'s
domain `agrusystems.pp.ua` has not passed Resend's DNS verification, TASK-260, blocked since
2026-06-19) and Telegram (works today, but only for a user who already linked their account —
an authenticated, opt-in flow that cannot be the *only* channel for someone locked out right
now). User confirmed via `AskUserQuestion`: email as the primary channel, Telegram as fallback
for already-linked accounts; build complete, correct code now so it activates the instant
TASK-260 unblocks — the same posture already accepted for `weekly-report`. TASK-455 (schema),
TASK-456 (backend + worker), TASK-457 (frontend) implemented this; this ADR records the
cross-cutting decisions behind it, verified against the shipped code.

Decision:

1. **Delivery reuses the existing Postgres outbox (ADR-018), not a new C# BullMQ producer.**
   ADR-018 already settled this question for backend-originated notifications in general: no
   new cross-language job-producer infrastructure — the triggering C# service inserts a row
   into `notification_queue` and `notification-dispatch.job.ts` (Node, 1-min poll) picks it up.
   `AuthService.ForgotPasswordAsync` follows this exactly: `INotificationRepository.EnqueueAsync`
   with `UserId` set (a **targeted**, not broadcast, intent row — the same shape ADR-019
   introduced for temporary-access-grant expiry notifications), `EventType =
   "auth.password_reset_requested"`, `Payload = {resetUrl, expiresInMinutes}`. The worker's
   `dispatchTargeted()` (added by ADR-019) already handles single-recipient delivery via
   `TARGETED_EVENT_CHANNELS`; this task only adds one map entry (`["email", "telegram"]` —
   deliberately no `"push"`, not implemented anywhere in this codebase yet) plus the
   `formatEmail`/`formatText` branches that turn the payload into an actual clickable-link
   message. Zero new delivery infrastructure of any kind.
2. **`password_reset_tokens` is the third documented fail-open RLS exception — not a fourth,
   and not a new kind of exception.** `database-schema.md`'s exceptions table already correctly
   lists exactly `users` / `refresh_tokens` / `password_reset_tokens` (`notification_settings`
   was removed from that list on 2026-07-15, TASK-360 — it never had a real pre-auth access
   path to begin with, so its old fail-open branch was a plain leak, not a necessary exception).
   The reasoning for the new table is identical to `refresh_tokens`'s: an anonymous
   forgot/reset-password request must find its token/user row through an `EXISTS`-through-`users`
   join before `TenantConnectionInterceptor` has any `app.tenant_id` to `SET` — there is no
   tighter alternative, since the interceptor only ever `RESET`s session vars for unauthenticated
   connections rather than setting them to something narrower.
3. **Email primary / Telegram fallback is a product decision (`AskUserQuestion`), not an
   engineering default — and it carries an explicit, tracked dependency.** The email channel will
   not actually reach a real user until TASK-260 (Resend DNS verification for
   `agrusystems.pp.ua`) unblocks — the same standing dependency already accepted for
   `weekly-report`. Telegram works today for any user who has already linked their account via
   the existing `/start <code>` flow and does not depend on TASK-260 at all.
   `.claude/tasks/blocked.md`'s TASK-260 entry now cross-references this flow rather than a new
   `known-issues.md` entry being created for it — it is a new dependent of an already-tracked
   blocker, not a new problem.
4. **`Frontend__BaseUrl` is read via `Environment.GetEnvironmentVariable`, not
   `IConfiguration`.** `ShelfGuard.Application.csproj` carries no `Microsoft.Extensions.
   Configuration` package reference at all (confirmed directly) — `AuthService` lives in the
   Application layer and physically cannot resolve `IConfiguration["Frontend:BaseUrl"]`.
   `TelegramLinkService.cs` already established the exact precedent for this same constraint
   (`Environment.GetEnvironmentVariable("Telegram__BotUsername") ?? "shelfguard_bot"`, with a
   comment stating "Application layer has no IConfiguration dependency — env var with a sane
   default"); `AuthService`'s constructor copies this pattern verbatim for `Frontend__BaseUrl`
   (default `http://localhost:3000`). Env plumbing (`.env.staging.example`,
   `.env.production.example`, both `docker-compose.*.yml`) follows the existing per-environment
   convention — no new mechanism, and no new appsettings.json entry.
5. **`POST /api/auth/reset-password` returns `400`, not `401`, on failure — unlike
   `2fa/verify`.** `2fa/verify` is mid-authentication (the password already checked out; the
   code is the second factor of that *same* login attempt), so a rejected code is genuinely an
   authorization failure — `401` fits. `reset-password` authenticates nothing and issues no
   tokens; it is a state-changing action gated by possession of a single-use, out-of-band
   secret — the same category as `change-password`/`public-leads`, both already `400`. Using
   `401` here would incorrectly imply the caller was attempting to authenticate as someone,
   which is not what this endpoint does.

Consequences:
+ Zero new delivery infrastructure — the outbox/`dispatchTargeted()` path from ADR-018/019
  absorbs a fourth event type with one map entry and two formatting branches
+ The fail-open RLS list stays a closed, understood, three-row exception set rather than
  growing unboundedly — a future table needing a similar "look this up before we know the
  tenant" flow is still expected to get its own narrower policy per `database-schema.md`'s
  existing warning, not join this list by default
+ Email ships fully built and correct, ready to work the moment TASK-260 unblocks — no
  half-finished code to revisit later — but also no way to demonstrate real end-to-end email
  delivery until that DNS dependency clears; Telegram is the only channel demonstrably live
  today, and only for already-linked accounts
+ One more confirmed precedent (`Frontend__BaseUrl`) for "Application layer has no
  `IConfiguration`, use an env var with a default" alongside `Telegram__BotUsername` — no
  architectural surprise for the next similar case
- The generic reset-link error text ("Invalid or expired reset link.") deliberately conflates
  three distinct backend states (token not found, token expired/used, owner account gone or
  inactive) into one message — correct for not leaking account state to the caller, but means
  support/debugging must rely on server-side `ActivityLog`/logs, never the client-visible error,
  to tell these apart

Extends: ADR-018 (Postgres outbox mechanism) and ADR-019 (`dispatchTargeted()` single-recipient
delivery, introduced for temporary-access-grant expiry notifications) — reuses both verbatim for
a fourth targeted event type; introduces no new notification-delivery primitive.

## ADR-023: Loyalty program & RFM marketing analytics — cross-tenant ConsumerAccount identity, TOTP-based live QR, independent module keys, RfmSegment naming
Date: 2026-07-26
Status: accepted

Context: `docs/uployal/RFM_ANALYSIS.md` is a competitive analysis of a retail RFM/marketing-
analytics dashboard. Reproducing it exposed a blocker: `PosTransaction.CustomerId` (nullable FK,
existed since v1) is never written by any code path — every sale is anonymous today, so RFM/LTV
would show all-zero data with no way to attribute a receipt to a person. Plan
`C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` splits the work into Фаза 0 (a loyalty/bonus
program that gives customers a reason to identify themselves at checkout — scan a QR, earn bonus
points — and thereby writes `CustomerId`) and Фаза 1 (the RFM dashboard itself, built on Фаза 0's
now-populated data). TASK-404 through TASK-414 implemented both; this ADR records the
architectural decisions behind the identity model and naming, verified against the actual shipped
code (not just the plan).

Decision:

1. **`ConsumerAccount` is a new, separate, global (no `TenantId`, no RLS) entity — not an
   extension of `Customer` or `User`.** `Customer` is tenant-scoped CRM data (phone unique only
   *within* a tenant — confirmed via `CustomerRepository.ExistsByPhoneAsync`); `User` is a
   tenant-scoped staff account. Neither can back "one login reads every tenant's bonus balance"
   (the plan's explicit requirement — a multi-tenant "wallet of cards"): a `ConsumerAccount` JWT
   carries `consumer_account_id` and **no** `tenant_id` at all, and reads across every
   `LoyaltyMembership` it holds regardless of tenant. Extending `Customer` would have required
   either making `Customer.Phone` globally unique (breaking existing tenant-scoped semantics — two
   unrelated tenants legitimately have customers who share a phone number) or bolting a parallel
   global-identity concept onto an entity whose entire reason to exist is tenant scoping. A
   `LoyaltyMembership` join row (tenant-scoped, FK to both `ConsumerAccount` and `Customer`)
   composes both without compromising either — `Customer` keeps its existing tenant-local meaning;
   `ConsumerAccount` is the only genuinely global identity concept added by this series.
   Consequence, accepted deliberately: `consumer_accounts` is the one table in the project with
   **no RLS** (same precedent as `tenants`), reviewed explicitly by security-reviewer (TASK-412,
   item #1 — verdict OK, no generic non-owner lookup exists anywhere in the codebase) and
   documented as a standing convention in `database-schema.md`, not a gap to "fix" later.

2. **The "live" rotating QR/barcode reuses the existing TOTP infrastructure (`Otp.NET`/
   `ITotpService`, already used for `User` 2FA) instead of inventing a new rotating-token format.**
   The plan's requirement (protect against screenshot-sharing — a static code defeats the whole
   point of scan-to-earn) is structurally identical to what TOTP already solves for 2FA: a shared
   secret plus a time-step counter produces a code that rotates on a fixed interval and can be
   verified with a bounded-window anti-replay check. `LoyaltyMembership.TotpSecret` +
   `LastRedeemedTimestep` mirror `User`'s 2FA columns exactly; `ITotpService` gained one new
   method, `GenerateCode(secret)` (the server computes the *current* code and hands it to the
   wallet screen) — the mirror image of the existing `VerifyCode` used for staff 2FA login. The QR
   payload itself (`SGLOY1.{membershipId}.{code}`) is a thin, new, deliberately simple wrapper —
   version tag + membership id for O(1) staff-side lookup + the rotating TOTP code — not a new
   cryptographic primitive. Anti-replay reuses the same "atomic claim of a monotonically
   increasing counter" shape `ProductStock`'s optimistic concurrency already established in this
   codebase (`ILoyaltyRepository.TryClaimTimestepAsync`, a single WHERE-guarded
   `ExecuteSqlInterpolatedAsync` UPDATE — verified genuinely atomic and parameterized by
   security-reviewer, TASK-412 item #3). Rejected: inventing a bespoke rotating-token scheme — it
   would duplicate exactly what TOTP already does correctly and add a second, unaudited crypto
   primitive to the codebase for no behavioral gain.

3. **`"loyalty"` and `"marketing_analytics"` are two independent `Tenant.modules` keys, not one.**
   A tenant can run a bonus program without ever activating the RFM dashboard (e.g. a small
   single-store client that wants scan-to-earn but has no marketing analyst to act on
   segmentation), and — less obviously but just as real — a tenant could in principle activate
   `marketing_analytics` without `loyalty` at all, since Фаза 1's RFM engine only needs
   `PosTransaction.CustomerId` populated, which POS's plain customer-search-and-attach path
   (`CustomerId` alone, no membership/balance involved) already provides independently of the
   bonus mechanism. Coupling both features behind a single module key would force an all-or-
   nothing activation that doesn't match either real usage pattern, and would entangle two
   independently-evolving features' rollout/pricing decisions behind one flag. Both keys were
   added to `Tenant.UpdateModules`'s `valid[]` list together (TASK-405) since Фаза 1 depends on
   Фаза 0's data-writing path existing, but they gate unrelated endpoint sets
   (`[RequireModule("loyalty")]` on `LoyaltyController` vs. `[RequireModule("marketing_analytics")]`
   on `MarketingAnalyticsController`) and can be toggled independently per tenant from day one.

4. **Naming discipline: always `RfmSegment...` (`RfmSegmentKey`, `RfmSegmentDetailDto`, ...),
   never a bare `Segment...`.** `Item.Segment` (nav property to `ProductSegment`) already means the
   promo-cannibalization demand segment from v2 (`Features/Cannibalization/`) — confirmed in
   `Item.cs` before any RFM code was written. A bare `SegmentDto`/`ISegmentService` in a new
   `Features/MarketingAnalytics/` module would silently collide in meaning (not in compiler-checked
   namespace — C# would happily compile two different `SegmentDto`s in two namespaces — but in the
   mind of every future reader grepping "Segment" across the codebase) with an unrelated,
   already-shipped concept. `RfmSegmentKey`'s own doc comment states this explicitly, and the
   convention is applied consistently across the whole new module — DTOs, the classifier, the
   repository methods, the frontend `types.ts` transcription (TASK-409) — with zero exceptions.

Consequences:
+ Cross-tenant loyalty wallet works with zero compromise to `Customer`'s existing tenant-scoped
  uniqueness semantics — no migration risk to the many existing tenant-scoped tables' assumption
  that a phone number is only meaningful within one tenant
+ Zero new cryptographic primitive — the QR "liveness" mechanism is exactly as auditable as the
  already-shipped, already-reviewed 2FA TOTP path, just pointed at a different entity
+ Independent module activation matches real tenant variation (bonus-only, analytics-only-via-
  plain-attach, or both) without a forced bundle
+ `RfmSegment...` naming avoids a real, confirmed collision with `ProductSegment`'s existing meaning
- `ConsumerAccount` carrying no RLS is a permanent, deliberate exception to this codebase's
  otherwise-universal "every tenant-touching table gets RLS" rule — every future reader must learn
  this is intentional (documented in three places: the migration's own class doc comment,
  `database-schema.md`, and this ADR) rather than assume it is an oversight
- Two module keys instead of one is marginally more provider-panel/admin-panel surface (two
  checkboxes, two i18n label pairs) for a feature pair that, in practice, most tenants will
  probably activate together — accepted, since the independent-activation case is real, not
  hypothetical
- `LoyaltyMembership`/`LoyaltyLedgerEntry`'s identity-based `consumer_self_access` RLS policy
  (`database-schema.md`) is the first of its kind in this repo — a new pattern future agents must
  learn alongside the existing role-based `tenant_isolation`/`provider_bypass`/`worker_bypass` triad

Extends: reuses ADR-020's `TenantRoleCapabilities`/`RoleOrCapabilityRequirement` mechanism
verbatim for `marketing_analytics.view`/`marketing_analytics.export_pii` (new "Маркетинг"
capability group) — no new authorization primitive introduced for Фаза 1's own access control.

**Addendum (TASK-419/420, 2026-07-27) — Фаза 2 price segments + frequency/reactivation.** Same
plan (`deep-cooking-nygaard.md` §"Фази 2-4"), same module key (`marketing_analytics`, no new one),
same `RfmSegment`-style naming discipline extended to `PriceSegmentKey`/`PriceAudienceKey`/
`FrequencyAudienceKey`. Three decisions worth recording:

1. **`PERCENTILE_CONT` (ordered-set aggregate), not `NTILE` (window function), for price-segment
   boundaries — a different quantile primitive than Фаза 1, deliberately.** Фаза 1's R/F/M scoring
   needs a per-customer **bucket assignment relative to the current query's own rows**
   (`NTILE(5)` — "which fifth is this customer in, among these rows, right now") and is always
   recomputed fresh; the assignment is never reused as a standalone number. Фаза 2 needs the
   opposite: an actual **₴ cutoff value** (P20/P40/.../P97) that must mean the same thing across
   three separate call sites — the comparison table, the all-time table, and the frequency tab's
   `priceSegment` filter all need to agree on what "Tier3" *is* in currency terms, not just which
   rows fall in it this query. `NTILE` has no notion of an interpolated cutoff that survives outside
   the query that produced it; `PERCENTILE_CONT(0.20/.../0.97) WITHIN GROUP (ORDER BY
   median_check)` computes exactly that reusable boundary, which `PriceSegmentCatalog.RangeLabelUa`
   renders as `"120–190 ₴"`. Implementation trap, not a design point: every `PERCENTILE_CONT` call
   must be cast `::numeric` — Postgres always returns `double precision` from it regardless of the
   input column's type, caught live when 7/10 of TASK-420's integration tests threw
   `InvalidCastException` before the cast was added at all 15 call sites (task log 420).

2. **Segment boundaries are computed all-time, never from the active comparison window.**
   `PriceSegmentsRepository.GetBoundariesAsync` carries no date filter at all — one P20..P97 cutoff
   set per tenant, shared by the 30/60/90-day comparison view, the all-time view, and the frequency
   tab's segment filter alike. Not an arbitrary simplification: the competitor analysis
   (`docs/uployal/PRICE_SEGMENTS_ANALYSIS.md` §8.3) directly observed the competitor's own
   boundaries holding identical across every period it tested and concluded "це вказує на мережеві,
   а не періодичні межі сегментації" — empirically confirmed competitor behavior, not a guess filled
   in where the source was silent. Recomputing boundaries per-window would also make a customer's
   tier label mean a different ₴ range depending only on which period filter happens to be active —
   actively confusing for a label whose whole purpose is a stable, nameable price tier.

3. **`Stable` (comparison mode) ships as a full first-class `PriceAudienceKey` member from day
   one — list, sort, paginate, export, and a real recommendation — not just the KPI number the
   competitor limits it to.** The competitor computes and displays a `Стабільні` count but
   deliberately gives it no card/list/export (analysis doc §7.4/§25.3 flags this as a functional gap,
   not a design worth copying). Since `PriceAudienceKey`/`PriceSegmentCatalog`/the repository's
   shared classification CASE ladder already treat all 4 audiences identically end-to-end, full
   parity for `Stable` cost nothing beyond the 4th enum member and its recommendation copy.

Consequences: (+) tier labels stay stable, comparable numbers across every view instead of shifting
meaning per filter; (+) `Stable` gives marketers a genuine "protect this base" workflow the
competitor's page structurally can't offer; (-) a brand-new tenant with little history gets
boundaries computed over a small all-time sample — `PriceSegmentSettings.
MinReceiptsForBoundaries` is persisted but not yet read by `GetBoundariesAsync`, flagged by
security-reviewer (TASK-422) as an inert functional gap for a follow-up task, not a security one.

**Addendum (TASK-428/429/431, 2026-07-27) — Фаза 3 AudienceBuilder: accept the Seq Scan; do not
mark `texticlike` LEAKPROOF or add a SECURITY DEFINER search function for v1.**

Context: TASK-428 (database-engineer) live-verified that `idx_items_name_trgm` — the new GIN
trigram index added specifically for AudienceBuilder's text-term search — is **structurally
unusable** by the query planner on the real, RLS-protected app connection. `items` has canonical
RLS + `FORCE ROW LEVEL SECURITY`; `ILIKE` compiles to `texticlike`, which Postgres's own `pg_proc`
marks `proleakproof = false`. Under `FORCE ROW LEVEL SECURITY`, a predicate built from a
non-LEAKPROOF function can only be applied as a post-scan `Filter`, never pushed into an index
condition — this holds even for the table owner. Live-measured: ~1085ms Seq Scan (real app role,
500k synthetic rows, rolled back after) vs ~2ms Bitmap Index Scan (superuser bypassing RLS, same
index/data; `enable_seqscan=off` on the app-role side still produced no index plan at all — proof
the planner has no alternative, not merely a deprioritized one). Not new to this feature: the same
live test against the pre-existing `idx_notification_queue_title_trgm` shows the identical
Filter-not-Index-Cond behavior — that index has, as best this session could tell, never actually
accelerated a real tenant-scoped keyword search in production either.

Three options were on the table (TASK-428's log; decided by the orchestrating session before
TASK-429 began, per CLAUDE.md's clarify-before-implementing gate — marking a core Postgres
function LEAKPROOF is a schema-wide security-posture change, not an isolated indexing decision):

1. **Mark `texticlike` (and related pattern-matching support functions) `LEAKPROOF`.** Would fix
   the index path for every RLS table using LIKE/ILIKE across the whole codebase, not just
   `items` — broadest fix, broadest blast radius. Rejected for v1: `LEAKPROOF` is Postgres's
   promise that a function reveals nothing about its arguments through side channels (errors,
   timing) to a caller who shouldn't see the underlying rows — asserting that for a core
   string-matching primitive used everywhere is a real security claim about timing side-channels
   that needs its own dedicated review, not a decision to make as a side effect of one feature's
   index tuning.
2. **A `SECURITY DEFINER` search function**, owned by a privileged role, that bypasses RLS
   internally but re-applies its own hardcoded, provably-safe `TenantId = current_setting(...)`
   guard before returning rows — narrower blast radius than (1) (scoped to whichever call sites
   adopt it, not every ILIKE in the codebase), same spirit as the existing `provider_bypass`/
   `worker_bypass` policy escape hatches. Rejected for v1: still a new, hand-written RLS-bypass
   surface that has to be gotten exactly right (the whole point of RLS is that the tenant guard is
   enforced uniformly by Postgres, not re-implemented correctly by every function that opts out of
   it) — worth building only if the Seq Scan cost actually becomes a measured problem.
3. **Accept the Seq Scan at realistic per-tenant catalog sizes, change nothing.** `items.Name`
   text search is a "type a term, press Enter" field, not a live-autocomplete search — at the scale
   this actually runs at (thousands of SKUs per tenant, not the 500k-row/all-tenants synthetic
   worst case TASK-428 tested), a few hundred milliseconds is not a UX problem worth a new
   security-posture decision to solve pre-emptively. **Chosen.**

Option 3 was picked as the most conservative of the three: it changes zero existing security
posture, defers both (1) and (2) as available future fixes rather than foreclosing them, and costs
nothing beyond documented latency at a scale this feature doesn't run at today. The tradeoff is
recorded redundantly in code (not just here), so a future reader doesn't have to rediscover it from
scratch: `IAudienceBuilderRepository`'s class-level doc comment, `AudienceBuilderRepository`'s
class doc comment, and an inline comment on `SearchCategoriesAsync` (the categories-`ILIKE` path
has the identical tradeoff, smaller table) — all three cite TASK-428's actual measurement.
security-reviewer (TASK-431) independently re-verified this is a **performance-only** tradeoff, not
a tenant-isolation bypass: TASK-428's own `EXPLAIN ANALYZE` shows the RLS tenant predicate still
applies as a `Filter` regardless of the index question, and every AudienceBuilder CTE additionally
carries its own redundant, explicit `TenantId = {0}` filter on top of whatever RLS does
(defense-in-depth, consistent with existing repository convention) — only query latency at large
multi-tenant catalog sizes is the accepted cost, never correctness or isolation.

Consequences: (+) zero new security-posture surface, zero new attack surface, decision fully
reversible later if (1) or (2) becomes worth it; (+) the same tradeoff note now also explains why
the pre-existing `idx_notification_queue_title_trgm` has likely never helped production either,
closing a question that would otherwise have resurfaced independently; (-) `idx_items_name_trgm`
is inert on the only connection that matters (the real app role) until (1) or (2) is adopted —
flagged as a known v1 limitation in `database-schema.md`, not a defect to "fix" by re-tuning the
index itself; (-) the identical class of bug (non-LEAKPROOF cross-type comparison functions) can
recur silently for any future raw-SQL query that compares a `timestamptz` column against a bare
`DateOnly`-derived parameter — mitigated here by explicit `::timestamptz` casts at every
`t."CreatedAt"` comparison in `AudienceBuilderRepository` (TASK-428's own side-finding, applied by
TASK-429, confirmed consistent by TASK-431), but the general pattern is worth remembering for the
next raw-SQL repository, not just this one.

## ADR-022: Store-scoped user assignment & data visibility (`user_locations` + RLS)
Date: 2026-07-19
Status: accepted (Stage 1 live in production; Stage 3 written and tested but deliberately not
deployed — see rollout checklist)

Context: `User.StoreId` ("assigned home store") has existed since `AddAuth` (2026-06-03) but was
a dead field — unmapped (no `HasColumnName`), no FK, no index, and no code path anywhere ever
read it for access control (unlike the ~19 other pre-v4 entities carried through
`V4LocationsRename`). Meanwhile every store-scoped business table (`product_stock`,
`daily_sales`, `pos_shifts`, etc.) is only tenant-isolated by RLS — any user in a tenant sees
every store's data tenant-wide regardless of role. Product owner asked for real store-scoped
visibility: a `store_manager`/`cashier`/etc. should see only their assigned store(s)' stock/
sales/POS/write-offs, not the whole tenant.

Decision:
1. **`enterprise_admin` — unconditional bypass.** No `user_locations` rows needed or ever
   written for this rank. Every other rank (`network_manager`, `store_manager`, `merchandiser`,
   `storekeeper`, `cashier`, `staff`) is scoped through a new many-to-many `user_locations`
   table — **including single-location roles**, which get exactly one row rather than being
   special-cased through `User.StoreId`. One enforcement mechanism for every restricted rank,
   not a shortcut for the common single-store case.
2. **New `user_locations` table**: `Id`, `TenantId` (direct column with its own leading index —
   Stage 3's RLS policy will `EXISTS`-subquery into this table from 9 other tables, so it needs
   to be efficiently scannable on its own), `UserId` (FK→users, Cascade), `LocationId`
   (FK→locations, Cascade), `AssignedByUserId` (FK→users, SetNull, audit field), `CreatedAt`.
   Unique `(TenantId, UserId, LocationId)` + secondary `(TenantId, LocationId)`. No soft-delete —
   pure leaf assignment table, hard DELETE revokes. RLS at this stage is the standard
   `tenant_isolation`/`provider_bypass`/`worker_bypass` triad only — **not** yet the RESTRICTIVE
   store-scope policy (that is Stage 3, point 5 below); nothing reads this table for access
   control until then.
3. **`User.StoreId` fixed, not removed.** Now correctly `.HasColumnName("LocationId")` +
   `SetNull` FK to `locations` (same nullable/optional shape as `ProviderRoleId`/`TenantRoleId`).
   It stays a UI/invite-time "default home location" hint only — **never** read by access-control
   enforcement. `user_locations` is the single source of truth for that; the two must not be
   conflated.
4. **API**: `PUT` / `GET /api/users/{id}/locations` (full-replace / current list) —
   `AtLeastEnterpriseAdmin`-only, **no** capability-OR bypass, same anti-escalation posture as
   `AssignTenantRole` (ADR-020) — this endpoint decides what real business data a whole role
   will see once Stage 3 lands, so a `users.manage` capability holder must never be able to grant
   it to themselves or others. `UserService.InviteAsync`/`UpdateAsync` write the single
   `user_locations` row automatically for single-location roles (`store_manager, merchandiser,
   storekeeper, cashier, staff`) from the existing `storeId` field; `network_manager`'s
   (potentially multi-location) assignment is managed only through the dedicated endpoint. New
   `ILocationService.BelongsToTenantAsync` closes a pre-existing gap where `storeId` accepted any
   GUID with zero tenant-ownership check.
5. **Three-stage rollout — deliberately not one migration:**
   - **Stage 1 (deployed to production)** — additive schema + `user_locations` API + assignment
     UI (invite modal, user detail panel, `UserLocationsEditor`). Zero behavior change: nothing
     queries `user_locations` for access control yet.
   - **Stage 2 (not code — a manual, per-tenant admin task)** — every existing
     `network_manager`/`store_manager`/`merchandiser`/`storekeeper`/`cashier`/`staff` user must
     get at least one `user_locations` row via the Stage 1 UI/API before Stage 3 can safely
     apply. Tracked via a coverage-gap SQL report in
     `.claude/docs/store-scope-rollout-checklist.md`.
   - **Stage 3 (written, tested, held back)** — RESTRICTIVE `store_scope` RLS policy,
     `EXISTS`-scoped through `user_locations`, on 9 tables: `product_stock`, `daily_sales`,
     `pos_shifts`, `pos_transactions`, `write_offs`, `discounts`, `stock_receipts` (one-sided,
     `DestinationLocationId` — a receipt comes from a supplier, not another store),
     `stock_movements`/`stock_transfers` (two-sided OR-match, `From`/`ToLocationId`). Bypass
     roles: `provider`, `provider_admin` (added beyond the original brief — it already has full
     bypass parity with `provider` via the pre-existing `provider_bypass` policy on these same
     tables; omitting it here would have silently regressed that already-audited access),
     `worker`, `enterprise_admin`. Migration `AddLocationStoreScopeRlsPolicies` exists, is fully
     tested (9 new xunit integration tests + manual live-verification scenarios against the real
     non-superuser app role, rollback/reapply round-trip confirmed), and is committed **only** on
     local branch `stage3-rls-enforcement-hold` — **not merged to `main`, not deployed anywhere.**
6. **Fail-closed, product-owner-confirmed.** The instant Stage 3's policy applies, a user in a
   scoped role with **zero** `user_locations` rows sees **zero** rows on all 9 tables — not a
   bypass, not a tenant-wide fallback. This is why Stage 2's backfill must reach zero gap
   *before* Stage 3 can ever be applied to a real environment; applying it early is an immediate,
   total functional outage for every un-backfilled user (their whole job — stock, sales, POS,
   write-offs — goes blank at once, tenant-wide, the moment the migration commits). Full gating
   procedure, the coverage-gap query, and the emergency rollback command live in
   `.claude/docs/store-scope-rollout-checklist.md` — not duplicated here.
7. **Child tables need no new policy** (`stock_receipt_items`, `stock_transfer_items`,
   `write_off_items`, `pos_transaction_items`) — Postgres re-applies a referenced table's
   RESTRICTIVE RLS inside any subquery/join that reads it, so they inherit the new scoping
   through their existing parent-`EXISTS` `tenant_isolation` policy for free, same mechanism
   `supplier_chat_messages` already relies on (ADR-017 era).

Consequences:
+ Single enforcement mechanism (`user_locations`) for every restricted rank — no special-cased
  single-store shortcut to keep in sync with the multi-store path
+ `enterprise_admin`/`provider`/`provider_admin`/`worker` bypass paths are unconditional and
  unchanged — zero risk of locking out administrative or platform-operational access
+ Explicit three-stage gate keeps the highest-risk step (Stage 3) reversible right up until the
  moment it's applied, and cheaply reversible after (`Down()` drops all 9 policies in one shot)
+ Child tables inherit store-scoping for free through existing parent-`EXISTS` policies — no
  additional migration surface
- Real operational dependency on Stage 2 being done *thoroughly* — a single missed user in any
  tenant sees a complete, immediate outage the moment Stage 3 ships; the rollout checklist's
  coverage-gap report is the only safety net and must be re-run right before cutover, not just once
- `User.StoreId` now has two "home location" concepts (the legacy hint field, and the real
  `user_locations` rows) a future reader could conflate — mitigated by the code comment on
  `User.StoreId` and this ADR stating explicitly it is UI-hint-only, never an access-control input
- Stage 3 sits on a long-lived side branch (`stage3-rls-enforcement-hold`) rather than `main` —
  normal branch-hygiene drift risk while it waits, accepted deliberately since merging code that
  isn't safe to run yet would invite an accidental deploy

Extends: ADR-020 (reuses its `AtLeastEnterpriseAdmin`-only, no-capability-bypass anti-escalation
posture for the new location-assignment endpoints).

## ADR-021: TenantRole — per-role sidebar tab visibility (`AllowedTabs`)
Date: 2026-07-19
Status: accepted (Tier 1 enforcement only — see point 5; Tier 2 explicitly deferred)

Context: ADR-020's `TenantRoleCapabilities` gates backend *actions* (can this capability holder
call this endpoint) — it says nothing about sidebar *visibility*. This left a real, confirmed
gap: a user granted e.g. `analytics.view` via a TenantRole template, but whose base `Role` rank
is below whatever `Sidebar.tsx` requires for the "Аналітика" NavGroup, passes every backend check
ADR-020 wired for them yet has no navigable link to the data they can legitimately call the API
for. The same shape recurs for `users.manage`/`schedules.manage` (workforce). Confirmed by
reading `Sidebar.tsx`'s `buildNavGroups()`/NavItem `roles` arrays directly against ADR-020's 8
gated controllers — the mismatch is real, not hypothetical.

Decision:
1. New `TenantRole.AllowedTabs: List<string>` column (`text[]`, default `[]`) — deliberately
   **the same storage shape as `Capabilities`**, not the `jsonb` the initial task brief assumed:
   the real `Capabilities` column (`AppDbContext.cs`) is a native Postgres `text[]`, matching
   `ProviderRole.Permissions`/`SupplierRole.Permissions` exactly, with no `HasConversion`/
   `EnableDynamicJson`. `AllowedTabs` follows that verified, three-entity-precedent pattern
   rather than the brief's unchecked wording.
2. **Fixed catalog of 10 tab keys** (`TenantRoleTabs`, `ShelfGuard.Domain.Constants`, mirrors
   `TenantRoleCapabilities`'s shape): `dashboard, operations, sales, procurement, marketplace,
   auto_service, production, analytics, workforce, support`. Verified 1:1 against `Sidebar.tsx`'s
   real `NavGroup.key` values (9 groups) plus the standalone `dashboard` NavItem (not a
   NavGroup, but a real, separate nav destination). Labels copied verbatim from
   `frontend/messages/uk.json`, not re-authored.
3. **Deliberately excluded, forever**: `admin` (provider-only NavGroup — a tenant-scoped
   TenantRole must never unlock the provider panel), `supplier_cabinet` (supplier_admin-only,
   governed by the separate `SupplierRole` mechanism), `settings` (always-visible
   personal-preferences NavItem, not a business module — nothing there is meant to be hidden
   per role).
4. **Additive, same compositional principle as `Capabilities`** — `AllowedTabs` only ever
   *widens* what a user sees beyond their base `Role`'s default nav; it never narrows or
   replaces the existing role-based sidebar/route logic.
5. **Enforcement is two-tier, and only Tier 1 exists today:**
   - **Tier 1 (real, live today):** for the tabs that correspond to an ADR-020 capability
     already wired to a real backend gate (`workforce` → `users.manage`/`schedules.manage`,
     `analytics` → `analytics.view`), granting the matching capability *and* the tab together is
     coherent end-to-end — the capability is what the backend actually checks; the tab is what
     makes the frontend show the link and pass the new `useRequireTab` page guard. This is the
     only case where `AllowedTabs` sits in front of something the backend genuinely enforces.
   - **Tier 2 (explicitly deferred, not built this wave):** the remaining tab keys (`sales`,
     `procurement`, `marketplace`, `auto_service`, `production`, `support`, plus `dashboard`/
     `operations`) have no matching ADR-020 capability at all today. Granting one of these makes
     the sidebar link appear (`Sidebar.tsx`'s tab check is generic across all 10 keys), but
     nothing server-side or page-level consults it — the destination page/API falls back to
     whatever role-only gate (or absence of one) already existed. This is a UX gap (a link that
     may lead to a page/API that still says no — not a security hole, since `AllowedTabs` never
     grants backend access on its own), to be closed only if/when new capabilities
     (`sales.view`, `marketplace.view`, etc.) or a generic `TabOrRoleRequirement`/Handler are
     built. Not scheduled — build only if a real specialty template needs it.
6. **Frontend wiring**: `Sidebar.tsx` computes `tabsSet` from `me.tabs` (null when empty/absent)
   and OR's it into the NavGroup visibility filter, positioned after the Legal Entities
   special-case and before the generic `item.roles` check — it bypasses only the coarse role
   check, never the narrower Legal Entities gate. New `useRequireTab(tabKey, alreadyAllowed)`
   hook is a page-level route guard: `effectiveAccess = alreadyAllowed || me.tabs.includes(tabKey)`,
   redirects to `/dashboard` otherwise. Wired to 3 pages so far: `/users` (tightens direct-URL
   access below store_manager rank — the actual point of the feature, closing a page that
   previously had no page-level gate at all), `/schedules` (wired but inert — that page has no
   role restriction to begin with, every role already reaches it), `/analytics` (also fixed the
   page's own pre-existing `access` variable to fold in the hook's result, so a tabs-granted user
   doesn't hit a dead sidebar link followed by an `AccessDenied` page).
7. **JWT/`AuthUserDto` plumbing mirrors ADR-020's Capabilities mechanism exactly**:
   `AuthService.BuildEffectiveTabsAsync` (parallel to `BuildEffectiveCapabilitiesAsync`, same
   null/archived-role handling, deliberately a *separate* `TenantRole` read — Tabs and
   Capabilities are independent axes per point 5, so one being empty must never suppress the
   other), comma-joined JWT `tabs` claim (absent when empty), new `AuthUserDto.Tabs`.
   `GET /api/tenant-roles/tabs` (`AtLeastEnterpriseAdmin`, same gate as `/capabilities`) serves
   the catalog for the role-editor UI.

Consequences:
+ Closes a real, confirmed capability-vs-visibility mismatch for the 3 capabilities ADR-020
  already enforces (`users.manage`, `schedules.manage`, `analytics.view`)
+ Zero behavior change for any user with no `TenantRoleId` — the `tabs` claim is simply absent,
  and `useRequireTab`'s OR degrades to whatever gate already existed
+ Same storage/JWT/DTO mechanism as Capabilities — one pattern to learn; `TenantRoleTabs.cs`'s
  own doc comment explains why the two lists are kept separate rather than merged into one
- Tier 2 is a known, unclosed gap: granting a tab outside {workforce, analytics} today produces
  a visible-but-not-fully-enforced nav destination — acceptable short-term (no security exposure,
  since `AllowedTabs` never grants backend access by itself) but a real UX rough edge if a
  template ever grants e.g. `sales` alone
- Two independent per-TenantRole axes (`Capabilities`, `AllowedTabs`) to reason about when
  designing a template, rather than one — mitigated by `TenantRoleTabs.cs`'s explicit rationale
  comment and by both following the identical mechanical pattern

Extends: ADR-020 (adds a second, independent per-TenantRole axis — `AllowedTabs` alongside
`Capabilities` — reusing the same storage/JWT/DTO mechanism rather than inventing a new one).

**Addendum (TASK-398, 2026-07-20) — item-level granularity:** product feedback confirmed the
original 10 group-level keys are too coarse (granting `operations` unlocks all 7 pages in that
group at once, no way to grant e.g. only Receipts). Added 27 item-level keys — the literal
`NavItem.href` per page (`"/inventory"`, `"/receipts"`, ...) — unioned into the same
`TenantRoleTabs.All`/`Validate` set as the original 10; no new column, no new JWT claim, no schema
change. The 10 group-level keys are kept exactly as-is, forever, for backward compat with
already-configured templates. `GET /api/tenant-roles/tabs` now returns a hierarchy
(`TenantRoleTabGroupDto[]` — a group's own bulk-grant key plus its nested per-page items; the
standalone Dashboard section has `groupKey: null`) instead of TASK-391b's flat list, so a future
editor UI can offer both granularities. **Deliberately backend/catalog-only** — `Sidebar.tsx`
still only ever checks the group-level key (`tabsSet.has(group.key)`, point 6 above); wiring
item-level enforcement into the sidebar/route guards is a separate, not-yet-scheduled follow-up.
Until then, granting only an item-level key does **nothing** client-side (Sidebar.tsx doesn't read
these keys yet) — one step behind even the Tier 2 status the original 10 keys had at ship time.
One included item is worth flagging for that follow-up: `"/settings/legal-entities"` is a real
Workforce NavItem so it's in the catalog for completeness, but Sidebar.tsx's TASK-397 carve-out
already excludes that one href from `tabsSet` entirely (visibility is `canManageLegalEntities`-only,
by design) — the follow-up should keep excluding it rather than newly wire it up.

## ADR-020: TenantRole — named custom-role templates with real backend capability enforcement
Date: 2026-07-13
Status: accepted

Context: User (enterprise_admin) wants named, reusable custom-role templates ("HR",
"Бухгалтер", "Фінансист", "Відділ закупки" — cashier skipped, already in `AppRoles`), each
an arbitrary capability set, assignable to many users, edited centrally (propagates to all
assignees). Clarified: (1) enforcement must be real on the backend, not UI-only; (2)
templates, not per-user snapshots; (3) sane per-specialty defaults, hand-tunable later via
UI. Precedent: `ProviderRole`/`SupplierRole` (`backend/ShelfGuard.Domain/Entities/
{ProviderRole,SupplierRole}.cs`) — free-form `List<string> Permissions`, resolved via
`User.ProviderRoleId`/`SupplierRoleId`. `User.Role` cannot become a free-form template
string — `AppPolicies.cs` gates ~50 controllers with `RequireRole(fixedRoleArray)`,
entirely independent of `User.Permissions`.

**The blocking discovery**: every controller the 5 specialties need (`SchedulesController`,
`AnalyticsController`, `IntegrationsController`, `OrdersController`, `SuppliersController`,
`ReceiptsController`, `AiOrdersController`, `UsersController`) already gates its *entire*
action set behind one class-level `[Authorize(Policy = X)]` — `AtLeastStoreManager` or
tighter (`CanViewAnalytics`, `CanReceiveStock`) — evaluated by ASP.NET Core's authorization
middleware *before* the action body runs. An imperative in-body check (the
`LegalEntityAuthorization.CanManage` pattern, `backend/ShelfGuard.Infrastructure/
Authorization/LegalEntityAuthorization.cs`) only ever *narrows* access the class-level gate
already let through — it cannot admit a user that gate rejected. `LegalEntitiesController`
works only because its class-level gate (`AtLeastStoreManager`) is *looser* than the
enterprise-admin-only check layered on top. A capability-only user below store_manager
rank is 403'd before any per-action logic runs, on 7 of these 8 controllers.

Decision:
1. **New minimal base role `AppRoles.Staff = "staff"`**, rank 0 (below cashier) in
   `UserService.RoleRank`, added to `UserService.ValidRoles` (invite whitelist) and
   `TenantConnectionInterceptor.ValidRoles` (session `app.role` whitelist) — it is a real,
   sanctioned role string, not a template name. Added to `AppRoles.All`. It is **not** added
   to any existing `AppPolicies` role array — by itself it grants nothing beyond bare auth
   (own profile, own notifications, `GET /api/schedules/my-shifts`, already ungated).
2. **`tenant_roles` table**: `Id, TenantId, Name, Capabilities (jsonb List<string>), IsActive,
   CreatedByUserId, CreatedAt, UpdatedAt`. Partial unique index `(TenantId, Name) WHERE
   "IsActive"`. `User.TenantRoleId Guid?` FK `ON DELETE SET NULL` (mirrors `ProviderRoleId`).
   Archiving sets `IsActive = false`; never hard-deleted (users may still reference it).
3. **New `TenantRoleCapabilities` constants class** (`ShelfGuard.Domain.Constants`, format
   `module.action`, shape of `TenantUserPermissions`) — capability → unlocked actions:
   `users.manage` (HR — Invite/Update/Deactivate on `UsersController`; excludes
   `UpdatePermissions`/`GrantTemporaryPermission`/tenant-role assignment, enterprise_admin-
   only, no escalation path), `schedules.manage` (HR — Create/Update/Delete + shift CRUD on
   `SchedulesController`), `analytics.view` (Бухгалтер/Фінансист — all GET on
   `AnalyticsController`, read-only controller), `integrations.view`/`integrations.manage`
   (Бухгалтер — GetAll/GetByService vs Upsert/Delete on `IntegrationsController`),
   `legal_entities.manage` (Бухгалтер — **reuse** the existing `TenantUserPermissions.
   LegalEntitiesManage` key), `orders.manage` (Закупка — `OrdersController.Calculate`),
   `suppliers.view`/`suppliers.manage` (Закупка — Get* vs Create/Update/Delete on
   `SuppliersController`), `receipts.view` (Закупка — Get* on `ReceiptsController`;
   Create/Receive/Cancel stay role-gated, write-heavy stock path), `ai_orders.view`/
   `ai_orders.manage` (Закупка — Get* vs Generate/Update/Accept/Reject on `AiOrdersController`).
4. **Enforcement — custom `IAuthorizationHandler`, not in-body checks.** New
   `RoleOrCapabilityRequirement(string[] allowedRoles, string capability)` +
   `RoleOrCapabilityHandler` (`ShelfGuard.Infrastructure/Authorization/`) succeeds when the
   caller's role ∈ `allowedRoles` (unchanged for every existing role) **OR** the JWT
   `capabilities` claim contains `capability`. For each capability in point 3, register one
   new named policy in `AppPolicies.Configure` (e.g. `AnalyticsViewOrCapability` =
   `CanViewAnalyticsRoles` ∪ `"analytics.view"`) and move the affected actions from the
   controller's class-level policy to **per-action** `[Authorize(Policy = ...)]` — class-level
   attribute removed only on these 8 controllers. `LegalEntitiesController` instead extends
   `LegalEntityAuthorization.CanManage` to OR-in `TenantRoleAuthorization.HasCapability(user,
   "legal_entities.manage")` — already has the imperative-check shape, no new policy needed.
   **Every other controller (POS, stock write-off, transfers, fiscalization) is untouched.**
5. **`TenantRoleAuthorization.HasCapability(ClaimsPrincipal, string)`** (mirrors
   `LegalEntityAuthorization`) reads the new `capabilities` claim — shared by point 4's
   handler and the `LegalEntitiesController` extension.
6. **Template CRUD**: `TenantRolesController` (`/api/tenant-roles`, GET list/GET id/POST/
   PUT/DELETE-archives) and `POST /api/users/{id}/tenant-role` (new action on
   `UsersController`) — both `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`, no
   capability bypass, per the brief's anti-escalation requirement.
7. **JWT merge**: new `AuthService.BuildEffectiveCapabilitiesAsync(User, ct)`, parallel to
   `BuildEffectivePermissionsAsync` (ADR-019) — resolves `user.TenantRoleId` (empty if null
   or inactive) into a `List<string>`. `JwtService.GenerateAccessToken` gets an optional
   `capabilities` param, serialized as a comma-joined claim (shape of `permissions`). Called
   at both mint sites (login, refresh) and fed into `AuthUserDto`. Same ~15-min propagation
   delay already accepted in ADR-019.
8. **RLS**: `tenant_roles` gets `tenant_isolation` + `provider_bypass` + `worker_bypass` in
   one migration — worker will never touch this table, added anyway per convention.
9. **Frontend contract**: new tab on `/users` (or `/users/roles`), reusing the
   `frontend/features/supplier-cabinet/components/RolesTab.tsx` +
   `frontend/features/provider/components/RolesSection.tsx` skeleton — name field + checkbox
   list of capabilities grouped by specialty, sourced from `GET /api/tenant-roles/
   capabilities` (backend is the source of truth for grouping, ADR-017 pattern, not a
   frontend hardcode). Assignment: new `<TenantRoleSelector>` next to `frontend/features/
   users/components/UserPermissionsEditor.tsx`, enterprise_admin-only visible, calling
   `POST /api/users/{id}/tenant-role`.

Consequences:
+ Real backend enforcement — a capability-only user cannot bypass it via direct API calls,
  unlike the page-slug `Permissions` mechanism this extends alongside
+ Zero behavior change for every existing role on every untouched controller — additive,
  OR-composed with the current `RequireRole` arrays; template edits propagate to every
  assignee automatically, bounded by the same JWT delay as ADR-019
+ `legal_entities.manage` reuses the existing key — one definition, two grant paths
- New per-action policy surface (~10 policies) instead of one blanket class-level gate on
  7 controllers — more `AppPolicies.Configure` entries, but each a narrow, auditable OR
- Two role-hierarchy mechanisms now compose (base `Role` rank + `TenantRoleId` capabilities)
  — mitigated by capabilities never granting rank and template management staying
  enterprise_admin-only

## ADR-019: Temporary/permanent access grants beyond role — additive layer over `User.Permissions`
Date: 2026-07-12
Status: accepted

Context: User wants to grant a user MORE access than their role gives — including two users
with the identical role diverging — either forever or until a deadline. Clarified with user
(AskUserQuestion): the existing ~15-min JWT-refresh propagation delay is acceptable (no move to
live per-request DB checks); granularity stays at the existing per-page level (`PAGES` /
`ValidPages` in `frontend/features/users/types.ts` / `UserService.cs`), no new per-action
granularity; expiry must notify the user via the ADR-018 outbox.

Today `User.Permissions` (`backend/ShelfGuard.Domain/Entities/User.cs:42`) is the only
role-independent per-user override — a `Dictionary<string,bool>?` (true=grant, false=deny,
absent=role default), always permanent, edited via `UserService.UpdatePermissionsAsync`
(`UserService.cs:251-297`, private `RoleRank` dict at line 29, mirrored on frontend as
`ROLE_RANK` in `types.ts:87`) and `PUT /api/users/{id}/permissions`
(`UsersController.cs:96`). It is baked into JWT claims at token-mint time only —
`AuthService.cs:132` (refresh) and `:326` (login) call
`_jwt.GenerateAccessToken(..., user.Permissions)`; `JwtService.cs:47-52` serializes only the
`true` entries into a comma-joined `permissions` claim, which `LegalEntityAuthorization.cs`
already reads the same way for `legal_entities.manage` — i.e. the "bake into JWT at mint time"
mechanism the user was told about already exists and is directly reusable.

Decision:
1. **New table `user_permission_grants`**, additive only — `User.Permissions` and
   `UpdatePermissionsAsync`/the PUT endpoint are untouched. Columns: `Id`, `TenantId`,
   `UserId` (recipient), `PermissionKey` (validated against the same page-slug set as
   `ValidPages`), `ExpiresAt timestamptz NOT NULL` (always temporary — permanent overrides
   keep living exclusively in `User.Permissions`, so this table never needs a `Granted bool` or
   a null-`ExpiresAt` "permanent" case), `GrantedByUserId`, `GrantedAt`, `RevokedAt?` (early
   revoke), `RevokedByUserId?`, `NotifiedExpiringAt?`, `NotifiedExpiredAt?` (worker dedupe
   markers). Standard `tenant_isolation` + `provider_bypass` RLS (pattern used by every table
   since ADR-016/017/018, e.g. `AddLegalEntities` migration). Index on `(TenantId, UserId)` and
   a partial index on `ExpiresAt WHERE "RevokedAt" IS NULL` for the worker scan. Rejected:
   folding permanent grants into the same table "for one audit trail" (per the brief) — two
   independent mechanisms with a narrow, explicit merge step is less regression risk to the
   existing, working permanent-override path than widening it.
2. **Merge happens once, at JWT-mint time**, not per request. `AuthService.cs` gets a new
   private `BuildEffectivePermissionsAsync(User, ct)`: start from `user.Permissions` (or empty),
   then for every grant with `ExpiresAt > utcNow AND RevokedAt IS NULL`, force
   `effective[PermissionKey] = true` — a temporary grant always wins over even an explicit
   permanent `false`, since it is the more specific and more recent authorization. Call this at
   both existing call sites (`:132`, `:326`) in place of `user.Permissions`, and also feed the
   same result into `ToDto`/`AuthUserDto` (`:389`) so the client's own `effectivePageAccess()`
   sidebar logic doesn't disagree with the JWT the server issued it.
3. **API — extend `UsersController`/`UserService`, no new controller.**
   `POST /api/users/{id}/permission-grants` (`permissionKey`, `expiresAt`, future-only),
   `GET /api/users/{id}/permission-grants` (active + recent, for the editor), `DELETE
   /api/users/{id}/permission-grants/{grantId}` (early revoke). Server-side authorization reuses
   the exact `RoleRank` check already in `UserService.UpdatePermissionsAsync` (editor rank >
   target rank, no self-grant, target must be same tenant) — same rule, same table, just called
   from the new methods too.
4. **Worker job `worker/src/jobs/permission-grant-expiry.job.ts`**, cron every 15 min (matches
   the JWT refresh cadence already accepted as the propagation delay). Two scans: expiring within
   24h (`NotifiedExpiringAt IS NULL`) and already expired (`NotifiedExpiredAt IS NULL`), both
   `RevokedAt IS NULL`. Each match inserts one outbox row into `notification_queue`
   (`Channel="system"`, `Status="pending"`, `EventType = "access.temporary_expiring_soon"` /
   `"access.temporary_expired"`) — same shape as `ReceiptService.EnqueueReceivedNotificationAsync`
   — then stamps the corresponding `Notified*At`. New event types added to `ValidEventTypes` in
   `NotificationService.cs:96-109`.
5. **`notification-dispatch.job.ts` needs one new capability**: it currently only does
   role-matrix fan-out (`DISPATCH_EVENT_ROLES`) and doesn't even `SELECT "UserId"` from the
   intent row. This notification is for one specific person (whose access is expiring), not a
   role broadcast — the outbox row must set `UserId = grant.UserId`, and the job needs a new
   branch: when `row.user_id` is present, skip the role matrix and deliver straight to that user
   (their own `notification_settings` for the event type still apply), then mark dispatched.
6. **Frontend**: `UserPermissionsEditor.tsx` gets a second, separately-applied section —
   temporary grants are NOT part of the existing tri-state Save-all-pages flow (different
   backing store, different lifecycle, instant-apply is more honest than batching two mechanisms
   behind one button). Add "Тимчасово до…" alongside the existing grant action, plus a list of
   active grants with a revoke button. New hooks alongside `useUsers.ts`; new
   `TemporaryGrantDto` in `types.ts`. New labels for the two event types in
   `frontend/features/notifications/types.ts` (`EVENT_TYPE_LABELS`, `EVENT_TYPE_SOURCE`,
   `NotificationEventType` union).

Consequences:
+ Zero regression risk to the existing permanent-override path — it is untouched
+ Reuses three already-accepted mechanisms end to end: JWT-bake merge point, ADR-018 outbox, RoleRank check — no new authorization model
+ 15-min worst-case propagation is already the accepted norm for `legal_entities.manage`
- `notification-dispatch.job.ts` needs a genuinely new (if small) code path for single-user targeted delivery, not just a new matrix entry
- Two independent per-user permission mechanisms (dict + table) to reason about instead of one — mitigated by the merge being a single, well-documented function

## ADR-018: Notification categories expansion + filter drawer — Postgres outbox instead of C# BullMQ producer
Date: 2026-07-12
Status: accepted

Context: `notifications` page only surfaces `weekly_report` in practice (expiry/IoT alerts exist
but `iot.temp_alert`/`iot.offline` have no frontend label — display bug). User wants 4 new
categories (надходження, поповнення/AI order, повідомлення постачальника, підписання документів)
with full triggers, plus a collapsible filter drawer (search/employee/category/date/store).
Today's delivery pipeline: `worker/src/jobs/notification.job.ts` (BullMQ "notifications" queue)
resolves role-based recipients + `notification_settings`, delivers via `deliver()`, and is the
only writer of real `NotificationQueue` history rows (`logNotifications`, one row per
user×channel, `Status` = sent/skipped/failed). `expiry-check.job.ts`/`mqtt-listener.ts` are
BullMQ producers, both in Node. `ai-order.job.ts` bypasses this pipeline entirely — it calls
`sendTelegramMessage` directly, no settings check, no history row. Backend (ASP.NET Core) has
**no** existing Redis/BullMQ producer (`grep` for `StackExchange.Redis`/`bullmq` under
`/backend` — zero hits) — the three new backend-originated triggers (receipt received, supplier
chat message, agreement signed) have no way to reach the worker's delivery logic today.

Decision:
1. **Backend-originated events use a Postgres outbox, not a new C# BullMQ producer.** Adding a
   BullMQ-compatible job producer in .NET (matching BullMQ's Lua-script job format) is new
   cross-language infra for 3 call sites. Instead, the triggering C# service inserts one
   broadcast-intent row directly into `NotificationQueue` (`UserId = null`, `Channel = "system"`,
   `Status = "pending"`) via `INotificationRepository` — reuses the existing table, no new
   dependency. A new worker cron `notification-dispatch.job.ts` (poll every 1 min, same shape as
   `fiscalization-retry.job.ts`) selects `Status = 'pending' AND Channel = 'system'` rows,
   resolves recipients by role (same matrix pattern as `EXPIRY_EVENT_ROLES`) +
   `notification_settings`, delivers, writes real per-user×channel rows via the existing
   `logNotifications`, then marks the intent row `Status = 'dispatched'` (terminal, excluded from
   `GetHistoryAsync` so it never appears as a phantom "system" notification in the feed).
2. **`ai-order.job.ts` is rewired to the same in-process pattern as `handleIotAlert`** (query
   users by role → check `notification_settings` → `deliver()` → `logNotifications()`), dropping
   its direct `sendTelegramMessage` loop — it already runs in the Node worker with DB access, so
   no outbox hop is needed there, only the missing settings/history integration.
3. **`NotificationQueue` gains `StoreId Guid?` and `Title string?`.** `StoreId` backs the "by
   store" filter (repeats the `EventType.namespace.action` DB-only-hardcoded-set pattern already
   used for events/channels — no new enum table). `Title` is a short human-readable line
   (e.g. "Надійшла поставка №1234 — Хрещатик") populated by whichever service enqueues the row,
   so keyword search runs `ILIKE`/trigram against `Title` instead of parsing the `Payload` JSONB
   on every query — cheaper and matches the existing "Payload is opaque, UI parses it lazily"
   convention in `NotificationDetailDrawer.tsx`. Add `pg_trgm` GIN index on `Title` for the
   keyword filter, plus btree indexes on `(TenantId, CreatedAt)`, `(TenantId, EventType)`,
   `(TenantId, StoreId)`, `(TenantId, UserId)` for the other filters.
4. **Filter drawer is a hand-rolled overlay, not a new shadcn `Sheet`.** `components/ui/sheet.tsx`
   does not exist in this repo and `NotificationDetailDrawer.tsx` already implements a fixed-panel
   + backdrop drawer by hand — the new `NotificationFilterDrawer` follows the same pattern for
   visual/behavioral consistency rather than introducing a new shadcn primitive for one page.
5. **Filter state lives in component state + React Query key, not the URL.** No page in this repo
   currently syncs filters to `useSearchParams` (checked — zero matches under `frontend/features`
   outside auth). Introducing URL-synced filters here would be a new, unprecedented pattern for a
   single page; skip it. React Query key includes the filter object so results stay cached per
   filter combination.

Consequences: `notification.job.ts` and the new `notification-dispatch.job.ts` share the
role-matrix + settings-check + `logNotifications` pattern — worth extracting to a shared helper
in a follow-up if a 4th producer appears. `Channel = "system"` is an internal sentinel, not added
to `ValidChannels` in `NotificationService.cs` (backend inserts the outbox row directly via the
repository, bypassing the public validate path, same way the worker's `logNotifications` already
bypasses `NotificationService` entirely). `GetHistoryAsync` must filter out `Channel = 'system'`
rows so undispatched intents never leak into the UI feed.

## ADR-017: Provider nav split (Клієнти/Постачальники) + per-item категорії з JSONB attributes
Date: 2026-07-03
Status: accepted

Context: v4.1 (ADR-016) додав supplier-as-tenant. Два подальші UX/дані запити:
(A) провайдер-панель показує всіх тенантів одним списком (`ProviderService.GetTenantsAsync`,
`frontend/features/provider/`, сторінка `/provider` з табами `tenants`/`logs`) — незручно шукати
серед клієнтів і постачальників разом; (B) `SupplierItem` (marketplace listing постачальника,
не Item catalog) не має категорії — постачальник, який працює в кількох галузях (продукти,
автозапчастини, медикаменти, будматеріали), не може задати категорійно-специфічні поля
(OEM-номер, дозування/рецептурний статус, партія/термін придатності, клас сертифікації) для
кожного товару окремо.

Decision:
1. **Feature A — один список, client-side split, без нового роуту.** Сторінка `/provider`
   лишається одна; `activeTab` розширюється з `"tenants" | "logs"` на
   `"clients" | "suppliers" | "logs"`. Дані й API-виклик (`useTenants()`) без змін — фільтрація
   `business_type === "supplier"` виконується на клієнті над уже завантаженим списком (список
   тенантів невеликий, provider-only, пагінації немає). Причина проти нового бекенд-ендпоінта
   чи нового Next-роуту: нуль нових абстракцій, нуль ризику розсинхронізації лічильників
   (health-картки лишаються на весь список), TenantDetailPanel/CreateTenantWizard реюзаються
   без змін. Лічильник міняється лише в лейблі табу (`Клієнти (N)` / `Постачальники (M)`).
2. **Feature A — фільтрація по business_type, не по slug.** `platform-marketplace` (BUG-014,
   системний, IsActive=false) вже виключається на рівні `TenantRepository.GetAllAsync` — таб
   «Постачальники» бачить тільки реальні supplier-tenant-и, створені онбордингом (ADR-016 п.3/TASK-289).
3. **Feature B — категорія товару: `category` string (nullable) + `attributes JSONB (nullable)` на `SupplierItem`.**
   Обрано (b) єдину JSONB-колонку над (a) фіксованими nullable-колонками per category:
   набір категорій зростатиме (спека вже передбачає 4 старт-категорії, будматеріали/медикаменти
   реально розширяться підкатегоріями), і кожна нова категорія з підходом (a) означала б нову
   міграцію + розпухання entity. Прецедент у кодовій базі: `Item.Barcodes` — `List<string>` →
   `jsonb`, EF Core вже сконфігурований на dynamic JSON (Npgsql `EnableDynamicJson`, див.
   пам'ять проєкту); тут форма JSON я — довільний `Dictionary<string, object?>` (не List), тому
   на рівні EF — `.HasColumnType("jsonb")` + serialize/deserialize через `System.Text.Json`
   (той самий патерн, без потреби у нових Npgsql-налаштуваннях). Значення в `attributes`
   ніколи не беруть участі в SQL WHERE/JOIN (лише читання/показ у формі) — тому втрата
   SQL-запитів по конкретних полях прийнятна: категорійний пошук/фільтр (якщо колись знадобиться)
   іде через `category`, не через вміст attributes.
4. **Довідник категорій і полів живе в backend (C# const/enum + shared DTO), не тільки в
   фронтенд-мапі.** `SupplierItemCategories` (`ShelfGuard.Domain.Constants`) — фіксований
   список ключів категорій (`food`, `auto_parts`, `medical`, `construction`) + для кожної:
   список полів з `{key, label, type, required}` — **backend є джерелом істини**, бо валідація
   обов'язкових полів (медикамент без терміну придатності — invalid) має відбуватись на
   сервері, а не тільки в React-формі. Ендпоінт `GET /api/marketplace/item-categories`
   (публічний, кешується на фронті) віддає цей довідник як DTO — фронтенд не хардкодить форму,
   а рендерить її з відповіді. Це трохи важче за "фронтенд-only мапу", але усуває клас багів
   (фронт і бек розходяться в тому, що обов'язково) і дає єдине місце для розширення категорій.
5. **Зворотна сумісність.** `category` і `attributes` — нові nullable-колонки, DEFAULT NULL.
   Existing `SupplierItem` (provider-created legacy, TASK-275, і вже створені кабінетом TASK-286)
   лишаються з `category = null` — трактуються фронтом як «без категорії» (стара форма
   customName/price/minQty/unit, без динамічних полів). Валідація обов'язкових
   категорійних полів застосовується **тільки** коли `category` заданий (create/update DTO);
   `category = null` — валідний стан назавжди, не тимчасова міграційна яма.
6. **DTO shape:** `AdminAddSupplierItemDto`/`AdminUpdateSupplierItemDto`/`SupplierItemDto`
   (Cabinet-варіанти теж) отримують `string? Category` + `Dictionary<string, object?>? Attributes`.
   Немає окремих DTO per категорія — один generic shape, валідація обов'язкових полів
   виконується сервісним методом `SupplierItemCategories.Validate(category, attributes)`,
   що повертає список помилок (400 з переліком відсутніх полів).

Consequences:
+ Нова категорія (наприклад «Текстиль») — тільки зміна в `SupplierItemCategories` (C#) +
  фронтенд рендерить нову форму автоматично через API-довідник, без міграції
+ Один generic DTO/контролер-шлях для всіх категорій — мінімум нового коду в MarketplaceService/SupplierCabinetService
+ Existing товари (без категорії) не ламаються, стара форма продовжує працювати
- Не можна ефективно фільтрувати/сортувати marketplace за конкретним атрибутом (напр. "OEM-номер X")
  без повного сканування JSONB — прийнятно, бо публічний пошук сьогодні йде по `ItemName`/`Region`, не по атрибутах
- Валідація обов'язкових полів існує тільки в коді (C# + дзеркальна перевірка у формі), не в БД CHECK constraint —
  узгоджено з існуючим правилом "Validate at boundaries only"
- Provider-панель `/provider` тепер має 3 таби замість 2 — трохи вищий когнітивний навантаження, без нового роутингу

## ADR-016: Supplier self-service — supplier як окремий tenant (business_type = "supplier")
Date: 2026-07-02
Status: accepted

Context: Потрібна роль «Постачальник», який сам наповнює маркетплейс (профіль, товари) і бачить свої відгуки/рейтинг. Сьогодні marketplace-постачальників створює провайдер вручну (TASK-275, `TenantId = Guid.Empty`). Entities `Supplier/SupplierProfile/SupplierItem/SupplierMetrics/SupplierReview` вже існують з RLS `tenant_isolation` + `provider_bypass`; публічний листинг читається через provider-level DB context (`app.role = 'provider'`) з фільтром `is_public = true`.

Decision:
1. **Supplier = окремий tenant** з `business_type = "supplier"` і default-модулем `["marketplace_supplier"]`. НЕ нова роль усередині клієнтського tenant. Rationale: існуючий RLS `tenant_isolation` автоматично дає постачальнику видимість ТІЛЬКИ своїх рядків (`Supplier.TenantId` = його власний tenant), а публічний cross-tenant read маркетплейсу вже працює через provider-context + `is_public` — нових RLS-механізмів не треба.
2. **Нова app-роль `supplier_admin`** (tenant-scoped, у `AppRoles` + `roles.ts`). Юзер постачальника — звичайний User з `TenantId` = supplier-tenant, `Role = supplier_admin`. Auth/JWT без змін.
3. **Онбординг — провайдер запрошує** через існуючий Admin tenant onboarding (`business_type = "supplier"`). При створенні такого tenant автоматично створюється пара `Supplier` + `SupplierProfile` (`IsPublic = false` до заповнення). Self-registration — фаза 2.
4. **Зв'язок User ↔ Supplier — через TenantId.** Нова колонка `supplier_profiles.IsOwnerManaged bool` + partial unique index на `TenantId WHERE IsOwnerManaged` — детермінований lookup «мій профіль» (suppliers-таблиця double-duty: локальний довідник клієнтів і marketplace-записи, тому unique по TenantId неможливий).
5. **Supplier cabinet** — новий `SupplierCabinetController` (`/api/supplier-cabinet/*`), `[RequireModule("marketplace_supplier")]` + роль supplier_admin: GET/PUT профіль (+ publish toggle), CRUD товарів, read-only відгуки/метрики. Реюз логіки `MarketplaceService` (Admin*-методи параметризуються supplierId, resolved by tenant).
6. **Відгуки:** лишають тільки клієнтські tenant-и (existing `POST /api/marketplace/suppliers/{id}/reviews`; unique (supplier_id, tenant_id) вже є). Guard від накруток: reviewer tenant ≠ supplier.TenantId і `business_type != "supplier"`. Rating у `SupplierMetrics.Rating` перераховується синхронно в `CreateReviewAsync` (AVG по відгуках). Додається публічний `GET /suppliers/{id}/reviews`.
7. Існуючі provider-created suppliers (`TenantId = Guid.Empty`) лишаються як є; кабінет для них недоступний, поки провайдер не привʼяже supplier-tenant.
   > **Amendment (BUG-012, 2026-07-03):** `Guid.Empty` ніколи не працював — FK `suppliers→tenants` існував завжди, тож admin-create завжди падав 500 і рядків з `TenantId = Guid.Empty` у prod немає. Provider-created suppliers тепер привʼязуються до системного tenant «Platform Marketplace» (slug `platform-marketplace`, `business_type = supplier`, inactive, без users), який створюється ліниво в `MarketplaceRepository.GetOrCreatePlatformTenantIdAsync`. Кабінет його не бачить: профілі мають `IsOwnerManaged = false`, а лукап кабінету фільтрує `IsOwnerManaged = true`.
   > **Amendment (TASK-305, 2026-07-05, план `calm-singing-marble.md`):** компроміс BUG-012 визнано остаточно проблемним — два шляхи створення постачальника (Admin/Провайдер vs Маркетплейс/Постачальники) дублювали функціонал і залишали "напівживі" записи. Рішення: **лишити тільки шлях через `CreateTenantWizard`** (Admin/Провайдер/Постачальники), а legacy-шлях (`MarketplaceAdminController.CreateSupplier`) видаляє backend-developer окремою задачею. Дані-міграція `MigrateOrphanSuppliersToTenants` (database-engineer) переносить кожного постачальника з `platform-marketplace` на власний реальний активний tenant (`IsOwnerManaged = true`), після чого провайдер додає керівника через уже існуючий `AddTenantUserModal`. Після підтвердження, що жоден рядок більше не вказує на `platform-marketplace`, сам системний tenant і `GetOrCreatePlatformTenantIdAsync`/`PlatformTenantSlug`/`PlatformTenantName` видаляються.
   > Заодно додана ієрархія кастомних ролей команди постачальника (`supplier_roles`, tenant-scoped — на відміну від глобального `provider_roles`, кожен supplier tenant керує своїми ролями незалежно) і нова окрема сутність дошки завдань `supplier_tasks` (не привʼязана до існуючих заявок/замовлень). Обидві таблиці — стандартний RLS `tenant_isolation` + `provider_bypass`. Деталі схеми: `database-schema.md` розділ "v4.1 — Supplier tenant migration + roles/tasks".
   > **Amendment (TASK-306, 2026-07-05, backend-developer):** `MarketplaceAdminController.CreateSupplier`, `MarketplaceService.AdminCreateSupplierAsync`, `AdminCreateSupplierDto` — видалені. `GetOrCreatePlatformTenantIdAsync`/`PlatformTenantSlug`/`PlatformTenantName` (`MarketplaceRepository.cs`) НЕ видалені — `TenantRepository.GetAllAsync` досі фільтрує провайдерський список тенантів за цим slug'ом, а `MarketplaceRepositoryPlatformTenantTests` досі покриває цю поведінку; видалення відкладено до підтвердження (наступна ітерація/QA), що жоден рядок `suppliers`/`supplier_profiles` більше не вказує на `platform-marketplace` в жодному оточенні. Додано `ISupplierRolesService`/`SupplierRolesService` + `ISupplierTaskService`/`SupplierTaskService` (Application/Marketplace), CRUD endpoints на `SupplierCabinetController` (`/api/supplier-cabinet/roles`, `/api/supplier-cabinet/tasks`). `SupplierCabinetService.InviteStaffAsync` тепер приймає опційний `SupplierRoleId` — резолвиться в `Dictionary<string,bool>` через `IUserRepository` (той самий підхід, що й `ProviderTeamService`), відсутність ролі = повний доступ (без змін).

Consequences:
+ Нуль нових RLS-механізмів; ізоляція та публічний read — існуючими політиками
+ Максимальний реюз: entities, MarketplaceService, marketplace UI-компоненти
+ Онбординг = існуючий tenant onboarding + один hook
- supplier-tenant «носить» повний tenant-каркас (stores, modules), хоча використовує лише кабінет
- Подвійна семантика suppliers-таблиці лишається (локальний довідник vs marketplace) — розділення відкладено

## ADR-015: Module-based tenant activation pattern
Date: 2026-06-15
Status: accepted

Context: v4-spec вимагає, щоб кожен тенант міг активувати тільки потрібні йому модулі (Inventory, Procurement, POS, AutoService, Production, Marketplace). Поле `modules` (JSONB) вже існує на таблиці tenants (додано в TASK-074). Потрібно визначити, як модулі активуються і як API захищає модульні ендпоінти.

Decision:
1. Ключі в `tenant.modules` JSONB відповідають ідентифікаторам модулів: `"inventory"`, `"procurement"`, `"pos"`, `"auto_service"`, `"production"`, `"marketplace"`. Значення `true` = активовано.
2. Default-набір модулів при онбордингу визначається полем `business_type` (ADR-014): retail → `{inventory, procurement, pos}`, auto_service → `{auto_service, procurement}`, restaurant → `{inventory, pos, production}` і т.д.
3. На рівні ASP.NET Core додається `[RequireModule("module_key")]` attribute + відповідний `IAsyncActionFilter`, який читає `ITenantContext.Modules` і повертає `403 { error: "Module not activated" }` якщо модуль вимкнений.
4. API для управління модулями: `GET /api/admin/tenants/{id}/modules`, `PATCH /api/admin/tenants/{id}/modules` (ProviderOnly), `GET /api/settings/modules` (enterprise_admin — власний тенант). Активація/деактивація модуля не видаляє дані — тільки приховує доступ.
5. Frontend: sidebar-групи показуються/ховаються за комбінацією RBAC (роль) + модуль (активований). Хук `useModules()` читає з `/api/settings/modules`.

Consequences:
+ Один механізм для всіх модулів — легко додати новий
+ Дані ніколи не видаляються при деактивації (безпечно)
+ Provider panel повністю контролює набір модулів тенанта
- На кожен запит потрібен доступ до tenant.modules (мінімізується через ITenantContext кеш у request scope)
- UI sidebar ускладнюється (подвійна умова: роль + модуль)

## ADR-014: Platform transformation — Universal Location/Item model
Date: 2026-06-15
Status: accepted

Context: v4-spec вимагає перетворити платформу з retail-специфічної (Store, Product) на universal Business Operations Platform (Location, Item). Поточна схема: `stores`, `catalog_products`, `store_manager` role, `store_inventory`. Трансформація зачіпає 15+ таблиць, RLS policies, усі шари (DB, Domain, Application, API, Frontend, Mobile).

Decision:
1. **DB rename** (через EF Core migration): `catalog_products` → `items` (+ `item_type` column), `stores` → `locations` (+ `location_type` column), `store_zones` → `location_zones`. Роль `store_manager` → `location_manager` в AppRoles enum (UI label змінюється, значення в DB теж — UPDATE users SET role='location_manager').
2. **Поетапна міграція** (не big bang): спочатку DB + Backend, потім Frontend, потім Mobile. На кожному етапі працює production.
3. **API routes** змінюються: `/api/stores` → `/api/locations`, `/api/catalog` → `/api/items`. Для зворотньої сумісності мобільного APK — тимчасові 301-редіректи зі старих маршрутів (протягом 1 спринту, потім видаляються).
4. **Entity rename у коді**: `Store` → `Location`, `StoreZone` → `LocationZone`, `CatalogProduct` → `Item`. POC `Products`/`Product` entity видаляється разом з legacy `Products` table (давно заплановано ADR-006).
5. **business_type** додається до `tenants` table як PostgreSQL enum: `retail` (default), `auto_service`, `warehouse`, `restaurant`, `production`, `distribution`.
6. **item_type** enum: `product`, `service`, `spare_part`, `consumable`, `raw_material`, `kit`. Default: `product`.
7. **location_type** enum: `retail_store`, `warehouse`, `auto_service`, `office`, `production`, `restaurant`. Default: `retail_store`.
8. **FEFO, RLS, batch_number/expiry_date rules незмінні** — трансформація виключно в іменуванні.

Consequences:
+ Платформа відкривається для нових індустрій без зміни архітектурних патернів
+ POC Products table нарешті видаляється (ADR-006 виконується)
+ item_type дозволяє Procurement і AutoService працювати з тим самим каталогом
- Великий обсяг rename-роботи (15+ файлів backend, 20+ frontend, mobile)
- 301-редіректи потрібно прибрати через 1 спринт щоб не залишати dead code
- Тести треба оновити (entity names)

## ADR-013: Per-tenant fiscal provider config in DB, env as fallback, per-tenant IFiscalService resolution
Date: 2026-06-12
Status: accepted

Context: ADR-012 point 5 configures the Checkbox provider via deployment-level env vars (`PRRO__*`), so one process = one fiscal provider for all tenants. ShelfGuard is multi-tenant: each tenant has its own cash register (license key, cashier creds, test vs prod environment). The Claude API key already solved the same problem (TASK-058/060): per-tenant `integration_configs` row (service='claude', JSONB config, RLS) managed via «Налаштування → Інтеграції», with env (`Claude:ApiKey`) as deployment-level fallback — see `ClaudeOrderAdvisor.ResolveAsync`.

Decision:
1. Fiscal provider config moves to the same mechanism: `integration_configs` row with `service='prro'`, JSONB shape `{provider, base_url, license_key, cashier_login, cashier_password, cashier_pin_code}`. `provider` is an extensible enum: `"checkbox"` now, `"disabled"` → NoopFiscalService; future providers (direct-ДПС etc.) are new enum values, no schema change.
2. Resolution order (same as Claude key): tenant's `integration_configs` (service='prro', IsEnabled, RLS-scoped) → fallback to `PRRO__*` env vars (current ADR-012 behavior, kept for single-tenant deployments and CI) → Noop if neither configured.
3. `IFiscalService` resolution becomes per-tenant: a scoped `IFiscalServiceFactory` (Infrastructure/Integrations/Prro) reads the tenant's settings through the RLS-scoped AppDbContext and returns the matching implementation. The startup-time DI switch on `PRRO:PROVIDER` (DependencyInjection.cs) is replaced by the factory; consumers (TASK-068 POS endpoints, TASK-069 retry job) resolve through the factory, never the concrete client. `CheckboxTokenStore` must key cached bearer tokens by tenant+license key, not globally.
4. Secrets are write-only in the API: GET returns masked values (e.g. `••••` + last 4); PUT treats a masked/empty secret field as "keep existing value". This rule applies to the generic integrations endpoint too (known gap: today GET /api/integrations/{service} returns raw credentials).

Consequences:
+ Each tenant connects its own Checkbox register from the web UI — no redeploy, no shared register
+ Same UX and code path as the Claude key — one pattern to learn and audit
+ Env fallback keeps existing prod deployment and live e2e tests working unchanged
- Factory adds a DB read on the fiscal path (mitigated by per-request scoping; config row is tiny)
- Token cache becomes per-tenant — more states to reason about on credential rotation

Extends: ADR-012 (point 5 becomes the fallback layer, not the primary source).

## ADR-012: Checkbox as fiscal provider behind IFiscalService
Date: 2026-06-12
Status: accepted

Context: ADR-011 planned direct integration with the ДПС fiscal server (fs.tax.gov.ua) with our own КЕП signing. КЕП + 1-ПРРО registration is still blocked on the user, which blocks any real fiscalization. The user registered a test cash register with Checkbox (checkbox.ua) — a Ukrainian SaaS ПРРО provider (фіскальний номер TEST582378, test mode). Checkbox handles КЕП signing server-side, fiscalization, offline numbering, and ДПС submission; we talk to its REST API. Auth model: `X-License-Key` header identifies the cash register; a cashier signs in (login/password or PIN) to obtain a bearer token; receipts and shifts go through that token.

Decision:
1. Checkbox becomes the fiscal provider. ADR-011's isolation rule stands: everything Checkbox-specific (HTTP client, DTOs, auth/token handling) lives in `ShelfGuard.Infrastructure/Integrations/Prro`; the Application layer sees only `IFiscalService` and never Checkbox shapes.
2. `IKepSigner` is NOT needed for the Checkbox path — Checkbox signs documents server-side with its own КЕП. The interface stays in the codebase only if/when a direct-ДПС provider is added.
3. The offline-first rule from ADR-011 stays unchanged: sale committed locally first (pos_transaction + items + FEFO write-down in one DB transaction), fiscalization is async with a retry job; `Status = 'pending_fiscalization'` until Checkbox returns a fiscal number.
4. Provider is pluggable behind `IFiscalService`: a future direct-ДПС client (with a real KEP signer) can be added via config switch without any flow changes in Application/API/worker.
5. Config via env (secrets only in `.env`, never committed): `PRRO__PROVIDER=checkbox`, `PRRO__BASEURL` (test: `https://dev-api.checkbox.in.ua/api/v1`, prod: `https://api.checkbox.ua/api/v1`), `PRRO__LICENSEKEY`, `PRRO__CASHIER__LOGIN` / `PRRO__CASHIER__PINCODE`. License key is stored in `.claude/private/access.md`.

Consequences:
+ No ПРРО certification / КЕП burden on our side — Checkbox is already certified with ДПС
+ Demo-able today: test cash register works without waiting for КЕП / 1-ПРРО registration
+ Checkbox handles offline numbering per ПРРО rules — we don't reimplement it
+ Flow (offline-first, async fiscalization, retry job) identical regardless of provider
- Vendor dependency + per-receipt cost on the production plan
- Cashier credentials (login/PIN) still pending from the user — token flow can't be live-tested end-to-end yet

Supersedes: ADR-011 points 2 (IKepSigner/StubKepSigner) for the Checkbox path; points 1, 3, 4 remain in force.

## ADR-011: PRRO fiscal integration — isolated client, pluggable signer, offline-first
Date: 2026-06-12
Status: accepted

Context: v3 Phase 4 needs integration with the ДПС fiscal server (ПРРО). Connectivity confirmed: POST fs.tax.gov.ua:8609/fs/cmd `{"Command":"ServerState"}` → 200 unsigned. All fiscal documents (checks, Z-reports, shift open/close) must be signed with КЕП, which is not yet available (user registering 1-ПРРО). Legal flow also requires offline mode (ПРРО must keep selling when ДПС is unreachable, with offline fiscal numbers).

Decision:
1. Fiscal client lives in `ShelfGuard.Infrastructure/Integrations/Prro` only (same isolation rule as Claude API). Application layer talks to `IFiscalService`; controllers never see ДПС shapes.
2. Signing behind `IKepSigner` (`SignAsync(byte[] document)`). Until КЕП arrives, `StubKepSigner` runs the pipeline in test mode: documents get local numbers, `FiscalNumber = null`, `Status = 'pending_fiscalization'`.
3. Offline-first: every sale is committed locally first (pos_transactions + stock_events + FEFO write-down in one DB transaction); fiscalization is a follow-up step that updates FiscalNumber. A BullMQ retry job re-submits unfiscalized documents.
4. POS UI = new screens in the existing Expo app (tablet layout), not a separate app. Same auth, same API client.

Consequences:
+ Sales never blocked by ДПС availability or missing КЕП — demo-able today
+ КЕП drop-in later: implement real signer + config, no flow changes
+ Single mobile codebase
- Fiscal numbers arrive asynchronously — receipt print/SMS must handle "fiscalization pending"
- Test mode receipts are legally non-fiscal — clearly marked in UI until КЕП configured

## ADR-010: MQTT ingestion lives in the Node worker
Date: 2026-06-12
Status: accepted

Context: v3 Phase 1 needs an MQTT consumer for weight/temperature sensors (v3-spec §1, §4). Options: (a) MQTT client hosted inside ASP.NET Core API; (b) a dedicated subscriber in the existing Node worker service.

Decision: The worker subscribes to Mosquitto (`mqtt` npm package, topic `shelfguard/{tenant_id}/{store_id}/#`) and owns the full ingestion path: validate device → write temperature_readings / weight_readings → derive stock_events → enqueue notifications via the existing BullMQ pipeline. The ASP.NET API never talks to MQTT; it only serves CRUD for iot_devices and read endpoints for readings. Mosquitto runs as a docker-compose service.

Consequences:
+ Reuses the worker's existing always-on process, pg pool, notification queue, and Telegram path (same pattern as telegram-listener)
+ API stays request/response only — no hosted background services
+ Ingestion can be scaled/restarted independently of the API
- Sensor business rules (confidence, alert thresholds) live in TypeScript, not C# — acceptable: they are stream-processing rules, not request-path domain logic
- Worker now requires MQTT_URL env; local dev needs Mosquitto up for IoT features

## ADR-009: IAnalyticsRepository in Application layer
Date: 2026-06-04
Status: accepted

Context: Analytics queries return DTO aggregates (ExpirySummaryDto, LossesDto etc.), not domain entities. The IRepository pattern in Domain requires returning entities; placing IAnalyticsRepository in Domain would create a Domain → Application circular reference.

Decision: IAnalyticsRepository is defined in ShelfGuard.Application.Features.Analytics (same namespace as IAnalyticsService). Infrastructure implements it. Domain is unaware of analytics contracts.

Consequences:
+ Avoids circular dependency
+ Analytics stays as a read-model concern, cleanly separated
- Minor inconsistency: most IRepository interfaces live in Domain.Interfaces; this one does not
- Future devs must know the exception exists (documented here)

## ADR-001: BullMQ with ASP.NET Core
Date: 2026-06-03
Status: accepted

Context: v1-spec requires BullMQ for background jobs. BullMQ is Node.js-only. Main API is ASP.NET Core.

Decision: Separate /worker Node.js service. API writes to Redis via StackExchange.Redis. Worker reads via BullMQ.

Consequences:
+ BullMQ used as specified; .NET remains primary business logic layer; worker scales independently
- Extra service to maintain; Redis required in infrastructure

---

## ADR-002: Modular Monolith over Turborepo
Date: 2026-06-03
Status: accepted

Context: v1-spec mentioned Turborepo monorepo.

Decision: Single ASP.NET Core solution with feature-based modules. No Turborepo. Frontend and mobile are separate npm projects.

Consequences: + Simpler deployment. - Less isolation between modules (mitigated by strict layer rules).

---

## ADR-003: Expo SDK 56 for Mobile
Date: 2026-06-03
Status: accepted

Decision: Expo SDK 56 with Expo Router, NativeWind v4 (spec said SDK 51+, updated to latest stable).

---

## ADR-004: Port Mapping (avoid local conflicts)
Date: 2026-06-03
Status: accepted

Decision:
- Docker PostgreSQL → port 5435 (avoids conflict with local 5432)
- Docker Redis → port 6380 (avoids conflict with local 6379)
- Connection string: `Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password`

---

## ADR-005: Worker scaffold in TASK-000
Date: 2026-06-03
Status: accepted

Decision: /worker scaffold created upfront (package.json, tsconfig, Dockerfile, job stubs). Real logic in TASK-008 / TASK-017.

---

## ADR-006: Separate catalog_products table (not replacing Products)
Date: 2026-06-04
Status: accepted

Context: TASK-002 (full schema) needed to add the v1 tenant-aware `products` table from the spec. The POC `Products` table (EF Core default name = "Products", no tenant_id) already exists and powers the catalog API.

Decision: Create new `catalog_products` table (EF entity `CatalogProduct`) for the v1 tenant-aware product catalog. Keep legacy `Products` table intact until TASK-003b migrates the catalog API.

Consequences:
+ No breaking change to existing catalog API
+ Full schema deployed without disrupting running dev environment
- Two product tables exist temporarily; devs must know which one to use
- `product_stock` references `catalog_products`, not legacy `Products`

Supersedes: nothing — this is additive.

---

## ADR-007: Dashboard data from POC Products (temporary proxy)
Date: 2026-06-04
Status: accepted (temporary)

Context: Dashboard stat cards (Safe/Warning/Critical/Expired) require real `product_stock` batch data with expiry dates. That endpoint does not exist yet.

Decision: Derive dashboard stats from POC `/api/products` using `stockQuantity vs reorderLevel` as proxy. Clearly documented as placeholder. "Expired" = stockQuantity is 0 (incorrect semantically, acceptable for demo).

Superseded by: TASK-011 + TASK-016 (real analytics endpoint from `product_stock`).

---

## ADR-008: RLS column names must be double-quoted
Date: 2026-06-04
Status: accepted

Context: EF Core creates columns with PascalCase names (e.g., `"TenantId"`). PostgreSQL folds unquoted identifiers to lowercase. Raw SQL in RLS policies using `tenant_id` (unquoted) throws `column "tenant_id" does not exist`.

Decision: All column references in manually-written RLS SQL must be double-quoted to match EF Core's PascalCase: `"TenantId"`, `"Id"`, `"StoreId"`, etc.

Rule: applies to all `migrationBuilder.Sql()` calls that reference column names.
