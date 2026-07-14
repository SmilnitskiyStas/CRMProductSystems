# Handoff 346 → 347 (security-reviewer)

TASK-346 implements ADR-020 points 3-8 (backend capability enforcement for TenantRole
templates). Build clean, 779/779 tests green. This is the authorization-critical part of
ADR-020 — please audit before point 9 (frontend) ships, since the frontend will start
surfacing "assign a role template" UI that depends on this being airtight.

## (а) Every new policy — exact roles + capability

All defined in `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`
(`RoleOrCapabilityRequirement`/`RoleOrCapabilityHandler` in the same directory implement the
OR — succeeds on `ClaimsPrincipal.IsInRole` for any of `AllowedRoles`, same check
`RequireRole()` uses, OR the JWT `capabilities` claim contains the capability string).

| Policy | AllowedRoles (unchanged from pre-existing gate) | Capability (OR) |
|---|---|---|
| `SchedulesManageOrCapability` | `AtLeastStoreManagerRoles` | `schedules.manage` |
| `AnalyticsViewOrCapability` | `CanViewAnalyticsRoles` | `analytics.view` |
| `IntegrationsViewOrCapability` | `AtLeastStoreManagerRoles` | `integrations.view` |
| `IntegrationsManageOrCapability` | `AtLeastStoreManagerRoles` | `integrations.manage` |
| `OrdersManageOrCapability` | `AtLeastStoreManagerRoles` | `orders.manage` |
| `SuppliersViewOrCapability` | `AtLeastStoreManagerRoles` | `suppliers.view` |
| `SuppliersManageOrCapability` | `AtLeastNetworkManagerRoles` | `suppliers.manage` |
| `ReceiptsViewOrCapability` | `CanReceiveStockRoles` | `receipts.view` |
| `AiOrdersViewOrCapability` | `AtLeastStoreManagerRoles` | `ai_orders.view` |
| `AiOrdersManageOrCapability` | `AtLeastStoreManagerRoles` | `ai_orders.manage` |
| `EnterpriseAdminOrUsersManage` | `AtLeastEnterpriseAdminRoles` | `users.manage` |
| `StoreManagerOrUsersManage` | `AtLeastStoreManagerRoles` | `users.manage` |

`EnterpriseAdminOrUsersManage`/`StoreManagerOrUsersManage` is the one deliberate deviation
from 1:1 capability→policy: `users.manage` unlocks three UsersController actions
(Invite/Update/Deactivate) that had two DIFFERENT pre-existing role floors (Invite/Deactivate
were `AtLeastEnterpriseAdmin`, Update was `AtLeastStoreManager`). Both floors are preserved
exactly rather than collapsed to one — collapsing to the looser floor would have let
`network_manager`/`store_manager` newly Invite/Deactivate users, which they could not do
before. Please specifically verify this reasoning — it's the least mechanical judgment call in
the diff.

Every `RoleOrCapabilityRequirement`'s `AllowedRoles` array is a **direct reference** to the
SAME pre-existing `internal static readonly string[]` the controller's old
`[Authorize(Policy=...)]`/class-level attribute used (e.g. `AtLeastStoreManagerRoles`) — not a
re-typed literal — so there is no way for the new policy's role floor to drift from the old
one. `AppPoliciesTests.cs` asserts this with `AssertSameRoles(...)` per policy (also asserts
`SuppliersManageOrCapability` excludes `store_manager` and `EnterpriseAdminOrUsersManage`
excludes `store_manager`/`network_manager` — the two places a naive collapse would have
silently loosened access).

## (б) Every changed controller — exact methods, old policy → new policy

**`UsersController`** (`backend/ShelfGuard.Api/Controllers/UsersController.cs`) — class-level
`[Authorize(Policy=AtLeastStoreManager)]` **removed**; every action now has an explicit
per-action attribute (11 total, including the 1 new endpoint):
- `GetAll`, `GetById`, `UpdatePermissions`, `GrantTemporaryPermission`,
  `GetActivePermissionGrants`, `RevokeTemporaryPermission`, `GetActivity` → unchanged, now
  explicit `AtLeastStoreManager` (no capability bypass — deliberate, per ADR-020 anti-escalation).
- `Invite` (was `AtLeastEnterpriseAdmin`) → `EnterpriseAdminOrUsersManage`.
- `Update` (was `AtLeastStoreManager`) → `StoreManagerOrUsersManage`.
- `Deactivate` (was `AtLeastEnterpriseAdmin`) → `EnterpriseAdminOrUsersManage`.
- `AssignTenantRole` (**new**, `POST /api/users/{id}/tenant-role`) → `AtLeastEnterpriseAdmin`,
  no capability bypass.

