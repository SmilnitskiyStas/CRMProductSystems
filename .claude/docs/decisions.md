# Architecture Decisions (ADR Log)

**Owner:** project-architect
**Updated:** 2026-06-03

## ADR-001: BullMQ with ASP.NET Core
Date: 2026-06-03
Status: accepted

Context:
v1-spec requires BullMQ for background jobs (expiry-check, notifications, weekly reports).
BullMQ is Node.js-only. Main API is ASP.NET Core.

Decision:
Separate /worker Node.js service. API writes to Redis via StackExchange.Redis.
Worker reads via BullMQ. Communication is async through Redis queues.

Consequences:
+ BullMQ used as specified
+ .NET API remains primary business logic layer
+ Worker can be scaled independently
- Extra service to maintain
- Redis required in infrastructure

## ADR-002: Modular Monolith over Turborepo
Date: 2026-06-03
Status: accepted

Context:
v1-spec mentioned Turborepo monorepo. Team decided to use modular monolith.

Decision:
Single ASP.NET Core solution with feature-based modules.
No Turborepo. Frontend and mobile are separate npm projects.

Consequences:
+ Simpler deployment and development
+ Single codebase for all backend logic
- Less isolation between modules (mitigated by strict layer rules)

## ADR-003: Expo SDK 56 for Mobile
Date: 2026-06-03
Status: accepted

Context:
v1-spec mentioned Expo SDK 51+. Updated to SDK 56 (latest stable).

Decision: Expo SDK 56 with Expo Router, NativeWind v4.

## ADR-004: Port Mapping
Date: 2026-06-03
Status: accepted

Context:
Local PostgreSQL installed on port 5432 conflicts with Docker.
Local Redis may be installed on port 6379.

Decision:
Docker PostgreSQL mapped to port 5435.
Docker Redis mapped to port 6380.
Connection string: Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password
Redis URL (local dev): redis://localhost:6380

## ADR-005: Worker scaffold created in TASK-000
Date: 2026-06-03
Status: accepted

Context:
BullMQ worker (ADR-001) requires a Node.js /worker service.
Docker Compose and the backend need to know queue names and service structure before TASK-008 implements real job logic.

Decision:
/worker scaffold (package.json, tsconfig.json, Dockerfile, src/index.ts, queues/index.ts, all four job stubs) is created in TASK-000.
Real job logic (expiry status updates, notification dispatch) is implemented in TASK-008 and TASK-017.

Consequences:
+ docker-compose.yml can include the worker service from day one
+ backend-developer knows queue names (expiry-check, notifications, weekly-report, cleanup) before implementing producers
+ clean placeholder pattern — each job stub has a TODO comment pointing to the implementing task
- Small overhead in TASK-000 scope
