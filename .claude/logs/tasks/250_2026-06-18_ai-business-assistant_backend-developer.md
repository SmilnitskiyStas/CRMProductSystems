# TASK-250 — AI Business Assistant
**Date:** 2026-06-18
**Agent:** backend-developer + frontend-developer
**Status:** done

## What was implemented

### Backend

**Infrastructure (AI isolation)**
- `ShelfGuard.Infrastructure/AI/BusinessAssistant/BusinessAssistantAdvisor.cs`
  - Implements `IBusinessAssistantAdvisor`
  - Aggregates cross-module DB context per tenant (critical stock, pending AI orders, last-7-day sales, active suppliers)
  - Calls Claude API (same key resolution as ClaudeOrderAdvisor: tenant integration_configs → env fallback)
  - Plain text response (no structured JSON output — free-form conversation)
  - Returns `BusinessAssistantResult` with reply, context summary, model, tokens

**Application layer**
- `ShelfGuard.Application/Features/AiAssistant/AiAssistantDtos.cs` — DTOs: request, response, context summary, result
- `ShelfGuard.Application/Features/AiAssistant/IAiAssistantService.cs` — service + advisor interfaces
- `ShelfGuard.Application/Features/AiAssistant/AiAssistantService.cs` — orchestration: validate → check config → call advisor → return response

**API Controller**
- `ShelfGuard.Api/Controllers/AiAssistantController.cs`
  - `POST /api/ai/assistant`
  - `[Authorize]` (any authenticated role)
  - `[RequireModule("inventory")]`
  - Thin: extract tenantId from JWT → call service → return 200/400/403

**DI Registration**
- `ShelfGuard.Application/DependencyInjection.cs`: `IAiAssistantService → AiAssistantService`
- `ShelfGuard.Infrastructure/DependencyInjection.cs`: `IBusinessAssistantAdvisor → BusinessAssistantAdvisor`

### Frontend

**Feature directory:** `frontend/features/ai-assistant/`
- `types.ts` — `AiAssistantRequest`, `AiAssistantResponse`, `AiAssistantContextSummary`
- `api/aiAssistant.ts` — `POST /api/ai/assistant` via shared `api` client
- `hooks/useAiAssistant.ts` — `useMutation` wrapping the API call
- `components/AiAssistantWidget.tsx` — chat UI: message history, textarea input (Enter to send), pulsing loading indicator, context badges per AI reply

**Dashboard integration**
- `frontend/app/(dashboard)/dashboard/page.tsx` — `<AiAssistantWidget />` added below Store Map

## Architecture decisions
- Advisor does both DB aggregation and AI call (single Infrastructure class) — avoids a cross-layer boundary for the aggregation data
- No structured JSON output — Claude responds with free-form Ukrainian text (simpler, better for Q&A)
- Context capped: 20 critical stock batches, 5 pending orders, 20 sales lines, 15 suppliers
- Error handling mirrors AiOrderService pattern (credit balance → user-friendly message)

## Build results
- `dotnet build`: 0 errors, 0 warnings
- `npm run build` (Next.js): clean, no errors
