namespace ShelfGuard.Application.Features.SupplySchedules.Dtos;

public sealed record SupplyScheduleDto(
    Guid Id,
    Guid StoreId,
    string StoreName,
    Guid SupplierId,
    string SupplierName,
    List<int> DayOfWeek,
    int? OrderLeadDays,
    bool IsActive
);

public sealed record CreateSupplyScheduleRequest(
    Guid StoreId,
    Guid SupplierId,
    List<int> DayOfWeek,
    int? OrderLeadDays
);

public sealed record UpdateSupplyScheduleRequest(
    List<int> DayOfWeek,
    int? OrderLeadDays,
    bool IsActive
);
