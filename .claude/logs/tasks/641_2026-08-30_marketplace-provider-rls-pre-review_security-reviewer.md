# TASK-641 — Pre-implementation threat model: marketplace `SetProviderRoleAsync` cross-tenant RLS leak

**Agent:** security-reviewer · **Model:** opus · **Date:** 2026-08-30 · **Status:** done
**Scope:** analysis only — no production code written.
**Plan reviewed:** `C:\Users\stass\.claude\plans\snappy-dreaming-hanrahan.md`

**VERDICT: SHIP-WITH-CHANGES** (7 required changes, §7).

---

## 1. Read leak and F2 write vector — CONFIRMED from source

### Mechanism (root cause, confirmed)

`MarketplaceRepository.SetProviderRoleAsync` — `MarketplaceRepository.cs:410-419`:

```csharp
var conn = _db.Database.GetDbConnection();
if (conn.State != System.Data.ConnectionState.Open)
    await conn.OpenAsync(ct);              // ← EF now treats the connection as externally-owned
await using var cmd = conn.CreateCommand();
cmd.CommandText = "SET app.role = 'provider';";   // ← session-level, not SET LOCAL, no transaction
await cmd.ExecuteNonQueryAsync(ct);               // ← never reset
```

Three independent defects compound:
1. `SET` (not `SET LOCAL`) with no enclosing transaction → the GUC persists for the whole session.
2. `conn.OpenAsync` makes the connection externally-owned → EF stops closing it after each query →
   `TenantConnectionInterceptor.ConnectionOpenedAsync` (`TenantConnectionInterceptor.cs:43-51`) never
   re-fires to restore the caller's real role.
3. Nothing resets it. Every subsequent statement in the same HTTP request runs as `app.role='provider'`.

### Policy shape (verified live on dev Postgres :5435, `pg_policies`)

```
items | provider_bypass  | PERMISSIVE | ALL | qual = current_setting('app.role',true) = ANY(ARRAY['provider','provider_admin'])
                                            | with_check = NULL  ← defaults to qual ⇒ WRITE bypass too
items | tenant_isolation | PERMISSIVE | ALL | qual = "TenantId" = NULLIF(current_setting('app.tenant_id',true),'')::uuid
items | worker_bypass    | PERMISSIVE | ALL | ...
```

PERMISSIVE policies OR together ⇒ under the leaked role every row of every tenant is visible **and
writable**. **F1 confirmed.**

Bonus datapoint (verified): `items.tenant_isolation` on **dev** is already fail-closed (no
`IS NULL` OR-branch). Part C's dev half is a no-op; prod still needs the live check (task #2).

### Read leak — exact chain (the reported symptom)

`POST /api/marketplace/suppliers/{id}/orders/conflicts`
→ `MarketplaceCooperationController.cs:169`
→ `MarketplaceOrderService.CheckCatalogConflictsAsync` (`MarketplaceOrderService.cs:188`)
→ `:195` `_marketplace.GetSupplierTenantIdAsync` → `MarketplaceRepository.cs:271` **SetProviderRoleAsync — leak starts**
→ `:203` `_marketplace.GetSupplierItemsAsync` → `MarketplaceRepository.cs:76` (leak re-applied)
→ `:217` `_items.GetByAnyBarcodeAsync(barcodes)` → `ItemRepository.cs:171-183` — **no app-level `TenantId` filter**
→ `:218` `matches.FirstOrDefault()` — **no tenant filter either**
→ `:222-224` `new MarketplaceOrderConflictingItemDto(match.Id, match.Name, match.ImageUrl, match.Barcodes)`

**CONFIRMED.** A client tenant with an empty catalog gets a "barcode collision" dialog listing a
**foreign tenant's** `Item.Id`, `Name`, `ImageUrl` and full barcode list. Confidentiality impact:
cross-tenant catalog disclosure (product names, images, EANs) — and, critically, the **primary key**
that arms F2.

### F2 write vector — CONFIRMED, and it is self-contained

`POST /api/marketplace/suppliers/{id}/orders` with `catalogAction:"link"`:

1. `MarketplaceOrderService.cs:99` `GetSupplierTenantIdAsync` → **leak starts**.
2. `:109` `GetSupplierItemsAsync` (leak re-applied).
3. `:125` → `PlanCatalogOutcomeAsync` `:424`
   - `:435` `_items.GetByIdAsync(line.LinkedItemId.Value)` → `ItemRepository.cs:134-139`, no tenant
     filter, `provider_bypass` USING = true ⇒ **resolves a foreign-tenant `Item`**.
   - The doc comment at `:417-419` explicitly and **wrongly** relies on this being impossible:
     *"ambient RLS on GetByIdAsync already enforces this — a foreign-tenant id resolves to null"*.
   - `:439` barcode-intersection guard passes trivially, because the attacker took `linkedItemId`
     from step (0) — the conflicts endpoint returned that row **precisely because** it shares a
     barcode with the supplier item.
