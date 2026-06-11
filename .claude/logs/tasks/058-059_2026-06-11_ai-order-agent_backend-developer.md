---
task_id: TASK-058, TASK-059
date: 2026-06-11
agent: backend-developer + database-engineer + devops-engineer
status: done (live Claude call pending CLAUDE_API_KEY from user)
---

# TASK-058/059 — AI Order Agent (v2-spec §7)

## Architecture (per CLAUDE.md: AI isolated in Infrastructure/AI)
```
worker 05:00 cron ──► POST /api/ai-orders/generate (service account netmgr)
                          │
AiOrderService (Application):
  1. context: weather 7d + events 14d + applied promos + supply schedule next delivery
  2. base order: IOrderCalcService (ADU → CDA buffer → k_event×k_weather×k_promo → MOQ/USQ)
  3. IAiOrderAdvisor.AdviseAsync(context)  ◄── Domain interface
                          │
ClaudeOrderAdvisor (Infrastructure/AI):
  - official Anthropic C# SDK (`Anthropic` NuGet)
  - model: Claude:Model env (default claude-sonnet-4-6 — project CLAUDE.md names Sonnet)
  - §7 prompt template (UA), structured outputs (json_schema) → guaranteed-valid JSON
  - hallucinated product_ids skipped (base qty stays)
  4. persist ai_order_suggestions + items (context_snapshot jsonb, tokens_used)
  5. worker → Telegram to managers: "AI замовлення готове"
```

## Files
- Domain: `Entities/AiOrderSuggestion.cs`, `Interfaces/{IAiOrderAdvisor, IAiOrderRepository}.cs`
- Infrastructure: `AI/ClaudeOrderAdvisor.cs`, `Data/Repositories/AiOrderRepository.cs`,
  migration `V2AiOrders` (2 tables + RLS incl. items-via-suggestion join)
- Application: `Features/AiOrders/AiOrderService.cs` (service+DTOs)
- Api: `Controllers/AiOrdersController.cs`
- Worker: `jobs/ai-order.job.ts` + cron `0 5 * * *`; env WORKER_API_EMAIL/PASSWORD
- Compose: api gets CLAUDE_API_KEY/CLAUDE_MODEL; worker gets API_BASE_URL + service creds
- Tests: `Tests/AiOrders/ClaudeAdviceParserTests.cs` — 3/3

## Endpoints (spec §9, `AtLeastStoreManager`)
```
GET  /api/ai-orders (?store_id)        POST /api/ai-orders/generate {storeId}
GET  /api/ai-orders/{id}               PUT  /api/ai-orders/{id}/items/{itemId} {quantityFinal, editReason}
POST /api/ai-orders/{id}/accept        POST /api/ai-orders/{id}/reject
```
- accept → status accepted / partially_accepted (when any item WasEdited) + AcceptedBy/At
- item edit blocked after finalize; WasEdited tracked for learning (spec phase 5)

## Verified on production
- ai_order_* tables with RLS ✓ · generate without key → clean 400
  "Claude API key is not configured" ✓ · list → [] ✓ · worker cron registered ✓

## Pending
1. **CLAUDE_API_KEY** in server .env (user provides) → live e2e generate
2. TASK-060 — web dashboard /ai-orders (review/edit/accept)
