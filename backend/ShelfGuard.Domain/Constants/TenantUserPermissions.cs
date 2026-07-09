namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Granular permission keys for regular tenant users (non-supplier, non-provider),
/// stored per-user in <see cref="Entities.User.Permissions"/> (jsonb overrides).
/// Distinct from the page-slug access map already used for menu visibility —
/// these are feature-level "manage" flags checked imperatively alongside the
/// role-rank policies in <c>AppPolicies</c> (TASK-322: Legal Entities).
/// </summary>
public static class TenantUserPermissions
{
    /// <summary>Create/update/deactivate legal entities (юридичні особи) for the tenant.</summary>
    public const string LegalEntitiesManage = "legal_entities.manage";

    public static readonly string[] All =
    [
        LegalEntitiesManage,
    ];
}