4. `:151` → `ExecuteCatalogPlanAsync` `:472`
   - `:476` `plan.LinkedItem!.SourceSupplierItemId = plan.SupplierItem.Id;`
   - `:477` `_items.Update(plan.LinkedItem)` → `:478` `_items.SaveChangesAsync`
   - `items.provider_bypass` `WITH CHECK` defaults to its `USING` (true) ⇒ **UPDATE on the foreign
     tenant's row succeeds.**

**F2 CONFIRMED.** Preconditions are only: (a) one ACTIVE `SupplierAgreement` with any supplier,
(b) a supplier item whose EAN also exists in a victim tenant's catalog. Both are ordinary. No id
guessing — the attacker's own earlier API response supplies the target id. **Severity: critical.**

**Extra blast radius the plan does not mention:** `ItemRepository.GetByIdAsync` `.Include`s
`Category`, `Segment`, `DefaultSupplier`. `DbSet.Update(entity)` marks the whole reachable graph
`Modified`, so the flush also emits full-row `UPDATE`s against the foreign tenant's `categories`,
`product_segments` and `suppliers` rows. Values are round-tripped so no field changes, but it is a
genuine cross-tenant **full-row rewrite / lost-update** primitive on 4 tables, not 1.

**Why unit tests are green:** `MarketplaceOrderServiceTests` mocks `IItemRepository`, so real RLS is
never exercised. Worse, the existing test `CreateOrder_LinkAction_LinkedItemNotOwnedByTenant_
ReturnsError` (`MarketplaceOrderServiceTests.cs:385-409`) **encodes the disproved assumption** in its
own comment at `:397-398` and stubs `GetByIdAsync → null`. It must be rewritten, not extended.

---

## 2. Every endpoint reachable through the 13 `SetProviderRoleAsync` call sites

The 13 bypass methods (`MarketplaceRepository.cs`): `GetPublicSuppliersAsync` :33,
`CountPublicSuppliersAsync` :49, `GetSupplierByIdAsync` :56, `GetSupplierItemsAsync` :76,
`GetSupplierItemImagesByIdsAsync` :94, `SearchSuppliersAsync` :110, `GetSupplierByRawIdAsync` :265,
`GetSupplierTenantIdAsync` :271, `GetReviewRatingsAsync` :350, `GetReviewsBySupplierAsync` :361,
`CountReviewsBySupplierAsync` :378, `GetReviewByIdAsync` :387, `GetMetricsBySupplierIdAsync` :397.
Plan's list is accurate and complete (verified by grep of all `_repo.`/`_marketplace.`/
`_marketplaceRepo.` call sites across `ShelfGuard.Application` + `ShelfGuard.Api`).

`SupplierTaskService` also injects `IMarketplaceRepository` but only calls
`GetOwnerManagedProfileAsync` (`SupplierTaskService.cs:122`) — **not** a bypass method. No task
endpoint is affected.

### Endpoint table — "tenant-scoped SaveChangesAsync AFTER a provider-bypass read?"

