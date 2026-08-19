# Pre-Launch Readiness — Go / No-Go

**Owner:** documentation-writer + project-manager
**Created:** 2026-07-16 (Block 19, final block of the pre-launch audit `eager-pondering-tower.md`)
**Scope:** synthesis of the full 20-block audit (Blocks 0–18 + this block), TASK-350..372.

---

## Executive summary

**Verdict: NO-GO as of today — but a clear, short path to GO.** The audit found and fixed a
large batch of genuinely launch-blocking defects (cross-tenant RLS data leak, non-functional
mobile app for staff, POS fiscalization that never ran, worker crons crashing on every run,
privilege escalation, oversell race). The product is now in far better shape than it was when the
audit started — **but every one of those fixes lives only on dev/staging and is not even committed
to git yet. Production still runs the entire pre-audit codebase with all the found bugs.** The
launch blocker is therefore not "more code to write" — it is **commit → deploy → verify** the audit
changes to production, plus three concrete confirmations (prod RLS role, mobile on a real device,
run the new migrations). Once those are done, the product is ready for first real customers, with a
short list of accepted risks below.

---

## What was checked (19 blocks, one line each)

| Block | Area | Headline outcome |
|---|---|---|
| 0 | Staging + audit tooling | Stood up `docker-compose.staging.yml`, vitest/k6/CVE-scan base; fixed KI-006 (seed guarded to dev/staging only). |
| 1 | Auth & Access Control | Already hardened; no P0/P1; closed KI-005 (hardcoded seed hash), explicit `[AllowAnonymous]`. |
| 2 | Multitenancy / RLS | **P0: `tenant_isolation` was fail-open on 60 tables** — unauthenticated/RESET state returned all tenants' rows. Fixed 57; 6 tables also had guards silently skipped. |
| 3 | Inventory / Stock / FEFO | FEFO + transfer immutability correct; fixed a real N+1 in stock suggestions; KI-008 confirmed already resolved. |
| 4 | Receipts / Transfers / Write-offs | **P0: approved write-offs never deducted stock** (mobile's only write-off path). Fixed + FK/tenant index gaps. |
| 5 | Orders / Buffer / ADU | Formulas match v2-spec; fixed MOQ/USQ rounding-ladder deviation (could order below need). |
| 6 | POS & Fiscalization (ПРРО) | **3× P0: online fiscalization never ran inline; oversell race (no optimistic lock); barcode scan crashed on real Postgres.** All fixed + cash reconciliation built. |
| 7 | AI Orders / Assistant | **P0: nightly AI-order + weather worker jobs queried the renamed `stores` table** — never produced a single suggestion. Fixed + N+1 + Claude timeout. |
| 8 | Suppliers / Marketplace | **P1: supplier custom roles were UI-only** — invited staff had full API access (self-escalation). Backend enforcement added. |
| 9 | CRM / HR / Notifications | **2× P0: hourly expiry-notification cron crashed every run** (pre-rename tables); `notification_settings` RLS fail-open. Fixed + shift-overlap + validation. |
| 10 | Auto-service / Production | **P1: Production used a fake `AddYears(10)` expiry** when shelf-life unset, defeating FEFO. Fixed; KI-018 (auto-service location scoping) planned. |
| 11 | IoT / Weather / Events / Cannibalization | **P0 (KI-016): worker jobs referenced pre-rename `StoreId` column + never set `app.role='worker'`.** Fixed + MQTT sensor sanity bounds. |
| 12 | Provider / Admin / ServiceDesk / Chat | **P0: `provider_admin` could self-escalate to the owner `provider` role.** Fixed + `chat_messages`/`support_messages` RLS enabled; orphaned Support feature retired (TASK-365). |
| 13 | Frontend cross-cutting | Added error boundaries; confirmed KI-004 resolved; stood up vitest (0→48 tests); flagged KI-020 (Sentry) + KI-021 (localStorage token). |
| 14 | Mobile | **3× critical: role gates used non-existent role names (app unusable for staff), `locationId` never populated (blocked create flows), `user` not restored on cold start.** All fixed (KI-024/025/026). |
| 15 | Cross-cutting duplication / dead code | Dead `Store`/`StoreZone` code, Claude-advisor duplication, unused endpoints (`SuppliersController` etc.) catalogued for cleanup. |
| 16 | DB performance | Systemic index sweep of 76 FORCE-RLS tables; fixed 2 real full-scans (`chat_sessions`, `supply_schedules`); over-indexing deliberately avoided. |
| 17 | Load testing | k6 scenarios (login-storm, 40-register POS queue, bulk orders, analytics); POS optimistic-locking verified zero-oversell under load; batched 3× `SaveChanges` in login. |
| 18 | Security / pentest | OWASP pass clean (SQLi/XSS/auth/2FA/RBAC/secret-masking); **P0 (KI-027): staging DB role was a superuser → RLS fully bypassed** (fixed on staging+dev, TASK-372); CVE remediation. |
| 19 | Readiness (this doc) | Go/no-go synthesis + stale-doc refresh. |

---

## Critical findings FIXED during the audit

The product **was in materially worse shape before this audit.** These are the launch-critical
defects found and closed. All fixes are applied on dev/staging (see the deploy blocker below).

### P0 — critical (data leak / money / core flow broken)
- **Cross-tenant data leak via fail-open RLS (Block 2):** `tenant_isolation` on 60 tables returned
  *all* tenants' rows when `app.tenant_id` was unset. Fixed to fail-closed (57 tables;
  `20260714180000`). Six more tables had their guard silently skipped (`20260714100000`).
