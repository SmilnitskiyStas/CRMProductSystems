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

This repository has a multi-agent system. Role definitions live in `.claude/agents/`.

> **The main session does the small, safe, local work itself. It spawns a role agent
> for work that is genuinely multi-file, multi-layer, risky, or long.**
>
> Spawning is not free: every agent starts cold — it re-reads `CLAUDE.md`, its role
> file, and re-explores the codebase, and none of that cost is shared with the main
> session's cache. An unnecessary spawn is pure overhead. Right-size the delegation
> (below) before reaching for `Agent()`.

### When the main session works directly

No agent — just do it in the main session:
- Reading / exploring code, `tsc --noEmit`, `dotnet build`, `git`, lint.
- A change confined to 1 file (or 2–3 tightly-coupled ones) up to ~60 lines, low-risk.
- A localized bug fix.
- Docs / task-log / task-status updates.
- Small follow-ups on an agent's output (a lint fix, a rename, an extra test).

### When to spawn a role agent

Spawn one when the task is any of:
- a change across 3+ files and more than one layer, or a new feature / module;
- a destructive or otherwise irreversible migration;
- a change to an auth / RLS / permission boundary, or to a money-movement calculation;
- concurrency-sensitive work (races, write boundaries, locking);
- a large exploratory effort where keeping the main session's context clean is worth it.

### Right-size the delegation

- **Don't fan out for a routine feature.** The main session already has the project
  context cached — it explores the relevant files itself and spawns **one**
  implementation agent with a tight brief. No separate Explore/Plan agents for
  ordinary work.
- **Explore agent(s)** — only for genuinely broad uncertainty (3+ unfamiliar areas,
  unclear scope).
- **Plan agent** — only for architecture-level design, not an ordinary feature.
- **One agent, end to end.** The implementing agent also runs its own build/test
  verification, writes its own task log, and appends its own short `current.md`
  entry. Do **not** chain a separate `qa-tester` + `documentation-writer` for small
  or medium changes. Spawn a separate `qa-tester` only for a full regression pass on
  a large or risky feature; spawn `documentation-writer` only for a substantial ADR
  or API-contract change.

### Clarify scope before spawning (gate)

A spawned agent runs from a written brief and cannot interactively chat with the user —
a question it raises mid-task comes back to the main session, not to the user in real
time. So clarification happens **before** spawning, in the main session:

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

Role files in `.claude/agents/` are **not** built-in subagent types. Spawn as
`general-purpose` and point the agent at its role file:

```
Agent({
  subagent_type: "general-purpose",
  model: "sonnet",   // set explicitly — see Model tiers
  description: "frontend-developer: <short task description>",
  prompt: `Read .claude/agents/frontend-developer.md first, then implement:
<brief — see the brief template below>`
})
```

### Brief template (keep the cold-start small)

The brief — not a doc trawl — carries the context. Include:
- the task, stated concretely (what changes, expected result);
- the **exact file paths** to read and change (5–15 files, not a directory);
- ~10–20 lines of the surrounding context / constraints;
- a pointer to the **one** relevant `v*-spec.md` section and, if needed, the **one**
  relevant `.claude/docs/` file — by name and heading;
- explicit: *don't read the full `.claude/docs/` set or `current.md` wholesale; if you
  need a specific ADR or past task, `grep` for it by ID.*

### Model tiers (which model to spawn per task)

Every `Agent()` call passes an explicit `model` matching the task's real difficulty —
don't let a spawn silently inherit the main session's model.

| Tier | `model` | Use for |
|---|---|---|
| `cheap` | `haiku` | Routing/triage, file discovery, simple research, summarization, docs sync, mechanical find-and-replace, i18n string sweeps, data backfills of a known shape, boilerplate CRUD from an existing template, applying an already-designed pattern to more files |
| `standard` | `sonnet` | Ordinary coding: frontend, backend, DB changes, normal debugging, tests, normal review, UI implementation, non-trivial business logic, first-of-its-kind work |
| `reasoning` | `opus` | **Rare (~1 task in 15).** Design of a destructive/irreversible migration; design or review of an RLS / tenant-isolation / auth boundary; subtle concurrency (races, write boundaries, locking); cross-system architecture with real unresolved ambiguity |

