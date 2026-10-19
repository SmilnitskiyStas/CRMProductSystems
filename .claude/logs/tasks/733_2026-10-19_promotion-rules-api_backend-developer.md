# TASK-733 Promotion rules Phase 2 API (backend)

- `Features/Promotions/`: `IPromotionRuleService`/`PromotionRuleService` (+`.Validation.cs`), `PromotionRuleDtos.cs`; `PromotionRulesController` (`api/promotion-rules`, AtLeastStoreManager, RequireModule pos); DI registered.
- Contract implemented as FINAL (routes, shapes, codes). Extra: `POST /{id}/resume`, `lookups/items` per contract.
- Versions: immutable. Save while current != published => insert v(max+1), repoint CurrentVersionId, DELETE the superseded unpublished draft (only UPDATE is blocked by the trigger) - no orphan drafts, numbers monotonic. Save while current == published => just adds v(max+1).
- Repo additions (no migration): `ListAsync(status)`, `GetVersionsByIdsAsync`, `RemoveVersionAsync`.
- Store scope: store/network manager = user_locations + primary store; rule visible only if current AND published definitions have non-empty storeIds within scope, else 404; save with empty/foreign storeIds => 400 `{errors:[store_scope_violation]}`; other roles see nothing. Simulate for a scoped manager with a store outside scope => 403.
- Notes: `price_below_minimum` also covers fixed_off amount and getPrice < 0.01; `invalid_period` also covers bad weekdays/time format; unknown type/audience kind/matchMode => `missing_param`. Publish validation failure => 400 with `{errors, warnings}`; other 400s `{error}`.
- Tests: `Promotions/PromotionRuleServiceTests.cs` (fakes), `PromotionRuleFlowIntegrationTests.cs` (real Postgres, incl. PosSyncService). FakePromotionRepo in PosServiceTests got the 3 new members.
- Build OK; full `dotnet test`: 2666 passed, 0 failed.
