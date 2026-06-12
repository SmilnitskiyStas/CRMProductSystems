# Architecture

**Owner:** project-architect
**Updated:** 2026-06-04

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

| Module | Status | Responsibility |
|---|---|---|
| Auth | ✅ implemented | JWT, roles, tenant context, refresh tokens |
| Inventory | ⚠️ POC only | POC product catalog (legacy `Products` table) |
| Catalog | ✅ implemented | Tenant-aware product catalog (`catalog_products`), MOQ/USQ |
| Shelf / Stock | ✅ implemented | FEFO batches, expiry statuses, suggestions, fefo-consume |
| Suppliers | ✅ implemented | Supplier CRUD |
| Stores / Zones | ✅ implemented | Store CRUD, zone CRUD, floor-plan |
| Receipts | ✅ implemented | Stock receiving documents, pre-populated workflow |
| Transfers | ✅ implemented | Store-to-store transfers, FEFO-safe copy |
| WriteOffs | ✅ implemented | Write-off documents, approve/reject |
| Movements | ❌ missing | Audit trail read endpoint (TASK-021) |
| Discounts | ❌ missing | Discount CRUD + approve/cancel (TASK-022) |
| Users / HR | ❌ missing | User management, invite, activity log (TASK-023) |
| Notifications | ❌ missing | Settings API + history (TASK-024) |
| Analytics | ✅ implemented | Expiry summary, write-offs, movements, by-zone, by-category, losses |
| Provider | ✅ implemented | Tenant list, health (stub) |

## Background Worker (BullMQ)
Separate Node.js service at `/worker`. Communicates with the API via Redis queues. See ADR-001.

| Queue | Trigger | Status |
|---|---|---|
| `expiry-check` | Cron: every hour | ✅ implemented (TASK-033) |
| `notifications` | Queue (pushed by expiry-check) | ✅ implemented (TASK-033) |
| `weekly-report` | Cron: Sunday 08:00 | ✅ implemented (TASK-040) — per-tenant summary, Telegram + email (email skipped until RESEND_API_KEY) |
| `cleanup` | Cron: daily 03:00 | ✅ implemented (TASK-040) — archive sold_out >30d; purge notification_queue 90d, stock_events/activity_logs 180d |

## AI Integration
All AI/ML logic isolated in `ShelfGuard.Infrastructure/AI`. Application layer calls through interfaces — never directly through provider SDKs. Status: **v2.0 — not started**.

## Multi-Tenancy
Row Level Security (RLS) on every tenant table. Tenant isolation enforced at the database level via `app.tenant_id` PostgreSQL session variable, set by `TenantConnectionInterceptor` on every DB connection open.

RLS pattern: see `database-schema.md`.
ADR: see ADR-008 (column names must be double-quoted).

## Current Infrastructure State
| Component | Status | Notes |
|---|---|---|
| PostgreSQL (Docker) | ✅ running | Port 5435, DB: crm |
| Redis (Docker) | ✅ running | Port 6380 |
| Backend API | ✅ running | dotnet run, port 5000 |
| Frontend | ✅ running | npm run dev, port 3000 |
| Worker | ⚠️ scaffold | stubs only, not required for v1 dev |
| Mobile | 🕐 not started | Expo SDK 56, pending TASK-020+ |

## Decisions
See `.claude/docs/decisions.md` for full ADR log (ADR-001 through ADR-008).
