# TASK-599 — Marketplace receipt enrichment (price/photo) + discrepancy auto-ticket

**Status:** done · **Agent:** backend-developer (isolated worktree, Wave 2 of TASK-586/ADR-033,
parallel to TASK-598, catalog auto-provisioning at order time)

**Note:** self-logged as TASK-596 originally (this worktree's stale pre-Wave-1 baseline made
596 look free); renumbered to TASK-599 by the orchestrator during merge — 596 and 598 were
already taken by the real Wave 1 (database-engineer) and the parallel Wave 2 (backend-developer,
catalog auto-provisioning) tasks respectively. All in-code `TASK-597`/`TASK-596` comment
references in this task's files were corrected to `TASK-599` during the merge.

## Pre-existing-condition mismatch (flag for orchestrator)

Brief said Wave 1 (database-engineer) already added `SupplierSupportTicket.MarketplaceOrderId`
to this worktree's commit (`aeb830fc`/`dfb45365`, matching this worktree's `main`). It did not
exist — `backend/ShelfGuard.Domain/Entities/SupplierSupportTicket.cs` had no such field, no
migration referenced it, and no task log above TASK-595 mentioned it. Per the brief's own
instruction ("confirm it's there before building on it") and CLAUDE.md's judgment-call carve-out,
I added the column myself, exactly to the spec given (nullable `Guid`, no nav property, FK →
`marketplace_orders.Id` ON DELETE RESTRICT, indexed) via
`20260822141854_AddMarketplaceOrderIdToSupplierSupportTickets`. **If the real Wave 1 migration
also lands (e.g. from the main-worktree agent or a separate database-engineer run), the
orchestrator must reconcile — drop whichever migration duplicates the column** — same
reconciliation risk already flagged for `CooperationDtos.cs`, just on one more file.

## Part A — richer receipt data

`MarketplaceOrderReceiptItemDto` gained two fields (appended after `IsResolved`):
- `decimal Price` — from `MarketplaceOrderItem.Price` (frozen at order time), always present.
- `string? ReferenceImageUrl` — resolved `Item.ImageUrl` once `ProductId` is set (no fallback,
  may be null); otherwise the order line's `SupplierItem`'s primary image (`Kind == "main"`,
  else lowest `SortOrder`), same convention `MarketplaceService`/`SupplierCabinetService` use.

Non-obvious wrinkle: `supplier_items`/`supplier_item_images` RLS has **no client-tenant read
policy** (`tenant_isolation` + `provider_bypass` only) — a plain EF `Include` from the client's
receipt query would silently return nulls. Added
`IMarketplaceRepository.GetSupplierItemImagesByIdsAsync` (provider-bypass read, same
`SetProviderRoleAsync` pattern `GetSupplierItemsAsync` already uses), batch-called once per
`ToDtoAsync` for every not-yet-scanned line. `ToDto`/`ToItemDto` became async instance methods.

## Part B — discrepancy auto-ticket

New `ISupplierSupportService.CreateSystemTicketAsync(clientTenantId, supplierTenantId,
marketplaceOrderId, subject, body, actingUserId, ct)` — mirrors `CreateTicketAsync`'s
ticket+message construction but skips the catalog-supplierId resolution and **deliberately does
not call `SaveChangesAsync`** (caller flushes it together with its own writes).

`MarketplaceOrderReceiptService.ReceiveAsync` now aggregates
`receipt.Items.Where(i => !string.IsNullOrWhiteSpace(i.DiscrepancyNotes))` after the existing
finalize `SaveChangesAsync`. If any exist, opens a system ticket + enqueues a
`NotificationQueue` row (`EventType = "supplier_support_ticket.opened"`,
`TenantId = receipt.SupplierTenantId`) inside `_tenantSessionOverride.ExecuteAsync(receipt.SupplierTenantId, ...)`.

**Deviation from the brief's literal ask (flag for orchestrator/documentation-writer):** the
brief asked for ticket+notification to commit in the SAME `SaveChangesAsync` call that flips the
receipt to `received`. Verified this is unsafe: `product_stocks`/`stock_movements` (written
earlier in the same method) carry `TenantId = clientTenantId` under the plain single-tenant
`tenant_isolation` RLS those tables use — they can only write while the ambient session is the
CLIENT tenant. The `notification_queue` row needs the opposite (ambient = SUPPLIER tenant, via
the override) since it also uses plain single-tenant RLS. No single ambient tenant satisfies
both inside one `SaveChangesAsync`. Implemented as two sequential `SaveChangesAsync` calls
instead (finalize, then discrepancy side-effect) — finalize is never blocked by a downstream
notification hiccup, at the cost of not being one DB transaction end-to-end. Documented inline
in `MarketplaceOrderReceiptService.ReceiveAsync`.

