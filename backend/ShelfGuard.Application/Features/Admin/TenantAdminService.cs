using ShelfGuard.Application.Features.Admin.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Admin;

/// <summary>
/// Business logic for the SaaS Admin Panel — tenant lifecycle management.
/// All operations require role = "provider"; authorization is enforced at the controller level.
/// </summary>
public sealed class TenantAdminService : ITenantAdminService
{
    private readonly ITenantAdminRepository _repo;
    private readonly IPasswordHasher _hasher;

    public TenantAdminService(ITenantAdminRepository repo, IPasswordHasher hasher)
    {
        _repo   = repo;
        _hasher = hasher;
    }

    // ── Read ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TenantDto>> GetAllTenantsAsync(CancellationToken ct)
    {
        var tenants = await _repo.GetAllTenantsAsync(ct);

        var result = new List<TenantDto>(tenants.Count);
        foreach (var t in tenants)
        {
            var usage = await BuildUsageAsync(t.Id, ct);
            result.Add(MapToDto(t, usage));
        }
        return result;
    }

    public async Task<TenantDto?> GetTenantAsync(Guid id, CancellationToken ct)
    {
        var tenant = await _repo.GetTenantByIdAsync(id, ct);
        if (tenant is null) return null;

        var usage = await BuildUsageAsync(id, ct);
        return MapToDto(tenant, usage);
    }

    // ── Create ──────────────────────────────────────────────────────────────

    public async Task<(TenantDto? Tenant, string? Error)> CreateTenantAsync(
        CreateTenantRequest req, CancellationToken ct)
    {
        // 1. Validate slug uniqueness
        var slugNormalized = req.Slug.Trim().ToLowerInvariant();
        if (await _repo.SlugExistsAsync(slugNormalized, ct))
            return (null, $"Slug '{slugNormalized}' is already taken.");

        // 2. Create tenant, set plan and business type
        var tenant = Tenant.Create(req.Name.Trim(), slugNormalized);
        var planError = tenant.UpdatePlan(req.Plan);
        if (planError is not null)
            return (null, planError);

        var businessType = string.IsNullOrWhiteSpace(req.BusinessType) ? "retail" : req.BusinessType.Trim();
        var businessTypeError = tenant.UpdateBusinessType(businessType);
        if (businessTypeError is not null)
            return (null, businessTypeError);

        // Default module set follows business_type (ADR-015) unless overridden later via PATCH .../modules
        var modulesError = tenant.UpdateModules(Tenant.DefaultModulesForBusinessType(businessType));
        if (modulesError is not null)
            return (null, modulesError);

        // 3. Create first EnterpriseAdmin user
        var passwordHash = _hasher.Hash(req.AdminPassword);
        var admin = User.Create(
            tenantId:     tenant.Id,
            email:        req.AdminEmail.Trim(),
            fullName:     req.AdminFullName.Trim(),
            passwordHash: passwordHash,
            role:         AppRoles.EnterpriseAdmin);

        // 4. Persist in a single SaveChanges
        await _repo.AddTenantAsync(tenant, ct);
        await _repo.AddUserAsync(admin, ct);
        await _repo.SaveChangesAsync(ct);

        var usage = await BuildUsageAsync(tenant.Id, ct);
        return (MapToDto(tenant, usage), null);
    }

    // ── Mutations ───────────────────────────────────────────────────────────

    public async Task<(TenantDto? Tenant, string? Error)> UpdatePlanAsync(
        Guid id, string plan, CancellationToken ct)
    {
        var tenant = await _repo.GetTenantByIdAsync(id, ct);
        if (tenant is null) return (null, "Tenant not found.");

        var error = tenant.UpdatePlan(plan);
        if (error is not null) return (null, error);

        await _repo.SaveChangesAsync(ct);
        var usage = await BuildUsageAsync(id, ct);
        return (MapToDto(tenant, usage), null);
    }

    public async Task<(TenantDto? Tenant, string? Error)> UpdateModulesAsync(
        Guid id, IReadOnlyList<string> modules, CancellationToken ct)
    {
        var tenant = await _repo.GetTenantByIdAsync(id, ct);
        if (tenant is null) return (null, "Tenant not found.");

        var error = tenant.UpdateModules(modules);
        if (error is not null) return (null, error);

        await _repo.SaveChangesAsync(ct);
        var usage = await BuildUsageAsync(id, ct);
        return (MapToDto(tenant, usage), null);
    }

    public async Task<(TenantDto? Tenant, string? Error)> ActivateAsync(
        Guid id, CancellationToken ct)
    {
        var tenant = await _repo.GetTenantByIdAsync(id, ct);
        if (tenant is null) return (null, "Tenant not found.");

        tenant.Activate();
        await _repo.SaveChangesAsync(ct);

        var usage = await BuildUsageAsync(id, ct);
        return (MapToDto(tenant, usage), null);
    }

    public async Task<(TenantDto? Tenant, string? Error)> DeactivateAsync(
        Guid id, CancellationToken ct)
    {
        var tenant = await _repo.GetTenantByIdAsync(id, ct);
        if (tenant is null) return (null, "Tenant not found.");

        tenant.Deactivate();
        await _repo.SaveChangesAsync(ct);

        var usage = await BuildUsageAsync(id, ct);
        return (MapToDto(tenant, usage), null);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<TenantUsageDto> BuildUsageAsync(Guid tenantId, CancellationToken ct)
    {
        var usersCount    = await _repo.CountUsersAsync(tenantId, ct);
        var storesCount   = await _repo.CountStoresAsync(tenantId, ct);
        var productsCount = await _repo.CountProductsAsync(tenantId, ct);
        var salesCount    = await _repo.CountSalesLast30DaysAsync(tenantId, ct);

        return new TenantUsageDto(usersCount, storesCount, productsCount, salesCount);
    }

    private static TenantDto MapToDto(Tenant t, TenantUsageDto usage) =>
        new(
            t.Id,
            t.Name,
            t.Slug,
            t.Plan,
            t.BusinessType,
            t.GetModules(),
            t.IsActive,
            t.CreatedAt,
            usage);
}