**`SchedulesController`** — class-level bare `[Authorize]` **kept** (no role restriction to
begin with, so it never blocked the capability path — no removal needed).
`Create`/`Update`/`Delete`/`AddShift`/`UpdateShift`/`DeleteShift` (all were
`AtLeastStoreManager`) → `SchedulesManageOrCapability`. `GetAll`/`GetById`/`GetMyShifts`
unchanged (still bare `[Authorize]` only).

**`AnalyticsController`** — class-level attribute's **policy value swapped**
`CanViewAnalytics` → `AnalyticsViewOrCapability` (not removed — every one of the ~9 GET actions
shares the identical policy, so this is behaviorally identical to decorating each method and
strictly less code to audit). No per-action attributes added.

**`IntegrationsController`** — class-level `AtLeastStoreManager` **removed**.
`GetAll`/`GetByService` → `IntegrationsViewOrCapability`. `Upsert`/`Delete` →
`IntegrationsManageOrCapability`.

**`OrdersController`** — class-level `AtLeastStoreManager` **removed**. Sole action
`Calculate` → `OrdersManageOrCapability`.

**`SuppliersController`** — class-level `AtLeastStoreManager` **removed**.
`GetAll`/`GetById` (were implicit via class-level) → `SuppliersViewOrCapability`.
`Create`/`Update`/`Delete` (were explicit `AtLeastNetworkManager`, tighter than the removed
class-level) → `SuppliersManageOrCapability` (base roles = `AtLeastNetworkManagerRoles`,
confirmed NOT loosened to `AtLeastStoreManagerRoles`).

**`ReceiptsController`** — class-level `CanReceiveStock` **removed**.
`GetAll`/`GetById` (implicit via class-level) → `ReceiptsViewOrCapability`.
`Create`/`UpdateItems`/`Receive` (implicit via class-level) → now explicit `CanReceiveStock`,
**no capability bypass** (write-heavy stock path, deliberately excluded per ADR-020 point 3 —
`UpdateItems` specifically was not named in the brief's prose but was treated the same way on
purpose, see task log). `Cancel` (was already explicit `AtLeastStoreManager`) → unchanged,
still `AtLeastStoreManager`, no capability bypass.

**`AiOrdersController`** — class-level `AtLeastStoreManager` **removed**.
`GetList`/`GetById` → `AiOrdersViewOrCapability`. `Generate`/`UpdateItem`/`Accept`/`Reject` →
`AiOrdersManageOrCapability`.

**`LegalEntitiesController`** — **no attribute changes at all**, class-level
`AtLeastStoreManager` untouched, no per-action attributes. Only
`LegalEntityAuthorization.CanManage` (`backend/ShelfGuard.Infrastructure/Authorization/LegalEntityAuthorization.cs`)
changed — added a third `TenantRoleAuthorization.HasCapability(user, TenantUserPermissions.LegalEntitiesManage)`
OR branch alongside the two that already existed (`AtLeastEnterpriseAdminRoles` role check,
`permissions` claim check). **Known limitation, please confirm you agree with accepting it**: a
`staff`-rank (rank 0) user whose only access is a `legal_entities.manage` TenantRole capability
is still 403'd by the controller's class-level `AtLeastStoreManager` gate before
`CanManage` ever runs in the action body — ASP.NET Core combines class-level + action-body
authorization as AND, and an imperative check can only narrow what middleware already let
through. This mirrors a pre-existing limitation of the `User.Permissions`-based
`legal_entities.manage` override (same class-level gate, same problem, already accepted before
this task). Fixing it would require removing the class-level attribute here too, which the
task brief explicitly instructed NOT to do for this controller — flagging for your judgment
rather than silently either fixing or leaving it.

**No other controller was touched** (POS, stock write-off, transfers, fiscalization, etc.) —
confirmed by `git status`/diff review, only the 9 controllers listed above appear in the diff.

## (в) Confirmation: no rank-bearing policy got a capability admixture

`ProviderOnly`, `AtLeastEnterpriseAdmin`, `AtLeastNetworkManager`, `AtLeastStoreManager`,
`CanReceiveStock`, `CanViewStock`, `CanViewAnalytics` (old, now unused but still registered —
left intact rather than deleted), `CanAccessPos`, `CanManageStore`, `CanViewNetworkAnalytics`,
`SupplierCabinet`, `ProviderTeamMember`, `ProviderCanInvite` — all still registered via plain
`p.RequireRole(...)` in `AppPolicies.Configure` (`AppPolicies.cs:140-152`), byte-for-byte the
same as before this task. Every new capability-aware policy is a **separate, newly-added**
named policy (`AppPolicies.cs:154-178`); none of the original ones were edited. Also
independently verified via `RoleOrCapabilityHandler` not being wired to any of them —
`grep` confirms `RoleOrCapabilityRequirement` only appears inside the 12 new `AddPolicy(...)`
calls.

