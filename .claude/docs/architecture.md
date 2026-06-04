# Architecture

**Owner:** project-architect
**Updated:** 2026-06-03

## Approach

Modular monolith. Services are split only when there is a concrete operational reason (independent scaling, separate deployment cadence, team boundary). Avoid premature distribution.

## Layer Responsibilities

| Layer | Responsibility |
|---|---|
| `ShelfGuard.Api` | HTTP routing, request/response mapping, auth middleware — no business logic |
| `ShelfGuard.Application` | Use cases, business rules, DTOs, validation |
| `ShelfGuard.Domain` | Entities, value objects, domain events, repository interfaces |
| `ShelfGuard.Infrastructure` | EF Core, repositories, PostgreSQL, external services, AI integrations |

> Note: backend projects are currently named `CRM.*` — rename is tracked in TASK-001.

## Domain Modules

| Module | Responsibility |
|---|---|
| Inventory | Product catalog, stock levels, warehouse locations |
| Shelf | Expiry tracking, FEFO batches, statuses, suggestions |
| Suppliers | Supplier catalog, purchase orders, lead times |
| Transfers | Store-to-store stock movements |
| WriteOffs | Write-off documents and audit |
| Notifications | Settings, delivery queue |
| Auth | JWT, roles, tenant context |
| Analytics | Reports, summaries |

## Background Worker (BullMQ)

Separate Node.js service at `/worker`. Communicates with the API via Redis queues.
See ADR-001 and ADR-005.

| Queue | Trigger | Handler |
|---|---|---|
| `expiry-check` | Cron: every hour | Update batch statuses, enqueue notifications |
| `notifications` | Queue (pushed by API) | Send Telegram / push / email |
| `weekly-report` | Cron: Sunday 08:00 | Generate and send weekly analytics |
| `cleanup` | Cron: daily 03:00 | Archive old events and logs |

## AI Integration

All AI/ML logic is isolated in `ShelfGuard.Infrastructure/AI`. The application layer calls AI services through interfaces — never directly through provider SDKs. This keeps provider details replaceable without touching business logic.

## Multi-Tenancy

Row Level Security (RLS) on every tenant table. Tenant isolation enforced at the database level via `app.tenant_id` PostgreSQL session variable set by `TenantInterceptor` middleware.

## Decisions

See `.claude/docs/decisions.md` for full ADR log.
