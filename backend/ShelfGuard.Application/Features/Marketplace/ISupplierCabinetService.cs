using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Users.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Self-service supplier cabinet (v4.1, ADR-016, TASK-284).
/// Every operation resolves "my supplier" from the calling tenant's single
/// owner-managed profile (supplier_profiles.IsOwnerManaged partial unique index),
/// so a supplier tenant can never touch another supplier's data.
/// Provider-created suppliers (platform tenant, IsOwnerManaged = false) are unreachable here.
/// </summary>
public interface ISupplierCabinetService
{
    Task<(SupplierProfileDto? Profile, string? Error)> GetProfileAsync(
        Guid tenantId, CancellationToken ct = default);

    Task<(SupplierProfileDto? Profile, string? Error)> UpdateProfileAsync(
        Guid tenantId, CabinetProfileUpdateDto request, CancellationToken ct = default);

    /// <summary>Toggles IsPublic and returns the updated profile.</summary>
    Task<(SupplierProfileDto? Profile, string? Error)> TogglePublishAsync(
        Guid tenantId, CancellationToken ct = default);

    Task<(IReadOnlyList<SupplierItemDto>? Items, string? Error)> GetItemsAsync(
        Guid tenantId, CancellationToken ct = default);

    Task<(SupplierItemDto? Item, string? Error)> AddItemAsync(
        Guid tenantId, AdminAddSupplierItemDto request, CancellationToken ct = default);

    Task<(SupplierItemDto? Item, string? Error)> UpdateItemAsync(
        Guid tenantId, Guid itemId, AdminUpdateSupplierItemDto request, CancellationToken ct = default);

    /// <summary>Returns null on success, error string on failure.</summary>
    Task<string?> DeleteItemAsync(Guid tenantId, Guid itemId, CancellationToken ct = default);

    Task<(PagedResult<PublicSupplierReviewDto>? Reviews, string? Error)> GetReviewsAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    Task<(SupplierMetricsDto? Metrics, string? Error)> GetMetricsAsync(
        Guid tenantId, CancellationToken ct = default);

    // ── Staff management (self-service) ──────────────────────────────────────

    /// <summary>Lists all staff/team members of the caller's own tenant.</summary>
    Task<IReadOnlyList<UserDto>> GetStaffAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Invites a new staff member into the caller's own tenant, always as supplier_admin.</summary>
    Task<(UserDto? User, string? Error)> InviteStaffAsync(
        Guid tenantId, CabinetInviteStaffDto request, string inviterName, CancellationToken ct = default);

    /// <summary>
    /// Deactivates a staff member. Returns an error (and does nothing) if the target
    /// user does not belong to the caller's own tenant.
    /// </summary>
    Task<string?> DeactivateStaffAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
