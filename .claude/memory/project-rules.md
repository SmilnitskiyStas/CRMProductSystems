# Project Rules

**Project:** ShelfGuard
**Last updated:** 2026-06-03

## Non-negotiable Rules
1. FEFO is sacred — any stock consumption must pick the batch with the nearest expiry_date
2. expiry_date and batch_number never change on stock transfer
3. tenant_id always from JWT, never from request body
4. RLS on every table with tenant data
5. Business logic in Application layer, never in controllers
6. AI (Claude API) only in Infrastructure/AI — never in Application or Domain

## Stack is locked
- Backend: ASP.NET Core 8 / C#
- Frontend: Next.js 14 / React / TypeScript
- Mobile: Expo SDK 56 / React Native
- Queue: BullMQ (Node.js worker) + Redis
- Database: PostgreSQL 16
- AI: Claude API (claude-sonnet-4)

## Architecture is locked
- Modular monolith (no microservices without explicit decision)
- Feature-based frontend (no flat component directories)
- React Query for server state (no manual fetch/useState for API data)

## Port Mapping (local dev)
- API: 5000
- Frontend: 3000
- PostgreSQL: 5435 (Docker) — local postgres on 5432
- Redis: 6379
