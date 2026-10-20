# TASK-732 - Promotion rules Phase 1 (backend) - review

Design: `.claude/docs/promotion-rules.md` (Phase 1).

## Done
- Schema (migration `20261019084338_AddPromotionRules`): `promotion_rules`, `promotion_rule_versions`
  (immutable - BEFORE UPDATE trigger raises), `promotion_rule_applications`; `pos_transaction_items`
  += `PromotionRuleId` (FK SET NULL) + `PromotionLabel`. RLS triad (tenant_isolation w/ NULLIF,
  provider_bypass, worker_bypass) + FORCE on all three. `IPromotionRuleRepository` + DI.
- Engine `Application/Features/Promotions/PromotionEngine.cs` (pure, deterministic; algorithm in the
  class summary) + `PromotionDefinition` (camelCase JSON). Golden vectors
  `backend/ShelfGuard.Tests/Promotions/golden-vectors.json` (37 vectors, schema in `_comment`, copied
  to test output) - shared with the future TS mirror.
- `PosService.CreateSaleCoreAsync`: published rules evaluated after item resolution (batch sales at
  OccurredAt if within -14d/+5min, else now); promo lines split into sub-lines
  (DiscountAmount = retail - promo price); FEFO consumption per whole line (stock events unchanged),
  each sub-line points at the batch of its first unit; application log staged in the same commit;
  fiscal receipt built from sub-lines; replay check skips the line-count test only when stored
  promo sub-lines exist (per-product quantity check still authoritative). Lines with no rule
  segment keep the exact legacy stock/tier maths. `SaleItemDto` += `PromotionRuleId/Label`.
- `GET /api/pos/sync/promotions?etag=&storeId=` (register store for operator tokens) ->
  `{ etag, notModified, serverTime, rules[{id, versionId, name, createdAt, definition}] }`.
  Catalog sync item += `categoryId`.

## Decisions
- Priority tie-break: LOWER number wins, then older rule, then id. Tier wins a price tie vs a rule.
- Buy units of a buy_n_get_m group pay the pre-promo price (no tier on them); a group is taken only
  if cheaper than the units' best per-unit prices. validTo / timeTo exclusive.
- Store-local time = UTC (locations have no time zone) - `PosService.StoreUtcOffsetMinutes`.
- Rules evaluated at OccurredAt use the CURRENT published version (no publish history yet).

## Status
dotnet build OK. Full `dotnet test` 2634/2634 green against a throw-away DB with all migrations
applied (`crm_promo_verify`, dropped afterwards). On the shared dev DB (:5435) the new migration is
NOT applied yet -> ~39 real-Postgres tests fail with "column PromotionLabel does not exist" until
the API is started once.

## Follow-ups
- Cashier must do one full catalog resync (since=null) to receive `categoryId` for old rows.
- Offline re-pricing difference report in the batch result (doc) not built.
- openapi.json not regenerated.
