# Architecture

**Owner:** project-architect
**Updated:** 2026-06-04
**Last reviewed:** 2026-07-16 (pre-launch audit) — module/worker/infra status tables below refreshed to reality; v1→v4 all shipped.

## Approach
Modular monolith. Services are split only when there is a concrete operational reason (independent scaling, separate deployment cadence, team boundary). Avoid premature distribution.

## Layer Responsibilities

| Layer | Responsibility |
|---|---|
| `ShelfGuard.Api` | HTTP routing, request/response mapping, auth middleware — no business logic |
| `ShelfGuard.Application` | Use cases, business rules, DTOs, validation |
| `ShelfGuard.Domain` | Entities, value objects, domain events, repository interfaces |
| `ShelfGuard.Infrastructure` | EF Core, repositories, PostgreSQL, external services, AI integrations |

## Domain Modules

> v1→v4 are all shipped; the codebase now has ~41 Application feature modules (see CLAUDE.md for the
> full list). The 2026-06-04 "❌ missing" rows below are all implemented. Two v4 renames apply
> throughout: **Store → Location** (`stores` table → `locations`, `"StoreId"` → `"LocationId"`) and
> **Product → Item** (`catalog_products` → `items`). Legacy `Products`/`ProductsController` is now
> only a `RedirectPermanent` shim to `/api/items/*` (KI-008).

| Module | Status | Responsibility |
|---|---|---|
| Auth / Users / TenantRoles | ✅ implemented | JWT, roles, refresh rotation + reuse detection, lockout, opt-in 2FA (TOTP), temporary grants (ADR-019), custom role templates (ADR-020) |
| Inventory (Items) | ✅ implemented | Tenant-aware item catalog (`items`), barcodes, MOQ/USQ; legacy `Products` = redirect shim only |
| Shelf / Stock / Locations | ✅ implemented | FEFO batches, expiry statuses, suggestions, fefo-consume, location zones |
| Suppliers / Marketplace | ✅ implemented | Supplier catalog + B2B marketplace, cabinet, cooperation, agreements, supplier chat/roles/tasks, Вчасно integration |
| Receipts / Transfers / WriteOffs | ✅ implemented | Movement documents; FEFO-safe copy; approve → real stock deduction + `stock_movements` |
| Orders / Adu / Buffer | ✅ implemented | Order formula, Average Daily Usage, CDA buffer (green/yellow/red) |
| Pos | ✅ implemented | Shifts, transactions, inline Checkbox ПРРО fiscalization + retry queue, cash reconciliation, xmin oversell guard |
| Customers / Schedules / Notifications | ✅ implemented | Customer master, employee shifts (overlap guard), alert settings + queue |
| AiOrders / AiAssistant | ✅ implemented | Claude-backed order suggestions + business advisor (isolated in Infrastructure/AI) |
| AutoService / Production | ✅ implemented | v4 industry modules, `[RequireModule]`-gated by business type |
| IoT / Weather / Events / Cannibalization | ✅ implemented | MQTT readings + alerts, Open-Meteo, demand events, promo cannibalization |
| Analytics | ✅ implemented | Expiry summary, write-offs, movements, by-zone, by-category, losses, POS analytics |
| Provider / Admin / ServiceDesk / Chat | ✅ implemented | SaaS provider panel, tenant onboarding, impersonation, support tickets, provider↔client live chat |
| Integrations / Settings / Telegram | ✅ implemented | Per-tenant ПРРО/Claude/Вчасно configs (masked secrets), module toggles, Telegram bot (verified link-code flow) |

## Background Worker (BullMQ)
Separate Node.js service at `/worker`. Communicates with the API via Redis queues. See ADR-001.

| Queue | Trigger | Status |
|---|---|---|
| `expiry-check` | Cron: every hour | ✅ implemented (TASK-033) |
| `notifications` | Queue (pushed by expiry-check) | ✅ implemented (TASK-033) |
| `weekly-report` | Cron: Sunday 08:00 | ✅ implemented (TASK-040) — per-tenant summary, Telegram + email (email skipped until RESEND_API_KEY) |
| `cleanup` | Cron: daily 03:00 | ✅ implemented (TASK-040) — archive sold_out >30d; purge notification_queue 90d, stock_events/activity_logs 180d |
| `ai-order` | Cron: 05:00 | ✅ implemented — Claude API → order suggestions (Block 7 fixed the `stores`→`locations` query bug) |
| `weather-fetch` | Cron: 06:00 | ✅ implemented — Open-Meteo → `weather_data` (Block 7/11 fixed table/column + `app.role` bugs) |
| `fiscalization-retry` | Cron: */5 min | ✅ implemented — poll Checkbox pending receipts |
| `mqtt-listener` | MQTT subscribe `shelfguard/#` (not a queue) | ✅ implemented (TASK-064, ADR-010) — readings (sanity-bounded), FEFO write-down, temp/offline alerts |
| `telegram-listener` | Telegram bot | ✅ implemented — `/start <code>` verified account linking, `/status /critical /tasks` |

> **Worker RLS rule (post-audit):** every worker DB job MUST `SET app.role = 'worker'` before
> querying — the Block 2 fail-closed RLS fix means jobs that skip it silently read zero rows. All
> jobs were audited/fixed in Blocks 2/7/9/11.

## AI Integration
All AI/ML logic isolated in `ShelfGuard.Infrastructure/AI` (`ClaudeOrderAdvisor`, `BusinessAssistant`,
`SupplierAdvisor`). Application layer calls through Domain interfaces — never directly through the
Anthropic SDK. Status: **✅ shipped (v2)** — 60s client timeout, graceful error degradation, per-tenant
API key (masked on GET).

## Multi-Tenancy
Row Level Security (RLS) on every tenant table. Tenant isolation enforced at the database level via `app.tenant_id` PostgreSQL session variable, set by `TenantConnectionInterceptor` on every DB connection open.

RLS pattern: see `database-schema.md`.
ADR: see ADR-008 (column names must be double-quoted).

## Current Infrastructure State
| Component | Status | Notes |
|---|---|---|
| PostgreSQL (Docker) | ✅ running | dev 5435 · staging 6381 · prod (loopback). **App must connect as a non-superuser role** (`shelfguard_app` family) or RLS is bypassed (KI-027); startup canary enforces this outside Development (KI-028). |
| Redis (Docker) | ✅ running | dev 6380 · staging 6381 |
| Backend API | ✅ running | dev 5000 · staging 5101 · prod 5100 |
| Frontend | ✅ running | dev 3000 · staging 3101 · prod 3100 |
| Worker | ✅ running | Full BullMQ + MQTT + Telegram service; not stubs |
| Mobile | ✅ shipped | Expo SDK 56 / React Native; local APK build workflow |
| Staging | ✅ running | `docker-compose.staging.yml` (Block 0), isolated stack |
| Production | ✅ running | `agrusystems.pp.ua` (Hetzner 93.127.143.98), Docker + Nginx + Let's Encrypt. **NOTE (2026-07-16): prod has NOT yet received the pre-launch audit fixes — see `prelaunch-readiness.md`.** |

## Decisions
See `.claude/docs/decisions.md` for full ADR log (grown well past ADR-008 — through ADR-020+: temporary
access grants ADR-019, custom tenant role templates ADR-020, module activation ADR-015, etc.).
See `.claude/docs/prelaunch-readiness.md` for the go/no-go launch checklist and `known-issues.md` (KI-004..028).
