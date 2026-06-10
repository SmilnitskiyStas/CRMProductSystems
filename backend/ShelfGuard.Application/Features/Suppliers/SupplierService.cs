using ShelfGuard.Application.Features.Suppliers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Suppliers;

public sealed class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _repo;

    public SupplierService(ISupplierRepository repo) => _repo = repo;

    public async Task<List<SupplierDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var suppliers = await _repo.GetAllAsync(includeInactive, ct);
        return suppliers.Select(ToDto).ToList();
    }

    public async Task<SupplierDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _repo.GetByIdAsync(id, ct);
        return supplier is null ? null : ToDto(supplier);
    }

    public async Task<(SupplierDto? Supplier, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplierRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Supplier name is required.");

        if (request.DeliveryDays < 0)
            return (null, "DeliveryDays cannot be negative.");

        var duplicate = await _repo.ExistsByNameAsync(request.Name.Trim(), null, ct);
        if (duplicate)
            return (null, $"Supplier with name '{request.Name.Trim()}' already exists.");

        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Edrpou = request.Edrpou?.Trim(),
            ContactPerson = request.ContactPerson?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            DeliveryDays = request.DeliveryDays,
            HasSupplierPortal = request.HasSupplierPortal,
            ReturnPolicy = request.ReturnPolicy,
            PaymentTerms = request.PaymentTerms?.Trim(),
            Notes = request.Notes?.Trim(),
        };

        await _repo.AddAsync(supplier, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(supplier), null);
    }

    public async Task<(SupplierDto? Supplier, string? Error)> UpdateAsync(
        Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await _repo.GetByIdAsync(id, ct);
        if (supplier is null)
            return (null, "Supplier not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Supplier name is required.");

        if (request.DeliveryDays < 0)
            return (null, "DeliveryDays cannot be negative.");

        var duplicate = await _repo.ExistsByNameAsync(request.Name.Trim(), id, ct);
        if (duplicate)
            return (null, $"Supplier with name '{request.Name.Trim()}' already exists.");

        supplier.Name = request.Name.Trim();
        supplier.Edrpou = request.Edrpou?.Trim();
        supplier.ContactPerson = request.ContactPerson?.Trim();
        supplier.Phone = request.Phone?.Trim();
        supplier.Email = request.Email?.Trim();
        supplier.DeliveryDays = request.DeliveryDays;
        supplier.HasSupplierPortal = request.HasSupplierPortal;
        supplier.ReturnPolicy = request.ReturnPolicy;
        supplier.PaymentTerms = request.PaymentTerms?.Trim();
        supplier.Notes = request.Notes?.Trim();
        supplier.IsActive = request.IsActive;

        _repo.Update(supplier);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(supplier), null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _repo.GetByIdAsync(id, ct);
        if (supplier is null) return false;

        supplier.IsActive = false;
        _repo.Update(supplier);
        await _repo.SaveChangesAsync(ct);

        return true;
    }

    private static SupplierDto ToDto(Supplier s) => new(
        s.Id, s.Name, s.Edrpou, s.ContactPerson, s.Phone, s.Email,
        s.DeliveryDays, s.HasSupplierPortal, s.ReturnPolicy,
        s.PaymentTerms, s.Notes, s.IsActive
    );
}
