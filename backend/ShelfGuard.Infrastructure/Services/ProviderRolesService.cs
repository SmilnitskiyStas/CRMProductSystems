using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

public sealed class ProviderRolesService(AppDbContext db) : IProviderRolesService
{
    public async Task<IReadOnlyList<ProviderRoleDto>> GetAllAsync(CancellationToken ct)
    {
        var roles = await db.ProviderRoles.AsNoTracking().OrderBy(r => r.IsSystem ? 0 : 1).ThenBy(r => r.DisplayName).ToListAsync(ct);
        return roles.Select(Map).ToList();
    }

    public async Task<(ProviderRoleDto? Role, string? Error)> CreateAsync(CreateProviderRoleRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return (null, "Назва ролі обов'язкова.");

        if (!AppRoles.ProviderTeamRoles.Contains(req.BaseRole))
            return (null, $"Базова роль '{req.BaseRole}' недопустима. Доступні: provider_admin, provider_agent.");

        var invalidPerms = req.Permissions.Where(p => !ProviderPermissions.All.Contains(p)).ToList();
        if (invalidPerms.Count > 0)
            return (null, $"Невідомі права: {string.Join(", ", invalidPerms)}.");

        var duplicate = await db.ProviderRoles.AnyAsync(r => r.DisplayName == req.DisplayName.Trim(), ct);
        if (duplicate)
            return (null, $"Роль '{req.DisplayName}' вже існує.");

        var role = ProviderRole.Create(req.DisplayName.Trim(), req.BaseRole, req.Permissions);
        db.ProviderRoles.Add(role);
        await db.SaveChangesAsync(ct);
        return (Map(role), null);
    }

    public async Task<(ProviderRoleDto? Role, string? Error)> UpdateAsync(Guid id, UpdateProviderRoleRequest req, CancellationToken ct)
    {
        var role = await db.ProviderRoles.FindAsync([id], ct);
        if (role is null) return (null, "Роль не знайдена.");
        if (role.IsSystem) return (null, "Системні ролі не можна редагувати.");

        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return (null, "Назва ролі обов'язкова.");

        if (!AppRoles.ProviderTeamRoles.Contains(req.BaseRole))
            return (null, $"Базова роль '{req.BaseRole}' недопустима.");

        var invalidPerms = req.Permissions.Where(p => !ProviderPermissions.All.Contains(p)).ToList();
        if (invalidPerms.Count > 0)
            return (null, $"Невідомі права: {string.Join(", ", invalidPerms)}.");

        role.Update(req.DisplayName.Trim(), req.BaseRole, req.Permissions);
        await db.SaveChangesAsync(ct);
        return (Map(role), null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct)
    {
        var role = await db.ProviderRoles.FindAsync([id], ct);
        if (role is null) return (false, "Роль не знайдена.");
        if (role.IsSystem) return (false, "Системні ролі не можна видаляти.");

        // Detach users from this role before deleting (FK is SET NULL, but let's be explicit)
        db.ProviderRoles.Remove(role);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private static ProviderRoleDto Map(ProviderRole r) =>
        new(r.Id, r.DisplayName, r.BaseRole, [.. r.Permissions], r.IsSystem);
}