| # | Endpoint | Auth | Bypass methods hit | Post-bypass `SaveChangesAsync`? |
|---|---|---|---|---|
| 1 | `GET /api/marketplace/suppliers` | AllowAnonymous | GetPublicSuppliers, CountPublicSuppliers | no |
| 2 | `GET /api/marketplace/suppliers/{id}` | AllowAnonymous | GetSupplierById, GetReviewRatings | no |
| 3 | `GET /api/marketplace/suppliers/{id}/items` | AllowAnonymous | GetSupplierById ×1, GetSupplierItems | no |
| 4 | `POST /api/marketplace/search` | AllowAnonymous | SearchSuppliers | no |
| 5 | `GET /api/marketplace/suppliers/{id}/reviews` | AllowAnonymous | GetSupplierById, GetReviewsBySupplier, CountReviewsBySupplier | no |
| 6 | `POST /api/marketplace/suppliers/{id}/reviews` | Authorize + marketplace | GetSupplierByRawId, GetReviewRatings, GetMetricsBySupplierId | **YES ×2** — `MarketplaceService.cs:102` (own-tenant, cat. b) and `:158` (**cross-tenant = W1**) |
| 7 | `POST /api/marketplace/ai-recommend` | Authorize + marketplace | SearchSuppliers, GetPublicSuppliers | no writes — but see **F5** below |
| 8 | `GET /api/marketplace/suppliers/{id}/chat/messages` | Authorize + marketplace | GetSupplierTenantId (`MarketplaceChatController.cs:44`) | **YES** — `SupplierChatService.cs:57` session insert + `SupplierChatRepository.cs:94` `ExecuteUpdateAsync` mark-read (both cat. c) |
| 9 | `POST /api/marketplace/suppliers/{id}/chat/messages` | Authorize + marketplace | GetSupplierTenantId (`:68`) | **YES** — `SupplierChatService.cs:57` + `:161` (both cat. c) |
| 10 | `POST /api/marketplace/suppliers/{id}/cooperation-requests` | Authorize + marketplace | GetSupplierTenantId (`SupplierAgreementService.cs:79`) | **YES** — `SupplierAgreementService.cs:118` (cat. c) |
| 11 | `POST /api/marketplace/suppliers/{id}/orders` | Authorize + marketplace | GetSupplierTenantId :99, GetSupplierItems :109 | **YES ×N+1** — `MarketplaceOrderService.cs:478` (**F2 cross-tenant**), `ItemService.cs:110` (cat. b), `:176` (cat. c) |
| 12 | `POST /api/marketplace/suppliers/{id}/orders/conflicts` | Authorize + marketplace | GetSupplierTenantId :195, GetSupplierItems :203 | no writes (read leak only) |
| 13 | `GET /api/marketplace/orders/awaiting-receipt` | Authorize + marketplace | GetSupplierItemImagesByIds (`MarketplaceOrderReceiptService.cs:360`) | no |
| 14 | `POST /api/marketplace/orders/{id}/receipt` | + CanReceiveStock | GetSupplierItemImagesByIds — **only inside `ToDtoAsync`, after the write** (`:82`, `:119`) | no (bypass is last) |
| 15 | `GET /api/marketplace/orders/{id}/receipt` | Authorize + marketplace | GetSupplierItemImagesByIds (`:130`) | no |
| 16 | `PUT /api/marketplace/orders/{id}/receipt/items/{itemId}` | + CanReceiveStock | GetSupplierItemImagesByIds — after the write (`:176-177`) | no (bypass is last) |
| 17 | `POST /api/marketplace/orders/{id}/receipt/finalize` | + CanReceiveStock | GetSupplierItemImagesByIds — after everything (`:303-304`) | no (bypass is last) |
| 18 | `POST /api/marketplace/suppliers/{id}/support-tickets` | Authorize + marketplace | GetSupplierTenantId (`SupplierSupportService.cs:59`) | **YES** — `:85` (cat. c) |
| 19 | `GET /api/supplier-cabinet/profile` | SupplierCabinet + marketplace_supplier | GetMetricsBySupplierId (`SupplierCabinetService.cs:50`) | no |
| 20 | `POST /api/supplier-cabinet/items` | SupplierCabinet | GetSupplierByRawId (`MarketplaceService.cs:231`) | **YES** — `:264` (own tenant, cat. b) |
| 21 | `GET /api/supplier-cabinet/reviews` | SupplierCabinet | GetReviewsBySupplier, CountReviewsBySupplier | no |
| 22 | `GET /api/supplier-cabinet/metrics` | SupplierCabinet | GetMetricsBySupplierId (`:176`) | no |
| 23 | `PUT /api/supplier-cabinet/reviews/{id}/reply` | SupplierCabinet | GetReviewById (`SupplierCabinetService.cs:206`) | **YES** — `:212` (**cross-tenant = W2**) |
| 24 | `GET /api/supplier-cabinet/reviews/stats` | SupplierCabinet | GetReviewRatings (`:226`) | no |
| 25 | `GET /api/supplier-cabinet/clients` | SupplierCabinet | CountReviewsBySupplier, GetReviewsBySupplier | no |
| 26 | `POST /api/admin/marketplace/suppliers/{id}/items` | **ProviderOnly** | GetSupplierByRawId | YES, but the session role is already `provider` from the JWT — unaffected either way |
| 27 | `DELETE /api/admin/marketplace/suppliers/{id}/items/{itemId}` | **ProviderOnly** | none (`GetSupplierItemByIdAsync` has no bypass) | relies on JWT provider role — unaffected |

**Not affected at all** (no bypass method anywhere in the request): every
`SupplierCabinetCooperationController` endpoint (approve / reject / regenerate-contract /
send-to-vchasno / **mark-signed** / terminate / contract-settings / orders / order status /
delay-reason / support tickets), `GET /api/marketplace/cooperation*`,
`POST cooperation/{id}/signing-method`, `GET my-orders`, `POST orders/{id}/cancel`,
`GET my-support-tickets`, `GET support-tickets/{id}`, `POST support-tickets/{id}/messages`,
all `/api/supplier-cabinet/{staff,roles,tasks,chat}` routes, and
`/api/settings/supplier-profile` (GET/PUT).

