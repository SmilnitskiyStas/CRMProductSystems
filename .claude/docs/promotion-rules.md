# Promotion rules (price rules) — design

Source idea: `docs/POS_RULE_BUILDER_ARCHITECTURE_UA.md` (rule builder for a third-party POS). Our POS is
our own cashier app, so we keep the *model* (rules as data, immutable versions, scope/audience/limits,
validation, execution log) and drop the *delivery* (Groovy, MySQL, import files): rules are synced to the
cashier as JSON and evaluated by the same deterministic engine on the server and in the cashier.

## Decisions (user, 2026-10-19)
- Several rules match one item → the **best for the customer** wins (single rule per unit, ties by `priority`
  then by older rule). The existing loyalty **tier discount** also competes in that choice (it is not stacked on
  top of a promo for the same unit).
- Targets in v1: **items and categories** (category ids are added to the catalog sync).
- **Loyalty-card scanning on the cashier is phase 4.** Phases 1–3 must already model `audience`, so
  loyalty-only rules exist but only fire once a membership is attached to the sale.

## Rule definition (stored as JSON in a version row, validated server-side)
```json
{
  "type": "buy_n_get_m",                // percent_off | fixed_off | fixed_price | buy_n_get_m
  "targets": { "itemIds": ["..."], "categoryIds": ["..."] },   // at least one
  "scope": {
    "storeIds": [],                      // empty = all stores of the tenant
    "validFrom": "2026-10-20T00:00:00Z", "validTo": null,
    "weekdays": [],                      // 1..7, empty = every day
    "timeFrom": null, "timeTo": null     // "HH:mm" local to the store, optional
  },
  "audience": { "kind": "all" },         // all | loyalty_member | loyalty_tier (+ "tierIds": [])
  "params": {                            // by type
    "percent": 20,                       // percent_off (0.01..100)
    "amount": 5.00,                      // fixed_off: off each unit
    "price": 9.99,                       // fixed_price: unit price
    "buyQty": 2, "getQty": 1,            // buy_n_get_m: every (buyQty+getQty)-th group's last getQty units…
    "getPrice": 0.01,                    // …cost this price (or "getPercent": 100 for % off those units)
    "matchMode": "per_item"              // per_item | across_set (cheapest units get the discount)
  },
  "limits": { "maxApplicationsPerReceipt": null },
  "excludeAlreadyDiscounted": false,     // skip units that already carry a stock/expiry Discount
  "priority": 100
}
```
Money rule: a promo price is never below 0.01 and never above the regular price.

## Entities (all tenant-scoped, RLS with provider/worker bypass)
- `promotion_rules` (Id, TenantId, Name, Description, Status `draft|published|paused|archived`,
  CurrentVersionId, PublishedVersionId, CreatedBy, timestamps).
- `promotion_rule_versions` (Id, RuleId, VersionNumber, DefinitionJson, Checksum, ChangeComment, CreatedBy,
  CreatedAt) — **immutable**; editing a published rule creates a new version (draft) and publishing swaps
  `PublishedVersionId`. Unpublish/pause keeps history.
- `promotion_rule_applications` (execution log: TenantId, RuleId, VersionId, TransactionId, StoreId, RegisterId,
  AppliedAt, Units, DiscountAmount) written when a sale uses a rule.
- Sale line storage: `pos_transaction_items` get `PromotionRuleId` (nullable) + `PromotionLabel` (nullable).
  A line that is only partly discounted is **split into sub-lines** (e.g. qty 3 of X at 2+1 → qty 2 @ price and
  qty 1 @ 0.01) so `PriceFinal × Quantity` stays exact everywhere (analytics, returns, receipts) without a new
  line-total column.

## Engine (pure function, identical in C# and TypeScript, golden test vectors shared as JSON)
Input: lines `[{ itemId, categoryId, unitPrice, quantity, hasStockDiscount }]`, rules (published version
definitions), context `{ storeId, at, membership? { id, tierId } }`.
Output: per input line a list of segments `{ quantity, unitPrice, ruleId?, label? }` (sum of quantities equals the
line quantity). Algorithm per unit: eligible rules = published ∧ in scope(store, date, weekday, time) ∧ audience ∧
target match; for `buy_n_get_m` build groups of `buyQty+getQty` units (per_item or across_set, cheapest units
discounted first); choose the cheapest resulting unit price among promos and the loyalty tier price; apply
`maxApplicationsPerReceipt`. Rounding: kopecks, per unit.
Server is authoritative; the cashier mirror shows the same result before payment. Offline sales are re-priced
by the server with the sale's `OccurredAt` and the rules published at that time; a difference is reported in the
batch result (never blocks the sale).

## API contract
Manager (web), policy AtLeastStoreManager, `[RequireModule("pos")]`:
- `GET /api/promotion-rules?status=&storeId=` list (name, type, status, scope summary, version).
- `GET /api/promotion-rules/{id}` rule + versions.  `POST /api/promotion-rules` create draft.
- `PUT /api/promotion-rules/{id}` save draft (new version when published).
- `POST /api/promotion-rules/{id}/validate` → `{ errors[], warnings[] }`.
- `POST /api/promotion-rules/{id}/publish` | `/pause` | `/archive`.
- `POST /api/promotion-rules/simulate` body: cart lines + store + time + audience → engine result (used by the
  web "test a receipt" panel).
Cashier (operator token `[AllowOperatorToken]`):
- `GET /api/pos/sync/promotions?etag=` → `{ etag, serverTime, rules: [ { id, versionId, definition } ] }`
  (published, valid now or in the future for this register's store; 304-style empty when etag unchanged).
- Catalog sync item DTO gains `categoryId`.
Store managers manage only rules whose scope is inside their assigned stores (same rule as registers).

## Validation (errors block publish, warnings do not)
Errors: no targets; no price param for the type; percent ∉ (0,100]; price < 0.01; buyQty<1/getQty<1;
validTo < validFrom; unknown item/category/store/tier ids; loyalty audience without tiers when kind=loyalty_tier.
Warnings: overlaps with another published rule on the same item (best-for-customer will decide);
no stores selected (= all stores); rule already expired; getPrice ≥ regular price for some target.

## Phases
1. Backend: schema + RLS, engine (C#) + golden vectors, PosService integration (sub-lines, log), sync endpoint
   + `categoryId` in catalog sync, tests.
2. Backend CRUD/validate/publish/simulate API + web builder (wizard per rule type, item/category/store pickers,
   test-a-receipt panel, list with statuses).
3. Cashier: engine mirror (TS, same vectors), promotions sync into SQLite, cart shows promo lines/labels,
   offline behaviour, re-pricing report.
4. Cashier loyalty: scan/enter member code → membership + tier in the sale → loyalty-audience rules and tier
   discount; `ResolveLoyaltyCodeResult` gets `tierId`.
