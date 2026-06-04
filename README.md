# ShelfGuard

B2B SaaS платформа для рітейлу — відстеження термінів придатності (FEFO), управління залишками, AI-автозамовлення.

## Stack

| Layer | Technology |
|---|---|
| Frontend | Next.js 14 · React · TypeScript · Tailwind CSS · shadcn/ui |
| Backend | ASP.NET Core 8 · C# · modular monolith |
| Mobile | Expo SDK 56 · React Native · Expo Router |
| Queue | BullMQ · Redis (Node.js worker) |
| Database | PostgreSQL 16 · EF Core · RLS |
| AI | Claude API (claude-sonnet-4) |
| Infrastructure | Docker · Docker Compose |

## Local Setup

### Prerequisites
- Docker Desktop
- .NET 8 SDK
- Node.js 18+
- dotnet-ef tool: `dotnet tool install --global dotnet-ef`

### 1. Start infrastructure
```bash
docker compose up -d
```

### 2. Backend
```bash
cd backend
dotnet ef migrations add InitialCreate --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
dotnet run --project ShelfGuard.Api
# API → http://localhost:5000
# Swagger → http://localhost:5000/swagger
```

### 3. Frontend
```bash
cd frontend
npm install
npx shadcn@latest add button input form table badge dialog alert-dialog switch label
npm run dev
# → http://localhost:3000
```

### 4. Mobile
```bash
cd mobile
npm install
npx expo start
```

### 5. Worker
```bash
cd worker
npm install
npm run dev
```

## Project Structure

```
ShelfGuard/
├── backend/          # ASP.NET Core API (modular monolith)
├── frontend/         # Next.js web app
├── mobile/           # Expo React Native app
├── worker/           # BullMQ background jobs (Node.js)
├── docker-compose.yml
├── v1-spec.md        # MVP specification
├── v2-spec.md        # Auto Order + AI spec
├── v3-spec.md        # IoT + POS spec
└── CLAUDE.md         # AI agent workflow rules
```

## Versioning

| Version | Status | Description |
|---|---|---|
| v1.0 | 🚧 In progress | Shelf Manager · CRM core · HR · Notifications |
| v2.0 | 📋 Planned | Auto Order · AI Forecasting · Weather |
| v3.0 | 📋 Planned | IoT sensors · CV cameras · POS |

## Spec Files

- [`v1-spec.md`](v1-spec.md) — бізнес-логіка, БД схема, API ендпоінти MVP
- [`v2-spec.md`](v2-spec.md) — ADU/CDA алгоритми, Claude API інтеграція
- [`v3-spec.md`](v3-spec.md) — IoT, Computer Vision, ПРРО каса
