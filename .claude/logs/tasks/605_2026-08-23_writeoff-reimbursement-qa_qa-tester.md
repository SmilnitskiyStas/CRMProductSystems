# TASK-605: Write-off reimbursement + purchase-price loss — QA

**Status:** done
**Agent:** qa-tester
**Scope:** Web + backend only (mobile untouched, per instructions). Verifies TASK-602/603/604.

## Verdict: PASS — ready to commit

No bugs found. Backend automated tests, direct API checks, and live UI walkthrough (local dev, store `Свіжий Кут Центральний`) all match the plan's expected formulas exactly.

## Automated

- `dotnet build` (full solution): 0 errors, 0 warnings.
- `dotnet test --filter "FullyQualifiedName~WriteOffs"`: **28/28 passed**.
- `dotnet test` (full solution): **1837/1837 passed**, no regressions.

## Manual scenarios (dev DB, real create/approve flow via UI + direct API verification of persisted values)

| # | Scenario | Result |
|---|---|---|
| 1 | Auto-fill unit price from `priceRetail`, stays editable | PASS — Сир кисломолочний batch auto-filled `110`, overridden to `120`, override respected end-to-end |
| 2 | Both loss figures shown + live recompute | PASS — retail `360.00 ₴` (120×3), purchase `255.00 ₴` (85×3), live on keystroke |
| 3 | Returned-to-supplier, no prior default → empty fields; fixed type; amount = value×qty | PASS — fields empty pre-selection; fixed/40 → reimbursement `120` (40×3), confirmed via API on the created write-off |
| 4 | Second write-off, same product → type+value pre-fill automatically | PASS — checkbox instantly populated `fixed`/`40` from the item's saved default, no re-entry (core "не вказувати двічі" requirement) |
| 5 | Percent mode, different item (no default) + reuse | PASS — Куряче філе: percent/15 → reimbursement `28.50` (190×0.15); item's `defaultReimbursementType/Value` persisted as `percent`/`15`; second batch of same product pre-filled `percent`/`15` on reuse |
| 6 | Multi-item mix (returned + not-returned), aggregate math | PASS — Куряче філе (returned) + Кефір (not returned) in one write-off → `totalLossAmountPurchase=254` (190+64), `totalReimbursementAmount=28.5`, `netLossAmount=225.5` (254−28.5), Кефір's reimbursement fields all `null` |
| 7 | Detail drawer renders new fields | PASS — purchase loss, reimbursement, net loss all shown; "—" for non-returned items; "RETURNED TO SUPPLIER" badge on returned items |
| 8 | Approve flow unaffected | PASS — approved the mixed write-off; stock deducted exactly (`KUR-2026-051` 20→18, `KEF-2026-051` 30→26), matching quantities written off |
| 9a | Item with null `PricePurchase`/`PriceRetail` | PASS — created a test item with both prices null, write-off created (201) with `unitPrice`/`lossAmount`/`unitPricePurchase`/`lossAmountPurchase` all `null`, no crash; `totalLossAmount` reports `0` (not null) — consistent with the pre-existing `.Where(hasValue).Sum()` pattern noted in TASK-603, not a new bug |
| 9b | Invalid reimbursement value — percent > 100 | PASS — API returns 400 `"Reimbursement percent value cannot exceed 100."`; reproduced through the real UI form (Помідори cherry, percent=150) — error box renders the exact backend message, no write-off created |
| 9c | Invalid reimbursement value — negative | PASS — API returns 400 `"Reimbursement value must be greater than 0."` |

## Notes

- Confirmed via code read that UI error surfacing is generic (`ApiError extends Error`, `lib/api.ts:106-107` parses `{error}` body into `.message`), so 9b/9c's backend messages reach the form's error box through the same existing path used by all other validation errors in this form — verified live for 9b, inferred by mechanism (not separately reproduced) for 9c.
- `Item.Update`/upsert-back logic verified twice independently (fixed→Сир кисломолочний, percent→Куряче філе): both default types persist and both correctly resurface on the next write-off for that product.
- Computer-tool clicks/checkboxes were unreliable against this app's React handlers in this session too (same issue TASK-604 noted) — worked around with `javascript_tool` dispatching real `mousedown/mouseup/click` sequences. Tooling artifact only, not a product bug.
- Left two QA artifacts in the dev DB: item "QA Test NullPrice Item" (id `fc702372-18bf-496d-99f7-ac23d2448109`) and a few extra write-off documents created during testing. Harmless dev-only data; no cleanup endpoint used since none was requested.

## Files reviewed (no changes made)

- `backend/ShelfGuard.Application/Features/WriteOffs/WriteOffService.cs`
- `backend/ShelfGuard.Application/Features/WriteOffs/Dtos/WriteOffDtos.cs`
- `backend/ShelfGuard.Application/Features/Catalog/Dtos/ItemDto.cs`
- `backend/ShelfGuard.Application/Features/Stock/Dtos/StockDtos.cs`, `StockService.cs`
- `frontend/features/write-offs/components/CreateWriteOffForm.tsx`, `types.ts`
- `frontend/features/shelf/types.ts`
- `frontend/app/(dashboard)/write-offs/page.tsx`
- `frontend/lib/api.ts` (error propagation path)