This matters for §5: none of the five `ITenantSessionOverride` lambdas sits on a request that has
already gone through a marketplace bypass method, and none of them contains one.

---

## 3. Classification of every downstream `SaveChangesAsync`

Live `pg_policies` shapes (dev, verified) that drive the classification:

| Table | `tenant_isolation` qual |
|---|---|
| `marketplace_orders`, `marketplace_order_items`, `supplier_agreements`, `supplier_support_tickets`, `supplier_chat_sessions` | `SupplierTenantId = session **OR** ClientTenantId = session` |
| `supplier_support_ticket_messages`, `supplier_chat_messages` | `EXISTS(parent WHERE Supplier… OR Client… = session)` |
| `supplier_metrics`, `supplier_reviews`, `supplier_items`, `supplier_profiles`, `suppliers`, `items`, `product_stock`, `stock_movements`, `supplier_tasks`, `supplier_contract_settings`, `integration_configs` | plain single-tenant `TenantId = session` |
| `notification_queue` | `TenantId = session OR TenantId IS NULL` |
| `marketplace_order_receipts` | `ClientTenantId = session` (+ separate `supplier_read` SELECT policy) |

Legend — **(a)** must run under provider bypass · **(b)** own-tenant write that today only *happens*
to run under the leak · **(c)** unaffected (never on a leaked request, or its policy is OR-based so
the ambient session already satisfies it).

### `MarketplaceService`
| Site | Writes | Class |
|---|---|---|
| `:102` `CreateReviewAsync` | INSERT `supplier_reviews`, `TenantId` = reviewer = session | **(b)** — passes `tenant_isolation` on its own. Safe. |
| `:158` `RecalculateRatingAsync` | UPDATE **or** INSERT `supplier_metrics`, `TenantId` = **supplier** tenant ≠ session | **(a) — W1 CONFIRMED.** Plain single-tenant policy ⇒ UPDATE affects 0 rows (`DbUpdateConcurrencyException`) or INSERT → 42501. |
| `:211` `UpdateOwnProfileAsync` | own `supplier_profiles` | **(c)** — no bypass on that request |
| `:264` `AdminAddSupplierItemAsync` | `supplier_items`, `TenantId` = `supplier.TenantId` | **(b)** via cabinet (own tenant). Via `MarketplaceAdminController` the caller is ProviderOnly and gets `provider` from the JWT — **(c)**. |
| `:334` `AdminUpdateSupplierItemAsync` | `supplier_items` + barcodes/images | **(c)** — no bypass method on that path |
| `:406` `AdminDeleteSupplierItemAsync` | DELETE `supplier_items` | **(c)** |

W1 note the plan under-states: the **INSERT** branch (`AddMetricsAsync`, `MarketplaceService.cs:150`)
is cross-tenant too, not just the UPDATE. The plan's `UpsertMetricsRatingAsync` covers both — keep it
that way.

### `SupplierCabinetService`
| Site | Writes | Class |
|---|---|---|
| `:81` `UpdateProfileAsync`, `:98` `TogglePublishAsync` | own `supplier_profiles` | **(c)** |
| `:212` `ReplyToReviewAsync` | UPDATE `supplier_reviews`, row's `TenantId` = **reviewer** ≠ session (supplier) | **(a) — W2 CONFIRMED.** |
| `:352` `InviteStaffAsync` (`_userRepo`) | own `users` | **(c)** |
| `ResolveAsync` → `GetOrCreateOwnerManagedProfileAsync` (`MarketplaceRepository.cs:302`) | own `suppliers` + `supplier_profiles` | **(c)** — always runs *before* any bypass |

W2 is certain, not probable: `supplier_admin` is **not** in
`TenantConnectionInterceptor.ValidRoles` (`TenantConnectionInterceptor.cs:22-37`), so a cabinet
session has **no** `app.role` set at all. Once the leak is removed there is no role value that could
accidentally satisfy `provider_bypass`, and the UPDATE will deterministically hit 0 rows.

### `MarketplaceOrderService`
| Site | Writes | Class |
|---|---|---|
| `:478` `ExecuteCatalogPlanAsync` (link) | UPDATE `items` (+ graph: `categories`, `product_segments`, `suppliers`) | **(b)** for the legitimate own-tenant case; **the F2 attack vector** for the foreign case. Part B's re-validation is the fix. |
| `ItemService.cs:110` via `ExecuteCatalogPlanAsync` (create) | INSERT `items`, `TenantId` = `clientTenantId` = session | **(b)** — safe |
| `:176` `CreateOrderAsync` | INSERT `marketplace_orders` + `marketplace_order_items` (foreign `SupplierTenantId`) | **(c)** — OR-based policy |
| `:255` `CancelOrderAsync` | `marketplace_orders` | **(c)** — no bypass on that request |
| `:328` / `:369` (inside `ITenantSessionOverride`) | `marketplace_orders` + `notification_queue` | **(c)** — supplier-side endpoints, no bypass on the request |

