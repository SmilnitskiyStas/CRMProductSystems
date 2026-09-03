namespace ShelfGuard.Application.Features.SupplierInventory.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// Supplier-portal expansion — Phase 1 (plan `1-partitioned-book.md`, decision D1).
// A supplier "warehouse" IS a Location row with type = "warehouse". These DTOs are the
// thin supplier-facing projection over LocationDto / Create|UpdateLocationRequest — the
// supplier cabinet never sees zones, floor plans or store-scope, which the retail
// LocationsController carries.
// ═══════════════════════════════════════════════════════════════════════════

public sealed record SupplierWarehouseDto(
    Guid Id,
    string Name,
    string? Address,
    /// <summary>Structured Ukraine region code (ISO 3166-2:UA oblast or city code), nullable.</summary>
    string? RegionCode,
    bool IsActive);

public sealed record CreateSupplierWarehouseRequest(
    string Name,
    string? Address,
    string? RegionCode);

public sealed record UpdateSupplierWarehouseRequest(
    string Name,
    string? Address,
    string? RegionCode,
    bool IsActive);
