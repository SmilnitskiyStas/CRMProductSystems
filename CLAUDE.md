# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**ShelfGuard** — B2B SaaS платформа для рітейлу: продуктові магазини, мережі, квіткові магазини.
Відстеження термінів придатності (FEFO), управління залишками, AI-автозамовлення, IoT-інтеграції.
Modular monolith, multi-tenant (RLS), що масштабується до enterprise.

**Spec-файли (primary source of truth):**
- `v1-spec.md` — MVP: Shelf Manager + CRM ядро + HR + Notifications
- `v2-spec.md` — Auto Order + AI Forecasting (Claude API)
- `v3-spec.md` — IoT + CV Camera + ПРРО Каса

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Next.js · React · TypeScript · Tailwind CSS · shadcn/ui · React Query · Zustand |
| Backend | ASP.NET Core Web API · C# · modular monolith |
| Mobile | Expo SDK 56 · React Native · Expo Router · NativeWind v4 |
| Queue | BullMQ · Redis (окремий Node.js worker-сервіс для фонових задач) |
| Database | PostgreSQL · EF Core · Row Level Security (RLS) |
| AI | Claude API (claude-sonnet-4) |
| Infrastructure | Docker · Docker Compose |

## Development Commands

### Frontend (`/frontend`)
```bash
npm install        # install dependencies
npm run dev        # dev server → localhost:3000
npm run build      # production build
npm run lint       # ESLint
npm test           # run tests
```

### Backend (`/backend`)
```bash
dotnet restore     # restore packages
dotnet run --project CRM.Api         # API server → localhost:5000
dotnet build       # build solution
dotnet test        # run all tests
dotnet test --filter "FullyQualifiedName~FeatureName"  # run single test
dotnet ef migrations add <Name> --project CRM.Infrastructure --startup-project CRM.Api
```

### Mobile (`/mobile`)
```bash
npm install        # install dependencies
npx expo start     # start dev server
npx expo run:ios   # iOS simulator
npx expo run:android  # Android emulator
```

### Worker (`/worker`)
```bash
npm install        # install dependencies
npm run dev        # start BullMQ worker
```

### Infrastructure
```bash
docker compose up -d    # start PostgreSQL + Redis and services
docker compose down     # stop all services
docker compose logs -f  # tail logs
```

## Architecture

**Modular monolith.** Сервіси розбиваються тільки при конкретній операційній необхідності.

### Backend layout
```
/backend
├── ShelfGuard.Api/            # Controllers, middleware, startup — thin layer only
├── ShelfGuard.Application/    # Business logic, use cases, DTOs
│   └── Features/
│       ├── Inventory/         # Products, stock, FEFO
│       ├── Shelf/             # Expiry tracking, statuses, suggestions
│       ├── Suppliers/         # Supplier catalog, receipts
│       ├── Transfers/         # Store-to-store movements
│       ├── WriteOffs/         # Write-off documents
│       ├── Notifications/     # Notification settings, queue
│       ├── Auth/              # JWT, roles, tenant context
│       └── Analytics/         # Reports, summaries
├── ShelfGuard.Domain/         # Entities, value objects, domain interfaces
├── ShelfGuard.Infrastructure/ # EF Core, repositories, external services
│   ├── Data/                  # AppDbContext, migrations, repositories
│   ├── AI/                    # Claude API client — isolated here
│   └── Integrations/          # Telegram, email, webhooks
└── ShelfGuard.Tests/
```

### Frontend layout
```
/frontend
├── app/                       # Next.js App Router
│   ├── (auth)/
│   └── (dashboard)/
├── features/                  # One directory per domain
│   ├── inventory/             # components/ hooks/ api/ types.ts
│   ├── shelf/                 # Expiry tracking, statuses, suggestions
│   ├── suppliers/
│   ├── transfers/
│   ├── write-offs/
│   ├── analytics/
│   └── settings/
├── components/                # Shared UI components
└── lib/                       # API client, auth config, utilities
```

### Mobile layout
```
/mobile
└── app/
    ├── (auth)/
    │   ├── _layout.tsx
    │   └── login.tsx
    └── (app)/
        ├── _layout.tsx        # Bottom Tab Navigator
        ├── index.tsx          # Dashboard
        ├── scan.tsx           # Barcode scan (center tab)
        ├── stock/
        ├── receipt/
        ├── inventory/
        └── profile/
```