### `MarketplaceOrderReceiptService`
All writes are **(c)**. Verified ordering: in `GetOrCreateDraftAsync` (`:116` write → `:119`
`ToDtoAsync`), `UpdateItemAsync` (`:174` write → `:176-177`), and `ReceiveAsync` (`:267` write,
`:289-300` override, then `:303-304`), the only bypass call
(`GetSupplierItemImagesByIdsAsync`, reached from `ToDtoAsync` `:360`) always runs **after** every
write. Nothing here depends on the leak.

### `SupplierAgreementService`
`:118` `SubmitRequestAsync` is the only write downstream of a bypass (`:79`
`GetSupplierTenantIdAsync`). `supplier_agreements.tenant_isolation` is OR-based and the row carries
`ClientTenantId = session` ⇒ **(c)**. All other writes (`:226`, `:264`, `:287`, `:316`, `:359`,
`:397`, `:453`, `:493`, `:527`) are on supplier-cabinet endpoints that never touch a bypass method ⇒
**(c)**.

### `SupplierChatService`
`:57` (session insert), `:161` (client→supplier message + session bump) and
`SupplierChatRepository.cs:94-97` `MarkMessagesReadAsync` (`ExecuteUpdateAsync`) all run downstream of
`MarketplaceChatController.cs:44/:68`'s `GetSupplierTenantIdAsync` leak. All three targets
(`supplier_chat_sessions` OR-based; `supplier_chat_messages` EXISTS-over-OR-based) are satisfied by
the client's own ambient session ⇒ **(c)**. `:152`'s override block is supplier-side only ⇒ **(c)**.

### `SupplierSupportService`
`:85` `CreateTicketAsync` runs downstream of `:59`'s leak; `supplier_support_tickets` is OR-based and
`supplier_support_ticket_messages` is an EXISTS over it ⇒ **(c)**. `:169` `AddMessageAsync`, `:197`
`UpdateStatusAsync` — no bypass on those requests ⇒ **(c)**. `CreateSystemTicketAsync` deliberately
does not save ⇒ **(c)**.

### `SupplierTaskService`
No bypass method used ⇒ all writes **(c)**.

### `MarketplaceChatController`
No writes of its own; classified under `SupplierChatService`.

### Verdict on the plan's F3 hypothesis
**W1 and W2 are both real and are the ONLY category-(a) sites.** No third one exists. Every other
downstream write is (b) or (c) and will keep working after the fix — because the marketplace's
genuinely cross-tenant tables (`marketplace_orders`, `marketplace_order_items`,
`supplier_agreements`, `supplier_support_tickets`, `supplier_chat_*`) were all deliberately given
OR-based `tenant_isolation` policies, and only `supplier_metrics` / `supplier_reviews` were left on
the plain single-tenant shape.

---

## 4. Keep `'provider'`, no sentinel — **RATIFIED, with two conditions**

The plan's argument holds:

- ADR-028 minted `marketing_analytics_bypass` because analytics needed a bypass on tables that had
  no bypass value it could reuse — that was **widening** a policy, so it needed a migration.
- Here `provider_bypass` **already exists** and `MarketplaceRepository` **already** sets
  `app.role='provider'`. Wrapping it in `SET LOCAL` inside a short transaction strictly **narrows**
  the window from "rest of the HTTP request" to "one transaction". No new row becomes reachable and
  no policy changes. A migration would buy nothing for correctness.
- Cross-check for a hidden dependency: `app.role` is **never** read as an authorization input outside
  RLS policies. `[Authorize(Policy = ProviderOnly)]` reads the JWT claim, not the DB GUC. Introducing
  the value inside a transaction therefore cannot elevate anything at the application layer.
- Provider-role callers are unaffected: `TenantConnectionInterceptor.cs:104-105` still sets
  `app.role='provider'` session-wide from their JWT, so `MarketplaceAdminController` keeps working.

**But the plan understates the residual risk, and the ADR must record it accurately.** I measured it:
`SELECT count(*) FROM pg_policies WHERE policyname='provider_bypass'` → **107**. The `'provider'`
value unlocks a full read+write bypass on **107 tables**, not the ~8 marketplace ones. That is fine
while every block body is a single query inside one repository, and it is exactly the property that
makes the sentinel a real (deferred) hardening rather than bikeshedding.

**Conditions (both required):**
- **C1.** The `IProviderRlsOverride` XML doc must state the 107-table blast radius as a number and
  state the rule "one repository, one query, no outward calls" as a security invariant — not
  "marketplace tables only", which is false.