`TenantRolesController` (template CRUD) and `UsersController.AssignTenantRole` (assignment)
are **entirely outside** the RoleOrCapability mechanism — both are gated by the plain,
unmodified `AtLeastEnterpriseAdmin` policy, so a capability-only user (no real elevated role)
cannot create a template, edit an existing template's capability list, or assign/reassign
any user's TenantRole — including their own. This is the anti-escalation backstop: even a
`staff`-rank user holding `users.manage` cannot use that capability to grant itself or anyone
else a stronger template, because the assignment endpoint itself never accepts the
capability-OR path.

## (г) Cross-tenant belonging check on template assignment

`UserService.AssignTenantRoleAsync`
(`backend/ShelfGuard.Application/Features/Users/UserService.cs`, new method, ~15 lines after
`GetActivePermissionGrantsAsync`):

1. Loads the target user by id; if null or `target.TenantId != tenantId` (the ACTING user's
   own tenant, taken from the controller's JWT `tenant_id` claim, never from the request body)
   → `"User not found."` (404).
2. If `tenantRoleId` is not null, calls `ITenantRoleRepository.GetByIdAsync(tenantId, tenantRoleId.Value, ct)`
   — this repository method takes `tenantId` as an explicit filter
   (`_db.TenantRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct)`,
   `TenantRoleRepository.cs:18-19`), so a `tenantRoleId` belonging to a different tenant
   returns `null` from the repository call itself — indistinguishable from a genuinely
   nonexistent id. Mapped to `"TenantRole not found."` (404), never 403, so the response
   cannot be used to confirm another tenant's template id exists.
3. If found but `!role.IsActive` (archived) → `"Cannot assign an archived TenantRole."` (400 —
   the id demonstrably exists and belongs to the caller's own tenant, so 400 here does not leak
   cross-tenant information; only step 2's cross-tenant case is mapped to 404).
4. Only after all three checks pass: `target.SetTenantRole(tenantRoleId)`, activity-logged as
   `"user.tenant_role_assigned"`, saved.

Controller side (`UsersController.AssignTenantRole`) maps `"Cannot assign an archived TenantRole."`
to 400 and everything else (`"User not found."`, `"TenantRole not found."`) to 404 — see
`backend/ShelfGuard.Api/Controllers/UsersController.cs` around the new action. Test coverage:
`UserServiceTenantRoleTests.cs`, specifically
`AssignTenantRoleAsync_RoleBelongsToDifferentTenant_ReturnsNotFound_NotForbidden` and
`AssignTenantRoleAsync_TargetUserBelongsToDifferentTenant_ReturnsNotFound`.

## Other things worth a look

- JWT `capabilities` claim: minted in `JwtService.GenerateAccessToken`
  (`backend/ShelfGuard.Infrastructure/Services/JwtService.cs`), comma-joined, same shape/claim
  pattern as the existing `permissions` claim — no new parsing surface, reuses
  `TenantRoleAuthorization.HasCapability` (mirrors `LegalEntityAuthorization`'s existing
  `permissions`-claim parsing byte-for-byte).
- `AuthService.BuildEffectiveCapabilitiesAsync` returns `[]` (not null) whenever
  `User.TenantRoleId` is null, the user has no tenant (provider), the referenced `TenantRole`
  row doesn't exist, or `IsActive == false` — archiving a template silently and immediately
  zeroes out every assignee's capabilities on their next login/refresh (~15 min worst case,
  same propagation delay already accepted for ADR-019 permission grants), without needing to
  touch `User.TenantRoleId` on each affected row.
- `TenantRoleCapabilities.All` is the single validation gate in `TenantRoleService.Validate` —
  any capability string outside that set is rejected on both Create and Update, so
  `TenantRole.Capabilities` can never contain a value the enforcement layer doesn't recognize.

## Test coverage added this task

`backend/ShelfGuard.Tests/Authorization/RoleOrCapabilityHandlerTests.cs`,
`backend/ShelfGuard.Tests/Authorization/LegalEntityAuthorizationTests.cs`,
`backend/ShelfGuard.Tests/Authorization/AppPoliciesTests.cs` (extended),
`backend/ShelfGuard.Tests/TenantRoles/TenantRoleServiceTests.cs`,
`backend/ShelfGuard.Tests/Users/UserServiceTenantRoleTests.cs`,
`backend/ShelfGuard.Tests/Auth/AuthServiceCapabilitiesTests.cs`.
779/779 total suite green.
