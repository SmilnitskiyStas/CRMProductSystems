using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 1 (plan `1-partitioned-book.md`, decision D1).
///
/// Warehouse-type field decision: <see cref="Domain.Entities.Location"/> carries both a
/// <c>Type</c> and a (dead) <c>LocationType</c> property. <see cref="ILocationService"/>
/// operates exclusively on entity <c>Type</c> — <see cref="LocationService.CreateAsync"/>
/// writes <c>CreateLocationRequest.LocationType</c> onto entity <c>Type</c>, and
/// <see cref="LocationDto.LocationType"/> is mapped back from entity <c>Type</c>. Entity
/// <c>LocationType</c> is never read by anything in Application (documented in
/// LoyaltyService.cs). So a supplier warehouse is created by passing
/// <c>LocationType = "warehouse"</c> (accepted by <c>LocationService.IsValidLocationType</c>)
/// and identified everywhere by <c>LocationDto.LocationType == "warehouse"</c>. No change to
/// LocationService / CreateLocationRequest was needed.
/// </summary>
public sealed class SupplierWarehouseService : ISupplierWarehouseService
{
    /// <summary>Location.Type value that marks a row as a supplier warehouse.</summary>
    public const string WarehouseType = "warehouse";

    public const string WarehouseNotFoundError = "Склад не знайдено.";

    private readonly ILocationService _locations;

    public SupplierWarehouseService(ILocationService locations) => _locations = locations;

    public async Task<List<SupplierWarehouseDto>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        // role: null -> LocationService skips the store-scope narrowing and returns every
        // location of the tenant visible under the caller's (supplier) RLS context.
        var locations = await _locations.GetAllAsync(tenantId, userId: null, role: null, ct);
        return locations
            .Where(l => string.Equals(l.LocationType, WarehouseType, StringComparison.OrdinalIgnoreCase))
            .Select(ToDto)
            .ToList();
    }

    public async Task<(SupplierWarehouseDto? Warehouse, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplierWarehouseRequest request, CancellationToken ct = default)
    {
        var (location, error) = await _locations.CreateAsync(
            tenantId,
            new CreateLocationRequest(
                Name: request.Name,
                Address: request.Address,
                Latitude: null,
                Longitude: null,
                LocationType: WarehouseType,
                RegionCode: request.RegionCode),
            ct);

        return error is not null ? (null, error) : (ToDto(location!), null);
    }

    public async Task<(SupplierWarehouseDto? Warehouse, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateSupplierWarehouseRequest request, CancellationToken ct = default)
    {
        var ownError = await EnsureOwnedWarehouseAsync(tenantId, id, ct);
        if (ownError is not null)
            return (null, ownError);

        var (location, error) = await _locations.UpdateAsync(
            id,
            new UpdateLocationRequest(
                Name: request.Name,
                Address: request.Address,
                Latitude: null,
                Longitude: null,
                LocationType: WarehouseType,
                IsActive: request.IsActive,
                RegionCode: request.RegionCode),
            ct);

        return error is not null ? (null, error) : (ToDto(location!), null);
    }

    public async Task<(bool Success, string? Error)> DeactivateAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var ownError = await EnsureOwnedWarehouseAsync(tenantId, id, ct);
        if (ownError is not null)
            return (false, ownError);

        // LocationService.UpdateAsync needs the full request (Name required) — carry the
        // current values through and only flip IsActive.
        var current = await _locations.GetByIdAsync(id, ct);
        if (current is null)
            return (false, WarehouseNotFoundError);

        var (_, error) = await _locations.UpdateAsync(
            id,
            new UpdateLocationRequest(
                Name: current.Name,
                Address: current.Address,
                Latitude: null,
                Longitude: null,
                LocationType: WarehouseType,
                IsActive: false,
                RegionCode: current.RegionCode),
            ct);

        return error is not null ? (false, error) : (true, null);
    }

    /// <summary>
    /// App-layer ownership + type guard (defence in depth on top of RLS, per the ILocationService
    /// TASK-392b convention): the id must belong to the calling tenant AND be a warehouse row.
    /// Returns an error string when it isn't, null when the caller may proceed.
    /// </summary>
    private async Task<string?> EnsureOwnedWarehouseAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        if (!await _locations.BelongsToTenantAsync(tenantId, id, ct))
            return WarehouseNotFoundError;

        var location = await _locations.GetByIdAsync(id, ct);
        if (location is null || !string.Equals(location.LocationType, WarehouseType, StringComparison.OrdinalIgnoreCase))
            return WarehouseNotFoundError;

        return null;
    }

    private static SupplierWarehouseDto ToDto(LocationDto l) =>
        new(l.Id, l.Name, l.Address, l.RegionCode, l.IsActive);
}