### Worker layout
```
/worker                        # Node.js BullMQ worker service
├── src/
│   ├── jobs/
│   │   ├── expiry-check.job.ts   # cron: every hour
│   │   ├── notification.job.ts   # queue worker + retry
│   │   ├── weekly-report.job.ts  # cron: Sunday 08:00
│   │   └── cleanup.job.ts        # cron: daily
│   ├── queues/
│   └── index.ts
└── package.json
```

## Architecture Rules

- **Thin controllers.** Business logic belongs in `ShelfGuard.Application` services, never in controllers.
- **Feature-based frontend.** Components, hooks, API calls, and types live inside their feature directory.
- **React Query owns server state.** Do not duplicate it into Zustand stores.
- **CSR for all authenticated/dashboard views.** SSR only when a page has an explicit SEO requirement.
- **AI integrations are isolated.** Claude API client, prompts, and AI logic stay in `ShelfGuard.Infrastructure/AI`. Never couple AI providers to business logic.
- **Validate at boundaries only.** API endpoints, external integrations, user input. No redundant internal re-validation.
- **FEFO is sacred.** Any stock consumption must use FEFO logic — always take the batch with the nearest expiry_date.
- **Tenant isolation via RLS.** Every table with tenant data must have `tenant_id` and a corresponding PostgreSQL RLS policy.
- **expiry_date and batch_number never change on transfer.** These fields are copied as-is when stock moves between locations.

## Multi-Agent Workflow

This repository uses a structured multi-agent system. Agents are defined in `.claude/agents/`.

### Agent Responsibilities

| Agent | When to invoke |
|---|---|
| `project-manager` | Task creation, status tracking, sprint coordination |
| `project-architect` | Architecture decisions, breaking requirements into tasks |
| `backend-developer` | API endpoints, services, domain logic, backend tests |
| `frontend-developer` | Pages, components, forms, API integration (Next.js / React) |
| `mobile-developer` | Expo screens, components, navigation, API integration (Expo SDK 56 / RN) |
| `database-engineer` | Schema design, migrations, indexes, RLS policies |
| `qa-tester` | Test plans, checklists, regression testing |
| `security-reviewer` | Auth, permissions, input validation, sensitive data |
| `devops-engineer` | Docker, CI/CD, environment configuration |
| `documentation-writer` | Docs, API contracts, architecture summaries |

### Agent Workflow (all agents must follow)

1. Load context: read `CLAUDE.md` → relevant `v*-spec.md` → `.claude/docs/` → `.claude/tasks/current.md`
2. Check task dependencies before starting
3. Implement only assigned responsibilities
4. Create task log in `.claude/logs/tasks/`
5. Create handoff in `.claude/logs/handoffs/` if next agent needed
6. Update task status in `.claude/tasks/`
7. Update `.claude/docs/` if architecture or domain behavior changes

### Task ID Format
```
TASK-001, TASK-002, ...
```

### Task Log File Format
```
.claude/logs/tasks/TASK-ID_YYYY-MM-DD_short-description_agent.md
Example: .claude/logs/tasks/001_2026-06-03_products-api_backend-developer.md
```

### Task States
`planned` → `in_progress` → `review` → `done`
`planned` → `blocked` (with reason)

## Documentation

Architecture decisions and domain context live in `.claude/docs/`.
**Read these files before asking architecture or domain questions.**

```
.claude/docs/
├── architecture.md        # Key decisions and rationale
├── domain-model.md        # Core entities and relationships
├── api-contracts.md       # Shared request/response shapes
├── database-schema.md     # Schema decisions, RLS patterns
├── frontend-structure.md  # Frontend conventions and patterns
├── backend-structure.md   # Backend layer conventions
├── integrations.md        # Claude API, Telegram, BullMQ, Open-Meteo
├── decisions.md           # Architecture decision log (ADR)
├── known-issues.md        # Known bugs and limitations
└── glossary.md            # Domain terms (FEFO, CDA, ADU, etc.)
```

## AI Workflow

- **Read `v*-spec.md` first** for domain requirements — these are the source of truth.
- **Read `.claude/docs/` next** for architecture decisions.
- **Plan before code** for anything larger than a single function.
- **File structure before code** when introducing a new feature or module.
- **Log completed work** in `.claude/logs/tasks/`.
- **Create handoff** when passing work to another agent.

## Token Efficiency

- Reference `.claude/docs/` and `v*-spec.md` by name rather than pasting content.
- Scope prompts to one feature and one layer at a time.
- Include file path and relevant line range rather than quoting large blocks.
