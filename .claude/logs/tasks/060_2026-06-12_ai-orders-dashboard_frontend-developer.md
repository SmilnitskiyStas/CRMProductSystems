---
task_id: TASK-060
date: 2026-06-12
agent: frontend-developer
status: done
---

# TASK-060 — Web: AI Orders Dashboard (/ai-orders)

## Files
```
app/(dashboard)/ai-orders/page.tsx       — store filter, "Згенерувати зараз", history chips
features/ai-orders/
  types.ts                               — AiOrder(+items), STATUS_META colors
  api/aiOrders.ts                        — list, detail, generate, updateItem, accept, reject
  hooks/useAiOrders.ts                   — 6 React Query hooks
  components/AiOrderReview.tsx           — spec §7 UI: Базове | AI пропонує (±%) | Ваша зміна | Причина
components/layout/Sidebar.tsx            — + "AI Замовлення" (Sparkles, AT_LEAST_STORE_MANAGER)
```

## UX (matches spec §7 mockup)
- Review table per item: base qty, AI suggestion with ±% delta badge, editable final qty
  (blur-save; blue border + "✏️ змінено" when edited), reasoning with confidence dot
- "Підтвердити замовлення (N)" / "Відхилити" — hidden after finalize; status chip
  (Очікує / Прийнято / Прийнято зі змінами / Відхилено)
- History as clickable chips (date · store · status · items); generation auto-opens result
- "Згенерувати зараз" for on-demand runs alongside the 05:00 cron

## Verification
- tsc clean; deployed; GET /ai-orders → 200
- Live generate blocked only by Anthropic credits (clean error toast shown to user)

## Sprint v2.5 «AI Agent» — COMPLETE (pending live-credits e2e)
TASK-058 ✅ 059 ✅ 060 ✅ → **v2 specification fully implemented**