- **C2.** ADR-035 must record the sentinel as *deferred hardening* with that same count and the
  concrete trigger for revisiting it (any new call site outside `MarketplaceRepository`, or any block
  body that touches a non-marketplace table).

---

## 5. Ambient transactions — CONFIRMED SAFE, with two corrections to F4

**Correction 1 — there are 4 `BeginTransactionAsync` sites in the backend, not 3.** The plan missed
`ShelfGuard.Api/Controllers/MobileCatalogSettingsController.cs:76`. Verified harmless: its
transaction body (`:77-101`) only touches `MobileCatalogSettings` / `MobileCatalogItems` /
`MobileCatalogLocations` and never resolves `IMarketplaceRepository`. The other three are as the plan
says (`LoyaltyRepository.cs:56`, `AnalyticsRlsOverride.cs:19`, `TenantSessionOverride.cs:19`).
`ShelfGuard.Tools.PchilkaImport/ImportRunner.cs:566` also does a `SET LOCAL app.role` but is an
offline CLI tool outside the request pipeline.

**Correction 2 — nothing else in the codebase does a raw `SET`/`SET ROLE` on a live connection.**
Grep for `GetDbConnection` outside tests returns exactly two hits: the bug itself
(`MarketplaceRepository.cs:412`) and `Program.cs:251`, which is startup-only (the KI-028 RLS-role
canary, runs once before the host starts serving). So deleting `SetProviderRoleAsync` really does
remove the last per-request raw-connection mutation. The plan's review criterion ("no
`GetDbConnection()` left in the file") is the right gate.

**Re-verified the two lambdas the plan called out:**
- `SupplierAgreementService.cs:394-399` → `EnqueueSignedNotificationAsync` (`:409-434`) →
  `_tenantNames.GetTenantDisplayNameAsync` (`ISupplierChatRepository`) + `_notifications.EnqueueAsync`
  + `_agreements.SaveChangesAsync`. **No `IMarketplaceRepository` reachable.** ✅
- `SupplierChatService.cs:152-157` → `EnqueueSupplierMessageNotificationAsync` (`:172-198`) →
  `_notifications.EnqueueAsync` + `_repo.SaveChangesAsync` (`ISupplierChatRepository`).
  **No `IMarketplaceRepository` reachable.** ✅

**Also verified the other three override lambdas** (the plan only named two):
- `MarketplaceOrderService.cs:323-328` and `:366-371` → `Enqueue*NotificationAsync` → `_tenantNames`
  + `_notifications` + `_orders`. ✅
- `MarketplaceOrderReceiptService.cs:289-300` → `_supplierSupport.CreateSystemTicketAsync` (which
  reaches `SupplierSupportService.ToDtoAsync` → `_orders` + `_tenantNames`, **not** the marketplace
  repo) + `EnqueueDiscrepancyTicketNotificationAsync` + `_receipts.SaveChangesAsync`. ✅

**Conclusion:** zero nesting risk today, in either direction. The plan's decision to let EF throw
`InvalidOperationException` loudly rather than silently join an ambient transaction is correct — but
see required change R4.

---

## 6. Additional findings the implementer must know

### F5 — cross-tenant **Claude API key** consumption via `POST /api/marketplace/ai-recommend` (NEW, high)

`SupplierAdvisor.ResolveAsync` (`ShelfGuard.Infrastructure/AI/SupplierAdvisor/SupplierAdvisor.cs:40-61`):

```csharp
var row = await _db.IntegrationConfigs
    .Where(i => i.Service == "claude" && i.IsEnabled)
    .Select(i => i.Config)
    .FirstOrDefaultAsync(ct);          // ← no TenantId filter, no ORDER BY
```

`integration_configs` has a `provider_bypass` policy (verified live). In
`MarketplaceController.AiRecommend`:

- `:173` `IsConfiguredAsync` → `ResolveAsync` — runs **before** any leak, correctly tenant-scoped.
- `:178` `SearchSuppliersAsync` → **leak starts.**
- `:202` `_advisor.RecommendAsync` → `SupplierAdvisor.cs:68` `ResolveAsync(ct)` again → now under
  `app.role='provider'` → the unordered `FirstOrDefaultAsync` can return **another tenant's**
  Claude `api_key`, which is then used to make a live outbound Anthropic call.

Impact: another tenant's paid API key is silently spent (billing/quota abuse), and the secret value
crosses a tenant boundary inside the process. It is not rendered to the client, so this is
misuse-not-disclosure, but it is a real cross-tenant secret-material leak.

**This is fixed for free by Part A** (the leak is what enables it). No extra work is required — but
it must be listed in KI-036's blast radius, and it is a second independent argument for keeping the
`items` app-level filters *and* for never treating "no writes on this endpoint" as "not affected".

