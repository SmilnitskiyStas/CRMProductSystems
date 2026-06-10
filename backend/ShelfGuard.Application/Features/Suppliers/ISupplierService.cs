using ShelfGuard.Application.Features.Suppliers.Dtos;

namespace ShelfGuard.Application.Features.Suppliers;

public interface ISupplierService
{
    Task<List<SupplierDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<SupplierDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(SupplierDto? Supplier, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplierRequest request, CancellationToken ct = default);

    Task<(SupplierDto? Supplier, string? Error)> UpdateAsync(
        Guid id, UpdateSupplierRequest request, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