- **Staging DB connected as a Postgres superuser (Block 18, KI-027):** superusers bypass RLS
  unconditionally — live cross-tenant IDOR reproduced on staging. Fixed on staging **and dev** via a
  dedicated non-superuser owner role; a startup canary (KI-028) now refuses to boot outside
  Development if the connected role can bypass RLS.
- **POS online fiscalization never ran inline (Block 6):** it executed on a detached `Task.Run` using
  an already-disposed request scope — every "instant fiscal receipt" silently fell back to the 5-min
  retry job. Now runs inline (8s bounded).
- **POS oversell race (Block 6):** `ProductStock.Quantity` had no optimistic concurrency — two
  concurrent sales of the last unit both succeeded. Fixed with an `xmin` token → clean 409
  (`20260715054917`), verified zero-oversell under 40-register load (Block 17).
- **POS barcode scan crashed on real Postgres (Block 6):** `GetByBarcodeAsync` threw
  `cannot cast text[] to jsonb` — core scanning could not have worked in prod (masked by in-memory
  test fakes). Fixed via `EF.Functions.JsonContains`.
- **Approved write-offs never touched stock (Block 4):** `WriteOffService.ApproveAsync` skipped
  deduction when `productStockId` was null — which is exactly what the mobile app (the only write-off
  UI) sends. Every real write-off showed `approved` but never adjusted `product_stock`. Fixed to
  FEFO-consume.
- **Hourly expiry-notification cron crashed every run (Block 9):** worker jobs queried pre-rename
  tables (`catalog_products`/`stores`/`"StoreId"`) — the entire expiry/dashboard-snapshot pipeline
  was dead. Root cause: dev worker `DATABASE_URL` had never been fixed, so no worker job had run in
  dev the whole series.
- **Nightly AI-order + weather jobs queried the renamed `stores` table (Block 7):** never generated
  a suggestion; Claude was always fed an empty weather array.
- **IoT worker jobs used pre-rename `StoreId` + never set `app.role='worker'` (Block 11, KI-016):**
  MQTT/weather writes threw or silently returned zero rows depending on connection-pool luck.
- **`provider_admin` privilege escalation (Block 12):** could grant itself the owner `provider` role
  (full tenant CRUD / impersonation) and deactivate the owner. Rank/owner checks added.
- **Mobile app non-functional for staff (Block 14):** every role gate used invented PascalCase role
  names (POS tab invisible to cashiers, manager actions invisible everywhere; KI-024);
  `user.locationId` was always `undefined`, blocking write-off/transfer/production creation (KI-025);
  `user` was never restored after a cold restart (KI-026). All fixed at the code level.
- **Unverified Telegram account linking (TASK-368):** `POST /api/auth/telegram/link` accepted a raw
  client-supplied `chat_id` with zero proof of ownership. Removed; replaced with the verified
  bot-code flow.

### P1 — high
- KI-005 hardcoded bcrypt seed hash in source (Block 1).
- Supplier custom roles had no backend enforcement (Block 8).
- `chat_messages` / `support_messages` had RLS disabled entirely (Block 12).
- Production fake `AddYears(10)` expiry defeating FEFO (Block 10).
- Schedule shift-overlap check ran only at publish, not on add/edit (Block 9).
- `notification_settings` RLS fail-open (Block 9).
- N+1s in stock suggestions (Block 3) and AI-order list (Block 7); missing tenant indexes on
  `stock_receipts`/`stock_transfers` (Block 4), `chat_sessions`/`supply_schedules` (Block 16).