### F6 — second copy of the disproved comment

`MarketplaceOrderReceiptService.cs:153-154` carries the same falsified claim as
`MarketplaceOrderService.cs:417-419`:

> *"Defence in depth: GetByIdAsync is RLS-scoped to the ambient (client) session, so a ProductId
> belonging to another tenant can never resolve here even if guessed."*

I traced `PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}` end to end: no bypass method
runs before `:155`, so it is **not exploitable today**. But the comment states an invariant that is
only true by accident of call ordering, and a future refactor that hoists a supplier lookup earlier
would silently turn it into a second IDOR. The plan only rewrites the `MarketplaceOrderService`
comment.

### F7 — connection-pool reset is a load-bearing, undocumented invariant

The leak is bounded to one HTTP request only because Npgsql's pool sends `DISCARD ALL` on connection
return (default `No Reset On Close=false`; the dev/prod connection strings do not override it —
`appsettings.Development.json:4`, `DependencyInjection.cs:30-37`). If anyone ever sets
`No Reset On Close=true` as a perf tweak, the leak becomes **cross-request**, and
`TenantConnectionInterceptor.BuildSetSql` would not save it: when the JWT role is absent or not
whitelisted, **no `SET app.role` is emitted at all** (`TenantConnectionInterceptor.cs:104-105`) — the
stale value would simply survive. `supplier_admin` is precisely such a role (it is **not** in
`ValidRoles`, `:22-37`), so supplier-cabinet requests are the ones that would inherit it.

After this fix the exposure is moot for `app.role`, but the invariant is worth one line in ADR-035
and one in `backend-structure.md`, because it constrains a plausible future ops change.

### F8 — `GetReviewByIdAsync` becomes dead code

