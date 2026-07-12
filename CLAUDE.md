# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**ShelfGuard** — B2B SaaS платформа для рітейлу та суміжних галузей: продуктові магазини, мережі, автосервіси, виробництво, склади.
Відстеження термінів придатності (FEFO), управління залишками, POS-каса (Checkbox ПРРО), AI-автозамовлення, IoT-інтеграції, B2B marketplace.
Modular monolith, multi-tenant (RLS), що масштабується до enterprise.

**Production:** `agrusystems.pp.ua` (Hetzner 93.127.143.98, Docker, Nginx + Let's Encrypt)

**Spec-файли (primary source of truth):**
- `v1-spec.md` — MVP: Shelf Manager + CRM ядро + HR + Notifications
- `v2-spec.md` — Auto Order + AI Forecasting (Claude API)
- `v3-spec.md` — IoT + CV Camera + ПРРО Каса
- `v4-spec.md` — Platform Transformation: multi-industry, module activation system

**Поточний стан:**
- v1 ✅ · v2 ✅ · v3 ✅ · v4 ✅ (Store→Location, Product→Item, module activation)
- Sprint v3.5 «Provider UX» завершено (TASK-275..278)

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Next.js · React · TypeScript · Tailwind CSS · shadcn/ui · React Query · Zustand |
| Backend | ASP.NET Core Web API · C# · .NET 8 · modular monolith |
| Mobile | Expo SDK 56 · React Native · Expo Router · NativeWind v4 |
| Queue | BullMQ · Redis 7 (окремий Node.js worker-сервіс) |
| Database | PostgreSQL 16 · EF Core 8 · Row Level Security (RLS) |
| AI | Claude API (claude-sonnet-4-5, Anthropic SDK) |
| Fiscal | Checkbox ПРРО (IFiscalService, per-tenant via integration_configs) |
| IoT | MQTT / Mosquitto (температура, вага, stock events) |
| Infrastructure | Docker · Docker Compose · GitHub Actions CI/CD |

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
dotnet run --project ShelfGuard.Api         # API server → localhost:5000
dotnet build       # build solution
dotnet test        # run all tests
dotnet test --filter "FullyQualifiedName~FeatureName"  # run single test
dotnet ef migrations add <Name> --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
```

### Mobile (`/mobile`)
```bash
npm install           # install dependencies
npx expo start        # start dev server
npx expo run:android  # Android emulator
```

### Worker (`/worker`)
```bash
npm install        # install dependencies
npm run dev        # start BullMQ worker
```

### Infrastructure
```bash
docker compose up -d    # start PostgreSQL + Redis + Mosquitto + Worker
docker compose down     # stop all services
docker compose logs -f  # tail logs
```

## Architecture

**Modular monolith.** Сервіси розбиваються тільки при конкретній операційній необхідності.

### Backend layout
```
/backend
├── ShelfGuard.Api/            # Controllers (50), middleware, startup — thin layer only
├── ShelfGuard.Application/    # Business logic, use cases, DTOs (41 feature modules)
│   └── Features/
│       ├── Auth/              # JWT, roles, tenant context
│       ├── Inventory/         # Product catalog, FEFO
│       ├── Stock/             # Stock status, expiry tracking
│       ├── Receipts/          # Supplier deliveries
│       ├── Transfers/         # Store-to-store movements
│       ├── WriteOffs/         # Write-off documents
│       ├── Orders/            # Purchase orders, order formula
│       ├── Adu/               # Average Daily Usage
│       ├── Buffer/            # CDA buffer engine (green/yellow/red)
│       ├── Sales/             # Daily sales
│       ├── Pos/               # POS shifts, transactions, fiscalization
│       ├── Suppliers/         # Supplier catalog, metrics
│       ├── Marketplace/       # B2B supplier marketplace
│       ├── Customers/         # Customer master data
│       ├── Locations/         # Store zones, shelves
│       ├── Stores/            # Store master
│       ├── Users/             # Team members, roles
│       ├── Schedules/         # Employee work schedules
│       ├── Notifications/     # Alert settings, queue
│       ├── IoT/               # IoT device management
│       ├── Analytics/         # Reports, summaries, KPIs
│       ├── AiOrders/          # AI order suggestions
│       ├── AiAssistant/       # Claude AI business advisor
│       ├── AutoService/       # Auto-service module
│       ├── Production/        # Production orders, recipes
│       ├── Events/            # Demand events (holidays, promos)
│       ├── Weather/           # Open-Meteo integration
│       ├── Cannibalization/   # Promo cannibalization
│       ├── Chat/              # Provider ↔ client live chat
│       ├── ServiceDesk/       # Support tickets
│       ├── Provider/          # SaaS provider management
│       ├── Admin/             # Super admin (tenant onboarding)
│       ├── Integrations/      # Third-party API configs (ПРРО, Claude)
│       ├── Settings/          # Module settings, toggles
│       └── Telegram/          # Telegram bot integration
├── ShelfGuard.Domain/         # Entities (69), value objects, repo interfaces (42)
├── ShelfGuard.Infrastructure/ # EF Core (87 migrations), repositories (42), external services
│   ├── Data/                  # AppDbContext, migrations, repositories
│   ├── AI/                    # Claude API client — isolated here
│   │   ├── ClaudeOrderAdvisor.cs
│   │   ├── BusinessAssistant/
│   │   └── SupplierAdvisor/
│   └── Integrations/
│       ├── Prro/              # IFiscalService → CheckboxFiscalClient (per-tenant factory)
│       └── OpenMeteoClient.cs
└── ShelfGuard.Tests/          # 45 test files (~8% coverage; Pos + Prro best covered)
```

### Frontend layout
```
/frontend
├── app/                       # Next.js App Router (43 pages)
│   ├── (auth)/login/
│   └── (dashboard)/           # All authenticated routes
├── features/                  # 35 feature directories (one per domain)
│   ├── inventory/             # Product catalog
│   ├── shelf/                 # FEFO tracking, expiry statuses
│   ├── stock/                 # Live stock view
│   ├── receipts/              # Supplier deliveries
│   ├── transfers/             # Store-to-store movements
│   ├── write-offs/            # Waste management
│   ├── orders/                # Purchase orders + buffer funnel
│   ├── sales/                 # Manual sales entry
│   ├── pos/                   # Point of sale (5 components)
│   ├── analytics/             # Charts, dashboards, POS analytics
│   ├── ai-orders/             # Claude AI forecasting
│   ├── ai-assistant/          # AI chat widget
│   ├── marketplace/           # B2B supplier catalog (11 components)
│   ├── suppliers/             # Supplier management
│   ├── customers/             # Customer directory
│   ├── auto-service/          # Auto-service module (9 components)
│   ├── production/            # Manufacturing, recipes
│   ├── locations/             # Store zones, floor plans
│   ├── stores/                # Store management
│   ├── schedules/             # Staff shifts
│   ├── iot/                   # Temperature sensors, devices
│   ├── users/                 # Team members, permissions
│   ├── notifications/         # Alert history
│   ├── integrations/          # ПРРО, Claude key config
│   ├── service-desk/          # Support tickets (7 components)
│   ├── chat/                  # Live chat sessions
│   ├── provider/              # SaaS admin panel (14 components)
│   ├── admin/                 # Tenant onboarding
│   ├── dashboard/             # Main home page
│   ├── settings/              # User preferences
│   ├── profile/               # Account management
│   ├── auth/                  # JWT, session
│   ├── events/                # Activity log
│   ├── catalog/               # Internal catalog service
│   └── modules/               # Feature flags / module activation
├── components/                # Shared UI: shadcn/ui + custom (20 components)
│   ├── ui/                    # button, dialog, form, table, badge, etc.
│   └── layout/                # Sidebar, TopBar, UserMenu, StoreSelector
└── lib/                       # api.ts, query-client.ts, roles.ts, utils.ts
```

### Mobile layout
```
/mobile
├── app/
│   ├── (auth)/
│   │   ├── _layout.tsx
│   │   └── login.tsx
│   └── (app)/
│       ├── _layout.tsx              # Bottom Tab Navigator (5 tabs)
│       ├── index.tsx                # Dashboard
│       ├── scan.tsx                 # Barcode scan (center FAB tab)
│       ├── notifications.tsx
│       ├── ai-assistant.tsx
│       ├── profile.tsx
│       ├── stock/                   # Inventory + batches
│       ├── receipt/                 # Receive deliveries
│       ├── transfers/               # Create/view transfers
│       ├── write-offs/              # Quick waste entry
│       ├── pos/                     # Full POS: scanner → cart → payment → receipt
│       ├── production/              # Recipes, batch management
│       ├── schedules/               # View shifts
│       ├── customers/               # Customer lookup
│       ├── service-desk/            # Support tickets
│       ├── marketplace/             # Supplier catalog
│       ├── auto-service/            # Auto shop module
│       └── inventory/[zoneId]       # Zone inventory
└── features/                        # 15 feature directories (same pattern as web)
```

### Worker layout
```
/worker                        # Node.js BullMQ worker service
├── src/
│   ├── jobs/
│   │   ├── expiry-check.job.ts        # cron: every hour — update stock statuses
│   │   ├── notification.job.ts        # queue worker — Telegram/Push/Email + retry ×3
│   │   ├── weekly-report.job.ts       # cron: Sunday 08:00
│   │   ├── cleanup.job.ts             # cron: daily 03:00 — archive + purge logs
│   │   ├── ai-order.job.ts            # cron: 05:00 — Claude API → order suggestions
│   │   ├── fiscalization-retry.job.ts # cron: */5 min — poll Checkbox pending receipts
│   │   ├── mqtt-listener.ts           # MQTT subscriber (shelfguard/#) — IoT events
│   │   └── telegram-listener.ts       # Telegram bot: /start /status /critical /tasks
│   ├── services/
│   │   ├── db.ts              # PostgreSQL connection pool
│   │   ├── redis.ts           # Redis client (BullMQ broker)
│   │   ├── telegram.ts        # Telegraf.js bot client
│   │   ├── email.ts           # Resend API (blocked: domain not verified)
│   │   ├── iot-rules.ts       # Pure functions: confidence calc, threshold logic
│   │   └── notification-log.ts
│   └── index.ts
└── package.json
```

## Architecture Rules

- **Thin controllers.** Business logic belongs in `ShelfGuard.Application` services, never in controllers.
- **Feature-based frontend.** Components, hooks, API calls, and types live inside their feature directory. Pattern: `types.ts`, `api/`, `hooks/`, `components/`.
- **React Query owns server state.** Do not duplicate it into Zustand stores. Zustand — тільки UI state (auth token, notification badge).
- **CSR for all authenticated/dashboard views.** SSR only when a page has an explicit SEO requirement.
- **AI integrations are isolated.** Claude API client, prompts, and AI logic stay in `ShelfGuard.Infrastructure/AI`. Never couple AI providers to business logic.
- **Validate at boundaries only.** API endpoints, external integrations, user input. No redundant internal re-validation.
- **FEFO is sacred.** Any stock consumption must use FEFO logic — always take the batch with the nearest `expiry_date`.
- **Tenant isolation via RLS.** Every table with tenant data must have `tenant_id` and a corresponding PostgreSQL RLS policy. Use `NULLIF(current_setting(...), '')` guard in all policies.
- **expiry_date and batch_number never change on transfer.** These fields are copied as-is when stock moves between locations.
- **Module activation.** Feature endpoints guarded by `[RequireModule("module_key")]`. Module sets stored in `tenants.modules` (JSONB). Business type in `tenants.business_type`.
- **Fiscal service is per-tenant.** Use `IFiscalServiceFactory` to resolve `IFiscalService` per-tenant from `integration_configs`. Never inject `IFiscalService` directly at startup.
- **Secrets never in code.** ПРРО creds, Claude API key, Telegram token → `.env` only. Masked on GET (last 4 chars).

## Multi-Agent Workflow

This repository uses a **mandatory** multi-agent system. Agents are defined in `.claude/agents/`.

> **RULE: Never implement code directly in the main session.**
> For any implementation task — always spawn the appropriate role agent.
> The main session orchestrates; agents implement.

### Clarify scope before implementing (MANDATORY gate)

Spawned agents run from a written brief and cannot interactively chat with the user —
background agents work async, so a question raised mid-task comes back to the main
session, not to the user, in real time. So clarification happens **before** spawning,
in the main session, not inside the agent:

- If the request leaves a decision only the user can make (product/UX choice, content,
  priority between tradeoffs, branding, scope — what to build vs skip) — ask first, via
  `AskUserQuestion` or plain text, and wait for the answer before spawning anything.
- If the task is already fully specified — a described bug, a spec/CLAUDE.md that already
  answers the open questions, an explicit instruction with clear scope — don't add
  questions for their own sake. Go straight to a complete, unambiguous brief.
- Judgment calls with an objective best-practice answer (security hardening, standard
  error handling, following the architecture rules in this file) don't need user
  sign-off — implement per project convention and note the decision in the task log.
- If a spawned agent hits a genuine unresolved decision it cannot infer from its brief,
  it must stop and report the specific question back instead of guessing — the main
  session relays it to the user and resumes the agent (via SendMessage) once answered.

### How to spawn an agent

Agents in `.claude/agents/` are **not** built-in subagent types.
Always spawn as `general-purpose` with an instruction to read the role file first:

```
Agent({
  subagent_type: "general-purpose",
  description: "frontend-developer: <short task description>",
  prompt: `Read .claude/agents/frontend-developer.md first, then implement:
<detailed task description with file paths, requirements, context>`
})
```

### Agent → Task mapping (MANDATORY)

| Task type | Agent to spawn |
|---|---|
| New page / component / form / hook (Next.js/React) | `frontend-developer` |
| API endpoint / service / domain logic (C#) | `backend-developer` |
| Expo screen / mobile component / navigation | `mobile-developer` |
| DB schema / EF migration / index / RLS policy | `database-engineer` |
| Architecture decision / module design | `project-architect` |
| Task tracking / sprint / status update | `project-manager` |
| Test plan / regression check | `qa-tester` |
| Auth / permissions / input validation | `security-reviewer` |
| Docker / CI/CD / deployment | `devops-engineer` |
| Docs / API contracts / ADR | `documentation-writer` |

### When the main session acts directly (exceptions)

The main session may act without spawning an agent **only** for:
- Reading files / exploring codebase
- Running `tsc --noEmit`, `git status`, `git push`, lint
- Quick isolated fix in a single well-known file (< 10 lines)
- Answering architecture questions without writing code

### Agent Workflow (all agents must follow)

1. Load context: read `CLAUDE.md` → relevant `v*-spec.md` → `.claude/docs/` → `.claude/tasks/current.md`
2. Check task dependencies before starting
3. Implement only assigned responsibilities
4. Create task log in `.claude/logs/tasks/`
5. Create handoff in `.claude/logs/handoffs/` if next agent needed
6. Update task status in `.claude/tasks/`
7. Update `.claude/docs/` if architecture or domain behavior changes

### Codex CLI as a parallel channel (optional)

`codex` (OpenAI Codex CLI, ChatGPT-login auth — no API billing) is available on this
machine and can run **alongside** Claude agents for extra throughput. It is not a
replacement for the mandatory role-agent workflow above — use it to parallelize an
independent workstream, or as a second-opinion reviewer, never as the only implementer
of record for a task.

Invoke non-interactively via Bash (`run_in_background: true`), same briefing discipline
as a Claude agent prompt (self-contained, cites CLAUDE.md/spec/file paths):
```
codex exec -C <dir> --sandbox workspace-write --ask-for-approval never \
  --json -o <output-file> "<full self-contained task brief>"
```
`-m <model>` forces a specific model (config default is set in `~/.codex/config.toml`;
override per-call when a newer model tag is confirmed available).

- **Isolation rule (mandatory, no need to ask):** if Codex's task can touch the same
  files a concurrently-running Claude agent might touch, give Codex its own `git
  worktree` (`git worktree add <path> <branch>`) and point `-C` at it — same principle
  as this repo's `isolation: "worktree"` option for parallel Claude agents. Only share
  the main working tree when the two workstreams have disjoint, explicitly-stated scope.
- `codex review` runs a non-interactive code review against the current repo — a safe
  read-only parallel check on a Claude agent's diff, no worktree needed.
- After a Codex run, write the same `.claude/logs/tasks/TASK-ID_..._codex.md` log as any
  other agent, so the trail stays consistent regardless of which tool did the work.

### Task ID Format
```
TASK-001, TASK-002, ...  (current max: TASK-278)
```

### Task Log File Format
```
.claude/logs/tasks/TASK-ID_YYYY-MM-DD_short-description_agent.md
Example: .claude/logs/tasks/278_2026-06-21_live-chat_backend-developer.md
```

### Task States
`planned` → `in_progress` → `review` → `done`
`planned` → `blocked` (with reason)

## Documentation

Architecture decisions and domain context live in `.claude/docs/`.
**Read these files before asking architecture or domain questions.**

```
.claude/docs/
├── architecture.md        # Key decisions and rationale (ADR-001..015)
├── domain-model.md        # Core entities and relationships
├── api-contracts.md       # Shared request/response shapes
├── database-schema.md     # Schema decisions, RLS patterns
├── frontend-structure.md  # Frontend conventions and patterns
├── backend-structure.md   # Backend layer conventions
├── integrations.md        # Claude API, Telegram, BullMQ, Open-Meteo, Checkbox, MQTT
├── decisions.md           # Architecture decision log (ADR)
├── known-issues.md        # Known bugs and limitations
└── glossary.md            # Domain terms (FEFO, CDA, ADU, MOQ, USQ, etc.)
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
- **Short reports.** Звіти про роботу і завершення задач — стислі: що зроблено, статус build/tests/deploy, знайдені проблеми. Без таблиць, повторів контексту й переказу процесу. Стосується фінальних відповідей, agent report-back і task logs.
