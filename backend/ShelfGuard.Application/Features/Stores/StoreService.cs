using ShelfGuard.Application.Features.Stores.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Stores;

public sealed class StoreService : IStoreService
{
    private readonly IStoreRepository _repo;

    public StoreService(IStoreRepository repo) => _repo = repo;

    public async Task<List<StoreDto>> GetAllAsync(CancellationToken ct = default)
    {
        var stores = await _repo.GetAllAsync(ct);
        return stores.Select(ToDto).ToList();
    }

    public async Task<StoreDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var store = await _repo.GetByIdAsync(id, ct);
        return store is null ? null : ToDto(store);
    }

    public async Task<(StoreDto? Store, string? Error)> CreateAsync(
        Guid tenantId, CreateStoreRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Store name is required.");

        if (!IsValidStoreType(request.Type))
            return (null, $"Invalid store type '{request.Type}'. Valid: shop, central_warehouse, production, distribution.");

        var store = new Store
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Type = request.Type,
        };

        await _repo.AddAsync(store, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(store), null);
    }

    public async Task<(StoreDto? Store, string? Error)> UpdateAsync(
        Guid id, UpdateStoreRequest request, CancellationToken ct = default)
    {
        var store = await _repo.GetByIdAsync(id, ct);
        if (store is null)
            return (null, "Store not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Store name is required.");

        if (!IsValidStoreType(request.Type))
            return (null, $"Invalid store type '{request.Type}'. Valid: shop, central_warehouse, production, distribution.");

        store.Name = request.Name.Trim();
        store.Address = request.Address?.Trim();
        store.Latitude = request.Latitude;
        store.Longitude = request.Longitude;
        store.Type = request.Type;
        store.IsActive = request.IsActive;

        _repo.Update(store);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(store), null);
    }

    public async Task<(StoreDto? Store, string? Error)> UpdateFloorPlanAsync(
        Guid id, UpdateFloorPlanRequest request, CancellationToken ct = default)
    {
        var store = await _repo.GetByIdAsync(id, ct);
        if (store is null)
            return (null, "Store not found.");

        store.FloorPlan = request.FloorPlan;

        _repo.Update(store);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(store), null);
    }

    public async Task<List<StoreZoneDto>> GetZonesAsync(Guid storeId, CancellationToken ct = default)
    {
        var zones = await _repo.GetZonesAsync(storeId, ct);
        return zones.Select(ToZoneDto).ToList();
    }

    public async Task<(StoreZoneDto? Zone, string? Error)> CreateZoneAsync(
        Guid storeId, CreateZoneRequest request, CancellationToken ct = default)
    {
        var store = await _repo.GetByIdAsync(storeId, ct);
        if (store is null)
            return (null, "Store not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Zone name is required.");

        if (!IsValidZoneType(request.Type))
            return (null, $"Invalid zone type '{request.Type}'. Valid: shelf, fridge, freezer, display, production, warehouse.");

        if (request.ShelvesCount < 1)
            return (null, "ShelvesCount must be at least 1.");

        var zone = new StoreZone
        {
            StoreId = storeId,
            Name = request.Name.Trim(),
            Type = request.Type,
            Position = request.Position,
            ShelvesCount = request.ShelvesCount,
            TempMin = request.TempMin,
            TempMax = request.TempMax,
        };

        await _repo.AddZoneAsync(zone, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToZoneDto(zone), null);
    }

    public async Task<(StoreZoneDto? Zone, string? Error)> UpdateZoneAsync(
        Guid storeId, Guid zoneId, UpdateZoneRequest request, CancellationToken ct = default)
    {
        var zone = await _repo.GetZoneByIdAsync(zoneId, ct);
        if (zone is null || zone.StoreId != storeId)
            return (null, "Zone not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Zone name is required.");

        if (!IsValidZoneType(request.Type))
            return (null, $"Invalid zone type '{request.Type}'. Valid: shelf, fridge, freezer, display, production, warehouse.");

        if (request.ShelvesCount < 1)
            return (null, "ShelvesCount must be at least 1.");

        zone.Name = request.Name.Trim();
        zone.Type = request.Type;
        zone.Position = request.Position;
        zone.ShelvesCount = request.ShelvesCount;
        zone.TempMin = request.TempMin;
        zone.TempMax = request.TempMax;
        zone.IsActive = request.IsActive;

        _repo.UpdateZone(zone);
        await _repo.SaveChangesAsync(ct);

        return (ToZoneDto(zone), null);
    }

    public async Task<(bool Success, string? Error)> DeleteZoneAsync(
        Guid storeId, Guid zoneId, CancellationToken ct = default)
    {
        var zone = await _repo.GetZoneByIdAsync(zoneId, ct);
        if (zone is null || zone.StoreId != storeId)
            return (false, "Zone not found.");

        zone.IsActive = false;
        _repo.UpdateZone(zone);
        await _repo.SaveChangesAsync(ct);

        return (true, null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static StoreDto ToDto(Store s) => new(
        s.Id,
        s.Name,
        s.Address,
        s.Latitude,
        s.Longitude,
        s.Type,
        s.FloorPlan,
        s.IsActive,
        s.CreatedAt,
        s.Zones.Where(z => z.IsActive).Select(ToZoneDto).ToList()
    );

    private static StoreZoneDto ToZoneDto(StoreZone z) => new(
        z.Id,
        z.StoreId,
        z.Name,
        z.Type,
        z.Position,
        z.ShelvesCount,
        z.TempMin,
        z.TempMax,
        z.IsActive
    );

    private static bool IsValidStoreType(string type) =>
        type is "shop" or "central_warehouse" or "production" or "distribution";

    private static bool IsValidZoneType(string type) =>
        type is "shelf" or "fridge" or "freezer" or "display" or "production" or "warehouse";
}