`SupplierSupportTicketDto` gained `string? OrderNumber` (resolved via new
`IMarketplaceOrderRepository` dependency on `SupplierSupportService`, cached per list call).
Shown in `CabinetSupportTab.tsx`'s thread header as "Щодо замовлення {orderNumber}" (uk) /
"Regarding order {orderNumber}" (en) when non-null — new `orderReference` i18n key in both
`messages/uk.json`/`en.json`. Also added `orderNumber` to `frontend/features/marketplace/types.ts`
`SupplierSupportTicketDto` (necessary companion to the header change — not itself a UI edit).

Worker: `worker/src/jobs/notification-dispatch.job.ts` — `"supplier_support_ticket.opened"`
added to `DISPATCH_EVENT_ROLES` (roles `["store_manager","network_manager","enterprise_admin"]`,
channels `["telegram","push"]`, same as `"supplier.message"`) and to the icons map (`⚠️`).

## Build / test

- `dotnet build ShelfGuard.sln` — clean (1 pre-existing unrelated warning).
- `dotnet test --filter "FullyQualifiedName~Marketplace"` — 220/220 passing.
- Full `dotnet test` — 1813/1813 passing.
- `npx tsc --noEmit` (frontend, after `npm install` — worktree had no `node_modules`) — 0 errors.

New tests: `MarketplaceOrderReceiptServiceTests.cs` (+4: no-discrepancy regression guard,
discrepancy → ticket+notification, resolved-item image, unresolved-item fallback,
neither-available → null) and new `SupplierSupportServiceTests.cs` (+2:
`CreateSystemTicketAsync` entity/message shape, order-not-found → null `OrderNumber`).

## Files touched (backend)

- `ShelfGuard.Domain/Entities/SupplierSupportTicket.cs`
- `ShelfGuard.Domain/Interfaces/IMarketplaceRepository.cs`
- `ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/MarketplaceRepository.cs`
- `ShelfGuard.Infrastructure/Migrations/20260822141854_AddMarketplaceOrderIdToSupplierSupportTickets.cs` (+Designer, +snapshot)
- `ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs`
- `ShelfGuard.Application/Features/Marketplace/MarketplaceOrderReceiptService.cs`
- `ShelfGuard.Application/Features/Marketplace/ISupplierSupportService.cs`
- `ShelfGuard.Application/Features/Marketplace/SupplierSupportService.cs`
- `ShelfGuard.Tests/Marketplace/MarketplaceOrderReceiptServiceTests.cs`
- `ShelfGuard.Tests/Marketplace/SupplierSupportServiceTests.cs` (new)

No DI registration changes needed — `IMarketplaceRepository`, `ISupplierSupportService`,
`INotificationRepository`, `ITenantSessionOverride`, `IMarketplaceOrderRepository` were all
already registered in `ShelfGuard.Infrastructure/DependencyInjection.cs`.

## Merge resolution (orchestrator, post-hoc)

Confirmed (per this agent's own flag above) that `SupplierSupportTicket.MarketplaceOrderId` was
already added by the real Wave 1 (TASK-596, database-engineer) directly in `main` — migration
`20260822134439_AddItemSourceSupplierItemAndTicketOrderRef`, identical property/FK/index shape.
When merging this worktree into `main`:
- **Discarded**: this worktree's own `SupplierSupportTicket.cs` diff, `AppDbContext.cs` diff (the
  `MarketplaceOrderId` FK/index block only), and migration
  `20260822141854_AddMarketplaceOrderIdToSupplierSupportTickets` (+Designer) — pure duplicates of
  Wave 1, never applied to `main`.
- **Merged as-is**: `ISupplierSupportService.cs`, `SupplierSupportService.cs`,
  `MarketplaceOrderReceiptService.cs`, `IMarketplaceRepository.cs`, `MarketplaceRepository.cs`,
  both new/extended test files, `notification-dispatch.job.ts`, `CabinetSupportTab.tsx`.
- **Hand-merged** (touched by both this task and the parallel TASK-598 agent):
  `Dtos/CooperationDtos.cs` — this task's `MarketplaceOrderReceiptItemDto`
  (`Price`/`ReferenceImageUrl`) and `SupplierSupportTicketDto` (`OrderNumber`) additions applied
  on top of TASK-598's already-committed `CreateMarketplaceOrderItemDto`/conflict-DTO additions;
  no overlap, both sets of changes are in disjoint records. `frontend/features/marketplace/types.ts`
  similarly hand-merged (this task's `orderNumber` field alongside TASK-597's `BarcodeConflict`
  additions). `.claude/tasks/current.md` and `messages/{en,uk}.json` hand-merged the same way.

Post-merge full-suite verification (not just this worktree's isolated 1813/1813): see
`.claude/tasks/current.md`'s TASK-599 entry for the final combined test count.
