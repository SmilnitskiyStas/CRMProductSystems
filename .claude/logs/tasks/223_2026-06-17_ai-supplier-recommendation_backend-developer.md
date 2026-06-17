# TASK-223 — Backend: AI Supplier Recommendation

**Agent:** backend-developer
**Date:** 2026-06-17
**Status:** done
**Depends on:** TASK-221 (Marketplace API)

## Summary

Implemented `POST /api/marketplace/ai-recommend` — a Claude-powered endpoint that accepts a procurement need and returns a ranked list of recommended suppliers with Ukrainian-language reasoning.

## Files Created

| File | Purpose |
|---|---|
| `ShelfGuard.Application/Features/Marketplace/ISupplierAdvisor.cs` | Interface + request/result records (Application layer) |
| `ShelfGuard.Infrastructure/AI/SupplierAdvisor/SupplierAdvisor.cs` | Claude API implementation (isolated in Infrastructure/AI) |
| `ShelfGuard.Tests/Marketplace/SupplierAdvisorTests.cs` | 9 unit tests (all green) |

## Files Modified

| File | Change |
|---|---|
| `ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs` | Added `AiRecommendRequestDto`, `AiRecommendResultDto`, `SupplierRecommendationDto` |
| `ShelfGuard.Api/Controllers/MarketplaceController.cs` | Added `POST /api/marketplace/ai-recommend` endpoint + injected `ISupplierAdvisor` |
| `ShelfGuard.Infrastructure/DependencyInjection.cs` | Registered `ISupplierAdvisor → SupplierAdvisor` |

## Architecture

- `ISupplierAdvisor` lives in `Application` layer (following IAiOrderAdvisor pattern)
- `SupplierAdvisor` lives in `Infrastructure/AI/SupplierAdvisor/` (isolated)
- Reuses same Claude key resolution as `ClaudeOrderAdvisor`: tenant `integration_configs` (service='claude') → fallback to `Claude:ApiKey` env
- Structured JSON output via `JsonOutputFormat { Schema = ResponseSchema() }`
- If API key not configured → 503 `{"error": "AI service not configured"}`

## Test Results

- 444 tests pass (435 existing + 9 new)
- 0 errors, 0 warnings

## Acceptance Criteria — all met

1. `dotnet build` — green (0 errors, 0 warnings)
2. `dotnet test` — 444/444 green
3. `POST /api/marketplace/ai-recommend` exists, requires `[Authorize]` + `[RequireModule("marketplace")]`
4. `ISupplierAdvisor` in Application layer; `SupplierAdvisor` in `Infrastructure/AI/SupplierAdvisor/`
5. Missing API key → 503 with `{"error": "AI service not configured"}`
6. Task log created (this file)
7. Backlog updated: TASK-223 → `done`