**Haiku is under-used — reach for it.** Once the hard design was done once (by a
`sonnet`/`opus` agent) and verified, applying that same pattern to more
files/endpoints/components is `haiku` work, not `sonnet`. Example from this project:
designing `components/ui/Table.tsx` needed `sonnet`; the later batches applying it to
30+ files were `haiku`. When genuinely unsure which of `haiku`/`sonnet` a task is,
pick `sonnet` — a wrong downgrade produces silently-worse code with no error to catch it.

**`reasoning`/Opus is a deliberate, rare choice — not "harder than average."**
- It is the short list in the table above, nothing else.
- The escalation trigger is a **change to** an auth/RLS/permission boundary or a
  money-movement calculation — not any code that merely sits near one. "Touches
  security" is too broad; it caught half the backend.
- Opus consumes the usage limit at a much higher rate than Sonnet, and Max plans have
  a separate, smaller weekly Opus cap. **"Use Opus to save limits" is false.**
- When you do spawn Opus: surgical brief, exact files, and **explicitly tell it not
  to read the big context set.** An Opus cold-start reading 300 KB of docs is the
  single most expensive thing this workflow can do.

Head rule: use the cheapest model that can reliably complete the task. A manual
instruction from the user ("use reasoning tier", "keep it cheap", "don't use Opus")
overrides the heuristic unless it breaks a safety or project rule.

Example — a cheap-tier delegation:
```
Agent({
  subagent_type: "general-purpose",
  model: "haiku",
  description: "documentation-writer: sync glossary term",
  prompt: `Read .claude/agents/documentation-writer.md first, then: <task>`
})
```

(A richer per-agent `default_model_tier` + risk-escalation system exists on the
unmerged branch `chore/agent-system-v2` — not active on `main`. Apply the table above
manually per spawn.)

### Which role fits which task

When you do spawn (per "When to spawn a role agent" above), pick the role by task type:

| Task type | Role file |
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

### Agent Workflow (all spawned agents follow this)

1. Load **only** what the brief names: `CLAUDE.md`, the one relevant `v*-spec.md`
   section, the named `.claude/docs/` file(s) if any, and the listed source files.
   Do **not** read the whole `.claude/docs/` set or `.claude/tasks/current.md` in
   full — `grep` by TASK-ID / ADR-ID for anything specific.
2. Check task dependencies before starting.
3. Implement only the assigned scope.
4. Run the build/tests you can (`tsc --noEmit`, `dotnet build`, the relevant
   `dotnet test` filter, `next build` if a route changed).
5. Write a **short** task log in `.claude/logs/tasks/` (what changed, build/test
   status, issues — no process narration).
6. Append a **short** entry (a few lines) to `.claude/tasks/current.md`.
7. Write a handoff in `.claude/logs/handoffs/` **only** if another agent must
   genuinely pick up unfinished work. Update `.claude/docs/` only when architecture
   or documented domain behavior actually changed.

### `/code-review` effort levels

`/code-review` at normal effort, or `codex review`, is the everyday check.
`/code-review ultra` launches a **5-agent Opus cloud fleet ($5–25, heavy limit
burn)** — reserve it for a genuinely high-stakes diff (security-sensitive, RLS,
pre-release), never routine review.

### Codex CLI as a parallel channel (optional)

`codex` (OpenAI Codex CLI, ChatGPT-login auth — no API billing) is available on this
machine and can run **alongside** Claude agents for extra throughput. It is not a
replacement for the role-agent workflow above — use it to parallelize an independent
workstream, or as a second-opinion reviewer, never as the only implementer of record
for a task.

