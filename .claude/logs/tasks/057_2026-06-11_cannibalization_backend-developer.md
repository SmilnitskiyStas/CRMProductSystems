---
task_id: TASK-057
date: 2026-06-11
agent: database-engineer + backend-developer
status: done
---

# TASK-057 — Promo Cannibalization (v2-spec §5)

## Files
| Layer | File |
|---|---|
| Domain | `Entities/PromoCannibalization.cs`, `Interfaces/ICannibalizationRepository.cs` |
| Infrastructure | Migration `V2Cannibalization` (+RLS), `Data/Repositories/CannibalizationRepository.cs` |
| Application | `Features/Cannibalization/CannibalizationService.cs` (service+DTOs in one file) |
| Api | `Controllers/CannibalizationController.cs` |
| Orders | promoCoefficient in OrderLineDto; multiplier = event × weather × promo |

## Endpoints (spec §9, `AtLeastStoreManager`)
```
GET  /api/cannibalization/{discountId}   → suggestions (auto-generates on first call)
PUT  /api/cannibalization/{id}           → edit coefficient (source → manual)
POST /api/cannibalization/apply/{discountId} → IsApplied=true for all rows
```

## Behavior
- **Generation:** discounted product ×2.0; same-segment same-tenant products ×0.7
  (spec ranges 2.0–2.5 / 0.6–0.7 — defaults at the conservative end, editable).
  Lazy generate-on-first-GET keeps DiscountService decoupled while preserving
  the spec UX (manager opens promo → sees suggestions).
- **IsApplied flag** — schema extension (spec has apply step but no column):
  formula counts only applied rows of discounts that are active right now;
  several simultaneous promos on one product multiply.
- UNIQUE(DiscountId, AffectedProductId); rows cascade with discount.

## Production e2e
- Discount −25% on Вода → GET → suggestion {×2.0, ai_suggested, isPromoProduct}
  (no segment siblings in demo data — seeds have SegmentId null) ✓
- apply → rowsApplied 1; discount activated →
  **Вода: k_event 2.0 × k_weather 1.0 × k_promo 2.0 → ORDER 304** (75.97×4)
  Гречка untouched → 67 ✓
- OrderFormula regression: 9/9.

## Follow-up
- Cannibalization confirm/edit UI — blocked on a discounts web page (none exists);
  API is complete for it.
- Demo catalog products need SegmentId values to demo sibling dampening (×0.7).