- 3× sequential `SaveChangesAsync` in login batched to 1 (Block 17).
- 4 High-severity backend NuGet CVEs → 0; Next.js CVEs patched; worker/mobile High CVEs fixed (Block 18).

---

## ⚠️ LAUNCH BLOCKERS — mandatory before production

> **All audit fixes are applied to dev/staging only. Production is untouched and still runs the
> full pre-audit codebase — RLS fail-open, dead worker crons, POS race, privilege escalation,
> broken write-offs, non-functional mobile.** Production deploy was deliberately deferred by the
> user. Nothing below is optional for launch.

1. **Commit and deploy the entire audit to production.** The whole audit is currently an
   **uncommitted working tree** — the new migrations, `RlsRoleGuard.cs`, load tests, staging compose,
   and every modified controller/service/worker file are untracked/modified in git, not yet on `main`.
   Commit, push, and run the standard prod deploy (`deploy.sh`). Until then production has received
   *zero* of the ~16 P0 fixes above.

2. **Run the new EF migrations on prod** (all created 2026-07-14/15, applied only to dev so far):
   - `20260714100000_FixMissingRlsGuardsAndProviderBypass`
   - `20260714180000_FixFailOpenTenantIsolationOnReset` ← the cross-tenant fail-open fix
   - `20260714210933_AddStockReceiptsTransfersTenantIndexes`
   - `20260715054917_AddProductStockXminConcurrencyToken` ← POS oversell fix
   - `20260715120000_FixNotificationSettingsRlsFailOpen`
   - `20260715153812_AddChatAndSupportMessagesRls`
   - `20260715180053_AddActivityLogsIndexesAndDropSupersededStockIndexes`
   - `20260715204612_AddChatSessionsAndSupplySchedulesTenantIndexes`
   - **Decision required before deploy:** `20260714150000_ExpandProviderBypassToProviderAdmin` also
     exists in the tree but was **never applied to any DB** — it awaits the user's explicit go-ahead
     (it broadens `provider_bypass` from `provider` to `provider_admin` across 71 tables). If it stays
     committed, `MigrateAsync` will apply it automatically on deploy. Decide keep-and-apply vs
     remove-from-tree **before** the deploy, don't let it ride in silently.

