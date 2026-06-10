namespace ShelfGuard.Application.Features.Suppliers.Dtos;

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string? Edrpou,
    string? ContactPerson,
    string? Phone,
    string? Email,
    int DeliveryDays,
    bool HasSupplierPortal,
    bool ReturnPolicy,
    string? PaymentTerms,
    string? Notes,
    bool IsActive
);

public sealed record CreateSupplierRequest(
    string Name,
    string? Edrpou,
    string? ContactPerson,
    string? Phone,
    string? Email,
    int DeliveryDays,
    bool HasSupplierPortal,
    bool ReturnPolicy,
    string? PaymentTerms,
    string? Notes
);

public sealed record UpdateSupplierRequest(
    string Name,
    string? Edrpou,
    string? ContactPerson,
    string? Phone,
    string? Email,
    int DeliveryDays,
    bool HasSupplierPortal,
    bool ReturnPolicy,
    string? PaymentTerms,
    string? Notes,
    bool IsActive
);