Once W2 moves into the composite `SetReviewReplyAsync`, `IMarketplaceRepository.GetReviewByIdAsync`
has **zero** production callers (verified: only `SupplierCabinetService.cs:206` today). Leaving a
`provider`-bypass cross-tenant single-row read on the interface with no caller is exactly the
"repurposable escape hatch" the post-implementation review (agent #5) is meant to prevent. It should
be **deleted**, not converted to `AsNoTracking()`.

### F9 — plan's `AsNoTracking()` changes: verified safe

- `GetSupplierByRawIdAsync` — callers `MarketplaceService.cs:78` and `:231` read `.Id`/`.TenantId`
  only. Safe, and **required**: today it loads a *foreign-tenant* `Supplier` into the shared change
  tracker, which then sits there across `CreateReviewAsync`'s `SaveChangesAsync` at `:102`.
- `GetMetricsBySupplierIdAsync` — remaining callers after W1 moves are
  `SupplierCabinetService.cs:50` and `:176`, both read-only. Safe.
- `GetReviewByIdAsync` — see F8, delete instead.

### F10 — the two composite methods' documented invariant is currently satisfied

Verified for both: at the moment `UpsertMetricsRatingAsync` would run, `CreateReviewAsync` has
already flushed the review (`MarketplaceService.cs:101-102`) and (with F9 applied) holds no dirty
foreign entity; at the moment `SetReviewReplyAsync` would run, `ReplyToReviewAsync` has staged
nothing and `ResolveAsync`'s own-tenant entities are `Unchanged`. The plan's invariant is real
today — keep it as an XML-doc contract plus a review criterion, since it is not enforced by the type
system.

### F11 — behavioural side effects worth stating in the ADR
- `GET /api/marketplace/suppliers/{id}` and `/items` and `/reviews` each open **2-3 short explicit
  transactions** where they previously opened none (`IsPublishedAsync` + the payload query, or
  rows + count). Consistency is unchanged (they were already separate statements); the cost is a few
  extra round-trips on anonymous endpoints.
- `SearchSuppliersAsync` (`:110-138`) issues **two** dependent queries and **must** stay in **one**
  block — the plan says this; it is load-bearing, not cosmetic.
- `GetSupplierItemImagesByIdsAsync`'s `Count == 0` early return (`:91-92`) must stay **outside** the
  block so the common empty case still costs zero transactions.

---

## 7. Verdict — **SHIP-WITH-CHANGES**

The plan's diagnosis is correct, its root-cause fix is the right shape, its precedent
(`IAnalyticsRlsOverride` / ADR-028) is the right one to mirror, and its W1/W2 hypothesis survives
scrutiny — I found no third cross-tenant write it missed. Part B's three app-level `clientTenantId`
filters are proportionate and correctly sourced from the JWT
(`MarketplaceCooperationController.cs:166`, never the request body).

**Required changes before merge:**

- **R1.** Delete `IMarketplaceRepository.GetReviewByIdAsync` + its implementation entirely rather
  than converting it to `AsNoTracking()` — it has no caller once W2 moves (F8). Update
  `MarketplaceRepositoryTests`/cabinet tests accordingly.
- **R2.** `IProviderRlsOverride`'s XML doc must state the measured blast radius —
  **107 tables carry a `provider_bypass` policy** — and the invariant "called only from
  `MarketplaceRepository`; each block wraps exactly one repository operation and makes **no** call
  out to another service, repository or `ITenantSessionOverride`" (condition C1, §4).
- **R3.** ADR-035 must record the sentinel decision with that same 107-table figure and a concrete
  revisit trigger, not a bare "rejected / future hardening" (condition C2, §4).
- **R4.** Add an explicit statement (XML doc + ADR) that `IProviderRlsOverride` must never be invoked
  from inside an `ITenantSessionOverride` / `IAnalyticsRlsOverride` lambda, and add one unit test
  asserting `IProviderRlsOverride` is resolved by no type other than `MarketplaceRepository` (a
  simple DI/assembly-scan or constructor-parameter assertion). §5 shows this holds today; nothing
  currently keeps it true.
- **R5.** Fix the second copy of the disproved comment at
  `MarketplaceOrderReceiptService.cs:153-154` (F6) in the same change as
  `MarketplaceOrderService.cs:417-419`. Both encode the same wrong invariant; leaving one is how the
  next instance of this bug gets written.
- **R6.** KI-036 must list the full blast radius, not just `items`:
  (i) `_items.Update` writes the loaded **graph**, so F2 also rewrites foreign
  `categories` / `product_segments` / `suppliers` rows (§1);
  (ii) **F5** — cross-tenant Claude API-key consumption on `POST /api/marketplace/ai-recommend`
  (§6). F5 should be called out explicitly as resolved-by-this-fix so nobody re-derives it later.
- **R7.** Record F7 (Npgsql `DISCARD ALL` pool reset is load-bearing; `supplier_admin` is not in
  `TenantConnectionInterceptor.ValidRoles`, so a non-resetting pool would carry a stale `app.role`
  into cabinet requests) in ADR-035 Consequences and `backend-structure.md`.

**Test-plan additions for the QA agent (#4), beyond the plan's list:**
- Regression proving the leak's *scope*, not just its effect: on the same open connection, after
  `GetSupplierItemsAsync`, assert `current_setting('app.role', true)` equals the session role
  (`'store_manager'`) — the plan has this; keep it, it is the single highest-value assertion.
- **New:** an F2 end-to-end negative control that asserts the foreign `Item`'s
  `SourceSupplierItemId` **and** its `UpdatedAt`-equivalent columns are untouched, and that no row in
  `categories` / `suppliers` of the third tenant was rewritten.
- **New:** a W1 test that exercises the **INSERT** branch of `supplier_metrics` (first-ever review
  for a supplier), not only the UPDATE branch — the plan's file-2 test as written could pass on
  UPDATE alone.
- The plan's step-5 "prove it fails before the fix" discipline is correct and must not be skipped;
  note `git worktree` is unusable on this repo on Windows (MAX_PATH on old migration filenames) —
  use `git stash -u`.

**Nothing here blocks starting implementation.** R1-R7 are all additive to the plan as written.

---

## Verification performed
- Read in full: `MarketplaceRepository.cs`, `MarketplaceOrderService.cs`, `MarketplaceService.cs`,
  `SupplierCabinetService.cs`, `SupplierAgreementService.cs`, `SupplierSupportService.cs`,
  `SupplierChatService.cs`, `ItemRepository.cs`, `ItemService.cs` (create path),
  `TenantConnectionInterceptor.cs`, `AnalyticsRlsOverride.cs`, `TenantSessionOverride.cs`,
  `MarketplaceController.cs`, `MarketplaceChatController.cs`, plus the relevant halves of
  `MarketplaceOrderReceiptService.cs` and every marketplace controller's route/attribute list.
- Live `pg_policies` queries against dev Postgres (`crmproductsystems-postgres-1`, port 5435):
  policy shapes for 24 marketplace//catalog/notification tables + the global
  `count(*) WHERE policyname='provider_bypass'` = 107. Read-only, no writes.
- Repo-wide greps: `GetDbConnection`, raw `SET app.`/`SET ROLE`, `BeginTransactionAsync`,
  `*.ExecuteAsync` override call sites, and every consumer of the 13 bypass methods.
- `dotnet build ShelfGuard.sln` baseline (informational only — no code changed): **0 errors,
  1 warning** — `CS8602` at `ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs:534`
  (pre-existing). The plan's "0 new warnings" gate should be measured against **1**, not 0.
