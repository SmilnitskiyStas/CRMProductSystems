# TASK-585: fix HTTP 403 on GET /api/supplier-cabinet/profile for supplier_admin users

**Status:** done

## Change
`backend/ShelfGuard.Api/Controllers/SupplierCabinetController.cs` — `GetProfile` action.
Removed the `SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ProfileManagement)`
check, replaced with a one-line comment (mirrors the TASK-359 precedent in `Sidebar.tsx`):

```diff
 var tenantId = ResolveTenantId();
 if (tenantId is null) return Forbid();
-if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ProfileManagement)) return Forbid();
+// Intentionally ungated (TASK-585, mirrors TASK-359): any supplier_admin can view their own profile regardless of assigned permissions.

 var (profile, error) = await _cabinet.GetProfileAsync(tenantId.Value, ct);
```

`UpdateProfile` and `TogglePublish` untouched — still require `ProfileManagement` permission.
Remaining guards on `GetProfile` unchanged: `[Authorize(Policy = AppPolicies.SupplierCabinet)]`,
`[RequireModule("marketplace_supplier")]`, `ResolveTenantId()` tenant scoping.

## Verification
- `dotnet build` — succeeded, 0 errors, 1 pre-existing warning (unrelated, `MarketplaceServiceTests.cs:534`).
- `dotnet test --filter FullyQualifiedName~SupplierCabinet` — 31 passed, 0 failed.
- `dotnet test --filter FullyQualifiedName~SupplierPermission` — 4 passed, 0 failed.
- No controller-level test file exists for `SupplierCabinetController` (only service-layer
  `SupplierCabinetServiceTests.cs` and `SupplierPermissionAuthorizationTests.cs`, neither of
  which asserts controller HTTP status for `GetProfile`) — no test changes needed.
