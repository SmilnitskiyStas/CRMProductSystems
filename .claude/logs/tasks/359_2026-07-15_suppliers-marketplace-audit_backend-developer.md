# TASK-359 — Backend: Block 8 pre-launch audit — Suppliers & Marketplace

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-358

Block 8 of the pre-launch audit (`eager-pondering-tower.md`). Scope: `Features/Suppliers`,
`Features/Marketplace` (supplier cabinet, cooperation/agreements, Вчасно, chat, roles, tasks,
marketplace orders).

## Pre-existing uncommitted changes (verified, not touched further)

`SupplierCabinetCooperationController.cs` (doc-comment fix) and `CooperationRequestsTab.tsx`
(removed the "Перегенерувати договір" button from the `active` branch, where it would always
400 — backend already restricted regeneration to `awaiting_signature` only) were confirmed
correct on inspection; included in the review, no further changes needed.

## Found and fixed — P1: supplier custom roles/permissions are UI-only, not enforced server-side

`SupplierCabinetController` and `SupplierCabinetCooperationController` are gated only by
`AppPolicies.SupplierCabinet` = `RequireRole(supplier_admin)`. Every invited supplier staff
member is created with role `supplier_admin` regardless of the `SupplierRole` assigned at
invite time (`SupplierCabinetService.InviteStaffAsync` resolves `SupplierRole.Permissions`
into `User.Permissions`, which the existing generic `AuthService`/`JwtService` pipeline
correctly bakes into the JWT `permissions` claim — only the *read* side was missing). The
`SupplierPermissions` constants (`catalog_management`, `client_reviews`, `task_board`,
`staff_management`, `profile_management`, `client_management`) only ever drove frontend nav
visibility (`Sidebar.tsx`) — confirmed zero backend check anywhere in the codebase. Same class
of bug ADR-020 documented and fixed for tenant roles ("the blocking discovery"), unaddressed
on the supplier side. Practical impact: a supplier staff member invited with only
`task_board` could still call `POST /api/supplier-cabinet/staff` (invite new staff with full
access), `DELETE /api/supplier-cabinet/roles/{id}`, `DELETE /api/supplier-cabinet/items/{id}`,
etc. directly — self-escalation within the supplier's own tenant boundary (not cross-tenant).

Also found a **stale/misleading comment** in `frontend/components/layout/Sidebar.tsx` (lines
238-239) claiming the cooperation-flow routes (`/supplier/requests`, `/supplier/orders`,
`/supplier/contract-settings`, `/supplier/support`) have no permission key because "бекенд
гейтить сам" (backend gates it itself) — verified false: `SupplierCabinetCooperationController`
has zero fine-grained checks either, same role-only gate as above.

