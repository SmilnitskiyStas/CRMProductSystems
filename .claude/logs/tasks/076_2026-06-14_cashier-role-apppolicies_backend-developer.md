# TASK-076 — Cashier role + AppPolicies update

**Date:** 2026-06-14
**Agent:** backend-developer
**Status:** done

## What was done

### 1. AppRoles.cs — already complete
`Cashier = "cashier"` was already present in `ShelfGuard.Domain/Constants/AppRoles.cs`
(added in a prior session). No change needed.

### 2. UserService.cs — already complete
`cashier` was already present in both `ValidRoles` and `RoleRank` dictionaries
in `ShelfGuard.Application/Features/Users/UserService.cs`. No change needed.

### 3. AppPolicies.cs — 3 new policies added
File: `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`

Added:
- `CanAccessPos` — cashier + storekeeper + store_manager + network_manager + enterprise_admin + provider
- `CanManageStore` — store_manager + network_manager + enterprise_admin + provider (no cashier/storekeeper/merchandiser)
- `CanViewNetworkAnalytics` — network_manager + enterprise_admin + provider

Updated comment block to reflect new policies and cashier exclusion from `CanReceiveStock`.

### 4. PosController.cs — policy updated
File: `backend/ShelfGuard.Api/Controllers/PosController.cs`

Changed controller-level policy from `CanReceiveStock` → `CanAccessPos`.
This allows cashier role to access shifts/sales endpoints.
Worker-facing endpoints (`/pending-fiscalization`, `/fiscalize`) retain `AtLeastStoreManager`.

### 5. AppPoliciesTests.cs — tests added
File: `backend/ShelfGuard.Tests/Authorization/AppPoliciesTests.cs`

Added tests for:
- `CanAccessPos_allows_correct_roles` (6 roles)
- `CanAccessPos_denies_merchandiser`
- `CanManageStore_allows_correct_roles` (4 roles)
- `CanManageStore_denies_lower_roles` (storekeeper, merchandiser, cashier)
- `CanViewNetworkAnalytics_allows_correct_roles` (3 roles)
- `CanViewNetworkAnalytics_denies_lower_roles` (store_manager, storekeeper, merchandiser, cashier)
- Updated `ProviderOnly_denies_all_other_roles` to include cashier
- Updated `CanReceiveStock_denies_merchandiser` → `CanReceiveStock_denies_merchandiser_and_cashier`
- Updated `All_policies_are_registered` to include 3 new policy names

## Build & Test results

- `dotnet build` — green (0 warnings, 0 errors)
- `dotnet test` — 400 passed / 2 failed (pre-existing failures in `CheckboxFiscalClientTests.CheckCashier_*` — confirmed failing before this task)

## Files changed

- `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`
- `backend/ShelfGuard.Api/Controllers/PosController.cs`
- `backend/ShelfGuard.Tests/Authorization/AppPoliciesTests.cs`