3. **Re-verify production's Postgres role — do NOT assume (KI-027/KI-028).** Production was not
   touched this session. Memory says prod already switched from a superuser to a non-superuser
   `shelfguard_app` role once — but that is an *assumption*, not a checked fact, and staging shipped
   without the same fix (that's exactly how KI-027 happened). **Before launch, SSH to the prod server
   and run, as the application's connection role:**
   `SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;`
   It must return `rolsuper=f, rolbypassrls=f`. If it returns `t`, **all of production's RLS is inert**
   and the cross-tenant fail-open fix (blocker 2) buys nothing — apply the KI-027 role fix on prod
   first. The new startup canary (blocker 1) will also fail-fast on boot if the prod role is wrong,
   which is a second safety net — but verify explicitly rather than relying on the canary catching it
   post-deploy.

4. **Test the mobile app end-to-end on a real device.** The three critical mobile fixes (KI-024/025/026)
   were verified at the code/contract level only — **there was no emulator or device in the audit
   environment**, and the web target isn't installed so even `expo start --web` couldn't render. Build
   the APK locally (per the documented `gradlew assembleRelease` workflow) and manually walk the
   critical flows before release: cashier login → POS scan → cart → payment → receipt; delivery
   receive; quick write-off; transfer confirm; and confirm role-gated UI now actually appears for
   real staff accounts.

---

## Open items needing a USER DECISION (not launch blockers, but decide soon)

| Ref | Item | Scope |
|---|---|---|
| KI-020 | No frontend error tracking (Sentry). Error boundaries log to console only. | User must create a Sentry project + provide a DSN; code wiring is small after that. |
| KI-021 | Access token in `localStorage` (XSS blast-radius). | Choose: (a) accept, (b) add CSP header, or (c) full bootstrap-refresh rewrite to drop localStorage. |
| KI-015 | POS shift-open is per-tenant, not per-store — blocks simultaneous multi-store POS. | Plan written; needs schema migration + multi-register Checkbox setup. Decide if worth it now. |
| KI-018 | Auto-service spare-part FEFO write-down is tenant-wide, not location-scoped. | Plan written (~1 day, additive). Real cross-location leak for auto-service chains. |
| KI-019 | Most v2/v3 controllers have no `[RequireModule]` gate (billing/entitlement gap, not a leak). | Adding it blind would 403 working tenants; needs a product call on default module sets + backfill. |
| KI-023 | Mobile 2FA login is implemented (TOTP + recovery code); device acceptance is pending. | Run live Android TOTP and one-time recovery-code verification when TASK-435 is unblocked. |
| KI-017 | `needs_verification` stock status never triggers its cron notification. | Small schema + worker task. |
| — | Cooperation-flow controller (agreements/orders/contract-settings) has no fine-grained permission key. | Needs a permission-taxonomy product decision (Block 8). |
| — | `SuppliersController` full CRUD + several coefficient CRUD endpoints (Discounts/Cannibalization/SupplySchedules/Weather) have no UI. | v2-spec tuning knobs built backend-first; pre-launch product gap, not a code bug (Block 15). |
| — | Dead `Store`/`StoreZone`/`StoreService` code; Claude-advisor duplication; POS sale over-fetches stock unscoped by store; `OpenShiftDialog` stale-state; Telegram link writes no `activity_logs` row. | Low-severity cleanups flagged as background tasks (Blocks 15/17/6/368). |

---

## Accepted risks (deliberately left as-is for launch)

- **KI-014 — per-IP rate limiting ineffective in production.** The hosting provider's port-mapping
  doesn't preserve client IPs, so per-IP partitions never accumulate. **Mitigations are live and
  IP-independent and were re-verified end-to-end in Block 18:** per-account lockout (5 fails → 15 min,
  covers both password and TOTP brute force) and password policy. Full fix requires a provider change
  (PROXY protocol / X-Forwarded-For) or an edge like Cloudflare — out of our stack.
- **KI-022 — mobile has no offline support.** No local queue/draft persistence; a dropped connection
  mid-action loses the in-progress cart/scan. Block 6's POS optimistic locking at least makes a manual
  *retry* safe. Offline-first is a substantial dedicated effort; deferred pending a priority decision.
- **KI-028 — single-object reads trust RLS as the sole tenant filter** (by design, per CLAUDE.md's
  "trust RLS" architecture). Mitigated by the new startup canary; deeper `&& TenantId==` defense-in-depth
  left as optional future hardening.
- **Test coverage is intentionally partial** — critical paths (money/FEFO/tenant isolation) are
  covered; frontend/mobile UI rendering and full integration coverage are not.
- **Remaining CVEs** (Next.js/ESLint/Vitest/Expo transitive) require major-version bumps; documented,
  not forced, per Block 18's patch/minor-only mandate.
- **KI-007/009/010/011** — dashboard POC placeholder data, missing `staleTime`, static store-map
  zones, "coming soon" pages — low-severity/known, unchanged.

---

## Metrics

- **Backend tests:** 854/854 green (`dotnet test`, as of TASK-372) — up from 805 at the start of
  Block 1; the audit added regression tests for every P0/P1 it fixed.
- **Frontend tests:** 48/48 green (`npx vitest run`, 6 files) — the frontend went from **0 tests** to
  a covered `lib/` (api state machine, roles, permissions) in Block 13.
- **Bugs found & fixed:** ~16 P0/critical and ~12 P1/high across Blocks 1–18 (see the fixed-findings
  section) — plus CVE remediation (backend 4 High → 0; Next.js/worker/mobile High CVEs cleared).
- **Migrations added:** 8 functional EF migrations applied to dev (+1 decision-gated, not applied).
- **Known issues:** ~11 resolved during the audit (KI-004/005/006/008/016/024/025/026/027/028, plus
  the pre-audit KI-013); ~13 remain open — of which only the deploy-related blockers above gate launch,
  the rest are accepted risks or user decisions.

---

## Bottom line

Do the four launch blockers — **commit + deploy to prod, run the migrations, verify the prod DB role,
device-test mobile** — and ShelfGuard is ready for its first real customers. Skip any of them and prod
keeps running the pre-audit bugs the audit exists to have caught. The open decisions and accepted
risks above can be worked in parallel or shortly after launch; none of them block go-live.