**Fix (implemented):**
- New `backend/ShelfGuard.Infrastructure/Authorization/SupplierPermissionAuthorization.cs` —
  `HasPermission(ClaimsPrincipal, string)`, mirrors `LegalEntityAuthorization`'s "permissions"
  claim read. No claim at all (Permissions dict null/empty — default for staff invited without
  an explicit `SupplierRoleId`, and for the tenant's original owner-admin) = unrestricted,
  matching `InviteStaffAsync`'s documented "no role → full access".
- `SupplierCabinetController.cs`: added an in-body `HasPermission` check (imperative, same
  shape as `LegalEntitiesController`'s `LegalEntityAuthorization.CanManage` calls) at the top
  of every action, mapped 1:1 to the existing frontend nav grouping (`Sidebar.tsx`):
  profile (GET/PUT/publish) → `ProfileManagement`; items CRUD → `CatalogManagement`;
  reviews/metrics/reply/stats → `ClientReviews`; staff list/invite/deactivate → `StaffManagement`;
  roles CRUD → `StaffManagement`; tasks list/create/update/status → `TaskBoard`;
  clients list → `ClientManagement`. Chat endpoints deliberately left ungated — matches the
  existing, intentional BUG-019 product decision that any staff member must be able to reply
  to client chats regardless of permissions.
- `Sidebar.tsx`: corrected the stale comment to state accurately that the cooperation-flow
  routes are ungated on the backend too, and flagged as an open decision (see below) instead
  of leaving a false claim that could mislead a future reviewer into skipping this exact check.
- New `backend/ShelfGuard.Tests/Authorization/SupplierPermissionAuthorizationTests.cs` (4
  tests: no claim → unrestricted, matching key → true, present-but-missing key → false,
  unrelated single key → false).

**Flagged, NOT fixed — needs a product decision:** `SupplierCabinetCooperationController`
(cooperation-requests approve/reject/regenerate/send-to-vchasno/mark-signed/terminate/download,
contract-settings + signature/stamp upload, marketplace order status changes, support-ticket
replies/status) has no fine-grained permission key at all today — inventing one means deciding
new taxonomy/grouping (e.g. does order status management belong under `catalog_management`?
does contract-settings belong under `profile_management`? a new `contracts_management` key?)
that is a product/UX call, not an objective code-correctness fix, per this repo's "clarify
scope before implementing" gate. Until decided, every supplier_admin staff member — regardless
of assigned `SupplierRole` — retains full authority over these actions (approve/terminate
contracts, send to Вчасно, change order status, reply on support tickets). Contained to the
supplier's own tenant, no cross-tenant exposure.

## Reviewed and confirmed correct, no changes needed

- **Agreement lifecycle** (`SupplierAgreementService.cs`): `pending → awaiting_signature
  (Approve, requires contract settings filled) → active (MarkSigned) → terminated`. No status
  transition can be skipped — `ApproveAsync` always lands on `AwaitingSignature`, never
  `Active`; `MarkSignedAsync` requires `AwaitingSignature`; `TerminateAsync` requires `Active`.
  `RegenerateContractAsync` already correctly restricted to `AwaitingSignature` only (the
  pre-existing uncommitted doc-comment fix matches the actual code, which was never buggy).
- **Вчасно integration** (`VchasnoClientFactory.cs`, `VchasnoClient.cs`): per-tenant API key
  resolved from `integration_configs` (service=`vchasno`), same pattern as `IFiscalServiceFactory`
  (ADR-013) — not hardcoded/shared. Returns `null` → clean `VchasnoNotConfiguredError` when
  the integration is absent/disabled/missing an api_key. Network/API failures
  (`HttpRequestException` and friends) are caught at both call sites
  (`SendToVchasnoAsync`/`ChooseSigningMethodAsync`) and surfaced as a readable Ukrainian error,
  never a 500.
- **Marketplace order isolation**: `MarketplaceOrderService.CreateOrderAsync` validates every
  line against `_marketplace.GetSupplierItemsAsync(supplierId)` — a supplier-scoped catalog
  dictionary, so cross-supplier item ids cannot leak into an order. `ListForClientAsync`/
  `ListForSupplierAsync` are tenant-scoped at the repository level; `CancelOrderAsync`/
  `UpdateOrderStatusAsync` re-check row ownership (`order.ClientTenantId`/`SupplierTenantId`)
  before mutating. Order gate (`AgreementRequiredError`) correctly requires `Status == Active`.
- **RLS on supplier tables** (`supplier_agreements`, `supplier_chat_sessions`,
  `supplier_chat_messages`, `supplier_roles`, `supplier_tasks`, `supplier_contract_settings`,
  `marketplace_orders`, `marketplace_order_items`, `supplier_support_tickets`,
  `supplier_support_ticket_messages`): all created with the canonical NULLIF-guard pattern
  from day one (two-tenant tables use `SupplierTenantId = ... OR ClientTenantId = ...`, no
  `IS NULL OR` prefix) — none of them were ever subject to the Block 2 (TASK-352) fail-open
  bug, so that fix correctly did not need to touch them and did not disturb legitimate
  supplier↔client shared access. All have `provider_bypass` and `worker_bypass` (verified in
  `20260712175141_AddWorkerBypassRlsPolicy.cs`'s table list). Note: Block 2's still-open
  question ("71 tables' `provider_bypass` only matches role `provider`, not `provider_admin`")
  applies equally to these tables — not re-flagged separately, already tracked upstream.
- **N+1 checks**: `MarketplaceOrderRepository`/`SupplierAgreementRepository`/
  `SupplierSupportTicketRepository` list queries are single round-trips with `.Include()`
  where item/message eager-loading is needed; `SupplierChatRepository.GetSessionsAsync`
  batches other-tenant names, last message, and unread count via 3 grouped queries instead of
  per-row lookups (already hardened in TASK-319). The per-row `GetTenantDisplayNameAsync` calls
  in `SupplierAgreementService`/`MarketplaceOrderService`/`SupplierSupportService`'s `ToDtosAsync`
  helpers cache by distinct tenant id within the request — not a true N+1 (bounded by the
  number of distinct counterparties, not row count), consistent with prior blocks' judgment on
  similar low-volume per-supplier patterns.

## Build / tests

`dotnet build` (full solution) — 0 errors, 1 pre-existing unrelated warning
(`MarketplaceServiceTests.cs:534`, nullable dereference, not touched). `dotnet test` —
846/846 green (was 842; +4 new `SupplierPermissionAuthorizationTests`). Frontend
`npx tsc --noEmit` — clean (comment-only change in `Sidebar.tsx`).

## Needs user decision

Whether/how to add fine-grained supplier permission keys for the cooperation-flow controller
(agreements, orders, contract-settings, support-tickets) — see "Flagged, NOT fixed" above.