Invoke non-interactively via Bash (`run_in_background: true`), same briefing discipline
as a Claude agent prompt (self-contained, cites CLAUDE.md/spec/file paths). `exec` mode
has **no** `--ask-for-approval` flag (that's interactive-only) — the sandbox flag alone
governs what it may do without asking:
```
codex exec -C <dir> --sandbox workspace-write \
  -m <model> -c model_reasoning_effort=<effort> \
  --json -o <output-file> "<full self-contained task brief>"
```

**Models (verified 2026-07-12 by direct `codex exec` call on codex-cli 0.144.1 — older
CLI errors "requires a newer version of Codex"; run `codex update` if that happens):**

| Model ID | Tier | Use for |
|---|---|---|
| `gpt-5.6-terra` | balanced, ~2x cheaper than Sol, competitive with GPT-5.5 | **Default.** Everyday feature work — the Codex-side equivalent of what backend/frontend-developer agents do |
| `gpt-5.6-sol` | flagship, most capable | Ambiguous/high-stakes work only: architecture calls, security review, gnarly bugs, migrations |
| `gpt-5.6-luna` | fastest/cheapest | High-volume mechanical work: boilerplate, docstrings, simple pattern-following transforms |

`model_reasoning_effort` values (only `low`/`medium` directly verified; `high`/`xhigh` per
OpenAI's public docs, not independently tested): scale effort up with task difficulty,
default `medium`. Never edit the *global* `~/.codex/config.toml` default for this — it's
shared across every project on this machine; always pass `-m`/`-c` per call instead.

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
TASK-001, TASK-002, ...  (current max: TASK-673)
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

Architecture decisions and domain context live in `.claude/docs/`. Consult the
**one** file a question needs — don't read the set end to end. The large files
(`decisions.md`, `api-contracts.md`, `database-schema.md`, `known-issues.md`) are
reference material: `grep` by ADR-ID / endpoint / KI-ID rather than reading whole.

```
.claude/docs/
├── architecture.md         # Key decisions and rationale (ADR-001..015)
├── domain-model.md         # Core entities and relationships
├── api-contracts.md        # Shared request/response shapes
├── database-schema.md      # Schema decisions, RLS patterns
├── frontend-structure.md   # Frontend conventions and patterns
├── backend-structure.md    # Backend layer conventions
├── integrations.md         # Claude API, Telegram, BullMQ, Open-Meteo, Checkbox, MQTT
├── decisions.md            # ADR log — index + recent ADRs in full
├── decisions-archive.md    # Older ADRs, full text (grep by ADR-ID)
├── known-issues.md         # Open bugs and limitations
├── known-issues-archive.md # Resolved / historical issues
└── glossary.md             # Domain terms (FEFO, CDA, ADU, MOQ, USQ, etc.)
```

## AI Workflow

- **Read `v*-spec.md` first** for domain requirements — these are the source of truth.
- **Consult the one relevant `.claude/docs/` file** when a task needs an architecture
  decision — not the whole set.
- **Plan before code** for a new feature or module — a short plan in the main session,
  not necessarily a Plan agent.
- **File structure before code** when introducing a new feature or module.
- **Log completed work** in `.claude/logs/tasks/`.
- **Create handoff** only when another agent must genuinely pick up unfinished work.

## Token Efficiency

- **Small change → main session, not a spawn.** A fresh agent re-pays the whole
  context cost (`CLAUDE.md` + role file + re-exploration), none of it shared with the
  main session's cache. Spawn only per the "When to spawn a role agent" criteria.
- Reference `.claude/docs/` and `v*-spec.md` by name rather than pasting content.
- Scope prompts to one feature and one layer at a time.
- Include file path and relevant line range rather than quoting large blocks.
- Keep `.claude/tasks/current.md` and `.claude/docs/decisions.md` lean — archive
  finished sprints / old ADRs so every future cold-start reads less.
- **Short reports.** Звіти про роботу і завершення задач — стислі: що зроблено, статус build/tests/deploy, знайдені проблеми. Без таблиць, повторів контексту й переказу процесу. Стосується фінальних відповідей, agent report-back і task logs.
